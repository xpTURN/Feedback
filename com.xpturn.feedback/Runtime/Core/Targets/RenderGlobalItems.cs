using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the global render state — no target (Global). A shader global is one value of the session; fog · skybox · ambient light
    // are the active scene's RenderSettings

    // a global Float — named (an empty name is refused)
    [FeedbackTarget("Renderer/Shader Global Float", PreviewGrade.Full, Aliases = "global shader", Global = true)]
    [Serializable]
    public sealed class ShaderGlobalFloatItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Set | MotionMask.Tween | MotionMask.Curve,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_PropertyName = "";

        internal override TargetTraits Traits => s_Traits;
    }

    // a global color — the picker's color: linear in a Linear project (as a material's Color property)
    [FeedbackTarget("Renderer/Shader Global Color", PreviewGrade.Full, Aliases = "global shader", Global = true)]
    [Serializable]
    public sealed class ShaderGlobalColorItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color, supportedMotions: MotionMask.Set | MotionMask.Tween | MotionMask.Curve,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_PropertyName = "";

        internal override TargetTraits Traits => s_Traits;
    }

    // a global Vector — as written (no conversion)
    [FeedbackTarget("Renderer/Shader Global Vector", PreviewGrade.Full, Aliases = "global shader", Global = true)]
    [Serializable]
    public sealed class ShaderGlobalVectorItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector4, supportedMotions: MotionMask.Set | MotionMask.Tween | MotionMask.Curve,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_PropertyName = "";

        internal override TargetTraits Traits => s_Traits;
    }

    // RenderSettings.fogColor — the fog's switch and mode stay as they are
    [FeedbackTarget("Renderer/Fog Color", PreviewGrade.Full, Aliases = "fog", Global = true)]
    [Serializable]
    public sealed class FogColorItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color, supportedMotions: MotionMask.Tween | MotionMask.Flash,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // RenderSettings.fogDensity — never below 0 (the exponential modes)
    [FeedbackTarget("Renderer/Fog Density", PreviewGrade.Full, Aliases = "fog", Global = true)]
    [Serializable]
    public sealed class FogDensityItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Flash,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // a copy of RenderSettings.skybox — the original asset is not written. Empty name = _Exposure
    [FeedbackTarget("Renderer/Skybox Exposure", PreviewGrade.Full, Aliases = "sky skybox", Global = true)]
    [Serializable]
    public sealed class SkyboxExposureItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Oscillate,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_PropertyName = "";

        internal override TargetTraits Traits => s_Traits;
    }

    // empty name = _Tint, else _SkyTint (Procedural)
    [FeedbackTarget("Renderer/Skybox Tint", PreviewGrade.Full, Aliases = "sky skybox", Global = true)]
    [Serializable]
    public sealed class SkyboxTintItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color, supportedMotions: MotionMask.Tween | MotionMask.Oscillate,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_PropertyName = "";

        internal override TargetTraits Traits => s_Traits;
    }

    // degrees — empty name = _Rotation (Cubemap · 6 Sided · Panoramic; Procedural has none)
    [FeedbackTarget("Renderer/Skybox Rotation", PreviewGrade.Full, Aliases = "sky skybox", Global = true)]
    [Serializable]
    public sealed class SkyboxRotationItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Oscillate,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_PropertyName = "";

        internal override TargetTraits Traits => s_Traits;
    }

    // RenderSettings.ambientLight — the sky color of the Flat and Trilight sources
    [FeedbackTarget("Renderer/Ambient Color", PreviewGrade.Full, Aliases = "ambient environment", Global = true)]
    [Serializable]
    public sealed class AmbientColorItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // RenderSettings.ambientIntensity — the Skybox source's multiplier, never below 0
    [FeedbackTarget("Renderer/Ambient Intensity", PreviewGrade.Full, Aliases = "ambient environment", Global = true)]
    [Serializable]
    public sealed class AmbientIntensityItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }
}
