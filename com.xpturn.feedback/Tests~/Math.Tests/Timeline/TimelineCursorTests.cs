using System;
using System.Collections.Generic;
using NUnit.Framework;
using static xpTURN.Feedback.Tests.Timeline.TimelineDescs;

namespace xpTURN.Feedback.Tests.Timeline
{
    sealed class CursorRig
    {
        public readonly SamplerRig S;
        public readonly TimelineCursor Cursor;
        double _accS;
        double _accU;
        long _baseS;
        long _baseU;

        public long TauS { get; private set; }
        public long TauU { get; private set; }

        public CursorRig(SamplerRig s)
        {
            S = s;
            Cursor = new TimelineCursor(s.P, s.R, s.Sampler);
        }

        public CursorRig(TimelineProgram p, float[] itemScale = null, bool[] skip = null, float globalScale = 1f, float rootDelay = 0f)
            : this(new SamplerRig(p, itemScale, skip, globalScale, rootDelay))
        {
        }

        public List<TimelineEvent> Begin(TimelineClock baseClock = TimelineClock.Scaled)
        {
            Cursor.Begin(baseClock);
            _accS = _accU = 0;
            _baseS = _baseU = 0;
            TauS = TauU = 0;
            var events = Drain();
            Cursor.EndAdvance();
            return events;
        }

        public List<TimelineEvent> Tick(double dt) => Tick(dt, dt);

        public List<TimelineEvent> Tick(double dtScaled, double dtUnscaled)
        {
            _accS += dtScaled;
            _accU += dtUnscaled;
            TauS = _baseS + TimelineTime.FromSeconds(_accS);
            TauU = _baseU + TimelineTime.FromSeconds(_accU);
            Cursor.BeginAdvance(dtScaled, dtUnscaled);
            var events = Drain();
            Cursor.EndAdvance();
            return events;
        }

        public List<TimelineEvent> Complete()
        {
            Cursor.BeginComplete();
            var events = Drain();
            Cursor.EndAdvance();
            return events;
        }

        public void Rebuild(long position)
        {
            Cursor.Rebuild(position);
            _accS = _accU = 0;
            _baseS = _baseU = position;
            TauS = TauU = position;
        }

        public List<TimelineEvent> Drain()
        {
            var list = new List<TimelineEvent>();
            while (Cursor.TryNext(out var e))
                list.Add(e);
            return list;
        }
    }

    // one clock · two clocks · a regression case · completion and rebuild.
    // Expected values are hand-computed.
    public sealed class TimelineCursorTests
    {
        static string[] Fmt(List<TimelineEvent> events) => SamplerRig.Format(events);

        static string[] Kinds(List<TimelineEvent> events)
            => events.ConvertAll(e => e.Kind == TimelineEventKind.LoopIteration ? $"{e.Kind} b{e.Block}#{e.Occurrence}" : e.Kind == TimelineEventKind.Finished ? "Finished" : $"{e.Kind} {e.Item}#{e.Occurrence}").ToArray();

        static TimelineItemDesc WithRule(TimelineItemDesc d, StartRule rule)
        {
            d.Rule = rule;
            return d;
        }

        // ---- one clock ----

        [Test]
        public void C1_BeginRunsTimeZeroOnceWithItsChains()
        {
            var rig = new CursorRig(Compile(Instant(), Fx(StartRule.AfterPrevious, duration: 0f), Instant(StartRule.AfterPrevious), Fx(duration: 1f)));
            Assert.That(Fmt(rig.Begin()), Is.EqualTo(new[]
            {
                "Fired 0#0@0", "Started 1#0@0", "Ended 1#0@0", "Fired 2#0@0", "Started 3#0@0",
            }));
            Assert.That(rig.Tick(0.5), Is.Empty);
        }

        [Test]
        public void C1_RootDelayLeavesBeginEmpty()
        {
            var rig = new CursorRig(Compile(Instant(), Fx(duration: 0.5f)), rootDelay: 0.25f);
            Assert.That(rig.Begin(), Is.Empty);
            Assert.That(Fmt(rig.Tick(0.25)), Is.EqualTo(new[] { "Fired 0#0@250000", "Started 1#0@250000" }));
        }

        [Test]
        public void C1_InstantAtTheTickEndFiresInThatTick()
        {
            var rig = new CursorRig(Compile(Instant(delay: 0.5f), Fx(duration: 1f)));
            Assert.That(rig.Begin(), Is.Empty);
            Assert.That(rig.Tick(0.25), Is.Empty);
            Assert.That(Fmt(rig.Tick(0.25)), Is.EqualTo(new[] { "Fired 0#0@500000", "Started 1#0@500000" }));
        }

