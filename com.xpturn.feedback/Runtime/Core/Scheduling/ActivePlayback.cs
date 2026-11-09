namespace xpTURN.Feedback
{
    // one row of the query — for the editor, never on the playback path
    internal readonly struct ActivePlayback
    {
        internal readonly PlaybackHandle Handle;
        internal readonly IPlaybackRunner Runner;
        internal readonly UnityEngine.Object DebugOwner;
        internal readonly int State;

        internal ActivePlayback(PlaybackHandle handle, IPlaybackRunner runner, UnityEngine.Object debugOwner, int state)
        {
            Handle = handle;
            Runner = runner;
            DebugOwner = debugOwner;
            State = state;
        }
    }
}
