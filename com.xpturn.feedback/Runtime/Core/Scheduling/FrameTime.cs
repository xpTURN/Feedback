namespace xpTURN.Feedback
{
    // one frame's deltas, passed by in
    internal readonly struct FrameTime
    {
        internal readonly float ScaledDelta;
        internal readonly float UnscaledDelta;

        internal FrameTime(float scaledDelta, float unscaledDelta)
        {
            ScaledDelta = scaledDelta;
            UnscaledDelta = unscaledDelta;
        }
    }
}
