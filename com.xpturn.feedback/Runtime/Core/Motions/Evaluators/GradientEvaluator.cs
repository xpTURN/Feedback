using UnityEngine;

namespace xpTURN.Feedback
{
    // the gradient's color, cut to finite (a NaN channel arrives through YAML). A null gradient (code only — a loaded one is
    // the default) is white like the default gradient. Persist is absolute
    internal sealed class GradientEvaluator : MotionEvaluator
    {
        readonly GradientParams m_Params;

        internal GradientEvaluator(in GradientParams p, int dimension) : base(dimension)
        {
            m_Params = p;
        }

        internal override MotionWrite Write => m_Params.m_Mode == GradientWrite.Persist ? MotionWrite.Persist : MotionWrite.Override;

        internal override void Evaluate(in MotionState s, in MotionTime time, out MotionSample sample)
        {
            Gradient gradient = m_Params.m_Gradient;
            Color c = gradient == null ? Color.white : gradient.Evaluate(time.T);
            var v = new Vector4(MotionKernels.Finite(c.r), MotionKernels.Finite(c.g), MotionKernels.Finite(c.b), MotionKernels.Finite(c.a));
            sample.Value = Scaled(v, 1f, m_Dimension);
            sample.Weight = m_Params.m_Mode == GradientWrite.Persist ? 1f : Cap(s.Intensity);
        }
    }
}
