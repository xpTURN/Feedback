using System;
using NUnit.Framework;
using UnityEngine;
using static xpTURN.Feedback.Tests.Editor.MotionEvaluatorTests;

namespace xpTURN.Feedback.Tests.Editor
{
    public sealed class SpringEvaluatorTests
    {
        [Test]
        public void E08a_TheNaturalDurationIsTheDataOne()
        {
            foreach (SpringParams p in new[] { Spring(SpringMode.Bump), Spring(SpringMode.MoveTo, 3f), Spring(SpringMode.Bump, 1f), SpringParams.Default })
            {
                var motion = new SpringMotion { m_Params = p };
                MotionEvaluator e = MotionEvaluators.Create(motion, 3);
                Assert.That(BitConverter.SingleToInt32Bits(e.NaturalDuration), Is.EqualTo(BitConverter.SingleToInt32Bits(motion.NaturalDuration)), $"{p.m_Mode} ζ {p.m_Damping}");
            }
        }

        // two Values added, not two intensities — a Bump nonlinear in Value is linear in the intensity
        [Test]
        public void E08b_OverlappingBumpsAddLikeVelocities()
        {
            Vector4 v1 = new Vector4(1f, -2f, 0.5f, 3f), v2 = new Vector4(2f, 1f, -1.5f, 0.25f);
            SpringEvaluator Make(Vector4 v) => new SpringEvaluator(new SpringParams { m_Mode = SpringMode.Bump, m_Value = v, m_Frequency = 4f, m_Damping = 0.4f, m_Settle = 0.001f }, 4);
            SpringEvaluator a = Make(v1), b = Make(v2), both = Make(v1 + v2);
            for (int i = 1; i < 20; i++)
            {
                float t = i / 20f;
                Near(At(both, Begin(both, Vector4.zero), t).Value, At(a, Begin(a, Vector4.zero), t).Value + At(b, Begin(b, Vector4.zero), t).Value, $"t {t}", 1e-4f);
            }
        }

        // the end snaps, so near the end must already be settled — the evaluator has no duration multiplier: it comes as
        // Seconds out of step with T × natural duration (0.5), which a spring on Seconds would follow. The true value just
        // before the end reaches 1.008 × the threshold (ζ ≥ 1 bisects to it), hence × 2
        [Test]
        public void E01f_NearTheEndTheSpringHasSettled()
        {
            foreach (float damping in new[] { 0.4f, 3f })
            foreach (float multiplier in new[] { 0.5f, 2f })
            {
                SpringParams p = Spring(SpringMode.MoveTo, damping);
                p.m_Value = new Vector4(100f, 0f, 0f, 0f);
                var e = new SpringEvaluator(p, 1);
                var s = Begin(e, Vector4.zero);
                float duration = p.SettleTime() * multiplier;
                float near = Eval(e, s, 0.999f, 0.999 * duration).Value.x, end = Eval(e, s, 1f, duration).Value.x;
                Assert.That(Mathf.Abs(near - end), Is.LessThanOrEqualTo(0.001f * 100f * 2f), $"ζ {damping} ×{multiplier}");
            }
        }
    }
}
