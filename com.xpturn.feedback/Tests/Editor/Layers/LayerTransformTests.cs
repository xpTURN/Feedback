using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the transform slots (EditMode — the value's shape: spaces · composition · order · the rotation's base carried on ·
    // ends · allocation · a destroyed target · keys). A rotation's end is compared by bits with q0 written once to a scratch
    // transform — N(q0). Orders that are not a contract are written to the test output only
    public sealed class LayerTransformTests
    {
        const float Dt = 1f / 60f;
        static readonly FrameTime s_Frame = new FrameTime(Dt, Dt);
        static object s_Sink;

        readonly List<Object> m_Objects = new List<Object>();
        FeedbackScheduler m_Scheduler;

        [TearDown]
        public void TearDown()
        {
            m_Scheduler?.Shutdown();
            m_Scheduler = null;
            foreach (Object o in m_Objects)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Objects.Clear();
        }

        PropertyLayerRegistry Layers()
        {
            m_Scheduler = new FeedbackScheduler(ContextKind.Test);
            return m_Scheduler.Context.Layers;
        }

        void Tick(int n = 1)
        {
            for (int i = 0; i < n; i++)
                m_Scheduler.Tick(s_Frame);
        }

        Transform T(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            m_Objects.Add(go);
            if (parent != null)
                go.transform.SetParent(parent, false);
            return go.transform;
        }

        static bool Bits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        static bool Bits(Vector3 a, Vector3 b) => Bits(a.x, b.x) && Bits(a.y, b.y) && Bits(a.z, b.z);
        static bool Bits(Quaternion a, Quaternion b) => Bits(a.x, b.x) && Bits(a.y, b.y) && Bits(a.z, b.z) && Bits(a.w, b.w);

        static LocalPositionSlot Pos(PropertyLayerRegistry r, Transform t) => r.GetOrCreate(t, 0, 0, () => new LocalPositionSlot(t));
        static LocalRotationSlot Rot(PropertyLayerRegistry r, Transform t) => r.GetOrCreate(t, 0, 0, () => new LocalRotationSlot(t));
        static LocalScaleSlot Scl(PropertyLayerRegistry r, Transform t) => r.GetOrCreate(t, 0, 0, () => new LocalScaleSlot(t));

        // q written once and read back
        static Quaternion N(Quaternion q)
        {
            var s = new GameObject("N").transform;
            s.localRotation = q;
            Quaternion r = s.localRotation;
            Object.DestroyImmediate(s.gameObject);
            return r;
        }

        // Euler starts whose read value is not a fixed point; multi — one write does not settle it (N(N(q)) ≠ N(q))
        static List<Vector3> Starts(int count, bool multi, int seed)
        {
            var s = new GameObject("Starts").transform;
            var rng = new System.Random(seed);
            var list = new List<Vector3>();
            for (int i = 0; i < 2000000 && list.Count < count; i++)
            {
                var e = new Vector3((float)(rng.NextDouble() * 360 - 180), (float)(rng.NextDouble() * 360 - 180), (float)(rng.NextDouble() * 360 - 180));
                s.localRotation = Quaternion.Euler(e);
                Quaternion q0 = s.localRotation;
                s.localRotation = q0;
                Quaternion n1 = s.localRotation;
                if (Bits(n1, q0))
                    continue;
                s.localRotation = n1;
                if (multi != Bits(s.localRotation, n1))
                    list.Add(e);
            }
            Object.DestroyImmediate(s.gameObject);
            return list;
        }

        // true only when five runs in a row allocate — 6000.3 counts unrelated allocations now and then
        static bool Allocates(TestDelegate run)
        {
            for (int i = 0; i < 5; i++)
            {
                if (!Is.AllocatingGCMemory().ApplyTo(run).IsSuccess)
                    return false;
            }
            return true;
        }

        // position: children of a rotated parent under a non-uniformly scaled grandparent. During the effect Local is v + o,
        // World's world displacement is o and Self's is rotation · o (world units); after the end the values are bit-equal
        [Test]
        public void TT01_PositionSpaces()
        {
            var layers = Layers();
            var grand = T("grand");
            grand.localScale = new Vector3(1.3f, 0.7f, 2.1f);
            grand.localRotation = Quaternion.Euler(17, 33, -8);
            var parent = T("parent", grand);
            parent.localRotation = Quaternion.Euler(10, 20, 5);
            var local = T("local", parent);
            var world = T("world", parent);
            var self = T("self", parent);
            local.localPosition = new Vector3(1, 2, 3);
            world.localPosition = new Vector3(-1, 0.5f, 2);
            self.localPosition = new Vector3(0.3f, -0.2f, 1);
            self.localRotation = Quaternion.Euler(0, 30, 45);
            Vector3 l0 = local.localPosition, w0 = world.localPosition, s0 = self.localPosition;
            Vector3 worldAt = world.position, selfAt = self.position;
            var pl = Pos(layers, local);
            var pw = Pos(layers, world);
            var ps = Pos(layers, self);
            pl.Reserve(1);
            pw.Reserve(1);
            ps.Reserve(1);
            var hl = pl.Begin(ContributionKind.Offset, LayerSpace.Local);
            var hw = pw.Begin(ContributionKind.Offset, LayerSpace.World);
            var hs = ps.Begin(ContributionKind.Offset, LayerSpace.Self);
            for (int f = 0; f < 10; f++)
            {
                var ol = new Vector3(0.1f * Mathf.Sin(f), 0.05f, 0f);
                var ow = new Vector3(0f, 0.1f * Mathf.Cos(f), 0.2f);
                var os = new Vector3(0.05f, 0.02f * Mathf.Sin(f), 0.1f);
                pl.SetOffset(hl, ol);
                pw.SetOffset(hw, ow);
                ps.SetOffset(hs, os);
                Tick();
                Assert.That((local.localPosition - (l0 + ol)).magnitude, Is.LessThan(1e-5f), $"Local · frame {f}");
                Assert.That((world.position - worldAt - ow).magnitude, Is.LessThan(1e-4f), $"World · frame {f}");
                Assert.That((self.position - selfAt - self.rotation * os).magnitude, Is.LessThan(1e-4f), $"Self · frame {f}");
            }
            pl.End(hl);
            pw.End(hw);
            ps.End(hs);
            Tick(2);
            Assert.That(Bits(local.localPosition, l0) && Bits(world.localPosition, w0) && Bits(self.localPosition, s0), "end");
        }

        // rotation: Local then World under a rotated grandparent and parent — the world rotation during the effect is
        // o_w · (P · v) · o_l; starts that are not fixed points (one step and more) × frame counts odd and even — Base = q0
        // every frame, the end = N(q0)
        [Test]
        public void TT02_RotationSpacesAndEnd()
        {
            var layers = Layers();
            var grand = T("grand");
            grand.localRotation = Quaternion.Euler(-20, 50, 10);
            var parent = T("parent", grand);
            parent.localRotation = Quaternion.Euler(17, 33, -8);
            var t = T("t", parent);
            var rot = Rot(layers, t);
            rot.Reserve(2);
            List<Vector3> starts = Starts(6, false, 3);
            starts.AddRange(Starts(6, true, 4));
            Assert.That(starts.Count, Is.EqualTo(12), "starts");
            foreach (Vector3 e in starts)
            {
                foreach (int frames in new[] { 9, 10 })
                {
                    t.localRotation = Quaternion.Euler(e);
                    Quaternion q0 = t.localRotation;
                    Quaternion once = N(q0);
                    var hl = rot.Begin(ContributionKind.Offset, LayerSpace.Local);
                    var hw = rot.Begin(ContributionKind.Offset, LayerSpace.World);
                    for (int f = 0; f < frames; f++)
                    {
                        Quaternion ol = Quaternion.Euler(3f * Mathf.Sin(f * 0.7f), -1.5f * Mathf.Sin(f * 0.7f), 0.75f);
                        Quaternion ow = Quaternion.Euler(0f, 2f * Mathf.Cos(f), 1f);
                        rot.SetOffset(hl, ol);
                        rot.SetOffset(hw, ow);
                        Tick();
                        Assert.That(Bits(rot.Base, q0), $"Base = q0 · start {e} frame {f}");
                        Quaternion expected = ow * (parent.rotation * q0) * ol;
                        Assert.That(Quaternion.Angle(t.rotation, expected), Is.LessThan(0.01f), $"value · start {e} frame {f}");
                    }
                    rot.End(hl);
                    rot.End(hw);
                    Tick(2);
                    Assert.That(Bits(t.localRotation, once), $"end = N(q0) · start {e} frames {frames}");
                }
            }
        }

        // scale: two scale offsets multiply — v ⊙ (1 + a) ⊙ (1 + b); the end is bit-equal
        [Test]
        public void TT03_ScaleMultiplies()
        {
            var layers = Layers();
            var t = T("t");
            t.localScale = new Vector3(2f, 3f, 0.5f);
            Vector3 start = t.localScale;
            var sc = Scl(layers, t);
            sc.Reserve(2);
            var a = sc.Begin(ContributionKind.Offset);
            var b = sc.Begin(ContributionKind.Offset);
            sc.SetOffset(a, new Vector3(0.5f, 0.5f, 0.5f));
            sc.SetOffset(b, new Vector3(0.25f, 0.25f, 0.25f));
            Tick();
            Vector3 expected = Vector3.Scale(Vector3.Scale(start, Vector3.one * 1.5f), Vector3.one * 1.25f);
            Assert.That((t.localScale - expected).magnitude, Is.LessThan(1e-5f), "during");
            sc.End(a);
            sc.End(b);
            Tick(2);
            Assert.That(Bits(t.localScale, start), "end");
        }

        // rotation order: X 90° and Y 90° in both start orders — two Local, two World: the product in start order. Local
        // with World is a right product and a left conjugation, the same in either order — those cases see the formula only.
        // The grandparent turns on an axis the offsets do not (a shared axis hides the grandparent)
        [Test]
        public void TT04_RotationOrder()
        {
            var layers = Layers();
            var grand = T("grand");
            grand.localRotation = Quaternion.Euler(0, 0, 40);
            var parent = T("parent", grand);
            parent.localRotation = Quaternion.Euler(25, 0, 10);
            var t = T("t", parent);
            var rot = Rot(layers, t);
            rot.Reserve(2);
            Quaternion x = Quaternion.Euler(90, 0, 0), y = Quaternion.Euler(0, 90, 0);
            for (int c = 0; c < 6; c++)
            {
                t.localRotation = Quaternion.Euler(10, 20, 30);
                Quaternion v = t.localRotation;
                Quaternion p = parent.rotation;
                Quaternion ip = Quaternion.Inverse(p);
                LayerSpace first = c == 3 || c >= 4 ? LayerSpace.World : LayerSpace.Local;
                LayerSpace second = c == 2 || c >= 4 ? LayerSpace.World : LayerSpace.Local;
                bool swapped = c == 1 || c == 3 || c == 5;
                Quaternion a = swapped ? y : x;
                Quaternion b = swapped ? x : y;
                ContributionHandle h1 = rot.Begin(ContributionKind.Offset, first);
                ContributionHandle h2 = rot.Begin(ContributionKind.Offset, second);
                rot.SetOffset(h1, a);
                rot.SetOffset(h2, b);
                Quaternion expected;
                switch (c)
                {
                    case 0:
                    case 1:
                        expected = v * a * b;
                        break;
                    case 2:
                        expected = ip * b * p * (v * a);
                        break;
                    case 3:
                        expected = (ip * a * p * v) * b;
                        break;
                    default:
                        expected = ip * b * p * (ip * a * p * v);
                        break;
                }
                Tick();
                Assert.That(Quaternion.Angle(t.localRotation, expected), Is.LessThan(0.01f), $"case {c} — {first} then {second}");
                rot.End(h1);
                rot.End(h2);
                Tick(2);
            }
        }

        // before the first value: Begin with no value yet — the apply does not write, the values are bit-equal
        [Test]
        public void TT05_BeforeTheFirstValue()
        {
            var layers = Layers();
            var t = T("t");
            t.localRotation = Quaternion.Euler(10, 20, 30);
            t.localPosition = new Vector3(1, 2, 3);
            Quaternion r0 = t.localRotation;
            Vector3 p0 = t.localPosition;
            var rot = Rot(layers, t);
            var pos = Pos(layers, t);
            rot.Reserve(1);
            pos.Reserve(1);
            rot.Begin(ContributionKind.Offset);
            pos.Begin(ContributionKind.Offset);
            Tick();
            Assert.That(rot.Pending, Is.False, "rotation wrote");
            Assert.That(pos.Pending, Is.False, "position wrote");
            Assert.That(Bits(t.localRotation, r0) && Bits(t.localPosition, p0), "values");
        }

        // the Self base: the camera shape — the rotation slot begun first applies first in the stage, and Self still turns by
        // its Base; with the rotation slot released, and with none, by localRotation
        [Test]
        public void TT06_SelfOnTheRotationBase()
        {
            var layers = Layers();
            var parent = T("parent");
            parent.localRotation = Quaternion.Euler(10, 20, 5);
            var cam = T("camera", parent);
            cam.localRotation = Quaternion.Euler(0, 0, 45);
            cam.localPosition = new Vector3(0.2f, 0.1f, -1f);
            Vector3 baseLocal = cam.localPosition;
            Quaternion rBase = cam.localRotation;
            var rot = Rot(layers, cam);
            rot.Reserve(1);
            var hr = rot.Begin(ContributionKind.Offset);
            rot.SetOffset(hr, Quaternion.Euler(0, 0, 20));
            var pos = Pos(layers, cam);
            pos.Reserve(1);
            var hp = pos.Begin(ContributionKind.Offset, LayerSpace.Self);
            var o = new Vector3(0.1f, 0.05f, 0f);
            pos.SetOffset(hp, o);
            Tick();
            Vector3 d = cam.position - parent.TransformPoint(baseLocal);
            Assert.That((d - parent.rotation * (rBase * o)).magnitude, Is.LessThan(1e-5f), "the rotation slot first — Self on its Base");
            rot.End(hr);
            layers.Release(rot, 1);
            Tick(2);
            d = cam.position - parent.TransformPoint(baseLocal);
            Assert.That((d - parent.rotation * (cam.localRotation * o)).magnitude, Is.LessThan(1e-5f), "the rotation slot released — localRotation");

            var cam2 = T("camera2", parent);
            cam2.localRotation = Quaternion.Euler(30, 0, 0);
            Vector3 base2 = cam2.localPosition;
            var pos2 = Pos(layers, cam2);
            pos2.Reserve(1);
            pos2.SetOffset(pos2.Begin(ContributionKind.Offset, LayerSpace.Self), o);
            Tick();
            d = cam2.position - parent.TransformPoint(base2);
            Assert.That((d - parent.rotation * (cam2.localRotation * o)).magnitude, Is.LessThan(1e-5f), "no rotation slot — localRotation");
        }

        // world units: under a uniformly and a non-uniformly scaled parent Self's world displacement is rotation · o; under a
        // parent whose scale slot applies first too. The camera first is written down only
        [Test]
        public void TT06_SelfInWorldUnits()
        {
            var layers = Layers();
            var o = new Vector3(0.1f, 0.05f, 0f);
            foreach (Vector3 scale in new[] { Vector3.one * 2f, new Vector3(1f, 3f, 1f) })
            {
                var parent = T("parent");
                parent.localScale = scale;
                parent.localRotation = Quaternion.Euler(10, 20, 5);
                var cam = T("camera", parent);
                cam.localRotation = Quaternion.Euler(0, 0, 45);
                Vector3 baseLocal = cam.localPosition;
                var pos = Pos(layers, cam);
                pos.Reserve(1);
                var h = pos.Begin(ContributionKind.Offset, LayerSpace.Self);
                pos.SetOffset(h, o);
                Tick();
                Vector3 d = cam.position - parent.TransformPoint(baseLocal);
                Assert.That((d - cam.rotation * o).magnitude, Is.LessThan(1e-5f), $"world units under {scale}");
                pos.End(h);
                Tick(2);
            }
            for (int cameraFirst = 0; cameraFirst < 2; cameraFirst++)
            {
                var parent = T("parent-shaken");
                var cam = T("camera", parent);
                cam.localRotation = Quaternion.Euler(0, 0, 45);
                Vector3 baseLocal = cam.localPosition;
                var ps = Scl(layers, parent);
                var pos = Pos(layers, cam);
                ps.Reserve(1);
                pos.Reserve(1);
                if (cameraFirst == 1)
                    pos.SetOffset(pos.Begin(ContributionKind.Offset, LayerSpace.Self), o);
                ps.SetOffset(ps.Begin(ContributionKind.Offset), Vector3.one * 0.2f);
                if (cameraFirst == 0)
                    pos.SetOffset(pos.Begin(ContributionKind.Offset, LayerSpace.Self), o);
                Tick();
                float error = (cam.position - parent.TransformPoint(baseLocal) - cam.rotation * o).magnitude;
                if (cameraFirst == 0)
                    Assert.That(error, Is.LessThan(1e-5f), "the parent's scale slot first");
                else
                    TestContext.WriteLine($"the camera first under a parent scale shake ×1.2 — error {error} (not a contract)");
            }
        }

        // World order: a World offset under a shaken parent whose slot applies first — rotation, scale — moves the child by
        // o in the world. The child first, and a parent shaken again after its end, are written down only
        [Test]
        public void TT07_WorldUnderAShakenParent()
        {
            var layers = Layers();
            var o = new Vector3(1f, 0f, 0f);
            for (int c = 0; c < 4; c++)
            {
                var parent = T("parent");
                parent.localRotation = Quaternion.Euler(10, 20, 5);
                if (c != 1)
                    parent.localScale = new Vector3(1.3f, 0.7f, 2.1f);
                var child = T("child", parent);
                child.localPosition = new Vector3(0.3f, 0.4f, 0.5f);
                Vector3 baseLocal = child.localPosition;
                var pr = Rot(layers, parent);
                var ps = Scl(layers, parent);
                var cp = Pos(layers, child);
                pr.Reserve(1);
                ps.Reserve(1);
                cp.Reserve(1);
                ContributionHandle hc = default;
                if (c == 2)
                    hc = cp.Begin(ContributionKind.Offset, LayerSpace.World);
                ContributionHandle hp = c == 1 ? ps.Begin(ContributionKind.Offset) : pr.Begin(ContributionKind.Offset);
                if (c != 2)
                    hc = cp.Begin(ContributionKind.Offset, LayerSpace.World);
                if (c == 1)
                    ps.SetOffset(hp, Vector3.one * 0.2f);
                else
                    pr.SetOffset(hp, Quaternion.Euler(0, 0, 30));
                cp.SetOffset(hc, o);
                Tick();
                if (c == 3)
                {
                    pr.End(hp);
                    Tick();
                    hp = pr.Begin(ContributionKind.Offset);
                    pr.SetOffset(hp, Quaternion.Euler(0, 0, 30));
                    Tick();
                }
                float error = (child.position - (parent.TransformPoint(baseLocal) + o)).magnitude;
                switch (c)
                {
                    case 0:
                        Assert.That(error, Is.LessThan(1e-5f), "the parent's rotation slot first");
                        break;
                    case 1:
                        Assert.That(error, Is.LessThan(1e-5f), "the parent's scale slot first");
                        break;
                    case 2:
                        TestContext.WriteLine($"the child first under a parent shake 30° — error {error} (not a contract)");
                        break;
                    default:
                        TestContext.WriteLine($"the parent shaken again after its end — error {error} (not a contract)");
                        break;
                }
            }
        }

        // rotation overrides of weight 0 · 0.5 · 1 over an offset end at N(q0) — starts one write does not settle. The frame
        // writes a weight 0 and does not reach the target at 1 — no binding puts overrides on a rotation
        [Test]
        public void TT08_OverridesEndAtOnceWritten()
        {
            var layers = Layers();
            var t = T("t");
            var rot = Rot(layers, t);
            rot.Reserve(2);
            List<Vector3> starts = Starts(8, true, 9);
            Assert.That(starts.Count, Is.EqualTo(8), "starts");
            foreach (Vector3 e in starts)
            {
                t.localRotation = Quaternion.Euler(e);
                Quaternion q0 = t.localRotation;
                Quaternion once = N(q0);
                var h = rot.Begin(ContributionKind.Override);
                var ho = rot.Begin(ContributionKind.Offset);
                for (int f = 0; f < 9; f++)
                {
                    rot.SetOverride(h, Quaternion.Euler(-40, 70, 15), f % 3 * 0.5f);
                    rot.SetOffset(ho, Quaternion.Euler(4f * Mathf.Sin(f), 5f, 6f));
                    Tick();
                }
                rot.End(h);
                rot.End(ho);
                Tick(2);
                Assert.That(Bits(t.localRotation, once), $"end = N(q0) · start {e}");
            }
        }

        // transform: the target destroyed during the effect — no error log, the three slots drop
        [Test]
        public void TT09_TargetDestroyed()
        {
            var layers = Layers();
            var t = T("t");
            var pos = Pos(layers, t);
            var rot = Rot(layers, t);
            var sc = Scl(layers, t);
            pos.Reserve(1);
            rot.Reserve(1);
            sc.Reserve(1);
            pos.SetOffset(pos.Begin(ContributionKind.Offset), new Vector3(0.1f, 0, 0));
            rot.SetOffset(rot.Begin(ContributionKind.Offset), Quaternion.Euler(3, 0, 0));
            sc.SetOffset(sc.Begin(ContributionKind.Offset), new Vector3(0.1f, 0.1f, 0.1f));
            Tick();
            Object.DestroyImmediate(t.gameObject);
            Tick(2);
            LogAssert.NoUnexpectedReceived();
            Assert.That(layers.SlotCount, Is.EqualTo(0), "slots");
        }

        // transform: no allocation — the three slots' contribution updates and Tick with World, Self (the lookup) and
        // rotation World
        [Test]
        public void TT10_NoAllocation()
        {
            var layers = Layers();
            var parent = T("parent");
            parent.localRotation = Quaternion.Euler(10, 20, 5);
            parent.localScale = new Vector3(1.3f, 0.7f, 2.1f);
            var t = T("t", parent);
            var pos = Pos(layers, t);
            var rot = Rot(layers, t);
            var sc = Scl(layers, t);
            pos.Reserve(2);
            rot.Reserve(1);
            sc.Reserve(1);
            var hw = pos.Begin(ContributionKind.Offset, LayerSpace.World);
            var hs = pos.Begin(ContributionKind.Offset, LayerSpace.Self);
            var hr = rot.Begin(ContributionKind.Offset, LayerSpace.World);
            var hc = sc.Begin(ContributionKind.Offset);
            int k = 0;
            TestDelegate run = () =>
            {
                k++;
                float s = (k & 1) == 0 ? 0.01f : -0.01f;
                pos.SetOffset(hw, new Vector3(s, 0, 0));
                pos.SetOffset(hs, new Vector3(0, s, 0));
                rot.SetOffset(hr, Quaternion.Euler(s * 100f, 0, 0));
                sc.SetOffset(hc, new Vector3(s, s, s));
                m_Scheduler.Tick(s_Frame);
            };
            run();
            run();
            Assert.That(Allocates(run), Is.False, "allocates");
            Assert.That(Allocates(() => s_Sink = new byte[16]), Is.True, "positive control");
        }

        // keys · no parent: a transform's three slots are different slots, the same type is the same slot, a
        // RectTransform takes the same type; with no parent World is v + o for a position and o · v for a rotation — no log
        [Test]
        public void TT11_KeysAndNoParent()
        {
            var layers = Layers();
            var t = T("t");
            var pos = Pos(layers, t);
            var rot = Rot(layers, t);
            var sc = Scl(layers, t);
            Assert.That(ReferenceEquals(pos, Pos(layers, t)), "the same slot");
            Assert.That(!ReferenceEquals((LayerSlot)pos, rot) && !ReferenceEquals((LayerSlot)pos, sc) && !ReferenceEquals((LayerSlot)rot, sc), "three slots");
            var rect = new GameObject("rect", typeof(RectTransform));
            m_Objects.Add(rect);
            Assert.That(Pos(layers, rect.transform), Is.InstanceOf<LocalPositionSlot>(), "RectTransform");
            t.localPosition = new Vector3(1, 2, 3);
            t.localRotation = Quaternion.Euler(10, 20, 30);
            Vector3 p0 = t.localPosition;
            Quaternion r0 = t.localRotation;
            pos.Reserve(1);
            rot.Reserve(1);
            var o = new Vector3(0.1f, 0.2f, 0.3f);
            Quaternion q = Quaternion.Euler(0, 30, 0);
            pos.SetOffset(pos.Begin(ContributionKind.Offset, LayerSpace.World), o);
            rot.SetOffset(rot.Begin(ContributionKind.Offset, LayerSpace.World), q);
            Tick();
            LogAssert.NoUnexpectedReceived();
            Assert.That(Bits(t.localPosition, p0 + o), "position World with no parent");
            Assert.That(Quaternion.Angle(t.localRotation, q * r0), Is.LessThan(0.01f), "rotation World with no parent");
        }
    }
}
