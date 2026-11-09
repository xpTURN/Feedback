using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests
{
    // the runtime context's pools (the container DontDestroyOnLoad · a parent None spawn moved to the active scene · a scene's
    // unload), Destroy in play, a Component Enabled turning off its own player (E09's three cases)
    public sealed class GameObjectPlayTests
    {
        readonly List<Object> m_Made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in m_Made)
                if (o != null) Object.Destroy(o);
            m_Made.Clear();
        }

        GameObject Go(string n) { var g = new GameObject(n); m_Made.Add(g); return g; }

        static SetMotion SetTo(float v) => new SetMotion { m_Params = new SetParams { m_Value = new Vector4(v, 0, 0, 0) } };

        static T With<T>(T item, Object target, FeedbackMotion motion = null) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion ?? SetTo(1f);
            return item;
        }

        static SpawnItem Spawn(GameObject prefab, Object target, int size, float lifetime, bool pooled = true, SpawnParent parent = SpawnParent.None)
        {
            SpawnItem item = With(new SpawnItem(), target);
            item.m_Prefab = prefab;
            item.m_Pooled = pooled;
            item.m_PoolSize = size;
            item.m_Lifetime = lifetime;
            item.m_Parent = parent;
            return item;
        }

        FeedbackPlayer Player(params FeedbackItem[] items)
        {
            FeedbackPlayer p = Go("Player").AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            p.EnsureRuntime();
            return p;
        }

        GameObject Template(string name)
        {
            GameObject t = Go(name);
            t.AddComponent<MeshFilter>();
            t.AddComponent<MeshRenderer>();
            return t;
        }

        static PoolService Pools => FeedbackScheduler.Default.Context.PoolsIfMade;

        // the runtime container — DontDestroyOnLoad, off · a parent None spawn in the active scene (not the container's) · given
        // back into the container · a non-pooled one's lifetime destroys it (Destroy — gone the next frame)
        [UnityTest]
        public IEnumerator GQ04b_TheRuntimeContainer()
        {
            GameObject tpl = Template("tpl");
            GameObject target = Go("target");
            FeedbackPlayer p = Player(Spawn(tpl, target, 4, 0f));
            yield return null;
            PoolService pools = Pools;
            Assert.That(FeedbackScheduler.Default.Context.Kind, Is.EqualTo(ContextKind.Runtime));
            Assert.That((pools.Container.gameObject.scene.name, pools.Container.gameObject.activeSelf), Is.EqualTo(("DontDestroyOnLoad", false)));
            p.Play();
            yield return null;
            GameObject a = pools.LiveAt(pools.LiveCount - 1);
            Assert.That((a.scene, a.transform.parent, a.activeInHierarchy), Is.EqualTo((SceneManager.GetActiveScene(), (Transform)null, true)), "a parent None spawn is the active scene's");
            Assert.That(pools.TryReturn(a), Is.True);
            Assert.That((a.scene.name, a.transform.parent, a.activeInHierarchy), Is.EqualTo(("DontDestroyOnLoad", pools.Container, false)), "back in the container");
            GameObject tpl2 = Template("tpl2");
            Player(Spawn(tpl2, target, 1, 0.05f, pooled: false)).Play();
            yield return null;
            GameObject made = pools.LiveAt(pools.LiveCount - 1);
            Assert.That(made.scene, Is.EqualTo(SceneManager.GetActiveScene()));
            float until = Time.time + 0.5f;
            while (made != null && Time.time < until)
                yield return null;
            Assert.That(made == null, Is.True, "its lifetime destroyed it");
        }

        // a spawn under an object of another scene — the scene's unload destroys it, the next update lets it go · the binding of a
        // destroyed target does not play
        [UnityTest]
        public IEnumerator GQ04c_AScenesUnload()
        {
            GameObject tpl = Template("tpl");
            Scene extra = SceneManager.CreateScene("GQ04cExtra");
            var anchor = new GameObject("anchor");
            SceneManager.MoveGameObjectToScene(anchor, extra);
            FeedbackPlayer p = Player(Spawn(tpl, anchor, 2, 0f, parent: SpawnParent.Target));
            yield return null;
            PoolService pools = Pools;
            int before = pools.LiveCount;
            p.Play();
            yield return null;
            GameObject a = pools.LiveAt(pools.LiveCount - 1);
            Assert.That(a.scene, Is.EqualTo(extra));
            yield return SceneManager.UnloadSceneAsync(extra);
            yield return null;
            Assert.That((a == null, pools.LiveCount, pools.MadeOf(tpl)), Is.EqualTo((true, before, 1)), "destroyed with its scene, let go");
            p.Play();
            yield return null;
            Assert.That(pools.LiveCount, Is.EqualTo(before), "no spawn without its target");
        }

        // Destroy in play — a pooled live one goes back · a scene object is destroyed
        [UnityTest]
        public IEnumerator GQ06b_DespawnInPlay()
        {
            GameObject tpl = Template("tpl");
            FeedbackPlayer ps = Player(Spawn(tpl, Go("target"), 2, 0f));
            yield return null;
            ps.Play();
            yield return null;
            PoolService pools = Pools;
            GameObject a = pools.LiveAt(pools.LiveCount - 1);
            Player(With(new DespawnItem(), a)).Play();
            yield return null;
            Assert.That((a != null, a.transform.parent, pools.RestingOf(tpl)), Is.EqualTo((true, pools.Container, 2)), "back to its pool");
            var b = new GameObject("scene object");
            Player(With(new DespawnItem(), b)).Play();
            yield return null;
            yield return null;
            Assert.That(b == null, Is.True, "destroyed");
        }

        // a Component Enabled turning off its own player — a StopRevert's revert turns it on again · a StopHold's stays off and
        // stops · a Continue's stays off and plays on (the engine takes an enable inside OnDisable)
        [UnityTest]
        public IEnumerator GQ08d_TurningOffItsOwnPlayer([Values(PlayerDisableBehavior.StopRevert, PlayerDisableBehavior.StopHold, PlayerDisableBehavior.Continue)] PlayerDisableBehavior behavior)
        {
            FeedbackPlayer p = Go("own").AddComponent<FeedbackPlayer>();
            p.m_DisableBehavior = behavior;
            ComponentEnabledItem item = With(new ComponentEnabledItem(), p, SetTo(0f));
            item.m_ComponentType = typeof(FeedbackPlayer).FullName;
            var keeper = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(0.5f, 0f, 0f, 0f), m_Vibrato = 1f, m_Decay = 0f } } };
            keeper.m_Timing.m_Duration = 1f;
            p.m_Items.Add(item);
            p.m_Items.Add(keeper);
            p.EnsureRuntime();
            yield return null;
            p.Play();
            yield return null;
            yield return null;
            bool on = behavior == PlayerDisableBehavior.StopRevert;
            bool playing = behavior == PlayerDisableBehavior.Continue;
            Assert.That((p.enabled, p.IsPlaying), Is.EqualTo((on, playing)));
        }

        // a fresh warm instance's take into the active scene · its give-back into the container allocate nothing (the move to the
        // active scene too)
        [UnityTest]
        public IEnumerator GQ03g_ARuntimeTakeAllocatesNothing()
        {
            GameObject tpl = Template("alloc");
            Player(Spawn(tpl, Go("target"), 24, 0f));
            yield return null;
            PoolService pools = Pools;
            float f = 0f;
            Assert.That(UIs.AllocatingGCMemory().ApplyTo((TestDelegate)(() => { var x = new int[4]; f += x.Length; })).IsSuccess, Is.True, "the positive control");
            int need = 16 - pools.LiveCount;
            for (int i = 0; i < need; i++)
                pools.Spawn(tpl, true, Vector3.one, Quaternion.identity, null, 0f, false, null, 0, 0);
            int takes = 0;
            for (int i = 0; i < 5; i++)
                if (UIs.AllocatingGCMemory().ApplyTo((TestDelegate)(() => pools.Spawn(tpl, true, Vector3.one, Quaternion.identity, null, 0f, false, null, 0, 0))).IsSuccess) takes++;
            int backs = 0;
            for (int i = 0; i < 5; i++)
            {
                GameObject g = pools.LiveAt(pools.LiveCount - 1);
                if (UIs.AllocatingGCMemory().ApplyTo((TestDelegate)(() => pools.TryReturn(g))).IsSuccess) backs++;
            }
            Assert.That((takes, backs), Is.EqualTo((0, 0)));
            pools.ReturnOwned(null, -1, -1);
        }
    }
}
