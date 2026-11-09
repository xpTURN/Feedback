using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor
{
    public sealed class MotionDataTests
    {
        static readonly Assembly Core = typeof(FeedbackMotion).Assembly;
        static readonly float[] Odd = { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity };

        static FeedbackMotion[] AllMotions() => new FeedbackMotion[]
        {
            new SetMotion(), new TweenMotion(), new PunchMotion(), new ShakeMotion(), new CurveMotion(), new FlashMotion(),
            new GradientMotion(), new OscillateMotion(), new BlinkMotion(), new WiggleMotion(), new SpringMotion(),
        };

        static void AssertLinear(Ease e, string at)
            => Assert.That((e.m_Type, e.m_P1, e.m_P2, e.m_Curve), Is.EqualTo((EaseType.Linear, 0f, 0f, (AnimationCurve)null)), at);

        static string Keys(AnimationCurve c) => string.Join(" ", c.keys.Select(k => $"{k.time}:{k.value}"));

        [Test]
        public void MT01_DefaultsFollowTheTable()
        {
            var set = new SetMotion().m_Params;
            Assert.That((set.m_Value, set.m_Relative), Is.EqualTo((Vector4.zero, false)), "Set");
            var tween = new TweenMotion().m_Params;
            Assert.That((tween.m_Mode, tween.m_From, tween.m_To, tween.m_Loops, tween.m_Yoyo), Is.EqualTo((TweenMode.To, Vector4.zero, Vector4.one, 1, false)), "Tween");
            AssertLinear(tween.m_Ease, "Tween ease");
            var punch = new PunchMotion().m_Params;
            Assert.That(punch.m_Amplitude, Is.EqualTo(Vector4.one), "Punch amplitude");
            Assert.That((punch.m_Vibrato, punch.m_Decay), Is.EqualTo((5f, 1f)), "Punch — Vibrato 5 · Decay 1");
            var shake = new ShakeMotion().m_Params;
            Assert.That((shake.m_Amplitude, shake.m_Frequency, shake.m_Noise), Is.EqualTo((Vector4.one, 10f, ShakeNoise.Smooth)), "Shake");
            AssertLinear(shake.m_Falloff, "Shake — Falloff Linear");
            var curve = new CurveMotion().m_Params;
            Assert.That(Keys(curve.m_Curve), Is.EqualTo("0:0 0.5:1 1:0"), "Curve keys");
            Assert.That((curve.m_RemapZero, curve.m_RemapOne), Is.EqualTo((Vector4.zero, Vector4.one)), "Curve remap");
            var flash = new FlashMotion().m_Params;
            Assert.That((flash.m_Target, flash.m_Attack, flash.m_Hold, flash.m_Release), Is.EqualTo((Vector4.one, 0.1f, 0.2f, 0.7f)), "Flash");
            AssertLinear(flash.m_Ease, "Flash ease");
            var gradient = new GradientMotion().m_Params;
            var white = new Gradient();
            Assert.That(gradient.m_Gradient.colorKeys, Is.EqualTo(white.colorKeys), "Gradient colors");
            Assert.That(gradient.m_Gradient.alphaKeys, Is.EqualTo(white.alphaKeys), "Gradient alphas");
            Assert.That(gradient.m_Mode, Is.EqualTo(GradientWrite.Override));
            var oscillate = new OscillateMotion().m_Params;
            Assert.That((oscillate.m_Amplitude, oscillate.m_Frequency, oscillate.m_Waveform, oscillate.m_Phase), Is.EqualTo((Vector4.one, 1f, Waveform.Sine, 0f)), "Oscillate");
            var blink = new BlinkMotion().m_Params;
            Assert.That((blink.m_Value, blink.m_Period, blink.m_Duty, blink.m_Jitter, blink.m_StartOn), Is.EqualTo((Vector4.one, 0.2f, 0.5f, 0f, true)), "Blink");
            var wiggle = new WiggleMotion().m_Params;
            Assert.That((wiggle.m_Amplitude, wiggle.m_Frequency, wiggle.m_Octaves), Is.EqualTo((Vector4.one, 1f, 2)), "Wiggle");
            var spring = new SpringMotion().m_Params;
            Assert.That((spring.m_Mode, spring.m_Value, spring.m_Relative), Is.EqualTo((SpringMode.Bump, Vector4.one, false)), "Spring");
            Assert.That((spring.m_Frequency, spring.m_Damping, spring.m_Settle), Is.EqualTo((6f, 0.4f, 0.001f)), "Spring — 6 · 0.4 · 0.001");

            // Default twice is the same value
            Assert.That(EditorJsonUtility.ToJson(CurveParams.Default), Is.EqualTo(EditorJsonUtility.ToJson(CurveParams.Default)));
            Assert.That(EditorJsonUtility.ToJson(TweenParams.Default), Is.EqualTo(EditorJsonUtility.ToJson(TweenParams.Default)));
        }

        [Test]
        public void MT02_DefaultsAreNewInstances()
        {
            Assert.That(CurveParams.Default.m_Curve, Is.Not.SameAs(CurveParams.Default.m_Curve), "Default twice — curve");
            Assert.That(GradientParams.Default.m_Gradient, Is.Not.SameAs(GradientParams.Default.m_Gradient), "Default twice — gradient");
            var a = new CurveMotion();
            var b = new CurveMotion();
            Assert.That(a.m_Params.m_Curve, Is.Not.SameAs(b.m_Params.m_Curve), "two motions — curve");
            Assert.That(new GradientMotion().m_Params.m_Gradient, Is.Not.SameAs(new GradientMotion().m_Params.m_Gradient), "two motions — gradient");
        }

        [Test]
        public void MT02_EditingOneLeavesTheOthers()
        {
            var a = new CurveMotion();
            var b = new CurveMotion();
            a.m_Params.m_Curve.AddKey(0.25f, 0.3f);
            Assert.That(b.m_Params.m_Curve.length, Is.EqualTo(3), "the other motion");
            Assert.That(CurveParams.Default.m_Curve.length, Is.EqualTo(3), "a new Default");

            var go = new GameObject("MT02");
            try
            {
                var container = go.AddComponent<DataModelTestContainer>();
                container.m_Items.Add(new RotationItem { m_Motion = new CurveMotion() });
                container.m_Items.Add(new RotationItem { m_Motion = new CurveMotion() });
                var so = new SerializedObject(container);
                so.FindProperty("m_Items.Array.data[0].m_Motion.m_Params.m_Curve").animationCurveValue =
                    new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(0.6f, 0.2f), new Keyframe(1f, 0f));
                so.ApplyModifiedProperties();
                Assert.That(((CurveMotion)((TargetItem)container.m_Items[0]).m_Motion).m_Params.m_Curve.length, Is.EqualTo(4), "the edited item");
                Assert.That(((CurveMotion)((TargetItem)container.m_Items[1]).m_Motion).m_Params.m_Curve.length, Is.EqualTo(3), "the other item");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void MT03_NaturalDuration()
        {
            Assert.That(new SetMotion().NaturalDuration, Is.EqualTo(0f), "Set");
            foreach (var m in AllMotions().Where(m => !(m is SetMotion) && !(m is SpringMotion)))
                Assert.That(m.NaturalDuration, Is.EqualTo(-1f), m.GetType().Name);

            foreach (float zeta in new[] { 0.4f, 1f, 3f })
                foreach (bool moveTo in new[] { true, false })
                {
                    var s = new SpringMotion();
                    s.m_Params.m_Damping = zeta;
                    s.m_Params.m_Mode = moveTo ? SpringMode.MoveTo : SpringMode.Bump;
                    float expected = SpringMath.SettleTime(SpringShape.Of(6f, zeta), 0.001f, moveTo);
                    Assert.That(BitConverter.SingleToInt32Bits(s.NaturalDuration), Is.EqualTo(BitConverter.SingleToInt32Bits(expected)), $"zeta {zeta} moveTo {moveTo}");
                }
            foreach (float zeta in new[] { 1.2f, 10f })
            {
                var bump = new SpringMotion();
                bump.m_Params.m_Damping = zeta;
                var moveTo = new SpringMotion();
                moveTo.m_Params.m_Damping = zeta;
                moveTo.m_Params.m_Mode = SpringMode.MoveTo;
                Assert.That(bump.NaturalDuration, Is.LessThan(moveTo.NaturalDuration), $"zeta {zeta}");
            }
            foreach (float w in Odd)
                foreach (int which in new[] { 0, 1, 2 })
                {
                    var s = new SpringMotion();
                    if (which == 0) s.m_Params.m_Frequency = w;
                    else if (which == 1) s.m_Params.m_Damping = w;
                    else s.m_Params.m_Settle = w;
                    float d = s.NaturalDuration;
                    Assert.That(float.IsFinite(d) && d >= 0f, $"field {which} = {w} → {d}");
                }
        }

        [Test]
        public void MT04_Ease()
        {
            float[] ts = { -1f, 0f, 0.1f, 0.37f, 0.5f, 0.83f, 1f, 2f, float.NaN };
            float[] ps = { 0f, 1f, 1.70158f, 0.3f, -2f };
            for (int k = 0; k < 31; k++)
                foreach (float p1 in ps)
                    foreach (float p2 in ps)
                        foreach (float t in ts)
                        {
                            var e = new Ease { m_Type = (EaseType)k, m_P1 = p1, m_P2 = p2 };
                            Assert.That(BitConverter.SingleToInt32Bits(e.Evaluate(t)), Is.EqualTo(BitConverter.SingleToInt32Bits(Easing.Evaluate((EaseType)k, p1, p2, t))), $"{(EaseType)k} {p1} {p2} {t}");
                        }

            foreach (EaseType type in Enum.GetValues(typeof(EaseType)))
            {
                var e = Ease.Of(type);
                bool back = type >= EaseType.BackIn && type <= EaseType.BackInOut;
                bool elastic = type >= EaseType.ElasticIn && type <= EaseType.ElasticInOut;
                Assert.That((e.m_Type, e.m_P1, e.m_P2, e.m_Curve), Is.EqualTo((type, back ? 1.70158f : elastic ? 1f : 0f, elastic ? 0.3f : 0f, (AnimationCurve)null)), type.ToString());
            }
            Assert.That(default(Ease).m_Type, Is.EqualTo(EaseType.Linear));

            var custom = new Ease { m_Type = EaseType.Custom };
            float[] clamped = { 0f, 0f, 0.1f, 0.37f, 0.5f, 0.83f, 1f, 1f, 0f };
            for (int i = 0; i < ts.Length; i++)
                Assert.That(custom.Evaluate(ts[i]), Is.EqualTo(clamped[i]), $"null curve at {ts[i]}");
            custom.m_Curve = new AnimationCurve();
            for (int i = 0; i < ts.Length; i++)
                Assert.That(custom.Evaluate(ts[i]), Is.EqualTo(clamped[i]), $"empty curve at {ts[i]}");
            custom.m_Curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1.5f), new Keyframe(1f, 1f));
            for (int i = 0; i < ts.Length; i++)
                Assert.That(custom.Evaluate(ts[i]), Is.EqualTo(custom.m_Curve.Evaluate(clamped[i])), $"curve at {ts[i]}");
            Assert.That(custom.Evaluate(0.5f), Is.EqualTo(1.5f), "an overshooting curve is not cut");
        }

        [Test]
        public void MT06_OneSealedMotionPerKind()
        {
            var types = TypeCache.GetTypesDerivedFrom<FeedbackMotion>().Where(t => t.Assembly == Core && !t.IsAbstract).ToArray();
            Assert.That(types.Length, Is.EqualTo(Enum.GetValues(typeof(MotionKind)).Length), "one per kind");
            var kinds = types.Select(t => ((FeedbackMotion)Activator.CreateInstance(t)).Kind).ToArray();
            Assert.That(kinds.Distinct().Count(), Is.EqualTo(types.Length), "kinds differ");
            foreach (var type in types)
            {
                var kind = ((FeedbackMotion)Activator.CreateInstance(type)).Kind;
                Assert.That(type.Name, Is.EqualTo(kind + "Motion"), "Kind matches the name");
                Assert.That(type.IsSealed && type.IsSerializable && type.GetConstructor(Type.EmptyTypes) != null, type.Name);
            }
        }

        // the calls an evaluator makes on its copied parameters are readonly members — no defensive copy
        [Test]
        public void MT06_ReadonlyMembers()
        {
            static bool IsReadOnly(MethodInfo m) => m.GetCustomAttributes(false).Any(a => a.GetType().Name == "IsReadOnlyAttribute");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Assert.That(IsReadOnly(typeof(Ease).GetMethod(nameof(Ease.Evaluate), flags)), "Ease.Evaluate");
            Assert.That(IsReadOnly(typeof(SpringParams).GetMethod(nameof(SpringParams.SettleTime), flags)), "SpringParams.SettleTime");
        }

        [Test]
        public void MT08_FlashesWithRealMotions()
        {
            TargetItem[] sensitive = { new MaterialColorItem(), new LightIntensityItem(), new SetActiveItem() };
            foreach (var item in sensitive)
            {
                item.m_Motion = new FlashMotion();
                Assert.That((item.Flashes, item.EffectiveCategory), Is.EqualTo((true, FeedbackCategory.Flash)), $"{item.GetType().Name} Flash");
                item.m_Motion = new BlinkMotion();
                Assert.That((item.Flashes, item.EffectiveCategory), Is.EqualTo((true, FeedbackCategory.Flash)), $"{item.GetType().Name} Blink");
                item.m_Motion = new TweenMotion();
                Assert.That((item.Flashes, item.EffectiveCategory), Is.EqualTo((false, item.Traits.Category)), $"{item.GetType().Name} Tween");
            }
        }

        // Core's public types
        static readonly string[] PublicTypes =
        {
            "ActionItem", "BlinkMotion", "CameraShakeItem", "CameraShakeReceiver", "CategoryOverride", "ChannelRef", "ChannelRefMode", "ChildLookup", "CurveMotion",
            "DirectionCondition", "DistanceReference", "EffectItem", "FeedbackCategories", "FeedbackCategory", "FeedbackChannel", "FeedbackChannels", "FeedbackCommand", "FeedbackCommandKind",
            "FeedbackItem", "FeedbackMotion", "FeedbackPlayer", "FeedbackPlayerLoop", "FeedbackPreset", "FeedbackRange", "FeedbackReceiver", "FeedbackSettings", "FeedbackTimeScale",
            "FlashMotion", "FlowItem", "GradientMotion", "GradientWrite", "HitStopItem", "HoldItem", "IPlaybackListener", "IntensityResponse", "ItemClock",
            "ItemConditions", "ItemPlayDirection", "ItemRandomness", "ItemTiming", "LightIntensityItem", "LoopEndItem", "LoopStartItem", "MaterialColorItem",
            "MaterialWriteMode", "MotionDriver", "MotionDriverMode", "OscillateMotion", "PlayArgs", "PlayPolicy", "PlaybackEnd", "PlaybackHandle", "PlayerAutoPlay", "PlayerDirection", "PlayerDisableBehavior", "PlayerEventKind", "PlayerInitialization", "PlayerTimeMode", "PositionItem", "PositionSpace", "PunchMotion", "RotationInterpolation",
            "RotationItem", "ScaleItem", "SetActiveItem", "SetMotion", "ShakeMotion", "ShakeNoise", "SpringColor", "SpringFloat", "SpringMode", "SpringMotion", "SpringVector2", "SpringVector3", "StopMode", "TargetItem",
            "TargetRef", "TargetRefMode", "TransformSpace", "TweenMode", "TweenMotion", "UnityEventItem", "WaitForSignalItem", "WaitItem",
            "WiggleMotion",
            "LightRangeItem", "LightColorItem", "LightSpotAngleItem", "LightShadowStrengthItem",
            "Light2DIntensityItem", "Light2DColorItem", "Light2DRadiusItem", "Light2DFalloffItem",
            "GraphicColorItem", "GraphicAlphaItem", "GraphicCrossFadeItem", "GraphicMaterialItem", "GraphicRaycastTargetItem", "GraphicTextureOffsetItem", "GraphicTextureScaleItem", "ImageFillItem", "ImageSpriteItem", "ImageSpriteSheetItem", "CanvasGroupAlphaItem", "CanvasGroupInteractableItem", "CanvasGroupBlocksRaycastsItem", "TextContentItem", "TextFontSizeItem", "SelectableInteractableItem", "RectSizeDeltaItem", "RectPivotItem", "RectAnchorsItem", "RectOffsetsItem", "VideoPlayerControlItem", "VideoAction",
            "TMPFontSizeItem", "TMPCharacterSpacingItem", "TMPWordSpacingItem", "TMPLineSpacingItem", "TMPParagraphSpacingItem", "TMPDilateItem", "TMPSoftnessItem", "TMPOutlineWidthItem", "TMPOutlineColorItem", "TMPUnderlayColorItem", "TMPUnderlayOffsetItem", "TMPTextItem", "TMPRevealItem", "TMPCountToItem", "TMPRevealUnit", "TMPCharacterOffsetItem", "TMPCharacterScaleItem", "TMPCharacterAlphaItem", "TMPCharacterOrder",
            "ScreenFlashItem", "ScreenFadeItem", "FloatingTextItem", "ScreenFadeShape", "ScreenSide", "ScreenFadeCenter", "FloatingTextSource",
            "PostFXBloomItem", "PostFXChromaticAberrationItem", "PostFXColorAdjustmentsItem", "PostFXDepthOfFieldItem", "PostFXFilmGrainItem", "PostFXLensDistortionItem", "PostFXMotionBlurItem", "PostFXPaniniProjectionItem", "PostFXVignetteItem", "PostFXWhiteBalanceItem", "PostFXChannelMixerItem", "PostFXSplitToningItem", "PostFXLiftGammaGainItem", "PostFXShadowsMidtonesHighlightsItem", "PostFXVolumeBlendItem", "PostFXVolumeWeightItem",
            "PostFXFloat", "PostFXColor", "PostFXVector2", "PostFXVector4", "PostFXDepthOfFieldMode",
            "CinemachineImpulseItem", "CinemachineImpulseClearItem", "CinemachineLensFieldOfViewItem", "CinemachineLensOrthographicSizeItem", "CinemachineLensDutchItem", "CinemachineNoiseAmplitudeGainItem", "CinemachineNoiseFrequencyGainItem", "CinemachineActivateCameraItem",
            "CinemachineImpulseShape", "CinemachineImpulseSpread", "CinemachineImpulseMode", "CinemachineBlendStyle", "CinemachineBlendSpec",
            "GamepadRumbleItem", "GamepadRumbleDevice",
            "CameraFieldOfViewItem", "CameraOrthographicSizeItem", "CameraZoomItem", "CameraClippingPlanesItem", "CameraBackgroundColorItem",
            "SquashAndStretchItem", "LookAtItem", "RotateAroundItem", "MatchTransformItem", "SetParentItem", "SquashAxis", "LookAtMode", "LookAtUp",
            // the material items' common fields' interface is internal
            "MaterialFloatItem", "MaterialVectorItem", "TextureOffsetItem", "TextureScaleItem", "EmissionItem", "MaterialSwapItem", "RendererVisibilityItem",
            "ShaderGlobalFloatItem", "ShaderGlobalColorItem", "ShaderGlobalVectorItem", "FogColorItem", "FogDensityItem", "SkyboxExposureItem", "SkyboxTintItem",
            "SkyboxRotationItem", "AmbientColorItem", "AmbientIntensityItem",
            "SpriteColorItem", "SpriteAlphaItem", "SpriteSwapItem", "SpriteFlipItem", "SpriteSheetItem", "LineWidthItem", "LineColorItem",
            "TrailTimeItem", "TrailWidthItem", "TrailEmittingItem", "TrailClearItem", "SpriteAxis", "LineEnd",
            "ComponentEnabledItem", "SpawnItem", "DespawnItem", "SetLayerItem", "SpawnPlace", "SpawnParent",
            "ParticleControlItem", "ParticleEmitItem", "ParticleSpawnItem", "ParticleSimulationSpeedItem", "ParticleEmissionRateItem", "ParticleAction", "ParticleStopMode",
            "AnimatorParameterItem", "AnimatorFloatItem", "AnimatorSpeedItem", "AnimatorLayerWeightItem", "AnimatorStateItem", "AnimatorParameterKind", "AnimatorValuePick",
            "RigidbodyForceItem", "RigidbodyExplosionItem", "RigidbodyVelocityItem", "Rigidbody2DForceItem", "Rigidbody2DVelocityItem",
            "PhysicsForceMode", "PhysicsForceMode2D", "PhysicsForceKind", "VelocityWrite",
            // IFeedbackTarget`1 — the package's public target interface
            "TimeScaleItem", "CustomFloatItem", "CustomVector3Item", "CustomColorItem", "CustomFloatEventItem", "IFeedbackTarget`1", "IFeedbackTargetStage", "FeedbackTargetStage",
            "PlayPlayerItem", "PlayerChainItem", "PlayerControlItem", "RandomPlayerItem", "RandomUnityEventItem", "SignalBroadcastItem", "DebugItem",
            "PlayerControlAction", "DebugAction", "IFeedbackSignalReceiver", "FeedbackSignal", "FeedbackSignals", "FeedbackSignalReceiver",
            // the extension bases and what their code gets
            "CustomActionItem", "CustomEffectItem", "CustomAction", "CustomEffect", "CustomPrepare", "CustomPlay", "CustomTick", "CustomEnd",
            "CustomFeedbackAttribute", "CustomPreview",
        };

        [Test]
        public void MT09_PublicSurface()
        {
            var names = Core.GetExportedTypes().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.That(names, Is.EqualTo(PublicTypes.OrderBy(n => n, StringComparer.Ordinal).ToArray()));
        }
    }
}
