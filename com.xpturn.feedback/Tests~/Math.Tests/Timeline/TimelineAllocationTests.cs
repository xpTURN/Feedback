using System;
using NUnit.Framework;
using static xpTURN.Feedback.Tests.Timeline.TimelineDescs;

namespace xpTURN.Feedback.Tests.Timeline
{
    // no allocation after warm-up. CoreCLR's 0 B is a necessary condition only — Mono · IL2CPP are the player's
    // allocation gate's. Inside the measured run: product API and sums only.
    public sealed class TimelineAllocationTests
    {
        static readonly object[] Escaped = new object[1000];

        // blocks · repeats · background (repeat · zero period) · a Hold on another clock · a signal wait ·
        // the start delay (rigs) · skips and item scales (rigs)
        static TimelineProgram Seekable() => Compile(
            Fx(duration: 1f),
            LoopStart(StartRule.AfterPrevious), Instant(), Fx(duration: 0.25f), Clock(LoopEnd(3, gap: 0.25f), TimelineClock.Unscaled),
            LoopStart(), LoopStart(), Instant(), LoopEnd(3), LoopEnd(2),
            Clock(Hold(0.2f), TimelineClock.Unscaled), Signal(timeout: 1f),
            Clock(Fx(duration: 0.3f), TimelineClock.Unscaled),
            Background(Repeat(Clock(Fx(duration: 0.4f), TimelineClock.Unscaled), -1)),
            Background(Repeat(Instant(delay: 0.5f), 2)), Background(Repeat(Instant(), 3)), Instant());

        // a zero-period infinite block — the cursor's deferral (not seekable)
        static TimelineProgram Looping() => Compile(Fx(duration: 0.5f), LoopStart(), Instant(), LoopEnd(0));

        static float[] Scales(TimelineProgram p)
        {
            var scale = new float[p.ItemCount];
            for (int i = 0; i < scale.Length; i++)
                scale[i] = 0.5f + 0.25f * (i % 4);
            return scale;
        }

        static bool[] Skips(TimelineProgram p)
        {
            var skip = new bool[p.ItemCount];
            for (int i = 0; i < skip.Length; i++)
                skip[i] = i % 5 == 3;
            return skip;
        }

        // two warm-up runs, the third measured; the same checksum shows the measured run did the same work
        static long Allocated(Func<long> run)
        {
            long first = run();
            run();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long last = run();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.WriteLine($"{allocated} B (checksum {last}) on .NET {Environment.Version}");
            Assert.That(last, Is.EqualTo(first), "the measured run repeats the warm-up");
            return allocated;
        }

        static long Fold(in TimelineEvent e) => (long)e.Kind + e.Item + e.Block + e.Occurrence + e.Time + e.OccurrenceStart;

        static long Drain(TimelineSampler s)
        {
            long sum = 0;
            while (s.TryNext(out var e))
                sum += Fold(e);
            return sum;
        }

        static long Drain(TimelineCursor c)
        {
            long sum = 0;
            while (c.TryNext(out var e))
                sum += Fold(e);
            return sum;
        }

        static long Query(TimelineSample s, TimelineProgram p)
        {
            long sum = 0;
            for (int item = 0; item < p.ItemCount; item++)
            {
                if (p.NodeOfItem(item) < 0)
                    continue;
                sum += (long)s.Stage(item) + s.Occurrence(item) + BitConverter.SingleToInt32Bits(s.LocalTime(item)) + BitConverter.DoubleToInt64Bits(s.Seconds(item));
            }
            return sum;
        }

        static long Query(TimelineCursor c, TimelineProgram p)
        {
            long sum = c.Position + (c.IsOffNominal ? 1 : 0) + (long)c.PositionClock;
            for (int item = 0; item < p.ItemCount; item++)
                sum += (long)c.Stage(item) + c.Occurrence(item) + BitConverter.SingleToInt32Bits(c.LocalTime(item)) + BitConverter.DoubleToInt64Bits(c.Seconds(item));
            return sum;
        }

        // positive control: 1,000 × new object() = 24,000 B on 64-bit. Escaping — an unused one counts 23,976 B
        [Test]
        public void T9_CounterSeesEscapingAllocations()
        {
            Assert.That(Allocated(() =>
            {
                for (int i = 0; i < Escaped.Length; i++)
                    Escaped[i] = new object();
                return Escaped.Length;
            }), Is.EqualTo(24_000));
        }

        [Test]
        public void T9_ResolveAllocatesNothing()
        {
            var programs = new[] { Seekable(), Looping() };
            var resolved = new[] { new ResolvedTimeline(programs[0]), new ResolvedTimeline(programs[1]) };
            var scales = new[] { Scales(programs[0]), Scales(programs[1]) };
            var skips = new[] { Skips(programs[0]), Skips(programs[1]) };
            Assert.That(Allocated(() =>
            {
                long sum = 0;
                for (int i = 0; i < 100; i++)
                {
                    for (int k = 0; k < programs.Length; k++)
                    {
                        programs[k].Resolve(default, default, 1f, 0f, resolved[k]);
                        sum += resolved[k].Total;
                        programs[k].Resolve(scales[k], skips[k], 1.25f, 0.25f, resolved[k]);
                        sum += resolved[k].Total + (resolved[k].IsSeekable ? 1 : 0);
                    }
                }
                return sum;
            }), Is.Zero);
        }

