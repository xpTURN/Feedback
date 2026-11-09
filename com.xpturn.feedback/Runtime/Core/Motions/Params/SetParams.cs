using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // Set's saved parameters
    [Serializable]
    internal struct SetParams
    {
        [SerializeField] internal Vector4 m_Value;
        [SerializeField] internal bool m_Relative;

        internal static SetParams Default => new SetParams();
    }
}
