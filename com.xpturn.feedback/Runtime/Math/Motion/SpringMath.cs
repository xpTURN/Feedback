using System;

namespace xpTURN.Feedback
{
    // a spring whose frequency and damping went through the domain — the only way into Y · Step · SettleTime, so no
    // caller can forget the cut
    internal readonly struct SpringShape
    {
        public readonly float Zeta;
        public readonly float Omega0;

        SpringShape(float zeta, float omega0)
        {
            Zeta = zeta;
            Omega0 = omega0;
        }

        internal static SpringShape Of(float frequency, float damping)
            => new SpringShape(MotionKernels.Within(damping, 0.01f, 1000f), 2f * MathF.PI * MotionKernels.Within(frequency, 1e-3f, 1000f));
    }

    // (y, v) ← (A11·y + A12·v, A21·y + A22·v) — the exact step
    internal struct SpringCoeffs
    {
        public float A11;
        public float A12;
        public float A21;
        public float A22;
    }

    // the damped oscillator's closed form in three regions — critical within 10⁻⁴ of ζ = 1, underdamped, and
    // overdamped as two exponentials with the stable slow root −ω₀ / (ζ + √(ζ² − 1))
    internal static class SpringMath
    {
        const float Band = 1e-4f;
        internal const int MaxSettleSteps = 1000;

        internal static float Y(in SpringShape shape, float y0, float v0, float t)
        {
            y0 = MotionKernels.Finite(y0);
            v0 = MotionKernels.Finite(v0);
            t = MotionKernels.Within(t, 0f, 1e6f);
            float z = shape.Zeta;
            float w0 = shape.Omega0;
            if (MathF.Abs(z - 1f) < Band)
                return MathF.Exp(-w0 * t) * (y0 + (v0 + w0 * y0) * t);
            if (z < 1f)
            {
                float wd = w0 * MathF.Sqrt(1f - z * z);
                return MathF.Exp(-z * w0 * t) * (y0 * MathF.Cos(wd * t) + (v0 + z * w0 * y0) / wd * MathF.Sin(wd * t));
            }
            float q = MathF.Sqrt(z * z - 1f);
            float r1 = -w0 / (z + q);
            float r2 = -w0 * (z + q);
            float c1 = (v0 - r2 * y0) / (r1 - r2);
            return c1 * MathF.Exp(r1 * t) + (y0 - c1) * MathF.Exp(r2 * t);
        }

        internal static float SettleTime(in SpringShape shape, float settle, bool moveTo) => SettleTime(shape, settle, moveTo, out _);

        // seconds until the response stays within settle × its amplitude (MoveTo y0 = 1 · Bump v0 = ω₀). Underdamped is
        // the envelope bound; otherwise the response is walked on its slow root's grid from its peak and bisected. A
        // response that never exceeds settle has settled at 0
        internal static float SettleTime(in SpringShape shape, float settle, bool moveTo, out int steps)
        {
            steps = 0;
            float epsilon = MotionKernels.Within(settle, 1e-6f, 0.99f);
            double z = shape.Zeta;
            double w0 = shape.Omega0;
            if (z < 1.0 - Band)
            {
                double wd = w0 * Math.Sqrt(1.0 - z * z);
                return (float)Math.Max(0.0, Math.Log(w0 / (wd * epsilon)) / (z * w0));
            }
            double slow = Math.Abs(z - 1.0) < Band ? w0 : w0 / (z + Math.Sqrt(z * z - 1.0));
            double h = 1.0 / (8.0 * slow);
            float y0 = moveTo ? 1f : 0f;
            float v0 = moveTo ? 0f : shape.Omega0;
            float previous = MathF.Abs(Y(shape, y0, v0, 0f));
            bool above = previous > epsilon;
            bool falling = moveTo;
            double previousT = 0.0;
            for (int s = 1; s <= MaxSettleSteps; s++)
            {
                steps = s;
                double t = s * h;
                float current = MathF.Abs(Y(shape, y0, v0, (float)t));
                if (current > epsilon)
                    above = true;
                if (!falling && current < previous)
                    falling = true;
                if (falling && current <= epsilon)
                {
                    if (!above)
                        return 0f;
                    double lo = previousT;
                    double hi = t;
                    for (int b = 0; b < 20; b++)
                    {
                        double mid = 0.5 * (lo + hi);
                        if (MathF.Abs(Y(shape, y0, v0, (float)mid)) > epsilon)
                            lo = mid;
                        else
                            hi = mid;
                    }
                    return (float)hi;
                }
                previous = current;
                previousT = t;
            }
            return (float)(MaxSettleSteps * h);
        }

        // dt: NaN · ≤ 0 is 0 (identity), beyond 10⁶ s is 10⁶ s — a public struct's state is never made NaN or diverging
        internal static SpringCoeffs Step(in SpringShape shape, float dt)
        {
            dt = !(dt > 0f) ? 0f : dt > 1e6f ? 1e6f : dt;
            float z = shape.Zeta;
            float w0 = shape.Omega0;
            SpringCoeffs m;
            if (MathF.Abs(z - 1f) < Band)
            {
                float e = MathF.Exp(-w0 * dt);
                m.A11 = e * (1f + w0 * dt);
                m.A12 = e * dt;
                m.A21 = -e * w0 * w0 * dt;
                m.A22 = e * (1f - w0 * dt);
            }
            else if (z < 1f)
            {
                float wd = w0 * MathF.Sqrt(1f - z * z);
                float e = MathF.Exp(-z * w0 * dt);
                float c = MathF.Cos(wd * dt);
                float s = MathF.Sin(wd * dt);
                m.A11 = e * (c + z * w0 / wd * s);
                m.A12 = e * s / wd;
                m.A21 = -e * w0 * w0 / wd * s;
                m.A22 = e * (c - z * w0 / wd * s);
            }
            else
            {
                float q = MathF.Sqrt(z * z - 1f);
                float r1 = -w0 / (z + q);
                float r2 = -w0 * (z + q);
                float d = r1 - r2;
                float e1 = MathF.Exp(r1 * dt);
                float e2 = MathF.Exp(r2 * dt);
                m.A11 = (-r2 * e1 + r1 * e2) / d;
                m.A12 = (e1 - e2) / d;
                m.A21 = r1 * r2 * (e2 - e1) / d;
                m.A22 = (r1 * e1 - r2 * e2) / d;
            }
            return m;
        }
    }
}
