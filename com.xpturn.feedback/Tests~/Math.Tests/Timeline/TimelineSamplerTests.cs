using System;
using System.Collections.Generic;
using NUnit.Framework;
using static xpTURN.Feedback.Tests.Timeline.TimelineDescs;

namespace xpTURN.Feedback.Tests.Timeline
{
    sealed class SamplerRig
    {
        public readonly TimelineProgram P;
        public readonly ResolvedTimeline R;
        public readonly TimelineSampler Sampler;
        public readonly TimelineSample Sample;

        public SamplerRig(TimelineProgram p, float[] itemScale = null, bool[] skip = null, float globalScale = 1f, float rootDelay = 0f)
        {
            P = p;
            R = new ResolvedTimeline(p);
            p.Resolve(itemScale ?? Array.Empty<float>(), skip ?? Array.Empty<bool>(), globalScale, rootDelay, R);
            Sampler = new TimelineSampler(p, R);
            Sample = new TimelineSample(p);
        }

        public TimelineSample At(long position, bool reverse = false)
        {
            Sampler.Sample(position, reverse, Sample);
            return Sample;
        }

        public List<TimelineEvent> Drain()
        {
            var list = new List<TimelineEvent>();
            while (Sampler.TryNext(out var e))
                list.Add(e);
            return list;
        }

        public List<TimelineEvent> Forward(long lo, long hi)
        {
            Sampler.SweepForward(lo, hi);
            return Drain();
        }

        public List<TimelineEvent> Reverse(long lo, long hi)
        {
            Sampler.SweepReverse(lo, hi);
            return Drain();
        }

        public static string Format(TimelineEvent e)
        {
            string head = (e.Reverse ? "~" : "") + e.Kind;
            if (e.Kind == TimelineEventKind.Finished)
                return $"{head}@{e.Time}";
            string what = e.Kind == TimelineEventKind.LoopIteration ? "b" + e.Block : e.Item.ToString();
            return $"{head} {what}#{e.Occurrence}@{e.Time}{(e.Skipped ? " skip" : "")}{(e.Truncated ? " trunc" : "")}";
        }

        public static string[] Format(List<TimelineEvent> events) => events.ConvertAll(Format).ToArray();
    }

    // samples · sweep events · the reverse head · seeks, with a regression case. Expected values are hand-computed.
    public sealed class TimelineSamplerTests
    {
        const float Eps = 1e-6f;

        static void AssertState(TimelineSample s, int item, TimelineStage stage, long occurrence, string where = "")
        {
            Assert.That(s.Stage(item), Is.EqualTo(stage), $"item {item} stage {where}");
            Assert.That(s.Occurrence(item), Is.EqualTo(occurrence), $"item {item} occurrence {where}");
        }

        static string[] Fmt(List<TimelineEvent> events) => SamplerRig.Format(events);

        // ---- samples ----

        [Test]
        public void S1_OccurrenceIsTheLatestReached()
        {
            // [Loop Start][A delay 0.25, 0.25][Loop End 2] — A = [0.25, 0.5] · [0.75, 1.0]
            var rig = new SamplerRig(Compile(LoopStart(), Fx(delay: 0.25f, duration: 0.25f), LoopEnd(2)));
            var s = rig.At(6 * S / 10);
            AssertState(s, 1, TimelineStage.Done, 0);
            Assert.That(s.BlockScopeOccurrence(0), Is.EqualTo(1));

            s = rig.At(8 * S / 10);
            AssertState(s, 1, TimelineStage.Running, 1);
            Assert.That(s.LocalTime(1), Is.EqualTo(0.2f).Within(Eps));
            Assert.That(s.Seconds(1), Is.EqualTo(0.05).Within(1e-9));

            AssertState(rig.At(S / 4 - 1), 1, TimelineStage.Pending, -1);
            Assert.That(rig.Sample.BlockScopeOccurrence(0), Is.EqualTo(0));
            AssertState(rig.At(S / 4), 1, TimelineStage.Running, 0);
            AssertState(rig.At(S / 2), 1, TimelineStage.Done, 0);
            AssertState(rig.At(3 * S / 4), 1, TimelineStage.Running, 1);
            AssertState(rig.At(S), 1, TimelineStage.Done, 1);

            AssertState(rig.At(6 * S / 10, true), 1, TimelineStage.Done, 1);
            AssertState(rig.At(S / 2, true), 1, TimelineStage.Running, 0);
            AssertState(rig.At(S / 4, true), 1, TimelineStage.Done, 0);
            AssertState(rig.At(S, true), 1, TimelineStage.Running, 1);
            AssertState(rig.At(S + 1, true), 1, TimelineStage.Pending, -1);
        }

