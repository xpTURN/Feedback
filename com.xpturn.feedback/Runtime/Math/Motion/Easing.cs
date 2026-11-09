using System;

namespace xpTURN.Feedback
{
    // the standard easing equations with exact ends — Expo is normalized and Elastic takes its ends apart. Custom is
    // the curve Core's Ease evaluates; here it is Linear, as is an unknown value
    internal static class Easing
    {
        internal static float Evaluate(EaseType type, float p1, float p2, float t)
        {
            t = MotionKernels.Clamp01(t);
            int index = (int)type - 1;
            if ((uint)index >= 30u)
                return t;
            int family = index / 3;
            switch (index % 3)
            {
                case 0:
                    return In(family, p1, p2, t);
                case 1:
                    return 1f - In(family, p1, p2, 1f - t);
                default:
                    return t < 0.5f ? 0.5f * In(family, p1, p2, 2f * t) : 1f - 0.5f * In(family, p1, p2, 2f - 2f * t);
            }
        }

        static float In(int family, float p1, float p2, float t)
        {
            switch (family)
            {
                case 0:
                    return 1f - MathF.Cos(t * MathF.PI * 0.5f);
                case 1:
                    return t * t;
                case 2:
                    return t * t * t;
                case 3:
                    return t * t * t * t;
                case 4:
                    return t * t * t * t * t;
                case 5:
                    return (MathF.Pow(2f, 10f * t) - 1f) / 1023f;
                case 6:
                    return 1f - MathF.Sqrt(MathF.Max(0f, 1f - t * t));
                case 7:
                {
                    float s = MotionKernels.Finite(p1);
                    return t * t * ((s + 1f) * t - s);
                }
                case 8:
                    return ElasticIn(p1, p2, t);
                default:
                    return 1f - BounceOut(1f - t);
            }
        }

        static float ElasticIn(float amplitude, float period, float t)
        {
            if (t <= 0f)
                return 0f;
            if (t >= 1f)
                return 1f;
            float a = MotionKernels.AtLeast(amplitude, 1f);
            float p = MotionKernels.Finite(period);
            if (!(p > 0f))
                p = 0.3f;
            float shift = p / (2f * MathF.PI) * MathF.Asin(1f / a);
            return -(a * MathF.Pow(2f, 10f * (t - 1f)) * MathF.Sin((t - 1f - shift) * 2f * MathF.PI / p));
        }

        static float BounceOut(float t)
        {
            const float n = 7.5625f;
            const float d = 2.75f;
            if (t < 1f / d)
                return n * t * t;
            if (t < 2f / d)
            {
                t -= 1.5f / d;
                return n * t * t + 0.75f;
            }
            if (t < 2.5f / d)
            {
                t -= 2.25f / d;
                return n * t * t + 0.9375f;
            }
            t -= 2.625f / d;
            return n * t * t + 0.984375f;
        }
    }
}
