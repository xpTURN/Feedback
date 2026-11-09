using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the grades' behavior in a preview — UnityEvents not invoked, the hit stop's clock and the speed, the open signal waits,
    // the camera shake, the channel receivers, a MotionDriver as the subject, the notices. The session runs on the test's
    // clock — an update is 1/120 s, a tick 1/60 s; a stop or a progress is read at set times (two updates may hold no tick)
    public sealed class PreviewGradesTests
    {
        const string ScenePath = "Assets/PreviewGradesTests.unity";
        const string Scene2Path = "Assets/PreviewGradesTests2.unity";
        const string PrefabPath = "Assets/PreviewGradesTests.prefab";
        const string ResourcesDir = "Assets/Resources";
        const string SettingsPath = "Assets/Resources/FeedbackSettings.asset";
        const string Skipped = "Feedback preview did not invoke a UnityEvent";
        const double UpdateStep = 1.0 / 120.0;

        static double s_Now;

        IPreviewRecordSource m_Source;
        Func<double> m_Clock;
        Scene m_Scene;
        Scene m_Active;
        bool m_HadDeselect;
        bool m_Deselect;
        bool m_HadResources;
        readonly List<string> m_Logs = new List<string>();
        readonly List<GameObject> m_Untagged = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            m_Source = PreviewRecordPaths.Source;
            m_Clock = PreviewClock.Source;
            PreviewRecordPaths.Source = new PreviewRecordPathTable();
            s_Now = 0;
            PreviewClock.Source = () => s_Now;
            m_HadDeselect = EditorPrefs.HasKey(PreviewSession.EndOnDeselectKey);
            m_Deselect = PreviewSession.EndOnDeselect;
            m_HadResources = AssetDatabase.IsValidFolder(ResourcesDir);
            m_Active = SceneManager.GetActiveScene();
            m_Scene = OpenEmpty(ScenePath);
            SceneManager.SetActiveScene(m_Scene);
            m_Logs.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            PreviewSession.Current?.Stop();
            PreviewRecovery.Run();
            if (StageUtility.GetCurrentStage() is PrefabStage)
                StageUtility.GoToMainStage();
            if (m_HadDeselect)
                PreviewSession.EndOnDeselect = m_Deselect;
            else
                EditorPrefs.DeleteKey(PreviewSession.EndOnDeselectKey);
            if (Selection.activeObject != null)
                Selection.activeObject = null;
            foreach (GameObject camera in m_Untagged)
            {
                if (camera != null)
                    camera.tag = "MainCamera";
            }
            m_Untagged.Clear();
            PreviewRecordPaths.Source = m_Source;
            PreviewClock.Source = m_Clock;
            if (m_Active.IsValid())
                SceneManager.SetActiveScene(m_Active);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.path == Scene2Path)
                    EditorSceneManager.CloseScene(scene, true);
            }
            if (m_Scene.IsValid())
                EditorSceneManager.CloseScene(m_Scene, true);
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(Scene2Path);
            AssetDatabase.DeleteAsset(PrefabPath);
            AssetDatabase.DeleteAsset(SettingsPath);
            if (!m_HadResources && AssetDatabase.IsValidFolder(ResourcesDir))
                AssetDatabase.DeleteAsset(ResourcesDir);
        }

        void OnLog(string message, string stack, LogType type) => m_Logs.Add(message);

        static Scene OpenEmpty(string path)
        {
            File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        static void Pump(int updates)
        {
            for (int i = 0; i < updates; i++)
            {
                s_Now += UpdateStep;
                PreviewSession.Current?.Update();
            }
        }

        static void Seconds(double seconds) => Pump((int)Math.Round(seconds / UpdateStep));

        int Logged(string start) => m_Logs.Count(m => m.StartsWith(start, StringComparison.Ordinal));

        static FeedbackPlayer Player(string name) => new GameObject(name).AddComponent<FeedbackPlayer>();

        static T On<T>(FeedbackPlayer player, Object target, FeedbackMotion motion, float duration) where T : TargetItem, new()
        {
            var item = new T { m_Motion = motion };
            item.m_Target.m_Mode = target != null ? TargetRefMode.Direct : TargetRefMode.Self;
            item.m_Target.m_Object = target;
            item.m_Timing.m_Duration = duration;
            player.m_Items.Add(item);
            return item;
        }

        static TweenMotion Tween(float to) => new TweenMotion { m_Params = new TweenParams { m_To = new Vector4(to, to, to, to), m_Loops = 1 } };

        static ShakeMotion Shake() => new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = new Vector4(1f, 1f, 0f, 0f), m_Frequency = 7f } };

        static bool Moved(Transform t, Vector3 home) => (t.position - home).sqrMagnitude > 1e-8f;

        // a listener that turns the target off, called in the editor too
        static UnityEventItem EventItem(GameObject target)
        {
            var item = new UnityEventItem();
            UnityEventTools.AddBoolPersistentListener(item.m_Event, target.SetActive, false);
            item.m_Event.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
            return item;
        }

        static void WaitAfter(FeedbackPlayer player, float duration)
        {
            var wait = new WaitItem();
            wait.m_Timing.m_Duration = duration;
            wait.m_Timing.m_Rule = StartRule.AfterPrevious;
            player.m_Items.Add(wait);
        }

        // a hit stop of 0.2 s after `delay` — a scaled position tween and an unscaled scale tween beside it
        static FeedbackPlayer Stopper(out Transform scaled, out Transform unscaled, float delay = 0.1f)
        {
            scaled = new GameObject("Scaled").transform;
            unscaled = new GameObject("Unscaled").transform;
            FeedbackPlayer player = Player("Stopper");
            var stop = new HitStopItem { m_FreezeDuration = 0.2f, m_TimeScale = 0f, m_MinimumTimeScale = 0f };
            stop.m_Timing.m_Delay = delay;
            player.m_Items.Add(stop);
            On<PositionItem>(player, scaled, Tween(10f), 1f).m_Timing.m_Clock = ItemClock.Scaled;
            On<ScaleItem>(player, unscaled, Tween(3f), 1f).m_Timing.m_Clock = ItemClock.Unscaled;
            return player;
        }

        // a 0.1 s tween, then a signal wait, then a 0.1 s scale tween on `after`
        static FeedbackPlayer Waiter(float timeout, out Transform after)
        {
            FeedbackPlayer player = Player("Waiter");
            On<PositionItem>(player, null, Tween(1f), 0.1f);
            var wait = new WaitForSignalItem { m_Timeout = timeout };
            wait.m_Timing.m_Rule = StartRule.AfterPrevious;
            player.m_Items.Add(wait);
            after = new GameObject("After").transform;
            On<ScaleItem>(player, after, Tween(2f), 0.1f).m_Timing.m_Rule = StartRule.AfterPrevious;
            return player;
        }

        static CameraShakeItem CameraShake(FeedbackPlayer player)
        {
            var item = new CameraShakeItem { m_Motion = Shake() };
            item.m_Timing.m_Duration = 1f;
            player.m_Items.Add(item);
            return item;
        }

        static CameraShakeItem ChannelShake(FeedbackPlayer player, float duration, float delay = 0f)
        {
            var item = new CameraShakeItem { m_Motion = Shake() };
            item.m_Target.m_Mode = TargetRefMode.Channel;
            item.m_Timing.m_Duration = duration;
            item.m_Timing.m_Delay = delay;
            player.m_Items.Add(item);
            return item;
        }

        static CameraShakeReceiver Receiver(string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            return go.AddComponent<CameraShakeReceiver>();
        }

        static MotionDriver LoopDriver(string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            var driver = go.AddComponent<MotionDriver>();
            var item = new PositionItem { m_Motion = new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(1f, 0f, 0f, 0f), m_Frequency = 1f } } };
            item.m_Target.m_Mode = TargetRefMode.Self;
            item.m_Timing.m_Duration = 1f;
            driver.m_Item = item;
            driver.m_Mode = MotionDriverMode.Loop;
            return driver;
        }

        // a second scene saved as a file, open beside the test's — or closed again
        Scene SecondScene(Action build, bool close = false)
        {
            Scene second = OpenEmpty(Scene2Path);
            SceneManager.SetActiveScene(second);
            build();
            SceneManager.SetActiveScene(m_Scene);
            if (close)
            {
                Assert.That(EditorSceneManager.SaveScene(second), Is.True);
                EditorSceneManager.CloseScene(second, true);
            }
            return second;
        }

        // the batch editor's untitled scene has a 'Main Camera' — untagged here so that this one is Camera.main
        Camera MainCamera(Vector3 at)
        {
            NoMainCamera();
            var go = new GameObject("Cam") { tag = "MainCamera" };
            go.transform.position = at;
            return go.AddComponent<Camera>();
        }

        void NoMainCamera()
        {
            foreach (Camera camera in Camera.allCameras)
            {
                if (!camera.CompareTag("MainCamera"))
                    continue;
                camera.gameObject.tag = "Untagged";
                m_Untagged.Add(camera.gameObject);
            }
        }

        static void Settings(Action<FeedbackSettings> set)
        {
            Assume.That(AssetDatabase.LoadAssetAtPath<FeedbackSettings>(SettingsPath), Is.Null, "the project has its own settings asset");
            if (!AssetDatabase.IsValidFolder(ResourcesDir))
                AssetDatabase.CreateFolder("Assets", "Resources");
            var asset = ScriptableObject.CreateInstance<FeedbackSettings>();
            set(asset);
            AssetDatabase.CreateAsset(asset, SettingsPath);
        }

        static List<PreviewNotice> Notices()
        {
            var notices = new List<PreviewNotice>();
            PreviewNotices.Collect(PreviewSession.Current, notices);
            return notices;
        }

        static List<PreviewNoticeKind> Kinds() => Notices().Select(n => n.Kind).ToList();

        static string Text(PreviewNoticeKind kind) => Notices().Where(n => n.Kind == kind).Select(n => n.Text).FirstOrDefault() ?? "";

        // ---- UnityEvent — a session that called the listeners would turn the target off and leave it off after the end,
        //      the scene clean

        [Test]
        public void PX07a_ThePreviewInvokesNoUnityEvent()
        {
            var itemTarget = new GameObject("ItemTarget");
            var playerTarget = new GameObject("PlayerTarget");
            FeedbackPlayer player = Player("Eventful");
            player.m_Items.Add(EventItem(itemTarget));
            var played = new UnityEvent();
            UnityEventTools.AddBoolPersistentListener(played, playerTarget.SetActive, false);
            played.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
            player.m_UnityEvents.Add(new PlayerUnityEvent { m_Kind = PlayerEventKind.Played, m_Event = played });
            PreviewSession.Start(player, default);
            Seconds(0.1);
            Assert.That(itemTarget.activeSelf && playerTarget.activeSelf, Is.True, "during");
            PreviewSession.Current?.Stop();
            Assert.That(itemTarget.activeSelf && playerTarget.activeSelf, Is.True, "after the end");
        }

        [Test]
        public void PX07b_EverySkipCountsAndAnEventLogsOnce()
        {
            var target = new GameObject("Target");
            FeedbackPlayer player = Player("Looper");
            player.m_Items.Add(new LoopStartItem());
            UnityEventItem item = EventItem(target);
            item.m_Timing.m_Rule = StartRule.AfterPrevious;
            player.m_Items.Add(item);
            WaitAfter(player, 0.05f);
            var end = new LoopEndItem { m_LoopCount = 3 };
            end.m_Timing.m_Rule = StartRule.AfterPrevious;
            player.m_Items.Add(end);
            PreviewSession.Start(player, default);
            PreviewSession session = PreviewSession.Current;
            Seconds(0.4);
            Assert.That(session.Scheduler.Context.SkippedUnityEvents, Is.EqualTo(3));
            Assert.That(Logged(Skipped), Is.EqualTo(1));
        }

        [Test]
        public void PX07c_TheLogCountsThePersistentListeners()
        {
            var target = new GameObject("Target");
            FeedbackPlayer player = Player("Eventful");
            UnityEventItem item = EventItem(target);
            UnityEventTools.AddBoolPersistentListener(item.m_Event, target.SetActive, false);
            item.m_Event.SetPersistentListenerState(1, UnityEventCallState.RuntimeOnly);
            player.m_Items.Add(item);
            PreviewSession.Start(player, default);
            Seconds(0.05);
            Assert.That(m_Logs.Any(m => m.EndsWith("(item): 2 persistent listener(s), 1 for the editor too.", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void PX07d_TheCSharpEventsStillCome()
        {
            FeedbackPlayer player = Player("Eventful");
            player.m_Items.Add(EventItem(new GameObject("Target")));
            int played = 0;
            player.Played += (p, h) => played++;
            PreviewSession.Start(player, default);
            Seconds(0.05);
            Assert.That(played, Is.EqualTo(1));
        }

        [Test]
        public void PX07e_ATestContextInvokesThem()
        {
            var target = new GameObject("Target");
            FeedbackPlayer player = Player("Ticked");
            player.m_Items.Add(EventItem(target));
            var scheduler = new FeedbackScheduler(ContextKind.Test);
            player.Bind(scheduler);
            try
            {
                Assert.That(player.EnsureRuntime(), Is.True);
                player.Play();
                scheduler.Tick(0.05f);
                Assert.That(target.activeSelf, Is.False, "called");
                Assert.That(scheduler.Context.SkippedUnityEvents, Is.Zero);
            }
            finally
            {
                player.ReleaseRuntime();
                player.Bind(null);
                scheduler.Shutdown();
            }
        }

        [Test]
        public void PX07f_OneLogAcrossARebuildAndAnotherInANewSession()
        {
            FeedbackPlayer player = Player("Eventful");
            player.m_Items.Add(EventItem(new GameObject("Target")));
            WaitAfter(player, 1f);
            PreviewSession.Start(player, default);
            Seconds(0.1);
            player.Rebuild();
            PreviewSession.Start(player, default);
            Seconds(0.1);
            Assert.That(Logged(Skipped), Is.EqualTo(1), "the same session");
            PreviewSession.Current?.Stop();
            PreviewSession.Start(player, default);
            Seconds(0.1);
            Assert.That(Logged(Skipped), Is.EqualTo(2), "a new session");
        }

        [Test]
        public void PX07g_RuntimeListenersAreNotCalledAndTheDiagnosticsCome()
        {
            FeedbackPlayer player = Player("Eventful");
            var item = new UnityEventItem();
            int calls = 0;
            item.m_Event.AddListener(() => calls++);
            player.m_Items.Add(item);
            int diagnostics = 0;
            Action<FeedbackPlayer, PlayerEventKind, PlaybackHandle> on = (p, k, h) => { if (p == player) diagnostics++; };
            FeedbackDiagnostics.PlayerEvent += on;
            try
            {
                PreviewSession.Start(player, default);
                Seconds(0.1);
            }
            finally
            {
                FeedbackDiagnostics.PlayerEvent -= on;
            }
            Assert.That(calls, Is.Zero);
            Assert.That(diagnostics, Is.GreaterThanOrEqualTo(1));
        }

        // ---- the hit stop's clock and the speed

        [Test]
        public void PX06a_AHitStopStopsTheScaledItemsOnly()
        {
            FeedbackPlayer player = Stopper(out Transform scaled, out Transform unscaled);
            PreviewSession.Start(player, default);
            Seconds(0.20);
            float x20 = scaled.position.x;
            float s20 = unscaled.localScale.x;
            Seconds(0.05);
            Assert.That(scaled.position.x, Is.EqualTo(x20), "the scaled item stops");
            Assert.That(unscaled.localScale.x, Is.GreaterThan(s20), "the unscaled item runs");
            Seconds(0.10);
            Assert.That(scaled.position.x, Is.GreaterThan(x20), "and moves after it");
        }

        [Test]
        public void PX06b_TheRemainingTimeGoesDown()
        {
            FeedbackPlayer player = Stopper(out _, out _);
            PreviewSession.Start(player, default);
            Seconds(0.15);
            TimeScaleService timeScale = PreviewSession.Current.Scheduler.Context.TimeScaleIfMade;
            Assert.That(timeScale, Is.Not.Null);
            Assert.That(timeScale.Count, Is.GreaterThan(0));
            double r15 = timeScale.RemainingAt(timeScale.Count - 1);
            Seconds(0.10);
            Assert.That(timeScale.Count, Is.GreaterThan(0));
            double r25 = timeScale.RemainingAt(timeScale.Count - 1);
            Assert.That(r25, Is.LessThan(r15).And.GreaterThan(0));
        }

        [Test]
        public void PX06c_NoTimeValueIsWritten()
        {
            GlobalStateCheck check = GlobalStateCheck.Take();
            float timeScale = Time.timeScale;
            FeedbackPlayer player = Stopper(out _, out _);
            PreviewSession.Start(player, default);
            Seconds(0.2);
            Assert.That(Time.timeScale, Is.EqualTo(timeScale), "during the stop");
            PreviewSession.Current?.Stop();
            Assert.That(Time.timeScale, Is.EqualTo(timeScale));
            check.AssertSame();
            Assert.That(GlobalStateCheck.Left(), Is.Empty);
        }

        [Test]
        public void PX06d_AtSpeedTwoTheStopIsHalfAsLong()
        {
            FeedbackPlayer player = Stopper(out Transform scaled, out _);
            PreviewSession.Start(player, default);
            PreviewSession.Current.Speed = 2f;
            Seconds(0.10);
            float x10 = scaled.position.x;
            Seconds(0.05);
            Assert.That(scaled.position.x, Is.EqualTo(x10), "stopped");
            Seconds(0.05);
            Assert.That(scaled.position.x, Is.GreaterThan(x10), "past the stop at 0.15 s");
        }

        [Test]
        public void PX06e_TheSpeedScalesTheProgress()
        {
            FeedbackPlayer slow = Player("Slow");
            On<PositionItem>(slow, null, Tween(10f), 1f);
            PreviewSession.Start(slow, default);
            PreviewSession.Current.Speed = 0.5f;
            Seconds(0.5);
            float half = slow.transform.position.x;
            PreviewSession.Current?.Stop();
            FeedbackPlayer fast = Player("Fast");
            On<PositionItem>(fast, null, Tween(10f), 1f);
            PreviewSession.Start(fast, default);
            PreviewSession.Current.Speed = 2f;
            Seconds(0.5);
            Assert.That(half, Is.InRange(2f, 3f), "a quarter");
            Assert.That(fast.transform.position.x, Is.GreaterThan(9f), "all of it");
        }

        [Test]
        public void PX06f_TheSpeedIsClamped()
        {
            FeedbackPlayer player = Player("Any");
            On<PositionItem>(player, null, Tween(1f), 1f);
            PreviewSession.Start(player, default);
            PreviewSession.Current.Speed = 5f;
            Assert.That(PreviewSession.Current.Speed, Is.EqualTo(2f));
            PreviewSession.Current.Speed = 0f;
            Assert.That(PreviewSession.Current.Speed, Is.EqualTo(0.1f));
        }

        [Test]
        public void PX06g_OnlyAHitStopMakesTheTimeService()
        {
            FeedbackPlayer plain = Player("Plain");
            On<PositionItem>(plain, null, Tween(1f), 1f);
            PreviewSession.Start(plain, default);
            Assert.That(PreviewSession.Current.Scheduler.Context.TimeScaleIfMade, Is.Null);
            PreviewSession.Current?.Stop();
            FeedbackPlayer stop = Stopper(out _, out _);
            PreviewSession.Start(stop, default);
            Assert.That(PreviewSession.Current.Scheduler.Context.TimeScaleIfMade, Is.Not.Null);
        }

        // the cooldown's clock is the preview's unscaled time: 0.2 + 0.45 × 2 = 1.1 s > 1 s only at the speed
        [Test]
        public void PX06h_TheCooldownFollowsTheSpeed()
        {
            FeedbackPlayer player = Player("Mover");
            player.m_Cooldown = 1f;
            On<PositionItem>(player, null, Tween(10f), 2f);
            int played = 0;
            player.Played += (p, h) => played++;
            PreviewSession.Start(player, default);
            Seconds(0.2);
            float x = player.transform.position.x;
            PreviewSession.Current.Speed = 2f;
            Assert.That(player.transform.position.x, Is.EqualTo(x), "from the next tick");
            Seconds(0.45);
            PreviewSession.Start(player, default);
            Seconds(0.05);
            Assert.That(played, Is.EqualTo(2));
        }

        // a stalled editor's long step: capped at 0.1 s, then the speed
        [Test]
        public void PX06i_ALongStepIsCappedBeforeTheSpeed()
        {
            FeedbackPlayer player = Player("Stalled");
            On<PositionItem>(player, null, Tween(10f), 1f);
            PreviewSession.Start(player, default);
            PreviewSession.Current.Speed = 2f;
            Pump(1);
            s_Now += 0.3;
            PreviewSession.Current.Update();
            Assert.That(player.transform.position.x, Is.GreaterThan(1.5f));
        }

        [Test]
        public void PX06j_AnUnscaledPlayersItemsRunThroughTheStop()
        {
            var follows = new GameObject("Follows").transform;
            var own = new GameObject("Own").transform;
            FeedbackPlayer player = Player("Unscaled");
            player.m_TimeMode = PlayerTimeMode.Unscaled;
            var stop = new HitStopItem { m_FreezeDuration = 0.2f, m_TimeScale = 0f, m_MinimumTimeScale = 0f };
            stop.m_Timing.m_Delay = 0.1f;
            player.m_Items.Add(stop);
            On<PositionItem>(player, follows, Tween(10f), 1f);
            On<ScaleItem>(player, own, Tween(3f), 1f).m_Timing.m_Clock = ItemClock.Scaled;
            PreviewSession.Start(player, default);
            Seconds(0.20);
            float x20 = follows.position.x;
            float s20 = own.localScale.x;
            Seconds(0.05);
            Assert.That(follows.position.x, Is.GreaterThan(x20), "the player's clock — unscaled");
            Assert.That(own.localScale.x, Is.EqualTo(s20), "its own clock — scaled");
        }

        // ---- the open signal waits

        [Test]
        public void PX08a_ASignalClosesTheOpenWaitAtOnce()
        {
            FeedbackPlayer player = Waiter(0.5f, out Transform after);
            PreviewSession.Start(player, default);
            Seconds(0.05);
            Assert.That(player.OpenSignalWaits(), Is.Zero, "before the wait");
            Seconds(0.15);
            Assert.That(player.OpenSignalWaits(), Is.EqualTo(1));
            Assert.That(player.SignalQuietly(), Is.EqualTo(1));
            Assert.That(player.OpenSignalWaits(), Is.Zero, "right after the signal");
            Seconds(0.05);
            Assert.That(after.localScale.x, Is.GreaterThan(1f), "the next item goes on");
        }

        [Test]
        public void PX08b_TheTimeoutClosesTheWait()
        {
            FeedbackPlayer player = Waiter(0.5f, out _);
            PreviewSession.Start(player, default);
            Seconds(0.2);
            Assert.That(player.OpenSignalWaits(), Is.EqualTo(1));
            Seconds(0.6);
            Assert.That(player.OpenSignalWaits(), Is.Zero);
        }

        [Test]
        public void PX08c_ABackwardPlayHasNoOpenWait()
        {
            FeedbackPlayer player = Waiter(0.5f, out _);
            PreviewSession.Start(player, PlayArgs.Default.WithDirection(PlayerDirection.Backward));
            Seconds(0.05);
            Assert.That(player.IsPlaying, Is.True);
            Assert.That(player.OpenSignalWaits(), Is.Zero);
            Seconds(0.3);
            Assert.That(player.OpenSignalWaits(), Is.Zero);
        }

        [Test]
        public void PX08d_SignalWarnsOnlyWithoutAWait()
        {
            FeedbackPlayer player = Waiter(0.5f, out _);
            PreviewSession.Start(player, default);
            Seconds(0.2);
            player.Signal();
            Assert.That(m_Logs.Count(m => m == FeedbackPlayer.SignalWarning), Is.Zero);
            Seconds(0.1);
            player.Signal();
            Assert.That(m_Logs.Count(m => m == FeedbackPlayer.SignalWarning), Is.EqualTo(1));
        }

        // the second wait starts at the first one's end: one open at a time
        [Test]
        public void PX08e_ConsecutiveWaitsOpenOneAtATimeAndAStopClosesThem()
        {
            FeedbackPlayer player = Player("Two");
            player.m_Items.Add(new WaitForSignalItem { m_Timeout = 1f });
            player.m_Items.Add(new WaitForSignalItem { m_Timeout = 1f });
            PreviewSession.Start(player, default);
            Seconds(0.05);
            Assert.That(player.OpenSignalWaits(), Is.EqualTo(1));
            player.SignalQuietly();
            Assert.That(player.OpenSignalWaits(), Is.Zero);
            Seconds(0.05);
            Assert.That(player.OpenSignalWaits(), Is.EqualTo(1), "the second, from the next tick");
            player.Stop();
            Assert.That(player.OpenSignalWaits(), Is.Zero);
        }

        [Test]
        public void PX08f_AWaitWithoutATimeoutIsCountedAndTold()
        {
            FeedbackPlayer untimed = Waiter(0f, out _);
            PreviewSession.Start(untimed, default);
            Seconds(0.2);
            Assert.That(untimed.OpenSignalWaits(out int none), Is.EqualTo(1));
            Assert.That(none, Is.EqualTo(1));
            Assert.That(Text(PreviewNoticeKind.SignalWait), Does.Contain("no timeout"));
            PreviewSession.Current?.Stop();
            FeedbackPlayer timed = Waiter(0.5f, out _);
            PreviewSession.Start(timed, default);
            Seconds(0.2);
            Assert.That(timed.OpenSignalWaits(out none), Is.EqualTo(1));
            Assert.That(none, Is.Zero);
            Assert.That(Text(PreviewNoticeKind.SignalWait), Does.Contain("or the timeout"));
        }

        // no auto end — it waits holding the lock, as the runtime waits
        [Test]
        public void PX08g_AWaitWithoutATimeoutHoldsTheSessionUntilTheSignal()
        {
            FeedbackPlayer player = Waiter(0f, out _);
            PreviewSession.Start(player, default);
            Seconds(3.0);
            Assert.That(PreviewSession.Current, Is.Not.Null);
            Assert.That(player.OpenSignalWaits(), Is.EqualTo(1));
            player.SignalQuietly();
            Seconds(0.3);
            Assert.That(PreviewSession.Current, Is.Null);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.AutoEnd));
        }

        // ---- the camera shake — 'the Scene view's camera stays' is a manual check (no Scene view in batch)

        [Test]
        public void PX09a_TheMainCameraShakesAndComesBack()
        {
            Camera camera = MainCamera(new Vector3(0f, 1f, -10f));
            Vector3 home = camera.transform.position;
            FeedbackPlayer player = Player("Shaker");
            CameraShake(player);
            PreviewSession.Start(player, default);
            Seconds(0.13);
            Assert.That(Moved(camera.transform, home), Is.True);
            PreviewSession.Current?.Stop();
            Assert.That(Moved(camera.transform, home), Is.False);
        }

        [Test]
        public void PX09b_APrefabStageShakesTheMainScenesCamera()
        {
            Camera camera = MainCamera(new Vector3(0f, 1f, -10f));
            Vector3 home = camera.transform.position;
            var root = new GameObject("StageShaker");
            CameraShake(root.AddComponent<FeedbackPlayer>());
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            PrefabStage stage = PrefabStageUtility.OpenPrefab(PrefabPath);
            PreviewSession.Start(stage.prefabContentsRoot.GetComponent<FeedbackPlayer>(), default);
            Seconds(0.13);
            Assert.That(Moved(camera.transform, home), Is.True);
            Assert.That(PreviewSession.Current.Touches(m_Scene), Is.True, "the session touches the main scene");
            Assert.That(Kinds(), Has.Member(PreviewNoticeKind.CameraMainScene));
            PreviewSession.Current?.Stop();
            Assert.That(Moved(camera.transform, home), Is.False);
        }

        [Test]
        public void PX09c_TheCameraNotices()
        {
            FeedbackPlayer player = Player("Shaker");
            CameraShake(player);
            NoMainCamera();
            PreviewSession.Start(player, default);
            Seconds(0.05);
            Assert.That(Kinds(), Has.Member(PreviewNoticeKind.NoMainCamera));
            PreviewSession.Current?.Stop();
            MainCamera(Vector3.zero);
            PreviewSession.Start(player, default);
            Seconds(0.05);
            Assert.That(Kinds(), Has.Member(PreviewNoticeKind.CameraGameView));
            Assert.That(Kinds(), Has.No.Member(PreviewNoticeKind.NoMainCamera));
        }

        // ---- the channel receivers — a session that bound none would leave a channel shake doing nothing

        [Test]
        public void PX10a_TheReceiversOfTheOpenScenesShakeAndComeBack()
        {
            CameraShakeReceiver a = Receiver("CamA", Vector3.zero);
            CameraShakeReceiver b = null;
            SecondScene(() => b = Receiver("CamB", new Vector3(5f, 0f, 0f)));
            FeedbackPlayer player = Player("Shaker");
            ChannelShake(player, 1f);
            PreviewSession.Start(player, default);
            Seconds(0.13);
            Assert.That(Moved(a.transform, Vector3.zero) && Moved(b.transform, new Vector3(5f, 0f, 0f)), Is.True, "both shake");
            Assert.That(AnimationMode.IsPropertyAnimated(b.transform, "m_LocalPosition.x"), Is.True, "recorded");
            PreviewSession.Current?.Stop();
            Assert.That(a.IsLive || b.IsLive, Is.False, "let go");
            Assert.That(Moved(a.transform, Vector3.zero) || Moved(b.transform, new Vector3(5f, 0f, 0f)), Is.False, "back");
        }

        [Test]
        public void PX10b_AReceiverThatIsOffIsNotShaken()
        {
            CameraShakeReceiver on = Receiver("On", Vector3.zero);
            CameraShakeReceiver off = Receiver("Off", new Vector3(5f, 0f, 0f));
            off.enabled = false;
            FeedbackPlayer player = Player("Shaker");
            ChannelShake(player, 1f);
            PreviewSession.Start(player, default);
            Seconds(0.13);
            Assert.That(Moved(on.transform, Vector3.zero), Is.True);
            Assert.That(Moved(off.transform, new Vector3(5f, 0f, 0f)), Is.False);
        }

        [Test]
        public void PX10c_AFeedbackReceiverIsNotBound()
        {
            var go = new GameObject("Commanded");
            go.AddComponent<FeedbackPlayer>();
            var command = go.AddComponent<FeedbackReceiver>();
            Receiver("CamA", Vector3.zero);
            FeedbackPlayer player = Player("Shaker");
            ChannelShake(player, 1f);
            PreviewSession.Start(player, default);
            Seconds(0.05);
            Assert.That(command.IsRegistered, Is.False);
        }

        [Test]
        public void PX10d_APrefabStageAndTheMainScene()
        {
            CameraShakeReceiver main = Receiver("MainCam", Vector3.zero);
            var root = new GameObject("StageRoot");
            ChannelShake(root.AddComponent<FeedbackPlayer>(), 1f);
            var stageCamera = new GameObject("StageCam");
            stageCamera.transform.SetParent(root.transform, false);
            stageCamera.transform.localPosition = new Vector3(3f, 0f, 0f);
            stageCamera.AddComponent<CameraShakeReceiver>();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            PrefabStage stage = PrefabStageUtility.OpenPrefab(PrefabPath);
            CameraShakeReceiver inStage = stage.prefabContentsRoot.GetComponentInChildren<CameraShakeReceiver>();
            Vector3 stageHome = inStage.transform.position;
            PreviewSession.Start(stage.prefabContentsRoot.GetComponent<FeedbackPlayer>(), default);
            Seconds(0.13);
            Assert.That(Moved(main.transform, Vector3.zero), Is.True, "the main scene's");
            Assert.That(Moved(inStage.transform, stageHome), Is.True, "the stage's");
        }

        // edit mode runs no OnDisable — the check lets it go at the next tick, back at its place
        [Test]
        public void PX10e_AReceiverTurnedOffIsLetGo()
        {
            CameraShakeReceiver a = Receiver("CamA", Vector3.zero);
            FeedbackPlayer player = Player("Shaker");
            ChannelShake(player, 1f);
            PreviewSession.Start(player, default);
            Seconds(0.13);
            Assert.That(Moved(a.transform, Vector3.zero), Is.True);
            a.enabled = false;
            Seconds(0.05);
            Assert.That(Moved(a.transform, Vector3.zero), Is.False);
            Seconds(0.05);
            Assert.That(Moved(a.transform, Vector3.zero), Is.False);
        }

        [Test]
        public void PX10f_AnUnwrittenReceiversSceneClosesUnderTheSession()
        {
            CameraShakeReceiver a = Receiver("CamA", Vector3.zero);
            Scene second = SecondScene(() => Receiver("CamB", new Vector3(5f, 0f, 0f)));
            FeedbackPlayer player = Player("Shaker");
            ChannelShake(player, 0.5f, 0.2f);
            PreviewSession.Start(player, default);
            Seconds(0.1);
            EditorSceneManager.CloseScene(second, true);
            Assert.That(PreviewSession.Current, Is.Not.Null, "goes on");
            Seconds(0.2);
            Assert.That(PreviewSession.Current, Is.Not.Null);
            Assert.That(Moved(a.transform, Vector3.zero), Is.True, "the one left shakes");
        }

        [Test]
        public void PX10g_AWrittenReceiversSceneClosingEndsTheSession()
        {
            Scene second = SecondScene(() => Receiver("CamB", new Vector3(5f, 0f, 0f)));
            FeedbackPlayer player = Player("Shaker");
            ChannelShake(player, 1f);
            PreviewSession.Start(player, default);
            Seconds(0.13);
            EditorSceneManager.CloseScene(second, true);
            Assert.That(PreviewSession.Current, Is.Null);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.SceneClosing));
        }

        // a shake reaches the receivers bound at its Begin: the one of a scene opened later is reached by the next shake
        [Test]
        public void PX10h_AReceiverOfASceneOpenedDuringThePreview()
        {
            Receiver("CamA", Vector3.zero);
            SecondScene(() => Receiver("CamB", new Vector3(5f, 0f, 0f)), true);
            FeedbackPlayer player = Player("Shaker");
            ChannelShake(player, 1f);
            PreviewSession.Start(player, default);
            Seconds(0.05);
            Scene opened = EditorSceneManager.OpenScene(Scene2Path, OpenSceneMode.Additive);
            CameraShakeReceiver b = opened.GetRootGameObjects()[0].GetComponent<CameraShakeReceiver>();
            Assert.That(PreviewSession.Current.Receivers.Bound, Has.Member(b), "bound when it opened");
            Seconds(0.05);
            Assert.That(Moved(b.transform, new Vector3(5f, 0f, 0f)), Is.False, "the running shake began before it");
            PreviewSession.Start(player, default);
            Seconds(0.1);
            Assert.That(Moved(b.transform, new Vector3(5f, 0f, 0f)), Is.True, "the next shake");
        }

        [Test]
        public void PX10i_AReceiverTurnedOnAgainIsBoundAgain()
        {
            CameraShakeReceiver a = Receiver("CamA", Vector3.zero);
            FeedbackPlayer player = Player("Shaker");
            ChannelShake(player, 2f);
            PreviewSession.Start(player, default);
            Seconds(0.1);
            a.enabled = false;
            Seconds(0.05);
            Assert.That(PreviewSession.Current.Receivers.Parked, Has.Member(a));
            a.enabled = true;
            Seconds(0.05);
            PreviewSession.Start(player, default);
            Seconds(0.1);
            Assert.That(PreviewSession.Current.Receivers.Bound, Has.Member(a));
            Assert.That(Moved(a.transform, Vector3.zero), Is.True);
        }

        // the scenes in order, the roots in order, depth first — past a play's receiver count (4) the first ones shake
        [Test]
        public void PX10j_TheBindingOrderAndAPlaysReceiverCount()
        {
            CameraShakeReceiver a = Receiver("A", Vector3.zero);
            Receiver("B", new Vector3(10f, 0f, 0f));
            CameraShakeReceiver child = Receiver("A.child", new Vector3(20f, 0f, 0f));
            child.transform.SetParent(a.transform, true);
            CameraShakeReceiver c = Receiver("C", new Vector3(30f, 0f, 0f));
            CameraShakeReceiver d = null;
            SecondScene(() => d = Receiver("D", new Vector3(40f, 0f, 0f)));
            FeedbackPlayer player = Player("Shaker");
            ChannelShake(player, 1f);
            PreviewSession.Start(player, default);
            Seconds(0.13);
            Assert.That(string.Join(",", PreviewSession.Current.Receivers.Bound.Select(r => r.name)), Is.EqualTo("A,A.child,B,C,D"));
            Assert.That(Moved(c.transform, new Vector3(30f, 0f, 0f)), Is.True, "the fourth");
            Assert.That(Moved(d.transform, new Vector3(40f, 0f, 0f)), Is.False, "the fifth");
        }

        // one off at the start waits in the parked list: on, it is bound at the next tick and the next shake reaches it
        [Test]
        public void PX10k_AReceiverOffAtTheStartTurnedOn()
        {
            CameraShakeReceiver a = Receiver("CamA", Vector3.zero);
            a.enabled = false;
            FeedbackPlayer player = Player("Shaker");
            ChannelShake(player, 2f);
            PreviewSession.Start(player, default);
            Seconds(0.1);
            Assert.That(PreviewSession.Current.Receivers.Parked, Has.Member(a));
            Assert.That(Moved(a.transform, Vector3.zero), Is.False);
            a.enabled = true;
            Seconds(0.05);
            Assert.That(PreviewSession.Current.Receivers.Bound, Has.Member(a));
            Assert.That(Moved(a.transform, Vector3.zero), Is.False, "the running shake began before it");
            PreviewSession.Start(player, default);
            Seconds(0.1);
            Assert.That(Moved(a.transform, Vector3.zero), Is.True, "the next shake");
        }

        // ---- a MotionDriver as the subject

        [Test]
        public void PX11a_ALoopDriversSessionHasNoAutoEnd()
        {
            MotionDriver driver = LoopDriver("Bob", new Vector3(1f, 2f, 3f));
            Assert.That(PreviewSession.Start(driver), Is.EqualTo(PreviewRefusal.None));
            Seconds(0.25);
            Assert.That(Moved(driver.transform, new Vector3(1f, 2f, 3f)), Is.True);
            Assert.That(AnimationMode.IsPropertyAnimated(driver.transform, "m_LocalPosition.x"), Is.True, "recorded");
            Seconds(2.0);
            Assert.That(PreviewSession.Current, Is.Not.Null);
        }

        [Test]
        public void PX11b_TheSameDriverStartsOver()
        {
            MotionDriver driver = LoopDriver("Bob", Vector3.zero);
            PreviewSession.Start(driver);
            Seconds(0.5);
            Assert.That(driver.Seconds, Is.GreaterThan(0.3));
            PreviewSession session = PreviewSession.Current;
            PreviewSession.Start(driver);
            Assert.That(PreviewSession.Current, Is.SameAs(session), "the same session");
            Assert.That(driver.Seconds, Is.LessThan(0.05));
        }

        // a saved field's edit is not a recorded property — the driver prepares again at the next tick
        [Test]
        public void PX11c_AnEditOfTheDriverPreparesItAgain()
        {
            MotionDriver driver = LoopDriver("Bob", Vector3.zero);
            PreviewSession.Start(driver);
            Seconds(0.5);
            UserEdit.Inspector(driver, "m_Intensity", p => p.floatValue = 2f);
            Seconds(0.05);
            Assert.That(PreviewSession.Current, Is.Not.Null);
            Assert.That(driver.Seconds, Is.LessThan(0.2));
        }

        [Test]
        public void PX11d_TheEndLetsTheDriverGo()
        {
            MotionDriver driver = LoopDriver("Bob", new Vector3(1f, 0f, 0f));
            PreviewSession.Start(driver);
            Seconds(0.25);
            PreviewSession.Current?.Stop();
            Assert.That(Moved(driver.transform, new Vector3(1f, 0f, 0f)), Is.False, "back");
            Assert.That(driver.IsRunning || driver.IsPrepared, Is.False, "let go");
        }

        [Test]
        public void PX11e1_TheDriverDestroyedEndsIt()
        {
            MotionDriver driver = LoopDriver("Bob", Vector3.zero);
            PreviewSession.Start(driver);
            Seconds(0.2);
            Object.DestroyImmediate(driver.gameObject);
            Pump(1);
            Assert.That(PreviewSession.Current, Is.Null);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.SubjectDestroyed));
        }

        [Test]
        public void PX11e2_TheDriverDeselectedEndsIt()
        {
            MotionDriver driver = LoopDriver("Bob", Vector3.zero);
            PreviewSession.EndOnDeselect = true;
            Selection.activeGameObject = driver.gameObject;
            PreviewSession.Start(driver);
            Seconds(0.1);
            Selection.activeObject = null;
            PreviewSession.Current?.SelectionChanged();
            Assert.That(PreviewSession.Current, Is.Null);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.Deselected));
        }

        [Test]
        public void PX11e3_TheDriversSceneClosingEndsIt()
        {
            MotionDriver driver = null;
            Scene second = SecondScene(() => driver = LoopDriver("Bob", Vector3.zero));
            PreviewSession.Start(driver);
            Seconds(0.1);
            EditorSceneManager.CloseScene(second, true);
            Assert.That(PreviewSession.Current, Is.Null);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.SceneClosing));
        }

        [Test]
        public void PX11e4_APlayersStartReplacesIt()
        {
            MotionDriver driver = LoopDriver("Bob", new Vector3(1f, 0f, 0f));
            PreviewSession.Start(driver);
            Seconds(0.25);
            FeedbackPlayer player = Player("Mover");
            On<PositionItem>(player, null, Tween(1f), 0.5f);
            PreviewSession.Start(player, default);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.Replaced));
            Assert.That(driver.IsRunning, Is.False);
            Assert.That(Moved(driver.transform, new Vector3(1f, 0f, 0f)), Is.False);
        }

        [Test]
        public void PX11e5_AnEditOfARecordedPropertyEndsIt()
        {
            MotionDriver driver = LoopDriver("Bob", Vector3.zero);
            PreviewSession.Start(driver);
            Seconds(0.25);
            UserEdit.Inspector(driver.transform, "m_LocalPosition.y", p => p.floatValue = 7f);
            Pump(1);
            Assert.That(PreviewSession.Current, Is.Null);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.UserEdit));
            Assert.That(driver.transform.localPosition.y, Is.EqualTo(7f), "the edit stays");
        }

        [Test]
        public void PX11f_ADriverWithoutAnItemIsRefused()
        {
            var driver = new GameObject("Empty").AddComponent<MotionDriver>();
            Assert.That(PreviewSession.Start(driver), Is.EqualTo(PreviewRefusal.PrepareFailed));
            Assert.That(PreviewSession.Current, Is.Null);
            Assert.That(AnimationMode.InAnimationMode(), Is.False, "the lock is free");
        }

        // its Bind prepares and starts at once — the receivers bound before it are reached
        [Test]
        public void PX11g_AChannelShakeDriverShakesTheReceivers()
        {
            CameraShakeReceiver a = Receiver("CamA", Vector3.zero);
            var driver = new GameObject("ShakeDriver").AddComponent<MotionDriver>();
            var item = new CameraShakeItem { m_Motion = Shake() };
            item.m_Target.m_Mode = TargetRefMode.Channel;
            item.m_Timing.m_Duration = 1f;
            driver.m_Item = item;
            driver.m_Mode = MotionDriverMode.Loop;
            PreviewSession.Start(driver);
            Seconds(0.13);
            Assert.That(Moved(a.transform, Vector3.zero), Is.True);
        }

        [Test]
        public void PX11h_APrefabStageDriverEndsWithTheStage()
        {
            var root = new GameObject("Root");
            LoopDriver("Bob", new Vector3(1f, 0f, 0f)).transform.SetParent(root.transform, false);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            PrefabStage stage = PrefabStageUtility.OpenPrefab(PrefabPath);
            PreviewSession.Start(stage.prefabContentsRoot.GetComponentInChildren<MotionDriver>());
            Seconds(0.2);
            Assert.That(PreviewSession.Current, Is.Not.Null);
            StageUtility.GoToMainStage();
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.StageClosing));
        }

        [Test]
        public void PX11h2_AFollowDriverFollowsAMovedTarget()
        {
            var target = new GameObject("FollowTarget").transform;
            var follower = new GameObject("Follower");
            var driver = follower.AddComponent<MotionDriver>();
            var spring = new PositionItem { m_Motion = new SpringMotion { m_Params = new SpringParams { m_Mode = SpringMode.MoveTo, m_Value = Vector4.zero, m_Frequency = 6f, m_Damping = 0.6f, m_Settle = 0.001f } } };
            spring.m_Target.m_Mode = TargetRefMode.Self;
            spring.m_Timing.m_Duration = 1f;
            driver.m_Item = spring;
            driver.m_Mode = MotionDriverMode.Follow;
            driver.m_FollowTarget = target;
            PreviewSession.Start(driver);
            Seconds(0.1);
            target.position = new Vector3(4f, 0f, 0f);
            Seconds(1.5);
            Assert.That(follower.transform.position.x, Is.EqualTo(4f).Within(0.05f));
            PreviewSession.Current?.Stop();
            Assert.That(follower.transform.position.x, Is.EqualTo(0f), "back");
        }

        // it runs — the bound path does not look at the enabled flag; a notice tells it runs in play only while on
        [Test]
        public void PX11i_ADriverThatIsOffRuns()
        {
            MotionDriver driver = LoopDriver("Bob", new Vector3(1f, 0f, 0f));
            driver.enabled = false;
            Assert.That(PreviewSession.Start(driver), Is.EqualTo(PreviewRefusal.None));
            Seconds(0.25);
            Assert.That(driver.IsRunning, Is.True);
            Assert.That(Moved(driver.transform, new Vector3(1f, 0f, 0f)), Is.True);
        }

        // ---- the notices' data

        [Test]
        public void PX20a_TheNoticesOfASession()
        {
            NoMainCamera();
            FeedbackPlayer player = Player("Busy");
            CameraShake(player);
            player.m_Items.Add(new HitStopItem { m_FreezeDuration = 0.5f, m_MinimumTimeScale = 0f });
            player.m_Items.Add(new UnityEventItem());
            player.m_Items.Add(new WaitForSignalItem { m_Timeout = 2f });
            PreviewSession.Start(player, default);
            Seconds(0.1);
            Assert.That(Kinds(), Is.SupersetOf(new[]
            {
                PreviewNoticeKind.Locked, PreviewNoticeKind.HitStop, PreviewNoticeKind.UnityEvents, PreviewNoticeKind.SignalWait,
                PreviewNoticeKind.NoMainCamera, PreviewNoticeKind.ExecuteAlways,
            }));
        }

        [Test]
        public void PX20b_TheSettingsAssetsDifferences()
        {
            Settings(s =>
            {
                s.m_Categories = new[]
                {
                    new CategoryDefault { m_Category = FeedbackCategory.Camera, m_Enabled = false, m_Scale = 1f },
                    new CategoryDefault { m_Category = FeedbackCategory.Motion, m_Enabled = true, m_Scale = 0.5f },
                };
                s.m_FlashLimit = 0;
                s.m_ShakeReceiversPerPlay = 2;
            });
            FeedbackPlayer player = Player("Busy");
            CameraShake(player);
            On<PositionItem>(player, null, Tween(1f), 1f);
            PreviewSession.Start(player, default);
            Seconds(0.05);
            Assert.That(Kinds(), Is.SupersetOf(new[]
            {
                PreviewNoticeKind.CategoryOff, PreviewNoticeKind.CategoryScale, PreviewNoticeKind.FlashLimit, PreviewNoticeKind.ShakeReceivers,
            }));
        }

        [Test]
        public void PX20c_ACategoryTheItemsDoNotUseIsNotTold()
        {
            Settings(s => s.m_Categories = new[] { new CategoryDefault { m_Category = FeedbackCategory.Particles, m_Enabled = false, m_Scale = 1f } });
            FeedbackPlayer player = Player("Mover");
            On<PositionItem>(player, null, Tween(1f), 1f);
            PreviewSession.Start(player, default);
            Seconds(0.05);
            Assert.That(Kinds(), Has.No.Member(PreviewNoticeKind.CategoryOff));
        }

        [Test]
        public void PX20d_NoSettingsAssetOrTheDefaults()
        {
            Assume.That(Resources.Load<FeedbackSettings>(FeedbackSettings.ResourcePath), Is.Null, "the project has a settings asset");
            FeedbackPlayer player = Player("Mover");
            On<PositionItem>(player, null, Tween(1f), 1f);
            PreviewSession.Start(player, default);
            Seconds(0.05);
            Assert.That(string.Join(",", Kinds()), Is.EqualTo("Locked,ExecuteAlways"), "no asset");
            PreviewSession.Current?.Stop();
            Settings(s => { });
            PreviewSession.Start(player, default);
            Seconds(0.05);
            Assert.That(string.Join(",", Kinds()), Is.EqualTo("Locked,ExecuteAlways"), "the defaults");
        }

        [Test]
        public void PX20e_NoSessionLeavesTheListEmpty()
        {
            var notices = new List<PreviewNotice> { new PreviewNotice(PreviewNoticeKind.Locked, "left") };
            PreviewNotices.Collect(null, notices);
            Assert.That(notices, Is.Empty);
        }

        [Test]
        public void PX20f_NoReceiversAndASubjectThatIsOff()
        {
            FeedbackPlayer channel = Player("Channel");
            ChannelShake(channel, 1f);
            PreviewSession.Start(channel, default);
            Seconds(0.05);
            Assert.That(Kinds(), Has.Member(PreviewNoticeKind.NoReceivers), "no receiver on the channel");
            PreviewSession.Current?.Stop();
            Receiver("CamR", new Vector3(5f, 0f, 0f));
            PreviewSession.Start(channel, default);
            Seconds(0.05);
            Assert.That(Kinds(), Has.No.Member(PreviewNoticeKind.NoReceivers), "one bound");
            PreviewSession.Current?.Stop();
            MotionDriver driver = LoopDriver("Off", Vector3.zero);
            driver.enabled = false;
            PreviewSession.Start(driver);
            Seconds(0.05);
            Assert.That(Kinds(), Has.Member(PreviewNoticeKind.SubjectOff));
            Assert.That(Text(PreviewNoticeKind.SubjectOff), Is.EqualTo("In play this MotionDriver runs only while it is on."));
        }
    }
}
