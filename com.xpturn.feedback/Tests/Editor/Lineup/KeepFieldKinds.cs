using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor
{
    // field kinds a target may save — the keep's record takes them as integer children
    public sealed class KeepFieldKinds : ScriptableObject
    {
        public LayerMask m_Mask;
        public Color32 m_Color32;
        public Hash128 m_Hash;
    }
}
