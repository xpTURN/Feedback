using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a state toggle — no Revert · Apply cycle. The Update stage writes the wanted state only when it differs from the actual
    // one: the latest started contribution of weight > 0, or the value before the episode. An actual value other than the one
    // last seen is another's write — the new before. The end, Close and a release put the before back by the same rule. A
    // concrete toggle gives its state's Read · Write · IsAlive
    internal abstract class ToggleSlot : LayerSlot
    {
        // start order in the ledger; wanted states and weights by handle number (the value slot's shape)
        readonly SlotMap<LedgerEntry> m_Ledger = new SlotMap<LedgerEntry>();
        bool[] m_Wanted = Array.Empty<bool>();
        float[] m_Weights = Array.Empty<float>();
        // the episode — from the first Begin to the apply that handled the end
        bool m_Open;
        // the before is taken at the episode's first apply
        bool m_Captured;
        bool m_Before;
        // the value last seen — the one taken, then our write read back
        bool m_Seen;
        // inside our own write — a Close there waits for the registry's Close after the write
        bool m_Writing;

        protected ToggleSlot()
            : base(ConsumptionStage.Update)
        {
        }

        protected abstract bool Read();

        protected abstract void Write(bool value);

        // the episode is open — Revert keeps the slot until the apply that handles the end
        internal override bool Pending => m_Open;

        internal override int LiveContributions => m_Ledger.LiveCount;

        internal override int Reserved => m_Ledger.Reserved;

        // growth on the playback path
        internal int GrowthCount => m_Ledger.GrowthCount;

        internal override Vector4 BaseVector => new Vector4(Base ? 1f : 0f, 0f, 0f, 0f);

        // the before while a captured episode is open — a Set's snapshot is not the state a blink holds
        internal bool Base => (Detached || (m_Open && m_Captured)) ? m_Before : Read();

        internal bool ReadValue() => Read();

        // Persist. In a captured episode only the before moves and the next apply writes — no pair of callbacks for the
        // opposite state, no nested SetActive from a callback. Otherwise written at once
        internal void WriteValue(bool value)
        {
            if (m_Open && m_Captured)
            {
                m_Before = value;
                return;
            }
            RecordOnce();
            Write(value);
        }

        // the preparation path (allocation allowed): the ledger takes slots ≥ twice the sum, the side arrays follow it
        internal void Reserve(int count)
        {
            m_Ledger.Reserve(count);
            FitSide();
        }

        internal override void Unreserve(int count) => m_Ledger.Unreserve(Math.Min(count, m_Ledger.Reserved));

        void FitSide()
        {
            int length = m_Ledger.Capacity;
            if (m_Wanted.Length >= length)
                return;
            Array.Resize(ref m_Wanted, length);
            Array.Resize(ref m_Weights, length);
        }

        // reads nothing — a destroyed target does not throw here
        internal ContributionHandle Begin()
        {
            if (Detached || Registry == null || Registry.IsClosed)
                return default;
            if (!m_Open)
            {
                m_Open = true;
                m_Captured = false;
            }
            int growth = m_Ledger.GrowthCount;
            SlotHandle h = m_Ledger.Add(new LedgerEntry { Kind = ContributionKind.Override });
            if (m_Ledger.GrowthCount != growth)
            {
                FitSide();
                Registry.LedgerGrew(this);
            }
            m_Wanted[h.Index] = false;
            m_Weights[h.Index] = 0f;
            if (!Active)
                Registry.Activate(this);
            return new ContributionHandle(h);
        }

        internal bool SetOverride(ContributionHandle h, bool wanted, float weight)
        {
            if (Detached || !m_Ledger.IsValid(h.Slot))
                return false;
            m_Wanted[h.Slot.Index] = wanted;
            m_Weights[h.Slot.Index] = weight;
            return true;
        }

        internal bool End(ContributionHandle h) => !Detached && m_Ledger.Remove(h.Slot);

        internal override void EndAll()
        {
            for (int i = 0; i < m_Ledger.Count; i++)
            {
                if (m_Ledger.IsAlive(i))
                    m_Ledger.RemoveAt(i);
            }
        }

        // a released toggle is put back here — the registry's Revert drops it right after
        internal override void Revert()
        {
            m_Ledger.Compact();
            if (ReleasePending)
                Close();
        }

        internal override void Apply()
        {
            if (!m_Open)
                return;
            bool actual = Read();
            if (!m_Captured)
            {
                m_Before = actual;
                m_Seen = actual;
                m_Captured = true;
            }
            else if (actual != m_Seen)
            {
                // another's write — the new before
                m_Before = actual;
            }
            bool wanted = m_Before;
            if (m_Ledger.LiveCount == 0)
            {
                m_Open = false;
                m_Captured = false;
            }
            else
            {
                // lit at weight > 0 — the latest started wins
                int count = m_Ledger.Count;
                for (int i = 0; i < count; i++)
                {
                    if (!m_Ledger.IsAlive(i))
                        continue;
                    int n = m_Ledger.HandleAt(i).Index;
                    if (m_Weights[n] > 0f)
                        wanted = m_Wanted[n];
                }
            }
            if (wanted == actual)
            {
                m_Seen = actual;
                return;
            }
            RecordOnce();
            m_Writing = true;
            try
            {
                Write(wanted);
            }
            finally
            {
                m_Writing = false;
            }
            m_Seen = Read();
        }

        // the end's rule: the before goes back unless another wrote. Inside our own write nothing — the registry calls it
        // again once the write is over
        internal override void Close()
        {
            if (m_Writing || !m_Open)
                return;
            m_Open = false;
            if (!m_Captured)
                return;
            m_Captured = false;
            bool actual = Read();
            if (actual == m_Seen && actual != m_Before)
            {
                RecordOnce();
                Write(m_Before);
            }
        }
    }
}
