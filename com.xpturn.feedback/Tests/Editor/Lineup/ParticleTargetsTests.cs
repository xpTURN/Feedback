using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests.Editor
{
    // the values, Control, Emit, the spawn, the pools' end, the simulation's frame, the preview, the keep, the saved form and the
    // debugger (direct Tick on test contexts · the preview on the test's clock — two versions). The Particles assembly's types are
    // not named (the minimal project has none): a test that plays a particle item is the dev project's
    public sealed class ParticleTargetsTests
    {
        const float Step = 0.05f;
        static readonly PlayArgs Back = PlayArgs.Default.WithDirection(PlayerDirection.Backward);
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_S = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_P = new List<FeedbackPlayer>();
        readonly List<string> m_Assets = new List<string>();
        static double s_Now;
        Func<double> m_Clock;

        [SetUp]
        public void SetUp()
        {
            m_Clock = PreviewClock.Source;
            PreviewClock.Source = () => s_Now;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void TearDown()
        {
            PreviewSession.Current?.Stop();
            foreach (FeedbackPlayer p in m_P)
                if (p != null) p.ReleaseRuntime();
            foreach (FeedbackScheduler s in m_S)
                if (!s.IsShutdown) s.Shutdown();
            foreach (Object o in m_Made)
                if (o != null) Object.DestroyImmediate(o);
            foreach (GameObject g in Resources.FindObjectsOfTypeAll<GameObject>())
                if (g != null && !EditorUtility.IsPersistent(g) && g.name.StartsWith(PropertyLayerRegistry.PreviewPrefix, StringComparison.Ordinal))
                    Object.DestroyImmediate(g);
            foreach (string a in m_Assets)
                AssetDatabase.DeleteAsset(a);
            m_Made.Clear(); m_S.Clear(); m_P.Clear(); m_Assets.Clear();
            PreviewClock.Source = m_Clock;
            DebuggerSource.Override = null;
            DebuggerTesting.CloseWindows();
        }

        static void Pump(int n) { for (int i = 0; i < n; i++) { s_Now += 1.0 / 120.0; PreviewSession.Current?.Update(); } }
        static void Tick(FeedbackScheduler s, int n) { for (int i = 0; i < n; i++) s.Tick(Step); }
        GameObject Go(string n) { var g = new GameObject(n); m_Made.Add(g); return g; }
        FeedbackScheduler Sched() { var s = new FeedbackScheduler(ContextKind.Test); m_S.Add(s); return s; }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            FeedbackPlayer p = Go("Player").AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            if (s != null)
            {
                p.Bind(s);
                p.EnsureRuntime();
            }
            m_P.Add(p);
            return p;
        }

        ParticleSystem Sys(string name, bool looping = true, float rate = 20f, float duration = 1f, float lifetime = 1f, Transform parent = null, bool playOnAwake = false, bool unscaled = false)
        {
            GameObject go = Go(name);
            if (parent != null)
                go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = 1;
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = playOnAwake;
            main.loop = looping;
            main.duration = duration;
            main.startLifetime = lifetime;
            main.maxParticles = 100000;
            main.useUnscaledTime = unscaled;
            ParticleSystem.EmissionModule em = ps.emission;
            em.rateOverTime = rate;
            return ps;
        }

        static T With<T>(T item, FeedbackMotion motion, float duration, Object target) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static SetMotion Set() => new SetMotion { m_Params = new SetParams { m_Value = new Vector4(1f, 0, 0, 0) } };

        static TweenMotion TweenTo(float v)
        {
            var m = new TweenMotion();
            m.m_Params.m_Mode = TweenMode.To;
            m.m_Params.m_To = new Vector4(v, 0, 0, 0);
            return m;
        }

        static FlashMotion Hold(float v, float hold)
        {
            var m = new FlashMotion();
            m.m_Params.m_Target = new Vector4(v, 0, 0, 0);
            m.m_Params.m_Attack = 0f;
            m.m_Params.m_Hold = hold;
            m.m_Params.m_Release = 0f;
            return m;
        }

        static PositionItem Keeper(Component target, float d)
        {
            var k = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target.transform, m_Path = "", m_Anchor = "" }, m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(0.01f, 0, 0, 0), m_Vibrato = 1f, m_Decay = 0f } } };
            k.m_Timing.m_Duration = d;
            return k;
        }

        static ParticleControlItem Control(ParticleSystem ps, ParticleAction a, bool children = true, ParticleStopMode mode = ParticleStopMode.StopEmitting)
        {
            ParticleControlItem item = With(new ParticleControlItem(), Set(), 0f, ps);
            item.m_Action = a;
            item.m_WithChildren = children;
            item.m_StopMode = mode;
            return item;
        }

        GameObject Template(string name, bool looping, float duration, float lifetime)
        {
            GameObject t = Go(name);
            var ps = t.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = looping;
            main.duration = duration;
            main.startLifetime = lifetime;
            ParticleSystem.EmissionModule em = ps.emission;
            em.rateOverTime = 50f;
            t.SetActive(false);
            return t;
        }

        static ParticleSpawnItem Spawn(GameObject prefab, Object target, int size, float stopAfter)
        {
            ParticleSpawnItem item = With(new ParticleSpawnItem(), Set(), 0f, target);
            item.m_Prefab = prefab;
            item.m_PoolSize = size;
            item.m_StopAfter = stopAfter;
            return item;
        }

        static int Bits(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0);

        static Dictionary<string, string> SnapOf(Object o)
        {
            var d = new Dictionary<string, string>();
            SerializedProperty it = new SerializedObject(o).GetIterator();
            while (it.Next(true))
            {
                switch (it.propertyType)
                {
                    case SerializedPropertyType.Float: d[it.propertyPath] = it.floatValue.ToString("R"); break;
                    case SerializedPropertyType.Integer: d[it.propertyPath] = it.longValue.ToString(); break;
                    case SerializedPropertyType.Boolean: d[it.propertyPath] = it.boolValue.ToString(); break;
                    case SerializedPropertyType.Enum: d[it.propertyPath] = it.intValue.ToString(); break;
                }
            }
            return d;
        }

        static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out string v) && v == kv.Value);

        // ---- the requires name resolves — in the minimal project too (the editor references the module)
        [Test]
        public void PQ02_TheRequiresName()
        {
            Assert.That(TargetChecks.Requires(typeof(ParticleControlItem))?.FullName, Is.EqualTo("UnityEngine.ParticleSystem"));
            Assert.That(TargetChecks.Requires(typeof(ParticleEmissionRateItem))?.FullName, Is.EqualTo("UnityEngine.ParticleSystem"));
        }

        // ---- the values — as written: Tween · Flash · the base's bits (Revert) · the Update stage · a curve mode keeps its curve
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void PQ04a_TheValues()
        {
            FeedbackScheduler s = Sched();
            ParticleSystem a = Sys("speed");
            FeedbackPlayer p = Player(s, With(new ParticleSimulationSpeedItem(), TweenTo(3f), 0.5f, a), Keeper(a, 1f));
            p.Play();
            s.RunRevert(); s.RunUpdate(new FrameTime(Step, Step));
            float inUpdate = a.main.simulationSpeed;
            s.RunLateApply();
            Assert.That(inUpdate, Is.GreaterThan(1f), "written in the Update stage");
            Tick(s, 4);
            p.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Bits(a.main.simulationSpeed), Is.EqualTo(Bits(1f)), "the base's bits");
            var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
            ParticleSystem r = Sys("curve");
            ParticleSystem.EmissionModule e = r.emission;
            e.rateOverTime = new ParticleSystem.MinMaxCurve(10f, curve);
            FeedbackPlayer pr = Player(s, With(new ParticleEmissionRateItem(), TweenTo(4f), 0.2f, r), Keeper(r, 1f));
            pr.Play(); Tick(s, 2);
            Assert.That(r.emission.rateOverTime.mode, Is.EqualTo(ParticleSystemCurveMode.Curve), "the curve stays");
            pr.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(r.emission.rateOverTimeMultiplier, Is.EqualTo(10f));
        }

        // two constants (the ratio · the exact source back through the Persist door) · the engine's clamp read back
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void PQ04b_TwoConstantsAndTheClamp()
        {
            FeedbackScheduler s = Sched();
            ParticleSystem r = Sys("two");
            ParticleSystem.EmissionModule e = r.emission;
            e.rateOverTime = new ParticleSystem.MinMaxCurve(5f, 15f);
            FeedbackPlayer pr = Player(s, With(new ParticleEmissionRateItem(), TweenTo(0f), 0.4f, r), Keeper(r, 1f));
            pr.Play(); Tick(s, 4);
            ParticleSystem.MinMaxCurve mid = r.emission.rateOverTime;
            Assert.That(mid.constantMin, Is.EqualTo(mid.constantMax / 3f).Within(1e-4f), "two constants scale together");
            Tick(s, 6);
            pr.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That((Bits(r.emission.rateOverTime.constantMin), Bits(r.emission.rateOverTime.constantMax)), Is.EqualTo((Bits(5f), Bits(15f))), "the source pair's bits after a revert from 0");
            ParticleSystem c = Sys("clamp");
            FeedbackPlayer pc = Player(s, With(new ParticleSimulationSpeedItem(), TweenTo(150f), 0.1f, c), Keeper(c, 1f));
            pc.Play(); Tick(s, 4);
            Assert.That(c.main.simulationSpeed, Is.EqualTo(100f), "the engine's clamp");
            pc.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Bits(c.main.simulationSpeed), Is.EqualTo(Bits(1f)), "the base's bits after a clamped write");
        }

        // ---- Control — as written: the actions · children · Revert to the record's start (a keeper) · Restore · backward
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void PQ06a_Control()
        {
            FeedbackScheduler s = Sched();
            ParticleSystem a = Sys("a");
            FeedbackPlayer p = Player(s, Control(a, ParticleAction.Play), Keeper(a, 1f));
            p.Play(); s.Tick(0f);
            Assert.That(a.isPlaying, Is.True, "Play");
            p.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(a.isStopped, Is.True, "back to the stopped start");
            ParticleSystem b = Sys("b");
            b.Simulate(0.3f, true, true, false);
            b.Play(true);
            FeedbackPlayer pb = Player(s, Control(b, ParticleAction.Stop, true, ParticleStopMode.StopEmittingAndClear));
            pb.Play(); s.Tick(0f);
            Assert.That((b.isStopped, b.particleCount), Is.EqualTo((true, 0)), "Stop and clear");
            ParticleSystem c = Sys("c");
            c.Simulate(0.3f, true, true, false);
            c.Play(true);
            FeedbackPlayer pc = Player(s, Control(c, ParticleAction.Stop));
            pc.Play(); s.Tick(0f);
            Assert.That((c.isEmitting, c.particleCount > 0), Is.EqualTo((false, true)), "Stop emitting keeps the particles");
            ParticleSystem parent = Sys("parent");
            ParticleSystem child = Sys("child", parent: parent.transform);
            Player(s, Control(parent, ParticleAction.Play, false)).Play(); s.Tick(0f);
            Assert.That((parent.isPlaying, child.isPlaying), Is.EqualTo((true, false)), "without children");
            ParticleSystem g = Sys("g");
            FeedbackPlayer pg = Player(s, Control(g, ParticleAction.Play));
            pg.Play(); s.Tick(0f);
            pg.Restore(); s.Tick(0f);
            Assert.That(g.isStopped, Is.True, "Restore");
            ParticleSystem h = Sys("h");
            FeedbackPlayer ph = Player(s, Control(h, ParticleAction.Play));
            ph.Play(); s.Tick(0f);
            ph.Play(in Back); s.Tick(0f);
            Assert.That(h.isStopped, Is.True, "backward — the origin");
        }

        // the start read from the engine's flags (a playing system paused → Revert plays · Stop → Revert plays) · a system played
        // and driven before the prepare (the preview's simulate leaves it paused) — the slot's first record is its flags then
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void PQ06b_TheStartFromTheEnginesFlags()
        {
            FeedbackScheduler s = Sched();
            foreach ((ParticleAction action, ParticleStopMode mode) in new[] { (ParticleAction.Pause, ParticleStopMode.StopEmitting), (ParticleAction.Stop, ParticleStopMode.StopEmittingAndClear) })
            {
                ParticleSystem a = Sys("s" + action);
                FeedbackPlayer p = Player(s, Control(a, action, true, mode), Keeper(a, 1f));
                // played after the prepare — the slot was made while it was stopped
                a.Play(true);
                p.Play(); s.Tick(0f);
                p.Stop(StopMode.Revert); s.Tick(0f);
                Assert.That((a.isPlaying, a.isEmitting), Is.EqualTo((true, true)), action + " → Revert plays");
            }
            ParticleSystem b = Sys("driven");
            b.Play(true);
            FeedbackPlayer pb = Player(s, Control(b, ParticleAction.Stop, true, ParticleStopMode.StopEmittingAndClear), Keeper(b, 1f));
            b.Simulate(0.1f, true, false, false);
            pb.Play(); s.Tick(0f);
            pb.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That((b.isPlaying, b.isEmitting), Is.EqualTo((true, true)), "driven → Revert plays");
        }

        // a play-on-awake system plays from the first record — a Control's start is read after that record, so its Revert plays
        // the system (the runtime's system already ran)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void PQ06c_APlayOnAwakeStartInThePreview()
        {
            ParticleSystem a = Sys("awake", playOnAwake: true);
            FeedbackPlayer player = Player(null, Control(a, ParticleAction.Pause), Keeper(a, 1f));
            s_Now += 1.0;
            PreviewSession.Start(player, default);
            Pump(12);
            Assert.That(a.isPaused, Is.True, "paused");
            player.Stop(StopMode.Revert);
            Assert.That((a.isStopped, a.isEmitting), Is.EqualTo((false, true)), "Revert plays it");
            PreviewSession.Current?.Stop();
        }

        // ---- Emit
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void PQ07_Emit()
        {
            FeedbackScheduler s = Sched();
            ParticleSystem a = Sys("a");
            ParticleEmitItem item = With(new ParticleEmitItem(), Set(), 0f, a);
            item.m_Count = 30;
            FeedbackPlayer p = Player(s, item);
            p.Play(PlayArgs.Default.WithIntensity(0.5f)); s.Tick(0f);
            Assert.That(a.particleCount, Is.EqualTo(15), "30 × 0.5");
            p.Play(PlayArgs.Default.WithIntensity(0.01f)); s.Tick(0f);
            Assert.That(a.particleCount, Is.EqualTo(15), "0.3 rounds to none");
        }

        // ---- Spawn (edit mode) — as written: taken, cleared and played · Stop After ends a looping one · Revert · Restore ·
        // backward · a prefab without a system refused
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void PQ08a_TheSpawn()
        {
            FeedbackScheduler s = Sched();
            GameObject anchor = Go("anchor");
            GameObject looping = Template("looping", true, 1f, 0.3f);
            FeedbackPlayer p = Player(s, Spawn(looping, anchor, 2, 0.2f));
            p.Play(); s.Tick(0f);
            PoolService pools = s.Context.PoolsIfMade;
            GameObject live = pools.LiveAt(pools.LiveCount - 1);
            ParticleSystem ps = live.GetComponent<ParticleSystem>();
            Assert.That((ps.isPlaying, ps.particleCount), Is.EqualTo((true, 0)), "played from its start");
            Tick(s, 6);
            Assert.That(pools.LiveCount, Is.Zero, "Stop After ends it");
            FeedbackPlayer pr = Player(s, Spawn(looping, anchor, 2, 0f), Keeper(anchor.transform, 1f));
            pr.Play(); s.Tick(0f);
            pr.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(pools.LiveCount, Is.Zero, "Revert gives it back");
            GameObject none = Go("noSystem");
            none.SetActive(false);
            FeedbackPlayer pn = Player(s, Spawn(none, anchor, 2, 0f));
            Assert.That(pn.Runtime.Binding(0), Is.Null, "refused");
        }

        // a reused instance's particles cleared · the take's component access 0 B after the reserve
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void PQ08c_AReusedOneAndAWarmedTake()
        {
            FeedbackScheduler s = Sched();
            GameObject anchor = Go("anchor2");
            GameObject looping = Template("reuse", true, 1f, 2f);
            FeedbackPlayer p = Player(s, Spawn(looping, anchor, 1, 0f), Keeper(anchor.transform, 1f));
            p.Play(); s.Tick(0f);
            PoolService pools = s.Context.PoolsIfMade;
            GameObject live = pools.LiveAt(pools.LiveCount - 1);
            live.GetComponent<ParticleSystem>().Emit(20);
            p.Stop(StopMode.Revert); s.Tick(0f);
            p.Play(); s.Tick(0f);
            GameObject again = pools.LiveAt(pools.LiveCount - 1);
            Assert.That((ReferenceEquals(again, live), again.GetComponent<ParticleSystem>().particleCount), Is.EqualTo((true, 0)), "the reused one is cleared");
            GameObject fresh = Template("fresh", false, 0.3f, 0.3f);
            pools.Reserve(fresh, 6);
            float f = 0f;
            Assert.That(UIs.AllocatingGCMemory().ApplyTo((TestDelegate)(() => { var x = new int[4]; f += x.Length; })).IsSuccess, Is.True, "positive control");
            int hits = 0;
            for (int k = 0; k < 5; k++)
            {
                GameObject g = pools.Spawn(fresh, true, Vector3.zero, Quaternion.identity, null, 0f, false, null, 0, 0);
                if (UIs.AllocatingGCMemory().ApplyTo((TestDelegate)(() => g.TryGetComponent(out ParticleSystem _))).IsSuccess) hits++;
            }
            // an edit-mode allocation check has its noise — a cold instance is 5/5; the gate is the play mode's
            Assert.That(hits, Is.LessThan(5), "a warmed instance's component — " + hits + "/5");
        }

        // ---- the pools' end (Core)
        [Test]
        public void PQ09_ThePoolsEnd()
        {
            FeedbackScheduler s = Sched();
            PoolService pools = s.Context.Pools;
            GameObject t = Go("t");
            t.SetActive(false);
            pools.Reserve(t, 2);
            bool done = false;
            pools.Spawn(t, true, Vector3.zero, Quaternion.identity, null, 0f, false, null, 0, 0, null, g => done);
            Assert.That(pools.HoldsTimed, Is.True, "an end holds the preview's end");
            s.Tick(Step);
            Assert.That(pools.LiveCount, Is.EqualTo(1));
            done = true;
            s.Tick(Step);
            Assert.That(pools.LiveCount, Is.Zero, "the end gives it back");
            int expired = 0;
            pools.Spawn(t, true, Vector3.zero, Quaternion.identity, null, 0.1f, false, null, 0, 0, g => expired++, g => false);
            Tick(s, 4);
            Assert.That((expired, pools.LiveCount), Is.EqualTo((1, 1)), "an expiry calls — not a return");
        }

        // ---- the simulation's frame — as written
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void PQ10a_TheFrame()
        {
            ParticleSystem a = Sys("a", duration: 100f);
            ParticleSystem parent = Sys("parent", duration: 100f);
            ParticleSystem child = Sys("child", duration: 100f, parent: parent.transform);
            Dictionary<string, string> before = SnapOf(a);
            FeedbackPlayer player = Player(null, Control(a, ParticleAction.Play), Control(parent, ParticleAction.Play), Control(child, ParticleAction.Play, false), With(new ParticleSimulationSpeedItem(), Hold(4f, 1f), 1f, a));
            s_Now += 1.0;
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(12);
            float t1 = a.time;
            Pump(12);
            Assert.That(a.time - t1, Is.GreaterThan(0.3f), "driven at the speed written in the tick (0.1 s × 4)");
            Assert.That(child.time, Is.EqualTo(parent.time).Within(0.02f), "a child under a driven parent once");
            PreviewSession.Current.Stop();
            Assert.That((a.time, a.particleCount, Same(before, SnapOf(a)), SceneManager.GetActiveScene().isDirty), Is.EqualTo((0f, 0, true, false)), "back at the start · serialized · clean");
        }

        // a held one is not driven · a stopped one not driven · the end is stopped · an unscaled one under a hit stop
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void PQ10b_HeldStoppedTheEndUnscaled()
        {
            ParticleSystem a = Sys("held", duration: 100f);
            ParticleSystem idle = Sys("idle", duration: 100f);
            ParticleControlItem pause = Control(a, ParticleAction.Pause);
            pause.m_Timing.m_Delay = 0.2f;
            FeedbackPlayer player = Player(null, Control(a, ParticleAction.Play), pause, With(new ParticleEmissionRateItem(), TweenTo(5f), 1f, idle), Keeper(a, 1f));
            s_Now += 1.0;
            PreviewSession.Start(player, default);
            Pump(36);
            float t1 = a.time;
            Pump(36);
            Assert.That(a.time, Is.EqualTo(t1), "held");
            Assert.That(idle.time, Is.EqualTo(0f), "a stopped one is not driven");
            PreviewSession.Current.Stop();
            Assert.That(a.isStopped, Is.True, "the end stops");
            ParticleSystem u = Sys("unscaled", duration: 100f, unscaled: true);
            ParticleSystem sc = Sys("scaled", duration: 100f);
            var stop = new HitStopItem { m_FreezeDuration = 1f, m_TimeScale = 0f, m_MinimumTimeScale = 0f };
            FeedbackPlayer hp = Player(null, Control(u, ParticleAction.Play), Control(sc, ParticleAction.Play), stop);
            s_Now += 1.0;
            PreviewSession.Start(hp, default);
            Pump(48);
            Assert.That((u.time > 0.2f, sc.time < 0.05f), Is.EqualTo((true, true)), "the unscaled one runs in a hit stop · the scaled one stops");
            PreviewSession.Current?.Stop();
        }

        // ---- the preview — as written
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void PQ11a_ThePreview()
        {
            ParticleSystem a = Sys("a", rate: 10f, playOnAwake: true);
            FeedbackPlayer player = Player(null, With(new ParticleEmissionRateItem(), TweenTo(50f), 0.5f, a));
            s_Now += 1.0;
            PreviewSession.Start(player, default);
            Pump(36);
            Assert.That(a.particleCount, Is.GreaterThan(0), "a play-on-awake system plays");
            Assert.That(PreviewSession.Current.ScrubState(out _, out _, out _), Is.EqualTo(SeekRefusal.Simulated), "no scrub");
            Pump(84);
            Assert.That(PreviewSession.Current, Is.Not.Null, "a looping one holds the end");
            PreviewSession.Current.Stop();
            Assert.That(a.emission.rateOverTimeMultiplier, Is.EqualTo(10f), "the record's row back");
            GameObject anchor = Go("anchor");
            GameObject oneShot = Template("oneShot", false, 0.3f, 0.3f);
            FeedbackPlayer sp = Player(null, Spawn(oneShot, anchor, 2, 0f));
            s_Now += 1.0;
            PreviewSession.Start(sp, default);
            Pump(12);
            PoolService pools = PreviewSession.Current.Scheduler.Context.PoolsIfMade;
            Assert.That(pools.LiveAt(0).GetComponent<ParticleSystem>().particleCount, Is.GreaterThan(0), "the spawn is simulated");
            int pumps = 12;
            while (PreviewSession.Current != null && pumps < 360) { Pump(1); pumps++; }
            Assert.That((PreviewSession.LastEnd, pumps / 120.0 < 1.5), Is.EqualTo((PreviewEndReason.AutoEnd, true)), "it ends after its particles");
        }

        // an emitted burst on a stopped system ages and ends · a one-shot spawn ends at its duration (not before)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void PQ11b_ABurstAndATail()
        {
            ParticleSystem a = Sys("burst", lifetime: 0.5f);
            ParticleEmitItem item = With(new ParticleEmitItem(), Set(), 0f, a);
            item.m_Count = 10;
            FeedbackPlayer player = Player(null, item);
            s_Now += 1.0;
            PreviewSession.Start(player, default);
            Pump(12);
            float t = a.time;
            int pumps = 12;
            while (PreviewSession.Current != null && pumps < 360) { Pump(1); pumps++; }
            Assert.That((t > 0f, pumps / 120.0 > 0.4, a.particleCount), Is.EqualTo((true, true, 0)), "the burst aged, the end waited, gone");
            // a looping spawn's Stop After stops the emission — its tail lives on (the expiry), then it goes back
            GameObject anchor = Go("anchorTail");
            GameObject looping = Template("loopTail", true, 1f, 0.4f);
            FeedbackPlayer sp = Player(null, Spawn(looping, anchor, 2, 0.2f));
            s_Now += 1.0;
            PreviewSession.Start(sp, default);
            Pump(36);
            PoolService pools = PreviewSession.Current.Scheduler.Context.PoolsIfMade;
            Assert.That((pools.LiveCount, pools.LiveCount > 0 && pools.LiveAt(0).GetComponent<ParticleSystem>().particleCount > 0), Is.EqualTo((1, true)), "0.3 s — past Stop After, the tail still shows");
            pumps = 36;
            while (PreviewSession.Current != null && pumps < 360) { Pump(1); pumps++; }
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.AutoEnd), "then it ends");
        }

        // the inactive container clears a system's particles — a pooled take needs no Clear
        [Test]
        public void PQ08b_TheInactiveContainerClears()
        {
            GameObject container = Go("container");
            container.SetActive(false);
            ParticleSystem a = Sys("a");
            a.Emit(20);
            int before = a.particleCount;
            a.transform.SetParent(container.transform, false);
            int asleep = a.particleCount;
            a.transform.SetParent(null, false);
            Assert.That((before, asleep, a.particleCount, a.isPlaying), Is.EqualTo((20, 0, 0, false)));
        }

        // ---- the inspector — no direct write on the instants · the stop's way with a Stop only · the names · the add menu's
        // defaults seen from a new system (speed 1 · rate 10)
        [Test]
        public void PQ18_TheInspectorAndTheDefaults()
        {
            foreach (FeedbackItem one in new FeedbackItem[] { new ParticleControlItem(), new ParticleEmitItem(), new ParticleSpawnItem() })
                Assert.That(ItemRules.Visible(one, "m_DirectWrite"), Is.False, one.GetType().Name + " direct write");
            Assert.That((ItemRules.Visible(new ParticleSimulationSpeedItem(), "m_DirectWrite"), ItemRules.Visible(new ParticleEmissionRateItem(), "m_DirectWrite")), Is.EqualTo((true, true)), "the values write directly");
            Assert.That((ItemRules.Visible(new ParticleControlItem(), "m_StopMode"), ItemRules.Visible(new ParticleControlItem { m_Action = ParticleAction.Stop }, "m_StopMode")), Is.EqualTo((false, true)), "the stop's way with a Stop");
            Assert.That((ItemRules.Label(new ParticleSpawnItem(), "m_Pooled"), ItemRules.Label(new ParticleSpawnItem(), "m_StopAfter"), ItemRules.Label(new ParticleEmitItem(), "m_Count")),
                Is.EqualTo(("Use a Pool", "Stop After (0 = never — a looping system needs it)", "Count (× the play's intensity)")));
            Assert.That(((TweenMotion)MotionDefaults.Create(typeof(ParticleSimulationSpeedItem), MotionKind.Tween, out _)).m_Params.m_To.x, Is.EqualTo(0.25f), "a speed tween to slow motion");
            Assert.That(((FlashMotion)MotionDefaults.Create(typeof(ParticleSimulationSpeedItem), MotionKind.Flash, out _)).m_Params.m_Target.x, Is.EqualTo(3f), "a speed flash to three times");
            Assert.That(((TweenMotion)MotionDefaults.Create(typeof(ParticleEmissionRateItem), MotionKind.Tween, out _)).m_Params.m_To.x, Is.EqualTo(0f), "a rate tween to none");
            Assert.That(((FlashMotion)MotionDefaults.Create(typeof(ParticleEmissionRateItem), MotionKind.Flash, out _)).m_Params.m_Target.x, Is.EqualTo(50f), "a rate flash to five times");
        }

        // ---- the play mode's keep — the enums, a count, a prefab reference, Stop After
        [Test]
        public void PQ19_TheKeepRoundTrips()
        {
            var source = new GameObject("PQ19Prefab");
            source.AddComponent<ParticleSystem>();
            string prefabPath = "Assets/PQ19Prefab.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            Object.DestroyImmediate(source);
            m_Assets.Add(prefabPath);
            var control = new ParticleControlItem { m_Action = ParticleAction.Stop, m_WithChildren = false, m_StopMode = ParticleStopMode.StopEmittingAndClear, m_Motion = Set() };
            var emit = new ParticleEmitItem { m_Count = 7, m_Motion = Set() };
            var spawn = new ParticleSpawnItem { m_Prefab = prefab, m_Pooled = false, m_PoolSize = 3, m_Place = SpawnPlace.World, m_Offset = new Vector3(1f, 2f, 3f), m_MatchRotation = true, m_Parent = SpawnParent.Target, m_StopAfter = 0.75f, m_Motion = Set() };
            FeedbackPlayer a = Go("PQ19From").AddComponent<FeedbackPlayer>();
            a.m_Items.AddRange(new FeedbackItem[] { control, emit, spawn, new ParticleSimulationSpeedItem { m_Motion = TweenTo(2f) }, new ParticleEmissionRateItem { m_Motion = TweenTo(3f) } });
            FeedbackPlayer b = Go("PQ19Into").AddComponent<FeedbackPlayer>();
            PlayModeRecord.Apply(b, PlayModeRecord.Capture(a, false, out int unknown), out PlayModeRecord.Stats stats);
            Assert.That((unknown, stats.Dropped), Is.EqualTo((0, 0)));
            var c2 = (ParticleControlItem)b.m_Items[0];
            var s2 = (ParticleSpawnItem)b.m_Items[2];
            Assert.That((c2.m_Action, c2.m_WithChildren, c2.m_StopMode, ((ParticleEmitItem)b.m_Items[1]).m_Count), Is.EqualTo((ParticleAction.Stop, false, ParticleStopMode.StopEmittingAndClear, 7)));
            Assert.That((s2.m_Prefab, s2.m_Pooled, s2.m_PoolSize, s2.m_Place, s2.m_Offset, s2.m_MatchRotation, s2.m_Parent, s2.m_StopAfter), Is.EqualTo((prefab, false, 3, SpawnPlace.World, new Vector3(1f, 2f, 3f), true, SpawnParent.Target, 0.75f)));
        }

        // ---- the data is Core's — a player with the five items saves and loads with no missing type, in the minimal project too
        // (no Particles assembly)
        [Test]
        public void PQ20_TheSavedFormOutlivesTheModule()
        {
            var source = new GameObject("PQ20");
            var player = source.AddComponent<FeedbackPlayer>();
            player.m_Items.AddRange(new FeedbackItem[] { new ParticleControlItem { m_Action = ParticleAction.Pause, m_Motion = Set() }, new ParticleEmitItem { m_Count = 9, m_Motion = Set() },
                new ParticleSpawnItem { m_StopAfter = 0.5f, m_Motion = Set() }, new ParticleSimulationSpeedItem { m_Motion = TweenTo(2f) }, new ParticleEmissionRateItem { m_Motion = TweenTo(3f) } });
            string path = "Assets/PQ20Player.prefab";
            PrefabUtility.SaveAsPrefabAsset(source, path);
            Object.DestroyImmediate(source);
            m_Assets.Add(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<FeedbackPlayer>();
            Assert.That(SerializationUtility.HasManagedReferencesWithMissingTypes(loaded), Is.False, "no missing type");
            Assert.That(loaded.m_Items.Select(i => i.GetType().Name), Is.EqualTo(new[] { "ParticleControlItem", "ParticleEmitItem", "ParticleSpawnItem", "ParticleSimulationSpeedItem", "ParticleEmissionRateItem" }));
            Assert.That((((ParticleControlItem)loaded.m_Items[0]).m_Action, ((ParticleEmitItem)loaded.m_Items[1]).m_Count, ((ParticleSpawnItem)loaded.m_Items[2]).m_StopAfter), Is.EqualTo((ParticleAction.Pause, 9, 0.5f)));
        }

        // ---- the debugger's Pools line shows a particle spawn's prefab (a GameObject spawn's line)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void PQ22_TheDebuggersPools()
        {
            FeedbackScheduler s = Sched();
            DebuggerSource.Override = s;
            FeedbackDebuggerWindow w = DebuggerTesting.Window();
            GameObject tpl = Template("burst", false, 0.3f, 0.3f);
            FeedbackPlayer p = Player(s, Spawn(tpl, Go("target"), 3, 0f));
            p.Play(); s.Tick(Step);
            w.Refresh();
            Assert.That(w.ServiceLines, Does.Contain("Pool burst — 1 live · 2 resting · 0 grown"));
        }
    }
}
