using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace xpTURN.Feedback
{
    // the UnityEvent item — an instant, forward only. The event is user code: an exception is logged and the play goes on.
    // Nothing to undo — Revert and the other ends do nothing
    internal sealed class UnityEventBinding : ItemBinding
    {
        readonly UnityEventItem m_Item;
        FeedbackContext m_Context;

        internal UnityEventBinding(UnityEventItem item)
        {
            m_Item = item;
        }

        internal override bool Prepare(in PrepareContext context)
        {
            m_Context = context.Context;
            return true;
        }

        // backward it is not called — user code cannot be undone (a one-direction item comes forward)
        internal override void Execute(int record, in BeginContext context)
        {
            UnityEvent e = m_Item.m_Event;
            if (e == null || context.Reverse)
                return;
            if (m_Context.Kind == ContextKind.Preview)
            {
                m_Context.SkipUnityEvent(e, "item");
                return;
            }
            try
            {
                e.Invoke();
            }
            catch (Exception x)
            {
                m_Context.LogException(x);
            }
        }

        // an instant never starts
        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
        }
    }
}

namespace xpTURN.Feedback
{
    // the base of the bindings that play another player — the play's arguments, the wait (the binding is the child play's
    // listener: each record keeps the child's handle and its own serial past the end, so a reused record's late end is
    // stale), the waiting cycle's refusal and the ends' propagation. Only what the prepare gave is used — a child's end can
    // come while the owner's runtime is being released
    internal abstract class NestedPlayBinding : ItemBinding
    {
        internal const string CycleWarning = "A Feedback flow item would wait on a player that waits on it (or the waiting chain is deeper than 16) — the play was not made.";

        // a record's wait: the child play's listener (one per record, made at the prepare) — the chain's link while the child
        // starts (ChainSerial), the handle and the serial it waits on once it plays
        sealed class WaitLink : IPlaybackListener, IWaitLink
        {
            readonly NestedPlayBinding m_Owner;
            internal readonly int Index;
            internal long Chain;
            internal PlaybackHandle Handle;
            internal long Serial;
            internal FeedbackPlayer Child;
            internal bool Propagating;

            internal WaitLink(NestedPlayBinding owner, int index)
            {
                m_Owner = owner;
                Index = index;
            }

            public PlayerRuntime Runtime => m_Owner.m_Runtime;
            public int Record => Index;
            public long ChainSerial => Chain;

            public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end) => m_Owner.Ended(this, handle);
        }

        protected PlayerRuntime m_Runtime;
        protected FeedbackPlayer m_Owner;
        protected FeedbackContext m_Context;
        protected int m_ListIndex;
        WaitLink[] m_Links;
        int m_Starting = -1;
        long m_StartingSerial;
        bool m_EndedAtStart;
        bool m_Played;
#if DEBUG
        bool m_WarnedCycle;
#endif

        protected bool PrepareBase(in PrepareContext context)
        {
            if (context.Runtime == null || context.Player == null)
                return Refuse("no player");
            m_Runtime = context.Runtime;
            m_Owner = context.Player;
            m_Context = context.Context;
            m_ListIndex = context.ListIndex;
            m_Links = new WaitLink[context.MaxOverlap];
            for (int i = 0; i < m_Links.Length; i++)
                m_Links[i] = new WaitLink(this, i);
            return true;
        }

        // the child prepares with the owner — its first play allocates nothing. Itself is skipped: its own prepare refuses the
        // reentry
        protected void PrepareChild(FeedbackPlayer child)
        {
            if (child != null && child != m_Owner)
                child.EnsureRuntime();
        }

        // plays the child for the record. wait: the node ends with the child's play — a refused play (any refusal) ends it now
        protected void PlayChild(int record, in BeginContext context, FeedbackPlayer child, in NestedPlay play, bool wait)
        {
            if (child == null)
            {
                if (wait)
                    EndNow(record, context.Serial);
                return;
            }
            m_Played = true;
            PlayArgs args = PlayArgs.Default.WithSeed(Hash32.Tagged(context.ItemSeed, Hash32.Occurrence, Hash32.Fold(context.Occurrence)));
            if (play.m_PassIntensity)
                args = args.WithIntensity(context.Intensity);
            if (play.m_PassDirection && context.Reverse)
                args = args.WithDirection(PlayerDirection.Backward);
            if (play.m_PassPosition && context.HasPosition)
                args = args.WithPosition(context.Position);
            WaitLink link = m_Links[record];
            if (wait)
            {
                if (m_Runtime.InWaitChain(child, record, out _))
                {
#if DEBUG
                    if (!m_WarnedCycle)
                    {
                        m_WarnedCycle = true;
                        Debug.LogWarning(CycleWarning, m_Owner);
                    }
#endif
                    EndNow(record, context.Serial);
                    return;
                }
                link.Chain = context.Serial;
                args = args.WithListener(link);
            }
            m_Starting = record;
            m_StartingSerial = context.Serial;
            m_EndedAtStart = false;
            PlaybackHandle handle;
            try
            {
                handle = child.Play(args);
            }
            finally
            {
                m_Starting = -1;
            }
            if (!wait || m_EndedAtStart)
                return;
            if (handle == default)
            {
                EndNow(record, context.Serial);
                return;
            }
            link.Handle = handle;
            link.Serial = context.Serial;
            link.Child = child;
        }

