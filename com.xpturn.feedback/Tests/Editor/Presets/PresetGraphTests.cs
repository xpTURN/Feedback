using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using F = xpTURN.Feedback.Tests.Editor.PresetFixtures;
using K = xpTURN.Feedback.Tests.Editor.KeepTesting;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the preset inspector's Graph Player: the default (a link first), the strip and the Graph on it, none, a player that does
    // not list the preset, the items' Graphs, a change, the dropdown of an object's players, a prefab asset's player, the
    // memory across play mode, an unsaved scene and a delete
    internal sealed class PresetGraphTests
    {
        F m_Fx;

        [SetUp]
        public void SetUp() => m_Fx = new F();

        // a reload in play empties the fields — a new set of fixtures cleans the folder all the same
        [TearDown]
        public void TearDown()
        {
            (m_Fx ?? new F()).Clean();
            K.Clean();
        }

        static PositionItem Punch(float duration, float delay = 0f)
        {
            var item = new PositionItem { m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(0.4f, 0.2f, 0f, 0f), m_Vibrato = 4f, m_Decay = 1f } } };
            item.m_Target.m_Mode = TargetRefMode.Self;
            item.m_Timing.m_Duration = duration;
            item.m_Timing.m_Delay = delay;
            return item;
        }

        FeedbackPresetEditor Draw(FeedbackPreset preset, out VisualElement root)
        {
            var editor = (FeedbackPresetEditor)UnityEditor.Editor.CreateEditor(preset);
            root = m_Fx.Draw(editor);
            return editor;
        }

        // the dry model the player's own inspector builds for this list: the same function, the same axis
        static SlotGraphModel Expected(FeedbackPlayer player, FeedbackPreset preset, FeedbackPresetEditor editor)
        {
            GraphInputs inputs = GraphInputs.Of(player);
            return SlotGraphModel.Build(player, preset.m_Items, preset, in inputs, editor.Strip.Span, editor.SlotGraph.Model.Columns, null,
                SlotGraphModel.LiveLayers(player, false), null, RecordWatch.FollowOf(player, preset));
        }

        static IEnumerator OpenGraph(FeedbackPresetEditor editor)
        {
            editor.SlotGraph.Switch.value = true;
            yield return InspectorTesting.Wait(0.4);
        }

        // the Graph Preview's place: right after the bottom bar (Add · Paste), below the list
        static void AfterTheBottomBar(VisualElement root, SlotGraphField field)
        {
            VisualElement bottom = root.Q(className: InspectorStyles.Bottom);
            VisualElement place = field.parent;
            Assert.That(place.parent, Is.SameAs(bottom.parent));
            Assert.That(place.parent.IndexOf(place), Is.EqualTo(place.parent.IndexOf(bottom) + 1), "right after the bottom bar");
            Assert.That(field.worldBound.yMin, Is.GreaterThan(root.Q<ItemListView>().worldBound.yMax), "below the list");
        }

        static Component FieldHost(TargetRefField field) => (Component)typeof(TargetRefField).GetField("m_Host", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(field);

        // T2 — a linked player by default (before a Variants player made first), a Graph Preview right after the bottom bar, its
        // sum the linked player's
        [UnityTest]
        public IEnumerator PG02_TheLinkedPlayerByDefault()
        {
            FeedbackPreset preset = m_Fx.Preset("PG02", Punch(0.3f), Punch(0.5f, 0.1f));
            FeedbackPlayer variant = m_Fx.Player("PG02v");
            variant.m_Variants.Add(preset);
            FeedbackPlayer linked = m_Fx.Player("PG02l");
            PresetOps.Link(linked, preset);
            FeedbackPresetEditor editor = Draw(preset, out VisualElement root);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(editor.Shown, Is.SameAs(linked), "a link first");
            Assert.That(editor.GraphPlayer.value, Is.SameAs(linked));
            Assert.That(root.Q<SlotGraphField>(), Is.SameAs(editor.SlotGraph), "a Graph in a preset's inspector");
            AfterTheBottomBar(root, editor.SlotGraph);
            yield return OpenGraph(editor);
            SlotGraphModel m = editor.SlotGraph.Model;
            Assert.That(m, Is.Not.Null);
            Assert.That(m.Refused, Is.Null);
            Assert.That(m.Groups.Count, Is.GreaterThan(0));
            Assert.That(m.Ticks, Is.EqualTo(Expected(linked, preset, editor).Ticks), "the linked player's list composed");
            Assert.That(editor.SlotGraph.Notes.text, Does.Not.Contain("as if"));
        }

        // T3 — the strip on the player: its length multiplier and start delay; the Graph spans the strip's pads
        [UnityTest]
        public IEnumerator PG03_TheStripIsThePlayers()
        {
            FeedbackPreset preset = m_Fx.Preset("PG03", Punch(0.3f), Punch(0.2f, 0.1f));
            FeedbackPlayer linked = m_Fx.Player("PG03l");
            linked.m_DurationMultiplier = 2f;
            linked.m_StartDelay = 0.5f;
            PresetOps.Link(linked, preset);
            var playerEditor = (FeedbackPlayerEditor)UnityEditor.Editor.CreateEditor(linked);
            m_Fx.Draw(playerEditor);
            yield return InspectorTesting.Wait(0.3);
            TimelineStrip playerStrip = playerEditor.Section.Q<TimelineStrip>();
            string total = playerStrip.Total.text;
            double span = playerStrip.Span;
            FeedbackPresetEditor editor = Draw(preset, out _);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(total, Is.EqualTo("Total 1.1 s"), "the player's ×2 and its 0.5 s start delay");
            Assert.That(editor.Strip.Total.text, Is.EqualTo(total));
            Assert.That(editor.Strip.Span, Is.EqualTo(span));
            yield return OpenGraph(editor);
            Rect canvas = editor.Strip.Canvas.worldBound;
            Rect view = editor.SlotGraph.View.worldBound;
            Assert.That(view.xMin, Is.EqualTo(canvas.xMin + TimelineStrip.Pad).Within(1f), "the left pad");
            Assert.That(view.xMax, Is.EqualTo(canvas.xMax - TimelineStrip.Pad).Within(1f), "the right pad");
        }

        // T4 — no player: the Graph Preview is there, switched on it builds nothing and says how; the items' Graphs have no host
        [UnityTest]
        public IEnumerator PG04_NoPlayer()
        {
            FeedbackPreset preset = m_Fx.Preset("PG04", Punch(0.3f));
            FeedbackPresetEditor editor = Draw(preset, out VisualElement root);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(editor.Shown, Is.Null);
            Assert.That(editor.Strip.Total.text, Is.EqualTo("Total 0.3 s"), "×1 · no start delay");
            yield return OpenGraph(editor);
            Assert.That(editor.SlotGraph.Builds, Is.EqualTo(0), "nothing built");
            Assert.That(editor.SlotGraph.Model, Is.Null);
            Assert.That(editor.SlotGraph.Notes.text, Is.EqualTo(SlotGraphField.NoPlayerText));
            List<ItemElement> rows = InspectorTesting.Rows(root);
            rows[0].Header.Fold.value = true;
            yield return InspectorTesting.Wait(0.2);
            Assert.That(rows[0].Body.Graph, Is.Not.Null);
            Assert.That(rows[0].Body.Graph.Player, Is.Null, "no host — ×1, no start delay");
        }

        // T5 — a player that neither links nor holds the preset: drawn as if it played it, said so; holding it in its Variants
        // drops the words; no recorded dots
        [UnityTest]
        public IEnumerator PG05_AnUnlistedPlayer()
        {
            FeedbackPreset preset = m_Fx.Preset("PG05", Punch(0.3f), Punch(0.2f, 0.2f));
            FeedbackPlayer other = m_Fx.Player("PG05o", Punch(1f));
            other.m_StartDelay = 0.25f;
            FeedbackPresetEditor editor = Draw(preset, out _);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(editor.Shown, Is.Null);
            editor.GraphPlayer.value = other;
            yield return InspectorTesting.Wait(0.1);
            Assert.That(editor.Shown, Is.SameAs(other));
            yield return OpenGraph(editor);
            SlotGraphModel m = editor.SlotGraph.Model;
            Assert.That(m.Refused, Is.Null);
            Assert.That(m.Ticks, Is.EqualTo(Expected(other, preset, editor).Ticks), "the preset's list on the player's settings");
            Assert.That(m.Follow, Is.Null, "no record of a list it does not play");
            Assert.That(editor.SlotGraph.Notes.text, Does.StartWith(FeedbackPresetEditor.AsIfText(other)));
            var so = new SerializedObject(other);
            SerializedProperty variants = so.FindProperty("m_Variants");
            variants.InsertArrayElementAtIndex(0);
            variants.GetArrayElementAtIndex(0).objectReferenceValue = preset;
            so.ApplyModifiedProperties();
            yield return InspectorTesting.Wait(0.4);
            Assert.That(editor.SlotGraph.Notes.text, Does.Not.Contain("as if"), "it holds the preset now");
        }

        // T6 — the items' Graphs on the player (its start delay and multiplier); the fields and the folds keep no host
        [UnityTest]
        public IEnumerator PG06_TheItemGraphsFollowThePlayer()
        {
            FeedbackPreset preset = m_Fx.Preset("PG06", Punch(0.3f));
            FeedbackPlayer linked = m_Fx.Player("PG06l");
            linked.m_DurationMultiplier = 2f;
            linked.m_StartDelay = 0.5f;
            PresetOps.Link(linked, preset);
            FeedbackPresetEditor editor = Draw(preset, out VisualElement root);
            yield return InspectorTesting.Wait(0.3);
            List<ItemElement> rows = InspectorTesting.Rows(root);
            rows[0].Header.Fold.value = true;
            yield return InspectorTesting.Wait(0.2);
            ItemGraphField graph = rows[0].Body.Graph;
            Assert.That(graph.Player, Is.SameAs(linked));
            graph.Fold.value = true;
            yield return InspectorTesting.Wait(0.4);
            Assert.That(graph.Model.Start, Is.EqualTo(0.5).Within(1e-6), "the player's start delay");
            Assert.That(graph.Model.To - graph.Model.Start, Is.EqualTo(0.6).Within(1e-6), "×2");
            Assert.That(editor.List.FoldHost, Is.SameAs(preset), "the folds keyed by the preset");
            Assert.That(FieldHost(rows[0].Body.Q<TargetRefField>()), Is.Null, "the target field without a host");
        }

        // T7 — another player: the strip and the Graph Preview built again on it — in its place, the open items' Graphs moved to
        // it, the switch, the folds and the selection kept; remembered for the next inspector; a cleared field stays cleared
        [UnityTest]
        public IEnumerator PG07_AnotherPlayer()
        {
            FeedbackPreset preset = m_Fx.Preset("PG07", Punch(0.3f), Punch(0.2f));
            FeedbackPlayer a = m_Fx.Player("PG07a");
            PresetOps.Link(a, preset);
            FeedbackPlayer b = m_Fx.Player("PG07b");
            b.m_StartDelay = 0.4f;
            b.m_Variants.Add(preset);
            FeedbackPresetEditor editor = Draw(preset, out VisualElement root);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(editor.Shown, Is.SameAs(a));
            List<ItemElement> rows = InspectorTesting.Rows(root);
            rows[0].Header.Fold.value = true;
            yield return InspectorTesting.Wait(0.2);
            rows[0].Body.Graph.Fold.value = true;
            editor.List.List.SetSelection(1);
            yield return OpenGraph(editor);
            TimelineStrip strip = editor.Strip;
            SlotGraphField slot = editor.SlotGraph;
            editor.GraphPlayer.value = b;
            yield return InspectorTesting.Wait(0.5);
            Assert.That(editor.Shown, Is.SameAs(b));
            Assert.That(editor.Strip, Is.Not.SameAs(strip), "the strip built again");
            Assert.That(editor.SlotGraph, Is.Not.SameAs(slot), "the Graph built again");
            Assert.That(editor.Strip.Total.text, Is.EqualTo("Total 0.7 s"), "b's start delay");
            Assert.That(editor.SlotGraph.Switch.value, Is.True, "the Graph Preview's switch is the preset's");
            AfterTheBottomBar(root, editor.SlotGraph);
            Assert.That(editor.SlotGraph.Model, Is.Not.Null);
            rows = InspectorTesting.Rows(root);
            Assert.That(rows[0].Header.Fold.value, Is.True, "the row's fold kept");
            Assert.That(rows[0].Body.Graph.Player, Is.SameAs(b), "the item's Graph on the new player");
            Assert.That(rows[0].Body.Graph.Fold.value, Is.True);
            Assert.That(rows[0].Body.Graph.Model.Start, Is.EqualTo(0.4).Within(1e-6), "b's start delay");
            Assert.That(editor.List.List.selectedIndex, Is.EqualTo(1), "the selection kept");
            FeedbackPresetEditor again = Draw(preset, out _);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(again.Shown, Is.SameAs(b), "remembered");
            again.GraphPlayer.value = null;
            yield return InspectorTesting.Wait(0.1);
            Assert.That(again.Shown, Is.Null);
            FeedbackPresetEditor cleared = Draw(preset, out _);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(cleared.Shown, Is.Null, "a cleared field stays cleared");
        }

        // T20 — an object's players: the dropdown's 'n · alias' lines (no Empty line), a pick by it, remembered; none for a lone
        // player or no player
        [UnityTest]
        public IEnumerator PG20_TheDropdown()
        {
            FeedbackPreset preset = m_Fx.Preset("PG20", Punch(0.3f));
            GameObject go = m_Fx.Go("PG20");
            var players = new[] { go.AddComponent<FeedbackPlayer>(), go.AddComponent<FeedbackPlayer>(), go.AddComponent<FeedbackPlayer>() };
            players[0].m_ListAlias = "hit";
            players[2].m_ListAlias = "end";
            PresetOps.Link(players[1], preset);
            FeedbackPlayer lone = m_Fx.Player("PG20lone");
            FeedbackPresetEditor editor = Draw(preset, out _);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(editor.Shown, Is.SameAs(players[1]));
            Assert.That(editor.GraphPick.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(editor.GraphPick.choices, Is.EqualTo(new[] { "1 · hit", "2 · " + PlayerChoices.NoAlias, "3 · end" }), "the players' lines — no Empty line");
            Assert.That(editor.GraphPick.value, Is.EqualTo("2 · " + PlayerChoices.NoAlias));
            editor.GraphPick.value = "3 · end";
            yield return InspectorTesting.Wait(0.1);
            Assert.That(editor.Shown, Is.SameAs(players[2]));
            Assert.That(editor.GraphPlayer.value, Is.SameAs(players[2]));
            FeedbackPresetEditor again = Draw(preset, out _);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(again.Shown, Is.SameAs(players[2]), "remembered");
            Assert.That(again.GraphPick.value, Is.EqualTo("3 · end"));
            again.GraphPlayer.value = lone;
            yield return InspectorTesting.Wait(0.1);
            Assert.That(again.GraphPick.style.display.value, Is.EqualTo(DisplayStyle.None), "a lone player");
            again.GraphPlayer.value = null;
            yield return InspectorTesting.Wait(0.1);
            Assert.That(again.GraphPick.style.display.value, Is.EqualTo(DisplayStyle.None), "no player");
        }

        // T21 — a prefab asset's player: taken, its Graph the same computation as its own inspector's; no head, no dots; never
        // the default
        [UnityTest]
        public IEnumerator PG21_APrefabAssetsPlayer()
        {
            FeedbackPreset preset = m_Fx.Preset("PG21", Punch(0.3f), Punch(0.2f, 0.1f));
            var source = new GameObject("PG21");
            new GameObject("Child").transform.SetParent(source.transform);
            var sourcePlayer = source.AddComponent<FeedbackPlayer>();
            sourcePlayer.m_Preset = preset;
            sourcePlayer.m_StartDelay = 0.2f;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, F.Dir + "/PG21.prefab");
            Object.DestroyImmediate(source);
            var assetPlayer = prefab.GetComponent<FeedbackPlayer>();
            FeedbackPresetEditor editor = Draw(preset, out _);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(editor.Shown, Is.Null, "a prefab asset's player is never the default");
            editor.GraphPlayer.value = assetPlayer;
            yield return InspectorTesting.Wait(0.1);
            Assert.That(editor.Shown, Is.SameAs(assetPlayer), "taken");
            Assert.That(editor.Strip.Total.text, Is.EqualTo("Total 0.5 s"), "its start delay");
            yield return OpenGraph(editor);
            SlotGraphModel m = editor.SlotGraph.Model;
            Assert.That(m.Refused, Is.Null);
            Assert.That(m.Ticks, Is.EqualTo(Expected(assetPlayer, preset, editor).Ticks));
            Assert.That(m.Follow, Is.Null);
            Assert.That(editor.SlotGraph.View.Heads.Count, Is.EqualTo(0));
            Assert.That(editor.SlotGraph.Notes.text, Does.Not.Contain("as if"));
            FeedbackPresetEditor again = Draw(preset, out _);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(again.Shown, Is.SameAs(assetPlayer), "remembered");
        }

        // T22 — a saved scene's prefab instance picked, play entered and left with the reload off: the inspector finds the play
        // object (its id folded) and, picked in play, the edit object after (unfolded)
        [UnityTest]
        public IEnumerator PG22a_AcrossPlayReloadOff()
        {
            K.SetOptions(EnterPlayModeOptions.DisableDomainReload);
            Scene s = K.NewScene();
            FeedbackPreset preset = K.Preset(K.PresetPath, K.Tween(0.5f));
            FeedbackPlayer plain = K.Player("PG22Plain");
            PresetOps.Link(plain, preset);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(K.Prefab("PG22Prefab"), s);
            instance.name = "PG22I";
            EditorSceneManager.SaveScene(s);
            var editor = (FeedbackPresetEditor)UnityEditor.Editor.CreateEditor(preset);
            m_Fx.Draw(editor);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(editor.Shown, Is.SameAs(plain), "the link by default");
            editor.GraphPlayer.value = instance.GetComponent<FeedbackPlayer>();
            yield return InspectorTesting.Wait(0.1);
            Assert.That(editor.Shown.gameObject, Is.SameAs(instance));
            yield return new EnterPlayMode();
            yield return InspectorTesting.Wait(0.6);
            FeedbackPlayer inPlay = K.Find("PG22I");
            Assert.That(inPlay, Is.Not.Null);
            Assert.That(editor.Shown, Is.SameAs(inPlay), "the play object of the picked instance");
            Assert.That(PresetGraphPlayers.Resolve(preset), Is.SameAs(inPlay));
            editor.GraphPlayer.value = K.Find("PG22Plain");
            yield return InspectorTesting.Wait(0.1);
            editor.GraphPlayer.value = inPlay;
            yield return InspectorTesting.Wait(0.1);
            yield return new ExitPlayMode();
            yield return InspectorTesting.Wait(0.6);
            FeedbackPlayer edit = K.InScene("PG22I");
            Assert.That(edit, Is.Not.Null);
            Assert.That(editor.Shown, Is.SameAs(edit), "picked in play, the edit object after");
        }

        // T22 — with the reload on: the memory (SessionState) finds the play object, then the edit object
        [UnityTest]
        public IEnumerator PG22b_AcrossPlayReloadOn()
        {
            K.SetOptions(EnterPlayModeOptions.None);
            Scene s = K.NewScene();
            FeedbackPreset made = K.Preset(K.PresetPath, K.Tween(0.5f));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(K.Prefab("PG22bPrefab"), s);
            instance.name = "PG22bI";
            EditorSceneManager.SaveScene(s);
            PresetGraphPlayers.Remember(made, instance.GetComponent<FeedbackPlayer>());
            yield return new EnterPlayMode();
            var preset = AssetDatabase.LoadAssetAtPath<FeedbackPreset>(K.PresetPath);
            FeedbackPlayer inPlay = K.Find("PG22bI");
            Assert.That(inPlay, Is.Not.Null);
            Assert.That(PresetGraphPlayers.Resolve(preset), Is.SameAs(inPlay), "the play object");
            yield return new ExitPlayMode();
            preset = AssetDatabase.LoadAssetAtPath<FeedbackPreset>(K.PresetPath);
            Assert.That(PresetGraphPlayers.Resolve(preset), Is.SameAs(K.InScene("PG22bI")), "the edit object");
        }

        // T22 — an unsaved scene's player (its GlobalObjectId names no object): found again in the session by its entity id;
        // deleted, the inspector goes back to the default
        [UnityTest]
        public IEnumerator PG22c_AnUnsavedScenesPlayer()
        {
            FeedbackPreset preset = m_Fx.Preset("PG22c", Punch(0.3f));
            FeedbackPlayer linked = m_Fx.Player("PG22cl");
            PresetOps.Link(linked, preset);
            FeedbackPlayer unsaved = m_Fx.Player("PG22cu");
            Assert.That(KeepPlayModeChanges.NoId(KeepPlayModeChanges.Id(unsaved)), Is.True, "an unsaved scene's object");
            FeedbackPresetEditor editor = Draw(preset, out _);
            yield return InspectorTesting.Wait(0.3);
            editor.GraphPlayer.value = unsaved;
            yield return InspectorTesting.Wait(0.1);
            FeedbackPresetEditor again = Draw(preset, out _);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(again.Shown, Is.SameAs(unsaved), "found again");
            Object.DestroyImmediate(unsaved.gameObject);
            yield return InspectorTesting.Wait(0.6);
            Assert.That(again.Shown, Is.SameAs(linked), "the default after a delete");
        }
    }
}
