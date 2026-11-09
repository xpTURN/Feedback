using System;
using System.Collections.Generic;

namespace xpTURN.Feedback.Editor
{
    internal struct EventEntry
    {
        internal FeedbackPlayer Player;
        internal PlayerEventKind Kind;
        internal PlaybackHandle Handle;
        internal int Frame;
        internal double Time;
        // an end that came without a Completed or Stopped event — the row has its reason, not a kind
        internal bool Quiet;
        internal PlaybackEnd End;
    }

    internal struct EndEntry
    {
        internal FeedbackPlayer Player;
        internal PlaybackHandle Handle;
        internal PlaybackEnd End;
    }

    // the diagnostics' events of every context in a fixed ring — the handler allocates nothing (a struct written in place; the
    // players it met kept up to a capacity for the missed-signal line). A full ring overwrites its oldest entry and counts it.
    // Every play's end and its reason go to a second ring; an end with no event gets a row of its own. With the call sites on,
    // a Played keeps where the play started. Not serialized: a domain reload drops it
    internal sealed class DebuggerEvents
    {
        internal const int Size = 200;
        internal const int SeenCapacity = 64;
        internal const int EndSize = 256;

        internal readonly List<FeedbackPlayer> Seen = new List<FeedbackPlayer>(SeenCapacity);
        readonly EventEntry[] m_Ring = new EventEntry[Size];
        readonly Action<FeedbackPlayer, PlayerEventKind, PlaybackHandle> m_Handler;
        readonly Action<FeedbackPlayer, PlaybackHandle, PlaybackEnd, bool> m_EndHandler;
        readonly EndEntry[] m_Ends = new EndEntry[EndSize];
        int m_EndHead;
        internal int EndCount { get; private set; }
        internal long EndTotal { get; private set; }
        int m_Head;

        internal DebuggerEvents()
        {
            m_Handler = OnEvent;
            m_EndHandler = OnEnd;
        }

        internal int Count { get; private set; }

        // every event taken since the window opened
        internal long Total { get; private set; }

        // entries a full ring wrote over since the last Clear
        internal long Overwritten { get; private set; }

        // players met past the capacity
        internal int SeenDropped { get; private set; }

        internal bool Subscribed { get; private set; }

        internal void Subscribe()
        {
            if (Subscribed)
                return;
            Subscribed = true;
            FeedbackDiagnostics.PlayerEvent += m_Handler;
            FeedbackDiagnostics.PlaybackEnded += m_EndHandler;
        }

        internal void Unsubscribe()
        {
            if (!Subscribed)
                return;
            Subscribed = false;
            FeedbackDiagnostics.PlayerEvent -= m_Handler;
            FeedbackDiagnostics.PlaybackEnded -= m_EndHandler;
        }

        // newest first
        internal EventEntry At(int i) => m_Ring[(m_Head + Count - 1 - i) % Size];

        // the ring and its overwrite count — the total stays
        internal void Clear()
        {
            m_Head = 0;
            Count = 0;
            Overwritten = 0;
        }

        // this play's reason from the end ring, newest first — false once the ring has dropped it
        internal bool EndOf(FeedbackPlayer player, PlaybackHandle handle, out PlaybackEnd end)
        {
            for (int i = 0; i < EndCount; i++)
            {
                EndEntry e = m_Ends[(m_EndHead + EndCount - 1 - i) % EndSize];
                if (e.Handle == handle && ReferenceEquals(e.Player, player))
                {
                    end = e.End;
                    return true;
                }
            }
            end = default;
            return false;
        }

        void OnEnd(FeedbackPlayer player, PlaybackHandle handle, PlaybackEnd end, bool quiet)
        {
            int at;
            if (EndCount == EndSize)
            {
                at = m_EndHead;
                m_EndHead = (m_EndHead + 1) % EndSize;
            }
            else
            {
                at = (m_EndHead + EndCount) % EndSize;
                EndCount++;
            }
            m_Ends[at] = new EndEntry { Player = player, Handle = handle, End = end };
            EndTotal++;
            if (quiet)
                Write(player, default, handle, true, end);
        }

        void OnEvent(FeedbackPlayer player, PlayerEventKind kind, PlaybackHandle handle)
        {
            if (kind == PlayerEventKind.Played && DebuggerCallSites.Enabled)
                DebuggerCallSites.Capture(player, handle);
            Write(player, kind, handle, false, default);
        }

        void Write(FeedbackPlayer player, PlayerEventKind kind, PlaybackHandle handle, bool quiet, PlaybackEnd end)
        {
            int at;
            if (Count == Size)
            {
                at = m_Head;
                m_Head = (m_Head + 1) % Size;
                Overwritten++;
            }
            else
            {
                at = (m_Head + Count) % Size;
                Count++;
            }
            m_Ring[at] = new EventEntry { Player = player, Kind = kind, Handle = handle, Frame = UnityEngine.Time.frameCount, Time = UnityEngine.Time.realtimeSinceStartupAsDouble, Quiet = quiet, End = end };
            Total++;
            for (int i = 0; i < Seen.Count; i++)
            {
                if (ReferenceEquals(Seen[i], player))
                    return;
            }
            if (Seen.Count < SeenCapacity)
                Seen.Add(player);
            else
                SeenDropped++;
        }
    }
}
