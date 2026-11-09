using UnityEngine;

namespace xpTURN.Feedback
{
    // τ = T × natural duration, so a duration multiplier speeds the spring and T = 1 is settled — the end is exact (MoveTo's
    // target · Bump 0). The response is linear, so one unit response per evaluation is scaled per component. The natural
    // duration is computed once here (2.8 ~ 12 µs in Mono per call)
    internal sealed class SpringEvaluator : MotionEvaluator
    {
        readonly SpringParams m_Params;
        readonly SpringShape m_Shape;
        readonly float m_Natural;

        internal SpringEvaluator(in SpringParams p, int dimension) : base(dimension)
        {
            m_Params = p;
            m_Params.m_Value = Finite(p.m_Value);
            m_Shape = SpringShape.Of(p.m_Frequency, p.m_Damping);
            m_Natural = p.SettleTime();
        }

        internal override MotionWrite Write => m_Params.m_Mode == SpringMode.MoveTo ? MotionWrite.Persist : MotionWrite.Offset;

        internal override float NaturalDuration => m_Natural;

        internal override void Evaluate(in MotionState s, in MotionTime time, out MotionSample sample)
        {
            float t = time.T;
            if (m_Params.m_Mode == SpringMode.MoveTo)
            {
                // the target is a change when Relative, so it takes the intensity
                Vector4 target = m_Params.m_Relative
                    ? AddScaled(s.Start, m_Params.m_Value, s.Intensity, m_Dimension)
                    : Scaled(m_Params.m_Value, 1f, m_Dimension);
                sample.Value = t >= 1f ? target : Lerp(target, s.Start, SpringMath.Y(m_Shape, 1f, 0f, t * m_Natural), m_Dimension);
            }
            else
            {
                sample.Value = t >= 1f ? default : Scaled(m_Params.m_Value, s.Intensity * SpringMath.Y(m_Shape, 0f, 1f, t * m_Natural), m_Dimension);
            }
            sample.Weight = 1f;
        }

        // ω₀ · y(τ; 0, 1) — Value is a velocity, not an amplitude, and this peaks at 1 or less
        internal override float EvaluateShape(in MotionState s, in MotionTime time)
        {
            float t = time.T;
            if (m_Params.m_Mode == SpringMode.MoveTo || t >= 1f)
                return 0f;
            return m_Shape.Omega0 * SpringMath.Y(m_Shape, 0f, 1f, t * m_Natural) * s.Intensity;
        }
    }
}
