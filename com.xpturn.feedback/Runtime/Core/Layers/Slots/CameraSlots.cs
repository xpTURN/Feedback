using UnityEngine;

namespace xpTURN.Feedback
{
    // a physical camera's field of view comes from its focal length — the preview records both or the stop leaves the field
    // of view changed
    [RecordPaths(typeof(Camera), "field of view", "m_FocalLength")]
    internal sealed class CameraFieldOfViewSlot : ValueSlot<float, FloatOps>
    {
        readonly Camera m_Target;

        internal CameraFieldOfViewSlot(Camera target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.fieldOfView;

        protected override void Write(in float value) => m_Target.fieldOfView = value;
    }

    [RecordPaths(typeof(Camera), "orthographic size")]
    internal sealed class CameraOrthographicSizeSlot : ValueSlot<float, FloatOps>
    {
        readonly Camera m_Target;

        internal CameraOrthographicSizeSlot(Camera target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.orthographicSize;

        protected override void Write(in float value) => m_Target.orthographicSize = value;
    }

    // the written pair stays drawable — near ≥ MinNear, far ≥ near + MinGap (past it the camera draws nothing and screen
    // conversions log errors). The base and the revert are the engine's own pair as it was
    [RecordPaths(typeof(Camera), "near clip plane", "far clip plane")]
    internal sealed class CameraClippingPlanesSlot : ValueSlot<Vector2, Vector2Ops>
    {
        internal const float MinNear = 0.01f;
        internal const float MinGap = 0.01f;

        readonly Camera m_Target;

        internal CameraClippingPlanesSlot(Camera target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override Vector2 Read() => new Vector2(m_Target.nearClipPlane, m_Target.farClipPlane);

        protected override void Write(in Vector2 value)
        {
            float near = Mathf.Max(value.x, MinNear);
            m_Target.nearClipPlane = near;
            m_Target.farClipPlane = Mathf.Max(value.y, near + MinGap);
        }

        protected override void Restore(in Vector2 baseValue)
        {
            m_Target.nearClipPlane = baseValue.x;
            m_Target.farClipPlane = baseValue.y;
        }
    }

    // HDR as it is
    [RecordPaths(typeof(Camera), "m_BackGroundColor.r", "m_BackGroundColor.g", "m_BackGroundColor.b", "m_BackGroundColor.a")]
    internal sealed class CameraBackgroundColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly Camera m_Target;

        internal CameraBackgroundColorSlot(Camera target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override Color Read() => m_Target.backgroundColor;

        protected override void Write(in Color value) => m_Target.backgroundColor = value;
    }
}
