using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace xpTURN.Feedback.Editor
{
    // group I — the global render state. A RenderSettings item writes the active scene's; what the scene's set-up hides is
    // said here. RenderSettings has no object an Undo can record — the rules have no fix but a property name
    internal static class RenderGlobalTargets
    {
        internal static bool Fog(FeedbackItem item) => item is FogColorItem || item is FogDensityItem;

        internal static bool Skybox(FeedbackItem item) => item is SkyboxExposureItem || item is SkyboxTintItem || item is SkyboxRotationItem;

        internal static bool Ambient(FeedbackItem item) => item is AmbientColorItem || item is AmbientIntensityItem;

        internal static bool OfScene(FeedbackItem item) => Fog(item) || Skybox(item) || Ambient(item);

        // the property the binding writes: the item's name, else the standard one (the tint: _Tint, else _SkyTint)
        internal static string SkyboxProperty(FeedbackItem item, Material sky)
        {
            switch (item)
            {
                case SkyboxExposureItem e:
                    return string.IsNullOrEmpty(e.m_PropertyName) ? "_Exposure" : e.m_PropertyName;
                case SkyboxRotationItem r:
                    return string.IsNullOrEmpty(r.m_PropertyName) ? "_Rotation" : r.m_PropertyName;
                case SkyboxTintItem t:
                    return string.IsNullOrEmpty(t.m_PropertyName) ? SkyboxPrepare.TintName(sky) : t.m_PropertyName;
                default:
                    return null;
            }
        }

        internal static bool Takes(FeedbackItem item, ShaderPropertyType type)
            => item is SkyboxTintItem ? type == ShaderPropertyType.Color : type == ShaderPropertyType.Float || type == ShaderPropertyType.Range;

        internal static string ShaderGlobal(FeedbackItem item, out string form)
        {
            switch (item)
            {
                case ShaderGlobalFloatItem f:
                    form = "Float";
                    return f.m_PropertyName;
                case ShaderGlobalColorItem c:
                    form = "Color";
                    return c.m_PropertyName;
                case ShaderGlobalVectorItem v:
                    form = "Vector";
                    return v.m_PropertyName;
                default:
                    form = null;
                    return null;
            }
        }
    }

    // FB-I01: a fog item while the active scene's fog is off — nothing shows (turn it on in the Lighting window)
    internal sealed class FogOffRule : FeedbackRule
    {
        internal override string Id => "FB-I01";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (RenderSettings.fog)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] != null && c.Items[i].m_Active && RenderGlobalTargets.Fog(c.Items[i]))
                    into.Add(Result(c, i, "The scene's fog is off — nothing shows (Lighting window › Environment › Fog)"));
            }
        }
    }

    // FB-I02: a fog density with linear fog — the start and end distances fade it, not the density
    internal sealed class FogDensityLinearRule : FeedbackRule
    {
        internal override string Id => "FB-I02";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (!RenderSettings.fog || RenderSettings.fogMode != FogMode.Linear)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is FogDensityItem && c.Items[i].m_Active)
                    into.Add(Result(c, i, "Linear fog — the density shows nothing (the start and end distances fade it)"));
            }
        }
    }

    // FB-I03: an ambient item the source does not read — the color is the Flat · Trilight sky, the intensity the Skybox source's
    internal sealed class AmbientSourceRule : FeedbackRule
    {
        internal override string Id => "FB-I03";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            bool skybox = RenderSettings.ambientMode == AmbientMode.Skybox;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] == null || !c.Items[i].m_Active)
                    continue;
                if (c.Items[i] is AmbientColorItem && skybox)
                    into.Add(Result(c, i, "The ambient source is the skybox — the color shows nothing (Color or Gradient does)"));
                else if (c.Items[i] is AmbientIntensityItem && !skybox)
                    into.Add(Result(c, i, "The ambient source is a " + RenderSettings.ambientMode + " — the intensity shows nothing (the Skybox source's)"));
            }
        }
    }

    // FB-I04: a skybox item with no skybox, or a skybox without the property — the item is skipped. The fixes: the shader's
    // properties of the item's type
    internal sealed class SkyboxPropertyRule : FeedbackRule
    {
        internal override string Id => "FB-I04";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            Material sky = RenderSettings.skybox;
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if (item == null || !item.m_Active || !RenderGlobalTargets.Skybox(item))
                    continue;
                if (sky == null || sky.shader == null)
                {
                    into.Add(Result(c, i, "The scene has no skybox — the item is skipped"));
                    continue;
                }
                Shader shader = sky.shader;
                string name = RenderGlobalTargets.SkyboxProperty(item, sky);
                int at = shader.FindPropertyIndex(name);
                if (at >= 0 && RenderGlobalTargets.Takes(item, shader.GetPropertyType(at)))
                    continue;
                RuleResult r = Result(c, i, at < 0 ? "The skybox " + shader.name + " has no property " + name + " — the item is skipped"
                    : "The skybox's " + name + " is a " + shader.GetPropertyType(at) + " — the item is skipped");
                string path = c.ItemPath(i, "m_PropertyName");
                int n = 0;
                for (int k = 0; k < shader.GetPropertyCount() && n < MaterialTargets.MaxFixes; k++)
                {
                    if (!RenderGlobalTargets.Takes(item, shader.GetPropertyType(k)))
                        continue;
                    string pick = shader.GetPropertyName(k);
                    n++;
                    r.Fixes.Add(c.ItemFix("Use " + pick, path, x => x.stringValue = pick));
                }
                into.Add(r);
            }
        }
    }

    // FB-I05: a shader global without a name (skipped), or one name in two forms of the list — a Color and a Vector are one
    // value, a Float is another
    internal sealed class ShaderGlobalNameRule : FeedbackRule
    {
        internal override string Id => "FB-I05";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                string name = RenderGlobalTargets.ShaderGlobal(c.Items[i], out string form);
                if (form == null || !c.Items[i].m_Active)
                    continue;
                if (string.IsNullOrEmpty(name))
                {
                    into.Add(Result(c, i, "Name the shader global — the item is skipped"));
                    continue;
                }
                for (int k = 0; k < c.Items.Count; k++)
                {
                    if (k == i || c.Items[k] == null || !c.Items[k].m_Active)
                        continue;
                    string other = RenderGlobalTargets.ShaderGlobal(c.Items[k], out string otherForm);
                    if (otherForm != null && otherForm != form && other == name)
                    {
                        into.Add(Result(c, i, "Item " + k + " writes " + name + " as a " + otherForm + " too — the two fight over it"));
                        break;
                    }
                }
            }
        }
    }

    // FB-I06: a fog · skybox · ambient item of a player outside the active scene — it writes the active scene's settings (a prefab
    // stage's player writes the main scene's)
    internal sealed class ActiveSceneRule : FeedbackRule
    {
        internal override string Id => "FB-I06";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (c.Host == null || !c.Host.gameObject.scene.IsValid())
                return;
            UnityEngine.SceneManagement.Scene active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (c.Host.gameObject.scene == active)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] != null && c.Items[i].m_Active && RenderGlobalTargets.OfScene(c.Items[i]))
                    into.Add(Result(c, i, "The player is not in the active scene — the item writes " + active.name + "'s settings"));
            }
        }
    }

    // FB-I07: a skybox item with the skybox as the ambient source — the ambient light is made from the skybox at a bake, not
    // as it changes
    internal sealed class SkyboxAmbientRule : FeedbackRule
    {
        internal override string Id => "FB-I07";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (RenderSettings.ambientMode != AmbientMode.Skybox)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] != null && c.Items[i].m_Active && RenderGlobalTargets.Skybox(c.Items[i]))
                    into.Add(Result(c, i, "The ambient light is made from the skybox — it does not follow this change"));
            }
        }
    }
}
