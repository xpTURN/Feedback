using Unity.Cinemachine;
using UnityEngine;

namespace xpTURN.Feedback
{
    // an impulse's signal of our own — the velocity times the shape at t / duration, no rotation (Cinemachine's definition
    // signal, without its allocation per event)
    internal sealed class CinemachineImpulseSignal : ISignalSource6D
    {
        internal Vector3 Velocity;
        internal float Duration = 0.2f;
        readonly AnimationCurve m_Curve;

        internal CinemachineImpulseSignal(AnimationCurve curve)
        {
            m_Curve = curve;
        }

        public float SignalDuration => Duration;

        public void GetSignal(float timeSinceSignalStart, out Vector3 pos, out Quaternion rot)
        {
            pos = Velocity * m_Curve.Evaluate(timeSinceSignalStart / Duration);
            rot = Quaternion.identity;
        }
    }

    // Cinemachine's standard impulse shapes — its keys (the package keeps them internal; the two versions' are the same)
    internal static class CinemachineImpulseCurves
    {
        static AnimationCurve[] s_Shapes;
        static AnimationCurve s_Fallback;

        internal static AnimationCurve Of(CinemachineImpulseShape shape, AnimationCurve custom)
        {
            if (shape == CinemachineImpulseShape.Custom)
                return custom != null && custom.length >= 2 ? custom : s_Fallback ??= AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            s_Shapes ??= Make();
            return s_Shapes[(int)shape];
        }

        static AnimationCurve Curve(params Keyframe[] keys)
        {
            var c = new AnimationCurve(keys);
            c.preWrapMode = WrapMode.ClampForever;
            c.postWrapMode = WrapMode.ClampForever;
            return c;
        }

        static AnimationCurve[] Make()
        {
            var shapes = new AnimationCurve[5];
            shapes[(int)CinemachineImpulseShape.Recoil] = Curve(new Keyframe(0f, 1f, -3.2f, -3.2f), new Keyframe(1f, 0f, 0f, 0f));
            shapes[(int)CinemachineImpulseShape.Bump] = Curve(new Keyframe(0f, 0f, -4.9f, -4.9f), new Keyframe(0.2f, 0f, 8.25f, 8.25f), new Keyframe(1f, 0f, -0.25f, -0.25f));
            shapes[(int)CinemachineImpulseShape.Explosion] = Curve(new Keyframe(0f, -1.4f, -7.9f, -7.9f), new Keyframe(0.27f, 0.78f, 23.4f, 23.4f), new Keyframe(0.54f, -0.12f, 22.6f, 22.6f),
                new Keyframe(0.75f, 0.042f, 9.23f, 9.23f), new Keyframe(0.9f, -0.02f, 5.8f, 5.8f), new Keyframe(0.95f, -0.006f, -3f, -3f), new Keyframe(1f, 0f, 0f, 0f));
            shapes[(int)CinemachineImpulseShape.Rumble] = Curve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.1f, 0.25f, 0f, 0f), new Keyframe(0.2f, 0f, 0f, 0f), new Keyframe(0.3f, 0.75f, 0f, 0f),
                new Keyframe(0.4f, 0f, 0f, 0f), new Keyframe(0.5f, 1f, 0f, 0f), new Keyframe(0.6f, 0f, 0f, 0f), new Keyframe(0.7f, 0.75f, 0f, 0f), new Keyframe(0.8f, 0f, 0f, 0f),
                new Keyframe(0.9f, 0.25f, 0f, 0f), new Keyframe(1f, 0f, 0f, 0f));
            return shapes;
        }
    }
}
