using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace xpTURN.Feedback.Tests
{
    // the PostFX items' fixtures — the leaf's volume for Volume Weight (the globals take no target) · a profile of its own for
    // Volume Blend. Registered at the runtime's load (each play entry — the same keys again): the test assemblies without URP
    // can't name the volume
    static class PostFXFixtures
    {
        static VolumeProfile s_Profile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
            => ModuleFixtures.Add("postfx", Leaf, new[]
            {
                typeof(PostFXBloomItem), typeof(PostFXChromaticAberrationItem), typeof(PostFXColorAdjustmentsItem), typeof(PostFXDepthOfFieldItem),
                typeof(PostFXFilmGrainItem), typeof(PostFXLensDistortionItem), typeof(PostFXMotionBlurItem), typeof(PostFXPaniniProjectionItem),
                typeof(PostFXVignetteItem), typeof(PostFXWhiteBalanceItem), typeof(PostFXChannelMixerItem), typeof(PostFXSplitToningItem),
                typeof(PostFXLiftGammaGainItem), typeof(PostFXShadowsMidtonesHighlightsItem), typeof(PostFXVolumeBlendItem), typeof(PostFXVolumeWeightItem),
            }, Fields);

        static void Leaf(GameObject leaf) => leaf.AddComponent<Volume>();

        static void Fields(TargetItem t)
        {
            if (!(t is PostFXVolumeBlendItem blend))
                return;
            if (s_Profile == null)
            {
                s_Profile = ScriptableObject.CreateInstance<VolumeProfile>();
                s_Profile.name = "G1-profile";
                Vignette v = s_Profile.Add<Vignette>(false);
                v.intensity.overrideState = true;
                v.intensity.value = 0.6f;
            }
            blend.m_Profile = s_Profile;
        }
    }
}
