using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the sprite renderer's color — LightColorBinding's form
    internal sealed class SpriteColorBinding : ValueTargetBinding<Color, ColorOps>
    {
        internal SpriteColorBinding(SpriteColorItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            slot = null;
            SpriteRenderer renderer = TargetResolver.Find<SpriteRenderer>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (renderer == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(renderer, 0, 0, () => new SpriteColorSlot(renderer));
            else
                slot = Layers.GetOrCreate(renderer, 0, 0, () => new SpriteColorSlot(renderer));
            return true;
        }
    }

    // the alpha over the color's slot (the texture halves' form): the motion's one dimension is the alpha, put over the
    // slot's rgb now. An override (Flash · Blink) takes the alpha only — the color's own contributions stay
    internal sealed class SpriteAlphaBinding : ValueTargetBinding<Color, ColorOps>
    {
        internal SpriteAlphaBinding(SpriteAlphaItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            slot = null;
            SpriteRenderer renderer = TargetResolver.Find<SpriteRenderer>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (renderer == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(renderer, 0, 0, () => new SpriteColorSlot(renderer));
            else
                slot = Layers.GetOrCreate(renderer, 0, 0, () => new SpriteColorSlot(renderer));
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

    // a sprite for another — MaterialSwapBinding's form with one reference (the play's start is the slot's read now)
    internal sealed class SpriteSwapBinding : ItemBinding
    {
        readonly SpriteSwapItem m_Item;
        PropertyLayerRegistry m_Layers;
        SpriteSlot m_Slot;
        int m_Reserved;
        long[] m_Serials;
        bool[] m_HasStart;
        Sprite[] m_Starts;
        Sprite m_Origin;
        Sprite m_First;
        bool m_HasOrigin;
        bool m_HasFirst;

        internal SpriteSwapBinding(SpriteSwapItem item)
        {
            m_Item = item;
        }

        internal SpriteSlot Slot => m_Slot;

        internal override bool Prepare(in PrepareContext context)
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
            SpriteRenderer renderer = TargetResolver.Find<SpriteRenderer>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (renderer == null)
                return Refuse(TargetResolver.Describe(failure));
            if (m_Item.m_Sprite == null)
                return Refuse("no sprite to swap in");
            m_Layers = context.Context.Layers;
            m_Slot = m_Layers.GetOrCreate(renderer, 0, 0, () => new SpriteSlot(renderer));
            int n = context.MaxOverlap;
            m_Slot.Reserve(n);
            m_Reserved = n;
            m_Serials = new long[n];
            m_HasStart = new bool[n];
            m_Starts = new Sprite[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        bool Live => m_Slot != null && m_Slot.IsAlive && !m_Slot.Detached;

        internal override bool IsAlive => Live;

        internal override void Execute(int record, in BeginContext context)
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
                m_Starts[record] = m_Slot.Read();
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
                    m_Slot.WriteSprite(m_Origin);
                return;
            }
            m_Slot.WriteSprite(m_Item.m_Sprite);
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
            if (reason == EndReason.Revert && m_HasStart[record] && Live)
                m_Slot.WriteSprite(m_Starts[record]);
        }

        internal override void Restore()
        {
            if (m_HasFirst && Live)
                m_Slot.WriteSprite(m_First);
            m_HasOrigin = false;
            m_HasFirst = false;
        }

        internal override void Inherit(ItemBinding old)
        {
            if (!(old is SpriteSwapBinding from) || m_Slot == null || from.m_Slot == null || !m_Slot.Key.Equals(from.m_Slot.Key))
                return;
            m_Origin = from.m_Origin;
            m_HasOrigin = from.m_HasOrigin;
            m_First = from.m_First;
            m_HasFirst = from.m_HasFirst;
        }

        internal override void Release()
        {
            if (m_Slot == null)
                return;
            m_Layers.Release(m_Slot, m_Reserved);
            m_Slot = null;
        }
    }

    // an axis's flip — the toggle target's form
    internal sealed class SpriteFlipBinding : ToggleTargetBinding<SpriteFlipSlot>
    {
        internal SpriteFlipBinding(SpriteFlipItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, out SpriteFlipSlot slot)
        {
            slot = null;
            SpriteRenderer renderer = TargetResolver.Find<SpriteRenderer>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (renderer == null)
                return Refuse(TargetResolver.Describe(failure));
            SpriteAxis axis = ((SpriteFlipItem)m_Item).m_Axis;
            slot = Layers.GetOrCreate(renderer, 0, (int)axis, () => new SpriteFlipSlot(renderer, axis));
            return true;
        }
    }

    // the sheet — a value binding over its own slot (the frames are the binding's: another sheet's place on the same renderer
    // is not this one's). Every play's motion starts at 0, the first frame; a backward play ends there. The direct write
    // changes nothing (the slot is always the binding's)
    internal sealed class SpriteSheetBinding : ValueTargetBinding<float, FloatOps>
    {
        readonly SpriteSheetItem m_Sheet;

        internal SpriteSheetBinding(SpriteSheetItem item)
            : base(item)
        {
            m_Sheet = item;
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            SpriteRenderer renderer = TargetResolver.Find<SpriteRenderer>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (renderer == null)
                return Refuse(TargetResolver.Describe(failure));
            List<Sprite> frames = m_Sheet.m_Frames;
            bool any = false;
            for (int i = 0; frames != null && i < frames.Count && !any; i++)
                any = frames[i] != null;
            if (!any)
                return Refuse("no frames");
            slot = Layers.CreatePrivate(renderer, 0, 0, () => new SpriteSheetSlot(renderer, frames));
            return true;
        }

        protected override Vector4 ToMotion(in float value) => Vector4.zero;
    }

    internal sealed class LineWidthBinding : ValueTargetBinding<float, FloatOps>
    {
        internal LineWidthBinding(LineWidthItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            LineRenderer renderer = TargetResolver.Find<LineRenderer>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (renderer == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(renderer, 0, 0, () => new LineWidthSlot(renderer));
            else
                slot = Layers.GetOrCreate(renderer, 0, 0, () => new LineWidthSlot(renderer));
            return true;
        }
    }

    internal sealed class LineColorBinding : ValueTargetBinding<Color, ColorOps>
    {
        internal LineColorBinding(LineColorItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
        {
            slot = null;
            LineRenderer renderer = TargetResolver.Find<LineRenderer>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (renderer == null)
                return Refuse(TargetResolver.Describe(failure));
            LineEnd end = ((LineColorItem)m_Item).m_End;
            if (direct)
                slot = Layers.CreatePrivate(renderer, 0, (int)end, () => new LineColorSlot(renderer, end));
            else
                slot = Layers.GetOrCreate(renderer, 0, (int)end, () => new LineColorSlot(renderer, end));
            return true;
        }
    }

    internal sealed class TrailTimeBinding : ValueTargetBinding<float, FloatOps>
    {
        internal TrailTimeBinding(TrailTimeItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            TrailRenderer renderer = TargetResolver.Find<TrailRenderer>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (renderer == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(renderer, 0, 0, () => new TrailTimeSlot(renderer));
            else
                slot = Layers.GetOrCreate(renderer, 0, 0, () => new TrailTimeSlot(renderer));
            return true;
        }
    }

    internal sealed class TrailWidthBinding : ValueTargetBinding<float, FloatOps>
    {
        internal TrailWidthBinding(TrailWidthItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            TrailRenderer renderer = TargetResolver.Find<TrailRenderer>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (renderer == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(renderer, 0, 0, () => new TrailWidthSlot(renderer));
            else
                slot = Layers.GetOrCreate(renderer, 0, 0, () => new TrailWidthSlot(renderer));
            return true;
        }
    }

    internal sealed class TrailEmittingBinding : ToggleTargetBinding<TrailEmittingSlot>
    {
        internal TrailEmittingBinding(TrailEmittingItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, out TrailEmittingSlot slot)
        {
            slot = null;
            TrailRenderer renderer = TargetResolver.Find<TrailRenderer>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (renderer == null)
                return Refuse(TargetResolver.Describe(failure));
            slot = Layers.GetOrCreate(renderer, 0, 0, () => new TrailEmittingSlot(renderer));
            return true;
        }
    }

    // the trail's points gone — an instant (Execute — a Set's Begin is not called), no slot, nothing to put back (the points
    // are no serialized state)
    internal sealed class TrailClearBinding : ItemBinding
    {
        readonly TrailClearItem m_Item;
        TrailRenderer m_Target;

        internal TrailClearBinding(TrailClearItem item)
        {
            m_Item = item;
        }

        internal override bool Prepare(in PrepareContext context)
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
            m_Target = TargetResolver.Find<TrailRenderer>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (m_Target == null)
                return Refuse(TargetResolver.Describe(failure));
            return true;
        }

        internal override bool IsAlive => m_Target != null;

        internal override void Execute(int record, in BeginContext context)
        {
            if (!context.Reverse && m_Target != null)
                m_Target.Clear();
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
        }
    }
}