        // the child's play ended — once. The link's own play's end, unless its record plays a later play now or this binding's
        // propagation made it; another handle inside the child's Play is the new play's t = 0 end
        void Ended(WaitLink link, PlaybackHandle handle)
        {
            if (handle != default && handle == link.Handle)
            {
                link.Handle = default;
                if (link.Propagating || m_Runtime.Serial(link.Index) != link.Serial)
                    return;
                ChildEnded(link.Index, link.Serial);
                return;
            }
            if (m_Starting == link.Index)
            {
                m_EndedAtStart = true;
                ChildEnded(m_Starting, m_StartingSerial);
            }
        }

        protected virtual void ChildEnded(int record, long serial) => EndNow(record, serial);

        protected void EndNow(int record, long serial) => m_Runtime.EndOpenItem(record, m_ListIndex, serial);

        // Completed (the child's end, the time limit) leaves the child alone; the other ends stop it the same way
        internal override void End(int record, EndReason reason)
        {
            WaitLink link = m_Links[record];
            if (reason == EndReason.Completed || link.Handle == default || link.Child == null)
                return;
            link.Propagating = true;
            try
            {
                link.Child.Stop(link.Handle, reason == EndReason.Revert ? StopMode.Revert : reason == EndReason.Complete ? StopMode.Complete : StopMode.Hold);
            }
            finally
            {
                link.Propagating = false;
                link.Handle = default;
            }
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        // the players it played go back — their own Restore, in reverse
        internal override void Restore()
        {
            if (!m_Played)
                return;
            m_Played = false;
            RestoreChildren();
        }

        protected abstract void RestoreChildren();

        protected static void RestoreOne(FeedbackPlayer child, FeedbackPlayer owner)
        {
            if (child != null && child != owner)
                child.Restore();
        }
    }

    internal sealed class PlayPlayerBinding : NestedPlayBinding
    {
        readonly PlayPlayerItem m_Item;

        internal PlayPlayerBinding(PlayPlayerItem item) => m_Item = item;

        internal override bool Prepare(in PrepareContext context)
        {
            if (!PrepareBase(in context))
                return false;
            PrepareChild(m_Item.m_Player);
            return true;
        }

        internal override void Begin(int record, in BeginContext context) => PlayChild(record, in context, m_Item.m_Player, in m_Item.m_Play, m_Item.m_Play.m_Wait);

        internal override void Execute(int record, in BeginContext context) => PlayChild(record, in context, m_Item.m_Player, in m_Item.m_Play, false);

        protected override void RestoreChildren() => RestoreOne(m_Item.m_Player, m_Owner);
    }

    internal sealed class RandomPlayerBinding : NestedPlayBinding
    {
        internal const string NoWeightWarning = "A Random Player item has no entry with a positive weight — it played nothing.";

        readonly RandomPlayerItem m_Item;
        float[] m_Weights;
#if DEBUG
        bool m_Warned;
#endif

        internal RandomPlayerBinding(RandomPlayerItem item) => m_Item = item;

        internal override bool Prepare(in PrepareContext context)
        {
            if (!PrepareBase(in context))
                return false;
            List<WeightedPlayer> entries = m_Item.m_Entries;
            m_Weights = new float[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                WeightedPlayer e = entries[i];
                m_Weights[i] = e != null && e.m_Player != null ? e.m_Weight : 0f;
                if (e != null)
                    PrepareChild(e.m_Player);
            }
            return true;
        }

