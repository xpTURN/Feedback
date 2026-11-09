using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // Blink's saved parameters
    [Serializable]
    internal struct BlinkParams
    {
        [SerializeField] internal Vector4 m_Value;
        [SerializeField, Min(0.0001f)] internal float m_Period;
        [SerializeField, Range(0f, 1f)] internal float m_Duty;
        [SerializeField, Range(0f, 1f)] internal float m_Jitter;
        [SerializeField] internal bool m_StartOn;

        internal static BlinkParams Default => new BlinkParams { m_Value = Vector4.one, m_Period = 0.2f, m_Duty = 0.5f, m_StartOn = true };
    }
}
