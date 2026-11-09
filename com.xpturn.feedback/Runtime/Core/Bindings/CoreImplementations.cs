namespace xpTURN.Feedback
{
    // Core's explicit list, no attribute scan. It only registers lambdas — an exception in the registry's static constructor
    // would disable the registry for good (TypeInitializationException)
    internal static class CoreImplementations
    {
        internal static void RegisterAll()
        {
            FeedbackImplementations.Register<PositionItem>(item => new PositionBinding(item));
            FeedbackImplementations.Register<RotationItem>(item => new RotationBinding(item));
            FeedbackImplementations.Register<ScaleItem>(item => new ScaleBinding(item));
            FeedbackImplementations.Register<MaterialColorItem>(item => new MaterialColorBinding(item));
            FeedbackImplementations.Register<LightIntensityItem>(item => new LightIntensityBinding(item));
            FeedbackImplementations.Register<SetActiveItem>(item => new SetActiveBinding(item));
            FeedbackImplementations.Register<UnityEventItem>(item => new UnityEventBinding(item));
            FeedbackImplementations.Register<CameraShakeItem>(item => new CameraShakeBinding(item));
            FeedbackImplementations.Register<HitStopItem>(item => new HitStopBinding(item));
            FeedbackImplementations.Register<LightRangeItem>(item => new LightRangeBinding(item));
            FeedbackImplementations.Register<LightColorItem>(item => new LightColorBinding(item));
            FeedbackImplementations.Register<LightSpotAngleItem>(item => new LightSpotAngleBinding(item));
            FeedbackImplementations.Register<LightShadowStrengthItem>(item => new LightShadowStrengthBinding(item));
            FeedbackImplementations.Register<CameraFieldOfViewItem>(item => new CameraFieldOfViewBinding(item));
            FeedbackImplementations.Register<CameraOrthographicSizeItem>(item => new CameraOrthographicSizeBinding(item));
            FeedbackImplementations.Register<CameraZoomItem>(item => new CameraZoomBinding(item));
            FeedbackImplementations.Register<CameraClippingPlanesItem>(item => new CameraClippingPlanesBinding(item));
            FeedbackImplementations.Register<CameraBackgroundColorItem>(item => new CameraBackgroundColorBinding(item));
            FeedbackImplementations.Register<SquashAndStretchItem>(item => new SquashAndStretchBinding(item));
            FeedbackImplementations.Register<LookAtItem>(item => new LookAtBinding(item));
            FeedbackImplementations.Register<RotateAroundItem>(item => new RotateAroundBinding(item));
            FeedbackImplementations.Register<MatchTransformItem>(item => new MatchTransformBinding(item));
            FeedbackImplementations.Register<SetParentItem>(item => new SetParentBinding(item));
            FeedbackImplementations.Register<MaterialFloatItem>(item => new MaterialFloatBinding(item));
            FeedbackImplementations.Register<MaterialVectorItem>(item => new MaterialVectorBinding(item));
            FeedbackImplementations.Register<TextureOffsetItem>(item => new TextureOffsetBinding(item));
            FeedbackImplementations.Register<TextureScaleItem>(item => new TextureScaleBinding(item));
            FeedbackImplementations.Register<EmissionItem>(item => new EmissionBinding(item));
            FeedbackImplementations.Register<MaterialSwapItem>(item => new MaterialSwapBinding(item));
            FeedbackImplementations.Register<RendererVisibilityItem>(item => new RendererVisibilityBinding(item));
            FeedbackImplementations.Register<ShaderGlobalFloatItem>(item => new ShaderGlobalFloatBinding(item));
            FeedbackImplementations.Register<ShaderGlobalColorItem>(item => new ShaderGlobalColorBinding(item));
            FeedbackImplementations.Register<ShaderGlobalVectorItem>(item => new ShaderGlobalVectorBinding(item));
            FeedbackImplementations.Register<FogColorItem>(item => new FogColorBinding(item));
            FeedbackImplementations.Register<FogDensityItem>(item => new FogDensityBinding(item));
            FeedbackImplementations.Register<SkyboxExposureItem>(item => new SkyboxExposureBinding(item));
            FeedbackImplementations.Register<SkyboxTintItem>(item => new SkyboxTintBinding(item));
            FeedbackImplementations.Register<SkyboxRotationItem>(item => new SkyboxRotationBinding(item));
            FeedbackImplementations.Register<AmbientColorItem>(item => new AmbientColorBinding(item));
            FeedbackImplementations.Register<AmbientIntensityItem>(item => new AmbientIntensityBinding(item));
            FeedbackImplementations.Register<SpriteColorItem>(item => new SpriteColorBinding(item));
            FeedbackImplementations.Register<SpriteAlphaItem>(item => new SpriteAlphaBinding(item));
            FeedbackImplementations.Register<SpriteSwapItem>(item => new SpriteSwapBinding(item));
            FeedbackImplementations.Register<SpriteFlipItem>(item => new SpriteFlipBinding(item));
            FeedbackImplementations.Register<SpriteSheetItem>(item => new SpriteSheetBinding(item));
            FeedbackImplementations.Register<LineWidthItem>(item => new LineWidthBinding(item));
            FeedbackImplementations.Register<LineColorItem>(item => new LineColorBinding(item));
            FeedbackImplementations.Register<TrailTimeItem>(item => new TrailTimeBinding(item));
            FeedbackImplementations.Register<TrailWidthItem>(item => new TrailWidthBinding(item));
            FeedbackImplementations.Register<TrailEmittingItem>(item => new TrailEmittingBinding(item));
            FeedbackImplementations.Register<TrailClearItem>(item => new TrailClearBinding(item));
            FeedbackImplementations.Register<ComponentEnabledItem>(item => new ComponentEnabledBinding(item));
            FeedbackImplementations.Register<SpawnItem>(item => new SpawnBinding(item));
            FeedbackImplementations.Register<DespawnItem>(item => new DespawnBinding(item));
            FeedbackImplementations.Register<SetLayerItem>(item => new SetLayerBinding(item));
            FeedbackImplementations.Register<TimeScaleItem>(item => new TimeScaleBinding(item));
            FeedbackImplementations.Register<CustomFloatItem>(item => new CustomFloatBinding(item));
            FeedbackImplementations.Register<CustomVector3Item>(item => new CustomVector3Binding(item));
            FeedbackImplementations.Register<CustomColorItem>(item => new CustomColorBinding(item));
            FeedbackImplementations.Register<CustomFloatEventItem>(item => new CustomFloatEventBinding(item));
            FeedbackImplementations.Register<PlayPlayerItem>(item => new PlayPlayerBinding(item));
            FeedbackImplementations.Register<PlayerChainItem>(item => new PlayerChainBinding(item));
            FeedbackImplementations.Register<PlayerControlItem>(item => new PlayerControlBinding(item));
            FeedbackImplementations.Register<RandomPlayerItem>(item => new RandomPlayerBinding(item));
            FeedbackImplementations.Register<RandomUnityEventItem>(item => new RandomUnityEventBinding(item));
            FeedbackImplementations.Register<SignalBroadcastItem>(item => new SignalBroadcastBinding(item));
            FeedbackImplementations.Register<DebugItem>(item => new DebugBinding(item));
            // a RectTransform's size delta · pivot · anchors · offsets (CoreModule — Core's own)
            FeedbackImplementations.Register<RectSizeDeltaItem>(item => new RectSizeDeltaBinding(item));
            FeedbackImplementations.Register<RectPivotItem>(item => new RectPivotBinding(item));
            FeedbackImplementations.Register<RectAnchorsItem>(item => new RectAnchorsBinding(item));
            FeedbackImplementations.Register<RectOffsetsItem>(item => new RectOffsetsBinding(item));
        }
    }
}
