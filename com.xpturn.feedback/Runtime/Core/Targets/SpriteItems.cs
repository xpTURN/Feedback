using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback
{
    // saved — add members at the end only
    public enum SpriteAxis
    {
        X = 0,
        Y = 1,
    }

    public enum LineEnd
    {
        Start = 0,
        End = 1,
    }

    // the sprite renderer's color (not clamped)
    [FeedbackTarget("Renderer/Sprite Color", PreviewGrade.Full, Aliases = "sprite tint", Requires = typeof(SpriteRenderer))]
    [Serializable]
    public sealed class SpriteColorItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color,
            supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Gradient | MotionMask.Blink | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // the color's alpha — the Sprite Color slot shared
    [FeedbackTarget("Renderer/Sprite Alpha", PreviewGrade.Full, Aliases = "sprite fade transparency", Requires = typeof(SpriteRenderer))]
    [Serializable]
    public sealed class SpriteAlphaItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Blink | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // a sprite for another — Set. The value is not read
    [FeedbackTarget("Renderer/Sprite Swap", PreviewGrade.Full, Requires = typeof(SpriteRenderer))]
    [Serializable]
    public sealed class SpriteSwapItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal Sprite m_Sprite;

        internal override TargetTraits Traits => s_Traits;
    }

    // an axis's flip — a toggle
    [FeedbackTarget("Renderer/Sprite Flip", PreviewGrade.Full, Aliases = "mirror", Requires = typeof(SpriteRenderer))]
    [Serializable]
    public sealed class SpriteFlipItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal SpriteAxis m_Axis;

        internal override TargetTraits Traits => s_Traits;
    }

    // the frames in order — the tween's value 0 ~ 1 is the place in the list
    [FeedbackTarget("Renderer/Sprite Sheet", PreviewGrade.Full, Aliases = "flipbook frames animation", Requires = typeof(SpriteRenderer))]
    [Serializable]
    public sealed class SpriteSheetItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal List<Sprite> m_Frames = new List<Sprite>();

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Renderer/Line Width", PreviewGrade.Full, Requires = typeof(LineRenderer))]
    [Serializable]
    public sealed class LineWidthItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Punch,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // an end's color — the gradient's first or last keys (8 bits, 0 ~ 1)
    [FeedbackTarget("Renderer/Line Color", PreviewGrade.Full, Requires = typeof(LineRenderer))]
    [Serializable]
    public sealed class LineColorItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color, supportedMotions: MotionMask.Tween | MotionMask.Punch,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal LineEnd m_End;

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Renderer/Trail Time", PreviewGrade.Full, Requires = typeof(TrailRenderer))]
    [Serializable]
    public sealed class TrailTimeItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Set | MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Renderer/Trail Width", PreviewGrade.Full, Requires = typeof(TrailRenderer))]
    [Serializable]
    public sealed class TrailWidthItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Set | MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Renderer/Trail Emitting", PreviewGrade.Full, Requires = typeof(TrailRenderer))]
    [Serializable]
    public sealed class TrailEmittingItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // the trail's points gone — an instant with nothing to put back
    [FeedbackTarget("Renderer/Trail Clear", PreviewGrade.Full, Requires = typeof(TrailRenderer))]
    [Serializable]
    public sealed class TrailClearItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }
}
