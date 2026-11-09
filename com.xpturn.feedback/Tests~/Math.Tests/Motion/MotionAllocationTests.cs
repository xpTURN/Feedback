using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Motion
{
    // every kernel allocates 0 B after two warm-ups. The called list is checked against the kernel types' methods by
    // reflection — a hand list misses a new kernel
    public sealed class MotionAllocationTests
    {
        static readonly Type[] KernelTypes =
        {
            typeof(Easing), typeof(MotionKernels), typeof(Noise), typeof(SpringMath), typeof(SpringShape), typeof(Hash32), typeof(XorShiftRandom),
            typeof(SpringStepper),
        };

        static readonly float[] Zetas = { 0.4f, 1f, 3f };

        static string Key(MethodInfo m) => $"{m.DeclaringType.Name}.{m.Name}/{m.GetParameters().Length}";

        static readonly (string Key, Func<double> Run)[] Calls =
        {
            ("Easing.Evaluate/4", static () =>
            {
                double s = 0;
                for (int e = 0; e < 32; e++)
                    for (int i = 0; i <= 16; i++)
                        s += Easing.Evaluate((EaseType)e, 1.70158f, 0.3f, i / 16f);
                return s;
            }),
            ("MotionKernels.Punch/3", static () =>
            {
                double s = 0;
                for (int i = 0; i <= 64; i++)
                    s += MotionKernels.Punch(i / 64f, 5f, 1f);
                return s;
            }),
            ("MotionKernels.Wave/2", static () =>
            {
                double s = 0;
                for (int w = 0; w < 5; w++)
                    for (int i = 0; i < 64; i++)
                        s += MotionKernels.Wave((Waveform)w, i * 0.037);
                return s;
            }),
            ("MotionKernels.Warp/3", static () =>
            {
                double s = 0;
                for (int i = 0; i <= 64; i++)
                    s += MotionKernels.Warp(i / 64f, 3, true) + MotionKernels.Warp(i / 64f, 2, false);
                return s;
            }),
            ("MotionKernels.Trapezoid/5", static () =>
            {
                double s = 0;
                for (int i = 0; i <= 64; i++)
                {
                    s += MotionKernels.Trapezoid(i / 64f, 0.2f, 0.3f, 0.2f, out bool ramp);
                    if (ramp)
                        s += 1;
                }
                return s;
            }),
            ("MotionKernels.Blink/6", static () =>
            {
                double s = 0;
                for (int i = 0; i < 64; i++)
                    if (MotionKernels.Blink(3u, i * 0.013, 0.1f, 0.3f, 0.5f, (i & 1) == 0))
                        s += 1;
                return s;
            }),
            ("MotionKernels.Clamp01/1", static () => MotionKernels.Clamp01(1.5f) + MotionKernels.Clamp01(float.NaN)),
            ("MotionKernels.Within/3", static () => MotionKernels.Within(3f, 0f, 1f) + MotionKernels.Within(float.NaN, 0f, 1f)),
            ("MotionKernels.AtLeast/2", static () => MotionKernels.AtLeast(-3f, 0.01f) + MotionKernels.AtLeast(float.PositiveInfinity, 0f)),
            ("MotionKernels.Finite/1", static () => MotionKernels.Finite(2f) + MotionKernels.Finite(float.NaN)),
            ("MotionKernels.Seconds/1", static () => MotionKernels.Seconds(2.0) + MotionKernels.Seconds(1e300)),
            ("MotionKernels.Coordinate/1", static () => MotionKernels.Coordinate(2.0) + MotionKernels.Coordinate(double.NaN)),
            ("Noise.Smooth/2", static () =>
            {
                double s = 0;
                for (int i = 0; i < 64; i++)
                    s += Noise.Smooth(7u, 3e9 + i * 0.37);
                return s;
            }),
            ("Noise.SmoothHz/3", static () =>
            {
                double s = 0;
                for (int i = 0; i < 64; i++)
                    s += Noise.SmoothHz(7u, 10f, i * 0.011);
                return s;
            }),
            ("Noise.Jitter/3", static () =>
            {
                double s = 0;
                for (int i = 0; i < 64; i++)
                    s += Noise.Jitter(7u, 3f, i * 0.1);
                return s;
            }),
            ("Noise.Fbm/4", static () =>
            {
                double s = 0;
                for (int i = 0; i < 64; i++)
                    s += Noise.Fbm(7u, 2f, i * 0.07, 1 + i % 3);
                return s;
            }),
            ("SpringShape.Of/2", static () =>
            {
                var shape = SpringShape.Of(6f, 0.4f);
                return shape.Zeta + shape.Omega0;
            }),
            ("SpringMath.Y/4", static () =>
            {
                double s = 0;
                foreach (float zeta in Zetas)
                {
                    var shape = SpringShape.Of(6f, zeta);
                    for (int i = 0; i < 16; i++)
                        s += SpringMath.Y(shape, 1f, 0f, i * 0.02f);
                }
                return s;
            }),
            ("SpringMath.SettleTime/3", static () => SpringMath.SettleTime(SpringShape.Of(6f, 1.5f), 1e-3f, true)),
            ("SpringMath.SettleTime/4", static () =>
            {
                double s = SpringMath.SettleTime(SpringShape.Of(6f, 3f), 1e-3f, false, out int steps);
                return s + steps + SpringMath.SettleTime(SpringShape.Of(6f, 0.4f), 1e-3f, true, out _);
            }),
            ("SpringMath.Step/2", static () =>
            {
                double s = 0;
                foreach (float zeta in Zetas)
                {
                    var m = SpringMath.Step(SpringShape.Of(6f, zeta), 1f / 60f);
                    s += m.A11 + m.A12 + m.A21 + m.A22;
                }
                return s;
            }),
            ("SpringStepper.Prepare/3", static () =>
            {
                var stepper = new SpringStepper();
                float value = 1f, velocity = 0f;
                for (int i = 0; i < 16; i++)
                {
                    stepper.Prepare(1f / 60f + i * 1e-4f, 6f, 0.4f);
                    stepper.Advance(ref value, ref velocity, 0f);
                }
                return value + velocity;
            }),
            ("SpringStepper.Advance/3", static () =>
            {
                var stepper = new SpringStepper();
                stepper.Prepare(1f / 60f, 6f, 0.4f);
                float value = 1f, velocity = 0f;
                for (int i = 0; i < 16; i++)
                    stepper.Advance(ref value, ref velocity, i == 5 ? float.NaN : 0f);
                return value + velocity;
            }),
            ("Hash32.Mix/1", static () => Hash32.Mix(12345u)),
            ("Hash32.Of/2", static () => Hash32.Of(77u, 3u)),
            ("Hash32.Tagged/3", static () => Hash32.Tagged(77u, Hash32.Chance, 3u)),
            ("Hash32.Fold/1", static () => Hash32.Fold(-3000000000L)),
            ("Hash32.Unit/2", static () => Hash32.Unit(77u, 3u)),
            ("Hash32.ToUnit/1", static () => Hash32.ToUnit(uint.MaxValue)),
            ("XorShiftRandom.Next/0", static () =>
            {
                var random = new XorShiftRandom(0u);
                double s = 0;
                for (int i = 0; i < 64; i++)
                    s += random.Next();
                return s;
            }),
            ("XorShiftRandom.NextUnit/0", static () =>
            {
                var random = default(XorShiftRandom);
                double s = 0;
                for (int i = 0; i < 64; i++)
                    s += random.NextUnit();
                return s;
            }),
        };

        [Test]
        public void TheCallListCoversEveryKernelMethod()
        {
            var declared = KernelTypes
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(m => m.IsAssembly || m.IsPublic || m.IsFamilyOrAssembly)
                .Select(Key)
                .OrderBy(k => k, StringComparer.Ordinal);
            Assert.That(Calls.Select(c => c.Key).OrderBy(k => k, StringComparer.Ordinal), Is.EqualTo(declared));
        }

        [Test]
        public void EveryKernelAllocatesNothingAfterWarmUp()
        {
            foreach (var (key, run) in Calls)
            {
                double first = run();
                run();
                long before = GC.GetAllocatedBytesForCurrentThread();
                double last = run();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(last, Is.EqualTo(first), $"{key} repeats the warm-up");
                Assert.That(allocated, Is.Zero, key);
            }
        }
    }
}
