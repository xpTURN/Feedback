using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // Oscillate's saved parameters
    [Serializable]
    internal struct OscillateParams
    {
        [SerializeField] internal Vector4 m_Amplitude;
        [SerializeField, Min(0f)] internal float m_Frequency;
        [SerializeField] internal Waveform m_Waveform;
        [SerializeField, Range(0f, 1f)] internal float m_Phase;

        internal static OscillateParams Default => new OscillateParams { m_Amplitude = Vector4.one, m_Frequency = 1f };
    }
}
