using System;
using System.Text;
using NUnit.Framework;
using static xpTURN.Feedback.Tests.Timeline.TimelineDescs;

namespace xpTURN.Feedback.Tests.Timeline
{
    // literal programs × literal dt, hashed per scene — catches unintended behavior changes. A commit that changes a golden
    // says which behavior changed. Debug and Release share the goldens; Unity is not promised the same.
    public sealed class TimelineGoldenTests
    {
        // FNV-1a 64 — TimelineEvent.GetHashCode is seeded per process
        sealed class Golden
        {
            ulong _hash = 14695981039346656037UL;
            public int Events;
            public ulong Hash => _hash;

            public void Byte(int b) => _hash = (_hash ^ (byte)b) * 1099511628211UL;

            // byte order independent
            public void Long(long v)
            {
                for (int shift = 0; shift < 64; shift += 8)
                    Byte((int)(v >> shift));
            }

            public void Event(in TimelineEvent e)
            {
                Events++;
                Byte((int)e.Kind);
                Byte((e.Reverse ? 1 : 0) | (e.Skipped ? 2 : 0) | (e.Truncated ? 4 : 0));
                Long(e.Item);
                Long(e.Block);
                Long(e.Occurrence);
                Long(e.Time);
                Long(e.OccurrenceStart);
            }
        }

        const int TickMark = 0xF0, SignalMark = 0xF1, SeekMark = 0xF2, WindowMark = 0xF3, HeadMark = 0xF4, SampleMark = 0xF5;

        // on the grid · off it · zero · the two clocks apart
        static readonly float[] DtScaled = { 0.25f, 0.0173f, 0f, 0.1f, 1f / 60, 0.25f, 0f, 0.3333f, 0.05f, 0.125f, 0.0071f, 0.4f, 0.2f, 0.75f, 1f / 144, 0.5f };
        static readonly float[] DtUnscaled = { 0.25f, 0.0173f, 0.05f, 0f, 1f / 30, 0.5f, 0f, 0.125f, 0.3333f, 0.05f, 0.0213f, 0.1f, 0.6f, 0.25f, 1f / 60, 0.5f };

        static TimelineItemDesc After(TimelineItemDesc d)
        {
            d.Rule = StartRule.AfterPrevious;
            return d;
        }

        // blocks · nesting · a Hold on another clock · background · the Player clock · a wait · After all
        static TimelineProgram Blocks() => Compile(
            Fx(duration: 0.5f),
            LoopStart(StartRule.AfterPrevious, delay: 0.1f), Fx(duration: 0.2f),
            LoopStart(), Instant(delay: 0.05f), Clock(Fx(duration: 0.1f), TimelineClock.Unscaled), LoopEnd(2, gap: 0.05f),
            Clock(LoopEnd(3, gap: 0.1f), TimelineClock.Unscaled),
            Clock(Hold(0.15f), TimelineClock.Unscaled),
            Clock(Fx(StartRule.AfterPrevious, duration: 0.35f), TimelineClock.Player),
            Background(Repeat(Clock(Fx(duration: 0.3f), TimelineClock.Unscaled), -1, gap: 0.1f)),
            Background(Repeat(Instant(delay: 0.2f), 4)),
            Wait(0.4f, StartRule.AfterPrevious),
            Instant(StartRule.AfterAll));

        // a random draw, frozen
        static readonly float[] BlocksScale = { 1f, 1f, 1.3f, 1f, 1f, 0.7f, 1f, 1f, 1f, 1.1f, 0.9f, 1f, 1.2f, 1f };
        static readonly bool[] BlocksSkip = { false, false, true, false, false, false, false, false, false, false, true, false, false, false };

        // signal waits — a timeout, an open one, a timeout after a clock change; the background runs throughout
        static TimelineProgram Signals() => Compile(
            Fx(duration: 0.3f),
            Background(Repeat(Fx(duration: 0.15f), -1)),
            Signal(timeout: 0.5f),
            Fx(StartRule.AfterPrevious, duration: 0.2f),
            After(Signal()),
            Clock(Fx(StartRule.AfterPrevious, delay: 0.1f, duration: 0.25f), TimelineClock.Unscaled),
            After(Signal(timeout: 0.4f)),
            Instant(StartRule.AfterAll));

