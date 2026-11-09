namespace xpTURN.Feedback
{
    // instant — the value at any T. Relative is a change, so it takes the intensity
    internal sealed class SetEvaluator : MotionEvaluator
    {
        readonly SetParams m_Params;

        internal SetEvaluator(in SetParams p, int dimension) : base(dimension)
        {
            m_Params = p;
            m_Params.m_Value = Finite(p.m_Value);
        }

        internal override MotionWrite Write => MotionWrite.Persist;

        internal override bool IsInstant => true;

        internal override float NaturalDuration => 0f;

        internal override void Evaluate(in MotionState s, in MotionTime time, out MotionSample sample)
        {
            sample.Value = m_Params.m_Relative
                ? AddScaled(s.Start, m_Params.m_Value, s.Intensity, m_Dimension)
                : Scaled(m_Params.m_Value, 1f, m_Dimension);
            sample.Weight = 1f;
        }
    }
}
