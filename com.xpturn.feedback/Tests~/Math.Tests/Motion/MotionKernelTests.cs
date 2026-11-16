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
        public void FadeIsExactlyOneWithoutFades()
        {
            foreach (float t in new[] { 0f, 0.5f, 1f, -1f, 2f })
            {
                Assert.That(MotionKernels.Fade(t, 0f, 0f, out bool ramp), Is.EqualTo(1f), $"t {t}");
                Assert.That(ramp, Is.False, $"ramp at {t}");
            }
        }

        [Test]
        public void FadeRampsLinearlyInAndOut()
        {
            const float fadeIn = 0.2f, fadeOut = 0.3f;
            for (int i = 1; i < 1000; i++)
            {
                float t = i / 1000f;
                float height = MotionKernels.Fade(t, fadeIn, fadeOut, out bool ramp);
                bool onSlope = t < fadeIn || t > 1f - fadeOut;
                float expected = t < fadeIn ? t / fadeIn : t > 1f - fadeOut ? (1f - t) / fadeOut : 1f;
                Assert.That(ramp, Is.EqualTo(onSlope), $"ramp at {t}");
                Assert.That(height, Is.EqualTo(expected).Within(1e-6f), $"height at {t}");
            }
        }

        // the ends of a fade are 0 off the slope, so no ease reaches them; a side without a fade keeps its end at 1
        [Test]
        public void FadeEndsAreExactlyZero()
        {
            foreach (float t in new[] { 0f, -0.5f })
            {
                Assert.That(MotionKernels.Fade(t, 0.2f, 0.3f, out bool ramp), Is.EqualTo(0f), $"start {t}");
                Assert.That(ramp, Is.False, $"start {t} ramp");
            }
            foreach (float t in new[] { 1f, 1.5f })
            {
                Assert.That(MotionKernels.Fade(t, 0.2f, 0.3f, out bool ramp), Is.EqualTo(0f), $"end {t}");
                Assert.That(ramp, Is.False, $"end {t} ramp");
            }
            Assert.That((MotionKernels.Fade(1f, 0.2f, 0f, out bool inOnly), inOnly), Is.EqualTo((1f, false)), "Fade In only — the end");
            Assert.That((MotionKernels.Fade(0f, 0f, 0.3f, out bool outOnly), outOnly), Is.EqualTo((1f, false)), "Fade Out only — the start");
        }

        [Test]
        public void FadeShrinksSharesOverOne()
        {
            // 0.6 + 0.9 → 0.4 + 0.6
            Assert.That(MotionKernels.Fade(0.2f, 0.6f, 0.9f, out bool rise), Is.EqualTo(0.5f).Within(1e-6f), "in");
            Assert.That(rise, Is.True);
            Assert.That(MotionKernels.Fade(0.7f, 0.6f, 0.9f, out bool fall), Is.EqualTo(0.5f).Within(1e-6f), "out");
            Assert.That(fall, Is.True);
        }

        // NaN · ±∞ · a negative share are no fade on that side; a non-finite t is cut
        [Test]
        public void FadeOddInputsAreCut()
        {
            foreach (float w in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.5f })
            {
                Assert.That((MotionKernels.Fade(0.5f, w, w, out bool none), none), Is.EqualTo((1f, false)), $"both {w}");
                Assert.That(MotionKernels.Fade(0.1f, w, 0.3f, out bool a), Is.EqualTo(MotionKernels.Fade(0.1f, 0f, 0.3f, out bool a0)), $"in {w}");
                Assert.That(a, Is.EqualTo(a0), $"in {w} ramp");
                Assert.That(MotionKernels.Fade(0.9f, 0.2f, w, out bool b), Is.EqualTo(MotionKernels.Fade(0.9f, 0.2f, 0f, out bool b0)), $"out {w}");
                Assert.That(b, Is.EqualTo(b0), $"out {w} ramp");
            }
            foreach (float w in Odd)
                Assert.That(MotionKernels.Fade(w, 0.2f, 0.3f, out _), Is.InRange(0f, 1f), $"t {w}");
        }

        static readonly (int Count, float E)[] Bounces = { (3, 0.5f), (5, 0.7f), (2, 0.3f), (4, 0.999f), (10, 0.05f), (10, 0.9f) };

        // hop k starts at d₀(1 − eᵏ)/(1 − e), lasts d₀eᵏ and peaks at e²ᵏ — set up here from the definition, not from the kernel
        static (double Start, double Length, double Height) Hop(int count, float restitution, int k)
        {
            double e = restitution, d0 = (1.0 - e) / (1.0 - Math.Pow(e, count));
            return (d0 * (1.0 - Math.Pow(e, k)) / (1.0 - e), d0 * Math.Pow(e, k), Math.Pow(e, 2 * k));
        }

        [Test]
        public void BounceEndsAreExactlyZero()
        {
            foreach (var (count, e) in Bounces)
                foreach (float t in new[] { 0f, 1f, -0.5f, 1.5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    Assert.That(MotionKernels.Bounce(t, count, e), Is.EqualTo(0f), $"{count} {e} at {t}");
        }

        [Test]
        public void BounceOfOneHopIsAParabola()
        {
            for (int i = 0; i <= 1000; i++)
            {
                float t = i / 1000f;
                Assert.That(MotionKernels.Bounce(t, 1, 0.5f), Is.EqualTo(4.0 * t * (1.0 - t)).Within(1e-6), $"t {t}");
            }
        }

        // inside every hop the value is its height · 4u(1 − u); at the middle it is the peak e²ᵏ, at a seam 0
        [Test]
        public void BounceHopsFollowTheRestitution()
        {
            foreach (var (count, e) in Bounces)
                for (int k = 0; k < count; k++)
                {
                    var (start, length, height) = Hop(count, e, k);
                    if (length < 1e-4)
                        continue;
                    for (int j = 1; j < 20; j++)
                    {
                        float t = (float)(start + length * j / 20.0);
                        double u = (t - start) / length;
                        Assert.That(MotionKernels.Bounce(t, count, e), Is.EqualTo(height * 4.0 * u * (1.0 - u)).Within(1e-5), $"{count} {e} hop {k} at {j}/20");
                    }
                    Assert.That(MotionKernels.Bounce((float)(start + length / 2.0), count, e), Is.EqualTo(height).Within(1e-5), $"{count} {e} peak {k}");
                    if (k > 0)
                        Assert.That(MotionKernels.Bounce((float)start, count, e), Is.EqualTo(0f).Within(1e-5), $"{count} {e} seam {k}");
                }
        }

        [Test]
        public void BounceLastHopEndsAtOne()
        {
            foreach (var (count, e) in Bounces)
            {
                Assert.That(MotionKernels.Bounce(0.99999994f, count, e), Is.InRange(0f, 1e-5f), $"{count} {e}");
                for (int i = 0; i <= 2000; i++)
                    Assert.That(MotionKernels.Bounce(i / 2000f, count, e), Is.InRange(0f, 1f), $"{count} {e} at {i}");
            }
        }

        [Test]
        public void BounceAtFullRestitutionHasEqualHops()
        {
            const int count = 4;
            for (int k = 0; k < count; k++)
            {
                Assert.That(MotionKernels.Bounce((k + 0.5f) / count, count, 1f), Is.EqualTo(1f), $"peak {k}");
                Assert.That(MotionKernels.Bounce((k + 0.25f) / count, count, 1f), Is.EqualTo(0.75f), $"quarter {k}");
                if (k > 0)
                    Assert.That(MotionKernels.Bounce((float)k / count, count, 1f), Is.EqualTo(0f), $"seam {k}");
            }
        }

        // Count is cut to 1 – 10, Restitution to 0.05 – 1 (NaN and −∞ to 0.05)
        [Test]
        public void BounceOddInputsAreCut()
        {
            for (int i = 1; i < 50; i++)
            {
                float t = i / 50f;
                Assert.That(MotionKernels.Bounce(t, 0, 0.5f), Is.EqualTo(MotionKernels.Bounce(t, 1, 0.5f)), $"count 0 at {t}");
                Assert.That(MotionKernels.Bounce(t, -3, 0.5f), Is.EqualTo(MotionKernels.Bounce(t, 1, 0.5f)), $"count −3 at {t}");
                Assert.That(MotionKernels.Bounce(t, 11, 0.7f), Is.EqualTo(MotionKernels.Bounce(t, 10, 0.7f)), $"count 11 at {t}");
                Assert.That(MotionKernels.Bounce(t, int.MaxValue, 0.7f), Is.EqualTo(MotionKernels.Bounce(t, 10, 0.7f)), $"count max at {t}");
                foreach (float e in new[] { float.NaN, float.NegativeInfinity, -1f, 0f, 0.01f })
                    Assert.That(MotionKernels.Bounce(t, 3, e), Is.EqualTo(MotionKernels.Bounce(t, 3, 0.05f)), $"e {e} at {t}");
                foreach (float e in new[] { 2f, float.PositiveInfinity })
                    Assert.That(MotionKernels.Bounce(t, 3, e), Is.EqualTo(MotionKernels.Bounce(t, 3, 1f)), $"e {e} at {t}");
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

        // the soft cap's reference, in double: the identity up to half the limit, then knee + room · tanh((x − knee) / room)
        static double Capped(double x, double limit)
        {
            double knee = limit / 2.0, room = limit - knee;
            return x <= knee ? x : knee + room * Math.Tanh((x - knee) / room);
        }

        [TestCase(1f)]
        [TestCase(0.3f)]
        [TestCase(12f)]
        public void SoftLimitKeepsUpToHalfAndNeverPassesTheLimit(float limit)
        {
            for (int i = 0; i <= 400; i++)
            {
                float x = limit * i / 100f;
                float y = MotionKernels.SoftLimit(x, limit);
                if (x <= limit / 2f)
                    Assert.That(BitConverter.SingleToInt32Bits(y), Is.EqualTo(BitConverter.SingleToInt32Bits(x)), "up to the knee, unchanged: " + x);
                Assert.That(y, Is.LessThanOrEqualTo(limit), "never past the limit: " + x);
                Assert.That((double)y, Is.EqualTo(Capped(x, limit)).Within(1e-6 * limit), "the curve at " + x);
            }
            Assert.That(MotionKernels.SoftLimit(limit, limit), Is.EqualTo(0.5f * limit + 0.5f * limit * (float)Math.Tanh(1.0)).Within(1e-6f * limit), "at the limit");
        }

        [Test]
        public void SoftLimitIsMonotoneAndLeavesTheKneeWithSlopeOne()
        {
            const float limit = 2f;
            float previous = 0f;
            for (int i = 1; i <= 2000; i++)
            {
                float y = MotionKernels.SoftLimit(i * 0.005f, limit);
                Assert.That(y, Is.GreaterThanOrEqualTo(previous), "monotone at " + i * 0.005f);
                previous = y;
            }
            const double h = 1e-3;
            double above = (MotionKernels.SoftLimit(1f + (float)h, limit) - MotionKernels.SoftLimit(1f, limit)) / h;
            double below = (MotionKernels.SoftLimit(1f, limit) - MotionKernels.SoftLimit(1f - (float)h, limit)) / h;
            Assert.That(above, Is.EqualTo(1.0).Within(2e-3), "slope just above the knee");
            Assert.That(below, Is.EqualTo(1.0).Within(2e-3), "slope just below the knee");
        }

        [Test]
        public void SoftLimitOddInputsAreDecided()
        {
            Assert.That(MotionKernels.SoftLimit(0f, 1f), Is.EqualTo(0f), "0");
            Assert.That(MotionKernels.SoftLimit(-3f, 1f), Is.EqualTo(0f), "negative");
            Assert.That(MotionKernels.SoftLimit(float.NaN, 1f), Is.EqualTo(0f), "NaN");
            Assert.That(MotionKernels.SoftLimit(float.PositiveInfinity, 1f), Is.EqualTo(1f), "+∞ at the limit");
            foreach (float limit in new[] { 0f, -1f, float.NaN })
                Assert.That(MotionKernels.SoftLimit(0.4f, limit), Is.EqualTo(0f), "no room — limit " + limit);
            Assert.That(MotionKernels.SoftLimit(1e30f, float.PositiveInfinity), Is.EqualTo(1e30f), "an endless limit caps nothing");
        }

        // trauma: adds stop at 1, the fade is linear and stops at 0, the shape is t^exponent — references in double
        [Test]
        public void TraumaAddStopsAtOne()
        {
            Assert.That(MotionKernels.TraumaAdd(0f, 0.3f), Is.EqualTo(0.3f));
            Assert.That(MotionKernels.TraumaAdd(0.5f, 0.5f), Is.EqualTo(1f));
            float t = 0f;
            for (int i = 0; i < 3; i++)
                t = MotionKernels.TraumaAdd(t, 0.5f);
            Assert.That(t, Is.EqualTo(1f), "three of 0.5 stop at 1");
            Assert.That(MotionKernels.TraumaAdd(0.4f, -0.2f), Is.EqualTo(0.4f), "a negative add leaves it");
            Assert.That(MotionKernels.TraumaAdd(0.4f, float.NaN), Is.EqualTo(0.4f), "a NaN add leaves it");
            Assert.That(MotionKernels.TraumaAdd(float.NaN, 0.2f), Is.EqualTo(0.2f), "a NaN level reads as 0");
            Assert.That(MotionKernels.TraumaAdd(3f, 0.2f), Is.EqualTo(1f), "a level past 1 reads as 1");
        }

        [Test]
        public void TraumaDecayIsLinearAndStopsAtZero()
        {
            float t = 1f;
            int steps = 0;
            while (t > 0f && steps < 1000)
            {
                double expected = Math.Max(0.0, t - 2.0 * 0.01);
                t = MotionKernels.TraumaDecay(t, 2f, 0.01);
                Assert.That((double)t, Is.EqualTo(expected).Within(1e-6), "step " + steps);
                steps++;
            }
            Assert.That(steps, Is.InRange(50, 51), "a full trauma at 2 a second lasts 0.5 s of 0.01 steps (a float level may leave a last crumb)");
            Assert.That(MotionKernels.TraumaDecay(0.3f, 1f, 10.0), Is.EqualTo(0f), "a long step stops at 0");
            Assert.That(MotionKernels.TraumaDecay(0.3f, 0f, 0.1), Is.EqualTo(0.3f), "no decay");
            Assert.That(MotionKernels.TraumaDecay(0.3f, 1f, -0.1), Is.EqualTo(0.3f), "a negative step");
            Assert.That(MotionKernels.TraumaDecay(0.3f, float.NaN, 0.1), Is.EqualTo(0.3f), "a NaN decay");
            Assert.That(MotionKernels.TraumaDecay(0.3f, 1f, double.NaN), Is.EqualTo(0.3f), "a NaN step");
        }

        [Test]
        public void TraumaShapeIsThePower()
        {
            foreach (float e in new[] { 1f, 2f, 3f, 1.5f })
            {
                for (int i = 0; i <= 20; i++)
                {
                    float t = i / 20f;
                    Assert.That((double)MotionKernels.TraumaShape(t, e), Is.EqualTo(Math.Pow(t, e)).Within(1e-6), "t " + t + " · e " + e);
                }
            }
            Assert.That(MotionKernels.TraumaShape(0.5f, 0.2f), Is.EqualTo(MotionKernels.TraumaShape(0.5f, 1f)), "the exponent within 1 ~ 3 — below");
            Assert.That(MotionKernels.TraumaShape(0.5f, 9f), Is.EqualTo(MotionKernels.TraumaShape(0.5f, 3f)), "above");
            Assert.That(MotionKernels.TraumaShape(0.5f, float.NaN), Is.EqualTo(0.5f), "a NaN exponent reads as 1");
            Assert.That(MotionKernels.TraumaShape(float.NaN, 2f), Is.EqualTo(0f), "a NaN level reads as 0");
        }
    }
}
