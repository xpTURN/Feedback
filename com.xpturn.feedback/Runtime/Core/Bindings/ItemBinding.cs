namespace xpTURN.Feedback
{
    // internal, so every binding is internal and no public intermediate base can open the closed layer. The initialization
    // calls Prepare · Release, the pipeline and the tick the rest
    internal abstract class ItemBinding
    {
        internal ItemBinding()
        {
        }

        // resolves the target, gets the slots and makes the per-play state. false = the item keeps its place and has no effect
        internal abstract bool Prepare(in PrepareContext context);

        // a timed occurrence starts in this play record — the end of the pass evaluates it
        internal abstract void Begin(int record, in BeginContext context);

        internal abstract void Evaluate(int record, in MotionTime time);

        // Revert comes to the items that ended earlier in the play and to the executed instants too
        internal abstract void End(int record, EndReason reason);

        // an instant
        internal virtual void Execute(int record, in BeginContext context)
        {
        }

        // once after a running item's End, whatever the reason: a pass's at its end — a restart inside the pass takes the
        // contribution over — an end's before its event
        internal virtual void Settle(int record)
        {
        }

        // the record's play turned around where it is (serial: that play) — what runs keeps running; a binding that keeps the
        // play's direction past its Begin takes the new one
        internal virtual void Turn(int record, long serial, bool reverse)
        {
        }

        // false = the target is gone: this item ends, the play goes on
        internal virtual bool IsAlive => true;

        // gives back what Prepare got — also after a Prepare that returned false or threw
        internal virtual void Release()
        {
        }

        // the first frame without a play — a Persist timed motion writes its value at the time given. false = nothing written
        internal virtual bool ApplyInitial(in MotionTime time, float intensity) => false;

        // back to the value before the first play that reached it, then the snapshots are cleared
        internal virtual void Restore()
        {
        }

        // Rebuild hands the old binding of the same item over — its snapshots, the same property only
        internal virtual void Inherit(ItemBinding old)
        {
        }

        // a short note on this play record for the debugger's item line — null = none
        internal virtual string DebugNote(int record) => null;

        // why Prepare returned false — the prepare's warning names it
        internal string SkipReason { get; private set; }

        protected bool Refuse(string reason)
        {
            SkipReason = reason;
            return false;
        }
    }
}
