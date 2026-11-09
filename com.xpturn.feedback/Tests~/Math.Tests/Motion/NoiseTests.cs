using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Motion
{
    // deterministic, within −1 ~ 1, continuous and C², varying near 10⁷ and past int, independent seeds, the ±1-gradient
    // RMS, sine-like zero crossings, non-finite inputs
    public sealed class NoiseTests
    {
        [Test]
        public void SameSeedAndPointGiveTheSameValueWithinOne()
        {
            for (int i = 0; i < 100000; i++)
            {
                double x = i * 0.0137;
                float smooth = Noise.Smooth(7u, x);
                Assert.That(Noise.Smooth(7u, x), Is.EqualTo(smooth));
                Assert.That(MathF.Abs(smooth), Is.LessThanOrEqualTo(1f));
                Assert.That(MathF.Abs(Noise.Jitter(7u, 3f, x)), Is.LessThanOrEqualTo(1f));
                for (int octaves = 1; octaves <= 3; octaves++)
                    Assert.That(MathF.Abs(Noise.Fbm(7u, 2f, x, octaves)), Is.LessThanOrEqualTo(1f));
            }
        }

        [Test]
        public void SmoothIsContinuous()
        {
            const double d = 1e-4;
            for (int i = 0; i < 100000; i++)
            {
                double x = i * 0.0137;
                Assert.That(Math.Abs(Noise.Smooth(7u, x + d) - Noise.Smooth(7u, x)), Is.LessThanOrEqualTo(4 * d), $"{x}");
            }
        }

        [TestCase(1e7)]
        [TestCase(3e9)]
        public void SmoothStaysSmoothAndVariesFarOut(double origin)
        {
            const double d = 1e-4;
            double sum2 = 0.0;
            var seen = new HashSet<float>();
            for (int i = 0; i < 10000; i++)
            {
                double x = origin + i * 0.0137;
                float n = Noise.Smooth(7u, x);
                Assert.That(MathF.Abs(n), Is.LessThanOrEqualTo(1f));
                Assert.That(Math.Abs(Noise.Smooth(7u, x + d) - n), Is.LessThanOrEqualTo(4 * d), $"{x}");
                sum2 += n * n;
                seen.Add(n);
            }
            Assert.That(Math.Sqrt(sum2 / 10000), Is.GreaterThan(0.3));
            Assert.That(seen.Count, Is.GreaterThan(1000));
        }

        [Test]
        public void SmoothIsC2AtLatticePoints()
        {
            const double h = 1e-3;
            for (int i = 1; i < 2000; i++)
            {
                double x = i;
                double left = (Noise.Smooth(7u, x - 2 * h) - 2 * Noise.Smooth(7u, x - h) + Noise.Smooth(7u, x)) / (h * h);
                double right = (Noise.Smooth(7u, x) - 2 * Noise.Smooth(7u, x + h) + Noise.Smooth(7u, x + 2 * h)) / (h * h);
                Assert.That(right, Is.EqualTo(left).Within(0.5), $"{x}");
            }
        }

        [Test]
        public void DifferentSeedsAreUncorrelated()
        {
            for (uint p = 0; p < 20; p++)
            {
                uint a = Hash32.Of(11u, 2 * p);
                uint b = Hash32.Of(11u, 2 * p + 1);
                double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
                const int n = 10000;
                for (int i = 0; i < n; i++)
                {
                    double x = i + 0.5;
                    double u = Noise.Smooth(a, x), v = Noise.Smooth(b, x);
                    sx += u; sy += v; sxx += u * u; syy += v * v; sxy += u * v;
                }
                double r = (sxy - sx * sy / n) / Math.Sqrt((sxx - sx * sx / n) * (syy - sy * sy / n));
                Assert.That(Math.Abs(r), Is.LessThan(0.1), $"pair {p}");
            }
        }

        [Test]
        public void SmoothRmsIsThatOfPlusMinusOneGradients()
        {
            double sum2 = 0.0;
            const int n = 200000;
            for (int i = 0; i < n; i++)
            {
                float v = Noise.Smooth(7u, i * 0.0137 + 0.005);
                sum2 += v * v;
            }
            Assert.That(Math.Sqrt(sum2 / n), Is.EqualTo(0.49).Within(0.03));
        }

        [Test]
        public void SmoothHzCrossesZeroLikeASineOfTheSameFrequency()
        {
            int zeros = 0;
            float previous = Noise.SmoothHz(5u, 10f, 1e-4);
            const int n = 1000000;
            const double seconds = 1000.0;
            for (int i = 1; i < n; i++)
            {
                float current = Noise.SmoothHz(5u, 10f, i * seconds / n + 1e-4);
                if ((current > 0f) != (previous > 0f))
                    zeros++;
                previous = current;
            }
            Assert.That(zeros / seconds / 20.0, Is.EqualTo(1.0).Within(0.01));
        }

        [Test]
        public void NonFiniteInputsGiveFiniteValues()
        {
            foreach (double s in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e300 })
            {
                Assert.That(float.IsFinite(Noise.Smooth(1u, s)), $"x {s}");
                Assert.That(float.IsFinite(Noise.SmoothHz(1u, 5f, s)), $"s {s}");
                Assert.That(float.IsFinite(Noise.Jitter(1u, 5f, s)), $"jitter s {s}");
                Assert.That(float.IsFinite(Noise.Fbm(1u, 5f, s, 3)), $"fbm s {s}");
            }
            foreach (float f in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Assert.That(float.IsFinite(Noise.SmoothHz(1u, f, 3.0)), $"F {f}");
                Assert.That(float.IsFinite(Noise.Jitter(1u, f, 3.0)), $"jitter F {f}");
                Assert.That(float.IsFinite(Noise.Fbm(1u, f, 3.0, 2)), $"fbm F {f}");
            }
        }
    }
}
