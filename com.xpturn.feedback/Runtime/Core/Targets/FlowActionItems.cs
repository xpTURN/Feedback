using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace xpTURN.Feedback
{
    // an instant action item — invoked forward only
    [FeedbackTarget("Flow/Unity Event", PreviewGrade.DisplayOnly, Aliases = "event callback")]
    [Serializable]
    public sealed class UnityEventItem : ActionItem
    {
        [SerializeField] internal UnityEvent m_Event = new UnityEvent();

        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;
        internal override float NaturalDuration => 0f;

        internal override bool ActsBackward => false;
    }

    // how the flow items that play one player play it — wait for its end (with a time limit, 0 = none), and what the
    // occurrence hands it
    [Serializable]
    internal struct NestedPlay
    {
        [SerializeField] internal bool m_Wait;
        [SerializeField] internal float m_Timeout;
        [SerializeField] internal bool m_PassIntensity;
        [SerializeField] internal bool m_PassDirection;
        [SerializeField] internal bool m_PassPosition;

        internal static NestedPlay Default => new NestedPlay { m_Wait = true, m_PassIntensity = true, m_PassDirection = true, m_PassPosition = true };

        // an open item's length — its time limit, else +∞
        internal static float OpenLength(float timeout) => timeout > 0f && timeout < float.PositiveInfinity ? timeout : float.PositiveInfinity;
    }

    [Serializable]
    internal sealed class ChainEntry
    {
        [SerializeField] internal FeedbackPlayer m_Player;
        [SerializeField] internal float m_Delay;
        [SerializeField] internal bool m_Wait = true;
        [SerializeField] internal bool m_Active = true;
    }

    [Serializable]
    internal sealed class WeightedPlayer
    {
        [SerializeField] internal FeedbackPlayer m_Player;
        [SerializeField] internal float m_Weight = 1f;
    }

    [Serializable]
    internal sealed class WeightedUnityEvent
    {
        [SerializeField] internal float m_Weight = 1f;
        [SerializeField] internal UnityEvent m_Event = new UnityEvent();
    }

    public enum PlayerControlAction
    {
        Play = 0,
        PlayBackward = 1,
        Stop = 2,
        Pause = 3,
        Resume = 4,
        Skip = 5,
        Restore = 6,
        Signal = 7,
    }

    public enum DebugAction
    {
        Log = 0,
        Warning = 1,
        Error = 2,
        Break = 3,
        Note = 4,
    }

    // plays another player — waiting, it ends with that play
    [FeedbackTarget("Flow/Play Player", PreviewGrade.Full, Aliases = "player nested play feedbacks", Requires = typeof(FeedbackPlayer))]
    [Serializable]
    public sealed class PlayPlayerItem : ActionItem
    {
        [SerializeField] internal FeedbackPlayer m_Player;
        [SerializeField] internal NestedPlay m_Play = NestedPlay.Default;

        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;
        internal override bool Opens => m_Play.m_Wait;
        internal override float NaturalDuration => m_Play.m_Wait ? NestedPlay.OpenLength(m_Play.m_Timeout) : 0f;
    }

    // players one after another — an entry's delay after the previous one starts, or ends when it waits
    [FeedbackTarget("Flow/Player Chain", PreviewGrade.Full, Aliases = "player chain sequence")]
    [Serializable]
    public sealed class PlayerChainItem : ActionItem
    {
        [SerializeField] internal List<ChainEntry> m_Entries = new List<ChainEntry>();
        [SerializeField] internal NestedPlay m_Play = NestedPlay.Default;

        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;

        internal override bool Opens
        {
            get
            {
                for (int i = 0; i < m_Entries.Count; i++)
                {
                    ChainEntry e = m_Entries[i];
                    if (e != null && e.m_Active && e.m_Player != null && e.m_Wait)
                        return true;
                }
                return false;
            }
        }

        // waiting: the time limit; else the delays' sum (0 = instant)
        internal override float NaturalDuration => Opens ? NestedPlay.OpenLength(m_Play.m_Timeout) : DelaySum;

        internal float DelaySum
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < m_Entries.Count; i++)
                {
                    ChainEntry e = m_Entries[i];
                    if (e != null && e.m_Active && e.m_Delay > 0f && e.m_Delay < float.PositiveInfinity)
                        sum += e.m_Delay;
                }
                return sum;
            }
        }
    }

    // an act on players — instant, no wait
    [FeedbackTarget("Flow/Player Control", PreviewGrade.Full, Aliases = "player control stop pause resume", Requires = typeof(FeedbackPlayer))]
    [Serializable]
    public sealed class PlayerControlItem : ActionItem
    {
        [SerializeField] internal List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        [SerializeField] internal PlayerControlAction m_Action;
        [SerializeField] internal StopMode m_StopMode;

        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;
        internal override float NaturalDuration => 0f;

        internal override bool ActsBackward => false;
    }

    // one player by its weight
    [FeedbackTarget("Flow/Random Player", PreviewGrade.Full, Aliases = "random player weighted")]
    [Serializable]
    public sealed class RandomPlayerItem : ActionItem
    {
        [SerializeField] internal List<WeightedPlayer> m_Entries = new List<WeightedPlayer>();
        [SerializeField] internal NestedPlay m_Play = NestedPlay.Default;

        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;
        internal override bool Opens => m_Play.m_Wait;
        internal override float NaturalDuration => m_Play.m_Wait ? NestedPlay.OpenLength(m_Play.m_Timeout) : 0f;
    }

    // one UnityEvent by its weight — forward only
    [FeedbackTarget("Flow/Random Unity Event", PreviewGrade.DisplayOnly, Aliases = "random event callback weighted")]
    [Serializable]
    public sealed class RandomUnityEventItem : ActionItem
    {
        [SerializeField] internal List<WeightedUnityEvent> m_Entries = new List<WeightedUnityEvent>();

        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;
        internal override float NaturalDuration => 0f;

        internal override bool ActsBackward => false;
    }

    // a signal on a channel — the value × the occurrence's intensity
    [FeedbackTarget("Flow/Signal Broadcast", PreviewGrade.DisplayOnly, Aliases = "signal broadcast channel event radio", Requires = typeof(FeedbackSignalReceiver))]
    [Serializable]
    public sealed class SignalBroadcastItem : ActionItem
    {
        [SerializeField] internal ChannelRef m_Channel;
        [SerializeField] internal float m_Value = 1f;

        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;
        internal override float NaturalDuration => 0f;

        internal override bool ActsBackward => false;
    }

    // a log line, a break or a note
    [FeedbackTarget("Flow/Debug", PreviewGrade.DisplayOnly, Aliases = "debug log break comment note")]
    [Serializable]
    public sealed class DebugItem : ActionItem
    {
        [SerializeField] internal DebugAction m_Action;
        [SerializeField, TextArea] internal string m_Message = "";
        [SerializeField] internal bool m_DevelopmentOnly = true;

        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;
        internal override float NaturalDuration => 0f;
    }

    // the players a list's flow items name — once each, in list order (the preview binds them, the rules walk them)
    internal static class FlowPlayers
    {
        internal static void Collect(List<FeedbackItem> items, List<FeedbackPlayer> into)
        {
            if (items == null)
                return;
            for (int i = 0; i < items.Count; i++)
            {
                switch (items[i])
                {
                    case PlayPlayerItem play:
                        Add(play.m_Player, into);
                        break;
                    case RandomPlayerItem random:
                        foreach (WeightedPlayer e in random.m_Entries)
                            Add(e?.m_Player, into);
                        break;
                    case PlayerChainItem chain:
                        foreach (ChainEntry e in chain.m_Entries)
                            Add(e?.m_Player, into);
                        break;
                    case PlayerControlItem control:
                        foreach (FeedbackPlayer p in control.m_Players)
                            Add(p, into);
                        break;
                }
            }
        }

        static void Add(FeedbackPlayer player, List<FeedbackPlayer> into)
        {
            if (player != null && !into.Contains(player))
                into.Add(player);
        }
    }
}
