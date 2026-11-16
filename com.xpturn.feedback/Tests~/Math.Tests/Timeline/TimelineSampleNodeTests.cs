using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Timeline
{
    // one node sampled alone is what Sample gives that node: every node of the random programs, both directions, at every
    // event's time and its neighbours, the ends and random places — the node's state, its block's iteration and its item's
    // local time and seconds. No allocation
    public sealed class TimelineSampleNodeTests
    {
        static IEnumerable<long> Places(SamplerRig rig, Random rng)
        {
            long total = rig.R.Total;
            yield return -1;
            yield return 0;
            if (TimelineTime.IsInfinite(total))
            {
                for (int i = 0; i < 24; i++)
                    yield return rng.Next(0, 64 * 40) * 15625L;
                yield break;
            }
            yield return total - 1;
            yield return total;
            yield return total + 1;
            foreach (TimelineEvent e in rig.Forward(-1, total))
            {
                yield return e.Time - 1;
                yield return e.Time;
                yield return e.Time + 1;
            }
            for (int i = 0; i < 16; i++)
                yield return (long)(rng.NextDouble() * (total + 2)) - 1;
        }

        static void Same(SamplerRig rig, TimelineSample all, TimelineSample one, int node, long at, bool reverse, int seed)
        {
            string where = $"seed {seed} node {node} at {at} {(reverse ? "reverse" : "forward")}";
            Assert.That(one.NodeStage(node), Is.EqualTo(all.NodeStage(node)), where + " stage");
            Assert.That(one.NodeOccurrence(node), Is.EqualTo(all.NodeOccurrence(node)), where + " occurrence");
            Assert.That(one.NodeStart(node), Is.EqualTo(all.NodeStart(node)), where + " start");
            Assert.That(one.NodeNaturalEnd(node), Is.EqualTo(all.NodeNaturalEnd(node)), where + " natural end");
            Assert.That(one.NodeEffectiveEnd(node), Is.EqualTo(all.NodeEffectiveEnd(node)), where + " effective end");
            int block = rig.P.NodeBlock(node);
            if (block >= 0)
                Assert.That(one.BlockScopeOccurrence(block), Is.EqualTo(all.BlockScopeOccurrence(block)), where + " block iteration");
            int item = rig.P.NodeItem(node);
            if (item >= 0)
            {
                Assert.That(one.LocalTime(item), Is.EqualTo(all.LocalTime(item)), where + " local time");
                Assert.That(one.Seconds(item), Is.EqualTo(all.Seconds(item)), where + " seconds");
                Assert.That(one.LocalTimeFromEnd(item), Is.EqualTo(all.LocalTimeFromEnd(item)), where + " local time from the end");
                Assert.That(one.SecondsFromEnd(item), Is.EqualTo(all.SecondsFromEnd(item)), where + " seconds from the end");
            }
        }

        [Test]
        public void N1_ANodeAloneIsTheSamplesNode()
        {
            int checkedNodes = 0;
            for (int seed = 0; seed < TimelineSweepPropertyTests.Programs; seed++)
            {
                var c = TimelineSweepPropertyTests.Generate(seed);
                SamplerRig rig = c.Rig;
                var rng = new Random(seed);
                var one = new TimelineSample(rig.P);
                foreach (long at in Places(rig, rng))
                {
                    foreach (bool reverse in new[] { false, true })
                    {
                        TimelineSample all = rig.At(at, reverse);
                        for (int node = 0; node < rig.P.NodeCount; node++)
                        {
                            rig.Sampler.SampleNode(node, at, reverse, one);
                            Same(rig, all, one, node, at, reverse, seed);
                            checkedNodes++;
                        }
                    }
                }
            }
            TestContext.WriteLine($"{checkedNodes} node samples");
            Assert.That(checkedNodes, Is.GreaterThan(10000), "the property covered the programs");
        }

        [Test]
        public void N2_ANodeAloneAllocatesNothing()
        {
            var c = TimelineSweepPropertyTests.Generate(7);
            SamplerRig rig = c.Rig;
            var one = new TimelineSample(rig.P);
            long Run()
            {
                long sum = 0;
                for (long at = -1; at < 4000000L; at += 15625L)
                {
                    for (int node = 0; node < rig.P.NodeCount; node++)
                    {
                        rig.Sampler.SampleNode(node, at, false, one);
                        sum += one.NodeOccurrence(node) + one.NodeStart(node);
                    }
                }
                return sum;
            }
            long first = Run();
            Run();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long last = Run();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(last, Is.EqualTo(first), "the measured run repeats the warm-up");
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated");
        }
    }
}
