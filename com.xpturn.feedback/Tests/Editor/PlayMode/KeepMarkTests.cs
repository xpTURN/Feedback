using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using T = xpTURN.Feedback.Tests.Editor.KeepTesting;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the mark · a player of an unsaved scene · the inspector's row and hint
    internal sealed class KeepMarkTests
    {
        [TearDown]
        public void TearDown() => T.Clean();

        // the edit id in SessionState across a reload, the scene not dirtied; the row (one · several — mixed); hidden on a
        // prefab asset and in a prefab stage
        [UnityTest]
        public IEnumerator KP01a_TheMark()
        {
            Scene s = T.NewScene();
            FeedbackPlayer a = T.Player("KeepMarkA", T.Tween(1f));
            FeedbackPlayer b = T.Player("KeepMarkB", T.Tween(1f));
            T.Prefab("KeepMarkPrefab");
            EditorSceneManager.SaveScene(s);
            Assert.That(KeepPlayModeChanges.Mark(a, true), Is.True);
            Assert.That(KeepPlayModeChanges.Marks(), Does.Contain(KeepPlayModeChanges.Id(a)));
            Assert.That(s.isDirty, Is.False);
            VisualElement root = T.Draw(b);
            yield return null;
            var keep = root.Q<Toggle>(KeepPlayModeChanges.ToggleName);
            Assert.That(T.Shown(keep), Is.EqualTo("on"));
            keep.value = true;
            Assert.That(KeepPlayModeChanges.IsMarked(b), Is.True);
            KeepPlayModeChanges.Mark(b, false);
            root = T.Draw(a, b);
            yield return null;
            Assert.That(root.Q<Toggle>(KeepPlayModeChanges.ToggleName).showMixedValue, Is.True, "several");
            root = T.Draw(AssetDatabase.LoadAssetAtPath<GameObject>(T.PrefabPath).GetComponent<FeedbackPlayer>());
            yield return null;
            Assert.That(T.Shown(root.Q(KeepPlayModeChanges.RowName)), Is.EqualTo("hidden"), "a prefab asset");
            PrefabStage stage = PrefabStageUtility.OpenPrefab(T.PrefabPath);
            yield return null;
            root = T.Draw(stage.prefabContentsRoot.GetComponent<FeedbackPlayer>());
            yield return null;
            Assert.That(T.Shown(root.Q(KeepPlayModeChanges.RowName)), Is.EqualTo("hidden"), "a prefab stage");
            StageUtility.GoToMainStage();
            T.CloseWindow();
            EditorUtility.RequestScriptReload();
            yield return new WaitForDomainReload();
            Assert.That(KeepPlayModeChanges.IsMarked(T.InScene("KeepMarkA")), Is.True, "across a reload");
        }

        // a player of an unsaved scene: the toggle off with the reason, no mark, no null id; a reference into that scene is
        // cleared and counted
        [UnityTest]
        public IEnumerator KP06a_AnUnsavedScenesPlayer()
        {
            LogAssert.ignoreFailingMessages = true;
            Scene untitled = SceneManager.GetSceneAt(0);
            SceneManager.SetActiveScene(untitled);
            FeedbackPlayer n = T.Player("KeepUnsaved", T.Tween(1f));
            new GameObject("KeepUnsavedTarget");
            Assert.That(KeepPlayModeChanges.Mark(n, true), Is.False);
            Assert.That(KeepPlayModeChanges.Marks().Any(m => m.StartsWith("GlobalObjectId_V1-0-", StringComparison.Ordinal)), Is.False);
            VisualElement root = T.Draw(n);
            yield return null;
            var keep = root.Q<Toggle>(KeepPlayModeChanges.ToggleName);
            Assert.That(T.Shown(keep), Is.EqualTo("off"));
            Assert.That(keep.tooltip, Is.EqualTo(KeepPlayModeChanges.UnsavedText));
            T.CloseWindow();
            T.SetOptions(EnterPlayModeOptions.None);
            Scene s = T.NewScene();
            FeedbackPlayer p = T.Player("KeepSaved", T.Tween(1f));
            EditorSceneManager.SaveScene(s);
            KeepPlayModeChanges.Mark(p, true);
            yield return new EnterPlayMode();
            var item = (PositionItem)T.Find("KeepSaved").m_Items[0];
            item.m_Target.m_Mode = TargetRefMode.Direct;
            item.m_Target.m_Object = GameObject.Find("KeepUnsavedTarget").transform;
            yield return new ExitPlayMode();
            Assert.That(((PositionItem)T.InScene("KeepSaved").m_Items[0]).m_Target.m_Object == null, Is.True);
            Assert.That(KeepPlayModeChanges.LastAlert, Is.EqualTo("Keep Play Mode Changes — 1 reference to an object made in play mode was cleared"));
            Object.DestroyImmediate(GameObject.Find("KeepUnsaved"));
            Object.DestroyImmediate(GameObject.Find("KeepUnsavedTarget"));
        }

        // the row's words; the second toggle once on; in play, the hint on the marked player only
        [UnityTest]
        public IEnumerator KP09a_TheRowAndTheHint()
        {
            T.SetOptions(EnterPlayModeOptions.None);
            Scene s = T.NewScene();
            FeedbackPlayer a = T.Player("KeepHintA", T.Tween(1f));
            T.Player("KeepHintB", T.Tween(1f));
            EditorSceneManager.SaveScene(s);
            VisualElement root = T.Draw(a);
            yield return null;
            var keep = root.Q<Toggle>(KeepPlayModeChanges.ToggleName);
            Assert.That(keep.label, Is.EqualTo(KeepPlayModeChanges.ToggleText));
            Assert.That(keep.tooltip, Is.EqualTo(KeepPlayModeChanges.ToggleTooltip));
            Assert.That(T.Shown(root.Q<Toggle>(KeepPlayModeChanges.TurnOffName)), Is.EqualTo("hidden"));
            keep.value = true;
            Assert.That(T.Shown(root.Q<Toggle>(KeepPlayModeChanges.TurnOffName)), Is.EqualTo("on"));
            Assert.That(root.Q<Toggle>(KeepPlayModeChanges.TurnOffName).label, Is.EqualTo(KeepPlayModeChanges.TurnOffText));
            T.CloseWindow();
            yield return new EnterPlayMode();
            VisualElement r1 = T.Draw(T.Find("KeepHintA"));
            yield return null;
            yield return null;
            string marked = T.Shown(r1.Q<InspectorStatus>().Kept);
            VisualElement r2 = T.Draw(T.Find("KeepHintB"));
            yield return null;
            yield return null;
            string unmarked = T.Shown(r2.Q<InspectorStatus>().Kept);
            T.CloseWindow();
            yield return new ExitPlayMode();
            Assert.That(marked, Is.EqualTo("on"));
            Assert.That(unmarked, Is.EqualTo("hidden"));
        }
    }
}
