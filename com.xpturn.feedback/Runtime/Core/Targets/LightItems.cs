using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    [FeedbackTarget("Light/Intensity", PreviewGrade.Full, Aliases = "brightness", Requires = typeof(Light))]
    [Serializable]
    public sealed class LightIntensityItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Punch | MotionMask.Shake | MotionMask.Oscillate | MotionMask.Blink | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Light/Range", PreviewGrade.Full, Requires = typeof(Light))]
    [Serializable]
    public sealed class LightRangeItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Light/Color", PreviewGrade.Full, Requires = typeof(Light))]
    [Serializable]
    public sealed class LightColorItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color,
            supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Gradient | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // a spot light's outer angle — its inner angle stays its own (LightSpotAngleSlot)
    [FeedbackTarget("Light/Spot Angle", PreviewGrade.Full, Requires = typeof(Light))]
    [Serializable]
    public sealed class LightSpotAngleItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Light/Shadow Strength", PreviewGrade.Full, Requires = typeof(Light))]
    [Serializable]
    public sealed class LightShadowStrengthItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }
}
