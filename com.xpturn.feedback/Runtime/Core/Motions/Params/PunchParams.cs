using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // Punch's saved parameters
    [Serializable]
    internal struct PunchParams
    {
        [SerializeField] internal Vector4 m_Amplitude;
        [SerializeField, Min(0f)] internal float m_Vibrato;
        [SerializeField, Min(0.01f)] internal float m_Decay;

        internal static PunchParams Default => new PunchParams { m_Amplitude = Vector4.one, m_Vibrato = 5f, m_Decay = 1f };
    }
}
