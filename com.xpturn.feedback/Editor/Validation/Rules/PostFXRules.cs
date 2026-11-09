using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // the PostFX items' rules — URP is a package the editor can't name: its camera data, pipeline asset and renderer data are
    // read by their serialized fields, the profile's type by name
    static class PostFXTargets
    {
        internal static readonly Type CameraData = Type.GetType(PostFXNames.CameraData);
        internal static readonly Type VolumeProfile = Type.GetType(PostFXNames.VolumeProfile);

        // the first active PostFX item (the overlay's only — not a user volume's weight — when asked), or −1
        internal static int First(RuleContext c, bool overlay)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is TargetItem t && t.m_Active && t.Traits.Category == FeedbackCategory.PostFX && (!overlay || !(t is PostFXVolumeWeightItem)))
                    return i;
            }
            return -1;
        }

        // the main camera's URP data — null without a main camera, without URP or without the data (URP's defaults then)
        internal static Component MainCameraData(out Camera main)
        {
            main = Camera.main;
            if (main == null || CameraData == null)
                return null;
            return main.TryGetComponent(CameraData, out Component data) ? data : null;
        }
    }

    // FB-Q01: PostFX items while the main camera's post-processing is off, or its renderer has no post-process data — nothing shows
    internal sealed class PostFXPostProcessingRule : FeedbackRule
    {
        internal override string Id => "FB-Q01";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            int i = PostFXTargets.First(c, false);
            if (i < 0 || PostFXTargets.CameraData == null)
                return;
            Component data = PostFXTargets.MainCameraData(out Camera main);
            if (main == null)
                return;
            bool on = data != null && new SerializedObject(data).FindProperty("m_RenderPostProcessing").boolValue;
            if (!on)
            {
                RuleResult r = Result(c, i, "The main camera's post-processing is off — PostFX does not show");
                r.Fixes.Add(new RuleFix("Turn on the main camera's post-processing", () =>
                {
                    Component d = data != null ? data : Undo.AddComponent(main.gameObject, PostFXTargets.CameraData);
                    RuleFixes.Write(d, "m_RenderPostProcessing", p => p.boolValue = true);
                }));
                into.Add(r);
            }
            Object renderer = RendererOf(data);
            if (renderer != null && !HasPostProcessData(renderer))
                into.Add(Result(c, i, "The renderer " + renderer.name + " has no post-process data — PostFX does not show"));
        }

        // the camera's renderer data: its index (−1 · out of range — the asset's default) in the pipeline asset's list
        static Object RendererOf(Component data)
        {
            RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
            if (asset == null)
                return null;
            var so = new SerializedObject(asset);
            SerializedProperty list = so.FindProperty("m_RendererDataList");
            SerializedProperty fallback = so.FindProperty("m_DefaultRendererIndex");
            if (list == null || !list.isArray || list.arraySize == 0 || fallback == null)
                return null;
            int index = data != null ? new SerializedObject(data).FindProperty("m_RendererIndex").intValue : -1;
            if (index < 0 || index >= list.arraySize)
                index = fallback.intValue;
            return index >= 0 && index < list.arraySize ? list.GetArrayElementAtIndex(index).objectReferenceValue : null;
        }

        // the universal renderer's postProcessData · the 2D renderer's m_PostProcessData — a renderer with neither field passes
        static bool HasPostProcessData(Object renderer)
        {
            var so = new SerializedObject(renderer);
            SerializedProperty p = so.FindProperty("postProcessData") ?? so.FindProperty("m_PostProcessData");
            return p == null || p.objectReferenceValue != null;
        }
    }

    // FB-Q02: the settings' PostFX layer out of the main camera's volume mask — the overlay does not show
    internal sealed class PostFXVolumeMaskRule : FeedbackRule
    {
        internal override string Id => "FB-Q02";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            int i = PostFXTargets.First(c, true);
            if (i < 0)
                return;
            Component data = PostFXTargets.MainCameraData(out _);
            if (data == null)
                return;
            int layer = SettingsValues.From(Resources.Load<FeedbackSettings>(FeedbackSettings.ResourcePath)).PostFXLayer;
            if ((new SerializedObject(data).FindProperty("m_VolumeLayerMask").intValue & (1 << layer)) != 0)
                return;
            RuleResult r = Result(c, i, "The main camera's volume mask leaves out layer " + layer + " (the PostFX overlay's) — the overlay does not show");
            r.Fixes.Add(new RuleFix("Add layer " + layer + " to the main camera's volume mask", () => RuleFixes.Write(data, "m_VolumeLayerMask", p => p.intValue |= 1 << layer)));
            into.Add(r);
        }
    }

    // FB-Q03: a Volume Blend whose profile is not a Volume Profile — the prepare skips the item (the field takes any
    // ScriptableObject: the profile's type is Core RP's)
    internal sealed class PostFXBlendProfileRule : FeedbackRule
    {
        internal override string Id => "FB-Q03";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (PostFXTargets.VolumeProfile == null)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is PostFXVolumeBlendItem t) || !t.m_Active)
                    continue;
                if (t.m_Profile == null)
                    into.Add(Result(c, i, "No profile — the item is skipped"));
                else if (!PostFXTargets.VolumeProfile.IsInstanceOfType(t.m_Profile))
                    into.Add(Result(c, i, "The profile is not a Volume Profile — the item is skipped"));
            }
        }
    }

    // FB-Q04: a Depth of Field that does not set the mode — with the volumes' depth of field off nothing shows
    internal sealed class PostFXDepthOfFieldModeRule : FeedbackRule
    {
        internal override string Id => "FB-Q04";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is PostFXDepthOfFieldItem t && t.m_Active && (!t.m_OverrideMode || t.m_Mode == PostFXDepthOfFieldMode.Off))
                    into.Add(Result(c, i, "The mode is not set — with the volumes' depth of field off it does not show"));
            }
        }
    }
}
