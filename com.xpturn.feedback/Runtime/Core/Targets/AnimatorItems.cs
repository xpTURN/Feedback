using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a Parameter's kind — saved (the engine's parameter type has other values; the implementation maps it)
    public enum AnimatorParameterKind
    {
        Trigger = 0,
        Bool = 1,
        Int = 2,
        Float = 3,
    }

    // how a Parameter's Set picks its value
    public enum AnimatorValuePick
    {
        Value = 0,
        Random = 1,
        Increment = 2,
    }

    // the names Core can't type
    internal static class AnimationNames
    {
        internal const string Animator = "UnityEngine.Animator, UnityEngine.AnimationModule";
        internal const string Assembly = "xpTURN.Feedback.Animation";
    }

    // an animator parameter — Set: a trigger (set · a Revert clears it while still set), a bool, an int or a float — its
    // value, a random one (the play's seed — an int's range takes both ends) or the value now plus a step. Names resolve at
    // the prepare; the intensity is not read
    [FeedbackTarget("Animation/Parameter", PreviewGrade.Simulated, Aliases = "animator parameter trigger bool int float random increment",
        RequiresName = AnimationNames.Animator, Implementation = AnimationNames.Assembly)]
    [Serializable]
    public sealed class AnimatorParameterItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_Parameter = "";
        [SerializeField] internal AnimatorParameterKind m_Kind;
        [SerializeField] internal bool m_Bool = true;
        [SerializeField] internal int m_Int;
        [SerializeField] internal float m_Float;
        [SerializeField] internal AnimatorValuePick m_Pick;
        [SerializeField] internal float m_Min;
        [SerializeField] internal float m_Max = 5f;
        [SerializeField] internal float m_Step = 1f;

        internal override TargetTraits Traits => s_Traits;
    }

    // a float parameter over time — the Update stage (the animators evaluate at PreLateUpdate 01 ~ 03). It shares the
    // parameter's slot with a Parameter's float Set
    [FeedbackTarget("Animation/Float Parameter", PreviewGrade.Simulated, Aliases = "animator float parameter",
        RequiresName = AnimationNames.Animator, Implementation = AnimationNames.Assembly)]
    [Serializable]
    public sealed class AnimatorFloatItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_Parameter = "";

        internal override TargetTraits Traits => s_Traits;
    }

    // the animator's speed — the Update stage. A Fixed (Animate Physics) animator evaluates after the next revert: its offsets
    // (Punch · Spring) show with the direct write only (rule L05)
    [FeedbackTarget("Animation/Speed", PreviewGrade.Simulated, Aliases = "animator speed",
        RequiresName = AnimationNames.Animator, Implementation = AnimationNames.Assembly)]
    [Serializable]
    public sealed class AnimatorSpeedItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Spring,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // a layer's weight by the layer's name — the Update stage (the base layer's weight is always 1)
    [FeedbackTarget("Animation/Layer Weight", PreviewGrade.Simulated, Aliases = "animator layer weight",
        RequiresName = AnimationNames.Animator, Implementation = AnimationNames.Assembly)]
    [Serializable]
    public sealed class AnimatorLayerWeightItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_Layer = "";

        internal override TargetTraits Traits => s_Traits;
    }

    // a state — a transition of 0 plays it, any other a fixed-time cross fade; the offset in seconds; no layer named takes the
    // first layer with the state. A Revert puts the layer's state at the play's start back
    [FeedbackTarget("Animation/Play State", PreviewGrade.Simulated, Aliases = "animator play state crossfade transition",
        RequiresName = AnimationNames.Animator, Implementation = AnimationNames.Assembly)]
    [Serializable]
    public sealed class AnimatorStateItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_State = "";
        [SerializeField] internal string m_Layer = "";
        [SerializeField] internal float m_Transition = 0.1f;
        [SerializeField] internal float m_Offset;

        internal override TargetTraits Traits => s_Traits;
    }
}