        // zero-period blocks — finite ones at once, the infinite one deferred to the next tick
        static TimelineProgram ZeroPeriod() => Compile(
            LoopStart(), Instant(), LoopStart(), Instant(), LoopEnd(2), LoopEnd(3),
            Fx(StartRule.AfterPrevious, duration: 0.1f),
            Background(Repeat(Instant(), 3)),
            LoopStart(StartRule.AfterPrevious), Instant(), LoopEnd(0));

        // the finite part — seekable, for the sampler's replay
        static TimelineProgram ZeroPeriodFinite() => Compile(
            LoopStart(), Instant(), LoopStart(), Instant(), LoopEnd(2), LoopEnd(3),
            Fx(StartRule.AfterPrevious, duration: 0.1f),
            Background(Repeat(Instant(), 3)),
            Instant(StartRule.AfterPrevious));

        // items longer than a chunk (2^31 − 1 µs) on two clocks
        static TimelineProgram Long() => Compile(
            Fx(duration: 2500f),
            Fx(StartRule.AfterPrevious, duration: 1000f),
            Clock(Fx(duration: 3000f), TimelineClock.Unscaled),
            Clock(Fx(StartRule.AfterPrevious, delay: 100f, duration: 50f), TimelineClock.Unscaled),
            Background(Repeat(Fx(duration: 700f), -1)));

        // ---- folding ----

        static void Drain(TimelineCursor c, Golden g)
        {
            while (c.TryNext(out var e))
                g.Event(e);
        }

        static void Drain(TimelineSampler s, Golden g)
        {
            while (s.TryNext(out var e))
                g.Event(e);
        }

        // the tick's end: a boundary · the position · off-nominal · every item's LocalTime bits (the player evaluates them)
        static void EndTick(TimelineCursor c, TimelineProgram p, Golden g)
        {
            c.EndAdvance();
            g.Byte(TickMark);
            g.Long(c.Position);
            g.Byte(c.IsOffNominal ? 1 : 0);
            for (int item = 0; item < p.ItemCount; item++)
                g.Long(BitConverter.SingleToInt32Bits(c.LocalTime(item)));
        }

        static void Begin(CursorRig rig, TimelineClock baseClock, Golden g)
        {
            rig.Cursor.Begin(baseClock);
            Drain(rig.Cursor, g);
            EndTick(rig.Cursor, rig.S.P, g);
        }

        static void Tick(CursorRig rig, double dtScaled, double dtUnscaled, Golden g)
        {
            rig.Cursor.BeginAdvance(dtScaled, dtUnscaled);
            Drain(rig.Cursor, g);
            EndTick(rig.Cursor, rig.S.P, g);
        }

        static void Ticks(CursorRig rig, int count, Golden g)
        {
            for (int i = 0; i < count && !rig.Cursor.IsFinished; i++)
                Tick(rig, DtScaled[i % DtScaled.Length], DtUnscaled[i % DtUnscaled.Length], g);
        }

        static void SendSignal(TimelineCursor c, Golden g)
        {
            g.Byte(SignalMark);
            g.Long(c.Signal());
        }

        static void FoldSample(SamplerRig rig, long position, bool reverse, Golden g)
        {
            var s = rig.At(position, reverse);
            g.Byte(SampleMark);
            g.Long(s.Position);
            for (int item = 0; item < rig.P.ItemCount; item++)
            {
                if (rig.P.NodeOfItem(item) < 0)
                    continue;
                g.Byte((int)s.Stage(item));
                g.Long(s.Occurrence(item));
                g.Long(BitConverter.SingleToInt32Bits(s.LocalTime(item)));
            }
        }

        static void Forward(SamplerRig rig, long lo, long hi, Golden g)
        {
            g.Byte(WindowMark);
            rig.Sampler.SweepForward(lo, hi);
            Drain(rig.Sampler, g);
        }

