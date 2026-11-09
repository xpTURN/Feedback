namespace xpTURN.Feedback
{
    // what the scheduler drives
    internal interface IPlaybackRunner
    {
        // RunUpdate's runners step. false = ended, after the runner has cleaned up
        bool Advance(int state, in FrameTime time);

        // the scheduler ends the playback — the slot is already dead and the handle invalid when this is called
        void Abort(int state, AbortReason reason);

        UnityEngine.Object DebugOwner { get; }
    }
}
