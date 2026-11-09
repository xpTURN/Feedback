using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Collections
{
    // the contents of a slot — a reference (a runner) and a number, as Core's PlaybackSlot is
    internal readonly struct SlotItem
    {
        public readonly object Ref;
        public readonly int Id;

        public SlotItem(int id, object reference = null)
        {
            Id = id;
            Ref = reference;
        }
    }

    // hand cases. Generation values are never asserted — the counter is shared by every map in the domain.
    public sealed class SlotMapTests
    {
        static SlotMap<SlotItem> Reserved(int count)
        {
            var map = new SlotMap<SlotItem>();
            map.Reserve(count);
            return map;
        }

        static int[] Ids(SlotMap<SlotItem> map)
        {
            var ids = new int[map.Count];
            for (int i = 0; i < map.Count; i++)
                ids[i] = map.IsAlive(i) ? map.ItemAt(i).Id : -1;
            return ids;
        }

        static void AssertAt(SlotMap<SlotItem> map, SlotHandle handle, int index, int id)
        {
            Assert.That(map.TryGetIndex(handle, out int at), Is.True, "handle valid");
            Assert.That(at, Is.EqualTo(index), "slot index");
            Assert.That(map.ItemAt(at).Id, Is.EqualTo(id), "contents");
        }

        // an Add between the removal and Compact keeps the dead slot in play (a map that reused it would pass without one)
        [Test]
        public void ST1_CompactKeepsStartOrder()
        {
            var map = Reserved(8);
            var h = new SlotHandle[6];
            for (int i = 0; i < 5; i++)
                h[i] = map.Add(new SlotItem(i));
            Assert.That(map.Remove(h[2]), Is.True);
            h[5] = map.Add(new SlotItem(5));
            map.Compact();
            Assert.That(Ids(map), Is.EqualTo(new[] { 0, 1, 3, 4, 5 }));
            AssertAt(map, h[0], 0, 0);
            AssertAt(map, h[1], 1, 1);
            AssertAt(map, h[3], 2, 3);
            AssertAt(map, h[4], 3, 4);
            AssertAt(map, h[5], 4, 5);
        }

        // the other map has the same history, so only the generation tells the handles apart
        [Test]
        public void ST2_EndedDefaultAndForeignHandlesDoNothing()
        {
            var map = Reserved(4);
            var a = map.Add(new SlotItem(1));
            var keep = map.Add(new SlotItem(2));
            Assert.That(map.Remove(a), Is.True);
            Assert.That(map.IsValid(a), Is.False);
            Assert.That(map.Remove(a), Is.False);
            Assert.That(map.IsValid(default), Is.False);
            Assert.That(map.Remove(default), Is.False);
            var b = map.Add(new SlotItem(3));
            Assert.That(b.Index, Is.EqualTo(a.Index), "the freed number is reused");
            Assert.That(map.IsValid(b), Is.True);
            Assert.That(map.IsValid(a), Is.False, "the old handle stays ended");

            var other = Reserved(4);
            var o = other.Add(new SlotItem(9));
            other.Add(new SlotItem(10));
            other.Remove(o);
            o = other.Add(new SlotItem(11));
            Assert.That(o.Index, Is.EqualTo(b.Index));
            Assert.That(map.IsValid(o), Is.False);
            Assert.That(map.Remove(o), Is.False);
            Assert.That(other.IsValid(b), Is.False);
            Assert.That(map.LiveCount, Is.EqualTo(2));
            Assert.That(map.IsValid(keep) && map.IsValid(b), Is.True);
        }

        // default matches a free number's generation 0 unless generation 0 is filtered first
        [Test]
        public void ST2_RemovingDefaultFreesNothing()
        {
            var map = Reserved(4);
            Assert.That(map.Remove(default), Is.False);
            var x = map.Add(new SlotItem(1));
            var y = map.Add(new SlotItem(2));
            Assert.That(x.Index, Is.Not.EqualTo(y.Index));
            Assert.That(map.LiveCount, Is.EqualTo(2));
        }

        // a number beyond this map's table, a negative number, and TryGetIndex's failure value
        [Test]
        public void ST2_OutOfRangeHandlesAreInvalid()
        {
            var map = Reserved(2);
            map.Add(new SlotItem(1));
            var big = Reserved(64);
            SlotHandle far = default;
            for (int i = 0; i < 40; i++)
                far = big.Add(new SlotItem(i));
            Assert.That(map.IsValid(far), Is.False);
            Assert.That(map.Remove(far), Is.False);
            var negative = new SlotHandle(-1, 5);
            Assert.That(map.IsValid(negative), Is.False);
            Assert.That(map.TryGetIndex(negative, out int index), Is.False);
            Assert.That(index, Is.EqualTo(-1));
        }

        // a fresh range hands out 0, 1, 2 …, and the most recently freed number comes first
        [Test]
        public void ST2_FreeNumbersComeInOrder()
        {
            var map = Reserved(4);
            Assert.That(map.Add(new SlotItem(0)).Index, Is.EqualTo(0));
            Assert.That(map.Add(new SlotItem(1)).Index, Is.EqualTo(1));
            var c = map.Add(new SlotItem(2));
            Assert.That(c.Index, Is.EqualTo(2));
            map.Remove(c);
            Assert.That(map.Add(new SlotItem(3)).Index, Is.EqualTo(2));
        }

        // a removed slot's second removal leaves the table alone, even after its number went to a new playback
        [Test]
        public void ST3_RemovalHappensOncePerSlot()
        {
            var map = Reserved(4);
            var h = map.Add(new SlotItem(1));
            Assert.That(map.TryGetIndex(h, out int i), Is.True);
            Assert.That(map.Remove(h), Is.True);
            Assert.That(map.RemoveAt(i), Is.False);
            Assert.That(map.Remove(h), Is.False);
            var next = map.Add(new SlotItem(2));
            Assert.That(next.Index, Is.EqualTo(h.Index));
            Assert.That(map.RemoveAt(i), Is.False);
            Assert.That(map.IsValid(next), Is.True);
            Assert.That(map.LiveCount, Is.EqualTo(1));
        }

        [Test]
        public void ST4_CompactWithoutDeadSlotsChangesNothing()
        {
            var map = Reserved(4);
            var h0 = map.Add(new SlotItem(0));
            var h1 = map.Add(new SlotItem(1));
            map.Compact();
            Assert.That(map.Count, Is.EqualTo(2));
            Assert.That(Ids(map), Is.EqualTo(new[] { 0, 1 }));
            AssertAt(map, h0, 0, 0);
            AssertAt(map, h1, 1, 1);
        }

        // the reference lives only here — a local of the test method would stay alive to the method's end (tiered JIT)
        [MethodImpl(MethodImplOptions.NoInlining)]
        static SlotHandle AddReferenced(SlotMap<SlotItem> map, int id, out WeakReference weak)
        {
            var reference = new object();
            weak = new WeakReference(reference);
            return map.Add(new SlotItem(id, reference));
        }

        static void Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        // the removed slot becomes the tail, so only the tail clear drops it (removing a front slot, the move would overwrite
        // it)
        [Test]
        public void ST4_CompactClearsTheTail()
        {
            var map = Reserved(4);
            map.Add(new SlotItem(0));
            var h = AddReferenced(map, 1, out var weak);
            map.Remove(h);
            Collect();
            Assert.That(weak.IsAlive, Is.True, "held while the dead slot is not compacted");
            map.Compact();
            Collect();
            Assert.That(weak.IsAlive, Is.False);
        }

        // the cleared tail is a dead slot. Debug: the host turns the assert into an exception first
        [Test]
        public void ST4_ClearedTailIsDead()
        {
            var map = Reserved(4);
            var a = map.Add(new SlotItem(1));
            var b = map.Add(new SlotItem(2));
            var c = map.Add(new SlotItem(3));
            map.Remove(b);
            map.Compact();
#if DEBUG
            Assert.Catch(() => map.RemoveAt(map.Count));
#else
            Assert.That(map.RemoveAt(map.Count), Is.False);
#endif
            Assert.That(map.IsValid(a) && map.IsValid(c), Is.True);
            Assert.That(map.LiveCount, Is.EqualTo(2));
        }

        // twice the reservation covers every playback stopping and starting again in one frame; the handle table's size has
        // no accessor, so 'n Adds grow nothing' stands for it
        [Test]
        public void ST5_ReserveDoublesTheSlots()
        {
            const int n = 100;
            var map = Reserved(n);
            Assert.That(map.Capacity, Is.GreaterThanOrEqualTo(2 * n));
            Assert.That(map.GrowthCount, Is.Zero, "Reserve's growth is not counted");
            var h = new SlotHandle[n];
            for (int i = 0; i < n; i++)
                h[i] = map.Add(new SlotItem(i));
            for (int i = 0; i < n; i++)
            {
                map.Remove(h[i]);
                h[i] = map.Add(new SlotItem(i));
            }
            Assert.That(map.GrowthCount, Is.Zero);
            int capacity = map.Capacity;
            map.Unreserve(n);
            Assert.That(map.Capacity, Is.EqualTo(capacity), "Unreserve keeps the arrays");
        }

        // three shapes of overflow, each counted once; LiveCount > Reserved tells the first two apart
        [Test]
        public void ST5_OverflowCountsOncePerAdd()
        {
            const int n = 50;
            var map = Reserved(n);
            for (int i = 0; i <= n; i++)
                map.Add(new SlotItem(i));
            Assert.That(map.GrowthCount, Is.EqualTo(1), "handle table only — under-reserved");
            Assert.That(map.LiveCount, Is.GreaterThan(map.Reserved));

            var h = new SlotHandle[n];
            map = Reserved(n);
            for (int i = 0; i < n; i++)
                h[i] = map.Add(new SlotItem(i));
            for (int i = 0; i < n; i++)
            {
                map.Remove(h[i]);
                h[i] = map.Add(new SlotItem(i));
            }
            map.Remove(h[0]);
            h[0] = map.Add(new SlotItem(0));
            Assert.That(map.GrowthCount, Is.EqualTo(1), "slot array only — one playback replaced twice");
            Assert.That(map.LiveCount, Is.EqualTo(map.Reserved));

            map = Reserved(n);
            for (int i = 0; i < n; i++)
                h[i] = map.Add(new SlotItem(i));
            for (int i = 0; i < n; i++)
            {
                map.Remove(h[i]);
                h[i] = map.Add(new SlotItem(i));
            }
            int capacity = map.Capacity;
            map.Add(new SlotItem(-1));
            Assert.That(map.GrowthCount, Is.EqualTo(1), "both arrays in one Add");
            Assert.That(map.Capacity, Is.EqualTo(capacity * 2));
        }

        // 5,000 players each reserving one at scene load rebuild the slot array fourteen times
        [Test]
        public void ST5_SequentialReservesGrowByDoubling()
        {
            var map = new SlotMap<SlotItem>();
            int changes = 0, last = map.Capacity;
            for (int i = 0; i < 5000; i++)
            {
                map.Reserve(1);
                if (map.Capacity != last)
                {
                    changes++;
                    last = map.Capacity;
                }
            }
            Assert.That(changes, Is.EqualTo(14));
            Assert.That(map.GrowthCount, Is.Zero);
        }

        // doubling an empty array would stay 0
        [Test]
        public void ST5_EmptyMapFirstAddGrowsToFour()
        {
            var map = new SlotMap<SlotItem>();
            map.Add(new SlotItem(1));
            Assert.That(map.Capacity, Is.EqualTo(4));
            Assert.That(map.GrowthCount, Is.EqualTo(1));
        }

        // split by configuration: Debug sees the assert (as an exception, before any change), Release sees the behavior
        // Unity always has
        [Test]
        public void ST5_UnreserveStopsAtZero()
        {
            var map = Reserved(2);
#if DEBUG
            Assert.Catch(() => map.Unreserve(3));
            Assert.That(map.Reserved, Is.EqualTo(2));
#else
            map.Unreserve(3);
            Assert.That(map.Reserved, Is.Zero);
#endif
        }

        [Test]
        public void ST5_NegativeCountsAreIgnored()
        {
            var map = Reserved(2);
#if DEBUG
            Assert.Catch(() => map.Reserve(-1));
            Assert.Catch(() => map.Unreserve(-1));
#else
            map.Reserve(-1);
            map.Unreserve(-1);
#endif
            Assert.That(map.Reserved, Is.EqualTo(2));
        }

        // the pass limit's premise: Adds go to the end even with a dead slot present, and slot numbers and contents hold
        // across growth by Add and by Reserve
        [Test]
        public void ST6_AddAppendsAndIndicesHold()
        {
            var map = Reserved(2);
            var h = new List<SlotHandle>();
            for (int i = 0; i < 4; i++)
                h.Add(map.Add(new SlotItem(i)));
            map.Remove(h[1]);
            var n = map.Add(new SlotItem(10));
            Assert.That(map.TryGetIndex(n, out int at), Is.True);
            Assert.That(at, Is.EqualTo(map.Count - 1), "appended at the end");
            for (int k = 0; k < 4; k++)
                if (k != 1)
                    AssertAt(map, h[k], k, k);
            for (int i = 0; i < 20; i++)
                map.Add(new SlotItem(100 + i));
            map.Reserve(100);
            for (int k = 0; k < 4; k++)
                if (k != 1)
                    AssertAt(map, h[k], k, k);
            Assert.That(map.IsAlive(1), Is.False);
            AssertAt(map, n, 4, 10);
        }

        static FieldInfo LastGeneration => typeof(SlotGeneration).GetField("s_Last", BindingFlags.NonPublic | BindingFlags.Static);

        // moved just before the wrap; put back afterwards so later tests don't get small generations again
        [Test]
        public void ST7_WrapSkipsGenerationZero()
        {
            var saved = (uint)LastGeneration.GetValue(null);
            try
            {
                LastGeneration.SetValue(null, uint.MaxValue - 1);
                var map = Reserved(4);
                for (int i = 0; i < 3; i++)
                {
                    var h = map.Add(new SlotItem(i));
                    Assert.That(h.Generation, Is.Not.Zero);
                    Assert.That(map.IsValid(h), Is.True);
                }
            }
            finally
            {
                LastGeneration.SetValue(null, saved);
            }
        }

        [Test]
        public void ST7_SameNumberInTwoMapsIsInvalidInEachOther()
        {
            var a = Reserved(2);
            var b = Reserved(2);
            var ha = a.Add(new SlotItem(1));
            var hb = b.Add(new SlotItem(2));
            Assert.That(ha.Index, Is.EqualTo(hb.Index));
            Assert.That(a.IsValid(hb), Is.False);
            Assert.That(b.IsValid(ha), Is.False);
        }
    }
}
