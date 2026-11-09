using NUnit.Framework;
using static xpTURN.Feedback.Tests.Timeline.TimelineDescs;

namespace xpTURN.Feedback.Tests.Timeline
{
    // the open signal waits of a play: the waits Signal() would end at that moment, and how many of them have no timeout
    sealed class TimelineSignalWaitTests
    {
        static TimelineItemDesc After(TimelineItemDesc d)
        {
            d.Rule = StartRule.AfterPrevious;
            return d;
        }

        [Test]
        public void W1_BeforeDuringAndRightAfterTheSignal()
        {
            var rig = new CursorRig(Compile(Fx(duration: 0.5f), After(Signal(timeout: 1f)), Instant(StartRule.AfterPrevious)));
            rig.Begin();
            Assert.That(rig.Cursor.OpenSignalWaits(out int untimed), Is.EqualTo(0));
            Assert.That(untimed, Is.EqualTo(0));
            rig.Tick(0.25);
            Assert.That(rig.Cursor.OpenSignalWaits(), Is.EqualTo(0));
            rig.Tick(0.25);
            Assert.That(rig.Cursor.OpenSignalWaits(out untimed), Is.EqualTo(1), "the wait began at 0.5");
            Assert.That(untimed, Is.EqualTo(0));
            Assert.That(rig.Cursor.Signal(), Is.EqualTo(1));
            Assert.That(rig.Cursor.OpenSignalWaits(), Is.EqualTo(0), "closed by the signal itself, before the tick that ends it");
            rig.Tick(0.1);
            Assert.That(rig.Cursor.OpenSignalWaits(), Is.EqualTo(0));
            Assert.That(rig.Cursor.IsFinished, Is.True);
        }

        [Test]
        public void W2_TheTimeoutClosesTheWait()
        {
            var rig = new CursorRig(Compile(Signal(timeout: 0.5f), Instant(StartRule.AfterPrevious)));
            rig.Begin();
            Assert.That(rig.Cursor.OpenSignalWaits(out int untimed), Is.EqualTo(1));
            Assert.That(untimed, Is.EqualTo(0));
            rig.Tick(0.25);
            Assert.That(rig.Cursor.OpenSignalWaits(), Is.EqualTo(1));
            rig.Tick(0.25);
            Assert.That(rig.Cursor.OpenSignalWaits(), Is.EqualTo(0));
            Assert.That(rig.Cursor.IsFinished, Is.True);
        }

        [Test]
        public void W3_AWaitWithoutATimeoutStaysOpenUntilTheSignal()
        {
            var rig = new CursorRig(Compile(Signal(), Instant(StartRule.AfterPrevious)));
            rig.Begin();
            Assert.That(rig.Cursor.OpenSignalWaits(out int untimed), Is.EqualTo(1));
            Assert.That(untimed, Is.EqualTo(1));
            rig.Tick(5.0);
            Assert.That(rig.Cursor.OpenSignalWaits(out untimed), Is.EqualTo(1));
            Assert.That(untimed, Is.EqualTo(1));
            Assert.That(rig.Cursor.Signal(), Is.EqualTo(1));
            Assert.That(rig.Cursor.OpenSignalWaits(out untimed), Is.EqualTo(0));
            Assert.That(untimed, Is.EqualTo(0));
            rig.Tick(0.1);
            Assert.That(rig.Cursor.IsFinished, Is.True);
        }

        [Test]
        public void W4_AnAbortedOrFinishedPlayHasNone()
        {
            var aborted = new CursorRig(Compile(Signal(), Instant(StartRule.AfterPrevious)));
            aborted.Begin();
            Assert.That(aborted.Cursor.OpenSignalWaits(), Is.EqualTo(1));
            aborted.Cursor.Abort();
            Assert.That(aborted.Cursor.OpenSignalWaits(out int untimed), Is.EqualTo(0));
            Assert.That(untimed, Is.EqualTo(0));
            Assert.That(aborted.Cursor.Signal(), Is.EqualTo(0));

            var finished = new CursorRig(Compile(Signal(timeout: 0.25f)));
            finished.Begin();
            finished.Tick(0.5);
            Assert.That(finished.Cursor.IsFinished, Is.True);
            Assert.That(finished.Cursor.OpenSignalWaits(), Is.EqualTo(0));

            var completed = new CursorRig(Compile(Signal(timeout: 2f), Instant(StartRule.AfterPrevious)));
            completed.Begin();
            completed.Complete();
            Assert.That(completed.Cursor.OpenSignalWaits(), Is.EqualTo(0));
        }

