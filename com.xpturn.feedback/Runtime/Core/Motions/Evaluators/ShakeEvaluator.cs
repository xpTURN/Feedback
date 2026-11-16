using UnityEngine;

namespace xpTURN.Feedback
{
    // I · A_c · N(seed_c, F, Seconds) · (1 − Falloff(T)) · h(T) — each component its own seed, h the Fade In along Fade Ease (1
    // without one). An infinite item has T = 0, so no falloff, and takes no fade
    internal sealed class ShakeEvaluator : MotionEvaluator
    {
        readonly ShakeParams m_Params;
        readonly bool m_Fades;

        internal ShakeEvaluator(in ShakeParams p, int dimension) : base(dimension)
        {
            m_Params = p;
            m_Params.m_Amplitude = Finite(p.m_Amplitude);
            m_Fades = MotionKernels.AtLeast(p.m_FadeIn, 0f) > 0f;
        }

        internal override MotionWrite Write => MotionWrite.Offset;

        float Falloff(in MotionTime time) => 1f - m_Params.m_Falloff.Evaluate(time.T);

        float Height(in MotionTime time) => Fade(time.T, m_Params.m_FadeIn, 0f, in m_Params.m_FadeEase);

        float Sample(uint seed, uint component, double seconds)
        {
            uint channel = Hash32.Tagged(seed, Hash32.Component, component);
            return m_Params.m_Noise == ShakeNoise.Jitter
                ? Noise.Jitter(channel, m_Params.m_Frequency, seconds)
                : Noise.SmoothHz(channel, m_Params.m_Frequency, seconds);
        }

        internal override void Evaluate(in MotionState s, in MotionTime time, out MotionSample sample)
        {
            float k = Falloff(time) * s.Intensity;
            if (m_Fades && !s.Endless)
                k *= Height(time);
            double seconds = time.Seconds;
            Vector4 a = m_Params.m_Amplitude;
            Vector4 v = default;
            switch (m_Dimension)
            {
                case 4: v.w = a.w * Sample(s.Seed, 3, seconds) * k; goto case 3;
                case 3: v.z = a.z * Sample(s.Seed, 2, seconds) * k; goto case 2;
                case 2: v.y = a.y * Sample(s.Seed, 1, seconds) * k; goto default;
                default: v.x = a.x * Sample(s.Seed, 0, seconds) * k; break;
            }
            sample.Value = v;
            sample.Weight = 1f;
        }

        internal override float EvaluateShape(in MotionState s, in MotionTime time)
        {
            float v = Sample(s.Seed, 0, time.Seconds) * Falloff(time) * s.Intensity;
            return m_Fades && !s.Endless ? v * Height(time) : v;
        }
    }
}
