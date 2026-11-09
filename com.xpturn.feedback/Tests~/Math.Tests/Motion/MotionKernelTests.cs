using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Motion
{
    // Punch · Warp · waves · trapezoid · Blink
    public sealed class MotionKernelTests
    {
        static readonly float[] Odd = { float.NaN, float.PositiveInfinity, float.NegativeInfinity };

        [TestCase(5f, 1f)]
        [TestCase(3f, 0.5f)]
        [TestCase(2.25f, 2f)]
        [TestCase(1f, 1f)]
        public void PunchStartsAndEndsAtZeroWithinOne(float vibrato, float decay)
        {
            Assert.That(MotionKernels.Punch(0f, vibrato, decay) == 0f, "start");
            Assert.That(MotionKernels.Punch(1f, vibrato, decay) == 0f, "end (a value comparison — −0 counts)");
            for (int i = 0; i <= 1000; i++)
                Assert.That(MathF.Abs(MotionKernels.Punch(i / 1000f, vibrato, decay)), Is.LessThanOrEqualTo(1f));
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(8)]
        public void PunchCrossesZeroTwiceVibratoLessOne(int vibrato)
        {
            int zeros = 0;
            float previous = MotionKernels.Punch(1e-5f, vibrato, 1f);
            for (int i = 1; i < 100000; i++)
            {
                float current = MotionKernels.Punch(i / 100000f + 5e-6f, vibrato, 1f);
                if ((current > 0f) != (previous > 0f))
                    zeros++;
                previous = current;
            }
            Assert.That(zeros, Is.EqualTo(2 * vibrato - 1));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(-3f)]
        public void PunchDecayOutsideItsDomainStaysFiniteAndEndsAtZero(float decay)
        {
            for (int i = 0; i <= 100; i++)
                Assert.That(float.IsFinite(MotionKernels.Punch(i / 100f, 5f, decay)));
            Assert.That(MotionKernels.Punch(1f, 2.25f, decay) == 0f);
        }

        [Test]
        public void PunchNonFiniteInputsGiveFiniteValues()
        {
            foreach (float w in Odd)
            {
                Assert.That(float.IsFinite(MotionKernels.Punch(w, 5f, 1f)), $"t {w}");
                Assert.That(float.IsFinite(MotionKernels.Punch(0.3f, w, 1f)), $"vibrato {w}");
                Assert.That(float.IsFinite(MotionKernels.Punch(0.3f, 5f, w)), $"decay {w}");
            }
        }

        [Test]
        public void WarpEndsExactlyAndStaysWithinOne()
        {
            foreach (int loops in new[] { 1, 2, 3, 4 })
                foreach (bool yoyo in new[] { false, true })
                {
                    Assert.That(MotionKernels.Warp(1f, loops, yoyo), Is.EqualTo(yoyo && loops % 2 == 0 ? 0f : 1f), $"end {loops} {yoyo}");
                    Assert.That(MotionKernels.Warp(0f, loops, yoyo), Is.EqualTo(0f), $"start {loops} {yoyo}");
                    for (int i = 0; i <= 4000; i++)
                        Assert.That(MotionKernels.Warp(i / 4000f, loops, yoyo), Is.InRange(0f, 1f));
                }
        }

        [Test]
        public void WarpYoyoIsContinuousAcrossLoops()
        {
            foreach (int loops in new[] { 2, 3, 4 })
                for (int k = 1; k < loops; k++)
                {
                    float before = MotionKernels.Warp((k - 1e-4f) / loops, loops, true);
                    float after = MotionKernels.Warp((k + 1e-4f) / loops, loops, true);
                    Assert.That(after, Is.EqualTo(before).Within(0.01f), $"{loops} loops at {k}");
                }
        }

        [Test]
        public void WarpLoopsBelowOneAreOne()
        {
            Assert.That(MotionKernels.Warp(0.3f, 0, false), Is.EqualTo(MotionKernels.Warp(0.3f, 1, false)));
            Assert.That(MotionKernels.Warp(0.3f, -3, true), Is.EqualTo(MotionKernels.Warp(0.3f, 1, true)));
        }

        [Test]
        public void WavesArePeriodicWithinTheirRange()
        {
            for (int w = 0; w < 5; w++)
            {
                float lower = (Waveform)w == Waveform.PingPong ? 0f : -1f;
                for (int i = 0; i < 10000; i++)
                {
                    double x = i * 0.00731;
                    float v = MotionKernels.Wave((Waveform)w, x);
                    Assert.That(v, Is.EqualTo(MotionKernels.Wave((Waveform)w, x + 3.0)).Within(1e-5f), $"{(Waveform)w} period");
                    Assert.That(v, Is.InRange(lower, 1f), $"{(Waveform)w} range");
                }
            }
        }

        [Test]
        public void SquareIsOnHalfThePeriod()
        {
            int on = 0;
            const int n = 100000;
            for (int i = 0; i < n; i++)
                if (MotionKernels.Wave(Waveform.Square, (i + 0.5) / n) > 0f)
                    on++;
            Assert.That(on / (double)n, Is.EqualTo(0.5).Within(1e-3));
        }

        [Test]
        public void WaveNonFiniteInputsGiveFiniteValues()
        {
            foreach (double x in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1e300 })
                for (int w = 0; w < 6; w++)
                    Assert.That(float.IsFinite(MotionKernels.Wave((Waveform)w, x)), $"{w} {x}");
        }

        [Test]
        public void TrapezoidShrinksRatiosOverOne()
        {
            Assert.That(MotionKernels.Trapezoid(0.99f, 0.6f, 0.6f, 0.6f, out _), Is.LessThan(0.1f));
        }

        [Test]
        public void TrapezoidRampsLinearlyOnItsSlopes()
        {
            const float a = 0.2f, h = 0.3f, r = 0.2f;
            for (int i = 0; i <= 1000; i++)
            {
                float t = i / 1000f;
                float height = MotionKernels.Trapezoid(t, a, h, r, out bool ramp);
                bool onSlope = t < a || (t >= a + h && t < a + h + r);
                float expected = t < a ? t / a : t < a + h ? 1f : t < a + h + r ? 1f - (t - a - h) / r : 0f;
                Assert.That(ramp, Is.EqualTo(onSlope), $"ramp at {t}");
                Assert.That(height, Is.EqualTo(expected).Within(1e-5f), $"height at {t}");
            }
        }

        [Test]
        public void TrapezoidNonFiniteInputsGiveFiniteValues()
        {
            foreach (float w in Odd)
            {
                Assert.That(float.IsFinite(MotionKernels.Trapezoid(w, 0.2f, 0.3f, 0.2f, out _)));
                Assert.That(float.IsFinite(MotionKernels.Trapezoid(0.3f, w, 0.3f, 0.2f, out _)));
                Assert.That(float.IsFinite(MotionKernels.Trapezoid(0.3f, 0.2f, w, 0.2f, out _)));
                Assert.That(float.IsFinite(MotionKernels.Trapezoid(0.3f, 0.2f, 0.3f, w, out _)));
            }
        }

        [Test]
        public void BlinkIsOnForDutyAndTheSameAtTheSameSecond()
        {
            int on = 0;
            const int n = 200000;
            for (int i = 0; i < n; i++)
            {
                double s = i * 0.0005;
                bool lit = MotionKernels.Blink(3u, s, 0.1f, 0.3f, 0f, true);
                Assert.That(MotionKernels.Blink(3u, s, 0.1f, 0.3f, 0f, true), Is.EqualTo(lit));
                if (lit)
                    on++;
            }
            Assert.That(on / (double)n, Is.EqualTo(0.3).Within(0.01));
        }

        [Test]
        public void BlinkJitterVariesEachPeriodAroundDuty()
        {
            var lengths = new HashSet<int>();
            double sum = 0.0;
            const int periods = 1000;
            for (int k = 0; k < periods; k++)
            {
                int on = 0;
                for (int i = 0; i < 1000; i++)
                    if (MotionKernels.Blink(3u, k * 0.1 + (i + 0.5) * 1e-4, 0.1f, 0.3f, 0.5f, true))
                        on++;
                lengths.Add(on);
                sum += on / 1000.0;
            }
            Assert.That(lengths.Count, Is.GreaterThan(50));
            Assert.That(sum / periods, Is.EqualTo(0.3).Within(0.02));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void BlinkPeriodOutsideItsDomainKeepsDuty(float period)
        {
            int on = 0;
            const int n = 10000;
            for (int i = 0; i < n; i++)
                if (MotionKernels.Blink(3u, i * 1.37e-5 + 3e-6, period, 0.3f, 0f, true))
                    on++;
            Assert.That(on / (double)n, Is.EqualTo(0.3).Within(0.05));
        }

        [Test]
        public void BlinkStartOnFalseKeepsDuty()
        {
            int on = 0;
            const int n = 100000;
            for (int i = 0; i < n; i++)
                if (MotionKernels.Blink(3u, (i + 0.37) * 1e-3, 0.1f, 0.3f, 0f, false))
                    on++;
            Assert.That(on / (double)n, Is.EqualTo(0.3).Within(0.01));
        }

        [Test]
        public void BlinkNonFiniteInputsAreDecided()
        {
            foreach (float w in Odd)
            {
                Assert.DoesNotThrow(() => MotionKernels.Blink(3u, 0.37, w, 0.3f, 0.2f, true));
                Assert.DoesNotThrow(() => MotionKernels.Blink(3u, 0.37, 0.1f, w, 0.2f, true));
                Assert.DoesNotThrow(() => MotionKernels.Blink(3u, 0.37, 0.1f, 0.3f, w, true));
            }
            foreach (double s in new[] { double.NaN, double.PositiveInfinity, 1e300 })
                Assert.That(MotionKernels.Blink(3u, s, 0.1f, 0.3f, 0.2f, true), Is.EqualTo(MotionKernels.Blink(3u, s, 0.1f, 0.3f, 0.2f, true)));
        }
    }
}