        static void Reverse(SamplerRig rig, long lo, long hi, Golden g)
        {
            g.Byte(WindowMark);
            rig.Sampler.SweepReverse(lo, hi);
            Drain(rig.Sampler, g);
        }

        static void Seek(SamplerRig rig, long from, long to, bool reversePlay, Golden g)
        {
            long target = rig.Sampler.SeekBegin(from, to, reversePlay);
            g.Byte(SeekMark);
            g.Long(target);
            Drain(rig.Sampler, g);
            FoldSample(rig, target, reversePlay, g);
        }

        // ---- scenes ----

        static Action<Golden> CursorBlocks()
        {
            var rig = new CursorRig(Blocks());
            return g =>
            {
                Begin(rig, TimelineClock.Scaled, g);
                Ticks(rig, 64, g);
            };
        }

        static Action<Golden> CursorScaledSkippedDelayed()
        {
            var rig = new CursorRig(Blocks(), BlocksScale, BlocksSkip, 1.5f, 0.2f);
            return g =>
            {
                Begin(rig, TimelineClock.Unscaled, g);
                Ticks(rig, 64, g);
            };
        }

        static Action<Golden> CursorSignals()
        {
            var rig = new CursorRig(Signals());
            var c = rig.Cursor;
            return g =>
            {
                Begin(rig, TimelineClock.Scaled, g);
                bool inTick = false;
                int openSince = -1;
                for (int i = 0; i < 64 && !c.IsFinished; i++)
                {
                    c.BeginAdvance(DtScaled[i % DtScaled.Length], DtUnscaled[i % DtUnscaled.Length]);
                    while (c.TryNext(out var e))
                    {
                        g.Event(e);
                        // in tick: while handling a background start, from the fourth tick
                        if (!inTick && i >= 3 && e.Item == 1 && e.Kind == TimelineEventKind.Started)
                        {
                            SendSignal(c, g);
                            inTick = true;
                        }
                        if (e.Item == 4 && e.Kind == TimelineEventKind.SignalWaitBegan)
                            openSince = i;
                    }
                    EndTick(c, rig.S.P, g);
                    // out of tick: two ticks into the open wait
                    if (openSince >= 0 && i == openSince + 2)
                        SendSignal(c, g);
                }
            };
        }

        static Action<Golden> CursorSeeksStopComplete()
        {
            var rig = new CursorRig(Blocks(), BlocksScale, null, 1f, 0.25f);
            var c = rig.Cursor;
            return g =>
            {
                Begin(rig, TimelineClock.Scaled, g);
                Ticks(rig, 3, g);
                g.Byte(SeekMark);
                c.Rebuild(700_000);
                Ticks(rig, 4, g);

                // a stop while handling the tick's first event
                c.BeginAdvance(0.05, 0.05);
                if (c.TryNext(out var e))
                    g.Event(e);
                c.Abort();
                g.Byte(SeekMark);
                g.Long(c.Position);

                c.Rebuild(2_100_000);
                Ticks(rig, 3, g);

                // a skip — event-driven completion on the other base clock
                Begin(rig, TimelineClock.Unscaled, g);
                Ticks(rig, 2, g);
                c.BeginComplete();
                Drain(c, g);
                EndTick(c, rig.S.P, g);
            };
        }

        static Action<Golden> CursorChunkedTicks()
        {
            var rig = new CursorRig(Long());
            return g =>
            {
                Begin(rig, TimelineClock.Scaled, g);
                Tick(rig, 3000.0, 4500.0, g);
                Tick(rig, 0.0, 2500.0, g);
                Tick(rig, 1234.5, 0.5, g);
                Begin(rig, TimelineClock.Unscaled, g);
                Tick(rig, 2147.483647, 2147.483648, g);
                Tick(rig, 2147.483648, 0.0, g);
                Tick(rig, 5000.0, 5000.0, g);
            };
        }

        static Action<Golden> CursorZeroPeriod()
        {
            var rig = new CursorRig(ZeroPeriod());
            return g =>
            {
                Begin(rig, TimelineClock.Scaled, g);
                Ticks(rig, 12, g);
            };
        }

