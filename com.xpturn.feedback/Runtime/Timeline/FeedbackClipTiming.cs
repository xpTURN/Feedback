namespace xpTURN.Feedback
{
    // how a Feedback clip plays its track's player — the saved values, add members at the end only
    internal enum FeedbackClipTiming
    {
        Auto = 0,       // Driven when the list can be scrubbed, else Triggered
        Driven = 1,     // the clip's local time seeks one play
        Triggered = 2,  // a play starts with the clip and runs on the player's clock
    }

    // how a clip ends its play when the timeline leaves it — the saved values
    internal enum FeedbackClipEnd
    {
        Revert = 0,
        Hold = 1,
        Complete = 2,
    }
}
