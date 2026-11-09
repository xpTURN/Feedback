using UnityEngine;

namespace xpTURN.Feedback
{
    // a RectTransform's size delta · pivot · anchors — LightIntensityBinding's form
    internal sealed class RectSizeDeltaBinding : ValueTargetBinding<Vector2, Vector2Ops>
    {
        internal RectSizeDeltaBinding(RectSizeDeltaItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector2, Vector2Ops> slot)
        {
            slot = null;
            RectTransform rect = TargetResolver.Find<RectTransform>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (rect == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(rect, 0, 0, () => new RectSizeDeltaSlot(rect));
            else
                slot = Layers.GetOrCreate(rect, 0, 0, () => new RectSizeDeltaSlot(rect));
            return true;
        }
    }

    internal sealed class RectPivotBinding : ValueTargetBinding<Vector2, Vector2Ops>
    {
        internal RectPivotBinding(RectPivotItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector2, Vector2Ops> slot)
        {
            slot = null;
            RectTransform rect = TargetResolver.Find<RectTransform>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (rect == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(rect, 0, 0, () => new RectPivotSlot(rect));
            else
                slot = Layers.GetOrCreate(rect, 0, 0, () => new RectPivotSlot(rect));
            return true;
        }
    }

    internal sealed class RectAnchorsBinding : ValueTargetBinding<Vector4, Vector4Ops>
    {
        internal RectAnchorsBinding(RectAnchorsItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector4, Vector4Ops> slot)
        {
            slot = null;
            RectTransform rect = TargetResolver.Find<RectTransform>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (rect == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(rect, 0, 0, () => new RectAnchorsSlot(rect));
            else
                slot = Layers.GetOrCreate(rect, 0, 0, () => new RectAnchorsSlot(rect));
            return true;
        }
    }

    // the offsets over two slots — the size delta's and the position's (Match Transform's form: one play over more than one
    // slot). Each part is a value binding whose Persist value is the offsets' tween at that progress: offsetMin = anchored −
    // size ∘ pivot · offsetMax = anchored + size ∘ (1 − pivot) — the motion's value is the four offsets, taken from both
    // slots' values and the pivot now
    internal sealed class RectOffsetsBinding : ItemBinding
    {
        readonly SizePart m_Size;
        readonly PositionPart m_Position;

        internal RectOffsetsBinding(RectOffsetsItem item)
        {
            m_Size = new SizePart(item, this);
            m_Position = new PositionPart(item, this);
        }

        RectTransform m_Rect;

        // observation
        internal ValueSlot<Vector2, Vector2Ops> SizeSlot => m_Size.Slot;

        internal ValueSlot<Vector3, Vector3Ops> PositionSlot => m_Position.Slot;

        static Vector4 Offsets(Vector2 size, Vector3 local, RectTransform rect)
        {
            Vector2 anchored = (Vector2)(local - PositionBinding.AnchorReference(rect));
            Vector2 pivot = rect.pivot;
            return new Vector4(anchored.x - size.x * pivot.x, anchored.y - size.y * pivot.y, anchored.x + size.x * (1f - pivot.x), anchored.y + size.y * (1f - pivot.y));
        }

        static bool Tweened(MotionEvaluator evaluator, in MotionState s, in MotionTime time, out Vector4 offsets)
        {
            offsets = default;
            if (!(evaluator is TweenEvaluator tween))
                return false;
            tween.Endpoints(in s, out Vector4 from, out Vector4 to);
            float p = tween.Progress(in time);
            offsets = from + (to - from) * p;
            return true;
        }

        Vector2 SizeNow => m_Size.Slot != null ? m_Size.Slot.Base : m_Rect.sizeDelta;

        Vector3 LocalNow => m_Position.Slot != null ? m_Position.Slot.Base : m_Rect.localPosition;

        sealed class SizePart : ValueTargetBinding<Vector2, Vector2Ops>
        {
            readonly RectOffsetsBinding m_Owner;

            internal SizePart(RectOffsetsItem item, RectOffsetsBinding owner)
                : base(item)
            {
                m_Owner = owner;
            }

            protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector2, Vector2Ops> slot)
            {
                slot = null;
                RectTransform rect = TargetResolver.Find<RectTransform>(in m_Item.m_Target, in context, out TargetFailure failure);
                if (rect == null)
                    return Refuse(TargetResolver.Describe(failure));
                m_Owner.m_Rect = rect;
                slot = direct ? Layers.CreatePrivate(rect, 0, 0, () => new RectSizeDeltaSlot(rect)) : Layers.GetOrCreate(rect, 0, 0, () => new RectSizeDeltaSlot(rect));
                return true;
            }

