using UnityEngine;

namespace xpTURN.Feedback
{
    // the URP assembly's registration — the runtime at SubsystemRegistration (every play entry — the same factories
    // again), the editor at its load (the preview prepares them)
    static class URPImplementations
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            FeedbackImplementations.Register<Light2DIntensityItem>(item => new Light2DIntensityBinding(item));
            FeedbackImplementations.Register<Light2DColorItem>(item => new Light2DColorBinding(item));
            FeedbackImplementations.Register<Light2DRadiusItem>(item => new Light2DRadiusBinding(item));
            FeedbackImplementations.Register<Light2DFalloffItem>(item => new Light2DFalloffBinding(item));
            // the PostFX items — one sealed binding, each item's target · the overlay service
            FeedbackImplementations.Register<PostFXBloomItem>(item => new PostFXBinding(item, new PostFXBloomTarget(item)));
            FeedbackImplementations.Register<PostFXChromaticAberrationItem>(item => new PostFXBinding(item, new PostFXChromaticAberrationTarget(item)));
            FeedbackImplementations.Register<PostFXColorAdjustmentsItem>(item => new PostFXBinding(item, new PostFXColorAdjustmentsTarget(item)));
            FeedbackImplementations.Register<PostFXDepthOfFieldItem>(item => new PostFXBinding(item, new PostFXDepthOfFieldTarget(item)));
            FeedbackImplementations.Register<PostFXFilmGrainItem>(item => new PostFXBinding(item, new PostFXFilmGrainTarget(item)));
            FeedbackImplementations.Register<PostFXLensDistortionItem>(item => new PostFXBinding(item, new PostFXLensDistortionTarget(item)));
            FeedbackImplementations.Register<PostFXMotionBlurItem>(item => new PostFXBinding(item, new PostFXMotionBlurTarget(item)));
            FeedbackImplementations.Register<PostFXPaniniProjectionItem>(item => new PostFXBinding(item, new PostFXPaniniProjectionTarget(item)));
            FeedbackImplementations.Register<PostFXVignetteItem>(item => new PostFXBinding(item, new PostFXVignetteTarget(item)));
            FeedbackImplementations.Register<PostFXWhiteBalanceItem>(item => new PostFXBinding(item, new PostFXWhiteBalanceTarget(item)));
            FeedbackImplementations.Register<PostFXChannelMixerItem>(item => new PostFXBinding(item, new PostFXChannelMixerTarget(item)));
            FeedbackImplementations.Register<PostFXSplitToningItem>(item => new PostFXBinding(item, new PostFXSplitToningTarget(item)));
            FeedbackImplementations.Register<PostFXLiftGammaGainItem>(item => new PostFXBinding(item, new PostFXLiftGammaGainTarget(item)));
            FeedbackImplementations.Register<PostFXShadowsMidtonesHighlightsItem>(item => new PostFXBinding(item, new PostFXShadowsMidtonesHighlightsTarget(item)));
            FeedbackImplementations.Register<PostFXVolumeBlendItem>(item => new PostFXBinding(item, new PostFXVolumeBlendTarget(item)));
            FeedbackImplementations.Register<PostFXVolumeWeightItem>(item => new PostFXVolumeWeightBinding(item));
            FeedbackContext.RegisterExtension(PostFXOverlayService.Key, context => new PostFXOverlayService(context));
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();
#endif
    }
}