        [Test]
        public void C1_FrameJumpCrossesTheBoundariesInOrder()
        {
            // 0.25 · 0.5 · [0.5, 0.75] · 0.75
            var rig = new CursorRig(Compile(Instant(delay: 0.25f), Instant(delay: 0.25f), Fx(StartRule.AfterPrevious, duration: 0.25f), Instant(StartRule.AfterPrevious)));
            Assert.That(rig.Begin(), Is.Empty);
            Assert.That(Fmt(rig.Tick(1.0)), Is.EqualTo(new[]
            {
                "Fired 0#0@250000", "Fired 1#0@500000", "Started 2#0@500000", "Ended 2#0@750000", "Fired 3#0@750000", "Finished@750000",
            }));
        }

        [Test]
        public void C1_FiniteZeroLengthBlockRunsWholeInOneTick()
        {
            var rig = new CursorRig(Compile(Fx(duration: 0.5f), LoopStart(StartRule.AfterPrevious), Instant(), LoopEnd(3)));
            Assert.That(Fmt(rig.Begin()), Is.EqualTo(new[] { "Started 0#0@0" }));
            Assert.That(Fmt(rig.Tick(0.5)), Is.EqualTo(new[]
            {
                "Ended 0#0@500000", "Fired 2#0@500000", "LoopIteration b0#0@500000", "Fired 2#1@500000", "LoopIteration b0#1@500000",
                "Fired 2#2@500000", "LoopIteration b0#2@500000", "Finished@500000",
            }));
        }

        [Test]
        public void C1_InfiniteZeroLengthBlockRestartsOncePerTickAndNotWhileItsClockStands()
        {
            var rig = new CursorRig(Compile(LoopStart(), Instant(), LoopEnd(0)));
            Assert.That(Fmt(rig.Begin()), Is.EqualTo(new[] { "Fired 1#0@0", "LoopIteration b0#0@0" }));
            // the restart comes at the next tick's start
            Assert.That(Fmt(rig.Tick(0.1)), Is.EqualTo(new[] { "Fired 1#1@0", "LoopIteration b0#1@0" }));
            Assert.That(rig.Tick(0), Is.Empty);
            Assert.That(rig.Tick(0, 0.1), Is.Empty);
            Assert.That(Fmt(rig.Tick(0.1)), Is.EqualTo(new[] { "Fired 1#2@100000", "LoopIteration b0#2@100000" }));
        }

        [Test]
        public void C1_SignalAndTimeoutEndTheWait()
        {
            var p = Compile(Signal(timeout: 1f), Instant(StartRule.AfterPrevious));
            var signaled = new CursorRig(p);
            Assert.That(Fmt(signaled.Begin()), Is.EqualTo(new[] { "Started 0#0@0", "SignalWaitBegan 0#0@0" }));
            Assert.That(signaled.Tick(0.25), Is.Empty);
            Assert.That(signaled.Cursor.Signal(), Is.EqualTo(1));
            // between ticks: the end is at the signal's τ and comes out at the next tick's f = 0
            Assert.That(Fmt(signaled.Tick(0.25)), Is.EqualTo(new[] { "Ended 0#0@250000", "Fired 1#0@250000", "Finished@250000" }));
            Assert.That(signaled.Cursor.IsOffNominal, Is.True, "a signal before the timeout leaves the nominal axis");

            var timedOut = new CursorRig(p);
            timedOut.Begin();
            Assert.That(timedOut.Tick(0.5), Is.Empty);
            Assert.That(Fmt(timedOut.Tick(0.5)), Is.EqualTo(new[] { "Ended 0#0@1000000", "Fired 1#0@1000000", "Finished@1000000" }));
            Assert.That(timedOut.Cursor.IsOffNominal, Is.False);
        }

        [Test]
        public void C1_SignalDuringEventHandlingEndsTheWaitAtThatMoment()
        {
            var rig = new CursorRig(Compile(Fx(duration: 0.5f), Signal(timeout: 2f), Instant(StartRule.AfterPrevious)));
            Assert.That(Fmt(rig.Begin()), Is.EqualTo(new[] { "Started 0#0@0", "Started 1#0@0", "SignalWaitBegan 1#0@0" }));
            rig.Cursor.BeginAdvance(1.0, 1.0);
            Assert.That(rig.Cursor.TryNext(out var first), Is.True);
            Assert.That(SamplerRig.Format(first), Is.EqualTo("Ended 0#0@500000"));
            Assert.That(rig.Cursor.Signal(), Is.EqualTo(1));
            Assert.That(Fmt(rig.Drain()), Is.EqualTo(new[] { "Ended 1#0@500000", "Fired 2#0@500000", "Finished@500000" }));
            rig.Cursor.EndAdvance();
        }

        [Test]
        public void C1_SignalWithoutAWaitIsNotStored()
        {
            var rig = new CursorRig(Compile(Fx(duration: 0.5f), WithRule(Signal(timeout: 1f), StartRule.AfterPrevious)));
            rig.Begin();
            rig.Tick(0.25);
            Assert.That(rig.Cursor.Signal(), Is.EqualTo(0));
            Assert.That(Fmt(rig.Tick(0.25)), Is.EqualTo(new[] { "Ended 0#0@500000", "Started 1#0@500000", "SignalWaitBegan 1#0@500000" }));
            Assert.That(Fmt(rig.Tick(1.0)), Is.EqualTo(new[] { "Ended 1#0@1500000", "Finished@1500000" }));
        }