        static Action<Golden> SamplerSweeps()
        {
            var blocks = new SamplerRig(Blocks(), BlocksScale, BlocksSkip, 1f, 0.25f);
            var zero = new SamplerRig(ZeroPeriodFinite());
            return g =>
            {
                foreach (var rig in new[] { blocks, zero })
                {
                    long total = rig.R.Total;
                    Forward(rig, -1, total, g);
                    Reverse(rig, rig.R.RootDelay, total + 1, g);
                }
                Forward(blocks, 0, 333_333, g);
                Forward(blocks, 333_333, 1_500_000, g);
                Reverse(blocks, 250_000, 900_000, g);
                Reverse(blocks, 900_000, 2_000_000, g);
                Forward(zero, -1, 0, g);
                Reverse(zero, 0, 1, g);
                Forward(zero, 0, 50_000, g);
                Reverse(zero, 50_000, 100_001, g);
            };
        }

        static Action<Golden> SamplerReverseHeadSeeks()
        {
            var rig = new SamplerRig(Blocks(), BlocksScale, BlocksSkip, 1f, 0.3f);
            var s = rig.Sampler;
            return g =>
            {
                // a forward play — ahead · back
                Seek(rig, 0, 1_234_567, false, g);
                Seek(rig, 1_234_567, 400_000, false, g);

                // a reverse play — back · ahead move the head, which then runs to the start
                s.BeginReverse();
                for (int i = 0; i < 200; i++)
                {
                    if (i > 0)
                        s.AdvanceReverse(DtScaled[i % DtScaled.Length]);
                    Drain(s, g);
                    g.Byte(HeadMark);
                    g.Long(s.ReversePosition);
                    if (s.IsReverseFinished)
                        break;
                    FoldSample(rig, s.ReversePosition, true, g);
                    long head = s.ReversePosition;
                    if (i == 4)
                        Seek(rig, head, head / 3, true, g);
                    else if (i == 8)
                        Seek(rig, head, head + 600_000, true, g);
                }
            };
        }

        static readonly (string Name, Func<Action<Golden>> Scene, int Events, ulong Hash)[] Scenes =
        {
            ("cursor: blocks · nesting · Hold on another clock · background · Player clock · After all", CursorBlocks, 52, 0x21EC8F7771D5CC4BUL),
            ("cursor: unscaled base · item scales · skips · global scale · start delay", CursorScaledSkippedDelayed, 52, 0xEFD4E935358A3AEBUL),
            ("cursor: signals in tick · out of tick · timeout · open wait", CursorSignals, 37, 0x27243DD2223E621DUL),
            ("cursor: seeks (rebuild) · stop while handling · skip (complete)", CursorSeeksStopComplete, 68, 0x52D84C18359E590FUL),
            ("cursor: chunked ticks over 2^31 - 1 µs · two clocks", CursorChunkedTicks, 22, 0xC3607E34E2FD6C2CUL),
            ("cursor: zero-period blocks · finite at once · infinite deferred", CursorZeroPeriod, 44, 0x2F527A8639535233UL),
            ("sampler: forward · reverse sweeps · zero-period replay", SamplerSweeps, 258, 0x878A5D7062E08471UL),
            ("sampler: seeks of both plays · reverse head · samples", SamplerReverseHeadSeeks, 85, 0x8631E62704BA34A6UL),
        };

        [Test]
        public void T11_ScenesMatchTheirGoldens()
        {
            var lines = new StringBuilder();
            var changed = new StringBuilder();
            foreach (var (name, scene, events, hash) in Scenes)
            {
                var play = scene();
                var first = new Golden();
                play(first);
                var again = new Golden();
                play(again);
                Assert.That((again.Events, again.Hash), Is.EqualTo((first.Events, first.Hash)), $"{name} — the same buffers twice");
                lines.AppendLine($"(\"{name}\", {first.Events}, 0x{first.Hash:X16}UL),");
                if (first.Events != events || first.Hash != hash)
                    changed.AppendLine(name);
            }
            TestContext.WriteLine(lines.ToString());
            Assert.That(changed.ToString(), Is.Empty, "scenes off their goldens — every scene's new line:\n" + lines);
        }
    }
}
