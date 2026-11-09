using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // group P — the UI. uGUI is a package the editor can't name — its types are found by their names and read by reflection
    // (FB-H06's way)
    static class UGUITypes
    {
        internal static readonly Type Selectable = Type.GetType(UGUINames.Selectable);
        internal static readonly Type Text = Type.GetType(UGUINames.Text);
        internal static readonly PropertyInfo Transition = Selectable?.GetProperty("transition", BindingFlags.Public | BindingFlags.Instance);
        internal static readonly PropertyInfo TargetGraphic = Selectable?.GetProperty("targetGraphic", BindingFlags.Public | BindingFlags.Instance);
        internal static readonly PropertyInfo Font = Text?.GetProperty("font", BindingFlags.Public | BindingFlags.Instance);
        // TextMeshPro (the same package)
        internal static readonly Type TMPText = Type.GetType(UGUINames.TMPText);
        internal static readonly Type TextMeshPro = Type.GetType(UGUINames.TextMeshPro);
        internal static readonly PropertyInfo FontSharedMaterial = TMPText?.GetProperty("fontSharedMaterial", BindingFlags.Public | BindingFlags.Instance);
        internal static readonly PropertyInfo AutoSizing = TMPText?.GetProperty("enableAutoSizing", BindingFlags.Public | BindingFlags.Instance);

        internal static bool IsTMP(object target) => TMPText != null && target is Component c && c != null && TMPText.IsInstanceOfType(c);

        // the screen overlay's types (the UGUI assembly), an Image's fill, TMP's font
        internal static readonly Type Image = Type.GetType(UGUINames.Image);
        internal static readonly PropertyInfo ImageType = Image?.GetProperty("type", BindingFlags.Public | BindingFlags.Instance);
        internal static readonly PropertyInfo Sprite = Image?.GetProperty("sprite", BindingFlags.Public | BindingFlags.Instance);
        internal static readonly Type FloatingTextStyle = Type.GetType(UGUINames.FloatingTextStyle);
        internal static readonly Type ProgressBar = Type.GetType(UGUINames.ProgressBar);
        internal static readonly Type UITrigger = Type.GetType(UGUINames.UITrigger);
        internal static readonly Type PointerDownHandler = Type.GetType(UGUINames.PointerDownHandler);
        internal static readonly Type PointerClickHandler = Type.GetType(UGUINames.PointerClickHandler);
        internal static readonly MethodInfo ParseFormat = Type.GetType(UGUINames.NumberFormat)?.GetMethod("TryParse", BindingFlags.NonPublic | BindingFlags.Static);
        internal static readonly Type FontAsset = Type.GetType(UGUINames.TMPFontAsset);
        internal static readonly PropertyInfo Population = FontAsset?.GetProperty("atlasPopulationMode", BindingFlags.Public | BindingFlags.Instance);
        internal static readonly MethodInfo HasCharacter = FontAsset?.GetMethod("HasCharacter", new[] { typeof(int) });
        internal static readonly PropertyInfo TextFont = TMPText?.GetProperty("font", BindingFlags.Public | BindingFlags.Instance);

        internal static bool IsStyle(ScriptableObject style) => style != null && FloatingTextStyle != null && FloatingTextStyle.IsInstanceOfType(style);

        // the prepare's parse of a number format
        internal static bool Reads(string format, out string reason)
        {
            var args = new object[] { format, null, null };
            bool ok = (bool)ParseFormat.Invoke(null, args);
            reason = args[2] as string;
            return ok;
        }
    }

    // FB-P01: a Cross Fade on a Selectable's tinted graphic — the transition writes the same CanvasRenderer color
    internal sealed class CrossFadeTintRule : FeedbackRule
    {
        internal override string Id => "FB-P01";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (UGUITypes.Transition == null || UGUITypes.TargetGraphic == null)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is GraphicCrossFadeItem t) || !t.m_Active || !(c.Target(i) is Component graphic) || graphic == null)
                    continue;
                foreach (Component s in graphic.GetComponentsInParent(UGUITypes.Selectable, true))
                {
                    if (UGUITypes.Transition.GetValue(s).ToString() == "ColorTint" && ReferenceEquals(UGUITypes.TargetGraphic.GetValue(s), graphic))
                    {
                        into.Add(Result(c, i, "A Selectable tints this graphic — the cross fade and its transitions write over each other"));
                        break;
                    }
                }
            }
        }
    }

    // FB-P02: a font size motion on a dynamic font — each new size asks the atlas for its glyphs (a rebuild redraws every Text of
    // the font)
    internal sealed class DynamicFontSizeRule : FeedbackRule
    {
        internal override string Id => "FB-P02";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (UGUITypes.Font == null)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is TextFontSizeItem t) || !t.m_Active || !(c.Target(i) is Component text) || text == null || text.GetType() != UGUITypes.Text)
                    continue;
                if (UGUITypes.Font.GetValue(text) is Font font && font.dynamic)
                    into.Add(Result(c, i, "A dynamic font builds its atlas for each new size — the Texts of the font redraw"));
            }
        }
    }

    // FB-P03: a material swap and a texture offset · scale on one Graphic — the swap replaces the copy the offset · scale write
    // (both come back at the end)
    internal sealed class SwapOverCopyRule : FeedbackRule
    {
        internal override string Id => "FB-P03";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is GraphicTextureOffsetItem || c.Items[i] is GraphicTextureScaleItem) || !((TargetItem)c.Items[i]).m_Active
                    || !(c.Target(i) is Component graphic) || graphic == null)
                    continue;
                for (int j = 0; j < c.Items.Count; j++)
                {
                    if (c.Items[j] is GraphicMaterialItem swap && swap.m_Active && ReferenceEquals(c.Target(j), graphic))
                    {
                        into.Add(Result(c, i, "A material swap replaces the copy — the texture offset · scale do not show"));
                        break;
                    }
                }
            }
        }
    }

    // FB-P04: a UI texture offset · scale on a TextMeshPro text, a cross fade on a 3D one — the prepare skips them (the drawn
    // material is the font's shared one · a 3D text's CanvasRenderer would be added in a player)
    internal sealed class UIItemOnTMPRule : FeedbackRule
    {
        internal override string Id => "FB-P04";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is TargetItem t) || !t.m_Active)
                    continue;
                object target = c.Target(i);
                bool texture = (t is GraphicTextureOffsetItem || t is GraphicTextureScaleItem) && UGUITypes.IsTMP(target);
                bool fade = t is GraphicCrossFadeItem && UGUITypes.TextMeshPro != null && target is Component g && g != null && UGUITypes.TextMeshPro.IsInstanceOfType(g);
                if (texture || fade)
                    into.Add(Result(c, i, "A TextMeshPro text takes no UI texture offset · scale, a 3D one no cross fade — the item is skipped (the TMP/… items write its own copy)"));
            }
        }
    }

    // FB-P05: a UI material on a TextMeshPro text — written and given back, never drawn (TMP draws its font material)
    internal sealed class UIMaterialOnTMPRule : FeedbackRule
    {
        internal override string Id => "FB-P05";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is GraphicMaterialItem m && m.m_Active && UGUITypes.IsTMP(c.Target(i)))
                    into.Add(Result(c, i, "A UI material on a TextMeshPro text shows nothing — TMP draws its font material"));
            }
        }
    }

    // FB-P06: a font size motion on a text that sizes itself — TMP writes over it (FB-G08's form)
    internal sealed class TMPAutoSizeRule : FeedbackRule
    {
        internal override string Id => "FB-P06";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (UGUITypes.AutoSizing == null)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is TMPFontSizeItem t && t.m_Active && UGUITypes.IsTMP(c.Target(i)) && UGUITypes.AutoSizing.GetValue(c.Target(i)) is bool on && on)
                    into.Add(Result(c, i, "Auto size is on — TMP writes over the font size motion"));
            }
        }
    }

    // FB-P07: an outline · underlay motion on a font material whose shader keeps them behind a keyword that is off (the Mobile
    // shader's outline, every shader's underlay)
    internal sealed class TMPKeywordRule : FeedbackRule
    {
        internal override string Id => "FB-P07";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (UGUITypes.FontSharedMaterial == null)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is TargetItem t) || !t.m_Active || !UGUITypes.IsTMP(c.Target(i)))
                    continue;
                bool outline = t is TMPOutlineColorItem || t is TMPOutlineWidthItem;
                bool underlay = t is TMPUnderlayColorItem || t is TMPUnderlayOffsetItem;
                if (!outline && !underlay || !(UGUITypes.FontSharedMaterial.GetValue(c.Target(i)) is Material m) || m == null || m.shader == null)
                    continue;
                if (outline && Off(m, "OUTLINE_ON", null))
                    into.Add(Result(c, i, "The font material's outline is off (OUTLINE_ON) — the outline motion does not show"));
                else if (underlay && Off(m, "UNDERLAY_ON", "UNDERLAY_INNER"))
                    into.Add(Result(c, i, "The font material has no underlay (UNDERLAY_ON) — the underlay motion does not show"));
            }
        }

        // the shader declares the keyword and the material has it (or its other form) off
        static bool Off(Material m, string keyword, string other)
            => m.shader.keywordSpace.FindKeyword(keyword).isValid && !m.IsKeywordEnabled(keyword) && (other == null || !m.IsKeywordEnabled(other));
    }

    // FB-P08: a floating text's style that is not a Floating Text Style — the prepare skips the item (the field takes any
    // ScriptableObject: the style's type lives in the UGUI assembly)
    internal sealed class FloatingTextStyleRule : FeedbackRule
    {
        internal override string Id => "FB-P08";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (UGUITypes.FloatingTextStyle == null)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is FloatingTextItem t && t.m_Active && t.m_Style != null && !UGUITypes.IsStyle(t.m_Style))
                    into.Add(Result(c, i, "The style is not a Floating Text Style — the item is skipped"));
            }
        }
    }

    // FB-P09: a floating text on a dynamic font, or with characters its static font does not hold — the atlas's growth or a
    // fallback font's first characters allocate at a play
    internal sealed class FloatingTextFontRule : FeedbackRule
    {
        internal override string Id => "FB-P09";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (UGUITypes.Population == null || UGUITypes.HasCharacter == null || UGUITypes.ParseFormat == null)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is FloatingTextItem t) || !t.m_Active || t.m_Style != null && !UGUITypes.IsStyle(t.m_Style))
                    continue;
                Object font = FontOf(t.m_Style);
                string chars = CharactersOf(t);
                if (font == null || chars == null)
                    continue;
                if (UGUITypes.Population.GetValue(font).ToString() != "Static")
                {
                    into.Add(Result(c, i, "The font " + font.name + " is dynamic — a new character grows its atlas at a play (a static font with the characters keeps it 0)"));
                    continue;
                }
                string missing = Missing(font, chars);
                if (missing.Length > 0)
                    into.Add(Result(c, i, "The font " + font.name + " has no '" + missing + "' — a fallback font draws them, its first characters allocate at a play"));
            }
        }

        // the style's text font: its prefab's, its own, else TMP's default (read from the settings asset — no importer window)
        static Object FontOf(ScriptableObject style)
        {
            if (style != null)
            {
                using (var so = new SerializedObject(style))
                {
                    if (so.FindProperty("m_Prefab").objectReferenceValue is GameObject prefab)
                    {
                        Component text = UGUITypes.TMPText != null ? prefab.GetComponentInChildren(UGUITypes.TMPText, true) : null;
                        return text != null ? UGUITypes.TextFont?.GetValue(text) as Object : null;
                    }
                    Object own = so.FindProperty("m_Font").objectReferenceValue;
                    if (own != null)
                        return own;
                }
            }
            Object settings = Resources.Load("TMP Settings");
            if (settings == null)
                return null;
            using (var so = new SerializedObject(settings))
                return so.FindProperty("m_defaultFontAsset")?.objectReferenceValue;
        }

        // what the item writes: its text, or the digits · sign · marks · words of its number format (null: P11's)
        static string CharactersOf(FloatingTextItem t)
        {
            if (t.m_Source == FloatingTextSource.Text)
                return t.m_Text ?? "";
            string format = t.m_Format ?? "";
            if (!UGUITypes.Reads(format, out _))
                return null;
            int open = format.IndexOf("{0", StringComparison.Ordinal);
            int close = format.IndexOf('}', open);
            string inner = format.Substring(open + 2, close - open - 2);
            return "0123456789-" + (inner.IndexOf('.') >= 0 ? "." : "") + (inner.IndexOf(',') >= 0 ? "," : "") + format.Substring(0, open) + format.Substring(close + 1);
        }

        static string Missing(Object font, string chars)
        {
            var seen = new HashSet<int>();
            var missing = new StringBuilder();
            for (int i = 0; i < chars.Length; i++)
            {
                int code = char.IsSurrogatePair(chars, i) ? char.ConvertToUtf32(chars[i], chars[++i]) : chars[i];
                if (char.IsWhiteSpace((char)code) || !seen.Add(code))
                    continue;
                if (!(bool)UGUITypes.HasCharacter.Invoke(font, new object[] { code }))
                    missing.Append(char.ConvertFromUtf32(code));
            }
            return missing.ToString();
        }
    }

    // FB-P10: a progress bar whose bars do not fit its fill — Fill Amount on a bar that is not a Filled Image with a sprite, a
    // scale · size fill whose pivot is not at the side the fill starts from, a size fill across its side
    internal sealed class ProgressBarFitRule : FeedbackRule
    {
        const int LocalScale = 0, FillAmount = 1, Width = 2, Height = 3;

        internal override string Id => "FB-P10";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => UGUITypes.ProgressBar != null && host != null && UGUITypes.ProgressBar.IsInstanceOfType(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            SerializedObject so = c.HostSerialized;
            int fill = so.FindProperty("m_Fill").intValue;
            var side = (ScreenSide)so.FindProperty("m_From").intValue;
            Bar(c, into, so.FindProperty("m_Foreground").objectReferenceValue as RectTransform, "The foreground", fill, side);
            Bar(c, into, so.FindProperty("m_DelayedDecrease").objectReferenceValue as RectTransform, "The delayed decrease bar", fill, side);
            Bar(c, into, so.FindProperty("m_DelayedIncrease").objectReferenceValue as RectTransform, "The delayed increase bar", fill, side);
        }

        void Bar(RuleContext c, List<RuleResult> into, RectTransform bar, string name, int fill, ScreenSide side)
        {
            if (bar == null)
                return;
            if (fill == FillAmount)
            {
                Component image = UGUITypes.Image != null ? bar.GetComponent(UGUITypes.Image) : null;
                if (image == null || UGUITypes.ImageType == null || UGUITypes.ImageType.GetValue(image).ToString() != "Filled" || !(UGUITypes.Sprite?.GetValue(image) is Object sprite) || sprite == null)
                    into.Add(Result(c.Host, name + " is not a Filled Image with a sprite — Fill Amount does not show"));
                return;
            }
            if (fill != LocalScale && fill != Width && fill != Height)
                return;
            bool horizontal = side == ScreenSide.Left || side == ScreenSide.Right;
            if (fill == Width && !horizontal || fill == Height && horizontal)
            {
                into.Add(Result(c.Host, (fill == Width ? "Width fills along x" : "Height fills along y") + " — the side " + side + " is across it"));
                return;
            }
            float pivot = horizontal ? bar.pivot.x : bar.pivot.y;
            float start = side == ScreenSide.Left || side == ScreenSide.Bottom ? 0f : 1f;
            if (Mathf.Abs(pivot - start) > 1e-4f)
                into.Add(Result(c.Host, name + "'s pivot is not at its " + side + " side — the fill does not start there"));
        }
    }

    // FB-P11: a floating text's number format that does not read — the prepare skips the item (TMP's {0:00.00} form)
    internal sealed class FloatingTextFormatRule : FeedbackRule
    {
        internal override string Id => "FB-P11";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (UGUITypes.ParseFormat == null)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is FloatingTextItem t && t.m_Active && t.m_Source == FloatingTextSource.Intensity && !UGUITypes.Reads(t.m_Format, out string reason))
                    into.Add(Result(c, i, "The number format does not read (" + reason + ") — the item is skipped"));
            }
        }
    }

    // FB-P12: a UI trigger below an object that takes presses or clicks — uGUI sends a press and a click to the first object up
    // the hierarchy that handles them, so the trigger takes them from that object (a Button loses its press, its selection and
    // its On Click). A disabled handler does not count. The message names the nearest such object
    internal sealed class UITriggerPlacementRule : FeedbackRule
    {
        internal override string Id => "FB-P12";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => UGUITypes.UITrigger != null && host != null && UGUITypes.UITrigger.IsInstanceOfType(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            GameObject pressable = Pressable(c.Host.transform.parent);
            if (pressable != null)
                into.Add(Result(c.Host, "This trigger is below " + pressable.name + ", which takes presses and clicks — the trigger takes them from it; put the trigger on " + pressable.name));
        }

        // the nearest object up the hierarchy with an enabled press or click handler
        internal static GameObject Pressable(Transform from)
        {
            for (Transform t = from; t != null; t = t.parent)
            {
                foreach (Component component in t.GetComponents<Component>())
                {
                    if (component == null || component is Behaviour b && !b.enabled)
                        continue;
                    Type type = component.GetType();
                    if (UGUITypes.PointerDownHandler != null && UGUITypes.PointerDownHandler.IsAssignableFrom(type)
                        || UGUITypes.PointerClickHandler != null && UGUITypes.PointerClickHandler.IsAssignableFrom(type))
                        return t.gameObject;
                }
            }
            return null;
        }
    }

    // FB-P13: a UI trigger row with no player on an object without a Feedback Player — the row does nothing
    internal sealed class UITriggerPlayerRule : FeedbackRule
    {
        internal override string Id => "FB-P13";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => UGUITypes.UITrigger != null && host != null && UGUITypes.UITrigger.IsInstanceOfType(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (c.Host.GetComponent<FeedbackPlayer>() != null)
                return;
            SerializedProperty entries = c.HostSerialized.FindProperty("m_Entries");
            int empty = 0;
            for (int i = 0; i < entries.arraySize; i++)
            {
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("m_Player").objectReferenceValue == null)
                    empty++;
            }
            if (empty > 0)
                into.Add(Result(c.Host, (empty == 1 ? "A row has" : empty + " rows have") + " no player and there is no Feedback Player on this object — " + (empty == 1 ? "it does" : "they do") + " nothing"));
        }
    }
}
