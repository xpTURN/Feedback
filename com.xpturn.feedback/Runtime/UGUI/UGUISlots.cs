using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace xpTURN.Feedback
{
    // a Graphic's vertex color — not clamped, read back bit for bit. Graphic Alpha shares it. A write rebuilds the Graphic's
    // mesh at the canvas update (the setter drops an equal color)
    [RecordPaths(typeof(Graphic), "m_Color.r", "m_Color.g", "m_Color.b", "m_Color.a")]
    internal sealed class GraphicColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly Graphic m_Target;

        internal GraphicColorSlot(Graphic target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override Color Read() => m_Target.color;

        protected override void Write(in Color value) => m_Target.color = value;
    }

    // the CanvasRenderer's color — runtime state (not serialized): kept for the preview by its kind. A Selectable's tint
    // writes the same color (another's write — taken as the game's)
    [NotRecorded("not serialized — the preview keeps it by its kind")]
    [PreviewKeeps(PreviewKeep.CanvasRendererColor)]
    internal sealed class CanvasRendererColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly CanvasRenderer m_Target;

        internal CanvasRendererColorSlot(CanvasRenderer target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override Color Read() => m_Target.GetColor();

        protected override void Write(in Color value) => m_Target.SetColor(value);
    }

    // the toggles — written when the value changes
    [RecordPaths(typeof(Graphic), "m_RaycastTarget")]
    internal sealed class GraphicRaycastTargetSlot : ToggleSlot
    {
        readonly Graphic m_Target;

        internal GraphicRaycastTargetSlot(Graphic target) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        protected override bool Read() => m_Target.raycastTarget;

        protected override void Write(bool value) => m_Target.raycastTarget = value;
    }

    [RecordPaths(typeof(CanvasGroup), "m_Interactable")]
    internal sealed class CanvasGroupInteractableSlot : ToggleSlot
    {
        readonly CanvasGroup m_Target;

        internal CanvasGroupInteractableSlot(CanvasGroup target) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        protected override bool Read() => m_Target.interactable;

        protected override void Write(bool value) => m_Target.interactable = value;
    }

    [RecordPaths(typeof(CanvasGroup), "m_BlocksRaycasts")]
    internal sealed class CanvasGroupBlocksRaycastsSlot : ToggleSlot
    {
        readonly CanvasGroup m_Target;

        internal CanvasGroupBlocksRaycastsSlot(CanvasGroup target) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        protected override bool Read() => m_Target.blocksRaycasts;

        protected override void Write(bool value) => m_Target.blocksRaycasts = value;
    }

    // a Selectable's interactable — a change runs its transition (uGUI's tint allocates)
    [RecordPaths(typeof(Selectable), "m_Interactable")]
    internal sealed class SelectableInteractableSlot : ToggleSlot
    {
        readonly Selectable m_Target;

        internal SelectableInteractableSlot(Selectable target) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        protected override bool Read() => m_Target.interactable;

        protected override void Write(bool value) => m_Target.interactable = value;
    }

    // the values
    [RecordPaths(typeof(Image), "m_FillAmount")]
    internal sealed class ImageFillSlot : ValueSlot<float, FloatOps>
    {
        readonly Image m_Target;

        internal ImageFillSlot(Image target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.fillAmount;

        protected override void Write(in float value) => m_Target.fillAmount = value;
    }

    [RecordPaths(typeof(CanvasGroup), "m_Alpha")]
    internal sealed class CanvasGroupAlphaSlot : ValueSlot<float, FloatOps>
    {
        readonly CanvasGroup m_Target;

        internal CanvasGroupAlphaSlot(CanvasGroup target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.alpha;

        protected override void Write(in float value) => m_Target.alpha = value;
    }

    // the font size — an int: written rounded, judged on the value read back. The animation can't bind it
    [NotRecorded("the animation can't bind a Text's font data — the preview keeps the path")]
    [PreviewKeeps(PreviewKeep.SerializedPath, "m_FontData.m_FontSize")]
    internal sealed class TextFontSizeSlot : ValueSlot<float, FloatOps>
    {
        readonly Text m_Target;

        internal TextFontSizeSlot(Text target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.fontSize;

        protected override void Write(in float value) => m_Target.fontSize = Mathf.RoundToInt(value);
    }

    // a state — Persist only (Sprite Swap's slot form): the value read now is the play's start
    internal abstract class StateSlot<T> : LayerSlot
    {
        int m_Reserved;

        protected StateSlot()
            : base(ConsumptionStage.Update)
        {
        }

        internal override bool Pending => false;

        internal override int LiveContributions => 0;

        internal override int Reserved => m_Reserved;

        internal override Vector4 BaseVector => Vector4.zero;

        internal void Reserve(int count) => m_Reserved += count;

        internal override void Unreserve(int count) => m_Reserved -= Math.Min(count, m_Reserved);

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

        internal abstract T ReadState();

        protected abstract void Put(T value);

        // Persist — the door
        internal void WriteState(T value)
        {
            RecordOnce();
            Put(value);
        }
    }

    [RecordPaths(typeof(Image), "m_Sprite")]
    internal sealed class ImageSpriteSlot : StateSlot<Sprite>
    {
        readonly Image m_Target;

        internal ImageSpriteSlot(Image target) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        internal override Sprite ReadState() => m_Target.sprite;

        protected override void Put(Sprite value) => m_Target.sprite = value;
    }

    // the Graphic's material — Material Swap's state
    [RecordPaths(typeof(Graphic), "m_Material")]
    internal sealed class GraphicMaterialSlot : StateSlot<Material>
    {
        readonly Graphic m_Target;

        internal GraphicMaterialSlot(Graphic target) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        // the default material is no assignment (the getter gives it for a null)
        internal override Material ReadState() => m_Target.material == m_Target.defaultMaterial ? null : m_Target.material;

        protected override void Put(Material value) => m_Target.material = value;
    }

    // the text — the item's string; the animation can't bind it (a string)
    [NotRecorded("a string — the preview keeps the path")]
    [PreviewKeeps(PreviewKeep.SerializedPath, "m_Text")]
    internal sealed class TextContentSlot : StateSlot<string>
    {
        readonly Text m_Target;

        internal TextContentSlot(Text target) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        internal override string ReadState() => m_Target.text;

        protected override void Put(string value) => m_Target.text = value;
    }

    // an Image's sheet — Sprite Sheet's slot over an Image
    [RecordPaths(typeof(Image), "m_Sprite")]
    internal sealed class ImageSpriteSheetSlot : ValueSlot<float, FloatOps>
    {
        internal const float Original = -1e9f;
        internal const float Other = -2e9f;
        readonly Image m_Target;
        readonly List<Sprite> m_Frames;
        Sprite m_First;
        bool m_HasFirst;
        Sprite m_Before;
        Sprite m_Shown;
        float m_Place;
        bool m_Showing;

        internal ImageSpriteSheetSlot(Image target, List<Sprite> frames)
            : base(ConsumptionStage.Update, Composition.Additive, false)
        {
            m_Target = target;
            m_Frames = frames;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read()
        {
            Sprite now = m_Target.sprite;
            if (m_Showing && now == m_Shown)
                return m_Place;
            return !m_HasFirst || now == m_First ? Original : Other;
        }

        protected override void Write(in float value)
        {
            Sprite now = m_Target.sprite;
            if (!m_HasFirst)
            {
                m_First = now;
                m_HasFirst = true;
            }
            if (!(m_Showing && now == m_Shown))
                m_Before = now;
            if (value == Original || value == Other)
            {
                Put(value == Original ? m_First : m_Before);
                m_Showing = false;
                return;
            }
            int n = m_Frames != null ? m_Frames.Count : 0;
            if (n == 0)
                return;
            float p = value > 0f ? (value < 1f ? value : 1f) : 0f;
            Sprite frame = m_Frames[Mathf.Min((int)(p * n), n - 1)];
            if (frame == null)
            {
                if (m_Showing && now == m_Shown)
                    m_Place = value;
                return;
            }
            Put(frame);
            m_Shown = frame;
            m_Place = value;
            m_Showing = true;
        }

        void Put(Sprite sprite)
        {
            if (m_Target.sprite != sprite)
                m_Target.sprite = sprite;
        }
    }

    // one Graphic's copy of its material — made when the first property slot asks, put in once and kept to the end (a swap
    // per frame would remake a Mask's stencil copy each time). The reference is recorded at the attach; the drop puts the
    // original back where our copy still is and destroys the copy. A preview's copy is hidden with the recovery's prefix
    [RecordPaths(typeof(Graphic), "m_Material")]
    internal sealed class GraphicCopySlot : LayerSlot
    {
        readonly Graphic m_Graphic;
        readonly CanvasRenderer m_Renderer;
        readonly Material m_Original;
        Material m_Copy;
        string m_Name;
        Material m_Seen;
        bool m_SeenOurs;
        bool m_Freed;

        internal GraphicCopySlot(Graphic graphic)
            : base(ConsumptionStage.LateApply)
        {
            m_Graphic = graphic;
            // a component's first read from C# allocates — taken at the prepare, not at a play's first write
            m_Renderer = graphic.canvasRenderer;
            m_Original = graphic.material == graphic.defaultMaterial ? null : graphic.material;
        }

        internal override void Attached() => RecordOnce();

        internal Material Copy => m_Copy;

        internal bool Live => !m_Freed;

        internal void Ensure()
        {
            if (m_Copy != null || m_Freed)
                return;
            Material from = m_Graphic.material;
            bool preview = Registry != null && Registry.IsPreview;
            m_Copy = new Material(from) { name = (preview ? PropertyLayerRegistry.PreviewPrefix : "") + from.name + " (Feedback)" };
            m_Name = m_Copy.name;
            if (preview)
                m_Copy.hideFlags = HideFlags.HideAndDontSave;
            m_Graphic.material = m_Copy;
        }

        // the material drawn when it is a copy of ours — a Mask's stencil copy. Until the canvas updates, the CanvasRenderer
        // still draws the material before ours (the default UI material): a stencil copy carries our copy's name; the
        // verdict is kept for the material seen (a name read makes a string)
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

        internal override bool IsAlive => m_Graphic != null;

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
            if (m_Graphic != null && ReferenceEquals(m_Graphic.material, m_Copy))
                m_Graphic.material = m_Original;
            if (m_Copy == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(m_Copy);
            else
                UnityEngine.Object.DestroyImmediate(m_Copy);
        }
    }

    // the main texture's scale (xy) and offset (zw) in the Graphic's copy — and in the material drawn (a Mask's stencil copy)
    [NotRecorded("the Graphic's copy slot records the reference")]
    internal sealed class GraphicTextureSTSlot : ValueSlot<Vector4, Vector4Ops>
    {
        static readonly int s_MainTex = Shader.PropertyToID("_MainTex");
        readonly Graphic m_Graphic;
        GraphicCopySlot m_Copies;
        bool m_Let;

        internal GraphicTextureSTSlot(Graphic graphic)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Graphic = graphic;
        }

        internal GraphicCopySlot Copies => m_Copies;

        internal override void Attached()
        {
            Graphic graphic = m_Graphic;
            m_Copies = Registry.GetOrCreate(graphic, 0, -1, () => new GraphicCopySlot(graphic));
            m_Copies.Ensure();
        }

        internal override bool IsAlive => m_Graphic != null && m_Copies != null && m_Copies.Live;

        protected override Vector4 Read()
        {
            Material copy = m_Copies.Copy;
            if (copy == null)
                return new Vector4(1f, 1f, 0f, 0f);
            Vector2 s = copy.GetTextureScale(s_MainTex), o = copy.GetTextureOffset(s_MainTex);
            return new Vector4(s.x, s.y, o.x, o.y);
        }

        protected override void Write(in Vector4 value)
        {
            Put(m_Copies.Copy, value);
            Put(m_Copies.Drawn, value);
        }

        static void Put(Material m, in Vector4 value)
        {
            if (m == null)
                return;
            m.SetTextureScale(s_MainTex, new Vector2(value.x, value.y));
            m.SetTextureOffset(s_MainTex, new Vector2(value.z, value.w));
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
}
