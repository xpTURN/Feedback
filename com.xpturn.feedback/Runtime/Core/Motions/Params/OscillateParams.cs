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
        // each component its own wave: Axis Phase adds to Phase (cycles), Axis Frequency multiplies Frequency
        [SerializeField] internal bool m_PerAxis;
        [SerializeField] internal Vector4 m_AxisPhase;
        [SerializeField, Min(0f)] internal Vector4 m_AxisFrequency;
        [SerializeField, Range(0f, 1f)] internal float m_FadeIn;
        [SerializeField, Range(0f, 1f)] internal float m_FadeOut;
        [SerializeField] internal Ease m_FadeEase;

        internal static OscillateParams Default => new OscillateParams { m_Amplitude = Vector4.one, m_Frequency = 1f, m_AxisFrequency = Vector4.one };
    }
}
