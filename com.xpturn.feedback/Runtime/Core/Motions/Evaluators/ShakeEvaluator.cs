using UnityEngine;

namespace xpTURN.Feedback
{
    // I · A_c · N(seed_c, F, Seconds) · (1 − Falloff(T)) — each component its own seed. An infinite item has T = 0, so no
    // falloff
    internal sealed class ShakeEvaluator : MotionEvaluator
    {
        readonly ShakeParams m_Params;

        internal ShakeEvaluator(in ShakeParams p, int dimension) : base(dimension)
        {
            m_Params = p;
            m_Params.m_Amplitude = Finite(p.m_Amplitude);
        }

        internal override MotionWrite Write => MotionWrite.Offset;

        float Falloff(in MotionTime time) => 1f - m_Params.m_Falloff.Evaluate(time.T);

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
            => Sample(s.Seed, 0, time.Seconds) * Falloff(time) * s.Intensity;
    }
}
