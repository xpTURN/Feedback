using UnityEditor;

namespace xpTURN.Feedback.Editor
{
    internal enum DebuggerSourceKind
    {
        None,
        Play,
        Preview,
        Test,
    }

    // the scheduler the debugger reads, picked at each refresh and never kept — the loop's Default is a new one each session
    // and each recompile during play, and it stays closed after play (not null). The test's source first, then a playing
    // open Default (a paused play too), then the preview session's
    internal static class DebuggerSource
    {
        // a test swaps the source and clears it at its end
        internal static FeedbackScheduler Override;

        internal static FeedbackScheduler Pick(out DebuggerSourceKind kind)
        {
            if (Override != null && !Override.IsShutdown)
            {
                kind = DebuggerSourceKind.Test;
                return Override;
            }
            FeedbackScheduler loop = FeedbackScheduler.Default;
            if (EditorApplication.isPlaying && loop != null && !loop.IsShutdown)
            {
                kind = DebuggerSourceKind.Play;
                return loop;
            }
            FeedbackScheduler preview = PreviewSession.Current?.Scheduler;
            if (preview != null && !preview.IsShutdown)
            {
                kind = DebuggerSourceKind.Preview;
                return preview;
            }
            kind = DebuggerSourceKind.None;
            return null;
        }
    }
}
