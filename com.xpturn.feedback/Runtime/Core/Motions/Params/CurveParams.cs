using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // Curve's saved parameters
    // Default makes a new curve each call — a shared static instance would share it
    [Serializable]
    internal struct CurveParams
    {
        [SerializeField] internal AnimationCurve m_Curve;
        [SerializeField] internal Vector4 m_RemapZero;
        [SerializeField] internal Vector4 m_RemapOne;

        internal static CurveParams Default => new CurveParams
        {
            m_Curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f)),
            m_RemapOne = Vector4.one,
        };
    }
}
