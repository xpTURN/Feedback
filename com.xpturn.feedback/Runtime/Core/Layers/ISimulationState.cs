namespace xpTURN.Feedback
{
    // a slot that knows a simulated target's state — the preview's simulation reads it (the engine's simulate ignores a
    // pause, and its flags after a simulate are always the same)
    internal interface ISimulationState
    {
        // the last action paused it — not driven
        bool Held { get; }

        // the last action emits (a play) — false after a stop: the preview waits only for its particles
        bool Emits { get; }
    }
}
