using UnityEngine;

namespace xpTURN.Feedback
{
    /// <summary>Takes the signals sent to a channel it is registered on — a Signal Broadcast item's or FeedbackSignals.Send's.</summary>
    public interface IFeedbackSignalReceiver
    {
        /// <summary>Called with each signal sent to the channel.</summary>
        void OnFeedbackSignal(in FeedbackSignal signal);
    }

    /// <summary>What one signal carries: its value, intensity, place and sender.</summary>
    public readonly struct FeedbackSignal
    {
        /// <summary>The channel it is sent to.</summary>
        public readonly ChannelRef Channel;
        /// <summary>The item's value times the occurrence's intensity (see Intensity).</summary>
        public readonly float Value;
        /// <summary>The occurrence's intensity: the play's intensity times the item's random output, or the output alone when the item ignores intensity.</summary>
        public readonly float Intensity;
        /// <summary>True when the signal has a place.</summary>
        public readonly bool HasPosition;
        /// <summary>The place, when <see cref="FeedbackSignal.HasPosition"/> is true.</summary>
        public readonly Vector3 Position;
        /// <summary>The player that sent it; null when code sends it without one.</summary>
        public readonly FeedbackPlayer Sender;

        /// <summary>A signal for <paramref name="channel"/>, to send from code.</summary>
        public FeedbackSignal(in ChannelRef channel, float value, float intensity = 1f, bool hasPosition = false, Vector3 position = default, FeedbackPlayer sender = null)
        {
            Channel = channel;
            Value = value;
            Intensity = intensity;
            HasPosition = hasPosition;
            Position = position;
            Sender = sender;
        }
    }

    // the loop's context (Default), like FeedbackChannels
    /// <summary>Registers signal receivers on channels and sends signals to them.</summary>
    public static class FeedbackSignals
    {
        internal const string NoContextWarning = "FeedbackSignals was called with no running Feedback context (outside play); nothing happened.";
#if DEBUG
        static bool s_Warned;
#endif

        /// <summary>Registers <paramref name="receiver"/> on <paramref name="channel"/> — call it from OnEnable, since each play session and each recompile starts with no receivers; false outside play, for a null receiver or for an asset channel without its asset.</summary>
        public static bool Register(in ChannelRef channel, IFeedbackSignalReceiver receiver)
        {
            ChannelService channels = Channels();
            return channels != null && receiver != null && channels.Register(receiver, new ChannelKey(in channel));
        }

        /// <summary>Takes <paramref name="receiver"/> off <paramref name="channel"/>.</summary>
        public static void Unregister(in ChannelRef channel, IFeedbackSignalReceiver receiver)
        {
            FeedbackScheduler s = FeedbackScheduler.Default;
            if (s != null && !s.IsShutdown && receiver != null)
                s.Context.Channels.Unregister(receiver, new ChannelKey(in channel));
        }

        /// <summary>Sends <paramref name="signal"/> to its channel's receivers and returns how many it reached — a receiver that throws or drops it out of range counts; 0 when sends nest too deep.</summary>
        public static int Send(in FeedbackSignal signal)
        {
            ChannelService channels = Channels();
            return channels != null ? channels.Send(in signal) : 0;
        }

        static ChannelService Channels()
        {
            FeedbackScheduler s = FeedbackScheduler.Default;
            if (s == null || s.IsShutdown)
            {
#if DEBUG
                if (!s_Warned)
                {
                    s_Warned = true;
                    Debug.LogWarning(NoContextWarning);
                }
#endif
                return null;
            }
            return s.Context.Channels;
        }
    }
}
