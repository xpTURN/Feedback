using System.Collections.Generic;
using NUnit.Framework;
using static xpTURN.Feedback.Tests.Timeline.TimelineDescs;

namespace xpTURN.Feedback.Tests.Timeline
{
    // an open effect item (its binding ends it; Duration is its time limit or +inf), one node ended by its item (EndItem —
    // Signal's moments), and the queries measured from the natural end (the mirror of the forward ones)
    sealed class TimelineOpenItemTests
    {
        static string[] Fmt(List<TimelineEvent> events) => SamplerRig.Format(events);

        static TimelineItemDesc Open(float limit = float.PositiveInfinity, StartRule rule = StartRule.WithPrevious)
        {
            TimelineItemDesc d = Fx(rule, duration: limit);
            d.Open = true;
            return d;
        }

        [Test]
        public void O1_AnOpenEffectOpensTheProgram()
        {
            TimelineProgram untimed = Compile(Open(), Instant(StartRule.AfterPrevious));
            Assert.That((untimed.HasOpenItem, untimed.IsInfinite, untimed.IsSeekable), Is.EqualTo((true, true, false)), "untimed");
            Assert.That((untimed.ItemOpen(0), untimed.ItemOpen(1)), Is.EqualTo((true, false)), "the item only");

            TimelineProgram timed = Compile(Open(0.5f), Instant(StartRule.AfterPrevious));
            Assert.That((timed.HasOpenItem, timed.IsInfinite, timed.IsSeekable), Is.EqualTo((true, false, false)), "a time limit — open, not infinite");

            TimelineItemDesc wait = Wait(0.5f);
            wait.Open = true;
            TimelineProgram notEffect = Compile(wait, Fx());
            Assert.That((notEffect.HasOpenItem, notEffect.ItemOpen(0), notEffect.IsSeekable), Is.EqualTo((false, false, true)), "an effect only");
        }

        [Test]
        public void E1_EndItemBetweenTicksEndsThatNodeOnly()
        {
            var rig = new CursorRig(Compile(Open(), Open(), Instant(StartRule.AfterPrevious)));
            Assert.That(Fmt(rig.Begin()), Is.EqualTo(new[] { "Started 0#0@0", "Started 1#0@0" }));
            Assert.That(rig.Tick(0.25), Is.Empty);
            Assert.That(rig.Cursor.EndItem(0), Is.EqualTo(1));
            Assert.That(rig.Cursor.EndItem(0), Is.EqualTo(0), "once a run");
            Assert.That(Fmt(rig.Tick(0.25)), Is.EqualTo(new[] { "Ended 0#0@250000" }));
            Assert.That((rig.Cursor.Stage(0), rig.Cursor.Stage(1)), Is.EqualTo((TimelineStage.Done, TimelineStage.Running)));
            Assert.That(rig.Cursor.EndItem(1), Is.EqualTo(1));
            Assert.That(Fmt(rig.Tick(0.25)), Is.EqualTo(new[] { "Ended 1#0@500000", "Fired 2#0@500000", "Finished@500000" }));
            Assert.That(rig.Cursor.EndItem(1), Is.EqualTo(0), "a finished play");
        }

        [Test]
        public void E2_EndItemDuringEventHandlingEndsAtThatMoment()
        {
            var rig = new CursorRig(Compile(Fx(duration: 0.5f), Open(), Instant(StartRule.AfterPrevious)));
            Assert.That(Fmt(rig.Begin()), Is.EqualTo(new[] { "Started 0#0@0", "Started 1#0@0" }));
            rig.Cursor.BeginAdvance(1.0, 1.0);
            Assert.That(rig.Cursor.TryNext(out var first), Is.True);
            Assert.That(SamplerRig.Format(first), Is.EqualTo("Ended 0#0@500000"));
            Assert.That(rig.Cursor.EndItem(1), Is.EqualTo(1));
            Assert.That(Fmt(rig.Drain()), Is.EqualTo(new[] { "Ended 1#0@500000", "Fired 2#0@500000", "Finished@500000" }));
            rig.Cursor.EndAdvance();
        }

        [Test]
        public void E2b_EndItemInATickBeforeAnyEventHasSignalsMoment()
        {
            var open = new CursorRig(Compile(Open(), Instant(StartRule.AfterPrevious)));
            var signal = new CursorRig(Compile(Signal(), Instant(StartRule.AfterPrevious)));
            open.Begin();
            signal.Begin();
            open.Tick(0.25);
            signal.Tick(0.25);
            open.Cursor.BeginAdvance(0.5, 0.5);
            signal.Cursor.BeginAdvance(0.5, 0.5);
            Assert.That(open.Cursor.EndItem(0), Is.EqualTo(1));
            Assert.That(signal.Cursor.Signal(), Is.EqualTo(1));
            string[] a = Fmt(open.Drain()), b = Fmt(signal.Drain());
            open.Cursor.EndAdvance();
            signal.Cursor.EndAdvance();
            Assert.That(a, Is.EqualTo(new[] { "Ended 0#0@250000", "Fired 1#0@250000", "Finished@250000" }), "the tick's start");
            Assert.That(a, Is.EqualTo(b), "the signal's moments");
        }

