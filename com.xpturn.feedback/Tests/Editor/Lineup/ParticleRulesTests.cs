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
    // K01 ~ K06 speak in their cases only — their severities, their fixes · J02 · J03 · J04 read a particle spawn too. The rules
    // read the systems through the module's types (the editor names them with the module off)
    public sealed class ParticleRulesTests
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

        static ParticleSystem System(GameObject go, bool looping = false, ParticleSystemStopAction action = ParticleSystemStopAction.None, bool emission = true)
        {
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = looping;
            main.stopAction = action;
            ParticleSystem.EmissionModule e = ps.emission;
            e.enabled = emission;
            return ps;
        }

        static TweenMotion TweenTo(float v, TweenMode mode = TweenMode.To)
        {
            var m = new TweenMotion();
            m.m_Params.m_Mode = mode;
            m.m_Params.m_To = new Vector4(v, 0, 0, 0);
            return m;
        }

        static FlashMotion FlashTo(float v)
        {
            var m = new FlashMotion();
            m.m_Params.m_Target = new Vector4(v, 0, 0, 0);
            return m;
        }

        static T On<T>(T item, Object target, FeedbackMotion motion = null) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion ?? new SetMotion();
            return item;
        }

        static void Want(Component host, string id, int[] items, RuleSeverity severity)
        {
            List<RuleResult> results = F.Of(host, id);
            Assert.That(results.Select(r => r.Item).OrderBy(i => i), Is.EqualTo(items), id);
            Assert.That(results.All(r => r.Severity == severity), Is.True, id + " " + severity);
        }

        static string Message(Component host, string id, int item) => F.Of(host, id).Single(r => r.Item == item).Message;

        GameObject Prefab(string name, bool system = true, bool looping = false, ParticleSystemStopAction action = ParticleSystemStopAction.None)
        {
            var source = new GameObject(name);
            if (system)
                System(source, looping, action);
            string path = F.Dir + "/" + name + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
            Object.DestroyImmediate(source);
            m_Assets.Add(path);
            return prefab;
        }

        // K01 · K02 · K03: the spawns — no prefab · a root without a system · a pooled self-destroying system · a looping one without
        // Stop After (its fix)
        [Test]
        public void PQ17a_TheSpawnRules()
        {
            GameObject target = Go("PQ17aTarget");
            GameObject plain = Prefab("PQ17aPlain", system: false);
            GameObject oneShot = Prefab("PQ17aOneShot");
            GameObject looping = Prefab("PQ17aLooping", looping: true);
            GameObject destroys = Prefab("PQ17aDestroys", action: ParticleSystemStopAction.Destroy);
            var none = On(new ParticleSpawnItem(), target.transform);
            var rootless = On(new ParticleSpawnItem { m_Prefab = plain }, target.transform);
            var lost = On(new ParticleSpawnItem { m_Prefab = destroys }, target.transform);
            var lostUnpooled = On(new ParticleSpawnItem { m_Prefab = destroys, m_Pooled = false }, target.transform);
            var endless = On(new ParticleSpawnItem { m_Prefab = looping }, target.transform);
            var stopped = On(new ParticleSpawnItem { m_Prefab = looping, m_StopAfter = 0.5f }, target.transform);
            var fine = On(new ParticleSpawnItem { m_Prefab = oneShot }, target.transform);
            FeedbackPlayer p = m_Fx.Player("PQ17a", none, rootless, lost, lostUnpooled, endless, stopped, fine);
            Want(p, "FB-K01", new[] { 0, 1 }, RuleSeverity.Warning);
            Assert.That(Message(p, "FB-K01", 1), Is.EqualTo("The prefab's root has no particle system — the item is skipped"));
            Want(p, "FB-K02", new[] { 2 }, RuleSeverity.Warning);
            Want(p, "FB-K03", new[] { 4 }, RuleSeverity.Warning);
            RuleResult k03 = F.Of(p, "FB-K03").Single();
            FeedbackValidator.Apply(k03, k03.Fixes.Single(f => f.Name == "Stop after 1 s"));
            Assert.That(endless.m_StopAfter, Is.EqualTo(1f));
            Want(p, "FB-K03", new int[0], RuleSeverity.Warning);
        }

        // K04 · K05 · K06: the systems — the emission off (its fix) · a stop that destroys or turns off · the values out of the engine's
        // range (a By tween is relative — not judged)
        [Test]
        public void PQ17b_TheSystemRules()
        {
            ParticleSystem off = System(Go("PQ17bOff"), emission: false);
            ParticleSystem on = System(Go("PQ17bOn"));
            ParticleSystem destroys = System(Go("PQ17bDestroys"), action: ParticleSystemStopAction.Destroy);
            ParticleSystem disables = System(Go("PQ17bDisables"), action: ParticleSystemStopAction.Disable);
            FeedbackPlayer p = m_Fx.Player("PQ17b",
                On(new ParticleEmissionRateItem(), off, TweenTo(5f)),
                On(new ParticleEmissionRateItem(), on, TweenTo(5f)),
                On(new ParticleControlItem { m_Action = ParticleAction.Stop }, destroys),
                On(new ParticleControlItem { m_Action = ParticleAction.Stop }, disables),
                On(new ParticleControlItem { m_Action = ParticleAction.Play }, destroys),
                On(new ParticleControlItem { m_Action = ParticleAction.Stop }, on),
                On(new ParticleSimulationSpeedItem(), on, TweenTo(150f)),
                On(new ParticleSimulationSpeedItem(), on, TweenTo(50f)),
                On(new ParticleSimulationSpeedItem(), on, FlashTo(-1f)),
                On(new ParticleEmissionRateItem(), on, TweenTo(-1f)),
                On(new ParticleEmissionRateItem(), on, TweenTo(1000f)),
                On(new ParticleSimulationSpeedItem(), on, TweenTo(-5f, TweenMode.By)));
            Want(p, "FB-K04", new[] { 0 }, RuleSeverity.Warning);
            Want(p, "FB-K05", new[] { 2, 3 }, RuleSeverity.Info);
            Assert.That(Message(p, "FB-K05", 3), Does.Contain("turns off"));
            Want(p, "FB-K06", new[] { 6, 8, 9 }, RuleSeverity.Info);
            RuleResult k04 = F.Of(p, "FB-K04").Single();
            FeedbackValidator.Apply(k04, k04.Fixes.Single(f => f.Name == "Turn the emission module on"));
            Assert.That(off.emission.enabled, Is.True);
            Want(p, "FB-K04", new int[0], RuleSeverity.Warning);
        }

        // J02 · J03 · J04 read a particle spawn's same fields
        [Test]
        public void PQ17c_GroupJReadsAParticleSpawn()
        {
            GameObject target = Go("PQ17cTarget");
            GameObject prefab = Prefab("PQ17cPrefab");
            GameObject sceneObject = Go("PQ17cScene");
            System(sceneObject);
            var empty = On(new ParticleSpawnItem { m_Prefab = prefab, m_PoolSize = 0 }, target.transform);
            var made = On(new ParticleSpawnItem { m_Prefab = prefab, m_Pooled = false }, target.transform);
            var scene = On(new ParticleSpawnItem { m_Prefab = sceneObject }, target.transform);
            FeedbackPlayer p = m_Fx.Player("PQ17c", empty, made, scene);
            Want(p, "FB-J02", new[] { 0 }, RuleSeverity.Warning);
            Want(p, "FB-J03", new[] { 1 }, RuleSeverity.Info);
            Want(p, "FB-J04", new[] { 2 }, RuleSeverity.Info);
            Want(p, "FB-J01", new int[0], RuleSeverity.Warning);
            RuleResult j02 = F.Of(p, "FB-J02").Single();
            FeedbackValidator.Apply(j02, j02.Fixes.Single(f => f.Name == "Set the pool size to 5"));
            Assert.That(empty.m_PoolSize, Is.EqualTo(5));
        }
    }
}
