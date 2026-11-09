namespace xpTURN.Feedback
{
    // what a binding starts or executes with
    internal readonly struct BeginContext
    {
        internal readonly float Intensity;      // the play's intensity × this item's random output (IntensityResponse.Ignore: the output alone)
        internal readonly uint ItemSeed;        // Hash32(the play's seed, the list index)
        internal readonly long Occurrence;      // the event's — fixed by ν
        internal readonly long Serial;          // the play's number in its player: a binding resets its per-play cells when it changes
        internal readonly bool Reverse;         // the event's direction — a backward Started begins on the T = 1 side, a backward instant undoes
        // the occurrence's real length (the resolved length / Speed, +∞ at Speed 0 — a hit stop's request) and the play's
        // place (a channel shake's distance — the Play's place once the player is gone)
        internal readonly double Seconds;
        internal readonly bool HasPosition;
        internal readonly UnityEngine.Vector3 Position;
        internal readonly bool Swept;           // a Seek's or a Skip's sweep reached the event, not a frame

        internal BeginContext(float intensity, uint itemSeed, long occurrence, long serial, bool reverse)
            : this(intensity, itemSeed, occurrence, serial, reverse, 0.0, false, default)
        {
        }

        internal BeginContext(float intensity, uint itemSeed, long occurrence, long serial, bool reverse, double seconds, bool hasPosition, UnityEngine.Vector3 position, bool swept = false)
        {
            Swept = swept;
            Intensity = intensity;
            ItemSeed = itemSeed;
            Occurrence = occurrence;
            Serial = serial;
            Reverse = reverse;
            Seconds = seconds;
            HasPosition = hasPosition;
            Position = position;
        }
    }

    // why a binding's End came. The public reason of a play's end is PlaybackEnd — its listener's
    internal enum EndReason
    {
        Completed,
        Hold,
        Revert,
        Complete,
        Aborted,
    }
}
