using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor
{
    // the representative items' traits, metadata and defaults, and a UnityEvent saved inside an item
    public sealed class DataModelTargetsTests
    {
        const string AssetPath = "Assets/DataModelTargetsTests.asset";

        const MotionMask TransformMotions = MotionMask.Tween | MotionMask.Punch | MotionMask.Shake | MotionMask.Curve | MotionMask.Oscillate | MotionMask.Wiggle | MotionMask.Spring;

        // the traits per target — written by hand, not read from the code. Every target applies as an offset
        static readonly (Type Type, ValueKind Value, MotionMask Motions, FeedbackCategory Category, bool FlashSensitive)[] TargetRows =
        {
            (typeof(PositionItem), ValueKind.Vector3, TransformMotions, FeedbackCategory.Motion, false),
            (typeof(RotationItem), ValueKind.Vector3, TransformMotions, FeedbackCategory.Motion, false),
            (typeof(ScaleItem), ValueKind.Vector3, TransformMotions, FeedbackCategory.Motion, false),
            (typeof(MaterialColorItem), ValueKind.Color,
                MotionMask.Tween | MotionMask.Flash | MotionMask.Gradient | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Blink | MotionMask.Spring,
                FeedbackCategory.None, true),
            (typeof(LightIntensityItem), ValueKind.Float,
                MotionMask.Tween | MotionMask.Flash | MotionMask.Punch | MotionMask.Shake | MotionMask.Oscillate | MotionMask.Blink | MotionMask.Spring,
                FeedbackCategory.None, true),
            (typeof(CameraShakeItem), ValueKind.Vector3, MotionMask.Shake | MotionMask.Punch | MotionMask.Wiggle | MotionMask.Curve, FeedbackCategory.Camera, false),
            (typeof(SetActiveItem), ValueKind.Bool, MotionMask.Set | MotionMask.Blink, FeedbackCategory.None, true),
            (typeof(LightRangeItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Spring, FeedbackCategory.None, false),
            (typeof(LightColorItem), ValueKind.Color, MotionMask.Tween | MotionMask.Flash | MotionMask.Gradient | MotionMask.Spring, FeedbackCategory.None, true),
            (typeof(LightSpotAngleItem), ValueKind.Float, MotionMask.Tween, FeedbackCategory.None, false),
            (typeof(LightShadowStrengthItem), ValueKind.Float, MotionMask.Tween, FeedbackCategory.None, false),
            // the 2D light — URP's
            (typeof(Light2DIntensityItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Blink, FeedbackCategory.None, true),
            (typeof(Light2DColorItem), ValueKind.Color, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.None, true),
            (typeof(Light2DRadiusItem), ValueKind.Float, MotionMask.Tween | MotionMask.Oscillate, FeedbackCategory.None, false),
            (typeof(Light2DFalloffItem), ValueKind.Float, MotionMask.Tween | MotionMask.Oscillate, FeedbackCategory.None, false),
            // uGUI's · the RectTransform's (Core) · the Video Player's
            (typeof(GraphicColorItem), ValueKind.Color, MotionMask.Tween | MotionMask.Flash | MotionMask.Gradient | MotionMask.Blink | MotionMask.Punch | MotionMask.Spring, FeedbackCategory.None, true),
            (typeof(GraphicAlphaItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash | MotionMask.Blink | MotionMask.Punch | MotionMask.Spring, FeedbackCategory.None, true),
            (typeof(GraphicCrossFadeItem), ValueKind.Color, MotionMask.Tween, FeedbackCategory.None, false),
            (typeof(GraphicMaterialItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(GraphicRaycastTargetItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(GraphicTextureOffsetItem), ValueKind.Vector2, MotionMask.Tween, FeedbackCategory.None, false),
            (typeof(GraphicTextureScaleItem), ValueKind.Vector2, MotionMask.Tween, FeedbackCategory.None, false),
            (typeof(ImageFillItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch | MotionMask.Spring, FeedbackCategory.None, false),
            (typeof(ImageSpriteItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(ImageSpriteSheetItem), ValueKind.Float, MotionMask.Tween, FeedbackCategory.None, false),
            (typeof(CanvasGroupAlphaItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash | MotionMask.Blink, FeedbackCategory.None, true),
            (typeof(CanvasGroupInteractableItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(CanvasGroupBlocksRaycastsItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(TextContentItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(TextFontSizeItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch, FeedbackCategory.None, false),
            (typeof(SelectableInteractableItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(RectSizeDeltaItem), ValueKind.Vector2, MotionMask.Tween | MotionMask.Punch | MotionMask.Spring, FeedbackCategory.Motion, false),
            (typeof(RectPivotItem), ValueKind.Vector2, MotionMask.Tween, FeedbackCategory.Motion, false),
            (typeof(RectAnchorsItem), ValueKind.Vector4, MotionMask.Tween, FeedbackCategory.Motion, false),
            (typeof(TMPFontSizeItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch | MotionMask.Spring, FeedbackCategory.None, false),
            (typeof(TMPCharacterSpacingItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Spring, FeedbackCategory.None, false),
            (typeof(TMPWordSpacingItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Spring, FeedbackCategory.None, false),
            (typeof(TMPLineSpacingItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Spring, FeedbackCategory.None, false),
            (typeof(TMPParagraphSpacingItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Spring, FeedbackCategory.None, false),
            (typeof(TMPDilateItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Spring, FeedbackCategory.None, false),
            (typeof(TMPSoftnessItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Spring, FeedbackCategory.None, false),
            (typeof(TMPOutlineWidthItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Spring, FeedbackCategory.None, false),
            (typeof(TMPOutlineColorItem), ValueKind.Color, MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Spring, FeedbackCategory.None, true),
            (typeof(TMPUnderlayColorItem), ValueKind.Color, MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Spring, FeedbackCategory.None, true),
            (typeof(TMPUnderlayOffsetItem), ValueKind.Vector2, MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Spring, FeedbackCategory.None, false),
            (typeof(TMPTextItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(TMPRevealItem), ValueKind.Float, MotionMask.Tween, FeedbackCategory.None, false),
            (typeof(TMPCountToItem), ValueKind.Float, MotionMask.Tween, FeedbackCategory.None, false),
            (typeof(TMPCharacterOffsetItem), ValueKind.Vector2, MotionMask.Tween | MotionMask.Oscillate | MotionMask.Wiggle, FeedbackCategory.Motion, false),
            (typeof(TMPCharacterScaleItem), ValueKind.Float, MotionMask.Tween | MotionMask.Oscillate | MotionMask.Wiggle, FeedbackCategory.Motion, false),
            (typeof(TMPCharacterAlphaItem), ValueKind.Float, MotionMask.Tween | MotionMask.Oscillate, FeedbackCategory.None, false),
            (typeof(ScreenFlashItem), ValueKind.Float, MotionMask.Flash | MotionMask.Blink, FeedbackCategory.None, true),
            (typeof(ScreenFadeItem), ValueKind.Float, MotionMask.Tween, FeedbackCategory.None, false),
            (typeof(FloatingTextItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(PostFXBloomItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Curve, FeedbackCategory.PostFX, true),
            (typeof(PostFXChromaticAberrationItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Curve, FeedbackCategory.PostFX, false),
            (typeof(PostFXColorAdjustmentsItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash | MotionMask.Punch | MotionMask.Oscillate | MotionMask.Curve, FeedbackCategory.PostFX, true),
            (typeof(PostFXDepthOfFieldItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.PostFX, false),
            (typeof(PostFXFilmGrainItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.PostFX, false),
            (typeof(PostFXLensDistortionItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash | MotionMask.Punch | MotionMask.Oscillate, FeedbackCategory.PostFX, false),
            (typeof(PostFXMotionBlurItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.PostFX, false),
            (typeof(PostFXPaniniProjectionItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.PostFX, false),
            (typeof(PostFXVignetteItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash | MotionMask.Punch | MotionMask.Oscillate, FeedbackCategory.PostFX, false),
            (typeof(PostFXWhiteBalanceItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.PostFX, false),
            (typeof(PostFXChannelMixerItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.PostFX, false),
            (typeof(PostFXSplitToningItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.PostFX, false),
            (typeof(PostFXLiftGammaGainItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.PostFX, false),
            (typeof(PostFXShadowsMidtonesHighlightsItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.PostFX, false),
            (typeof(PostFXVolumeBlendItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.PostFX, false),
            (typeof(PostFXVolumeWeightItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.PostFX, false),
            (typeof(CinemachineLensFieldOfViewItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch | MotionMask.Spring, FeedbackCategory.Camera, false),
            (typeof(CinemachineLensOrthographicSizeItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch | MotionMask.Spring, FeedbackCategory.Camera, false),
            (typeof(CinemachineLensDutchItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch | MotionMask.Spring, FeedbackCategory.Camera, false),
            (typeof(CinemachineNoiseAmplitudeGainItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash | MotionMask.Punch, FeedbackCategory.Camera, false),
            (typeof(CinemachineNoiseFrequencyGainItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash | MotionMask.Punch, FeedbackCategory.Camera, false),
            (typeof(CinemachineActivateCameraItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(GamepadRumbleItem), ValueKind.Vector2, MotionMask.Tween | MotionMask.Flash | MotionMask.Curve, FeedbackCategory.Haptics, false),
            (typeof(RectOffsetsItem), ValueKind.Vector4, MotionMask.Tween, FeedbackCategory.Motion, false),
            (typeof(VideoPlayerControlItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(CameraFieldOfViewItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch | MotionMask.Shake | MotionMask.Spring, FeedbackCategory.Camera, false),
            (typeof(CameraOrthographicSizeItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch | MotionMask.Shake | MotionMask.Spring, FeedbackCategory.Camera, false),
            (typeof(CameraZoomItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch, FeedbackCategory.Camera, false),
            (typeof(CameraClippingPlanesItem), ValueKind.Vector2, MotionMask.Tween, FeedbackCategory.Camera, false),
            (typeof(CameraBackgroundColorItem), ValueKind.Color, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.Camera, true),
            (typeof(SquashAndStretchItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch | MotionMask.Curve | MotionMask.Spring, FeedbackCategory.Motion, false),
            (typeof(LookAtItem), ValueKind.Float, MotionMask.Set | MotionMask.Tween, FeedbackCategory.Motion, false),
            (typeof(RotateAroundItem), ValueKind.Float, MotionMask.Tween | MotionMask.Oscillate, FeedbackCategory.Motion, false),
            (typeof(MatchTransformItem), ValueKind.Float, MotionMask.Tween, FeedbackCategory.Motion, false),
            (typeof(SetParentItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.Motion, false),
            (typeof(MaterialFloatItem), ValueKind.Float,
                MotionMask.Tween | MotionMask.Flash | MotionMask.Punch | MotionMask.Shake | MotionMask.Curve | MotionMask.Oscillate | MotionMask.Spring,
                FeedbackCategory.None, false),
            (typeof(MaterialVectorItem), ValueKind.Vector4, MotionMask.Tween | MotionMask.Curve | MotionMask.Oscillate, FeedbackCategory.None, false),
            (typeof(TextureOffsetItem), ValueKind.Vector2, MotionMask.Tween | MotionMask.Curve | MotionMask.Oscillate, FeedbackCategory.None, false),
            (typeof(TextureScaleItem), ValueKind.Vector2, MotionMask.Tween | MotionMask.Curve | MotionMask.Oscillate, FeedbackCategory.None, false),
            (typeof(EmissionItem), ValueKind.Color, MotionMask.Tween | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Blink, FeedbackCategory.None, true),
            (typeof(MaterialSwapItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(RendererVisibilityItem), ValueKind.Bool, MotionMask.Set | MotionMask.Blink, FeedbackCategory.None, true),
            // the global render state
            (typeof(ShaderGlobalFloatItem), ValueKind.Float, MotionMask.Set | MotionMask.Tween | MotionMask.Curve, FeedbackCategory.None, false),
            (typeof(ShaderGlobalColorItem), ValueKind.Color, MotionMask.Set | MotionMask.Tween | MotionMask.Curve, FeedbackCategory.None, false),
            (typeof(ShaderGlobalVectorItem), ValueKind.Vector4, MotionMask.Set | MotionMask.Tween | MotionMask.Curve, FeedbackCategory.None, false),
            (typeof(FogColorItem), ValueKind.Color, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.None, true),
            (typeof(FogDensityItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.None, true),
            (typeof(SkyboxExposureItem), ValueKind.Float, MotionMask.Tween | MotionMask.Oscillate, FeedbackCategory.None, false),
            (typeof(SkyboxTintItem), ValueKind.Color, MotionMask.Tween | MotionMask.Oscillate, FeedbackCategory.None, false),
            (typeof(SkyboxRotationItem), ValueKind.Float, MotionMask.Tween | MotionMask.Oscillate, FeedbackCategory.None, false),
            (typeof(AmbientColorItem), ValueKind.Color, MotionMask.Tween, FeedbackCategory.None, false),
            (typeof(AmbientIntensityItem), ValueKind.Float, MotionMask.Tween, FeedbackCategory.None, false),
            (typeof(SpriteColorItem), ValueKind.Color, MotionMask.Tween | MotionMask.Flash | MotionMask.Gradient | MotionMask.Blink | MotionMask.Spring, FeedbackCategory.None, true),
            (typeof(SpriteAlphaItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash | MotionMask.Blink | MotionMask.Spring, FeedbackCategory.None, true),
            (typeof(SpriteSwapItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(SpriteFlipItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(SpriteSheetItem), ValueKind.Float, MotionMask.Tween, FeedbackCategory.None, false),
            (typeof(LineWidthItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch, FeedbackCategory.None, false),
            (typeof(LineColorItem), ValueKind.Color, MotionMask.Tween | MotionMask.Punch, FeedbackCategory.None, false),
            (typeof(TrailTimeItem), ValueKind.Float, MotionMask.Set | MotionMask.Tween, FeedbackCategory.None, false),
            (typeof(TrailWidthItem), ValueKind.Float, MotionMask.Set | MotionMask.Tween, FeedbackCategory.None, false),
            (typeof(TrailEmittingItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(TrailClearItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(ComponentEnabledItem), ValueKind.Bool, MotionMask.Set | MotionMask.Blink, FeedbackCategory.None, true),
            (typeof(SpawnItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(DespawnItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(SetLayerItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(ParticleControlItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(ParticleEmitItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(ParticleSpawnItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(ParticleSimulationSpeedItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.None, false),
            (typeof(ParticleEmissionRateItem), ValueKind.Float, MotionMask.Tween | MotionMask.Flash, FeedbackCategory.None, false),
            (typeof(AnimatorParameterItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(AnimatorFloatItem), ValueKind.Float, MotionMask.Tween, FeedbackCategory.None, false),
            (typeof(AnimatorSpeedItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch | MotionMask.Spring, FeedbackCategory.None, false),
            (typeof(AnimatorLayerWeightItem), ValueKind.Float, MotionMask.Tween, FeedbackCategory.None, false),
            (typeof(AnimatorStateItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(RigidbodyForceItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(RigidbodyExplosionItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(RigidbodyVelocityItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(Rigidbody2DForceItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(Rigidbody2DVelocityItem), ValueKind.Bool, MotionMask.Set, FeedbackCategory.None, false),
            (typeof(TimeScaleItem), ValueKind.Float, MotionMask.Set | MotionMask.Tween | MotionMask.Flash, FeedbackCategory.Time, false),
            (typeof(CustomFloatItem), ValueKind.Float,
                MotionMask.Set | MotionMask.Tween | MotionMask.Punch | MotionMask.Shake | MotionMask.Curve | MotionMask.Flash | MotionMask.Oscillate | MotionMask.Wiggle | MotionMask.Spring,
                FeedbackCategory.None, false),
            (typeof(CustomVector3Item), ValueKind.Vector3,
                MotionMask.Tween | MotionMask.Punch | MotionMask.Shake | MotionMask.Curve | MotionMask.Oscillate | MotionMask.Wiggle | MotionMask.Spring, FeedbackCategory.None, false),
            (typeof(CustomColorItem), ValueKind.Color, MotionMask.Tween | MotionMask.Flash | MotionMask.Gradient | MotionMask.Spring, FeedbackCategory.None, true),
            (typeof(CustomFloatEventItem), ValueKind.Float, MotionMask.Tween | MotionMask.Punch | MotionMask.Shake | MotionMask.Curve | MotionMask.Oscillate, FeedbackCategory.None, false),
        };

        static readonly (Type Type, FeedbackCategory Category, float NaturalDuration)[] ActionRows =
        {
            (typeof(UnityEventItem), FeedbackCategory.None, 0f),
            (typeof(HitStopItem), FeedbackCategory.Time, 0.1f),
            // a wait is open (+∞ — its time limit when set) · an empty chain is an instant
            (typeof(PlayPlayerItem), FeedbackCategory.None, float.PositiveInfinity),
            (typeof(PlayerChainItem), FeedbackCategory.None, 0f),
            (typeof(PlayerControlItem), FeedbackCategory.None, 0f),
            (typeof(RandomPlayerItem), FeedbackCategory.None, float.PositiveInfinity),
            (typeof(RandomUnityEventItem), FeedbackCategory.None, 0f),
            (typeof(SignalBroadcastItem), FeedbackCategory.None, 0f),
            (typeof(DebugItem), FeedbackCategory.None, 0f),
            // an impulse lives its signal's length · a clear is an instant
            (typeof(CinemachineImpulseItem), FeedbackCategory.Camera, 0.2f),
            (typeof(CinemachineImpulseClearItem), FeedbackCategory.None, 0f),
        };

        // Aliases are not pinned — they are search words only
        static readonly (Type Type, string Path, PreviewGrade Preview)[] Metadata =
        {
            (typeof(PositionItem), "Transform/Position", PreviewGrade.Full),
            (typeof(RotationItem), "Transform/Rotation", PreviewGrade.Full),
            (typeof(ScaleItem), "Transform/Scale", PreviewGrade.Full),
            (typeof(MaterialColorItem), "Renderer/Material Color", PreviewGrade.Full),
            (typeof(LightIntensityItem), "Light/Intensity", PreviewGrade.Full),
            (typeof(CameraShakeItem), "Camera/Shake", PreviewGrade.Full),
            (typeof(SetActiveItem), "GameObject/Set Active", PreviewGrade.Full),
            (typeof(UnityEventItem), "Flow/Unity Event", PreviewGrade.DisplayOnly),
            (typeof(HitStopItem), "Time/Hit Stop", PreviewGrade.DisplayOnly),
            (typeof(PlayPlayerItem), "Flow/Play Player", PreviewGrade.Full),
            (typeof(PlayerChainItem), "Flow/Player Chain", PreviewGrade.Full),
            (typeof(PlayerControlItem), "Flow/Player Control", PreviewGrade.Full),
            (typeof(RandomPlayerItem), "Flow/Random Player", PreviewGrade.Full),
            (typeof(RandomUnityEventItem), "Flow/Random Unity Event", PreviewGrade.DisplayOnly),
            (typeof(SignalBroadcastItem), "Flow/Signal Broadcast", PreviewGrade.DisplayOnly),
            (typeof(DebugItem), "Flow/Debug", PreviewGrade.DisplayOnly),
            (typeof(LightRangeItem), "Light/Range", PreviewGrade.Full),
            (typeof(LightColorItem), "Light/Color", PreviewGrade.Full),
            (typeof(LightSpotAngleItem), "Light/Spot Angle", PreviewGrade.Full),
            (typeof(LightShadowStrengthItem), "Light/Shadow Strength", PreviewGrade.Full),
            (typeof(Light2DIntensityItem), "Light 2D/Intensity", PreviewGrade.Full),
            (typeof(Light2DColorItem), "Light 2D/Color", PreviewGrade.Full),
            (typeof(Light2DRadiusItem), "Light 2D/Radius", PreviewGrade.Full),
            (typeof(Light2DFalloffItem), "Light 2D/Falloff", PreviewGrade.Full),
            (typeof(GraphicColorItem), "UI/Color", PreviewGrade.Full),
            (typeof(GraphicAlphaItem), "UI/Alpha", PreviewGrade.Full),
            (typeof(GraphicCrossFadeItem), "UI/Cross Fade", PreviewGrade.Full),
            (typeof(GraphicMaterialItem), "UI/Material", PreviewGrade.Full),
            (typeof(GraphicRaycastTargetItem), "UI/Raycast Target", PreviewGrade.Full),
            (typeof(GraphicTextureOffsetItem), "UI/Texture Offset", PreviewGrade.Full),
            (typeof(GraphicTextureScaleItem), "UI/Texture Scale", PreviewGrade.Full),
            (typeof(ImageFillItem), "UI/Image Fill", PreviewGrade.Full),
            (typeof(ImageSpriteItem), "UI/Image Sprite", PreviewGrade.Full),
            (typeof(ImageSpriteSheetItem), "UI/Image Sprite Sheet", PreviewGrade.Full),
            (typeof(CanvasGroupAlphaItem), "UI/Canvas Group Alpha", PreviewGrade.Full),
            (typeof(CanvasGroupInteractableItem), "UI/Canvas Group Interactable", PreviewGrade.Full),
            (typeof(CanvasGroupBlocksRaycastsItem), "UI/Canvas Group Blocks Raycasts", PreviewGrade.Full),
            (typeof(TextContentItem), "UI/Text", PreviewGrade.Full),
            (typeof(TextFontSizeItem), "UI/Font Size", PreviewGrade.Full),
            (typeof(SelectableInteractableItem), "UI/Interactable", PreviewGrade.Full),
            (typeof(RectSizeDeltaItem), "Rect Transform/Size Delta", PreviewGrade.Full),
            (typeof(RectPivotItem), "Rect Transform/Pivot", PreviewGrade.Full),
            (typeof(RectAnchorsItem), "Rect Transform/Anchors", PreviewGrade.Full),
            (typeof(RectOffsetsItem), "Rect Transform/Offsets", PreviewGrade.Full),
            (typeof(VideoPlayerControlItem), "Video/Player Control", PreviewGrade.None),
            (typeof(TMPFontSizeItem), "TMP/Font Size", PreviewGrade.Full),
            (typeof(TMPCharacterSpacingItem), "TMP/Character Spacing", PreviewGrade.Full),
            (typeof(TMPWordSpacingItem), "TMP/Word Spacing", PreviewGrade.Full),
            (typeof(TMPLineSpacingItem), "TMP/Line Spacing", PreviewGrade.Full),
            (typeof(TMPParagraphSpacingItem), "TMP/Paragraph Spacing", PreviewGrade.Full),
            (typeof(TMPDilateItem), "TMP/Dilate", PreviewGrade.Full),
            (typeof(TMPSoftnessItem), "TMP/Softness", PreviewGrade.Full),
            (typeof(TMPOutlineWidthItem), "TMP/Outline Width", PreviewGrade.Full),
            (typeof(TMPOutlineColorItem), "TMP/Outline Color", PreviewGrade.Full),
            (typeof(TMPUnderlayColorItem), "TMP/Underlay Color", PreviewGrade.Full),
            (typeof(TMPUnderlayOffsetItem), "TMP/Underlay Offset", PreviewGrade.Full),
            (typeof(TMPTextItem), "TMP/Set Text", PreviewGrade.Full),
            (typeof(TMPRevealItem), "TMP/Reveal", PreviewGrade.Full),
            (typeof(TMPCountToItem), "TMP/Count To", PreviewGrade.Full),
            (typeof(TMPCharacterOffsetItem), "TMP/Character Offset", PreviewGrade.Full),
            (typeof(TMPCharacterScaleItem), "TMP/Character Scale", PreviewGrade.Full),
            (typeof(TMPCharacterAlphaItem), "TMP/Character Alpha", PreviewGrade.Full),
            (typeof(ScreenFlashItem), "Screen/Flash", PreviewGrade.Full),
            (typeof(ScreenFadeItem), "Screen/Fade", PreviewGrade.Full),
            (typeof(FloatingTextItem), "Screen/Floating Text", PreviewGrade.DisplayOnly),
            (typeof(PostFXBloomItem), "PostFX/Bloom", PreviewGrade.Full),
            (typeof(PostFXChromaticAberrationItem), "PostFX/Chromatic Aberration", PreviewGrade.Full),
            (typeof(PostFXColorAdjustmentsItem), "PostFX/Color Adjustments", PreviewGrade.Full),
            (typeof(PostFXDepthOfFieldItem), "PostFX/Depth of Field", PreviewGrade.Full),
            (typeof(PostFXFilmGrainItem), "PostFX/Film Grain", PreviewGrade.Full),
            (typeof(PostFXLensDistortionItem), "PostFX/Lens Distortion", PreviewGrade.Full),
            (typeof(PostFXMotionBlurItem), "PostFX/Motion Blur", PreviewGrade.None),
            (typeof(PostFXPaniniProjectionItem), "PostFX/Panini Projection", PreviewGrade.Full),
            (typeof(PostFXVignetteItem), "PostFX/Vignette", PreviewGrade.Full),
            (typeof(PostFXWhiteBalanceItem), "PostFX/White Balance", PreviewGrade.Full),
            (typeof(PostFXChannelMixerItem), "PostFX/Channel Mixer", PreviewGrade.Full),
            (typeof(PostFXSplitToningItem), "PostFX/Split Toning", PreviewGrade.Full),
            (typeof(PostFXLiftGammaGainItem), "PostFX/Lift Gamma Gain", PreviewGrade.Full),
            (typeof(PostFXShadowsMidtonesHighlightsItem), "PostFX/Shadows Midtones Highlights", PreviewGrade.Full),
            (typeof(PostFXVolumeBlendItem), "PostFX/Volume Blend", PreviewGrade.Full),
            (typeof(PostFXVolumeWeightItem), "PostFX/Volume Weight", PreviewGrade.Full),
            (typeof(CinemachineImpulseItem), "Cinemachine/Impulse", PreviewGrade.Full),
            (typeof(CinemachineImpulseClearItem), "Cinemachine/Impulse Clear", PreviewGrade.Full),
            (typeof(CinemachineLensFieldOfViewItem), "Cinemachine/Lens FOV", PreviewGrade.Full),
            (typeof(CinemachineLensOrthographicSizeItem), "Cinemachine/Lens Orthographic Size", PreviewGrade.Full),
            (typeof(CinemachineLensDutchItem), "Cinemachine/Lens Dutch", PreviewGrade.Full),
            (typeof(CinemachineNoiseAmplitudeGainItem), "Cinemachine/Noise Amplitude Gain", PreviewGrade.Simulated),
            (typeof(CinemachineNoiseFrequencyGainItem), "Cinemachine/Noise Frequency Gain", PreviewGrade.Simulated),
            (typeof(CinemachineActivateCameraItem), "Cinemachine/Activate Camera", PreviewGrade.DisplayOnly),
            (typeof(GamepadRumbleItem), "Haptics/Gamepad Rumble", PreviewGrade.None),
            (typeof(CameraFieldOfViewItem), "Camera/Field of View", PreviewGrade.Full),
            (typeof(CameraOrthographicSizeItem), "Camera/Orthographic Size", PreviewGrade.Full),
            (typeof(CameraZoomItem), "Camera/Zoom", PreviewGrade.Full),
            (typeof(CameraClippingPlanesItem), "Camera/Clipping Planes", PreviewGrade.Full),
            (typeof(SquashAndStretchItem), "Transform/Squash & Stretch", PreviewGrade.Full),
            (typeof(LookAtItem), "Transform/Look At", PreviewGrade.Full),
            (typeof(RotateAroundItem), "Transform/Rotate Around", PreviewGrade.Full),
            (typeof(MatchTransformItem), "Transform/Match Transform", PreviewGrade.Full),
            (typeof(SetParentItem), "Transform/Set Parent", PreviewGrade.Full),
            (typeof(CameraBackgroundColorItem), "Camera/Background Color", PreviewGrade.Full),
            (typeof(MaterialFloatItem), "Renderer/Material Float", PreviewGrade.Full),
            (typeof(MaterialVectorItem), "Renderer/Material Vector", PreviewGrade.Full),
            (typeof(TextureOffsetItem), "Renderer/Texture Offset", PreviewGrade.Full),
            (typeof(TextureScaleItem), "Renderer/Texture Scale", PreviewGrade.Full),
            (typeof(EmissionItem), "Renderer/Emission", PreviewGrade.Full),
            (typeof(MaterialSwapItem), "Renderer/Material Swap", PreviewGrade.Full),
            (typeof(RendererVisibilityItem), "Renderer/Visibility", PreviewGrade.Full),
            (typeof(ShaderGlobalFloatItem), "Renderer/Shader Global Float", PreviewGrade.Full),
            (typeof(ShaderGlobalColorItem), "Renderer/Shader Global Color", PreviewGrade.Full),
            (typeof(ShaderGlobalVectorItem), "Renderer/Shader Global Vector", PreviewGrade.Full),
            (typeof(FogColorItem), "Renderer/Fog Color", PreviewGrade.Full),
            (typeof(FogDensityItem), "Renderer/Fog Density", PreviewGrade.Full),
            (typeof(SkyboxExposureItem), "Renderer/Skybox Exposure", PreviewGrade.Full),
            (typeof(SkyboxTintItem), "Renderer/Skybox Tint", PreviewGrade.Full),
            (typeof(SkyboxRotationItem), "Renderer/Skybox Rotation", PreviewGrade.Full),
            (typeof(AmbientColorItem), "Renderer/Ambient Color", PreviewGrade.Full),
            (typeof(AmbientIntensityItem), "Renderer/Ambient Intensity", PreviewGrade.Full),
            (typeof(SpriteColorItem), "Renderer/Sprite Color", PreviewGrade.Full),
            (typeof(SpriteAlphaItem), "Renderer/Sprite Alpha", PreviewGrade.Full),
            (typeof(SpriteSwapItem), "Renderer/Sprite Swap", PreviewGrade.Full),
            (typeof(SpriteFlipItem), "Renderer/Sprite Flip", PreviewGrade.Full),
            (typeof(SpriteSheetItem), "Renderer/Sprite Sheet", PreviewGrade.Full),
            (typeof(LineWidthItem), "Renderer/Line Width", PreviewGrade.Full),
            (typeof(LineColorItem), "Renderer/Line Color", PreviewGrade.Full),
            (typeof(TrailTimeItem), "Renderer/Trail Time", PreviewGrade.Full),
            (typeof(TrailWidthItem), "Renderer/Trail Width", PreviewGrade.Full),
            (typeof(TrailEmittingItem), "Renderer/Trail Emitting", PreviewGrade.Full),
            (typeof(TrailClearItem), "Renderer/Trail Clear", PreviewGrade.Full),
            (typeof(ComponentEnabledItem), "GameObject/Component Enabled", PreviewGrade.Full),
            (typeof(SpawnItem), "GameObject/Spawn", PreviewGrade.DisplayOnly),
            (typeof(DespawnItem), "GameObject/Despawn", PreviewGrade.DisplayOnly),
            (typeof(SetLayerItem), "GameObject/Set Layer", PreviewGrade.Full),
            (typeof(ParticleControlItem), "Particles/Control", PreviewGrade.Simulated),
            (typeof(ParticleEmitItem), "Particles/Emit", PreviewGrade.Simulated),
            (typeof(ParticleSpawnItem), "Particles/Spawn", PreviewGrade.DisplayOnly),
            (typeof(ParticleSimulationSpeedItem), "Particles/Simulation Speed", PreviewGrade.Simulated),
            (typeof(ParticleEmissionRateItem), "Particles/Emission Rate", PreviewGrade.Simulated),
            (typeof(AnimatorParameterItem), "Animation/Parameter", PreviewGrade.Simulated),
            (typeof(AnimatorFloatItem), "Animation/Float Parameter", PreviewGrade.Simulated),
            (typeof(AnimatorSpeedItem), "Animation/Speed", PreviewGrade.Simulated),
            (typeof(AnimatorLayerWeightItem), "Animation/Layer Weight", PreviewGrade.Simulated),
            (typeof(AnimatorStateItem), "Animation/Play State", PreviewGrade.Simulated),
            (typeof(RigidbodyForceItem), "Physics/Force", PreviewGrade.DisplayOnly),
            (typeof(RigidbodyExplosionItem), "Physics/Explosion", PreviewGrade.DisplayOnly),
            (typeof(RigidbodyVelocityItem), "Physics/Velocity", PreviewGrade.DisplayOnly),
            (typeof(Rigidbody2DForceItem), "Physics 2D/Force", PreviewGrade.DisplayOnly),
            (typeof(Rigidbody2DVelocityItem), "Physics 2D/Velocity", PreviewGrade.DisplayOnly),
            (typeof(TimeScaleItem), "Time/Time Scale", PreviewGrade.DisplayOnly),
            (typeof(CustomFloatItem), "Custom/Float Value", PreviewGrade.Full),
            (typeof(CustomVector3Item), "Custom/Vector3 Value", PreviewGrade.Full),
            (typeof(CustomColorItem), "Custom/Color Value", PreviewGrade.Full),
            (typeof(CustomFloatEventItem), "Custom/Float Event", PreviewGrade.Full),
        };

        [TearDown]
        public void DeleteAsset() => AssetDatabase.DeleteAsset(AssetPath);

        static string Sorted(IEnumerable<string> names) => string.Join(" ", names.OrderBy(n => n, StringComparer.Ordinal));

        // the table covers every concrete effect item in Core, and the length follows the field — not only its default
        [Test]
        public void RepresentativeItemsCarryTheirTraits()
        {
            var core = TypeCache.GetTypesDerivedFrom<EffectItem>().Where(t => t.Assembly == typeof(EffectItem).Assembly && !t.IsAbstract);
            var listed = TargetRows.Select(r => r.Type).Concat(ActionRows.Select(r => r.Type));
            Assert.That(Sorted(listed.Select(t => t.Name)), Is.EqualTo(Sorted(core.Select(t => t.Name))), "every concrete effect item has a row");

            foreach (var (type, value, motions, category, flashSensitive) in TargetRows)
            {
                var traits = ((TargetItem)Activator.CreateInstance(type)).Traits;
                Assert.That((traits.ValueKind, traits.SupportedMotions, traits.Category, traits.FlashSensitive, traits.ApplyMode),
                    Is.EqualTo((value, motions, category, flashSensitive, type.Name.StartsWith("PostFX", StringComparison.Ordinal) && type != typeof(PostFXVolumeWeightItem) ? ApplyMode.WeightTowardTarget : ApplyMode.Offset)), type.Name);
            }

            foreach (var (type, category, naturalDuration) in ActionRows)
            {
                var item = (ActionItem)Activator.CreateInstance(type);
                Assert.That((item.DeclaredCategory, item.NaturalDuration), Is.EqualTo((category, naturalDuration)), type.Name);
            }

            Assert.That(new HitStopItem { m_FreezeDuration = 0.35f }.NaturalDuration, Is.EqualTo(0.35f), "the hit stop's length is its freeze duration");
            Assert.That((new CinemachineImpulseItem { m_Duration = 0.7f }.NaturalDuration, new CinemachineImpulseItem { m_Duration = -1f }.NaturalDuration), Is.EqualTo((0.7f, 0f)), "an impulse's length is its signal's");
            // a wait opens — its length is its time limit; no wait is an instant; a chain waits when an entry does
            var play = new PlayPlayerItem();
            play.m_Play.m_Timeout = 0.4f;
            var free = new PlayPlayerItem();
            free.m_Play.m_Wait = false;
            var chain = new PlayerChainItem();
            chain.m_Entries.Add(new ChainEntry { m_Player = new GameObject("chain entry").AddComponent<FeedbackPlayer>(), m_Delay = 0.3f, m_Wait = false });
            Assert.That((play.Opens, play.NaturalDuration, free.Opens, free.NaturalDuration), Is.EqualTo((true, 0.4f, false, 0f)));
            Assert.That((chain.Opens, chain.NaturalDuration), Is.EqualTo((false, 0.3f)), "a chain that waits on none is timed");
            chain.m_Entries[0].m_Wait = true;
            Assert.That((chain.Opens, chain.NaturalDuration), Is.EqualTo((true, float.PositiveInfinity)));
            UnityEngine.Object.DestroyImmediate(chain.m_Entries[0].m_Player.gameObject);
            Assert.That(new UnityEventItem().Opens, Is.False);
        }

        // the timeline's constructs carry no target metadata
        static readonly Type[] NoMetadata = { typeof(WaitItem), typeof(WaitForSignalItem), typeof(HoldItem), typeof(LoopStartItem), typeof(LoopEndItem) };

        // every target type has its metadata row — a new target that forgets the row is red
        [Test]
        public void MetadataCoversEveryTarget()
        {
            var have = new HashSet<Type>(Metadata.Select(m => m.Item1));
            List<string> missing = UnityEditor.TypeCache.GetTypesWithAttribute<FeedbackTargetAttribute>().Where(t => !t.IsAbstract && !NoMetadata.Contains(t) && !have.Contains(t))
                .Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.That(missing, Is.Empty, "a target without its metadata row");
        }

        // no default motion · the camera's targets start on the main camera · the others start empty (Direct)
        [Test]
        public void RepresentativeItemsCarryTheirMetadataAndDefaults()
        {
            foreach (var (type, path, preview) in Metadata)
            {
                var meta = type.GetCustomAttribute<FeedbackTargetAttribute>(false);
                Assert.That((meta.Path, meta.Preview), Is.EqualTo((path, preview)), type.Name);
            }

            foreach (var (type, _, _, category, _) in TargetRows)
            {
                var item = (TargetItem)Activator.CreateInstance(type);
                Assert.That(item.m_Motion, Is.Null, $"{type.Name} has no default motion");
                // an activation starts on its own Cinemachine camera
                var mode = category == FeedbackCategory.Camera ? TargetRefMode.MainCamera : type == typeof(CinemachineActivateCameraItem) ? TargetRefMode.Self : TargetRefMode.Direct;
                Assert.That(item.m_Target.m_Mode, Is.EqualTo(mode), type.Name);
            }

            Assert.That(new PositionItem().m_Space, Is.EqualTo(PositionSpace.World));
            var rotation = new RotationItem();
            Assert.That((rotation.m_Space, rotation.m_Interpolation), Is.EqualTo((TransformSpace.World, RotationInterpolation.Euler)));
            var material = new MaterialColorItem();
            Assert.That((material.m_PropertyName, material.m_MaterialIndex, material.m_WriteMode), Is.EqualTo(("", -1, MaterialWriteMode.PropertyBlock)));
            Assert.That(new CameraShakeItem().m_RotationAmplitude, Is.EqualTo(Vector3.zero));
            var unityEvent = new UnityEventItem();
            Assert.That(unityEvent.m_Event, Is.Not.Null);
            Assert.That(unityEvent.m_Event.GetPersistentEventCount(), Is.EqualTo(0));
            var hitStop = new HitStopItem();
            Assert.That((hitStop.m_FreezeDuration, hitStop.m_MinimumTimeScale, hitStop.m_TimeScale), Is.EqualTo((0.1f, 0.1f, 0f)));
            // the second references start empty (Direct) — a new parent's empty is the root
            Assert.That(new SquashAndStretchItem().m_Axis, Is.EqualTo(SquashAxis.YtoXZ));
            var look = new LookAtItem();
            Assert.That((look.m_Mode, look.m_Position, look.m_Direction, look.m_Up), Is.EqualTo((LookAtMode.Transform, Vector3.zero, Vector3.forward, LookAtUp.Up)));
            var around = new RotateAroundItem();
            Assert.That(around.m_Axis, Is.EqualTo(Vector3.up));
            var match = new MatchTransformItem();
            Assert.That((match.m_Position, match.m_Rotation, match.m_Scale, match.m_Track), Is.EqualTo((true, true, true, false)));
            var set = new SetParentItem();
            Assert.That(set.m_WorldPositionStays, Is.True);
            foreach (TargetRef reference in new[] { look.m_LookTarget, around.m_Pivot, match.m_Destination, set.m_NewParent })
                Assert.That((reference.m_Mode, reference.m_Object, reference.m_Path, reference.m_Anchor), Is.EqualTo((TargetRefMode.Direct, (UnityEngine.Object)null, "", "")));
            // the common fields of Material Color (an internal interface — the six abstract bases stay closed) · the emission's
            // multiplier (not EffectItem.m_Intensity) · a swap's index 0
            foreach (TargetItem item in new TargetItem[] { new MaterialFloatItem(), new MaterialVectorItem(), new TextureOffsetItem(), new TextureScaleItem(), new EmissionItem() })
            {
                var common = (IMaterialPropertyItem)item;
                Assert.That((common.PropertyName, common.MaterialIndex, common.WriteMode), Is.EqualTo(("", -1, MaterialWriteMode.PropertyBlock)), item.GetType().Name);
            }
            Assert.That(new MaterialColorItem() is IMaterialPropertyItem, Is.True);
            Assert.That(new EmissionItem().m_Multiplier, Is.EqualTo(1f));
            var swap = new MaterialSwapItem();
            Assert.That((swap.m_Material, swap.m_MaterialIndex), Is.EqualTo(((Material)null, 0)));
            // the ten are Global with no Requires · the named ones start unnamed (a shader global's empty name is refused, a
            // skybox's is the standard property); a target item outside them is not Global
            Type[] globals = { typeof(ShaderGlobalFloatItem), typeof(ShaderGlobalColorItem), typeof(ShaderGlobalVectorItem), typeof(FogColorItem), typeof(FogDensityItem),
                typeof(SkyboxExposureItem), typeof(SkyboxTintItem), typeof(SkyboxRotationItem), typeof(AmbientColorItem), typeof(AmbientIntensityItem),
                typeof(TimeScaleItem), typeof(CustomFloatEventItem), typeof(ScreenFlashItem), typeof(ScreenFadeItem),
                typeof(PostFXBloomItem), typeof(PostFXChromaticAberrationItem), typeof(PostFXColorAdjustmentsItem), typeof(PostFXDepthOfFieldItem), typeof(PostFXFilmGrainItem), typeof(PostFXLensDistortionItem), typeof(PostFXMotionBlurItem), typeof(PostFXPaniniProjectionItem), typeof(PostFXVignetteItem), typeof(PostFXWhiteBalanceItem), typeof(PostFXChannelMixerItem), typeof(PostFXSplitToningItem), typeof(PostFXLiftGammaGainItem), typeof(PostFXShadowsMidtonesHighlightsItem), typeof(PostFXVolumeBlendItem), typeof(GamepadRumbleItem) };
            foreach (var (type, _, _, _, _) in TargetRows)
            {
                var meta = type.GetCustomAttribute<FeedbackTargetAttribute>(false);
                Assert.That((meta.Global, meta.Global ? meta.Requires : null), Is.EqualTo((globals.Contains(type), (Type)null)), type.Name + " Global");
            }
            Assert.That((new ShaderGlobalFloatItem().m_PropertyName, new ShaderGlobalColorItem().m_PropertyName, new ShaderGlobalVectorItem().m_PropertyName,
                new SkyboxExposureItem().m_PropertyName, new SkyboxTintItem().m_PropertyName, new SkyboxRotationItem().m_PropertyName), Is.EqualTo(("", "", "", "", "", "")));
            // what each needs · the fields' defaults (no sprite · no frame — a new list · X · Start)
            foreach (var (type, requires) in new (Type, Type)[] { (typeof(SpriteColorItem), typeof(SpriteRenderer)), (typeof(SpriteAlphaItem), typeof(SpriteRenderer)),
                (typeof(SpriteSwapItem), typeof(SpriteRenderer)), (typeof(SpriteFlipItem), typeof(SpriteRenderer)), (typeof(SpriteSheetItem), typeof(SpriteRenderer)),
                (typeof(LineWidthItem), typeof(LineRenderer)), (typeof(LineColorItem), typeof(LineRenderer)), (typeof(TrailTimeItem), typeof(TrailRenderer)),
                (typeof(TrailWidthItem), typeof(TrailRenderer)), (typeof(TrailEmittingItem), typeof(TrailRenderer)), (typeof(TrailClearItem), typeof(TrailRenderer)) })
                Assert.That(type.GetCustomAttribute<FeedbackTargetAttribute>(false).Requires, Is.EqualTo(requires), type.Name + " Requires");
            var sheet = new SpriteSheetItem();
            Assert.That((new SpriteSwapItem().m_Sprite, new SpriteFlipItem().m_Axis, sheet.m_Frames != null && sheet.m_Frames.Count == 0, new LineColorItem().m_End),
                Is.EqualTo(((Sprite)null, SpriteAxis.X, true, LineEnd.Start)));
            // what each needs · the fields' defaults (no type · the first · a pool of 5 · a second's lifetime · the target's place ·
            // no parent · layer 0)
            foreach (var (type, requires) in new (Type, Type)[] { (typeof(ComponentEnabledItem), typeof(Component)), (typeof(SpawnItem), typeof(Transform)),
                (typeof(DespawnItem), typeof(GameObject)), (typeof(SetLayerItem), typeof(GameObject)) })
                Assert.That(type.GetCustomAttribute<FeedbackTargetAttribute>(false).Requires, Is.EqualTo(requires), type.Name + " Requires");
            var spawn = new SpawnItem();
            Assert.That((new ComponentEnabledItem().m_ComponentType, new ComponentEnabledItem().m_ComponentIndex, new SetLayerItem().m_Layer), Is.EqualTo(("", 0, 0)));
            Assert.That((spawn.m_Prefab, spawn.m_Pooled, spawn.m_PoolSize, spawn.m_Place, spawn.m_Offset, spawn.m_MatchRotation, spawn.m_Parent, spawn.m_Lifetime),
                Is.EqualTo(((GameObject)null, true, 5, SpawnPlace.Target, Vector3.zero, false, SpawnParent.None, 1f)));
            // the module type by name, the implementing assembly · the fields' defaults (Play with the children · stop emitting ·
            // 30 · a pool of 5 · the target's place · no Stop After)
            foreach (Type type in new[] { typeof(ParticleControlItem), typeof(ParticleEmitItem), typeof(ParticleSimulationSpeedItem), typeof(ParticleEmissionRateItem) })
            {
                var meta = type.GetCustomAttribute<FeedbackTargetAttribute>(false);
                Assert.That((meta.Requires, meta.RequiresName, meta.Implementation), Is.EqualTo(((Type)null, "UnityEngine.ParticleSystem, UnityEngine.ParticleSystemModule", "xpTURN.Feedback.Particles")), type.Name);
            }
            var particleSpawn = typeof(ParticleSpawnItem).GetCustomAttribute<FeedbackTargetAttribute>(false);
            Assert.That((particleSpawn.Requires, particleSpawn.RequiresName, particleSpawn.Implementation), Is.EqualTo((typeof(Transform), (string)null, "xpTURN.Feedback.Particles")));
            var control = new ParticleControlItem();
            var ps = new ParticleSpawnItem();
            Assert.That((control.m_Action, control.m_WithChildren, control.m_StopMode, new ParticleEmitItem().m_Count), Is.EqualTo((ParticleAction.Play, true, ParticleStopMode.StopEmitting, 30)));
            Assert.That((ps.m_Prefab, ps.m_Pooled, ps.m_PoolSize, ps.m_Place, ps.m_Offset, ps.m_MatchRotation, ps.m_Parent, ps.m_StopAfter),
                Is.EqualTo(((GameObject)null, true, 5, SpawnPlace.Target, Vector3.zero, false, SpawnParent.None, 0f)));
            // the Animator by name, the implementing assembly · the fields' defaults (a trigger · a bool's true · a random 0 ~ 5 ·
            // a step of 1 · a cross fade of 0.1 s)
            foreach (Type type in new[] { typeof(AnimatorParameterItem), typeof(AnimatorFloatItem), typeof(AnimatorSpeedItem), typeof(AnimatorLayerWeightItem), typeof(AnimatorStateItem) })
            {
                var meta = type.GetCustomAttribute<FeedbackTargetAttribute>(false);
                Assert.That((meta.Requires, meta.RequiresName, meta.Implementation), Is.EqualTo(((Type)null, "UnityEngine.Animator, UnityEngine.AnimationModule", "xpTURN.Feedback.Animation")), type.Name);
            }
            var parameter = new AnimatorParameterItem();
            var state = new AnimatorStateItem();
            Assert.That((parameter.m_Parameter, parameter.m_Kind, parameter.m_Bool, parameter.m_Int, parameter.m_Float, parameter.m_Pick, parameter.m_Min, parameter.m_Max, parameter.m_Step),
                Is.EqualTo(("", AnimatorParameterKind.Trigger, true, 0, 0f, AnimatorValuePick.Value, 0f, 5f, 1f)));
            Assert.That((new AnimatorFloatItem().m_Parameter, new AnimatorLayerWeightItem().m_Layer, state.m_State, state.m_Layer, state.m_Transition, state.m_Offset), Is.EqualTo(("", "", "", "", 0.1f, 0f)));
            // the bodies by name, the two implementing assemblies (the explosion's centre is a Transform) · the fields' defaults (an
            // impulse of (0, 5, 0) · an explosion of 10 in 5 m · everything · 32 · a 2D torque of 1)
            foreach (Type type in new[] { typeof(RigidbodyForceItem), typeof(RigidbodyVelocityItem) })
            {
                var meta = type.GetCustomAttribute<FeedbackTargetAttribute>(false);
                Assert.That((meta.Requires, meta.RequiresName, meta.Implementation), Is.EqualTo(((Type)null, "UnityEngine.Rigidbody, UnityEngine.PhysicsModule", "xpTURN.Feedback.Physics")), type.Name);
            }
            foreach (Type type in new[] { typeof(Rigidbody2DForceItem), typeof(Rigidbody2DVelocityItem) })
            {
                var meta = type.GetCustomAttribute<FeedbackTargetAttribute>(false);
                Assert.That((meta.Requires, meta.RequiresName, meta.Implementation), Is.EqualTo(((Type)null, "UnityEngine.Rigidbody2D, UnityEngine.Physics2DModule", "xpTURN.Feedback.Physics2D")), type.Name);
            }
            var explosionMeta = typeof(RigidbodyExplosionItem).GetCustomAttribute<FeedbackTargetAttribute>(false);
            Assert.That((explosionMeta.Requires, explosionMeta.RequiresName, explosionMeta.Implementation), Is.EqualTo((typeof(Transform), (string)null, "xpTURN.Feedback.Physics")));
            var force = new RigidbodyForceItem();
            var explosion = new RigidbodyExplosionItem();
            var velocity = new RigidbodyVelocityItem();
            var force2 = new Rigidbody2DForceItem();
            var velocity2 = new Rigidbody2DVelocityItem();
            Assert.That((force.m_Kind, force.m_Space, force.m_Mode, force.m_Min, force.m_Max),
                Is.EqualTo((PhysicsForceKind.Force, TransformSpace.World, PhysicsForceMode.Impulse, new Vector3(0f, 5f, 0f), new Vector3(0f, 5f, 0f))));
            Assert.That((explosion.m_MinForce, explosion.m_MaxForce, explosion.m_Radius, explosion.m_Upwards, explosion.m_Mode, explosion.m_Offset, explosion.m_Layers.value, explosion.m_MaxBodies, explosion.m_Triggers),
                Is.EqualTo((10f, 10f, 5f, 0f, PhysicsForceMode.Impulse, Vector3.zero, ~0, 32, false)));
            Assert.That((velocity.m_Write, velocity.m_Space, velocity.m_Min, velocity.m_Max), Is.EqualTo((VelocityWrite.Set, TransformSpace.World, new Vector3(0f, 5f, 0f), new Vector3(0f, 5f, 0f))));
            Assert.That((force2.m_Kind, force2.m_Space, force2.m_Mode, force2.m_Min, force2.m_Max, force2.m_MinTorque, force2.m_MaxTorque),
                Is.EqualTo((PhysicsForceKind.Force, TransformSpace.World, PhysicsForceMode2D.Impulse, new Vector2(0f, 5f), new Vector2(0f, 5f), 1f, 1f)));
            Assert.That((velocity2.m_Write, velocity2.m_Space, velocity2.m_Min, velocity2.m_Max), Is.EqualTo((VelocityWrite.Set, TransformSpace.World, new Vector2(0f, 5f), new Vector2(0f, 5f))));
            // the custom values need their IFeedbackTarget<T> · a float event starts with an empty event (its initializer — a field
            // without one is null live), base 0 and no call in a preview
            Assert.That((typeof(CustomFloatItem).GetCustomAttribute<FeedbackTargetAttribute>(false).Requires, typeof(CustomVector3Item).GetCustomAttribute<FeedbackTargetAttribute>(false).Requires,
                typeof(CustomColorItem).GetCustomAttribute<FeedbackTargetAttribute>(false).Requires), Is.EqualTo((typeof(IFeedbackTarget<float>), typeof(IFeedbackTarget<Vector3>), typeof(IFeedbackTarget<Color>))));
            var floatEvent = new CustomFloatEventItem();
            Assert.That((floatEvent.m_Event != null, floatEvent.m_Event.GetPersistentEventCount(), floatEvent.m_Base, floatEvent.m_InvokeInPreview), Is.EqualTo((true, 0, 0f, false)));
            // the PostFX items are the URP assembly's · Volume Weight needs Core RP's volume
            foreach (var (type, _, _, _, _) in TargetRows.Where(r => r.Type.Name.StartsWith("PostFX", StringComparison.Ordinal)))
                Assert.That(type.GetCustomAttribute<FeedbackTargetAttribute>(false).Implementation, Is.EqualTo("xpTURN.Feedback.URP"), type.Name + " Implementation");
            Assert.That(typeof(PostFXVolumeWeightItem).GetCustomAttribute<FeedbackTargetAttribute>(false).RequiresName, Is.EqualTo("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime"));
            // the Cinemachine items are the Cinemachine assembly's · the camera's, the noise's by name (the actions have no target
            // type)
            foreach (Type type in TargetRows.Select(r => r.Type).Concat(ActionRows.Select(r => r.Type)).Where(t => t.Name.StartsWith("Cinemachine", StringComparison.Ordinal)))
            {
                var meta = type.GetCustomAttribute<FeedbackTargetAttribute>(false);
                string requires = type.Name.StartsWith("CinemachineNoise", StringComparison.Ordinal) ? CinemachineNames.Perlin : type.IsSubclassOf(typeof(TargetItem)) ? CinemachineNames.Camera : null;
                Assert.That((meta.Requires, meta.RequiresName, meta.Implementation), Is.EqualTo(((Type)null, requires, "xpTURN.Feedback.Cinemachine")), type.Name);
            }
            // the rumble is the InputSystem assembly's · global — no target type
            var rumble = typeof(GamepadRumbleItem).GetCustomAttribute<FeedbackTargetAttribute>(false);
            Assert.That((rumble.Requires, rumble.RequiresName, rumble.Implementation), Is.EqualTo(((Type)null, (string)null, "xpTURN.Feedback.InputSystem")));
            // every other item is Core's
            foreach (var (type, _, _, _, _) in TargetRows.Where(r => !r.Type.Name.StartsWith("Particle", StringComparison.Ordinal) && !r.Type.Name.StartsWith("Animator", StringComparison.Ordinal)
                && !r.Type.Name.StartsWith("Rigidbody", StringComparison.Ordinal) && !r.Type.Name.StartsWith("Light2D", StringComparison.Ordinal)
                && !new[] { "Graphic", "Image", "CanvasGroup", "Text", "Selectable", "Video", "TMP", "Screen", "FloatingText", "PostFX", "Cinemachine", "Gamepad" }.Any(prefix => r.Type.Name.StartsWith(prefix, StringComparison.Ordinal))))
                Assert.That(type.GetCustomAttribute<FeedbackTargetAttribute>(false).Implementation, Is.Empty, type.Name + " Implementation");
        }

        // a persistent listener inside a [SerializeReference] item comes back from the file
        [Test]
        public void PersistentListenerSurvivesSaveAndReload()
        {
            var holder = ScriptableObject.CreateInstance<DataModelTestHolder>();
            var item = new UnityEventItem();
            holder.items.Add(item);
            UnityEventTools.AddVoidPersistentListener(item.m_Event, holder.OnTestEvent);
            AssetDatabase.CreateAsset(holder, AssetPath);
            AssetDatabase.SaveAssets();

            Resources.UnloadAsset(holder);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            var loaded = AssetDatabase.LoadAssetAtPath<DataModelTestHolder>(AssetPath);
            var back = (UnityEventItem)loaded.items[0];

            // without the unload the old objects come back holding the listener and nothing below reads the file
            Assert.That(loaded, Is.Not.SameAs(holder), "the asset was read again");
            Assert.That(back, Is.Not.SameAs(item), "the item was read again");
            Assert.That(back.m_Event.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(back.m_Event.GetPersistentTarget(0), Is.SameAs(loaded));
            Assert.That(back.m_Event.GetPersistentMethodName(0), Is.EqualTo(nameof(DataModelTestHolder.OnTestEvent)));
        }
    }
}
