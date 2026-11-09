using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // A — targets
    internal sealed class ValidationTargetTests
    {
        F m_Fx;

        [SetUp]
        public void SetUp() => m_Fx = new F();

        [TearDown]
        public void TearDown() => m_Fx.Clean();

        static string Describe(TargetFailure f) => TargetResolver.Describe(f);

        // each failure reason says its words; a candidate fixes it
        [Test]
        public void VT03a_EachFailureReason()
        {
            var cases = new List<(string Name, FeedbackItem Item, TargetFailure Reason)>
            {
                ("empty Direct", F.Item<PositionItem>(F.Tween, TargetRefMode.Direct), TargetFailure.NoObject),
                ("Self light without a Light", F.Item<LightIntensityItem>(F.Tween), TargetFailure.NoComponent),
                ("no parent", F.Item<PositionItem>(F.Tween, TargetRefMode.Parent), TargetFailure.NoParent),
                ("no child of that path", Child(ChildLookup.Path, "Nope", 0), TargetFailure.NoChild),
                ("a child index out of range", Child(ChildLookup.Index, "", 5), TargetFailure.IndexOutOfRange),
                ("no anchor of that name", Anchor("nope"), TargetFailure.NoAnchor),
            };
            foreach (var (name, item, reason) in cases)
            {
                FeedbackPlayer p = m_Fx.Player("VT03a", item);
                RuleResult r = F.Of(p, "FB-A01").SingleOrDefault();
                Assert.That(r, Is.Not.Null, name);
                Assert.That(r.Message, Does.Contain(Describe(reason)), name);
                Assert.That(r.Item, Is.EqualTo(0), name);
            }
            // a candidate fixes the light
            FeedbackPlayer light = m_Fx.Player("VT03aLight", F.Item<LightIntensityItem>(F.Tween));
            var child = new GameObject("VT03aChild");
            child.transform.SetParent(light.transform);
            child.AddComponent<Light>();
            RuleResult result = F.Of(light, "FB-A01").Single();
            RuleFix pick = result.Fixes.Single(f => f.Name.Contains("VT03aChild"));
            FeedbackValidator.Apply(result, pick);
            Assert.That(F.Of(light, "FB-A01"), Is.Empty, "after the candidate");
            Assert.That(((TargetItem)light.m_Items[0]).m_Target.m_Object, Is.EqualTo(child.GetComponent<Light>()));
            // quiet: the Light on the object
            FeedbackPlayer quiet = m_Fx.Player("VT03aQuiet", F.Item<LightIntensityItem>(F.Tween));
            quiet.gameObject.AddComponent<Light>();
            Assert.That(F.Of(quiet, "FB-A01"), Is.Empty);
        }

        static PositionItem Child(ChildLookup lookup, string path, int index)
        {
            PositionItem t = F.Item<PositionItem>(F.Tween, TargetRefMode.Child);
            t.m_Target.m_ChildLookup = lookup;
            t.m_Target.m_Path = path;
            t.m_Target.m_Index = index;
            return t;
        }

        static PositionItem Anchor(string name)
        {
            PositionItem t = F.Item<PositionItem>(F.Tween, TargetRefMode.Anchor);
            t.m_Target.m_Anchor = name;
            return t;
        }

        // the main camera: the batch test scene has one — untagged for the case
        [Test]
        public void VT03b_NoMainCamera()
        {
            Camera main = Camera.main;
            string tag = main != null ? main.tag : null;
            if (main != null)
                main.tag = "Untagged";
            try
            {
                FeedbackPlayer p = m_Fx.Player("VT03b", F.Item<CameraShakeItem>(new ShakeMotion(), TargetRefMode.MainCamera));
                Assert.That(F.Of(p, "FB-A01").Single().Message, Does.Contain(Describe(TargetFailure.NoMainCamera)));
            }
            finally
            {
                if (main != null)
                    main.tag = tag;
            }
        }

        [Test]
        public void VT03c_AnAssetTarget()
        {
            var src = new GameObject("VT03cSrc");
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(src, F.Dir + "/VT03c.prefab");
            Object.DestroyImmediate(src);
            PositionItem t = F.Item<PositionItem>(F.Tween, TargetRefMode.Direct);
            t.m_Target.m_Object = asset.transform;
            FeedbackPlayer p = m_Fx.Player("VT03c", t);
            p.m_Anchors.Add(new PlayerAnchor { m_Name = "asset", m_Object = asset.transform });
            List<RuleResult> r = F.Of(p, "FB-A02");
            Assert.That(r.Select(x => x.Item), Is.EquivalentTo(new[] { 0, -1 }), "the Direct target and the anchor");
            Assert.That(r.Single(x => x.Item == 0).Message, Is.EqualTo(TargetChecks.AssetText));
            Assert.That(F.Of(p, "FB-A01"), Is.Empty, "the asset is A02's, not A01's");
            FeedbackValidator.Apply(r[0], r[0].Fixes.First(f => f.Name.StartsWith("Clear")));
            FeedbackValidator.Apply(r[1], r[1].Fixes.First(f => f.Name.StartsWith("Clear")));
            Assert.That(F.Of(p, "FB-A02"), Is.Empty, "cleared");
            // quiet: a prefab asset's player on its own child
            var host = new GameObject("VT03cHost");
            var kid = new GameObject("VT03cKid");
            kid.transform.SetParent(host.transform);
            PositionItem own = F.Item<PositionItem>(F.Tween, TargetRefMode.Direct);
            own.m_Target.m_Object = kid.transform;
            host.AddComponent<FeedbackPlayer>().m_Items.Add(own);
            GameObject hostAsset = PrefabUtility.SaveAsPrefabAsset(host, F.Dir + "/VT03cHost.prefab");
            Object.DestroyImmediate(host);
            FeedbackPlayer assetPlayer = hostAsset.GetComponent<FeedbackPlayer>();
            Assert.That(F.Of(assetPlayer, "FB-A02"), Is.Empty);
            Assert.That(F.Of(assetPlayer, "FB-A01"), Is.Empty);
        }

        [Test]
        public void VT03d_AnotherScenesReferences()
        {
            Scene b = m_Fx.Scene("VT03d");
            var there = new GameObject("VT03dThere");
            SceneManager.MoveGameObjectToScene(there, b);
            PositionItem t = F.Item<PositionItem>(F.Tween, TargetRefMode.Direct);
            t.m_Target.m_Object = there.transform;
            var ev = new UnityEventItem();
            UnityEventTools.AddBoolPersistentListener(ev.m_Event, there.SetActive, true);
            FeedbackPlayer p = m_Fx.Player("VT03d", t, ev);
            p.m_Anchors.Add(new PlayerAnchor { m_Name = "there", m_Object = there.transform });
            List<RuleResult> r = F.Of(p, "FB-A03");
            Assert.That(r.Select(x => x.Item), Is.EquivalentTo(new[] { 0, 1, -1 }), "the target, the listener, the anchor");
            Assert.That(r.Single(x => x.Item == 1).Message, Does.Contain("m_Target"));
            foreach (RuleResult x in r)
                FeedbackValidator.Apply(x, x.Fixes.Single());
            Assert.That(F.Of(p, "FB-A03"), Is.Empty, "cleared");
            // quiet: the same scene
            PositionItem here = F.Item<PositionItem>(F.Tween, TargetRefMode.Direct);
            FeedbackPlayer q = m_Fx.Player("VT03dQuiet", here);
            here.m_Target.m_Object = q.transform;
            Assert.That(F.Of(q, "FB-A03"), Is.Empty);
        }

        [Test]
        public void VT03e_AnchorsAndTheGate()
        {
            FeedbackPlayer twice = m_Fx.Player("VT03eTwice");
            twice.m_Anchors.Add(new PlayerAnchor { m_Name = "a", m_Object = twice.transform });
            twice.m_Anchors.Add(new PlayerAnchor { m_Name = "a", m_Object = twice.transform });
            twice.m_Anchors.Add(new PlayerAnchor { m_Name = "b", m_Object = twice.transform });
            Assert.That(F.Of(twice, "FB-A04").Single().Message, Does.Contain("\"a\""));
            FeedbackPlayer gate = m_Fx.Player("VT03eGate");
            gate.m_Anchors.Add(new PlayerAnchor { m_Name = "y", m_Object = gate.transform });
            gate.m_UseDistanceGate = true;
            gate.m_GateReference = DistanceReference.Anchor;
            gate.m_GateAnchor = "x";
            RuleResult r = F.Of(gate, "FB-A05").Single();
            Assert.That(r.Fixes.Select(f => f.Name), Is.EqualTo(new[] { "Use anchor \"y\"", "Measure from the main camera" }));
            FeedbackValidator.Apply(r, r.Fixes[0]);
            Assert.That(gate.m_GateAnchor, Is.EqualTo("y"));
            Assert.That(F.Of(gate, "FB-A05"), Is.Empty);
            gate.m_GateAnchor = "x";
            r = F.Of(gate, "FB-A05").Single();
            FeedbackValidator.Apply(r, r.Fixes[1]);
            Assert.That(gate.m_GateReference, Is.EqualTo(DistanceReference.MainCamera));
            Assert.That(F.Of(gate, "FB-A05"), Is.Empty);
        }

        [Test]
        public void VT03f_Channels()
        {
            FeedbackPlayer position = m_Fx.Player("VT03fPosition", F.Item<PositionItem>(F.Tween, TargetRefMode.Channel));
            RuleResult r = F.Of(position, "FB-A06").Single();
            Assert.That(F.Of(position, "FB-A01"), Is.Empty, "a channel is not A01's");
            FeedbackValidator.Apply(r, r.Fixes.Single());
            Assert.That(((TargetItem)position.m_Items[0]).m_Target.m_Mode, Is.EqualTo(TargetRefMode.Self));
            CameraShakeItem shake = F.Item<CameraShakeItem>(new ShakeMotion(), TargetRefMode.Channel);
            FeedbackPlayer channel = m_Fx.Player("VT03fShake", shake);
            Assert.That(F.Of(channel, "FB-A06"), Is.Empty, "a camera shake receives");
            Assert.That(F.Of(channel, "FB-A07"), Is.Empty);
            shake.m_DirectWrite = true;
            r = F.Of(channel, "FB-A07").Single();
            FeedbackValidator.Apply(r, r.Fixes.Single());
            Assert.That(((TargetItem)channel.m_Items[0]).m_DirectWrite, Is.False);
        }

        // a project rule: in the project's check only
        [Test]
        public void VT03g_AReceiverWithoutAPlayer()
        {
            var lone = new GameObject("VT03gLone");
            m_Fx.Made.Add(lone);
            lone.AddComponent<FeedbackReceiver>();
            FeedbackPlayer owned = m_Fx.Player("VT03gOwned");
            owned.gameObject.AddComponent<FeedbackReceiver>();
            List<RuleResult> r = FeedbackValidator.RunProject().Where(x => x.Id == "FB-A08").ToList();
            Assert.That(r.Select(x => x.Host.name), Is.EqualTo(new[] { "VT03gLone" }));
            Assert.That(F.Of(owned, "FB-A08"), Is.Empty, "not in a host's check");
        }
    }
}
