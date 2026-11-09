using TMPro;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the font size · the spacings
    internal sealed class TMPFontSizeBinding : ValueTargetBinding<float, FloatOps>
    {
        internal TMPFontSizeBinding(TMPFontSizeItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            TMP_Text text = TargetResolver.Find<TMP_Text>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (text == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = direct ? Layers.CreatePrivate(text, 0, 0, () => new TMPFontSizeSlot(text)) : Layers.GetOrCreate(text, 0, 0, () => new TMPFontSizeSlot(text));
            return true;
        }
    }

    internal sealed class TMPCharacterSpacingBinding : ValueTargetBinding<float, FloatOps>
    {
        internal TMPCharacterSpacingBinding(TMPCharacterSpacingItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            TMP_Text text = TargetResolver.Find<TMP_Text>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (text == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = direct ? Layers.CreatePrivate(text, 0, 0, () => new TMPCharacterSpacingSlot(text)) : Layers.GetOrCreate(text, 0, 0, () => new TMPCharacterSpacingSlot(text));
            return true;
        }
    }

    internal sealed class TMPWordSpacingBinding : ValueTargetBinding<float, FloatOps>
    {
        internal TMPWordSpacingBinding(TMPWordSpacingItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            TMP_Text text = TargetResolver.Find<TMP_Text>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (text == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = direct ? Layers.CreatePrivate(text, 0, 0, () => new TMPWordSpacingSlot(text)) : Layers.GetOrCreate(text, 0, 0, () => new TMPWordSpacingSlot(text));
            return true;
        }
    }

    internal sealed class TMPLineSpacingBinding : ValueTargetBinding<float, FloatOps>
    {
        internal TMPLineSpacingBinding(TMPLineSpacingItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            TMP_Text text = TargetResolver.Find<TMP_Text>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (text == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = direct ? Layers.CreatePrivate(text, 0, 0, () => new TMPLineSpacingSlot(text)) : Layers.GetOrCreate(text, 0, 0, () => new TMPLineSpacingSlot(text));
            return true;
        }
    }

    internal sealed class TMPParagraphSpacingBinding : ValueTargetBinding<float, FloatOps>
    {
        internal TMPParagraphSpacingBinding(TMPParagraphSpacingItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            TMP_Text text = TargetResolver.Find<TMP_Text>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (text == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = direct ? Layers.CreatePrivate(text, 0, 0, () => new TMPParagraphSpacingSlot(text)) : Layers.GetOrCreate(text, 0, 0, () => new TMPParagraphSpacingSlot(text));
            return true;
        }
    }

    // a float · a color · the underlay's offset of the text's copy of its font material
    internal sealed class TMPMaterialFloatBinding : ValueTargetBinding<float, FloatOps>
    {
        readonly int m_Id;
        readonly bool m_Pads;

        internal TMPMaterialFloatBinding(TargetItem item, int id, bool pads)
            : base(item)
        {
            m_Id = id;
            m_Pads = pads;
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            if (!TMPAcquire.Material(in m_Item.m_Target, in context, m_Id, out TMP_Text text, out string reason))
                return Refuse(reason);
            int id = m_Id;
            bool pads = m_Pads;
            slot = direct ? Layers.CreatePrivate(text, id, 0, () => new TMPMaterialFloatSlot(text, id, pads)) : Layers.GetOrCreate(text, id, 0, () => new TMPMaterialFloatSlot(text, id, pads));
            return true;
        }
    }

    internal sealed class TMPMaterialColorBinding : ValueTargetBinding<Color, ColorOps>
    {
        readonly int m_Id;

        internal TMPMaterialColorBinding(TargetItem item, int id)
            : base(item)
        {
            m_Id = id;
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            slot = null;
            if (!TMPAcquire.Material(in m_Item.m_Target, in context, m_Id, out TMP_Text text, out string reason))
                return Refuse(reason);
            int id = m_Id;
            slot = direct ? Layers.CreatePrivate(text, id, 0, () => new TMPMaterialColorSlot(text, id)) : Layers.GetOrCreate(text, id, 0, () => new TMPMaterialColorSlot(text, id));
            return true;
        }
    }

    internal sealed class TMPUnderlayOffsetBinding : ValueTargetBinding<Vector2, Vector2Ops>
    {
        internal TMPUnderlayOffsetBinding(TMPUnderlayOffsetItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector2, Vector2Ops> slot)
        {
            slot = null;
            if (!TMPAcquire.Material(in m_Item.m_Target, in context, TMPShader.UnderlayOffsetX, out TMP_Text text, out string reason))
                return Refuse(reason);
            slot = direct ? Layers.CreatePrivate(text, 0, 0, () => new TMPUnderlayOffsetSlot(text)) : Layers.GetOrCreate(text, 0, 0, () => new TMPUnderlayOffsetSlot(text));
            return true;
        }
    }

    static class TMPAcquire
    {
        internal static bool Material(in TargetRef target, in PrepareContext context, int id, out TMP_Text text, out string reason)
        {
            reason = null;
            text = TargetResolver.Find<TMP_Text>(in target, in context, out TargetFailure failure);
            if (text == null)
            {
                reason = TargetResolver.Describe(failure);
                return false;
            }
            Material shared = text.fontSharedMaterial;
            if (shared == null)
            {
                reason = "the text has no font material";
                return false;
            }
            if (!shared.HasProperty(id))
            {
                reason = "the font material's shader has no such property";
                return false;
            }
            return true;
        }
    }

    // Set Text — the item's string (Sprite Swap's form); the text slot is Count To's too
    internal sealed class TMPTextBinding : StateSwapBinding<TMPTextSlot, string>
    {
        internal TMPTextBinding(TMPTextItem item)
            : base(item)
        {
        }

        protected override string Value => ((TMPTextItem)m_Item).m_Text ?? "";

        protected override bool Acquire(in PrepareContext context, PropertyLayerRegistry layers, out TMPTextSlot slot)
        {
            slot = null;
            TMP_Text text = TargetResolver.Find<TMP_Text>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (text == null)
                return RefuseWith(TargetResolver.Describe(failure));
            slot = layers.GetOrCreate(text, 0, 0, () => new TMPTextSlot(text));
            return true;
        }
    }

    // Count To · Reveal: a progress over their own slot (Sprite Sheet's binding form)
    internal sealed class TMPCountToBinding : ValueTargetBinding<float, FloatOps>
    {
        readonly TMPCountToItem m_Count;

        internal TMPCountToBinding(TMPCountToItem item)
            : base(item)
        {
            m_Count = item;
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            TMP_Text text = TargetResolver.Find<TMP_Text>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (text == null)
                return Refuse(TargetResolver.Describe(failure));
            if (!TMPNumberFormat.TryParse(m_Count.m_Format, out TMPNumberFormat format, out string reason))
                return Refuse(reason);
            if (!FloatingTextBinding.s_TmpResources())
                return Refuse(FloatingTextBinding.NoTmpResources);
            // a text never parsed makes its style stacks at its first SetText(char[]) — a player's too. The preferred values
            // parse the text it has (a string set's — a SetText's is parsed already) and change nothing shown
            text.GetPreferredValues(0f, 0f);
            double from = m_Count.m_From, to = m_Count.m_To;
            slot = Layers.CreatePrivate(text, 0, 0, () => new TMPCountToSlot(text, from, to, format));
            return true;
        }

        protected override Vector4 ToMotion(in float value) => Vector4.zero;
    }

    internal sealed class TMPRevealBinding : ValueTargetBinding<float, FloatOps>
    {
        readonly TMPRevealItem m_Reveal;

        internal TMPRevealBinding(TMPRevealItem item)
            : base(item)
        {
            m_Reveal = item;
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            TMP_Text text = TargetResolver.Find<TMP_Text>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (text == null)
                return Refuse(TargetResolver.Describe(failure));
            // a text never drawn has no counts yet
            if (text.textInfo == null || text.textInfo.characterCount == 0)
                text.ForceMeshUpdate();
            TMPRevealUnit unit = m_Reveal.m_Unit;
            slot = Layers.CreatePrivate(text, 0, 0, () => new TMPRevealSlot(text, unit));
            return true;
        }

        protected override Vector4 ToMotion(in float value) => Vector4.zero;
    }
}
