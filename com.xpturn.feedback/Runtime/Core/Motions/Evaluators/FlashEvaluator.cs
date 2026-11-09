namespace xpTURN.Feedback
{
    // the trapezoid's slopes take the ease as a time mirror (Release is Attack run backwards), cut to 0 ~ 1 — the slot cuts
    // the weight after the category scale, so an overshoot would show only when I · c is small
    internal sealed class FlashEvaluator : MotionEvaluator
    {
        readonly FlashParams m_Params;

        internal FlashEvaluator(in FlashParams p, int dimension) : base(dimension)
        {
            m_Params = p;
            m_Params.m_Target = Finite(p.m_Target);
        }

        internal override MotionWrite Write => MotionWrite.Override;

        float Height(in MotionTime time)
        {
            float h = MotionKernels.Trapezoid(time.T, m_Params.m_Attack, m_Params.m_Hold, m_Params.m_Release, out bool ramp);
            return ramp ? MotionKernels.Clamp01(m_Params.m_Ease.Evaluate(h)) : h;
        }

        internal override void Evaluate(in MotionState s, in MotionTime time, out MotionSample sample)
        {
            sample.Value = Scaled(m_Params.m_Target, 1f, m_Dimension);
            sample.Weight = Cap(s.Intensity) * Height(time);
        }
    }
}
