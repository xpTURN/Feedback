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
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the player inspector's Variants list and its Show — which list the strip, the rows and the bottom bar read
    internal sealed class InspectorVariantTests
    {
        F m_Fx;
        readonly List<string> m_Errors = new List<string>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();

        [SetUp]
        public void SetUp()
        {
            m_Fx = new F();
            m_Errors.Clear();
            m_Players.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                {
                    VariantShow.Set(p, null);
                    PreviewMasks.Clear(p);
                }
            }
            m_Fx.Clean();
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                m_Errors.Add(message.Split('\n')[0]);
        }

        FeedbackPlayer Player(string name, params FeedbackItem[] items)
        {
            FeedbackPlayer p = m_Fx.Player(name, items);
            m_Players.Add(p);
            return p;
        }

        static List<string> Rows(ListView list) => list.Query<ObjectField>().ToList().Select(f => f.value != null ? f.value.name : "null").ToList();

        static List<string> Data(FeedbackPlayer p) => p.m_Variants.Select(v => ReferenceEquals(v, null) ? "null" : v != null ? v.name : "missing").ToList();

        static void Submit(Button button)
        {
            using (NavigationSubmitEvent e = NavigationSubmitEvent.GetPooled())
            {
                e.target = button;
                button.SendEvent(e);
            }
        }

        // the list: m_Variants, + adds an empty slot (not a copy of the last), each edit and its Undo leave the rows on the data —
        // and nothing logged (no rebind of the rows); several players have none
        [UnityTest]
        public IEnumerator VS20_TheVariantsListEditsTheSlots()
        {
            FeedbackPreset a = m_Fx.Preset("VS20A", F.Tween(0.2f));
            FeedbackPreset b = m_Fx.Preset("VS20B", F.Tween(0.3f));
            FeedbackPlayer p = Player("VS20", F.Tween(1f));
            p.m_Variants = new List<FeedbackPreset> { a, b };
            var editor = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(p);
            m_Fx.Draw(editor);
            yield return InspectorTesting.Wait(0.3);
            ListView list = editor.Variants;
            Assert.That(list, Is.Not.Null, "the Variants list");
            Assert.That(list.bindingPath, Is.EqualTo("m_Variants"));
            Assert.That(list.showBoundCollectionSize, Is.False, "no size field");
            Assert.That(list.showAddRemoveFooter, Is.True);
            Assert.That(list.reorderable, Is.True);
            Assert.That(list.headerTitle, Is.EqualTo("Variants (2)"));
            Foldout fold = list.Q<Foldout>();
            Assert.That(fold.value, Is.False, "closed at first");
            fold.value = true;
            yield return InspectorTesting.Wait(0.3);
            Assert.That(Rows(list), Is.EqualTo(new[] { "VS20A", "VS20B" }));

            Button add = list.Q<Button>("unity-list-view__add-button");
            Assert.That(add, Is.Not.Null, "the + button");
            Undo.IncrementCurrentGroup();
            Submit(add);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(Data(p), Is.EqualTo(new[] { "VS20A", "VS20B", "null" }), "+ adds an empty slot");
            Assert.That(list.headerTitle, Is.EqualTo("Variants (3)"));
            Undo.PerformUndo();
            yield return InspectorTesting.Wait(0.5);
            Assert.That(Data(p), Is.EqualTo(new[] { "VS20A", "VS20B" }));
            Assert.That(Rows(list), Is.EqualTo(new[] { "VS20A", "VS20B" }), "the rows after the add's Undo");

            Undo.IncrementCurrentGroup();
            list.viewController.RemoveItem(0);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(Data(p), Is.EqualTo(new[] { "VS20B" }));
            Undo.PerformUndo();
            yield return InspectorTesting.Wait(0.5);
            Assert.That(Rows(list), Is.EqualTo(new[] { "VS20A", "VS20B" }), "the rows after the remove's Undo");

            var so = new SerializedObject(p);
            Undo.IncrementCurrentGroup();
            so.FindProperty("m_Variants").MoveArrayElement(0, 1);
            so.ApplyModifiedProperties();
            yield return InspectorTesting.Wait(0.5);
            Assert.That(Rows(list), Is.EqualTo(new[] { "VS20B", "VS20A" }), "a move in code");
            Undo.PerformUndo();
            yield return InspectorTesting.Wait(0.5);
            Assert.That(Rows(list), Is.EqualTo(new[] { "VS20A", "VS20B" }), "its Undo");

            Undo.IncrementCurrentGroup();
            list.viewController.Move(0, 1);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(Data(p), Is.EqualTo(new[] { "VS20B", "VS20A" }), "the view's move");
            Undo.PerformUndo();
            yield return InspectorTesting.Wait(0.5);
            Assert.That(Rows(list), Is.EqualTo(new[] { "VS20A", "VS20B" }));
            Assert.That(m_Errors, Is.Empty, "nothing logged");

            FeedbackPlayer other = Player("VS20b");
            var several = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(new Object[] { p, other });
            VisualElement root = m_Fx.Draw(several);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(several.Variants, Is.Null);
            Assert.That(root.Query<ListView>().ToList().Any(l => l.bindingPath == "m_Variants"), Is.False, "several players: none");
        }

        // Show: there with a slot that plays · a variant read-only in the strip and the rows, Open Preset alone below · kept when
        // drawn again · a change from outside reaches the open inspector · the slot gone or its asset deleted: Base · the
        // Variants list stays the same element · two presets of one name told apart · the rows' masks are the shown list's
        [UnityTest]
        public IEnumerator VS34_ShowPicksTheListTheInspectorShows()
        {
            FeedbackPreset a = m_Fx.Preset("VS34A", F.Tween(0.2f, "a0"), F.Tween(0.3f, "a1"));
            FeedbackPreset b = m_Fx.Preset("VS34B", F.Tween(0.4f, "b0"));
            AssetDatabase.CreateFolder(F.Dir, "Sub");
            var twin = ScriptableObject.CreateInstance<FeedbackPreset>();
            twin.m_Items.Add(F.Tween(0.1f, "t0"));
            AssetDatabase.CreateAsset(twin, F.Dir + "/Sub/VS34A.asset");
            FeedbackPlayer p = Player("VS34", F.Tween(1f, "own0"), F.Tween(1f, "own1"), F.Tween(1f, "own2"));
            FeedbackPlayer none = Player("VS34None", F.Tween(1f));
            none.m_Variants = new List<FeedbackPreset> { null };
            var noneEditor = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(none);
            m_Fx.Draw(noneEditor);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(noneEditor.Show.style.display.value, Is.EqualTo(DisplayStyle.None), "no slot that plays: no Show");

            p.m_Variants = new List<FeedbackPreset> { a, b, twin };
            var editor = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(p);
            m_Fx.Draw(editor);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(editor.Show.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(editor.Show.choices, Is.EqualTo(new[] { "Base", "VS34A (slot 0)", "VS34B", "VS34A (slot 2)" }));
            Assert.That(editor.Show.index, Is.EqualTo(0));
            Assert.That(InspectorTesting.Rows(editor.Section).Count, Is.EqualTo(3), "Base: the player's rows");
            Assert.That(editor.List.ReadOnly, Is.False);
            ListView variantsBefore = editor.Variants;
            int builds = editor.Builds;

            VariantShow.Set(p, a);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(editor.Builds, Is.GreaterThan(builds), "the open inspector follows a change from outside");
            Assert.That(editor.Show.index, Is.EqualTo(1));
            Assert.That(editor.List.ReadOnly, Is.True, "a variant is read-only");
            Assert.That(editor.List.ListOwner, Is.SameAs(a));
            List<ItemElement> rows = InspectorTesting.Rows(editor.Section);
            Assert.That(rows.Count, Is.EqualTo(2), "the variant's rows");
            List<string> buttons = editor.Section.Query<Button>().ToList().Select(x => x.text).Where(t => !string.IsNullOrEmpty(t)).ToList();
            Assert.That(buttons, Does.Contain("Open Preset"));
            Assert.That(buttons, Has.No.Member(FeedbackPlayerEditor.AddText));
            Assert.That(buttons, Has.No.Member("Unlink"));
            Assert.That(editor.Section.Q<TimelineStrip>(), Is.Not.Null);

            PreviewMasks.Set(p, a, 1, PreviewMask.Mute);
            var again = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(p);
            m_Fx.Draw(again);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(again.List.ListOwner, Is.SameAs(a), "kept when drawn again");
            rows = InspectorTesting.Rows(again.Section);
            Assert.That(rows[1].Header.Mute.value, Is.True, "the variant's mask on its row");
            Assert.That(rows[0].Header.Mute.value, Is.False);
            editor = again;
            variantsBefore = editor.Variants;

            VariantShow.Set(p, null);
            yield return InspectorTesting.Wait(0.3);
            rows = InspectorTesting.Rows(editor.Section);
            Assert.That(rows.Count, Is.EqualTo(3));
            Assert.That(rows[1].Header.Mute.value, Is.False, "Base: the player's masks");

            var so = new SerializedObject(p);
            VariantShow.Set(p, b);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(editor.List.ListOwner, Is.SameAs(b));
            so.Update();
            so.FindProperty("m_Variants").DeleteArrayElementAtIndex(1);
            so.ApplyModifiedProperties();
            yield return InspectorTesting.Wait(0.4);
            Assert.That(editor.List.ListOwner, Is.SameAs(p), "the shown slot gone: Base");
            Assert.That(editor.Variants, Is.SameAs(variantsBefore), "the Variants list is not built again");
            Assert.That(editor.Variants.panel, Is.Not.Null);
            Assert.That(editor.Show.choices, Is.EqualTo(new[] { "Base", "VS34A (slot 0)", "VS34A (slot 1)" }));

            VariantShow.Set(p, twin);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(editor.List.ListOwner, Is.SameAs(twin));
            AssetDatabase.DeleteAsset(F.Dir + "/Sub/VS34A.asset");
            yield return InspectorTesting.Wait(0.5);
            Assert.That(editor.List.ListOwner, Is.SameAs(p), "the shown asset deleted: Base");
            Assert.That(editor.Show.choices, Is.EqualTo(new[] { "Base", "VS34A" }));
            Assert.That(m_Errors, Is.Empty);
        }

        // the strip's heads are the shown list's plays · Last play is the player's last accepted play, of any list
        [Test]
        public void VS25_TheHeadsAndLastPlayFollowTheShownList()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            preset.name = "VS25V";
            preset.m_Items.Add(F.Tween(1f));
            m_Fx.Made.Add(preset);
            FeedbackPlayer p = Player("VS25", F.Tween(1f));
            p.m_Variants = new List<FeedbackPreset> { preset };
            p.Bind(s);
            try
            {
                var heads = new List<TimelineModel.Head>();
                var variantStrip = new TimelineStrip(new SerializedObject(preset), ItemListEdits.ItemsPath, p, true);
                var baseStrip = new TimelineStrip(new SerializedObject(p), ItemListEdits.ItemsPath, p);
                int Heads(TimelineStrip strip)
                {
                    strip.RefreshHeads();
                    return strip.Heads.Count;
                }

                p.Play(PlayArgs.Default.WithPreset(preset).WithIntensity(0.5f));
                s.Tick(0.1f);
                Assert.That(Heads(variantStrip), Is.EqualTo(1), "the variant's play on the variant's strip");
                Assert.That(Heads(baseStrip), Is.EqualTo(0), "none on the player's");
                TimelineModel.Heads(p, preset, heads);
                Assert.That(heads.Count, Is.EqualTo(1));
                Assert.That(IntensityView.LastIntensity(p, out float last), Is.True);
                Assert.That(last, Is.EqualTo(0.5f));

                p.Play(PlayArgs.Default.WithIntensity(0.8f));
                s.Tick(0.1f);
                Assert.That(Heads(baseStrip), Is.EqualTo(1));
                Assert.That(Heads(variantStrip), Is.EqualTo(0), "Restart ended the variant's play");
                IntensityView.LastIntensity(p, out last);
                Assert.That(last, Is.EqualTo(0.8f));

                p.Play(PlayArgs.Default.WithPreset(preset).WithIntensity(0.3f));
                s.Tick(0.1f);
                IntensityView.LastIntensity(p, out last);
                Assert.That(last, Is.EqualTo(0.3f), "the last play, the variant's");
            }
            finally
            {
                p.Bind(null);
                s.Shutdown();
            }
        }
    }
}
