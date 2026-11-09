using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;
using F = xpTURN.Feedback.Tests.Editor.PresetFixtures;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // copy · save · apply · the hierarchy drop · the linker
    internal sealed class PresetOpsTests
    {
        F m_Fx;

        [SetUp]
        public void SetUp() => m_Fx = new F();

        [TearDown]
        public void TearDown()
        {
            DragAndDrop.PrepareStartDrag();
            m_Fx.Clean();
        }

        static string Mode(TargetItem t) => t.m_Target.m_Mode + (t.m_Target.m_Mode == TargetRefMode.Child ? "(" + t.m_Target.m_Path + ")" : "") + (t.m_Target.m_Object != null ? "+object" : "");

        // a preset dropped on a list: deep copies at the insert place, one undo; a read-only list and the preset itself refuse
        [Test]
        public void PH06a_APresetDroppedOnAListIsADeepCopy()
        {
            FeedbackPreset source = m_Fx.Preset("PH06a", F.Tween(0.2f, "s0"), F.Tween(0.3f, "s1"));
            FeedbackPlayer p = m_Fx.Player("PH06a", F.Tween(1f, "own"));
            var list = new ItemListView(new SerializedObject(p), ItemListEdits.ItemsPath, p, null);
            Undo.ClearAll();
            list.DropPresets(new List<FeedbackPreset> { source });
            Assert.That(p.m_Items.Select(i => i.m_Label), Is.EqualTo(new[] { "own", "s0", "s1" }));
            Assert.That(p.m_Items[1], Is.Not.SameAs(source.m_Items[0]));
            source.m_Items[0].m_Label = "edited";
            Assert.That(p.m_Items[1].m_Label, Is.EqualTo("s0"), "the copy stays");
            Undo.PerformUndo();
            Assert.That(p.m_Items.Count, Is.EqualTo(1), "one undo");
            var self = new ItemListView(new SerializedObject(source), ItemListEdits.ItemsPath, null, null);
            self.DropPresets(new List<FeedbackPreset> { source });
            Assert.That(source.m_Items.Count, Is.EqualTo(2), "a preset on itself");
            FeedbackPreset other = m_Fx.Preset("PH06aOther", F.Tween(0.4f));
            var readOnly = new ItemListView(new SerializedObject(other), ItemListEdits.ItemsPath, p, null, true);
            readOnly.DropPresets(new List<FeedbackPreset> { source });
            Assert.That(other.m_Items.Count, Is.EqualTo(1), "a read-only list");
        }

        // save: deep, the Direct targets on the player · under it · on its parent made Self · Child · Parent, the others and a
        // listener's scene target cleared (counted · said), the same after a reload, not an undo step; the player's own items
        // untouched
        [Test]
        public void PH06b_SaveMakesTheTargetsRelative()
        {
            GameObject parent = m_Fx.Go("PH06bParent");
            GameObject other = m_Fx.Go("PH06bOther");
            FeedbackPlayer p = m_Fx.Player("PH06b");
            p.transform.SetParent(parent.transform, false);
            m_Fx.Made.Remove(p.gameObject);
            var child = new GameObject("c1");
            child.transform.SetParent(p.transform, false);
            var ev = new UnityEventItem();
            UnityEventTools.AddBoolPersistentListener(ev.m_Event, other.SetActive, true);
            p.m_Items.Add(F.Direct(p.transform, "self"));
            p.m_Items.Add(F.Direct(child.transform, "child"));
            p.m_Items.Add(F.Direct(parent.transform, "parent"));
            p.m_Items.Add(F.Direct(other.transform, "other"));
            p.m_Items.Add(ev);
            string path = F.Dir + "/PH06b.asset";
            LogAssert.Expect(LogType.Warning, new Regex("^" + Regex.Escape(PresetOps.ClearedWarning) + "2$"));
            FeedbackPreset saved = PresetOps.SaveAs(p, path, out int cleared);
            Assert.That(cleared, Is.EqualTo(2));
            Assert.That(((TargetItem)p.m_Items[0]).m_Target.m_Mode, Is.EqualTo(TargetRefMode.Direct), "the player's own items");
            Assert.That(saved.m_Items[0], Is.Not.SameAs(p.m_Items[0]));
            Undo.PerformUndo();
            Assert.That(saved.m_Items.Count, Is.EqualTo(5), "saving is not an undo step");
            Resources.UnloadAsset(saved);
            var fresh = AssetDatabase.LoadAssetAtPath<FeedbackPreset>(path);
            Assert.That(fresh.m_Items.OfType<TargetItem>().Select(Mode), Is.EqualTo(new[] { "Self", "Child(c1)", "Parent", "Direct" }));
            Assert.That(((UnityEventItem)fresh.m_Items[4]).m_Event.GetPersistentTarget(0) == null, Is.True, "the listener's scene target");
        }

        // apply writes the player's items over the preset in one undo; the preset the player links is refused
        [Test]
        public void PH06c_ApplyOverwritesInOneUndo()
        {
            FeedbackPreset preset = m_Fx.Preset("PH06c", F.Tween(0.2f, "a"), F.Tween(0.3f, "b"));
            FeedbackPlayer q = m_Fx.Player("PH06c", F.Tween(0.4f, "q"));
            Undo.ClearAll();
            Assert.That(PresetOps.ApplyTo(q, preset, out _), Is.True);
            Assert.That(preset.m_Items.Select(i => i.m_Label), Is.EqualTo(new[] { "q" }));
            Assert.That(preset.m_Items[0], Is.Not.SameAs(q.m_Items[0]));
            Undo.PerformUndo();
            Assert.That(preset.m_Items.Select(i => i.m_Label), Is.EqualTo(new[] { "a", "b" }), "one undo");
            PresetOps.Link(q, preset);
            LogAssert.Expect(LogType.Warning, PresetOps.SameText);
            Assert.That(PresetOps.ApplyTo(q, preset, out _), Is.False);
        }

        // a list with missing item types is not copied: their data would be lost
        [Test]
        public void PH06d_AMissingTypeIsNotCopied()
        {
            var fx = new ValidationFixtures();
            try
            {
                FeedbackPlayer missing = fx.MissingScenePlayer();
                FeedbackPreset preset = m_Fx.Preset("PH06d", F.Tween(0.2f));
                LogAssert.Expect(LogType.Warning, PresetOps.MissingText);
                Assert.That(PresetOps.SaveAs(missing, F.Dir + "/PH06dSaved.asset", out _), Is.Null);
                LogAssert.Expect(LogType.Warning, PresetOps.MissingText);
                Assert.That(PresetOps.ApplyTo(missing, preset, out _), Is.False);
                Assert.That(preset.m_Items.Count, Is.EqualTo(1));
                missing.m_Preset = preset;
                LogAssert.Expect(LogType.Warning, PresetOps.MissingText);
                Assert.That(PresetOps.Unlink(missing, true), Is.False, "its own missing data would be dropped");
                Assert.That(missing.m_Preset, Is.SameAs(preset));
            }
            finally
            {
                fx.Clean();
            }
        }

        DragAndDropVisualMode Drop(Object[] dragged, GameObject on, HierarchyDropFlags flags = HierarchyDropFlags.DropUpon, bool perform = true)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = dragged;
#if UNITY_6000_3_OR_NEWER
            return PresetDrop.OnHierarchyDrop(on != null ? on.GetEntityId() : EntityId.None, flags, null, perform);
#else
            return PresetDrop.OnHierarchyDrop(on != null ? on.GetEntityId() : 0, flags, null, perform);
#endif
        }

        // on an object: a player and the copies, chosen, in one undo; on a player: added at its end; on a linked player: refused,
        // nothing changed
        [Test]
        public void PH07a_DropOnAnObjectAPlayerOrALinkedPlayer()
        {
            FeedbackPreset a = m_Fx.Preset("PH07a", F.Tween(0.2f), F.Tween(0.3f));
            FeedbackPreset b = m_Fx.Preset("PH07aB", F.Tween(0.4f));
            GameObject bare = m_Fx.Go("PH07aBare");
            Assert.That(Drop(new Object[] { a }, bare, perform: false), Is.EqualTo(DragAndDropVisualMode.Copy));
            Assert.That(bare.GetComponent<FeedbackPlayer>(), Is.Null, "hovering changes nothing");
            Undo.ClearAll();
            Assert.That(Drop(new Object[] { a }, bare), Is.EqualTo(DragAndDropVisualMode.Copy));
            Assert.That(bare.GetComponent<FeedbackPlayer>().m_Items.Count, Is.EqualTo(2));
            Assert.That(Selection.activeGameObject, Is.SameAs(bare));
            Undo.PerformUndo();
            Assert.That(bare.GetComponent<FeedbackPlayer>(), Is.Null, "one undo — the player too");
            FeedbackPlayer has = m_Fx.Player("PH07aHas", F.Tween(1f));
            Drop(new Object[] { a }, has.gameObject);
            Assert.That(has.m_Items.Count, Is.EqualTo(3));
            FeedbackPlayer linked = m_Fx.Player("PH07aLinked", F.Tween(1f));
            PresetOps.Link(linked, b);
            LogAssert.Expect(LogType.Warning, new Regex("^" + Regex.Escape(PresetDrop.LinkedWarning)));
            Assert.That(Drop(new Object[] { a }, linked.gameObject), Is.EqualTo(DragAndDropVisualMode.Rejected));
            Assert.That(linked.m_Items.Count, Is.EqualTo(1));
            Assert.That(b.m_Items.Count, Is.EqualTo(1));
            Selection.activeObject = null;
        }

        // not on an object (nothing, between): a new object named after the preset, in one undo; several presets copied in
        // order; the link way; a drag of no preset passes on
        [Test]
        public void PH07b_DropElsewhereSeveralTheLinkWayAndOthers()
        {
            FeedbackPreset a = m_Fx.Preset("PH07b", F.Tween(0.2f), F.Tween(0.3f));
            FeedbackPreset b = m_Fx.Preset("PH07bB", F.Tween(0.4f));
            GameObject near = m_Fx.Go("PH07bNear");
            Undo.ClearAll();
            Drop(new Object[] { a }, null, HierarchyDropFlags.None);
            GameObject made = GameObject.Find("PH07b");
            Assert.That(made, Is.Not.Null);
            Assert.That(made.GetComponent<FeedbackPlayer>().m_Items.Count, Is.EqualTo(2));
            Undo.PerformUndo();
            Assert.That(made == null, Is.True, "one undo — the new object");
            Drop(new Object[] { a }, near, HierarchyDropFlags.DropBetween);
            GameObject between = GameObject.Find("PH07b");
            m_Fx.Made.Add(between);
            Assert.That(between, Is.Not.Null);
            Assert.That(near.GetComponent<FeedbackPlayer>(), Is.Null, "between is not on");
            GameObject two = m_Fx.Go("PH07bTwo");
            Drop(new Object[] { a, b }, two);
            Assert.That(two.GetComponent<FeedbackPlayer>().m_Items.Select(i => i.m_Timing.m_Duration), Is.EqualTo(new[] { 0.2f, 0.3f, 0.4f }));
            bool links = PresetDrop.Links;
            PresetDrop.Links = true;
            try
            {
                GameObject target = m_Fx.Go("PH07bLink");
                Assert.That(Drop(new Object[] { b }, target), Is.EqualTo(DragAndDropVisualMode.Link));
                Assert.That(target.GetComponent<FeedbackPlayer>().m_Preset, Is.SameAs(b));
                Assert.That(target.GetComponent<FeedbackPlayer>().m_Items, Is.Empty);
            }
            finally
            {
                PresetDrop.Links = links;
            }
            Assert.That(Drop(new Object[] { near }, near), Is.EqualTo(DragAndDropVisualMode.None));
            Selection.activeObject = null;
        }

        // the arguments the hierarchy passes: between items the new object goes where the line is — under the target's parent
        // after it, before it above, the parent's first place after the parent; one undo
        [Test]
        public void PH07c_DropBetweenFollowsTheLine()
        {
            FeedbackPreset a = m_Fx.Preset("PH07c", F.Tween(0.2f));
            GameObject parent = m_Fx.Go("PH07cParent");
            var children = new GameObject[3];
            for (int i = 0; i < children.Length; i++)
            {
                children[i] = new GameObject("PH07cChild" + i);
                children[i].transform.SetParent(parent.transform, false);
            }
            var cases = new (HierarchyDropFlags flags, GameObject near, int index)[]
            {
                (HierarchyDropFlags.DropBetween, children[1], 2),
                (HierarchyDropFlags.DropBetween | HierarchyDropFlags.DropAbove, children[1], 1),
                (HierarchyDropFlags.DropBetween | HierarchyDropFlags.DropAfterParent, children[2], 0),
            };
            foreach (var (flags, near, index) in cases)
            {
                Undo.ClearAll();
                Assert.That(Drop(new Object[] { a }, near, flags, false), Is.EqualTo(DragAndDropVisualMode.Copy), flags + " — hovering");
                Assert.That(Drop(new Object[] { a }, near, flags), Is.EqualTo(DragAndDropVisualMode.Copy), flags.ToString());
                Transform made = parent.transform.Find("PH07c");
                Assert.That(made, Is.Not.Null, flags + " — under the target's parent");
                Assert.That(made.GetSiblingIndex(), Is.EqualTo(index), flags + " — at the line");
                Assert.That(made.GetComponent<FeedbackPlayer>().m_Items.Count, Is.EqualTo(1), flags + " — the copy");
                Undo.PerformUndo();
                Assert.That(parent.transform.Find("PH07c"), Is.Null, flags + " — one undo");
                Assert.That(parent.transform.childCount, Is.EqualTo(3), flags + " — the children");
            }
            Selection.activeObject = null;
        }

        // between top objects of a scene that is not active: in that scene, after the target; while
        // searching: refused, as the hierarchy's own drop; in a prefab stage between its root (no parent) the root takes it
        [Test]
        public void PH07d_DropBetweenAnotherSceneSearchingAndAStage()
        {
            FeedbackPreset a = m_Fx.Preset("PH07d", F.Tween(0.2f));
            Scene active = SceneManager.GetActiveScene();
            string scenePath = F.Dir + "/PH07d.unity";
            string prefabPath = F.Dir + "/PH07d.prefab";
            File.WriteAllText(Path.GetFullPath(scenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceUpdate);
            Scene other = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var first = new GameObject("PH07dFirst");
                SceneManager.MoveGameObjectToScene(first, other);
                var second = new GameObject("PH07dSecond");
                SceneManager.MoveGameObjectToScene(second, other);
                Assert.That(SceneManager.GetActiveScene(), Is.Not.EqualTo(other), "the target's scene is not the active one");
                Undo.ClearAll();
                Drop(new Object[] { a }, first, HierarchyDropFlags.DropBetween);
                GameObject made = other.GetRootGameObjects().FirstOrDefault(g => g.name == "PH07d");
                Assert.That(made, Is.Not.Null, "in the target's scene");
                Assert.That(made.transform.GetSiblingIndex(), Is.EqualTo(first.transform.GetSiblingIndex() + 1), "after the target");
                Undo.PerformUndo();
                Assert.That(other.GetRootGameObjects().Any(g => g.name == "PH07d"), Is.False, "one undo");

                const HierarchyDropFlags searching = HierarchyDropFlags.DropBetween | HierarchyDropFlags.SearchActive;
                Assert.That(Drop(new Object[] { a }, first, searching, false), Is.EqualTo(DragAndDropVisualMode.Rejected), "searching — hovering");
                Assert.That(Drop(new Object[] { a }, first, searching), Is.EqualTo(DragAndDropVisualMode.Rejected), "searching");
                Assert.That(GameObject.Find("PH07d") == null && other.GetRootGameObjects().All(g => g.name != "PH07d"), Is.True, "searching — nothing made");

                var root = new GameObject("PH07dRoot");
                new GameObject("PH07dChild").transform.SetParent(root.transform, false);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Object.DestroyImmediate(root);
                PrefabStage stage = PrefabStageUtility.OpenPrefab(prefabPath);
                GameObject stageRoot = stage.prefabContentsRoot;
                Drop(new Object[] { a }, stageRoot, HierarchyDropFlags.DropBetween);
                Transform underRoot = stageRoot.transform.Find("PH07d");
                Assert.That(underRoot, Is.Not.Null, "between the stage's root — under the root");
                Assert.That(underRoot.GetSiblingIndex(), Is.EqualTo(stageRoot.transform.childCount - 1), "at its end");
                Drop(new Object[] { a }, stageRoot.transform.Find("PH07dChild").gameObject, HierarchyDropFlags.DropBetween | HierarchyDropFlags.DropAbove);
                Assert.That(stageRoot.transform.GetChild(0).name, Is.EqualTo("PH07d"), "above the stage's child");
            }
            finally
            {
                StageUtility.GoToMainStage();
                if (active.IsValid())
                    SceneManager.SetActiveScene(active);
                EditorSceneManager.CloseScene(other, true);
                AssetDatabase.DeleteAsset(prefabPath);
                Selection.activeObject = null;
            }
        }

        // the linker follows the link, once a preset; a preset no player links is not read
        [Test]
        public void PH10a_TheLinkerFollowsTheLink()
        {
            GameObject target = m_Fx.Go("PH10aTarget");
            var ev = new UnityEventItem();
            UnityEventTools.AddBoolPersistentListener(ev.m_Event, target.SetActive, true);
            FeedbackPreset preset = m_Fx.Preset("PH10a", ev);
            var lone = new UnityEventItem();
            UnityEventTools.AddVoidPersistentListener(lone.m_Event, target.transform.DetachChildren);
            m_Fx.Preset("PH10aLone", lone);
            FeedbackPlayer a = m_Fx.Player("PH10aA");
            FeedbackPlayer b = m_Fx.Player("PH10aB");
            PresetOps.Link(a, preset);
            PresetOps.Link(b, preset);
            var read = (HashSet<string>)typeof(UnityEventLinkerProcessor).GetField("s_ReadAssets", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            read.Clear();
            var into = new HashSet<string>();
            UnityEventLinkerProcessor.Collect(new SerializedObject(a), into);
            UnityEventLinkerProcessor.Collect(new SerializedObject(b), into);
            read.Clear();
            Assert.That(into.Any(m => m.EndsWith("|UnityEngine.GameObject|SetActive", System.StringComparison.Ordinal)), Is.True, string.Join(" ", into));
            Assert.That(into.Any(m => m.EndsWith("|DetachChildren", System.StringComparison.Ordinal)), Is.False, "a preset no player links");
        }
    }
}
