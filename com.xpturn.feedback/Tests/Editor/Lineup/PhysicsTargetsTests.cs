using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the forces, the velocities, the explosion, 2D, the seed, the body's state, the refusals, the preview's marks, the Scene
    // view, the inspector, the keep and the saved form (direct Tick on test contexts · the preview on the test's clock — two
    // versions). The Physics assemblies' types are not named: a test that plays a physics item is the dev project's. The traps:
    // edit mode syncs no body — a test syncs after placing its bodies and reads the engine's accumulated force · a kinematic
    // body's accumulated force is not read (the engine's value) · a 2D impulse is a velocity at once · the scene of a query is a
    // preview scene's own · the Scene view's callback is counted after the end
    public sealed class PhysicsTargetsTests
    {
        const float Step = 0.05f;
        const string Folder = "Assets/PhysicsTargetsTests";
        const string ScenePath = Folder + "/BQ12.unity";
        const string KinematicWarning = "Setting linear velocity of a kinematic body is not supported";
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_S = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_P = new List<FeedbackPlayer>();
        readonly List<string> m_Logs = new List<string>();
        static double s_Now;
        Func<double> m_Clock;

        [SetUp]
        public void SetUp()
        {
            m_Clock = PreviewClock.Source;
            PreviewClock.Source = () => s_Now;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            PreviewSession.Current?.Stop();
            foreach (FeedbackPlayer p in m_P)
                if (p != null) p.ReleaseRuntime();
            foreach (FeedbackScheduler s in m_S)
                if (!s.IsShutdown) s.Shutdown();
            foreach (Object o in m_Made)
                if (o != null) Object.DestroyImmediate(o);
            m_Made.Clear(); m_S.Clear(); m_P.Clear(); m_Logs.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Folder);
            PreviewClock.Source = m_Clock;
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Warning || type == LogType.Error)
                m_Logs.Add(message);
        }

        int Logged(string part) => m_Logs.Count(m => m.Contains(part));

        static void Pump(int n) { for (int i = 0; i < n; i++) { s_Now += 1.0 / 120.0; PreviewSession.Current?.Update(); } }
        static void Tick(FeedbackScheduler s, int n) { for (int i = 0; i < n; i++) s.Tick(Step); }
        FeedbackScheduler Sched() { var s = new FeedbackScheduler(ContextKind.Test); m_S.Add(s); return s; }
        static bool Near(Vector3 a, Vector3 b, float e = 1e-3f) => (a - b).magnitude <= e;
        static bool Near(Vector2 a, Vector2 b, float e = 1e-3f) => (a - b).magnitude <= e;

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            FeedbackPlayer p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            if (s != null)
            {
                p.Bind(s);
                p.EnsureRuntime();
            }
            m_P.Add(p);
            return p;
        }

        static T With<T>(T item, Object target) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = new SetMotion();
            item.m_Timing.m_Duration = 0f;
            return item;
        }

        GameObject Go(string name, Vector3 at, Quaternion rotation = default)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            go.transform.SetPositionAndRotation(at, rotation == default ? Quaternion.identity : rotation);
            return go;
        }

        Rigidbody Body(string name, Vector3 at, float mass = 1f, Quaternion rotation = default)
        {
            GameObject go = Go(name, at, rotation);
            go.AddComponent<SphereCollider>();
            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.mass = mass;
            return rb;
        }

        Rigidbody2D Body2D(string name, Vector3 at, float angle = 0f)
        {
            GameObject go = Go(name, at, Quaternion.Euler(0f, 0f, angle));
            go.AddComponent<CircleCollider2D>();
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            return rb;
        }

        static RigidbodyForceItem Force(Rigidbody rb, PhysicsForceMode mode, Vector3 v, PhysicsForceKind kind = PhysicsForceKind.Force, TransformSpace space = TransformSpace.World)
        {
            RigidbodyForceItem item = With(new RigidbodyForceItem(), rb);
            item.m_Mode = mode; item.m_Min = v; item.m_Max = v; item.m_Kind = kind; item.m_Space = space;
            return item;
        }

        static RigidbodyVelocityItem Velocity(Rigidbody rb, VelocityWrite write, Vector3 v, TransformSpace space = TransformSpace.World)
        {
            RigidbodyVelocityItem item = With(new RigidbodyVelocityItem(), rb);
            item.m_Write = write; item.m_Min = v; item.m_Max = v; item.m_Space = space;
            return item;
        }

        static Rigidbody2DForceItem Force2D(Rigidbody2D rb, PhysicsForceMode2D mode, Vector2 v, PhysicsForceKind kind = PhysicsForceKind.Force, TransformSpace space = TransformSpace.World, float torque = 0f)
        {
            Rigidbody2DForceItem item = With(new Rigidbody2DForceItem(), rb);
            item.m_Mode = mode; item.m_Min = v; item.m_Max = v; item.m_Kind = kind; item.m_Space = space; item.m_MinTorque = torque; item.m_MaxTorque = torque;
            return item;
        }

        static Rigidbody2DVelocityItem Velocity2D(Rigidbody2D rb, VelocityWrite write, Vector2 v, TransformSpace space = TransformSpace.World)
        {
            Rigidbody2DVelocityItem item = With(new Rigidbody2DVelocityItem(), rb);
            item.m_Write = write; item.m_Min = v; item.m_Max = v; item.m_Space = space;
            return item;
        }

        // ---- the bodies' names resolve (the minimal editor too — it references the module)
        [Test]
        public void BQ02_TheRequiresName()
        {
            foreach (Type t in new[] { typeof(RigidbodyForceItem), typeof(RigidbodyVelocityItem) })
                Assert.That(TargetChecks.Requires(t)?.FullName, Is.EqualTo("UnityEngine.Rigidbody"), t.Name);
            foreach (Type t in new[] { typeof(Rigidbody2DForceItem), typeof(Rigidbody2DVelocityItem) })
                Assert.That(TargetChecks.Requires(t)?.FullName, Is.EqualTo("UnityEngine.Rigidbody2D"), t.Name);
            Assert.That(TargetChecks.Requires(typeof(RigidbodyExplosionItem)), Is.EqualTo(typeof(Transform)));
        }

        // ---- 3D Force — the four modes mapped (mass 2 · (1, 0, 0) → the engine's accumulated 1 · 2 · 50 · 100) · a torque · the
        // body's own axes · the intensity · each axis in its range
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void BQ04a_TheForce()
        {
            FeedbackScheduler s = Sched();
            var bodies = new Rigidbody[4];
            var items = new List<FeedbackItem>();
            for (int i = 0; i < 4; i++)
            {
                bodies[i] = Body("Mode" + i, new Vector3(i * 3f, 0f, 0f), 2f);
                items.Add(Force(bodies[i], (PhysicsForceMode)i, Vector3.right));
            }
            Rigidbody torque = Body("Torque", new Vector3(0f, 5f, 0f));
            items.Add(Force(torque, PhysicsForceMode.Force, Vector3.up, PhysicsForceKind.Torque));
            Rigidbody local = Body("Local", new Vector3(3f, 5f, 0f), 1f, Quaternion.Euler(0f, 90f, 0f));
            items.Add(Force(local, PhysicsForceMode.Force, Vector3.right, PhysicsForceKind.Force, TransformSpace.Local));
            Rigidbody localTorque = Body("LocalTorque", new Vector3(6f, 5f, 0f), 1f, Quaternion.Euler(0f, 90f, 0f));
            items.Add(Force(localTorque, PhysicsForceMode.Force, Vector3.right, PhysicsForceKind.Torque, TransformSpace.Local));
            Rigidbody range = Body("Range", new Vector3(9f, 5f, 0f));
            RigidbodyForceItem r = Force(range, PhysicsForceMode.Force, Vector3.zero);
            r.m_Max = new Vector3(10f, 10f, 10f);
            items.Add(r);
            Physics.SyncTransforms();
            Player(s, items.ToArray()).Play(); Tick(s, 1);
            float[] want = { 1f, 2f, 50f, 100f };
            Assert.That(Enumerable.Range(0, 4).Select(i => bodies[i].GetAccumulatedForce().x), Is.EqualTo(want).Within(0.01f), "the modes");
            Assert.That((Near(torque.GetAccumulatedTorque(), Vector3.up), Near(torque.GetAccumulatedForce(), Vector3.zero)), Is.EqualTo((true, true)), "a torque, no force");
            Assert.That((Near(local.GetAccumulatedForce(), new Vector3(0f, 0f, -1f)), Near(localTorque.GetAccumulatedTorque(), new Vector3(0f, 0f, -1f))), Is.EqualTo((true, true)), "the body's own axes");
            Vector3 f = range.GetAccumulatedForce();
            Assert.That(f.x >= 0f && f.x <= 10f && f.y >= 0f && f.y <= 10f && f.z >= 0f && f.z <= 10f && f.x != f.y, Is.True, "each axis in its range " + f);
            Rigidbody half = Body("Intensity", new Vector3(12f, 5f, 0f));
            Physics.SyncTransforms();
            Player(s, Force(half, PhysicsForceMode.Force, new Vector3(0f, 4f, 0f))).Play(PlayArgs.Default.WithIntensity(0.5f)); Tick(s, 1);
            Assert.That(half.GetAccumulatedForce().y, Is.EqualTo(2f).Within(1e-3f), "× the intensity");
        }

        // ---- Velocity — Set · Add · the body's own axes · the intensity
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void BQ06a_TheVelocity()
        {
            FeedbackScheduler s = Sched();
            Rigidbody set = Body("Set", Vector3.zero);
            Rigidbody add = Body("Add", new Vector3(3f, 0f, 0f));
            add.linearVelocity = Vector3.right;
            Rigidbody local = Body("Local", new Vector3(6f, 0f, 0f), 1f, Quaternion.Euler(0f, 90f, 0f));
            Rigidbody half = Body("Intensity", new Vector3(9f, 0f, 0f));
            Physics.SyncTransforms();
            Player(s, Velocity(set, VelocityWrite.Set, new Vector3(0f, 5f, 0f)), Velocity(add, VelocityWrite.Add, new Vector3(0f, 5f, 0f)), Velocity(local, VelocityWrite.Set, Vector3.right, TransformSpace.Local)).Play();
            Player(s, Velocity(half, VelocityWrite.Set, new Vector3(0f, 4f, 0f))).Play(PlayArgs.Default.WithIntensity(0.5f));
            Tick(s, 1);
            Assert.That(set.linearVelocity, Is.EqualTo(new Vector3(0f, 5f, 0f)), "Set");
            Assert.That(add.linearVelocity, Is.EqualTo(new Vector3(1f, 5f, 0f)), "Add");
            Assert.That(Near(local.linearVelocity, new Vector3(0f, 0f, -1f)), Is.True, "the body's own axes " + local.linearVelocity);
            Assert.That(half.linearVelocity.y, Is.EqualTo(2f).Within(1e-4f), "× the intensity");
        }

        // the body's rotation, not the transform's — turned after the last sync, physics still at identity
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void BQ06b_TheBodysRotation()
        {
            FeedbackScheduler s = Sched();
            Rigidbody body = Body("Unsynced", Vector3.zero);
            Physics.SyncTransforms();
            body.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            Player(s, Velocity(body, VelocityWrite.Set, Vector3.right, TransformSpace.Local)).Play(); Tick(s, 1);
            Assert.That(Near(body.linearVelocity, Vector3.right), Is.True, body.linearVelocity.ToString());
        }

        // ---- Explosion — the bodies in the radius, a body of two colliders once (an impulse of 10 × (1 − 1.5 / 5) = 7 → 350 a
        // step) · out of the radius · another layer · a trigger (off, then on) · a collider without a body
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void BQ07a_TheExplosion()
        {
            FeedbackScheduler s = Sched();
            Transform center = Go("Center", Vector3.zero).transform;
            Rigidbody compound = Body("Compound", new Vector3(2f, 0f, 0f));
            var part = new GameObject("Part");
            part.transform.SetParent(compound.transform, false);
            part.transform.localPosition = new Vector3(0.5f, 0f, 0f);
            part.AddComponent<BoxCollider>();
            Rigidbody far = Body("Far", new Vector3(8f, 0f, 0f));
            Rigidbody layer = Body("Layer5", new Vector3(0f, 0f, 3f));
            layer.gameObject.layer = 5;
            Rigidbody trigger = Body("Trigger", new Vector3(-3f, 0f, 0f));
            trigger.GetComponent<SphereCollider>().isTrigger = true;
            Go("Static", new Vector3(0f, -3f, 0f)).AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            RigidbodyExplosionItem e = With(new RigidbodyExplosionItem(), center);
            e.m_Layers = ~(1 << 5);
            Player(s, e).Play(); Tick(s, 1);
            Assert.That(compound.GetAccumulatedForce().x, Is.EqualTo(350f).Within(0.5f), "once");
            Assert.That((far.GetAccumulatedForce(), layer.GetAccumulatedForce(), trigger.GetAccumulatedForce()), Is.EqualTo((Vector3.zero, Vector3.zero, Vector3.zero)), "out of the radius · another layer · a trigger");
            RigidbodyExplosionItem t = With(new RigidbodyExplosionItem(), center);
            t.m_Triggers = true;
            t.m_Layers = ~(1 << 5);
            Player(s, t).Play(); Tick(s, 1);
            Assert.That(trigger.GetAccumulatedForce().x, Is.EqualTo(-250f).Within(0.5f), "the trigger with Include Triggers");
        }

        // the play's position is the centre · a buffer of one finds one · it warns once over two plays
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void BQ07b_ThePositionTheBufferTheWarning()
        {
            FeedbackScheduler s = Sched();
            Transform center = Go("Center", Vector3.zero).transform;
            Rigidbody b = Body("B", new Vector3(20f, 0f, 2f));
            Rigidbody c = Body("C", new Vector3(20f, 0f, -2f));
            Physics.SyncTransforms();
            RigidbodyExplosionItem e = With(new RigidbodyExplosionItem(), center);
            e.m_MaxBodies = 1;
            FeedbackPlayer p = Player(s, e);
            p.Play(PlayArgs.Default.WithPosition(new Vector3(20f, 0f, 0f))); Tick(s, 1);
            p.Play(PlayArgs.Default.WithPosition(new Vector3(20f, 0f, 0f))); Tick(s, 1);
            bool pushedB = b.GetAccumulatedForce().sqrMagnitude > 0f, pushedC = c.GetAccumulatedForce().sqrMagnitude > 0f;
            Assert.That(pushedB || pushedC, Is.True, "the play's position");
            Assert.That(pushedB != pushedC, Is.True, "one in a buffer of one");
            Assert.That(Logged("raise Max Bodies"), Is.EqualTo(1), "once over two plays");
        }

        // the target's local offset (a turned target) · the target's own physics scene (a preview scene has its own)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void BQ07c_TheOffsetAndTheScene()
        {
            FeedbackScheduler s = Sched();
            Transform center = Go("Center", new Vector3(0f, 0f, -40f), Quaternion.Euler(0f, 180f, 0f)).transform;
            Rigidbody near = Body("Near", new Vector3(0f, 0f, -48f));
            Rigidbody atTarget = Body("AtTarget", new Vector3(0f, 0f, -34f));
            Physics.SyncTransforms();
            RigidbodyExplosionItem e = With(new RigidbodyExplosionItem(), center);
            e.m_Offset = new Vector3(0f, 0f, 10f);
            Player(s, e).Play(); Tick(s, 1);
            Assert.That((near.GetAccumulatedForce().sqrMagnitude > 0f, atTarget.GetAccumulatedForce() == Vector3.zero), Is.EqualTo((true, true)), "the centre at TransformPoint(offset)");
            Scene local = EditorSceneManager.NewPreviewScene();
            try
            {
                Transform there = Go("There", new Vector3(50f, 0f, 0f)).transform;
                Rigidbody inLocal = Body("InLocal", new Vector3(52f, 0f, 0f));
                Rigidbody inMain = Body("InMain", new Vector3(52f, 0f, 0f));
                SceneManager.MoveGameObjectToScene(there.gameObject, local);
                SceneManager.MoveGameObjectToScene(inLocal.gameObject, local);
                Physics.SyncTransforms();
                Player(s, With(new RigidbodyExplosionItem(), there)).Play(); Tick(s, 1);
                Assert.That((inLocal.GetAccumulatedForce().sqrMagnitude > 0f, inMain.GetAccumulatedForce() == Vector3.zero), Is.EqualTo((true, true)), "the target's physics scene only");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(local);
            }
        }

        // one force drawn for all — two bodies at the same distance take the same
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void BQ07d_OneForceForAll()
        {
            FeedbackScheduler s = Sched();
            Transform center = Go("Center", Vector3.zero).transform;
            Rigidbody left = Body("Left", new Vector3(-2f, 0f, 0f));
            Rigidbody right = Body("Right", new Vector3(2f, 0f, 0f));
            Physics.SyncTransforms();
            RigidbodyExplosionItem e = With(new RigidbodyExplosionItem(), center);
            e.m_MinForce = 0f;
            e.m_MaxForce = 20f;
            Player(s, e).Play(PlayArgs.Default.WithSeed(7)); Tick(s, 1);
            Assert.That(left.GetAccumulatedForce().magnitude, Is.GreaterThan(0f));
            Assert.That(left.GetAccumulatedForce().magnitude, Is.EqualTo(right.GetAccumulatedForce().magnitude).Within(0.01f));
            // × the play's intensity — 10 × 0.5 × (1 − 1.5 / 5) = 3.5 → 175 a step
            Transform second = Go("Second", new Vector3(0f, 0f, 30f)).transform;
            Rigidbody half = Body("Half", new Vector3(2f, 0f, 30f));
            Physics.SyncTransforms();
            Player(s, With(new RigidbodyExplosionItem(), second)).Play(PlayArgs.Default.WithIntensity(0.5f)); Tick(s, 1);
            Assert.That(half.GetAccumulatedForce().x, Is.EqualTo(175f).Within(0.5f), "× the intensity");
        }

        // ---- 2D — a Force mode accumulates · a torque (radians — reads its own field) · the body's own axes · Set · Add · a
        // static body · a kinematic body's force · a body not simulated — skipped without a warning
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void BQ08a_TwoD()
        {
            FeedbackScheduler s = Sched();
            Rigidbody2D f = Body2D("Force", Vector3.zero);
            Rigidbody2D tq = Body2D("Torque", new Vector3(3f, 0f, 0f));
            Rigidbody2D loc = Body2D("Local", new Vector3(6f, 0f, 0f), 90f);
            Rigidbody2D set = Body2D("Set", new Vector3(9f, 0f, 0f));
            Rigidbody2D add = Body2D("Add", new Vector3(12f, 0f, 0f));
            add.linearVelocity = Vector2.right;
            Rigidbody2D vloc = Body2D("VelocityLocal", new Vector3(15f, 0f, 0f), 90f);
            Rigidbody2D still = Body2D("Static", new Vector3(18f, 0f, 0f));
            still.bodyType = RigidbodyType2D.Static;
            Rigidbody2D kin = Body2D("KinematicForce", new Vector3(21f, 0f, 0f));
            kin.bodyType = RigidbodyType2D.Kinematic;
            Rigidbody2D off = Body2D("NotSimulated", new Vector3(24f, 0f, 0f));
            off.simulated = false;
            Rigidbody2D offForce = Body2D("NotSimulatedForce", new Vector3(27f, 0f, 0f));
            offForce.simulated = false;
            Physics2D.SyncTransforms();
            Player(s,
                Force2D(f, PhysicsForceMode2D.Force, new Vector2(0f, 5f)),
                Force2D(tq, PhysicsForceMode2D.Force, new Vector2(3f, 3f), PhysicsForceKind.Torque, TransformSpace.World, 2f),
                Force2D(loc, PhysicsForceMode2D.Force, Vector2.right, PhysicsForceKind.Force, TransformSpace.Local),
                Velocity2D(set, VelocityWrite.Set, new Vector2(0f, 5f)),
                Velocity2D(add, VelocityWrite.Add, new Vector2(0f, 5f)),
                Velocity2D(vloc, VelocityWrite.Set, Vector2.right, TransformSpace.Local),
                Velocity2D(still, VelocityWrite.Set, new Vector2(0f, 5f)),
                Force2D(kin, PhysicsForceMode2D.Impulse, new Vector2(0f, 5f)),
                Velocity2D(off, VelocityWrite.Set, new Vector2(0f, 5f)),
                Force2D(offForce, PhysicsForceMode2D.Force, new Vector2(0f, 5f))).Play();
            Tick(s, 1);
            Assert.That(Near(f.totalForce, new Vector2(0f, 5f)), Is.True, "a force");
            Assert.That(tq.totalTorque, Is.EqualTo(2f).Within(1e-4f), "a torque — its own field");
            Assert.That(Near(loc.totalForce, new Vector2(0f, 1f)), Is.True, "the body's own axes " + loc.totalForce);
            Assert.That((set.linearVelocity, add.linearVelocity), Is.EqualTo((new Vector2(0f, 5f), new Vector2(1f, 5f))), "Set · Add");
            Assert.That(Near(vloc.linearVelocity, new Vector2(0f, 1f)), Is.True, "a velocity on the body's axes " + vloc.linearVelocity);
            Assert.That((still.linearVelocity, kin.linearVelocity, kin.totalForce, off.linearVelocity, offForce.totalForce), Is.EqualTo((Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero)), "skipped");
            Assert.That(m_Logs, Is.Empty, "no engine warning");
        }

        // an Impulse is a velocity at once (it does not accumulate) · a kinematic body's velocity is written
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void BQ08b_TheImpulseAndTheKinematicVelocity()
        {
            FeedbackScheduler s = Sched();
            Rigidbody2D imp = Body2D("Impulse", Vector3.zero);
            Rigidbody2D kin = Body2D("Kinematic", new Vector3(3f, 0f, 0f));
            kin.bodyType = RigidbodyType2D.Kinematic;
            Physics2D.SyncTransforms();
            Player(s, Force2D(imp, PhysicsForceMode2D.Impulse, new Vector2(0f, 5f)), Velocity2D(kin, VelocityWrite.Set, new Vector2(0f, 5f))).Play();
            Tick(s, 1);
            Assert.That((imp.totalForce, imp.linearVelocity, kin.linearVelocity), Is.EqualTo((Vector2.zero, new Vector2(0f, 5f), new Vector2(0f, 5f))));
        }

        // 2D's intensity · seed · backward — the same forms as 3D's
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void BQ08d_TwoDIntensitySeedAndBackward()
        {
            FeedbackScheduler s = Sched();
            Rigidbody2D half = Body2D("Half", Vector3.zero);
            Rigidbody2D halfTorque = Body2D("HalfTorque", new Vector3(3f, 0f, 0f));
            Physics2D.SyncTransforms();
            Player(s, Force2D(half, PhysicsForceMode2D.Force, new Vector2(0f, 4f)), Force2D(halfTorque, PhysicsForceMode2D.Force, Vector2.zero, PhysicsForceKind.Torque, TransformSpace.World, 2f))
                .Play(PlayArgs.Default.WithIntensity(0.5f));
            Tick(s, 1);
            Assert.That(Near(half.totalForce, new Vector2(0f, 2f)), Is.True, "a force × the intensity " + half.totalForce);
            Assert.That(halfTorque.totalTorque, Is.EqualTo(1f).Within(1e-4f), "a torque × the intensity");
            Rigidbody2D seeded = Body2D("Seeded", new Vector3(6f, 0f, 0f));
            Rigidbody2DVelocityItem rnd = Velocity2D(seeded, VelocityWrite.Set, Vector2.zero);
            rnd.m_Max = new Vector2(10f, 10f);
            FeedbackPlayer pr = Player(s, rnd);
            var seen = new List<Vector2>();
            foreach (uint seed in new uint[] { 1, 2, 1 }) { pr.Play(PlayArgs.Default.WithSeed(seed)); s.Tick(0f); seen.Add(seeded.linearVelocity); }
            Assert.That((seen[0] == seen[2], seen[0] != seen[1]), Is.EqualTo((true, true)), "the play's seed");
            Rigidbody2D backForce = Body2D("BackForce", new Vector3(9f, 0f, 0f));
            Rigidbody2D backVelocity = Body2D("BackVelocity", new Vector3(12f, 0f, 0f));
            Physics2D.SyncTransforms();
            Player(s, Force2D(backForce, PhysicsForceMode2D.Force, new Vector2(0f, 5f)), Velocity2D(backVelocity, VelocityWrite.Set, new Vector2(0f, 5f)))
                .Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward));
            Tick(s, 1);
            Assert.That((backForce.totalForce, backVelocity.linearVelocity), Is.EqualTo((Vector2.zero, Vector2.zero)), "backward");
        }

        // ---- the play's seed — the same vector for a seed, another for another · each occurrence of a repeat its own · a
        // backward play does nothing · Revert and Restore put nothing back
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void BQ09a_TheSeedTheOccurrencesBackAndRevert()
        {
            FeedbackScheduler s = Sched();
            Rigidbody a = Body("Seed", Vector3.zero);
            RigidbodyVelocityItem rnd = Velocity(a, VelocityWrite.Set, Vector3.zero);
            rnd.m_Max = new Vector3(10f, 10f, 10f);
            FeedbackPlayer pr = Player(s, rnd);
            var seen = new List<Vector3>();
            for (int i = 0; i < 6; i++) { pr.Play(PlayArgs.Default.WithSeed((uint)(i % 3 + 1))); s.Tick(0f); seen.Add(a.linearVelocity); }
            Assert.That((seen[0] == seen[3], seen[1] == seen[4], seen[2] == seen[5], seen[0] != seen[1], seen[1] != seen[2]), Is.EqualTo((true, true, true, true, true)));
            Rigidbody o = Body("Occurrences", new Vector3(3f, 0f, 0f));
            RigidbodyVelocityItem rep = Velocity(o, VelocityWrite.Set, Vector3.zero);
            rep.m_Max = new Vector3(10f, 10f, 10f);
            rep.m_Timing.m_RepeatCount = 2;
            rep.m_Timing.m_RepeatGap = 0.1f;
            Player(s, rep).Play(PlayArgs.Default.WithSeed(5));
            var occurrences = new List<Vector3> { o.linearVelocity };
            for (int i = 0; i < 12; i++)
            {
                s.Tick(0.025f);
                if (o.linearVelocity != occurrences[occurrences.Count - 1])
                    occurrences.Add(o.linearVelocity);
            }
            Assert.That(occurrences.Count, Is.EqualTo(3), "three occurrences, three vectors");
            Rigidbody back = Body("Back", new Vector3(6f, 0f, 0f));
            Rigidbody backForce = Body("BackForce", new Vector3(9f, 0f, 0f));
            Transform backCenter = Go("BackCenter", new Vector3(0f, 0f, 40f)).transform;
            Rigidbody blasted = Body("Blasted", new Vector3(2f, 0f, 40f));
            Physics.SyncTransforms();
            FeedbackPlayer pb = Player(s, Velocity(back, VelocityWrite.Set, new Vector3(0f, 5f, 0f)), Force(backForce, PhysicsForceMode.Impulse, Vector3.up), With(new RigidbodyExplosionItem(), backCenter));
            pb.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward)); Tick(s, 1);
            Assert.That((back.linearVelocity, backForce.GetAccumulatedForce(), blasted.GetAccumulatedForce()), Is.EqualTo((Vector3.zero, Vector3.zero, Vector3.zero)), "backward");
            pb.Play(); s.Tick(0f);
            pb.Stop(StopMode.Revert); s.Tick(0f);
            pb.Restore();
            Assert.That(back.linearVelocity, Is.EqualTo(new Vector3(0f, 5f, 0f)), "nothing put back");
        }

        // ---- the body's state — a kinematic body's velocity is skipped (no engine warning) · an inactive body's too (6000.3
        // keeps a velocity written while inactive — 6000.6 drops it) · a sleeping body wakes in play (Tests.Physics)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void BQ10a_TheBodysState()
        {
            FeedbackScheduler s = Sched();
            Rigidbody kinematic = Body("Kinematic", Vector3.zero);
            kinematic.isKinematic = true;
            Rigidbody inactive = Body("Inactive", new Vector3(3f, 0f, 0f));
            inactive.gameObject.SetActive(false);
            Physics.SyncTransforms();
            Player(s, Velocity(kinematic, VelocityWrite.Set, new Vector3(0f, 5f, 0f)), Velocity(inactive, VelocityWrite.Set, new Vector3(0f, 5f, 0f))).Play();
            Tick(s, 1);
            inactive.gameObject.SetActive(true);
            Assert.That((kinematic.linearVelocity, inactive.linearVelocity), Is.EqualTo((Vector3.zero, Vector3.zero)));
            Assert.That(Logged(KinematicWarning), Is.Zero, "no engine warning");
        }

        // ---- the refusals — no body on the target · no motion · a motion the item doesn't take · an explosion without a centre
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void BQ11a_TheRefusals()
        {
            FeedbackScheduler s = Sched();
            GameObject bare = Go("Bare", Vector3.zero);
            Rigidbody body = Body("Body", new Vector3(3f, 0f, 0f));
            RigidbodyForceItem noMotion = With(new RigidbodyForceItem(), body);
            noMotion.m_Motion = null;
            RigidbodyVelocityItem tween = With(new RigidbodyVelocityItem(), body);
            tween.m_Motion = new TweenMotion();
            RigidbodyExplosionItem nowhere = With(new RigidbodyExplosionItem(), null);
            FeedbackItem[] items = { With(new RigidbodyForceItem(), bare), With(new Rigidbody2DVelocityItem(), bare), noMotion, tween, nowhere };
            var refused = new List<int>();
            for (int i = 0; i < items.Length; i++)
            {
                FeedbackPlayer p = Player(s, items[i]);
                if (p.Runtime.Binding(0) == null)
                    refused.Add(i);
            }
            Assert.That(refused, Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
        }

        // ---- the preview — nothing written · a mark each (an explosion's centre and radius, a mark at each body it finds — one
        // moved after the last sync is found where it is now) · a local item's mark in world space · the notice · the end waits a
        // mark's second · the scene clean · a repeating session keeps only the live marks
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void BQ12a_ThePreview()
        {
            Rigidbody a = Body("Force", Vector3.zero);
            Rigidbody b = Body("Velocity", new Vector3(3f, 0f, 0f));
            Rigidbody l = Body("Local", new Vector3(6f, 0f, 0f), 1f, Quaternion.Euler(0f, 90f, 0f));
            Rigidbody c = Body("Near", new Vector3(0f, 0f, 2f));
            Physics.SyncTransforms();
            c.transform.position = new Vector3(0f, 0f, 32f);
            Transform center = Go("Center", new Vector3(0f, 0f, 30f)).transform;
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "PhysicsTargetsTests");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            FeedbackPlayer player = Player(null, Force(a, PhysicsForceMode.Impulse, Vector3.up), Velocity(b, VelocityWrite.Set, new Vector3(0f, 5f, 0f)),
                Force(l, PhysicsForceMode.Impulse, Vector3.right, PhysicsForceKind.Force, TransformSpace.Local), With(new RigidbodyExplosionItem(), center));
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(1);
            PreviewMarks marks = PreviewSession.Current.Scheduler.Context.Marks;
            var list = Enumerable.Range(0, marks.Count).Select(i => marks[i]).ToList();
            var notices = new List<PreviewNotice>();
            PreviewNotices.Collect(PreviewSession.Current, notices);
            Assert.That((a.GetAccumulatedForce(), b.linearVelocity, l.GetAccumulatedForce(), c.GetAccumulatedForce()), Is.EqualTo((Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero)), "nothing written");
            Assert.That(list.Count(m => m.Kind == PreviewMarkKind.Velocity && Near(m.Origin, b.transform.position) && Near(m.Vector, new Vector3(0f, 5f, 0f))), Is.EqualTo(1), "the velocity's mark");
            Assert.That(list.Count(m => m.Kind == PreviewMarkKind.Explosion && Near(m.Origin, center.position) && Mathf.Approximately(m.Radius, 5f)), Is.EqualTo(1), "the explosion's centre and radius");
            Assert.That(list.Any(m => m.Kind == PreviewMarkKind.Force && Near(m.Origin, l.transform.position) && Near(m.Vector, new Vector3(0f, 0f, -1f))), Is.True, "a local item's mark in world space");
            Assert.That(list.Any(m => m.Kind == PreviewMarkKind.Force && Near(m.Origin, new Vector3(0f, 0f, 32f))), Is.True, "the moved body found where it is now");
            Assert.That(list.All(m => m.Until > PreviewSession.Current.Scheduler.Context.Now), Is.True, "alive");
            Assert.That(notices.Count(n => n.Kind == PreviewNoticeKind.PhysicsMarks), Is.EqualTo(1), "the notice");
            int pumps = 0;
            while (PreviewSession.Current != null && pumps < 600) { Pump(1); pumps++; }
            Assert.That(pumps, Is.InRange(108, 144), "the end waits a mark's second");
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.False, "the scene clean");
            Assert.That((a.GetAccumulatedForce(), b.linearVelocity), Is.EqualTo((Vector3.zero, Vector3.zero)), "nothing after the end");
            PreviewSession.Start(player, default);
            PreviewSession.Current.Repeat = true;
            for (int i = 0; i < 1200; i++)
                Pump(1);
            PreviewMarks repeated = PreviewSession.Current.Scheduler.Context.Marks;
            Assert.That((repeated.Count <= 30, repeated.Total > 100), Is.EqualTo((true, true)), "only the live marks kept — " + repeated.Count + " of " + repeated.Total);
        }

        // ---- the Scene view draws the live marks while the session runs, and its callback is gone after the end (a batch
        // editor draws once the window is made — closed after: a Scene view left open renders the later tests' scenes, and URP adds
        // its light data to their lights)
        [UnityTest, Category(OptionalAssemblyTests.DevProjectCategory)]
        public IEnumerator BQ14a_TheSceneViewDraws()
        {
            bool made = SceneView.sceneViews.Count == 0;
            SceneView view = EditorWindow.GetWindow<SceneView>();
            try
            {
                yield return Draws(view);
            }
            finally
            {
                if (made)
                    view.Close();
            }
        }

        IEnumerator Draws(SceneView view)
        {
            Rigidbody a = Body("Force", Vector3.zero);
            FeedbackPlayer player = Player(null, Force(a, PhysicsForceMode.Impulse, Vector3.up));
            PreviewSession.Start(player, default);
            Pump(1);
            int draws = PreviewMarksView.Draws;
            for (int i = 0; i < 6; i++)
            {
                view.Repaint();
                yield return null;
            }
            Assert.That(PreviewMarksView.Draws, Is.GreaterThan(draws), "drawn");
            while (PreviewSession.Current != null)
                Pump(1);
            int calls = PreviewMarksView.Calls;
            for (int i = 0; i < 3; i++)
            {
                view.Repaint();
                yield return null;
            }
            Assert.That(PreviewMarksView.Calls, Is.EqualTo(calls), "no callback after the end");
        }

        // ---- the inspector — no direct write · a 2D force's kind chooses its fields · the names
        [Test]
        public void BQ21a_TheInspector()
        {
            FeedbackItem[] five = { new RigidbodyForceItem(), new RigidbodyExplosionItem(), new RigidbodyVelocityItem(), new Rigidbody2DForceItem(), new Rigidbody2DVelocityItem() };
            Assert.That(five.Select(i => ItemRules.Visible(i, "m_DirectWrite")), Is.All.False, "no direct write on the instants");
            string[] Shown(PhysicsForceKind kind)
            {
                var item = new Rigidbody2DForceItem { m_Kind = kind };
                return new[] { "m_Space", "m_Min", "m_Max", "m_MinTorque", "m_MaxTorque" }.Where(f => ItemRules.Visible(item, f)).ToArray();
            }
            Assert.That(Shown(PhysicsForceKind.Force), Is.EqualTo(new[] { "m_Space", "m_Min", "m_Max" }), "a 2D force");
            Assert.That(Shown(PhysicsForceKind.Torque), Is.EqualTo(new[] { "m_MinTorque", "m_MaxTorque" }), "a 2D torque");
            Assert.That(new[] { "m_Space", "m_Min", "m_Max" }.All(f => ItemRules.Visible(new RigidbodyForceItem { m_Kind = PhysicsForceKind.Torque }, f)), Is.True, "a 3D torque keeps its fields");
            Assert.That((ItemRules.Label(new RigidbodyForceItem(), "m_Min"), ItemRules.Label(new Rigidbody2DForceItem(), "m_MaxTorque"), ItemRules.Label(new RigidbodyExplosionItem(), "m_Offset"),
                ItemRules.Label(new RigidbodyExplosionItem(), "m_MaxBodies"), ItemRules.Label(new AnimatorParameterItem(), "m_Min")),
                Is.EqualTo(("Min (random per axis × intensity)", "Max Torque (N·m · random × intensity)", "Offset (target's local)", "Max Bodies (query buffer)", (string)null)));
        }

        // the body in a panel builds again when a 2D force's kind changes — its fields follow the kind
        [UnityTest]
        public IEnumerator BQ21b_AKindChangeBuildsTheBodyAgain()
        {
            var window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            window.position = new Rect(0, 0, 500, 800);
            window.Show();
            try
            {
                var go = new GameObject("BQ21b");
                m_Made.Add(go);
                FeedbackPlayer host = go.AddComponent<FeedbackPlayer>();
                host.m_Items.Add(new Rigidbody2DForceItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = new SetMotion() });
                var so = new SerializedObject(host);
                SerializedProperty item = so.FindProperty("m_Items").GetArrayElementAtIndex(0);
                var body = new ItemBody();
                window.rootVisualElement.Add(body);
                body.Build(item, "BQ21b", host);
                yield return InspectorTesting.Wait(0.3);
                bool before = body.Fields.Contains("m_Min");
                item.FindPropertyRelative("m_Kind").intValue = (int)PhysicsForceKind.Torque;
                so.ApplyModifiedProperties();
                yield return InspectorTesting.Wait(0.5);
                Assert.That((before, body.Fields.Contains("m_Min"), body.Fields.Contains("m_MinTorque")), Is.EqualTo((true, false, true)));
            }
            finally
            {
                window.Close();
            }
        }

        // ---- the play mode's keep — the enums, the vectors, the layer mask, the numbers
        [Test]
        public void BQ22_TheKeepRoundTrips()
        {
            var force = new RigidbodyForceItem { m_Kind = PhysicsForceKind.Torque, m_Space = TransformSpace.Local, m_Mode = PhysicsForceMode.VelocityChange, m_Min = new Vector3(1f, 2f, 3f), m_Max = new Vector3(4f, 5f, 6f), m_Motion = new SetMotion() };
            var explosion = new RigidbodyExplosionItem { m_MinForce = 3f, m_MaxForce = 9f, m_Radius = 7f, m_Upwards = 0.5f, m_Mode = PhysicsForceMode.Acceleration, m_Offset = new Vector3(0f, 1f, 0f), m_Layers = 544, m_MaxBodies = 12, m_Triggers = true, m_Motion = new SetMotion() };
            var force2 = new Rigidbody2DForceItem { m_Kind = PhysicsForceKind.Torque, m_Mode = PhysicsForceMode2D.Force, m_Min = new Vector2(1.5f, -2.25f), m_Max = new Vector2(2f, 3f), m_MinTorque = 0.5f, m_MaxTorque = 2f, m_Motion = new SetMotion() };
            var velocity2 = new Rigidbody2DVelocityItem { m_Write = VelocityWrite.Add, m_Space = TransformSpace.Local, m_Min = new Vector2(-1f, 1f), m_Max = new Vector2(1f, 2f), m_Motion = new SetMotion() };
            var go = new GameObject("BQ22From");
            m_Made.Add(go);
            FeedbackPlayer a = go.AddComponent<FeedbackPlayer>();
            a.m_Items.AddRange(new FeedbackItem[] { force, explosion, new RigidbodyVelocityItem { m_Write = VelocityWrite.Add, m_Motion = new SetMotion() }, force2, velocity2 });
            var into = new GameObject("BQ22Into");
            m_Made.Add(into);
            FeedbackPlayer b = into.AddComponent<FeedbackPlayer>();
            PlayModeRecord.Apply(b, PlayModeRecord.Capture(a, false, out int unknown), out PlayModeRecord.Stats stats);
            Assert.That((unknown, stats.Dropped), Is.EqualTo((0, 0)));
            var f = (RigidbodyForceItem)b.m_Items[0];
            var e = (RigidbodyExplosionItem)b.m_Items[1];
            var f2 = (Rigidbody2DForceItem)b.m_Items[3];
            var v2 = (Rigidbody2DVelocityItem)b.m_Items[4];
            Assert.That((f.m_Kind, f.m_Space, f.m_Mode, f.m_Min, f.m_Max), Is.EqualTo((PhysicsForceKind.Torque, TransformSpace.Local, PhysicsForceMode.VelocityChange, new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f))));
            Assert.That((e.m_MinForce, e.m_MaxForce, e.m_Radius, e.m_Upwards, e.m_Mode, e.m_Offset, e.m_Layers.value, e.m_MaxBodies, e.m_Triggers),
                Is.EqualTo((3f, 9f, 7f, 0.5f, PhysicsForceMode.Acceleration, new Vector3(0f, 1f, 0f), 544, 12, true)));
            Assert.That(((RigidbodyVelocityItem)b.m_Items[2]).m_Write, Is.EqualTo(VelocityWrite.Add));
            Assert.That((f2.m_Kind, f2.m_Mode, f2.m_Min, f2.m_Max, f2.m_MinTorque, f2.m_MaxTorque), Is.EqualTo((PhysicsForceKind.Torque, PhysicsForceMode2D.Force, new Vector2(1.5f, -2.25f), new Vector2(2f, 3f), 0.5f, 2f)));
            Assert.That((v2.m_Write, v2.m_Space, v2.m_Min, v2.m_Max), Is.EqualTo((VelocityWrite.Add, TransformSpace.Local, new Vector2(-1f, 1f), new Vector2(1f, 2f))));
        }

        // ---- the data is Core's — a player with the five items saves and loads with no missing type, in the minimal project too
        // (no Physics assemblies)
        [Test]
        public void BQ23_TheSavedFormOutlivesTheModules()
        {
            var source = new GameObject("BQ23");
            var player = source.AddComponent<FeedbackPlayer>();
            player.m_Items.AddRange(new FeedbackItem[] { new RigidbodyForceItem { m_Mode = PhysicsForceMode.Force, m_Motion = new SetMotion() }, new RigidbodyExplosionItem { m_Radius = 3f, m_Motion = new SetMotion() },
                new RigidbodyVelocityItem { m_Write = VelocityWrite.Add, m_Motion = new SetMotion() }, new Rigidbody2DForceItem { m_MaxTorque = 4f, m_Motion = new SetMotion() }, new Rigidbody2DVelocityItem { m_Min = Vector2.one, m_Motion = new SetMotion() } });
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "PhysicsTargetsTests");
            string path = Folder + "/BQ23Player.prefab";
            PrefabUtility.SaveAsPrefabAsset(source, path);
            Object.DestroyImmediate(source);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<FeedbackPlayer>();
            Assert.That(SerializationUtility.HasManagedReferencesWithMissingTypes(loaded), Is.False, "no missing type");
            Assert.That(loaded.m_Items.Select(i => i.GetType().Name), Is.EqualTo(new[] { "RigidbodyForceItem", "RigidbodyExplosionItem", "RigidbodyVelocityItem", "Rigidbody2DForceItem", "Rigidbody2DVelocityItem" }));
            Assert.That((((RigidbodyForceItem)loaded.m_Items[0]).m_Mode, ((RigidbodyExplosionItem)loaded.m_Items[1]).m_Radius, ((Rigidbody2DForceItem)loaded.m_Items[3]).m_MaxTorque),
                Is.EqualTo((PhysicsForceMode.Force, 3f, 4f)));
        }
    }
}
