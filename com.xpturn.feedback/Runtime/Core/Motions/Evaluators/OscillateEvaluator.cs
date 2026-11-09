namespace xpTURN.Feedback
{
    // I · A · Wave(F · Seconds + Phase) — Seconds is double, so an infinite item keeps its phase
    internal sealed class OscillateEvaluator : MotionEvaluator
    {
        readonly OscillateParams m_Params;

        internal OscillateEvaluator(in OscillateParams p, int dimension) : base(dimension)
        {
            m_Params = p;
            m_Params.m_Amplitude = Finite(p.m_Amplitude);
        }

        internal override MotionWrite Write => MotionWrite.Offset;

        float Shape(in MotionTime time) => MotionKernels.Wave(m_Params.m_Waveform, m_Params.m_Frequency * time.Seconds + m_Params.m_Phase);

        internal override void Evaluate(in MotionState s, in MotionTime time, out MotionSample sample)
        {
            sample.Value = Scaled(m_Params.m_Amplitude, Shape(time) * s.Intensity, m_Dimension);
            sample.Weight = 1f;
        }

        internal override float EvaluateShape(in MotionState s, in MotionTime time) => Shape(time) * s.Intensity;
    }
}
