namespace xpTURN.Feedback
{
    // a waiting play's listener that names the record waiting on it — the waiting chain's link
    internal interface IWaitLink
    {
        PlayerRuntime Runtime { get; }
        int Record { get; }
        long ChainSerial { get; }
    }

    // not saved
    /// <summary>Why a play ended — what its listener hears, once.</summary>
    public enum PlaybackEnd
    {
        /// <summary>The play reached its end.</summary>
        Completed = 0,
        /// <summary>The play stopped before its end: a Stop, a canceled token, a Restore, a Rebuild, a later Play the Policy made room for, the player turned off with Disable Behavior Stop Hold or Stop Revert, or a Skip of a play that cannot seek.</summary>
        Stopped = 1,
        /// <summary>An exception in one of the play's items ended it.</summary>
        Faulted = 2,
        /// <summary>The player was destroyed while the play ran and the play could not run on by itself; with Disable Behavior Continue, a play that has an end finishes instead.</summary>
        Destroyed = 3,
        /// <summary>Play mode ended, or a recompile reloaded the scripts, while the play ran.</summary>
        ContextShutdown = 4,
        /// <summary>PlayAsync's own: the play was not accepted (a condition, the policy, an argument, not prepared, a closed scheduler, a nested Play, a canceled token); a listener never hears it.</summary>
        Refused = 5,
    }

    /// <summary>Hears a play's end exactly once for each accepted play, after the end's event and on the main thread; set it with PlayArgs.WithListener or FeedbackPlayer.SetListener.</summary>
    public interface IPlaybackListener
    {
        /// <summary>Called once when the play <paramref name="handle"/> ends, with the reason <paramref name="end"/>.</summary>
        void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end);
    }
}
