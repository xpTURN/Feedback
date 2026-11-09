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

        internal static void Raise(FeedbackPlayer player, PlayerEventKind kind, PlaybackHandle handle) => PlayerEvent?.Invoke(player, kind, handle);

        internal static void RaiseEnded(FeedbackPlayer player, PlaybackHandle handle, PlaybackEnd end, bool quiet) => PlaybackEnded?.Invoke(player, handle, end, quiet);
    }
}
