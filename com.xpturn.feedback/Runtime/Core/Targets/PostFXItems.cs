using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the PostFX items — the value is the weight (0 ~ 1) of the item's own overlay volume, the targets are the item's fields
    // written into that volume's component. URP's types by name (Core can't name them)
    internal static class PostFXNames
    {
        internal const string Volume = "UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime";
        internal const string VolumeProfile = "UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core.Runtime";
        internal const string VolumeComponent = "UnityEngine.Rendering.VolumeComponent, Unity.RenderPipelines.Core.Runtime";
        internal const string CameraData = "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime";
        internal const string Assembly = Light2DNames.Assembly;

        // a shape motion weighs toward the item's fields (WeightTowardTarget) — Bloom · Color Adjustments change the brightness
        internal static readonly TargetTraits Weighted = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Curve,
            category: FeedbackCategory.PostFX, flashSensitive: false, applyMode: ApplyMode.WeightTowardTarget);
        internal static readonly TargetTraits WeightedBright = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Curve,
            category: FeedbackCategory.PostFX, flashSensitive: true, applyMode: ApplyMode.WeightTowardTarget);
        internal static readonly TargetTraits WeightedFour = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Punch | MotionMask.Oscillate,
            category: FeedbackCategory.PostFX, flashSensitive: false, applyMode: ApplyMode.WeightTowardTarget);
        internal static readonly TargetTraits WeightedTwo = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Flash,
            category: FeedbackCategory.PostFX, flashSensitive: false, applyMode: ApplyMode.WeightTowardTarget);
    }

    // a target field: URP's override box and its value — an unchosen field passes the volumes under the overlay
    [Serializable]
    public struct PostFXFloat
    {
        [SerializeField] internal bool m_Override;
        [SerializeField] internal float m_Value;

        internal PostFXFloat(bool on, float value)
        {
            m_Override = on;
            m_Value = value;
        }
    }

    [Serializable]
    public struct PostFXColor
    {
        [SerializeField] internal bool m_Override;
        [SerializeField] internal Color m_Value;

        internal PostFXColor(bool on, Color value)
        {
            m_Override = on;
            m_Value = value;
        }
    }

    [Serializable]
    public struct PostFXVector2
    {
        [SerializeField] internal bool m_Override;
        [SerializeField] internal Vector2 m_Value;

        internal PostFXVector2(bool on, Vector2 value)
        {
            m_Override = on;
            m_Value = value;
        }
    }

    [Serializable]
    public struct PostFXVector4
    {
        [SerializeField] internal bool m_Override;
        [SerializeField] internal Vector4 m_Value;

        internal PostFXVector4(bool on, Vector4 value)
        {
            m_Override = on;
            m_Value = value;
        }
    }

    // URP's DepthOfFieldMode
    public enum PostFXDepthOfFieldMode
    {
        Off = 0,
        Gaussian = 1,
        Bokeh = 2,
    }

    // the intensity fields are m_IntensityTarget — EffectItem.m_Intensity is the play's intensity
    [FeedbackTarget("PostFX/Bloom", PreviewGrade.Full, Aliases = "glow", Global = true, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXBloomItem : TargetItem
    {
        [SerializeField] internal PostFXFloat m_IntensityTarget = new PostFXFloat(true, 3f);
        [SerializeField] internal PostFXFloat m_Threshold = new PostFXFloat(false, 0.9f);
        [SerializeField] internal PostFXFloat m_Scatter = new PostFXFloat(false, 0.7f);
        [SerializeField] internal PostFXColor m_Tint = new PostFXColor(false, Color.white);

        internal override TargetTraits Traits => PostFXNames.WeightedBright;
    }

    [FeedbackTarget("PostFX/Chromatic Aberration", PreviewGrade.Full, Global = true, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXChromaticAberrationItem : TargetItem
    {
        [SerializeField] internal PostFXFloat m_IntensityTarget = new PostFXFloat(true, 1f);

        internal override TargetTraits Traits => PostFXNames.Weighted;
    }

    [FeedbackTarget("PostFX/Color Adjustments", PreviewGrade.Full, Aliases = "exposure contrast saturation hue filter", Global = true, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXColorAdjustmentsItem : TargetItem
    {
        [SerializeField] internal PostFXFloat m_PostExposure = new PostFXFloat(true, 1f);
        [SerializeField] internal PostFXFloat m_Contrast = new PostFXFloat(false, 0f);
        [SerializeField] internal PostFXColor m_ColorFilter = new PostFXColor(false, Color.white);
        [SerializeField] internal PostFXFloat m_HueShift = new PostFXFloat(false, 0f);
        [SerializeField] internal PostFXFloat m_Saturation = new PostFXFloat(false, 0f);

        internal override TargetTraits Traits => PostFXNames.WeightedBright;
    }

    // the mode is not interpolated — it takes at a weight above 0 and comes back at 0
    [FeedbackTarget("PostFX/Depth of Field", PreviewGrade.Full, Aliases = "focus blur", Global = true, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXDepthOfFieldItem : TargetItem
    {
        [SerializeField] internal bool m_OverrideMode = true;
        [SerializeField] internal PostFXDepthOfFieldMode m_Mode = PostFXDepthOfFieldMode.Gaussian;
        [SerializeField] internal PostFXFloat m_GaussianStart = new PostFXFloat(true, 0f);
        [SerializeField] internal PostFXFloat m_GaussianEnd = new PostFXFloat(true, 5f);
        [SerializeField] internal PostFXFloat m_FocusDistance = new PostFXFloat(false, 1f);
        [SerializeField] internal PostFXFloat m_Aperture = new PostFXFloat(false, 5.6f);
        [SerializeField] internal PostFXFloat m_FocalLength = new PostFXFloat(false, 50f);

        internal override TargetTraits Traits => PostFXNames.WeightedTwo;
    }

    [FeedbackTarget("PostFX/Film Grain", PreviewGrade.Full, Aliases = "noise", Global = true, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXFilmGrainItem : TargetItem
    {
        [SerializeField] internal PostFXFloat m_IntensityTarget = new PostFXFloat(true, 1f);
        [SerializeField] internal PostFXFloat m_Response = new PostFXFloat(false, 0.8f);

        internal override TargetTraits Traits => PostFXNames.WeightedTwo;
    }

    [FeedbackTarget("PostFX/Lens Distortion", PreviewGrade.Full, Aliases = "warp", Global = true, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXLensDistortionItem : TargetItem
    {
        [SerializeField] internal PostFXFloat m_IntensityTarget = new PostFXFloat(true, -0.5f);
        [SerializeField] internal PostFXFloat m_Scale = new PostFXFloat(false, 1f);
        [SerializeField] internal PostFXVector2 m_Center = new PostFXVector2(false, new Vector2(0.5f, 0.5f));

        internal override TargetTraits Traits => PostFXNames.WeightedFour;
    }

    // URP draws motion blur in play only — no preview
    [FeedbackTarget("PostFX/Motion Blur", PreviewGrade.None, Global = true, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXMotionBlurItem : TargetItem
    {
        [SerializeField] internal PostFXFloat m_IntensityTarget = new PostFXFloat(true, 1f);
        [SerializeField] internal PostFXFloat m_Clamp = new PostFXFloat(false, 0.05f);

        internal override TargetTraits Traits => PostFXNames.WeightedTwo;
    }

    [FeedbackTarget("PostFX/Panini Projection", PreviewGrade.Full, Global = true, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXPaniniProjectionItem : TargetItem
    {
        [SerializeField] internal PostFXFloat m_Distance = new PostFXFloat(true, 1f);
        [SerializeField] internal PostFXFloat m_CropToFit = new PostFXFloat(false, 1f);

        internal override TargetTraits Traits => PostFXNames.WeightedTwo;
    }

    // rounded is not interpolated — it takes at a weight above 0
    [FeedbackTarget("PostFX/Vignette", PreviewGrade.Full, Aliases = "edge darken", Global = true, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXVignetteItem : TargetItem
    {
        [SerializeField] internal PostFXFloat m_IntensityTarget = new PostFXFloat(true, 0.45f);
        [SerializeField] internal PostFXFloat m_Smoothness = new PostFXFloat(false, 0.2f);
        [SerializeField] internal PostFXColor m_Color = new PostFXColor(false, Color.black);
        [SerializeField] internal PostFXVector2 m_Center = new PostFXVector2(false, new Vector2(0.5f, 0.5f));
        [SerializeField] internal bool m_OverrideRounded;
        [SerializeField] internal bool m_Rounded;

        internal override TargetTraits Traits => PostFXNames.WeightedFour;
    }

    [FeedbackTarget("PostFX/White Balance", PreviewGrade.Full, Aliases = "temperature tint", Global = true, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXWhiteBalanceItem : TargetItem
    {
        [SerializeField] internal PostFXFloat m_Temperature = new PostFXFloat(true, 50f);
        [SerializeField] internal PostFXFloat m_Tint = new PostFXFloat(false, 0f);

        internal override TargetTraits Traits => PostFXNames.WeightedTwo;
    }

    [FeedbackTarget("PostFX/Channel Mixer", PreviewGrade.Full, Global = true, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXChannelMixerItem : TargetItem
    {
        [SerializeField] internal PostFXFloat m_RedOutRedIn = new PostFXFloat(true, 150f);
        [SerializeField] internal PostFXFloat m_RedOutGreenIn = new PostFXFloat(false, 0f);
        [SerializeField] internal PostFXFloat m_RedOutBlueIn = new PostFXFloat(false, 0f);
        [SerializeField] internal PostFXFloat m_GreenOutRedIn = new PostFXFloat(false, 0f);
        [SerializeField] internal PostFXFloat m_GreenOutGreenIn = new PostFXFloat(false, 100f);
        [SerializeField] internal PostFXFloat m_GreenOutBlueIn = new PostFXFloat(false, 0f);
        [SerializeField] internal PostFXFloat m_BlueOutRedIn = new PostFXFloat(false, 0f);
        [SerializeField] internal PostFXFloat m_BlueOutGreenIn = new PostFXFloat(false, 0f);
        [SerializeField] internal PostFXFloat m_BlueOutBlueIn = new PostFXFloat(false, 100f);

        internal override TargetTraits Traits => PostFXNames.WeightedTwo;
    }

    [FeedbackTarget("PostFX/Split Toning", PreviewGrade.Full, Global = true, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXSplitToningItem : TargetItem
    {
        [SerializeField] internal PostFXColor m_Shadows = new PostFXColor(true, new Color(0.3f, 0.4f, 0.7f, 1f));
        [SerializeField] internal PostFXColor m_Highlights = new PostFXColor(false, new Color(0.5f, 0.5f, 0.5f, 1f));
        [SerializeField] internal PostFXFloat m_Balance = new PostFXFloat(false, 0f);

        internal override TargetTraits Traits => PostFXNames.WeightedTwo;
    }

    // lift · gamma · gain: xyz the color, w the offset
    [FeedbackTarget("PostFX/Lift Gamma Gain", PreviewGrade.Full, Global = true, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXLiftGammaGainItem : TargetItem
    {
        [SerializeField] internal PostFXVector4 m_Lift = new PostFXVector4(false, new Vector4(1f, 1f, 1f, 0f));
        [SerializeField] internal PostFXVector4 m_Gamma = new PostFXVector4(false, new Vector4(1f, 1f, 1f, 0f));
        [SerializeField] internal PostFXVector4 m_Gain = new PostFXVector4(true, new Vector4(1f, 1f, 1f, 0.25f));

        internal override TargetTraits Traits => PostFXNames.WeightedTwo;
    }

    [FeedbackTarget("PostFX/Shadows Midtones Highlights", PreviewGrade.Full, Global = true, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXShadowsMidtonesHighlightsItem : TargetItem
    {
        [SerializeField] internal PostFXVector4 m_Shadows = new PostFXVector4(true, new Vector4(1f, 1f, 1f, -0.25f));
        [SerializeField] internal PostFXVector4 m_Midtones = new PostFXVector4(false, new Vector4(1f, 1f, 1f, 0f));
        [SerializeField] internal PostFXVector4 m_Highlights = new PostFXVector4(false, new Vector4(1f, 1f, 1f, 0f));
        [SerializeField] internal PostFXFloat m_ShadowsStart = new PostFXFloat(false, 0f);
        [SerializeField] internal PostFXFloat m_ShadowsEnd = new PostFXFloat(false, 0.3f);
        [SerializeField] internal PostFXFloat m_HighlightsStart = new PostFXFloat(false, 0.55f);
        [SerializeField] internal PostFXFloat m_HighlightsEnd = new PostFXFloat(false, 1f);

        internal override TargetTraits Traits => PostFXNames.WeightedTwo;
    }

    // a user's profile asset blended in on the item's own volume (its sharedProfile — the asset is neither copied nor written).
    // The field takes any ScriptableObject — a Volume Profile only (FB-Q03 · the prepare skips another)
    [FeedbackTarget("PostFX/Volume Blend", PreviewGrade.Full, Aliases = "profile", Global = true, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXVolumeBlendItem : TargetItem
    {
        [SerializeField] internal ScriptableObject m_Profile;

        internal override TargetTraits Traits => PostFXNames.WeightedTwo;
    }

    // a user volume's weight — a value target (no overlay)
    [FeedbackTarget("PostFX/Volume Weight", PreviewGrade.Full, RequiresName = PostFXNames.Volume, Implementation = PostFXNames.Assembly)]
    [Serializable]
    public sealed class PostFXVolumeWeightItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Flash,
            category: FeedbackCategory.PostFX, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }
}
