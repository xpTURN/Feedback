using System;
using NUnit.Framework;
using static xpTURN.Feedback.Tests.Timeline.TimelineDescs;

namespace xpTURN.Feedback.Tests.Timeline
{
    // the resolve, with two regression cases. Expected values are hand-computed.
    public sealed class TimelineResolveTests
    {
        const long Inf = TimelineTime.Infinite;

        static ResolvedTimeline Resolve(TimelineProgram p, float[] itemScale = null, bool[] skip = null, float globalScale = 1f, float rootDelay = 0f)
        {
            var r = new ResolvedTimeline(p);
            p.Resolve(itemScale ?? Array.Empty<float>(), skip ?? Array.Empty<bool>(), globalScale, rootDelay, r);
            return r;
        }

        static void AssertSpan(TimelineProgram p, ResolvedTimeline r, int item, long start, long end)
        {
            int node = p.NodeOfItem(item);
            Assert.That(r.NodeStart(node), Is.EqualTo(start), $"item {item} start");
            Assert.That(r.NodeEnd(node), Is.EqualTo(end), $"item {item} end");
        }

        [Test]
        public void RuleCombinations()
        {
            var p = Compile(Fx(duration: 1f), Fx(delay: 0.25f, duration: 0.5f), Fx(StartRule.AfterPrevious, duration: 0.25f));
            var r = Resolve(p);
            AssertSpan(p, r, 0, 0, S);
            AssertSpan(p, r, 1, S / 4, 3 * S / 4);
            AssertSpan(p, r, 2, 3 * S / 4, S);
            Assert.That(r.Total, Is.EqualTo(S));

            var all = Compile(Fx(duration: 1f), Fx(duration: 0.5f), Fx(StartRule.AfterAll, delay: 0.5f, duration: 0.25f));
            var ra = Resolve(all);
            AssertSpan(all, ra, 2, 3 * S / 2, 7 * S / 4);
            Assert.That(ra.Total, Is.EqualTo(7 * S / 4));
        }

        [Test]
        public void SkippedItemKeepsItsPlace()
        {
            var p = Compile(Fx(duration: 1f), Fx(StartRule.AfterPrevious, duration: 0.5f), Fx(StartRule.AfterPrevious, duration: 0.25f));
            var r = Resolve(p, skip: new[] { false, true, false });
            Assert.That(r.ItemSkipped(1), Is.True);
            AssertSpan(p, r, 2, 3 * S / 2, 7 * S / 4);
            Assert.That(r.Total, Is.EqualTo(Resolve(p).Total));
        }

        [Test]
        public void NominalGlobalScale_IsThePlayerDurationScale()
        {
            var p = Compile(Fx(delay: 0.25f, duration: 1f));
            Assert.That(Resolve(p).Total, Is.EqualTo(5 * S / 4));
            var r = Resolve(p, globalScale: 2f);
            AssertSpan(p, r, 0, S / 2, 5 * S / 2);
            Assert.That(r.Total, Is.EqualTo(5 * S / 2));
        }

        [Test]
        public void RootDelay_IsUnscaledAndCounted()
        {
            var p = Compile(Fx(duration: 1f));
            var r = Resolve(p, globalScale: 2f, rootDelay: 0.5f);
            Assert.That(r.RootDelay, Is.EqualTo(S / 2));
            Assert.That(r.NodeLocalStart(p.NodeOfItem(0)), Is.Zero);
            AssertSpan(p, r, 0, S / 2, 5 * S / 2);
            Assert.That(r.Total, Is.EqualTo(5 * S / 2));

            Assert.That(Resolve(Compile(), rootDelay: 0.5f).Total, Is.EqualTo(S / 2), "empty list = root delay");
        }

        [Test]
        public void ScalePath_ItemRepeatTakesItemTimesGlobal()
        {
            var p = Compile(Repeat(Fx(delay: 0.5f, duration: 1f), 1, gap: 0.25f));
            var r = Resolve(p, itemScale: new[] { 2f }, globalScale: 1.5f);
            Assert.That(r.ItemDuration(0), Is.EqualTo(3 * S));
            Assert.That(r.NodeDelay(p.BlockNode(0)), Is.EqualTo(3 * S / 2));
            Assert.That(r.BlockGap(0), Is.EqualTo(3 * S / 4));
            Assert.That(r.BlockLength(0), Is.EqualTo(3 * S));
            Assert.That(r.Total, Is.EqualTo(3 * S / 2 + 2 * 3 * S + 3 * S / 4));
        }

