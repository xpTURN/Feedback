using UnityEngine;

namespace xpTURN.Feedback
{
    // Endpoints · Progress are what a Shortest rotation needs; Evaluate is built from them. Progress keeps an overshooting
    // ease's values past 0 ~ 1 (SlerpUnclamped uses them)
    internal sealed class TweenEvaluator : MotionEvaluator
    {
        readonly TweenParams m_Params;

        internal TweenEvaluator(in TweenParams p, int dimension) : base(dimension)
        {
            m_Params = p;
            m_Params.m_From = Finite(p.m_From);
            m_Params.m_To = Finite(p.m_To);
        }

        internal override MotionWrite Write => MotionWrite.Persist;

        // By is a change, so it takes the intensity
        internal void Endpoints(in MotionState s, out Vector4 from, out Vector4 to)
        {
            switch (m_Params.m_Mode)
            {
                case TweenMode.From:
                    from = m_Params.m_From;
                    to = s.Start;
                    break;
                case TweenMode.FromTo:
                    from = m_Params.m_From;
                    to = m_Params.m_To;
                    break;
                case TweenMode.By:
                    from = s.Start;
                    to = AddScaled(s.Start, m_Params.m_To, s.Intensity, 4);
                    break;
                default:
                    from = s.Start;
                    to = m_Params.m_To;
                    break;
            }
        }

        // T ≥ 1 warps to exactly 1 (0 after an even yoyo count) — a Custom ease then ends at its curve's end
        internal float Progress(in MotionTime time) => m_Params.m_Ease.Evaluate(MotionKernels.Warp(time.T, m_Params.m_Loops, m_Params.m_Yoyo));

        internal override void Evaluate(in MotionState s, in MotionTime time, out MotionSample sample)
        {
            Endpoints(s, out Vector4 from, out Vector4 to);
            sample.Value = Lerp(from, to, Progress(time), m_Dimension);
            sample.Weight = 1f;
        }
    }
}
