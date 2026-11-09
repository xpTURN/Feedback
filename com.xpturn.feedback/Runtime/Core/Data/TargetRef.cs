using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    public enum TargetRefMode
    {
        Direct = 0,
        Self = 1,
        Child = 2,
        Parent = 3,
        Anchor = 4,
        MainCamera = 5,
        Channel = 6,
    }

    public enum ChildLookup
    {
        Path = 0,
        Index = 1,
        FirstOfType = 2,
    }

    // resolved by the binding. Channel needs a receiver
    [Serializable]
    public struct TargetRef
    {
        [SerializeField] internal TargetRefMode m_Mode;
        [SerializeField] internal UnityEngine.Object m_Object;
        [SerializeField] internal string m_Path;
        [SerializeField] internal int m_Index;
        [SerializeField] internal ChildLookup m_ChildLookup;
        [SerializeField] internal string m_Anchor;
        [SerializeField] internal ChannelRef m_Channel;

        internal static TargetRef Default => new TargetRef { m_Path = "", m_Anchor = "" };
    }
}
