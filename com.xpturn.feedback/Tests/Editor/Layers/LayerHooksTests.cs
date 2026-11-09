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
    // the layer hooks (EditMode — the direct mode's writes · end shapes · Persist · close · release · destroyed target ·
    // allocation · keys · a shared slot on the same property · Self pair · private toggle, the preview recorder and the
    // instance slot's record, the transform hook). The transform hook is a test delegate (no physics types) and the
    // registered one is put back after each test. The rows on URP Lit materials (`_BaseColor`) are in the DevProject category
    public sealed class LayerHooksTests
    {
        const float Dt = 1f / 60f;
        static readonly FrameTime s_Frame = new FrameTime(Dt, Dt);
        static readonly int s_Base = Shader.PropertyToID("_BaseColor");
        static readonly int s_Smooth = Shader.PropertyToID("_Smoothness");
        static readonly Color s_C0 = new Color(0.8f, 0.3f, 0.2f, 1f);
        static readonly Color s_C1 = new Color(0.1f, 0.6f, 0.9f, 1f);
        static readonly Color s_Green = new Color(0.1f, 0.9f, 0.1f, 1f);
        static object s_Sink;

        readonly List<Object> m_Objects = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        Action<Transform, bool> m_Hook;
        FeedbackScheduler m_Scheduler;
        Material m_Scratch;
        Transform m_Normalizer;

        [SetUp]
        public void SetUp() => m_Hook = LayerHooks.TransformSlotCreated;

        [TearDown]
        public void TearDown()
        {
            foreach (FeedbackScheduler s in m_Schedulers)
                s.Shutdown();
            m_Schedulers.Clear();
            m_Scheduler = null;
            m_Scratch = null;
            m_Normalizer = null;
            LayerHooks.TransformSlotCreated = m_Hook;
            foreach (Object o in m_Objects)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Objects.Clear();
        }

        PropertyLayerRegistry Layers(ContextKind kind = ContextKind.Test)
        {
            m_Scheduler = Another(kind);
            return m_Scheduler.Context.Layers;
        }

        FeedbackScheduler Another(ContextKind kind)
        {
            var s = new FeedbackScheduler(kind);
            m_Schedulers.Add(s);
            return s;
        }

        void Tick(int n = 1)
        {
            for (int i = 0; i < n; i++)
                m_Scheduler.Tick(s_Frame);
        }

        Transform Make(string name)
        {
            var go = new GameObject(name);
            m_Objects.Add(go);
            return go.transform;
        }

        // a default cube with a material copy per color
        MeshRenderer Cube(string name, params Color[] colors)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            m_Objects.Add(go);
            var r = go.GetComponent<MeshRenderer>();
            var materials = new Material[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                materials[i] = new Material(r.sharedMaterial) { name = $"{name}-m{i}" };
                materials[i].SetColor(s_Base, colors[i]);
                m_Objects.Add(materials[i]);
            }
            r.sharedMaterials = materials;
            return r;
        }

        // what a material gives back for a color written into it — one round trip
        Color Once(Color c)
        {
            if (m_Scratch == null)
                m_Scratch = Cube("scratch", s_C0).sharedMaterial;
            m_Scratch.SetColor(s_Base, c);
            return m_Scratch.GetColor(s_Base);
        }

        // q written once and read back
        Quaternion N(Quaternion q)
        {
            if (m_Normalizer == null)
                m_Normalizer = Make("normalizer");
            m_Normalizer.localRotation = q;
            return m_Normalizer.localRotation;
        }

        static MaterialPropertyBlock Get(Renderer r)
        {
            var b = new MaterialPropertyBlock();
            r.GetPropertyBlock(b);
            return b;
        }

        static bool B(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        static bool B(Vector3 a, Vector3 b) => B(a.x, b.x) && B(a.y, b.y) && B(a.z, b.z);

        static bool B(Quaternion a, Quaternion b) => B(a.x, b.x) && B(a.y, b.y) && B(a.z, b.z) && B(a.w, b.w);

        static bool B(Color a, Color b) => B(a.r, b.r) && B(a.g, b.g) && B(a.b, b.b) && B(a.a, b.a);

        static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 1e-3f && Mathf.Abs(a.g - b.g) < 1e-3f && Mathf.Abs(a.b - b.b) < 1e-3f;

        static LocalPositionSlot Position(PropertyLayerRegistry layers, Transform t, bool direct)
            => direct ? layers.CreatePrivate(t, 0, 0, () => new LocalPositionSlot(t)) : layers.GetOrCreate(t, 0, 0, () => new LocalPositionSlot(t));

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

        // a direct position — each apply writes snapshot + offset (no read back) · the next Revert leaves it (the game's
        // Update sees the composition) · a game write between applies is covered · the apply of the frame the contribution
        // ended puts the snapshot back, bit for bit · released after the apply → the next Revert
        [Test]
        public void HE01_DirectPosition()
        {
            var layers = Layers();
            Transform t = Make("he1");
            var p0 = new Vector3(1f, 2f, 3f);
            t.localPosition = p0;
            var slot = Position(layers, t, true);
            slot.Reserve(1);
            var h = slot.Begin(ContributionKind.Offset);
            slot.SetOffset(h, new Vector3(0.1f, 0f, 0f));
            Tick();
            Assert.That(B(t.localPosition, p0 + new Vector3(0.1f, 0f, 0f)), $"frame 1 — {t.localPosition}");
            slot.SetOffset(h, new Vector3(0.2f, 0f, 0f));
            Tick();
            Vector3 two = p0 + new Vector3(0.2f, 0f, 0f);
            Assert.That(B(t.localPosition, two), $"frame 2 is snapshot + offset — {t.localPosition}");
            m_Scheduler.RunRevert();
            Assert.That(B(t.localPosition, two), $"the Revert leaves it — {t.localPosition}");
            t.localPosition = new Vector3(9f, 9f, 9f);
            m_Scheduler.RunUpdate(s_Frame);
            m_Scheduler.RunLateApply();
            Assert.That(B(t.localPosition, two), $"the game's write covered — {t.localPosition}");
            m_Scheduler.RunRevert();
            slot.End(h);
            m_Scheduler.RunUpdate(s_Frame);
            m_Scheduler.RunLateApply();
            Assert.That(B(t.localPosition, p0), $"the end frame's apply — {t.localPosition}");
            Transform u = Make("he1 release");
            u.localPosition = p0;
            var released = Position(layers, u, true);
            released.Reserve(1);
            released.SetOffset(released.Begin(ContributionKind.Offset), new Vector3(0.1f, 0f, 0f));
            Tick(2);
            layers.Release(released, 1);
            m_Scheduler.RunRevert();
            Assert.That(B(u.localPosition, p0), $"released after the apply — the next Revert — {u.localPosition}");
        }

        // the end's shape: rotation — the end is N(q0) after one and after three episodes (300 random starts) · MPB — no
        // block left where there was none · another's block P only · instance — each copy its own color · released: the
        // originals back and the copies destroyed
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void HE02_EndShapes()
        {
            var layers = Layers();
            var random = new System.Random(11);
            Transform t = Make("he2 rotation");
            int one = 0, three = 0;
            for (int i = 0; i < 300; i++)
            {
                t.localRotation = Quaternion.Euler((float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f);
                Quaternion n = N(t.localRotation);
                var q = layers.CreatePrivate(t, 0, 0, () => new LocalRotationSlot(t));
                q.Reserve(1);
                for (int e = 0; e < 3; e++)
                {
                    var h = q.Begin(ContributionKind.Offset);
                    q.SetOffset(h, Quaternion.Euler(3f, 0f, 0f));
                    Tick(2);
                    q.End(h);
                    Tick();
                    if (!B(t.localRotation, n))
                    {
                        if (e == 0)
                            one++;
                        if (e == 2)
                            three++;
                    }
                }
                layers.Release(q, 1);
                Tick();
            }
            Assert.That(one, Is.EqualTo(0), "rotation end ≠ N(q0) after one episode, of 300");
            Assert.That(three, Is.EqualTo(0), "rotation end ≠ N(q0) after three episodes, of 300");
            MeshRenderer a = Cube("he2 a", s_C0);
            var sa = layers.CreatePrivate(a, s_Base, -1, () => new MaterialBlockColorSlot(a, s_Base, -1));
            MeshRenderer b = Cube("he2 b", s_C0);
            var other = new MaterialPropertyBlock();
            other.SetFloat(s_Smooth, 0.2f);
            b.SetPropertyBlock(other);
            var sb = layers.CreatePrivate(b, s_Base, -1, () => new MaterialBlockColorSlot(b, s_Base, -1));
            MeshRenderer c = Cube("he2 c", s_C0, s_C1);
            Material[] originals = c.sharedMaterials;
            var sc = layers.CreatePrivate(c, s_Base, -1, () => new MaterialInstanceColorSlot(c, s_Base, -1));
            Material[] copies = c.sharedMaterials;
            sa.Reserve(1);
            sb.Reserve(1);
            sc.Reserve(1);
            var ha = sa.Begin(ContributionKind.Offset);
            var hb = sb.Begin(ContributionKind.Offset);
            var hc = sc.Begin(ContributionKind.Offset);
            sa.SetOffset(ha, new Color(0.1f, 0f, 0f, 0f));
            sb.SetOffset(hb, new Color(0.1f, 0f, 0f, 0f));
            sc.SetOffset(hc, new Color(0.1f, 0f, 0f, 0f));
            Tick(3);
            sa.End(ha);
            sb.End(hb);
            sc.End(hc);
            Tick();
            Assert.That(a.HasPropertyBlock(), Is.False, "MPB — a block left");
            var left = Get(b);
            Assert.That(left.HasFloat(s_Smooth) && !left.HasColor(s_Base), "MPB — another's block P only");
            Color c0 = c.sharedMaterials[0].GetColor(s_Base), c1 = c.sharedMaterials[1].GetColor(s_Base);
            Assert.That(B(c0, Once(originals[0].GetColor(s_Base))) && B(c1, Once(originals[1].GetColor(s_Base))), $"instance — each copy its own color — {c0} · {c1}");
            layers.Release(sc, 1);
            Tick();
            Assert.That(ReferenceEquals(c.sharedMaterials[0], originals[0]) && ReferenceEquals(c.sharedMaterials[1], originals[1]), "instance — the originals back");
            Assert.That(copies[0] == null && copies[1] == null, "instance — the copies destroyed");
        }

        // a Persist in a captured episode ends it first, then writes — a live contribution composes over it · after the
        // effect position · MPB · instance keep it (MPB in the block) · outside an episode written at once
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void HE03_CapturedPersist()
        {
            var layers = Layers();
            Transform t = Make("he3");
            t.localPosition = new Vector3(1f, 2f, 3f);
            var p = Position(layers, t, true);
            MeshRenderer rb = Cube("he3 block", s_C0);
            var b = layers.CreatePrivate(rb, s_Base, -1, () => new MaterialBlockColorSlot(rb, s_Base, -1));
            MeshRenderer ri = Cube("he3 instance", s_C0);
            var ins = layers.CreatePrivate(ri, s_Base, -1, () => new MaterialInstanceColorSlot(ri, s_Base, -1));
            p.Reserve(1);
            b.Reserve(1);
            ins.Reserve(1);
            var hp = p.Begin(ContributionKind.Offset);
            var hb = b.Begin(ContributionKind.Offset);
            var hi = ins.Begin(ContributionKind.Offset);
            p.SetOffset(hp, new Vector3(0.1f, 0f, 0f));
            b.SetOffset(hb, new Color(0.1f, 0f, 0f, 0f));
            ins.SetOffset(hi, new Color(0.1f, 0f, 0f, 0f));
            Tick(2);
            p.WriteValue(new Vector3(5f, 5f, 5f));
            b.WriteValue(s_Green);
            ins.WriteValue(s_Green);
            Tick();
            Assert.That(B(t.localPosition, new Vector3(5f, 5f, 5f) + new Vector3(0.1f, 0f, 0f)), $"composed over the Persist — {t.localPosition}");
            Tick();
            p.End(hp);
            b.End(hb);
            ins.End(hi);
            Tick(2);
            Assert.That(B(t.localPosition, new Vector3(5f, 5f, 5f)), $"position after — {t.localPosition}");
            var block = Get(rb);
            Assert.That(block.HasColor(s_Base) && Near(block.GetColor(s_Base), s_Green), "MPB — the Persist in the block");
            Assert.That(Near(ri.sharedMaterial.GetColor(s_Base), s_Green), $"instance — the Persist kept — {ri.sharedMaterial.GetColor(s_Base)}");
            Transform u = Make("he3 outside");
            var q = Position(layers, u, true);
            q.WriteValue(new Vector3(7f, 7f, 7f));
            Assert.That(B(u.localPosition, new Vector3(7f, 7f, 7f)), "outside an episode written at once");
        }

        // a direct effect closed (Shutdown) or released mid-effect — the snapshot · the instance's original
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void HE04_CloseAndRelease([Values(true, false)] bool close)
        {
            var layers = Layers();
            Transform t = Make("he4");
            t.localPosition = new Vector3(1f, 2f, 3f);
            var p = Position(layers, t, true);
            MeshRenderer ri = Cube("he4 instance", s_C0);
            Material original = ri.sharedMaterial;
            var ins = layers.CreatePrivate(ri, s_Base, -1, () => new MaterialInstanceColorSlot(ri, s_Base, -1));
            p.Reserve(1);
            ins.Reserve(1);
            p.SetOffset(p.Begin(ContributionKind.Offset), new Vector3(0.1f, 0f, 0f));
            ins.SetOffset(ins.Begin(ContributionKind.Offset), new Color(0.1f, 0f, 0f, 0f));
            Tick(2);
            if (close)
            {
                m_Scheduler.Shutdown();
            }
            else
            {
                layers.Release(p, 1);
                layers.Release(ins, 1);
                m_Scheduler.RunRevert();
            }
            Assert.That(B(t.localPosition, new Vector3(1f, 2f, 3f)), $"position — {t.localPosition}");
            Assert.That(ReferenceEquals(ri.sharedMaterial, original), "the instance's original back");
        }

        // a direct effect's target destroyed — no error log, the private slot drops (slot count) · a contribution update
        // returns false (the binding's signal to end the item)
        [Test]
        public void HE05_DestroyedTarget()
        {
            var layers = Layers();
            var go = new GameObject("he5");
            Transform t = go.transform;
            var p = Position(layers, t, true);
            p.Reserve(1);
            var h = p.Begin(ContributionKind.Offset);
            p.SetOffset(h, new Vector3(0.1f, 0f, 0f));
            Tick();
            int before = layers.SlotCount;
            Object.DestroyImmediate(go);
            Tick();
            Assert.That(layers.SlotCount, Is.EqualTo(before - 1), "the private slot dropped");
            Assert.That(p.SetOffset(h, new Vector3(0.2f, 0f, 0f)), Is.False, "an update returns false");
            LogAssert.NoUnexpectedReceived();
        }

        sealed class Recorder : ILayerRecorder
        {
            public readonly List<string> Calls = new List<string>();
            public Func<LayerKey, string> Seen;
            public bool Throw;

            public void Record(in LayerKey key)
            {
                Calls.Add(key.SlotType.Name + " " + (Seen != null ? Seen(key) : ""));
                if (Throw)
                    throw new InvalidOperationException("recorder threw");
            }
        }

        static string V(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";

        // a preview context — a shared value slot · a toggle · an MPB slot · a private slot · a slot whose first write is a
        // Persist, each once right before its first write (over frames · Persist · end — no more), the value then the base
        // (before the write) · a toggle whose first write is its Close · runtime and test contexts 0 even with a recorder · a
        // throwing recorder logged once, the writes go on
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void HE06_Recorder()
        {
            var layers = Layers(ContextKind.Preview);
            var recorder = new Recorder
            {
                Seen = key => key.Target is Transform x ? V(x.localPosition) : key.Target is GameObject g ? (g.activeSelf ? "active" : "inactive") : ""
            };
            layers.Recorder = recorder;
            Transform a = Make("he6 shared");
            a.localPosition = new Vector3(1f, 2f, 3f);
            Transform d = Make("he6 direct");
            d.localPosition = new Vector3(4f, 5f, 6f);
            Transform w = Make("he6 persist");
            var go = new GameObject("he6 toggle");
            m_Objects.Add(go);
            MeshRenderer rb = Cube("he6 block", s_C0);
            var sa = Position(layers, a, false);
            var sd = Position(layers, d, true);
            var sw = Position(layers, w, false);
            var toggle = layers.GetOrCreate(go, 0, 0, () => new GameObjectActiveSlot(go));
            var block = layers.GetOrCreate(rb, s_Base, -1, () => new MaterialBlockColorSlot(rb, s_Base, -1));
            sa.Reserve(1);
            sd.Reserve(1);
            toggle.Reserve(1);
            block.Reserve(1);
            var ha = sa.Begin(ContributionKind.Offset);
            sa.SetOffset(ha, new Vector3(0.1f, 0f, 0f));
            var hd = sd.Begin(ContributionKind.Offset);
            sd.SetOffset(hd, new Vector3(0.1f, 0f, 0f));
            var ht = toggle.Begin();
            toggle.SetOverride(ht, false, 1f);
            var hb = block.Begin(ContributionKind.Offset);
            block.SetOffset(hb, new Color(0.1f, 0f, 0f, 0f));
            sw.WriteValue(new Vector3(7f, 7f, 7f));
            Tick(5);
            sw.WriteValue(new Vector3(8f, 8f, 8f));
            sa.End(ha);
            sd.End(hd);
            toggle.End(ht);
            block.End(hb);
            Tick(2);
            string calls = string.Join(" / ", recorder.Calls);
            Assert.That(recorder.Calls.Count, Is.EqualTo(5), calls);
            foreach (string e in new[] { "LocalPositionSlot (0, 0, 0)", "GameObjectActiveSlot active", "LocalPositionSlot (4, 5, 6)", "LocalPositionSlot (1, 2, 3)", "MaterialBlockColorSlot " })
                Assert.That(recorder.Calls.Contains(e), $"'{e}' in {calls}");

            // captured without a write, a Persist moves the before, the close writes it
            var closing = Another(ContextKind.Preview);
            var closeRecorder = new Recorder { Seen = key => key.Target is GameObject g ? (g.activeSelf ? "active" : "inactive") : "" };
            closing.Context.Layers.Recorder = closeRecorder;
            var go2 = new GameObject("he6 close");
            m_Objects.Add(go2);
            var t2 = closing.Context.Layers.GetOrCreate(go2, 0, 0, () => new GameObjectActiveSlot(go2));
            t2.Reserve(1);
            var h2 = t2.Begin();
            t2.SetOverride(h2, false, 0f);
            closing.Tick(s_Frame);
            t2.WriteValue(false);
            closing.Shutdown();
            Assert.That(closeRecorder.Calls, Is.EqualTo(new[] { "GameObjectActiveSlot active" }), "Close as the first write");
            Assert.That(go2.activeSelf, Is.False, "the close wrote the before");

            foreach (ContextKind kind in new[] { ContextKind.Runtime, ContextKind.Test })
            {
                var o = Another(kind);
                var quiet = new Recorder();
                o.Context.Layers.Recorder = quiet;
                Transform x = Make("he6 " + kind);
                var sx = Position(o.Context.Layers, x, false);
                sx.Reserve(1);
                sx.SetOffset(sx.Begin(ContributionKind.Offset), new Vector3(0.1f, 0f, 0f));
                o.Tick(s_Frame);
                sx.WriteValue(Vector3.one);
                o.Tick(s_Frame);
                Assert.That(quiet.Calls.Count, Is.EqualTo(0), $"{kind} recorded");
            }

            var throwing = Another(ContextKind.Preview);
            throwing.Context.Layers.Recorder = new Recorder { Throw = true };
            Transform y = Make("he6 throw");
            y.localPosition = new Vector3(1f, 2f, 3f);
            var sy = Position(throwing.Context.Layers, y, false);
            sy.Reserve(1);
            var hy = sy.Begin(ContributionKind.Offset);
            sy.SetOffset(hy, new Vector3(0.1f, 0f, 0f));
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: recorder threw");
            throwing.Tick(s_Frame);
            sy.SetOffset(hy, new Vector3(0.2f, 0f, 0f));
            throwing.Tick(s_Frame);
            Assert.That(B(y.localPosition, new Vector3(1f, 2f, 3f) + new Vector3(0.2f, 0f, 0f)), $"a throwing recorder stops no write — {y.localPosition}");
        }

        // a preview instance slot — recorded while the renderer still has the original (before the swap) · the copy right
        // after GetOrCreate. The renderer's copies slot records it
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void HE07_InstanceRecordedBeforeTheSwap()
        {
            var layers = Layers(ContextKind.Preview);
            MeshRenderer r = Cube("he7", s_C0);
            Material original = r.sharedMaterial;
            var recorder = new Recorder { Seen = key => key.Target is Renderer x && ReferenceEquals(x.sharedMaterial, original) ? "original" : "copy" };
            layers.Recorder = recorder;
            layers.GetOrCreate(r, s_Base, -1, () => new MaterialInstanceColorSlot(r, s_Base, -1));
            Assert.That(recorder.Calls, Is.EqualTo(new[] { "MaterialCopiesSlot original" }), "recorded before the swap");
            Assert.That(ReferenceEquals(r.sharedMaterial, original), Is.False, "the copy after GetOrCreate");
        }

        // the transform hook — once per transform (position · rotation · scale) · again for another transform · a direct
        // slot once with direct · not for another target · not in a preview · again after all the transform's slots left ·
        // a throwing hook logged once, the slot stands
        [Test]
        public void HE08_TransformHook()
        {
            var calls = new List<string>();
            LayerHooks.TransformSlotCreated = (t, direct) => calls.Add(t.name + (direct ? " direct" : ""));
            var layers = Layers();
            Transform a = Make("a");
            Transform b = Make("b");
            var pa = layers.GetOrCreate(a, 0, 0, () => new LocalPositionSlot(a));
            var ra = layers.GetOrCreate(a, 0, 0, () => new LocalRotationSlot(a));
            var ka = layers.GetOrCreate(a, 0, 0, () => new LocalScaleSlot(a));
            Assert.That(calls, Is.EqualTo(new[] { "a" }), "three slots on a");
            layers.GetOrCreate(b, 0, 0, () => new LocalPositionSlot(b));
            Assert.That(calls, Is.EqualTo(new[] { "a", "b" }), "b");
            layers.CreatePrivate(a, 0, 0, () => new LocalPositionSlot(a));
            Assert.That(calls, Is.EqualTo(new[] { "a", "b", "a direct" }), "a direct slot on a");
            var go = new GameObject("toggle target");
            m_Objects.Add(go);
            layers.GetOrCreate(go, 0, 0, () => new GameObjectActiveSlot(go));
            Assert.That(calls.Count, Is.EqualTo(3), "a GameObject target");
            var preview = Another(ContextKind.Preview);
            Transform c = Make("c");
            preview.Context.Layers.GetOrCreate(c, 0, 0, () => new LocalPositionSlot(c));
            Assert.That(calls.Count, Is.EqualTo(3), "a preview");
            layers.Release(pa, 0);
            layers.Release(ra, 0);
            layers.Release(ka, 0);
            Tick();
            layers.GetOrCreate(a, 0, 0, () => new LocalPositionSlot(a));
            Assert.That(calls, Is.EqualTo(new[] { "a", "b", "a direct", "a" }), "a again after its shared slots left");
            LayerHooks.TransformSlotCreated = (t, direct) => throw new InvalidOperationException("hook threw");
            Transform d = Make("d");
            LocalPositionSlot sd = null;
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: hook threw");
            Assert.DoesNotThrow(() => sd = layers.GetOrCreate(d, 0, 0, () => new LocalPositionSlot(d)), "the hook's exception escaped");
            Assert.That(sd, Is.Not.Null, "the slot stands");
        }

        // no allocation — a direct slot's contribution update and Tick · no ledger growth
        [Test]
        public void HE09_NoAllocation()
        {
            var layers = Layers();
            Transform t = Make("he9");
            var slot = Position(layers, t, true);
            slot.Reserve(1);
            var h = slot.Begin(ContributionKind.Offset);
            int k = 0;
            TestDelegate run = () =>
            {
                k++;
                slot.SetOffset(h, new Vector3(0.01f * (k & 3), 0f, 0f));
                m_Scheduler.Tick(s_Frame);
            };
            for (int i = 0; i < 20; i++)
                run();
            Assert.That(Allocates(run), Is.False, "allocates");
            Assert.That(slot.GrowthCount, Is.EqualTo(0), "growth");
        }

        // positive control: the constraint sees an allocation
        [Test]
        public void HE09_AllocationIsSeen()
        {
            Assert.That(Allocates(() => s_Sink = new byte[16]), Is.True);
        }

        // keys: GetOrCreate · TryFind never find a private slot · two private slots of one property are two · a shared slot
        // is another · the query shows all three
        [Test]
        public void HE10_Keys()
        {
            var layers = Layers();
            Transform t = Make("he10");
            var p1 = Position(layers, t, true);
            Assert.That(layers.TryFind(t, 0, 0, out LocalPositionSlot _), Is.False, "TryFind found a private slot");
            var p2 = Position(layers, t, true);
            Assert.That(ReferenceEquals(p1, p2), Is.False, "two private slots are one");
            var shared = Position(layers, t, false);
            Assert.That(!ReferenceEquals(shared, p1) && !ReferenceEquals(shared, p2), "GetOrCreate returned a private slot");
            Assert.That(layers.TryFind(t, 0, 0, out LocalPositionSlot found) && ReferenceEquals(found, shared), "TryFind finds the shared slot");
            int seen = 0;
            for (int i = 0; i < layers.SlotCount; i++)
            {
                if (layers.TryGetSlot(i, out LayerSlotInfo info) && ReferenceEquals(info.Target, t))
                    seen++;
            }
            Assert.That(seen, Is.EqualTo(3), "the query");
        }

        // a shared slot (+0.1 x) and a direct slot (+0.2 y) on one property, both start orders — the game's Update sees the
        // direct value only (the Revert took the shared one back) · the end is the snapshot, bit for bit
        [Test]
        public void HE11_SharedAndDirect([Values(false, true)] bool directFirst)
        {
            var layers = Layers();
            Transform t = Make("he11");
            var p0 = new Vector3(1f, 2f, 3f);
            t.localPosition = p0;
            var shared = Position(layers, t, false);
            var direct = Position(layers, t, true);
            shared.Reserve(1);
            direct.Reserve(1);
            ContributionHandle hs, hd;
            if (directFirst)
            {
                hd = direct.Begin(ContributionKind.Offset);
                hs = shared.Begin(ContributionKind.Offset);
            }
            else
            {
                hs = shared.Begin(ContributionKind.Offset);
                hd = direct.Begin(ContributionKind.Offset);
            }
            shared.SetOffset(hs, new Vector3(0.1f, 0f, 0f));
            direct.SetOffset(hd, new Vector3(0f, 0.2f, 0f));
            Tick(2);
            m_Scheduler.RunRevert();
            Vector3 update = t.localPosition;
            m_Scheduler.RunUpdate(s_Frame);
            m_Scheduler.RunLateApply();
            shared.End(hs);
            direct.End(hd);
            Tick(2);
            Assert.That(B(update, p0 + new Vector3(0f, 0.2f, 0f)), $"the game's Update sees — {update}");
            Assert.That(B(t.localPosition, p0), $"the end — {t.localPosition}");
        }

        // a binding's direct position (Self) paired with its direct rotation turns by the base rotation — (1, 0, 0) under a
        // 30° shake
        [Test]
        public void HE12_SelfPair()
        {
            var layers = Layers();
            Transform t = Make("he12");
            var rotation = layers.CreatePrivate(t, 0, 0, () => new LocalRotationSlot(t));
            var position = Position(layers, t, true);
            position.SelfPair = rotation;
            rotation.Reserve(1);
            position.Reserve(1);
            rotation.SetOffset(rotation.Begin(ContributionKind.Offset), Quaternion.Euler(0f, 30f, 0f));
            position.SetOffset(position.Begin(ContributionKind.Offset, LayerSpace.Self), new Vector3(1f, 0f, 0f));
            Tick(2);
            Assert.That((t.localPosition - new Vector3(1f, 0f, 0f)).sqrMagnitude, Is.LessThan(1e-10f), $"Self turned by the base rotation — {t.localPosition}");
        }

        // a toggle through CreatePrivate logs an error (development builds)
        [Test]
        public void HE13_PrivateToggle()
        {
            var layers = Layers();
            var go = new GameObject("he13");
            m_Objects.Add(go);
            LogAssert.Expect(LogType.Error, PropertyLayerRegistry.PrivateToggleError);
            layers.CreatePrivate(go, 0, 0, () => new GameObjectActiveSlot(go));
        }
    }
}
