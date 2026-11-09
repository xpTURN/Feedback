using UnityEngine;

namespace xpTURN.Feedback
{
    // a RectTransform's size delta · pivot · anchors — written before the canvas update (LateApply). A write of any of the
    // three is a dimension change (the layout above rebuilds)
    [RecordPaths(typeof(RectTransform), "m_SizeDelta.x", "m_SizeDelta.y")]
    internal sealed class RectSizeDeltaSlot : ValueSlot<Vector2, Vector2Ops>
    {
        readonly RectTransform m_Target;

        internal RectSizeDeltaSlot(RectTransform target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        protected override bool JudgesByComponent => true;

        internal override bool IsAlive => m_Target != null;

        protected override Vector2 Read() => m_Target.sizeDelta;

        protected override void Write(in Vector2 value) => m_Target.sizeDelta = value;
    }

    [RecordPaths(typeof(RectTransform), "m_Pivot.x", "m_Pivot.y")]
    internal sealed class RectPivotSlot : ValueSlot<Vector2, Vector2Ops>
    {
        readonly RectTransform m_Target;

        internal RectPivotSlot(RectTransform target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        protected override bool JudgesByComponent => true;

        internal override bool IsAlive => m_Target != null;

        protected override Vector2 Read() => m_Target.pivot;

        protected override void Write(in Vector2 value) => m_Target.pivot = value;
    }

    // the anchors as (min x, min y, max x, max y) — the anchored position stays, so the local position moves (a derived row)
    [RecordPaths(typeof(RectTransform), "m_AnchorMin.x", "m_AnchorMin.y", "m_AnchorMax.x", "m_AnchorMax.y", "m_LocalPosition.x", "m_LocalPosition.y")]
    internal sealed class RectAnchorsSlot : ValueSlot<Vector4, Vector4Ops>
    {
        readonly RectTransform m_Target;

        internal RectAnchorsSlot(RectTransform target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        protected override bool JudgesByComponent => true;

        internal override bool IsAlive => m_Target != null;

        protected override Vector4 Read()
        {
            Vector2 min = m_Target.anchorMin, max = m_Target.anchorMax;
            return new Vector4(min.x, min.y, max.x, max.y);
        }

        protected override void Write(in Vector4 value)
        {
            m_Target.anchorMin = new Vector2(value.x, value.y);
            m_Target.anchorMax = new Vector2(value.z, value.w);
        }
    }
}