        [Test]
        public void E3_APendingItemAndATimeLimit()
        {
            var pending = new CursorRig(Compile(Open(), Open(rule: StartRule.AfterPrevious)));
            pending.Begin();
            pending.Tick(0.25);
            Assert.That(pending.Cursor.EndItem(1), Is.EqualTo(0), "not running yet");
            Assert.That(pending.Cursor.EndItem(0), Is.EqualTo(1));
            Assert.That(Fmt(pending.Tick(0.25)), Is.EqualTo(new[] { "Ended 0#0@250000", "Started 1#0@250000" }));

            var limit = new CursorRig(Compile(Open(0.5f)));
            limit.Begin();
            Assert.That(Fmt(limit.Tick(0.5)), Is.EqualTo(new[] { "Ended 0#0@500000", "Finished@500000" }), "the time limit ends it");
            Assert.That(limit.Cursor.IsOffNominal, Is.False);

            var early = new CursorRig(Compile(Open(0.5f)));
            early.Begin();
            early.Tick(0.25);
            Assert.That(early.Cursor.EndItem(0), Is.EqualTo(1));
            Assert.That(Fmt(early.Tick(0.25)), Is.EqualTo(new[] { "Ended 0#0@250000", "Finished@250000" }));
            Assert.That(early.Cursor.IsOffNominal, Is.True, "ended before its limit — off the nominal axis");
        }

        [Test]
        public void F1_TheCursorFromTheEndMirrorsTheForwardQueries()
        {
            var rig = new CursorRig(Compile(Fx(duration: 0.5f), Fx(StartRule.AfterPrevious, duration: 0.4f), Open(rule: StartRule.AfterPrevious), Fx(StartRule.WithPrevious, duration: 0f)));
            rig.Begin();
            rig.Tick(0.2);
            Assert.That((rig.Cursor.LocalTime(0), rig.Cursor.LocalTimeFromEnd(0)), Is.EqualTo((0.4f, 0.6f)).Using(new FloatPair()), "running");
            Assert.That(rig.Cursor.SecondsFromEnd(0), Is.EqualTo(0.3).Within(1e-9));
            Assert.That((rig.Cursor.LocalTime(1), rig.Cursor.LocalTimeFromEnd(1), rig.Cursor.Seconds(1)), Is.EqualTo((0f, 1f, 0.0)), "pending");
            Assert.That(rig.Cursor.SecondsFromEnd(1), Is.EqualTo(0.4).Within(1e-9), "pending — the whole duration");
            for (int i = 0; i < 6; i++)
            {
                Assert.That(rig.Cursor.LocalTime(0) + rig.Cursor.LocalTimeFromEnd(0), Is.EqualTo(1f).Within(1e-6f), "the mirror at tick " + i);
                Assert.That(rig.Cursor.Seconds(0) + rig.Cursor.SecondsFromEnd(0), Is.EqualTo(0.5).Within(1e-9), "the seconds' mirror at tick " + i);
                rig.Tick(0.05);
            }
            rig.Tick(0.6);
            Assert.That((rig.Cursor.LocalTime(2), rig.Cursor.LocalTimeFromEnd(2)), Is.EqualTo((0f, 0f)), "an open item without a limit — 0 both ways");
            Assert.That((rig.Cursor.LocalTime(3), rig.Cursor.LocalTimeFromEnd(3)), Is.EqualTo((1f, 0f)), "a zero duration once begun");
        }

        [Test]
        public void F2_TheSampleFromTheEndMirrorsTheForwardQueries()
        {
            var s = new SamplerRig(Compile(Fx(duration: 0.5f), Fx(StartRule.AfterPrevious, duration: 0.4f)));
            foreach (bool reverse in new[] { false, true })
            {
                TimelineSample at = s.At(200_000, reverse);
                Assert.That((at.LocalTime(0), at.LocalTimeFromEnd(0)), Is.EqualTo((0.4f, 0.6f)).Using(new FloatPair()), "running · reverse " + reverse);
                Assert.That(at.SecondsFromEnd(0), Is.EqualTo(0.3).Within(1e-9));
                Assert.That((at.LocalTime(1), at.LocalTimeFromEnd(1)), Is.EqualTo((0f, 1f)), "pending · reverse " + reverse);
                at = s.At(700_000, reverse);
                Assert.That((at.LocalTime(1), at.LocalTimeFromEnd(1)), Is.EqualTo((0.5f, 0.5f)).Using(new FloatPair()), "the second · reverse " + reverse);
                Assert.That(at.SecondsFromEnd(1), Is.EqualTo(0.2).Within(1e-9));
            }
        }

        sealed class FloatPair : IEqualityComparer<(float, float)>
        {
            public bool Equals((float, float) a, (float, float) b) => System.Math.Abs(a.Item1 - b.Item1) <= 1e-6f && System.Math.Abs(a.Item2 - b.Item2) <= 1e-6f;

            public int GetHashCode((float, float) v) => 0;
        }
    }
}
