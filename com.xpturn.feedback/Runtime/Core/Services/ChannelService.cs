using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the channel as a dictionary key — equality and a hash (ChannelRef has neither: as a key it boxes each lookup). A missing
    // asset is a key nothing registers under
    internal readonly struct ChannelKey : IEquatable<ChannelKey>
    {
        readonly ChannelRefMode m_Mode;
        readonly int m_Id;
        readonly FeedbackChannel m_Asset;

        internal ChannelKey(in ChannelRef channel)
        {
            m_Mode = channel.m_Mode;
            m_Id = channel.m_Mode == ChannelRefMode.Int ? channel.m_Id : 0;
            // the engine's null: a destroyed or empty reference is no asset
            m_Asset = channel.m_Mode == ChannelRefMode.Asset && channel.m_Asset != null ? channel.m_Asset : null;
        }

        internal bool IsNone => m_Mode == ChannelRefMode.Asset && ReferenceEquals(m_Asset, null);

        internal ChannelRefMode Mode => m_Mode;

        internal int Id => m_Id;

        internal FeedbackChannel Asset => m_Asset;

        public bool Equals(ChannelKey other) => m_Mode == other.m_Mode && m_Id == other.m_Id && ReferenceEquals(m_Asset, other.m_Asset);

        public override bool Equals(object obj) => obj is ChannelKey other && Equals(other);

        public override int GetHashCode() => m_Mode == ChannelRefMode.Int ? m_Id : RuntimeHelpers.GetHashCode(m_Asset);
    }

    // a channel's receivers in registration order. A removal while the list is walked leaves a hole the outermost walk's
    // end closes; a walk goes to the count it started with (one registered meanwhile takes the next command)
    internal sealed class ReceiverList<T> where T : class
    {
        internal T[] Items = new T[4];
        internal int Count;
        internal int Walking;
        int m_Holes;

        internal int Live => Count - m_Holes;

        internal void Add(T receiver)
        {
            if (Count == Items.Length)
                Array.Resize(ref Items, Count * 2);
            Items[Count++] = receiver;
        }

        internal bool Remove(T receiver)
        {
            for (int i = 0; i < Count; i++)
            {
                if (!ReferenceEquals(Items[i], receiver))
                    continue;
                if (Walking > 0)
                {
                    Items[i] = null;
                    m_Holes++;
                    return true;
                }
                for (int j = i + 1; j < Count; j++)
                    Items[j - 1] = Items[j];
                Items[--Count] = null;
                return true;
            }
            return false;
        }

        internal void BeginWalk() => Walking++;

        internal void EndWalk()
        {
            if (--Walking > 0 || m_Holes == 0)
                return;
            int n = 0;
            for (int i = 0; i < Count; i++)
            {
                if (Items[i] != null)
                    Items[n++] = Items[i];
            }
            for (int i = n; i < Count; i++)
                Items[i] = null;
            Count = n;
            m_Holes = 0;
        }
    }

    // one channel's row of the walk — its key and the live receivers of each kind (the signal receivers too)
    internal readonly struct ChannelRow
    {
        internal readonly ChannelKey Key;
        internal readonly int Commands;
        internal readonly int Shakes;
        internal readonly int Signals;

        internal ChannelRow(ChannelKey key, int commands, int shakes, int signals)
        {
            Key = key;
            Commands = commands;
            Shakes = shakes;
            Signals = signals;
        }
    }

    // a receiver of commands (FeedbackReceiver) — the send's position too: a Play without one plays there
    internal interface ICommandReceiver
    {
        Vector3 Position { get; }
        float Range { get; }
        void Receive(in FeedbackCommand command, bool hasPosition, Vector3 position);
    }

    // the context's registry — the command receivers and the camera receivers per channel. Nothing to put back at the end. An
    // emptied list stays (a channel comes back without a new list)
    internal sealed class ChannelService : FeedbackService
    {
        readonly Dictionary<ChannelKey, ReceiverList<ICommandReceiver>> m_Commands = new Dictionary<ChannelKey, ReceiverList<ICommandReceiver>>();
        readonly Dictionary<ChannelKey, ReceiverList<CameraShakeReceiver>> m_Shakes = new Dictionary<ChannelKey, ReceiverList<CameraShakeReceiver>>();
        // the signal receivers · the sends in progress (a receiver that sends again)
        readonly Dictionary<ChannelKey, ReceiverList<IFeedbackSignalReceiver>> m_Signals = new Dictionary<ChannelKey, ReceiverList<IFeedbackSignalReceiver>>();
        int m_SignalDepth;
#if DEBUG
        bool m_WarnedDepth;
#endif

        internal const int SignalDepth = 8;
        internal const string SignalDepthWarning = "A Feedback signal was sent from inside its receivers more than 8 deep — the send was dropped.";

        internal ChannelService(FeedbackContext context)
            : base(context)
        {
        }

        static ReceiverList<T> ListOf<T>(Dictionary<ChannelKey, ReceiverList<T>> lists, in ChannelKey key, bool make) where T : class
        {
            if (lists.TryGetValue(key, out ReceiverList<T> list))
                return list;
            if (!make)
                return null;
            list = new ReceiverList<T>();
            lists.Add(key, list);
            return list;
        }

        internal bool Register(ICommandReceiver receiver, in ChannelKey key)
        {
            if (key.IsNone || Refuses())
                return false;
            ListOf(m_Commands, in key, true).Add(receiver);
            return true;
        }

        internal void Unregister(ICommandReceiver receiver, in ChannelKey key) => ListOf(m_Commands, in key, false)?.Remove(receiver);

        internal bool Register(CameraShakeReceiver receiver, in ChannelKey key)
        {
            if (key.IsNone || Refuses())
                return false;
            ListOf(m_Shakes, in key, true).Add(receiver);
            return true;
        }

        internal void Unregister(CameraShakeReceiver receiver, in ChannelKey key) => ListOf(m_Shakes, in key, false)?.Remove(receiver);

        internal ReceiverList<CameraShakeReceiver> Shakes(in ChannelKey key) => ListOf(m_Shakes, in key, false);

        internal bool Register(IFeedbackSignalReceiver receiver, in ChannelKey key)
        {
            if (key.IsNone || Refuses())
                return false;
            ListOf(m_Signals, in key, true).Add(receiver);
            return true;
        }

        internal void Unregister(IFeedbackSignalReceiver receiver, in ChannelKey key) => ListOf(m_Signals, in key, false)?.Remove(receiver);

        internal int SignalReceivers(in ChannelRef channel) => ListOf(m_Signals, new ChannelKey(in channel), false)?.Live ?? 0;

        // to the receivers registered when it is called — a receiver's exception is logged and the next takes it; a send from a
        // receiver goes through up to SignalDepth deep. The number that took it
        internal int Send(in FeedbackSignal signal)
        {
            if (Refuses())
                return 0;
            ReceiverList<IFeedbackSignalReceiver> list = ListOf(m_Signals, new ChannelKey(in signal.Channel), false);
            if (list == null)
                return 0;
            if (m_SignalDepth >= SignalDepth)
            {
#if DEBUG
                if (!m_WarnedDepth)
                {
                    m_WarnedDepth = true;
                    Debug.LogWarning(SignalDepthWarning);
                }
#endif
                return 0;
            }
            int taken = 0;
            int count = list.Count;
            m_SignalDepth++;
            list.BeginWalk();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    IFeedbackSignalReceiver receiver = list.Items[i];
                    if (receiver == null)
                        continue;
                    taken++;
                    try
                    {
                        receiver.OnFeedbackSignal(in signal);
                    }
                    catch (Exception e)
                    {
                        Context.LogException(e);
                    }
                }
            }
            finally
            {
                list.EndWalk();
                m_SignalDepth--;
            }
            return taken;
        }

        // observation: the live receivers of a channel
        internal int CommandReceivers(in ChannelRef channel) => ListOf(m_Commands, new ChannelKey(in channel), false)?.Live ?? 0;

        internal int ShakeReceivers(in ChannelRef channel) => ListOf(m_Shakes, new ChannelKey(in channel), false)?.Live ?? 0;

        // observation: every channel that has a list, once — the three kinds of a key on one row, an emptied list as 0
        internal void Describe(List<ChannelRow> into)
        {
            foreach (KeyValuePair<ChannelKey, ReceiverList<ICommandReceiver>> e in m_Commands)
                into.Add(new ChannelRow(e.Key, e.Value.Live, LiveOf(m_Shakes, e.Key), LiveOf(m_Signals, e.Key)));
            foreach (KeyValuePair<ChannelKey, ReceiverList<CameraShakeReceiver>> e in m_Shakes)
            {
                if (!m_Commands.ContainsKey(e.Key))
                    into.Add(new ChannelRow(e.Key, 0, e.Value.Live, LiveOf(m_Signals, e.Key)));
            }
            foreach (KeyValuePair<ChannelKey, ReceiverList<IFeedbackSignalReceiver>> e in m_Signals)
            {
                if (!m_Commands.ContainsKey(e.Key) && !m_Shakes.ContainsKey(e.Key))
                    into.Add(new ChannelRow(e.Key, 0, 0, e.Value.Live));
            }
        }

        static int LiveOf<T>(Dictionary<ChannelKey, ReceiverList<T>> lists, in ChannelKey key) where T : class
            => lists.TryGetValue(key, out ReceiverList<T> list) ? list.Live : 0;

        // to the receivers registered when it is called, each within its range of a given position. A receiver's exception is
        // logged and the next takes the command; a send from a handler goes through. The number that took it
        internal int Send(in FeedbackCommand command, bool hasPosition, Vector3 position)
        {
            if (Refuses())
                return 0;
            ReceiverList<ICommandReceiver> list = ListOf(m_Commands, new ChannelKey(command.Channel), false);
            if (list == null)
                return 0;
            int taken = 0;
            int count = list.Count;
            list.BeginWalk();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    ICommandReceiver receiver = list.Items[i];
                    if (receiver == null)
                        continue;
                    float range = receiver.Range;
                    if (hasPosition && range > 0f && (receiver.Position - position).sqrMagnitude > range * range)
                        continue;
                    taken++;
                    try
                    {
                        receiver.Receive(in command, hasPosition, position);
                    }
                    catch (Exception e)
                    {
                        Context.LogException(e);
                    }
                }
            }
            finally
            {
                list.EndWalk();
            }
            return taken;
        }

        internal override void Shutdown()
        {
            m_Commands.Clear();
            m_Shakes.Clear();
            m_Signals.Clear();
        }
    }
}
