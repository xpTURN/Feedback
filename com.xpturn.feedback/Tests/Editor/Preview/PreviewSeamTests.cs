using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    // a prefab instance applied, reverted or unpacked ends the session before it acts; a slot type with no record rows is
    // named before the play and falls back to what animates of its target; the temporaries, the global states and the save
    // states are put back at the end, the save states also around a save. The session runs on the test's clock (an update is
    // 1/120 s); the dirty flag and a prefab stage's auto save need the editor's updates. The fakes register in a test and
    // leave in the tear-down. The fallback's warnings are read by their words
    public sealed class PreviewSeamTests
    {
        const string ScenePath = "Assets/PreviewSeamTests.unity";
        const string OtherScenePath = "Assets/PreviewSeamTests.Other.unity";
        const string PrefabPath = "Assets/PreviewSeamTests.prefab";
        const string OtherDriver = "PreviewSeamTests.Other";
        const double UpdateStep = 1.0 / 120.0;

        static double s_Now;

        IPreviewRecordSource m_Source;
        Func<double> m_Clock;
        PreviewTestSource m_Test;
        Scene m_Scene;
        Scene m_Other;
        Scene m_Active;
        readonly List<string> m_Logs = new List<string>();
        readonly List<IPreviewGlobalState> m_Registered = new List<IPreviewGlobalState>();

        [SetUp]
        public void SetUp()
        {
            PreviewTestBinding.Register();
            m_Source = PreviewRecordPaths.Source;
            m_Clock = PreviewClock.Source;
            m_Test = new PreviewTestSource();
            PreviewRecordPaths.Source = m_Test;
            s_Now = 0;
            PreviewClock.Source = () => s_Now;
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
            foreach (IPreviewGlobalState state in m_Registered)
                PreviewGlobalState.Unregister(state);
            m_Registered.Clear();
            foreach (AnimationModeDriver driver in Resources.FindObjectsOfTypeAll<AnimationModeDriver>())
            {
                if (driver == null || (driver.name != PreviewDriver.Name && driver.name != OtherDriver))
                    continue;
                if (AnimationMode.InAnimationMode(driver))
                    AnimationMode.StopAnimationMode(driver);
                Object.DestroyImmediate(driver);
            }
            // a failed test's temporaries would make the next one's check red
            PreviewRecovery.Run();
            if (StageUtility.GetCurrentStage() is PrefabStage)
                StageUtility.GoToMainStage();
            PreviewRecordPaths.Source = m_Source;
            PreviewClock.Source = m_Clock;
            if (m_Active.IsValid())
                SceneManager.SetActiveScene(m_Active);
            if (m_Other.IsValid())
                EditorSceneManager.CloseScene(m_Other, true);
            if (m_Scene.IsValid())
                EditorSceneManager.CloseScene(m_Scene, true);
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(OtherScenePath);
            AssetDatabase.DeleteAsset(PrefabPath);
        }

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
                m_Logs.Add(message);
        }

        // the warnings logged, by their words
        List<string> Warnings() => new List<string>(m_Logs);

        static string FallbackWarning(Type slotType) => string.Format(PreviewRecorder.FallbackWarning, slotType.Name);

        // a new scene cannot be made additively next to an untitled one — an empty scene file, opened
        static Scene OpenEmpty(string path)
        {
            File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        static void Save(Scene scene) => Assert.That(EditorSceneManager.SaveScene(scene), Is.True);

        static void Pump(int updates)
        {
            for (int i = 0; i < updates; i++)
            {
                s_Now += UpdateStep;
                PreviewSession.Current?.Update();
            }
        }

        // whether the value was off its start at any update
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

        static T ItemOn<T>(FeedbackPlayer player, Object target, FeedbackMotion motion, float duration) where T : TargetItem, new()
        {
            T item = Item<T>(player, motion, duration);
            item.m_Target.m_Mode = TargetRefMode.Direct;
            item.m_Target.m_Object = target;
            return item;
        }

        static FeedbackPlayer LightPlayer(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<Light>().intensity = 1.25f;
            var player = go.AddComponent<FeedbackPlayer>();
            Item<LightIntensityItem>(player, new TweenMotion(), 5f);
            return player;
        }

        // a prefab of a light the preview tweens
        static GameObject LightPrefab()
        {
            var root = new GameObject("Lamp");
            root.AddComponent<Light>().intensity = 1.25f;
            Item<LightIntensityItem>(root.AddComponent<FeedbackPlayer>(), new TweenMotion(), 5f);
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return asset;
        }

        // an instance's override, the inspector's way
        static void Override(Light light, float intensity)
        {
            var serialized = new SerializedObject(light);
            serialized.FindProperty("m_Intensity").floatValue = intensity;
            serialized.ApplyModifiedProperties();
        }

        static List<string> Animated(Object owner)
        {
            var animated = new List<string>();
            SerializedProperty property = new SerializedObject(owner).GetIterator();
            while (property.Next(true))
            {
                if (AnimationMode.IsPropertyAnimated(owner, property.propertyPath))
                    animated.Add(property.propertyPath);
            }
            return animated;
        }

        static void AssertEnded(PreviewEndReason reason)
        {
            Assert.That(PreviewSession.Current, Is.Null, "ended");
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(reason));
        }

        static void AssertFree()
        {
            Assert.That(PreviewSession.Current, Is.Null, "no session");
            Assert.That(GlobalStateCheck.Left(), Is.Empty, "nothing left");
        }

        // an instance whose recorded light it overrides (2 — the asset 1.25) is applied, reverted or unpacked during the
        // preview. The session ends before the operation, which meets the real values: the override applied (2 in the asset
        // and the instance) · the asset's value (1.25) · the override kept as a plain value (2). Without the end the stop
        // leaves the preview's value
        [Test]
        public void ES25_PrefabOperationEndsFirst([Values("apply", "revert", "unpack")] string operation)
        {
            GameObject asset = LightPrefab();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, m_Scene);
            Light light = instance.GetComponent<Light>();
            Override(light, 2f);
            Save(m_Scene);
            PreviewSession.Start(instance.GetComponent<FeedbackPlayer>(), default);
            Assert.That(Pump(30, () => light.intensity != 2f), Is.True, "the preview wrote");
            if (operation == "apply")
                PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.UserAction);
            else if (operation == "revert")
                PrefabUtility.RevertPrefabInstance(instance, InteractionMode.UserAction);
            else
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.UserAction);
            AssertEnded(PreviewEndReason.PrefabOperation);
            Assert.That(light.intensity, Is.EqualTo(operation == "revert" ? 1.25f : 2f));
            if (operation == "apply")
                Assert.That(asset.GetComponent<Light>().intensity, Is.EqualTo(2f), "the override applied");
            AssertFree();
        }

        // an instance in another scene applied during the preview — the session goes on and the previewed light comes back to
        // its override
        [Test]
        public void ES25_AnotherScenesInstanceGoesOn()
        {
            GameObject asset = LightPrefab();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, m_Scene);
            Light light = instance.GetComponent<Light>();
            Override(light, 2f);
            m_Other = OpenEmpty(OtherScenePath);
            var other = (GameObject)PrefabUtility.InstantiatePrefab(asset, m_Other);
            Override(other.GetComponent<Light>(), 4f);
            Save(m_Scene);
            PreviewSession.Start(instance.GetComponent<FeedbackPlayer>(), default);
            Pump(30);
            PrefabUtility.ApplyPrefabInstance(other, InteractionMode.UserAction);
            Assert.That(PreviewSession.Current, Is.Not.Null, "the session goes on");
            PreviewSession.Current.Stop();
            Assert.That(light.intensity, Is.EqualTo(2f));
            Assert.That(asset.GetComponent<Light>().intensity, Is.EqualTo(4f), "the other instance's override applied");
            AssertFree();
        }

        // a slot type with no rows on two targets — the test slot, a user's extension without them. The preflight names it
        // before the play, one warning in its words for both and again in the next session; what binds is what animates — the
        // float, the vector's components, not the string, not another component's property; the source is asked once a slot,
        // a save during the preview writes the file as it was and the end brings the values back
        [Test]
        public void ES17_UnknownSlotType()
        {
            var a = new GameObject("A").AddComponent<PreviewTestTarget>();
            var light = a.gameObject.AddComponent<Light>();
            a.Value = 0.5f;
            a.Offset = new Vector3(1f, 2f, 3f);
            a.Label = "home";
            var b = new GameObject("B").AddComponent<PreviewTestTarget>();
            var player = new GameObject("Host").AddComponent<FeedbackPlayer>();
            ItemOn<PreviewTestItem>(player, a, new TweenMotion(), 5f);
            ItemOn<PreviewTestItem>(player, b, new TweenMotion(), 5f);
            Save(m_Scene);
            string before = File.ReadAllText(ScenePath);

            PreviewSession.Start(player, default);
            Assert.That(PreviewSession.Current.Unrecorded, Is.EqualTo(new[] { typeof(PreviewTestSlot) }), "named before the play");
            Assert.That(Pump(30, () => a.Value != 0.5f), Is.True, "the preview wrote");
            Assert.That(Warnings(), Is.EqualTo(new[] { FallbackWarning(typeof(PreviewTestSlot)) }));
            Assert.That(AnimationMode.IsPropertyAnimated(a, "m_Value"), Is.True);
            Assert.That(AnimationMode.IsPropertyAnimated(a, "m_Offset.x"), Is.True, "a vector's component");
            Assert.That(AnimationMode.IsPropertyAnimated(a, "m_Label"), Is.False, "a string does not animate");
            Assert.That(AnimationMode.IsPropertyAnimated(light, "m_Intensity"), Is.False, "another component's property");
            Assert.That(m_Test.Calls(a, typeof(PreviewTestSlot)), Is.EqualTo(1), "the source once a slot");
            Assert.That(m_Test.Calls(b, typeof(PreviewTestSlot)), Is.EqualTo(1));
            Save(m_Scene);
            Assert.That(File.ReadAllText(ScenePath), Is.EqualTo(before), "a save during the preview");
            PreviewSession.Current.Stop();
            Assert.That(a.Value, Is.EqualTo(0.5f));
            Assert.That(a.Offset, Is.EqualTo(new Vector3(1f, 2f, 3f)));

            m_Logs.Clear();
            PreviewSession.Start(player, default);
            Pump(5);
            Assert.That(Warnings(), Is.EqualTo(new[] { FallbackWarning(typeof(PreviewTestSlot)) }), "again in the next session");
            PreviewSession.Current.Stop();
            AssertFree();
        }

        // a core slot type whose rows are forgotten — the light falls back as a whole: a save during the preview writes it as
        // it was, and the end brings back what the preview wrote and what a setter changed of what animates
        [Test]
        public void ES17_ForgottenRows()
        {
            m_Test.Forgotten.Add(typeof(LightIntensitySlot));
            FeedbackPlayer player = LightPlayer("L");
            Light light = player.GetComponent<Light>();
            light.range = 7f;
            Save(m_Scene);
            string before = File.ReadAllText(ScenePath);
            PreviewSession.Start(player, default);
            Assert.That(Pump(30, () => light.intensity != 1.25f), Is.True, "the preview wrote");
            light.range = 11f;
            Save(m_Scene);
            Assert.That(File.ReadAllText(ScenePath), Is.EqualTo(before));
            PreviewSession.Current.Stop();
            Assert.That(light.intensity, Is.EqualTo(1.25f));
            Assert.That(light.range, Is.EqualTo(7f), "the range animates — kept");
            Assert.That(Warnings(), Is.EqualTo(new[] { FallbackWarning(typeof(LightIntensitySlot)) }));
            AssertFree();
        }

        // a GameObject target whose rows are forgotten — its active state only, by the direct binding: a save during the
        // preview writes it as it was, and a layer the user changes during the preview stays (binding every GameObject
        // property would let the stop undo it)
        [Test]
        public void ES17_GameObjectActiveOnly()
        {
            m_Test.Forgotten.Add(typeof(GameObjectActiveSlot));
            var player = new GameObject("Host").AddComponent<FeedbackPlayer>();
            var toggled = new GameObject("Toggled") { layer = 5 };
            var blink = new BlinkMotion();
            blink.m_Params.m_Value = Vector4.zero;
            ItemOn<SetActiveItem>(player, toggled, blink, 5f);
            Save(m_Scene);
            string before = File.ReadAllText(ScenePath);
            PreviewSession.Start(player, default);
            for (int i = 0; i < 400 && toggled.activeSelf; i++)
                Pump(1);
            Assert.That(toggled.activeSelf, Is.False, "blinked off");
            Assert.That(Animated(toggled), Is.EqualTo(new[] { "m_IsActive" }));
            Save(m_Scene);
            Assert.That(File.ReadAllText(ScenePath), Is.EqualTo(before));
            toggled.layer = 2;
            PreviewSession.Current.Stop();
            Assert.That(toggled.activeSelf, Is.True);
            Assert.That(toggled.layer, Is.EqualTo(2), "the user's layer stays");
            AssertFree();
        }

        // a renderer whose block rows are forgotten — its material reference is recorded, not the reference's m_FileID (the
        // bindings hold no child of a reference)
        [Test]
        public void ES17_ReferenceNotItsChildren()
        {
            m_Test.Forgotten.Add(typeof(MaterialBlockColorSlot));
            var go = new GameObject("R");
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            var player = go.AddComponent<FeedbackPlayer>();
            Item<MaterialColorItem>(player, new TweenMotion(), 5f);
            PreviewSession.Start(player, default);
            Pump(20);
            Assert.That(AnimationMode.IsPropertyAnimated(renderer, "m_Materials.Array.data[0]"), Is.True);
            Assert.That(AnimationMode.IsPropertyAnimated(renderer, "m_Materials.Array.data[0].m_FileID"), Is.False);
            PreviewSession.Current.Stop();
            AssertFree();
        }

        // an edit of a property the fallback recorded is intercepted as a row's is — the session ends and the edit stays
        [Test]
        public void ES17_FallbackEditIsIntercepted()
        {
            var go = new GameObject("T");
            var target = go.AddComponent<PreviewTestTarget>();
            target.Offset = new Vector3(1f, 2f, 3f);
            var player = go.AddComponent<FeedbackPlayer>();
            Item<PreviewTestItem>(player, new TweenMotion(), 5f);
            PreviewSession.Start(player, default);
            Pump(20);
            UserEdit.Inspector(target, "m_Offset.x", p => p.floatValue = 9f);
            Pump(2);
            AssertEnded(PreviewEndReason.UserEdit);
            Assert.That(target.Offset.x, Is.EqualTo(9f), "the edit stays");
            AssertFree();
        }

        // an asset target: a key whose target is no scene object — the warning alone, no exception
        [Test]
        public void ES17_AssetTargetWarnsOnly()
        {
            PreviewDriver driver = PreviewDriver.Start();
            var material = new Material(AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat"));
            try
            {
                new PreviewRecorder().Record(new LayerKey(material, typeof(PreviewTestSlot), 0, 0));
                Assert.That(Warnings(), Is.EqualTo(new[] { FallbackWarning(typeof(PreviewTestSlot)) }));
            }
            finally
            {
                driver.Stop();
                driver.Destroy();
                Object.DestroyImmediate(material);
            }
            AssertFree();
        }

        // the preflight's time: a slot of a later prepare is not named; the fallback warns for it once
        [Test]
        public void ES17_PreflightAtTheStartOnly()
        {
            FeedbackPlayer player = LightPlayer("P");
            player.gameObject.AddComponent<PreviewTestTarget>();
            PreviewSession.Start(player, default);
            PreviewSession session = PreviewSession.Current;
            Pump(5);
            Item<PreviewTestItem>(player, new TweenMotion(), 5f);
            player.ReleaseRuntime();
            player.Play();
            Pump(10);
            Assert.That(session.Unrecorded, Is.Empty);
            Assert.That(Warnings(), Is.EqualTo(new[] { FallbackWarning(typeof(PreviewTestSlot)) }));
            session.Stop();
            AssertFree();
        }

        // a temporary — none outside a session; in one named with the recovery's prefix, hidden and in no scene, not in a
        // save; destroyed with its child at the end and at a lost lock's end
        [Test]
        public void ES20_Temporaries()
        {
            Assert.That(PreviewTemporaries.Create("Outside"), Is.Null, "outside a session");
            FeedbackPlayer player = LightPlayer("P");
            Save(m_Scene);
            string before = File.ReadAllText(ScenePath);
            PreviewSession.Start(player, default);
            Pump(5);
            GameObject temporary = PreviewTemporaries.Create("Seam", typeof(Light));
            var child = new GameObject("SeamChild");
            child.transform.SetParent(temporary.transform, false);
            Assert.That(temporary.name, Is.EqualTo(PreviewRecovery.TemporaryPrefix + "Seam"));
            Assert.That(temporary.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
            Assert.That(temporary.scene.IsValid(), Is.False, "in no scene");
            Assert.That(temporary.GetComponent<Light>(), Is.Not.Null);
            Save(m_Scene);
            Assert.That(File.ReadAllText(ScenePath), Is.EqualTo(before), "not in a save");
            PreviewSession.Current.Stop();
            Assert.That(temporary == null, Is.True, "destroyed at the end");
            Assert.That(child == null, Is.True, "with its child");

            PreviewSession.Start(player, default);
            GameObject lost = PreviewTemporaries.Create("Lost");
            Object.DestroyImmediate(PreviewSession.Current.Driver.Instance);
            Pump(1);
            AssertEnded(PreviewEndReason.LockLost);
            Assert.That(lost == null, Is.True, "at a lost lock's end too");
            AssertFree();
        }

        // the answer when asked — one made there would be gone by the end's step 4 all the same, so the test reads the answer
        // and not what is left afterwards
        sealed class MakingListener : IPlaybackListener
        {
            internal readonly List<string> Answers = new List<string>();

            public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end) => Answers.Add(PreviewTemporaries.Create("InsideTheEnd") == null ? "null" : "made");
        }

        // a temporary asked for inside the end — a playback's listener at the shutdown — is not made
        [Test]
        public void ES20_NoTemporaryInsideTheEnd()
        {
            var listener = new MakingListener();
            PreviewSession.Start(LightPlayer("P"), PlayArgs.Default.WithListener(listener));
            Pump(10);
            PreviewSession.Current.Stop();
            Assert.That(listener.Answers, Is.Not.Empty, "the listener heard the shutdown");
            Assert.That(listener.Answers, Has.All.EqualTo("null"));
            AssertFree();
        }

        sealed class FakeGlobal : IPreviewGlobalState
        {
            readonly List<string> m_Log;
            float m_Captured;
            internal float Value;
            internal bool ThrowsOnCapture;
            internal bool ThrowsOnRestore;
            internal bool Forgets;
            internal Func<string> Seen;

            internal FakeGlobal(string name, float value, List<string> log)
            {
                Name = name;
                Value = value;
                m_Log = log;
            }

            public string Name { get; }

            public void Capture()
            {
                m_Log.Add("capture " + Name + Seen?.Invoke());
                if (ThrowsOnCapture)
                    throw new InvalidOperationException("capture " + Name);
                m_Captured = Value;
            }

            public void Restore()
            {
                m_Log.Add("restore " + Name + Seen?.Invoke());
                if (ThrowsOnRestore)
                    throw new InvalidOperationException("restore " + Name);
                if (!Forgets)
                    Value = m_Captured;
            }

            public string Describe() => Value.ToString("R", CultureInfo.InvariantCulture);
        }

        FakeGlobal Register(string name, float value, List<string> log)
        {
            var state = new FakeGlobal(name, value, log);
            PreviewGlobalState.Register(state);
            m_Registered.Add(state);
            return state;
        }

        // two global states — captured after the driver and before the bind (no runtime · the light as it was), restored
        // after the stop (no lock · the recorded light back) and before the temporaries and the driver go, the other way round
        [Test]
        public void ES20_GlobalStates()
        {
            FeedbackPlayer player = LightPlayer("P");
            Light light = player.GetComponent<Light>();
            var log = new List<string>();
            FakeGlobal a = Register("A", 1f, log);
            FakeGlobal b = Register("B", 2f, log);
            Func<string> seen = () => " runtime=" + (player.Runtime != null) + " light=" + light.intensity.ToString("R", CultureInfo.InvariantCulture)
                + " mode=" + AnimationMode.InAnimationMode() + " drivers=" + GlobalStateCheck.Drivers() + " temporaries=" + PreviewTemporaries.Count;
            a.Seen = seen;
            b.Seen = seen;
            PreviewSession.Start(player, default);
            Assert.That(Pump(20, () => light.intensity != 1.25f), Is.True, "the preview wrote");
            PreviewTemporaries.Create("Seam");
            a.Value = 10f;
            b.Value = 20f;
            PreviewSession.Current.Stop();
            Assert.That(log, Is.EqualTo(new[]
            {
                "capture A runtime=False light=1.25 mode=True drivers=1 temporaries=0",
                "capture B runtime=False light=1.25 mode=True drivers=1 temporaries=0",
                "restore B runtime=False light=1.25 mode=False drivers=1 temporaries=1",
                "restore A runtime=False light=1.25 mode=False drivers=1 temporaries=1",
            }));
            Assert.That(a.Value, Is.EqualTo(1f));
            Assert.That(b.Value, Is.EqualTo(2f));
            AssertFree();
        }

        // the global states at the other ends — a lost lock (no stop), a scene closing, an edit of a recorded property
        [Test]
        public void ES20_GlobalStatesAtEveryEnd([Values("LockLost", "SceneClosing", "UserEdit")] string end)
        {
            FeedbackPlayer player = LightPlayer("P");
            var log = new List<string>();
            FakeGlobal a = Register("A", 1f, log);
            FakeGlobal b = Register("B", 2f, log);
            PreviewSession.Start(player, default);
            Pump(10);
            a.Value = 11f;
            b.Value = 21f;
            if (end == "LockLost")
            {
                Object.DestroyImmediate(PreviewSession.Current.Driver.Instance);
                Pump(1);
            }
            else if (end == "SceneClosing")
                PreviewHooks.SceneClosing(m_Scene, false);
            else
            {
                UserEdit.Inspector(player.GetComponent<Light>(), "m_Intensity", p => p.floatValue = 3f);
                Pump(2);
            }
            Assert.That(PreviewSession.Current, Is.Null, "ended");
            Assert.That(a.Value, Is.EqualTo(1f));
            Assert.That(b.Value, Is.EqualTo(2f));
            Assert.That(log, Is.EqualTo(new[] { "capture A", "capture B", "restore B", "restore A" }));
            AssertFree();
        }

        // global states that throw — a capture's is logged and that state is not restored, a restore's is logged and the
        // others are restored; the end goes to its end
        [Test]
        public void ES20_GlobalStatesIsolated()
        {
            var log = new List<string>();
            FakeGlobal a = Register("A", 1f, log);
            FakeGlobal b = Register("B", 2f, log);
            FakeGlobal c = Register("C", 3f, log);
            a.ThrowsOnCapture = true;
            b.ThrowsOnRestore = true;
            LogAssert.Expect(LogType.Exception, new Regex("capture A"));
            PreviewSession.Start(LightPlayer("P"), default);
            Pump(5);
            a.Value = 12f;
            b.Value = 22f;
            c.Value = 33f;
            LogAssert.Expect(LogType.Exception, new Regex("restore B"));
            PreviewSession.Current.Stop();
            Assert.That(c.Value, Is.EqualTo(3f), "restored after a throwing one");
            Assert.That(a.Value, Is.EqualTo(12f), "a capture that threw is not restored");
            Assert.That(log, Is.EqualTo(new[] { "capture A", "capture B", "capture C", "restore C", "restore B" }));
            AssertFree();
        }

        // GlobalStateCheck sees a registered state left changed — the negative control — and passes once it is back
        [Test]
        public void ES20_GlobalStateCheckSeesAStateLeft()
        {
            var log = new List<string>();
            FakeGlobal a = Register("A", 1f, log);
            FakeGlobal b = Register("B", 2f, log);
            GlobalStateCheck check = GlobalStateCheck.Take();
            PreviewSession.Start(LightPlayer("P"), default);
            Pump(5);
            b.Forgets = true;
            a.Value = 5f;
            b.Value = 6f;
            PreviewSession.Current.Stop();
            Assert.Throws<AssertionException>(() => check.AssertSame(), "B left at 6");
            b.Value = 2f;
            check.AssertSame();
        }

        // a refused start captures nothing; another subject's start restores the first session's capture before it captures
        // again
        [Test]
        public void ES20_GlobalStatesRefusedAndReplaced()
        {
            var log = new List<string>();
            FakeGlobal a = Register("A", 1f, log);
            var other = ScriptableObject.CreateInstance<AnimationModeDriver>();
            other.name = OtherDriver;
            other.hideFlags = HideFlags.HideAndDontSave;
            AnimationMode.StartAnimationMode(other);
            Assert.That(PreviewSession.Start(LightPlayer("Refused"), default), Is.EqualTo(PreviewRefusal.Locked));
            AnimationMode.StopAnimationMode(other);
            Object.DestroyImmediate(other);
            Assert.That(log, Is.Empty, "a refused start captures nothing");
            PreviewSession.Start(LightPlayer("First"), default);
            Pump(5);
            a.Value = 5f;
            PreviewSession.Start(LightPlayer("Second"), default);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.Replaced));
            Assert.That(log, Is.EqualTo(new[] { "capture A", "restore A", "capture A" }));
            Assert.That(a.Value, Is.EqualTo(1f));
            PreviewSession.Current.Stop();
            AssertFree();
        }

        // a string the preview cannot record, by its setter
        sealed class LabelState : IPreviewSaveState
        {
            readonly PreviewTestTarget m_Target;
            readonly string m_Home;
            string m_Kept;
            internal int Reverts;

            internal LabelState(PreviewTestTarget target, string home)
            {
                m_Target = target;
                m_Home = home;
            }

            public Scene Scene => m_Target.gameObject.scene;

            public void Revert()
            {
                Reverts++;
                m_Kept = m_Target.Label;
                m_Target.Label = m_Home;
            }

            public void Reapply() => m_Target.Label = m_Kept;
        }

        // save states in two scenes — a save of one writes its state as it was and leaves the other's untouched, the preview's
        // value is back after it and the scene clean three updates later; two dirty scenes in one save each their own; the end
        // reverts them all; a state's scene closing ends the session
        [UnityTest]
        public IEnumerator ES20_SaveStates()
        {
            var go = new GameObject("P");
            var target = go.AddComponent<PreviewTestTarget>();
            target.Label = "home";
            FeedbackPlayer player = go.AddComponent<FeedbackPlayer>();
            Item<PreviewTestItem>(player, new TweenMotion(), 5f);
            m_Other = OpenEmpty(OtherScenePath);
            var otherTarget = new GameObject("Other").AddComponent<PreviewTestTarget>();
            SceneManager.MoveGameObjectToScene(otherTarget.gameObject, m_Other);
            otherTarget.Label = "homeB";
            Save(m_Scene);
            Save(m_Other);
            string before = File.ReadAllText(ScenePath);
            string otherBefore = File.ReadAllText(OtherScenePath);
            PreviewSession.Start(player, default);
            Pump(10);
            target.Label = "preview";
            Assert.That(PreviewSaveState.Add(new LabelState(target, "home")), Is.True);
            otherTarget.Label = "previewB";
            var otherState = new LabelState(otherTarget, "homeB");
            PreviewSaveState.Add(otherState);

            Save(m_Scene);
            Assert.That(File.ReadAllText(ScenePath), Is.EqualTo(before), "the save writes it as it was");
            Assert.That(target.Label, Is.EqualTo("preview"), "the preview's value after the save");
            Assert.That(otherState.Reverts, Is.Zero, "the other scene's state untouched");
            yield return null;
            yield return null;
            yield return null;
            Assert.That(m_Scene.isDirty, Is.False, "clean after the save");

            EditorSceneManager.MarkSceneDirty(m_Scene);
            EditorSceneManager.MarkSceneDirty(m_Other);
            Assert.That(EditorSceneManager.SaveScenes(new[] { m_Scene, m_Other }), Is.True);
            Assert.That(File.ReadAllText(ScenePath), Is.EqualTo(before), "two scenes in one save");
            Assert.That(File.ReadAllText(OtherScenePath), Is.EqualTo(otherBefore));
            Assert.That(target.Label, Is.EqualTo("preview"));
            Assert.That(otherTarget.Label, Is.EqualTo("previewB"));

            PreviewSession.Current.Stop();
            Assert.That(target.Label, Is.EqualTo("home"), "the end reverts");
            Assert.That(otherTarget.Label, Is.EqualTo("homeB"));
            Assert.That(PreviewSaveState.Count, Is.Zero);

            PreviewSession.Start(player, default);
            Pump(5);
            otherTarget.Label = "previewB";
            PreviewSaveState.Add(new LabelState(otherTarget, "homeB"));
            EditorSceneManager.CloseScene(m_Other, true);
            AssertEnded(PreviewEndReason.SceneClosing);
            AssertFree();
        }

        // a save state is added only while a session lives, and once
        [Test]
        public void ES20_SaveStateAddedOnceInASession()
        {
            FeedbackPlayer player = LightPlayer("P");
            var state = new LabelState(player.gameObject.AddComponent<PreviewTestTarget>(), "home");
            Assert.That(PreviewSaveState.Add(state), Is.False, "outside a session");
            PreviewSession.Start(player, default);
            Assert.That(PreviewSaveState.Add(state), Is.True);
            Assert.That(PreviewSaveState.Add(state), Is.False, "once");
            Assert.That(PreviewSaveState.Count, Is.EqualTo(1));
            PreviewSession.Current.Stop();
            Assert.That(PreviewSaveState.Count, Is.Zero);
            AssertFree();
        }

        // a prefab stage: another property's auto save with a save state — the file as it was, the preview's value after it,
        // no second auto save, the end reverts
        [UnityTest]
        public IEnumerator ES20_SaveStatePrefabStage()
        {
            var root = new GameObject("PrefabRoot");
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<PreviewTestTarget>().Label = "home";
            ItemOn<PositionItem>(root.AddComponent<FeedbackPlayer>(), body, new ShakeMotion(), 30f);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            PrefabStage stage = PrefabStageUtility.OpenPrefab(PrefabPath);
            bool autoSave = SceneFileCheck.AutoSave(stage, true);
            int saves = 0;
            Action<GameObject> counting = saved => saves++;
            PrefabStage.prefabSaving += counting;
            try
            {
                yield return SceneFileCheck.WaitForAutoSave();
                FeedbackPlayer staged = stage.prefabContentsRoot.GetComponent<FeedbackPlayer>();
                Transform stagedBody = staged.transform.Find("Body");
                PreviewTestTarget target = stagedBody.GetComponent<PreviewTestTarget>();
                PreviewSession.Start(staged, default);
                Pump(10);
                target.Label = "preview";
                PreviewSaveState.Add(new LabelState(target, "home"));
                saves = 0;
                UserEdit.Inspector(stagedBody, "m_LocalScale.x", p => p.floatValue = 2f);
                yield return SceneFileCheck.WaitForAutoSave(1.0);
                string file = File.ReadAllText(PrefabPath);
                Assert.That(saves, Is.GreaterThanOrEqualTo(1), "the other property's edit auto-saved");
                Assert.That(file, Does.Contain("m_LocalScale: {x: 2,"));
                Assert.That(file, Does.Contain("m_Label: home"));
                Assert.That(file, Does.Not.Contain("m_Label: preview"));
                Assert.That(target.Label, Is.EqualTo("preview"), "the preview's value after it");
                saves = 0;
                yield return SceneFileCheck.WaitForAutoSave(1.5);
                Assert.That(saves, Is.Zero, "no second auto save");
                Assert.That(stage.scene.isDirty, Is.False);
                PreviewSession.Current.Stop();
                Assert.That(target.Label, Is.EqualTo("home"), "the end reverts");
                AssertFree();
            }
            finally
            {
                PrefabStage.prefabSaving -= counting;
                SceneFileCheck.AutoSave(stage, autoSave);
                if (StageUtility.GetCurrentStage() is PrefabStage)
                    StageUtility.GoToMainStage();
            }
        }
    }
}