        [Test]
        public void C1_SecondSignalBeforeTheTickCountsNothing()
        {
            var rig = new CursorRig(Compile(Signal(timeout: 1f)));
            rig.Begin();
            rig.Tick(0.25);
            Assert.That(rig.Cursor.Signal(), Is.EqualTo(1));
            Assert.That(rig.Cursor.Signal(), Is.EqualTo(0));
            Assert.That(Fmt(rig.Tick(0.25)), Is.EqualTo(new[] { "Ended 0#0@250000", "Finished@250000" }));
            Assert.That(rig.Cursor.Signal(), Is.EqualTo(0), "a finished play has no wait");
        }

        [Test]
        public void C1_LoopIterationForUserBlocksOnly()
        {
            var user = new CursorRig(Compile(LoopStart(), Fx(duration: 0.25f), LoopEnd(3)));
            var events = user.Begin();
            for (int i = 0; i < 4; i++)
                events.AddRange(user.Tick(0.25));
            var loops = events.FindAll(e => e.Kind == TimelineEventKind.LoopIteration);
            Assert.That(Fmt(loops), Is.EqualTo(new[] { "LoopIteration b0#0@250000", "LoopIteration b0#1@500000", "LoopIteration b0#2@750000" }));

            var repeated = new CursorRig(Compile(Repeat(Fx(duration: 0.25f), 2)));
            var all = repeated.Begin();
            for (int i = 0; i < 4; i++)
                all.AddRange(repeated.Tick(0.25));
            Assert.That(all.FindAll(e => e.Kind == TimelineEventKind.LoopIteration), Is.Empty);
            Assert.That(all.FindAll(e => e.Kind == TimelineEventKind.Started).Count, Is.EqualTo(3));
        }

        [Test]
        public void C1_FinishedOnceThenNothing()
        {
            var rig = new CursorRig(Compile(Fx(duration: 0.5f)));
            rig.Begin();
            Assert.That(Fmt(rig.Tick(1.0)), Is.EqualTo(new[] { "Ended 0#0@500000", "Finished@500000" }));
            Assert.That(rig.Cursor.IsFinished, Is.True);
            Assert.That(rig.Tick(1.0), Is.Empty);
            Assert.That(rig.Cursor.Position, Is.EqualTo(S / 2));
        }

        [Test]
        public void C1_AbortDropsTheRestOfTheTick()
        {
            var rig = new CursorRig(Compile(Instant(delay: 0.25f), Instant(delay: 0.25f)));
            rig.Begin();
            rig.Cursor.BeginAdvance(1.0, 1.0);
            Assert.That(rig.Cursor.TryNext(out var first), Is.True);
            Assert.That(SamplerRig.Format(first), Is.EqualTo("Fired 0#0@250000"));
            rig.Cursor.Abort();
            Assert.That(rig.Cursor.TryNext(out _), Is.False);
            rig.Cursor.EndAdvance();
            Assert.That(rig.Tick(1.0), Is.Empty);
        }

        [Test]
        public void C1_BlockEndWaitsForAScheduledForegroundMember()
        {
            // A = [0, 0.3] · B = [0.5, 0.6] (With previous + 0.5) — the iteration ends at 0.6
            var rig = new CursorRig(Compile(LoopStart(), Fx(duration: 0.3f), Fx(delay: 0.5f, duration: 0.1f), LoopEnd(1)));
            Assert.That(Fmt(rig.Begin()), Is.EqualTo(new[] { "Started 1#0@0" }));
            Assert.That(Fmt(rig.Tick(0.4)), Is.EqualTo(new[] { "Ended 1#0@300000" }));
            Assert.That(Fmt(rig.Tick(0.4)), Is.EqualTo(new[] { "Started 2#0@500000", "Ended 2#0@600000", "LoopIteration b0#0@600000", "Finished@600000" }));
        }

