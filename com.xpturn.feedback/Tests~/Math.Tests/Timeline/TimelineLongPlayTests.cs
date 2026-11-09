using System;
using System.Numerics;
using NUnit.Framework;
using static xpTURN.Feedback.Tests.Timeline.TimelineDescs;

namespace xpTURN.Feedback.Tests.Timeline
{
    // long plays — the accumulated clock, marker saturation, large N × period.
    // Chunked ticks over 50 minutes are covered by TimelineCursorTests (C2_LongTick*) and the C6_* properties.
    public sealed class TimelineLongPlayTests
    {
        const long Inf = TimelineTime.Infinite;

        static string[] Fmt(System.Collections.Generic.List<TimelineEvent> events) => SamplerRig.Format(events);

        // 10^6 ticks of 1/60f (about 4.6 hours) — within the range where the double sum is exact (about 88 days)
        [Test]
        public void T10_MillionTicksAt60HzStayOnTheExactMicrosecond()
        {
            var rig = new CursorRig(Compile(LoopStart(), Fx(duration: 1f), LoopEnd(0)));
            var cursor = rig.Cursor;
            cursor.Begin(TimelineClock.Scaled);
            while (cursor.TryNext(out _))
            {
            }
            cursor.EndAdvance();

            float dt = 1f / 60f;
            const int ticks = 1_000_000;
            long loops = 0, misplaced = 0;
            for (int i = 0; i < ticks; i++)
            {
                cursor.BeginAdvance(dt, dt);
                while (cursor.TryNext(out var e))
                {
                    if (e.Kind != TimelineEventKind.LoopIteration)
                        continue;
                    loops++;
                    if (e.Time != loops * S)
                        misplaced++;
                }
                cursor.EndAdvance();
            }
            Assert.That(loops, Is.EqualTo(16_666));
            Assert.That(misplaced, Is.Zero, "every iteration boundary is k × the period");

            // the exact value from the float's bits (a (decimal)(double) cast keeps only 15 significant digits)
            int bits = BitConverter.SingleToInt32Bits(dt);
            int exponent = ((bits >> 23) & 0xFF) - 150;
            BigInteger mantissa = (bits & 0x7FFFFF) | 0x800000;
            BigInteger numerator = ticks * mantissa * 1_000_000;
            BigInteger denominator = BigInteger.Pow(2, -exponent);
            BigInteger micros = BigInteger.DivRem(numerator, denominator, out var remainder);
            Assert.That(remainder * 2, Is.Not.EqualTo(denominator), "a tie would need the rounding rule");
            if (remainder * 2 > denominator)
                micros += 1;
            Assert.That(cursor.Position, Is.EqualTo((long)micros));
        }

        // an open wait ends at the marker; what follows waits for it and nothing wraps
        [Test]
        public void T10_OpenWaitSaturatesWithoutWrapping()
        {
            var rig = new CursorRig(Compile(Signal(), Fx(StartRule.AfterPrevious, delay: 1f, duration: 1f)));
            Assert.That(Fmt(rig.Begin()), Is.EqualTo(new[] { "Started 0#0@0", "SignalWaitBegan 0#0@0" }));
            long last = rig.Cursor.Position;
            for (int i = 0; i < 1000; i++)
            {
                Assert.That(rig.Tick(1e6), Is.Empty, $"tick {i}");
                long now = rig.Cursor.Position;
                Assert.That(now, Is.GreaterThan(last), $"tick {i}");
                last = now;
            }
            long end = 1_000_000_000L * S;
            Assert.That(last, Is.EqualTo(end));

            Assert.That(rig.Cursor.Signal(), Is.EqualTo(1));
            Assert.That(Fmt(rig.Tick(1.0)), Is.EqualTo(new[] { $"Ended 0#0@{end}", $"Started 1#0@{end + S}" }));
            Assert.That(Fmt(rig.Tick(1.0)), Is.EqualTo(new[] { $"Ended 1#0@{end + 2 * S}", $"Finished@{end + 2 * S}" }));
            Assert.That(rig.Cursor.IsOffNominal, Is.False, "an open wait has no timeout to end early");
        }

        // a finite play whose total saturates is as infinite as an infinite structure's
        [Test]
        public void T10_SaturatedTotalIsNotSeekable()
        {
            var loop = new SamplerRig(Compile(LoopStart(), Fx(duration: 7200f), LoopEnd(int.MaxValue)));
            Assert.That(loop.P.IsInfinite, Is.False, "the structure is finite");
            Assert.That(loop.R.Total, Is.EqualTo(Inf));
            Assert.That(loop.R.TotalSeconds, Is.EqualTo(double.PositiveInfinity));
            Assert.That(loop.R.IsSeekable, Is.False);

            var sum = new SamplerRig(Compile(Fx(duration: 5e12f), Fx(StartRule.AfterPrevious, duration: 5e12f)));
            Assert.That(sum.R.Total, Is.EqualTo(Inf));
            Assert.That(sum.R.IsSeekable, Is.False);
        }

        // just below saturation the play stays seekable — samples and the reverse head do not wrap
        [Test]
        public void T10_NearLimitTotalSamplesAndReversesWithoutWrapping()
        {
            var rig = new SamplerRig(Compile(LoopStart(), Fx(duration: 3600f), LoopEnd(int.MaxValue)));
            long hour = 3600 * S;
            long total = hour * int.MaxValue;
            Assert.That(rig.R.Total, Is.EqualTo(total));
            Assert.That(rig.R.IsSeekable, Is.True);

            var s = rig.At(total - 1);
            Assert.That(s.Stage(1), Is.EqualTo(TimelineStage.Running));
            Assert.That(s.Occurrence(1), Is.EqualTo(int.MaxValue - 1));
            Assert.That(s.NodeStart(rig.P.NodeOfItem(1)), Is.EqualTo(total - hour));

            rig.Sampler.BeginReverse();
            Assert.That(Fmt(rig.Drain()), Is.EqualTo(new[]
            {
                $"~LoopIteration b0#{int.MaxValue - 1}@{total}", $"~Started 1#{int.MaxValue - 1}@{total}",
            }));
            rig.Sampler.AdvanceReverse(1.0);
            Assert.That(rig.Drain(), Is.Empty);
            Assert.That(rig.Sampler.ReversePosition, Is.EqualTo(total - S));
            var back = rig.At(total - S, true);
            Assert.That(back.Stage(1), Is.EqualTo(TimelineStage.Running));
            Assert.That(back.LocalTime(1), Is.EqualTo((float)((double)(hour - S) / hour)));
        }

        // the cursor never looks at the total — a saturated play runs forward causally
        [Test]
        public void T10_CursorRunsASaturatedPlayForward()
        {
            var rig = new CursorRig(Compile(LoopStart(), Fx(duration: 7200f), LoopEnd(int.MaxValue)));
            Assert.That(Fmt(rig.Begin()), Is.EqualTo(new[] { "Started 1#0@0" }));
            Assert.That(Fmt(rig.Tick(7200)), Is.EqualTo(new[] { "Ended 1#0@7200000000", "LoopIteration b0#0@7200000000", "Started 1#1@7200000000" }));
            Assert.That(rig.Cursor.IsFinished, Is.False);
        }
    }
}
