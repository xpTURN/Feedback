using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace xpTURN.Feedback.Tests.Editor
{
    // a motion whose kind the test picks
    [Serializable]
    public sealed class TraitsTestMotion : FeedbackMotion
    {
        [SerializeField] internal MotionKind m_Kind;

        internal override MotionKind Kind => m_Kind;
    }

    [Serializable]
    public sealed class TraitsFlashTarget : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color, supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Blink,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [Serializable]
    public sealed class TraitsMotionTarget : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector3, supportedMotions: MotionMask.Tween | MotionMask.Flash,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // flash sensitive and Time — only to tell the declared category from the effective one
    [Serializable]
    public sealed class TraitsTimeTarget : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Flash,
            category: FeedbackCategory.Time, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    [Serializable]
    public sealed class TraitsTimeAction : ActionItem
    {
        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.Time;
        internal override float NaturalDuration => 0.1f;
    }

    [Serializable]
    public sealed class TraitsPlainAction : ActionItem
    {
        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;
        internal override float NaturalDuration => 0f;
    }

    // an action may flash — the action side is not sealed
    [Serializable]
    public sealed class TraitsFlashingAction : ActionItem
    {
        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;
        internal override float NaturalDuration => 0f;
        internal override bool Flashes => true;
    }

    // negative controls — each breaks one trait rule, RuleKeepsAll keeps them all
    static class RuleTraits
    {
        internal static TargetTraits Make(ValueKind kind, MotionMask motions, bool flashSensitive)
            => new TargetTraits(valueKind: kind, supportedMotions: motions, category: FeedbackCategory.None, flashSensitive: flashSensitive, applyMode: ApplyMode.Offset);
    }

    public sealed class RuleNoMotions : TargetItem
    {
        static readonly TargetTraits s_Traits = RuleTraits.Make(ValueKind.Float, MotionMask.None, false);

        internal override TargetTraits Traits => s_Traits;
    }

    public sealed class RuleBoolTween : TargetItem
    {
        static readonly TargetTraits s_Traits = RuleTraits.Make(ValueKind.Bool, MotionMask.Set | MotionMask.Tween, false);

        internal override TargetTraits Traits => s_Traits;
    }

    public sealed class RuleGradientVector3 : TargetItem
    {
        static readonly TargetTraits s_Traits = RuleTraits.Make(ValueKind.Vector3, MotionMask.Gradient, false);

        internal override TargetTraits Traits => s_Traits;
    }

    public sealed class RuleFlashSensitiveNoFlash : TargetItem
    {
        static readonly TargetTraits s_Traits = RuleTraits.Make(ValueKind.Color, MotionMask.Tween, true);

        internal override TargetTraits Traits => s_Traits;
    }

    public sealed class RuleNewEveryRead : TargetItem
    {
        internal override TargetTraits Traits => RuleTraits.Make(ValueKind.Float, MotionMask.Tween, false);
    }

    public sealed class RuleKeepsAll : TargetItem
    {
        static readonly TargetTraits s_Traits = RuleTraits.Make(ValueKind.Bool, MotionMask.Set | MotionMask.Blink, true);

        internal override TargetTraits Traits => s_Traits;
    }

    // traits, metadata, flow items, effective category and clock, saved names
    public sealed class DataModelTraitsTests
    {
        static readonly Assembly Core = typeof(FeedbackItem).Assembly;

        static readonly (Type Type, string Path, PreviewGrade Preview)[] FlowMeta =
        {
            (typeof(WaitItem), "Flow/Wait", PreviewGrade.Full),
            (typeof(WaitForSignalItem), "Flow/Wait For Signal", PreviewGrade.DisplayOnly),
            (typeof(LoopStartItem), "Flow/Loop Start", PreviewGrade.Full),
            (typeof(LoopEndItem), "Flow/Loop End", PreviewGrade.Full),
            (typeof(HoldItem), "Flow/Hold", PreviewGrade.Full),
        };

        static readonly (Type Type, string Violation)[] RuleControls =
        {
            (typeof(RuleNoMotions), "no motions"),
            (typeof(RuleBoolTween), "bool beyond Set · Blink"),
            (typeof(RuleGradientVector3), "gradient without color"),
            (typeof(RuleFlashSensitiveNoFlash), "flash sensitive without Flash · Blink"),
            (typeof(RuleNewEveryRead), "new traits per read"),
        };

        // the saved type names — a rename or move needs [MovedFrom] and a new row with the reason
        static readonly string[] SavedTypes =
        {
            "xpTURN.Feedback:xpTURN.Feedback.HoldItem",
            "xpTURN.Feedback:xpTURN.Feedback.LoopEndItem",
            "xpTURN.Feedback:xpTURN.Feedback.LoopStartItem",
            "xpTURN.Feedback:xpTURN.Feedback.WaitForSignalItem",
            "xpTURN.Feedback:xpTURN.Feedback.WaitItem",
            "xpTURN.Feedback:xpTURN.Feedback.CameraShakeItem",
            "xpTURN.Feedback:xpTURN.Feedback.HitStopItem",
            "xpTURN.Feedback:xpTURN.Feedback.LightIntensityItem",
            "xpTURN.Feedback:xpTURN.Feedback.MaterialColorItem",
            "xpTURN.Feedback:xpTURN.Feedback.PositionItem",
            "xpTURN.Feedback:xpTURN.Feedback.RotationItem",
            "xpTURN.Feedback:xpTURN.Feedback.ScaleItem",
            "xpTURN.Feedback:xpTURN.Feedback.SetActiveItem",
            "xpTURN.Feedback:xpTURN.Feedback.UnityEventItem",
            "xpTURN.Feedback:xpTURN.Feedback.LightRangeItem",
            "xpTURN.Feedback:xpTURN.Feedback.LightColorItem",
            "xpTURN.Feedback:xpTURN.Feedback.LightSpotAngleItem",
            "xpTURN.Feedback:xpTURN.Feedback.LightShadowStrengthItem",
            "xpTURN.Feedback:xpTURN.Feedback.Light2DIntensityItem",
            "xpTURN.Feedback:xpTURN.Feedback.Light2DColorItem",
            "xpTURN.Feedback:xpTURN.Feedback.Light2DRadiusItem",
            "xpTURN.Feedback:xpTURN.Feedback.Light2DFalloffItem",
            "xpTURN.Feedback:xpTURN.Feedback.GraphicColorItem",
            "xpTURN.Feedback:xpTURN.Feedback.GraphicAlphaItem",
            "xpTURN.Feedback:xpTURN.Feedback.GraphicCrossFadeItem",
            "xpTURN.Feedback:xpTURN.Feedback.GraphicMaterialItem",
            "xpTURN.Feedback:xpTURN.Feedback.GraphicRaycastTargetItem",
            "xpTURN.Feedback:xpTURN.Feedback.GraphicTextureOffsetItem",
            "xpTURN.Feedback:xpTURN.Feedback.GraphicTextureScaleItem",
            "xpTURN.Feedback:xpTURN.Feedback.ImageFillItem",
            "xpTURN.Feedback:xpTURN.Feedback.ImageSpriteItem",
            "xpTURN.Feedback:xpTURN.Feedback.ImageSpriteSheetItem",
            "xpTURN.Feedback:xpTURN.Feedback.CanvasGroupAlphaItem",
            "xpTURN.Feedback:xpTURN.Feedback.CanvasGroupInteractableItem",
            "xpTURN.Feedback:xpTURN.Feedback.CanvasGroupBlocksRaycastsItem",
            "xpTURN.Feedback:xpTURN.Feedback.TextContentItem",
            "xpTURN.Feedback:xpTURN.Feedback.TextFontSizeItem",
            "xpTURN.Feedback:xpTURN.Feedback.SelectableInteractableItem",
            "xpTURN.Feedback:xpTURN.Feedback.RectSizeDeltaItem",
            "xpTURN.Feedback:xpTURN.Feedback.RectPivotItem",
            "xpTURN.Feedback:xpTURN.Feedback.RectAnchorsItem",
            "xpTURN.Feedback:xpTURN.Feedback.RectOffsetsItem",
            "xpTURN.Feedback:xpTURN.Feedback.VideoPlayerControlItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPFontSizeItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPCharacterSpacingItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPWordSpacingItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPLineSpacingItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPParagraphSpacingItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPDilateItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPSoftnessItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPOutlineWidthItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPOutlineColorItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPUnderlayColorItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPUnderlayOffsetItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPTextItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPRevealItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPCountToItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPCharacterOffsetItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPCharacterScaleItem",
            "xpTURN.Feedback:xpTURN.Feedback.TMPCharacterAlphaItem",
            "xpTURN.Feedback:xpTURN.Feedback.ScreenFlashItem",
            "xpTURN.Feedback:xpTURN.Feedback.ScreenFadeItem",
            "xpTURN.Feedback:xpTURN.Feedback.FloatingTextItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXBloomItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXChromaticAberrationItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXColorAdjustmentsItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXDepthOfFieldItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXFilmGrainItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXLensDistortionItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXMotionBlurItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXPaniniProjectionItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXVignetteItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXWhiteBalanceItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXChannelMixerItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXSplitToningItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXLiftGammaGainItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXShadowsMidtonesHighlightsItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXVolumeBlendItem",
            "xpTURN.Feedback:xpTURN.Feedback.PostFXVolumeWeightItem",
            "xpTURN.Feedback:xpTURN.Feedback.CinemachineImpulseItem",
            "xpTURN.Feedback:xpTURN.Feedback.CinemachineImpulseClearItem",
            "xpTURN.Feedback:xpTURN.Feedback.CinemachineLensFieldOfViewItem",
            "xpTURN.Feedback:xpTURN.Feedback.CinemachineLensOrthographicSizeItem",
            "xpTURN.Feedback:xpTURN.Feedback.CinemachineLensDutchItem",
            "xpTURN.Feedback:xpTURN.Feedback.CinemachineNoiseAmplitudeGainItem",
            "xpTURN.Feedback:xpTURN.Feedback.CinemachineNoiseFrequencyGainItem",
            "xpTURN.Feedback:xpTURN.Feedback.CinemachineActivateCameraItem",
            "xpTURN.Feedback:xpTURN.Feedback.GamepadRumbleItem",
            "xpTURN.Feedback:xpTURN.Feedback.CameraFieldOfViewItem",
            "xpTURN.Feedback:xpTURN.Feedback.CameraOrthographicSizeItem",
            "xpTURN.Feedback:xpTURN.Feedback.CameraZoomItem",
            "xpTURN.Feedback:xpTURN.Feedback.CameraClippingPlanesItem",
            "xpTURN.Feedback:xpTURN.Feedback.CameraBackgroundColorItem",
            "xpTURN.Feedback:xpTURN.Feedback.SquashAndStretchItem",
            "xpTURN.Feedback:xpTURN.Feedback.LookAtItem",
            "xpTURN.Feedback:xpTURN.Feedback.RotateAroundItem",
            "xpTURN.Feedback:xpTURN.Feedback.MatchTransformItem",
            "xpTURN.Feedback:xpTURN.Feedback.SetParentItem",
            "xpTURN.Feedback:xpTURN.Feedback.MaterialFloatItem",
            "xpTURN.Feedback:xpTURN.Feedback.MaterialVectorItem",
            "xpTURN.Feedback:xpTURN.Feedback.TextureOffsetItem",
            "xpTURN.Feedback:xpTURN.Feedback.TextureScaleItem",
            "xpTURN.Feedback:xpTURN.Feedback.EmissionItem",
            "xpTURN.Feedback:xpTURN.Feedback.MaterialSwapItem",
            "xpTURN.Feedback:xpTURN.Feedback.RendererVisibilityItem",
            "xpTURN.Feedback:xpTURN.Feedback.ShaderGlobalFloatItem",
            "xpTURN.Feedback:xpTURN.Feedback.ShaderGlobalColorItem",
            "xpTURN.Feedback:xpTURN.Feedback.ShaderGlobalVectorItem",
            "xpTURN.Feedback:xpTURN.Feedback.FogColorItem",
            "xpTURN.Feedback:xpTURN.Feedback.FogDensityItem",
            "xpTURN.Feedback:xpTURN.Feedback.SkyboxExposureItem",
            "xpTURN.Feedback:xpTURN.Feedback.SkyboxTintItem",
            "xpTURN.Feedback:xpTURN.Feedback.SkyboxRotationItem",
            "xpTURN.Feedback:xpTURN.Feedback.AmbientColorItem",
            "xpTURN.Feedback:xpTURN.Feedback.AmbientIntensityItem",
            "xpTURN.Feedback:xpTURN.Feedback.SpriteColorItem",
            "xpTURN.Feedback:xpTURN.Feedback.SpriteAlphaItem",
            "xpTURN.Feedback:xpTURN.Feedback.SpriteSwapItem",
            "xpTURN.Feedback:xpTURN.Feedback.SpriteFlipItem",
            "xpTURN.Feedback:xpTURN.Feedback.SpriteSheetItem",
            "xpTURN.Feedback:xpTURN.Feedback.LineWidthItem",
            "xpTURN.Feedback:xpTURN.Feedback.LineColorItem",
            "xpTURN.Feedback:xpTURN.Feedback.TrailTimeItem",
            "xpTURN.Feedback:xpTURN.Feedback.TrailWidthItem",
            "xpTURN.Feedback:xpTURN.Feedback.TrailEmittingItem",
            "xpTURN.Feedback:xpTURN.Feedback.TrailClearItem",
            "xpTURN.Feedback:xpTURN.Feedback.ComponentEnabledItem",
            "xpTURN.Feedback:xpTURN.Feedback.SpawnItem",
            "xpTURN.Feedback:xpTURN.Feedback.DespawnItem",
            "xpTURN.Feedback:xpTURN.Feedback.SetLayerItem",
            "xpTURN.Feedback:xpTURN.Feedback.ParticleControlItem",
            "xpTURN.Feedback:xpTURN.Feedback.ParticleEmitItem",
            "xpTURN.Feedback:xpTURN.Feedback.ParticleSpawnItem",
            "xpTURN.Feedback:xpTURN.Feedback.ParticleSimulationSpeedItem",
            "xpTURN.Feedback:xpTURN.Feedback.ParticleEmissionRateItem",
            "xpTURN.Feedback:xpTURN.Feedback.AnimatorParameterItem",
            "xpTURN.Feedback:xpTURN.Feedback.AnimatorFloatItem",
            "xpTURN.Feedback:xpTURN.Feedback.AnimatorSpeedItem",
            "xpTURN.Feedback:xpTURN.Feedback.AnimatorLayerWeightItem",
            "xpTURN.Feedback:xpTURN.Feedback.AnimatorStateItem",
            "xpTURN.Feedback:xpTURN.Feedback.RigidbodyForceItem",
            "xpTURN.Feedback:xpTURN.Feedback.RigidbodyExplosionItem",
            "xpTURN.Feedback:xpTURN.Feedback.RigidbodyVelocityItem",
            "xpTURN.Feedback:xpTURN.Feedback.Rigidbody2DForceItem",
            "xpTURN.Feedback:xpTURN.Feedback.Rigidbody2DVelocityItem",
            "xpTURN.Feedback:xpTURN.Feedback.TimeScaleItem",
            "xpTURN.Feedback:xpTURN.Feedback.CustomFloatItem",
            "xpTURN.Feedback:xpTURN.Feedback.CustomVector3Item",
            "xpTURN.Feedback:xpTURN.Feedback.CustomColorItem",
            "xpTURN.Feedback:xpTURN.Feedback.CustomFloatEventItem",
            "xpTURN.Feedback:xpTURN.Feedback.PlayPlayerItem",
            "xpTURN.Feedback:xpTURN.Feedback.PlayerChainItem",
            "xpTURN.Feedback:xpTURN.Feedback.PlayerControlItem",
            "xpTURN.Feedback:xpTURN.Feedback.RandomPlayerItem",
            "xpTURN.Feedback:xpTURN.Feedback.RandomUnityEventItem",
            "xpTURN.Feedback:xpTURN.Feedback.SignalBroadcastItem",
            "xpTURN.Feedback:xpTURN.Feedback.DebugItem",
            "xpTURN.Feedback:xpTURN.Feedback.SetMotion",
            "xpTURN.Feedback:xpTURN.Feedback.TweenMotion",
            "xpTURN.Feedback:xpTURN.Feedback.PunchMotion",
            "xpTURN.Feedback:xpTURN.Feedback.ShakeMotion",
            "xpTURN.Feedback:xpTURN.Feedback.CurveMotion",
            "xpTURN.Feedback:xpTURN.Feedback.FlashMotion",
            "xpTURN.Feedback:xpTURN.Feedback.GradientMotion",
            "xpTURN.Feedback:xpTURN.Feedback.OscillateMotion",
            "xpTURN.Feedback:xpTURN.Feedback.BlinkMotion",
            "xpTURN.Feedback:xpTURN.Feedback.WiggleMotion",
            "xpTURN.Feedback:xpTURN.Feedback.SpringMotion",
        };

        // the saved field names by declaring type — a rename is a field step that keeps the old field and a new row with the reason
        static readonly (string Type, string Fields)[] SavedFields =
        {
            ("FeedbackItem", "m_Active m_Label m_Timing"),
            ("EffectItem", "m_Conditions m_PlayDirection m_Random m_Intensity m_Category"),
            ("TargetItem", "m_Target m_Motion m_DirectWrite"),
            ("ActionItem", ""),
            ("CustomActionItem", "m_Target"),
            ("CustomEffectItem", "m_Base"),
            ("FlowItem", ""),
            ("WaitItem", ""),
            ("WaitForSignalItem", "m_Timeout"),
            ("LoopStartItem", ""),
            ("LoopEndItem", "m_LoopCount m_LoopGap"),
            ("HoldItem", ""),
            ("FeedbackMotion", ""),
            ("FeedbackChannel", ""),
            ("FeedbackPlayer", "m_Items m_Preset m_Anchors m_FormatVersion m_Initialization m_Policy m_MaxOverlap m_DurationMultiplier m_StartDelay m_AutoPlay m_TimeMode m_Speed m_RandomizeDuration m_DurationRange m_Cooldown m_Chance m_Intensity m_RemapIntensity m_IntensityRemap m_UseDistanceGate m_GateReference m_GateAnchor m_GateDistance m_UseGateFalloff m_GateFalloff m_UnityEvents m_Direction m_ApplyInitialState m_DisableBehavior"),
            ("PlayerUnityEvent", "m_Kind m_Event"),
            // the preset (an item list and its format version)
            ("FeedbackPreset", "m_Items m_FormatVersion"),
            ("PlayerAnchor", "m_Name m_Object"),
            // the receivers and the settings
            ("FeedbackReceiver", "m_Channel m_Player m_Range"),
            ("CameraShakeReceiver", "m_Channel m_Range"),
            ("FeedbackSettings", "m_Categories m_FlashLimit m_TimeScaleCapacity m_AdjustFixedDeltaTime m_ShakeReceiversPerPlay m_ReceiverContributions m_MaximumUnscaledDelta m_OverlaySortingOrder m_PostFXLayer m_PostFXPriority"),
            ("CategoryDefault", "m_Category m_Enabled m_Scale"),
            // a container of one item
            ("MotionDriver", "m_Item m_Mode m_TimeMode m_Intensity m_FollowTarget m_FormatVersion"),
            ("ItemTiming", "m_Rule m_Delay m_Duration m_Infinite m_Clock m_RepeatCount m_RepeatForever m_RepeatGap m_Background"),
            ("ItemConditions", "m_Chance m_Cooldown m_MaxPlays m_Direction m_UseIntensityRange m_IntensityRange"),
            ("ItemRandomness", "m_RandomizeOutput m_OutputRange m_RandomizeDuration m_DurationRange"),
            ("CategoryOverride", "m_Override m_Category"),
            ("TargetRef", "m_Mode m_Object m_Path m_Index m_ChildLookup m_Anchor m_Channel"),
            ("ChannelRef", "m_Mode m_Id m_Asset"),
            ("PositionItem", "m_Space"),
            ("RotationItem", "m_Space m_Interpolation"),
            ("ScaleItem", ""),
            ("MaterialColorItem", "m_PropertyName m_MaterialIndex m_WriteMode"),
            ("LightIntensityItem", ""),
            ("CameraShakeItem", "m_RotationAmplitude"),
            ("SetActiveItem", ""),
            ("UnityEventItem", "m_Event"),
            ("HitStopItem", "m_FreezeDuration m_MinimumTimeScale m_TimeScale"),
            // the common fields only
            ("LightRangeItem", ""),
            ("LightColorItem", ""),
            ("LightSpotAngleItem", ""),
            ("LightShadowStrengthItem", ""),
            ("Light2DIntensityItem", ""),
            ("Light2DColorItem", ""),
            ("Light2DRadiusItem", ""),
            ("Light2DFalloffItem", ""),
            ("GraphicColorItem", ""),
            ("GraphicAlphaItem", ""),
            ("GraphicCrossFadeItem", ""),
            ("GraphicMaterialItem", "m_Material"),
            ("GraphicRaycastTargetItem", ""),
            ("GraphicTextureOffsetItem", ""),
            ("GraphicTextureScaleItem", ""),
            ("ImageFillItem", ""),
            ("ImageSpriteItem", "m_Sprite"),
            ("ImageSpriteSheetItem", "m_Frames"),
            ("CanvasGroupAlphaItem", ""),
            ("CanvasGroupInteractableItem", ""),
            ("CanvasGroupBlocksRaycastsItem", ""),
            ("TextContentItem", "m_Text"),
            ("TextFontSizeItem", ""),
            ("SelectableInteractableItem", ""),
            ("RectSizeDeltaItem", ""),
            ("RectPivotItem", ""),
            ("RectAnchorsItem", ""),
            ("RectOffsetsItem", ""),
            ("VideoPlayerControlItem", "m_Action m_Time"),
            ("TMPFontSizeItem", ""),
            ("TMPCharacterSpacingItem", ""),
            ("TMPWordSpacingItem", ""),
            ("TMPLineSpacingItem", ""),
            ("TMPParagraphSpacingItem", ""),
            ("TMPDilateItem", ""),
            ("TMPSoftnessItem", ""),
            ("TMPOutlineWidthItem", ""),
            ("TMPOutlineColorItem", ""),
            ("TMPUnderlayColorItem", ""),
            ("TMPUnderlayOffsetItem", ""),
            ("TMPTextItem", "m_Text"),
            ("TMPRevealItem", "m_Unit"),
            ("TMPCountToItem", "m_From m_To m_Format"),
            ("TMPCharacterOffsetItem", "m_Stagger m_Order"),
            ("TMPCharacterScaleItem", "m_Stagger m_Order"),
            ("TMPCharacterAlphaItem", "m_Stagger m_Order"),
            ("ScreenFlashItem", "m_Color"),
            ("ScreenFadeItem", "m_Color m_Shape m_From m_Center"),
            ("FloatingTextItem", "m_Style m_Source m_Text m_Format m_Multiplier m_Offset m_Follow m_PoolSize"),
            ("PostFXBloomItem", "m_IntensityTarget m_Threshold m_Scatter m_Tint"),
            ("PostFXChromaticAberrationItem", "m_IntensityTarget"),
            ("PostFXColorAdjustmentsItem", "m_PostExposure m_Contrast m_ColorFilter m_HueShift m_Saturation"),
            ("PostFXDepthOfFieldItem", "m_OverrideMode m_Mode m_GaussianStart m_GaussianEnd m_FocusDistance m_Aperture m_FocalLength"),
            ("PostFXFilmGrainItem", "m_IntensityTarget m_Response"),
            ("PostFXLensDistortionItem", "m_IntensityTarget m_Scale m_Center"),
            ("PostFXMotionBlurItem", "m_IntensityTarget m_Clamp"),
            ("PostFXPaniniProjectionItem", "m_Distance m_CropToFit"),
            ("PostFXVignetteItem", "m_IntensityTarget m_Smoothness m_Color m_Center m_OverrideRounded m_Rounded"),
            ("PostFXWhiteBalanceItem", "m_Temperature m_Tint"),
            ("PostFXChannelMixerItem", "m_RedOutRedIn m_RedOutGreenIn m_RedOutBlueIn m_GreenOutRedIn m_GreenOutGreenIn m_GreenOutBlueIn m_BlueOutRedIn m_BlueOutGreenIn m_BlueOutBlueIn"),
            ("PostFXSplitToningItem", "m_Shadows m_Highlights m_Balance"),
            ("PostFXLiftGammaGainItem", "m_Lift m_Gamma m_Gain"),
            ("PostFXShadowsMidtonesHighlightsItem", "m_Shadows m_Midtones m_Highlights m_ShadowsStart m_ShadowsEnd m_HighlightsStart m_HighlightsEnd"),
            ("PostFXVolumeBlendItem", "m_Profile"),
            ("PostFXVolumeWeightItem", ""),
            ("PostFXFloat", "m_Override m_Value"),
            ("PostFXColor", "m_Override m_Value"),
            ("PostFXVector2", "m_Override m_Value"),
            ("PostFXVector4", "m_Override m_Value"),
            ("CinemachineImpulseItem", "m_Origin m_Mode m_Source m_Shape m_CustomShape m_Spread m_Duration m_Channel m_DissipationDistance m_DissipationRate m_PropagationSpeed m_Velocity"),
            ("CinemachineImpulseClearItem", ""),
            ("CinemachineLensFieldOfViewItem", ""),
            ("CinemachineLensOrthographicSizeItem", ""),
            ("CinemachineLensDutchItem", ""),
            ("CinemachineNoiseAmplitudeGainItem", ""),
            ("CinemachineNoiseFrequencyGainItem", ""),
            ("CinemachineActivateCameraItem", "m_ToTop m_Priority m_Blend"),
            ("CinemachineBlendSpec", "m_Override m_Style m_Time m_Curve"),
            ("GamepadRumbleItem", "m_Device m_Player"),
            ("CameraFieldOfViewItem", ""),
            ("CameraOrthographicSizeItem", ""),
            ("CameraZoomItem", ""),
            ("CameraClippingPlanesItem", ""),
            ("CameraBackgroundColorItem", ""),
            // the second references are TargetRef
            ("SquashAndStretchItem", "m_Axis"),
            ("LookAtItem", "m_Mode m_LookTarget m_Position m_Direction m_Up"),
            ("RotateAroundItem", "m_Pivot m_Axis"),
            ("MatchTransformItem", "m_Destination m_Position m_Rotation m_Scale m_Track"),
            ("SetParentItem", "m_NewParent m_WorldPositionStays"),
            // the emission's multiplier is not EffectItem.m_Intensity
            ("MaterialFloatItem", "m_PropertyName m_MaterialIndex m_WriteMode"),
            ("MaterialVectorItem", "m_PropertyName m_MaterialIndex m_WriteMode"),
            ("TextureOffsetItem", "m_PropertyName m_MaterialIndex m_WriteMode"),
            ("TextureScaleItem", "m_PropertyName m_MaterialIndex m_WriteMode"),
            ("EmissionItem", "m_PropertyName m_MaterialIndex m_WriteMode m_Multiplier"),
            ("MaterialSwapItem", "m_Material m_MaterialIndex"),
            ("RendererVisibilityItem", ""),
            ("ShaderGlobalFloatItem", "m_PropertyName"),
            ("ShaderGlobalColorItem", "m_PropertyName"),
            ("ShaderGlobalVectorItem", "m_PropertyName"),
            ("FogColorItem", ""),
            ("FogDensityItem", ""),
            ("SkyboxExposureItem", "m_PropertyName"),
            ("SkyboxTintItem", "m_PropertyName"),
            ("SkyboxRotationItem", "m_PropertyName"),
            ("AmbientColorItem", ""),
            ("AmbientIntensityItem", ""),
            ("SpriteColorItem", ""),
            ("SpriteAlphaItem", ""),
            ("SpriteSwapItem", "m_Sprite"),
            ("SpriteFlipItem", "m_Axis"),
            ("SpriteSheetItem", "m_Frames"),
            ("LineWidthItem", ""),
            ("LineColorItem", "m_End"),
            ("TrailTimeItem", ""),
            ("TrailWidthItem", ""),
            ("TrailEmittingItem", ""),
            ("TrailClearItem", ""),
            ("ComponentEnabledItem", "m_ComponentType m_ComponentIndex"),
            ("SpawnItem", "m_Prefab m_Pooled m_PoolSize m_Place m_Offset m_MatchRotation m_Parent m_Lifetime"),
            ("DespawnItem", ""),
            ("SetLayerItem", "m_Layer"),
            ("ParticleControlItem", "m_Action m_WithChildren m_StopMode"),
            ("ParticleEmitItem", "m_Count"),
            ("ParticleSpawnItem", "m_Prefab m_Pooled m_PoolSize m_Place m_Offset m_MatchRotation m_Parent m_StopAfter"),
            ("ParticleSimulationSpeedItem", ""),
            ("ParticleEmissionRateItem", ""),
            ("AnimatorParameterItem", "m_Parameter m_Kind m_Bool m_Int m_Float m_Pick m_Min m_Max m_Step"),
            ("AnimatorFloatItem", "m_Parameter"),
            ("AnimatorSpeedItem", ""),
            ("AnimatorLayerWeightItem", "m_Layer"),
            ("AnimatorStateItem", "m_State m_Layer m_Transition m_Offset"),
            ("RigidbodyForceItem", "m_Kind m_Space m_Mode m_Min m_Max"),
            ("RigidbodyExplosionItem", "m_MinForce m_MaxForce m_Radius m_Upwards m_Mode m_Offset m_Layers m_MaxBodies m_Triggers"),
            ("RigidbodyVelocityItem", "m_Write m_Space m_Min m_Max"),
            ("Rigidbody2DForceItem", "m_Kind m_Space m_Mode m_Min m_Max m_MinTorque m_MaxTorque"),
            ("Rigidbody2DVelocityItem", "m_Write m_Space m_Min m_Max"),
            ("TimeScaleItem", ""),
            ("CustomFloatItem", ""),
            ("CustomVector3Item", ""),
            ("CustomColorItem", ""),
            ("CustomFloatEventItem", "m_Event m_Base m_InvokeInPreview"),
            ("PlayPlayerItem", "m_Player m_Play"),
            ("PlayerChainItem", "m_Entries m_Play"),
            ("PlayerControlItem", "m_Players m_Action m_StopMode"),
            ("RandomPlayerItem", "m_Entries m_Play"),
            ("RandomUnityEventItem", "m_Entries"),
            ("SignalBroadcastItem", "m_Channel m_Value"),
            ("DebugItem", "m_Action m_Message m_DevelopmentOnly"),
            ("NestedPlay", "m_Wait m_Timeout m_PassIntensity m_PassDirection m_PassPosition"),
            ("ChainEntry", "m_Player m_Delay m_Wait m_Active"),
            ("WeightedPlayer", "m_Player m_Weight"),
            ("WeightedUnityEvent", "m_Weight m_Event"),
            ("FeedbackSignalReceiver", "m_Channel m_Range m_OnSignal"),
            ("SetMotion", "m_Params"),
            ("SetParams", "m_Value m_Relative"),
            ("TweenMotion", "m_Params"),
            ("TweenParams", "m_Mode m_From m_To m_Ease m_Loops m_Yoyo"),
            ("PunchMotion", "m_Params"),
            ("PunchParams", "m_Amplitude m_Vibrato m_Decay"),
            ("ShakeMotion", "m_Params"),
            ("ShakeParams", "m_Amplitude m_Frequency m_Noise m_Falloff"),
            ("CurveMotion", "m_Params"),
            ("CurveParams", "m_Curve m_RemapZero m_RemapOne"),
            ("FlashMotion", "m_Params"),
            ("FlashParams", "m_Target m_Attack m_Hold m_Release m_Ease"),
            ("GradientMotion", "m_Params"),
            ("GradientParams", "m_Gradient m_Mode"),
            ("OscillateMotion", "m_Params"),
            ("OscillateParams", "m_Amplitude m_Frequency m_Waveform m_Phase"),
            ("BlinkMotion", "m_Params"),
            ("BlinkParams", "m_Value m_Period m_Duty m_Jitter m_StartOn"),
            ("WiggleMotion", "m_Params"),
            ("WiggleParams", "m_Amplitude m_Frequency m_Octaves"),
            ("SpringMotion", "m_Params"),
            ("SpringParams", "m_Mode m_Value m_Relative m_Frequency m_Damping m_Settle"),
            // public API fields, saved in the user's components
            ("SpringFloat", "Value Velocity Target Frequency Damping"),
            ("SpringVector2", "Value Velocity Target Frequency Damping"),
            ("SpringVector3", "Value Velocity Target Frequency Damping"),
            ("SpringColor", "Value Velocity Target Frequency Damping"),
            ("Ease", "m_Type m_P1 m_P2 m_Curve"),
        };

        static IEnumerable<Type> CoreConcrete<T>() => TypeCache.GetTypesDerivedFrom<T>().Where(t => t.Assembly == Core && !t.IsAbstract);

        static string Sorted(IEnumerable<string> names) => string.Join(" ", names.OrderBy(n => n, StringComparer.Ordinal));

        // the contract for the flow items, and the closed item layer
        [Test]
        public void Reg21_ConcreteItemsKeepTheContractAndTheLayerIsClosed()
        {
            var paths = new HashSet<string>();
            foreach (var type in CoreConcrete<FeedbackItem>())
            {
                Assert.That(type.IsSealed, $"{type.Name} is sealed");
                Assert.That(type.IsSerializable, $"{type.Name} is [Serializable]");
                Assert.That(type.GetConstructor(Type.EmptyTypes), Is.Not.Null, $"{type.Name} has a public parameterless constructor");
                var meta = type.GetCustomAttribute<FeedbackTargetAttribute>(false);
                Assert.That(meta, Is.Not.Null, $"{type.Name} has [FeedbackTarget]");
                Assert.That(meta.Path, Is.Not.Empty, type.Name);
                Assert.That(paths.Add(meta.Path), $"{type.Name}: the path {meta.Path} is taken");
            }
            foreach (var type in CoreConcrete<FeedbackMotion>())
                Assert.That(type.IsSealed, $"{type.Name} is sealed");

            // the two extension bases open the layer (protected constructors — their bindings are the package's adapters); the
            // other six stay closed
            var bases = Core.GetTypes().Where(t => t.IsAbstract && (typeof(FeedbackItem).IsAssignableFrom(t) || typeof(FeedbackMotion).IsAssignableFrom(t))).ToArray();
            Assert.That(Sorted(bases.Select(t => t.Name)), Is.EqualTo("ActionItem CustomActionItem CustomEffectItem EffectItem FeedbackItem FeedbackMotion FlowItem TargetItem"), "the eight abstract bases");
            var extension = new[] { typeof(CustomActionItem), typeof(CustomEffectItem) };
            foreach (var type in bases)
                foreach (var ctor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    Assert.That(extension.Contains(type) ? ctor.IsFamily : ctor.IsAssembly, $"{type.Name} constructor is {(extension.Contains(type) ? "protected" : "internal")}");
        }

        [Test]
        public void FlowItemsCarryTheirDefaultsAndMetadata()
        {
            var wait = new WaitItem();
            Assert.That((wait.m_Timing.m_Duration, wait.m_Timing.m_Infinite, wait.m_Timing.m_Clock), Is.EqualTo((1f, false, ItemClock.Player)));
            Assert.That(new WaitForSignalItem().m_Timeout, Is.Zero);
            var loopEnd = new LoopEndItem();
            Assert.That((loopEnd.m_LoopCount, loopEnd.m_LoopGap), Is.EqualTo((2, 0f)));

            foreach (var (type, path, preview) in FlowMeta)
            {
                var meta = type.GetCustomAttribute<FeedbackTargetAttribute>(false);
                Assert.That(meta, Is.Not.Null, type.Name);
                Assert.That((meta.Path, meta.Preview), Is.EqualTo((path, preview)), type.Name);
            }
        }

        // MotionMask follows MotionKind bit for bit
        [Test]
        public void MotionMaskFollowsMotionKind()
        {
            var kinds = Enum.GetNames(typeof(MotionKind));
            foreach (var name in kinds)
            {
                Assert.That(Enum.IsDefined(typeof(MotionMask), name), $"MotionMask.{name}");
                Assert.That(Convert.ToInt32(Enum.Parse(typeof(MotionMask), name)), Is.EqualTo(1 << Convert.ToInt32(Enum.Parse(typeof(MotionKind), name))), name);
            }
            Assert.That(Sorted(Enum.GetNames(typeof(MotionMask))), Is.EqualTo(Sorted(kinds.Append("None"))));

            var traits = new TargetTraits(valueKind: ValueKind.Vector3, supportedMotions: MotionMask.Tween | MotionMask.Spring,
                category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);
            foreach (MotionKind kind in Enum.GetValues(typeof(MotionKind)))
                Assert.That(traits.Supports(kind), Is.EqualTo(kind == MotionKind.Tween || kind == MotionKind.Spring), kind.ToString());
        }

        // the override wins, then flashing, then the declared category
        [Test]
        public void EffectiveCategoryAndFlashesFollowTheRule()
        {
            static TraitsTestMotion MotionOf(MotionKind kind) => new TraitsTestMotion { m_Kind = kind };

            var flash = new TraitsFlashTarget();
            Assert.That((flash.Flashes, flash.EffectiveCategory), Is.EqualTo((false, FeedbackCategory.None)), "no motion");
            flash.m_Motion = MotionOf(MotionKind.Tween);
            Assert.That((flash.Flashes, flash.EffectiveCategory), Is.EqualTo((false, FeedbackCategory.None)), "tween");
            flash.m_Motion = MotionOf(MotionKind.Flash);
            Assert.That((flash.Flashes, flash.EffectiveCategory), Is.EqualTo((true, FeedbackCategory.Flash)), "flash");
            flash.m_Motion = MotionOf(MotionKind.Blink);
            Assert.That((flash.Flashes, flash.EffectiveCategory), Is.EqualTo((true, FeedbackCategory.Flash)), "blink");
            flash.m_Category = new CategoryOverride { m_Override = true, m_Category = FeedbackCategory.Camera };
            Assert.That((flash.Flashes, flash.EffectiveCategory), Is.EqualTo((true, FeedbackCategory.Camera)), "the override wins and Flashes stays");
            flash.m_Category.m_Category = FeedbackCategory.None;
            Assert.That(flash.EffectiveCategory, Is.EqualTo(FeedbackCategory.None), "overridden to None");

            var motion = new TraitsMotionTarget { m_Motion = MotionOf(MotionKind.Flash) };
            Assert.That((motion.Flashes, motion.EffectiveCategory), Is.EqualTo((false, FeedbackCategory.Motion)), "not flash sensitive");
            motion.m_Category = new CategoryOverride { m_Override = true, m_Category = FeedbackCategory.Flash };
            Assert.That((motion.Flashes, motion.EffectiveCategory), Is.EqualTo((false, FeedbackCategory.Flash)), "overridden to Flash");

            var plain = new TraitsPlainAction();
            Assert.That((plain.Flashes, plain.EffectiveCategory), Is.EqualTo((false, FeedbackCategory.None)), "an action is not flashing by default");
            Assert.That(new TraitsTimeAction().EffectiveCategory, Is.EqualTo(FeedbackCategory.Time));
            var flashing = new TraitsFlashingAction();
            Assert.That((flashing.Flashes, flashing.EffectiveCategory), Is.EqualTo((true, FeedbackCategory.Flash)), "an action may flash");
        }

        // the trait rules hold for Core's targets; the controls prove the rules bite
        [Test]
        public void TraitRulesHoldForCoreTargetsAndCatchTheControls()
        {
            foreach (var type in CoreConcrete<TargetItem>())
                Assert.That(TraitViolations(type), Is.Empty, type.Name);
            foreach (var (type, violation) in RuleControls)
                Assert.That(TraitViolations(type), Is.EqualTo(new[] { violation }), type.Name);
            Assert.That(TraitViolations(typeof(RuleKeepsAll)), Is.Empty);
        }

        static List<string> TraitViolations(Type type)
        {
            var a = (TargetItem)Activator.CreateInstance(type);
            var b = (TargetItem)Activator.CreateInstance(type);
            var traits = a.Traits;
            var violations = new List<string>();
            if (!ReferenceEquals(traits, a.Traits) || !ReferenceEquals(traits, b.Traits))
                violations.Add("new traits per read");
            if (traits.SupportedMotions == MotionMask.None)
                violations.Add("no motions");
            if (traits.ValueKind == ValueKind.Bool && (traits.SupportedMotions & ~(MotionMask.Set | MotionMask.Blink)) != 0)
                violations.Add("bool beyond Set · Blink");
            if ((traits.SupportedMotions & MotionMask.Gradient) != 0 && traits.ValueKind != ValueKind.Color)
                violations.Add("gradient without color");
            if (traits.FlashSensitive && (traits.SupportedMotions & (MotionMask.Flash | MotionMask.Blink)) == 0)
                violations.Add("flash sensitive without Flash · Blink");
            return violations;
        }

        // a Time item never counts its slot on a clock it stops
        [Test]
        public void TimeItemsCountOnTheUnscaledClock()
        {
            var clocks = new[] { ItemClock.Player, ItemClock.Scaled, ItemClock.Unscaled };
            var time = new TraitsTimeAction();
            foreach (var clock in clocks)
            {
                time.m_Timing.m_Clock = clock;
                Assert.That(time.EffectiveClock, Is.EqualTo(ItemClock.Unscaled), clock.ToString());
            }
            time.m_Timing.m_Clock = ItemClock.Scaled;
            time.m_Category = new CategoryOverride { m_Override = true, m_Category = FeedbackCategory.Camera };
            Assert.That(time.EffectiveClock, Is.EqualTo(ItemClock.Unscaled), "the override does not move the clock");

            var flashingTime = new TraitsTimeTarget { m_Motion = new TraitsTestMotion { m_Kind = MotionKind.Flash } };
            flashingTime.m_Timing.m_Clock = ItemClock.Scaled;
            Assert.That((flashingTime.EffectiveCategory, flashingTime.EffectiveClock), Is.EqualTo((FeedbackCategory.Flash, ItemClock.Unscaled)), "the declared category decides");

            FeedbackItem[] others = { new TraitsPlainAction(), new TraitsMotionTarget(), new WaitItem(), new HoldItem() };
            foreach (var item in others)
                foreach (var clock in clocks)
                {
                    item.m_Timing.m_Clock = clock;
                    Assert.That(item.EffectiveClock, Is.EqualTo(clock), $"{item.GetType().Name} {clock}");
                }
        }

        // the type name is the format, field names are saved — renames break data silently
        [Test]
        public void SavedTypeAndFieldNamesMatchTheGolden()
        {
            var types = CoreConcrete<FeedbackItem>().Concat(CoreConcrete<FeedbackMotion>()).Select(t => $"{t.Assembly.GetName().Name}:{t.FullName}");
            Assert.That(Sorted(types), Is.EqualTo(Sorted(SavedTypes)), "saved type names — a rename needs [MovedFrom]");

            var actual = Core.GetTypes().Where(IsSavedType).ToDictionary(t => t.Name, t => Sorted(SavedFieldNames(t)));
            var golden = SavedFields.ToDictionary(r => r.Type, r => Sorted(r.Fields.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)));
            Assert.That(Sorted(actual.Keys), Is.EqualTo(Sorted(golden.Keys)), "saved types without a row");
            foreach (var pair in golden)
                Assert.That(actual[pair.Key], Is.EqualTo(pair.Value), $"{pair.Key} — a field rename is a field step that keeps the old field");
        }

        // the springs' public fields are public API on purpose — the golden above holds their names
        static readonly Type[] PublicFieldTypes = { typeof(SpringFloat), typeof(SpringVector2), typeof(SpringVector3), typeof(SpringColor) };

        // the data's saved fields are [SerializeField] internal with the m_ prefix — a public field is public API. The containers
        // (UnityEngine.Object) too
        [Test]
        public void SavedDataFieldsAreInternalWithThePrefix()
        {
            var wrong = Core.GetTypes().Where(t => IsSavedType(t) && !PublicFieldTypes.Contains(t))
                .SelectMany(t => SavedFieldInfos(t).Where(f => !f.IsAssembly || !f.Name.StartsWith("m_", StringComparison.Ordinal)).Select(f => $"{t.Name}.{f.Name}"));
            Assert.That(wrong, Is.Empty, "saved data fields are [SerializeField] internal m_…");
        }

        // [FormerlySerializedAs] inside an item or a motion drops the prefab instance and variant overrides of that field. A
        // container's top-level field may carry it
        [Test]
        public void SavedDataFieldsCarryNoFormerlySerializedAs()
        {
            var renamed = Core.GetTypes().Where(t => IsSavedType(t) && !typeof(UnityEngine.Object).IsAssignableFrom(t))
                .SelectMany(t => SavedFieldInfos(t).Where(f => f.IsDefined(typeof(FormerlySerializedAsAttribute), false)).Select(f => $"{t.Name}.{f.Name}"));
            Assert.That(renamed, Is.Empty, "a rename is a field step that keeps the old field");
        }

        internal static bool IsSavedType(Type type)
            => typeof(UnityEngine.Object).IsAssignableFrom(type)
               || (type.IsSerializable && !type.IsEnum && !typeof(Delegate).IsAssignableFrom(type) && !type.Name.StartsWith("<", StringComparison.Ordinal));

        static IEnumerable<string> SavedFieldNames(Type type) => SavedFieldInfos(type).Select(f => f.Name);

        internal static IEnumerable<FieldInfo> SavedFieldInfos(Type type)
            => type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => (f.IsPublic && !f.IsNotSerialized) || f.IsDefined(typeof(SerializeField), false) || f.IsDefined(typeof(SerializeReference), false));
    }
}
