using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // the physics-conditional assembly. Default is a test scheduler, the capture delta mixes frames with and without
    // FixedUpdate, and TearDown puts Default, the capture delta and the player loop back after a red test too. HP01_TwinScenes
    // steps twin local physics scenes together from a loop system right after FixedUpdate's PhysicsFixedUpdate — bit for bit
    // in one process. HP02_Rules counts the notices from the log (LogAssert.Expect keeps an order) and starts a new session
    // with Register(). No UnityEditor here
    public class LayerPhysicsTests
    {
        struct StepTwins { }

        sealed class Body
        {
            public string Name;
            public Rigidbody Rigidbody;
            public ulong Hash = 1469598103934665603UL;
        }

        const int Steps = 190;
        static readonly List<Scene> s_Scenes = new List<Scene>();
        static readonly List<List<Body>> s_Bodies = new List<List<Body>>();
        static int s_Steps;
        static FeedbackScheduler s_Saved;
        static FeedbackScheduler s_Test;
        readonly List<Object> m_Made = new List<Object>();

        // each twin scene one step, then its bodies folded into their hashes (position · rotation · velocities)
        static void OnStep()
        {
            if (s_Steps >= Steps)
                return;
            s_Steps++;
            for (int i = 0; i < s_Scenes.Count; i++)
            {
                if (!s_Scenes[i].IsValid())
                    continue;
                s_Scenes[i].GetPhysicsScene().Simulate(Time.fixedDeltaTime);
                foreach (Body b in s_Bodies[i])
                {
                    Fold(b, b.Rigidbody.position);
                    Fold(b, b.Rigidbody.rotation.eulerAngles);
                    Fold(b, b.Rigidbody.linearVelocity);
                    Fold(b, b.Rigidbody.angularVelocity);
                }
            }
        }

        static void Fold(Body b, Vector3 v)
        {
            b.Hash ^= (uint)BitConverter.SingleToInt32Bits(v.x);
            b.Hash *= 1099511628211UL;
            b.Hash ^= (uint)BitConverter.SingleToInt32Bits(v.y);
            b.Hash *= 1099511628211UL;
            b.Hash ^= (uint)BitConverter.SingleToInt32Bits(v.z);
            b.Hash *= 1099511628211UL;
        }

        static void SetStep(bool on)
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.FixedUpdate))
                    continue;
                var list = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList);
                list.RemoveAll(s => s.type == typeof(StepTwins));
                if (on)
                {
                    int at = list.FindIndex(s => s.type == typeof(UnityEngine.PlayerLoop.FixedUpdate.PhysicsFixedUpdate));
                    list.Insert(at + 1, new PlayerLoopSystem { type = typeof(StepTwins), updateDelegate = OnStep });
                }
                loop.subSystemList[i].subSystemList = list.ToArray();
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SetStep(false);
            if (s_Test != null)
            {
                FeedbackScheduler.Default = s_Saved;
                s_Test.Shutdown();
                s_Test = null;
                s_Saved = null;
            }
            Time.captureDeltaTime = 0f;
            foreach (Object o in m_Made)
                if (o != null)
                    Object.Destroy(o);
            m_Made.Clear();
            foreach (Scene s in s_Scenes)
                if (s.IsValid())
                    yield return SceneManager.UnloadSceneAsync(s);
            s_Scenes.Clear();
            s_Bodies.Clear();
            yield return null;
        }

        PropertyLayerRegistry Swap()
        {
            Time.captureDeltaTime = 1f / 90f;
            s_Saved = FeedbackScheduler.Default;
            s_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = s_Test;
            return s_Test.Context.Layers;
        }

        static void Add(Scene scene, List<Body> bodies, string name, Vector3 at, Quaternion rotation, bool freeze, PrimitiveType shape = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.transform.SetPositionAndRotation(at, rotation);
            var rb = go.AddComponent<Rigidbody>();
            if (freeze)
                rb.constraints = RigidbodyConstraints.FreezeRotation;
            SceneManager.MoveGameObjectToScene(go, scene);
            bodies.Add(new Body { Name = name, Rigidbody = rb });
        }

        // a local physics scene: ground, a slope, a rolling cube, an upright cube with frozen rotation, a sphere on the slope
        static List<Body> Build(string name)
        {
            Scene scene = SceneManager.CreateScene(name, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var bodies = new List<Body>();
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(60f, 1f, 60f);
            SceneManager.MoveGameObjectToScene(ground, scene);
            var slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slope.transform.SetPositionAndRotation(new Vector3(-10f, 1f, 0f), Quaternion.Euler(0f, 0f, 20f));
            slope.transform.localScale = new Vector3(8f, 0.5f, 4f);
            SceneManager.MoveGameObjectToScene(slope, scene);
            Add(scene, bodies, "rolling", new Vector3(0f, 3f, 0f), Quaternion.Euler(10f, 20f, 30f), false);
            Add(scene, bodies, "upright", new Vector3(10f, 3f, 0f), Quaternion.identity, true);
            Add(scene, bodies, "sphere", new Vector3(-12f, 4f, 0f), Quaternion.identity, false, PrimitiveType.Sphere);
            s_Scenes.Add(scene);
            s_Bodies.Add(bodies);
            return bodies;
        }

        // three twin pairs stepped together for 190 steps over 360 frames of a shake — without layers each body of B is A's bit
        // for bit (the shape is deterministic) · the layer shaking B's three bodies (rolling · upright · sphere) leaves each one
        // A's (the layer puts the pose back before physics) · direct slots shaking B's bodies put each one apart (the positive
        // control — no revert, physics takes the shaken pose)
        [UnityTest]
        public IEnumerator HP01_TwinScenes()
        {
            var layers = Swap();
            s_Steps = 0;
            List<Body> a1 = Build("A1"), b1 = Build("B1"), a2 = Build("A2"), b2 = Build("B2"), a3 = Build("A3"), b3 = Build("B3");
            var shakes = new List<(LocalPositionSlot, ContributionHandle)>();
            foreach ((List<Body> pair, bool direct) in new[] { (b2, false), (b3, true) })
            {
                foreach (Body b in pair)
                {
                    Transform x = b.Rigidbody.transform;
                    LocalPositionSlot slot = direct
                        ? layers.CreatePrivate(x, 0, 0, () => new LocalPositionSlot(x))
                        : layers.GetOrCreate(x, 0, 0, () => new LocalPositionSlot(x));
                    slot.Reserve(1);
                    shakes.Add((slot, slot.Begin(ContributionKind.Offset)));
                }
            }
            SetStep(true);
            yield return null;
            for (int f = 1; f <= 360; f++)
            {
                var o = new Vector3(0.1f * Mathf.Sin(f * 0.9f), 0.05f * Mathf.Sin(f * 1.3f), 0f);
                foreach (var (slot, h) in shakes)
                    slot.SetOffset(h, o);
                yield return null;
            }
            foreach (var (slot, h) in shakes)
                slot.End(h);
            yield return null;
            yield return null;
            SetStep(false);
            Assert.That(s_Steps, Is.EqualTo(Steps), "steps");
            for (int i = 0; i < 3; i++)
            {
                Assert.That(b1[i].Hash, Is.EqualTo(a1[i].Hash), $"without layers — {a1[i].Name}");
                Assert.That(b2[i].Hash, Is.EqualTo(a2[i].Hash), $"the layer — {a2[i].Name}");
                Assert.That(b3[i].Hash, Is.Not.EqualTo(a3[i].Hash), $"direct slots (the positive control) — {a3[i].Name}");
            }
        }

        GameObject Cube(string name, bool body, RigidbodyInterpolation interpolation = RigidbodyInterpolation.None, bool kinematic = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            m_Made.Add(go);
            if (body)
            {
                var rb = go.AddComponent<Rigidbody>();
                rb.interpolation = interpolation;
                rb.isKinematic = kinematic;
            }
            return go;
        }

        static List<(LogType, string)> Capture(Action run)
        {
            var seen = new List<(LogType, string)>();
            Application.LogCallback log = (message, stack, type) =>
            {
                if (message.Contains("Feedback"))
                    seen.Add((type, message));
            };
            Application.logMessageReceived += log;
            try
            {
                run();
            }
            finally
            {
                Application.logMessageReceived -= log;
            }
            return seen;
        }

        static int Count(List<(LogType, string)> seen, LogType type, GameObject target)
            => seen.FindAll(e => e.Item1 == type && e.Item2.Contains("'" + target.name + "'")).Count;

        // the Physics assembly registered its check (play mode) · warnings per transform, naming it — an interpolated and an
        // extrapolated body · a direct slot's body without interpolation · a collider of a parent's body (naming the body too) ·
        // notices once per session per rule — two bodies without interpolation and a kinematic one give one · two static
        // colliders give one · nothing for a transform without a collider · nothing for a second slot of the same transform
        [UnityTest]
        public IEnumerator HP02_Rules()
        {
            var layers = Swap();
            Assert.That(LayerHooks.TransformSlotCreated?.Method.DeclaringType, Is.EqualTo(typeof(RigidbodyLayerCheck)), "registered");
            RigidbodyLayerCheck.Register();
            GameObject interpolated = Cube("hp2 interpolated", true, RigidbodyInterpolation.Interpolate);
            GameObject extrapolated = Cube("hp2 extrapolated", true, RigidbodyInterpolation.Extrapolate);
            GameObject bodyA = Cube("hp2 body A", true);
            GameObject bodyB = Cube("hp2 body B", true);
            GameObject kinematic = Cube("hp2 kinematic", true, RigidbodyInterpolation.None, true);
            GameObject root = Cube("hp2 root", true);
            GameObject child = Cube("hp2 child", false);
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = new Vector3(1f, 0f, 0f);
            GameObject staticA = Cube("hp2 static A", false);
            GameObject staticB = Cube("hp2 static B", false);
            GameObject pinned = Cube("hp2 direct", true);
            var plain = new GameObject("hp2 plain");
            m_Made.Add(plain);
            yield return null;
            var seen = Capture(() =>
            {
                foreach (GameObject g in new[] { interpolated, extrapolated, bodyA, bodyB, kinematic, child, staticA, staticB, plain })
                {
                    Transform t = g.transform;
                    layers.GetOrCreate(t, 0, 0, () => new LocalPositionSlot(t));
                }
                Transform ti = interpolated.transform;
                layers.GetOrCreate(ti, 0, 0, () => new LocalRotationSlot(ti));
                Transform tp = pinned.transform;
                layers.CreatePrivate(tp, 0, 0, () => new LocalPositionSlot(tp));
            });
            Assert.That(Count(seen, LogType.Warning, interpolated), Is.EqualTo(1), "interpolated — one warning");
            Assert.That(Count(seen, LogType.Warning, extrapolated), Is.EqualTo(1), "extrapolated — one warning");
            Assert.That(Count(seen, LogType.Warning, pinned), Is.EqualTo(1), "a direct slot's body — one warning");
            Assert.That(Count(seen, LogType.Warning, child) == 1 && Count(seen, LogType.Warning, root) == 1, "a collider of a parent's body — one warning naming both");
            Assert.That(Count(seen, LogType.Log, bodyA), Is.EqualTo(1), "the first body — one notice");
            Assert.That(Count(seen, LogType.Log, bodyB) + Count(seen, LogType.Log, kinematic), Is.EqualTo(0), "a second body · a kinematic one — no notice");
            Assert.That(Count(seen, LogType.Log, staticA), Is.EqualTo(1), "the first static collider — one notice");
            Assert.That(Count(seen, LogType.Log, staticB), Is.EqualTo(0), "a second static collider — no notice");
            Assert.That(Count(seen, LogType.Log, plain) + Count(seen, LogType.Warning, plain), Is.EqualTo(0), "a transform without a collider — nothing");
            Assert.That(seen.Count, Is.EqualTo(6), string.Join(" / ", seen.ConvertAll(e => e.Item2)));
        }
    }
}
