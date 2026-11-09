using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // requests a time scale for its own length on the unscaled clock
    [FeedbackTarget("Time/Hit Stop", PreviewGrade.DisplayOnly, Aliases = "hitstop freeze")]
    [Serializable]
    public sealed class HitStopItem : ActionItem
    {
        [SerializeField] internal float m_FreezeDuration = 0.1f;       // seconds, unscaled — the item's length (0 = instant, no request)
        [SerializeField] internal float m_MinimumTimeScale = 0.1f;     // skipped while Time.timeScale is below this
        [SerializeField] internal float m_TimeScale;                   // the scale while frozen, 0 = stopped

        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.Time;
        internal override float NaturalDuration => m_FreezeDuration;

        internal override bool ActsBackward => false;
    }

    // the game's time scale through the service's open request — a global value target of the Time category (the unscaled
    // clock). A Flash goes and comes back, a Tween · Set stays (Revert · Restore put it back); several in one context are the
    // later one's (no offset motion)
    [FeedbackTarget("Time/Time Scale", PreviewGrade.DisplayOnly, Aliases = "timescale slow motion slowmo time scale", Global = true)]
    [Serializable]
    public sealed class TimeScaleItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Set | MotionMask.Tween | MotionMask.Flash,
            category: FeedbackCategory.Time, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }
}
