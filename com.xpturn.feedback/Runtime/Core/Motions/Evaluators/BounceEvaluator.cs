namespace xpTURN.Feedback
{
    // I · A · Bounce(T) — one-sided, 0 at T = 0 and T = 1
    internal sealed class BounceEvaluator : MotionEvaluator
    {
        readonly BounceParams m_Params;

        internal BounceEvaluator(in BounceParams p, int dimension) : base(dimension)
        {
            m_Params = p;
            m_Params.m_Amplitude = Finite(p.m_Amplitude);
        }

        internal override MotionWrite Write => MotionWrite.Offset;

        float Shape(in MotionTime time) => MotionKernels.Bounce(time.T, m_Params.m_Count, m_Params.m_Restitution);

        internal override void Evaluate(in MotionState s, in MotionTime time, out MotionSample sample)
        {
            sample.Value = Scaled(m_Params.m_Amplitude, Shape(time) * s.Intensity, m_Dimension);
            sample.Weight = 1f;
        }

        internal override float EvaluateShape(in MotionState s, in MotionTime time) => Shape(time) * s.Intensity;
    }
}