        [Test]
        public void C1_BackgroundBoundaries()
        {
            // ① After previous skips the background · the running background is cut at the total
            var skip = new CursorRig(Compile(Fx(duration: 1f), Background(Fx(duration: 2f)), Fx(StartRule.AfterPrevious, duration: 0.5f)));
            Assert.That(Fmt(skip.Begin()), Is.EqualTo(new[] { "Started 0#0@0", "Started 1#0@0" }));
            Assert.That(Fmt(skip.Tick(1.0)), Is.EqualTo(new[] { "Ended 0#0@1000000", "Started 2#0@1000000" }));
            var end = skip.Tick(0.5);
            Assert.That(Fmt(end), Is.EqualTo(new[] { "Ended 2#0@1500000", "Ended 1#0@1500000 trunc", "Finished@1500000" }));
            Assert.That(end[1].OccurrenceStart, Is.EqualTo(0));

            // ② a background not started when the foreground ends has no events
            var late = new CursorRig(Compile(Fx(duration: 1f), Background(Fx(delay: 2f, duration: 1f))));
            Assert.That(Fmt(late.Begin()), Is.EqualTo(new[] { "Started 0#0@0" }));
            Assert.That(Fmt(late.Tick(3.0)), Is.EqualTo(new[] { "Ended 0#0@1000000", "Finished@1000000" }));

            // ③ with no foreground the background mark is ignored
            var alone = new CursorRig(Compile(Background(Fx(duration: 1f))));
            Assert.That(Fmt(alone.Begin()), Is.EqualTo(new[] { "Started 0#0@0" }));
            Assert.That(Fmt(alone.Tick(1.0)), Is.EqualTo(new[] { "Ended 0#0@1000000", "Finished@1000000" }));
        }

        [Test]
        public void C1_SkippedEventsCarryTheBit()
        {
            var rig = new CursorRig(Compile(Fx(duration: 0.5f), Instant(StartRule.AfterPrevious)), skip: new[] { true, true });
            Assert.That(Fmt(rig.Begin()), Is.EqualTo(new[] { "Started 0#0@0 skip" }));
            Assert.That(Fmt(rig.Tick(0.5)), Is.EqualTo(new[] { "Ended 0#0@500000 skip", "Fired 1#0@500000 skip", "Finished@500000" }));
        }

        [Test]
        public void C1_LocalTimeAndSecondsOnTheItemsClock()
        {
            var rig = new CursorRig(Compile(Fx(duration: 1f), Fx(delay: 0.5f, duration: 0.25f)));
            rig.Begin();
            rig.Tick(0.25);
            Assert.That(rig.Cursor.LocalTime(0), Is.EqualTo(0.25f));
            Assert.That(rig.Cursor.Seconds(0), Is.EqualTo(0.25));
            Assert.That(rig.Cursor.Stage(1), Is.EqualTo(TimelineStage.Pending));
            Assert.That(rig.Cursor.LocalTime(1), Is.EqualTo(0f));
            rig.Tick(0.375);
            Assert.That(rig.Cursor.LocalTime(1), Is.EqualTo(0.5f));
            Assert.That(rig.Cursor.Seconds(1), Is.EqualTo(0.125));
        }

        [Test]
        public void C1_QueryIsTheLatestReachedOccurrence()
        {
            // at 0.6 the block is in iteration 1 but A starts again only at 0.75 — A is done, occurrence 0
            var rig = new CursorRig(Compile(LoopStart(), Fx(delay: 0.25f, duration: 0.25f), LoopEnd(2)));
            rig.Begin();
            rig.Tick(0.6);
            Assert.That(rig.Cursor.Stage(1), Is.EqualTo(TimelineStage.Done));
            Assert.That(rig.Cursor.Occurrence(1), Is.EqualTo(0));
            // a flow marker has no node
            Assert.That(rig.Cursor.Stage(0), Is.EqualTo(TimelineStage.Pending));
            Assert.That(rig.Cursor.Occurrence(0), Is.EqualTo(-1));
            Assert.That(rig.Cursor.LocalTime(0), Is.EqualTo(0f));
            Assert.That(rig.Cursor.Seconds(2), Is.EqualTo(0.0));
        }

        [Test]
        public void C1_ReusedBufferPlaysLikeAFreshCursor()
        {
            var p = Compile(
                Fx(duration: 0.5f), LoopStart(StartRule.AfterPrevious), Instant(), Fx(duration: 0.25f), LoopEnd(2, gap: 0.25f),
                Hold(0.1f), Signal(timeout: 0.5f), Clock(Fx(duration: 0.3f), TimelineClock.Unscaled),
                Background(Repeat(Fx(duration: 0.2f), -1)));
            var fresh = new CursorRig(p);
            var expected = Play(fresh);

            var reused = new CursorRig(p);
            reused.Begin();
            reused.Cursor.BeginAdvance(0.6, 0.6);
            reused.Cursor.TryNext(out _);
            reused.Cursor.Abort();
            reused.Cursor.EndAdvance();
            Assert.That(Fmt(Play(reused)), Is.EqualTo(Fmt(expected)), "after an abort");

            reused.Begin();
            reused.Tick(0.3);
            reused.Complete();
            Assert.That(Fmt(Play(reused)), Is.EqualTo(Fmt(expected)), "after a completion");

            // the wait runs [1.35, 1.85] — a signal at 1.4 ends it early
            reused.Begin();
            reused.Tick(1.4);
            Assert.That(reused.Cursor.Signal(), Is.EqualTo(1));
            reused.Tick(0.1);
            Assert.That(reused.Cursor.IsOffNominal, Is.True);
            reused.Begin();
            Assert.That(reused.Cursor.IsOffNominal, Is.False);
            Assert.That(Fmt(Play(reused)), Is.EqualTo(Fmt(expected)), "after an off-nominal play");
        }