        [Test]
        public void ScalePath_LoopGapTakesGlobalOnly()
        {
            var p = Compile(LoopStart(), Fx(duration: 1f), LoopEnd(2, gap: 0.5f));
            var r = Resolve(p, itemScale: new[] { 1f, 1f, 5f }, globalScale: 2f);
            Assert.That(r.BlockLength(0), Is.EqualTo(2 * S));
            Assert.That(r.BlockGap(0), Is.EqualTo(S));
            Assert.That(r.Total, Is.EqualTo(2 * 2 * S + S));
        }

        [Test]
        public void ScalePath_HoldDelayTakesGlobalOnly()
        {
            var p = Compile(Fx(duration: 1f), Hold(0.25f), Fx(duration: 0.5f));
            var r = Resolve(p, itemScale: new[] { 1f, 1f, 4f }, globalScale: 2f);
            AssertSpan(p, r, 2, 2 * S + S / 2, 2 * S + S / 2 + 4 * S);
        }

        [Test]
        public void SignalWaitWithTimeout_NominalLengthIsTheTimeout()
        {
            var p = Compile(Fx(duration: 0.5f), Signal(2f), Fx(duration: 0.25f));
            var r = Resolve(p);
            AssertSpan(p, r, 1, 0, 2 * S);
            AssertSpan(p, r, 2, 2 * S, 9 * S / 4);
            Assert.That(r.Total, Is.EqualTo(9 * S / 4));
            Assert.That(r.IsSeekable, Is.True);
        }

        [Test]
        public void Infinite_IsTheMarker()
        {
            var inf = Resolve(Compile(Fx(duration: float.PositiveInfinity)));
            Assert.That(inf.Total, Is.EqualTo(Inf));
            Assert.That(inf.TotalSeconds, Is.EqualTo(double.PositiveInfinity));
            Assert.That(inf.IsSeekable, Is.False);

            Assert.That(Resolve(Compile(LoopStart(), Fx(), LoopEnd(0))).Total, Is.EqualTo(Inf));
            Assert.That(Resolve(Compile(Fx(), Signal())).Total, Is.EqualTo(Inf));
            Assert.That(Resolve(Compile(Repeat(Fx(), -1))).Total, Is.EqualTo(Inf));
        }

        [Test]
        public void Infinite_SaturatesInsteadOfWrapping()
        {
            var p = Compile(Fx(duration: float.PositiveInfinity), Fx(StartRule.AfterPrevious, delay: 0.5f, duration: 1f));
            var r = Resolve(p);
            AssertSpan(p, r, 1, Inf, Inf);

            var zeroScale = Compile(Fx(duration: float.PositiveInfinity), Fx(duration: 1f));
            var rz = Resolve(zeroScale, globalScale: 0f);
            Assert.That(rz.ItemDuration(0), Is.EqualTo(Inf), "+inf x 0 stays infinite");
            Assert.That(rz.ItemDuration(1), Is.Zero);
        }

        [Test]
        public void Microseconds_AreRounded()
        {
            var r = Resolve(Compile(Fx(duration: 0.1f), Fx(duration: 1e-7f), Fx(duration: 6e-7f)));
            Assert.That(r.ItemDuration(0), Is.EqualTo(100_000L));
            Assert.That(r.ItemDuration(1), Is.Zero);
            Assert.That(r.ItemDuration(2), Is.EqualTo(1L));
        }

