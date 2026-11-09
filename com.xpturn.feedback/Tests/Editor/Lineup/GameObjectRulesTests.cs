using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // E09 · E10 · J01 ~ J07 speak in their cases only — their severities, their fixes
    public sealed class GameObjectRulesTests
    {
        F m_Fx;
        readonly List<Object> m_Made = new List<Object>();
        readonly List<string> m_Assets = new List<string>();

        [SetUp]
        public void SetUp() => m_Fx = new F();

        [TearDown]
        public void TearDown()
        {
            m_Fx.Clean();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            foreach (string a in m_Assets)
                AssetDatabase.DeleteAsset(a);
            m_Assets.Clear();
        }

        GameObject Go(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        static SetMotion SetTo(float v, bool relative = false) => new SetMotion { m_Params = new SetParams { m_Value = new Vector4(v, 0f, 0f, 0f), m_Relative = relative } };

        static T On<T>(T item, Object target, FeedbackMotion motion = null) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion ?? SetTo(1f);
            return item;
        }

        static T Self<T>(T item, FeedbackMotion motion = null) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion ?? SetTo(1f);
            return item;
        }

        static ComponentEnabledItem Enabled(string type, int index, FeedbackMotion motion = null) => new ComponentEnabledItem { m_ComponentType = type, m_ComponentIndex = index, m_Motion = motion };

        static void Want(Component host, string id, int[] items, RuleSeverity severity)
        {
            List<RuleResult> results = F.Of(host, id);
            Assert.That(results.Select(r => r.Item).OrderBy(i => i), Is.EqualTo(items), id);
            Assert.That(results.All(r => r.Severity == severity), Is.True, id + " " + severity);
        }

        static string Message(Component host, string id, int item) => F.Of(host, id).Single(r => r.Item == item).Message;

        GameObject Prefab(string name)
        {
            var source = new GameObject(name);
            string path = F.Dir + "/" + name + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
            Object.DestroyImmediate(source);
            m_Assets.Add(path);
            return prefab;
        }

        // E09: its own player · the three disable behaviours · a blink with an OnEnable play · another component (E01's form)
        [Test]
        public void GQ16a_E09TurnsOffItsOwnPlayer()
        {
            FeedbackPlayer p = m_Fx.Player("GQ16a");
            p.m_Items.Add(Self(Enabled(typeof(FeedbackPlayer).FullName, 0), SetTo(0f)));
            p.m_Items.Add(Self(Enabled("UnityEngine.AudioSource", 0), SetTo(0f)));
            p.gameObject.AddComponent<AudioSource>();
            p.m_DisableBehavior = PlayerDisableBehavior.StopRevert;
            Want(p, "FB-E09", new[] { 0 }, RuleSeverity.Warning);
            Assert.That(Message(p, "FB-E09", 0), Does.Contain("turns the player on again"));
            Assert.That(F.Of(p, "FB-E09").Single().Fixes.Select(f => f.Name), Is.EqualTo(new[] { "Keep playing on disable" }));
            p.m_DisableBehavior = PlayerDisableBehavior.StopHold;
            Assert.That(Message(p, "FB-E09", 0), Does.EndWith("ends the effect at once"));
            p.m_DisableBehavior = PlayerDisableBehavior.Continue;
            Want(p, "FB-E09", new int[0], RuleSeverity.Warning);
            p.m_Items[0] = Self(Enabled(typeof(FeedbackPlayer).FullName, 0), new BlinkMotion());
            p.m_AutoPlay = PlayerAutoPlay.OnEnable;
            Assert.That(Message(p, "FB-E09", 0), Does.Contain("OnEnable"));
            p.m_Items[0] = Self(Enabled(typeof(FeedbackPlayer).FullName, 0), SetTo(1f));
            Want(p, "FB-E09", new int[0], RuleSeverity.Warning);
        }

        // E10: no type · a missing index · not turned · two types of one name (none here — the engine's types are one each) · a
        // Direct component's index after a reorder, its fix
        [Test]
        public void GQ16b_E10TheComponentsSetUp()
        {
            GameObject g = Go("GQ16b");
            AudioSource first = g.AddComponent<AudioSource>();
            AudioSource second = g.AddComponent<AudioSource>();
            g.AddComponent<LODGroup>();
            FeedbackPlayer p = m_Fx.Player("GQ16bPlayer",
                On(Enabled("", 0), g),
                On(Enabled("UnityEngine.AudioSource", 2), g),
                On(Enabled("UnityEngine.LODGroup", 0), g),
                On(Enabled("UnityEngine.AudioSource", 1), second),
                On(Enabled("UnityEngine.AudioSource", 1), g));
            Want(p, "FB-E10", new[] { 0, 1, 2 }, RuleSeverity.Warning);
            Assert.That((Message(p, "FB-E10", 0), Message(p, "FB-E10", 1)), Is.EqualTo(("No component chosen — the item is skipped", "No UnityEngine.AudioSource #2 on the target — the item is skipped")));
            Assert.That(Message(p, "FB-E10", 2), Does.Contain("not a Behaviour, Renderer or Collider"));
            ComponentUtility.MoveComponentUp(second);
            Want(p, "FB-E10", new[] { 0, 1, 2, 3 }, RuleSeverity.Warning);
            RuleResult drift = F.Of(p, "FB-E10").Single(r => r.Item == 3);
            Assert.That(drift.Message, Does.Contain("#0 of its type, the index says #1"));
            FeedbackValidator.Apply(drift, drift.Fixes.Single(f => f.Name == "Set the index to 0"));
            Assert.That(((ComponentEnabledItem)p.m_Items[3]).m_ComponentIndex, Is.Zero);
            Want(p, "FB-E10", new[] { 0, 1, 2 }, RuleSeverity.Warning);
        }

        // J01 ~ J07
        [Test]
        public void GQ16c_GroupJSpeaksInItsCases()
        {
            GameObject prefab = Prefab("GQ16cPrefab");
            GameObject sceneObject = Go("GQ16cScene");
            GameObject target = Go("GQ16cTarget");
            var none = On(new SpawnItem(), target);
            var empty = On(new SpawnItem { m_Prefab = prefab, m_PoolSize = 0 }, target);
            var made = On(new SpawnItem { m_Prefab = prefab, m_Pooled = false }, target);
            var scene = On(new SpawnItem { m_Prefab = sceneObject }, target);
            var forever = On(new SpawnItem { m_Prefab = prefab, m_Lifetime = 0f }, target);
            var unnamed = On(new SetLayerItem { m_Layer = 31 }, target);
            var outside = On(new SetLayerItem { m_Layer = 32 }, target);
            var named = On(new SetLayerItem { m_Layer = 0 }, target);
            FeedbackPlayer p = m_Fx.Player("GQ16c", none, empty, made, scene, forever, unnamed, outside, named);
            Want(p, "FB-J01", new[] { 0 }, RuleSeverity.Warning);
            Want(p, "FB-J02", new[] { 1 }, RuleSeverity.Warning);
            Want(p, "FB-J03", new[] { 2 }, RuleSeverity.Info);
            Want(p, "FB-J04", new[] { 3 }, RuleSeverity.Info);
            Assert.That(F.Of(p, "FB-J06").Select(r => (r.Item, r.Severity)).OrderBy(x => x.Item), Is.EqualTo(new[] { (5, RuleSeverity.Info), (6, RuleSeverity.Warning) }), "J06 — no name · out of range");
            Assert.That(F.Of(p, "FB-J07").Select(r => r.Item), Is.EqualTo(new[] { 4 }), "a lifetime of 0 without a despawn");
            p.m_Items.Add(On(new DespawnItem(), target));
            Want(p, "FB-J07", new int[0], RuleSeverity.Info);
            RuleResult j02 = F.Of(p, "FB-J02").Single();
            FeedbackValidator.Apply(j02, j02.Fixes.Single(f => f.Name == "Set the pool size to 5"));
            Assert.That(empty.m_PoolSize, Is.EqualTo(5));
            RuleResult j03 = F.Of(p, "FB-J03").Single();
            FeedbackValidator.Apply(j03, j03.Fixes.Single(f => f.Name == "Use a pool"));
            Assert.That(made.m_Pooled, Is.True);
            FeedbackPlayer own = m_Fx.Player("GQ16cOwn", Self(new DespawnItem()));
            Want(own, "FB-J05", new[] { 0 }, RuleSeverity.Info);
            Assert.That(Message(own, "FB-J05", 0), Is.EqualTo("Despawns its own player — the rest of its play does not run"));
            own.m_Items[0] = On(new DespawnItem(), target);
            Want(own, "FB-J05", new int[0], RuleSeverity.Info);
        }
    }
}