        static List<TimelineEvent> Play(CursorRig rig)
        {
            var events = rig.Begin();
            for (int i = 0; i < 30 && !rig.Cursor.IsFinished; i++)
            {
                events.AddRange(rig.Tick(0.125));
                if (i == 11)
                    Assert.That(rig.Cursor.Signal(), Is.EqualTo(1));
            }
            return events;
        }

        // ---- two clocks ----

        [Test]
        public void C2_HitStopStartsTheScaledFollowerAtTheMoment()
        {
            var rig = new CursorRig(Compile(Clock(Fx(duration: 0.1f), TimelineClock.Unscaled), Fx(StartRule.AfterPrevious, duration: 0.5f)));
            Assert.That(Fmt(rig.Begin()), Is.EqualTo(new[] { "Started 0#0@0" }));
            Assert.That(rig.Tick(0, 0.05), Is.Empty);
            Assert.That(Fmt(rig.Tick(0, 0.05)), Is.EqualTo(new[] { "Ended 0#0@100000", "Started 1#0@100000" }));
            Assert.That(rig.Tick(0, 0.1), Is.Empty);
            Assert.That(rig.Cursor.LocalTime(1), Is.EqualTo(0f));
            Assert.That(rig.Tick(0.25, 0.25), Is.Empty);
            Assert.That(rig.Cursor.LocalTime(1), Is.EqualTo(0.5f));
            Assert.That(Fmt(rig.Tick(0.25, 0.25)), Is.EqualTo(new[] { "Ended 1#0@600000", "Finished@600000" }));
            Assert.That(rig.Cursor.IsOffNominal, Is.False, "only one clock was live at a time");
        }

        [Test]
        public void C2_ParentHitStopExampleLeavesTheNominalAxis()
        {
            // [flash 0.3 Scaled][hit stop 0.1 Unscaled, With previous][particle Scaled, After previous]
            var rig = new CursorRig(Compile(Fx(duration: 0.3f), Clock(Fx(duration: 0.1f), TimelineClock.Unscaled), Instant(StartRule.AfterPrevious)));
            Assert.That(Fmt(rig.Begin()), Is.EqualTo(new[] { "Started 0#0@0", "Started 1#0@0" }));
            Assert.That(Kinds(rig.Tick(0, 0.1)), Is.EqualTo(new[] { "Ended 1#0", "Fired 2#0" }));
            Assert.That(rig.Cursor.IsOffNominal, Is.True, "the flash and the hit stop were live at different rates");
        }

        [Test]
        public void C2_TickFractionPlacesTheOtherClock()
        {
            // Scaled at twice the Unscaled rate: U ends at f = 0.5 — the Scaled follower starts at τ_S(0.5) = 1.0
            var rig = new CursorRig(Compile(Clock(Fx(duration: 0.5f), TimelineClock.Unscaled), Instant(StartRule.AfterPrevious), Fx(StartRule.AfterPrevious, duration: 0.25f)));
            rig.Begin();
            Assert.That(Fmt(rig.Tick(2.0, 1.0)), Is.EqualTo(new[]
            {
                "Ended 0#0@500000", "Fired 1#0@500000", "Started 2#0@500000", "Ended 2#0@750000", "Finished@750000",
            }));
            Assert.That(rig.Cursor.IsOffNominal, Is.False);
            Assert.That(rig.Cursor.Position, Is.EqualTo(3 * S / 4));
        }

        [Test]
        public void C2_EqualDtOnTwoClocksIsTheNominalPlay()
        {
            var rig = new CursorRig(Compile(
                Fx(duration: 0.5f), Clock(Fx(StartRule.AfterPrevious, duration: 0.25f), TimelineClock.Unscaled),
                Clock(LoopStart(delay: 0.125f), TimelineClock.Unscaled), Instant(), Clock(LoopEnd(2, gap: 0.25f), TimelineClock.Unscaled),
                Clock(Hold(0.125f), TimelineClock.Unscaled), Fx(duration: 0.125f)));
            TimelinePropertyAssert.Events(rig.S.Forward(-1, 0), rig.Begin(), "Begin");
            for (int i = 0; i < 24; i++)
            {
                long from = rig.TauS;
                TimelinePropertyAssert.Events(rig.S.Forward(from, from + S / 8), rig.Tick(0.125), $"tick {i}");
            }
            Assert.That(rig.Cursor.IsFinished, Is.True);
            Assert.That(rig.Cursor.IsOffNominal, Is.False);
        }

