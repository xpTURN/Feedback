using System;

namespace xpTURN.Feedback
{
    // the event table's third stage — the debugger's hook (the editor subscribes through its assembly's friend line).
    // ResetSession does not clear it: a subscription that outlives a session with the domain reload off is its subscriber's
    internal static class FeedbackDiagnostics
    {
        internal static event Action<FeedbackPlayer, PlayerEventKind, PlaybackHandle> PlayerEvent;

        // every end of an accepted play, before its listener — quiet: no Completed or Stopped event came with it
        internal static event Action<FeedbackPlayer, PlaybackHandle, PlaybackEnd, bool> PlaybackEnded;

        // a scheduler that is not dry, after its LateApply stage — the frame's values are written (the editor's record)
        internal static event Action<FeedbackScheduler> LayersApplied;

        // a subscriber's exception is logged and the frame goes on
        internal static void RaiseApplied(FeedbackScheduler scheduler)
        {
            Action<FeedbackScheduler> handler = LayersApplied;
            if (handler == null)
                return;
            try
            {
                handler(scheduler);
            }
            catch (Exception e)
            {
                scheduler.Context.LogException(e);
            }
        }

        internal static void Raise(FeedbackPlayer player, PlayerEventKind kind, PlaybackHandle handle) => PlayerEvent?.Invoke(player, kind, handle);

        internal static void RaiseEnded(FeedbackPlayer player, PlaybackHandle handle, PlaybackEnd end, bool quiet) => PlaybackEnded?.Invoke(player, handle, end, quiet);
    }
}
