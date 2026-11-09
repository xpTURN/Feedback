using UnityEngine;

namespace xpTURN.Feedback
{
    // how the target takes the value
    internal enum MotionWrite
    {
        Offset,
        Override,
        Persist,
    }

    // the evaluation seed (occurrence number and use tag mixed in) and the intensity
    internal struct MotionBegin
    {
        public uint Seed;
        public float Intensity;
    }

    // one per play record × item
    internal struct MotionState
    {
        public Vector4 Start;
        public uint Seed;
        public float Intensity;
    }

    // T is 0 ~ 1 (0 for an infinite item), Seconds the item's elapsed seconds — both the timeline's forward coordinate
    internal readonly struct MotionTime
    {
        public readonly float T;
        public readonly double Seconds;

        internal MotionTime(float t, double seconds)
        {
            T = t;
            Seconds = seconds;
        }
    }

    internal struct MotionSample
    {
        public Vector4 Value;
        public float Weight;
    }

    // stateless — N play records share one evaluator, each with its MotionState. The parameters are copied when made and
    // their Vector4 values cut to finite. Writes d components; the rest are 0
    internal abstract class MotionEvaluator
    {
        protected readonly int m_Dimension;

        // cut here so a direct construction (a camera copy) is cut too
        protected MotionEvaluator(int dimension)
        {
            m_Dimension = dimension < 1 ? 1 : dimension > 4 ? 4 : dimension;
        }

        internal abstract MotionWrite Write { get; }

        internal virtual bool IsInstant => false;

        // seconds, −1 = none (the item timing's duration) — the same value as the motion data's
        internal virtual float NaturalDuration => -1f;

        // current is the value before composition — not cut: a non-finite one is a broken target
        internal void Begin(ref MotionState s, in Vector4 current, in MotionBegin args)
        {
            s.Start = current;
            s.Seed = args.Seed;
            s.Intensity = MotionKernels.Finite(args.Intensity);
        }

        internal abstract void Evaluate(in MotionState s, in MotionTime time, out MotionSample sample);

        // the Offset motions' scalar shape (−1 ~ 1 × intensity, before the amplitude)
        internal virtual float EvaluateShape(in MotionState s, in MotionTime time) => 0f;

        protected static float Cap(float intensity) => MotionKernels.Clamp01(intensity);

        protected static Vector4 Finite(in Vector4 v)
            => new Vector4(MotionKernels.Finite(v.x), MotionKernels.Finite(v.y), MotionKernels.Finite(v.z), MotionKernels.Finite(v.w));

        // switch fall-through — the Vector4 indexer and per-component ternaries are slow in Mono
        protected static Vector4 Scaled(in Vector4 a, float k, int d)
        {
            Vector4 v = default;
            switch (d)
            {
                case 4: v.w = a.w * k; goto case 3;
                case 3: v.z = a.z * k; goto case 2;
                case 2: v.y = a.y * k; goto default;
                default: v.x = a.x * k; break;
            }
            return v;
        }

        protected static Vector4 AddScaled(in Vector4 s, in Vector4 a, float k, int d)
        {
            Vector4 v = default;
            switch (d)
            {
                case 4: v.w = s.w + a.w * k; goto case 3;
                case 3: v.z = s.z + a.z * k; goto case 2;
                case 2: v.y = s.y + a.y * k; goto default;
                default: v.x = s.x + a.x * k; break;
            }
            return v;
        }

        // the ends are chosen, not computed — a + (b − a)·1 is not b in narrow float (dotnet · IL2CPP)
        protected static Vector4 Lerp(in Vector4 a, in Vector4 b, float e, int d)
        {
            if (e == 1f)
                return Scaled(b, 1f, d);
            if (e == 0f)
                return Scaled(a, 1f, d);
            Vector4 v = default;
            switch (d)
            {
                case 4: v.w = a.w + (b.w - a.w) * e; goto case 3;
                case 3: v.z = a.z + (b.z - a.z) * e; goto case 2;
                case 2: v.y = a.y + (b.y - a.y) * e; goto default;
                default: v.x = a.x + (b.x - a.x) * e; break;
            }
            return v;
        }
    }
}
