using System;

namespace xpTURN.Feedback
{
    // 1D gradient noise — ±1 gradients, quintic fade (C²), ×2 to −1 ~ 1.
    // SmoothHz feeds (4/3)·F·s so the zero crossings match a sine of F Hz; Jitter changes value F times a second.
    // Lattice numbers floor to long and fold to uint — a double-to-int conversion out of range is undefined in C#
    internal static class Noise
    {
        internal static float Smooth(uint seed, double x)
        {
            x = MotionKernels.Coordinate(x);
            long n = (long)Math.Floor(x);
            double f = x - n;
            double g0 = (Hash32.Of(seed, Hash32.Fold(n)) & 1u) == 0u ? 1.0 : -1.0;
            double g1 = (Hash32.Of(seed, Hash32.Fold(n + 1)) & 1u) == 0u ? 1.0 : -1.0;
            double a = g0 * f;
            double b = g1 * (f - 1.0);
            double fade = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
            return (float)(2.0 * (a + (b - a) * fade));
        }

        internal static float SmoothHz(uint seed, float frequency, double seconds)
            => Smooth(seed, 4.0 / 3.0 * MotionKernels.Within(frequency, 0f, 1000f) * MotionKernels.Seconds(seconds));

        internal static float Jitter(uint seed, float frequency, double seconds)
        {
            double x = MotionKernels.Within(frequency, 0f, 1000f) * MotionKernels.Seconds(seconds);
            return (float)(2.0 * Hash32.Unit(seed, Hash32.Fold((long)Math.Floor(x))) - 1.0);
        }

        // octave o: seed tagged Octave · o, frequency × 2^o, amplitude × 0.5^o, divided by the amplitude sum (peak 1)
        internal static float Fbm(uint seed, float frequency, double seconds, int octaves)
        {
            if (octaves < 1)
                octaves = 1;
            else if (octaves > 3)
                octaves = 3;
            float octaveFrequency = MotionKernels.Within(frequency, 0f, 1000f);
            double sum = 0.0;
            double amplitude = 1.0;
            double norm = 0.0;
            for (int o = 0; o < octaves; o++)
            {
                sum += amplitude * SmoothHz(Hash32.Tagged(seed, Hash32.Octave, (uint)o), octaveFrequency, seconds);
                norm += amplitude;
                amplitude *= 0.5;
                octaveFrequency *= 2f;
            }
            return (float)(sum / norm);
        }
    }
}
