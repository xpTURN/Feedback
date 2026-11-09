using System;
using TMPro;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a TextMeshPro text's color — UI/Color · UI/Alpha's slot on a TMP text. The rows are the color's components only: the
    // 32-bit copy (m_fontColor32) has no animation binding and is computed again from them
    [RecordPaths(typeof(TMP_Text), "m_fontColor.r", "m_fontColor.g", "m_fontColor.b", "m_fontColor.a")]
    internal sealed class TMPColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly TMP_Text m_Target;

        internal TMPColorSlot(TMP_Text target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override Color Read() => m_Target.color;

        protected override void Write(in Color value) => m_Target.color = value;
    }

    // the font size — the setter writes the base size too while auto size is off (with it on, TMP writes over ours)
    [RecordPaths(typeof(TMP_Text), "m_fontSize", "m_fontSizeBase")]
    internal sealed class TMPFontSizeSlot : ValueSlot<float, FloatOps>
    {
        readonly TMP_Text m_Target;

        internal TMPFontSizeSlot(TMP_Text target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.fontSize;

        protected override void Write(in float value) => m_Target.fontSize = value;
    }

    [RecordPaths(typeof(TMP_Text), "m_characterSpacing")]
    internal sealed class TMPCharacterSpacingSlot : ValueSlot<float, FloatOps>
    {
        readonly TMP_Text m_Target;

        internal TMPCharacterSpacingSlot(TMP_Text target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.characterSpacing;

        protected override void Write(in float value) => m_Target.characterSpacing = value;
    }

    [RecordPaths(typeof(TMP_Text), "m_wordSpacing")]
    internal sealed class TMPWordSpacingSlot : ValueSlot<float, FloatOps>
    {
        readonly TMP_Text m_Target;

        internal TMPWordSpacingSlot(TMP_Text target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.wordSpacing;

        protected override void Write(in float value) => m_Target.wordSpacing = value;
    }

    [RecordPaths(typeof(TMP_Text), "m_lineSpacing")]
    internal sealed class TMPLineSpacingSlot : ValueSlot<float, FloatOps>
    {
        readonly TMP_Text m_Target;

        internal TMPLineSpacingSlot(TMP_Text target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.lineSpacing;

        protected override void Write(in float value) => m_Target.lineSpacing = value;
    }

    [RecordPaths(typeof(TMP_Text), "m_paragraphSpacing")]
    internal sealed class TMPParagraphSpacingSlot : ValueSlot<float, FloatOps>
    {
        readonly TMP_Text m_Target;

        internal TMPParagraphSpacingSlot(TMP_Text target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.paragraphSpacing;

        protected override void Write(in float value) => m_Target.paragraphSpacing = value;
    }

    // one text's copy of its font material — made when the first property slot asks, put in through fontSharedMaterial once
    // (TMP draws m_sharedMaterial — the Graphic's material is not drawn) and kept to the end. Not fontMaterial (its getter makes an
    // instance TMP never destroys). The drop puts the original back where our copy still is (the setter dirties the material — a 3D
    // text's renderer follows) and destroys the copy
    [RecordPaths(typeof(TMP_Text), "m_sharedMaterial")]
    internal sealed class TMPCopySlot : LayerSlot
    {
        readonly TMP_Text m_Text;
        readonly CanvasRenderer m_Renderer;
        readonly Material m_Original;
        Material m_Copy;
        string m_Name;
        Material m_Seen;
        bool m_SeenOurs;
        bool m_Padded;
        bool m_Freed;

        internal TMPCopySlot(TMP_Text text)
            : base(ConsumptionStage.LateApply)
        {
            m_Text = text;
            // a UGUI text's CanvasRenderer, read at the prepare (a component's first read allocates); a 3D text has none — its
            // getter would add one in a player
            m_Renderer = text is TextMeshProUGUI ? text.canvasRenderer : null;
            m_Original = text.fontSharedMaterial;
        }

        internal override void Attached() => RecordOnce();

        internal Material Copy => m_Copy;

        internal bool Live => !m_Freed;

        // the padding measured once, at the prepare, for every padding property at its range's end (a measure allocates —
        // GetPadding reads the keywords); the values go back and their ratios are measured again. A play then never clips and
        // never measures; the quads stay the widest while the copy is in
        internal void PadToTheEnd()
        {
            if (m_Padded || m_Copy == null)
                return;
            m_Padded = true;
            Material m = m_Copy;
            float dilate = End(m, TMPShader.FaceDilate), width = End(m, TMPShader.OutlineWidth), softness = End(m, TMPShader.OutlineSoftness);
            float x = End(m, TMPShader.UnderlayOffsetX), y = End(m, TMPShader.UnderlayOffsetY);
            m_Text.UpdateMeshPadding();
            Back(m, TMPShader.FaceDilate, dilate);
            Back(m, TMPShader.OutlineWidth, width);
            Back(m, TMPShader.OutlineSoftness, softness);
            Back(m, TMPShader.UnderlayOffsetX, x);
            Back(m, TMPShader.UnderlayOffsetY, y);
            ShaderUtilities.UpdateShaderRatios(m);
            m_Text.SetVerticesDirty();
        }

        static float End(Material m, int id)
        {
            if (!m.HasProperty(id))
                return 0f;
            float v = m.GetFloat(id);
            m.SetFloat(id, 1f);
            return v;
        }

        static void Back(Material m, int id, float v)
        {
            if (m.HasProperty(id))
                m.SetFloat(id, v);
        }

        internal void Ensure()
        {
            if (m_Copy != null || m_Freed || m_Original == null)
                return;
            bool preview = Registry != null && Registry.IsPreview;
            m_Copy = new Material(m_Original) { name = (preview ? PropertyLayerRegistry.PreviewPrefix : "") + m_Original.name + " (Feedback)" };
            m_Name = m_Copy.name;
            if (preview)
                m_Copy.hideFlags = HideFlags.HideAndDontSave;
            m_Text.fontSharedMaterial = m_Copy;
        }

        // the material drawn when it is a copy of ours — a Mask's stencil copy (TMP names it after ours). Until the canvas updates,
        // the CanvasRenderer still draws the font's shared material — not ours; the verdict is kept for the material seen
        internal Material Drawn
        {
            get
            {
                CanvasRenderer cr = m_Renderer;
                if (cr == null || cr.materialCount == 0)
                    return null;
                Material drawn = cr.GetMaterial(0);
                if (drawn == null || ReferenceEquals(drawn, m_Copy))
                    return null;
                if (!ReferenceEquals(drawn, m_Seen))
                {
                    m_Seen = drawn;
                    m_SeenOurs = m_Name != null && drawn.name.Contains(m_Name);
                }
                return m_SeenOurs ? drawn : null;
            }
        }

        internal override bool IsAlive => m_Text != null;

        internal override bool Pending => false;

        internal override int LiveContributions => 0;

        internal override int Reserved => 0;

        internal override Vector4 BaseVector => Vector4.zero;

        internal override void Revert()
        {
        }

        internal override void Apply()
        {
        }

        internal override void Close()
        {
        }

        internal override void EndAll()
        {
        }

        internal override void Unreserve(int count)
        {
        }

        internal override void Dropped()
        {
            if (m_Freed)
                return;
            m_Freed = true;
            if (m_Text != null && m_Copy != null && ReferenceEquals(m_Text.fontSharedMaterial, m_Copy))
                m_Text.fontSharedMaterial = m_Original;
            if (m_Copy == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(m_Copy);
            else
                UnityEngine.Object.DestroyImmediate(m_Copy);
        }
    }

    // the copy's property ids (not TMP's ShaderUtilities — its ids fill in a static constructor on one of the two versions)
    static class TMPShader
    {
        internal static readonly int FaceDilate = Shader.PropertyToID("_FaceDilate");
        internal static readonly int OutlineSoftness = Shader.PropertyToID("_OutlineSoftness");
        internal static readonly int OutlineWidth = Shader.PropertyToID("_OutlineWidth");
        internal static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");
        internal static readonly int UnderlayColor = Shader.PropertyToID("_UnderlayColor");
        internal static readonly int UnderlayOffsetX = Shader.PropertyToID("_UnderlayOffsetX");
        internal static readonly int UnderlayOffsetY = Shader.PropertyToID("_UnderlayOffsetY");

    }

    // a float of the copy — and of the material drawn (a Mask's stencil copy)
    [NotRecorded("the copy slot records the reference")]
    internal sealed class TMPMaterialFloatSlot : ValueSlot<float, FloatOps>
    {
        readonly TMP_Text m_Text;
        readonly int m_Id;
        readonly bool m_Pads;
        TMPCopySlot m_Copies;
        bool m_Let;

        internal TMPMaterialFloatSlot(TMP_Text text, int id, bool pads)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Text = text;
            m_Id = id;
            m_Pads = pads;
        }

        internal override void Attached()
        {
            TMP_Text text = m_Text;
            m_Copies = Registry.GetOrCreate(text, 0, -1, () => new TMPCopySlot(text));
            m_Copies.Ensure();
            if (m_Pads)
                m_Copies.PadToTheEnd();
        }

        internal override bool IsAlive => m_Text != null && m_Copies != null && m_Copies.Live && m_Copies.Copy != null;

        protected override float Read() => m_Copies.Copy.GetFloat(m_Id);

        protected override void Write(in float value)
        {
            m_Copies.Copy.SetFloat(m_Id, value);
            Material drawn = m_Copies.Drawn;
            if (drawn != null)
                drawn.SetFloat(m_Id, value);
        }

        internal override void Dropped()
        {
            if (m_Let)
                return;
            m_Let = true;
            if (m_Copies != null && Registry != null && !Registry.IsClosed)
                Registry.Release(m_Copies, 0);
        }
    }

    [NotRecorded("the copy slot records the reference")]
    internal sealed class TMPMaterialColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly TMP_Text m_Text;
        readonly int m_Id;
        TMPCopySlot m_Copies;
        bool m_Let;

        internal TMPMaterialColorSlot(TMP_Text text, int id)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Text = text;
            m_Id = id;
        }

        internal override void Attached()
        {
            TMP_Text text = m_Text;
            m_Copies = Registry.GetOrCreate(text, 0, -1, () => new TMPCopySlot(text));
            m_Copies.Ensure();
        }

        internal override bool IsAlive => m_Text != null && m_Copies != null && m_Copies.Live && m_Copies.Copy != null;

        protected override Color Read() => m_Copies.Copy.GetColor(m_Id);

        protected override void Write(in Color value)
        {
            m_Copies.Copy.SetColor(m_Id, value);
            Material drawn = m_Copies.Drawn;
            if (drawn != null)
                drawn.SetColor(m_Id, value);
        }

        internal override void Dropped()
        {
            if (m_Let)
                return;
            m_Let = true;
            if (m_Copies != null && Registry != null && !Registry.IsClosed)
                Registry.Release(m_Copies, 0);
        }
    }

    // the underlay's offset (x · y — two floats of the shader)
    [NotRecorded("the copy slot records the reference")]
    internal sealed class TMPUnderlayOffsetSlot : ValueSlot<Vector2, Vector2Ops>
    {
        readonly TMP_Text m_Text;
        TMPCopySlot m_Copies;
        bool m_Let;

        internal TMPUnderlayOffsetSlot(TMP_Text text)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Text = text;
        }

        internal override void Attached()
        {
            TMP_Text text = m_Text;
            m_Copies = Registry.GetOrCreate(text, 0, -1, () => new TMPCopySlot(text));
            m_Copies.Ensure();
            m_Copies.PadToTheEnd();
        }

        internal override bool IsAlive => m_Text != null && m_Copies != null && m_Copies.Live && m_Copies.Copy != null;

        protected override Vector2 Read()
        {
            Material m = m_Copies.Copy;
            return new Vector2(m.GetFloat(TMPShader.UnderlayOffsetX), m.GetFloat(TMPShader.UnderlayOffsetY));
        }

        protected override void Write(in Vector2 value)
        {
            Put(m_Copies.Copy, value);
            Put(m_Copies.Drawn, value);
        }

        static void Put(Material m, in Vector2 value)
        {
            if (m == null)
                return;
            m.SetFloat(TMPShader.UnderlayOffsetX, value.x);
            m.SetFloat(TMPShader.UnderlayOffsetY, value.y);
        }

        internal override void Dropped()
        {
            if (m_Let)
                return;
            m_Let = true;
            if (m_Copies != null && Registry != null && !Registry.IsClosed)
                Registry.Release(m_Copies, 0);
        }
    }

    // the text — one slot, two doors: a string (Set Text · the revert — through the setter, which drops TMP's SetText
    // source; the serialized path alone would leave it) and characters (Count To — SetText(char[]) makes no string in a
    // player). The animation can't bind a string
    [NotRecorded("a string — the preview keeps the path")]
    [PreviewKeeps(PreviewKeep.SerializedPath, "m_text")]
    internal sealed class TMPTextSlot : StateSlot<string>
    {
        readonly TMP_Text m_Target;

        internal TMPTextSlot(TMP_Text target) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        // a player's getter makes the string after a SetText and keeps it nowhere — read once a play
        internal override string ReadState() => m_Target.text;

        protected override void Put(string value) => m_Target.text = value;

        internal void WriteChars(char[] chars, int length)
        {
            RecordOnce();
            m_Target.SetText(chars, 0, length);
        }
    }

    // TMP's {0:00.00} form — a prefix, a pattern of 0 · # · , · . and a suffix — read once at the prepare; a value is
    // written into a char buffer with no string made. The digits are the value's 15 significant ones (a decimal) rounded half
    // away from zero
    internal sealed class TMPNumberFormat
    {
        readonly string m_Prefix;
        readonly string m_Suffix;
        readonly int m_MinInt;
        readonly int m_MinFrac;
        readonly int m_MaxFrac;
        readonly bool m_Groups;

        TMPNumberFormat(string prefix, string suffix, int minInt, int minFrac, int maxFrac, bool groups)
        {
            m_Prefix = prefix;
            m_Suffix = suffix;
            m_MinInt = minInt;
            m_MinFrac = minFrac;
            m_MaxFrac = maxFrac;
            m_Groups = groups;
        }

        // sign · 29 digits · their group marks · the point · the fraction
        internal int Capacity => m_Prefix.Length + m_Suffix.Length + 1 + 29 + 9 + 1 + m_MaxFrac;

        internal static bool TryParse(string format, out TMPNumberFormat result, out string reason)
        {
            result = null;
            reason = null;
            format = format ?? "";
            int open = format.IndexOf("{0", StringComparison.Ordinal);
            int close = open < 0 ? -1 : format.IndexOf('}', open);
            if (open < 0 || close < 0)
            {
                reason = "the format has no {0}";
                return false;
            }
            string inner = format.Substring(open + 2, close - open - 2);
            string pattern = inner.Length == 0 ? "0" : inner[0] == ':' ? inner.Substring(1) : null;
            if (string.IsNullOrEmpty(pattern))
            {
                reason = "the format's {0…} is not {0} or {0:pattern}";
                return false;
            }
            int point = pattern.IndexOf('.');
            string whole = point < 0 ? pattern : pattern.Substring(0, point);
            string fraction = point < 0 ? "" : pattern.Substring(point + 1);
            int minInt = 0, minFrac = 0, maxFrac = 0;
            bool groups = false;
            foreach (char ch in whole)
            {
                if (ch == '0')
                    minInt++;
                else if (ch == ',')
                    groups = true;
                else if (ch != '#')
                {
                    reason = "the pattern takes 0 # , . only";
                    return false;
                }
            }
            foreach (char ch in fraction)
            {
                if (ch == '0')
                {
                    if (maxFrac > minFrac)
                    {
                        reason = "a 0 after a # in the fraction";
                        return false;
                    }
                    minFrac++;
                    maxFrac++;
                }
                else if (ch == '#')
                    maxFrac++;
                else
                {
                    reason = "the pattern takes 0 # , . only";
                    return false;
                }
            }
            if (maxFrac > 10)
            {
                reason = "more than 10 decimals";
                return false;
            }
            result = new TMPNumberFormat(format.Substring(0, open), format.Substring(close + 1), minInt, minFrac, maxFrac, groups);
            return true;
        }

        static readonly decimal s_Limit = 7.9e27m;

        // the length written
        internal int Write(double value, char[] into)
        {
            int n = 0;
            for (int i = 0; i < m_Prefix.Length; i++)
                into[n++] = m_Prefix[i];
            decimal d;
            if (double.IsNaN(value))
                d = 0m;
            else if (value >= 7.9e27)
                d = s_Limit;
            else if (value <= -7.9e27)
                d = -s_Limit;
            else
                d = (decimal)value;
            bool negative = d < 0m;
            decimal a = Math.Round(negative ? -d : d, m_MaxFrac, MidpointRounding.AwayFromZero);
            if (negative && a != 0m)
                into[n++] = '-';
            decimal whole = decimal.Truncate(a);
            decimal fraction = a - whole;
            // the whole part's digits, last first, then turned
            int start = n, digits = 0;
            while (whole > 0m || digits < m_MinInt)
            {
                if (m_Groups && digits > 0 && digits % 3 == 0)
                    into[n++] = ',';
                decimal next = decimal.Truncate(whole / 10m);
                into[n++] = (char)('0' + (int)(whole - next * 10m));
                whole = next;
                digits++;
            }
            for (int i = start, j = n - 1; i < j; i++, j--)
            {
                char t = into[i];
                into[i] = into[j];
                into[j] = t;
            }
            // the fraction's digits — the optional ones (#) without their trailing zeros
            int point = n;
            into[n++] = '.';
            int last = n;
            for (int i = 0; i < m_MaxFrac; i++)
            {
                fraction *= 10m;
                decimal digit = decimal.Truncate(fraction);
                fraction -= digit;
                into[n++] = (char)('0' + (int)digit);
                if (i < m_MinFrac || digit != 0m)
                    last = n;
            }
            n = last == point + 1 ? point : last;
            for (int i = 0; i < m_Suffix.Length; i++)
                into[n++] = m_Suffix[i];
            return n;
        }
    }

    // Count To's progress (Sprite Sheet's slot form — the base is a mark, not a value): a progress writes the number between
    // the item's two values into the text through the text slot's char door; the mark puts back the text the play found (read
    // once — a player's getter after a SetText makes a string and keeps it nowhere)
    [NotRecorded("the text slot keeps the text")]
    internal sealed class TMPCountToSlot : ValueSlot<float, FloatOps>
    {
        internal const float Original = -1e9f;
        readonly TMP_Text m_Target;
        readonly double m_From;
        readonly double m_To;
        readonly TMPNumberFormat m_Format;
        readonly char[] m_Chars;
        readonly char[] m_Shown;
        TMPTextSlot m_Text;
        string m_First;
        int m_ShownLength = -1;
        float m_Place;
        bool m_Showing;
        bool m_Let;

        internal TMPCountToSlot(TMP_Text target, double from, double to, TMPNumberFormat format)
            : base(ConsumptionStage.Update, Composition.Additive, false)
        {
            m_Target = target;
            m_From = from;
            m_To = to;
            m_Format = format;
            m_Chars = new char[format.Capacity];
            m_Shown = new char[format.Capacity];
        }

        internal override void Attached()
        {
            TMP_Text target = m_Target;
            m_Text = Registry.GetOrCreate(target, 0, 0, () => new TMPTextSlot(target));
        }

        internal override bool IsAlive => m_Target != null && m_Text != null && !m_Text.Detached;

        protected override float Read() => m_Showing ? m_Place : Original;

        protected override void Write(in float value)
        {
            if (value == Original)
            {
                if (m_Showing)
                    m_Text.WriteState(m_First);
                m_Showing = false;
                m_ShownLength = -1;
                return;
            }
            if (!m_Showing)
            {
                m_First = m_Text.ReadState();
                m_Showing = true;
            }
            m_Place = value;
            double v = value >= 1f ? m_To : value <= 0f ? m_From : m_From + (m_To - m_From) * value;
            int length = m_Format.Write(v, m_Chars);
            if (length == m_ShownLength && Same(length))
                return;
            Array.Copy(m_Chars, m_Shown, length);
            m_ShownLength = length;
            m_Text.WriteChars(m_Chars, length);
        }

        bool Same(int length)
        {
            for (int i = 0; i < length; i++)
            {
                if (m_Chars[i] != m_Shown[i])
                    return false;
            }
            return true;
        }

        internal override void Dropped()
        {
            if (m_Let)
                return;
            m_Let = true;
            if (m_Text != null && Registry != null && !Registry.IsClosed)
                Registry.Release(m_Text, 0);
        }
    }

    // Reveal's progress over the characters · words · lines shown — not serialized: the preview keeps them by their kind (its
    // stop runs no slot's revert); a missed reload gives TMP's default, all. The counts are the last mesh's — a change of the
    // text follows a frame later
    [NotRecorded("not serialized — the preview keeps them by their kind")]
    [PreviewKeeps(PreviewKeep.RuntimeProperties, "maxVisibleCharacters maxVisibleWords maxVisibleLines")]
    internal sealed class TMPRevealSlot : ValueSlot<float, FloatOps>
    {
        internal const float Original = -1e9f;
        internal const int All = 99999;
        readonly TMP_Text m_Target;
        readonly TMPRevealUnit m_Unit;
        int m_First;
        float m_Place;
        bool m_Showing;

        internal TMPRevealSlot(TMP_Text target, TMPRevealUnit unit)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
            m_Unit = unit;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Showing ? m_Place : Original;

        protected override void Write(in float value)
        {
            if (value == Original)
            {
                if (m_Showing)
                    Put(m_First);
                m_Showing = false;
                return;
            }
            if (!m_Showing)
            {
                m_First = Get();
                m_Showing = true;
            }
            m_Place = value;
            TMP_TextInfo info = m_Target.textInfo;
            int total = info == null ? 0 : m_Unit == TMPRevealUnit.Word ? info.wordCount : m_Unit == TMPRevealUnit.Line ? info.lineCount : info.characterCount;
            Put(value >= 1f ? All : value <= 0f ? 0 : (int)(value * total));
        }

        int Get() => m_Unit == TMPRevealUnit.Word ? m_Target.maxVisibleWords : m_Unit == TMPRevealUnit.Line ? m_Target.maxVisibleLines : m_Target.maxVisibleCharacters;

        void Put(int count)
        {
            if (m_Unit == TMPRevealUnit.Word)
                m_Target.maxVisibleWords = count;
            else if (m_Unit == TMPRevealUnit.Line)
                m_Target.maxVisibleLines = count;
            else
                m_Target.maxVisibleCharacters = count;
        }
    }
}
