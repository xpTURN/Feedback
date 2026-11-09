using System;

namespace xpTURN.Feedback
{
    // a dense slot array in start order plus a handle table. The map keeps each slot's ledger — its handle number, or
    // dead — and T is the caller's contents. Removal only marks, once per slot; Compact pulls the live slots forward in
    // order when the caller says. Adds always go to the end, so slot numbers hold until Compact — the scheduler's pass
    // limit rests on that. The asserts compile only in the dotnet build (MathAssert), so in Unity a contract violation
    // takes the safe path: a slot past Count reads as dead, an excess Unreserve stops at 0.
    internal sealed class SlotMap<T> where T : struct
    {
        struct Entry
        {
            public T Item;
            public int Handle;      // handle number + 1, 0 = dead — a new or cleared slot is dead
        }

        const int MinLength = 4;    // the first growth of an empty array — doubling 0 stays 0

        Entry[] _entries = Array.Empty<Entry>();
        int _count;
        int _live;
        int _reserved;

        int[] _dense = Array.Empty<int>();          // handle number → slot index
        uint[] _generation = Array.Empty<uint>();   // handle number → generation, 0 = free
        int[] _free = Array.Empty<int>();           // free handle numbers — a stack
        int _freeCount;

        public int Count => _count;         // dead slots included — the scheduler's pass limit
        public int LiveCount => _live;
        public int Reserved => _reserved;
        public int Capacity => _entries.Length;

        // Adds that grew an array — one per Add even when both grow. Reserve's growth is not counted
        public int GrowthCount { get; private set; }

        public SlotHandle Add(in T item)
        {
            bool grew = false;
            if (_count == _entries.Length)
            {
                GrowEntries(Math.Max(MinLength, _entries.Length * 2));
                grew = true;
            }
            if (_freeCount == 0)
            {
                GrowHandles(Math.Max(MinLength, _generation.Length * 2));
                grew = true;
            }
            if (grew)
                GrowthCount++;

            int number = _free[--_freeCount];
            uint generation = SlotGeneration.Next();
            _generation[number] = generation;
            _dense[number] = _count;
            _entries[_count] = new Entry { Item = item, Handle = number + 1 };
            _count++;
            _live++;
            return new SlotHandle(number, generation);
        }

        // generation 0 first — a free number's generation is 0, and default must not match it
        public bool IsValid(SlotHandle handle)
            => handle.Generation != 0
               && (uint)handle.Index < (uint)_generation.Length
               && _generation[handle.Index] == handle.Generation;

        public bool TryGetIndex(SlotHandle handle, out int index)
        {
            if (!IsValid(handle))
            {
                index = -1;
                return false;
            }
            index = _dense[handle.Index];
            return true;
        }

        public bool Remove(SlotHandle handle)
        {
            if (!IsValid(handle))
                return false;
            Kill(_dense[handle.Index], handle.Index);
            return true;
        }

        // a dead slot holds no number, so a second removal cannot free a number a new playback has taken
        public bool RemoveAt(int index)
        {
            MathAssert.That((uint)index < (uint)_count, "slot index out of range");
            int code = _entries[index].Handle;
            if (code == 0)
                return false;
            Kill(index, code - 1);
            return true;
        }

        void Kill(int index, int number)
        {
            _entries[index].Handle = 0;
            _generation[number] = 0;
            _free[_freeCount++] = number;
            _live--;
        }

        public bool IsAlive(int index)
        {
            MathAssert.That((uint)index < (uint)_count, "slot index out of range");
            return _entries[index].Handle != 0;
        }

        // live and dead slots alike — contents stay until Compact. Read-only: contents never change after Add and death is
        // recorded by number, so a ref held across an array growth can only read, and reading the old array is still right
        public ref readonly T ItemAt(int index)
        {
            MathAssert.That((uint)index < (uint)_count, "slot index out of range");
            return ref _entries[index].Item;
        }

        public SlotHandle HandleAt(int index)
        {
            MathAssert.That((uint)index < (uint)_count, "slot index out of range");
            int code = _entries[index].Handle;
            if (code == 0)
                return default;
            int number = code - 1;
            return new SlotHandle(number, _generation[number]);
        }

        // stable: the live slots keep their order and the handle table follows them. The vacated tail goes back to default
        // so contents holding references (runners) can be collected
        public void Compact()
        {
            if (_live == _count)
                return;
            int write = 0;
            for (int read = 0; read < _count; read++)
            {
                int code = _entries[read].Handle;
                if (code == 0)
                    continue;
                if (write != read)
                {
                    _entries[write] = _entries[read];
                    _dense[code - 1] = write;
                }
                write++;
            }
            Array.Clear(_entries, write, _count - write);
            _count = write;
        }

        // the initialization path (allocation allowed): slots ≥ twice the reserved sum, handles ≥ the sum. Grows by doubling
        // at least — one Reserve(1) per player at scene load would otherwise rebuild the arrays on every call
        public void Reserve(int count)
        {
            MathAssert.That(count >= 0, "negative reserve");
            if (count <= 0)
                return;
            _reserved += count;
            if (_entries.Length < _reserved * 2)
                GrowEntries(Math.Max(_reserved * 2, _entries.Length * 2));
            if (_generation.Length < _reserved)
                GrowHandles(Math.Max(_reserved, _generation.Length * 2));
        }

        // the sum only — the arrays stay
        public void Unreserve(int count)
        {
            MathAssert.That(count >= 0 && count <= _reserved, "unreserve beyond the reservation");
            if (count <= 0)
                return;
            _reserved = Math.Max(0, _reserved - count);
        }

        void GrowEntries(int length)
        {
            var entries = new Entry[length];
            Array.Copy(_entries, entries, _count);
            _entries = entries;
        }

        // new numbers go on top of the free stack in reverse, so a fresh range hands out its smallest number first
        void GrowHandles(int length)
        {
            int old = _generation.Length;
            Array.Resize(ref _generation, length);
            Array.Resize(ref _dense, length);
            Array.Resize(ref _free, length);
            for (int number = length - 1; number >= old; number--)
                _free[_freeCount++] = number;
        }
    }
}
