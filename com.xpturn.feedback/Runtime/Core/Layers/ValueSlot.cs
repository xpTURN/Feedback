using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a contribution's fixed part — the SlotMap's contents never change after Add, so the value and the weight live beside it
    internal struct LedgerEntry
    {
        public ContributionKind Kind;
        public LayerSpace Space;
    }

    // a value slot's frame — the ledger, the composition, Revert's judgment on the value read back, the write skipped when
    // nothing changes and the continued base. A concrete slot gives its property's Read · Write · IsAlive (and Offset when it
    // has spaces)
    internal abstract class ValueSlot<T, TOps> : LayerSlot where TOps : struct, IValueOps<T>
    {
        readonly Composition m_Composition;
        readonly bool m_Renormalizes;
        // start order in the ledger; values and weights by handle number, as long as the ledger's slot array — a handle
        // number stays below it
        readonly SlotMap<LedgerEntry> m_Ledger = new SlotMap<LedgerEntry>();
        T[] m_Values = Array.Empty<T>();
        float[] m_Weights = Array.Empty<float>();
        // an override's components — 1 takes the target's, 0 keeps the composition's
        Vector4[] m_Masks = Array.Empty<Vector4>();
        T m_Base;
        T m_Written;
        T m_AfterRevert;
        bool m_Pending;
        bool m_Reverted;

        protected ValueSlot(ConsumptionStage stage, Composition composition, bool renormalizes)
            : base(stage)
        {
            m_Composition = composition;
            m_Renormalizes = renormalizes;
        }

        protected abstract T Read();

        protected abstract void Write(in T value);

        // right before the apply's write — a slot that puts back something other than its base takes it here
        // (a MaterialPropertyBlock's snapshot, the copies' colors)
        protected virtual void BeforeWrite()
        {
        }

        // the revert's write when the judgment holds
        protected virtual void Restore(in T baseValue) => Write(baseValue);

        // the Persist door's write
        protected virtual void WriteDirect(in T value) => Write(value);

        // another's write judged by component — a layout writes the axes it drives; the others still come back (a whole-value
        // judgment would take our offset on them as the new base)
        protected virtual bool JudgesByComponent => false;

        protected virtual T Offset(in T v, in T o, LayerSpace space)
            => m_Composition == Composition.Multiplicative ? default(TOps).MulOffset(v, o) : default(TOps).Add(v, o);

        internal override bool Pending => m_Pending;

        internal override int LiveContributions => m_Ledger.LiveCount;

        internal override int Reserved => m_Ledger.Reserved;

        // growth on the playback path — the ledger's (the side arrays grow with it)
        internal int GrowthCount => m_Ledger.GrowthCount;

        internal int Capacity => m_Ledger.Capacity;

        // Persist and direct writes. The slot's state stays — the next Apply reads the value as its base, and in the window the
        // next Revert takes it as another's write
        internal T ReadValue() => Read();

        internal void WriteValue(in T value)
        {
            RecordOnce();
            // a captured direct episode ends first — the state a shared slot takes Persist in after its Revert
            if (Direct && m_Pending)
                EndDirect();
            WriteDirect(value);
        }

        // the preparation path (allocation allowed): the ledger takes slots ≥ twice the sum, the side arrays follow it
        internal void Reserve(int count)
        {
            m_Ledger.Reserve(count);
            FitSide();
        }

        // through the registry's Release
        internal override void Unreserve(int count) => m_Ledger.Unreserve(Math.Min(count, m_Ledger.Reserved));

        void FitSide()
        {
            int length = m_Ledger.Capacity;
            if (m_Values.Length >= length)
                return;
            Array.Resize(ref m_Values, length);
            Array.Resize(ref m_Weights, length);
            Array.Resize(ref m_Masks, length);
        }

        internal ContributionHandle Begin(ContributionKind kind, LayerSpace space = LayerSpace.Local)
        {
            if (Detached || Registry == null || Registry.IsClosed)
                return default;
            int growth = m_Ledger.GrowthCount;
            SlotHandle h = m_Ledger.Add(new LedgerEntry { Kind = kind, Space = space });
            if (m_Ledger.GrowthCount != growth)
            {
                FitSide();
                Registry.LedgerGrew(this);
            }
            // the composition's identity — a rotation offset read before its first value would turn the target to identity
            m_Values[h.Index] = default(TOps).FromVector4(Vector4.zero);
            m_Weights[h.Index] = 0f;
            m_Masks[h.Index] = Vector4.one;
            if (!Active)
                Registry.Activate(this);
            return new ContributionHandle(h);
        }

        internal bool SetOffset(ContributionHandle h, in T value)
        {
            if (Detached || !m_Ledger.IsValid(h.Slot))
                return false;
            m_Values[h.Slot.Index] = value;
            return true;
        }

        internal bool SetOverride(ContributionHandle h, in T target, float weight)
        {
            if (Detached || !m_Ledger.IsValid(h.Slot))
                return false;
            m_Values[h.Slot.Index] = target;
            m_Weights[h.Slot.Index] = weight;
            return true;
        }

        // an override of some components (a shared slot's part — the sprite's alpha)
        internal bool SetOverride(ContributionHandle h, in T target, float weight, in Vector4 mask)
        {
            if (!SetOverride(h, in target, weight))
                return false;
            m_Masks[h.Slot.Index] = mask;
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

        // the value before composition. A detached slot reads nothing — the binding calls this outside the layers' isolation
        internal T Base
        {
            get
            {
                if (Detached || m_Pending)
                    return m_Base;
                if (m_Renormalizes && m_Reverted && default(TOps).BitEquals(Read(), m_AfterRevert))
                    return m_Base;
                return Read();
            }
        }

        internal override Vector4 BaseVector => default(TOps).ToVector4(Base);

        internal override void Revert()
        {
            m_Ledger.Compact();
            if (Direct)
            {
                if (m_Pending && m_Ledger.LiveCount == 0)
                    EndDirect();
                return;
            }
            if (!m_Pending)
                return;
            m_Pending = false;
            T cur = Read();
            if (JudgesByComponent && !default(TOps).BitEquals(cur, m_Written))
            {
                Vector4 c = default(TOps).ToVector4(cur), w = default(TOps).ToVector4(m_Written), b = default(TOps).ToVector4(m_Base);
                for (int i = 0; i < 4; i++)
                {
                    if (BitConverter.SingleToInt32Bits(c[i]) == BitConverter.SingleToInt32Bits(w[i]))
                        c[i] = b[i];
                }
                m_Base = default(TOps).FromVector4(c);
                Restore(m_Base);
                m_Reverted = false;
            }
            else if (default(TOps).BitEquals(cur, m_Written))
            {
                Restore(m_Base);
                if (m_Renormalizes)
                {
                    m_AfterRevert = Read();
                    m_Reverted = true;
                }
            }
            else
            {
                // another's write — the new base
                m_Base = cur;
                m_Reverted = false;
            }
        }

        internal override void Apply()
        {
            if (Direct)
            {
                ApplyDirect();
                return;
            }
            if (m_Ledger.LiveCount == 0)
                return;
            T now = Read();
            // our own restore read back — the base goes on
            if (!(m_Renormalizes && m_Reverted && default(TOps).BitEquals(now, m_AfterRevert)))
                m_Base = now;
            m_Reverted = false;
            T v = Compose(m_Base);
            if (default(TOps).BitEquals(v, m_Base))
                return;
            RecordOnce();
            BeforeWrite();
            Write(v);
            m_Written = Read();
            m_Pending = true;
        }

        // the episode's first apply takes the snapshot (or the continued base) and BeforeWrite once; every apply writes the
        // composition over it — no read back, no judgment, no skip. No live contribution left: the end
        void ApplyDirect()
        {
            if (m_Ledger.LiveCount == 0)
            {
                if (m_Pending)
                    EndDirect();
                return;
            }
            if (!m_Pending)
            {
                T now = Read();
                if (!(m_Renormalizes && m_Reverted && default(TOps).BitEquals(now, m_AfterRevert)))
                    m_Base = now;
                m_Reverted = false;
                RecordOnce();
                BeforeWrite();
                m_Pending = true;
            }
            Write(Compose(m_Base));
        }

        void EndDirect()
        {
            m_Pending = false;
            Restore(m_Base);
            if (m_Renormalizes)
            {
                m_AfterRevert = Read();
                m_Reverted = true;
            }
        }

        internal override void Close()
        {
            if (Direct)
            {
                if (m_Pending)
                    EndDirect();
                return;
            }
            if (m_Pending)
                Revert();
        }

        // offsets in start order, then overrides in start order. An ended contribution drops out this frame. An override's
        // ends are picked, not computed — weight ≤ 0 skips it, ≥ 1 is the target itself. A masked override takes its masked
        // components only
        T Compose(in T baseValue)
        {
            T v = baseValue;
            int count = m_Ledger.Count;
            for (int i = 0; i < count; i++)
            {
                if (!m_Ledger.IsAlive(i))
                    continue;
                ref readonly LedgerEntry e = ref m_Ledger.ItemAt(i);
                if (e.Kind == ContributionKind.Offset)
                    v = Offset(v, m_Values[m_Ledger.HandleAt(i).Index], e.Space);
            }
            TOps ops = default;
            for (int i = 0; i < count; i++)
            {
                if (!m_Ledger.IsAlive(i) || m_Ledger.ItemAt(i).Kind != ContributionKind.Override)
                    continue;
                int n = m_Ledger.HandleAt(i).Index;
                float w = m_Weights[n];
                if (w <= 0f)
                    continue;
                Vector4 mask = m_Masks[n];
                if (mask != Vector4.one)
                {
                    Vector4 a = ops.ToVector4(v);
                    Vector4 b = ops.ToVector4(m_Values[n]);
                    for (int c = 0; c < 4; c++)
                    {
                        if (mask[c] != 0f)
                            a[c] = w >= 1f ? b[c] : a[c] + (b[c] - a[c]) * w;
                    }
                    v = ops.FromVector4(a);
                    continue;
                }
                v = w >= 1f ? m_Values[n] : ops.Lerp(v, m_Values[n], w);
            }
            return v;
        }
    }
}
