using UnityEngine;

namespace xpTURN.Feedback
{
    // what a contribution does with its value
    internal enum ContributionKind
    {
        Offset = 0,
        Override = 1,
    }

    // the slot's space — a binding maps its item's space (PositionSpace.Anchored is Local's slot space — no cast). Self is
    // the target's own axes
    internal enum LayerSpace
    {
        World = 0,
        Local = 1,
        Self = 2,
    }

    // how offsets compose — Additive and Rotation take TOps.Add (for a rotation the product), Multiplicative takes MulOffset
    internal enum Composition
    {
        Additive,
        Multiplicative,
        Rotation,
    }

    // the registry's unit. The kind's constants come through the constructor — the registry files a slot by its stage once.
    // The flags below are the registry's
    internal abstract class LayerSlot
    {
        internal readonly ConsumptionStage Stage;
        internal PropertyLayerRegistry Registry;
        internal LayerKey Key;
        internal int RefCount;
        // in its stage's active list
        internal bool Active;
        // no reference left while active — the next Revert puts its value back and drops it
        internal bool ReleasePending;
        // failed or its target destroyed — final, never revived
        internal bool Detached;
        // a private slot — outside the registry's dictionary, written in direct mode
        internal bool Direct;
        bool m_Recorded;

        protected LayerSlot(ConsumptionStage stage)
        {
            Stage = stage;
        }

        internal abstract bool IsAlive { get; }

        // it wrote — the next Revert judges its value
        internal abstract bool Pending { get; }

        internal abstract int LiveContributions { get; }

        internal abstract int Reserved { get; }

        // the query's base — only of a live target
        internal abstract Vector4 BaseVector { get; }

        internal abstract void Revert();

        internal abstract void Apply();

        // puts a written value back once more
        internal abstract void Close();

        // the contributions of a slot no binding holds any more
        internal abstract void EndAll();

        internal abstract void Unreserve(int count);

        // once per slot, right before its first write — the registry calls the preview recorder
        protected void RecordOnce()
        {
            if (m_Recorded)
                return;
            m_Recorded = true;
            Registry?.Record(this);
        }

        // the registry filed the slot (Registry · Key set) — GetOrCreate · CreatePrivate call it once
        internal virtual void Attached()
        {
        }

        // the slot left the registry (forgotten or detached) or the registry closed — it may come more than once. A slot that
        // holds something of its own lets it go here (an instance slot's copies)
        internal virtual void Dropped()
        {
        }
    }
}
