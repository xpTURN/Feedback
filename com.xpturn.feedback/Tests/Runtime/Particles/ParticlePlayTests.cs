using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests
{
    // the Update stage on the real loop · the spawn's end in play (the particles gone · the emission stopped where no camera sees
    // it) · a warmed spawn cycle allocates nothing. The systems are always simulated (a batch PlayMode has no camera)
    public sealed class ParticlePlayTests
    {
        const float Step = 0.05f;
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();

        [TearDown]
        public void TearDown()
        {
            foreach (FeedbackPlayer p in m_Players)
                if (p != null) p.ReleaseRuntime();
            foreach (FeedbackScheduler s in m_Schedulers)
                if (!s.IsShutdown) s.Shutdown();
            foreach (Object o in m_Made)
                if (o != null) Object.Destroy(o);
            m_Made.Clear(); m_Schedulers.Clear(); m_Players.Clear();
        }

        GameObject Go(string n) { var g = new GameObject(n); m_Made.Add(g); return g; }

        static T With<T>(T item, FeedbackMotion motion, float duration, Object target) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            FeedbackPlayer p = Go("player").AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            if (s != null)
            {
                p.Bind(s);
                p.EnsureRuntime();
            }
            m_Players.Add(p);
            return p;
        }

        GameObject Template(string name, bool looping, float duration, float lifetime)
        {
            GameObject t = Go(name);
            ParticleFixtures.System(t, looping, duration, lifetime, 50f);
            t.SetActive(false);
            return t;
        }

        static ParticleSpawnItem Spawn(GameObject prefab, Object target, int size, float stopAfter)
        {
            ParticleSpawnItem item = With(new ParticleSpawnItem(), new SetMotion(), 0f, target);
            item.m_Prefab = prefab;
            item.m_PoolSize = size;
            item.m_StopAfter = stopAfter;
            return item;
        }

        // ---- a speed flash in the Update stage reaches the frame's particle update — the game code in Update reads the base (put
        // back at EarlyUpdate). A fixed 1/60 s frame: the particle clock steps at most Time.maximumParticleDeltaTime a frame, so a
        // slow real frame reads short
        [UnityTest]
        public IEnumerator PQ05_TheUpdateStageOnTheLoop()
        {
            float capture = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                ParticleSystem a = ParticleFixtures.System(Go("PQ05"), true, 100f, 100f, 0f);
                a.Play(true);
                var flash = new FlashMotion();
                flash.m_Params.m_Target = new Vector4(4f, 0, 0, 0);
                flash.m_Params.m_Attack = 0f;
                flash.m_Params.m_Hold = 1f;
                flash.m_Params.m_Release = 0f;
                FeedbackPlayer p = Player(null, With(new ParticleSimulationSpeedItem(), flash, 1f, a));
                yield return null;
                p.Play();
                float until = Time.time + 0.1f;
                while (Time.time < until)
                    yield return null;
                float t0 = a.time, dt = 0f;
                until = Time.time + 0.5f;
                while (Time.time < until)
                {
                    yield return null;
                    dt += Time.deltaTime;
                }
                Assert.That((a.time - t0) / dt, Is.EqualTo(4f).Within(0.1f), "the particle clock at the written speed");
                Assert.That(a.main.simulationSpeed, Is.EqualTo(1f), "the game's Update reads the base");
            }
            finally
            {
                Time.captureDeltaTime = capture;
            }
        }

        // ---- a one-shot goes back when its particles are gone · a looping one with Stop After goes back when nobody sees its tail ·
        // without Stop After it stays
        [UnityTest]
        public IEnumerator PQ08b_TheSpawnsEndInPlay()
        {
            // off every camera — an unseen tail is cut at Stop After
            GameObject anchor = Go("anchor");
            anchor.transform.position = new Vector3(0f, -100000f, 0f);
            var cases = new (GameObject prefab, float stopAfter, bool back)[]
            {
                (Template("oneShot", false, 0.3f, 0.3f), 0f, true),
                (Template("looping", true, 1f, 0.3f), 0.3f, true),
                (Template("endless", true, 1f, 0.3f), 0f, false),
                (Template("unseenTail", true, 1f, 2f), 0.3f, true),
            };
            foreach ((GameObject prefab, float stopAfter, bool back) in cases)
            {
                FeedbackPlayer p = Player(null, Spawn(prefab, anchor, 2, stopAfter));
                yield return null;
                p.Play();
                PoolService pools = FeedbackScheduler.Default.Context.PoolsIfMade;
                GameObject live = pools.LiveAt(pools.LiveCount - 1);
                Assert.That(live.GetComponent<ParticleSystem>().isPlaying, Is.True, prefab.name + " plays");
                float until = Time.time + 1.2f;
                while (Time.time < until)
                    yield return null;
                Assert.That(!live.activeInHierarchy, Is.EqualTo(back), prefab.name + (back ? " went back" : " stays"));
                p.Stop(StopMode.Revert);
                yield return null;
            }
        }

        // ---- a pooled particle spawn's cycle — a play takes one, Stop After stops it, the end gives it back: warmed, the
        // repeats allocate nothing
        [UnityTest]
        public IEnumerator PQ13b_ASpawnCycleAllocatesNothing()
        {
            yield return null;
            float f = 0f;
            Assert.That(UIs.AllocatingGCMemory().ApplyTo((TestDelegate)(() => { var x = new int[4]; f += x.Length; })).IsSuccess, Is.True, "the positive control");
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            GameObject anchor = Go("anchor");
            GameObject prefab = Template("cycle", true, 1f, 0.3f);
            FeedbackPlayer p = Player(s, Spawn(prefab, anchor, 2, 0.1f));
            TestDelegate cycle = () =>
            {
                p.Play();
                for (int i = 0; i < 4; i++)
                    s.Tick(Step);
            };
            for (int i = 0; i < 3; i++)
                cycle();
            int hits = 0;
            for (int i = 0; i < 5; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(cycle).IsSuccess)
                    hits++;
            }
            PoolService pools = s.Context.PoolsIfMade;
            Assert.That((hits, pools.LiveCount, pools.GrownOf(prefab)), Is.EqualTo((0, 0, 0)), "the cycle allocates nothing · given back · never grown");
        }
    }
}
