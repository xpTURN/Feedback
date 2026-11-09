using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the light's intensity — the frame with its read and write only. The engine clamps below 0 to +0; the judgment on the
    // value read back takes that in, so the end is the base bit for bit
    [RecordPaths(typeof(Light), "m_Intensity")]
    internal sealed class LightIntensitySlot : ValueSlot<float, FloatOps>
    {
        readonly Light m_Target;

        internal LightIntensitySlot(Light target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.intensity;

        protected override void Write(in float value) => m_Target.intensity = value;
    }

    // the engine keeps the range ≥ 0 — the judgment on the value read back takes that in
    [RecordPaths(typeof(Light), "m_Range")]
    internal sealed class LightRangeSlot : ValueSlot<float, FloatOps>
    {
        readonly Light m_Target;

        internal LightRangeSlot(Light target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.range;

        protected override void Write(in float value) => m_Target.range = value;
    }

    // HDR and negative components as they are; the color temperature leaves the color as written
    [RecordPaths(typeof(Light), "m_Color.r", "m_Color.g", "m_Color.b", "m_Color.a")]
    internal sealed class LightColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly Light m_Target;

        internal LightColorSlot(Light target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override Color Read() => m_Target.color;

        protected override void Write(in Color value) => m_Target.color = value;
    }

    // the engine keeps inner ≤ outer both ways and never gives a pushed inner angle back. Every write — an apply's, a
    // Persist's, a revert's — sets the outer angle first and then the inner one to min(the inner's own, outer): the inner opens
    // again with the outer. The inner's own is taken again when the inner is not what this slot last left there (another's
    // write)
    [RecordPaths(typeof(Light), "m_SpotAngle", "m_InnerSpotAngle")]
    internal sealed class LightSpotAngleSlot : ValueSlot<float, FloatOps>
    {
        readonly Light m_Target;
        float m_Inner;
        float m_Left;
        bool m_HasInner;

        internal LightSpotAngleSlot(Light target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.spotAngle;

        protected override void Write(in float value)
        {
            float inner = m_Target.innerSpotAngle;
            if (!m_HasInner || BitConverter.SingleToInt32Bits(inner) != BitConverter.SingleToInt32Bits(m_Left))
            {
                m_Inner = inner;
                m_HasInner = true;
            }
            m_Target.spotAngle = value;
            m_Target.innerSpotAngle = Mathf.Min(m_Inner, m_Target.spotAngle);
            m_Left = m_Target.innerSpotAngle;
        }
    }

    // the engine keeps the strength in [0, 1]
    [RecordPaths(typeof(Light), "m_Shadows.m_Strength")]
    internal sealed class LightShadowStrengthSlot : ValueSlot<float, FloatOps>
    {
        readonly Light m_Target;

        internal LightShadowStrengthSlot(Light target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.shadowStrength;

        protected override void Write(in float value) => m_Target.shadowStrength = value;
    }
}