        [Test]
        public void C2_InheritedFractionKeepsTheChainAtTheCause()
        {
            // Scaled stands still: A (U) ends at f = 0.6 → B (S) fires at that f → C (U) starts at U 0.6, not at the tick
            // start — so D (U, ends at 0.65) ends between C's start and its end
            var rig = new CursorRig(Compile(
                Clock(Fx(duration: 0.65f), TimelineClock.Unscaled), Clock(Fx(duration: 0.6f), TimelineClock.Unscaled),
                Instant(StartRule.AfterPrevious), Clock(Fx(StartRule.AfterPrevious, duration: 0.1f), TimelineClock.Unscaled)));
            rig.Begin();
            Assert.That(Fmt(rig.Tick(0, 1.0)), Is.EqualTo(new[]
            {
                "Ended 1#0@600000", "Fired 2#0@600000", "Started 3#0@600000", "Ended 0#0@650000", "Ended 3#0@700000", "Finished@700000",
            }));
            Assert.That(rig.Cursor.IsOffNominal, Is.False);
        }

        [Test]
        public void C2_TransferKeepsTheSameMomentKeyOrder()
        {
            // A and the background E end together on Unscaled; S (Scaled, key 2) follows A. f × dt_S is not a whole µs —
            // rounded down S ties at A's f and its key comes before E's (background keys come last). Rounded up it would
            // slip behind E.
            var rig = new CursorRig(Compile(
                Clock(Fx(duration: 0.3f), TimelineClock.Unscaled), Background(Clock(Fx(duration: 0.3f), TimelineClock.Unscaled)),
                Instant(StartRule.AfterPrevious)));
            rig.Begin();
            Assert.That(Fmt(rig.Tick(0.123457, 0.5)), Is.EqualTo(new[] { "Ended 0#0@300000", "Fired 2#0@300000", "Ended 1#0@300000", "Finished@300000" }));
            Assert.That(rig.Cursor.IsOffNominal, Is.False);
        }

        [Test]
        public void C2_LongTickKeepsTheOrder()
        {
            // one 5,000 s tick: fractions of that tick multiplied together overflow long — chunks keep them apart
            var rig = new CursorRig(Compile(Fx(duration: 3600f), Fx(duration: 1000f)));
            rig.Begin();
            Assert.That(Fmt(rig.Tick(5000)), Is.EqualTo(new[] { "Ended 1#0@1000000000", "Ended 0#0@3600000000", "Finished@3600000000" }));
        }

        [Test]
        public void C2_PositionFollowsTheLiveClockWhileTheBaseStands()
        {
            // base Scaled stands at 0.5 while only Unscaled items live — C fires at nominal 0.8, ν goes on
            var rig = new CursorRig(Compile(Fx(duration: 0.1f), Clock(Fx(StartRule.AfterPrevious, duration: 1f), TimelineClock.Unscaled), Clock(Instant(delay: 0.7f), TimelineClock.Unscaled)));
            rig.Begin();
            Assert.That(Fmt(rig.Tick(0.1)), Is.EqualTo(new[] { "Ended 0#0@100000", "Started 1#0@100000" }));
            Assert.That(rig.Tick(0.4), Is.Empty);
            Assert.That(Fmt(rig.Tick(0, 0.4)), Is.EqualTo(new[] { "Fired 2#0@800000" }));
            Assert.That(rig.Cursor.IsOffNominal, Is.False);
            Assert.That(rig.Cursor.Position, Is.EqualTo(9 * S / 10));
        }

        [Test]
        public void C2_LiveClockChangingInsideATickLeavesTheNominalAxis()
        {
            // U2 lives throughout; S lives only for f in [0.5, 0.7] of the tick — at a different rate
            var p = Compile(Clock(Fx(duration: 2f), TimelineClock.Unscaled), Clock(Fx(duration: 0.5f), TimelineClock.Unscaled), Fx(StartRule.AfterPrevious, duration: 0.1f));
            var rig = new CursorRig(p);
            rig.Begin();
            Assert.That(Kinds(rig.Tick(0.5, 1.0)), Is.EqualTo(new[] { "Ended 1#0", "Started 2#0", "Ended 2#0" }));
            Assert.That(rig.Cursor.IsOffNominal, Is.True);

            var control = new CursorRig(p);
            control.Begin();
            control.Tick(1.0, 1.0);
            Assert.That(control.Cursor.IsOffNominal, Is.False);
        }

        [Test]
        public void C2_LoopStartDelayCountsOnItsClock()
        {
            var rig = new CursorRig(Compile(Clock(LoopStart(delay: 0.5f), TimelineClock.Unscaled), Fx(duration: 0.1f), LoopEnd(1)));
            Assert.That(rig.Begin(), Is.Empty);
            Assert.That(Fmt(rig.Tick(0, 0.5)), Is.EqualTo(new[] { "Started 1#0@500000" }));
            Assert.That(rig.Tick(0, 0.5), Is.Empty);
            Assert.That(Fmt(rig.Tick(0.1, 0)), Is.EqualTo(new[] { "Ended 1#0@600000", "LoopIteration b0#0@600000", "Finished@600000" }));
            Assert.That(rig.Cursor.IsOffNominal, Is.False);
        }

