using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the saved ease — Custom reads the curve (null or no keys is Linear; a non-finite output — NaN keys arrive through
    // YAML — is 0), the rest is Math's Easing. internal: `Ease` is DOTween's and PrimeTween's public name, and the type name
    // is not saved
    [Serializable]
    internal struct Ease
    {
        [SerializeField] internal EaseType m_Type;
        [SerializeField] internal float m_P1;
        [SerializeField] internal float m_P2;
        [SerializeField] internal AnimationCurve m_Curve;

        internal static Ease Of(EaseType type)
        {
            switch (type)
            {
                case EaseType.BackIn:
                case EaseType.BackOut:
                case EaseType.BackInOut:
                    return new Ease { m_Type = type, m_P1 = 1.70158f };
                case EaseType.ElasticIn:
                case EaseType.ElasticOut:
                case EaseType.ElasticInOut:
                    return new Ease { m_Type = type, m_P1 = 1f, m_P2 = 0.3f };
                default:
                    return new Ease { m_Type = type };
            }
        }

        internal readonly float Evaluate(float t)
        {
            if (m_Type != EaseType.Custom)
                return Easing.Evaluate(m_Type, m_P1, m_P2, t);
            t = MotionKernels.Clamp01(t);
            return m_Curve == null || m_Curve.length == 0 ? t : MotionKernels.Finite(m_Curve.Evaluate(t));
        }
    }
}
