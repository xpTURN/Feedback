using UnityEngine;

namespace xpTURN.Feedback
{
    // the loop's context (Default)
    /// <summary>Sends commands to the receivers of a channel.</summary>
    public static class FeedbackChannels
    {
        internal const string NoContextWarning = "FeedbackChannels.Send was called with no running Feedback context (outside play); nothing was sent.";
#if DEBUG
        static bool s_Warned;
#endif

        /// <summary>Sends <paramref name="command"/> to its channel's receivers and returns how many took it; outside play nothing is sent (one warning).</summary>
        public static int Send(in FeedbackCommand command) => Send(in command, false, default);

        /// <summary>Sends <paramref name="command"/> from <paramref name="position"/>: a receiver with a range takes it only within its range, and a Play command without a position of its own plays there.</summary>
        public static int Send(in FeedbackCommand command, Vector3 position) => Send(in command, true, position);

        static int Send(in FeedbackCommand command, bool hasPosition, Vector3 position)
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
                return 0;
            }
            return s.Context.Channels.Send(in command, hasPosition, position);
        }
    }
}
