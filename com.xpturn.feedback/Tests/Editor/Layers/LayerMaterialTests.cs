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
    // the material slots (EditMode — the MPB slot's block · base · others' blocks · window rule · composition · Persist ·
    // index range, the instance slot and its life, close · release · destroyed target · allocation · keys · an index slot
    // over another's whole block). Renderers are default cubes with a material copy per color (URP Lit · `_BaseColor`) — the
    // dev project's pipeline, so the class is in the DevProject category. Colors are compared with what the material or the
    // block gives back — a written color does not read back bit for bit in Linear
    [Category(OptionalAssemblyTests.DevProjectCategory)]
    public sealed class LayerMaterialTests
    {
        const float Dt = 1f / 60f;
        static readonly FrameTime s_Frame = new FrameTime(Dt, Dt);
        static readonly int s_Base = Shader.PropertyToID("_BaseColor");
        static readonly int s_Emission = Shader.PropertyToID("_EmissionColor");
        static readonly int s_Smooth = Shader.PropertyToID("_Smoothness");
        static readonly int s_Metallic = Shader.PropertyToID("_Metallic");
        static readonly Color s_C0 = new Color(0.8f, 0.3f, 0.2f, 1f);
        static readonly Color s_C1 = new Color(0.1f, 0.6f, 0.9f, 1f);
        static object s_Sink;

        readonly List<Object> m_Objects = new List<Object>();
        FeedbackScheduler m_Scheduler;
        Material m_Scratch;

        [TearDown]
        public void TearDown()
        {
            m_Scheduler?.Shutdown();
            m_Scheduler = null;
            m_Scratch = null;
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
                materials[i].SetColor(s_Emission, new Color(0.1f, 0.2f, 0.3f, 1f));
                m_Objects.Add(materials[i]);
            }
            r.sharedMaterials = materials;
            return r;
        }

        static Color Mat(Renderer r, int i) => r.sharedMaterials[i].GetColor(s_Base);

        // what a material gives back for a color written into it — one round trip
        Color Once(Color c)
        {
            if (m_Scratch == null)
                m_Scratch = Cube("scratch", s_C0).sharedMaterial;
            m_Scratch.SetColor(s_Base, c);
            return m_Scratch.GetColor(s_Base);
        }

        static MaterialPropertyBlock Get(Renderer r, int index = -1)
        {
            var b = new MaterialPropertyBlock();
            if (index < 0)
                r.GetPropertyBlock(b);
            else
                r.GetPropertyBlock(b, index);
            return b;
        }

        // what a block gives back for a color written into it
        static Color RoundTrip(Color c)
        {
            var b = new MaterialPropertyBlock();
            b.SetColor(s_Base, c);
            return b.GetColor(s_Base);
        }

        static MaterialPropertyBlock Other(float smoothness)
        {
            var p = new MaterialPropertyBlock();
            p.SetFloat(s_Smooth, smoothness);
            return p;
        }

        static bool B(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        static bool B(Color a, Color b) => B(a.r, b.r) && B(a.g, b.g) && B(a.b, b.b) && B(a.a, b.a);

        static MaterialBlockColorSlot Block(PropertyLayerRegistry r, Renderer target, int id, int index)
            => r.GetOrCreate(target, id, index, () => new MaterialBlockColorSlot(target, id, index));

        static MaterialInstanceColorSlot Copy(PropertyLayerRegistry r, Renderer target, int id, int index)
            => r.GetOrCreate(target, id, index, () => new MaterialInstanceColorSlot(target, id, index));

        static int MaterialCount() => Resources.FindObjectsOfTypeAll<Material>().Length;

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

        // MPB whole: (0.8, 0.3, 0.2) + a sine offset over frames — the block's color is the value written, read back ·
        // written · Base the material's value; after the end no block and the material's value bit for bit
        [Test]
        public void ME01_WholeBlock()
        {
            var layers = Layers();
            var r = Cube("me1", s_C0);
            Color m0 = Mat(r, 0);
            var slot = Block(layers, r, s_Base, -1);
            slot.Reserve(1);
            var h = slot.Begin(ContributionKind.Offset);
            for (int f = 0; f < 5; f++)
            {
                var o = new Color(0.1f * Mathf.Sin(f + 1), 0.05f, 0f, 0f);
                slot.SetOffset(h, o);
                Tick();
                MaterialPropertyBlock b = Get(r);
                Assert.That(b.HasColor(s_Base) && B(b.GetColor(s_Base), RoundTrip(m0 + o)), $"frame {f}: the block's color is the value written, read back — {b.GetColor(s_Base)}");
                Assert.That(slot.Pending, $"frame {f}: written");
                Assert.That(B(slot.Base, m0), $"frame {f}: Base — {slot.Base}");
            }
            slot.End(h);
            Tick(2);
            Assert.That(r.HasPropertyBlock(), Is.False, "no block after the end");
            Assert.That(B(Mat(r, 0), m0), "the material's value");
        }

        // a whole slot over two materials of different colors — Base is material 0's value
        [Test]
        public void ME01_WholeBaseIsMaterialZero()
        {
            var layers = Layers();
            var r = Cube("me1b", s_C0, s_C1);
            Color m0 = Mat(r, 0);
            var slot = Block(layers, r, s_Base, -1);
            slot.Reserve(1);
            var h = slot.Begin(ContributionKind.Offset);
            slot.SetOffset(h, new Color(0.1f, 0f, 0f, 0f));
            Tick();
            Assert.That(B(slot.Base, m0), $"Base — {slot.Base}");
            slot.End(h);
            Tick(2);
            Assert.That(r.HasPropertyBlock(), Is.False, "no block after the end");
        }

        // MPB index: index 1 of two — the same; index 0 and the whole block untouched; after the end neither the index block
        // nor a whole block
        [Test]
        public void ME02_IndexBlock()
        {
            var layers = Layers();
            var r = Cube("me2", s_C0, s_C1);
            Color m1 = Mat(r, 1);
            var slot = Block(layers, r, s_Base, 1);
            slot.Reserve(1);
            var h = slot.Begin(ContributionKind.Offset);
            for (int f = 0; f < 3; f++)
            {
                var o = new Color(0.1f * Mathf.Sin(f + 1), 0.05f, 0f, 0f);
                slot.SetOffset(h, o);
                Tick();
                MaterialPropertyBlock b = Get(r, 1);
                Assert.That(b.HasColor(s_Base) && B(b.GetColor(s_Base), RoundTrip(m1 + o)), $"frame {f}: index 1's color is the value written, read back — {b.GetColor(s_Base)}");
                Assert.That(slot.Pending, $"frame {f}: written");
                Assert.That(Get(r, 0).isEmpty && Get(r).isEmpty, $"frame {f}: index 0 and the whole block untouched");
                Assert.That(B(slot.Base, m1), $"frame {f}: Base — {slot.Base}");
            }
            slot.End(h);
            Tick(2);
            Assert.That(Get(r, 1).isEmpty && Get(r).isEmpty && !r.HasPropertyBlock(), "no block after the end");
        }

        // others' blocks — a whole block with P: P stays, ours goes · a whole block with our property c: Base c, the end c
        // bit for bit · an index block with P: the same
        [Test]
        public void ME03_AnothersBlocks()
        {
            var layers = Layers();
            var a = Cube("me3a", s_C0);
            a.SetPropertyBlock(Other(0.2f));
            var sa = Block(layers, a, s_Base, -1);
            sa.Reserve(1);
            var ha = sa.Begin(ContributionKind.Offset);
            sa.SetOffset(ha, new Color(0.1f, 0f, 0f, 0f));

            var b = Cube("me3b", s_C0);
            var theirs = new MaterialPropertyBlock();
            theirs.SetColor(s_Base, new Color(0.5f, 0.5f, 0.5f, 1f));
            b.SetPropertyBlock(theirs);
            Color c = Get(b).GetColor(s_Base);
            var sb = Block(layers, b, s_Base, -1);
            sb.Reserve(1);
            var hb = sb.Begin(ContributionKind.Offset);
            sb.SetOffset(hb, new Color(0.1f, 0f, 0f, 0f));

            var d = Cube("me3d", s_C0, s_C1);
            d.SetPropertyBlock(Other(0.2f), 1);
            var sd = Block(layers, d, s_Base, 1);
            sd.Reserve(1);
            var hd = sd.Begin(ContributionKind.Offset);
            sd.SetOffset(hd, new Color(0.1f, 0f, 0f, 0f));
            Tick(2);
            Assert.That(B(sb.Base, c), $"Base is their value — {sb.Base}");
            sa.End(ha);
            sb.End(hb);
            sd.End(hd);
            Tick(2);
            MaterialPropertyBlock ea = Get(a);
            Assert.That(ea.HasFloat(s_Smooth) && !ea.HasColor(s_Base), "the whole block's P stays, ours goes");
            Assert.That(B(Get(b).GetColor(s_Base), c), $"their color at the end — {Get(b).GetColor(s_Base)}");
            MaterialPropertyBlock ed = Get(d, 1);
            Assert.That(ed.HasFloat(s_Smooth) && !ed.HasColor(s_Base), "the index block's P stays, ours goes");
        }

        // another's index block only and our whole slot — after the end no whole block; the other takes its index block off
        // → no block at all
        [Test]
        public void ME04_AnothersIndexBlockOnly()
        {
            var layers = Layers();
            var r = Cube("me4", s_C0, s_C1);
            r.SetPropertyBlock(Other(0.2f), 1);
            var slot = Block(layers, r, s_Base, -1);
            slot.Reserve(1);
            var h = slot.Begin(ContributionKind.Offset);
            slot.SetOffset(h, new Color(0.1f, 0f, 0f, 0f));
            Tick(2);
            slot.End(h);
            Tick(2);
            Assert.That(Get(r).isEmpty, "no whole block after the end");
            r.SetPropertyBlock(null, 1);
            Assert.That(r.HasPropertyBlock(), Is.False, "no block once the other took its own off");
        }

        // the window rule: another's Get → Q → Set after our apply is erased by the next revert · another writing our
        // property wins · another taking the block off is left alone · another changing P in Update during the effect keeps
        // it (the snapshot is taken at every apply)
        [Test]
        public void ME05_WindowRule()
        {
            var layers = Layers();
            var a = Cube("me5a", s_C0);
            var sa = Block(layers, a, s_Base, -1);
            sa.Reserve(1);
            sa.SetOffset(sa.Begin(ContributionKind.Offset), new Color(0.1f, 0f, 0f, 0f));
            Tick();
            MaterialPropertyBlock w = Get(a);
            w.SetFloat(s_Metallic, 0.7f);
            a.SetPropertyBlock(w);
            Tick();
            Assert.That(Get(a).HasFloat(s_Metallic), Is.False, "the window's Q is erased (the rule)");

            var b = Cube("me5b", s_C0);
            var sb = Block(layers, b, s_Base, -1);
            sb.Reserve(1);
            var hb = sb.Begin(ContributionKind.Offset);
            sb.SetOffset(hb, new Color(0.1f, 0f, 0f, 0f));
            Tick();
            MaterialPropertyBlock theirs = Get(b);
            theirs.SetColor(s_Base, new Color(0.1f, 0.1f, 0.9f, 1f));
            b.SetPropertyBlock(theirs);
            Color left = Get(b).GetColor(s_Base);
            sb.End(hb);
            Tick(2);
            Assert.That(Get(b).HasColor(s_Base) && B(Get(b).GetColor(s_Base), left), "their write of our property stays");

            var d = Cube("me5d", s_C0);
            d.SetPropertyBlock(Other(0.2f));
            var sd = Block(layers, d, s_Base, -1);
            sd.Reserve(1);
            var hd = sd.Begin(ContributionKind.Offset);
            sd.SetOffset(hd, new Color(0.1f, 0f, 0f, 0f));
            Tick();
            d.SetPropertyBlock(null);
            m_Scheduler.RunRevert();
            Assert.That(d.HasPropertyBlock(), Is.False, "their removal is left alone by the revert");
            m_Scheduler.RunUpdate(s_Frame);
            m_Scheduler.RunLateApply();
            sd.End(hd);
            Tick(2);
            Assert.That(d.HasPropertyBlock(), Is.False, "their removal stays after the end");

            var c = Cube("me5c", s_C0);
            c.SetPropertyBlock(Other(0.2f));
            var sc = Block(layers, c, s_Base, -1);
            sc.Reserve(1);
            var hc = sc.Begin(ContributionKind.Offset);
            sc.SetOffset(hc, new Color(0.1f, 0f, 0f, 0f));
            Tick();
            m_Scheduler.RunRevert();
            MaterialPropertyBlock u = Get(c);
            u.SetFloat(s_Smooth, 0.7f);
            c.SetPropertyBlock(u);
            m_Scheduler.RunUpdate(s_Frame);
            m_Scheduler.RunLateApply();
            sc.End(hc);
            Tick(2);
            Assert.That(B(Get(c).GetFloat(s_Smooth), 0.7f), $"P changed in Update during the effect stays — {Get(c).GetFloat(s_Smooth)}");
        }

        // the material's composition: weight 1 over an offset is the target as the block gives it back · weight 0 writes
        // nothing · two properties of one renderer share the block and leave none
        [Test]
        public void ME06_Composition()
        {
            var layers = Layers();
            var target = new Color(0.2f, 0.9f, 0.4f, 1f);
            var a = Cube("me6a", s_C0);
            var sa = Block(layers, a, s_Base, -1);
            sa.Reserve(2);
            sa.SetOffset(sa.Begin(ContributionKind.Offset), new Color(0.1f, 0f, 0f, 0f));
            sa.SetOverride(sa.Begin(ContributionKind.Override), target, 1f);
            var b = Cube("me6b", s_C0);
            var sb = Block(layers, b, s_Base, -1);
            sb.Reserve(1);
            sb.SetOverride(sb.Begin(ContributionKind.Override), target, 0f);
            Tick();
            Assert.That(B(Get(a).GetColor(s_Base), RoundTrip(target)), $"weight 1 is the target — {Get(a).GetColor(s_Base)}");
            Assert.That(!b.HasPropertyBlock() && !sb.Pending, "weight 0 writes nothing");

            var c = Cube("me6c", s_C0);
            var sc = Block(layers, c, s_Base, -1);
            var se = Block(layers, c, s_Emission, -1);
            sc.Reserve(1);
            se.Reserve(1);
            var hc = sc.Begin(ContributionKind.Offset);
            var he = se.Begin(ContributionKind.Offset);
            sc.SetOffset(hc, new Color(0.1f, 0f, 0f, 0f));
            se.SetOffset(he, new Color(0f, 0.1f, 0f, 0f));
            Tick();
            sc.End(hc);
            se.End(he);
            Tick(2);
            Assert.That(c.HasPropertyBlock(), Is.False, "two properties leave no block");
        }

        // two properties end two frames apart over another's block (P) — the other's block only
        [Test]
        public void ME06_TwoPropertiesStaggered()
        {
            var layers = Layers();
            var c = Cube("me6s", s_C0);
            c.SetPropertyBlock(Other(0.2f));
            var sc = Block(layers, c, s_Base, -1);
            var se = Block(layers, c, s_Emission, -1);
            sc.Reserve(1);
            se.Reserve(1);
            var hc = sc.Begin(ContributionKind.Offset);
            var he = se.Begin(ContributionKind.Offset);
            sc.SetOffset(hc, new Color(0.1f, 0f, 0f, 0f));
            se.SetOffset(he, new Color(0f, 0.1f, 0f, 0f));
            Tick();
            sc.End(hc);
            Tick(2);
            se.End(he);
            Tick(2);
            MaterialPropertyBlock e = Get(c);
            Assert.That(e.HasFloat(s_Smooth) && !e.HasColor(s_Base) && !e.HasColor(s_Emission), "the other's block only");
        }

        // Persist on the MPB leaves the block; its value is the next effect's base
        [Test]
        public void ME07_PersistLeavesTheBlock()
        {
            var layers = Layers();
            var r = Cube("me7", s_C0);
            var slot = Block(layers, r, s_Base, -1);
            slot.Reserve(1);
            var green = new Color(0.2f, 0.9f, 0.2f, 1f);
            slot.WriteValue(green);
            Tick();
            Assert.That(r.HasPropertyBlock(), "the block stays");
            var h = slot.Begin(ContributionKind.Offset);
            slot.SetOffset(h, new Color(0.1f, 0f, 0f, 0f));
            Tick();
            Assert.That(B(slot.Base, RoundTrip(green)), $"the next effect's base — {slot.Base}");
        }

        // Persist taken back the way the player does (the start value through WriteValue) — no block; over another's block
        // its P only
        [Test]
        public void ME07_PersistTakenBack()
        {
            var layers = Layers();
            var green = new Color(0.2f, 0.9f, 0.2f, 1f);
            var a = Cube("me7a", s_C0);
            var sa = Block(layers, a, s_Base, -1);
            sa.Reserve(1);
            Color start = sa.Base;
            sa.WriteValue(green);
            Tick();
            sa.WriteValue(start);
            Tick();
            Assert.That(a.HasPropertyBlock(), Is.False, "no block after taking it back");

            var b = Cube("me7b", s_C0);
            b.SetPropertyBlock(Other(0.2f));
            var sb = Block(layers, b, s_Base, -1);
            sb.Reserve(1);
            Color startB = sb.Base;
            sb.WriteValue(green);
            Tick();
            sb.WriteValue(startB);
            Tick();
            MaterialPropertyBlock e = Get(b);
            Assert.That(e.HasFloat(s_Smooth) && !e.HasColor(s_Base), "the other's P only");
        }

        // Persist's life — after the release the MPB's block stays; the instance's original comes back
        [Test]
        public void ME07_PersistLife()
        {
            var layers = Layers();
            var green = new Color(0.2f, 0.9f, 0.2f, 1f);
            var a = Cube("me7c", s_C0);
            var sa = Block(layers, a, s_Base, -1);
            sa.Reserve(1);
            sa.WriteValue(green);
            Tick();
            layers.Release(sa, 1);
            Tick();
            Assert.That(a.HasPropertyBlock(), "MPB — the block stays");

            var b = Cube("me7d", s_C0);
            Material original = b.sharedMaterial;
            Color color = original.GetColor(s_Base);
            var sb = Copy(layers, b, s_Base, -1);
            sb.Reserve(1);
            sb.WriteValue(green);
            Tick();
            layers.Release(sb, 1);
            Tick();
            Assert.That(ReferenceEquals(b.sharedMaterial, original) && B(original.GetColor(s_Base), color), "instance — the original back");
        }

        // the material array shrinks below the index during the effect — no error log, the slot drops; grown back, our
        // color is not there
        [Test]
        public void ME08_IndexOutOfRange()
        {
            var layers = Layers();
            var r = Cube("me8", s_C0, s_C1);
            Material[] two = r.sharedMaterials;
            var slot = Block(layers, r, s_Base, 1);
            slot.Reserve(1);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), new Color(0.1f, 0f, 0f, 0f));
            Tick();
            int slots = layers.SlotCount;
            r.sharedMaterials = new[] { two[0] };
            Tick(2);
            LogAssert.NoUnexpectedReceived();
            Assert.That(layers.SlotCount, Is.EqualTo(slots - 1), "dropped");
            r.sharedMaterials = two;
            Assert.That(Get(r, 1).HasColor(s_Base), Is.False, "our color is not back");
        }

        // an instance slot at index 1 — a copy there (no log, the original untouched) · the copy's color during · after the
        // end the base written once and read back · no block · −1 makes a copy per material
        [Test]
        public void ME09_Instance()
        {
            var layers = Layers();
            var r = Cube("me9", s_C0, s_C1);
            Material[] originals = r.sharedMaterials;
            Color o1 = originals[1].GetColor(s_Base);
            var slot = Copy(layers, r, s_Base, 1);
            LogAssert.NoUnexpectedReceived();
            Assert.That(ReferenceEquals(r.sharedMaterials[0], originals[0]) && !ReferenceEquals(r.sharedMaterials[1], originals[1]), "a copy at index 1 only");
            slot.Reserve(1);
            var h = slot.Begin(ContributionKind.Offset);
            Color before = Mat(r, 1);
            for (int f = 0; f < 3; f++)
            {
                slot.SetOffset(h, new Color(0.1f * Mathf.Sin(f + 1), 0.05f, 0f, 0f));
                Tick();
            }
            Assert.That(B(Mat(r, 1), before), Is.False, "the copy's color during the effect");
            slot.End(h);
            Tick(2);
            Assert.That(B(Mat(r, 1), Once(before)), $"the base written once, read back — {Mat(r, 1)} vs {before}");
            Assert.That(B(originals[1].GetColor(s_Base), o1), "the original untouched");
            Assert.That(r.HasPropertyBlock(), Is.False, "no block");
            var r2 = Cube("me9b", s_C0, s_C1);
            Material[] o2 = r2.sharedMaterials;
            Copy(layers, r2, s_Base, -1);
            Assert.That(!ReferenceEquals(r2.sharedMaterials[0], o2[0]) && !ReferenceEquals(r2.sharedMaterials[1], o2[1]), "−1 — a copy per material");
        }

        // −1 over two materials of different colors — after the end each copy's own color, read back; a hundred frames end
        // where one frame did; Base is the color before
        [Test]
        public void ME09_EachCopyItsOwn()
        {
            var layers = Layers();
            var r = Cube("me9s", s_C0, s_C1);
            var slot = Copy(layers, r, s_Base, -1);
            slot.Reserve(1);
            Color c0 = Mat(r, 0), c1 = Mat(r, 1);
            var h = slot.Begin(ContributionKind.Offset);
            slot.SetOffset(h, new Color(0.1f, 0f, 0f, 0f));
            Tick();
            slot.End(h);
            Tick(2);
            Assert.That(B(Mat(r, 0), Once(c0)) && B(Mat(r, 1), Once(c1)), $"each its own — {Mat(r, 0)} · {Mat(r, 1)}");
            h = slot.Begin(ContributionKind.Offset);
            for (int f = 0; f < 100; f++)
            {
                slot.SetOffset(h, new Color(0.05f * (f & 1), 0.02f, 0f, 0f));
                Tick();
            }
            slot.End(h);
            Tick(2);
            Assert.That(B(Mat(r, 0), Once(c0)) && B(Mat(r, 1), Once(c1)), $"after a hundred frames the same — {Mat(r, 0)} · {Mat(r, 1)}");
            Assert.That(B(slot.Base, c0), $"Base is the color before — {slot.Base}");
        }

        // the last reference released → after the next Revert the original and no copy · the renderer destroyed → no copy ·
        // the game swapped the material meanwhile → the game's stays
        [Test]
        public void ME10_InstanceLifecycle()
        {
            var layers = Layers();
            var a = Cube("me10a", s_C0);
            Material original = a.sharedMaterial;
            int count = MaterialCount();
            var sa = Copy(layers, a, s_Base, -1);
            sa.Reserve(1);
            sa.SetOffset(sa.Begin(ContributionKind.Offset), new Color(0.1f, 0f, 0f, 0f));
            Tick();
            layers.Release(sa, 1);
            Tick();
            Assert.That(ReferenceEquals(a.sharedMaterial, original), "released — the original");
            Assert.That(MaterialCount(), Is.EqualTo(count), "released — no copy");

            var b = Cube("me10b", s_C0);
            count = MaterialCount();
            var sb = Copy(layers, b, s_Base, -1);
            sb.Reserve(1);
            sb.SetOffset(sb.Begin(ContributionKind.Offset), new Color(0.1f, 0f, 0f, 0f));
            Tick();
            Object.DestroyImmediate(b.gameObject);
            Tick();
            Assert.That(MaterialCount(), Is.EqualTo(count), "destroyed — no copy");

            var c = Cube("me10c", s_C0);
            var sc = Copy(layers, c, s_Base, -1);
            sc.Reserve(1);
            sc.SetOffset(sc.Begin(ContributionKind.Offset), new Color(0.1f, 0f, 0f, 0f));
            Tick();
            var game = new Material(c.sharedMaterial) { name = "game's" };
            m_Objects.Add(game);
            c.sharedMaterial = game;
            layers.Release(sc, 1);
            Tick();
            Assert.That(ReferenceEquals(c.sharedMaterial, game), "the game's material stays");
        }

        // the MPB released during the effect — the next Revert puts the block back and drops the slot
        [Test]
        public void ME10_BlockReleased()
        {
            var layers = Layers();
            var r = Cube("me10s", s_C0);
            var slot = Block(layers, r, s_Base, -1);
            slot.Reserve(1);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), new Color(0.1f, 0f, 0f, 0f));
            Tick();
            int slots = layers.SlotCount;
            layers.Release(slot, 1);
            m_Scheduler.RunRevert();
            Assert.That(r.HasPropertyBlock(), Is.False, "no block");
            Assert.That(layers.SlotCount, Is.EqualTo(slots - 1), "dropped");
        }

        // the context's Shutdown during the effect — MPB: the other's block · none where there was none; instance: the
        // original material (the drop hook)
        [Test]
        public void ME11_Shutdown()
        {
            var layers = Layers();
            var a = Cube("me11a", s_C0);
            var b = Cube("me11b", s_C0);
            b.SetPropertyBlock(Other(0.2f));
            var c = Cube("me11c", s_C0);
            Material original = c.sharedMaterial;
            var sa = Block(layers, a, s_Base, -1);
            var sb = Block(layers, b, s_Base, -1);
            var sc = Copy(layers, c, s_Base, -1);
            sa.Reserve(1);
            sb.Reserve(1);
            sc.Reserve(1);
            sa.SetOffset(sa.Begin(ContributionKind.Offset), new Color(0.1f, 0f, 0f, 0f));
            sb.SetOffset(sb.Begin(ContributionKind.Offset), new Color(0.1f, 0f, 0f, 0f));
            sc.SetOffset(sc.Begin(ContributionKind.Offset), new Color(0.1f, 0f, 0f, 0f));
            Tick();
            m_Scheduler.Shutdown();
            Assert.That(a.HasPropertyBlock(), Is.False, "MPB — none");
            MaterialPropertyBlock e = Get(b);
            Assert.That(e.HasFloat(s_Smooth) && !e.HasColor(s_Base), "MPB — the other's block");
            Assert.That(ReferenceEquals(c.sharedMaterial, original), "instance — the original");
        }

        // the renderers destroyed during the effect — MPB and instance drop without an error log
        [Test]
        public void ME12_DestroyedDropQuietly()
        {
            var layers = Layers();
            var a = Cube("me12a", s_C0);
            var b = Cube("me12b", s_C0);
            var sa = Block(layers, a, s_Base, -1);
            var sb = Copy(layers, b, s_Base, -1);
            sa.Reserve(1);
            sb.Reserve(1);
            sa.SetOffset(sa.Begin(ContributionKind.Offset), new Color(0.1f, 0f, 0f, 0f));
            sb.SetOffset(sb.Begin(ContributionKind.Offset), new Color(0.1f, 0f, 0f, 0f));
            Tick();
            int slots = layers.SlotCount;
            Object.DestroyImmediate(a.gameObject);
            Object.DestroyImmediate(b.gameObject);
            Tick(2);
            LogAssert.NoUnexpectedReceived();
            Assert.That(layers.SlotCount, Is.EqualTo(slots - 3), "both dropped — the instance's copies slot with it");
        }

        // no allocation — the MPB whole · index and the instance's contribution updates and Tick
        [Test]
        public void ME13_NoAllocation()
        {
            var layers = Layers();
            var a = Cube("me13a", s_C0, s_C1);
            var b = Cube("me13b", s_C0);
            var sa = Block(layers, a, s_Base, -1);
            var si = Block(layers, a, s_Emission, 1);
            var sb = Copy(layers, b, s_Base, -1);
            sa.Reserve(1);
            si.Reserve(1);
            sb.Reserve(1);
            var ha = sa.Begin(ContributionKind.Offset);
            var hi = si.Begin(ContributionKind.Offset);
            var hb = sb.Begin(ContributionKind.Offset);
            int k = 0;
            TestDelegate run = () =>
            {
                k++;
                sa.SetOffset(ha, new Color(0.01f * (k & 3), 0f, 0f, 0f));
                si.SetOffset(hi, new Color(0f, 0.01f * (k & 3), 0f, 0f));
                sb.SetOffset(hb, new Color(0f, 0f, 0.01f * (k & 3), 0f));
                m_Scheduler.Tick(s_Frame);
            };
            for (int i = 0; i < 20; i++)
                run();
            Assert.That(Allocates(run), Is.False, "allocates");
        }

        // positive control: the constraint sees an allocation
        [Test]
        public void ME13_AllocationIsSeen()
        {
            Assert.That(Allocates(() => s_Sink = new byte[16]), Is.True);
        }

        // the ledger: after Reserve the MPB's and the instance's ledgers never grow — a stop and a new play in one frame,
        // again and again
        [Test]
        public void ME13_NoLedgerGrowth()
        {
            var layers = Layers();
            var a = Cube("me13c", s_C0);
            var b = Cube("me13d", s_C0);
            var sa = Block(layers, a, s_Base, -1);
            var sb = Copy(layers, b, s_Base, -1);
            sa.Reserve(2);
            sb.Reserve(2);
            var ha = sa.Begin(ContributionKind.Offset);
            var hb = sb.Begin(ContributionKind.Offset);
            for (int i = 0; i < 10; i++)
            {
                sa.End(ha);
                sb.End(hb);
                ha = sa.Begin(ContributionKind.Offset);
                hb = sb.Begin(ContributionKind.Offset);
                sa.SetOffset(ha, new Color(0.01f * (i & 3), 0f, 0f, 0f));
                sb.SetOffset(hb, new Color(0.01f * (i & 3), 0f, 0f, 0f));
                Tick();
            }
            Assert.That(sa.GrowthCount + sb.GrowthCount, Is.EqualTo(0), "growth");
        }

        // keys: Base is the value before composition during the effect · (renderer, property, index) → one slot · another
        // index or another mode → another slot
        [Test]
        public void ME14_BaseAndKeys()
        {
            var layers = Layers();
            var r = Cube("me14", s_C0);
            Color m0 = Mat(r, 0);
            var slot = Block(layers, r, s_Base, -1);
            slot.Reserve(1);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), new Color(0.2f, 0f, 0f, 0f));
            Tick();
            Assert.That(B(slot.Base, m0), $"Base during the effect — {slot.Base}");
            Assert.That(ReferenceEquals(Block(layers, r, s_Base, -1), slot), "the same key — the same slot");
            Assert.That(ReferenceEquals(Block(layers, r, s_Base, 0), slot), Is.False, "another index — another slot");
            Assert.That(ReferenceEquals(Copy(layers, r, s_Base, -1), slot), Is.False, "another mode — another slot");
        }

        // an index slot over another's whole block (P · our property 0.5) — Base 0.5 · P in the index block during the
        // effect · after the end no index block and the whole block as it was; over another's index block (our property c1)
        // and whole block (c2) Base is c1
        [Test]
        public void ME15_IndexOverAWholeBlock()
        {
            var layers = Layers();
            var r = Cube("me15", s_C0);
            var theirs = Other(0.2f);
            theirs.SetColor(s_Base, new Color(0.5f, 0.5f, 0.5f, 1f));
            r.SetPropertyBlock(theirs);
            Color c = Get(r).GetColor(s_Base);
            var slot = Block(layers, r, s_Base, 0);
            slot.Reserve(1);
            var h = slot.Begin(ContributionKind.Offset);
            slot.SetOffset(h, new Color(0.1f, 0f, 0f, 0f));
            Tick();
            Assert.That(B(slot.Base, c), $"Base is the shown value — {slot.Base}");
            Assert.That(Get(r, 0).HasFloat(s_Smooth), "P in the index block during the effect");
            slot.End(h);
            Tick(2);
            Assert.That(Get(r, 0).isEmpty && Get(r).HasFloat(s_Smooth) && B(Get(r).GetColor(s_Base), c), "no index block · the whole block as it was");

            var r2 = Cube("me15b", s_C0);
            var whole = new MaterialPropertyBlock();
            whole.SetColor(s_Base, new Color(0.3f, 0.3f, 0.3f, 1f));
            r2.SetPropertyBlock(whole);
            var index = new MaterialPropertyBlock();
            index.SetColor(s_Base, new Color(0.7f, 0.7f, 0.7f, 1f));
            r2.SetPropertyBlock(index, 0);
            Color c1 = Get(r2, 0).GetColor(s_Base);
            var s2 = Block(layers, r2, s_Base, 0);
            s2.Reserve(1);
            s2.SetOffset(s2.Begin(ContributionKind.Offset), new Color(0.1f, 0f, 0f, 0f));
            Tick();
            Assert.That(B(s2.Base, c1), $"Base is the index block's — {s2.Base}");
        }
    }
}
