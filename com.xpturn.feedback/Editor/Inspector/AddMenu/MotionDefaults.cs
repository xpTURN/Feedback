using System;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // a new motion fitted to its target — the same amplitude 1 is a unit of position, a degree of rotation and twice the
    // scale. A pair not in the table keeps the motion's own defaults. The item's duration on add comes with it (−1 = the
    // timing's own)
    internal static class MotionDefaults
    {
        internal const float RotationSize = 30f;
        internal const float CameraSize = 0.2f;
        internal const float ShakeHalf = 0.5f;
        internal const float ShakeDuration = 0.5f;
        internal const float CameraShakeDuration = 0.1f;
        internal const float RotationBump = 1900f;
        internal const float LightBump = 60f;
        internal const float Bump = 30f;
        internal const float BlinkPeriod = 0.4f;

        // the light's and the camera's value targets — a Tween's end visible from a new component's own values (a light's
        // range 10 · a spot's 30° · a camera's 60° and size 5)
        internal const float RangeSize = 5f;
        internal const float RangeTo = 20f;
        internal const float RangeBump = 300f;
        internal const float SpotAngleTo = 60f;
        internal const float ShadowStrengthTo = 0f;
        internal const float FieldOfViewSize = 10f;
        internal const float FieldOfViewShake = 5f;
        internal const float FieldOfViewTo = 90f;
        internal const float FieldOfViewBump = 600f;
        internal const float OrthographicSizeTo = 10f;
        internal const float OrthographicSizeBump = 60f;
        internal const float ZoomSize = 0.5f;
        internal const float ZoomTo = 1f;
        internal static readonly Vector4 ClippingPlanesTo = new Vector4(6.25f, 5000f, 0f, 0f);
        internal static readonly Vector4 LightColorTo = new Vector4(1f, 0f, 0f, 1f);

        // a 2D light's intensity doubled · its radius out to 3 (a new point light's 1) with a swing of a half · its falloff to
        // the hardest edge with a swing of a quarter (its range is 0 ~ 1) · its color red
        internal const float Light2DIntensityTo = 2f;
        internal const float Light2DRadiusTo = 3f;
        internal const float Light2DRadiusSize = 0.5f;
        internal const float Light2DFalloffTo = 1f;
        internal const float Light2DFalloffSize = 0.25f;

        // the UI — a Graphic's color · alpha as a sprite's · a cross fade out · a fill drained · a font size up by 10 · a size
        // delta to 200 × 200 with a swing of 20 px · a pivot to the corner · anchors stretched · offsets to the anchors
        internal static readonly Vector4 CrossFadeTo = new Vector4(1f, 1f, 1f, 0f);
        internal const float FontSizeSize = 10f;
        internal const float FontSizeTo = 40f;
        internal const float RectSizeSize = 20f;
        internal static readonly Vector4 RectSizeTo = new Vector4(200f, 200f, 0f, 0f);
        internal static readonly Vector4 RectAnchorsTo = new Vector4(0f, 0f, 1f, 1f);

        // seen from a new text's own values (size 36 · spacings 0 · the Mobile material's dilate, softness, outline 0 · a black
        // underlay) — the size doubled with a swing of 10 · the spacings to 20 (em / 100) with a swing of 10 · the dilate ·
        // softness to a half and the outline width to 0.3 (a swing of 0.2) · the outline and underlay red · the underlay out to
        // a half down right. A spring's bump peaks near the swing (a bump is a velocity — value × 0.016)
        internal const float TMPFontSizeTo = 72f;
        internal const float TMPSpacingTo = 20f;
        internal const float TMPTextSize = 10f;
        internal const float TMPTextBump = 600f;
        internal const float TMPMaterialTo = 0.5f;
        internal const float TMPOutlineWidthTo = 0.3f;
        internal const float TMPMaterialSize = 0.2f;
        internal static readonly Vector4 TMPUnderlayOffsetTo = new Vector4(0.5f, -0.5f, 0f, 0f);
        // in the characters' size — a wave up and down by 0.15 · a jitter of 0.05 · a pop in and a fade in from 0 · a slide in
        // from one character below
        internal const float TMPCharacterSize = 0.15f;
        internal const float TMPCharacterJitter = 0.05f;
        internal static readonly Vector4 TMPCharacterWave = new Vector4(0f, 0.15f, 0f, 0f);
        internal static readonly Vector4 TMPCharacterOffsetFrom = new Vector4(0f, -1f, 0f, 0f);

        // Squash & Stretch in stops (a punch's half stop, a tween's doubling — 1 → 2) · Rotate Around in degrees (a quarter
        // turn to go to, a swing of 30°). A spring's bump keeps the common 30 (a peak near half a stop). Look At, Match
        // Transform and Set Parent read no value
        internal const float SquashSize = 0.5f;
        internal const float SquashTo = 1f;
        internal const float RotateAroundSize = 30f;
        internal const float RotateAroundTo = 90f;

        // a texture's offset in tiles (a tween's half tile across — 0 → 1 is a whole tile, the same picture) and its scale in
        // repeats (a tween's doubling). Material Float · Vector keep the motion's own (a property's unit is unknown); an
        // emission's tween and flash are white
        internal const float TextureOffsetSize = 0.1f;
        internal const float TextureScaleSize = 0.25f;
        internal static readonly Vector4 TextureOffsetTo = new Vector4(0.5f, 0f, 0f, 0f);
        internal static readonly Vector4 TextureScaleTo = new Vector4(2f, 2f, 0f, 0f);

        // a fog's density to 0.05 (the default scene's 0.01 — a flash to 0.1) · the fog's, the tint's and the ambient light's
        // color red (a new scene's fog is gray, its ambient a skybox's) · the exposure doubled · the rotation a half turn (a
        // whole turn ends where it began) and a swing of 15° · the ambient intensity doubled. A shader global keeps the
        // motion's own (its unit is unknown)
        internal const float FogDensityTo = 0.05f;
        internal const float FogDensityFlash = 0.1f;
        internal const float ExposureSize = 0.5f;
        internal const float ExposureTo = 2f;
        internal const float SkyRotationSize = 15f;
        internal const float SkyRotationTo = 180f;
        internal const float AmbientIntensityTo = 2f;
        internal static readonly Vector4 RenderColorTo = new Vector4(1f, 0f, 0f, 1f);

        // seen from a new component's own values — a white sprite and line (their tween, flash and blink red · a line's punch
        // toward red), an alpha fading to 0 (a spring's bump dips: the drawn alpha stops at 1), a flip on, a line's width
        // doubled and a half punch, a trail's time to 2 (a new trail's 5) and width doubled, a trail's emitting off (a new one
        // emits). A sheet's tween is its whole list — half a second while it has no frames
        internal const float AlphaBump = -30f;
        internal const float LineWidthTo = 2f;
        internal const float LineWidthPunch = 0.5f;
        internal const float TrailTo = 2f;
        internal const float SheetDuration = 0.5f;
        internal static readonly Vector4 SpriteColorTo = new Vector4(1f, 0f, 0f, 1f);
        internal static readonly Vector4 LineColorPunch = new Vector4(0f, -1f, -1f, 0f);

        // seen from a new system's own values (speed 1 · rate 10) — a speed tween to slow motion and a flash to three times ·
        // a rate tween to none (the emission fades out) and a flash to five times
        internal const float ParticleSpeedTo = 0.25f;
        internal const float ParticleSpeedFlash = 3f;
        internal const float EmissionRateTo = 0f;
        internal const float EmissionRateFlash = 50f;

        // an animator's speed to half (a punch's half — a spring's common bump peaks near it). A layer's weight and a float
        // parameter tween to 1 (the common size)
        internal const float AnimatorSpeedTo = 0.5f;
        internal const float AnimatorSpeedPunch = 0.5f;

        // a time scale to half (a Set · a Tween — 0.5) and a flash to a fifth (a Tween's and a Set's value stays until a revert
        // — FB-N03)
        internal const float TimeScaleTo = 0.5f;
        internal const float TimeScaleFlash = 0.2f;

        // a screen flash of a fifth of a second (its alpha to 1)
        internal const float ScreenFlashDuration = 0.2f;

        // a PostFX flash of 0.3 s — its weight to 1 (the item's fields are the look)
        internal const float PostFXFlashDuration = 0.3f;

        // a rumble's Tween is a change by half on both motors (a play's intensity scales a By — the request's base is 0) · a
        // flash of a fifth of a second to full (the phases are the length's shares)
        internal const float RumbleBy = 0.5f;
        internal const float RumbleFlashDuration = 0.2f;

        internal static FeedbackMotion Create(Type item, MotionKind kind, out float duration)
        {
            duration = -1f;
            // a Cinemachine camera's field of view and size take the camera's
            item = item == typeof(CinemachineLensFieldOfViewItem) ? typeof(CameraFieldOfViewItem) : item == typeof(CinemachineLensOrthographicSizeItem) ? typeof(CameraOrthographicSizeItem) : item;
            float size = item == typeof(RotationItem) ? RotationSize : item == typeof(CameraShakeItem) ? CameraSize
                : item == typeof(LightRangeItem) ? RangeSize : item == typeof(CameraFieldOfViewItem) ? FieldOfViewSize : item == typeof(CameraZoomItem) ? ZoomSize
                : item == typeof(SquashAndStretchItem) ? SquashSize : item == typeof(RotateAroundItem) ? RotateAroundSize
                : item == typeof(TextureOffsetItem) ? TextureOffsetSize : item == typeof(TextureScaleItem) ? TextureScaleSize
                : item == typeof(SkyboxExposureItem) ? ExposureSize : item == typeof(SkyboxRotationItem) ? SkyRotationSize
                : item == typeof(Light2DRadiusItem) ? Light2DRadiusSize : item == typeof(Light2DFalloffItem) ? Light2DFalloffSize
                : item == typeof(GraphicTextureOffsetItem) ? TextureOffsetSize : item == typeof(GraphicTextureScaleItem) ? TextureScaleSize
                : item == typeof(ImageFillItem) ? 0.25f : item == typeof(TextFontSizeItem) ? FontSizeSize : item == typeof(RectSizeDeltaItem) ? RectSizeSize
                : IsTMPText(item) ? TMPTextSize : IsTMPMaterial(item) ? TMPMaterialSize : IsTMPCharacter(item) ? TMPCharacterSize : 1f;
            Vector4 sized = Vector4.one * size;
            switch (kind)
            {
                case MotionKind.Set:
                {
                    var m = new SetMotion();
                    if (item == typeof(SpriteFlipItem))
                        m.m_Params.m_Value = Vector4.one;
                    else if (item == typeof(TrailTimeItem) || item == typeof(TrailWidthItem))
                        m.m_Params.m_Value = Vector4.one * TrailTo;
                    else if (item == typeof(TimeScaleItem))
                        m.m_Params.m_Value = Vector4.one * TimeScaleTo;
                    return m;
                }
                case MotionKind.Tween:
                {
                    var m = new TweenMotion();
                    m.m_Params.m_To = TweenTo(item, sized);
                    if (IsTMPCharacter(item))
                    {
                        m.m_Params.m_Mode = TweenMode.From;
                        m.m_Params.m_From = item == typeof(TMPCharacterOffsetItem) ? TMPCharacterOffsetFrom : Vector4.zero;
                    }
                    if (item == typeof(GamepadRumbleItem))
                    {
                        m.m_Params.m_Mode = TweenMode.By;
                        m.m_Params.m_To = Vector4.one * RumbleBy;
                    }
                    if (item == typeof(SpriteSheetItem))
                        duration = SheetDuration;
                    return m;
                }
                case MotionKind.Punch:
                {
                    var m = new PunchMotion();
                    m.m_Params.m_Amplitude = item == typeof(LineWidthItem) ? Vector4.one * LineWidthPunch : item == typeof(LineColorItem) ? LineColorPunch
                        : item == typeof(AnimatorSpeedItem) ? Vector4.one * AnimatorSpeedPunch : sized;
                    return m;
                }
                case MotionKind.Shake:
                {
                    var m = new ShakeMotion();
                    bool half = item == typeof(PositionItem) || item == typeof(ScaleItem) || item == typeof(CameraOrthographicSizeItem);
                    m.m_Params.m_Amplitude = half ? Vector4.one * ShakeHalf : item == typeof(CameraFieldOfViewItem) ? Vector4.one * FieldOfViewShake : sized;
                    duration = item == typeof(CameraShakeItem) ? CameraShakeDuration : ShakeDuration;
                    return m;
                }
                case MotionKind.Curve:
                {
                    var m = new CurveMotion();
                    m.m_Params.m_RemapOne = sized;
                    return m;
                }
                case MotionKind.Flash:
                {
                    // a light's color starts white — a white flash shows nothing, so a light's flash is red
                    var m = new FlashMotion();
                    if (item == typeof(LightColorItem) || item == typeof(Light2DColorItem))
                        m.m_Params.m_Target = LightColorTo;
                    else if (item == typeof(FogColorItem))
                        m.m_Params.m_Target = RenderColorTo;
                    else if (item == typeof(FogDensityItem))
                        m.m_Params.m_Target = Vector4.one * FogDensityFlash;
                    else if (item == typeof(SpriteColorItem) || item == typeof(GraphicColorItem))
                        m.m_Params.m_Target = SpriteColorTo;
                    else if (item == typeof(SpriteAlphaItem) || item == typeof(GraphicAlphaItem) || item == typeof(CanvasGroupAlphaItem))
                        m.m_Params.m_Target = Vector4.zero;
                    else if (item == typeof(ParticleSimulationSpeedItem))
                        m.m_Params.m_Target = Vector4.one * ParticleSpeedFlash;
                    else if (item == typeof(ParticleEmissionRateItem))
                        m.m_Params.m_Target = Vector4.one * EmissionRateFlash;
                    else if (item == typeof(TimeScaleItem))
                        m.m_Params.m_Target = Vector4.one * TimeScaleFlash;
                    else if (item == typeof(TMPOutlineColorItem) || item == typeof(TMPUnderlayColorItem))
                        m.m_Params.m_Target = SpriteColorTo;
                    else if (item == typeof(TMPUnderlayOffsetItem))
                        m.m_Params.m_Target = TMPUnderlayOffsetTo;
                    else if (IsTMPMaterial(item))
                        m.m_Params.m_Target = Vector4.one * (item == typeof(TMPOutlineWidthItem) ? TMPOutlineWidthTo : TMPMaterialTo);
                    if (item == typeof(ScreenFlashItem))
                        duration = ScreenFlashDuration;
                    else if (typeof(TargetItem).IsAssignableFrom(item) && ((TargetItem)Activator.CreateInstance(item)).Traits.Category == FeedbackCategory.PostFX)
                        duration = PostFXFlashDuration;
                    else if (item == typeof(GamepadRumbleItem))
                        duration = RumbleFlashDuration;
                    return m;
                }
                case MotionKind.Gradient:
                    return new GradientMotion();
                case MotionKind.Oscillate:
                {
                    var m = new OscillateMotion();
                    m.m_Params.m_Amplitude = item == typeof(TMPCharacterOffsetItem) ? TMPCharacterWave : sized;
                    return m;
                }
                case MotionKind.Blink:
                {
                    var m = new BlinkMotion();
                    // (1, 1, 1, 1) changes nothing on an active object, a shown renderer or an enabled component — the blink turns it
                    // off; an emission's blink turns the glow off
                    if (item == typeof(SetActiveItem) || item == typeof(RendererVisibilityItem) || item == typeof(EmissionItem) || item == typeof(SpriteAlphaItem)
                        || item == typeof(ComponentEnabledItem) || item == typeof(GraphicAlphaItem) || item == typeof(CanvasGroupAlphaItem))
                        m.m_Params.m_Value = Vector4.zero;
                    else if (item == typeof(SpriteColorItem) || item == typeof(GraphicColorItem))
                        m.m_Params.m_Value = SpriteColorTo;
                    // a flashing target blinks under the default flash limit — 2.5 a second
                    if (typeof(TargetItem).IsAssignableFrom(item) && ((TargetItem)Activator.CreateInstance(item)).Traits.FlashSensitive)
                        m.m_Params.m_Period = BlinkPeriod;
                    return m;
                }
                case MotionKind.Wiggle:
                {
                    var m = new WiggleMotion();
                    m.m_Params.m_Amplitude = IsTMPCharacter(item) ? Vector4.one * TMPCharacterJitter : sized;
                    return m;
                }
                case MotionKind.Spring:
                {
                    // a bump's value is a velocity — the peak is about value × 0.016
                    var m = new SpringMotion();
                    float bump = item == typeof(RotationItem) ? RotationBump : item == typeof(LightIntensityItem) ? LightBump
                        : item == typeof(LightRangeItem) ? RangeBump : item == typeof(CameraFieldOfViewItem) ? FieldOfViewBump
                        : item == typeof(CameraOrthographicSizeItem) ? OrthographicSizeBump : item == typeof(SpriteAlphaItem) || item == typeof(GraphicAlphaItem) ? AlphaBump
                        : IsTMPText(item) ? TMPTextBump : Bump;
                    m.m_Params.m_Value = Vector4.one * bump;
                    return m;
                }
                default:
                    return null;
            }
        }

        // a Tween's end is the value itself — the targets whose amplitude is no value to go to have their own
        static Vector4 TweenTo(Type item, Vector4 sized)
        {
            if (item == typeof(LightRangeItem))
                return Vector4.one * RangeTo;
            if (item == typeof(GraphicColorItem))
                return SpriteColorTo;
            if (item == typeof(GraphicAlphaItem) || item == typeof(CanvasGroupAlphaItem) || item == typeof(ImageFillItem) || item == typeof(RectPivotItem) || item == typeof(RectOffsetsItem))
                return Vector4.zero;
            if (item == typeof(GraphicCrossFadeItem))
                return CrossFadeTo;
            if (item == typeof(GraphicTextureOffsetItem))
                return TextureOffsetTo;
            if (item == typeof(GraphicTextureScaleItem))
                return TextureScaleTo;
            if (item == typeof(TextFontSizeItem))
                return Vector4.one * FontSizeTo;
            if (item == typeof(TMPFontSizeItem))
                return Vector4.one * TMPFontSizeTo;
            if (IsTMPText(item))
                return Vector4.one * TMPSpacingTo;
            if (item == typeof(TMPOutlineColorItem) || item == typeof(TMPUnderlayColorItem))
                return SpriteColorTo;
            if (item == typeof(TMPUnderlayOffsetItem))
                return TMPUnderlayOffsetTo;
            if (item == typeof(TMPOutlineWidthItem))
                return Vector4.one * TMPOutlineWidthTo;
            if (IsTMPMaterial(item))
                return Vector4.one * TMPMaterialTo;
            if (item == typeof(RectSizeDeltaItem))
                return RectSizeTo;
            if (item == typeof(RectAnchorsItem))
                return RectAnchorsTo;
            if (item == typeof(LightColorItem) || item == typeof(Light2DColorItem))
                return LightColorTo;
            if (item == typeof(Light2DIntensityItem))
                return Vector4.one * Light2DIntensityTo;
            if (item == typeof(Light2DRadiusItem))
                return Vector4.one * Light2DRadiusTo;
            if (item == typeof(Light2DFalloffItem))
                return Vector4.one * Light2DFalloffTo;
            if (item == typeof(LightSpotAngleItem))
                return Vector4.one * SpotAngleTo;
            if (item == typeof(LightShadowStrengthItem))
                return Vector4.one * ShadowStrengthTo;
            if (item == typeof(CameraFieldOfViewItem))
                return Vector4.one * FieldOfViewTo;
            if (item == typeof(CameraOrthographicSizeItem))
                return Vector4.one * OrthographicSizeTo;
            if (item == typeof(CameraZoomItem))
                return Vector4.one * ZoomTo;
            if (item == typeof(CameraClippingPlanesItem))
                return ClippingPlanesTo;
            if (item == typeof(SquashAndStretchItem))
                return Vector4.one * SquashTo;
            if (item == typeof(RotateAroundItem))
                return Vector4.one * RotateAroundTo;
            if (item == typeof(TextureOffsetItem))
                return TextureOffsetTo;
            if (item == typeof(TextureScaleItem))
                return TextureScaleTo;
            if (item == typeof(FogDensityItem))
                return Vector4.one * FogDensityTo;
            if (item == typeof(SkyboxExposureItem))
                return Vector4.one * ExposureTo;
            if (item == typeof(SkyboxRotationItem))
                return Vector4.one * SkyRotationTo;
            if (item == typeof(AmbientIntensityItem))
                return Vector4.one * AmbientIntensityTo;
            if (item == typeof(FogColorItem) || item == typeof(SkyboxTintItem) || item == typeof(AmbientColorItem))
                return RenderColorTo;
            if (item == typeof(SpriteColorItem) || item == typeof(LineColorItem))
                return SpriteColorTo;
            if (item == typeof(SpriteAlphaItem))
                return Vector4.zero;
            if (item == typeof(LineWidthItem))
                return Vector4.one * LineWidthTo;
            if (item == typeof(TrailTimeItem) || item == typeof(TrailWidthItem))
                return Vector4.one * TrailTo;
            if (item == typeof(ParticleSimulationSpeedItem))
                return Vector4.one * ParticleSpeedTo;
            if (item == typeof(ParticleEmissionRateItem))
                return Vector4.one * EmissionRateTo;
            if (item == typeof(AnimatorSpeedItem))
                return Vector4.one * AnimatorSpeedTo;
            if (item == typeof(TimeScaleItem))
                return Vector4.one * TimeScaleTo;
            return sized;
        }

        // the size and the spacings (points · em / 100) — the floats of the font material
        static bool IsTMPText(Type item) => item == typeof(TMPFontSizeItem) || item == typeof(TMPCharacterSpacingItem) || item == typeof(TMPWordSpacingItem)
            || item == typeof(TMPLineSpacingItem) || item == typeof(TMPParagraphSpacingItem);

        static bool IsTMPCharacter(Type item) => item == typeof(TMPCharacterOffsetItem) || item == typeof(TMPCharacterScaleItem) || item == typeof(TMPCharacterAlphaItem);

        static bool IsTMPMaterial(Type item) => item == typeof(TMPDilateItem) || item == typeof(TMPSoftnessItem) || item == typeof(TMPOutlineWidthItem)
            || item == typeof(TMPOutlineColorItem) || item == typeof(TMPUnderlayColorItem) || item == typeof(TMPUnderlayOffsetItem);
    }
}
