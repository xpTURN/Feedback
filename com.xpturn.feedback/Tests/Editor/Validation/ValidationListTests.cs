using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // B — the list · the inspector's lock
    internal sealed class ValidationListTests
    {
        F m_Fx;

        [SetUp]
        public void SetUp() => m_Fx = new F();

        [TearDown]
        public void TearDown() => m_Fx.Clean();

        DataModelTestHolder Holder(bool missing)
        {
            var holder = ScriptableObject.CreateInstance<DataModelTestHolder>();
            holder.items.Add(new WaitItem());
            string path = F.Dir + "/Holder" + missing + ".asset";
            AssetDatabase.CreateAsset(holder, path);
            AssetDatabase.SaveAssets();
            if (missing)
                F.Rename(path);
            return AssetDatabase.LoadAssetAtPath<DataModelTestHolder>(path);
        }

        // the places a list lives: a scene object, a prefab asset, its instance, a prefab stage, a ScriptableObject
        [Test]
        public void VT04a_AMissingTypeInEveryPlace()
        {
            FeedbackPlayer scene = m_Fx.MissingScenePlayer();
            GameObject asset = m_Fx.MissingPrefab("VT04a");
            FeedbackPlayer instance = m_Fx.Instance(asset);
            foreach (FeedbackPlayer p in new[] { scene, asset.GetComponent<FeedbackPlayer>(), instance })
            {
                RuleResult r = F.Of(p, "FB-B01").Single();
                Assert.That(r.Item, Is.EqualTo(0), p.name);
                Assert.That(r.Severity, Is.EqualTo(RuleSeverity.Error));
                Assert.That(r.Fixes[0].Undoable, Is.False, "clearing is not undoable");
                Assert.That(F.Of(p, "FB-B02"), Is.Empty, "not an empty slot");
            }
            Assert.That(MissingTypes.Has(Holder(true)), Is.True, "a ScriptableObject asset");
            Assert.That(MissingTypes.Has(Holder(false)), Is.False);
            PrefabStage stage = PrefabStageUtility.OpenPrefab(AssetDatabase.GetAssetPath(asset));
            Assert.That(MissingTypes.Has(stage.prefabContentsRoot.GetComponent<FeedbackPlayer>()), Is.True, "a prefab stage");
            StageUtility.GoToMainStage();
        }

        // clearing a scene's entries dirties the scene; no reload offered with several scenes open
        [Test]
        public void VT04b_ClearingAndReopening()
        {
            FeedbackPlayer p = m_Fx.MissingScenePlayer();
            RuleResult r = F.Of(p, "FB-B01").Single();
            Assert.That(SceneManager.sceneCount, Is.GreaterThan(1));
            Assert.That(r.Fixes.Select(f => f.Name), Is.EqualTo(new[] { "Clear missing entries" }), "no reload with several scenes");
            Assert.That(r.Fixes[0].Label, Does.EndWith(RuleFix.NotUndoable));
            Assert.That(p.gameObject.scene.isDirty, Is.False);
            FeedbackValidator.Apply(r, r.Fixes[0]);
            Assert.That(MissingTypes.Has(p), Is.False);
            Assert.That(p.gameObject.scene.isDirty, Is.True, "dirty — the save keeps the clear");
            Assert.That(F.Of(p, "FB-B02").Select(x => x.Item), Is.EqualTo(new[] { 0 }), "the slot is empty now");
            // a prefab asset — in its copy, saved
            GameObject asset = m_Fx.MissingPrefab("VT04b");
            FeedbackPlayer instance = m_Fx.Instance(asset);
            r = F.Of(instance, "FB-B01").Single();
            FeedbackValidator.Apply(r, r.Fixes[0]);
            Assert.That(MissingTypes.Has(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(asset)).GetComponent<FeedbackPlayer>()), Is.False);
        }

        [Test]
        public void VT04c_EmptyAndSharedSlots()
        {
            PositionItem a = F.Position();
            FeedbackPlayer p = m_Fx.Player("VT04c", a, null, a);
            RuleResult empty = F.Of(p, "FB-B02").Single();
            RuleResult shared = F.Of(p, "FB-B03").Single();
            Assert.That(empty.Item, Is.EqualTo(1));
            Assert.That(shared.Item, Is.EqualTo(2));
            FeedbackValidator.Apply(shared, shared.Fixes.Single());
            Assert.That(p.m_Items[2], Is.Not.SameAs(p.m_Items[0]));
            Assert.That(F.Of(p, "FB-B03"), Is.Empty);
            FeedbackValidator.Apply(empty, empty.Fixes.Single());
            Assert.That(p.m_Items.Count, Is.EqualTo(2));
            Assert.That(F.Of(p, "FB-B02"), Is.Empty);
        }

        // a variant reordered, then the base inserted D: the structure override names the missing item; revert · apply
        [Test]
        public void VT04d_AVariantsListStructure()
        {
            FeedbackPlayer vp = m_Fx.ReorderedVariant(true);
            RuleResult r = F.Of(vp, "FB-B04").Single();
            Assert.That(r.Message, Does.Contain("missing base items: D"));
            Assert.That(F.Of(vp, "FB-B03"), Has.Count.EqualTo(1), "C twice");
            Assert.That(r.Fixes.Select(f => f.Label), Is.EqualTo(new[] { "Revert the list structure", "Apply the list structure to the base" + RuleFix.NotUndoable }));
            FeedbackValidator.Apply(r, r.Fixes[0]);
            vp = AssetDatabase.LoadAssetAtPath<GameObject>(F.Dir + "/Variant.prefab").GetComponent<FeedbackPlayer>();
            Assert.That(vp.m_Items.Select(x => x.m_Label), Is.EqualTo(new[] { "D", "A", "B", "C" }));
            Assert.That(F.Of(vp, "FB-B04"), Is.Empty);
        }

        // an instance's structure applies to its base in place — one undo
        [Test]
        public void VT04e_AnInstancesStructureToItsBase()
        {
            FeedbackPlayer ip = m_Fx.ReorderedInstance();
            RuleResult r = F.Of(ip, "FB-B04").Single();
            RuleFix apply = r.Fixes.Single(f => f.Name.StartsWith("Apply"));
            Assert.That(apply.Undoable, Is.True);
            Undo.ClearAll();
            FeedbackValidator.Apply(r, apply);
            var source = (FeedbackPlayer)PrefabUtility.GetCorrespondingObjectFromSource(ip);
            Assert.That(source.m_Items.Select(x => x.m_Label), Is.EqualTo(new[] { "C", "A", "B" }));
            Assert.That(F.Of(ip, "FB-B04"), Is.Empty);
            Undo.PerformUndo();
            Assert.That(source.m_Items.Select(x => x.m_Label), Is.EqualTo(new[] { "A", "B", "C" }), "one undo");
        }

        // the old managed-reference path is unused; an item field's override is not
        [Test]
        public void VT04f_UnusedOverrides()
        {
            FeedbackPlayer quiet = m_Fx.OverriddenInstance(false);
            Assert.That(F.Of(quiet, "FB-B05"), Is.Empty, "an item field's override resolves");
            m_Fx.Clean();
            m_Fx = new F();
            FeedbackPlayer ip = m_Fx.OverriddenInstance(true);
            var so = new SerializedObject(ip);
            so.FindProperty("m_Items.Array.data[0].m_Label").stringValue = "kept";
            so.ApplyModifiedPropertiesWithoutUndo();
            RuleResult r = F.Of(ip, "FB-B05").Single();
            Assert.That(r.Message, Does.Contain("m_OldLabel"));
            Assert.That(r.Message, Does.StartWith("Unity cannot restore these values"));
            FeedbackValidator.Apply(r, r.Fixes.Single());
            Assert.That(F.Of(ip, "FB-B05"), Is.Empty);
            Assert.That(ip.m_Items[0].m_Label, Is.EqualTo("kept"), "a valid override stays");
            // a variant asset — in its copy
            FeedbackPlayer vp = m_Fx.UnusedVariant();
            r = F.Of(vp, "FB-B05").Single();
            Assert.That(r.Fixes.Single().Undoable, Is.False);
            FeedbackValidator.Apply(r, r.Fixes.Single());
            vp = AssetDatabase.LoadAssetAtPath<GameObject>(F.Dir + "/UnusedVariant.prefab").GetComponent<FeedbackPlayer>();
            Assert.That(F.Of(vp, "FB-B05"), Is.Empty);
        }

        // a motionless item's fix keeps its length; an unregistered type
        [Test]
        public void VT04g_MotionlessAndUnregistered()
        {
            var item = new PositionItem();
            item.m_Timing.m_Duration = 0.37f;
            FeedbackPlayer p = m_Fx.Player("VT04g", item, new PlayerTestUnregistered());
            RuleResult r = F.Of(p, "FB-B06").Single();
            Assert.That(r.Fixes.Single().Name, Is.EqualTo("Add a Tween motion"));
            FeedbackValidator.Apply(r, r.Fixes.Single());
            Assert.That(((TargetItem)p.m_Items[0]).m_Motion, Is.InstanceOf<TweenMotion>());
            Assert.That(p.m_Items[0].m_Timing.m_Duration, Is.EqualTo(0.37f), "the length kept");
            Assert.That(F.Of(p, "FB-B07").Select(x => x.Item), Is.EqualTo(new[] { 1 }));
        }

        // the inspector's lock is the one function: an instance locked, a ScriptableObject does not throw
        [Test]
        public void VT08a_TheLockIsTheMissingCheck()
        {
            GameObject asset = m_Fx.MissingPrefab("VT08a");
            FeedbackPlayer instance = m_Fx.Instance(asset);
            Assert.That(ItemListEdits.Locked(asset.GetComponent<FeedbackPlayer>()), Is.True, "the prefab asset");
            Assert.That(ItemListEdits.Locked(instance), Is.True, "its scene instance");
            Assert.That(ItemListEdits.Locked(m_Fx.MissingScenePlayer()), Is.True, "a scene object");
            DataModelTestHolder clean = Holder(false);
            Assert.DoesNotThrow(() => ItemListEdits.Locked(clean));
            Assert.That(ItemListEdits.Locked(clean), Is.False);
            Assert.That(ItemListEdits.Locked(Holder(true)), Is.True);
        }
    }
}
