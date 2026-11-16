using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using F = xpTURN.Feedback.Tests.Editor.PresetFixtures;
using V = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // the validation of a player's Variants — the slots (FB-B10), each variant's list on the player's anchors, the results'
    // list, the inspector and the window
    internal sealed class ValidationVariantTests
    {
        F m_Fx;
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<string> m_Folds = new List<string>();

        [SetUp]
        public void SetUp()
        {
            m_Fx = new F();
            m_Players.Clear();
            m_Folds.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    VariantShow.Set(p, null);
            }
            foreach (string key in m_Folds)
                SessionState.EraseBool(key);
            m_Fx.Clean();
        }

        FeedbackPlayer Player(string name, params FeedbackItem[] items)
        {
            FeedbackPlayer p = m_Fx.Player(name, items);
            m_Players.Add(p);
            return p;
        }

        static PositionItem AtAnchor(string anchor, float duration = 0.2f)
        {
            PositionItem t = F.Tween(duration, "", TargetRefMode.Anchor);
            t.m_Target.m_Anchor = anchor;
            return t;
        }

        static PositionItem Bump(string anchor)
        {
            SpringParams spring = SpringParams.Default;
            spring.m_Mode = SpringMode.Bump;
            var t = new PositionItem { m_Motion = new SpringMotion { m_Params = spring } };
            t.m_Target.m_Mode = TargetRefMode.Anchor;
            t.m_Target.m_Anchor = anchor;
            t.m_Timing.m_Duration = 0.3f;
            return t;
        }

        // FB-B10: a slot each — empty, a repeat, its preset gone, the linked preset — none for a slot that plays; the fix takes
        // that slot out (one Undo); the inspector's notice says the same
        [UnityTest]
        public IEnumerator VS20b_TheVariantsSlotsThatPlayNothing()
        {
            FeedbackPreset a = m_Fx.Preset("VS20bA", F.Tween(0.2f));
            FeedbackPreset linked = m_Fx.Preset("VS20bL", F.Tween(0.2f));
            FeedbackPreset gone = m_Fx.Preset("VS20bM", F.Tween(0.2f));
            FeedbackPlayer clean = Player("VS20bClean", F.Tween(1f));
            clean.m_Variants = new List<FeedbackPreset> { a };
            Assert.That(V.Of(clean, "FB-B10"), Is.Empty, "a slot that plays");
            FeedbackPlayer p = Player("VS20b", F.Tween(1f));
            p.m_Preset = linked;
            p.m_Variants = new List<FeedbackPreset> { a, null, a, gone, linked };
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(gone));
            List<RuleResult> found = V.Of(p, "FB-B10");
            Assert.That(found.Select(r => r.Message), Is.EqualTo(new[]
            {
                "Variants slot 1 is empty — it plays nothing",
                "Variants slot 2 repeats slot 0 — the first one plays",
                "Variants slot 3's preset is missing — it plays nothing",
                "Variants slot 4 is the linked preset — it plays as the player's list",
            }));
            Assert.That(found.All(r => r.Item == -1 && r.List == null && r.Severity == RuleSeverity.Warning), Is.True);

            var editor = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(p);
            m_Fx.Draw(editor);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(editor.VariantNotice.style.display.value, Is.EqualTo(DisplayStyle.Flex), "the inspector's notice");
            foreach (RuleResult r in found)
                Assert.That(editor.VariantNotice.text, Does.Contain(r.Message));

            RuleResult repeat = found[1];
            Assert.That(repeat.Fixes.Select(f => f.Name), Is.EqualTo(new[] { "Remove slot 2" }));
            FeedbackValidator.Apply(repeat, repeat.Fixes[0]);
            Assert.That(p.m_Variants.Count, Is.EqualTo(4));
            Assert.That(p.m_Variants[2] == null && !ReferenceEquals(p.m_Variants[2], null), Is.True, "slot 2 gone — the missing one moved up");
            Undo.PerformUndo();
            Assert.That(p.m_Variants.Count, Is.EqualTo(5), "one Undo");
            Assert.That(p.m_Variants[2], Is.SameAs(a));
        }

        // FB-B11: two of a player's lists with one alias — the own list first, a repeated slot not counted — and two that differ
        // in case alone · the preset's Alias field
        [UnityTest]
        public IEnumerator PV20_TheAliasesFieldsAndTheirRule()
        {
            FeedbackPreset a = m_Fx.Preset("PV20A", F.Tween(0.2f));
            a.m_Alias = "heavy";
            FeedbackPreset b = m_Fx.Preset("PV20B", F.Tween(0.2f));
            b.m_Alias = "Heavy";
            FeedbackPreset c = m_Fx.Preset("PV20C", F.Tween(0.2f));
            c.m_Alias = "normal";
            FeedbackPlayer clean = Player("PV20Clean", F.Tween(1f));
            clean.m_ListAlias = "normal";
            clean.m_Variants = new List<FeedbackPreset> { a };
            Assert.That(V.Of(clean, "FB-B11"), Is.Empty, "aliases that differ");
            FeedbackPlayer p = Player("PV20", F.Tween(1f));
            p.m_ListAlias = "normal";
            p.m_Variants = new List<FeedbackPreset> { a, b, c, a };
            List<RuleResult> found = V.Of(p, "FB-B11");
            Assert.That(found.Select(r => r.Message), Is.EqualTo(new[]
            {
                "Variants slot 1's alias \"Heavy\" differs from Variants slot 0's only in case — a key matches the exact text",
                "Variants slot 2 has the alias \"normal\" of the player's list — that key plays the player's list",
            }));
            Assert.That(found.All(r => r.Item == -1 && r.Severity == RuleSeverity.Warning && r.Fixes.Count == 0), Is.True);
            FeedbackPreset d = m_Fx.Preset("PV20D", F.Tween(0.2f));
            d.m_Alias = "normal";
            FeedbackPlayer linked = Player("PV20Linked");
            linked.m_Preset = c;
            linked.m_ListAlias = "other";
            linked.m_Variants = new List<FeedbackPreset> { d };
            Assert.That(V.Of(linked, "FB-B11").Select(r => r.Message), Is.EqualTo(new[] { "Variants slot 0 has the alias \"normal\" of the player's list — that key plays the player's list" }), "a linked player's list takes its preset's alias");

            var presetEditor = (FeedbackPresetEditor)UnityEditor.Editor.CreateEditor(a);
            try
            {
                VisualElement root = presetEditor.CreateInspectorGUI();
                Assert.That(root.Query<UnityEditor.UIElements.PropertyField>().ToList().Any(f => f.bindingPath == "m_Alias"), Is.True, "the preset's Alias field");
            }
            finally
            {
                Object.DestroyImmediate(presetEditor);
            }
            yield return null;
        }

        // T30 — the shown list's Alias, first in its section (under Show, over the total) under a line with a little room above
        // and below: the own list's edited and written, a Variant's and a linked preset's read-only with their alias
        [UnityTest]
        public IEnumerator PV25_TheAliasFieldIsTheShownLists()
        {
            FeedbackPreset v = m_Fx.Preset("PV25V", F.Tween(0.2f));
            v.m_Alias = "heavy";
            FeedbackPreset l = m_Fx.Preset("PV25L", F.Tween(0.2f));
            l.m_Alias = "linked";
            FeedbackPlayer p = Player("PV25", F.Tween(1f));
            p.m_ListAlias = "normal";
            p.m_Variants = new List<FeedbackPreset> { v };
            var editor = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(p);
            m_Fx.Draw(editor);
            yield return InspectorTesting.Wait(0.3);
            TextField f = editor.ListAlias;
            string place = (f.parent == editor.Section) + " " + editor.Section.IndexOf(f) + " " + f.label + " " + f.ClassListContains(InspectorStyles.Alias)
                + " " + f.resolvedStyle.borderTopWidth + " " + f.resolvedStyle.marginTop + " " + f.resolvedStyle.paddingTop;
            string own = f.enabledSelf + " " + f.value;
            f.value = "base";
            yield return InspectorTesting.Wait(0.3);
            string written = p.m_ListAlias;
            VariantShow.Set(p, v);
            yield return InspectorTesting.Wait(0.4);
            string variant = editor.ListAlias.enabledSelf + " " + editor.ListAlias.value;
            VariantShow.Set(p, null);
            yield return InspectorTesting.Wait(0.3);
            var so = new SerializedObject(p);
            so.FindProperty("m_Preset").objectReferenceValue = l;
            so.ApplyModifiedProperties();
            yield return InspectorTesting.Wait(0.4);
            string linked = editor.ListAlias.enabledSelf + " " + editor.ListAlias.value;
            Assert.That((place, own, written, variant, linked), Is.EqualTo(("True 0 Alias True 1 6 4", "True normal", "base", "False heavy", "False linked")));
        }

        // T28 — the summary in its highest severity's colour and icon: a warning's, an error's; hidden and plain without results
        [UnityTest]
        public IEnumerator PV21_TheSummaryShowsItsSeverity()
        {
            FeedbackPlayer clean = Player("PV21Clean", F.Tween(1f));
            FeedbackPlayer warned = Player("PV21Warned", F.Tween(1f));
            warned.m_Variants = new List<FeedbackPreset> { null };
            var stop = new HitStopItem();
            stop.m_FreezeDuration = float.PositiveInfinity;
            FeedbackPlayer failed = Player("PV21Failed", F.Tween(1f), stop);
            var seen = new List<string>();
            var tops = new List<string>();
            foreach (FeedbackPlayer p in new[] { clean, warned, failed })
            {
                var editor = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(p);
                m_Fx.Draw(editor);
                yield return InspectorTesting.Wait(0.3);
                Button summary = editor.Validation.Summary;
                List<RuleResult> results = editor.Validation.Results;
                tops.Add(results.Count == 0 ? "none" : results.Max(r => r.Severity).ToString());
                seen.Add(summary.style.display.value + " " + summary.ClassListContains(InspectorStyles.ValidationSummaryWarning) + " "
                         + summary.ClassListContains(InspectorStyles.ValidationSummaryError) + " " + (summary.iconImage.texture != null));
            }
            Assert.That(tops, Is.EqualTo(new[] { "none", "Warning", "Error" }), "the fixtures");
            Assert.That(seen, Is.EqualTo(new[] { "None False False False", "Flex True False True", "Flex False True True" }));
        }

        // T29 — the Variants notice says FB-B11's lines beside FB-B10's, the rule's words; an alias edit elsewhere reaches it
        [UnityTest]
        public IEnumerator PV24_TheVariantsNoticeSaysTheAliases()
        {
            FeedbackPreset a = m_Fx.Preset("PV24A", F.Tween(0.2f));
            a.m_Alias = "heavy";
            FeedbackPreset b = m_Fx.Preset("PV24B", F.Tween(0.2f));
            b.m_Alias = "Heavy";
            FeedbackPlayer p = Player("PV24", F.Tween(1f));
            p.m_Variants = new List<FeedbackPreset> { a, null, b };
            var editor = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(p);
            m_Fx.Draw(editor);
            yield return InspectorTesting.Wait(0.3);
            List<string> rule = V.Of(p, "FB-B11").Select(r => r.Message).ToList();
            string before = editor.VariantNotice.text;
            bool shown = editor.VariantNotice.style.display.value == DisplayStyle.Flex;
            var so = new SerializedObject(b);
            so.FindProperty("m_Alias").stringValue = "light";
            so.ApplyModifiedProperties();
            yield return InspectorTesting.Wait(0.3);
            string after = editor.VariantNotice.text;
            Assert.That(rule, Is.EqualTo(new[] { "Variants slot 2's alias \"Heavy\" differs from Variants slot 0's only in case — a key matches the exact text" }));
            Assert.That((shown, before), Is.EqualTo((true, "Variants slot 1 is empty — it plays nothing\n" + rule[0])));
            Assert.That(after, Is.EqualTo("Variants slot 1 is empty — it plays nothing"), "the alias edit reached the notice");
        }

        // each variant's list on the player's anchors: its item's result carries the variant · no first-frame rule on a variant
        // · a player's line once · a line of the variant's list alone stays · FB-B10 once · a fix writes the variant
        [Test]
        public void VS23_TheVariantsListsAreCheckedOnThePlayersAnchors()
        {
            GameObject hit = m_Fx.Go("Hit");
            var debug = new DebugItem { m_Action = DebugAction.Break };
            FeedbackPreset v = m_Fx.Preset("VS23V", AtAnchor("Gone"), AtAnchor("Hit"), AtAnchor("Hit"), Bump("Hit"), debug);
            FeedbackPreset w = m_Fx.Preset("VS23W", F.Tween(0.2f));
            FeedbackPlayer p = Player("VS23", F.Tween(1f));
            p.m_Anchors.Add(new PlayerAnchor { m_Name = "Hit", m_Object = hit.transform });
            p.m_ApplyInitialState = true;
            p.m_AutoPlay = PlayerAutoPlay.OnEnable;
            p.m_Variants = new List<FeedbackPreset> { v, w, null };
            List<RuleResult> all = FeedbackValidator.Run(p);

            List<RuleResult> a01 = all.Where(r => r.Id == "FB-A01").ToList();
            Assert.That(a01.Count, Is.EqualTo(1));
            Assert.That(a01[0].List, Is.SameAs(v));
            Assert.That(a01[0].Item, Is.EqualTo(0));
            Assert.That(a01[0].ToString(), Does.EndWith(" (variant VS23V)"));
            Assert.That(all.Count(r => r.Id == "FB-D03"), Is.EqualTo(0), "the first frame is the player's list's");
            Assert.That(all.Count(r => r.Id == "FB-D02"), Is.EqualTo(1), "the player's line once");
            List<RuleResult> d04 = all.Where(r => r.Id == "FB-D04").ToList();
            Assert.That(d04.Count, Is.EqualTo(1), "the variant's spring under Restart");
            Assert.That(d04[0].List, Is.SameAs(v));
            Assert.That(all.Count(r => r.Id == "FB-B10"), Is.EqualTo(1), "the slots once");

            FeedbackPlayer control = Player("VS23Control", AtAnchor("Hit"), AtAnchor("Hit"));
            control.m_Anchors.Add(new PlayerAnchor { m_Name = "Hit", m_Object = hit.transform });
            control.m_ApplyInitialState = true;
            Assert.That(V.Of(control, "FB-D03").Count, Is.EqualTo(1), "the control: the rule speaks on a player's list");

            RuleResult brk = all.Single(r => r.Id == "FB-O06");
            Assert.That(brk.List, Is.SameAs(v));
            Assert.That(brk.Fixes[0].Name, Is.EqualTo("Use Log" + RuleContext.VariantWords));
            FeedbackValidator.Apply(brk, brk.Fixes[0]);
            Assert.That(((DebugItem)v.m_Items[4]).m_Action, Is.EqualTo(DebugAction.Log), "the fix writes the variant");

            FeedbackPreset linked = m_Fx.Preset("VS23L", F.Tween(0.2f));
            FeedbackPlayer withLink = Player("VS23Linked");
            withLink.m_Anchors.Add(new PlayerAnchor { m_Name = "Hit", m_Object = hit.transform });
            withLink.m_Preset = linked;
            withLink.m_Variants = new List<FeedbackPreset> { v };
            List<RuleResult> linkedA01 = V.Of(withLink, "FB-A01");
            Assert.That(linkedA01.Count, Is.EqualTo(1), "a linked player's variant");
            Assert.That(linkedA01[0].List, Is.SameAs(v));
        }

        // the inspector's marks are the shown list's results · its panel counts the other lists' · the window's row of a
        // variant's item shows that variant on the player, its fold open — the player already selected too
        [UnityTest]
        public IEnumerator VS30b_TheResultsFollowTheShownList()
        {
            FeedbackPreset v = m_Fx.Preset("VS30bV", F.Tween(0.2f), AtAnchor("Gone"));
            FeedbackPlayer p = Player("VS30b", AtAnchor("Missing"), F.Tween(1f));
            p.m_Variants = new List<FeedbackPreset> { v };
            var editor = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(p);
            m_Fx.Draw(editor);
            yield return InspectorTesting.Wait(0.4);
            Assert.That(editor.List.ResultsFor(0).Count(r => r.Id == "FB-A01"), Is.EqualTo(1), "the player's item 0");
            Assert.That(editor.List.ResultsFor(1).Count(r => r.Id == "FB-A01"), Is.EqualTo(0), "not the variant's item 1");
            editor.Validation.ShowHostPanel(true);
            List<string> lines = editor.Validation.HostPanel.Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.That(lines, Does.Contain("1" + ValidationView.OnItemsText));
            Assert.That(lines, Does.Contain("1" + ValidationView.OtherItemsText));

            VariantShow.Set(p, v);
            yield return InspectorTesting.Wait(0.4);
            Assert.That(editor.List.ResultsFor(1).Count(r => r.Id == "FB-A01"), Is.EqualTo(1), "the variant's item 1");
            Assert.That(editor.List.ResultsFor(0).Count(r => r.Id == "FB-A01"), Is.EqualTo(0));

            VariantShow.Set(p, null);
            yield return InspectorTesting.Wait(0.3);
            Selection.activeObject = p;
            RuleResult result = FeedbackValidator.Run(p).Single(r => r.Id == "FB-A01" && r.List == v);
            string fold = ItemListView.FoldKey(v, ManagedReferenceUtility.GetManagedReferenceIdForObject(v, v.m_Items[1]));
            m_Folds.Add(fold);
            ValidatorScan.Select(result);
            yield return InspectorTesting.Wait(0.4);
            Assert.That(VariantShow.Get(p), Is.SameAs(v), "the row shows its variant");
            Assert.That(Selection.activeObject, Is.SameAs(p), "on the player — its anchors");
            Assert.That(SessionState.GetBool(fold, false), Is.True);
            Assert.That(editor.List.ListOwner, Is.SameAs(v), "the open inspector follows");
            Assert.That(InspectorTesting.Rows(editor.Section)[1].Header.Fold.value, Is.True, "the item's fold open");
        }
    }
}
