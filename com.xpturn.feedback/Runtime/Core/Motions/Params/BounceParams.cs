using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // Bounce's saved parameters
    [Serializable]
    internal struct BounceParams
    {
        [SerializeField] internal Vector4 m_Amplitude;
        [SerializeField, Range(1, 10)] internal int m_Count;
        [SerializeField, Range(0.05f, 1f)] internal float m_Restitution;

        internal static BounceParams Default => new BounceParams { m_Amplitude = Vector4.one, m_Count = 3, m_Restitution = 0.5f };
    }
}