        [Test]
        public void Background_IsExcludedAndClampedToTheForeground()
        {
            var p = Compile(Fx(duration: 1f), Background(Fx(duration: 5f)));
            var r = Resolve(p);
            Assert.That(r.Total, Is.EqualTo(S));
            AssertSpan(p, r, 1, 0, S);
            Assert.That(r.NodeCancelled(p.NodeOfItem(1)), Is.False);

            var late = Compile(Fx(duration: 1f), Background(Fx(delay: 2f, duration: 1f)));
            Assert.That(Resolve(late).NodeCancelled(late.NodeOfItem(1)), Is.True, "not started when the foreground ends");

            var infinite = Compile(Fx(duration: 1f), Background(Fx(duration: float.PositiveInfinity)));
            var ri = Resolve(infinite);
            Assert.That(ri.Total, Is.EqualTo(S));
            Assert.That(ri.IsSeekable, Is.True);
            AssertSpan(infinite, ri, 1, 0, S);
        }

        [Test]
        public void Background_AfterPreviousUsesTheForegroundBefore()
        {
            var p = Compile(Fx(duration: 1f), Background(Fx(duration: 5f)), Fx(StartRule.AfterPrevious, duration: 0.5f));
            var r = Resolve(p);
            AssertSpan(p, r, 2, S, 3 * S / 2);
            AssertSpan(p, r, 1, 0, 3 * S / 2);
        }

        [Test]
        public void References_ToBlocksAndRepeatedItems()
        {
            var p = Compile(LoopStart(), Fx(duration: 0.5f), LoopEnd(3, gap: 0.25f), Fx(StartRule.AfterPrevious, duration: 0.5f));
            var r = Resolve(p);
            Assert.That(r.BlockLength(0), Is.EqualTo(S / 2));
            Assert.That(r.BlockPeriod(0), Is.EqualTo(3 * S / 4));
            Assert.That(r.NodeEnd(p.BlockNode(0)), Is.EqualTo(2 * S));
            AssertSpan(p, r, 3, 2 * S, 5 * S / 2);

            var rep = Compile(Repeat(Fx(duration: 0.5f), 2, gap: 0.125f), Fx(StartRule.AfterPrevious, duration: 0.25f));
            var rr = Resolve(rep);
            AssertSpan(rep, rr, 1, 7 * S / 4, 2 * S);
        }

        [Test]
        public void ZeroPeriodFiniteBlock_EndsAtItsStart()
        {
            var p = Compile(LoopStart(), Instant(), LoopEnd(5), Fx(StartRule.AfterPrevious, duration: 0.5f));
            var r = Resolve(p);
            Assert.That(r.BlockPeriod(0), Is.Zero);
            Assert.That(r.NodeEnd(p.BlockNode(0)), Is.Zero);
            AssertSpan(p, r, 3, 0, S / 2);
        }

        [Test]
        public void Blocks_FirstOccurrenceAndNesting()
        {
            var p = Compile(Fx(duration: 1f), LoopStart(StartRule.AfterPrevious), Fx(delay: 0.25f, duration: 0.5f), LoopEnd(2));
            var r = Resolve(p);
            int b = p.NodeOfItem(2);
            Assert.That(r.NodeLocalStart(b), Is.EqualTo(S / 4));
            AssertSpan(p, r, 2, 5 * S / 4, 7 * S / 4);
            Assert.That(r.BlockLength(0), Is.EqualTo(3 * S / 4));
            Assert.That(r.Total, Is.EqualTo(S + 2 * 3 * S / 4));

            var nested = Compile(LoopStart(), LoopStart(), Fx(duration: 0.25f), LoopEnd(2), LoopEnd(3, gap: 0.5f));
            var rn = Resolve(nested);
            Assert.That(rn.BlockLength(1), Is.EqualTo(S / 4));
            Assert.That(rn.BlockLength(0), Is.EqualTo(S / 2));
            Assert.That(rn.Total, Is.EqualTo(3 * S / 2 + 2 * S / 2));
        }

