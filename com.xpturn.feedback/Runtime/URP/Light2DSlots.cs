using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace xpTURN.Feedback
{
    // a 2D light's values — written in LateApply as they are (the color HDR), the frame with its read and write only
    [RecordPaths(typeof(Light2D), "m_Intensity")]
    internal sealed class Light2DIntensitySlot : ValueSlot<float, FloatOps>
    {
        readonly Light2D m_Target;

        internal Light2DIntensitySlot(Light2D target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.intensity;

        protected override void Write(in float value) => m_Target.intensity = value;
    }

    [RecordPaths(typeof(Light2D), "m_Color.r", "m_Color.g", "m_Color.b", "m_Color.a")]
    internal sealed class Light2DColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly Light2D m_Target;

        internal Light2DColorSlot(Light2D target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override Color Read() => m_Target.color;

        protected override void Write(in Color value) => m_Target.color = value;
    }

    // the renderer takes 1 / (1 − inner / outer) — an inner past the outer puts the light out. Every write sets the outer
    // radius and then the inner to min(the inner's own, outer); the inner's own is taken again when the inner is not what this
    // slot last left there (another's write) — LightSpotAngleSlot's form
    [RecordPaths(typeof(Light2D), "m_PointLightOuterRadius", "m_PointLightInnerRadius")]
    internal sealed class Light2DRadiusSlot : ValueSlot<float, FloatOps>
    {
        readonly Light2D m_Target;
        float m_Inner;
        float m_Left;
        bool m_HasInner;

        internal Light2DRadiusSlot(Light2D target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.pointLightOuterRadius;

        protected override void Write(in float value)
        {
            float inner = m_Target.pointLightInnerRadius;
            if (!m_HasInner || BitConverter.SingleToInt32Bits(inner) != BitConverter.SingleToInt32Bits(m_Left))
            {
                m_Inner = inner;
                m_HasInner = true;
            }
            m_Target.pointLightOuterRadius = value;
            m_Target.pointLightInnerRadius = Mathf.Min(m_Inner, m_Target.pointLightOuterRadius);
            m_Left = m_Target.pointLightInnerRadius;
        }
    }

    // the light keeps the falloff in [0, 1] — the judgment on the value read back takes that in
    [RecordPaths(typeof(Light2D), "m_FalloffIntensity")]
    internal sealed class Light2DFalloffSlot : ValueSlot<float, FloatOps>
    {
        readonly Light2D m_Target;

        internal Light2DFalloffSlot(Light2D target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.falloffIntensity;

        protected override void Write(in float value) => m_Target.falloffIntensity = value;
    }
}
