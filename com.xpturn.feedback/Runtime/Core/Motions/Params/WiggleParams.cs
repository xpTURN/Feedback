using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // Wiggle's saved parameters
    [Serializable]
    internal struct WiggleParams
    {
        [SerializeField] internal Vector4 m_Amplitude;
        [SerializeField, Min(0f)] internal float m_Frequency;
        [SerializeField, Range(1, 3)] internal int m_Octaves;

        internal static WiggleParams Default => new WiggleParams { m_Amplitude = Vector4.one, m_Frequency = 1f, m_Octaves = 2 };
    }
}
