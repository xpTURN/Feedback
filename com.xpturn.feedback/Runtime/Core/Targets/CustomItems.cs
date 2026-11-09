using System;
using UnityEngine;
using UnityEngine.Events;

namespace xpTURN.Feedback
{
    // a component's value through IFeedbackTarget<T> — the target found the components' way (a dragged implementer is that
    // one, else the found object's first)
    [FeedbackTarget("Custom/Float Value", PreviewGrade.Full, Aliases = "custom value float property interface", Requires = typeof(IFeedbackTarget<float>))]
    [Serializable]
    public sealed class CustomFloatItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Set | MotionMask.Tween | MotionMask.Punch | MotionMask.Shake | MotionMask.Curve | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Wiggle | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Custom/Vector3 Value", PreviewGrade.Full, Aliases = "custom value vector property interface", Requires = typeof(IFeedbackTarget<Vector3>))]
    [Serializable]
    public sealed class CustomVector3Item : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector3,
            supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Shake | MotionMask.Curve | MotionMask.Oscillate | MotionMask.Wiggle | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Custom/Color Value", PreviewGrade.Full, Aliases = "custom value color property interface", Requires = typeof(IFeedbackTarget<Color>))]
    [Serializable]
    public sealed class CustomColorItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color,
            supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Gradient | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // the motion's value to a UnityEvent<float> every frame the play runs — the base is the 'now' value (a Tween To goes from
    // it, an offset sits on it) · a revert calls it once with the base · the preview calls it only when asked (and then a
    // listener set to Runtime Only is not called in the editor)
    [FeedbackTarget("Custom/Float Event", PreviewGrade.Full, Aliases = "custom float event unityevent value", Global = true)]
    [Serializable]
    public sealed class CustomFloatEventItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Shake | MotionMask.Curve | MotionMask.Oscillate,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal UnityEvent<float> m_Event = new UnityEvent<float>();
        [SerializeField] internal float m_Base;
        [SerializeField] internal bool m_InvokeInPreview;

        internal override TargetTraits Traits => s_Traits;
    }
}