        [Test]
        public void ResolveRefillsTheSameBuffer()
        {
            var p = Compile(Fx(duration: 1f), Background(Fx(delay: 2f, duration: 1f)), Repeat(Fx(duration: 0.5f), 1, 0.25f));
            var reused = new ResolvedTimeline(p);
            p.Resolve(new[] { 1f, 1f, 3f }, new[] { true, false, false }, 0.5f, 1f, reused);
            p.Resolve(Array.Empty<float>(), Array.Empty<bool>(), 1f, 0f, reused);
            var fresh = Resolve(p);
            Assert.That(reused.Total, Is.EqualTo(fresh.Total));
            Assert.That(reused.RootDelay, Is.EqualTo(fresh.RootDelay));
            for (int i = 0; i < p.ItemCount; i++)
            {
                Assert.That(reused.ItemDuration(i), Is.EqualTo(fresh.ItemDuration(i)));
                Assert.That(reused.ItemSkipped(i), Is.EqualTo(fresh.ItemSkipped(i)));
            }
            for (int n = 0; n < p.NodeCount; n++)
            {
                Assert.That(reused.NodeStart(n), Is.EqualTo(fresh.NodeStart(n)));
                Assert.That(reused.NodeEnd(n), Is.EqualTo(fresh.NodeEnd(n)));
                Assert.That(reused.NodeLocalEnd(n), Is.EqualTo(fresh.NodeLocalEnd(n)));
                Assert.That(reused.NodeCancelled(n), Is.EqualTo(fresh.NodeCancelled(n)));
            }
            for (int b = 0; b < p.BlockCount; b++)
                Assert.That(reused.BlockGap(b), Is.EqualTo(fresh.BlockGap(b)));
        }

        // a wait and a root delay: the total is not the initial delay only.
        [Test]
        public void Reg02_TotalWithWaitAndRootDelay()
        {
            var p = Compile(Fx(duration: 0.5f), Wait(1f), Fx(duration: 0.25f));
            var r = Resolve(p, rootDelay: 0.5f);
            AssertSpan(p, r, 1, S / 2, 3 * S / 2);
            AssertSpan(p, r, 2, 3 * S / 2, 7 * S / 4);
            Assert.That(r.Total, Is.EqualTo(7 * S / 4));
        }

        // the items after a non-contributing wait stay in the total.
        [Test]
        public void Reg03_ItemsAfterBackgroundAndZeroLengthFlowAreCounted()
        {
            var p = Compile(
                Fx(duration: 0.5f),
                Background(Fx(duration: 0.1f)),
                Wait(0f, StartRule.AfterPrevious),
                Fx(StartRule.AfterPrevious, duration: 0.5f));
            var r = Resolve(p);
            AssertSpan(p, r, 2, S / 2, S / 2);
            AssertSpan(p, r, 3, S / 2, S);
            Assert.That(r.Total, Is.EqualTo(S));
        }
    }

    public sealed class TimelineTimeTests
    {
        [Test]
        public void FromSeconds_MarksInfinityNaNAndOverflow()
        {
            Assert.That(TimelineTime.FromSeconds(double.PositiveInfinity), Is.EqualTo(TimelineTime.Infinite));
            Assert.That(TimelineTime.FromSeconds(double.NaN), Is.EqualTo(TimelineTime.Infinite));
            Assert.That(TimelineTime.FromSeconds(1e13), Is.EqualTo(TimelineTime.Infinite));
            Assert.That(TimelineTime.FromSeconds(1.25), Is.EqualTo(1_250_000L));
            Assert.That(TimelineTime.ToSeconds(TimelineTime.Infinite), Is.EqualTo(double.PositiveInfinity));
        }

        [Test]
        public void Arithmetic_Saturates()
        {
            const long inf = TimelineTime.Infinite;
            Assert.That(TimelineTime.Add(inf, 5), Is.EqualTo(inf));
            Assert.That(TimelineTime.Add(5, inf), Is.EqualTo(inf));
            Assert.That(TimelineTime.Add(inf - 1, 2), Is.EqualTo(inf));
            Assert.That(TimelineTime.Add(3, 4), Is.EqualTo(7));
            Assert.That(TimelineTime.Multiply(inf, 3), Is.EqualTo(inf));
            Assert.That(TimelineTime.Multiply(4_000_000_000_000_000_000L, 3), Is.EqualTo(inf));
            Assert.That(TimelineTime.Multiply(inf, 0), Is.Zero);
            Assert.That(TimelineTime.Multiply(6, 7), Is.EqualTo(42));
        }
    }
}
