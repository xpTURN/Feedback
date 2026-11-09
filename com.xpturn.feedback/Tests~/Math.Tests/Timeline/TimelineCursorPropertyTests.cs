using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Timeline
{
    // the cursor against the sweep and the sampler. Fixed seeds; a failure prints the seed, the program and the dt
    // sequence — move it to TimelineCursorTests as a hand case.
    public sealed class TimelineCursorPropertyTests
    {
        const int Programs = TimelineSweepPropertyTests.Programs;
        const int Ticks = 40;

        static string Dts(List<double> dts)
        {
            var sb = new StringBuilder("dt { ");
            foreach (double dt in dts)
                sb.Append(dt.ToString("R", CultureInfo.InvariantCulture)).Append(", ");
            return sb.Append('}').ToString();
        }

        static string Describe(List<TimelineEvent> events) => string.Join(", ", SamplerRig.Format(events));

        internal static void AssertEvents(List<TimelineEvent> expected, List<TimelineEvent> actual, string where)
            => Assert.That(actual, Is.EqualTo(expected), $"{where}\nexpected: {Describe(expected)}\nactual:   {Describe(actual)}");

        // the tick-end state against the sample at the same position (the most recently reached occurrence)
        internal static void AssertState(CursorRig rig, long position, string where)
        {
            var s = rig.S.At(position);
            var p = rig.S.P;
            for (int item = 0; item < p.ItemCount; item++)
            {
                int node = p.NodeOfItem(item);
                if (node < 0)
                    continue;
                string at = $"{where} item {item} at {position}";
                Assert.That(rig.Cursor.Stage(item), Is.EqualTo(s.Stage(item)), at + " stage");
                Assert.That(rig.Cursor.Occurrence(item), Is.EqualTo(s.Occurrence(item)), at + " occurrence");
                if (s.Stage(item) == TimelineStage.Pending)
                    continue;
                Assert.That(rig.Cursor.Queue.NodeStartAt(node), Is.EqualTo(s.NodeStart(node)), at + " start");
                Assert.That(rig.Cursor.LocalTime(item), Is.EqualTo(s.LocalTime(item)), at + " local time");
                Assert.That(rig.Cursor.Seconds(item), Is.EqualTo(s.Seconds(item)), at + " seconds");
            }
        }

        static List<double> MakeDts(Random rng, bool gridOnly)
        {
            var dts = new List<double>();
            for (int i = 0; i < Ticks; i++)
            {
                int roll = rng.Next(100);
                double dt;
                if (roll < 20)
                    dt = 0;
                else if (roll < 70 || gridOnly && roll < 97)
                    dt = rng.Next(1, 33) / 64.0;
                else if (roll < 85)
                    dt = rng.NextDouble() * 0.5;
                else if (roll < 97)
                    dt = rng.Next(33, 641) / 64.0;
                else
                    dt = 2500.0 + rng.Next(0, 64) / 64.0;   // over 2^31 − 1 µs: a chunked tick
                dts.Add(dt);
            }
            return dts;
        }

        static void ForEachSeekable(Action<TimelineSweepPropertyTests.Case, Random> check, int minimumSeekable)
        {
            int seekable = 0;
            for (int seed = 0; seed < Programs; seed++)
            {
                var c = TimelineSweepPropertyTests.Generate(seed);
                if (!c.Rig.R.IsSeekable)
                    continue;
                seekable++;
                check(c, new Random(seed * 7919 + 13));
            }
            TestContext.WriteLine($"{seekable}/{Programs} seekable programs");
            Assert.That(seekable, Is.GreaterThanOrEqualTo(minimumSeekable));
        }

        static void Guard(TimelineSweepPropertyTests.Case c, List<double> dts, Action check)
        {
            try
            {
                check();
            }
            catch (Exception ex) when (!(ex is SuccessException))
            {
                throw new AssertionException($"{TimelineSweepPropertyTests.Literal(c)}\n{Dts(dts)}\n{ex.Message}", ex);
            }
        }

        // ---- one clock ----

        [Test]
        public void C6_SingleClockTicksMatchTheSweep()
        {
            long ticks = 0, chunked = 0;
            int peakQueue = 0;
            ForEachSeekable((c, rng) =>
            {
                var dts = MakeDts(rng, false);
                var rig = new CursorRig(c.Rig);
                Guard(c, dts, () =>
                {
                    AssertEvents(c.Rig.Forward(-1, 0), rig.Begin(), "Begin");
                    AssertState(rig, 0, "Begin");
                    foreach (double dt in dts)
                    {
                        long from = rig.TauS;
                        var events = rig.Tick(dt);
                        AssertEvents(c.Rig.Forward(from, rig.TauS), events, $"tick ({from}, {rig.TauS}]");
                        AssertState(rig, rig.TauS, "tick");
                        Assert.That(rig.Cursor.IsOffNominal, Is.False, "one clock never leaves the nominal axis");
                        ticks++;
                        if (rig.TauS - from > int.MaxValue)
                            chunked++;
                    }
                });
                peakQueue = Math.Max(peakQueue, rig.Cursor.Queue.MaxCount * 100 / rig.Cursor.Queue.Capacity);
                Assert.That(rig.Cursor.Queue.MaxCount, Is.LessThanOrEqualTo(rig.Cursor.Queue.Capacity));
            }, 300);
            TestContext.WriteLine($"{ticks} ticks · {chunked} chunked · queue peak {peakQueue}% of the bound");
            Assert.That(chunked, Is.GreaterThan(0));
        }

        [Test]
        public void C6_RebuildThenContinueMatchesTheSweep()
        {
            int forward = 0, backward = 0;
            ForEachSeekable((c, rng) =>
            {
                var dts = MakeDts(rng, true);
                var rig = new CursorRig(c.Rig);
                Guard(c, dts, () =>
                {
                    var taus = new long[dts.Count + 1];
                    double acc = 0;
                    for (int i = 0; i < dts.Count; i++)
                    {
                        acc += dts[i];
                        taus[i + 1] = TimelineTime.FromSeconds(acc);
                    }
                    int j = rng.Next(dts.Count + 1);
                    int m = rng.Next(dts.Count + 1);
                    rig.Begin();
                    for (int i = 0; i < j; i++)
                        rig.Tick(dts[i]);
                    // a seek happens only on a nominal play, at a position the play can reach
                    rig.Rebuild(taus[m]);
                    AssertState(rig, taus[m], "rebuilt");
                    if (m > j)
                        forward++;
                    else if (m < j)
                        backward++;
                    for (int i = m; i < dts.Count; i++)
                    {
                        var events = rig.Tick(dts[i]);
                        Assert.That(rig.TauS, Is.EqualTo(taus[i + 1]));
                        AssertEvents(c.Rig.Forward(taus[i], taus[i + 1]), events, $"after rebuild at {taus[m]}: tick ({taus[i]}, {taus[i + 1]}]");
                        AssertState(rig, taus[i + 1], "after rebuild");
                    }
                });
            }, 300);
            TestContext.WriteLine($"rebuilds forward {forward} · backward {backward}");
            Assert.That(forward, Is.GreaterThan(50));
            Assert.That(backward, Is.GreaterThan(50));
        }

        [Test]
        public void C6_CompletionMatchesTheSweepToTheEnd()
        {
            int unfinished = 0;
            ForEachSeekable((c, rng) =>
            {
                var dts = MakeDts(rng, false);
                var rig = new CursorRig(c.Rig);
                Guard(c, dts, () =>
                {
                    int j = rng.Next(dts.Count / 4 + 1);
                    rig.Begin();
                    for (int i = 0; i < j; i++)
                        rig.Tick(dts[i]);
                    if (!rig.Cursor.IsFinished)
                        unfinished++;
                    long from = rig.TauS;
                    AssertEvents(c.Rig.Forward(from, c.Rig.R.Total), rig.Complete(), $"completion from {from}");
                    Assert.That(rig.Cursor.IsFinished, Is.True);
                    // past the total the sample's stages and occurrences no longer change (its times do)
                    var end = c.Rig.At(TimelineTime.Max(from, c.Rig.R.Total));
                    for (int item = 0; item < c.Rig.P.ItemCount; item++)
                    {
                        if (c.Rig.P.NodeOfItem(item) < 0)
                            continue;
                        Assert.That(rig.Cursor.Stage(item), Is.EqualTo(end.Stage(item)), $"completed item {item} stage");
                        Assert.That(rig.Cursor.Occurrence(item), Is.EqualTo(end.Occurrence(item)), $"completed item {item} occurrence");
                    }
                });
            }, 300);
            TestContext.WriteLine($"{unfinished} completions started before the end");
            Assert.That(unfinished, Is.GreaterThan(100));
        }

        // ---- two clocks ----

        static TimelineClock RandomClock(Random rng)
        {
            int roll = rng.Next(5);
            return roll < 2 ? TimelineClock.Scaled : roll < 4 ? TimelineClock.Unscaled : TimelineClock.Player;
        }

        static float OnGrid(float seconds, int perSecond) => (float)Math.Floor(seconds * perSecond) / perSecond;

        // TimelineSweepPropertyTests' structure with a random clock on every item and every time on a 1/perSecond grid (no scales)
        static TimelineItemDesc[] Reclocked(int seed, Random rng, int perSecond)
        {
            var items = (TimelineItemDesc[])TimelineSweepPropertyTests.Generate(seed).Items.Clone();
            for (int i = 0; i < items.Length; i++)
            {
                items[i].Clock = RandomClock(rng);
                items[i].Delay = OnGrid(items[i].Delay, perSecond);
                items[i].Duration = OnGrid(items[i].Duration, perSecond);
                items[i].RepeatGap = OnGrid(items[i].RepeatGap, perSecond);
                items[i].LoopGap = OnGrid(items[i].LoopGap, perSecond);
                items[i].Timeout = items[i].Timeout > 0f ? Math.Max(OnGrid(items[i].Timeout, perSecond), 1f / perSecond) : 0f;
            }
            return items;
        }

        // an Unscaled quantity is r times as long on a Scaled clock that runs r times as fast.
        // Consecutive Holds count their summed delay on the first Hold's clock.
        static TimelineItemDesc[] OnScaled(TimelineItemDesc[] items, float r)
        {
            var result = (TimelineItemDesc[])items.Clone();
            var holdRun = TimelineClock.Scaled;
            for (int i = 0; i < result.Length; i++)
            {
                var clock = items[i].Clock;
                if (items[i].Kind == TimelineItemKind.Hold)
                {
                    if (i == 0 || items[i - 1].Kind != TimelineItemKind.Hold)
                        holdRun = clock;
                    clock = holdRun;
                }
                result[i].Clock = TimelineClock.Scaled;
                if (clock != TimelineClock.Unscaled)
                    continue;
                result[i].Delay *= r;
                result[i].Duration *= r;
                result[i].RepeatGap *= r;
                result[i].LoopGap *= r;
                result[i].Timeout *= r;
            }
            return result;
        }

        static string Shape(TimelineEvent e) => $"{e.Kind} {e.Item} b{e.Block} #{e.Occurrence}{(e.Truncated ? " trunc" : "")}";

        static string[] Shapes(List<TimelineEvent> events) => events.ConvertAll(Shape).ToArray();

        static string Literal(TimelineItemDesc[] items)
        {
            var sb = new StringBuilder("Compile(");
            foreach (var d in items)
                sb.Append($"{{ {d.Kind} {d.Clock} rule {d.Rule} delay {d.Delay} dur {d.Duration} instant {d.Instant} repeat {d.Repeat}/{d.RepeatGap} loop {d.LoopCount}/{d.LoopGap} timeout {d.Timeout} bg {d.Background} }}, ");
            return sb.Append(')').ToString();
        }

        [Test]
        public void C3_ProportionalClocksEqualTheRescaledSingleClockList()
        {
            int twoClock = 0, ticks = 0;
            for (int seed = 0; seed < Programs; seed++)
            {
                var rng = new Random(seed * 104729 + 3);
                float r = seed % 2 == 0 ? 2f : 0.5f;
                var items = Reclocked(seed, rng, 32);
                var mixed = new CursorRig(TimelineProgram.Compile(items));
                var single = new CursorRig(TimelineProgram.Compile(OnScaled(items, r)));
                int mask = mixed.S.P.ClockMask;
                if ((mask & (1 << (int)TimelineClock.Unscaled)) != 0 && (mask & ~(1 << (int)TimelineClock.Unscaled)) != 0)
                    twoClock++;
                var dts = new List<double>();
                try
                {
                    Assert.That(Shapes(mixed.Begin()), Is.EqualTo(Shapes(single.Begin())), "Begin");
                    for (int i = 0; i < Ticks; i++)
                    {
                        double dtU = rng.Next(4) == 0 ? 0 : rng.Next(1, 17) / 32.0;
                        double dtS = dtU * r;
                        dts.Add(dtU);
                        var a = mixed.Tick(dtS, dtU);
                        var b = single.Tick(dtS, dtS);
                        Assert.That(Shapes(a), Is.EqualTo(Shapes(b)), $"tick {i}");
                        for (int item = 0; item < items.Length; item++)
                        {
                            Assert.That(mixed.Cursor.Stage(item), Is.EqualTo(single.Cursor.Stage(item)), $"tick {i} item {item} stage");
                            Assert.That(mixed.Cursor.Occurrence(item), Is.EqualTo(single.Cursor.Occurrence(item)), $"tick {i} item {item} occurrence");
                        }
                        ticks++;
                    }
                }
                catch (Exception ex) when (!(ex is SuccessException))
                {
                    throw new AssertionException($"seed {seed} r {r}: {Literal(items)}\ndtU {Dts(dts)}\n{ex.Message}", ex);
                }
            }
            TestContext.WriteLine($"{Programs} programs · {twoClock} use both clocks · {ticks} ticks");
            Assert.That(twoClock, Is.GreaterThan(150));
        }

        // the two-clock generator: one clock per segment; segments meet at After all (or a Hold), so one clock lives at a time
        static TimelineItemDesc[] Segments(Random rng)
        {
            var items = new List<TimelineItemDesc>();
            int segments = rng.Next(2, 6);
            for (int s = 0; s < segments; s++)
            {
                var clock = rng.Next(2) == 0 ? TimelineClock.Scaled : TimelineClock.Unscaled;
                if (s > 0 && rng.Next(3) == 0)
                    items.Add(new TimelineItemDesc { Kind = TimelineItemKind.Hold, Delay = rng.Next(0, 17) / 64f, Clock = rng.Next(2) == 0 ? TimelineClock.Scaled : TimelineClock.Unscaled });
                int n = rng.Next(1, 4);
                for (int i = 0; i < n; i++)
                {
                    var d = new TimelineItemDesc { Clock = clock };
                    int roll = rng.Next(100);
                    if (roll < 50)
                    {
                        d.Kind = TimelineItemKind.Effect;
                        d.Duration = rng.Next(0, 33) / 64f;
                        if (rng.Next(5) == 0)
                        {
                            d.Repeat = rng.Next(1, 3);
                            d.RepeatGap = rng.Next(0, 9) / 64f;
                        }
                    }
                    else if (roll < 75)
                    {
                        d.Kind = TimelineItemKind.Effect;
                        d.Instant = true;
                    }
                    else
                    {
                        d.Kind = TimelineItemKind.Wait;
                        d.Duration = rng.Next(0, 33) / 64f;
                    }
                    d.Rule = i == 0 ? (s == 0 ? StartRule.WithPrevious : StartRule.AfterAll) : (rng.Next(2) == 0 ? StartRule.WithPrevious : StartRule.AfterPrevious);
                    d.Delay = rng.Next(2) == 0 ? 0f : rng.Next(1, 17) / 64f;
                    items.Add(d);
                }
            }
            return items.ToArray();
        }

        static void NextDts(Random rng, out double dtS, out double dtU)
        {
            double grid = rng.Next(1, 17) / 64.0;
            int roll = rng.Next(100);
            if (roll < 45)
            {
                dtS = dtU = grid;
            }
            else if (roll < 70)
            {
                dtS = rng.Next(2) == 0 ? 0 : grid;
                dtU = dtS == 0 ? grid : 0;
            }
            else if (roll < 90)
            {
                dtS = grid;
                dtU = rng.Next(1, 17) / 64.0;
            }
            else
            {
                // off the grid: a transfer lands between microseconds and the offset absorbs the floor
                dtS = rng.NextDouble() * 0.25;
                dtU = rng.NextDouble() * 0.25;
            }
        }

        [Test]
        public void C7_NominalTwoClockPlaysMatchTheSweep()
        {
            int nominalRuns = 0, offRuns = 0, unequalNominalTicks = 0;
            for (int seed = 0; seed < Programs; seed++)
            {
                var rng = new Random(seed * 65537 + 11);
                var items = seed % 10 < 7 ? Segments(rng) : Reclocked(seed, rng, 64);
                var rig = new CursorRig(TimelineProgram.Compile(items));
                if (!rig.S.R.IsSeekable)
                    continue;
                var dts = new List<double>();
                try
                {
                    TimelineCursorPropertyTests.AssertEvents(rig.S.Forward(-1, 0), rig.Begin(), "Begin");
                    long nu = rig.Cursor.Position;
                    bool off = false;
                    for (int i = 0; i < Ticks; i++)
                    {
                        NextDts(rng, out double dtS, out double dtU);
                        dts.Add(dtS);
                        dts.Add(dtU);
                        long scaled = rig.TauS, unscaled = rig.TauU;
                        var events = rig.Tick(dtS, dtU);
                        if (rig.Cursor.IsOffNominal)
                        {
                            off = true;
                            break;
                        }
                        long next = rig.Cursor.Position;
                        AssertEvents(rig.S.Forward(nu, next), events, $"tick {i} ({nu}, {next}]");
                        AssertNominalState(rig, next, $"tick {i}");
                        if (rig.TauS - scaled != rig.TauU - unscaled)
                            unequalNominalTicks++;
                        nu = next;
                    }
                    if (off)
                        offRuns++;
                    else
                        nominalRuns++;
                }
                catch (Exception ex) when (!(ex is SuccessException))
                {
                    throw new AssertionException($"seed {seed}: {Literal(items)}\n(dtS, dtU) {Dts(dts)}\n{ex.Message}", ex);
                }
            }
            TestContext.WriteLine($"nominal runs {nominalRuns} · off-nominal runs {offRuns} · nominal ticks at unequal rates {unequalNominalTicks}");
            Assert.That(unequalNominalTicks, Is.GreaterThan(1000));
            Assert.That(offRuns, Is.GreaterThan(10));
        }

        // on the nominal axis the running items read the sample's times; a finished item's clock may stand still
        static void AssertNominalState(CursorRig rig, long position, string where)
        {
            var s = rig.S.At(position);
            var p = rig.S.P;
            for (int item = 0; item < p.ItemCount; item++)
            {
                if (p.NodeOfItem(item) < 0)
                    continue;
                string at = $"{where} item {item} at {position}";
                Assert.That(rig.Cursor.Stage(item), Is.EqualTo(s.Stage(item)), at + " stage");
                Assert.That(rig.Cursor.Occurrence(item), Is.EqualTo(s.Occurrence(item)), at + " occurrence");
                if (s.Stage(item) != TimelineStage.Running)
                    continue;
                Assert.That(rig.Cursor.LocalTime(item), Is.EqualTo(s.LocalTime(item)), at + " local time");
                Assert.That(rig.Cursor.Seconds(item), Is.EqualTo(s.Seconds(item)), at + " seconds");
            }
        }
    }
}
