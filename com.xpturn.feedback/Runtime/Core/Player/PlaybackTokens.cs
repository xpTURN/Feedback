using System;
using System.Threading;

namespace xpTURN.Feedback
{
    // a runtime's cancellation tokens — one Register per (player, token), kept and reused (a Register is ~720 B). Made at
    // the first token, entries at their first use: a player without tokens pays nothing. Fixed size, max(4, the records):
    // with the records' count a full store always has an entry no play waits on. A new token takes a free entry, else the
    // longest unused one no play waits on (Dispose — 0 B). The callback runs on the canceling thread: it marks the entry,
    // and on the main thread stops the waiting plays now — off it the plays stop at their next tick
    internal sealed class PlaybackTokens
    {
        internal const int MinimumSize = 4;

        internal sealed class Entry
        {
            readonly PlaybackTokens m_Owner;
            readonly int m_Index;
            internal readonly Action Callback;
            internal CancellationToken Token;
            internal CancellationTokenRegistration Registration;
            internal bool Used;
            internal long LastUse;
            internal volatile bool Canceled;

            internal Entry(PlaybackTokens owner, int index)
            {
                m_Owner = owner;
                m_Index = index;
                Callback = OnCanceled;
            }

            void OnCanceled()
            {
                Canceled = true;
                if (FeedbackScheduler.OnMainThread)
                    m_Owner.m_Runtime.StopWaiting(m_Index);
            }
        }

        readonly PlayerRuntime m_Runtime;
        readonly Entry[] m_Entries;
        long m_Clock;

        internal PlaybackTokens(PlayerRuntime runtime, int records)
        {
            m_Runtime = runtime;
            m_Entries = new Entry[Math.Max(MinimumSize, records)];
        }

        internal int Size => m_Entries.Length;
        internal Entry At(int index) => m_Entries[index];

        // the entry of the token — registered when it is new. -1: a token that cannot be canceled is not kept
        internal int Acquire(CancellationToken token)
        {
            if (!token.CanBeCanceled)
                return -1;
            Entry[] entries = m_Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                Entry e = entries[i];
                if (e != null && e.Used && e.Token == token)
                {
                    e.LastUse = ++m_Clock;
                    return i;
                }
            }
            int pick = -1;
            for (int i = 0; i < entries.Length && pick < 0; i++)
            {
                if (entries[i] == null || !entries[i].Used)
                    pick = i;
            }
            if (pick < 0)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (!m_Runtime.IsWaitedOn(i) && (pick < 0 || entries[i].LastUse < entries[pick].LastUse))
                        pick = i;
                }
            }
            if (pick < 0)
                return -1;
            Entry entry = entries[pick] ?? (entries[pick] = new Entry(this, pick));
            if (entry.Used)
                entry.Registration.Dispose();
            entry.Token = token;
            entry.Used = true;
            entry.Canceled = false;
            entry.LastUse = ++m_Clock;
            entry.Registration = token.Register(entry.Callback);
            return pick;
        }

        internal bool IsCanceled(int index) => index >= 0 && m_Entries[index].Canceled;

        // the release: every registration goes — a cancel after it calls nothing
        internal void Clear()
        {
            Entry[] entries = m_Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                Entry e = entries[i];
                if (e == null || !e.Used)
                    continue;
                e.Registration.Dispose();
                e.Registration = default;
                e.Token = default;
                e.Used = false;
            }
        }
    }
}
