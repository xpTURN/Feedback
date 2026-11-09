using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // A10 and G11 ~ G13 speak in their cases only
    public sealed class TransformRulesTests
    {
        const string PrefabPath = "Assets/TransformRulesPrefab.prefab";

        readonly List<Object> m_Made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            AssetDatabase.DeleteAsset(PrefabPath);
        }

        GameObject Go(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };

        static T On<T>(T item, Object target, FeedbackMotion motion) where T : TargetItem
        {
            item.m_Target = Direct(target);
            item.m_Motion = motion;
            return item;
        }

        [Test]
        public void TQ15_ASecondReferenceAndGroupG()
        {
            var root = new GameObject("PrefabRoot");
            new GameObject("PrefabChild").transform.SetParent(root.transform, false);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            m_Made.Add(instance);
            Transform prefabChild = instance.transform.Find("PrefabChild");

            GameObject t = Go("T");
            GameObject below = Go("Below");
            below.transform.SetParent(t.transform, false);
            GameObject other = Go("Other");
            var look = On(new LookAtItem(), t, new SetMotion());
            var lookDirection = On(new LookAtItem { m_Mode = LookAtMode.Direction }, t, new SetMotion());
            var lookSelf = On(new LookAtItem(), t, new SetMotion());
            lookSelf.m_LookTarget = Direct(t.transform);
            var aroundSelf = On(new RotateAroundItem(), t, new TweenMotion());
            aroundSelf.m_Pivot = Direct(t);
            var around = On(new RotateAroundItem(), t, new TweenMotion());
            around.m_Pivot = Direct(other.transform);
            var matchChannel = On(new MatchTransformItem(), t, new TweenMotion());
            matchChannel.m_Destination = new TargetRef { m_Mode = TargetRefMode.Channel, m_Path = "", m_Anchor = "" };
            var matchSelf = On(new MatchTransformItem(), t, new TweenMotion());
            matchSelf.m_Destination = Direct(t.transform);
            var toRoot = On(new SetParentItem(), t, new SetMotion());
            var toBelow = On(new SetParentItem(), t, new SetMotion());
            toBelow.m_NewParent = Direct(below.transform);
            var toSelf = On(new SetParentItem(), t, new SetMotion());
            toSelf.m_NewParent = Direct(t);
            var child = On(new SetParentItem(), prefabChild, new SetMotion());
            child.m_NewParent = Direct(other.transform);
            var instanceRoot = On(new SetParentItem(), instance.transform, new SetMotion());
            instanceRoot.m_NewParent = Direct(other.transform);
            var offTarget = On(new SetParentItem(), null, new SetMotion());
            offTarget.m_NewParent = Direct(below.transform);

            var host = Go("Player").AddComponent<FeedbackPlayer>();
            host.m_Items.AddRange(new FeedbackItem[]
            {
                look,           // 0 A10 — Look Target: no target object
                lookDirection,  // 1 — not read in Direction mode
                lookSelf,       // 2 G11
                aroundSelf,     // 3 G11
                around,         // 4
                matchChannel,   // 5 A10 — a channel is no reference
                matchSelf,      // 6 G11
                toRoot,         // 7 — an empty new parent is the root
                toBelow,        // 8 G12
                toSelf,         // 9 G12
                child,          // 10 G13
                instanceRoot,   // 11 — an instance's root moves in edit mode
                offTarget,      // 12 — no target: group A's
            });
            var want = new Dictionary<string, (int[] Items, RuleSeverity Severity)>
            {
                { "FB-A10", (new[] { 0, 5 }, RuleSeverity.Warning) },
                { "FB-G11", (new[] { 2, 3, 6 }, RuleSeverity.Info) },
                { "FB-G12", (new[] { 8, 9 }, RuleSeverity.Error) },
                { "FB-G13", (new[] { 10 }, RuleSeverity.Info) },
            };
            foreach (KeyValuePair<string, (int[] Items, RuleSeverity Severity)> w in want)
            {
                List<RuleResult> got = F.Of(host, w.Key);
                Assert.That(got.Select(r => r.Item).OrderBy(i => i), Is.EqualTo(w.Value.Items), w.Key);
                Assert.That(got.Select(r => r.Severity).Distinct(), Is.EqualTo(new[] { w.Value.Severity }), w.Key);
                Assert.That(got.All(r => r.Fixes.Count == 0), Is.True, w.Key + " has no fix");
            }
            Assert.That(F.Of(host, "FB-A10").Select(r => r.Message), Is.EqualTo(new[]
            {
                "Look Target: no target object — the item is skipped",
                "Destination: channel targets need a receiver — the item is skipped",
            }));
        }
    }
}
