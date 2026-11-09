using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using xpTURN.Feedback.Tests;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the property layers' frame (EditMode — Tick, or the stages one by one). The test target's value touches the engine;
    // setups reserve what they begin — NoUnexpectedReceived counts the layers' growth warning too
    public sealed class LayerFrameTests
    {
        const float Dt = 1f / 60f;
        static readonly FrameTime s_Frame = new FrameTime(Dt, Dt);
        static readonly Regex s_WriteThrows = new Regex("LayerTestTarget write");

        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<Object> m_Objects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (FeedbackScheduler s in m_Schedulers)
                s.Shutdown();
            m_Schedulers.Clear();
            foreach (Object o in m_Objects)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Objects.Clear();
        }

        FeedbackScheduler Scheduler(ContextKind kind = ContextKind.Test)
        {
            var s = new FeedbackScheduler(kind);
            m_Schedulers.Add(s);
            return s;
        }

        LayerTestTarget Target(float value)
        {
            var go = new GameObject("LayerTestTarget");
            m_Objects.Add(go);
            var t = go.AddComponent<LayerTestTarget>();
            t.Value = value;
            return t;
        }

        static TestFloatSlot Slot(FeedbackScheduler s, LayerTestTarget t, ConsumptionStage stage = ConsumptionStage.LateApply,
            Composition composition = Composition.Additive, bool renormalizes = false, int aux = 0, int aux2 = 0)
            => s.Context.Layers.GetOrCreate(t, aux, aux2, () => new TestFloatSlot(t, stage, composition, renormalizes));

        static int Bits(float v) => BitConverter.SingleToInt32Bits(v);

        static float Flip(float v) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v) ^ 1);

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

        sealed class EndingRunner : IPlaybackRunner
        {
            public bool Advance(int state, in FrameTime time) => false;

            public void Abort(int state, AbortReason reason)
            {
            }

            public Object DebugOwner => null;
        }

        sealed class ClosingRunner : IPlaybackRunner
        {
            internal FeedbackScheduler Scheduler;
            internal int CloseAt;
            internal Action OnClosed;
            int m_Frame;

            public bool Advance(int state, in FrameTime time)
            {
                if (++m_Frame == CloseAt)
                {
                    Scheduler.Shutdown();
                    OnClosed?.Invoke();
                }
                return true;
            }

            public void Abort(int state, AbortReason reason)
            {
            }

            public Object DebugOwner => null;
        }

        sealed class TestService : FeedbackService
        {
            internal TestService(FeedbackContext context)
                : base(context)
            {
            }
        }

        // ---- the game's writes stay

        [Test]
        public void F01_UpdateWriteStays()
        {
            var s = Scheduler();
            var t = Target(2f);
            var slot = Slot(s, t);
            slot.Reserve(1);
            var h = slot.Begin(ContributionKind.Offset);
            slot.SetOffset(h, 0.5f);
            for (int f = 0; f < 4; f++)
            {
                s.RunRevert();
                if (f == 2)
                    t.Value = 3.25f;
                s.RunUpdate(s_Frame);
                s.RunLateApply();
            }
            slot.End(h);
            s.Tick(s_Frame);
            s.Tick(s_Frame);
            Assert.That(Bits(t.Value), Is.EqualTo(Bits(3.25f)));
        }

        [Test]
        public void F01_WindowWriteStays()
        {
            var s = Scheduler();
            var t = Target(2f);
            var slot = Slot(s, t);
            slot.Reserve(1);
            var h = slot.Begin(ContributionKind.Offset);
            slot.SetOffset(h, 0.5f);
            for (int f = 0; f < 4; f++)
            {
                s.Tick(s_Frame);
                if (f == 2)
                    t.Value = 4.5f;
            }
            slot.End(h);
            s.Tick(s_Frame);
            s.Tick(s_Frame);
            Assert.That(Bits(t.Value), Is.EqualTo(Bits(4.5f)));
        }

        // ---- the judgment is on the value read back

        [Test]
        public void F02_ClampedValueDoesNotStick()
        {
            var s = Scheduler();
            var t = Target(0.2f);
            t.Clamps = true;
            var slot = Slot(s, t);
            slot.Reserve(1);
            var h = slot.Begin(ContributionKind.Offset);
            slot.SetOffset(h, -0.5f);
            for (int f = 0; f < 5; f++)
            {
                s.Tick(s_Frame);
                Assert.That(Bits(t.Value), Is.EqualTo(Bits(0f)), $"clamped at frame {f}");
            }
            slot.End(h);
            s.Tick(s_Frame);
            s.Tick(s_Frame);
            Assert.That(Bits(t.Value), Is.EqualTo(Bits(0.2f)));
        }

        // ---- the composition

        // float addition shows the order: ((0 + 1) + 1e17) − 1e17 = 0, the reverse gives 1
        [Test]
        public void F03_OffsetsInStartOrder()
        {
            var s = Scheduler();
            var t = Target(0f);
            var slot = Slot(s, t);
            slot.Reserve(3);
            foreach (float o in new[] { 1f, 1e17f, -1e17f })
                slot.SetOffset(slot.Begin(ContributionKind.Offset), o);
            s.Tick(s_Frame);
            s.Tick(s_Frame);
            Assert.That(Bits(t.Value), Is.EqualTo(Bits(0f)));
        }

        [Test]
        public void F03_OverridesInStartOrder()
        {
            var s = Scheduler();
            var t = Target(0f);
            var slot = Slot(s, t);
            slot.Reserve(2);
            slot.SetOverride(slot.Begin(ContributionKind.Override), 10f, 0.5f);
            slot.SetOverride(slot.Begin(ContributionKind.Override), 20f, 0.5f);
            s.Tick(s_Frame);
            Assert.That(t.Value, Is.EqualTo(12.5f));
        }

        [Test]
        public void F03_WeightOneOverrideCoversOffsets()
        {
            var s = Scheduler();
            var t = Target(0f);
            var slot = Slot(s, t);
            slot.Reserve(2);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), 0.25f);
            slot.SetOverride(slot.Begin(ContributionKind.Override), 5f, 1f);
            s.Tick(s_Frame);
            Assert.That(Bits(t.Value), Is.EqualTo(Bits(5f)));
        }

        [Test]
        public void F03_WeightAboveOneIsOne()
        {
            var s = Scheduler();
            var t = Target(0f);
            var slot = Slot(s, t);
            slot.Reserve(1);
            slot.SetOverride(slot.Begin(ContributionKind.Override), 4f, 1.5f);
            s.Tick(s_Frame);
            Assert.That(Bits(t.Value), Is.EqualTo(Bits(4f)));
        }

        [Test]
        public void F03_ZeroContributionWritesNothing()
        {
            var s = Scheduler();
            var t = Target(2f);
            var slot = Slot(s, t);
            slot.Reserve(1);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), 0f);
            s.Tick(s_Frame);
            s.Tick(s_Frame);
            Assert.That(t.Writes, Is.EqualTo(0));
        }

        [Test]
        public void F03_ScaleMultiplies()
        {
            var s = Scheduler();
            var t = Target(2f);
            var slot = Slot(s, t, composition: Composition.Multiplicative);
            slot.Reserve(2);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), 0.5f);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), 0.5f);
            s.Tick(s_Frame);
            Assert.That(Bits(t.Value), Is.EqualTo(Bits(4.5f)));
        }

        // ---- a rewrite that changes the value — the base goes on. Both parities: the end alone passes the missing
        // continuation at an odd frame count

        [TestCase(9)]
        [TestCase(10)]
        public void F04_BaseContinuesEveryFrame(int frames)
        {
            var s = Scheduler();
            var t = Target(0.25f);
            t.Flips = true;
            float first = t.Value;
            var slot = Slot(s, t, renormalizes: true);
            slot.Reserve(1);
            var h = slot.Begin(ContributionKind.Offset);
            for (int f = 0; f < frames; f++)
            {
                slot.SetOffset(h, 0.01f * (f % 4 + 1));
                s.Tick(s_Frame);
                Assert.That(Bits(slot.Base), Is.EqualTo(Bits(first)), $"Base at frame {f}");
            }
            slot.End(h);
            s.Tick(s_Frame);
            s.Tick(s_Frame);
            Assert.That(Bits(t.Value), Is.EqualTo(Bits(Flip(first))), "the end = N(first)");
            Assert.That(Bits(slot.Base), Is.EqualTo(Bits(first)), "Base after the end");
        }

        // ---- no allocation, no growth from the reservation on

        [Test]
        public void F05_NoAllocation()
        {
            var s = Scheduler();
            var slots = new TestFloatSlot[3];
            var handles = new ContributionHandle[3];
            for (int i = 0; i < 3; i++)
            {
                slots[i] = Slot(s, Target(i));
                slots[i].Reserve(1);
                handles[i] = slots[i].Begin(ContributionKind.Offset);
            }
            float x = 0f;
            TestDelegate run = () =>
            {
                x += 0.01f;
                for (int i = 0; i < 3; i++)
                    slots[i].SetOffset(handles[i], x);
                s.Tick(s_Frame);
            };
            run();
            run();
            Assert.That(Allocates(run), Is.False);
        }

        [Test]
        public void F05_BoxingIsSeenAllocating()
        {
            float x = 1f;
            object box = null;
            TestDelegate run = () => box = x;
            run();
            Assert.That(Allocates(run), Is.True);
        }

        [Test]
        public void F05_NoGrowthWhenStopAndPlayShareAFrame()
        {
            var s = Scheduler();
            var slots = new TestFloatSlot[3];
            var handles = new ContributionHandle[3];
            for (int i = 0; i < 3; i++)
            {
                slots[i] = Slot(s, Target(i));
                slots[i].Reserve(1);
                handles[i] = slots[i].Begin(ContributionKind.Offset);
            }
            for (int f = 0; f < 20; f++)
            {
                for (int i = 0; i < 3; i++)
                {
                    slots[i].End(handles[i]);
                    handles[i] = slots[i].Begin(ContributionKind.Offset);
                    slots[i].SetOffset(handles[i], 0.1f);
                }
                s.Tick(s_Frame);
            }
            Assert.That(slots[0].GrowthCount + slots[1].GrowthCount + slots[2].GrowthCount, Is.EqualTo(0));
        }

        // a reservation after contributions came and went puts new handle numbers on top
        [Test]
        public void F05_NoGrowthAfterALaterReserve()
        {
            var s = Scheduler();
            var one = Slot(s, Target(0f));
            one.Reserve(1);
            var h = one.Begin(ContributionKind.Offset);
            one.SetOffset(h, 1f);
            s.Tick(s_Frame);
            one.End(h);
            s.Tick(s_Frame);
            one.Reserve(2);
            for (int i = 0; i < 3; i++)
                one.SetOffset(one.Begin(ContributionKind.Offset), 1f);
            s.Tick(s_Frame);
            // three bindings reserve one each — the sum is 3, the handle table 4
            var three = Slot(s, Target(0f));
            three.Reserve(1);
            three.Reserve(1);
            three.Reserve(1);
            for (int i = 0; i < 3; i++)
                three.SetOffset(three.Begin(ContributionKind.Offset), 1f);
            s.Tick(s_Frame);
            Assert.That(one.GrowthCount + three.GrowthCount, Is.EqualTo(0));
        }

        [Test]
        public void F05_IdleSlotsCostNothing()
        {
            var s = Scheduler();
            var idle = Slot(s, Target(1f));
            idle.Reserve(1);
            var busy = Slot(s, Target(2f));
            busy.Reserve(1);
            busy.SetOffset(busy.Begin(ContributionKind.Offset), 1f);
            for (int f = 0; f < 5; f++)
                s.Tick(s_Frame);
            Assert.That(idle.AliveChecks, Is.EqualTo(0));
        }

        // ---- isolation — a layer stage never throws

        [Test]
        public void F06_ThrowingSlotStopsNoOther()
        {
            var s = Scheduler();
            var bad = Target(1f);
            var good = Target(2f);
            var sb = Slot(s, bad, ConsumptionStage.Update);
            var sg = Slot(s, good, ConsumptionStage.Update);
            sb.Reserve(1);
            sg.Reserve(1);
            sb.SetOffset(sb.Begin(ContributionKind.Offset), 1f);
            sg.SetOffset(sg.Begin(ContributionKind.Offset), 1f);
            bad.Throws = true;
            s.Reserve(1);
            s.Add(new EndingRunner(), 0);
            int before = s.SlotCount;
            LogAssert.Expect(LogType.Exception, s_WriteThrows);
            s.Tick(s_Frame);
            Assert.That(s.SlotCount, Is.LessThan(before), "the scheduler's Compact ran");
            Assert.That(Bits(good.Value), Is.EqualTo(Bits(3f)), "the other slot applied");
            s.Tick(s_Frame);
            s.Tick(s_Frame);
            Assert.That(Bits(good.Value), Is.EqualTo(Bits(3f)), "and goes on");
            bad.Throws = false;
        }

        [Test]
        public void F06_ReleasedSlotThrowingStaysInside()
        {
            var s = Scheduler();
            var t = Target(2f);
            var slot = Slot(s, t);
            slot.Reserve(1);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), 5f);
            s.Tick(s_Frame);
            t.Throws = true;
            s.Context.Layers.Release(slot, 1);
            LogAssert.Expect(LogType.Exception, s_WriteThrows);
            Assert.DoesNotThrow(() => s.Tick(s_Frame));
            t.Throws = false;
        }

        [Test]
        public void F07_BaseIsBeforeComposition()
        {
            var s = Scheduler();
            var t = Target(2f);
            var slot = Slot(s, t);
            slot.Reserve(1);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), 5f);
            s.Tick(s_Frame);
            Assert.That(Bits(t.Value), Is.EqualTo(Bits(7f)));
            Assert.That(Bits(slot.Base), Is.EqualTo(Bits(2f)));
        }

        [Test]
        public void F08_ShutdownRevertsAndRefusesBegin()
        {
            var s = Scheduler();
            var t = Target(2f);
            var slot = Slot(s, t);
            slot.Reserve(1);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), 5f);
            s.Tick(s_Frame);
            s.Shutdown();
            Assert.That(Bits(t.Value), Is.EqualTo(Bits(2f)));
            Assert.That(slot.Begin(ContributionKind.Offset).IsValid, Is.False);
        }

        // ---- the last release

        [Test]
        public void F09_LastReleaseRevertsAndDrops()
        {
            var s = Scheduler();
            var reg = s.Context.Layers;
            var t = Target(2f);
            var slot = Slot(s, t);
            slot.Reserve(1);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), 5f);
            s.Tick(s_Frame);
            reg.Release(slot, 1);
            s.Tick(s_Frame);
            Assert.That(Bits(t.Value), Is.EqualTo(Bits(2f)), "put back");
            Assert.That(reg.SlotCount, Is.EqualTo(0), "dropped");
        }

        [Test]
        public void F09_WaitingSlotComesBackWithoutOrphans()
        {
            var s = Scheduler();
            var reg = s.Context.Layers;
            var t = Target(2f);
            var slot = Slot(s, t);
            slot.Reserve(1);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), 5f);
            s.Tick(s_Frame);
            reg.Release(slot, 1);
            var again = Slot(s, t);
            again.Reserve(1);
            Assert.That(again, Is.SameAs(slot), "the same slot");
            Assert.That(again.LiveContributions, Is.EqualTo(0), "the gone binding's contribution ended");
            s.Tick(s_Frame);
            s.Tick(s_Frame);
            Assert.That(Bits(t.Value), Is.EqualTo(Bits(2f)));
        }

        // ---- a destroyed target

        [Test]
        public void F10_DestroyedTargetDetachesQuietly()
        {
            var s = Scheduler();
            var reg = s.Context.Layers;
            var t = Target(2f);
            var slot = Slot(s, t);
            slot.Reserve(1);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), 5f);
            s.Tick(s_Frame);
            Object.DestroyImmediate(t.gameObject);
            s.Tick(s_Frame);
            s.Tick(s_Frame);
            LogAssert.NoUnexpectedReceived();
            Assert.That(reg.SlotCount, Is.EqualTo(0));
        }

        // ---- the query

        [Test]
        public void F11_QueryGivesTheRows()
        {
            var s = Scheduler();
            var reg = s.Context.Layers;
            var t = Target(2f);
            var slot = Slot(s, t, aux: 3, aux2: 4);
            slot.Reserve(1);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), 5f);
            s.Tick(s_Frame);
            Assert.That(reg.TryGetSlot(0, out LayerSlotInfo info), Is.True);
            Assert.That(info.Target, Is.SameAs(t));
            Assert.That(info.SlotType, Is.EqualTo(typeof(TestFloatSlot)));
            Assert.That(info.Aux, Is.EqualTo(3));
            Assert.That(info.Aux2, Is.EqualTo(4));
            Assert.That(info.Stage, Is.EqualTo(ConsumptionStage.LateApply));
            Assert.That(info.LiveContributions, Is.EqualTo(1));
            Assert.That(info.Pending, Is.True);
            Assert.That(info.IsAlive, Is.True);
            Assert.That(info.Base.x, Is.EqualTo(2f));
            Assert.That(reg.TryGetSlot(1, out _), Is.False);
            Assert.That(reg.TryGetSlot(-1, out _), Is.False);
        }

        // after the target is destroyed and before the next Revert takes the slot out
        [Test]
        public void F11_QueryOfADestroyedTarget()
        {
            var s = Scheduler();
            var reg = s.Context.Layers;
            var t = Target(2f);
            var slot = Slot(s, t);
            slot.Reserve(1);
            Object.DestroyImmediate(t.gameObject);
            LayerSlotInfo info = default;
            Assert.DoesNotThrow(() => reg.TryGetSlot(0, out info));
            Assert.That(info.IsAlive, Is.False);
        }

        // ---- each stage writes its slots

        [Test]
        public void F12_EachStageWritesItsSlots()
        {
            var s = Scheduler();
            var u = Target(0f);
            var l = Target(0f);
            var su = Slot(s, u, ConsumptionStage.Update);
            var sl = Slot(s, l);
            su.Reserve(1);
            sl.Reserve(1);
            su.SetOffset(su.Begin(ContributionKind.Offset), 1f);
            sl.SetOffset(sl.Begin(ContributionKind.Offset), 1f);
            s.RunRevert();
            s.RunUpdate(s_Frame);
            Assert.That(u.Writes, Is.EqualTo(1), "the Update slot in RunUpdate");
            Assert.That(l.Writes, Is.EqualTo(0), "the LateApply slot waits");
            s.RunLateApply();
            Assert.That(u.Writes, Is.EqualTo(1));
            Assert.That(l.Writes, Is.EqualTo(1), "the LateApply slot in RunLateApply");
        }

        // ---- ended, foreign and default handles change nothing

        [Test]
        public void F13_StaleHandlesChangeNothing()
        {
            var s = Scheduler();
            var a = Target(0f);
            var b = Target(0f);
            var sa = Slot(s, a);
            var sb = Slot(s, b);
            sa.Reserve(2);
            sb.Reserve(1);
            var live = sa.Begin(ContributionKind.Offset);
            sa.SetOffset(live, 1f);
            var ended = sa.Begin(ContributionKind.Offset);
            sa.End(ended);
            var reused = sa.Begin(ContributionKind.Offset);
            sa.SetOffset(reused, 2f);
            var foreign = sb.Begin(ContributionKind.Offset);
            Assert.That(reused.Slot.Index, Is.EqualTo(ended.Slot.Index), "the new contribution took the ended one's number");
            Assert.That(sa.SetOffset(ended, 100f), Is.False, "an ended handle");
            Assert.That(sa.SetOffset(foreign, 100f), Is.False, "another slot's handle");
            Assert.That(sa.SetOffset(default, 100f), Is.False, "the default handle");
            s.Tick(s_Frame);
            Assert.That(Bits(a.Value), Is.EqualTo(Bits(3f)), "the live cells unchanged");
        }

        // the runner closes the scheduler in its Advance, then RunUpdate calls the layers' Apply(Update). The end value alone
        // would pass a closed registry that still applies — its in-stage close check puts the value back
        [Test]
        public void F14_ClosedInsideAStageWritesNothing()
        {
            var s = Scheduler();
            var t = Target(2f);
            var slot = Slot(s, t, ConsumptionStage.Update);
            slot.Reserve(1);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), 5f);
            int writesAtClose = -1;
            s.Reserve(1);
            s.Add(new ClosingRunner { Scheduler = s, CloseAt = 3, OnClosed = () => writesAtClose = t.Writes }, 0);
            for (int f = 0; f < 4; f++)
                s.Tick(s_Frame);
            Assert.That(Bits(t.Value), Is.EqualTo(Bits(2f)), "the base");
            Assert.That(t.Writes, Is.EqualTo(writesAtClose), "no write after the close");
        }

        [Test]
        public void F15_ReleaseInsideAWriteSkipsNoSlot()
        {
            var s = Scheduler();
            var reg = s.Context.Layers;
            var tx = Target(1f);
            var ta = Target(2f);
            var tc = Target(3f);
            var x = Slot(s, tx);
            var a = Slot(s, ta);
            var c = Slot(s, tc);
            x.Reserve(1);
            a.Reserve(1);
            c.Reserve(1);
            // active and never written, before the writing slot
            x.SetOffset(x.Begin(ContributionKind.Offset), 0f);
            a.SetOffset(a.Begin(ContributionKind.Offset), 1f);
            c.SetOffset(c.Begin(ContributionKind.Offset), 1f);
            bool released = false;
            ta.OnWrite = () =>
            {
                if (released)
                    return;
                released = true;
                reg.Release(x, 1);
            };
            s.Tick(s_Frame);
            ta.OnWrite = null;
            Assert.That(tc.Writes, Is.EqualTo(1));
        }

        [Test]
        public void F16_ShutdownIsolatesAThrowingSlot()
        {
            var s = Scheduler();
            var t = Target(2f);
            var b = Target(2f);
            var st = Slot(s, t);
            var sb = Slot(s, b);
            st.Reserve(1);
            sb.Reserve(1);
            st.SetOffset(st.Begin(ContributionKind.Offset), 5f);
            sb.SetOffset(sb.Begin(ContributionKind.Offset), 5f);
            var service = new TestService(s.Context);
            s.Context.AddService(service);
            s.Tick(s_Frame);
            t.Throws = true;
            LogAssert.Expect(LogType.Exception, s_WriteThrows);
            s.Shutdown();
            t.Throws = false;
            Assert.That(Bits(b.Value), Is.EqualTo(Bits(2f)), "the other slot put back");
            Assert.That(service.IsClosed, Is.True, "the services closed");
        }

        // its restore throws: a new prepare gets a new slot, and the old binding's release does not touch it
        [Test]
        public void F17_FailedSlotDetachesForGood()
        {
            var s = Scheduler();
            var reg = s.Context.Layers;
            var t = Target(2f);
            var a = Slot(s, t);
            a.Reserve(2);
            a.SetOffset(a.Begin(ContributionKind.Offset), 5f);
            s.Tick(s_Frame);
            t.Throws = true;
            LogAssert.Expect(LogType.Exception, s_WriteThrows);
            s.Tick(s_Frame);
            t.Throws = false;
            t.Value = 2f;
            Assert.That(a.Begin(ContributionKind.Offset).IsValid, Is.False, "a detached slot takes no contribution");
            var b = Slot(s, t);
            b.Reserve(2);
            Assert.That(b, Is.Not.SameAs(a), "a new prepare gets a new slot");
            reg.Release(a, 2);
            var c = Slot(s, t);
            c.Reserve(2);
            Assert.That(c, Is.SameAs(b), "one slot per property");
            var hb = b.Begin(ContributionKind.Offset);
            b.SetOffset(hb, 1f);
            var hc = c.Begin(ContributionKind.Offset);
            c.SetOffset(hc, 10f);
            for (int f = 0; f < 3; f++)
            {
                s.Tick(s_Frame);
                Assert.That(Bits(t.Value), Is.EqualTo(Bits(13f)), $"during, frame {f}");
            }
            b.End(hb);
            c.End(hc);
            s.Tick(s_Frame);
            Assert.That(Bits(t.Value), Is.EqualTo(Bits(2f)), "after");
        }

        // it throws in Apply and is prepared again before the next Revert
        [Test]
        public void F17_FailedSlotIsNotRevived()
        {
            var s = Scheduler();
            var t = Target(2f);
            var a = Slot(s, t);
            a.Reserve(1);
            a.SetOffset(a.Begin(ContributionKind.Offset), 5f);
            t.Throws = true;
            LogAssert.Expect(LogType.Exception, s_WriteThrows);
            s.Tick(s_Frame);
            var again = Slot(s, t);
            Assert.That(again, Is.Not.SameAs(a), "not revived");
            for (int f = 0; f < 5; f++)
                s.Tick(s_Frame);
            t.Throws = false;
        }

        [Test]
        public void F18_KeyIsTargetTypeAndBothAux()
        {
            var s = Scheduler();
            var reg = s.Context.Layers;
            var t = Target(0f);
            var k = Slot(s, t, aux: 1, aux2: 0);
            Assert.That(Slot(s, t, aux: 1, aux2: 0), Is.SameAs(k), "the same key");
            Assert.That(Slot(s, t, aux: 1, aux2: 2), Is.Not.SameAs(k), "the second aux differs");
            Assert.That(Slot(s, t, aux: 2, aux2: 0), Is.Not.SameAs(k), "the first aux differs");
            Assert.That(reg.TryFind(t, 1, 0, out TestFloatSlot found), Is.True);
            Assert.That(found, Is.SameAs(k), "TryFind by the same key");
        }

        // never written, so never pending — Revert still compacts its ledger
        [Test]
        public void F19_WriteSkippedContributionsCompact()
        {
            var s = Scheduler();
            var slot = Slot(s, Target(2f));
            slot.Reserve(1);
            for (int k = 0; k < 12; k++)
            {
                var h = slot.Begin(ContributionKind.Offset);
                slot.SetOffset(h, 0f);
                s.Tick(s_Frame);
                slot.End(h);
                s.Tick(s_Frame);
            }
            Assert.That(slot.GrowthCount, Is.EqualTo(0));
        }

        // two bindings at once make the sum 4 and the ledger grows once on preparation — the capacity after the first
        // player is the one to keep
        [Test]
        public void F20_ReleaseGivesTheReservationBack()
        {
            var s = Scheduler();
            var reg = s.Context.Layers;
            var t = Target(0f);
            var keeper = Slot(s, t);
            keeper.Reserve(2);
            int capacity = 0;
            for (int p = 0; p < 50; p++)
            {
                var player = Slot(s, t);
                player.Reserve(2);
                var h = player.Begin(ContributionKind.Offset);
                player.SetOffset(h, 1f);
                s.Tick(s_Frame);
                player.End(h);
                reg.Release(player, 2);
                s.Tick(s_Frame);
                if (p == 0)
                    capacity = keeper.Capacity;
            }
            Assert.That(keeper.Reserved, Is.EqualTo(2), "the reserved sum");
            Assert.That(keeper.Capacity, Is.EqualTo(capacity), "the ledger's capacity");
        }

        [Test]
        public void F21_ShutdownInsideAWriteRevertsBoth()
        {
            var s = Scheduler();
            var ta = Target(2f);
            var tc = Target(2f);
            var a = Slot(s, ta);
            var c = Slot(s, tc);
            a.Reserve(1);
            c.Reserve(1);
            a.SetOffset(a.Begin(ContributionKind.Offset), 5f);
            c.SetOffset(c.Begin(ContributionKind.Offset), 5f);
            ta.OnWrite = () => s.Shutdown();
            s.Tick(s_Frame);
            ta.OnWrite = null;
            Assert.That(Bits(ta.Value), Is.EqualTo(Bits(2f)), "the writing slot");
            Assert.That(Bits(tc.Value), Is.EqualTo(Bits(2f)), "the slot after it");
        }

        [Test]
        public void F22_ShortageWarnsOnceAndPreviewsStayQuiet()
        {
            int warnings = 0;
            Application.LogCallback count = (message, stack, type) =>
            {
                if (type == LogType.Warning && message == PropertyLayerRegistry.ShortageWarning)
                    warnings++;
            };
            Application.logMessageReceived += count;
            try
            {
                var s = Scheduler();
                var slot = Slot(s, Target(0f));
                slot.Reserve(1);
                slot.Begin(ContributionKind.Offset);
                slot.Begin(ContributionKind.Offset);
                slot.Begin(ContributionKind.Offset);
                int outside = warnings;
                var p = Scheduler(ContextKind.Preview);
                var ps = Slot(p, Target(0f));
                ps.Reserve(1);
                ps.Begin(ContributionKind.Offset);
                ps.Begin(ContributionKind.Offset);
                Assert.That(outside, Is.EqualTo(1), "once outside a preview");
                Assert.That(warnings, Is.EqualTo(1), "quiet in a preview");
            }
            finally
            {
                Application.logMessageReceived -= count;
            }
        }

        [Test]
        public void F23_DetachedBaseReadsNothing()
        {
            var s = Scheduler();
            var t = Target(2f);
            var slot = Slot(s, t);
            slot.Reserve(1);
            slot.SetOffset(slot.Begin(ContributionKind.Offset), 5f);
            t.Throws = true;
            LogAssert.Expect(LogType.Exception, s_WriteThrows);
            s.Tick(s_Frame);
            t.ThrowsOnRead = true;
            float b = 0f;
            Assert.DoesNotThrow(() => b = slot.Base);
            Assert.That(Bits(b), Is.EqualTo(Bits(2f)), "the last base");
            t.Throws = false;
            t.ThrowsOnRead = false;
        }
    }
}
