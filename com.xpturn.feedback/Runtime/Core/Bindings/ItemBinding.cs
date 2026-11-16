using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback
{
    // internal, so every binding is internal and no public intermediate base can open the closed layer. The initialization
    // calls Prepare · Release, the pipeline and the tick the rest
    internal abstract class ItemBinding
    {
        internal ItemBinding()
        {
        }

        // the values a graph can draw, from the item alone — called on a binding that was never prepared. None by default
        internal virtual void Describe(List<GraphChannel> into)
        {
        }

        // the slots a prepared binding's described values go to — none by default
        internal virtual void GraphSlots(List<LayerSlot> into)
        {
        }

        // a record's evaluation start in the motion's units — a Persist's start value. False when the binding keeps none
        internal virtual bool TryGetStartValue(int record, out Vector4 start)
        {
            start = default;
            return false;
        }

        // a described value of a running record at its time, as the graph draws it — the editor's record of a frame. None by
        // default
        internal virtual bool GraphRecord(int record, int channel, in MotionTime time, out Vector4 value)
        {
            value = default;
            return false;
        }

        // the dry slots of another binding of the same item start at this record's start (a Persist's) — false: none kept
        internal virtual bool SeedStart(int record, ItemBinding dry) => false;

        // a dry binding takes a live binding's start of its play for its own record (a Persist's: the start and where its
        // occurrences start — an earlier play's origin the dry one never saw) once both began it — true when taken
        internal virtual bool TakeStart(int record, ItemBinding from, int fromRecord) => false;

        // a sample as the graph draws it: an offset × the category's scale, an override's weight × the scale, a Persist's value
        // (not scaled), a shape weighed toward the item's fields, a toggle's state while it drives. False: not driven
        internal static bool GraphSample(MotionEvaluator evaluator, in MotionState s, in MotionTime time, MotionWrite write, bool weighs, bool toggle, float scale, out Vector4 v)
        {
            if (weighs)
            {
                v = new Vector4(evaluator.EvaluateShape(in s, in time) * scale, 0f, 0f, 0f);
                return true;
            }
            evaluator.Evaluate(in s, in time, out MotionSample sample);
            if (toggle)
            {
                bool driven = write == MotionWrite.Persist || sample.Weight * scale > 0f;
                v = new Vector4(sample.Value.x != 0f ? 1f : 0f, 0f, 0f, 0f);
                return driven;
            }
            switch (write)
            {
                case MotionWrite.Offset:
                    v = sample.Value * scale;
                    return true;
                case MotionWrite.Override:
                    v = new Vector4(sample.Weight * scale, 0f, 0f, 0f);
                    return true;
                default:
                    v = sample.Value;
                    return true;
            }
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

        // the record's play paused (true) or resumed by the player — to the items the play began or executed. A binding with a
        // sound of its own pauses it
        internal virtual void Suspend(int record, bool paused)
        {
        }

        // true = End comes to an executed instant for every reason, not only Revert — a sound an instant started hears the
        // play's end
        internal virtual bool EndsInstants => false;

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
