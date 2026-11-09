using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    public enum DirectionCondition
    {
        Always = 0,
        ForwardOnly = 1,
        ReverseOnly = 2,
    }

    // the item's own way — the play's, always forward · backward, or the other way
    public enum ItemPlayDirection
    {
        FollowPlayer = 0,
        Forward = 1,
        Backward = 2,
        Opposite = 3,
    }

    public enum IntensityResponse
    {
        Receive = 0,
        Ignore = 1,
    }

    // per-play skips. Defaults come from the owner's initializer
    [Serializable]
    public struct ItemConditions
    {
        [SerializeField] internal float m_Chance;           // 0 ~ 100
        [SerializeField] internal float m_Cooldown;
        [SerializeField] internal int m_MaxPlays;           // 0 = unlimited
        [SerializeField] internal DirectionCondition m_Direction;
        [SerializeField] internal bool m_UseIntensityRange;
        [SerializeField] internal Vector2 m_IntensityRange;

        internal static ItemConditions Default => new ItemConditions { m_Chance = 100f, m_IntensityRange = new Vector2(0f, 1f) };
    }

    [Serializable]
    public struct ItemRandomness
    {
        [SerializeField] internal bool m_RandomizeOutput;
        [SerializeField] internal Vector2 m_OutputRange;
        [SerializeField] internal bool m_RandomizeDuration;
        [SerializeField] internal Vector2 m_DurationRange;

        internal static ItemRandomness Default => new ItemRandomness { m_OutputRange = new Vector2(0.8f, 1f), m_DurationRange = new Vector2(0.5f, 2f) };
    }

    // the category only counts while overriding — FeedbackCategory keeps its values
    [Serializable]
    public struct CategoryOverride
    {
        [SerializeField] internal bool m_Override;
        [SerializeField] internal FeedbackCategory m_Category;
    }
}
