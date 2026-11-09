using UnityEngine;

namespace xpTURN.Feedback
{
    // the loop's context's range center — a distance gate on DistanceReference.Global measures from it. None set or a destroyed
    // one does not stop a play. Outside play there is no context: a write warns once and does nothing
    /// <summary>The center that distance gates on the global reference measure from.</summary>
    public static class FeedbackRange
    {
        internal const string NoContextWarning = "FeedbackRange.Center was set with no running Feedback context (outside play) and was ignored.";
#if DEBUG
        static bool s_Warned;
#endif

        /// <summary>The transform distance gates on the global reference measure from; null outside play, where a write is ignored with a warning.</summary>
        public static Transform Center
        {
            get
            {
                FeedbackScheduler s = FeedbackScheduler.Default;
                return s != null && !s.IsShutdown ? s.Context.RangeCenter : null;
            }
            set
            {
                FeedbackScheduler s = FeedbackScheduler.Default;
                if (s != null && !s.IsShutdown)
                {
                    s.Context.RangeCenter = value;
                    return;
                }
#if DEBUG
                if (!s_Warned)
                {
                    s_Warned = true;
                    Debug.LogWarning(NoContextWarning);
                }
#endif
            }
        }
    }
}
