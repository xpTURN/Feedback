namespace xpTURN.Feedback
{
    // a slot's contents — the runner and its state number. Readonly, so a slot read by ref readonly is never copied
    // defensively. The slot map keeps the ledger (handle number, death)
    internal readonly struct PlaybackSlot
    {
        internal readonly IPlaybackRunner Runner;
        internal readonly int State;

        internal PlaybackSlot(IPlaybackRunner runner, int state)
        {
            Runner = runner;
            State = state;
        }
    }
}
