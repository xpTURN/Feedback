using System;

namespace xpTURN.Feedback.Tests
{
    // a float of the test target. Its stage, composition and renormalization come in the constructor; it counts its
    // liveness checks and throws, clamps below 0, flips the last bit (a rewrite that cycles with period 2, as a rotation
    // can) or calls back as the target says
    internal sealed class TestFloatSlot : ValueSlot<float, FloatOps>
    {
        readonly LayerTestTarget m_Target;
        internal int AliveChecks;

        internal TestFloatSlot(LayerTestTarget target, ConsumptionStage stage, Composition composition, bool renormalizes)
            : base(stage, composition, renormalizes)
        {
            m_Target = target;
        }

        internal override bool IsAlive
        {
            get
            {
                AliveChecks++;
                return m_Target != null;
            }
        }

        protected override float Read()
        {
            if (m_Target.ThrowsOnRead)
                throw new InvalidOperationException("LayerTestTarget read");
            return m_Target.Value;
        }

        protected override void Write(in float value)
        {
            m_Target.Writes++;
            if (m_Target.Throws)
                throw new InvalidOperationException("LayerTestTarget write");
            float v = value;
            if (m_Target.Clamps && v < 0f)
                v = 0f;
            if (m_Target.Flips)
                v = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v) ^ 1);
            m_Target.Value = v;
            m_Target.OnWrite?.Invoke();
        }
    }
}
