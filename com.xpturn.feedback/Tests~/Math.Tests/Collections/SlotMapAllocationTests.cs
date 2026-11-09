using System;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Collections
{
    // two warm-up runs, the third measured (TimelineAllocationTests' shape) — but each run gets a freshly reserved map built
    // outside the measured run, or the first warm-up would absorb a growth.
    // CoreCLR's 0 B is a necessary condition only — Mono · IL2CPP are the scheduler's and the player's allocation tests.
    public sealed class SlotMapAllocationTests
    {
        const int N = 256;
        static readonly object Runner = new object();
        static readonly object[] Escaped = new object[1000];

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

        static SlotMap<SlotItem> Reserved()
        {
            var map = new SlotMap<SlotItem>();
            map.Reserve(N);
            return map;
        }

        // one frame's worth within the reservation: add, walk, every playback stops and starts again in the same frame,
        // walk, remove by handle and by index, compact — then empty the map. The checksum leaves generations out (the
        // domain counter differs per run)
        static long Run(SlotMap<SlotItem> map, SlotHandle[] h)
        {
            long sum = 0;
            for (int i = 0; i < N; i++)
                h[i] = map.Add(new SlotItem(i, Runner));
            for (int i = 0; i < map.Count; i++)
                if (map.IsAlive(i))
                    sum += map.ItemAt(i).Id;
            for (int i = 0; i < N; i++)
            {
                map.Remove(h[i]);
                h[i] = map.Add(new SlotItem(i, Runner));
            }
            for (int i = 0; i < map.Count; i++)
                if (map.IsAlive(i))
                    sum += map.ItemAt(i).Id + map.HandleAt(i).Index;
            for (int i = 0; i < N; i += 3)
                map.Remove(h[i]);
            for (int i = 0; i < map.Count; i += 5)
                map.RemoveAt(i);
            map.Compact();
            sum += map.Count + map.LiveCount;
            for (int i = 0; i < map.Count; i++)
                map.RemoveAt(i);
            map.Compact();
            return sum + map.Count;
        }

        // positive control: 1,000 × new object() = 24,000 B on 64-bit
        [Test]
        public void ST9_CounterSeesEscapingAllocations()
        {
            Assert.That(Allocated(() =>
            {
                for (int i = 0; i < Escaped.Length; i++)
                    Escaped[i] = new object();
                return Escaped.Length;
            }), Is.EqualTo(24_000));
        }

        [Test]
        public void ST9_ReservedOperationsAllocateNothing()
        {
            var maps = new[] { Reserved(), Reserved(), Reserved() };
            var h = new SlotHandle[N];
            int run = 0;
            Assert.That(Allocated(() => Run(maps[run++], h)), Is.Zero);
        }
    }
}