        [Test]
        public void W5_ConsecutiveWaitsOpenOneAtATime()
        {
            // the second wait starts at the first one's end
            var rig = new CursorRig(Compile(Signal(timeout: 1f), Signal(timeout: 1f), Instant(StartRule.AfterPrevious)));
            rig.Begin();
            Assert.That(rig.Cursor.OpenSignalWaits(), Is.EqualTo(1));
            Assert.That(rig.Cursor.Signal(), Is.EqualTo(1));
            Assert.That(rig.Cursor.OpenSignalWaits(), Is.EqualTo(0), "the second one opens at the next tick");
            rig.Tick(0.1);
            Assert.That(rig.Cursor.OpenSignalWaits(), Is.EqualTo(1));
            Assert.That(rig.Cursor.Signal(), Is.EqualTo(1));
            rig.Tick(0.1);
            Assert.That(rig.Cursor.OpenSignalWaits(), Is.EqualTo(0));
            Assert.That(rig.Cursor.IsFinished, Is.True);
        }

        [Test]
        public void W6_OverlappingWaitsCountTogether()
        {
            // a user block is not a wait: the item after it starts with it, beside the wait inside it
            var rig = new CursorRig(Compile(LoopStart(), Signal(timeout: 1f), LoopEnd(2), Signal()));
            rig.Begin();
            Assert.That(rig.Cursor.OpenSignalWaits(out int untimed), Is.EqualTo(2));
            Assert.That(untimed, Is.EqualTo(1));
            Assert.That(rig.Cursor.Signal(), Is.EqualTo(2));
            Assert.That(rig.Cursor.OpenSignalWaits(out untimed), Is.EqualTo(0));
            Assert.That(untimed, Is.EqualTo(0));
        }

        [Test]
        public void W7_AWaitInsideALoopOpensOnEveryIteration()
        {
            var rig = new CursorRig(Compile(LoopStart(), Signal(timeout: 0.1f), LoopEnd(2), Instant(StartRule.AfterPrevious)));
            rig.Begin();
            Assert.That(rig.Cursor.OpenSignalWaits(), Is.EqualTo(1));
            rig.Tick(0.05);
            Assert.That(rig.Cursor.OpenSignalWaits(), Is.EqualTo(1));
            rig.Tick(0.1);
            Assert.That(rig.Cursor.OpenSignalWaits(), Is.EqualTo(1), "the second iteration's wait");
            rig.Tick(0.1);
            Assert.That(rig.Cursor.OpenSignalWaits(), Is.EqualTo(0));
            Assert.That(rig.Cursor.IsFinished, Is.True);
        }

        // the count is the number Signal() ends at that moment — every program and time here, both between ticks
        [Test]
        public void W8_TheCountIsWhatTheSignalEnds()
        {
            TimelineItemDesc[][] programs =
            {
                new[] { Fx(duration: 0.5f), After(Signal(timeout: 1f)), Instant(StartRule.AfterPrevious) },
                new[] { Signal(timeout: 0.5f), Instant(StartRule.AfterPrevious) },
                new[] { Signal(), Instant(StartRule.AfterPrevious) },
                new[] { Signal(timeout: 1f), Signal(timeout: 1f), Instant(StartRule.AfterPrevious) },
                new[] { LoopStart(), Signal(timeout: 1f), LoopEnd(2), Signal() },
                new[] { LoopStart(), Signal(timeout: 0.1f), LoopEnd(2), Instant(StartRule.AfterPrevious) },
            };
            double[] times = { 0, 0.05, 0.25, 0.5, 0.75, 1.25, 3 };
            int compared = 0, open = 0;
            foreach (TimelineItemDesc[] program in programs)
            {
                TimelineProgram p = Compile(program);
                foreach (double t in times)
                {
                    var rig = new CursorRig(p);
                    rig.Begin();
                    if (t > 0)
                        rig.Tick(t);
                    int count = rig.Cursor.OpenSignalWaits();
                    Assert.That(rig.Cursor.Signal(), Is.EqualTo(count), $"program {compared / times.Length} at {t}");
                    compared++;
                    open += count;
                }
            }
            Assert.That(open, Is.GreaterThan(0), "some states had open waits");
        }
    }
}
