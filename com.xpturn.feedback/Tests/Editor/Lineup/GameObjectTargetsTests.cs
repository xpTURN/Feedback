using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests.Editor
{
    // a slot type of the test assembly — a component key, a GameObject owner's row. The table maps the key's component to its
    // GameObject
    [RecordPaths(typeof(GameObject), "m_IsActive")]
    internal sealed class GameObjectTestSlot : ToggleSlot
    {
        readonly Component m_Target;

        internal GameObjectTestSlot(Component target)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override bool Read() => m_Target.gameObject.activeSelf;

        protected override void Write(bool value) => m_Target.gameObject.SetActive(value);
    }

    // the pools, the four items, the preview, the inspector, the debugger and the keep (direct Tick on test contexts · the preview
    // on the test's clock — two versions)
    public sealed class GameObjectTargetsTests
    {
        const float Step = 0.05f;
        static readonly PlayArgs Back = PlayArgs.Default.WithDirection(PlayerDirection.Backward);
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_S = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_P = new List<FeedbackPlayer>();
        readonly List<Scene> m_Scenes = new List<Scene>();
        readonly List<string> m_Assets = new List<string>();
        static double s_Now;
        Func<double> m_Clock;

        [SetUp]
        public void SetUp()
        {
            m_Clock = PreviewClock.Source;
            PreviewClock.Source = () => s_Now;
            EnabledTestBehaviour.Enables = 0;
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
            foreach (Scene s in m_Scenes)
                if (s.IsValid() && s.isLoaded) EditorSceneManager.CloseScene(s, true);
            foreach (string a in m_Assets)
                AssetDatabase.DeleteAsset(a);
            foreach (GameObject g in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (g != null && !EditorUtility.IsPersistent(g) && g.name.StartsWith(PropertyLayerRegistry.PreviewPrefix, StringComparison.Ordinal))
                    Object.DestroyImmediate(g);
            }
            m_Made.Clear(); m_S.Clear(); m_P.Clear(); m_Scenes.Clear(); m_Assets.Clear();
            PreviewClock.Source = m_Clock;
            DebuggerSource.Override = null;
            DebuggerTesting.CloseWindows();
        }

        // ---- helpers
        static void Pump(int n) { for (int i = 0; i < n; i++) { s_Now += 1.0 / 120.0; PreviewSession.Current?.Update(); } }
        static void Tick(FeedbackScheduler s, int n) { for (int i = 0; i < n; i++) s.Tick(Step); }
        static SetMotion SetTo(float v, bool relative = false) => new SetMotion { m_Params = new SetParams { m_Value = new Vector4(v, 0, 0, 0), m_Relative = relative } };
        static BlinkMotion BlinkTo(float v) { var b = new BlinkMotion(); b.m_Params.m_Value = new Vector4(v, 0f, 0f, 0f); return b; }

        static T With<T>(T item, FeedbackMotion motion, float duration, Object target) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static SpawnItem Spawn(GameObject prefab, Object target, int size = 3, float lifetime = 0f, bool pooled = true, SpawnPlace place = SpawnPlace.Target, Vector3 offset = default, bool match = false, SpawnParent parent = SpawnParent.None)
        {
            SpawnItem item = With(new SpawnItem(), SetTo(1f), 0f, target);
            item.m_Prefab = prefab;
            item.m_Pooled = pooled;
            item.m_PoolSize = size;
            item.m_Lifetime = lifetime;
            item.m_Place = place;
            item.m_Offset = offset;
            item.m_MatchRotation = match;
            item.m_Parent = parent;
            return item;
        }

        static ComponentEnabledItem Enabled(Object target, string type, int index, FeedbackMotion motion, float duration = 0f)
        {
            ComponentEnabledItem item = With(new ComponentEnabledItem(), motion, duration, target);
            item.m_ComponentType = type;
            item.m_ComponentIndex = index;
            return item;
        }

        static SetLayerItem Layer(GameObject g, int layer)
        {
            SetLayerItem item = With(new SetLayerItem(), SetTo(1f), 0f, g);
            item.m_Layer = layer;
            return item;
        }

        static PositionItem Keeper(float d = 1f)
        {
            var k = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(0.5f, 0.5f, 0.5f, 0f), m_Vibrato = 1f, m_Decay = 0f } } };
            k.m_Timing.m_Duration = d;
            return k;
        }

        GameObject Go(string n) { var g = new GameObject(n); m_Made.Add(g); return g; }
        FeedbackScheduler Sched() { var s = new FeedbackScheduler(ContextKind.Test); m_S.Add(s); return s; }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            FeedbackPlayer p = Go("Player").AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            p.Bind(s);
            p.EnsureRuntime();
            m_P.Add(p);
            return p;
        }

        static bool Bound(FeedbackPlayer p, int i = 0) => p.Runtime != null && p.Runtime.Binding(i) != null;

        GameObject Template(string name, bool script = false)
        {
            GameObject t = Go(name);
            t.AddComponent<MeshFilter>();
            t.AddComponent<MeshRenderer>();
            if (script)
                t.AddComponent<EnabledTestBehaviour>();
            return t;
        }

        Scene OpenScene(string name)
        {
            string path = "Assets/" + name + ".unity";
            File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Scene s = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            m_Scenes.Add(s);
            m_Assets.Add(path);
            return s;
        }

        static void Near(Vector3 actual, Vector3 expected, string what) => Assert.That((actual - expected).sqrMagnitude, Is.LessThan(1e-6f), what + " " + actual);

        static int Temporaries() => Resources.FindObjectsOfTypeAll<GameObject>().Count(o => o != null && !EditorUtility.IsPersistent(o) && o.name.StartsWith(PropertyLayerRegistry.PreviewPrefix, StringComparison.Ordinal));

        // ---- the pools — the accessors · per context · the reserve (the largest · no instance woken) · a take placed before it
        // wakes · given back asleep under the container · the lifetime on two clocks · growth with one warning · a destroyed live
        // one let go · the close · a closed service refuses
        [Test]
        public void GQ03a_TheReserveAndTheAccessors()
        {
            FeedbackScheduler s1 = Sched(), s2 = Sched();
            GameObject tpl = Template("tpl", true);
            GameObject target = Go("target");
            FeedbackPlayer plain = Player(s1, With(new SetActiveItem(), SetTo(1f), 0f, Go("other")));
            plain.Play();
            s1.Tick(Step);
            Assert.That(s1.Context.PoolsIfMade, Is.Null, "a play without a spawn makes no pools");
            int enables = EnabledTestBehaviour.Enables;
            Player(s1, Spawn(tpl, target, size: 2));
            Player(s1, Spawn(tpl, target, size: 1));
            PoolService pools = s1.Context.PoolsIfMade;
            Assert.That(pools, Is.Not.Null, "a spawn's prepare makes them");
            Assert.That((pools.MadeOf(tpl), pools.RestingOf(tpl)), Is.EqualTo((2, 2)), "the largest size asked");
            Assert.That(EnabledTestBehaviour.Enables, Is.EqualTo(enables), "the reserve wakes no instance (the container is off)");
            Assert.That(pools.Container.gameObject.activeSelf, Is.False);
            Assert.That(pools.Container.gameObject.scene, Is.EqualTo(SceneManager.GetActiveScene()), "a test context's container — the active scene");
            Player(s2, Spawn(tpl, target, size: 2));
            Assert.That(s2.Context.PoolsIfMade, Is.Not.SameAs(pools), "a context's own");
        }

        [Test]
        public void GQ03b_ATakeIsPlacedBeforeItWakesAndGoesBackAsleep()
        {
            FeedbackScheduler s = Sched();
            GameObject tpl = Template("tpl", true);
            GameObject target = Go("target");
            target.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));
            FeedbackPlayer p = Player(s, Spawn(tpl, target, size: 2, lifetime: 0.2f, offset: new Vector3(0f, 0f, 1f)));
            PoolService pools = s.Context.PoolsIfMade;
            p.Play();
            s.Tick(Step);
            Assert.That(pools.LiveCount, Is.EqualTo(1));
            GameObject a = pools.LiveAt(0);
            Vector3 want = target.transform.TransformPoint(new Vector3(0f, 0f, 1f));
            Near(a.transform.position, want, "placed");
            Near(EnabledTestBehaviour.EnabledAt, want, "seen by OnEnable");
            Assert.That(a.activeInHierarchy, Is.True);
            Tick(s, 3);
            Assert.That(pools.LiveCount, Is.EqualTo(1), "0.15 s of 0.2");
            Tick(s, 2);
            Assert.That(pools.LiveCount, Is.Zero, "the lifetime gave it back");
            Assert.That((a.activeInHierarchy, a.transform.parent), Is.EqualTo((false, pools.Container)), "asleep under the container");
            Assert.That(pools.RestingOf(tpl), Is.EqualTo(2));
        }

        [Test]
        public void GQ03c_TheLifetimeRunsOnTheSpawnsClock()
        {
            FeedbackScheduler s = Sched();
            GameObject tpl = Template("tpl");
            GameObject target = Go("target");
            SpawnItem scaled = Spawn(tpl, target, size: 2, lifetime: 0.2f), unscaled = Spawn(tpl, target, size: 2, lifetime: 0.2f);
            unscaled.m_Timing.m_Clock = ItemClock.Unscaled;
            FeedbackPlayer ps = Player(s, scaled), pu = Player(s, unscaled);
            FeedbackPlayer pm = Player(s, Spawn(tpl, target, size: 2, lifetime: 0.2f));
            pm.m_TimeMode = PlayerTimeMode.Unscaled;
            pm.Rebuild();
            ps.Play(); pu.Play(); pm.Play();
            for (int i = 0; i < 7; i++)
                s.Tick(new FrameTime(0f, Step));
            PoolService pools = s.Context.PoolsIfMade;
            Assert.That(pools.LiveCount, Is.EqualTo(1), "a scaled dt of 0 for 0.35 s — the scaled one stays, the unscaled item and the unscaled player's go back");
            Tick(s, 6);
            Assert.That(pools.LiveCount, Is.Zero);
        }

        [Test]
        public void GQ03d_GrowthWarnsOnce()
        {
            FeedbackScheduler s = Sched();
            GameObject small = Template("small");
            GameObject target = Go("target");
            FeedbackPlayer p = Player(s, Spawn(small, target, size: 1));
            PoolService pools = s.Context.PoolsIfMade;
            p.Play(); s.Tick(Step);
            LogAssert.Expect(LogType.Warning, string.Format(PoolService.GrowWarning, "small", 2));
            p.Play(); s.Tick(Step);
            p.Play(); s.Tick(Step);
            Assert.That((pools.MadeOf(small), pools.GrownOf(small), pools.LiveCount), Is.EqualTo((3, 2, 3)), "grown by one a take, one warning");
        }

        [Test]
        public void GQ03e_DestroyedOnesTheCloseAndAClosedService()
        {
            FeedbackScheduler s = Sched();
            GameObject tpl = Template("tpl");
            GameObject target = Go("target");
            FeedbackPlayer p = Player(s, Spawn(tpl, target, size: 3));
            PoolService pools = s.Context.PoolsIfMade;
            p.Play(); s.Tick(Step);
            p.Play(); s.Tick(Step);
            Object.DestroyImmediate(pools.LiveAt(1));
            s.Tick(Step);
            Assert.That((pools.LiveCount, pools.MadeOf(tpl)), Is.EqualTo((1, 2)), "a destroyed live one let go");
            Transform c = pools.Container;
            Object.DestroyImmediate(c.GetChild(0).gameObject);
            p.Play(); s.Tick(Step);
            Assert.That(pools.LiveCount, Is.EqualTo(2), "a destroyed resting one is replaced");
            Assert.That(pools.LiveAt(1), Is.Not.Null);
            var live = new List<GameObject> { pools.LiveAt(0), pools.LiveAt(1) };
            s.Shutdown();
            Assert.That(live.All(g => g == null) && c == null, Is.True, "the close destroys every instance and the container");
            Assert.That(pools.IsClosed, Is.True);
            Assert.That(pools.Spawn(tpl, true, Vector3.zero, Quaternion.identity, null, 0f, false, null, 0, 0), Is.Null, "a closed service refuses");
        }

        // a fresh instance's take and a give-back allocate nothing (the reserve read the transforms · the live array holds them all —
        // past the array's first 16)
        [Test]
        public void GQ03f_ATakeAllocatesNothing()
        {
            FeedbackScheduler s = Sched();
            GameObject tpl = Template("alloc");
            GameObject target = Go("target");
            FeedbackPlayer p = Player(s, Spawn(tpl, target, size: 24));
            for (int i = 0; i < 16; i++) { p.Play(); s.Tick(Step); }
            float f = 0f;
            Assert.That(UIs.AllocatingGCMemory().ApplyTo((TestDelegate)(() => { var x = new int[4]; f += x.Length; })).IsSuccess, Is.True, "the positive control");
            int hits = 0;
            for (int i = 0; i < 5; i++)
                if (UIs.AllocatingGCMemory().ApplyTo((TestDelegate)(() => { p.Play(); s.Tick(Step); })).IsSuccess) hits++;
            Assert.That(hits, Is.Zero, "a fresh take allocates");
            PoolService pools = s.Context.PoolsIfMade;
            GameObject one = pools.LiveAt(0);
            pools.TryReturn(one);
            int back = 0;
            for (int i = 0; i < 5; i++)
                if (UIs.AllocatingGCMemory().ApplyTo((TestDelegate)(() => { GameObject g = pools.LiveAt(pools.LiveCount - 1); pools.TryReturn(g); })).IsSuccess) back++;
            Assert.That(back, Is.Zero, "a give-back allocates");
        }

        // ---- a live one under an object of a closed scene is let go (the next update) · the next take
        [Test]
        public void GQ04_AClosedScenesSpawn()
        {
            FeedbackScheduler s = Sched();
            GameObject tpl = Template("tpl");
            Scene scene = OpenScene("GQ04Unload");
            var anchor = new GameObject("anchor");
            SceneManager.MoveGameObjectToScene(anchor, scene);
            FeedbackPlayer p = Player(s, Spawn(tpl, anchor, size: 2, parent: SpawnParent.Target));
            PoolService pools = s.Context.PoolsIfMade;
            p.Play(); s.Tick(Step);
            Assert.That(pools.LiveAt(0).scene, Is.EqualTo(scene), "under the anchor — that scene");
            EditorSceneManager.CloseScene(scene, true);
            m_Scenes.Remove(scene);
            s.Tick(Step);
            Assert.That((pools.LiveCount, pools.MadeOf(tpl)), Is.EqualTo((0, 1)), "let go");
            FeedbackPlayer p2 = Player(s, Spawn(tpl, Go("here"), size: 2));
            p2.Play(); s.Tick(Step);
            Assert.That(pools.LiveCount, Is.EqualTo(1));
        }

        // ---- one an execution · Revert its play's (a Restart holds the earlier play — its spawn stays) · Restore all · backward
        // the forward play's (no keeper) · a repeat's three · a release · a Rebuild
        [Test]
        public void GQ05a_RevertRestoreBackward()
        {
            FeedbackScheduler s = Sched();
            GameObject tpl = Template("tpl");
            GameObject target = Go("target");
            FeedbackPlayer p = Player(s, Spawn(tpl, target, size: 6), Keeper());
            PoolService pools = s.Context.PoolsIfMade;
            p.Play(); Tick(s, 2);
            Assert.That(pools.LiveCount, Is.EqualTo(1), "one a play");
            p.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(pools.LiveCount, Is.Zero, "Revert gives back the play's");
            p.Play(); Tick(s, 2); p.Play(); Tick(s, 2);
            Assert.That(pools.LiveCount, Is.EqualTo(2));
            p.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(pools.LiveCount, Is.EqualTo(1), "a Restart holds the earlier play: its spawn stays");
            p.Restore(); s.Tick(0f);
            Assert.That(pools.LiveCount, Is.Zero, "Restore gives back every one");
            FeedbackPlayer pb = Player(s, Spawn(tpl, target, size: 6));
            pb.Play(); Tick(s, 2);
            pb.Play(in Back); Tick(s, 2);
            Assert.That(pools.LiveCount, Is.Zero, "backward gives back the forward play's, spawns none");
        }

        [Test]
        public void GQ05b_RepeatReleaseRebuild()
        {
            FeedbackScheduler s = Sched();
            GameObject tpl = Template("tpl");
            GameObject target = Go("target");
            SpawnItem rep = Spawn(tpl, target, size: 6);
            rep.m_Timing.m_RepeatCount = 2;
            rep.m_Timing.m_RepeatGap = 0.1f;
            FeedbackPlayer pr = Player(s, rep, Keeper());
            PoolService pools = s.Context.PoolsIfMade;
            pr.Play(); Tick(s, 10);
            Assert.That(pools.LiveCount, Is.EqualTo(3), "three runs in a play");
            pr.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(pools.LiveCount, Is.Zero, "their Revert");
            FeedbackPlayer pl = Player(s, Spawn(tpl, target, size: 6, lifetime: 0.2f), Spawn(tpl, target, size: 6));
            pl.Play(); s.Tick(Step);
            pl.ReleaseRuntime();
            Tick(s, 6);
            Assert.That(pools.LiveCount, Is.EqualTo(1), "released: the lifetime one goes back, the other stays");
            pools.ReturnOwned(null, -1, -1);
            FeedbackPlayer pq = Player(s, Spawn(tpl, target, size: 6), Keeper());
            pq.Play(); Tick(s, 2);
            pq.Rebuild();
            Assert.That((pools.LiveCount, pq.IsPlaying), Is.EqualTo((1, false)), "a Rebuild holds the play");
            pq.Restore(); s.Tick(0f);
            Assert.That(pools.LiveCount, Is.Zero, "the new binding owns it");
        }

        [Test]
        public void GQ05c_PlaceParentAndNotPooled()
        {
            FeedbackScheduler s = Sched();
            GameObject tpl = Template("tpl");
            GameObject target = Go("target");
            target.transform.SetPositionAndRotation(new Vector3(1f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
            FeedbackPlayer pp = Player(s, Spawn(tpl, target, size: 2, offset: new Vector3(0f, 0f, 1f), match: true, parent: SpawnParent.Target),
                Spawn(tpl, target, size: 2, place: SpawnPlace.World, offset: new Vector3(5f, 0f, 0f)));
            PoolService pools = s.Context.PoolsIfMade;
            pp.Play(); s.Tick(Step);
            GameObject a = pools.LiveAt(0), b = pools.LiveAt(1);
            Near(a.transform.position, new Vector3(2f, 0f, 0f), "the target's place, the offset in its space");
            Assert.That(Mathf.DeltaAngle(a.transform.eulerAngles.y, 90f), Is.EqualTo(0f).Within(0.01f), "its rotation");
            Assert.That(a.transform.parent, Is.EqualTo(target.transform), "under the target");
            Near(b.transform.position, new Vector3(5f, 0f, 0f), "a world place");
            Assert.That(b.transform.parent, Is.Null);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no prefab to spawn"));
            Assert.That(Bound(Player(s, Spawn(null, target))), Is.False, "no prefab");
            GameObject tpl2 = Template("tpl2");
            FeedbackPlayer pn = Player(s, Spawn(tpl2, target, pooled: false, lifetime: 0.1f));
            pn.Play(); s.Tick(Step);
            GameObject made = pools.LiveAt(pools.LiveCount - 1);
            Assert.That((made.name.StartsWith("tpl2"), pools.MadeOf(tpl2)), Is.EqualTo((true, 0)), "made, not pooled");
            Tick(s, 4);
            Assert.That(made == null, Is.True, "its lifetime destroys it (DestroyImmediate in the edit mode — Destroy would log an error)");
        }

        // ---- the edit mode: a pooled live one goes back · a scene object hidden, Revert · Restore show it · Set Active's slot
        // and its rule
        [Test]
        public void GQ06_DespawnInTheEditMode()
        {
            FeedbackScheduler s = Sched();
            GameObject tpl = Template("tpl");
            FeedbackPlayer ps = Player(s, Spawn(tpl, Go("target"), size: 3));
            PoolService pools = s.Context.PoolsIfMade;
            ps.Play(); s.Tick(Step);
            GameObject a = pools.LiveAt(0);
            Player(s, With(new DespawnItem(), SetTo(1f), 0f, a)).Play();
            s.Tick(Step);
            Assert.That((a != null, pools.LiveCount, pools.RestingOf(tpl)), Is.EqualTo((true, 0, 3)), "back to its pool");
            GameObject b = Go("scene object");
            FeedbackPlayer ph = Player(s, With(new DespawnItem(), SetTo(1f), 0f, b), Keeper());
            ph.Play(); s.Tick(Step);
            Assert.That((b != null, b.activeSelf), Is.EqualTo((true, false)), "hidden, not destroyed");
            ph.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(b.activeSelf, Is.True, "Revert");
            ph.Play(); s.Tick(Step); ph.Stop(StopMode.Hold); s.Tick(0f); ph.Restore(); s.Tick(0f);
            Assert.That(b.activeSelf, Is.True, "Restore");
            var outcome = new List<bool>();
            foreach (bool despawnFirst in new[] { true, false })
            {
                GameObject c = Go("shared");
                FeedbackPlayer pa = Player(s, With(new SetActiveItem(), SetTo(0f), 0f, c), Keeper());
                FeedbackPlayer pc = Player(s, With(new DespawnItem(), SetTo(1f), 0f, c), Keeper());
                pa.Play(); s.Tick(Step); pc.Play(); s.Tick(Step);
                Assert.That(((DespawnBinding)pc.Runtime.Binding(0)).Hide, Is.SameAs(((SetActiveBinding)pa.Runtime.Binding(0)).Slot), "Set Active's slot");
                (despawnFirst ? pc : pa).Stop(StopMode.Revert); s.Tick(0f);
                (despawnFirst ? pa : pc).Stop(StopMode.Revert); s.Tick(0f);
                outcome.Add(c.activeSelf);
            }
            Assert.That(outcome, Is.EqualTo(new[] { true, false }), "the Set rule: the original after the reverts in the reverse order only");
        }

        // ---- Set Layer — Set · Revert · a game's write · Restore · backward · out of range (no engine error)
        [Test]
        public void GQ07_SetLayer()
        {
            FeedbackScheduler s = Sched();
            GameObject g = Go("layered");
            g.layer = 4;
            FeedbackPlayer p = Player(s, Layer(g, 5), Keeper());
            p.Play(); s.Tick(Step);
            Assert.That(g.layer, Is.EqualTo(5));
            p.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(g.layer, Is.EqualTo(4), "Revert");
            p.Play(); s.Tick(Step); p.Stop(StopMode.Hold); s.Tick(0f);
            g.layer = 8;
            p.Play(); s.Tick(Step); p.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(g.layer, Is.EqualTo(8), "the play's start — the game's layer");
            p.Restore(); s.Tick(0f);
            Assert.That(g.layer, Is.EqualTo(4), "Restore — the first");
            FeedbackPlayer pb = Player(s, Layer(g, 9));
            pb.Play(); s.Tick(Step); pb.Play(in Back); s.Tick(Step);
            Assert.That(g.layer, Is.EqualTo(4), "backward — the origin");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("out of 0 ~ 31"));
            Assert.That(Bound(Player(s, Layer(g, 32))), Is.False, "32 refused — no engine error");
        }

        // ---- Component Enabled — a Light · a script · a renderer (Visibility's slot) · a Collider2D (a Behaviour) · Set ·
        // Revert · Toggle · Restore · Blink (its own context — the flash limit) · refusals · the hook empty
        [Test]
        public void GQ08a_TheKinds()
        {
            FeedbackScheduler s = Sched(), sk = Sched();
            GameObject g = Go("kinds");
            Light light = g.AddComponent<Light>();
            var script = g.AddComponent<EnabledTestBehaviour>();
            MeshRenderer mesh = g.AddComponent<MeshRenderer>();
            GameObject g2 = Go("2d");
            BoxCollider2D box2d = g2.AddComponent<BoxCollider2D>();
            FeedbackPlayer p = Player(s,
                Enabled(g, "UnityEngine.Light", 0, SetTo(0f)),
                Enabled(g, typeof(EnabledTestBehaviour).FullName, 0, SetTo(0f)),
                Enabled(g, "UnityEngine.MeshRenderer", 0, SetTo(0f)),
                Enabled(g2, "UnityEngine.BoxCollider2D", 0, SetTo(0f)),
                With(new RendererVisibilityItem(), SetTo(0f), 0f, mesh),
                Keeper());
            string kinds = string.Join(",", Enumerable.Range(0, 4).Select(i => ((ComponentEnabledBinding)p.Runtime.Binding(i)).Slot.GetType().Name));
            Assert.That(kinds, Is.EqualTo("BehaviourEnabledSlot,BehaviourEnabledSlot,RendererEnabledSlot,BehaviourEnabledSlot"));
            Assert.That(((ComponentEnabledBinding)p.Runtime.Binding(2)).Slot, Is.SameAs(((RendererVisibilityBinding)p.Runtime.Binding(4)).Slot), "Visibility's slot");
            p.Play(); s.Tick(Step);
            Assert.That((light.enabled, script.enabled, mesh.enabled, box2d.enabled), Is.EqualTo((false, false, false, false)), "Set off");
            p.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That((light.enabled, script.enabled, mesh.enabled, box2d.enabled), Is.EqualTo((true, true, true, true)), "Revert");
            FeedbackPlayer pt = Player(s, Enabled(g, "UnityEngine.Light", 0, SetTo(1f, true)), Keeper());
            pt.Play(); s.Tick(Step);
            Assert.That(light.enabled, Is.False, "Toggle");
            pt.Stop(StopMode.Hold); s.Tick(0f); pt.Restore(); s.Tick(0f);
            Assert.That(light.enabled, Is.True, "Restore");
            FeedbackPlayer pk = Player(sk, Enabled(g, "UnityEngine.Light", 0, BlinkTo(0f), 0.5f));
            pk.Play();
            var seen = new List<bool>();
            for (int i = 0; i < 12; i++) { sk.Tick(Step); seen.Add(light.enabled); }
            Assert.That((seen.Contains(false), seen.Contains(true), light.enabled), Is.EqualTo((true, true, true)), "a blink, on at its end");
        }

        [Test]
        public void GQ08b_Refusals()
        {
            FeedbackScheduler s = Sched();
            GameObject g = Go("refused");
            g.AddComponent<AudioSource>();
            g.AddComponent<BoxCollider>();
            g.AddComponent<LODGroup>();
            Assert.That(Bound(Player(s, Enabled(g, "UnityEngine.AudioSource", 1, SetTo(0f)))), Is.False, "an index past");
            Assert.That(Bound(Player(s, Enabled(g, "", 0, SetTo(0f)))), Is.False, "no type");
            Assert.That(Bound(Player(s, Enabled(g, "UnityEngine.Transform", 0, SetTo(0f)))), Is.False, "a Transform");
            Assert.That(Bound(Player(s, Enabled(g, "UnityEngine.LODGroup", 0, SetTo(0f)))), Is.False, "a LODGroup");
            Func<Component, bool> check = LayerHooks.ColliderCheck;
            LayerHooks.ColliderCheck = null;
            try
            {
                Assert.That(Bound(Player(s, Enabled(g, "UnityEngine.BoxCollider", 0, SetTo(0f)))), Is.False, "a Collider without the hooks");
            }
            finally
            {
                LayerHooks.ColliderCheck = check;
            }
        }

        // the Collider through the Physics assembly's hooks (the development project)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void GQ08c_TheColliderHook()
        {
            FeedbackScheduler s = Sched();
            GameObject g = Go("box");
            BoxCollider first = g.AddComponent<BoxCollider>();
            BoxCollider second = g.AddComponent<BoxCollider>();
            FeedbackPlayer p = Player(s, Enabled(g, "UnityEngine.BoxCollider", 1, SetTo(0f)), Keeper());
            Assert.That(((ComponentEnabledBinding)p.Runtime.Binding(0)).Slot, Is.InstanceOf<ColliderEnabledSlot>());
            p.Play(); s.Tick(Step);
            Assert.That((first.enabled, second.enabled), Is.EqualTo((true, false)), "the second");
            p.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(second.enabled, Is.True);
        }

        // ---- a Direct second converted to Self keeps the second · a Direct second after a reorder is that one
        [Test]
        public void GQ09_TheSecondOfAType()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer owner = Go("owner").AddComponent<FeedbackPlayer>();
            m_P.Add(owner);
            AudioSource o1 = owner.gameObject.AddComponent<AudioSource>();
            AudioSource o2 = owner.gameObject.AddComponent<AudioSource>();
            List<FeedbackItem> portable = PresetOps.Portable(owner, new List<FeedbackItem> { Enabled(o2, "UnityEngine.AudioSource", 1, SetTo(0f)) });
            var converted = (ComponentEnabledItem)portable[0];
            Assert.That((converted.m_Target.m_Mode, converted.m_Target.m_Object), Is.EqualTo((TargetRefMode.Self, (Object)null)), "a Self conversion");
            owner.m_Items.AddRange(portable);
            owner.Bind(s);
            owner.EnsureRuntime();
            owner.Play(); s.Tick(Step);
            Assert.That((o1.enabled, o2.enabled), Is.EqualTo((true, false)), "the type's name and index keep the second");
            GameObject h = Go("reorder");
            AudioSource first = h.AddComponent<AudioSource>();
            AudioSource second = h.AddComponent<AudioSource>();
            ComponentEnabledItem item = Enabled(second, "UnityEngine.AudioSource", 1, SetTo(0f));
            ComponentUtility.MoveComponentUp(second);
            Player(s, item).Play();
            s.Tick(Step);
            Assert.That((first.enabled, second.enabled), Is.EqualTo((true, false)), "a Direct component of the name is that component");
        }

        // ---- the preview — Component Enabled · Set Layer · Despawn's hide recorded, back at the stop, a save during is the
        // original · a spawn's hidden temporary (out of the scenes · every instance flagged) · its end: as long as its lifetime, at
        // once without one · a parent Target's instance is in that scene but not in the file
        [Test]
        public void GQ10a_TheRecordedOnesAndASaveDuring()
        {
            Scene scene = OpenScene("GQ10Preview");
            SceneManager.SetActiveScene(scene);
            var light = new GameObject("light").AddComponent<Light>();
            var layered = new GameObject("layered");
            layered.layer = 4;
            var hidden = new GameObject("despawned");
            var anchor = new GameObject("anchor");
            var tpl = new GameObject("tpl");
            tpl.AddComponent<MeshFilter>();
            var player = new GameObject("player").AddComponent<FeedbackPlayer>();
            player.m_Items.Add(Enabled(light, "UnityEngine.Light", 0, SetTo(0f)));
            player.m_Items.Add(Layer(layered, 5));
            player.m_Items.Add(With(new DespawnItem(), SetTo(1f), 0f, hidden));
            player.m_Items.Add(Spawn(tpl, anchor, size: 2, lifetime: 0.5f));
            player.m_Items.Add(Spawn(tpl, anchor, size: 2, lifetime: 0.5f, parent: SpawnParent.Target));
            player.m_Items.Add(Keeper(0.5f));
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            string before = File.ReadAllText(scene.path);
            s_Now += 1.0;
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(6);
            Assert.That((light.enabled, layered.layer, hidden.activeSelf), Is.EqualTo((false, 5, false)), "during");
            GameObject under = anchor.transform.childCount > 0 ? anchor.transform.GetChild(0).gameObject : null;
            Assert.That(under != null && under.scene == scene && under.hideFlags == HideFlags.HideAndDontSave, Is.True, "a parent Target's instance — in the scene, hidden");
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            Assert.That(File.ReadAllText(scene.path), Is.EqualTo(before), "a save during is the original");
            Assert.That((light.enabled, layered.layer, hidden.activeSelf), Is.EqualTo((false, 5, false)), "after the save");
            PreviewSession.Current?.Stop();
            Assert.That((light.enabled, layered.layer, hidden.activeSelf, scene.isDirty), Is.EqualTo((true, 4, true, false)), "the end");
            Assert.That(Temporaries(), Is.Zero, "no temporary left");
        }

        [Test]
        public void GQ10b_ASpawnShowsAsLongAsItLives([Values(0.5f, 0f)] float lifetime)
        {
            Scene scene = OpenScene("GQ10Spawn");
            SceneManager.SetActiveScene(scene);
            var anchor = new GameObject("anchor");
            var tpl = new GameObject("tpl");
            tpl.AddComponent<MeshFilter>();
            var player = new GameObject("player").AddComponent<FeedbackPlayer>();
            player.m_Items.Add(Spawn(tpl, anchor, size: 2, lifetime: lifetime));
            s_Now += 1.0;
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(2);
            if (lifetime > 0f)
            {
                GameObject[] ours = Resources.FindObjectsOfTypeAll<GameObject>().Where(o => o != null && !EditorUtility.IsPersistent(o) && o.name.StartsWith(PropertyLayerRegistry.PreviewPrefix, StringComparison.Ordinal)).ToArray();
                Assert.That(ours.All(o => o.hideFlags == HideFlags.HideAndDontSave), Is.True, "every temporary flagged");
                GameObject live = ours.Single(o => o.activeInHierarchy);
                Assert.That((live.scene.IsValid(), live.name), Is.EqualTo((false, PropertyLayerRegistry.PreviewPrefix + "tpl")), "out of the scenes, the prefix");
            }
            int pumps = 2;
            while (PreviewSession.Current != null && pumps < 400) { Pump(1); pumps++; }
            if (lifetime > 0f)
                Assert.That(pumps / 120.0, Is.GreaterThan(0.4), "the end waited for its lifetime");
            else
                Assert.That(pumps / 120.0, Is.LessThan(0.1), "no lifetime — the end at once");
            Assert.That((PreviewSession.LastEnd, Temporaries(), scene.isDirty), Is.EqualTo((PreviewEndReason.AutoEnd, 0, false)));
        }

        // ---- a missed end — the recovery's pass during the session (the true reload is in PreviewReloadTests) leaves no
        // temporary (a parent Target's one too) and the session ends after it
        [Test]
        public void GQ12a_TheRecoveryLeavesNone()
        {
            Scene scene = OpenScene("GQ12Recover");
            SceneManager.SetActiveScene(scene);
            var anchor = new GameObject("anchor");
            var tpl = new GameObject("tpl");
            tpl.AddComponent<MeshFilter>();
            var player = new GameObject("player").AddComponent<FeedbackPlayer>();
            player.m_Items.Add(Spawn(tpl, anchor, size: 2, lifetime: 5f));
            player.m_Items.Add(Spawn(tpl, anchor, size: 2, lifetime: 5f, parent: SpawnParent.Target));
            s_Now += 1.0;
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(2);
            Assert.That(anchor.transform.childCount, Is.EqualTo(1));
            PreviewRecovery.Run();
            Assert.That((Temporaries(), anchor.transform.childCount), Is.EqualTo((0, 0)), "none left");
            Pump(4);
            PreviewSession.Current?.Stop();
        }

        // ---- the table maps a component key of a GameObject owner's row to the GameObject
        [Test]
        public void GQ14_AGameObjectOwnersRowOnAComponentsKey()
        {
            var table = new PreviewRecordPathTable();
            GameObject g = Go("double");
            Light light = g.AddComponent<Light>();
            var paths = new List<PreviewRecordPath>();
            var key = new LayerKey(light, typeof(GameObjectTestSlot), 0, -1);
            Assert.That(table.Get(in key, paths), Is.EqualTo(PreviewRecordResult.Record));
            Assert.That(paths.Select(p => (p.Owner, p.Path)), Is.EqualTo(new[] { ((Object)g, "m_IsActive") }));
        }

        // ---- the inspector — the component's menu and its two fields, the drop's two fields, the layer's field, the hidden
        // ones, the names, the defaults
        [Test]
        public void GQ17_TheInspectorAndTheDefaults()
        {
            foreach (FeedbackItem one in new FeedbackItem[] { new ComponentEnabledItem(), new SpawnItem(), new DespawnItem(), new SetLayerItem() })
                Assert.That(ItemRules.Visible(one, "m_DirectWrite"), Is.False, one.GetType().Name + " direct write");
            Assert.That(ItemRules.Visible(new ComponentEnabledItem(), "m_ComponentIndex"), Is.False, "the index with the component");
            Assert.That(ItemRules.Label(new ComponentEnabledItem(), "m_Relative"), Is.EqualTo("Toggle"));
            Assert.That(((BlinkMotion)MotionDefaults.Create(typeof(ComponentEnabledItem), MotionKind.Blink, out _)).m_Params.m_Value, Is.EqualTo(Vector4.zero), "a blink turns it off");
            Assert.That(((SetMotion)MotionDefaults.Create(typeof(ComponentEnabledItem), MotionKind.Set, out _)).m_Params.m_Value, Is.EqualTo(Vector4.zero), "a Set off");
            var spawn = new SpawnItem();
            Assert.That((spawn.m_Pooled, spawn.m_PoolSize, spawn.m_Lifetime), Is.EqualTo((true, 5, 1f)), "a spawn's own defaults");
            // a drop: a GameObject's first turned component · a component of the type · a Transform fits not
            GameObject g = Go("drop");
            AudioSource a1 = g.AddComponent<AudioSource>();
            AudioSource a2 = g.AddComponent<AudioSource>();
            var leaf = new CatalogLeaf(typeof(ComponentEnabledItem), (int)MotionKind.Set, "GameObject/Component Enabled", "", true);
            var fromObject = (ComponentEnabledItem)ComponentDrop.Create(leaf, g);
            Assert.That((fromObject.m_Target.m_Object, fromObject.m_ComponentType, fromObject.m_ComponentIndex), Is.EqualTo(((Object)a1, "UnityEngine.AudioSource", 0)));
            var fromSecond = (ComponentEnabledItem)ComponentDrop.Create(leaf, a2);
            Assert.That((fromSecond.m_Target.m_Object, fromSecond.m_ComponentIndex), Is.EqualTo(((Object)a2, 1)), "the second's index");
            Assert.That(ComponentDrop.Suggest(new[] { leaf }, g.transform), Is.Empty, "a Transform is not turned");
            // the menu's candidates and the field's two writes
            Assert.That(ComponentPick.Candidates(g).Select(x => x.Item2), Is.EqualTo(new[] { "AudioSource (0)", "AudioSource (1)" }));
            FeedbackPlayer host = Go("host").AddComponent<FeedbackPlayer>();
            var item = new ComponentEnabledItem { m_Motion = SetTo(0f) };
            item.m_Target.m_Mode = TargetRefMode.Direct;
            item.m_Target.m_Object = g;
            host.m_Items.Add(item);
            var so = new SerializedObject(host);
            SerializedProperty p = so.FindProperty("m_Items").GetArrayElementAtIndex(0);
            var field = new ComponentPickField(p, host);
            Assert.That((field.Text(), field.Target()), Is.EqualTo((ComponentPickField.NoneText, g)));
            field.Choose(a2);
            Assert.That((item.m_ComponentType, item.m_ComponentIndex, item.m_Target.m_Object, field.Text()), Is.EqualTo(("UnityEngine.AudioSource", 1, (Object)a2, "AudioSource (1)")));
            // the body: the component's field and a layer field
            host.m_Items.Add(Layer(g, 3));
            so.Update();
            var body = new ItemBody();
            body.Build(so.FindProperty("m_Items").GetArrayElementAtIndex(0), "GQ17", host);
            Assert.That((body.Q<ComponentPickField>() != null, body.Fields.Contains("m_ComponentIndex")), Is.EqualTo((true, false)));
            body.Build(so.FindProperty("m_Items").GetArrayElementAtIndex(1), "GQ17", host);
            Assert.That(body.Q<UnityEditor.UIElements.LayerField>(), Is.Not.Null, "a layer field");
        }

        // ---- the debugger — the pools read without making them ('not made yet') · a pool's row · the not pooled row
        [Test]
        public void GQ18_TheDebuggersPools()
        {
            FeedbackScheduler s = Sched();
            DebuggerSource.Override = s;
            FeedbackDebuggerWindow w = DebuggerTesting.Window();
            w.Refresh();
            Assert.That(w.ServiceLines, Does.Contain("Pools" + FeedbackDebuggerWindow.NotMadeText));
            Assert.That(s.Context.PoolsIfMade, Is.Null, "the read makes none");
            GameObject tpl = Template("tpl"), tpl2 = Template("tpl2");
            FeedbackPlayer p = Player(s, Spawn(tpl, Go("target"), size: 3), Spawn(tpl2, Go("target2"), pooled: false));
            p.Play(); s.Tick(Step);
            w.Refresh();
            Assert.That(w.ServiceLines, Does.Contain("Pool tpl — 1 live · 2 resting · 0 grown"));
            Assert.That(w.ServiceLines, Does.Contain("Not pooled — 1 live"));
        }

        // ---- the play mode's keep — a prefab reference, the component's name and index, a layer, an offset, the two enums ·
        // a LayerMask · Color32 · Hash128 (the codec takes them as integer children)
        [Test]
        public void GQ19_TheKeepRoundTrips()
        {
            var source = new GameObject("GQ19Prefab");
            string prefabPath = "Assets/GQ19Prefab.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            Object.DestroyImmediate(source);
            m_Assets.Add(prefabPath);
            var spawn = new SpawnItem { m_Prefab = prefab, m_Pooled = false, m_PoolSize = 7, m_Place = SpawnPlace.World, m_Offset = new Vector3(1f, 2f, 3f), m_MatchRotation = true, m_Parent = SpawnParent.Target, m_Lifetime = 0.25f, m_Motion = SetTo(1f) };
            var enabled = new ComponentEnabledItem { m_ComponentType = "UnityEngine.AudioSource", m_ComponentIndex = 1, m_Motion = SetTo(0f) };
            var layer = new SetLayerItem { m_Layer = 9, m_Motion = SetTo(1f) };
            FeedbackPlayer a = Go("GQ19From").AddComponent<FeedbackPlayer>();
            a.m_Items.AddRange(new FeedbackItem[] { spawn, enabled, layer, new DespawnItem { m_Motion = SetTo(1f) } });
            FeedbackPlayer b = Go("GQ19Into").AddComponent<FeedbackPlayer>();
            PlayModeRecord.Apply(b, PlayModeRecord.Capture(a, false, out int unknown), out PlayModeRecord.Stats stats);
            Assert.That((unknown, stats.Dropped), Is.EqualTo((0, 0)));
            var s2 = (SpawnItem)b.m_Items[0];
            Assert.That((s2.m_Prefab, s2.m_Pooled, s2.m_PoolSize, s2.m_Place, s2.m_Offset, s2.m_MatchRotation, s2.m_Parent, s2.m_Lifetime), Is.EqualTo((prefab, false, 7, SpawnPlace.World, new Vector3(1f, 2f, 3f), true, SpawnParent.Target, 0.25f)));
            Assert.That((((ComponentEnabledItem)b.m_Items[1]).m_ComponentType, ((ComponentEnabledItem)b.m_Items[1]).m_ComponentIndex, ((SetLayerItem)b.m_Items[2]).m_Layer), Is.EqualTo(("UnityEngine.AudioSource", 1, 9)));
            var fields = ScriptableObject.CreateInstance<KeepFieldKinds>();
            m_Made.Add(fields);
            fields.m_Mask = (1 << 5) | (1 << 31);
            fields.m_Color32 = new Color32(10, 20, 30, 40);
            fields.m_Hash = Hash128.Compute("GQ19");
            var into = ScriptableObject.CreateInstance<KeepFieldKinds>();
            m_Made.Add(into);
            PlayModeRecord.Apply(into, PlayModeRecord.Capture(fields, false, out int unknownKinds), out _);
            Assert.That(unknownKinds, Is.Zero, "no leaf the record does not hold");
            Assert.That((into.m_Mask.value, into.m_Color32, into.m_Hash), Is.EqualTo((fields.m_Mask.value, fields.m_Color32, fields.m_Hash)));
        }
    }
}
