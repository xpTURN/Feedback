using UnityEngine;

namespace xpTURN.Feedback
{
    // I · A · Wave(F · Seconds + Phase) · h(T) — Seconds is double, so an infinite item keeps its phase; h is the Fade In · Fade
    // Out along Fade Ease (1 without them, and on an endless item). Per Axis: component c is
    // I · A_c · Wave(F · k_c · Seconds + Phase + o_c) · h(T) (k Axis Frequency, o Axis Phase); the shape is component 0's
    internal sealed class OscillateEvaluator : MotionEvaluator
    {
        readonly OscillateParams m_Params;
        readonly bool m_Fades;

        internal OscillateEvaluator(in OscillateParams p, int dimension) : base(dimension)
        {
            m_Params = p;
            m_Params.m_Amplitude = Finite(p.m_Amplitude);
            m_Params.m_AxisPhase = Finite(p.m_AxisPhase);
            Vector4 k = p.m_AxisFrequency;
            m_Params.m_AxisFrequency = new Vector4(MotionKernels.AtLeast(k.x, 0f), MotionKernels.AtLeast(k.y, 0f), MotionKernels.AtLeast(k.z, 0f), MotionKernels.AtLeast(k.w, 0f));
            m_Fades = MotionKernels.AtLeast(p.m_FadeIn, 0f) > 0f || MotionKernels.AtLeast(p.m_FadeOut, 0f) > 0f;
        }

        internal override MotionWrite Write => MotionWrite.Offset;

        float Shape(in MotionTime time) => MotionKernels.Wave(m_Params.m_Waveform, m_Params.m_Frequency * time.Seconds + m_Params.m_Phase);

        float Axis(double seconds, float k, float o) => MotionKernels.Wave(m_Params.m_Waveform, m_Params.m_Frequency * k * seconds + m_Params.m_Phase + o);

        float Height(in MotionTime time) => Fade(time.T, m_Params.m_FadeIn, m_Params.m_FadeOut, in m_Params.m_FadeEase);

        internal override void Evaluate(in MotionState s, in MotionTime time, out MotionSample sample)
        {
            bool fades = m_Fades && !s.Endless;
            if (!m_Params.m_PerAxis)
            {
                float w = Shape(time) * s.Intensity;
                sample.Value = Scaled(m_Params.m_Amplitude, fades ? w * Height(time) : w, m_Dimension);
                sample.Weight = 1f;
                return;
            }
            float i = fades ? s.Intensity * Height(time) : s.Intensity;
            double seconds = time.Seconds;
            Vector4 a = m_Params.m_Amplitude;
            Vector4 k = m_Params.m_AxisFrequency;
            Vector4 o = m_Params.m_AxisPhase;
            Vector4 v = default;
            switch (m_Dimension)
            {
                case 4: v.w = a.w * (Axis(seconds, k.w, o.w) * i); goto case 3;
                case 3: v.z = a.z * (Axis(seconds, k.z, o.z) * i); goto case 2;
                case 2: v.y = a.y * (Axis(seconds, k.y, o.y) * i); goto default;
                default: v.x = a.x * (Axis(seconds, k.x, o.x) * i); break;
            }
            sample.Value = v;
            sample.Weight = 1f;
        }

        internal override float EvaluateShape(in MotionState s, in MotionTime time)
        {
            float v = (m_Params.m_PerAxis ? Axis(time.Seconds, m_Params.m_AxisFrequency.x, m_Params.m_AxisPhase.x) : Shape(time)) * s.Intensity;
            return m_Fades && !s.Endless ? v * Height(time) : v;
        }
    }
}