            protected override Vector4 ToMotion(in Vector2 size) => Offsets(size, m_Owner.LocalNow, m_Owner.m_Rect);

            protected override Vector2 FromMotion(in Vector4 o) => new Vector2(o.z - o.x, o.w - o.y);

            protected override bool TryPersist(in MotionState s, in MotionTime time, out Vector2 value)
            {
                if (!Tweened(Evaluator, in s, in time, out Vector4 o))
                {
                    value = default;
                    return false;
                }
                value = FromMotion(in o);
                return true;
            }
        }

        sealed class PositionPart : ValueTargetBinding<Vector3, Vector3Ops>
        {
            readonly RectOffsetsBinding m_Owner;

            internal PositionPart(RectOffsetsItem item, RectOffsetsBinding owner)
                : base(item)
            {
                m_Owner = owner;
            }

            protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector3, Vector3Ops> slot)
            {
                slot = null;
                RectTransform rect = TargetResolver.Find<RectTransform>(in m_Item.m_Target, in context, out TargetFailure failure);
                if (rect == null)
                    return Refuse(TargetResolver.Describe(failure));
                m_Owner.m_Rect = rect;
                _ = rect.parent;
                slot = direct ? Layers.CreatePrivate(rect, 0, 0, () => new LocalPositionSlot(rect)) : Layers.GetOrCreate(rect, 0, 0, () => new LocalPositionSlot(rect));
                return true;
            }

            protected override Vector4 ToMotion(in Vector3 local) => Offsets(m_Owner.SizeNow, local, m_Owner.m_Rect);

            protected override Vector3 FromMotion(in Vector4 o)
            {
                RectTransform rect = m_Owner.m_Rect;
                Vector2 pivot = rect.pivot;
                var size = new Vector2(o.z - o.x, o.w - o.y);
                Vector3 local = LocalNowOrSelf();
                Vector3 anchored = new Vector3(o.x + size.x * pivot.x, o.y + size.y * pivot.y, 0f);
                Vector3 v = anchored + PositionBinding.AnchorReference(rect);
                v.z = local.z;
                return v;
            }

            Vector3 LocalNowOrSelf() => Slot != null ? Slot.Base : m_Owner.m_Rect.localPosition;

            protected override bool TryPersist(in MotionState s, in MotionTime time, out Vector3 value)
            {
                if (!Tweened(Evaluator, in s, in time, out Vector4 o))
                {
                    value = default;
                    return false;
                }
                value = FromMotion(in o);
                return true;
            }
        }

        internal override bool Prepare(in PrepareContext context)
        {
            if (!m_Size.Prepare(in context))
                return Refuse(m_Size.SkipReason);
            if (!m_Position.Prepare(in context))
                return Refuse(m_Position.SkipReason);
            return true;
        }

        internal override void Begin(int record, in BeginContext context)
        {
            m_Size.Begin(record, in context);
            m_Position.Begin(record, in context);
        }

        internal override void Execute(int record, in BeginContext context)
        {
            m_Size.Execute(record, in context);
            m_Position.Execute(record, in context);
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
            m_Size.Evaluate(record, in time);
            m_Position.Evaluate(record, in time);
        }

        internal override void End(int record, EndReason reason)
        {
            m_Size.End(record, reason);
            m_Position.End(record, reason);
        }

        internal override void Settle(int record)
        {
            m_Size.Settle(record);
            m_Position.Settle(record);
        }

        internal override bool IsAlive => m_Size.IsAlive && m_Position.IsAlive;

        internal override void Release()
        {
            m_Size.Release();
            m_Position.Release();
        }

        internal override bool ApplyInitial(in MotionTime time, float intensity)
        {
            bool a = m_Size.ApplyInitial(in time, intensity);
            bool b = m_Position.ApplyInitial(in time, intensity);
            return a || b;
        }

        internal override void Restore()
        {
            m_Size.Restore();
            m_Position.Restore();
        }

        internal override void Inherit(ItemBinding old)
        {
            if (!(old is RectOffsetsBinding from))
                return;
            m_Size.Inherit(from.m_Size);
            m_Position.Inherit(from.m_Position);
        }
    }
}