        FeedbackPlayer Pick(in BeginContext context)
        {
            int i = WeightedPick.Index(m_Weights, m_Weights.Length, Hash32.ToUnit(Hash32.Tagged(context.ItemSeed, Hash32.Pick, Hash32.Fold(context.Occurrence))));
            if (i < 0)
            {
#if DEBUG
                if (!m_Warned)
                {
                    m_Warned = true;
                    Debug.LogWarning(NoWeightWarning, m_Owner);
                }
#endif
                return null;
            }
            return m_Item.m_Entries[i].m_Player;
        }

        internal override void Begin(int record, in BeginContext context) => PlayChild(record, in context, Pick(in context), in m_Item.m_Play, m_Item.m_Play.m_Wait);

        internal override void Execute(int record, in BeginContext context) => PlayChild(record, in context, Pick(in context), in m_Item.m_Play, false);

        protected override void RestoreChildren()
        {
            List<WeightedPlayer> entries = m_Item.m_Entries;
            for (int i = entries.Count - 1; i >= 0; i--)
                RestoreOne(entries[i]?.m_Player, m_Owner);
        }
    }

    // a waiting chain plays its entries one after another — the next one its delay after the previous one ends (or starts, if
    // that one does not wait), on the node's elapsed seconds at the passes' ends; the last one's end ends the node. A chain
    // that waits on none is timed — the delays' sum — and plays each entry at its offset, backward in reverse order
    internal sealed class PlayerChainBinding : NestedPlayBinding
    {
        readonly PlayerChainItem m_Item;
        bool m_Waits;
        // the timed chain's offsets on the timeline's own unit (µs) — a float's seconds against the elapsed seconds miss by a
        // rounding; −1 = a dead entry
        long[] m_Offsets;
        long m_Total;
        int[] m_Next;
        double[] m_Due;
        double[] m_Last;
        long[] m_LastAt;
        bool[] m_Waiting;
        BeginContext[] m_Begins;

        internal PlayerChainBinding(PlayerChainItem item) => m_Item = item;

        internal override bool Prepare(in PrepareContext context)
        {
            if (!PrepareBase(in context))
                return false;
            m_Waits = m_Item.Opens;
            List<ChainEntry> entries = m_Item.m_Entries;
            m_Offsets = new long[entries.Count];
            float at = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                ChainEntry e = entries[i];
                if (e == null || !e.m_Active)
                {
                    m_Offsets[i] = -1;
                    continue;
                }
                if (e.m_Delay > 0f && e.m_Delay < float.PositiveInfinity)
                    at += e.m_Delay;
                m_Offsets[i] = TimelineTime.FromSeconds(at);
                PrepareChild(e.m_Player);
            }
            m_Total = TimelineTime.FromSeconds(m_Item.DelaySum);
            int n = context.MaxOverlap;
            m_Next = new int[n];
            m_Due = new double[n];
            m_Last = new double[n];
            m_LastAt = new long[n];
            m_Waiting = new bool[n];
            m_Begins = new BeginContext[n];
            return true;
        }

        static float Delay(ChainEntry e) => e.m_Delay > 0f && e.m_Delay < float.PositiveInfinity ? e.m_Delay : 0f;

        bool Live(int i)
        {
            ChainEntry e = m_Item.m_Entries[i];
            return e != null && e.m_Active;
        }

        internal override void Begin(int record, in BeginContext context)
        {
            m_Begins[record] = context;
            if (m_Waits)
            {
                m_Next[record] = 0;
                m_Waiting[record] = false;
                m_Last[record] = 0;
                m_Due[record] = 0;
                SkipDead(record);
                if (m_Next[record] < m_Offsets.Length)
                    m_Due[record] = Delay(m_Item.m_Entries[m_Next[record]]);
                Step(record);
                return;
            }
            m_LastAt[record] = context.Reverse ? long.MaxValue : -1;
            Fire(record, context.Reverse ? m_Total : 0);
        }

        // a timed chain crosses its offsets the new way from the elapsed time it reached — a waiting chain does not seek, so
        // its play never turns
        internal override void Turn(int record, long serial, bool reverse)
        {
            if (m_Waits || m_Begins == null || m_Begins[record].Serial != serial)
                return;
            BeginContext c = m_Begins[record];
            m_Begins[record] = new BeginContext(c.Intensity, c.ItemSeed, c.Occurrence, c.Serial, reverse, c.Seconds, c.HasPosition, c.Position, c.Swept);
        }

