using UnityEngine;

namespace xpTURN.Feedback
{
    // I · A_c · Fbm(seed_c, F, Seconds, Octaves) — each component its own seed
    internal sealed class WiggleEvaluator : MotionEvaluator
    {
        readonly WiggleParams m_Params;

        internal WiggleEvaluator(in WiggleParams p, int dimension) : base(dimension)
        {
            m_Params = p;
            m_Params.m_Amplitude = Finite(p.m_Amplitude);
        }

        internal override MotionWrite Write => MotionWrite.Offset;

        float Sample(uint seed, uint component, double seconds)
            => Noise.Fbm(Hash32.Tagged(seed, Hash32.Component, component), m_Params.m_Frequency, seconds, m_Params.m_Octaves);

        internal override void Evaluate(in MotionState s, in MotionTime time, out MotionSample sample)
        {
            float k = s.Intensity;
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

        internal override float EvaluateShape(in MotionState s, in MotionTime time) => Sample(s.Seed, 0, time.Seconds) * s.Intensity;
    }
}
