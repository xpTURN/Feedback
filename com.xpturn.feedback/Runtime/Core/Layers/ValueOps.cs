using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the value operations the slots and the bindings use — motions stay four components. Structs, so a constrained call
    // specializes. FromVector4(Vector4.zero) is the identity of every composition (a ledger cell's start value)
    internal interface IValueOps<T>
    {
        T FromVector4(in Vector4 v);

        Vector4 ToVector4(in T v);

        T Add(in T a, in T b);

        // a ⊙ (1 + o)
        T MulOffset(in T a, in T o);

        // unclamped — the composition clamps the weight
        T Lerp(in T a, in T b, float t);

        bool BitEquals(in T a, in T b);
    }

    internal struct FloatOps : IValueOps<float>
    {
        public float FromVector4(in Vector4 v) => v.x;

        public Vector4 ToVector4(in float v) => new Vector4(v, 0f, 0f, 0f);

        public float Add(in float a, in float b) => a + b;

        public float MulOffset(in float a, in float o) => a * (1f + o);

        public float Lerp(in float a, in float b, float t) => a + (b - a) * t;

        public bool BitEquals(in float a, in float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
    }

    internal struct Vector2Ops : IValueOps<Vector2>
    {
        public Vector2 FromVector4(in Vector4 v) => new Vector2(v.x, v.y);

        public Vector4 ToVector4(in Vector2 v) => new Vector4(v.x, v.y, 0f, 0f);

        public Vector2 Add(in Vector2 a, in Vector2 b) => a + b;

        public Vector2 MulOffset(in Vector2 a, in Vector2 o) => new Vector2(a.x * (1f + o.x), a.y * (1f + o.y));

        public Vector2 Lerp(in Vector2 a, in Vector2 b, float t) => Vector2.LerpUnclamped(a, b, t);

        public bool BitEquals(in Vector2 a, in Vector2 b) => B(a.x) == B(b.x) && B(a.y) == B(b.y);

        static int B(float x) => BitConverter.SingleToInt32Bits(x);
    }

    internal struct Vector3Ops : IValueOps<Vector3>
    {
        public Vector3 FromVector4(in Vector4 v) => new Vector3(v.x, v.y, v.z);

        public Vector4 ToVector4(in Vector3 v) => new Vector4(v.x, v.y, v.z, 0f);

        public Vector3 Add(in Vector3 a, in Vector3 b) => a + b;

        public Vector3 MulOffset(in Vector3 a, in Vector3 o) => new Vector3(a.x * (1f + o.x), a.y * (1f + o.y), a.z * (1f + o.z));

        public Vector3 Lerp(in Vector3 a, in Vector3 b, float t) => Vector3.LerpUnclamped(a, b, t);

        public bool BitEquals(in Vector3 a, in Vector3 b) => B(a.x) == B(b.x) && B(a.y) == B(b.y) && B(a.z) == B(b.z);

        static int B(float x) => BitConverter.SingleToInt32Bits(x);
    }

    internal struct Vector4Ops : IValueOps<Vector4>
    {
        public Vector4 FromVector4(in Vector4 v) => v;

        public Vector4 ToVector4(in Vector4 v) => v;

        public Vector4 Add(in Vector4 a, in Vector4 b) => a + b;

        public Vector4 MulOffset(in Vector4 a, in Vector4 o) => new Vector4(a.x * (1f + o.x), a.y * (1f + o.y), a.z * (1f + o.z), a.w * (1f + o.w));

        public Vector4 Lerp(in Vector4 a, in Vector4 b, float t) => Vector4.LerpUnclamped(a, b, t);

        public bool BitEquals(in Vector4 a, in Vector4 b) => B(a.x) == B(b.x) && B(a.y) == B(b.y) && B(a.z) == B(b.z) && B(a.w) == B(b.w);

        static int B(float x) => BitConverter.SingleToInt32Bits(x);
    }

    internal struct ColorOps : IValueOps<Color>
    {
        public Color FromVector4(in Vector4 v) => new Color(v.x, v.y, v.z, v.w);

        public Vector4 ToVector4(in Color v) => new Vector4(v.r, v.g, v.b, v.a);

        public Color Add(in Color a, in Color b) => a + b;

        public Color MulOffset(in Color a, in Color o) => new Color(a.r * (1f + o.r), a.g * (1f + o.g), a.b * (1f + o.b), a.a * (1f + o.a));

        public Color Lerp(in Color a, in Color b, float t) => Color.LerpUnclamped(a, b, t);

        public bool BitEquals(in Color a, in Color b) => B(a.r) == B(b.r) && B(a.g) == B(b.g) && B(a.b) == B(b.b) && B(a.a) == B(b.a);

        static int B(float x) => BitConverter.SingleToInt32Bits(x);
    }

    // a rotation offset composes as the product v · q(o) — Add is the product and Lerp slerps. ToVector4 gives eulerAngles
    // (0 ~ 360°)
    internal struct QuaternionOps : IValueOps<Quaternion>
    {
        public Quaternion FromVector4(in Vector4 v) => Quaternion.Euler(v.x, v.y, v.z);

        public Vector4 ToVector4(in Quaternion v)
        {
            Vector3 e = v.eulerAngles;
            return new Vector4(e.x, e.y, e.z, 0f);
        }

        public Quaternion Add(in Quaternion a, in Quaternion b) => a * b;

        public Quaternion MulOffset(in Quaternion a, in Quaternion o) => a * o;

        public Quaternion Lerp(in Quaternion a, in Quaternion b, float t) => Quaternion.SlerpUnclamped(a, b, t);

        public bool BitEquals(in Quaternion a, in Quaternion b) => B(a.x) == B(b.x) && B(a.y) == B(b.y) && B(a.z) == B(b.z) && B(a.w) == B(b.w);

        static int B(float x) => BitConverter.SingleToInt32Bits(x);
    }
}