        [Test]
        public void C2_LoopGapCountsOnTheLoopEndsClock()
        {
            var rig = new CursorRig(Compile(LoopStart(), Fx(duration: 0.1f), Clock(LoopEnd(2, gap: 0.5f), TimelineClock.Unscaled)));
            Assert.That(Fmt(rig.Begin()), Is.EqualTo(new[] { "Started 1#0@0" }));
            Assert.That(Fmt(rig.Tick(0.3, 0)), Is.EqualTo(new[] { "Ended 1#0@100000", "LoopIteration b0#0@100000" }));
            Assert.That(Fmt(rig.Tick(0, 0.5)), Is.EqualTo(new[] { "Started 1#1@600000" }));
            Assert.That(rig.Cursor.IsOffNominal, Is.False);
        }

        [Test]
        public void C2_RootDelayCountsOnThePositionClock()
        {
            // one clock (Unscaled) with a Scaled base — ν's clock is Unscaled
            var single = new CursorRig(Compile(Clock(Fx(duration: 0.5f), TimelineClock.Unscaled)), rootDelay: 0.25f);
            Assert.That(single.Begin(), Is.Empty);
            Assert.That(single.Cursor.PositionClock, Is.EqualTo(TimelineClock.Unscaled));
            Assert.That(Fmt(single.Tick(0, 0.25)), Is.EqualTo(new[] { "Started 0#0@250000" }));

            // mixed clocks — the base clock
            var mixed = new CursorRig(Compile(Fx(duration: 0.5f), Clock(Fx(duration: 0.5f), TimelineClock.Unscaled)), rootDelay: 0.25f);
            Assert.That(mixed.Begin(), Is.Empty);
            Assert.That(mixed.Cursor.PositionClock, Is.EqualTo(TimelineClock.Scaled));
            Assert.That(mixed.Tick(0, 0.25), Is.Empty);
            Assert.That(Fmt(mixed.Tick(0.25, 0)), Is.EqualTo(new[] { "Started 0#0@250000", "Started 1#0@250000" }));
        }

        [Test]
        public void C2_HoldDelayCountsOnTheHoldsClock()
        {
            // B = After all + Hold 0.3 (Unscaled) + own 0.1 (Scaled) — two stages
            var rig = new CursorRig(Compile(Fx(duration: 0.2f), Clock(Hold(0.3f), TimelineClock.Unscaled), Fx(delay: 0.1f, duration: 0.1f)));
            Assert.That(Fmt(rig.Begin()), Is.EqualTo(new[] { "Started 0#0@0" }));
            Assert.That(Fmt(rig.Tick(0.2, 0.2)), Is.EqualTo(new[] { "Ended 0#0@200000" }));
            Assert.That(rig.Tick(0, 0.3), Is.Empty);
            Assert.That(Fmt(rig.Tick(0.1, 0)), Is.EqualTo(new[] { "Started 2#0@600000" }));
            Assert.That(Fmt(rig.Tick(0.1, 0.1)), Is.EqualTo(new[] { "Ended 2#0@700000", "Finished@700000" }));
            Assert.That(rig.Cursor.IsOffNominal, Is.False);
        }

        [Test]
        public void C2_TruncatedBackgroundOffNominalCarriesItsRealElapsed()
        {
            // the forced end's T is the background's own elapsed time
            var rig = new CursorRig(Compile(Fx(duration: 1f), Background(Clock(Fx(duration: 5f), TimelineClock.Unscaled))));
            rig.Begin();
            rig.Tick(0.5, 0.5);
            rig.Tick(0, 1.0);
            Assert.That(rig.Cursor.IsOffNominal, Is.True);
            var events = rig.Tick(0.5, 0.5);
            Assert.That(Kinds(events), Is.EqualTo(new[] { "Ended 0#0", "Ended 1#0", "Finished" }));
            Assert.That(events[1].Truncated, Is.True);
            Assert.That(events[1].Time - events[1].OccurrenceStart, Is.EqualTo(2 * S));
        }

        [Test]
        public void C2_LongTickEqualsTheProportionalSplit()
        {
            // over 2^31 − 1 µs: chunks of the tick move every clock by the same fraction
            var p = Compile(Clock(Fx(duration: 1000f), TimelineClock.Unscaled), Instant(StartRule.AfterPrevious), Fx(StartRule.AfterPrevious, duration: 500f), Clock(Instant(StartRule.AfterPrevious), TimelineClock.Unscaled));
            var whole = new CursorRig(p);
            whole.Begin();
            var one = whole.Tick(3000, 4500);
            var split = new CursorRig(p);
            split.Begin();
            var three = split.Tick(1000, 1500);
            three.AddRange(split.Tick(1000, 1500));
            three.AddRange(split.Tick(1000, 1500));
            Assert.That(one, Is.EqualTo(three));
            Assert.That(Kinds(one), Is.EqualTo(new[] { "Ended 0#0", "Fired 1#0", "Started 2#0", "Ended 2#0", "Fired 3#0", "Finished" }));
        }

