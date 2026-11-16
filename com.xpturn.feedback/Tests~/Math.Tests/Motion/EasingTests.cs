using System;
using System.Linq;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Motion
{
    // ends exact, Out mirrors In, InOut point-symmetric and continuous, the monotone families, Elastic's domain, input cut
    // to [0, 1] and non-finite inputs
    public sealed class EasingTests
    {
        internal static readonly EaseType[] Curves = Enumerable.Range(0, 31).Select(i => (EaseType)i).ToArray();

        internal static float P1(EaseType e)
            => e >= EaseType.BackIn && e <= EaseType.BackInOut ? 1.70158f : e >= EaseType.ElasticIn && e <= EaseType.ElasticInOut ? 1f : 0f;

        internal static float P2(EaseType e) => e >= EaseType.ElasticIn && e <= EaseType.ElasticInOut ? 0.3f : 0f;

        static float E(EaseType e, float t) => Easing.Evaluate(e, P1(e), P2(e), t);

        [Test]
        public void EveryCurveEndsExactlyAtZeroAndOne()
        {
            foreach (var e in Curves)
            {
                Assert.That(E(e, 0f), Is.EqualTo(0f), $"{e} at 0");
                Assert.That(E(e, 1f), Is.EqualTo(1f), $"{e} at 1");
            }
        }

        [Test]
        public void OutMirrorsIn()
        {
            for (int family = 0; family < 10; family++)
            {
                var inCurve = (EaseType)(1 + 3 * family);
                var outCurve = (EaseType)(2 + 3 * family);
                for (int i = 0; i <= 100; i++)
                {
                    float t = i / 100f;
                    Assert.That(E(outCurve, t), Is.EqualTo(1f - E(inCurve, 1f - t)).Within(1e-6f), $"{outCurve} at {t}");
                }
            }
        }

        [Test]
        public void InOutIsPointSymmetricAndContinuousAtHalf()
        {
            for (int family = 0; family < 10; family++)
            {
                var e = (EaseType)(3 + 3 * family);
                for (int i = 0; i <= 100; i++)
                {
                    float t = i / 100f;
                    Assert.That(E(e, t), Is.EqualTo(1f - E(e, 1f - t)).Within(1e-5f), $"{e} at {t}");
                }
                Assert.That(E(e, 0.4999f), Is.EqualTo(E(e, 0.5001f)).Within(0.02f), $"{e} at 0.5");
            }
        }

        [Test]
        public void FamiliesWithoutOvershootAreMonotone()
        {
            foreach (var e in Curves.Where(e => e < EaseType.BackIn))
            {
                float previous = float.NegativeInfinity;
                for (int i = 0; i <= 1000; i++)
                {
                    float v = E(e, i / 1000f);
                    Assert.That(v, Is.GreaterThanOrEqualTo(previous - 1e-6f), $"{e} at {i}");
                    previous = v;
                }
            }
        }

        [TestCase(0.5f, 0.3f)]
        [TestCase(0f, 0.3f)]
        [TestCase(1f, 0f)]
        [TestCase(1f, -1f)]
        public void ElasticOutsideItsDomainStaysFinite(float amplitude, float period)
        {
            foreach (var e in new[] { EaseType.ElasticIn, EaseType.ElasticOut, EaseType.ElasticInOut })
                for (int i = 0; i <= 100; i++)
                    Assert.That(float.IsFinite(Easing.Evaluate(e, amplitude, period, i / 100f)), $"{e} at {i}");
        }

        [Test]
        public void InputIsCutToTheUnitInterval()
        {
            foreach (var e in Curves)
            {
                Assert.That(E(e, -1f), Is.EqualTo(E(e, 0f)), $"{e} below");
                Assert.That(E(e, 2f), Is.EqualTo(E(e, 1f)), $"{e} above");
            }
        }

        [Test]
        public void NonFiniteInputsGiveFiniteValues()
        {
            float[] odd = { float.NaN, float.PositiveInfinity, float.NegativeInfinity };
            foreach (var e in Curves)
                foreach (float w in odd)
                {
                    Assert.That(float.IsFinite(Easing.Evaluate(e, P1(e), P2(e), w)), $"{e} t {w}");
                    Assert.That(float.IsFinite(Easing.Evaluate(e, w, P2(e), 0.3f)), $"{e} p1 {w}");
                    Assert.That(float.IsFinite(Easing.Evaluate(e, P1(e), w, 0.3f)), $"{e} p2 {w}");
                }
        }

        // Steps: n even steps, each jump at its step's end, t = 1 the last · n is P1 rounded half up, 1 ~ 1000
        [Test]
        public void StepsJumpAtEachStepsEnd()
        {
            static float S(float p1, float t) => Easing.Evaluate(EaseType.Steps, p1, 0f, t);
            Assert.That(new[] { 0f, 0.2499f, 0.25f, 0.5f, 0.9999f, 1f }.Select(t => S(4f, t)), Is.EqualTo(new[] { 0f, 0f, 0.25f, 0.5f, 0.75f, 1f }));
            foreach (float p1 in new[] { float.NaN, 0f, -3f, float.NegativeInfinity })
                Assert.That((S(p1, 0.9f), S(p1, 1f)), Is.EqualTo((0f, 1f)), $"P1 {p1} is one step");
            Assert.That(S(2.6f, 0.5f), Is.EqualTo(1f / 3f), "2.6 rounds to 3 steps");
            foreach (float p1 in new[] { 1e9f, float.PositiveInfinity })
                Assert.That(S(p1, 0.0015f), Is.EqualTo(0.001f), $"P1 {p1} is 1000 steps");
            Assert.That((S(4f, -1f), S(4f, float.NaN), S(4f, 2f)), Is.EqualTo((0f, 0f, 1f)), "the input cut to [0, 1]");
        }

        [Test]
        public void CustomAndUnknownValuesAreLinear()
        {
            Assert.That(Easing.Evaluate(EaseType.Custom, 0f, 0f, 0.3f), Is.EqualTo(0.3f));
            Assert.That(Easing.Evaluate((EaseType)99, 0f, 0f, 0.3f), Is.EqualTo(0.3f));
            Assert.That(Easing.Evaluate((EaseType)(-1), 0f, 0f, 0.3f), Is.EqualTo(0.3f));
        }
    }
}
