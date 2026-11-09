using System;

namespace xpTURN.Feedback
{
    // the names Core can't type: a package's type, so the editor and the tests name it by its string too
    internal static class Light2DNames
    {
        internal const string Light = "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.2D.Runtime";
        internal const string Assembly = "xpTURN.Feedback.URP";
    }

    // a 2D light's intensity — Blink is the intensity's override
    [FeedbackTarget("Light 2D/Intensity", PreviewGrade.Full, Aliases = "brightness", RequiresName = Light2DNames.Light, Implementation = Light2DNames.Assembly)]
    [Serializable]
    public sealed class Light2DIntensityItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Blink,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Light 2D/Color", PreviewGrade.Full, RequiresName = Light2DNames.Light, Implementation = Light2DNames.Assembly)]
    [Serializable]
    public sealed class Light2DColorItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color, supportedMotions: MotionMask.Tween | MotionMask.Flash,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // a point light's outer radius — its inner radius stays its own up to the outer (Light2DRadiusSlot)
    [FeedbackTarget("Light 2D/Radius", PreviewGrade.Full, Aliases = "size", RequiresName = Light2DNames.Light, Implementation = Light2DNames.Assembly)]
    [Serializable]
    public sealed class Light2DRadiusItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Oscillate,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // the falloff's intensity (0 ~ 1) — not the shape light's falloff size, whose change rebuilds the light's mesh each frame
    [FeedbackTarget("Light 2D/Falloff", PreviewGrade.Full, RequiresName = Light2DNames.Light, Implementation = Light2DNames.Assembly)]
    [Serializable]
    public sealed class Light2DFalloffItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Oscillate,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }
}
