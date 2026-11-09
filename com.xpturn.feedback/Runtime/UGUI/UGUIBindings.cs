using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace xpTURN.Feedback
{
    // a Graphic's color · alpha — Sprite Color · Alpha's form
    internal sealed class GraphicColorBinding : ValueTargetBinding<Color, ColorOps>
    {
        internal GraphicColorBinding(GraphicColorItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            slot = null;
            Graphic graphic = TargetResolver.Find<Graphic>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (graphic == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = UGUIAcquire.ColorSlot(graphic, Layers, direct);
            return true;
        }
    }

    // the alpha over the color's slot: an override (Flash · Blink) takes the alpha only
    internal sealed class GraphicAlphaBinding : ValueTargetBinding<Color, ColorOps>
    {
        internal GraphicAlphaBinding(GraphicAlphaItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            slot = null;
            Graphic graphic = TargetResolver.Find<Graphic>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (graphic == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = UGUIAcquire.ColorSlot(graphic, Layers, direct);
            return true;
        }

        Color Now => Slot != null ? Slot.Base : Color.white;

        static Color With(Color rgb, float alpha) => new Color(rgb.r, rgb.g, rgb.b, alpha);

        protected override Vector4 ToMotion(in Color value) => new Vector4(value.a, 0f, 0f, 0f);

        protected override Color FromMotion(in Vector4 value) => With(Now, value.x);

        protected override Color MapOffset(in Color offset) => new Color(0f, 0f, 0f, offset.r);

        protected override Color MapOverride(in Color target) => new Color(0f, 0f, 0f, target.r);

        protected override Vector4 OverrideMask => new Vector4(0f, 0f, 0f, 1f);

        protected override Color Stored(in Color value) => With(Now, value.a);
    }

    // the CanvasRenderer's color
    internal sealed class GraphicCrossFadeBinding : ValueTargetBinding<Color, ColorOps>
    {
        internal GraphicCrossFadeBinding(GraphicCrossFadeItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            slot = null;
            Graphic graphic = TargetResolver.Find<Graphic>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (graphic == null)
                return Refuse(TargetResolver.Describe(failure));
            // a 3D TextMeshPro text has no CanvasRenderer — the getter would add one in a player
            if (graphic is TextMeshPro)
                return Refuse(UGUIAcquire.TMPRefusal);
            CanvasRenderer cr = graphic.canvasRenderer;
            slot = direct ? Layers.CreatePrivate(cr, 0, 0, () => new CanvasRendererColorSlot(cr)) : Layers.GetOrCreate(cr, 0, 0, () => new CanvasRendererColorSlot(cr));
            return true;
        }
    }

    // the main texture's offset · scale halves over one slot (TextureST)
    internal sealed class GraphicTextureOffsetBinding : ValueTargetBinding<Vector4, Vector4Ops>
    {
        internal GraphicTextureOffsetBinding(GraphicTextureOffsetItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector4, Vector4Ops> slot)
        {
            slot = UGUIAcquire.TextureST(in m_Item.m_Target, in context, Layers, direct, out string reason);
            return slot != null || Refuse(reason);
        }

        protected override Vector4 ToMotion(in Vector4 value) => TextureST.Half(value, true);

        protected override Vector4 FromMotion(in Vector4 value) => TextureST.With(TextureST.Now(Slot), true, value.x, value.y);

        protected override Vector4 MapOffset(in Vector4 offset) => TextureST.With(Vector4.zero, true, offset.x, offset.y);

        protected override Vector4 Stored(in Vector4 value) => TextureST.With(TextureST.Now(Slot), true, value.z, value.w);
    }

    internal sealed class GraphicTextureScaleBinding : ValueTargetBinding<Vector4, Vector4Ops>
    {
        internal GraphicTextureScaleBinding(GraphicTextureScaleItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector4, Vector4Ops> slot)
        {
            slot = UGUIAcquire.TextureST(in m_Item.m_Target, in context, Layers, direct, out string reason);
            return slot != null || Refuse(reason);
        }

        protected override Vector4 ToMotion(in Vector4 value) => TextureST.Half(value, false);

        protected override Vector4 FromMotion(in Vector4 value) => TextureST.With(TextureST.Now(Slot), false, value.x, value.y);

        protected override Vector4 MapOffset(in Vector4 offset) => TextureST.With(Vector4.zero, false, offset.x, offset.y);

        protected override Vector4 Stored(in Vector4 value) => TextureST.With(TextureST.Now(Slot), false, value.x, value.y);
    }

    static class UGUIAcquire
    {
        internal const string TMPRefusal = "a TextMeshPro text — the TMP/… material items write its own copy";

        // a TMP text's color is its own slot (the rows m_fontColor.*)
        internal static ValueSlot<Color, ColorOps> ColorSlot(Graphic graphic, PropertyLayerRegistry layers, bool direct)
        {
            if (graphic is TMP_Text text)
                return direct ? layers.CreatePrivate(text, 0, 0, () => new TMPColorSlot(text)) : layers.GetOrCreate(text, 0, 0, () => new TMPColorSlot(text));
            return direct ? layers.CreatePrivate(graphic, 0, 0, () => new GraphicColorSlot(graphic)) : layers.GetOrCreate(graphic, 0, 0, () => new GraphicColorSlot(graphic));
        }

        internal static ValueSlot<Vector4, Vector4Ops> TextureST(in TargetRef target, in PrepareContext context, PropertyLayerRegistry layers, bool direct, out string reason)
        {
            reason = null;
            Graphic graphic = TargetResolver.Find<Graphic>(in target, in context, out TargetFailure failure);
            if (graphic == null)
            {
                reason = TargetResolver.Describe(failure);
                return null;
            }
            // a TMP text draws its font material — the drawn one is the font's shared material
            if (graphic is TMP_Text)
            {
                reason = TMPRefusal;
                return null;
            }
            if (!graphic.material.HasTexture("_MainTex"))
            {
                reason = "the material has no texture _MainTex";
                return null;
            }
            return direct ? layers.CreatePrivate(graphic, 0, 1, () => new GraphicTextureSTSlot(graphic)) : layers.GetOrCreate(graphic, 0, 1, () => new GraphicTextureSTSlot(graphic));
        }
    }

    // the values
    internal sealed class ImageFillBinding : ValueTargetBinding<float, FloatOps>
    {
        internal ImageFillBinding(ImageFillItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Image image = TargetResolver.Find<Image>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (image == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = direct ? Layers.CreatePrivate(image, 0, 0, () => new ImageFillSlot(image)) : Layers.GetOrCreate(image, 0, 0, () => new ImageFillSlot(image));
            return true;
        }
    }

    internal sealed class CanvasGroupAlphaBinding : ValueTargetBinding<float, FloatOps>
    {
        internal CanvasGroupAlphaBinding(CanvasGroupAlphaItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            CanvasGroup group = TargetResolver.Find<CanvasGroup>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (group == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = direct ? Layers.CreatePrivate(group, 0, 0, () => new CanvasGroupAlphaSlot(group)) : Layers.GetOrCreate(group, 0, 0, () => new CanvasGroupAlphaSlot(group));
            return true;
        }
    }

    internal sealed class TextFontSizeBinding : ValueTargetBinding<float, FloatOps>
    {
        internal TextFontSizeBinding(TextFontSizeItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Text text = TargetResolver.Find<Text>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (text == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = direct ? Layers.CreatePrivate(text, 0, 0, () => new TextFontSizeSlot(text)) : Layers.GetOrCreate(text, 0, 0, () => new TextFontSizeSlot(text));
            return true;
        }
    }

    // the sheet's own slot (Sprite Sheet's binding over an Image)
    internal sealed class ImageSpriteSheetBinding : ValueTargetBinding<float, FloatOps>
    {
        readonly ImageSpriteSheetItem m_Sheet;

        internal ImageSpriteSheetBinding(ImageSpriteSheetItem item)
            : base(item)
        {
            m_Sheet = item;
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Image image = TargetResolver.Find<Image>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (image == null)
                return Refuse(TargetResolver.Describe(failure));
            List<Sprite> frames = m_Sheet.m_Frames;
            bool any = false;
            for (int i = 0; frames != null && i < frames.Count && !any; i++)
                any = frames[i] != null;
            if (!any)
                return Refuse("no frames");
            slot = Layers.CreatePrivate(image, 0, 0, () => new ImageSpriteSheetSlot(image, frames));
            return true;
        }

        protected override Vector4 ToMotion(in float value) => Vector4.zero;
    }

    // the toggles
    internal sealed class GraphicRaycastTargetBinding : ToggleTargetBinding<GraphicRaycastTargetSlot>
    {
        internal GraphicRaycastTargetBinding(GraphicRaycastTargetItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, out GraphicRaycastTargetSlot slot)
        {
            slot = null;
            Graphic graphic = TargetResolver.Find<Graphic>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (graphic == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = Layers.GetOrCreate(graphic, 0, 0, () => new GraphicRaycastTargetSlot(graphic));
            return true;
        }
    }

    internal sealed class CanvasGroupInteractableBinding : ToggleTargetBinding<CanvasGroupInteractableSlot>
    {
        internal CanvasGroupInteractableBinding(CanvasGroupInteractableItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, out CanvasGroupInteractableSlot slot)
        {
            slot = null;
            CanvasGroup group = TargetResolver.Find<CanvasGroup>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (group == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = Layers.GetOrCreate(group, 0, 0, () => new CanvasGroupInteractableSlot(group));
            return true;
        }
    }

    internal sealed class CanvasGroupBlocksRaycastsBinding : ToggleTargetBinding<CanvasGroupBlocksRaycastsSlot>
    {
        internal CanvasGroupBlocksRaycastsBinding(CanvasGroupBlocksRaycastsItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, out CanvasGroupBlocksRaycastsSlot slot)
        {
            slot = null;
            CanvasGroup group = TargetResolver.Find<CanvasGroup>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (group == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = Layers.GetOrCreate(group, 0, 0, () => new CanvasGroupBlocksRaycastsSlot(group));
            return true;
        }
    }

    internal sealed class SelectableInteractableBinding : ToggleTargetBinding<SelectableInteractableSlot>
    {
        internal SelectableInteractableBinding(SelectableInteractableItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, out SelectableInteractableSlot slot)
        {
            slot = null;
            Selectable selectable = TargetResolver.Find<Selectable>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (selectable == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = Layers.GetOrCreate(selectable, 0, 0, () => new SelectableInteractableSlot(selectable));
            return true;
        }
    }

    // a value for another — Sprite Swap's form over a state slot (the play's start is the slot's read now).
    // Set's Relative means nothing for these (the value is the item's)
    internal abstract class StateSwapBinding<TSlot, T> : ItemBinding where TSlot : StateSlot<T>
    {
        protected readonly TargetItem m_Item;
        PropertyLayerRegistry m_Layers;
        TSlot m_Slot;
        int m_Reserved;
        long[] m_Serials;
        bool[] m_HasStart;
        T[] m_Starts;
        T m_Origin;
        T m_First;
        bool m_HasOrigin;
        bool m_HasFirst;

        protected StateSwapBinding(TargetItem item)
        {
            m_Item = item;
        }

        internal TSlot Slot => m_Slot;

        protected abstract T Value { get; }

        protected abstract bool Acquire(in PrepareContext context, PropertyLayerRegistry layers, out TSlot slot);

        internal sealed override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            m_Layers = context.Context.Layers;
            if (!Acquire(in context, m_Layers, out m_Slot))
                return false;
            int n = context.MaxOverlap;
            m_Slot.Reserve(n);
            m_Reserved = n;
            m_Serials = new long[n];
            m_HasStart = new bool[n];
            m_Starts = new T[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        protected bool RefuseWith(string reason) => Refuse(reason);

        bool Live => m_Slot != null && m_Slot.IsAlive && !m_Slot.Detached;

        internal override bool IsAlive => Live;

        internal sealed override void Execute(int record, in BeginContext context)
        {
            bool fresh = m_Serials[record] != context.Serial || !m_HasStart[record];
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_HasStart[record] = false;
            }
            if (!Live)
                return;
            if (!m_HasStart[record])
            {
                m_Starts[record] = m_Slot.ReadState();
                m_HasStart[record] = true;
            }
            if (fresh)
            {
                if (!m_HasFirst)
                {
                    m_First = m_Starts[record];
                    m_HasFirst = true;
                }
                if (!context.Reverse)
                {
                    m_Origin = m_Starts[record];
                    m_HasOrigin = true;
                }
            }
            if (context.Reverse)
            {
                if (m_HasOrigin)
                    m_Slot.WriteState(m_Origin);
                return;
            }
            m_Slot.WriteState(Value);
        }

        internal sealed override void Begin(int record, in BeginContext context)
        {
        }

        internal sealed override void Evaluate(int record, in MotionTime time)
        {
        }

        internal sealed override void End(int record, EndReason reason)
        {
            if (reason == EndReason.Revert && m_HasStart[record] && Live)
                m_Slot.WriteState(m_Starts[record]);
        }

        internal sealed override void Restore()
        {
            if (m_HasFirst && Live)
                m_Slot.WriteState(m_First);
            m_HasOrigin = false;
            m_HasFirst = false;
        }

        internal sealed override void Inherit(ItemBinding old)
        {
            if (!(old is StateSwapBinding<TSlot, T> from) || m_Slot == null || from.m_Slot == null || !m_Slot.Key.Equals(from.m_Slot.Key))
                return;
            m_Origin = from.m_Origin;
            m_HasOrigin = from.m_HasOrigin;
            m_First = from.m_First;
            m_HasFirst = from.m_HasFirst;
        }

        internal sealed override void Release()
        {
            if (m_Slot == null)
                return;
            m_Layers.Release(m_Slot, m_Reserved);
            m_Slot = null;
        }
    }

    internal sealed class ImageSpriteBinding : StateSwapBinding<ImageSpriteSlot, Sprite>
    {
        internal ImageSpriteBinding(ImageSpriteItem item)
            : base(item)
        {
        }

        protected override Sprite Value => ((ImageSpriteItem)m_Item).m_Sprite;

        protected override bool Acquire(in PrepareContext context, PropertyLayerRegistry layers, out ImageSpriteSlot slot)
        {
            slot = null;
            Image image = TargetResolver.Find<Image>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (image == null)
                return RefuseWith(TargetResolver.Describe(failure));
            if (Value == null)
                return RefuseWith("no sprite to swap in");
            slot = layers.GetOrCreate(image, 0, 0, () => new ImageSpriteSlot(image));
            return true;
        }
    }

    internal sealed class GraphicMaterialBinding : StateSwapBinding<GraphicMaterialSlot, Material>
    {
        internal GraphicMaterialBinding(GraphicMaterialItem item)
            : base(item)
        {
        }

        protected override Material Value => ((GraphicMaterialItem)m_Item).m_Material;

        protected override bool Acquire(in PrepareContext context, PropertyLayerRegistry layers, out GraphicMaterialSlot slot)
        {
            slot = null;
            Graphic graphic = TargetResolver.Find<Graphic>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (graphic == null)
                return RefuseWith(TargetResolver.Describe(failure));
            if (Value == null)
                return RefuseWith("no material to swap in");
            slot = layers.GetOrCreate(graphic, 0, 2, () => new GraphicMaterialSlot(graphic));
            return true;
        }
    }

    internal sealed class TextContentBinding : StateSwapBinding<TextContentSlot, string>
    {
        internal TextContentBinding(TextContentItem item)
            : base(item)
        {
        }

        protected override string Value => ((TextContentItem)m_Item).m_Text ?? "";

        protected override bool Acquire(in PrepareContext context, PropertyLayerRegistry layers, out TextContentSlot slot)
        {
            slot = null;
            Text text = TargetResolver.Find<Text>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (text == null)
                return RefuseWith(TargetResolver.Describe(failure));
            slot = layers.GetOrCreate(text, 0, 0, () => new TextContentSlot(text));
            return true;
        }
    }
}
