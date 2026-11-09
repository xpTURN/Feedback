using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // group H — a light's or a camera's set-up hides the item's effect. A target that does not resolve now says nothing here
    // (group A's)
    internal static class LightCameraTargets
    {
        internal static T Of<T>(RuleContext c, int i, out TargetItem t) where T : Component
        {
            t = c.Items[i] as TargetItem;
            return t != null && t.m_Active ? c.Target(i) as T : null;
        }

        // the lens items — what a Cinemachine brain rewrites
        internal static bool Lens(TargetItem t) => t is CameraFieldOfViewItem || t is CameraOrthographicSizeItem || t is CameraZoomItem || t is CameraClippingPlanesItem;
    }

    // FB-H01: a spot angle on a light that is not a spot light
    internal sealed class SpotAngleNotSpotRule : FeedbackRule
    {
        internal override string Id => "FB-H01";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                Light light = LightCameraTargets.Of<Light>(c, i, out TargetItem t);
                if (light != null && t is LightSpotAngleItem && light.type != LightType.Spot)
                    into.Add(Result(c, i, "Spot Angle changes a spot light only — this is a " + light.type + " light"));
            }
        }
    }

    // FB-H02: a shadow strength on a light without shadows
    internal sealed class ShadowStrengthNoShadowsRule : FeedbackRule
    {
        internal override string Id => "FB-H02";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                Light light = LightCameraTargets.Of<Light>(c, i, out TargetItem t);
                if (light != null && t is LightShadowStrengthItem && light.shadows == LightShadows.None)
                    into.Add(Result(c, i, "This light casts no shadows — Shadow Strength shows nothing"));
            }
        }
    }

    // FB-H03: a field of view on an orthographic camera, an orthographic size on a perspective one (Zoom reads the projection)
    internal sealed class ProjectionMismatchRule : FeedbackRule
    {
        internal override string Id => "FB-H03";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                Camera camera = LightCameraTargets.Of<Camera>(c, i, out TargetItem t);
                if (camera == null)
                    continue;
                if (t is CameraFieldOfViewItem && camera.orthographic)
                    into.Add(Result(c, i, "An orthographic camera — Field of View shows nothing (Orthographic Size or Zoom does)"));
                else if (t is CameraOrthographicSizeItem && !camera.orthographic)
                    into.Add(Result(c, i, "A perspective camera — Orthographic Size shows nothing (Field of View or Zoom does)"));
            }
        }
    }

    // FB-H04: a background color on a camera that draws a skybox — URP clears to the color with every other clear flag
    internal sealed class BackgroundSkyboxRule : FeedbackRule
    {
        internal override string Id => "FB-H04";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                Camera camera = LightCameraTargets.Of<Camera>(c, i, out TargetItem t);
                if (camera != null && t is CameraBackgroundColorItem && camera.clearFlags == CameraClearFlags.Skybox)
                    into.Add(Result(c, i, "The camera draws a skybox — Background Color shows nothing"));
            }
        }
    }

    // FB-H05: a lens item on a camera Cinemachine drives — its brain writes the lens each frame: a persistent value never shows,
    // an offset shows in play only (the preview's brain writes after it). The brain is found by its type name (no package reference)
    internal sealed class CinemachineLensRule : FeedbackRule
    {
        internal override string Id => "FB-H05";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                Camera camera = LightCameraTargets.Of<Camera>(c, i, out TargetItem t);
                if (camera != null && LightCameraTargets.Lens(t) && camera.GetComponent("CinemachineBrain") != null)
                    into.Add(Result(c, i, "Cinemachine drives this camera — its brain rewrites the lens each frame: a Tween or Set never shows, an offset shows in play only (Cinemachine/Lens animates the CinemachineCamera's lens)"));
            }
        }
    }

    // FB-H06: a 2D light's radius on a light that is not a point light, a radius or a falloff on a global light — the value
    // changes nothing. URP is a package the editor can't name: the light is found by its type name and its type read by
    // reflection (FB-H05's way — no package reference)
    internal sealed class Light2DUnusedRule : FeedbackRule
    {
        static readonly Type s_Light = Type.GetType(Light2DNames.Light);
        static readonly PropertyInfo s_LightType = s_Light?.GetProperty("lightType", BindingFlags.Public | BindingFlags.Instance);

        internal override string Id => "FB-H06";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (s_LightType == null)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                Component light = LightCameraTargets.Of<Component>(c, i, out TargetItem t);
                if (light == null || light.GetType() != s_Light || !(t is Light2DRadiusItem || t is Light2DFalloffItem))
                    continue;
                string type = s_LightType.GetValue(light).ToString();
                if (type == "Global")
                    into.Add(Result(c, i, "A global light has no radius or falloff — the value changes nothing"));
                else if (t is Light2DRadiusItem && type != "Point")
                    into.Add(Result(c, i, "Radius changes a point light only — this is a " + type + " light"));
            }
        }
    }
}
