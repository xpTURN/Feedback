using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    public enum ChannelRefMode
    {
        Int = 0,
        Asset = 1,
    }

    /// <summary>Names a channel by a number or by a FeedbackChannel asset.</summary>
    [Serializable]
    public struct ChannelRef
    {
        [SerializeField] internal ChannelRefMode m_Mode;
        [SerializeField] internal int m_Id;
        [SerializeField] internal FeedbackChannel m_Asset;

        /// <summary>A channel named by a number.</summary>
        public static ChannelRef Of(int id) => new ChannelRef { m_Mode = ChannelRefMode.Int, m_Id = id };

        /// <summary>A channel named by a FeedbackChannel asset.</summary>
        public static ChannelRef Of(FeedbackChannel channel) => new ChannelRef { m_Mode = ChannelRefMode.Asset, m_Asset = channel };

        // the same mode and the same value — a missing asset matches nothing
        internal bool Matches(in ChannelRef other)
        {
            if (m_Mode != other.m_Mode)
                return false;
            return m_Mode == ChannelRefMode.Int ? m_Id == other.m_Id : m_Asset != null && m_Asset == other.m_Asset;
        }
    }
}
