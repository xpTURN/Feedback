namespace xpTURN.Feedback
{
    // I · A · Punch(T) — 0 at T = 1 (the kernel's (1 − T)^Decay)
    internal sealed class PunchEvaluator : MotionEvaluator
    {
        readonly PunchParams m_Params;

        internal PunchEvaluator(in PunchParams p, int dimension) : base(dimension)
        {
            m_Params = p;
            m_Params.m_Amplitude = Finite(p.m_Amplitude);
        }

        internal override MotionWrite Write => MotionWrite.Offset;

        float Shape(in MotionTime time) => MotionKernels.Punch(time.T, m_Params.m_Vibrato, m_Params.m_Decay);

        internal override void Evaluate(in MotionState s, in MotionTime time, out MotionSample sample)
        {
            sample.Value = Scaled(m_Params.m_Amplitude, Shape(time) * s.Intensity, m_Dimension);
            sample.Weight = 1f;
        }

        internal override float EvaluateShape(in MotionState s, in MotionTime time) => Shape(time) * s.Intensity;
    }
}
