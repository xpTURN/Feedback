using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the eleven motion data classes — the type names are the saved format. Defaults come from the parameters' Default (a new
    // one per object)
    [Serializable]
    public sealed class SetMotion : FeedbackMotion
    {
        [SerializeField] internal SetParams m_Params = SetParams.Default;

        internal override MotionKind Kind => MotionKind.Set;
        internal override float NaturalDuration => 0f;
    }

    [Serializable]
    public sealed class TweenMotion : FeedbackMotion
    {
        [SerializeField] internal TweenParams m_Params = TweenParams.Default;

        internal override MotionKind Kind => MotionKind.Tween;
    }

    [Serializable]
    public sealed class PunchMotion : FeedbackMotion
    {
        [SerializeField] internal PunchParams m_Params = PunchParams.Default;

        internal override MotionKind Kind => MotionKind.Punch;
    }

    [Serializable]
    public sealed class ShakeMotion : FeedbackMotion
    {
        [SerializeField] internal ShakeParams m_Params = ShakeParams.Default;

        internal override MotionKind Kind => MotionKind.Shake;
    }

    [Serializable]
    public sealed class CurveMotion : FeedbackMotion
    {
        [SerializeField] internal CurveParams m_Params = CurveParams.Default;

        internal override MotionKind Kind => MotionKind.Curve;
    }

    [Serializable]
    public sealed class FlashMotion : FeedbackMotion
    {
        [SerializeField] internal FlashParams m_Params = FlashParams.Default;

        internal override MotionKind Kind => MotionKind.Flash;
    }

    [Serializable]
    public sealed class GradientMotion : FeedbackMotion
    {
        [SerializeField] internal GradientParams m_Params = GradientParams.Default;

        internal override MotionKind Kind => MotionKind.Gradient;
    }

    [Serializable]
    public sealed class OscillateMotion : FeedbackMotion
    {
        [SerializeField] internal OscillateParams m_Params = OscillateParams.Default;

        internal override MotionKind Kind => MotionKind.Oscillate;
    }

    [Serializable]
    public sealed class BlinkMotion : FeedbackMotion
    {
        [SerializeField] internal BlinkParams m_Params = BlinkParams.Default;

        internal override MotionKind Kind => MotionKind.Blink;

        // ⌈length / period⌉, at least one — an endless Blink counts the most an int holds
        internal override int FlashCount(double seconds)
            => double.IsInfinity(seconds) || seconds > int.MaxValue ? int.MaxValue : System.Math.Max(1, (int)System.Math.Ceiling(seconds / m_Params.m_Period));
    }

    [Serializable]
    public sealed class WiggleMotion : FeedbackMotion
    {
        [SerializeField] internal WiggleParams m_Params = WiggleParams.Default;

        internal override MotionKind Kind => MotionKind.Wiggle;
    }

    [Serializable]
    public sealed class SpringMotion : FeedbackMotion
    {
        [SerializeField] internal SpringParams m_Params = SpringParams.Default;

        internal override MotionKind Kind => MotionKind.Spring;
        internal override float NaturalDuration => m_Params.SettleTime();
    }
}
