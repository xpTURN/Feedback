namespace xpTURN.Feedback
{
    // lit by the kernel on Seconds; the end (T ≥ 1) has weight 0 — the only rule that reads T
    internal sealed class BlinkEvaluator : MotionEvaluator
    {
        readonly BlinkParams m_Params;

        internal BlinkEvaluator(in BlinkParams p, int dimension) : base(dimension)
        {
            m_Params = p;
            m_Params.m_Value = Finite(p.m_Value);
        }

        internal override MotionWrite Write => MotionWrite.Override;

        internal override void Evaluate(in MotionState s, in MotionTime time, out MotionSample sample)
        {
            sample.Value = Scaled(m_Params.m_Value, 1f, m_Dimension);
            bool lit = time.T < 1f
                && MotionKernels.Blink(s.Seed, time.Seconds, m_Params.m_Period, m_Params.m_Duty, m_Params.m_Jitter, m_Params.m_StartOn);
            sample.Weight = lit ? Cap(s.Intensity) : 0f;
        }
    }
}