        internal override void Execute(int record, in BeginContext context)
        {
            m_Begins[record] = context;
            List<ChainEntry> entries = m_Item.m_Entries;
            int count = entries.Count;
            for (int k = 0; k < count; k++)
            {
                int i = context.Reverse ? count - 1 - k : k;
                if (Live(i))
                    PlayChild(record, in context, entries[i].m_Player, in m_Item.m_Play, false);
            }
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
            if (m_Waits)
            {
                m_Last[record] = time.Seconds;
                Step(record);
                return;
            }
            Fire(record, TimelineTime.FromSeconds(time.Seconds));
        }

        // the timed chain: the entries whose offset the elapsed seconds crossed since the last call — forward (last, now],
        // backward [now, last), in their order
        void Fire(int record, long now)
        {
            long last = m_LastAt[record];
            BeginContext context = m_Begins[record];
            List<ChainEntry> entries = m_Item.m_Entries;
            int count = m_Offsets.Length;
            for (int k = 0; k < count; k++)
            {
                int i = context.Reverse ? count - 1 - k : k;
                long at = m_Offsets[i];
                if (at < 0)
                    continue;
                bool crossed = context.Reverse ? at >= now && at < last : at > last && at <= now;
                if (crossed)
                    PlayChild(record, in context, entries[i].m_Player, in m_Item.m_Play, false);
            }
            m_LastAt[record] = now;
        }

        void SkipDead(int record)
        {
            while (m_Next[record] < m_Offsets.Length && !Live(m_Next[record]))
                m_Next[record]++;
        }

        // the waiting chain: the next entry when it is due — a waiting one holds the chain until its end
        void Step(int record)
        {
            long serial = m_Begins[record].Serial;
            List<ChainEntry> entries = m_Item.m_Entries;
            while (!m_Waiting[record] && m_Next[record] < m_Offsets.Length && m_Last[record] >= m_Due[record])
            {
                int i = m_Next[record]++;
                ChainEntry e = entries[i];
                SkipDead(record);
                double next = m_Next[record] < m_Offsets.Length ? Delay(entries[m_Next[record]]) : 0.0;
                if (e.m_Wait && e.m_Player != null)
                {
                    m_Waiting[record] = true;
                    m_Due[record] = next;
                    PlayChild(record, in m_Begins[record], e.m_Player, in m_Item.m_Play, true);
                    if (m_Runtime.Serial(record) != serial)
                        return;
                }
                else
                {
                    PlayChild(record, in m_Begins[record], e.m_Player, in m_Item.m_Play, false);
                    m_Due[record] = m_Last[record] + next;
                }
            }
            if (!m_Waiting[record] && m_Next[record] >= m_Offsets.Length)
                EndNow(record, serial);
        }

        // a waiting entry ended (or was refused): the next one is due its delay after now
        protected override void ChildEnded(int record, long serial)
        {
            if (!m_Waiting[record])
                return;
            m_Waiting[record] = false;
            m_Due[record] = m_Last[record] + m_Due[record];
            Step(record);
        }

        internal override void End(int record, EndReason reason)
        {
            if (!m_Waits && reason == EndReason.Completed)
                Fire(record, m_Begins[record].Reverse ? 0 : m_Total);
            base.End(record, reason);
        }