        static long RunSampler(SamplerRig rig)
        {
            var s = rig.Sampler;
            long total = rig.R.Total, sum = 0;
            for (long v = -1; v <= total + 1; v += total / 7 + 1)
            {
                s.Sample(v, false, rig.Sample);
                sum += Query(rig.Sample, rig.P);
                s.Sample(v, true, rig.Sample);
                sum += Query(rig.Sample, rig.P);
            }
            s.SweepForward(-1, total);
            sum += Drain(s);
            s.SweepReverse(rig.R.RootDelay, total + 1);
            sum += Drain(s);

            // seeks of a forward play — ahead · back
            sum += s.SeekBegin(0, total / 2, false);
            sum += Drain(s);
            sum += s.SeekBegin(total / 2, 0, false);
            sum += Drain(s);

            // the reverse play head — seeks back · ahead move it, then it runs to the start
            s.BeginReverse();
            sum += Drain(s);
            s.AdvanceReverse(0.5);
            sum += Drain(s);
            long head = s.ReversePosition;
            sum += s.SeekBegin(head, head / 3, true);
            sum += Drain(s);
            sum += s.SeekBegin(head / 3, head, true);
            sum += Drain(s);
            while (!s.IsReverseFinished)
            {
                s.AdvanceReverse(1.0 / 64);
                sum += Drain(s) + s.ReversePosition;
            }
            return sum;
        }

        [Test]
        public void T9_SamplerAllocatesNothing()
        {
            var p = Seekable();
            var rig = new SamplerRig(p, Scales(p), Skips(p), 1f, 0.25f);
            Assert.That(Allocated(() => RunSampler(rig)), Is.Zero);
        }

        static long RunCursor(CursorRig rig, CursorRig looping, ref int inTick, ref int outOfTick)
        {
            var c = rig.Cursor;
            var p = rig.S.P;
            long total = rig.S.R.Total, sum = 0;

            // scaled base, two clocks — a signal while handling events
            c.Begin(TimelineClock.Scaled);
            sum += Drain(c);
            c.EndAdvance();
            for (int i = 0; i < 60 && !c.IsFinished; i++)
            {
                c.BeginAdvance(i % 4 == 0 ? 0.0 : 0.1, 0.15);
                while (c.TryNext(out var e))
                {
                    sum += Fold(e);
                    inTick += c.Signal();
                }
                c.EndAdvance();
                sum += Query(c, p);
            }

            // unscaled base — a signal between ticks
            c.Begin(TimelineClock.Unscaled);
            sum += Drain(c);
            c.EndAdvance();
            for (int i = 0; i < 60 && !c.IsFinished; i++)
            {
                c.BeginAdvance(0.12, i % 3 == 0 ? 0.0 : 0.1);
                sum += Drain(c);
                c.EndAdvance();
                sum += Query(c, p);
                outOfTick += c.Signal();
            }

            // a seek, a stop during event handling, a seek and a chunked tick (over 2^31 − 1 µs)
            c.Rebuild(total / 3);
            c.BeginAdvance(0.05, 0.05);
            if (c.TryNext(out var first))
                sum += Fold(first);
            c.Abort();
            c.Rebuild(total / 2);
            c.BeginAdvance(3000.0, 4500.0);
            sum += Drain(c);
            c.EndAdvance();
            sum += Query(c, p);

            // a skip — event-driven completion
            c.Begin(TimelineClock.Scaled);
            sum += Drain(c);
            c.EndAdvance();
            c.BeginAdvance(0.1, 0.1);
            sum += Drain(c);
            c.EndAdvance();
            c.BeginComplete();
            sum += Drain(c);
            c.EndAdvance();
            sum += Query(c, p);

            var l = looping.Cursor;
            l.Begin(TimelineClock.Scaled);
            sum += Drain(l);
            l.EndAdvance();
            for (int i = 0; i < 8; i++)
            {
                l.BeginAdvance(i % 2 == 0 ? 0.0 : 0.02, 0.02);
                sum += Drain(l);
                l.EndAdvance();
                sum += Query(l, looping.S.P);
            }
            return sum;
        }

        [Test]
        public void T9_CursorAllocatesNothing()
        {
            var p = Seekable();
            var rig = new CursorRig(p, Scales(p), Skips(p), 1f, 0.25f);
            var looping = new CursorRig(Looping());
            int inTick = 0, outOfTick = 0;
            Assert.That(Allocated(() =>
            {
                inTick = outOfTick = 0;
                return RunCursor(rig, looping, ref inTick, ref outOfTick);
            }), Is.Zero);
            Assert.That((inTick, outOfTick), Is.EqualTo((1, 1)), "each signal path ended a running wait");
        }
    }
}
