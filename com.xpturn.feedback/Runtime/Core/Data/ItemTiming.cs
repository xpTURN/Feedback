using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // saved — 0 follows the player. Copied to TimelineClock by name (the values differ)
    public enum ItemClock
    {
        Player = 0,
        Scaled = 1,
        Unscaled = 2,
    }

    // copied into TimelineItemDesc. Defaults come from the owner's initializer
    [Serializable]
    public struct ItemTiming
    {
        [SerializeField] internal StartRule m_Rule;
        [SerializeField] internal float m_Delay;
        [SerializeField] internal float m_Duration;
        [SerializeField] internal bool m_Infinite;
        [SerializeField] internal ItemClock m_Clock;
        [SerializeField] internal int m_RepeatCount;        // additional runs
        [SerializeField] internal bool m_RepeatForever;
        [SerializeField] internal float m_RepeatGap;
        [SerializeField] internal bool m_Background;

        internal static ItemTiming Default => new ItemTiming { m_Duration = 0.2f };
    }
}
