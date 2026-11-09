using UnityEngine;

namespace xpTURN.Feedback
{
    // I · Lerp(Zero, One, Curve(T)). A null curve (code only — a loaded one is an empty curve) is 0 like the empty curve's
    // Evaluate; a non-finite output (NaN keys arrive through YAML) is 0
    internal sealed class CurveEvaluator : MotionEvaluator
    {
        readonly CurveParams m_Params;

        internal CurveEvaluator(in CurveParams p, int dimension) : base(dimension)
        {
            m_Params = p;
            m_Params.m_RemapZero = Finite(p.m_RemapZero);
            m_Params.m_RemapOne = Finite(p.m_RemapOne);
        }

        internal override MotionWrite Write => MotionWrite.Offset;

        float Shape(in MotionTime time)
        {
            AnimationCurve curve = m_Params.m_Curve;
            return curve == null ? 0f : MotionKernels.Finite(curve.Evaluate(time.T));
        }

        internal override void Evaluate(in MotionState s, in MotionTime time, out MotionSample sample)
        {
            Vector4 v = Lerp(m_Params.m_RemapZero, m_Params.m_RemapOne, Shape(time), m_Dimension);
            sample.Value = Scaled(v, s.Intensity, m_Dimension);
            sample.Weight = 1f;
        }

        // the curve's own range
        internal override float EvaluateShape(in MotionState s, in MotionTime time) => Shape(time) * s.Intensity;
    }
}
