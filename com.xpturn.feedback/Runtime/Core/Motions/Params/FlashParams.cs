using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // Flash's saved parameters
    [Serializable]
    internal struct FlashParams
    {
        [SerializeField] internal Vector4 m_Target;
        [SerializeField, Min(0f)] internal float m_Attack;
        [SerializeField, Min(0f)] internal float m_Hold;
        [SerializeField, Min(0f)] internal float m_Release;
        [SerializeField] internal Ease m_Ease;

        internal static FlashParams Default => new FlashParams { m_Target = Vector4.one, m_Attack = 0.1f, m_Hold = 0.2f, m_Release = 0.7f };
    }
}