        [Test]
        public void S1_NestedBlocksCountStarts()
        {
            // outer 2 × inner 3 × A 0.1 — A starts at 0.1 k
            var rig = new SamplerRig(Compile(LoopStart(), LoopStart(), Fx(duration: 0.1f), LoopEnd(3), LoopEnd(2)));
            var s = rig.At(45 * S / 100);
            AssertState(s, 2, TimelineStage.Running, 4);
            Assert.That(s.NodeStart(rig.P.NodeOfItem(2)), Is.EqualTo(4 * S / 10));
            Assert.That(s.BlockScopeOccurrence(0), Is.EqualTo(1));
            Assert.That(s.BlockScopeOccurrence(1), Is.EqualTo(4));
            AssertState(rig.At(6 * S / 10), 2, TimelineStage.Done, 5);
        }

        [Test]
        public void S1_GapRegionLeavesTheMembersDone()
        {
            var rig = new SamplerRig(Compile(LoopStart(), Fx(duration: 0.25f), LoopEnd(2, gap: 0.5f)));
            var s = rig.At(S / 2);
            AssertState(s, 1, TimelineStage.Done, 0);
            Assert.That(s.NodeStage(rig.P.BlockNode(0)), Is.EqualTo(TimelineStage.Running));
            Assert.That(s.BlockScopeOccurrence(0), Is.EqualTo(0));
        }

        [Test]
        public void S1_ZeroPeriodBlock()
        {
            var rig = new SamplerRig(Compile(LoopStart(), Instant(), LoopEnd(3), Fx(StartRule.AfterPrevious, duration: 1f)));
            AssertState(rig.At(0), 1, TimelineStage.Done, 2);
            AssertState(rig.Sample, 3, TimelineStage.Running, 0);
            AssertState(rig.At(-1), 1, TimelineStage.Pending, -1);
            AssertState(rig.At(0, true), 1, TimelineStage.Done, 0);
            AssertState(rig.At(1, true), 1, TimelineStage.Pending, -1);
            AssertState(rig.Sample, 3, TimelineStage.Running, 0);
        }

