using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace xpTURN.Feedback.Editor
{
    // F — a material property item's renderer, materials and shader (the items of the common fields — Material Color · Float
    // · Vector · Texture Offset · Scale · Emission)

    internal static class MaterialTargets
    {
        internal const int MaxFixes = 8;

        internal static Renderer RendererOf(RuleContext c, int i, out IMaterialPropertyItem m)
        {
            m = c.Items[i] as IMaterialPropertyItem;
            return m != null && c.Items[i].m_Active ? c.Target(i) as Renderer : null;
        }

        // the material the property is read from (−1: material 0) — null when the prepare refuses the materials (F07)
        internal static Material Source(Renderer r, IMaterialPropertyItem m)
        {
            Material[] mats = r.sharedMaterials;
            if (mats.Length == 0 || m.MaterialIndex >= mats.Length)
                return null;
            if (m.MaterialIndex < 0 && m.WriteMode == MaterialWriteMode.Instance && mats.Any(x => x == null))
                return null;
            return mats[Mathf.Max(m.MaterialIndex, 0)];
        }

        // the property id the binding writes — a texture item's is the texture's (−1 when an empty name has no property: the
        // main color · a float's or a vector's)
        internal static int PropertyId(Material material, IMaterialPropertyItem m)
        {
            if (m is MaterialColorItem && string.IsNullOrEmpty(m.PropertyName))
                return MaterialColorBinding.MainColor(material.shader, out int id) ? id : -1;
            string name = PropertyName(material, m);
            return name != null ? Shader.PropertyToID(name) : -1;
        }

        // the named property — an empty name is _EmissionColor or the main texture (null: a color's main color · none)
        internal static string PropertyName(Material material, IMaterialPropertyItem m)
        {
            if (!string.IsNullOrEmpty(m.PropertyName))
                return m.PropertyName;
            switch (m)
            {
                case EmissionItem _:
                    return "_EmissionColor";
                case TextureOffsetItem _:
                case TextureScaleItem _:
                    return TextureST.MainTexture(material.shader);
                default:
                    return null;
            }
        }

        // the shader types the item takes
        internal static bool Takes(IMaterialPropertyItem m, ShaderPropertyType type)
        {
            switch (m)
            {
                case MaterialFloatItem _:
                    return type == ShaderPropertyType.Float || type == ShaderPropertyType.Range;
                case MaterialVectorItem _:
                    return type == ShaderPropertyType.Vector;
                case TextureOffsetItem _:
                case TextureScaleItem _:
                    return type == ShaderPropertyType.Texture;
                default:
                    return type == ShaderPropertyType.Color;
            }
        }

        internal static string What(IMaterialPropertyItem m)
        {
            switch (m)
            {
                case MaterialFloatItem _:
                    return "float";
                case MaterialVectorItem _:
                    return "vector";
                case TextureOffsetItem _:
                case TextureScaleItem _:
                    return "texture";
                default:
                    return "color";
            }
        }

        // the property's value in a material (a texture's scale and offset) — false when the material lacks it
        internal static bool TryValue(Material material, IMaterialPropertyItem m, int id, out Vector4 value)
        {
            value = default;
            if (material == null)
                return false;
            switch (m)
            {
                case MaterialFloatItem _:
                    if (!material.HasFloat(id))
                        return false;
                    value = new Vector4(material.GetFloat(id), 0f, 0f, 0f);
                    return true;
                case MaterialVectorItem _:
                    if (!material.HasVector(id))
                        return false;
                    value = material.GetVector(id);
                    return true;
                case TextureOffsetItem _:
                case TextureScaleItem _:
                    if (!material.HasTexture(id))
                        return false;
                    Vector2 scale = material.GetTextureScale(id);
                    Vector2 offset = material.GetTextureOffset(id);
                    value = new Vector4(scale.x, scale.y, offset.x, offset.y);
                    return true;
                default:
                    if (!material.HasColor(id))
                        return false;
                    value = material.GetColor(id);
                    return true;
            }
        }

        internal static bool Long(TargetItem t)
            => t.m_Timing.m_Infinite || t.m_Timing.m_RepeatForever || t.m_Timing.m_Duration >= 1f;
    }

    // FB-F01: a Persist through a property block — the block stays
    internal sealed class BlockPersistRule : FeedbackRule
    {
        internal override string Id => "FB-F01";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (MaterialTargets.RendererOf(c, i, out IMaterialPropertyItem m) == null || m.WriteMode != MaterialWriteMode.PropertyBlock
                    || !(c.Items[i] is TargetItem t) || t.m_Motion == null || !PersistTwiceRule.Persists(t.m_Motion))
                    continue;
                RuleResult r = Result(c, i, "A Persist through a property block stays as a block — the renderer stays off the SRP Batcher (an instance's Persist goes when its slot leaves)");
                Component host = c.Host;
                string path = c.ItemPath(i, "m_WriteMode");
                r.Fixes.Add(c.ItemFix("Write to material instances", path, x => x.enumValueIndex = (int)MaterialWriteMode.Instance));
                into.Add(r);
            }
        }
    }

    // FB-F02: many long blinks through property blocks — instances cost less
    internal sealed class ManyBlockBlinksRule : FeedbackRule
    {
        internal const int Count = 8;

        internal override string Id => "FB-F02";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            var many = new List<int>();
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (MaterialTargets.RendererOf(c, i, out IMaterialPropertyItem m) == null || m.WriteMode != MaterialWriteMode.PropertyBlock
                    || !(c.Items[i] is TargetItem t) || t.m_Motion == null)
                    continue;
                MotionKind k = t.m_Motion.Kind;
                if ((k == MotionKind.Blink || k == MotionKind.Flash || k == MotionKind.Oscillate) && MaterialTargets.Long(t))
                    many.Add(i);
            }
            if (many.Count < Count)
                return;
            Component host = c.Host;
            string[] all = many.Select(x => c.ItemPath(x, "m_WriteMode")).ToArray();
            foreach (int i in many)
            {
                RuleResult r = Result(c, i, many.Count + " renderers blink through property blocks — material instances cost less");
                string one = c.ItemPath(i, "m_WriteMode");
                r.Fixes.Add(c.ItemFix("Write to material instances", one, x => x.enumValueIndex = (int)MaterialWriteMode.Instance));
                UnityEngine.Object owner = c.For(one);
                r.Fixes.Add(new RuleFix(c.Words("Write to material instances (all " + many.Count + ")", one), () =>
                {
                    foreach (string p in all)
                        RuleFixes.Write(owner, p, x => x.enumValueIndex = (int)MaterialWriteMode.Instance);
                }));
                into.Add(r);
            }
        }
    }

    // FB-F03: a sprite renderer's material color — a sprite shader may not show a block's color; Sprite Color writes the
    // renderer's own (no fix: the motions differ)
    internal sealed class SpriteColorRule : FeedbackRule
    {
        internal override string Id => "FB-F03";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is MaterialColorItem && MaterialTargets.RendererOf(c, i, out _) is SpriteRenderer)
                    into.Add(Result(c, i, "A sprite shader may not show a block's color — use Sprite Color"));
            }
        }
    }

    // FB-F04: the whole renderer (−1) — an index block of the host hides it, or the materials differ. A material without the
    // property (the instances skip it, a block does not show there) · the instances' whole and an index of one property share
    // a copy
    internal sealed class WholeRendererRule : FeedbackRule
    {
        internal override string Id => "FB-F04";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                Renderer r = MaterialTargets.RendererOf(c, i, out IMaterialPropertyItem m);
                if (r == null || m.MaterialIndex >= 0)
                    continue;
                bool block = m.WriteMode == MaterialWriteMode.PropertyBlock;
                Material source = MaterialTargets.Source(r, m);
                if (source == null)
                    continue;
                int id = MaterialTargets.PropertyId(source, m);
                if (id == -1)
                    continue;
                string why = null;
                for (int k = 0; k < c.Items.Count && why == null; k++)
                {
                    if (k == i || !(c.Items[k] is IMaterialPropertyItem o) || !c.Items[k].m_Active || o.MaterialIndex < 0 || o.WriteMode != m.WriteMode || c.Target(k) != r)
                        continue;
                    Material om = MaterialTargets.Source(r, o);
                    if (om != null && MaterialTargets.PropertyId(om, o) == id)
                        why = block ? "An index block of item " + k + " hides the whole block for its material" : "Item " + k + " writes the property at an index — it shares a copy with the whole renderer";
                }
                Material[] mats = r.sharedMaterials;
                if (why == null && block && MaterialTargets.TryValue(source, m, id, out Vector4 first))
                {
                    for (int k = 1; k < mats.Length && why == null; k++)
                    {
                        if (MaterialTargets.TryValue(mats[k], m, id, out Vector4 other) && !Same(other, first))
                            why = "The materials differ — the whole renderer starts from material 0";
                    }
                }
                for (int k = 1; k < mats.Length && why == null; k++)
                {
                    if (mats[k] != null && !MaterialTargets.TryValue(mats[k], m, id, out _))
                        why = "Material " + k + " has no such property — " + (block ? "the block does not show there" : "its instance is skipped");
                }
                if (why == null)
                    continue;
                RuleResult res = Result(c, i, why);
                Component host = c.Host;
                string path = c.ItemPath(i, "m_MaterialIndex");
                for (int k = 0; k < Mathf.Min(r.sharedMaterials.Length, MaterialTargets.MaxFixes); k++)
                {
                    int index = k;
                    res.Fixes.Add(c.ItemFix("Use index " + index, path, x => x.intValue = index));
                }
                into.Add(res);
            }
        }

        static bool Same(Vector4 a, Vector4 b) => Mathf.Abs(a.x - b.x) < 1e-4f && Mathf.Abs(a.y - b.y) < 1e-4f && Mathf.Abs(a.z - b.z) < 1e-4f && Mathf.Abs(a.w - b.w) < 1e-4f;
    }

    // FB-F05: two write modes on one renderer's property — the result depends on the apply order
    internal sealed class TwoModesRule : FeedbackRule
    {
        internal override string Id => "FB-F05";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                Renderer r = MaterialTargets.RendererOf(c, i, out IMaterialPropertyItem m);
                Material source = r != null ? MaterialTargets.Source(r, m) : null;
                if (source == null)
                    continue;
                int id = MaterialTargets.PropertyId(source, m);
                for (int k = 0; k < i; k++)
                {
                    if (!(c.Items[k] is IMaterialPropertyItem o) || !c.Items[k].m_Active || o.WriteMode == m.WriteMode || c.Target(k) != r)
                        continue;
                    bool overlap = o.MaterialIndex < 0 || m.MaterialIndex < 0 || o.MaterialIndex == m.MaterialIndex;
                    Material om = MaterialTargets.Source(r, o);
                    if (overlap && om != null && MaterialTargets.PropertyId(om, o) == id)
                    {
                        into.Add(Result(c, i, "Two write modes on one property (with item " + k + ") — the result depends on the apply order"));
                        break;
                    }
                }
            }
        }
    }

    // FB-F06: the property — no main color for an empty name (the prepare refuses), or no such property in the shader. Of
    // the item's type — a float takes Float and Range (an Integer: SetFloat logs and writes nothing), a vector a Vector, a
    // texture item a texture (a name ending in _ST is the texture's scale and offset — the texture's name), an emission a
    // color (an empty name _EmissionColor)
    internal sealed class ColorPropertyRule : FeedbackRule
    {
        internal override string Id => "FB-F06";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                Renderer r = MaterialTargets.RendererOf(c, i, out IMaterialPropertyItem m);
                Material source = r != null ? MaterialTargets.Source(r, m) : null;
                if (source == null || source.shader == null)
                    continue;
                Shader shader = source.shader;
                string texture = null;
                string message = m is MaterialColorItem ? ColorMessage(shader, m) : Message(shader, m, out texture);
                if (message == null)
                    continue;
                RuleResult res = Result(c, i, message);
                Component host = c.Host;
                string path = c.ItemPath(i, "m_PropertyName");
                int n = 0;
                if (texture != null)
                {
                    n++;
                    res.Fixes.Add(c.ItemFix("Use " + texture, path, x => x.stringValue = texture));
                }
                for (int k = 0; k < shader.GetPropertyCount() && n < MaterialTargets.MaxFixes; k++)
                {
                    if (!MaterialTargets.Takes(m, shader.GetPropertyType(k)))
                        continue;
                    string name = shader.GetPropertyName(k);
                    if (name == texture)
                        continue;
                    n++;
                    res.Fixes.Add(c.ItemFix("Use " + name, path, x => x.stringValue = name));
                }
                into.Add(res);
            }
        }

        static string ColorMessage(Shader shader, IMaterialPropertyItem m)
        {
            if (string.IsNullOrEmpty(m.PropertyName))
                return MaterialColorBinding.MainColor(shader, out _) ? null : "The shader has no main color — name a color property";
            return shader.FindPropertyIndex(m.PropertyName) < 0 ? "The shader has no property " + m.PropertyName + " — nothing shows" : null;
        }

        // the prepare refuses each of these — texture: a texture whose scale and offset the name is
        static string Message(Shader shader, IMaterialPropertyItem m, out string texture)
        {
            texture = null;
            string what = MaterialTargets.What(m);
            string name = m.PropertyName;
            if (string.IsNullOrEmpty(name))
            {
                if (m is MaterialFloatItem || m is MaterialVectorItem)
                    return "Name a " + what + " property — the item is skipped";
                name = m is EmissionItem ? "_EmissionColor" : TextureST.MainTexture(shader);
            }
            int at = shader.FindPropertyIndex(name);
            if (at < 0)
            {
                string stem = name.EndsWith("_ST") ? name.Substring(0, name.Length - 3) : null;
                int st = stem != null && what == "texture" ? shader.FindPropertyIndex(stem) : -1;
                if (st >= 0 && shader.GetPropertyType(st) == ShaderPropertyType.Texture)
                {
                    texture = stem;
                    return name + " is the scale and offset of " + stem + " — name the texture";
                }
                return "The shader has no property " + name + " — the item is skipped";
            }
            ShaderPropertyType type = shader.GetPropertyType(at);
            if (MaterialTargets.Takes(m, type))
                return null;
            return name + " is " + (type == ShaderPropertyType.Int ? "an Integer" : "a " + type) + ", not a " + what + " — the item is skipped";
        }
    }

    // FB-F07: materials the prepare refuses — none · an index out of range · an empty slot
    internal sealed class MaterialSetupRule : FeedbackRule
    {
        internal override string Id => "FB-F07";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                Renderer r = MaterialTargets.RendererOf(c, i, out IMaterialPropertyItem m);
                if (r == null)
                    continue;
                Material[] mats = r.sharedMaterials;
                string message = null;
                bool outOfRange = false;
                if (mats.Length == 0)
                    message = "The renderer has no material — the item is skipped";
                else if (m.MaterialIndex >= mats.Length)
                {
                    message = "Material index " + m.MaterialIndex + " is out of range (" + mats.Length + ") — the item is skipped";
                    outOfRange = true;
                }
                else if (m.MaterialIndex >= 0 && mats[m.MaterialIndex] == null)
                    message = "No material at index " + m.MaterialIndex + " — the item is skipped";
                else if (m.MaterialIndex < 0 && m.WriteMode == MaterialWriteMode.Instance && mats.Any(x => x == null))
                    message = "An empty material slot — the whole renderer's instances are skipped";
                if (message == null)
                    continue;
                RuleResult res = Result(c, i, message);
                if (outOfRange)
                {
                    Component host = c.Host;
                    string path = c.ItemPath(i, "m_MaterialIndex");
                    res.Fixes.Add(c.ItemFix("Use the whole renderer (−1 — changes what plays)", path, x => x.intValue = -1));
                }
                into.Add(res);
            }
        }
    }

    // FB-F08: an emission whose material has its emission off (the _EMISSION keyword) — nothing shows. The keyword is the
    // material's: a build keeps only the variants its materials use, so neither mode turns it on
    internal sealed class EmissionKeywordRule : FeedbackRule
    {
        internal const string Keyword = "_EMISSION";

        internal override string Id => "FB-F08";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                Renderer r = MaterialTargets.RendererOf(c, i, out IMaterialPropertyItem m);
                if (!(m is EmissionItem) || r == null || MaterialTargets.Source(r, m) == null)
                    continue;
                Material[] mats = r.sharedMaterials;
                int off = -1;
                for (int k = Mathf.Max(m.MaterialIndex, 0); k < (m.MaterialIndex < 0 ? mats.Length : m.MaterialIndex + 1) && off < 0; k++)
                {
                    if (Off(mats[k]))
                        off = k;
                }
                if (off >= 0)
                    into.Add(Result(c, i, "Material " + off + " has its emission off (" + Keyword + ") — nothing shows; turn it on in the material"));
            }
        }

        static bool Off(Material material)
        {
            if (material == null || material.shader == null)
                return false;
            LocalKeyword keyword = material.shader.keywordSpace.FindKeyword(Keyword);
            return keyword.isValid && !material.IsKeywordEnabled(keyword);
        }
    }

    // FB-F09: a Material Swap the prepare refuses — no material to swap in · no material on the renderer · an index out of
    // range
    internal sealed class SwapSetupRule : FeedbackRule
    {
        internal override string Id => "FB-F09";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is MaterialSwapItem m) || !m.m_Active || !(c.Target(i) is Renderer r))
                    continue;
                Material[] mats = r.sharedMaterials;
                bool outOfRange = false;
                string message = null;
                if (m.m_Material == null)
                    message = "No material to swap in — the item is skipped";
                else if (mats.Length == 0)
                    message = "The renderer has no material — the item is skipped";
                else if (m.m_MaterialIndex >= mats.Length)
                {
                    message = "Material index " + m.m_MaterialIndex + " is out of range (" + mats.Length + ") — the item is skipped";
                    outOfRange = true;
                }
                if (message == null)
                    continue;
                RuleResult res = Result(c, i, message);
                if (outOfRange)
                {
                    Component host = c.Host;
                    string path = c.ItemPath(i, "m_MaterialIndex");
                    res.Fixes.Add(c.ItemFix("Swap every index (−1 — changes what plays)", path, x => x.intValue = -1));
                }
                into.Add(res);
            }
        }
    }

    // FB-F11: a Material Float tween's value outside its Range property's limits — the shader takes it as it is (only the
    // inspector clamps a Range)
    internal sealed class FloatRangeRule : FeedbackRule
    {
        internal override string Id => "FB-F11";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                Renderer r = MaterialTargets.RendererOf(c, i, out IMaterialPropertyItem m);
                if (!(m is MaterialFloatItem f) || !(f.m_Motion is TweenMotion tween) || tween.m_Params.m_Mode == TweenMode.By || string.IsNullOrEmpty(f.m_PropertyName))
                    continue;
                Material source = r != null ? MaterialTargets.Source(r, m) : null;
                int at = source != null && source.shader != null ? source.shader.FindPropertyIndex(f.m_PropertyName) : -1;
                if (at < 0 || source.shader.GetPropertyType(at) != ShaderPropertyType.Range)
                    continue;
                Vector2 limits = source.shader.GetPropertyRangeLimits(at);
                TweenParams p = tween.m_Params;
                float outside = float.NaN;
                if (p.m_Mode != TweenMode.From && Outside(p.m_To.x, limits))
                    outside = p.m_To.x;
                else if (p.m_Mode != TweenMode.To && Outside(p.m_From.x, limits))
                    outside = p.m_From.x;
                if (!float.IsNaN(outside))
                    into.Add(Result(c, i, "Tween value " + outside + " is outside " + f.m_PropertyName + "'s range (" + limits.x + " ~ " + limits.y + ") — the shader takes it as it is"));
            }
        }

        static bool Outside(float v, Vector2 limits) => v < limits.x || v > limits.y;
    }
}
