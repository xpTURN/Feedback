using System;

namespace xpTURN.Feedback
{
    // the shape kernels and the domain rule every kernel input goes through.
    // Out-of-domain values arrive legitimately (saved data, public fields, code), so they are cut, never asserted:
    // NaN goes to the lower bound (0 if none), ±∞ to the end (0 if none). Written as comparisons — Max · Clamp spread NaN
    internal static class MotionKernels
    {
        internal static float Punch(float t, float vibrato, float decay)
        {
            t = Clamp01(t);
            vibrato = AtLeast(vibrato, 0f);
            decay = AtLeast(decay, 0.01f);
            return MathF.Sin(2f * MathF.PI * vibrato * t) * MathF.Pow(1f - t, decay);
        }

        internal static float Wave(Waveform waveform, double x)
        {
            x = Coordinate(x);
            double f = x - Math.Floor(x);
            switch (waveform)
            {
                case Waveform.Triangle:
                    return (float)(f < 0.25 ? 4.0 * f : f < 0.75 ? 2.0 - 4.0 * f : 4.0 * f - 4.0);
                case Waveform.Square:
                    return f < 0.5 ? 1f : -1f;
                case Waveform.Saw:
                    return (float)(2.0 * f - 1.0);
                case Waveform.PingPong:
                    return (float)(f < 0.5 ? 2.0 * f : 2.0 - 2.0 * f);
                default:
                    return (float)Math.Sin(2.0 * Math.PI * f);
            }
        }

        // Tween's loop warp — T ≥ 1 is exact: the far end, or the start after an even count of yoyo loops
        internal static float Warp(float t, int loops, bool yoyo)
        {
            t = Clamp01(t);
            if (loops < 1)
                loops = 1;
            if (t >= 1f)
                return yoyo && (loops & 1) == 0 ? 0f : 1f;
            float p = t * loops;
            float k = MathF.Floor(p);
            float f = p - k;
            if (yoyo && ((int)k & 1) == 1)
                f = 1f - f;
            return f;
        }

        // the linear height; ramp is true on the attack and release slopes, where Flash puts its ease (Core)
        internal static float Trapezoid(float t, float attack, float hold, float release, out bool ramp)
        {
            t = Clamp01(t);
            attack = AtLeast(attack, 0f);
            hold = AtLeast(hold, 0f);
            release = AtLeast(release, 0f);
            float sum = attack + hold + release;
            if (sum > 1f)
            {
                attack /= sum;
                hold /= sum;
                release /= sum;
            }
            if (t < attack)
            {
                ramp = true;
                return t / attack;
            }
            if (t < attack + hold)
            {
                ramp = false;
                return 1f;
            }
            if (t < attack + hold + release)
            {
                ramp = true;
                return 1f - (t - attack - hold) / release;
            }
            ramp = false;
            return 0f;
        }

        // period k is lit for Duty · Period · (1 + Jitter · (2h − 1)); StartOn false moves the lit part to the period's end
        internal static bool Blink(uint seed, double seconds, float period, float duty, float jitter, bool startOn)
        {
            seconds = Seconds(seconds);
            period = Within(period, 1e-4f, 1e6f);
            duty = Clamp01(duty);
            jitter = Clamp01(jitter);
            long k = (long)Math.Floor(seconds / period);
            double h = Hash32.Unit(seed, Hash32.Fold(k));
            double on = duty * (double)period * (1.0 + jitter * (2.0 * h - 1.0));
            if (on < 0.0)
                on = 0.0;
            else if (on > period)
                on = period;
            double phase = seconds - k * (double)period;
            return startOn ? phase < on : phase >= period - on;
        }

        internal static float Clamp01(float x) => Within(x, 0f, 1f);

        internal static float Within(float x, float lower, float upper)
        {
            if (!(x >= lower))
                return lower;
            return x > upper ? upper : x;
        }

        internal static float AtLeast(float x, float lower)
        {
            x = Finite(x);
            return !(x >= lower) ? lower : x;
        }

        internal static float Finite(float x) => x >= -float.MaxValue && x <= float.MaxValue ? x : 0f;

        // item seconds: NaN · ±∞ are 0, the rest stays within ±10¹² so every later (long) conversion is defined
        internal static double Seconds(double s)
        {
            if (!(s > -double.MaxValue && s < double.MaxValue))
                return 0.0;
            return s > 1e12 ? 1e12 : s < -1e12 ? -1e12 : s;
        }

        // lattice and wave coordinates: NaN · ±∞ are 0, the rest stays within ±10¹⁵
        internal static double Coordinate(double x)
        {
            if (!(x > -double.MaxValue && x < double.MaxValue))
                return 0.0;
            return x > 1e15 ? 1e15 : x < -1e15 ? -1e15 : x;
        }
    }
}
