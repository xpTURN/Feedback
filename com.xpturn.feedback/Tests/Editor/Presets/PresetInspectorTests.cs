using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using F = xpTURN.Feedback.Tests.Editor.PresetFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // the preset's inspector · a linked player's inspector
    internal sealed class PresetInspectorTests
    {
        F m_Fx;

        [SetUp]
        public void SetUp() => m_Fx = new F();

        [TearDown]
        public void TearDown() => m_Fx.Clean();

        static bool FieldsOff(ItemElement row) => !row.Body.Query<VisualElement>().Where(e => e is IBindable).ToList().Any(e => e.enabledInHierarchy);

        // the list and the strip without a host (×1), the lock that does not throw, the results, a paste clearing its scene
        // references, the words of the two ways, the fold kept by the preset
        [UnityTest]
        public IEnumerator PH04a_ThePresetsInspector()
        {
            GameObject sceneObject = m_Fx.Go("PH04aScene");
            FeedbackPreset preset = m_Fx.Preset("PH04a", F.Tween(0.2f), F.Tween(0.3f), F.Direct(null, "direct"));
            VisualElement root = m_Fx.Draw(UnityEditor.Editor.CreateEditor(preset));
            yield return InspectorTesting.Wait(0.3);
            List<ItemElement> rows = InspectorTesting.Rows(root);
            var list = root.Q<ItemListView>();
            Assert.That(rows.Count, Is.EqualTo(3));
            Assert.That(list.Locked, Is.False);
            Assert.That(list.ReadOnly, Is.False);
            Assert.That(root.Q<TimelineStrip>().Total.text, Is.EqualTo("Total 0.3 s"), "×1 · no start delay");
            Assert.That(root.Q<ValidationView>().Results.Any(r => r.Id == "FB-A09" && r.Item == 2), Is.True, "the preset's check");
            Assert.That(root.Query<HelpBox>().ToList().Any(h => h.text == FeedbackPresetEditor.HelpText), Is.True);
            Assert.That(root.Query<Toggle>().ToList().Any(t => t.label == FeedbackPresetEditor.DropLinkText), Is.True);
            rows[0].Header.Fold.value = true;
            ItemClipboard.Set(new FeedbackItem[] { F.Direct(sceneObject.transform, "pasted") });
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(ItemListEdits.PasteClearedWarning)));
            int at = list.Paste();
            Assert.That(((TargetItem)preset.m_Items[at]).m_Target.m_Object, Is.Null, "a scene reference the preset cannot hold");
            VisualElement again = m_Fx.Draw(UnityEditor.Editor.CreateEditor(preset));
            yield return InspectorTesting.Wait(0.3);
            Assert.That(InspectorTesting.Rows(again)[0].Header.Fold.value, Is.True, "the fold keyed by the preset");
        }

        // a linked player reads the preset's list read-only: Copy only, the bodies and the switches off, ▶ · Solo alive;
        // the status says it is linked
        [UnityTest]
        public IEnumerator PH05a_ALinkedPlayerReadsThePresetReadOnly()
        {
            FeedbackPreset preset = m_Fx.Preset("PH05a", F.Tween(0.2f), F.Tween(0.3f));
            FeedbackPlayer p = m_Fx.Player("PH05a", F.Tween(1f, "own"));
            PresetOps.Link(p, preset);
            var editor = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(p);
            VisualElement root = m_Fx.Draw(editor);
            yield return InspectorTesting.Wait(0.3);
            ItemListView list = editor.List;
            List<ItemElement> rows = InspectorTesting.Rows(editor.Section);
            Assert.That(list.ReadOnly, Is.True);
            Assert.That(list.List.reorderable, Is.False);
            Assert.That(rows.Count, Is.EqualTo(2), "the preset's rows");
            rows[0].Header.Fold.value = true;
            yield return InspectorTesting.Wait(0.2);
            Assert.That(FieldsOff(rows[0]), Is.True);
            Assert.That(rows[0].Header.Active.enabledInHierarchy, Is.False);
            Assert.That(rows[0].Header.Play.enabledInHierarchy, Is.True);
            Assert.That(rows[0].Header.Solo.enabledInHierarchy, Is.True);
            list.Delete(0);
            list.Duplicate(0);
            list.Move(0, 1);
            ItemClipboard.Set(new FeedbackItem[] { F.Tween(0.1f) });
            Assert.That(list.Paste(), Is.EqualTo(-1));
            Assert.That(preset.m_Items.Count, Is.EqualTo(2), "no edit of the preset");
            Assert.That(preset.m_Items[0].m_Timing.m_Duration, Is.EqualTo(0.2f));
            Assert.That(root.Q<InspectorStatus>().Linked.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        // the linked strip: the preset's list, the player's multiplier (a child element watches the player — one element
        // tracks one SerializedObject)
        [UnityTest]
        public IEnumerator PH05b_TheLinkedStripFollowsThePlayersMultiplier()
        {
            LogAssert.ignoreFailingMessages = false;
            FeedbackPreset preset = m_Fx.Preset("PH05b", F.Tween(0.2f), F.Tween(0.3f));
            FeedbackPlayer p = m_Fx.Player("PH05b");
            PresetOps.Link(p, preset);
            var editor = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(p);
            m_Fx.Draw(editor);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(editor.Section.Q<TimelineStrip>().Total.text, Is.EqualTo("Total 0.3 s"));
            var other = new SerializedObject(p);
            other.FindProperty("m_DurationMultiplier").floatValue = 2f;
            other.ApplyModifiedProperties();
            yield return InspectorTesting.Wait(0.4);
            Assert.That(editor.Section.Q<TimelineStrip>().Total.text, Is.EqualTo("Total 0.6 s"));
        }

        // Unlink shows the own list again, Unlink and Copy replaces it by the preset's deep copies in one undo; the section
        // is built again at each link change
        [UnityTest]
        public IEnumerator PH05c_UnlinkAndUnlinkAndCopy()
        {
            FeedbackPreset preset = m_Fx.Preset("PH05c", F.Tween(0.2f), F.Tween(0.3f));
            FeedbackPlayer p = m_Fx.Player("PH05c", F.Tween(1f, "own"));
            PresetOps.Link(p, preset);
            var editor = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(p);
            m_Fx.Draw(editor);
            yield return InspectorTesting.Wait(0.3);
            int builds = editor.Builds;
            Assert.That(PresetOps.Unlink(p, false), Is.True);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(p.m_Preset, Is.Null);
            Assert.That(editor.Builds, Is.GreaterThan(builds), "built again");
            Assert.That(editor.List.ReadOnly, Is.False);
            Assert.That(InspectorTesting.Rows(editor.Section).Count, Is.EqualTo(1), "the own list again");
            PresetOps.Link(p, preset);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(editor.List.ReadOnly, Is.True);
            Undo.ClearAll();
            Assert.That(PresetOps.Unlink(p, true), Is.True);
            Assert.That(p.m_Preset, Is.Null);
            Assert.That(p.m_Items.Count, Is.EqualTo(2));
            Assert.That(p.m_Items[0], Is.Not.SameAs(preset.m_Items[0]), "deep copies");
            Assert.That(((PositionItem)p.m_Items[0]).m_Motion, Is.Not.SameAs(((PositionItem)preset.m_Items[0]).m_Motion));
            Undo.PerformUndo();
            Assert.That(p.m_Preset, Is.SameAs(preset), "one undo");
            Assert.That(p.m_Items.Count, Is.EqualTo(1));
            Assert.That(p.m_Items[0].m_Label, Is.EqualTo("own"));
        }

        // a deleted preset's player says so
        [UnityTest]
        public IEnumerator PH05d_ADeletedPresetSaysSo()
        {
            FeedbackPreset preset = m_Fx.Preset("PH05d", F.Tween(0.2f));
            FeedbackPlayer p = m_Fx.Player("PH05d", F.Tween(1f));
            PresetOps.Link(p, preset);
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(preset));
            var editor = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(p);
            VisualElement root = m_Fx.Draw(editor);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(root.Q<InspectorStatus>().MissingPreset.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q<InspectorStatus>().Linked.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(editor.List.ReadOnly, Is.False, "the own list it plays");
        }
    }
}
