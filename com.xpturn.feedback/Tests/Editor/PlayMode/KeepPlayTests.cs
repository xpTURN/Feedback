using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;
using T = xpTURN.Feedback.Tests.Editor.KeepTesting;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the play round trip · the mark without an inspector · linked players · the ways of finding · turn off after play. The
    // values after a play are read again by name — a reload empties the locals
    internal sealed class KeepPlayTests
    {
        [TearDown]
        public void TearDown() => T.Clean();

        // two marks (a plain player · a prefab instance's) and an unmarked player: a setting, a duration, the motion's type, a
        // Direct to another scene object, into the instance's child, to an object made in play, a listener → kept; the unmarked
        // one as it was; one undo for both; the instance overridden; the cleared reference said
        IEnumerator RoundTrip(EnterPlayModeOptions options)
        {
            T.SetOptions(options);
            Scene s = T.NewScene();
            FeedbackPlayer p = T.Player("KeepP", T.Tween(1f));
            T.Player("KeepU", T.Tween(1f));
            new GameObject("KeepO");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(T.Prefab("KeepPrefab"), s);
            inst.name = "KeepI";
            EditorSceneManager.SaveScene(s);
            KeepPlayModeChanges.Mark(p, true);
            KeepPlayModeChanges.Mark(inst.GetComponent<FeedbackPlayer>(), true);
            yield return new EnterPlayMode();
            FeedbackPlayer pp = T.Find("KeepP");
            GameObject o = GameObject.Find("KeepO");
            GameObject pi = GameObject.Find("KeepI");
            pp.m_Intensity = 0.4f;
            var first = (PositionItem)pp.m_Items[0];
            first.m_Timing.m_Duration = 0.9f;
            first.m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = Vector4.one * 2f, m_Vibrato = 7, m_Decay = 0.5f } };
            first.m_Target.m_Mode = TargetRefMode.Direct;
            first.m_Target.m_Object = o.transform;
            PositionItem intoChild = T.Tween(0.3f);
            intoChild.m_Target.m_Mode = TargetRefMode.Direct;
            intoChild.m_Target.m_Object = pi.transform.Find("Child");
            pp.m_Items.Add(intoChild);
            PositionItem made = T.Tween(0.3f);
            made.m_Target.m_Mode = TargetRefMode.Direct;
            made.m_Target.m_Object = new GameObject("KeepMadeInPlay").transform;
            pp.m_Items.Add(made);
            var ev = new UnityEventItem();
            UnityEventTools.AddBoolPersistentListener(ev.m_Event, o.SetActive, true);
            pp.m_Items.Add(ev);
            pi.GetComponent<FeedbackPlayer>().m_Intensity = 0.7f;
            T.Find("KeepU").m_Intensity = 0.2f;
            yield return new ExitPlayMode();
            FeedbackPlayer ep = T.InScene("KeepP");
            GameObject ei = T.Root("KeepI");
            GameObject eo = T.Root("KeepO");
            Assert.That(ep.m_Intensity, Is.EqualTo(0.4f));
            var f = (PositionItem)ep.m_Items[0];
            Assert.That(f.m_Timing.m_Duration, Is.EqualTo(0.9f));
            Assert.That(f.m_Motion, Is.TypeOf<PunchMotion>());
            Assert.That(((PunchMotion)f.m_Motion).m_Params.m_Vibrato, Is.EqualTo(7));
            Assert.That(f.m_Target.m_Object == eo.transform, Is.True, "a scene reference");
            Assert.That(((PositionItem)ep.m_Items[1]).m_Target.m_Object == ei.transform.Find("Child"), Is.True, "into the instance (unfolded)");
            Assert.That(((PositionItem)ep.m_Items[2]).m_Target.m_Object == null, Is.True, "made in play");
            Assert.That(((UnityEventItem)ep.m_Items[3]).m_Event.GetPersistentTarget(0) == eo, Is.True, "the listener");
            Assert.That(ei.GetComponent<FeedbackPlayer>().m_Intensity, Is.EqualTo(0.7f));
            Assert.That(PrefabUtility.GetPropertyModifications(ei).Any(m => m.propertyPath == "m_Intensity"), Is.True, "an override");
            Assert.That(T.InScene("KeepU").m_Intensity, Is.EqualTo(1f), "the unmarked one");
            Assert.That(KeepPlayModeChanges.LastAlert, Is.EqualTo("Keep Play Mode Changes — 1 reference to an object made in play mode was cleared"));
            Assert.That(Undo.GetCurrentGroupName(), Is.EqualTo(KeepPlayModeChanges.UndoName));
            Undo.PerformUndo();
            Assert.That(ep.m_Intensity, Is.EqualTo(1f), "one undo — the plain one");
            Assert.That(ei.GetComponent<FeedbackPlayer>().m_Intensity, Is.EqualTo(1f), "one undo — the instance");
        }

        [UnityTest]
        public IEnumerator KP03a_RoundTripReloadOn() => RoundTrip(EnterPlayModeOptions.None);

        [UnityTest]
        public IEnumerator KP03b_RoundTripReloadOff() => RoundTrip(EnterPlayModeOptions.DisableDomainReload);

        // an exit with no change: no undo group, the scene not dirtied
        [UnityTest]
        public IEnumerator KP03c_AnExitWithNoChange()
        {
            T.SetOptions(EnterPlayModeOptions.None);
            Scene s = T.NewScene();
            FeedbackPlayer a = T.Player("KeepNone", T.Tween(1f));
            EditorSceneManager.SaveScene(s);
            KeepPlayModeChanges.Mark(a, true);
            yield return new EnterPlayMode();
            yield return null;
            yield return new ExitPlayMode();
            Assert.That(Undo.GetCurrentGroupName(), Is.Not.EqualTo(KeepPlayModeChanges.UndoName));
            Assert.That(T.InScene("KeepNone").gameObject.scene.isDirty, Is.False);
            Assert.That(KeepPlayModeChanges.LastAlert, Is.Empty);
        }

        // a player whose record cannot be written is left and said; the others are kept in one undo
        [UnityTest]
        public IEnumerator KP03d_APlayerThatFailsLeavesTheOthers()
        {
            T.SetOptions(EnterPlayModeOptions.None);
            Scene s = T.NewScene();
            FeedbackPlayer x = T.Player("KeepFails", T.Tween(1f));
            FeedbackPlayer y = T.Player("KeepKept", T.Tween(1f));
            EditorSceneManager.SaveScene(s);
            KeepPlayModeChanges.Mark(x, true);
            KeepPlayModeChanges.Mark(y, true);
            yield return new EnterPlayMode();
            LogAssert.ignoreFailingMessages = true;
            ((PositionItem)T.Find("KeepFails").m_Items[0]).m_Motion = (FeedbackMotion)Activator.CreateInstance(typeof(KeepThrowingMotion), true);
            T.Find("KeepKept").m_Intensity = 0.3f;
            KeepThrowingMotion.Throw = true;
            yield return new ExitPlayMode();
            KeepThrowingMotion.Throw = false;
            Assert.That(T.InScene("KeepKept").m_Intensity, Is.EqualTo(0.3f));
            Assert.That(((PositionItem)T.InScene("KeepFails").m_Items[0]).m_Motion, Is.TypeOf<TweenMotion>());
            Assert.That(KeepPlayModeChanges.LastAlert, Is.EqualTo("Keep Play Mode Changes — 1 player could not be written"));
            Undo.PerformUndo();
            Assert.That(T.InScene("KeepKept").m_Intensity, Is.EqualTo(1f), "the group closed — one undo");
        }

        // a new domain, no inspector drawn, the mark by the API → kept
        [UnityTest]
        public IEnumerator KP04_Reg27_KeepHookWithoutInspector()
        {
            T.SetOptions(EnterPlayModeOptions.None);
            Scene s = T.NewScene();
            FeedbackPlayer a = T.Player("Keep27", T.Tween(1f));
            EditorSceneManager.SaveScene(s);
            KeepPlayModeChanges.Mark(a, true);
            EditorUtility.RequestScriptReload();
            yield return new WaitForDomainReload();
            yield return new EnterPlayMode();
            T.Find("Keep27").m_Intensity = 0.27f;
            yield return new ExitPlayMode();
            Assert.That(T.InScene("Keep27").m_Intensity, Is.EqualTo(0.27f));
        }

        // a linked player: settings kept, its hidden list as it was; an unlink and copy in play kept; an apply to a preset in
        // play kept (the asset)
        [UnityTest]
        public IEnumerator KP05a_ALinkedPlayer()
        {
            T.SetOptions(EnterPlayModeOptions.None);
            Scene s = T.NewScene();
            FeedbackPreset preset = T.Preset(T.PresetPath, T.Tween(0.2f, "preset"));
            T.Preset(T.Preset2Path, T.Tween(0.9f, "before"));
            FeedbackPlayer l = T.Player("KeepLinked", T.Tween(1f, "own"));
            l.m_Preset = preset;
            FeedbackPlayer u = T.Player("KeepUnlinked", T.Tween(1f, "own"));
            u.m_Preset = preset;
            T.Player("KeepApplier", T.Tween(0.55f, "applied"));
            EditorSceneManager.SaveScene(s);
            KeepPlayModeChanges.Mark(l, true);
            KeepPlayModeChanges.Mark(u, true);
            yield return new EnterPlayMode();
            T.Find("KeepLinked").m_Intensity = 0.4f;
            FeedbackPlayer pu = T.Find("KeepUnlinked");
            PresetOps.Unlink(pu, true);
            pu.m_Items[0].m_Timing.m_Duration = 0.33f;
            PresetOps.ApplyTo(T.Find("KeepApplier"), AssetDatabase.LoadAssetAtPath<FeedbackPreset>(T.Preset2Path), out _);
            yield return new ExitPlayMode();
            FeedbackPlayer el = T.InScene("KeepLinked");
            FeedbackPlayer eu = T.InScene("KeepUnlinked");
            Assert.That(el.m_Intensity, Is.EqualTo(0.4f));
            Assert.That(el.m_Preset == AssetDatabase.LoadAssetAtPath<FeedbackPreset>(T.PresetPath), Is.True);
            Assert.That(el.m_Items[0].m_Label, Is.EqualTo("own"));
            Assert.That(el.m_Items[0].m_Timing.m_Duration, Is.EqualTo(1f));
            Assert.That(eu.m_Preset == null, Is.True, "unlinked in play");
            Assert.That(eu.m_Items[0].m_Label, Is.EqualTo("preset"));
            Assert.That(eu.m_Items[0].m_Timing.m_Duration, Is.EqualTo(0.33f));
            Assert.That(AssetDatabase.LoadAssetAtPath<FeedbackPreset>(T.Preset2Path).m_Items[0].m_Label, Is.EqualTo("applied"), "applied to a preset in play");
        }

        // a linked player's hidden list changed by a script in play is not kept
        [UnityTest]
        public IEnumerator KP05b_TheHiddenListIsNotKept()
        {
            T.SetOptions(EnterPlayModeOptions.None);
            Scene s = T.NewScene();
            FeedbackPreset preset = T.Preset(T.PresetPath, T.Tween(0.2f, "preset"));
            FeedbackPlayer l = T.Player("KeepHidden", T.Tween(1f, "own"));
            l.m_Preset = preset;
            EditorSceneManager.SaveScene(s);
            KeepPlayModeChanges.Mark(l, true);
            yield return new EnterPlayMode();
            T.Find("KeepHidden").m_Items[0].m_Label = "changed in play";
            yield return new ExitPlayMode();
            Assert.That(T.InScene("KeepHidden").m_Items[0].m_Label, Is.EqualTo("own"));
        }

        // moved to DontDestroyOnLoad: kept · destroyed: quiet · a prefab instance marked in play: kept · a player made and
        // marked in play: not found
        [UnityTest]
        public IEnumerator KP07a_TheWaysOfFinding()
        {
            T.SetOptions(EnterPlayModeOptions.None);
            Scene s = T.NewScene();
            FeedbackPlayer d = T.Player("KeepDdol", T.Tween(1f));
            FeedbackPlayer g = T.Player("KeepGone", T.Tween(1f));
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(T.Prefab("KeepPrefab7"), s);
            inst.name = "KeepI7";
            EditorSceneManager.SaveScene(s);
            KeepPlayModeChanges.Mark(d, true);
            KeepPlayModeChanges.Mark(g, true);
            yield return new EnterPlayMode();
            FeedbackPlayer pd = T.Find("KeepDdol");
            Object.DontDestroyOnLoad(pd.gameObject);
            pd.m_Intensity = 0.2f;
            Object.Destroy(T.Find("KeepGone").gameObject);
            FeedbackPlayer pi = T.Find("KeepI7");
            KeepPlayModeChanges.Mark(pi, true);
            pi.m_Intensity = 0.7f;
            KeepPlayModeChanges.Mark(T.Player("KeepSpawned", T.Tween(1f)), true);
            yield return null;
            yield return new ExitPlayMode();
            Assert.That(T.InScene("KeepDdol").m_Intensity, Is.EqualTo(0.2f), "DontDestroyOnLoad");
            Assert.That(T.InScene("KeepGone").m_Intensity, Is.EqualTo(1f), "destroyed");
            Assert.That(T.Root("KeepI7").GetComponent<FeedbackPlayer>().m_Intensity, Is.EqualTo(0.7f), "a prefab instance marked in play");
            Assert.That(KeepPlayModeChanges.LastAlert, Is.EqualTo("Keep Play Mode Changes — 1 player was not found in edit mode"));
        }

        // unmarked in play: not kept, no longer marked
        [UnityTest]
        public IEnumerator KP07b_UnmarkedInPlay()
        {
            T.SetOptions(EnterPlayModeOptions.None);
            Scene s = T.NewScene();
            FeedbackPlayer a = T.Player("KeepOff", T.Tween(1f));
            EditorSceneManager.SaveScene(s);
            KeepPlayModeChanges.Mark(a, true);
            yield return new EnterPlayMode();
            FeedbackPlayer pa = T.Find("KeepOff");
            KeepPlayModeChanges.Mark(pa, false);
            pa.m_Intensity = 0.2f;
            yield return new ExitPlayMode();
            Assert.That(T.InScene("KeepOff").m_Intensity, Is.EqualTo(1f));
            Assert.That(KeepPlayModeChanges.IsMarked(T.InScene("KeepOff")), Is.False);
        }

        // turn off after play: on, the applied player's mark goes; off, it stays
        [UnityTest]
        public IEnumerator KP08a_TurnOffAfterPlay()
        {
            T.SetOptions(EnterPlayModeOptions.None);
            Scene s = T.NewScene();
            FeedbackPlayer a = T.Player("KeepTurn", T.Tween(1f));
            EditorSceneManager.SaveScene(s);
            KeepPlayModeChanges.Mark(a, true);
            KeepPlayModeChanges.TurnOffAfterPlay = true;
            yield return new EnterPlayMode();
            T.Find("KeepTurn").m_Intensity = 0.3f;
            yield return new ExitPlayMode();
            Assert.That(T.InScene("KeepTurn").m_Intensity, Is.EqualTo(0.3f));
            Assert.That(KeepPlayModeChanges.IsMarked(T.InScene("KeepTurn")), Is.False, "on — the mark goes");
            KeepPlayModeChanges.TurnOffAfterPlay = false;
            KeepPlayModeChanges.Mark(T.InScene("KeepTurn"), true);
            yield return new EnterPlayMode();
            T.Find("KeepTurn").m_Intensity = 0.6f;
            yield return new ExitPlayMode();
            Assert.That(T.InScene("KeepTurn").m_Intensity, Is.EqualTo(0.6f));
            Assert.That(KeepPlayModeChanges.IsMarked(T.InScene("KeepTurn")), Is.True, "off — it stays");
        }
    }
}
