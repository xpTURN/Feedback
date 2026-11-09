using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the list element. Field names are the saved format — a rename is a field step that keeps the old field
    // every abstract base has an internal constructor — the item layer is closed but for the two extension bases
    // (CustomActionItem · CustomEffectItem), whose bindings are the package's adapters
    [Serializable]
    public abstract class FeedbackItem
    {
        [SerializeField] internal bool m_Active = true;
        [SerializeField] internal string m_Label = "";
        [SerializeField] internal ItemTiming m_Timing = ItemTiming.Default;

        internal FeedbackItem()
        {
        }

        // the clock the timeline conversion copies
        internal virtual ItemClock EffectiveClock => m_Timing.m_Clock;
    }

    [Serializable]
    public abstract class EffectItem : FeedbackItem
    {
        [SerializeField] internal ItemConditions m_Conditions = ItemConditions.Default;
        [SerializeField] internal ItemPlayDirection m_PlayDirection;
        [SerializeField] internal ItemRandomness m_Random = ItemRandomness.Default;
        [SerializeField] internal IntensityResponse m_Intensity;
        [SerializeField] internal CategoryOverride m_Category;

        internal EffectItem()
        {
        }

        internal abstract FeedbackCategory DeclaredCategory { get; }

        // counted by the flash cap whatever the override says
        internal virtual bool Flashes => false;

        // run backward alone it changes something — false: its reverse only undoes its forward act
        internal virtual bool ActsBackward => true;

        // the override wins, then flashing, then the declared category
        internal FeedbackCategory EffectiveCategory
            => m_Category.m_Override ? m_Category.m_Category : Flashes ? FeedbackCategory.Flash : DeclaredCategory;

        // Time items change timeScale — they never count their own slot on a clock they stop
        internal sealed override ItemClock EffectiveClock
            => DeclaredCategory == FeedbackCategory.Time ? ItemClock.Unscaled : m_Timing.m_Clock;
    }

    [Serializable]
    public abstract class TargetItem : EffectItem
    {
        [SerializeField] internal TargetRef m_Target = TargetRef.Default;
        [SerializeReference] internal FeedbackMotion m_Motion;
        [SerializeField] internal bool m_DirectWrite;

        internal TargetItem()
        {
        }

        internal abstract TargetTraits Traits { get; }

        internal sealed override FeedbackCategory DeclaredCategory => Traits.Category;

        internal sealed override bool Flashes
            => Traits.FlashSensitive && m_Motion != null && (m_Motion.Kind == MotionKind.Flash || m_Motion.Kind == MotionKind.Blink);

        // a Set's reverse writes the origin its forward act took
        internal sealed override bool ActsBackward => !(m_Motion is SetMotion);
    }

    [Serializable]
    public abstract class ActionItem : EffectItem
    {
        internal ActionItem()
        {
        }

        // seconds on EffectiveClock, 0 = instant — the timeline conversion runs before any binding
        internal abstract float NaturalDuration { get; }

        // its binding ends it (a wait on another player) — NaturalDuration is then its time limit or +inf
        internal virtual bool Opens => false;
    }

    [Serializable]
    public abstract class FlowItem : FeedbackItem
    {
        internal FlowItem()
        {
        }
    }
}
