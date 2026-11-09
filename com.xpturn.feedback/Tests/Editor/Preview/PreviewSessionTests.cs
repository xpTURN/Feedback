using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the session's round trip, the driver's rules, the recorder before the prepare, the end's order and its once, the −0 and
    // GameObject records, the tick cap. The session runs on the test's clock — an update is 1/120 s and the test runs the
    // updates. The objects live in a saved scene of their own next to the editor's; its dirty flag is the check
    public sealed class PreviewSessionTests
    {
        const string ScenePath = "Assets/PreviewSessionTests.unity";
        const string OtherDriver = "PreviewSessionTests.Other";
        const double UpdateStep = 1.0 / 120.0;
        static readonly Vector3 s_Home = new Vector3(1f, 2f, 3f);

        static double s_Now;

        IPreviewRecordSource m_Source;
        Func<double> m_Clock;
        PreviewTestSource m_Test;
        Scene m_Scene;
        Scene m_Active;

        [SetUp]
        public void SetUp()
        {
            m_Source = PreviewRecordPaths.Source;
            m_Clock = PreviewClock.Source;
            m_Test = new PreviewTestSource();
            PreviewRecordPaths.Source = m_Test;
            s_Now = 0;
            PreviewClock.Source = () => s_Now;
            m_Active = SceneManager.GetActiveScene();
            // a new scene cannot be made additively next to an untitled one — an empty scene file, opened
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            m_Scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(m_Scene);
        }

        [TearDown]
        public void TearDown()
        {
            PreviewSession.Current?.Stop();
            // the drivers a failed test left
            foreach (AnimationModeDriver driver in Resources.FindObjectsOfTypeAll<AnimationModeDriver>())
            {
                if (driver == null || (driver.name != PreviewDriver.Name && driver.name != OtherDriver))
                    continue;
                if (AnimationMode.InAnimationMode(driver))
                    AnimationMode.StopAnimationMode(driver);
                Object.DestroyImmediate(driver);
            }
            PreviewRecordPaths.Source = m_Source;
            PreviewClock.Source = m_Clock;
            if (m_Active.IsValid())
                SceneManager.SetActiveScene(m_Active);
            if (m_Scene.IsValid())
                EditorSceneManager.CloseScene(m_Scene, true);
            AssetDatabase.DeleteAsset(ScenePath);
        }

        static void Pump(int updates)
        {
            for (int i = 0; i < updates; i++)
            {
                s_Now += UpdateStep;
                PreviewSession.Current?.Update();
            }
        }

        // the updates, and whether the value was off its start at any of them — a shake is 0 at its whole periods, so one
        // update's value can sit on its start
        static bool Pump(int updates, Func<bool> moved)
        {
            bool any = false;
            for (int i = 0; i < updates; i++)
            {
                s_Now += UpdateStep;
                PreviewSession.Current?.Update();
                any |= moved();
            }
            return any;
        }

        static T Item<T>(FeedbackPlayer player, FeedbackMotion motion, float duration) where T : TargetItem, new()
        {
            var item = new T();
            item.m_Target.m_Mode = TargetRefMode.Self;
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            player.m_Items.Add(item);
            return item;
        }

        // shakes its own position (an Offset) and tweens its light (a Persist)
        static FeedbackPlayer Player(string name, float shake, float tween)
        {
            var go = new GameObject(name);
            go.transform.localPosition = s_Home;
            go.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
            go.AddComponent<Light>().intensity = 1.25f;
            var player = go.AddComponent<FeedbackPlayer>();
            Item<PositionItem>(player, new ShakeMotion(), shake);
            Item<LightIntensityItem>(player, new TweenMotion(), tween);
            return player;
        }

        static FeedbackPlayer Player(string name, float duration) => Player(name, duration, duration);

        // a SetActive item on another object — Set's and Blink's value is on by default: 0 turns it off
        static GameObject Toggled(FeedbackPlayer player, string name, FeedbackMotion motion, float duration)
        {
            var go = new GameObject(name);
            var item = Item<SetActiveItem>(player, motion, duration);
            item.m_Target.m_Mode = TargetRefMode.Direct;
            item.m_Target.m_Object = go;
            return go;
        }

        static GameObject SetOff(FeedbackPlayer player, string name)
        {
            var set = new SetMotion();
            set.m_Params.m_Value = Vector4.zero;
            return Toggled(player, name, set, 0.1f);
        }

        static SerializedSnapshot Snapshot(FeedbackPlayer player)
            => SerializedSnapshot.Take(player.transform, player.GetComponent<Light>(), player.gameObject);

        static void Save(Scene scene) => Assert.That(EditorSceneManager.SaveScene(scene), Is.True);

        static int OurDrivers()
        {
            int n = 0;
            foreach (AnimationModeDriver driver in Resources.FindObjectsOfTypeAll<AnimationModeDriver>())
            {
                if (driver != null && driver.name == PreviewDriver.Name)
                    n++;
            }
            return n;
        }

        static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

        static string Bits(Quaternion q) => $"{Bits(q.x):X8} {Bits(q.y):X8} {Bits(q.z):X8} {Bits(q.w):X8}";

        static void AssertFree()
        {
            Assert.That(PreviewSession.Current, Is.Null, "no session");
            Assert.That(AnimationMode.InAnimationMode(), Is.False, "no lock");
            Assert.That(OurDrivers(), Is.Zero, "no driver");
        }

        // an Offset and two Persists (the light's tween · another object's Set), then the end — the serialized state as
        // before, the scene clean, no lock. A Persist is left by the shutdown and put back by the stop
        [Test]
        public void ES01_RoundTrip()
        {
            FeedbackPlayer player = Player("P", 5f);
            GameObject toggled = SetOff(player, "Toggled");
            Save(m_Scene);
            SerializedSnapshot before = SerializedSnapshot.Take(player.transform, player.GetComponent<Light>(), player.gameObject, toggled);

            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Assert.That(Pump(60, () => player.transform.localPosition != s_Home), Is.True, "the preview wrote");
            Assert.That(toggled.activeSelf, Is.False, "the Set's Persist");
            PreviewSession.Current.Stop();

            SerializedSnapshot after = SerializedSnapshot.Take(player.transform, player.GetComponent<Light>(), player.gameObject, toggled);
            Assert.That(before.Differences(after), Is.Empty);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.Stopped));
            Assert.That(DirtyCheck.Dirty(m_Scene), Is.Empty);
            AssertFree();
        }

        // someone else holds AnimationMode (Timeline's or the Animation window's stand-in) — refused, nothing recorded or
        // written, and their stop leaves the values as they were
        [Test]
        public void ES04_ForeignLock()
        {
            var other = ScriptableObject.CreateInstance<AnimationModeDriver>();
            other.hideFlags = HideFlags.HideAndDontSave;
            other.name = OtherDriver;
            AnimationMode.StartAnimationMode(other);
            FeedbackPlayer player = Player("P", 5f);
            SerializedSnapshot before = Snapshot(player);

            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.Locked));
            Pump(20);
            Assert.That(PreviewSession.Current, Is.Null);
            Assert.That(before.Differences(Snapshot(player)), Is.Empty, "nothing written");
            Assert.That(m_Test.AllCalls, Is.Zero, "nothing recorded");
            Assert.That(OurDrivers(), Is.Zero);
            AnimationMode.StopAnimationMode(other);
            Object.DestroyImmediate(other);
            Assert.That(before.Differences(Snapshot(player)), Is.Empty, "their stop left ours");
            AssertFree();
        }

        // the driver destroyed under the session — the next update ends it (LockLost) without a stop: the engine put the
        // values back. The scheduler is closed and the runtime let go
        [Test]
        public void ES05_DriverDestroyed()
        {
            FeedbackPlayer player = Player("P", 5f);
            SerializedSnapshot before = Snapshot(player);
            PreviewSession.Start(player, default);
            PreviewSession session = PreviewSession.Current;
            Pump(20);
            Assert.That(before.Differences(Snapshot(player)), Is.Not.Empty, "the preview wrote");

            Object.DestroyImmediate(session.Driver.Instance);
            Pump(2);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.LockLost));
            Assert.That(before.Differences(Snapshot(player)), Is.Empty);
            Assert.That(session.Scheduler.IsShutdown, Is.True);
            Assert.That(player.Runtime, Is.Null);
            AssertFree();
        }

        // a slot let go and made again records again — the runtime let go after the light's tween has ended forgets its slot,
        // and the next play's new slot records the value the tween left. A Rebuild would not: it makes the new runtime before
        // it lets the old one go, and the slots carry on. The end gives the value from before the preview — the engine keeps a
        // property's first record (no code of ours keeps it)
        [Test]
        public void ES11_RecordedTwice()
        {
            FeedbackPlayer player = Player("P", 5f, 0.2f);
            Light light = player.GetComponent<Light>();
            SerializedSnapshot before = Snapshot(player);
            PreviewSession.Start(player, default);
            Pump(60);
            Assert.That(light.intensity, Is.Not.EqualTo(1.25f), "the tween left its value");

            player.ReleaseRuntime();
            player.Play();
            Pump(60);
            Assert.That(m_Test.Calls(light, typeof(LightIntensitySlot)), Is.EqualTo(2), "recorded again over the left value");
            PreviewSession.Current.Stop();
            Assert.That(before.Differences(Snapshot(player)), Is.Empty);
            AssertFree();
        }

        // the recorder is on before the first slot — each slot records once, before its first write. The same player on a
        // Test scheduler with the recorder put on records nothing (the Preview kind only)
        [Test]
        public void ES12_RecorderBeforePrepare()
        {
            FeedbackPlayer player = Player("P", 5f);
            PreviewSession.Start(player, default);
            Pump(20);
            Assert.That(m_Test.Calls(player.transform, typeof(LocalPositionSlot)), Is.EqualTo(1));
            Assert.That(m_Test.Calls(player.GetComponent<Light>(), typeof(LightIntensitySlot)), Is.EqualTo(1));
            Assert.That(m_Test.AllCalls, Is.EqualTo(2));
            PreviewSession.Current.Stop();

            var test = new FeedbackScheduler(ContextKind.Test);
            test.Context.Layers.Recorder = new PreviewRecorder();
            FeedbackPlayer other = Player("Q", 5f);
            other.Bind(test);
            Assert.That(other.EnsureRuntime(), Is.True);
            other.Play();
            for (int i = 0; i < 10; i++)
                test.Tick(1f / 60f);
            test.Shutdown();
            other.ReleaseRuntime();
            other.Bind(null);
            Assert.That(m_Test.AllCalls, Is.EqualTo(2), "a Test scheduler records nothing");
            AssertFree();
        }

        static string MaterialsLine(string text)
        {
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length - 1; i++)
            {
                if (lines[i].Trim() == "m_Materials:")
                    return lines[i + 1].Trim();
            }
            return "";
        }

        // the slot recorded inside the prepare — the instance slot's swap. Saved during the preview, the scene's m_Materials
        // line is the original reference. A preview's copy is hidden and stays out of the file
        [Test]
        public void ES12_InstanceSwapRecorded()
        {
            var go = new GameObject("R");
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            var player = go.AddComponent<FeedbackPlayer>();
            Item<MaterialColorItem>(player, new FlashMotion(), 5f).m_WriteMode = MaterialWriteMode.Instance;
            Save(m_Scene);
            string before = MaterialsLine(File.ReadAllText(ScenePath));

            PreviewSession.Start(player, default);
            Pump(6);
            Assert.That(renderer.sharedMaterial.name, Does.EndWith("(Feedback)"), "the copy is in");
            Save(m_Scene);
            string during = MaterialsLine(File.ReadAllText(ScenePath));
            PreviewSession.Current.Stop();
            Assert.That(before, Is.Not.Empty);
            Assert.That(during, Is.EqualTo(before));
            Assert.That(renderer.sharedMaterial.name, Is.EqualTo("Default-Material"));
            AssertFree();
        }

        // a rotation whose bits the engine changes when it is read and written back (3 ~ 7 % of them)
        static Quaternion RewrittenRotation()
        {
            var probe = new GameObject("probe").transform;
            var random = new System.Random(3);
            try
            {
                for (int i = 0; i < 2000; i++)
                {
                    probe.localRotation = Quaternion.Euler((float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f);
                    Quaternion read = probe.localRotation;
                    probe.localRotation = read;
                    if (Bits(probe.localRotation) != Bits(read))
                        return read;
                }
            }
            finally
            {
                Object.DestroyImmediate(probe.gameObject);
            }
            Assert.Fail("no rotation changed when written back");
            return default;
        }

        // the stop puts the recorded value back bit for bit, on a rotation the engine changes when written back — the layers'
        // write-back comes first. The order reversed is not seen here: the layer's read-back judgement guards it as well
        [Test]
        public void ES13_EndOrderBits()
        {
            var go = new GameObject("P");
            go.transform.localRotation = RewrittenRotation();
            Quaternion stored = go.transform.localRotation;
            var player = go.AddComponent<FeedbackPlayer>();
            Item<RotationItem>(player, new ShakeMotion(), 5f);

            PreviewSession.Start(player, default);
            Assert.That(Pump(20, () => Bits(go.transform.localRotation) != Bits(stored)), Is.True, "the preview wrote");
            PreviewSession.Current.Stop();
            Assert.That(Bits(go.transform.localRotation), Is.EqualTo(Bits(stored)));
            AssertFree();
        }

        // the player's object destroyed in edit mode (no OnDestroy) — the next update ends the session, the runtime is let go
        // through the C# object (reserved 0), the lock is free and another object's values are back. Destroyed with Undo and
        // revived after the end, the player's object has its first values
        [Test]
        public void ES14_SubjectDestroyed([Values(false, true)] bool undo)
        {
            FeedbackPlayer player = Player("P", 5f);
            GameObject toggled = SetOff(player, "Toggled");
            SerializedSnapshot before = SerializedSnapshot.Take(toggled);
            PreviewSession.Start(player, default);
            PreviewSession session = PreviewSession.Current;
            Pump(10);
            Assert.That(toggled.activeSelf, Is.False);

            if (undo)
            {
                Undo.IncrementCurrentGroup();
                Undo.DestroyObjectImmediate(player.gameObject);
                Undo.IncrementCurrentGroup();
            }
            else
                Object.DestroyImmediate(player.gameObject);
            Pump(2);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.SubjectDestroyed));
            Assert.That(session.Scheduler.ReservedCount, Is.Zero);
            Assert.That(before.Differences(SerializedSnapshot.Take(toggled)), Is.Empty);
            AssertFree();
            if (!undo)
                return;
            Undo.PerformUndo();
            GameObject revived = Array.Find(m_Scene.GetRootGameObjects(), g => g.name == "P");
            Assert.That(revived, Is.Not.Null);
            Assert.That(revived.transform.localPosition, Is.EqualTo(s_Home));
            Assert.That(revived.GetComponent<Light>().intensity, Is.EqualTo(1.25f));
        }

        // an item's target destroyed under the session — that item is skipped and the session goes on; the stop with a
        // destroyed object recorded logs nothing. Destroyed with Undo and revived during the preview or after the stop, the
        // object has its first values
        [Test]
        public void ES14_ItemTargetDestroyed([Values(false, true)] bool reviveDuring)
        {
            FeedbackPlayer player = Player("P", 5f);
            var target = new GameObject("Target");
            target.transform.localPosition = new Vector3(3f, 3f, 3f);
            PositionItem item = Item<PositionItem>(player, new ShakeMotion(), 5f);
            item.m_Target.m_Mode = TargetRefMode.Direct;
            item.m_Target.m_Object = target;
            PreviewSession.Start(player, default);
            Assert.That(Pump(10, () => target.transform.localPosition != new Vector3(3f, 3f, 3f)), Is.True, "the preview wrote");

            Undo.IncrementCurrentGroup();
            Undo.DestroyObjectImmediate(target);
            Undo.IncrementCurrentGroup();
            Pump(10);
            Assert.That(PreviewSession.Current, Is.Not.Null, "the session goes on");
            if (reviveDuring)
            {
                Undo.PerformUndo();
                Pump(4);
            }
            PreviewSession.Current?.Stop();
            LogAssert.NoUnexpectedReceived();
            if (!reviveDuring)
                Undo.PerformUndo();
            GameObject revived = Array.Find(m_Scene.GetRootGameObjects(), g => g.name == "Target");
            Assert.That(revived, Is.Not.Null);
            Assert.That(revived.transform.localPosition, Is.EqualTo(new Vector3(3f, 3f, 3f)));
            AssertFree();
        }

        sealed class EndingListener : IPlaybackListener
        {
            internal int Calls;

            public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end)
            {
                Calls++;
                PreviewSession.Current?.Stop();
            }
        }

        // a playback's listener ends the session from inside the tick — the end is asked for and runs at the next update,
        // outside the stage: no slot left holding a runner, no exception. Ending straight away is not seen here — the stage's
        // own pass compacts
        [Test]
        public void ES19_EndFromAListener()
        {
            FeedbackPlayer player = Player("P", 0.2f);
            var listener = new EndingListener();
            PreviewSession.Start(player, PlayArgs.Default.WithListener(listener));
            PreviewSession session = PreviewSession.Current;
            Pump(60);
            Assert.That(listener.Calls, Is.EqualTo(1));
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.Stopped));
            Assert.That(session.Scheduler.SlotCount, Is.Zero);
            LogAssert.NoUnexpectedReceived();
            AssertFree();
        }

        // the end runs once — the shutdown calls the listener of the playback it ends and the listener asks for the end again
        [Test]
        public void ES19_EndRunsOnce()
        {
            FeedbackPlayer player = Player("P", 5f);
            var listener = new EndingListener();
            PreviewSession.Start(player, PlayArgs.Default.WithListener(listener));
            Pump(10);
            int ends = PreviewSession.Ends;
            PreviewSession.Current.Stop();
            Assert.That(listener.Calls, Is.EqualTo(1), "the shutdown ended the playback");
            Assert.That(PreviewSession.Ends - ends, Is.EqualTo(1));
            AssertFree();
        }

        // a −0 comes back with its sign — Unity's Mono writes −0 as "0", the recorder gives "-0" itself
        [Test]
        public void ES22_NegativeZero()
        {
            FeedbackPlayer player = Player("P", 5f);
            player.transform.localPosition = new Vector3(-0f, 2f, 3f);
            Assert.That(Bits(player.transform.localPosition.x), Is.EqualTo(unchecked((int)0x80000000)));
            PreviewSession.Start(player, default);
            Pump(20);
            PreviewSession.Current.Stop();
            Assert.That(Bits(player.transform.localPosition.x), Is.EqualTo(unchecked((int)0x80000000)));
            AssertFree();
        }

        // a GameObject's active state is recorded — a Blink turns its object off, a save during the preview writes the file as
        // it was, and the end turns it back on; another object's Set Persist comes back too. Bound from the owner's root it
        // would not be recorded at all
        [Test]
        public void ES23_ActiveRecorded()
        {
            var player = new GameObject("Host").AddComponent<FeedbackPlayer>();
            var blink = new BlinkMotion();
            blink.m_Params.m_Value = Vector4.zero;
            GameObject blinker = Toggled(player, "Blinker", blink, 5f);
            GameObject set = SetOff(player, "Set");
            Save(m_Scene);
            string before = File.ReadAllText(ScenePath);

            PreviewSession.Start(player, default);
            for (int i = 0; i < 400 && blinker.activeSelf; i++)
                Pump(1);
            Assert.That(blinker.activeSelf, Is.False, "blinked off");
            Assert.That(set.activeSelf, Is.False, "the Set's Persist");
            Save(m_Scene);
            string during = File.ReadAllText(ScenePath);
            PreviewSession.Current.Stop();
            Assert.That(during, Is.EqualTo(before));
            Assert.That(blinker.activeSelf, Is.True);
            Assert.That(set.activeSelf, Is.True);
            AssertFree();
        }

        // a tick at most every 1/60 s — 120 updates in a second of the session's clock tick 61 times at most (the first at
        // once)
        [Test]
        public void ES24_TickCap()
        {
            FeedbackPlayer player = Player("P", 5f);
            PreviewSession.Start(player, default);
            PreviewSession session = PreviewSession.Current;
            Pump(120);
            Assert.That(session.Ticks, Is.InRange(30, 61));
            session.Stop();
            AssertFree();
        }
    }
}