        [Test]
        public void S1_TruncatedBackgroundUsesTheNaturalDuration()
        {
            var rig = new SamplerRig(Compile(Fx(duration: 1f), Background(Fx(duration: 5f))));
            var s = rig.At(S / 2);
            AssertState(s, 1, TimelineStage.Running, 0);
            Assert.That(s.LocalTime(1), Is.EqualTo(0.1f).Within(Eps));
            Assert.That(s.Seconds(1), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(s.NodeEffectiveEnd(rig.P.NodeOfItem(1)), Is.EqualTo(S));
            Assert.That(s.NodeNaturalEnd(rig.P.NodeOfItem(1)), Is.EqualTo(5 * S));
            AssertState(rig.At(S), 1, TimelineStage.Done, 0);
            AssertState(rig.At(S, true), 1, TimelineStage.Running, 0);
            Assert.That(rig.Sample.LocalTime(1), Is.EqualTo(0.2f).Within(Eps));
        }

        [Test]
        public void S1_BackgroundRepeatIsCutAtTheTotal()
        {
            // period 0.75 — [0, 0.5] · [0.75, 1.25 → 1.0]
            var rig = new SamplerRig(Compile(Fx(duration: 1f), Background(Repeat(Fx(duration: 0.5f), -1, gap: 0.25f))));
            var s = rig.At(9 * S / 10);
            AssertState(s, 1, TimelineStage.Running, 1);
            Assert.That(s.LocalTime(1), Is.EqualTo(0.3f).Within(Eps));
            Assert.That(s.NodeEffectiveEnd(rig.P.NodeOfItem(1)), Is.EqualTo(S));
            AssertState(rig.At(S), 1, TimelineStage.Done, 1);
            AssertState(rig.At(2 * S), 1, TimelineStage.Done, 1);
            Assert.That(rig.Sample.BlockScopeOccurrence(0), Is.EqualTo(1));
        }

        [Test]
        public void S1_LastIterationDependsOnTruncation()
        {
            // not truncated: [instant · 3 times · gap 0.5] — the last iteration starts at E = 1.0
            var finite = new SamplerRig(Compile(Repeat(Instant(), 2, gap: 0.5f)));
            AssertState(finite.At(S), 0, TimelineStage.Done, 2);
            Assert.That(finite.Sample.BlockScopeOccurrence(0), Is.EqualTo(2));

            // truncated: [A 1][background 0.5 repeated forever] — the occurrence at the total (1.0) is cancelled
            var cut = new SamplerRig(Compile(Fx(duration: 1f), Background(Repeat(Fx(duration: 0.5f), -1))));
            AssertState(cut.At(S), 1, TimelineStage.Done, 1);
            Assert.That(cut.Sample.BlockScopeOccurrence(0), Is.EqualTo(1));
        }

        [Test]
        public void S1_CancelledBackgroundIsAlwaysPending()
        {
            var rig = new SamplerRig(Compile(Background(Fx(delay: 1f, duration: 1f)), Fx(StartRule.AfterAll, duration: 1f)));
            Assert.That(rig.R.NodeCancelled(rig.P.NodeOfItem(0)), Is.True);
            foreach (long v in new[] { S / 2, S, 5 * S })
                AssertState(rig.At(v), 0, TimelineStage.Pending, -1, $"at {v}");
            AssertState(rig.At(S / 2, true), 0, TimelineStage.Pending, -1);
        }

        [Test]
        public void S1_ZeroPeriodInfiniteBackgroundIsNotSeekable()
        {
            var rig = new SamplerRig(Compile(Fx(duration: 1f), Background(Repeat(Instant(), -1))));
            Assert.That(rig.R.IsSeekable, Is.False);
            Assert.That(new SamplerRig(Compile(Fx(duration: 1f), Background(Repeat(Fx(duration: 0.5f), -1)))).R.IsSeekable, Is.True);
        }

        [Test]
        public void S1_NegativePositionIsAllPending()
        {
            var rig = new SamplerRig(Compile(Fx(duration: 1f), Instant()));
            var s = rig.At(-1);
            AssertState(s, 0, TimelineStage.Pending, -1);
            AssertState(s, 1, TimelineStage.Pending, -1);
            Assert.That(s.LocalTime(0), Is.EqualTo(0f));
        }

        [Test]
        public void S1_ExactBoundariesInBothDirections()
        {
            // A = [0.25, 0.75], I = 0.75
            var rig = new SamplerRig(Compile(Fx(delay: 0.25f, duration: 0.5f), Instant(delay: 0.5f)));
            long a = S / 4, b = 3 * S / 4;
            AssertState(rig.At(a - 1), 0, TimelineStage.Pending, -1);
            AssertState(rig.At(a), 0, TimelineStage.Running, 0);
            AssertState(rig.At(b - 1), 0, TimelineStage.Running, 0);
            AssertState(rig.Sample, 1, TimelineStage.Pending, -1);
            AssertState(rig.At(b), 0, TimelineStage.Done, 0);
            AssertState(rig.Sample, 1, TimelineStage.Done, 0);

            AssertState(rig.At(b + 1, true), 0, TimelineStage.Pending, -1);
            AssertState(rig.Sample, 1, TimelineStage.Pending, -1);
            AssertState(rig.At(b, true), 0, TimelineStage.Running, 0);
            AssertState(rig.Sample, 1, TimelineStage.Done, 0);
            AssertState(rig.At(a + 1, true), 0, TimelineStage.Running, 0);
            AssertState(rig.At(a, true), 0, TimelineStage.Done, 0);

            Assert.That(rig.At(S / 2).LocalTime(0), Is.EqualTo(0.5f).Within(Eps));
            Assert.That(rig.At(S / 2, true).LocalTime(0), Is.EqualTo(0.5f).Within(Eps));
            Assert.That(rig.Sample.Seconds(0), Is.EqualTo(0.25).Within(1e-9));
        }

        // ---- sweep events ----

        [Test]
        public void S2_ZeroLengthChain()
        {
            var rig = new SamplerRig(Compile(Fx(duration: 0f), Fx(StartRule.AfterPrevious, duration: 0f), Instant(StartRule.AfterPrevious)));
            Assert.That(Fmt(rig.Forward(-1, 0)), Is.EqualTo(new[]
            {
                "Started 0#0@0", "Ended 0#0@0", "Started 1#0@0", "Ended 1#0@0", "Fired 2#0@0", "Finished@0",
            }));
        }

        [Test]
        public void S2_ZeroGapLoopRestart()
        {
            var rig = new SamplerRig(Compile(LoopStart(), Fx(duration: 0.5f), LoopEnd(2)));
            Assert.That(Fmt(rig.Forward(-1, S)), Is.EqualTo(new[]
            {
                "Started 1#0@0",
                "Ended 1#0@500000", "LoopIteration b0#0@500000", "Started 1#1@500000",
                "Ended 1#1@1000000", "LoopIteration b0#1@1000000", "Finished@1000000",
            }));
        }

        [Test]
        public void S2_BlockEndThenTheNextItem()
        {
            var rig = new SamplerRig(Compile(LoopStart(), Fx(duration: 0.5f), LoopEnd(1), Fx(StartRule.AfterPrevious, duration: 0.5f)));
            Assert.That(Fmt(rig.Forward(0, S / 2)), Is.EqualTo(new[]
            {
                "Ended 1#0@500000", "LoopIteration b0#0@500000", "Started 3#0@500000",
            }));
        }

        [Test]
        public void S2_ForegroundEndForcesTheBackgroundThenFinished()
        {
            var rig = new SamplerRig(Compile(Fx(duration: 1f), Background(Fx(duration: 2f)), Background(Fx(delay: 1f, duration: 1f))));
            var events = rig.Forward(-1, 3 * S);
            Assert.That(Fmt(events), Is.EqualTo(new[]
            {
                "Started 0#0@0", "Started 1#0@0",
                "Ended 0#0@1000000", "Ended 1#0@1000000 trunc", "Finished@1000000",
            }));
            Assert.That(events[3].OccurrenceStart, Is.EqualTo(0));
        }

        [Test]
        public void S2_SameInstantFollowsTheListOrder()
        {
            var rig = new SamplerRig(Compile(Fx(duration: 0.5f), LoopStart(), Fx(duration: 0.5f), LoopEnd(1)));
            Assert.That(Fmt(rig.Forward(0, S)), Is.EqualTo(new[]
            {
                "Ended 0#0@500000", "Ended 2#0@500000", "LoopIteration b0#0@500000", "Finished@500000",
            }));
        }

        [Test]
        public void S2_BackgroundEarlierInTheListStillComesAfterTheForeground()
        {
            var rig = new SamplerRig(Compile(Background(Fx(delay: 1f, duration: 1f)), Fx(StartRule.AfterAll, duration: 1f)));
            Assert.That(Fmt(rig.Forward(-1, 2 * S)), Is.EqualTo(new[]
            {
                "Started 1#0@0", "Ended 1#0@1000000", "Finished@1000000",
            }));
        }

        [Test]
        public void S2_ZeroPeriodFiniteBlockAndItsReverse()
        {
            var rig = new SamplerRig(Compile(LoopStart(), Instant(), LoopEnd(3), Instant(StartRule.AfterPrevious)));
            Assert.That(Fmt(rig.Forward(-1, 0)), Is.EqualTo(new[]
            {
                "Fired 1#0@0", "LoopIteration b0#0@0", "Fired 1#1@0", "LoopIteration b0#1@0",
                "Fired 1#2@0", "LoopIteration b0#2@0", "Fired 3#0@0", "Finished@0",
            }));
            Assert.That(Fmt(rig.Reverse(0, 1)), Is.EqualTo(new[]
            {
                "~Fired 3#0@0", "~LoopIteration b0#2@0", "~Fired 1#2@0", "~LoopIteration b0#1@0",
                "~Fired 1#1@0", "~LoopIteration b0#0@0", "~Fired 1#0@0",
            }));
        }

        [Test]
        public void S2_BackgroundRepeatEndsWithoutANewOccurrence()
        {
            var rig = new SamplerRig(Compile(Background(Repeat(Fx(duration: 0.5f), -1)), Fx(StartRule.AfterAll, duration: 1f)));
            Assert.That(Fmt(rig.Forward(-1, 2 * S)), Is.EqualTo(new[]
            {
                "Started 1#0@0", "Started 0#0@0",
                "Ended 0#0@500000", "Started 0#1@500000",
                "Ended 1#0@1000000", "Ended 0#1@1000000", "Finished@1000000",
            }));
        }

        [Test]
        public void S2_TruncatedEndCarriesItsOccurrenceStart()
        {
            var rig = new SamplerRig(Compile(Fx(duration: 1f), Background(Fx(delay: 0.25f, duration: 5f))));
            var events = rig.Forward(0, 10 * S);
            Assert.That(Fmt(events), Is.EqualTo(new[]
            {
                "Started 1#0@250000", "Ended 0#0@1000000", "Ended 1#0@1000000 trunc", "Finished@1000000",
            }));
            Assert.That(events[2].OccurrenceStart, Is.EqualTo(S / 4));
        }

        [Test]
        public void S2_QuietEndOfAnInstantIsACause()
        {
            var rig = new SamplerRig(Compile(Instant(), Fx(StartRule.AfterPrevious, duration: 0.5f)));
            Assert.That(Fmt(rig.Forward(-1, 0)), Is.EqualTo(new[] { "Fired 0#0@0", "Started 1#0@0" }));
        }

        [Test]
        public void S2_EmptyListFinishesAtTheRootDelay()
        {
            var rig = new SamplerRig(Compile(), rootDelay: 0.25f);
            Assert.That(Fmt(rig.Forward(-1, S)), Is.EqualTo(new[] { "Finished@250000" }));
        }

        [Test]
        public void S2_SkippedEventsAndSignalWaitBegan()
        {
            var rig = new SamplerRig(Compile(Fx(duration: 0.5f), Signal(timeout: 1f)), skip: new[] { true, false });
            Assert.That(Fmt(rig.Forward(-1, S)), Is.EqualTo(new[]
            {
                "Started 0#0@0 skip", "Started 1#0@0", "SignalWaitBegan 1#0@0",
                "Ended 0#0@500000 skip", "Ended 1#0@1000000", "Finished@1000000",
            }));
        }

        [Test]
        public void S2_LoopIterationIsAtTheIterationEndBeforeTheGap()
        {
            var rig = new SamplerRig(Compile(LoopStart(), Fx(duration: 0.25f), LoopEnd(2, gap: 0.5f)));
            var loops = rig.Forward(-1, S).FindAll(e => e.Kind == TimelineEventKind.LoopIteration);
            Assert.That(Fmt(loops), Is.EqualTo(new[] { "LoopIteration b0#0@250000", "LoopIteration b0#1@1000000" }));
        }

        [Test]
        public void S2_BackgroundZeroPeriodBlockAfterItsForegroundDependent()
        {
            // the background block's With previous dependent is foreground — it runs first, outside the replayed iteration
            var rig = new SamplerRig(Compile(Fx(duration: 1f), Background(Repeat(Instant(delay: 0.5f), 2)), Instant()));
            Assert.That(Fmt(rig.Forward(S / 2 - 1, S / 2)), Is.EqualTo(new[]
            {
                "Fired 2#0@500000", "Fired 1#0@500000", "Fired 1#1@500000", "Fired 1#2@500000",
            }));
            Assert.That(Fmt(rig.Reverse(S / 2, S / 2 + 1)), Is.EqualTo(new[]
            {
                "~Fired 1#2@500000", "~Fired 1#1@500000", "~Fired 1#0@500000", "~Fired 2#0@500000",
            }));
        }

        [Test]
        public void S2_NestedZeroPeriodBlocksReplayWithStrides()
        {
            // outer 3 × inner 2 at one instant — the item's occurrence grows by 2 per outer iteration
            var rig = new SamplerRig(Compile(LoopStart(), LoopStart(), Instant(), LoopEnd(2), LoopEnd(3)));
            var forward = rig.Forward(-1, 0);
            Assert.That(Fmt(forward), Is.EqualTo(new[]
            {
                "Fired 2#0@0", "LoopIteration b1#0@0", "Fired 2#1@0", "LoopIteration b1#1@0", "LoopIteration b0#0@0",
                "Fired 2#2@0", "LoopIteration b1#0@0", "Fired 2#3@0", "LoopIteration b1#1@0", "LoopIteration b0#1@0",
                "Fired 2#4@0", "LoopIteration b1#0@0", "Fired 2#5@0", "LoopIteration b1#1@0", "LoopIteration b0#2@0",
                "Finished@0",
            }));
            forward.RemoveAt(forward.Count - 1);
            var expected = forward.ConvertAll(e => e.Flipped());
            expected.Reverse();
            Assert.That(Fmt(rig.Reverse(0, 1)), Is.EqualTo(Fmt(expected)));
        }

        [Test]
        public void S2_EmptyLoopIterationsAreInstants()
        {
            var rig = new SamplerRig(Compile(LoopStart(), LoopEnd(3, gap: 0.5f)));
            Assert.That(Fmt(rig.Forward(-1, S)), Is.EqualTo(new[]
            {
                "LoopIteration b0#0@0", "LoopIteration b0#1@500000", "LoopIteration b0#2@1000000", "Finished@1000000",
            }));
            Assert.That(Fmt(rig.Reverse(0, S + 1)), Is.EqualTo(new[]
            {
                "~LoopIteration b0#2@1000000", "~LoopIteration b0#1@500000", "~LoopIteration b0#0@0",
            }));
        }

        [Test]
        public void S2_RestartComesBeforeLaterSiblings()
        {
            // the restart is keyed by the Loop End — B (after it in the list) starts after the new iteration's A
            var rig = new SamplerRig(Compile(LoopStart(), Fx(duration: 0.5f), LoopEnd(2), Fx(delay: 0.5f, duration: 0.5f)));
            Assert.That(Fmt(rig.Forward(S / 2 - 1, S / 2)), Is.EqualTo(new[]
            {
                "Ended 1#0@500000", "LoopIteration b0#0@500000", "Started 1#1@500000", "Started 3#0@500000",
            }));
        }

        // ---- the reverse head ----

        [Test]
        public void T7_ReverseBeginStartsTheEndAndPlaysBackToTheStart()
        {
            var rig = new SamplerRig(Compile(Fx(duration: 1f), Instant(StartRule.AfterPrevious)));
            rig.Sampler.BeginReverse();
            Assert.That(Fmt(rig.Drain()), Is.EqualTo(new[] { "~Fired 1#0@1000000", "~Started 0#0@1000000" }));

            rig.Sampler.AdvanceReverse(0.1);
            Assert.That(rig.Drain(), Is.Empty);
            float first = rig.At(rig.Sampler.ReversePosition, true).LocalTime(0);
            Assert.That(first, Is.EqualTo(0.9f).Within(Eps));
            rig.Sampler.AdvanceReverse(0.1);
            rig.Drain();
            Assert.That(rig.At(rig.Sampler.ReversePosition, true).LocalTime(0), Is.LessThan(first));

            rig.Sampler.AdvanceReverse(1.0);
            Assert.That(Fmt(rig.Drain()), Is.EqualTo(new[] { "~Ended 0#0@0", "~Finished@0" }));
            Assert.That(rig.Sampler.IsReverseFinished, Is.True);
            rig.Sampler.AdvanceReverse(0.1);
            Assert.That(rig.Drain(), Is.Empty);
        }

        [Test]
        public void T7_RootDelayIsWaitedFirst()
        {
            var rig = new SamplerRig(Compile(Fx(duration: 1f)), rootDelay: 0.5f);
            rig.Sampler.BeginReverse();
            Assert.That(rig.Drain(), Is.Empty);
            rig.Sampler.AdvanceReverse(0.25);
            Assert.That(rig.Drain(), Is.Empty);
            Assert.That(rig.Sampler.ReversePosition, Is.EqualTo(3 * S / 2 + 1));
            rig.Sampler.AdvanceReverse(0.25);
            Assert.That(Fmt(rig.Drain()), Is.EqualTo(new[] { "~Started 0#0@1500000" }));
            rig.Sampler.AdvanceReverse(1.0);
            Assert.That(Fmt(rig.Drain()), Is.EqualTo(new[] { "~Ended 0#0@500000", "~Finished@500000" }));
        }

        [Test]
        public void T7_DtLeftAfterTheDelayMovesTheSameTick()
        {
            var rig = new SamplerRig(Compile(Fx(duration: 1f)), rootDelay: 0.5f);
            rig.Sampler.BeginReverse();
            rig.Sampler.AdvanceReverse(0.75);
            Assert.That(Fmt(rig.Drain()), Is.EqualTo(new[] { "~Started 0#0@1500000" }));
            Assert.That(rig.Sampler.ReversePosition, Is.EqualTo(5 * S / 4));
        }

        [Test]
        public void T7_InfiniteBackgroundHasFiniteSeconds()
        {
            var rig = new SamplerRig(Compile(Fx(duration: 1f), Background(Repeat(Fx(duration: 0.4f), -1))));
            var s = rig.At(9 * S / 10, true);
            AssertState(s, 1, TimelineStage.Running, 2);
            Assert.That(s.Seconds(1), Is.EqualTo(0.1).Within(1e-9));
            Assert.That(s.LocalTime(1), Is.EqualTo(0.25f).Within(Eps));
        }

        [Test]
        public void T7_TruncatedBackgroundStartsWhereItWasCut()
        {
            var rig = new SamplerRig(Compile(Fx(duration: 1f), Background(Fx(duration: 5f))));
            rig.Sampler.BeginReverse();
            var events = rig.Drain();
            Assert.That(Fmt(events), Is.EqualTo(new[] { "~Started 1#0@1000000 trunc", "~Started 0#0@1000000" }));
            Assert.That(events[0].OccurrenceStart, Is.EqualTo(0));
        }

        [Test]
        public void T7_EmptyListFinishesRightAfterBegin()
        {
            var rig = new SamplerRig(Compile());
            rig.Sampler.BeginReverse();
            Assert.That(Fmt(rig.Drain()), Is.EqualTo(new[] { "~Finished@0" }));
        }

        // ---- seeks ----

        // I0 = 0 · I1 = 0.5 · A = [0.5, 1.5]
        static SamplerRig SeekRig() => new SamplerRig(Compile(Instant(), Instant(delay: 0.5f), Fx(duration: 1f)));

        static List<TimelineEvent> Seek(SamplerRig rig, long from, long to, bool reversePlay)
        {
            rig.Sampler.SeekBegin(from, to, reversePlay);
            return rig.Drain();
        }

        [Test]
        public void T8_ForwardPlaySeekToZeroKeepsTimeZero()
        {
            var rig = SeekRig();
            Assert.That(Fmt(Seek(rig, 0, S, false)), Is.EqualTo(new[] { "Fired 1#0@500000", "Started 2#0@500000" }));
            Assert.That(Fmt(Seek(rig, S, 0, false)), Is.EqualTo(new[] { "~Ended 2#0@500000", "~Fired 1#0@500000" }));
        }

        [Test]
        public void T8_SeekForwardThenBackIsOncePerBoundary()
        {
            var rig = new SamplerRig(Compile(LoopStart(), Instant(), Fx(duration: 0.25f), LoopEnd(3, gap: 0.25f), Background(Fx(duration: 5f))));
            var forward = Seek(rig, 0, rig.R.Total, false);
            var back = Seek(rig, rig.R.Total, 0, false);
            forward.RemoveAll(e => e.Kind == TimelineEventKind.Finished || e.Kind == TimelineEventKind.SignalWaitBegan);
            var expected = forward.ConvertAll(e => e.Flipped());
            expected.Reverse();
            Assert.That(Fmt(back), Is.EqualTo(Fmt(expected)));
        }

        [Test]
        public void T8_SamePositionHasNoEvents()
        {
            var rig = SeekRig();
            Assert.That(Seek(rig, S / 2, S / 2, false), Is.Empty);
            Assert.That(Seek(rig, S / 2, S / 2, true), Is.Empty);
        }

        [Test]
        public void T8_BeyondTheTotalIsClamped()
        {
            var rig = SeekRig();
            Assert.That(rig.Sampler.SeekBegin(S, 5 * S, false), Is.EqualTo(3 * S / 2));
            Assert.That(Fmt(rig.Drain()), Is.EqualTo(new[] { "Ended 2#0@1500000", "Finished@1500000" }));
        }

        [Test]
        public void T8_FourCasesAtTheExactBoundary()
        {
            var rig = SeekRig();
            long s = S / 2;
            var redo = new[] { "Fired 1#0@500000", "Started 2#0@500000" };
            var undo = new[] { "~Ended 2#0@500000", "~Fired 1#0@500000" };
            // forward play — s ≤ ν is passed
            Assert.That(Fmt(Seek(rig, s - 1, s, false)), Is.EqualTo(redo));
            Assert.That(Fmt(Seek(rig, s, s - 1, false)), Is.EqualTo(undo));
            Assert.That(Seek(rig, S, s, false), Is.Empty);
            // reverse play — s ≥ ν is passed
            Assert.That(Fmt(Seek(rig, s + 1, s, true)), Is.EqualTo(undo));
            Assert.That(Fmt(Seek(rig, s, s + 1, true)), Is.EqualTo(redo));
            Assert.That(Fmt(Seek(rig, S, s, true)), Is.EqualTo(undo));
        }

        [Test]
        public void T8_SeekIntoABlockGivesThatIteration()
        {
            var rig = new SamplerRig(Compile(LoopStart(), Fx(duration: 0.25f), LoopEnd(4)));
            long to = rig.Sampler.SeekBegin(0, 6 * S / 10, false);
            var events = rig.Drain();
            Assert.That(SamplerRig.Format(events[events.Count - 1]), Is.EqualTo("Started 1#2@500000"));
            AssertState(rig.At(to), 1, TimelineStage.Running, 2);
        }

        // ---- a regression case ----

        // a reverse play over a wait and a root delay finishes after the whole total, not the initial delay only.
        [Test]
        public void Reg02_ReverseFinishesAfterTheTotal()
        {
            var rig = new SamplerRig(Compile(Fx(duration: 0.5f), Wait(1f), Fx(duration: 0.25f)), rootDelay: 0.5f);
            Assert.That(rig.R.Total, Is.EqualTo(7 * S / 4));
            rig.Sampler.BeginReverse();
            int ticks = 0, finished = 0, finishedAt = -1;
            while (ticks < 1000 && !rig.Sampler.IsReverseFinished)
            {
                rig.Sampler.AdvanceReverse(1.0 / 64);
                ticks++;
                foreach (var e in rig.Drain())
                {
                    if (e.Kind != TimelineEventKind.Finished)
                        continue;
                    finished++;
                    finishedAt = ticks;
                }
            }
            Assert.That(finished, Is.EqualTo(1));
            Assert.That(finishedAt / 64.0, Is.EqualTo(1.75));
        }
    }
}