        // ---- a regression case ----

        // an item repeat's start times and the total take the same scale path.
        [Test]
        public void Reg24_ItemRepeatTimesMatchTheResolveAtNonUnitScales()
        {
            var cases = new (float global, float item)[] { (2f, 1f), (1.3f, 1f), (1f, 1.37f), (1.3f, 0.77f) };
            foreach (var (global, item) in cases)
            {
                var rig = new CursorRig(Compile(Repeat(Fx(duration: 0.3f), 3, gap: 0.2f), Fx(StartRule.AfterPrevious, duration: 0.1f)), itemScale: new[] { item, 1f }, globalScale: global);
                var events = rig.Begin();
                for (int i = 0; i < 400 && !rig.Cursor.IsFinished; i++)
                    events.AddRange(rig.Tick(1.0 / 64));
                var starts = events.FindAll(e => e.Kind == TimelineEventKind.Started && e.Item == 0);
                long period = rig.S.R.ItemDuration(0) + rig.S.R.BlockGap(0);
                Assert.That(starts.Count, Is.EqualTo(4), $"scale {global} × {item}");
                for (int k = 0; k < starts.Count; k++)
                    Assert.That(starts[k].Time, Is.EqualTo(k * period), $"scale {global} × {item} start {k}");
                var finished = events.Find(e => e.Kind == TimelineEventKind.Finished);
                Assert.That(finished.Time, Is.EqualTo(rig.S.R.Total), $"scale {global} × {item} total");
            }
        }

        // ---- completion and rebuild ----

        [Test]
        public void C5_CompletionOnTheNominalAxisIsTheSweepToTheEnd()
        {
            var rig = new CursorRig(Compile(Instant(), Fx(duration: 0.5f), LoopStart(StartRule.AfterPrevious), Instant(), Fx(duration: 0.25f), LoopEnd(2, gap: 0.25f), Background(Fx(duration: 5f))));
            rig.Begin();
            rig.Tick(0.3);
            TimelinePropertyAssert.Events(rig.S.Forward(rig.TauS, rig.S.R.Total), rig.Complete(), "completion");
            Assert.That(rig.Cursor.IsFinished, Is.True);
        }

        [Test]
        public void C5_CompletionOffTheNominalAxisRunsEachRemainingEventOnce()
        {
            // the hit stop example, then an instant 0.5 after the particle
            var rig = new CursorRig(Compile(Fx(duration: 0.3f), Clock(Fx(duration: 0.1f), TimelineClock.Unscaled), Instant(StartRule.AfterPrevious), Instant(delay: 0.5f)));
            rig.Begin();
            Assert.That(Kinds(rig.Tick(0, 0.1)), Is.EqualTo(new[] { "Ended 1#0", "Fired 2#0" }));
            Assert.That(rig.Cursor.IsOffNominal, Is.True);
            Assert.That(Kinds(rig.Complete()), Is.EqualTo(new[] { "Ended 0#0", "Fired 3#0", "Finished" }));
        }

        [Test]
        public void C5_RebuildSeedsAHoldOnItsOwnClock()
        {
            // A = [0, 1] · Hold 0.5 (Unscaled) · B 0.2 — at 1.2 the Hold has not elapsed
            var p = Compile(Fx(duration: 1f), Clock(Hold(0.5f), TimelineClock.Unscaled), Fx(duration: 0.2f));
            var fresh = new CursorRig(p);
            fresh.Begin();
            fresh.Tick(0.6);
            fresh.Tick(0.6);
            var expected = fresh.Tick(0.3);

            var rebuilt = new CursorRig(p);
            rebuilt.Begin();
            rebuilt.Tick(0.1);
            rebuilt.Rebuild(12 * S / 10);
            Assert.That(rebuilt.Cursor.Position, Is.EqualTo(12 * S / 10));
            Assert.That(Fmt(rebuilt.Tick(0.3)), Is.EqualTo(Fmt(expected)));
            Assert.That(Fmt(expected), Is.EqualTo(new[] { "Started 2#0@1500000" }));

            // the rebuilt Hold waits on its own clock: Scaled standing still does not stop it
            var held = new CursorRig(p);
            held.Begin();
            held.Rebuild(12 * S / 10);
            Assert.That(Fmt(held.Tick(0, 0.3)), Is.EqualTo(new[] { "Started 2#0@1500000" }));
        }
    }

    static class TimelinePropertyAssert
    {
        public static void Events(List<TimelineEvent> expected, List<TimelineEvent> actual, string where)
            => TimelineCursorPropertyTests.AssertEvents(expected, actual, where);
    }
}
