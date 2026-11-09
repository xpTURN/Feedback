using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace xpTURN.Feedback.Tests
{
    // the 2D light items' fixtures — the module leaf's point light. Registered at the runtime's load (each play entry — the same
    // keys again): the test assemblies without URP can't name the light
    static class Light2DFixtures
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
            => ModuleFixtures.Add("light2d", Leaf, new[] { typeof(Light2DIntensityItem), typeof(Light2DColorItem), typeof(Light2DRadiusItem), typeof(Light2DFalloffItem) }, null);

        internal static Light2D Make(GameObject go)
        {
            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.pointLightOuterRadius = 2f;
            light.pointLightInnerRadius = 0.5f;
            return light;
        }

        static void Leaf(GameObject leaf) => Make(leaf);
    }
}
