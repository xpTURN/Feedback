using System;

namespace xpTURN.Feedback
{
    // non-generic motion data, one parameter struct each — a closed set
    [Serializable]
    public abstract class FeedbackMotion
    {
        internal FeedbackMotion()
        {
        }

        internal abstract MotionKind Kind { get; }

        // seconds on the item's clock, −1 = none (the item timing's duration) — Set 0, Spring its settle time
        internal virtual float NaturalDuration => -1f;

        // the flashes one start makes over `seconds` on the item's clock (its resolved length) — one, Blink its periods
        internal virtual int FlashCount(double seconds) => 1;
    }
}
