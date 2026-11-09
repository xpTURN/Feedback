using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Collections
{
    // random operation sequences against a list model. Live slots stay within a few hundred (Add is less likely as they
    // grow) so the per-operation comparison stays in seconds. Operations stay inside the contract — Debug turns a
    // violation into an exception. A failure prints the seed and the step: move it to SlotMapTests.
    public sealed class SlotMapPropertyTests
    {
        sealed class ModelSlot
        {
            public int Id;
            public SlotHandle Handle;
            public bool Alive;
        }

        const int Steps = 100_000;

        [TestCase(1), TestCase(2), TestCase(3), TestCase(4)]
        public void ST8_MatchesTheListModel(int seed)
        {
            var rng = new Random(seed);
            var map = new SlotMap<SlotItem>();
            var other = new SlotMap<SlotItem>();
            other.Reserve(8);
            var foreign = new List<SlotHandle>();
            for (int i = 0; i < 8; i++)
                foreign.Add(other.Add(new SlotItem(-1)));
            var model = new List<ModelSlot>();
            var ended = new List<SlotHandle>();
            int reserved = 0, nextId = 0, live = 0;

            for (int step = 0; step < Steps; step++)
            {
                int op = rng.Next(100);
                if (op < 40)
                {
                    if (rng.Next(400) >= live)
                    {
                        int id = nextId++;
                        var h = map.Add(new SlotItem(id));
                        model.Add(new ModelSlot { Id = id, Handle = h, Alive = true });
                        live++;
                    }
                }
                else if (op < 55)
                {
                    if (live > 0)
                    {
                        ModelSlot s;
                        do s = model[rng.Next(model.Count)]; while (!s.Alive);
                        if (!map.Remove(s.Handle))
                            Fail(seed, step, "Remove(live) returned false");
                        s.Alive = false;
                        live--;
                        ended.Add(s.Handle);
                    }
                }
                else if (op < 62)
                {
                    if (ended.Count > 0 && map.Remove(ended[rng.Next(ended.Count)]))
                        Fail(seed, step, "Remove(ended) returned true");
                }
                else if (op < 66)
                {
                    if (map.Remove(foreign[rng.Next(foreign.Count)]))
                        Fail(seed, step, "Remove(foreign) returned true");
                }
                else if (op < 80)
                {
                    if (model.Count > 0)
                    {
                        int i = rng.Next(model.Count);
                        bool was = model[i].Alive;
                        if (map.RemoveAt(i) != was)
                            Fail(seed, step, "RemoveAt result");
                        if (was)
                        {
                            model[i].Alive = false;
                            live--;
                            ended.Add(model[i].Handle);
                        }
                    }
                }
                else if (op < 90)
                {
                    map.Compact();
                    model.RemoveAll(s => !s.Alive);
                }
                else if (op < 95)
                {
                    int k = rng.Next(1, 5);
                    map.Reserve(k);
                    reserved += k;
                }
                else if (reserved > 0)
                {
                    int k = rng.Next(1, Math.Min(4, reserved) + 1);
                    map.Unreserve(k);
                    reserved -= k;
                }
                Check(map, model, ended, live, reserved, rng, seed, step);
            }
        }

        // order · counts · IsAlive · handle validity and index after every operation; HandleAt, dead contents, Reserved and
        // the capacity invariant — HandleAt's generation has no other check
        static void Check(SlotMap<SlotItem> map, List<ModelSlot> model, List<SlotHandle> ended, int live, int reserved, Random rng, int seed, int step)
        {
            if (map.Count != model.Count)
                Fail(seed, step, "Count");
            if (map.LiveCount != live)
                Fail(seed, step, "LiveCount");
            if (map.Reserved != reserved)
                Fail(seed, step, "Reserved");
            if (map.Capacity < Math.Max(map.Count, 2 * reserved))
                Fail(seed, step, "capacity below twice the reservation");
            for (int i = 0; i < model.Count; i++)
            {
                var s = model[i];
                if (map.IsAlive(i) != s.Alive)
                    Fail(seed, step, "IsAlive");
                if (map.ItemAt(i).Id != s.Id)
                    Fail(seed, step, "order or contents");
                var h = map.HandleAt(i);
                if (s.Alive)
                {
                    if (!map.IsValid(s.Handle))
                        Fail(seed, step, "live handle invalid");
                    if (!map.TryGetIndex(s.Handle, out int at) || at != i)
                        Fail(seed, step, "slot index");
                    if (h.Index != s.Handle.Index || h.Generation != s.Handle.Generation)
                        Fail(seed, step, "HandleAt");
                }
                else if (h.Generation != 0)
                {
                    Fail(seed, step, "HandleAt of a dead slot");
                }
            }
            for (int k = 0; k < 3 && ended.Count > 0; k++)
                if (map.IsValid(ended[rng.Next(ended.Count)]))
                    Fail(seed, step, "ended handle valid");
        }

        static void Fail(int seed, int step, string what) => Assert.Fail($"seed {seed} step {step}: {what}");
    }
}
