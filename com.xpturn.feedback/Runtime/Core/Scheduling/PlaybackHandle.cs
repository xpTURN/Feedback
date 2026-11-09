using System;

namespace xpTURN.Feedback
{
    // (handle number, generation). Generations come from a domain-wide counter, so another scheduler's or a previous
    // session's handle never matches either
    /// <summary>Names one play; an ended handle never matches a live play, and default — what a refused Play returns — names none.</summary>
    public readonly struct PlaybackHandle : IEquatable<PlaybackHandle>
    {
        internal readonly int Index;
        internal readonly uint Generation;

        internal PlaybackHandle(SlotHandle handle)
        {
            Index = handle.Index;
            Generation = handle.Generation;
        }

        internal SlotHandle Slot => new SlotHandle(Index, Generation);

        /// <summary>True when both name the same play.</summary>
        public bool Equals(PlaybackHandle other) => Index == other.Index && Generation == other.Generation;

        /// <summary>True when <paramref name="obj"/> is a handle that names the same play.</summary>
        public override bool Equals(object obj) => obj is PlaybackHandle other && Equals(other);

        // the generation differs for every playback until the counter wraps
        /// <summary>A hash of the play's generation.</summary>
        public override int GetHashCode() => (int)Generation;

        /// <summary>True when both name the same play.</summary>
        public static bool operator ==(PlaybackHandle a, PlaybackHandle b) => a.Equals(b);

        /// <summary>True when they name different plays.</summary>
        public static bool operator !=(PlaybackHandle a, PlaybackHandle b) => !a.Equals(b);

        /// <summary>PlaybackHandle(index:generation), or PlaybackHandle(none) for default.</summary>
        public override string ToString() => Generation == 0 ? "PlaybackHandle(none)" : "PlaybackHandle(" + Index + ":" + Generation + ")";
    }
}
