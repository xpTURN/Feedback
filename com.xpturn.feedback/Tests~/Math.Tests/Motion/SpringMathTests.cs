using System;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Motion
{
    // The ranges are never taken from the kernel under test: RK4 runs to a fixed horizon and the settle time is compared
    // with a dense double scan
    public sealed class SpringMathTests
    {
        static readonly float[] Odd = { float.NaN, float.PositiveInfinity, float.NegativeInfinity };

        static readonly float[] Zetas =
        {
            0.001f, 0.05f, 0.4f, 0.99f, 1f - 0.5e-4f, 1f + 0.5e-4f, 1f - 1.5e-4f, 1f + 1.5e-4f, 1f - 3e-4f, 1f + 3e-4f,
            1f - 9e-4f, 1f + 9e-4f, 1f, 1.01f, 3f, 10f, 100f,
        };

        static double Reference(double z, double w0, double y0, double v0, double t)
        {
            if (Math.Abs(z - 1.0) < 1e-4)
                return Math.Exp(-w0 * t) * (y0 + (v0 + w0 * y0) * t);
            if (z < 1.0)
            {
                double wd = w0 * Math.Sqrt(1.0 - z * z);
                return Math.Exp(-z * w0 * t) * (y0 * Math.Cos(wd * t) + (v0 + z * w0 * y0) / wd * Math.Sin(wd * t));
            }
            double q = Math.Sqrt(z * z - 1.0), r1 = -w0 / (z + q), r2 = -w0 * (z + q), c1 = (v0 - r2 * y0) / (r1 - r2);
            return c1 * Math.Exp(r1 * t) + (y0 - c1) * Math.Exp(r2 * t);
        }

        static double SlowRate(double z, double w0)
            => z < 1.0 - 1e-4 ? z * w0 : Math.Abs(z - 1.0) < 1e-4 ? w0 : w0 / (z + Math.Sqrt(z * z - 1.0));

        static void Apply(in SpringCoeffs m, ref float y, ref float v)
        {
            float ny = m.A11 * y + m.A12 * v;
            v = m.A21 * y + m.A22 * v;
            y = ny;
        }

        [Test]
        public void ClosedFormMatchesRk4ToAFixedHorizon()
        {
            foreach (float zeta in Zetas)
            {
                var shape = SpringShape.Of(6f, zeta);
                double w0 = shape.Omega0, z = shape.Zeta;
                foreach (bool moveTo in new[] { true, false })
                {
                    double horizon = 60.0 / (z < 1.0 ? w0 : SlowRate(z, w0));
                    double y = moveTo ? 1.0 : 0.0, v = moveTo ? 0.0 : 1.0, scale = moveTo ? 1.0 : 1.0 / w0;
                    const double h = 1e-5;
                    int n = (int)(horizon / h);
                    double worst = 0.0;
                    for (int i = 0; i <= n; i++)
                    {
                        if (i % 1000 == 0)
                        {
                            float c = SpringMath.Y(shape, moveTo ? 1f : 0f, moveTo ? 0f : 1f, (float)(i * h));
                            worst = Math.Max(worst, float.IsFinite(c) ? Math.Abs(c - y) / scale : double.PositiveInfinity);
                        }
                        double k1y = v, k1v = -w0 * w0 * y - 2 * z * w0 * v;
                        double k2y = v + h / 2 * k1v, k2v = -w0 * w0 * (y + h / 2 * k1y) - 2 * z * w0 * (v + h / 2 * k1v);
                        double k3y = v + h / 2 * k2v, k3v = -w0 * w0 * (y + h / 2 * k2y) - 2 * z * w0 * (v + h / 2 * k2v);
                        double k4y = v + h * k3v, k4v = -w0 * w0 * (y + h * k3y) - 2 * z * w0 * (v + h * k3v);
                        y += h / 6 * (k1y + 2 * k2y + 2 * k3y + k4y);
                        v += h / 6 * (k1v + 2 * k2v + 2 * k3v + k4v);
                    }
                    Assert.That(worst, Is.LessThan(1e-4), $"zeta {zeta} moveTo {moveTo}");
                }
            }
        }

        [Test]
        public void StepsCompose()
        {
            var random = new XorShiftRandom(9u);
            foreach (float zeta in new[] { 0.05f, 0.4f, 1f, 3f })
            {
                var shape = SpringShape.Of(6f, zeta);
                for (int j = 0; j < 2000; j++)
                {
                    float a = random.Next() % 10000 * 1e-5f, b = random.Next() % 10000 * 1e-5f;
                    float y1 = 1f, v1 = 0.3f * shape.Omega0, y2 = y1, v2 = v1;
                    Apply(SpringMath.Step(shape, a), ref y1, ref v1);
                    Apply(SpringMath.Step(shape, b), ref y1, ref v1);
                    Apply(SpringMath.Step(shape, a + b), ref y2, ref v2);
                    Assert.That(y1, Is.EqualTo(y2).Within(1e-5f), $"zeta {zeta}");
                    Assert.That(v1 / shape.Omega0, Is.EqualTo(v2 / shape.Omega0).Within(1e-5f), $"zeta {zeta}");
                }
            }
        }

        [Test]
        public void AfterTheSettleTimeTheResponseStaysWithinTheThreshold()
        {
            const float epsilon = 0.001f;
            foreach (float zeta in new[] { 0.05f, 0.4f, 0.9f, 0.999f, 1f, 1.5f, 3f })
                foreach (bool moveTo in new[] { true, false })
                {
                    var shape = SpringShape.Of(6f, zeta);
                    float settle = SpringMath.SettleTime(shape, epsilon, moveTo);
                    Assert.That(settle, Is.GreaterThan(0f));
                    for (int i = 0; i <= 2000; i++)
                    {
                        float t = settle * (1f + 2f * i / 2000);
                        float y = moveTo ? SpringMath.Y(shape, 1f, 0f, t) : SpringMath.Y(shape, 0f, 1f, t) * shape.Omega0;
                        Assert.That(MathF.Abs(y), Is.LessThanOrEqualTo(epsilon * 1.0001f), $"zeta {zeta} moveTo {moveTo} t/ts {t / settle}");
                    }
                }
        }

        // the numeric region matches the last exceedance within 10⁻⁴; the underdamped formula is the envelope bound
        [Test]
        public void SettleTimeMatchesADenseReference()
        {
            foreach (float zeta in new[] { 0.4f, 1f, 1.5f, 10f })
                foreach (float epsilon in new[] { 1e-6f, 1e-3f, 0.2f, 0.5f })
                    foreach (bool moveTo in new[] { true, false })
                    {
                        var shape = SpringShape.Of(6f, zeta);
                        double w0 = shape.Omega0, z = shape.Zeta;
                        float settle = SpringMath.SettleTime(shape, epsilon, moveTo);
                        double horizon = (Math.Log(1.0 / epsilon) + 25.0) / SlowRate(z, w0), last = 0.0;
                        const int n = 400000;
                        for (int i = 0; i <= n; i++)
                        {
                            double t = horizon * i / n;
                            if (Math.Abs(Reference(z, w0, moveTo ? 1 : 0, moveTo ? 0 : w0, t)) > epsilon)
                                last = t;
                        }
                        string at = $"zeta {zeta} epsilon {epsilon} moveTo {moveTo}";
                        if (z < 1.0 - 1e-4)
                            Assert.That(settle, Is.GreaterThanOrEqualTo(last * (1 - 1e-4)), at);
                        else
                            Assert.That(settle, Is.EqualTo(last).Within(1e-4 * last + 1e-5), at);
                    }
        }

        [Test]
        public void BumpSettlesBeforeMoveToWhenOverdamped()
        {
            foreach (float zeta in new[] { 1.2f, 10f, 100f })
            {
                var shape = SpringShape.Of(6f, zeta);
                Assert.That(SpringMath.SettleTime(shape, 0.001f, false), Is.LessThan(SpringMath.SettleTime(shape, 0.001f, true)), $"zeta {zeta}");
            }
        }

        [Test]
        public void SettleWalkStaysUnderItsCap()
        {
            foreach (float zeta in new[] { 1f, 3f, 100f, 1000f })
                foreach (bool moveTo in new[] { true, false })
                {
                    SpringMath.SettleTime(SpringShape.Of(6f, zeta), 1e-6f, moveTo, out int steps);
                    Assert.That(steps, Is.LessThanOrEqualTo(SpringMath.MaxSettleSteps / 4), $"zeta {zeta} moveTo {moveTo}");
                }
        }

        [TestCase(0f, 0.4f, 0.001f)]
        [TestCase(-1f, 0.4f, 0.001f)]
        [TestCase(6f, 0f, 0.001f)]
        [TestCase(6f, -0.1f, 0.001f)]
        [TestCase(6f, 0.4f, 0f)]
        [TestCase(6f, 0.4f, 1f)]
        [TestCase(6f, 0.4f, 2f)]
        [TestCase(6f, 1.5f, 1f)]
        public void OutOfDomainSettleTimeIsFiniteAndNotNegative(float frequency, float damping, float settle)
        {
            foreach (bool moveTo in new[] { true, false })
            {
                float d = SpringMath.SettleTime(SpringShape.Of(frequency, damping), settle, moveTo);
                Assert.That(float.IsFinite(d) && d >= 0f, $"{d}");
            }
        }

        [Test]
        public void NonFiniteParametersStayFinite()
        {
            foreach (float w in Odd)
                foreach (var shape in new[] { SpringShape.Of(w, 1.5f), SpringShape.Of(6f, w), SpringShape.Of(w, w) })
                {
                    Assert.That(float.IsFinite(shape.Zeta) && float.IsFinite(shape.Omega0) && shape.Omega0 > 0f, $"shape {w}");
                    Assert.That(float.IsFinite(SpringMath.Y(shape, 1f, 0f, 0.1f)), $"Y {w}");
                    var m = SpringMath.Step(shape, 1f / 60f);
                    Assert.That(float.IsFinite(m.A11) && float.IsFinite(m.A12) && float.IsFinite(m.A21) && float.IsFinite(m.A22), $"Step {w}");
                    foreach (bool moveTo in new[] { true, false })
                    {
                        float d = SpringMath.SettleTime(shape, 0.001f, moveTo, out int steps);
                        Assert.That(float.IsFinite(d) && d >= 0f && steps <= SpringMath.MaxSettleSteps, $"settle {w}");
                    }
                }
            var s = SpringShape.Of(6f, 1.5f);
            foreach (float w in Odd)
            {
                Assert.That(float.IsFinite(SpringMath.SettleTime(s, w, true)), $"settle arg {w}");
                Assert.That(float.IsFinite(SpringMath.Y(s, w, w, w)), $"Y args {w}");
            }
        }

        [Test]
        public void StepWithAnOddDtKeepsTheStateFinite()
        {
            var shape = SpringShape.Of(6f, 0.4f);
            foreach (float dt in new[] { float.NaN, float.PositiveInfinity, -0.5f, -1e30f, 1e30f })
            {
                float y = 1f, v = 0f;
                Apply(SpringMath.Step(shape, dt), ref y, ref v);
                Assert.That(float.IsFinite(y) && float.IsFinite(v) && MathF.Abs(y) <= 1.01f, $"dt {dt} → {y}");
                Apply(SpringMath.Step(shape, 1f / 60f), ref y, ref v);
                Assert.That(float.IsFinite(y), $"dt {dt} then a normal step");
            }
        }
    }
}
