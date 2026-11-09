using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using xpTURN.Feedback.Tests;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the light slot and the toggle (EditMode — the light slot's clamp · end · overrides, the toggle's writes · another's
    // writes · overlap · threshold · door · stages · close · release · destroyed target · allocation · keys and the callbacks
    // inside the stage). The toggle's callbacks are counted by the [ExecuteAlways] target. No animation module types here
    public sealed class LayerLightToggleTests
    {
        const float Dt = 1f / 60f;
        static readonly FrameTime s_Frame = new FrameTime(Dt, Dt);
        static object s_Sink;

        readonly List<Object> m_Objects = new List<Object>();
        readonly List<LayerToggleTarget> m_Targets = new List<LayerToggleTarget>();
        FeedbackScheduler m_Scheduler;

        [TearDown]
        public void TearDown()
        {
            // hooks off first — the cleanup's Shutdown and DestroyImmediate call OnEnable · OnDisable
            foreach (LayerToggleTarget t in m_Targets)
            {
                if (t == null)
                    continue;
                t.OnEnableHook = null;
                t.OnDisableHook = null;
            }
            m_Targets.Clear();
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

        GameObject G(string name)
        {
            var go = new GameObject(name);
            m_Objects.Add(go);
            return go;
        }

        LayerToggleTarget Target(string name)
        {
            var t = G(name).AddComponent<LayerToggleTarget>();
            m_Targets.Add(t);
            return t;
        }

        Light L(string name, float intensity)
        {
            var l = G(name).AddComponent<Light>();
            l.intensity = intensity;
            return l;
        }

        static bool Bits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        static GameObjectActiveSlot Toggle(PropertyLayerRegistry r, GameObject go) => r.GetOrCreate(go, 0, 0, () => new GameObjectActiveSlot(go));

        static LightIntensitySlot Intensity(PropertyLayerRegistry r, Light l) => r.GetOrCreate(l, 0, 0, () => new LightIntensitySlot(l));

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

        // light: base 0.2 + offset −0.5 over frames — during the effect +0 (the engine clamps), the written flag and Base 0.2
        // each frame; after the end 0.2 bit for bit
        [Test]
        public void LE01_LightClampAndEnd()
        {
            var layers = Layers();
            var l = L("light", 0.2f);
            var slot = Intensity(layers, l);
            slot.Reserve(1);
            var h = slot.Begin(ContributionKind.Offset);
            for (int f = 0; f < 5; f++)
            {
                slot.SetOffset(h, -0.5f);
                Tick();
                Assert.That(Bits(l.intensity, 0f), $"frame {f}: during the effect +0 — {l.intensity}");
                Assert.That(slot.Pending, $"frame {f}: written");
                Assert.That(Bits(slot.Base, 0.2f), $"frame {f}: Base 0.2 — {slot.Base}");
            }
            slot.End(h);
            Tick(2);
            Assert.That(Bits(l.intensity, 0.2f), $"end 0.2 — {l.intensity}");
        }

        // light overrides — weight 1 over an offset is the target (bits) · base 1e20 → target 1 is 1 (the arithmetic end is
        // 0, in Mono too) · 1.5 is the target · 0 writes nothing · 0.5 lies between
        [Test]
        public void LE02_LightOverrides()
        {
            var layers = Layers();
            var la = L("a", 0.2f);
            var lb = L("b", 1e20f);
            var lc = L("c", 0.2f);
            var ld = L("d", 0.2f);
            var le = L("e", 0.2f);
            var sa = Intensity(layers, la);
            var sb = Intensity(layers, lb);
            var sc = Intensity(layers, lc);
            var sd = Intensity(layers, ld);
            var se = Intensity(layers, le);
            sa.Reserve(2);
            sb.Reserve(1);
            sc.Reserve(1);
            sd.Reserve(1);
            se.Reserve(1);
            sa.SetOffset(sa.Begin(ContributionKind.Offset), 0.37f);
            sa.SetOverride(sa.Begin(ContributionKind.Override), 3f, 1f);
            sb.SetOverride(sb.Begin(ContributionKind.Override), 1f, 1f);
            sc.SetOverride(sc.Begin(ContributionKind.Override), 3f, 1.5f);
            sd.SetOverride(sd.Begin(ContributionKind.Override), 3f, 0f);
            se.SetOverride(se.Begin(ContributionKind.Override), 3f, 0.5f);
            Tick();
            Assert.That(Bits(la.intensity, 3f), $"weight 1 over an offset — {la.intensity}");
            Assert.That(Bits(lb.intensity, 1f), $"1e20 → 1 — {lb.intensity}");
            Assert.That(Bits(lc.intensity, 3f), $"weight 1.5 — {lc.intensity}");
            Assert.That(Bits(ld.intensity, 0.2f) && !sd.Pending, $"weight 0 — {ld.intensity} · written {sd.Pending}");
            Assert.That(le.intensity > 0.2f && le.intensity < 3f, $"weight 0.5 — {le.intensity}");
        }

        // a blink of weight 1 · 0 held two frames each, four times — OnEnable · OnDisable = the toggles (4 · 4), the end is
        // the before. Flipped every frame, a Revert · Apply cycle would give the same counts
        [Test]
        public void LE03_BlinkWritesOnlyOnChange()
        {
            var layers = Layers();
            var t = Target("t");
            var slot = Toggle(layers, t.gameObject);
            slot.Reserve(1);
            var h = slot.Begin();
            int enables = t.Enables, disables = t.Disables;
            for (int i = 0; i < 16; i++)
            {
                slot.SetOverride(h, false, i % 4 < 2 ? 1f : 0f);
                Tick();
            }
            slot.End(h);
            Tick(2);
            Assert.That(t.Enables - enables, Is.EqualTo(4), "OnEnable");
            Assert.That(t.Disables - disables, Is.EqualTo(4), "OnDisable");
            Assert.That(t.gameObject.activeSelf, "the end is the before (on)");
        }

        // another's write: the game turns the target off in the blink's on stretch · between Begin and the first Tick · in a
        // weight-0 stretch (nothing written yet) — each time the game's value stays after the end
        [Test]
        public void LE04_ForeignWritesStay()
        {
            var layers = Layers();
            var a = G("a");
            var ta = Toggle(layers, a);
            ta.Reserve(1);
            var ha = ta.Begin();
            ta.SetOverride(ha, false, 1f);
            Tick();
            ta.SetOverride(ha, false, 0f);
            Tick();
            a.SetActive(false);
            Tick();
            ta.End(ha);
            Tick(2);

            var b = G("b");
            var tb = Toggle(layers, b);
            tb.Reserve(1);
            var hb = tb.Begin();
            tb.SetOverride(hb, false, 0f);
            b.SetActive(false);
            Tick();
            tb.End(hb);
            Tick(2);

            var c = G("c");
            var tc = Toggle(layers, c);
            tc.Reserve(1);
            var hc = tc.Begin();
            tc.SetOverride(hc, false, 0f);
            Tick(2);
            c.SetActive(false);
            Tick();
            tc.End(hc);
            Tick(2);
            Assert.That(!a.activeSelf, "the blink's on stretch — the game's off stays");
            Assert.That(!b.activeSelf, "between Begin and the first Tick — the game's off stays");
            Assert.That(!c.activeSelf, "a weight-0 stretch — the game's off stays");
        }

        // overlap: two contributions — the later start wins; the second begun while the blink holds the target off, the end
        // is still the first before
        [Test]
        public void LE05_LaterStartWins()
        {
            var layers = Layers();
            var go = G("t");
            var slot = Toggle(layers, go);
            slot.Reserve(2);
            var h1 = slot.Begin();
            slot.SetOverride(h1, false, 1f);
            Tick();
            Assert.That(!go.activeSelf, "the first holds it off");
            var h2 = slot.Begin();
            slot.SetOverride(h2, true, 1f);
            Tick();
            Assert.That(go.activeSelf, "the later start wins");
            slot.End(h2);
            Tick();
            Assert.That(!go.activeSelf, "the first again");
            slot.End(h1);
            Tick(2);
            Assert.That(go.activeSelf, "the end is the first before");
        }

        // weight 0.5 lights; 0 · NaN · negative do not
        [Test]
        public void LE06_Threshold()
        {
            var layers = Layers();
            float[] weights = { 0.5f, 0f, float.NaN, -1f };
            var gos = new GameObject[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                gos[i] = G("w" + i);
                var slot = Toggle(layers, gos[i]);
                slot.Reserve(1);
                slot.SetOverride(slot.Begin(), false, weights[i]);
            }
            Tick();
            Assert.That(!gos[0].activeSelf, "0.5 lights");
            Assert.That(gos[1].activeSelf, "0 does not");
            Assert.That(gos[2].activeSelf, "NaN does not");
            Assert.That(gos[3].activeSelf, "negative does not");
        }

        // the door and Base: WriteValue(false) — the same state — while the blink holds the target off stays off after the
        // end; Base in that stretch is the before (on), outside an episode the value now
        [Test]
        public void LE07_SetDoorAndBase()
        {
            var layers = Layers();
            var go = G("t");
            var slot = Toggle(layers, go);
            slot.Reserve(1);
            Assert.That(slot.Base, "outside — on");
            var h = slot.Begin();
            slot.SetOverride(h, false, 1f);
            Tick();
            Assert.That(slot.Base, "the blink holds it off — Base is the before (on)");
            slot.WriteValue(false);
            Tick();
            slot.End(h);
            Tick(2);
            Assert.That(!go.activeSelf, "the Set's off stays after the end");
            Assert.That(!slot.Base, "outside — the value now (off)");
        }

        // deferred in a captured episode: (b) the opposite state while the blink holds the target off — no callback that
        // frame, Base is the new value, on after the end · (c) a Set from the toggle's own write (OnDisable) — no nested
        // SetActive error, off meanwhile, on after the end
        [Test]
        public void LE07_SetDeferredInAnEpisode()
        {
            var layers = Layers();
            var b = Target("b");
            var tb = Toggle(layers, b.gameObject);
            tb.Reserve(1);
            var hb = tb.Begin();
            tb.SetOverride(hb, false, 1f);
            Tick();
            int enables = b.Enables, disables = b.Disables;
            tb.WriteValue(true);
            Assert.That(tb.Base, "(b) Base after the Set");
            Tick();
            Assert.That(b.Enables - enables + b.Disables - disables, Is.EqualTo(0), "(b) callbacks that frame");
            Assert.That(!b.gameObject.activeSelf, "(b) the blink still holds it off");
            tb.End(hb);
            Tick(2);
            Assert.That(b.gameObject.activeSelf, "(b) on after the end");

            var c = Target("c");
            var tc = Toggle(layers, c.gameObject);
            tc.Reserve(1);
            bool asked = false;
            c.OnDisableHook = _ =>
            {
                if (asked)
                    return;
                asked = true;
                tc.WriteValue(true);
            };
            var hc = tc.Begin();
            tc.SetOverride(hc, false, 1f);
            Tick();
            Assert.That(asked, "(c) asked from the write");
            Assert.That(!c.gameObject.activeSelf && !c.gameObject.activeInHierarchy, $"(c) off — self {c.gameObject.activeSelf} · in hierarchy {c.gameObject.activeInHierarchy}");
            Tick();
            Assert.That(!c.gameObject.activeSelf, "(c) off while the blink holds");
            tc.End(hc);
            Tick(2);
            LogAssert.NoUnexpectedReceived();
            Assert.That(c.gameObject.activeSelf, "(c) on after the end");
        }

        // the toggle's share: the stages called apart — the toggle has changed after RunUpdate (it does not wait for
        // RunLateApply), the light only after RunLateApply
        [Test]
        public void LE08_TogglesInUpdateLightInLateApply()
        {
            var layers = Layers();
            var go = G("t");
            var l = L("light", 0.2f);
            var toggle = Toggle(layers, go);
            var light = Intensity(layers, l);
            toggle.Reserve(1);
            light.Reserve(1);
            toggle.SetOverride(toggle.Begin(), false, 1f);
            light.SetOffset(light.Begin(ContributionKind.Offset), 1f);
            m_Scheduler.RunRevert();
            m_Scheduler.RunUpdate(s_Frame);
            Assert.That(!go.activeSelf, "the toggle after RunUpdate");
            Assert.That(Bits(l.intensity, 0.2f), $"the light after RunUpdate — {l.intensity}");
            m_Scheduler.RunLateApply();
            Assert.That(Bits(l.intensity, 0.2f + 1f), $"the light after RunLateApply — {l.intensity}");
        }

        // Shutdown during the effect puts the before back · while the toggle shows the before (a weight-0 stretch after the
        // blink) the game writes the opposite and the context closes without an apply — the game's value stays (a game write
        // in the blink's off stretch equals the before and cannot show the judgment) · before the capture (no apply yet)
        // nothing changes
        [Test]
        public void LE09_ShutdownPutsTheBeforeBack()
        {
            var layers = Layers();
            var a = G("a");
            var d = G("d");
            var c = G("c");
            var ta = Toggle(layers, a);
            var td = Toggle(layers, d);
            var tc = Toggle(layers, c);
            ta.Reserve(1);
            td.Reserve(1);
            tc.Reserve(1);
            ta.SetOverride(ta.Begin(), false, 1f);
            var hd = td.Begin();
            td.SetOverride(hd, false, 1f);
            Tick();
            td.SetOverride(hd, false, 0f);
            Tick();
            Assert.That(!a.activeSelf && d.activeSelf, "a held off · d back to its before");
            d.SetActive(false);
            tc.SetOverride(tc.Begin(), false, 1f);
            m_Scheduler.Shutdown();
            m_Scheduler = null;
            Assert.That(a.activeSelf, "the before (on)");
            Assert.That(!d.activeSelf, "the game's off stays");
            Assert.That(c.activeSelf, "no apply yet — untouched (on)");
        }

        // the context closed inside the toggle's own write (its OnDisable) — the pass's Close after the write puts the
        // before back, no error
        [Test]
        public void LE09_ClosedInsideItsOwnWrite()
        {
            var layers = Layers();
            var e = Target("e");
            var slot = Toggle(layers, e.gameObject);
            slot.Reserve(1);
            bool closed = false;
            e.OnDisableHook = _ =>
            {
                if (closed)
                    return;
                closed = true;
                m_Scheduler.Shutdown();
            };
            slot.SetOverride(slot.Begin(), false, 1f);
            Tick();
            m_Scheduler = null;
            LogAssert.NoUnexpectedReceived();
            Assert.That(closed, "closed inside the write");
            Assert.That(e.gameObject.activeSelf, "the before (on)");
        }

        // the last reference released during the effect — the next Revert puts the before back and drops the slot; the slot
        // got again has no old contribution
        [Test]
        public void LE10_ReleasePutsBackAndDrops()
        {
            var layers = Layers();
            var go = G("t");
            var slot = Toggle(layers, go);
            slot.Reserve(1);
            slot.SetOverride(slot.Begin(), false, 1f);
            Tick();
            Assert.That(!go.activeSelf, "off during the effect");
            int slots = layers.SlotCount;
            layers.Release(slot, 1);
            m_Scheduler.RunRevert();
            Assert.That(go.activeSelf, "the next Revert puts the before back");
            Assert.That(layers.SlotCount, Is.EqualTo(slots - 1), "dropped");
            m_Scheduler.RunUpdate(s_Frame);
            m_Scheduler.RunLateApply();
            var again = Toggle(layers, go);
            Assert.That(again.LiveContributions, Is.EqualTo(0), "no old contribution");
            Tick();
            Assert.That(go.activeSelf, "stays on");
        }

        // the light and the object destroyed during the effect — both drop out without an error log
        [Test]
        public void LE11_DestroyedTargetsDropQuietly()
        {
            var layers = Layers();
            var l = L("light", 0.2f);
            var go = G("t");
            var light = Intensity(layers, l);
            var toggle = Toggle(layers, go);
            light.Reserve(1);
            toggle.Reserve(1);
            light.SetOffset(light.Begin(ContributionKind.Offset), 1f);
            toggle.SetOverride(toggle.Begin(), false, 1f);
            Tick();
            int slots = layers.SlotCount;
            Object.DestroyImmediate(l.gameObject);
            Object.DestroyImmediate(go);
            Tick(2);
            LogAssert.NoUnexpectedReceived();
            Assert.That(layers.SlotCount, Is.EqualTo(slots - 2), "both dropped");
        }

        // Begin on a destroyed object does not throw; the next Revert drops the slot
        [Test]
        public void LE11_BeginOnADestroyedTarget()
        {
            var layers = Layers();
            var go = G("t");
            var slot = Toggle(layers, go);
            slot.Reserve(1);
            Object.DestroyImmediate(go);
            Assert.DoesNotThrow(() => slot.Begin(), "Begin");
            int slots = layers.SlotCount;
            Tick();
            Assert.That(layers.SlotCount, Is.EqualTo(slots - 1), "dropped");
        }

        // no allocation — the light's and the toggle's contribution updates and Tick with the toggle flipping an empty object
        [Test]
        public void LE12_NoAllocation()
        {
            var layers = Layers();
            var l = L("light", 0.2f);
            var go = G("t");
            var light = Intensity(layers, l);
            var toggle = Toggle(layers, go);
            light.Reserve(2);
            toggle.Reserve(1);
            var ho = light.Begin(ContributionKind.Offset);
            var hv = light.Begin(ContributionKind.Override);
            var ht = toggle.Begin();
            int k = 0;
            TestDelegate run = () =>
            {
                k++;
                light.SetOffset(ho, 0.1f * (k & 3));
                light.SetOverride(hv, 2f, (k & 1) == 0 ? 1f : 0f);
                toggle.SetOverride(ht, false, (k & 1) == 0 ? 1f : 0f);
                m_Scheduler.Tick(s_Frame);
            };
            for (int i = 0; i < 20; i++)
                run();
            Assert.That(Allocates(run), Is.False, "allocates");
        }

        // positive control: the constraint sees an allocation
        [Test]
        public void LE12_AllocationIsSeen()
        {
            Assert.That(Allocates(() => s_Sink = new byte[16]), Is.True);
        }

        // the ledger: after Reserve the toggle's ledger never grows — a stop and a new play in one frame, again and again
        [Test]
        public void LE12_NoLedgerGrowth()
        {
            var layers = Layers();
            var go = G("t");
            var slot = Toggle(layers, go);
            slot.Reserve(2);
            var h = slot.Begin();
            for (int i = 0; i < 10; i++)
            {
                slot.End(h);
                h = slot.Begin();
                slot.SetOverride(h, false, (i & 1) == 0 ? 1f : 0f);
                Tick();
            }
            Assert.That(slot.GrowthCount, Is.EqualTo(0), "growth");
        }

        // keys: the toggle, a transform slot and the light slot of one object are three slots; the same type is the same slot
        [Test]
        public void LE13_Keys()
        {
            var layers = Layers();
            var go = G("k");
            var l = go.AddComponent<Light>();
            var toggle = Toggle(layers, go);
            var pos = layers.GetOrCreate(go.transform, 0, 0, () => new LocalPositionSlot(go.transform));
            var light = Intensity(layers, l);
            Assert.That(layers.SlotCount, Is.EqualTo(3), "three slots");
            Assert.That(!ReferenceEquals((LayerSlot)toggle, pos) && !ReferenceEquals((LayerSlot)toggle, light) && !ReferenceEquals((LayerSlot)pos, light), "distinct");
            Assert.That(ReferenceEquals(Toggle(layers, go), toggle) && ReferenceEquals(Intensity(layers, l), light), "the same type — the same slot");
            Assert.That(layers.SlotCount, Is.EqualTo(3), "still three");
        }

        // the callbacks inside the stage: the toggle's OnEnable begins another toggle and a LateApply slot — the other toggle
        // next Tick, the LateApply slot this Tick; a toggle released by its own write's OnDisable is put back and dropped by
        // the next Revert
        [Test]
        public void LE14_CallbacksInsideTheStage()
        {
            var layers = Layers();
            var a = Target("a");
            var b = G("b");
            var l = L("light", 0.2f);
            var ta = Toggle(layers, a.gameObject);
            var tb = Toggle(layers, b);
            var light = Intensity(layers, l);
            ta.Reserve(1);
            tb.Reserve(1);
            light.Reserve(1);
            bool fired = false;
            a.OnEnableHook = _ =>
            {
                if (fired)
                    return;
                fired = true;
                tb.SetOverride(tb.Begin(), false, 1f);
                light.SetOffset(light.Begin(ContributionKind.Offset), 1f);
            };
            var ha = ta.Begin();
            ta.SetOverride(ha, false, 1f);
            Tick();
            ta.SetOverride(ha, false, 0f);
            Tick();
            Assert.That(fired, "a's OnEnable ran");
            Assert.That(Bits(l.intensity, 0.2f + 1f), $"the LateApply slot this Tick — {l.intensity}");
            Assert.That(b.activeSelf, "the other toggle not this Tick");
            Tick();
            Assert.That(!b.activeSelf, "the other toggle next Tick");

            var c = Target("c");
            var tc = Toggle(layers, c.gameObject);
            tc.Reserve(1);
            bool released = false;
            c.OnDisableHook = _ =>
            {
                if (released)
                    return;
                released = true;
                layers.Release(tc, 1);
            };
            tc.SetOverride(tc.Begin(), false, 1f);
            int slots = layers.SlotCount;
            Tick();
            Assert.That(released && !c.gameObject.activeSelf, "released inside its own write — off");
            Tick();
            Assert.That(c.gameObject.activeSelf, "the next Revert put the before back");
            Assert.That(layers.SlotCount, Is.EqualTo(slots - 1), "dropped");
        }
    }
}
