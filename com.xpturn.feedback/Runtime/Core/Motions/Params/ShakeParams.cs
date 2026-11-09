using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // saved — the values are the saved format, add members at the end only
    public enum ShakeNoise
    {
        Smooth = 0,
        Jitter = 1,
    }

    // Shake's saved parameters
    [Serializable]
    internal struct ShakeParams
    {
        [SerializeField] internal Vector4 m_Amplitude;
        [SerializeField, Min(0f)] internal float m_Frequency;
        [SerializeField] internal ShakeNoise m_Noise;
        [SerializeField] internal Ease m_Falloff;

        internal static ShakeParams Default => new ShakeParams { m_Amplitude = Vector4.one, m_Frequency = 10f };
    }
}
