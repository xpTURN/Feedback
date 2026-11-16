using System;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // a graph's recorded frames, oldest first: the nominal place, the value, the line's value there (when compared), whether
    // the play was off the nominal axis and whether the two differed — the oldest overwritten past the size
    internal sealed class RecordRing
    {
        internal const int Size = 2048;

        readonly long[] m_Place = new long[Size];
        readonly Vector4[] m_Value = new Vector4[Size];
        readonly Vector4[] m_Line = new Vector4[Size];
        readonly bool[] m_Compared = new bool[Size];
        readonly bool[] m_Off = new bool[Size];
        int m_Head;

        internal int Count { get; private set; }

        // a compared frame differed from the line since the clear
        internal bool Differs { get; private set; }

        internal void Clear()
        {
            m_Head = 0;
            Count = 0;
            Differs = false;
        }

        internal void Add(long place, in Vector4 value, bool off, bool compared, in Vector4 line, bool differs)
        {
            m_Place[m_Head] = place;
            m_Value[m_Head] = value;
            m_Line[m_Head] = line;
            m_Compared[m_Head] = compared;
            m_Off[m_Head] = off;
            m_Head = (m_Head + 1) % Size;
            if (Count < Size)
                Count++;
            Differs |= differs;
        }

        int At(int i) => (m_Head - Count + i + Size) % Size;

        internal double Seconds(int i) => TimelineTime.ToSeconds(m_Place[At(i)]);

        internal Vector4 Value(int i) => m_Value[At(i)];

        internal bool Compared(int i, out Vector4 line)
        {
            line = m_Line[At(i)];
            return m_Compared[At(i)];
        }

        internal bool Off(int i) => m_Off[At(i)];
    }

    // one graph's record from its follow: the slot graph's composed value in the line's form against the shadow's, an item's
    // own value against its line at that place
    internal sealed class RecordSeries
    {
        // the compared forms' gap that counts as a difference
        internal const float Tolerance = 1e-4f;

        internal readonly RecordRing Ring = new RecordRing();
        Vector3 m_LastLive;
        Vector3 m_LastShadow;

        internal void Clear()
        {
            Ring.Clear();
            m_LastLive = default;
            m_LastShadow = default;
        }

        static bool Apart(in Vector4 a, in Vector4 b, int dimension)
        {
            for (int c = 0; c < dimension && c < 4; c++)
            {
                if (!(Math.Abs(a[c] - b[c]) <= Tolerance))
                    return true;
            }
            return false;
        }

        static Vector4 Formed(SlotGraphModel model, LayerSlot slot, in Vector4 start, ref Vector3 last)
        {
            Vector4 v = slot.GraphValue;
            Vector4 d = SlotGraphModel.Form(model.Composition, model.Toggle, model.Dimension, in v, in start, ref last, out float size);
            if (model.Capped)
                d.w = size;
            return d;
        }

        // the frame of the slot model's shown group: the live slot's value less its start — this frame's base, or for a group
        // with a Persist item the play's start the holder keeps — against the shadow's the same way. False: no slot
        internal bool Slot(SlotGraphModel model, RecordFollow follow)
        {
            SlotGroup group = model?.Shown;
            if (group == null || follow?.Live == null)
                return false;
            LayerSlot live = follow.SlotOf(follow.Live, group.Key);
            if (live == null)
                return false;
            LayerSlot start = model.PersistGroup ? follow.SlotOf(follow.Holder, group.Key) : null;
            Vector4 d = Formed(model, live, start != null ? start.GraphValue : live.BaseVector, ref m_LastLive);
            LayerSlot shadow = follow.SlotOf(follow.Shadow, group.Key);
            Vector4 e = default;
            bool compared = false;
            if (shadow != null)
            {
                e = Formed(model, shadow, start != null ? start.GraphValue : shadow.BaseVector, ref m_LastShadow);
                compared = follow.Comparable;
            }
            int dimension = model.Dimension + (model.Capped ? 1 : 0);
            Ring.Add(follow.Place, in d, follow.OffNominal, compared, in e, compared && Apart(in d, in e, dimension));
            return true;
        }

        // an item's frame: what its binding evaluated at the time the live pass gave it, against the item's line at that place
        // when the line is of this play. False: the item does not run
        internal bool Item(ItemGraphModel model, RecordFollow follow, int listIndex, int channel)
        {
            if (model == null || model.Channels.Count == 0 || follow?.Live == null)
                return false;
            int item = follow.ProgramItem(listIndex);
            if (item < 0 || !follow.Live.RecordTime(follow.Record, item, out MotionTime time))
                return false;
            ItemBinding binding = follow.Live.Binding(item);
            if (binding == null || !binding.GraphRecord(follow.Record, channel, in time, out Vector4 v))
                return false;
            Vector4 line = default;
            bool compared = false;
            if (model.Play == follow.Copy && model.ValueAt != null && follow.Frames > 1 && !follow.OffNominal)
            {
                Vector4? at = model.ValueAt(TimelineTime.ToSeconds(follow.Place));
                if (at.HasValue)
                {
                    line = at.Value;
                    compared = true;
                }
            }
            Ring.Add(follow.Place, in v, follow.OffNominal, compared, in line, compared && Apart(in v, in line, model.Dimension));
            return true;
        }
    }
}