        protected override void RestoreChildren()
        {
            List<ChainEntry> entries = m_Item.m_Entries;
            for (int i = entries.Count - 1; i >= 0; i--)
                RestoreOne(entries[i]?.m_Player, m_Owner);
        }
    }

    // the players' public API — forward only (a command cannot be undone), no wait. A preview reaches the players bound to
    // it; another refuses quietly
    internal sealed class PlayerControlBinding : ItemBinding
    {
        readonly PlayerControlItem m_Item;
        FeedbackPlayer m_Owner;

        internal PlayerControlBinding(PlayerControlItem item) => m_Item = item;

        internal override bool Prepare(in PrepareContext context)
        {
            m_Owner = context.Player;
            return true;
        }

        internal override void Execute(int record, in BeginContext context)
        {
            if (context.Reverse)
                return;
            List<FeedbackPlayer> players = m_Item.m_Players;
            for (int i = 0; i < players.Count; i++)
            {
                FeedbackPlayer p = players[i];
                if (p == null)
                    continue;
                switch (m_Item.m_Action)
                {
                    case PlayerControlAction.Play:
                        p.Play(PlayArgs.Default.WithIntensity(context.Intensity));
                        break;
                    case PlayerControlAction.PlayBackward:
                        p.Play(PlayArgs.Default.WithIntensity(context.Intensity).WithDirection(PlayerDirection.Backward));
                        break;
                    case PlayerControlAction.Stop:
                        p.Stop(m_Item.m_StopMode);
                        break;
                    case PlayerControlAction.Pause:
                        p.Pause();
                        break;
                    case PlayerControlAction.Resume:
                        p.Resume();
                        break;
                    case PlayerControlAction.Skip:
                        p.Skip();
                        break;
                    case PlayerControlAction.Restore:
                        p.Restore();
                        break;
                    case PlayerControlAction.Signal:
                        p.SignalQuietly();
                        break;
                }
            }
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
        }
    }

    // the UnityEvent item's way, one entry by its weight
    internal sealed class RandomUnityEventBinding : ItemBinding
    {
        readonly RandomUnityEventItem m_Item;
        FeedbackContext m_Context;
        float[] m_Weights;

        internal RandomUnityEventBinding(RandomUnityEventItem item) => m_Item = item;

        internal override bool Prepare(in PrepareContext context)
        {
            m_Context = context.Context;
            List<WeightedUnityEvent> entries = m_Item.m_Entries;
            m_Weights = new float[entries.Count];
            for (int i = 0; i < entries.Count; i++)
                m_Weights[i] = entries[i] != null && entries[i].m_Event != null ? entries[i].m_Weight : 0f;
            return true;
        }

        internal override void Execute(int record, in BeginContext context)
        {
            if (context.Reverse)
                return;
            int i = WeightedPick.Index(m_Weights, m_Weights.Length, Hash32.ToUnit(Hash32.Tagged(context.ItemSeed, Hash32.Pick, Hash32.Fold(context.Occurrence))));
            if (i < 0)
                return;
            UnityEvent e = m_Item.m_Entries[i].m_Event;
            if (m_Context.Kind == ContextKind.Preview)
            {
                m_Context.SkipUnityEvent(e, "random");
                return;
            }
            try
            {
                e.Invoke();
            }
            catch (Exception x)
            {
                m_Context.LogException(x);
            }
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
        }
    }

    // the context's channel registry — forward only; a preview counts what it did not send
    internal sealed class SignalBroadcastBinding : ItemBinding
    {
        readonly SignalBroadcastItem m_Item;
        FeedbackContext m_Context;
        FeedbackPlayer m_Owner;

        internal SignalBroadcastBinding(SignalBroadcastItem item) => m_Item = item;

        internal override bool Prepare(in PrepareContext context)
        {
            m_Context = context.Context;
            m_Owner = context.Player;
            return true;
        }

        internal override void Execute(int record, in BeginContext context)
        {
            if (context.Reverse)
                return;
            if (m_Context.Kind == ContextKind.Preview)
            {
                m_Context.SkippedSignals++;
                return;
            }
            var signal = new FeedbackSignal(in m_Item.m_Channel, m_Item.m_Value * context.Intensity, context.Intensity, context.HasPosition, context.Position, m_Owner);
            m_Context.Channels.Send(in signal);
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
        }
    }

    // a log line with the player as its context, a break, a note (nothing). Development only by default; a preview prefixes
    // its lines and does not break. The break and the build's kind go through hooks the tests replace (a batch play mode run
    // stops at Debug.Break · the editor is always a debug build)
    internal sealed class DebugBinding : ItemBinding
    {
        internal const string PreviewPrefix = "[Preview] ";
        internal static Action s_Break = Debug.Break;
        internal static Func<bool> s_DebugBuild = () => Debug.isDebugBuild;

        readonly DebugItem m_Item;
        FeedbackContext m_Context;
        FeedbackPlayer m_Owner;

        internal DebugBinding(DebugItem item) => m_Item = item;

        internal override bool Prepare(in PrepareContext context)
        {
            m_Context = context.Context;
            m_Owner = context.Player;
            return true;
        }

        internal override void Execute(int record, in BeginContext context)
        {
            if (m_Item.m_DevelopmentOnly && !s_DebugBuild())
                return;
            bool preview = m_Context.Kind == ContextKind.Preview;
            switch (m_Item.m_Action)
            {
                case DebugAction.Log:
                    Debug.Log(preview ? PreviewPrefix + m_Item.m_Message : m_Item.m_Message, m_Owner);
                    break;
                case DebugAction.Warning:
                    Debug.LogWarning(preview ? PreviewPrefix + m_Item.m_Message : m_Item.m_Message, m_Owner);
                    break;
                case DebugAction.Error:
                    Debug.LogError(preview ? PreviewPrefix + m_Item.m_Message : m_Item.m_Message, m_Owner);
                    break;
                case DebugAction.Break:
                    if (!preview)
                        s_Break?.Invoke();
                    break;
            }
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
        }
    }
}
