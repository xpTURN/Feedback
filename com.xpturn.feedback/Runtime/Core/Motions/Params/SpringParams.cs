using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // saved — the values are the saved format, add members at the end only
    public enum SpringMode
    {
        Bump = 0,
        MoveTo = 1,
    }

    // Spring's saved parameters
    // SettleTime is the natural duration — the data and the evaluator call it
    [Serializable]
    internal struct SpringParams
    {
        [SerializeField] internal SpringMode m_Mode;
        [SerializeField] internal Vector4 m_Value;
        [SerializeField] internal bool m_Relative;
        [SerializeField, Min(0.001f)] internal float m_Frequency;
        [SerializeField, Min(0.01f)] internal float m_Damping;
        [SerializeField, Range(0.000001f, 0.99f)] internal float m_Settle;

        internal static SpringParams Default => new SpringParams { m_Value = Vector4.one, m_Frequency = 6f, m_Damping = 0.4f, m_Settle = 0.001f };

        internal readonly float SettleTime() => SpringMath.SettleTime(SpringShape.Of(m_Frequency, m_Damping), m_Settle, m_Mode == SpringMode.MoveTo);
    }
}
