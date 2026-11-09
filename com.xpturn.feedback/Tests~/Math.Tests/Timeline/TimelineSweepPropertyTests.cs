using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Timeline
{
    // split windows · sweeps against samples · reverse against forward, and the bound checks: random programs on a 1/64 s
    // grid with non-binary scales.
    // A failure prints the seed and the program as a literal — move it to TimelineSamplerTests as a hand case.
    public sealed class TimelineSweepPropertyTests
    {
        internal const int Programs = 400;
        const int WindowsPerProgram = 8;

        internal sealed class Case
        {
            public int Seed;
            public TimelineItemDesc[] Items;
            public float[] Scale;
            public bool[] Skip;
            public float Global;
            public float Root;
            public SamplerRig Rig;
        }

        internal static float Grid(Random rng, int maxSteps) => rng.Next(0, maxSteps + 1) / 64f;

        internal static float RandomScale(Random rng) => rng.Next(2) == 0 ? 1f : (float)(0.5 + rng.NextDouble() * 1.5);

        internal static Case Generate(int seed)
        {
            var rng = new Random(seed);
            int count = rng.Next(1, 13);
            var items = new List<TimelineItemDesc>();
            int depth = 0;
            for (int i = 0; i < count; i++)
            {
                var rule = (StartRule)rng.Next(3);
                float delay = rng.Next(2) == 0 ? 0f : Grid(rng, 32);
                int roll = rng.Next(100);
                var d = new TimelineItemDesc { Rule = rule, Delay = delay };
                if (roll < 9 && depth < 2)
                {
                    d.Kind = TimelineItemKind.LoopStart;
                    depth++;
                }
                else if (roll < 20 && depth > 0 || roll < 21)
                {
                    d = new TimelineItemDesc { Kind = TimelineItemKind.LoopEnd, LoopCount = rng.Next(40) == 0 ? 0 : rng.Next(1, 4), LoopGap = rng.Next(2) == 0 ? 0f : Grid(rng, 32) };
                    if (depth > 0)
                        depth--;
                }
                else if (roll < 26)
                {
                    d = new TimelineItemDesc { Kind = TimelineItemKind.Hold, Delay = delay };
                }
                else if (roll < 34)
                {
                    d.Kind = TimelineItemKind.Wait;
                    d.Duration = Grid(rng, 64);
                }
                else if (roll < 38)
                {
                    d.Kind = TimelineItemKind.SignalWait;
                    d.Timeout = rng.Next(10) == 0 ? 0f : Grid(rng, 64) + 1 / 64f;
                }
                else
                {
                    d.Kind = TimelineItemKind.Effect;
                    d.Instant = roll < 55;
                    d.Duration = Grid(rng, 64);
                    if (rng.Next(100) < 15)
                        d.Background = true;
                    if (rng.Next(100) < 15)
                    {
                        d.Repeat = rng.Next(10) == 0 ? -1 : rng.Next(1, 4);
                        d.RepeatGap = rng.Next(2) == 0 ? 0f : Grid(rng, 16);
                    }
                }
                items.Add(d);
            }
            while (depth-- > 0)
                items.Add(new TimelineItemDesc { Kind = TimelineItemKind.LoopEnd, LoopCount = rng.Next(1, 4), LoopGap = rng.Next(2) == 0 ? 0f : Grid(rng, 32) });

            var c = new Case { Seed = seed, Items = items.ToArray() };
            c.Scale = new float[c.Items.Length];
            c.Skip = new bool[c.Items.Length];
            for (int i = 0; i < c.Items.Length; i++)
            {
                c.Scale[i] = RandomScale(rng);
                c.Skip[i] = rng.Next(5) == 0;
            }
            c.Global = RandomScale(rng);
            c.Root = rng.Next(2) == 0 ? 0f : Grid(rng, 32);
            c.Rig = new SamplerRig(TimelineProgram.Compile(c.Items), c.Scale, c.Skip, c.Global, c.Root);
            return c;
        }

        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";

        internal static string Literal(Case c)
        {
            var sb = new StringBuilder();
            sb.Append($"seed {c.Seed}: Compile(");
            for (int i = 0; i < c.Items.Length; i++)
            {
                var d = c.Items[i];
                if (i > 0)
                    sb.Append(", ");
                sb.Append($"new TimelineItemDesc {{ Kind = TimelineItemKind.{d.Kind}, Instant = {(d.Instant ? "true" : "false")}, Rule = StartRule.{d.Rule}, Delay = {F(d.Delay)}, Duration = {F(d.Duration)}, Repeat = {d.Repeat}, RepeatGap = {F(d.RepeatGap)}, LoopCount = {d.LoopCount}, LoopGap = {F(d.LoopGap)}, Timeout = {F(d.Timeout)}, Background = {(d.Background ? "true" : "false")} }}");
            }
            sb.Append(") scale { ");
            foreach (float s in c.Scale)
                sb.Append(F(s)).Append(", ");
            sb.Append("} skip { ");
            foreach (bool s in c.Skip)
                sb.Append(s ? "true, " : "false, ");
            sb.Append($"}} global {F(c.Global)} root {F(c.Root)}");
            return sb.ToString();
        }

        static void ForEachSeekable(Action<Case, Random> check, int minimumSeekable)
        {
            int seekable = 0;
            double queueUse = 0, instantUse = 0;
            for (int seed = 0; seed < Programs; seed++)
            {
                var c = Generate(seed);
                if (!c.Rig.R.IsSeekable)
                    continue;
                seekable++;
                try
                {
                    check(c, new Random(seed * 7919 + 1));
                    Assert.That(c.Rig.Sampler.Queue.MaxCount, Is.LessThanOrEqualTo(c.Rig.Sampler.Queue.Capacity), "queue bound");
                    Assert.That(c.Rig.Sampler.Instant.MaxCount, Is.LessThanOrEqualTo(c.Rig.Sampler.Instant.Capacity), "instant bound");
                    queueUse = Math.Max(queueUse, (double)c.Rig.Sampler.Queue.MaxCount / c.Rig.Sampler.Queue.Capacity);
                    instantUse = Math.Max(instantUse, (double)c.Rig.Sampler.Instant.MaxCount / c.Rig.Sampler.Instant.Capacity);
                }
                catch (Exception ex) when (!(ex is SuccessException))
                {
                    throw new AssertionException($"{Literal(c)}\n{ex.Message}", ex);
                }
            }
            TestContext.WriteLine($"{seekable}/{Programs} seekable programs · peak use of the bound: queue {queueUse:P0} · instant {instantUse:P0}");
            Assert.That(seekable, Is.GreaterThanOrEqualTo(minimumSeekable));
        }

        static long RandomPosition(Random rng, Case c, List<TimelineEvent> events)
        {
            if (events.Count > 0 && rng.Next(3) > 0)
                return events[rng.Next(events.Count)].Time + rng.Next(3) - 1;
            return c.Rig.R.RootDelay - 1 + (long)(rng.NextDouble() * (c.Rig.R.Total - c.Rig.R.RootDelay + 3));
        }

        static string Describe(List<TimelineEvent> events) => string.Join(", ", SamplerRig.Format(events));

        // ---- split windows ----

        [Test]
        public void S3_SplittingAWindowKeepsTheEvents()
        {
            ForEachSeekable((c, rng) =>
            {
                var rig = c.Rig;
                var all = rig.Forward(-1, rig.R.Total);
                var sorted = new long[3];
                for (int w = 0; w < WindowsPerProgram; w++)
                {
                    sorted[0] = RandomPosition(rng, c, all);
                    sorted[1] = RandomPosition(rng, c, all);
                    sorted[2] = RandomPosition(rng, c, all);
                    Array.Sort(sorted);
                    long a = sorted[0], b = sorted[1], d = sorted[2];
                    var whole = rig.Forward(a, d);
                    var split = rig.Forward(a, b);
                    split.AddRange(rig.Forward(b, d));
                    Assert.That(split, Is.EqualTo(whole), $"({a}, {b}] + ({b}, {d}]\nwhole: {Describe(whole)}\nsplit: {Describe(split)}");
                }
            }, 300);
        }

        // ---- sweeps against samples ----

        struct ItemState
        {
            public TimelineStage Stage;
            public long Occurrence;
            public long Start;
        }

        static void Apply(ItemState[] state, TimelineEvent e)
        {
            if (e.Item < 0 || e.Kind == TimelineEventKind.SignalWaitBegan)
                return;
            bool running = e.Kind == TimelineEventKind.Started;
            state[e.Item] = new ItemState { Stage = running ? TimelineStage.Running : TimelineStage.Done, Occurrence = e.Occurrence, Start = e.OccurrenceStart };
        }

        static void Compare(SamplerRig rig, ItemState[] state, long position, bool reverse)
        {
            var s = rig.At(position, reverse);
            for (int item = 0; item < rig.P.ItemCount; item++)
            {
                int node = rig.P.NodeOfItem(item);
                if (node < 0)
                    continue;
                var expected = state[item];
                string where = $"item {item} at {position}{(reverse ? " reverse" : "")}";
                Assert.That(s.Stage(item), Is.EqualTo(expected.Stage), where + " stage");
                if (expected.Stage == TimelineStage.Pending)
                {
                    Assert.That(s.Occurrence(item), Is.EqualTo(-1), where + " occurrence");
                    continue;
                }
                Assert.That(s.Occurrence(item), Is.EqualTo(expected.Occurrence), where + " occurrence");
                Assert.That(s.NodeStart(node), Is.EqualTo(expected.Start), where + " start");
                long duration = rig.R.ItemDuration(item);
                float local = duration == TimelineTime.Infinite ? 0f
                    : duration <= 0 ? (position >= expected.Start ? 1f : 0f)
                    : (float)Math.Clamp((double)(position - expected.Start) / duration, 0.0, 1.0);
                Assert.That(s.LocalTime(item), Is.EqualTo(local), where + " local time");
            }
        }

        static ItemState[] Pending(SamplerRig rig)
        {
            var state = new ItemState[rig.P.ItemCount];
            for (int i = 0; i < state.Length; i++)
                state[i].Occurrence = -1;
            return state;
        }

        [Test]
        public void S4_ForwardSweepMatchesTheSample()
        {
            ForEachSeekable((c, rng) =>
            {
                var rig = c.Rig;
                var events = rig.Forward(-1, rig.R.Total);
                var state = Pending(rig);
                Compare(rig, state, -1, false);
                int i = 0;
                while (i < events.Count)
                {
                    long t = events[i].Time;
                    Compare(rig, state, t - 1, false);
                    while (i < events.Count && events[i].Time == t)
                        Apply(state, events[i++]);
                    Compare(rig, state, t, false);
                }
                Compare(rig, state, rig.R.Total + 1, false);
            }, 300);
        }

        [Test]
        public void S4_ReverseSweepMatchesTheReverseSample()
        {
            ForEachSeekable((c, rng) =>
            {
                var rig = c.Rig;
                var events = rig.Reverse(rig.R.RootDelay, rig.R.Total + 1);
                var state = Pending(rig);
                Compare(rig, state, rig.R.Total + 1, true);
                int i = 0;
                while (i < events.Count)
                {
                    long t = events[i].Time;
                    Compare(rig, state, t + 1, true);
                    while (i < events.Count && events[i].Time == t)
                        Apply(state, events[i++]);
                    Compare(rig, state, t, true);
                }
            }, 300);
        }

        [Test]
        public void S4_ReverseHeadMatchesTheReverseSample()
        {
            ForEachSeekable((c, rng) =>
            {
                var rig = c.Rig;
                var expected = rig.Reverse(rig.R.RootDelay, rig.R.Total + 1);
                var state = Pending(rig);
                var played = new List<TimelineEvent>();
                int finished = 0;
                rig.Sampler.BeginReverse();
                for (int tick = 0; tick < 100000 && !rig.Sampler.IsReverseFinished; tick++)
                {
                    foreach (var e in rig.Drain())
                    {
                        if (e.Kind == TimelineEventKind.Finished)
                        {
                            finished++;
                            Assert.That(e.Reverse && e.Time == rig.R.RootDelay, Is.True, "reverse Finished at the root delay");
                            continue;
                        }
                        played.Add(e);
                        Apply(state, e);
                    }
                    Compare(rig, state, rig.Sampler.ReversePosition, true);
                    rig.Sampler.AdvanceReverse(rng.Next(4) == 0 ? 0.0 : rng.NextDouble() / 8);
                }
                foreach (var e in rig.Drain())
                {
                    if (e.Kind == TimelineEventKind.Finished)
                    {
                        finished++;
                        continue;
                    }
                    played.Add(e);
                    Apply(state, e);
                }
                Compare(rig, state, rig.R.RootDelay, true);
                Assert.That(finished, Is.EqualTo(1), "reverse Finished once");
                Assert.That(played, Is.EqualTo(expected), $"head: {Describe(played)}\nsweep: {Describe(expected)}");
            }, 300);
        }

        // ---- reverse against forward ----

        [Test]
        public void S5_ReverseIsTheFlippedForward()
        {
            ForEachSeekable((c, rng) =>
            {
                var rig = c.Rig;
                var all = rig.Forward(-1, rig.R.Total);
                for (int w = 0; w < WindowsPerProgram; w++)
                {
                    long lo = RandomPosition(rng, c, all);
                    long hi = RandomPosition(rng, c, all);
                    if (lo > hi)
                        (lo, hi) = (hi, lo);
                    if (w == 0)
                        (lo, hi) = (rig.R.RootDelay, rig.R.Total + 1);
                    var reverse = rig.Reverse(lo, hi);
                    var forward = rig.Forward(lo - 1, hi - 1);
                    forward.RemoveAll(e => e.Kind == TimelineEventKind.Finished || e.Kind == TimelineEventKind.SignalWaitBegan);
                    var expected = forward.ConvertAll(e => e.Flipped());
                    expected.Reverse();
                    Assert.That(reverse, Is.EqualTo(expected), $"[{lo}, {hi})\nreverse: {Describe(reverse)}\nexpected: {Describe(expected)}");
                }
            }, 300);
        }

        [Test]
        public void Generator_CoversTheHardCases()
        {
            int zeroPeriodFinite = 0, backgroundRepeat = 0, truncated = 0, nested = 0, notSeekable = 0;
            for (int seed = 0; seed < Programs; seed++)
            {
                var c = Generate(seed);
                var p = c.Rig.P;
                var r = c.Rig.R;
                if (!r.IsSeekable)
                {
                    notSeekable++;
                    continue;
                }
                bool zp = false, bgr = false, tr = false, ne = false;
                for (int b = 0; b < p.BlockCount; b++)
                {
                    zp |= p.BlockIterations(b) > 1 && r.BlockPeriod(b) == 0;
                    bgr |= p.NodeInBackground(p.BlockNode(b));
                    ne |= p.BlockDepth(b) >= 2;
                }
                for (int n = 0; n < p.NodeCount; n++)
                    tr |= p.NodeBackground(n) && !r.NodeCancelled(n) && r.NodeEnd(n) == r.Total && r.NodeLocalEnd(n) < r.NodeLocalStart(n) + (p.IsBlockNode(n) ? TimelineTime.Infinite : r.ItemDuration(p.NodeItem(n)));
                zeroPeriodFinite += zp ? 1 : 0;
                backgroundRepeat += bgr ? 1 : 0;
                truncated += tr ? 1 : 0;
                nested += ne ? 1 : 0;
            }
            TestContext.WriteLine($"zero-period finite {zeroPeriodFinite} · background repeat {backgroundRepeat} · truncated background {truncated} · nested {nested} · not seekable {notSeekable}");
            Assert.That(zeroPeriodFinite, Is.GreaterThanOrEqualTo(5));
            Assert.That(backgroundRepeat, Is.GreaterThanOrEqualTo(5));
            Assert.That(truncated, Is.GreaterThanOrEqualTo(5));
            Assert.That(nested, Is.GreaterThanOrEqualTo(5));
            Assert.That(notSeekable, Is.GreaterThanOrEqualTo(1));
        }
    }
}
