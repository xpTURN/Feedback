using System.Linq;
using NUnit.Framework;
using UnityEngine;
using static xpTURN.Feedback.Tests.Editor.MotionEvaluatorTests;

namespace xpTURN.Feedback.Tests.Editor
{
    // the shape is −1 ~ 1 × intensity before the amplitude — an amplitude of 3, so a shape with the amplitude mixed in fails
    // (1 hides it). The target clause (WeightTowardTarget) is the player's
    public sealed class MotionShapeTests
    {
        static float ShapeAt(MotionEvaluator e, in MotionState s, float t) => e.EvaluateShape(s, new MotionTime(t, t * Item));

        [Test]
        public void E06a_OffsetShapesStayWithinTheIntensity()
        {
            var amplitude = new Vector4(3f, 3f, 3f, 3f);
            var offsets = new MotionEvaluator[]
            {
                new PunchEvaluator(new PunchParams { m_Amplitude = amplitude, m_Vibrato = 3f, m_Decay = 2f }, 3),
                new ShakeEvaluator(new ShakeParams { m_Amplitude = amplitude, m_Frequency = 7f }, 3),
                new OscillateEvaluator(new OscillateParams { m_Amplitude = amplitude, m_Frequency = 2f, m_Phase = 0.1f }, 3),
                new WiggleEvaluator(new WiggleParams { m_Amplitude = amplitude, m_Frequency = 3f, m_Octaves = 2 }, 3),
                new SpringEvaluator(new SpringParams { m_Mode = SpringMode.Bump, m_Value = amplitude, m_Frequency = 4f, m_Damping = 0.4f, m_Settle = 0.001f }, 3),
            };
            foreach (var e in offsets)
            {
                var s = Begin(e, Vector4.zero, 0.5f);
                float max = 0f;
                for (int i = 0; i <= 200; i++)
                    max = Mathf.Max(max, Mathf.Abs(ShapeAt(e, s, i / 200f)));
                Assert.That(max, Is.InRange(0.05f, 0.5f + 1e-5f), e.GetType().Name);
            }
            var curve = new CurveEvaluator(new CurveParams { m_Curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 2f)), m_RemapOne = amplitude }, 3);
            Assert.That(ShapeAt(curve, Begin(curve, Vector4.zero, 0.5f), 1f), Is.EqualTo(1f), "Curve — the curve's own range × I");
            foreach (var e in All(3).Where(x => x.Write != MotionWrite.Offset))
                Assert.That(ShapeAt(e, Begin(e, Vector4.zero, 0.5f), 0.4f), Is.EqualTo(0f), $"{e.GetType().Name} has no shape");
        }

        [Test]
        public void E06b_TheSpringShapeEndsAtZero()
        {
            var bump = new SpringEvaluator(Spring(SpringMode.Bump), 1);
            Assert.That(ShapeAt(bump, Begin(bump, Vector4.zero), 1f), Is.EqualTo(0f));
        }
    }
}
