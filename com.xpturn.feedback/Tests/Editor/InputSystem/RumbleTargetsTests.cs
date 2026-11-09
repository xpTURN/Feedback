using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the editor's load · the service's life · play exit · editor pause · reload while playing · the preview · the defaults ·
    // the debugger's row · the input handling rule. Each play round adds its recording pad in play (a play's exit reloads no
    // domain — the pad and the static record stay); a reload round keeps its record in SessionState; the tear-down cleans
    // whatever a stopped round left
    public sealed class RumbleTargetsTests
    {
        const string ScenePath = "Assets/RumbleTargetsTests.unity";
        const string PrefKey = "ScriptCompilationDuringPlay";
        const string PrefSaved = "xpTURN.Feedback.Tests.Rumble.Pref";

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        Scene m_Active;
        Scene m_Scene;
        Func<double> m_Clock;
        IPreviewRecordSource m_Source;
        double m_Now;
        static int s_QuitAt;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PreviewSession.Current?.Stop();
            EditorApplication.isPaused = false;
            if (Application.isPlaying)
                yield return new ExitPlayMode();
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Schedulers.Clear();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            if (m_Clock != null)
            {
                PreviewClock.Source = m_Clock;
                PreviewRecordPaths.Source = m_Source;
                m_Clock = null;
            }
            if (m_Scene.IsValid())
            {
                if (m_Active.IsValid())
                    SceneManager.SetActiveScene(m_Active);
                EditorSceneManager.CloseScene(m_Scene, true);
                AssetDatabase.DeleteAsset(ScenePath);
                m_Scene = default;
            }
            if (SessionState.GetInt(PrefSaved, int.MinValue) != int.MinValue)
            {
                int pref = SessionState.GetInt(PrefSaved, 0);
                if (pref < 0)
                    EditorPrefs.DeleteKey(PrefKey);
                else
                    EditorPrefs.SetInt(PrefKey, pref);
                SessionState.EraseInt(PrefSaved);
            }
            HapticsInputHandlingRule.NewInputOn = () =>
            {
#if ENABLE_INPUT_SYSTEM
                return true;
#else
                return false;
#endif
            };
            RecordingGamepad.RemoveAll();
        }

        FeedbackScheduler Sched(ContextKind kind = ContextKind.Test)
        {
            var s = new FeedbackScheduler(kind);
            m_Schedulers.Add(s);
            return s;
        }

        GameObject Made(string name)
        {
            var go = new GameObject(name);
            if (!m_Scene.IsValid())
                m_Made.Add(go);
            return go;
        }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            GameObject go = Made("HT player");
            go.SetActive(false);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            go.SetActive(true);
            if (s != null)
            {
                p.Bind(s);
                p.EnsureRuntime();
            }
            return p;
        }

        static GamepadRumbleItem Rumble(GamepadRumbleDevice device, float x, float y, float duration)
        {
            var item = new GamepadRumbleItem { m_Device = device };
            item.m_Motion = new FlashMotion { m_Params = new FlashParams { m_Target = new Vector4(x, y, 0f, 0f), m_Attack = 0.01f, m_Hold = 0.98f, m_Release = 0.01f } };
            item.m_Timing.m_Duration = duration;
            return item;
        }

        // a saved additive scene, active · the preview's own clock and record table — the tear-down closes it
        void OpenScene()
        {
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            m_Active = SceneManager.GetActiveScene();
            m_Scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(m_Scene);
            m_Clock = PreviewClock.Source;
            m_Source = PreviewRecordPaths.Source;
            m_Now = 0;
            PreviewClock.Source = () => m_Now;
            PreviewRecordPaths.Source = new PreviewRecordPathTable();
        }

        // ---- the item and the extension service come from the editor's load — after a reload with no play since · the
        // recording gamepad's layout too (the editor test assembly's load)

        [UnityTest]
        public IEnumerator HT02a_TheRegistrationAtTheEditorsLoad()
        {
            EditorUtility.RequestScriptReload();
            yield return new WaitForDomainReload();
            bool registered = FeedbackImplementations.RegisteredTypes.Contains(typeof(GamepadRumbleItem));
            var s = new FeedbackScheduler(ContextKind.Test);
            bool service = GamepadRumbleService.Of(s.Context) != null;
            s.Shutdown();
            Assert.That((registered, service, InputSystem.ListLayouts().Contains("RecordingGamepad")), Is.EqualTo((true, true, true)), "the item · the extension service · the recording layout");
        }

        // ---- made at the preparation (one) · a closed context's closes at once · closed it refuses and sends nothing · owns no
        // object · the close sends (0, 0) to every pad of the ledger (the edit mode's manual ticks)

        [Test]
        public void HT03a_TheServicesLife()
        {
            int objects = Resources.FindObjectsOfTypeAll<GameObject>().Length;
            FeedbackScheduler s = Sched();
            int services = s.Context.ServiceCount;
            Player(s, Rumble(GamepadRumbleDevice.All, 0.5f, 0.5f, 1f));
            Assert.That(s.Context.ServiceCount, Is.EqualTo(services + 1), "made at the preparation");
            GamepadRumbleService svc = GamepadRumbleService.Of(s.Context);
            Assert.That(s.Context.ServiceCount, Is.EqualTo(services + 1), "the same one after");
            Assert.That(Resources.FindObjectsOfTypeAll<GameObject>().Length, Is.EqualTo(objects + 1), "no object but the player's");
            RecordingGamepad a = InputSystem.AddDevice<RecordingGamepad>("HT a");
            RecordingGamepad b = InputSystem.AddDevice<RecordingGamepad>("HT b");
            RecordingGamepad.Count = 0;
            svc.SetRequest(GamepadRumbleService.AllPath, new Vector2(0.5f, 0.5f));
            s.Tick(1f / 60f);
            Vector2 driven = RecordingGamepad.LastFor(b);
            int from = RecordingGamepad.Count;
            s.Shutdown();
            Assert.That(driven, Is.EqualTo(new Vector2(0.5f, 0.5f)), "driven");
            Assert.That(RecordingGamepad.Tail(null, from).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, Is.EqualTo(2), "the close — both pads");
            Assert.That((RecordingGamepad.LastFor(a), RecordingGamepad.LastFor(b)), Is.EqualTo((Vector2.zero, Vector2.zero)), "(0, 0)");
            Assert.That(svc.IsClosed && svc.Refuses(), Is.True, "closed — it refuses");
            svc.SetRequest(GamepadRumbleService.AllPath, new Vector2(0.5f, 0.5f));
            svc.LateApply();
            Assert.That(RecordingGamepad.LastFor(a), Is.EqualTo(Vector2.zero), "closed — it sends nothing");
            FeedbackScheduler closed = Sched();
            closed.Shutdown();
            Assert.That(GamepadRumbleService.Of(closed.Context).IsClosed, Is.True, "a closed context's service closes at once");
        }

        // ---- a play's exit stops the pad (the Default's close at quitting — before the test's quitting handler) and the pad added
        // in play stays

        [UnityTest]
        public IEnumerator HT10a_ThePlaysExitStops()
        {
            yield return new EnterPlayMode();
            RecordingGamepad pad = InputSystem.AddDevice<RecordingGamepad>("HT pad");
            pad.MakeCurrent();
            RecordingGamepad.Count = 0;
            s_QuitAt = -1;
            Application.quitting += () => s_QuitAt = RecordingGamepad.Count;
            FeedbackPlayer player = Player(null, Rumble(GamepadRumbleDevice.Current, 0.5f, 0.25f, 5f));
            player.Play();
            for (int f = 0; f < 10; f++)
                yield return null;
            int before = RecordingGamepad.Count;
            yield return new ExitPlayMode();
            int zeroAt = -1;
            for (int i = 0; i < RecordingGamepad.Count; i++)
            {
                if (RecordingGamepad.Pads[i] == pad && RecordingGamepad.Values[i] == Vector2.zero)
                    zeroAt = i;
            }
            Assert.That(before, Is.GreaterThan(0), "driven in play");
            Assert.That(RecordingGamepad.LastFor(pad), Is.EqualTo(Vector2.zero), "the play's exit stops the motor");
            Assert.That(zeroAt >= before && zeroAt < s_QuitAt, Is.True, "at the exit — before the test's quitting (" + zeroAt + " of " + before + " · " + s_QuitAt + ")");
            Assert.That(pad.added, Is.True, "a pad added in play stays after it");
        }

        // ---- an editor pause stops the pad in its setter (the loop stands — nothing while paused) and the request goes again
        // after it (the batch editor raises focusChanged(true) right after an unpause — the reasons' overlap is RumblePlayTests'
        // seam). The batch editor opens a Game view at the unpause (321 × 552 — every later play gets its screen, a closed view's
        // too): with none before, the test puts the play's screen back as the rendering resolution and closes the view

        static readonly Type s_GameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");

        static EditorWindow[] GameViews() => s_GameView != null ? Resources.FindObjectsOfTypeAll(s_GameView).Cast<EditorWindow>().ToArray() : new EditorWindow[0];

        [UnityTest]
        public IEnumerator HT10b_TheEditorPause()
        {
            yield return new EnterPlayMode();
            RecordingGamepad pad = InputSystem.AddDevice<RecordingGamepad>("HT pad");
            pad.MakeCurrent();
            RecordingGamepad.Count = 0;
            FeedbackPlayer player = Player(null, Rumble(GamepadRumbleDevice.Current, 0.5f, 0.25f, 5f));
            player.Play();
            for (int f = 0; f < 5; f++)
                yield return null;
            int c0 = RecordingGamepad.Count;
            EditorWindow[] views = GameViews();
            int width = Screen.width, height = Screen.height;
            EditorApplication.isPaused = true;
            string atPause = RecordingGamepad.Tail(pad, c0);
            for (int f = 0; f < 5; f++)
                yield return null;
            int c1 = RecordingGamepad.Count;
            EditorApplication.isPaused = false;
            for (int f = 0; f < 5; f++)
                yield return null;
            string after = RecordingGamepad.Tail(pad, c1);
            yield return new ExitPlayMode();
            if (views.Length == 0 && GameViews().Length > 0)
                PlayModeWindow.SetCustomRenderingResolution((uint)width, (uint)height, "HT10b");
            foreach (EditorWindow view in GameViews())
                if (Array.IndexOf(views, view) < 0)
                    view.Close();
            Assert.That(atPause, Does.StartWith("S(0.00,0.00)@"), "the pause's stop — in the setter");
            Assert.That(c1 - c0, Is.EqualTo(1), "nothing while paused");
            Assert.That(after, Does.Contain("S(0.50,0.25)@"), "again after the pause");
        }

        // ---- a reload while playing — the Default's close at beforeAssemblyReload sends (0, 0) first; the play goes on with the
        // pad and nothing resumes

        [UnityTest]
        public IEnumerator HT10c_AReloadWhilePlaying()
        {
            SessionState.EraseString("xpTURN.Feedback.Tests.Rumble.Reload");
            SessionState.SetInt(PrefSaved, EditorPrefs.GetInt(PrefKey, -1));
            EditorPrefs.SetInt(PrefKey, 0);
            yield return new EnterPlayMode();
            RecordingGamepad pad = InputSystem.AddDevice<RecordingGamepad>("HT reload pad");
            pad.MakeCurrent();
            RecordingGamepad.Count = 0;
            AssemblyReloadEvents.beforeAssemblyReload += () => SessionState.SetString("xpTURN.Feedback.Tests.Rumble.Reload", RecordingGamepad.Tail(null, 0));
            FeedbackPlayer player = Player(null, Rumble(GamepadRumbleDevice.Current, 0.5f, 0.25f, 5f));
            player.Play();
            for (int f = 0; f < 10; f++)
                yield return null;
            EditorUtility.RequestScriptReload();
            yield return new WaitForDomainReload();
            bool playing = Application.isPlaying;
            string pads = string.Join(",", InputSystem.devices.Where(d => d is RecordingGamepad).Select(d => d.name));
            for (int f = 0; f < 5; f++)
                yield return null;
            int later = RecordingGamepad.Count;
            yield return new ExitPlayMode();
            Assert.That(SessionState.GetString("xpTURN.Feedback.Tests.Rumble.Reload", ""), Does.Match(@"S\(0\.00,0\.00\)@\d+ $"), "the Default's close sent (0, 0) before the reload");
            Assert.That((playing, pads, later), Is.EqualTo((true, "HT reload pad", 0)), "still playing · the pad · nothing resumed");
        }

        // ---- the preview sends no command (the service's guard — a value binding cannot skip its writes) · its notice, the
        // inspector bar's warning box too · no slot without record paths · the scene clean

        [UnityTest]
        public IEnumerator HT11a_ThePreviewSendsNothing()
        {
            OpenScene();
            RecordingGamepad pad = InputSystem.AddDevice<RecordingGamepad>("HT pad");
            RecordingGamepad.Count = 0;
            FeedbackPlayer player = Player(null, Rumble(GamepadRumbleDevice.All, 0.5f, 0.25f, 0.4f));
            Assert.That(EditorSceneManager.SaveScene(m_Scene), Is.True);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            List<Type> unrecorded = PreviewSession.Current.Unrecorded.ToList();
            var notices = new List<PreviewNotice>();
            PreviewNotices.Collect(PreviewSession.Current, notices);
            var bar = new PreviewBar(player);
            bar.Refresh();
            (string text, DisplayStyle display) box = (bar.Notice.text, bar.Notice.style.display.value);
            for (int i = 0; i < 30; i++)
            {
                m_Now += 1.0 / 60.0;
                PreviewSession.Current?.Update();
                yield return null;
            }
            PreviewSession.Current?.Stop();
            yield return null;
            Assert.That((RecordingGamepad.CountFor(pad), unrecorded.Count, m_Scene.isDirty), Is.EqualTo((0, 0, false)), "no command · no slot without record paths · the scene clean");
            Assert.That(notices.Single(n => n.Kind == PreviewNoticeKind.RumblePlayOnly).Text, Is.EqualTo("Gamepad Rumble plays in play mode only."));
            Assert.That(box, Is.EqualTo(("Gamepad Rumble plays in play mode only.", DisplayStyle.Flex)), "the inspector bar's warning box");
        }

        // ---- the defaults (Current · the Player reference on the parents · the Unscaled clock · the motions' — a By of half · a
        // flash of 0.2 s) · the fields kept through a save · a Player reference that finds no PlayerInput refuses (its reason a
        // development build's)

        [Test]
        public void HT13a_TheDefaultsAndTheFields()
        {
            var item = new GamepadRumbleItem();
            Assert.That((item.m_Device, item.m_Player.m_Mode, item.m_Timing.m_Clock, item.m_Target.m_Mode), Is.EqualTo((GamepadRumbleDevice.Current, TargetRefMode.Parent, ItemClock.Unscaled, TargetRefMode.Direct)));
            var tween = (TweenMotion)MotionDefaults.Create(typeof(GamepadRumbleItem), MotionKind.Tween, out float tweenDuration);
            var flash = (FlashMotion)MotionDefaults.Create(typeof(GamepadRumbleItem), MotionKind.Flash, out float flashDuration);
            var curve = (CurveMotion)MotionDefaults.Create(typeof(GamepadRumbleItem), MotionKind.Curve, out _);
            Assert.That((tween.m_Params.m_Mode, tween.m_Params.m_To, tweenDuration), Is.EqualTo((TweenMode.By, Vector4.one * 0.5f, -1f)), "a Tween changes both motors by half");
            Assert.That((flash.m_Params.m_Target, flashDuration), Is.EqualTo((Vector4.one, 0.2f)), "a flash to full for a fifth of a second");
            Assert.That(curve.m_Params.m_RemapOne, Is.EqualTo(Vector4.one));

            item.m_Device = GamepadRumbleDevice.Player;
            item.m_Player = new TargetRef { m_Mode = TargetRefMode.Child, m_Path = "pad", m_Anchor = "" };
            item.m_Timing.m_Clock = ItemClock.Scaled;
            var back = JsonUtility.FromJson<GamepadRumbleItem>(JsonUtility.ToJson(item));
            Assert.That((back.m_Device, back.m_Player.m_Mode, back.m_Player.m_Path, back.m_Timing.m_Clock), Is.EqualTo((GamepadRumbleDevice.Player, TargetRefMode.Child, "pad", ItemClock.Scaled)), "kept");

            FeedbackScheduler s = Sched();
            GameObject root = Made("HT root");
            GameObject child = Made("HT child");
            child.transform.SetParent(root.transform, false);
            var lone = new GamepadRumbleItem { m_Device = GamepadRumbleDevice.Player, m_Motion = new FlashMotion { m_Params = FlashParams.Default } };
            ItemBinding binding = FeedbackImplementations.Create(lone);
            var context = new PrepareContext(child, null, s.Context, 1);
            bool prepared = binding.Prepare(in context);
            Assert.That((prepared, binding.SkipReason), Is.EqualTo((false, "the target has no such component")), "no PlayerInput in the parents — refused");
            binding.Release();
        }

        // ---- the debugger's row — none read, none made · 'Gamepad rumble' and its five rows

        [Test]
        public void HT14a_TheDebuggerRow()
        {
            FeedbackScheduler s = Sched();
            var reader = new DebuggerReader();
            int services = s.Context.ServiceCount;
            reader.Read(s);
            Assert.That((reader.ExtensionNames.Count, s.Context.ServiceCount), Is.EqualTo((0, services)), "none made — none read, the read makes none");
            Player(s, Rumble(GamepadRumbleDevice.Current, 0.5f, 0.5f, 0.2f));
            reader.Read(s);
            Assert.That(reader.ExtensionNames, Is.EqualTo(new[] { "Gamepad rumble" }));
            Assert.That(reader.ExtensionRows[0].Select(r => r.Label + " " + r.Value), Is.EqualTo(new[] { "devices driven 0", "sending 0", "muted 0", "last low 0", "last high 0" }));
        }

        // ---- FB-S01 — a rumble item while the editor compiled with the old Input Manager only (the seam) · quiet with the new
        // input · quiet without a rumble item

        [Test]
        public void HT19a_FBS01()
        {
            GameObject go = Made("HT host");
            var host = go.AddComponent<FeedbackPlayer>();
            HapticsInputHandlingRule.NewInputOn = () => false;
            Assert.That(FeedbackValidator.Run(host).Where(r => r.Id == "FB-S01"), Is.Empty, "quiet without a rumble item");
            host.m_Items.Add(new PositionItem { m_Motion = new TweenMotion() });
            host.m_Items.Add(Rumble(GamepadRumbleDevice.Current, 1f, 1f, 0.2f));
            List<RuleResult> got = FeedbackValidator.Run(host).Where(r => r.Id == "FB-S01").ToList();
            Assert.That(got.Select(r => r.Item + ":" + r.Severity + ":" + r.Message),
                Is.EqualTo(new[] { "1:Warning:Gamepad Rumble does nothing while Active Input Handling is the old Input Manager only" }));
            Assert.That(got[0].Fixes, Is.Empty, "no fix — the setting restarts the editor");
            HapticsInputHandlingRule.NewInputOn = () => true;
            Assert.That(FeedbackValidator.Run(host).Where(r => r.Id == "FB-S01"), Is.Empty, "quiet with the new input");
        }
    }
}
