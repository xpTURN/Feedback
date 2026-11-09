using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a RectTransform's size delta · pivot · anchors · offsets — CoreModule types, Core's own. The position is
    // Transform/Position's Anchored space
    [FeedbackTarget("Rect Transform/Size Delta", PreviewGrade.Full, Aliases = "ui size width height", Requires = typeof(RectTransform))]
    [Serializable]
    public sealed class RectSizeDeltaItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector2, supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Spring,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Rect Transform/Pivot", PreviewGrade.Full, Requires = typeof(RectTransform))]
    [Serializable]
    public sealed class RectPivotItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector2, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // the anchors as (min x, min y, max x, max y)
    [FeedbackTarget("Rect Transform/Anchors", PreviewGrade.Full, Requires = typeof(RectTransform))]
    [Serializable]
    public sealed class RectAnchorsItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector4, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // the offsets as (min x, min y, max x, max y) — written through the size delta's and the position's slots
    [FeedbackTarget("Rect Transform/Offsets", PreviewGrade.Full, Aliases = "edges insets", Requires = typeof(RectTransform))]
    [Serializable]
    public sealed class RectOffsetsItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector4, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }
}
