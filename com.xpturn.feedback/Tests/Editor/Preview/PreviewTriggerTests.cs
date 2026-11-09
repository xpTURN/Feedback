using System;
using System.Collections;
using System.Collections.Generic;
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
    // each trigger ends the session where it comes, an edit of a recorded property goes back on after the stop, an undo or
    // a redo is done again at the next update; the recovery's reach. The session runs on the test's clock (Pump); what the
    // hooks do at an editor update — the step again, a selection event, the dirty flag — needs the update itself (yield).
    // Warnings and errors are counted
    public sealed class PreviewTriggerTests
    {
        const string ScenePath = "Assets/PreviewTriggerTests.unity";
        const string OtherScenePath = "Assets/PreviewTriggerTests.Other.unity";
        const string PrefabPath = "Assets/PreviewTriggerTests.prefab";
        const string Decoy = "PreviewTriggerTests.";
        const double UpdateStep = 1.0 / 120.0;
        static readonly Vector3 s_Home = new Vector3(1f, 2f, 3f);

        static double s_Now;

        IPreviewRecordSource m_Source;
        Func<double> m_Clock;
        Scene m_Scene;
        Scene m_Other;
        Scene m_Active;
        bool m_HadDeselect;
        bool m_Deselect;
        GlobalStateCheck m_Global;
        readonly List<string> m_Logs = new List<string>();

        [SetUp]
        public void SetUp()
        {
            m_Source = PreviewRecordPaths.Source;
            m_Clock = PreviewClock.Source;
            PreviewRecordPaths.Source = new PreviewTestSource();
            s_Now = 0;
            PreviewClock.Source = () => s_Now;
            m_HadDeselect = EditorPrefs.HasKey(PreviewSession.EndOnDeselectKey);
            m_Deselect = PreviewSession.EndOnDeselect;
            m_Global = GlobalStateCheck.Take();
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
            // a step a failed test left to do again — against its own history, before its objects go
            PreviewHooks.ReplayPending();
            foreach (AnimationModeDriver driver in Resources.FindObjectsOfTypeAll<AnimationModeDriver>())
            {
                if (driver == null || (driver.name != PreviewDriver.Name && !driver.name.StartsWith(Decoy, StringComparison.Ordinal)))
                    continue;
                if (AnimationMode.InAnimationMode(driver))
                    AnimationMode.StopAnimationMode(driver);
                Object.DestroyImmediate(driver);
            }
            if (m_HadDeselect)
                PreviewSession.EndOnDeselect = m_Deselect;
            else
                EditorPrefs.DeleteKey(PreviewSession.EndOnDeselectKey);
            if (Selection.activeObject != null)
                Selection.activeObject = null;
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
            if (type != LogType.Log)
                m_Logs.Add(type + ": " + message);
        }

        void AssertNoLogs() => Assert.That(m_Logs, Is.Empty, string.Join(" | ", m_Logs));

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

        // whether the value was off its start at any update — a shake is 0 at its whole periods
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

        // shakes its own position and tweens its light
        static FeedbackPlayer Player(string name, float duration)
        {
            var go = new GameObject(name);
            go.transform.localPosition = s_Home;
            go.AddComponent<Light>().intensity = 1.25f;
            var player = go.AddComponent<FeedbackPlayer>();
            Item<PositionItem>(player, new ShakeMotion(), duration);
            Item<LightIntensityItem>(player, new TweenMotion(), duration);
            return player;
        }

        // a light the session does not record
        static Light Marker(string name, float intensity)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.intensity = intensity;
            return light;
        }

        static bool Moved(FeedbackPlayer player) => player.transform.localPosition != s_Home;

        static void AssertEnded(PreviewEndReason reason)
        {
            Assert.That(PreviewSession.Current, Is.Null, "ended");
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(reason));
        }

        static void AssertBack(FeedbackPlayer player)
        {
            Assert.That(player.transform.localPosition, Is.EqualTo(s_Home), "the position back");
            Assert.That(player.GetComponent<Light>().intensity, Is.EqualTo(1.25f), "the light back");
        }

        static void AssertFree()
        {
            Assert.That(PreviewSession.Current, Is.Null, "no session");
            Assert.That(GlobalStateCheck.Left(), Is.Empty, "nothing left");
        }

        // a save during the preview writes the recorded properties as they were and ends nothing; after the end too
        [Test]
        public void ES02_SaveDuringPreview()
        {
            FeedbackPlayer player = Player("P", 5f);
            SceneFileCheck file = SceneFileCheck.ForScene(m_Scene);
            PreviewSession.Start(player, default);
            Assert.That(Pump(20, () => Moved(player)), Is.True, "the preview wrote");
            Assert.That(file.SaveAndCompare(m_Scene), Is.Empty, "saved during the preview");
            Assert.That(PreviewSession.Current, Is.Not.Null, "a save ends nothing");
            PreviewSession.Current.Stop();
            Assert.That(file.SaveAndCompare(m_Scene), Is.Empty, "saved after the end");
            AssertNoLogs();
        }

        // a user's edit, the preview, the end — one undo takes the edit back; the session moved no undo group
        [Test]
        public void ES03_UndoAfterPreview()
        {
            FeedbackPlayer player = Player("P", 5f);
            Light light = player.GetComponent<Light>();
            UndoCheck undo = UndoCheck.Edit(light, "m_Intensity", p => p.floatValue = 2f);
            PreviewSession.Start(player, default);
            Assert.That(Pump(20, () => light.intensity != 2f), Is.True, "the preview wrote");
            Assert.That(undo.GroupKept, Is.True, "the start and the ticks moved no group");
            PreviewSession.Current.Stop();
            Assert.That(undo.GroupKept, Is.True, "the end moved no group");
            Assert.That(light.intensity, Is.EqualTo(2f));
            Undo.PerformUndo();
            Assert.That(light.intensity, Is.EqualTo(1.25f), "one undo took the user's edit back");
            AssertNoLogs();
        }

        // the whole scene set replaced — the end comes inside the call
        [Test]
        public void ES06a_NewSceneSingle()
        {
            FeedbackPlayer player = Player("P", 5f);
            Save(m_Scene);
            PreviewSession.Start(player, default);
            Pump(20);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssertEnded(PreviewEndReason.SceneClosing);
            AssertFree();
            m_Global.AssertSame();
            AssertNoLogs();
        }

        // another scene with a recorded object closed, the subject's scene stays — the recorder kept that scene; the
        // subject's values are back right after the call. The reference across the scenes is set after the save: saved, the
        // engine warns and leaves it out
        [Test]
        public void ES06b_RecordedObjectsSceneClosed()
        {
            m_Other = OpenEmpty(OtherScenePath);
            SceneManager.SetActiveScene(m_Other);
            var target = new GameObject("TargetB");
            target.transform.localPosition = new Vector3(3f, 3f, 3f);
            SceneManager.SetActiveScene(m_Scene);
            FeedbackPlayer player = Player("P", 5f);
            Save(m_Other);
            Save(m_Scene);
            PositionItem item = Item<PositionItem>(player, new ShakeMotion(), 5f);
            item.m_Target.m_Mode = TargetRefMode.Direct;
            item.m_Target.m_Object = target;
            PreviewSession.Start(player, default);
            Assert.That(Pump(20, () => target.transform.localPosition != new Vector3(3f, 3f, 3f)), Is.True, "the preview wrote the other scene");
            EditorSceneManager.CloseScene(m_Other, true);
            AssertEnded(PreviewEndReason.SceneClosing);
            AssertBack(player);
            AssertFree();
            m_Global.AssertSame();
            AssertNoLogs();
        }

        // the subject's scene unloaded (removeScene false) — sceneClosing comes and the end is inside the call
        [Test]
        public void ES06c_SubjectsSceneUnloaded()
        {
            FeedbackPlayer player = Player("P", 5f);
            Save(m_Scene);
            PreviewSession.Start(player, default);
            Pump(20);
            Assert.That(EditorSceneManager.CloseScene(m_Scene, false), Is.True);
            AssertEnded(PreviewEndReason.SceneClosing);
            AssertFree();
            m_Global.AssertSame();
            AssertNoLogs();
        }

        // a scene with nothing of the session's closed — the session goes on
        [Test]
        public void ES06_UnrelatedSceneClosed()
        {
            FeedbackPlayer player = Player("P", 5f);
            m_Other = OpenEmpty(OtherScenePath);
            SceneManager.SetActiveScene(m_Other);
            new GameObject("Unrelated");
            SceneManager.SetActiveScene(m_Scene);
            PreviewSession.Start(player, default);
            Pump(10);
            EditorSceneManager.CloseScene(m_Other, true);
            Assert.That(PreviewSession.Current, Is.Not.Null, "an unrelated scene ends nothing");
            AssertNoLogs();
        }

        // the recovery's reach — our hidden GameObject with the prefix and our driver go; another name, a DontSave or visible
        // object with the prefix, the runtime's prefix, a ScriptableObject with the prefix and another tool's driver in the
        // mode stay. The reload cases are PreviewReloadTests
        [Test]
        public void ES07_RecoveryReach()
        {
            GameObject ours = EditorUtility.CreateGameObjectWithHideFlags(PreviewRecovery.TemporaryPrefix + Decoy + "Ours", HideFlags.HideAndDontSave);
            GameObject otherName = EditorUtility.CreateGameObjectWithHideFlags(Decoy + "UserTool", HideFlags.HideAndDontSave);
            GameObject dontSaveOnly = EditorUtility.CreateGameObjectWithHideFlags(PreviewRecovery.TemporaryPrefix + Decoy + "DontSave", HideFlags.DontSave);
            var visible = new GameObject(PreviewRecovery.TemporaryPrefix + Decoy + "Visible");
            GameObject runtime = EditorUtility.CreateGameObjectWithHideFlags("xpTURN.Feedback.Runtime." + Decoy + "Runtime", HideFlags.HideAndDontSave);
            var asset = ScriptableObject.CreateInstance<ScriptableObject>();
            asset.name = PreviewRecovery.TemporaryPrefix + Decoy + "Asset";
            asset.hideFlags = HideFlags.HideAndDontSave;
            var ourDriver = ScriptableObject.CreateInstance<AnimationModeDriver>();
            ourDriver.name = PreviewDriver.Name;
            ourDriver.hideFlags = HideFlags.HideAndDontSave;
            var otherDriver = ScriptableObject.CreateInstance<AnimationModeDriver>();
            otherDriver.name = Decoy + "OtherDriver";
            otherDriver.hideFlags = HideFlags.HideAndDontSave;
            AnimationMode.StartAnimationMode(otherDriver);
            try
            {
                PreviewRecovery.Run();
                Assert.That(ours == null, Is.True, "our hidden temporary went");
                Assert.That(ourDriver == null, Is.True, "our driver went");
                Assert.That(otherName != null && dontSaveOnly != null && visible != null && runtime != null && asset != null, Is.True, "the others stayed");
                Assert.That(otherDriver != null && AnimationMode.InAnimationMode(otherDriver), Is.True, "another tool's driver stayed in the mode");
            }
            finally
            {
                if (otherDriver != null && AnimationMode.InAnimationMode(otherDriver))
                    AnimationMode.StopAnimationMode(otherDriver);
                foreach (Object o in new Object[] { ours, otherName, dontSaveOnly, visible, runtime, asset, ourDriver, otherDriver })
                {
                    if (o != null)
                        Object.DestroyImmediate(o);
                }
            }
            AssertNoLogs();
        }

        // a recorded property edited in the inspector — the end at the next update (not inside the callback), the edit stays
        // and the rest goes back; the scene dirty two updates later and saved with the edit; one undo gives the value before
        // the preview, a second does not bring the preview's back
        [UnityTest]
        public IEnumerator ES09_InspectorEdit()
        {
            FeedbackPlayer player = Player("P", 5f);
            Save(m_Scene);
            PreviewSession.Start(player, default);
            Assert.That(Pump(20, () => Moved(player)), Is.True, "the preview wrote");
            UserEdit.Inspector(player.transform, "m_LocalPosition.x", p => p.floatValue = 4f);
            Assert.That(PreviewSession.Current, Is.Not.Null, "the end waits for the next update");
            Pump(2);
            AssertEnded(PreviewEndReason.UserEdit);
            Assert.That(player.transform.localPosition, Is.EqualTo(new Vector3(4f, 2f, 3f)), "the edit stays, the rest is back");
            Assert.That(player.GetComponent<Light>().intensity, Is.EqualTo(1.25f));
            yield return null;
            yield return null;
            Assert.That(DirtyCheck.Dirty(m_Scene), Is.EqualTo(new List<string> { ScenePath }), "dirty two updates later");
            Save(m_Scene);
            Assert.That(File.ReadAllText(ScenePath), Does.Contain("m_LocalPosition: {x: 4, y: 2, z: 3}"), "saved with the edit");
            Undo.PerformUndo();
            Assert.That(player.transform.localPosition.x, Is.EqualTo(1f), "one undo: the value before the preview");
            Undo.PerformUndo();
            Assert.That(player.transform.localPosition.x, Is.EqualTo(1f), "two: the preview's value does not come back");
            AssertNoLogs();
        }

        // the Scene view's gizmo — a record, the setter, the flush (the event comes at the flush)
        [Test]
        public void ES09_GizmoEdit()
        {
            FeedbackPlayer player = Player("P", 5f);
            PreviewSession.Start(player, default);
            Pump(20);
            UserEdit.Gizmo(player.transform, () => player.transform.localPosition = new Vector3(5f, 2f, 3f));
            Pump(2);
            AssertEnded(PreviewEndReason.UserEdit);
            Assert.That(player.transform.localPosition, Is.EqualTo(new Vector3(5f, 2f, 3f)));
            AssertNoLogs();
        }

        // a GameObject's active state the session turned off, turned on in the inspector — bound directly, so it is recorded
        // and intercepted
        [Test]
        public void ES09_ActiveEdit()
        {
            var host = new GameObject("Host").AddComponent<FeedbackPlayer>();
            var toggled = new GameObject("Toggled");
            var set = new SetMotion();
            set.m_Params.m_Value = Vector4.zero;
            SetActiveItem setItem = Item<SetActiveItem>(host, set, 0.1f);
            setItem.m_Target.m_Mode = TargetRefMode.Direct;
            setItem.m_Target.m_Object = toggled;
            Item<PositionItem>(host, new ShakeMotion(), 5f);
            PreviewSession.Start(host, default);
            Pump(10);
            Assert.That(toggled.activeSelf, Is.False, "the Set turned it off");
            UserEdit.Inspector(toggled, "m_IsActive", p => p.boolValue = true);
            Pump(2);
            AssertEnded(PreviewEndReason.UserEdit);
            Assert.That(toggled.activeSelf, Is.True, "the edit stays");
            AssertNoLogs();
        }

        // the material an instance slot recorded, swapped by the user — intercepted and put back after the slot let go: the
        // user's material, no copy left
        [Test]
        public void ES09_ReferenceEdit()
        {
            var go = new GameObject("R");
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            var user = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = Decoy + "UserMaterial" };
            var player = go.AddComponent<FeedbackPlayer>();
            Item<MaterialColorItem>(player, new FlashMotion(), 5f).m_WriteMode = MaterialWriteMode.Instance;
            int copies = Copies();
            try
            {
                PreviewSession.Start(player, default);
                Pump(6);
                Assert.That(Copies(), Is.EqualTo(copies + 1), "the instance slot's copy");
                UserEdit.Inspector(renderer, "m_Materials.Array.data[0]", p => p.objectReferenceValue = user);
                Pump(2);
                AssertEnded(PreviewEndReason.UserEdit);
                Assert.That(renderer.sharedMaterial, Is.SameAs(user), "the user's material");
                Assert.That(Copies(), Is.EqualTo(copies), "no copy left");
            }
            finally
            {
                PreviewSession.Current?.Stop();
                Object.DestroyImmediate(user);
            }
            AssertNoLogs();
        }

        static int Copies()
        {
            int count = 0;
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m != null && m.name.EndsWith("(Feedback)", StringComparison.Ordinal))
                    count++;
            }
            return count;
        }

        // an edit of a property the session did not record ends nothing
        [Test]
        public void ES09_UnrecordedEdit()
        {
            FeedbackPlayer player = Player("P", 5f);
            Light light = player.GetComponent<Light>();
            PreviewSession.Start(player, default);
            Pump(20);
            UserEdit.Inspector(light, "m_Color.r", p => p.floatValue = 0.5f);
            Pump(2);
            Assert.That(PreviewSession.Current, Is.Not.Null, "goes on");
            Assert.That(light.color.r, Is.EqualTo(0.5f));
            AssertNoLogs();
        }

        // a recorded edit and the stop in one update — the edit stays
        [Test]
        public void ES09_EditAndStopTogether()
        {
            FeedbackPlayer player = Player("P", 5f);
            PreviewSession.Start(player, default);
            Pump(20);
            UserEdit.Inspector(player.transform, "m_LocalPosition.x", p => p.floatValue = 4f);
            PreviewSession.Current.Stop();
            AssertEnded(PreviewEndReason.Stopped);
            Assert.That(player.transform.localPosition.x, Is.EqualTo(4f));
            AssertNoLogs();
        }

        // an undo of an edit the session did not record — the end inside the undo, the edit undone, the subject's values
        // back; the step again at the next update leaves it undone
        [UnityTest]
        public IEnumerator ES10a_UnrecordedUndo()
        {
            FeedbackPlayer player = Player("P", 5f);
            Light marker = Marker("Marker", 1f);
            UserEdit.Inspector(marker, "m_Intensity", p => p.floatValue = 2f);
            PreviewSession.Start(player, default);
            Pump(20);
            Undo.PerformUndo();
            AssertEnded(PreviewEndReason.UndoRedo);
            Assert.That(marker.intensity, Is.EqualTo(1f));
            AssertBack(player);
            yield return null;
            yield return null;
            Assert.That(marker.intensity, Is.EqualTo(1f), "still undone after the step again");
            AssertNoLogs();
        }

        // an edit made before the preview, of a property it records, undone during it — the stop wrote the recorded value
        // over the undo's; the step again an update later gives the undo's value, and the history matches the scene
        [UnityTest]
        public IEnumerator ES10b_RecordedUndo()
        {
            FeedbackPlayer player = Player("P", 5f);
            UserEdit.Inspector(player.transform, "m_LocalPosition.x", p => p.floatValue = 4f);
            PreviewSession.Start(player, default);
            Pump(20);
            Undo.PerformUndo();
            AssertEnded(PreviewEndReason.UndoRedo);
            yield return null;
            yield return null;
            Assert.That(player.transform.localPosition.x, Is.EqualTo(1f), "the undo's value");
            Undo.PerformRedo();
            Assert.That(player.transform.localPosition.x, Is.EqualTo(4f), "redo");
            Undo.PerformUndo();
            Assert.That(player.transform.localPosition.x, Is.EqualTo(1f), "undo again");
            AssertNoLogs();
        }

        // a redo during the preview — an update later the redo's value
        [UnityTest]
        public IEnumerator ES10c_Redo()
        {
            FeedbackPlayer player = Player("P", 5f);
            UserEdit.Inspector(player.transform, "m_LocalPosition.x", p => p.floatValue = 4f);
            Undo.PerformUndo();
            PreviewSession.Start(player, default);
            Pump(20);
            Undo.PerformRedo();
            AssertEnded(PreviewEndReason.UndoRedo);
            yield return null;
            yield return null;
            Assert.That(player.transform.localPosition.x, Is.EqualTo(4f), "the redo's value");
            AssertNoLogs();
        }

        // the current group reverted during the preview — an undo event that leaves nothing to redo: the step again finds no
        // redo and undoes nothing, the user's step under it stays
        [UnityTest]
        public IEnumerator ES10d_RevertCurrentGroup()
        {
            FeedbackPlayer player = Player("P", 5f);
            Light other = Marker("Other", 1f);
            UserEdit.Inspector(other, "m_Intensity", p => p.floatValue = 5f);
            Light marker = Marker("Marker", 2f);
            PreviewSession.Start(player, default);
            Pump(20);
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(marker, "Change");
            marker.intensity = 9f;
            Undo.RevertAllInCurrentGroup();
            yield return null;
            yield return null;
            Assert.That(marker.intensity, Is.EqualTo(2f), "reverted");
            Assert.That(other.intensity, Is.EqualTo(5f), "the user's step under it stays");
            AssertNoLogs();
        }

        // RevertAllDownToGroup — its group is one below the current: a group number cannot tell it, the redo can
        [UnityTest]
        public IEnumerator ES10d_RevertDownToGroup()
        {
            FeedbackPlayer player = Player("P", 5f);
            Light other = Marker("Other", 1f);
            UserEdit.Inspector(other, "m_Intensity", p => p.floatValue = 5f);
            Light marker = Marker("Marker", 2f);
            PreviewSession.Start(player, default);
            Pump(20);
            Undo.IncrementCurrentGroup();
            int down = Undo.GetCurrentGroup();
            Undo.RecordObject(marker, "Step 1");
            marker.intensity = 7f;
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(marker, "Step 2");
            marker.intensity = 8f;
            Undo.RevertAllDownToGroup(down);
            yield return null;
            yield return null;
            Assert.That(marker.intensity, Is.EqualTo(2f), "reverted");
            Assert.That(other.intensity, Is.EqualTo(5f), "the user's step under it stays");
            AssertNoLogs();
        }

        // two undos in one update — the top one on a recorded property ends the session, the second comes with no session;
        // both are done again, counted the same way
        [UnityTest]
        public IEnumerator ES10_TwoUndosInOneUpdate()
        {
            FeedbackPlayer player = Player("P", 5f);
            Light marker = Marker("Marker", 1f);
            UserEdit.Inspector(marker, "m_Intensity", p => p.floatValue = 2f);
            UserEdit.Inspector(player.transform, "m_LocalPosition.x", p => p.floatValue = 4f);
            PreviewSession.Start(player, default);
            Pump(20);
            Undo.PerformUndo();
            Undo.PerformUndo();
            yield return null;
            yield return null;
            Assert.That(player.transform.localPosition.x, Is.EqualTo(1f), "the first undo stands");
            Assert.That(marker.intensity, Is.EqualTo(1f), "the second too");
            Undo.PerformRedo();
            Undo.PerformRedo();
            Assert.That(player.transform.localPosition.x, Is.EqualTo(4f));
            Assert.That(marker.intensity, Is.EqualTo(2f));
            AssertNoLogs();
        }

        // the same subject started again in the undo's update, its first tick before the editor's update — the start does the
        // pending step first, so the new session records the undo's value: it lives, and after its end the undo stands
        [UnityTest]
        public IEnumerator ES10_NewSessionInTheUndosUpdate()
        {
            FeedbackPlayer player = Player("P", 5f);
            UserEdit.Inspector(player.transform, "m_LocalPosition.x", p => p.floatValue = 4f);
            PreviewSession.Start(player, default);
            Pump(20);
            Undo.PerformUndo();
            AssertEnded(PreviewEndReason.UndoRedo);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(20);
            yield return null;
            Pump(5);
            Assert.That(PreviewSession.Current, Is.Not.Null, "the new session lives");
            PreviewSession.Current.Stop();
            yield return null;
            yield return null;
            Assert.That(player.transform.localPosition.x, Is.EqualTo(1f), "the undo stands");
            AssertNoLogs();
        }

        // a prefab stage, isolated and in context — another property's edit auto-saves the file with the recorded ones as they
        // were and the preview goes on; a recorded property's edit is intercepted and put back, and the auto save writes it;
        // closing the stage ends the session inside the call. The baseline is taken after the stage opened. The recorded
        // property is a child's — in context the stage puts the root at the instance's place and an edit of the root's
        // position is not saved
        [UnityTest]
        public IEnumerator ES15_PrefabStage([Values(false, true)] bool context)
        {
            var root = new GameObject("PrefabRoot");
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = s_Home;
            FeedbackPlayer player = root.AddComponent<FeedbackPlayer>();
            PositionItem moved = Item<PositionItem>(player, new ShakeMotion(), 5f);
            moved.m_Target.m_Mode = TargetRefMode.Direct;
            moved.m_Target.m_Object = body;
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            PrefabStage stage;
            if (context)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, m_Scene);
                Save(m_Scene);
                stage = PrefabStageUtility.OpenPrefab(PrefabPath, instance, PrefabStage.Mode.InContext);
            }
            else
                stage = PrefabStageUtility.OpenPrefab(PrefabPath);
            bool autoSave = SceneFileCheck.AutoSave(stage, true);
            try
            {
                yield return SceneFileCheck.WaitForAutoSave();
                SceneFileCheck file = SceneFileCheck.ForPrefab(PrefabPath);
                FeedbackPlayer staged = stage.prefabContentsRoot.GetComponent<FeedbackPlayer>();
                Transform stagedBody = staged.transform.Find("Body");
                PreviewSession.Start(staged, default);
                Assert.That(Pump(20, () => stagedBody.localPosition != s_Home), Is.True, "the preview wrote");

                UserEdit.Inspector(stagedBody, "m_LocalScale.x", p => p.floatValue = 2f);
                yield return SceneFileCheck.WaitForAutoSave();
                Assert.That(file.Differences(), Is.Not.Empty, "the other property's edit auto-saved");
                Assert.That(file.Text, Does.Contain("m_LocalScale: {x: 2,"));
                Assert.That(file.Text, Does.Contain("m_LocalPosition: {x: 1, y: 2, z: 3}"), "the recorded position as it was");
                Assert.That(PreviewSession.Current, Is.Not.Null, "the preview goes on");

                UserEdit.Inspector(stagedBody, "m_LocalPosition.x", p => p.floatValue = 4f);
                Pump(2);
                AssertEnded(PreviewEndReason.UserEdit);
                yield return SceneFileCheck.WaitForAutoSave();
                Assert.That(file.Text, Does.Contain("m_LocalPosition: {x: 4, y: 2, z: 3}"), "the edit auto-saved");

                PreviewSession.Start(staged, default);
                Pump(20);
                SceneFileCheck.AutoSave(stage, autoSave);
                StageUtility.GoToMainStage();
                AssertEnded(PreviewEndReason.StageClosing);
                AssertFree();
            }
            finally
            {
                if (StageUtility.GetCurrentStage() is PrefabStage open)
                {
                    SceneFileCheck.AutoSave(open, autoSave);
                    StageUtility.GoToMainStage();
                }
            }
            AssertNoLogs();
        }

        // the stop button — the values back, nothing left
        [Test]
        public void ES16_Stop()
        {
            FeedbackPlayer player = Player("P", 5f);
            PreviewSession.Start(player, default);
            Assert.That(Pump(20, () => Moved(player)), Is.True, "the preview wrote");
            PreviewSession.Current.Stop();
            AssertEnded(PreviewEndReason.Stopped);
            AssertBack(player);
            AssertFree();
            AssertNoLogs();
        }

        // another subject's start ends the session first
        [Test]
        public void ES16_AnotherSubject()
        {
            FeedbackPlayer first = Player("P1", 5f);
            FeedbackPlayer second = Player("P2", 5f);
            PreviewSession.Start(first, default);
            Pump(10);
            Assert.That(PreviewSession.Start(second, default), Is.EqualTo(PreviewRefusal.None));
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.Replaced));
            Assert.That(PreviewSession.Current, Is.Not.Null);
            Assert.That(PreviewSession.Current.Player, Is.SameAs(second));
            AssertBack(first);
            AssertNoLogs();
        }

        // nothing plays — the end at the next update
        [Test]
        public void ES16_AutoEnd()
        {
            FeedbackPlayer player = Player("P", 0.1f);
            PreviewSession.Start(player, default);
            for (int i = 0; i < 200 && PreviewSession.Current != null; i++)
                Pump(1);
            AssertEnded(PreviewEndReason.AutoEnd);
            AssertBack(player);
            AssertNoLogs();
        }

        // the same subject started again after its play ended, before the auto end ran — the session goes on
        [Test]
        public void ES16_SameSubjectAfterThePlayEnded()
        {
            FeedbackPlayer player = Player("P", 0.1f);
            PreviewSession.Start(player, default);
            for (int i = 0; i < 200 && player.IsPlaying; i++)
                Pump(1);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(1);
            Assert.That(PreviewSession.Current, Is.Not.Null, "the session goes on");
            Assert.That(player.IsPlaying, Is.True);
            AssertNoLogs();
        }

        // the setting's two values — the selection event comes an update after the change: on, the subject left the
        // selection ends it; off, the preview goes on
        [UnityTest]
        public IEnumerator ES16_Deselect([Values(false, true)] bool endOnDeselect)
        {
            FeedbackPlayer player = Player("P", 5f);
            var other = new GameObject("Other");
            Selection.activeGameObject = player.gameObject;
            yield return null;
            PreviewSession.EndOnDeselect = endOnDeselect;
            PreviewSession.Start(player, default);
            Pump(10);
            Selection.activeGameObject = other;
            Assert.That(PreviewSession.Current, Is.Not.Null, "not yet");
            yield return null;
            if (endOnDeselect)
            {
                AssertEnded(PreviewEndReason.Deselected);
                AssertBack(player);
            }
            else
                Assert.That(PreviewSession.Current, Is.Not.Null, "goes on");
            AssertNoLogs();
        }

        // the setting on — the subject chosen with another object goes on; a child of the subject alone ends it (the
        // subject's GameObject only)
        [UnityTest]
        public IEnumerator ES16_DeselectKinds()
        {
            FeedbackPlayer player = Player("P", 5f);
            var other = new GameObject("Other");
            var child = new GameObject("Child");
            child.transform.SetParent(player.transform, false);
            Selection.activeGameObject = player.gameObject;
            yield return null;
            PreviewSession.EndOnDeselect = true;
            PreviewSession.Start(player, default);
            Pump(10);
            Selection.objects = new Object[] { player.gameObject, other };
            yield return null;
            Assert.That(PreviewSession.Current, Is.Not.Null, "chosen with another — goes on");
            Selection.activeGameObject = child;
            yield return null;
            AssertEnded(PreviewEndReason.Deselected);
            AssertNoLogs();
        }

        // the editor quitting — the event cannot be raised here, its handler is called
        [Test]
        public void ES16_Quit()
        {
            FeedbackPlayer player = Player("P", 5f);
            PreviewSession.Start(player, default);
            Pump(10);
            PreviewHooks.Quitting();
            AssertEnded(PreviewEndReason.Quit);
            AssertBack(player);
            AssertFree();
            AssertNoLogs();
        }

        // a prefab asset's player is refused (Asset) — no session, nothing left
        [Test]
        public void ES18_PrefabAsset()
        {
            FeedbackPlayer scenePlayer = Player("P", 5f);
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(scenePlayer.gameObject, PrefabPath);
            Object.DestroyImmediate(scenePlayer.gameObject);
            Assert.That(PreviewSession.Start(asset.GetComponent<FeedbackPlayer>(), default), Is.EqualTo(PreviewRefusal.Asset));
            AssertFree();
            m_Global.AssertSame();
            AssertNoLogs();
        }

        // a hidden object's player has no scene (NotInScene)
        [Test]
        public void ES18_HiddenObject()
        {
            GameObject hidden = EditorUtility.CreateGameObjectWithHideFlags(Decoy + "Hidden", HideFlags.HideAndDontSave);
            try
            {
                FeedbackPlayer player = hidden.AddComponent<FeedbackPlayer>();
                Item<PositionItem>(player, new ShakeMotion(), 5f);
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.NotInScene));
                AssertFree();
            }
            finally
            {
                Object.DestroyImmediate(hidden);
            }
            m_Global.AssertSame();
            AssertNoLogs();
        }

        // the C# object of a destroyed player (NotInScene)
        [Test]
        public void ES18_DestroyedPlayer()
        {
            FeedbackPlayer player = Player("P", 5f);
            Object.DestroyImmediate(player.gameObject);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.NotInScene));
            AssertFree();
            m_Global.AssertSame();
            AssertNoLogs();
        }

        // a player on an inactive object starts — the bound path prepares it: it writes and goes back at the end
        [Test]
        public void ES18_InactivePlayer()
        {
            FeedbackPlayer player = Player("P", 5f);
            player.gameObject.SetActive(false);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Assert.That(Pump(20, () => Moved(player)), Is.True, "the preview wrote");
            PreviewSession.Current.Stop();
            AssertBack(player);
            AssertFree();
            m_Global.AssertSame();
            AssertNoLogs();
        }
    }
}
