using UnityEngine;

namespace xpTURN.Feedback
{
    // not saved
    /// <summary>How a Stop ends the plays it reaches.</summary>
    public enum StopMode
    {
        /// <summary>Keeps what Persist effects wrote.</summary>
        Hold = 0,
        /// <summary>Puts the play's start values back.</summary>
        Revert = 1,
        /// <summary>Evaluates the end of each item that runs now (not the endless ones or those not started yet), then stops; offsets and overrides go back at once.</summary>
        Complete = 2,
    }

    // every value has its own mark, so default(PlayArgs) and new PlayArgs() are not a play of intensity 0. The listener
    // needs no mark — null is none
    /// <summary>What one Play carries — intensity, seed, position, direction and listener; default is a play of intensity 1 with the player's own settings.</summary>
    public readonly struct PlayArgs
    {
        const byte IntensityMark = 1;
        const byte SeedMark = 2;
        const byte PositionMark = 4;
        const byte DirectionMark = 8;
        // a preview's unconditional play — read in the Preview kind only
        const byte UnconditionalMark = 16;
        // a track's driven play — the clock does not move it, its track seeks it
        const byte DrivenMark = 32;

        readonly float m_Intensity;
        readonly uint m_Seed;
        readonly Vector3 m_Position;
        readonly byte m_Direction;
        readonly byte m_Marks;
        readonly IPlaybackListener m_Listener;

        PlayArgs(float intensity, uint seed, Vector3 position, byte direction, byte marks, IPlaybackListener listener)
        {
            m_Intensity = intensity;
            m_Seed = seed;
            m_Position = position;
            m_Direction = direction;
            m_Marks = marks;
            m_Listener = listener;
        }

        /// <summary>The default play: intensity 1, no seed, no position, the player's direction, no listener.</summary>
        public static PlayArgs Default => default;

        /// <summary>The play's intensity; 1 unless set.</summary>
        public float Intensity => (m_Marks & IntensityMark) != 0 ? m_Intensity : 1f;

        /// <summary>True when a seed is set.</summary>
        public bool HasSeed => (m_Marks & SeedMark) != 0;

        /// <summary>The seed of the play's random parts, when <see cref="PlayArgs.HasSeed"/> is true.</summary>
        public uint Seed => m_Seed;

        /// <summary>True when a position is set.</summary>
        public bool HasPosition => (m_Marks & PositionMark) != 0;

        /// <summary>The play's position, when <see cref="PlayArgs.HasPosition"/> is true.</summary>
        public Vector3 Position => m_Position;

        /// <summary>True when a direction is set; without one the player's own direction applies.</summary>
        public bool HasDirection => (m_Marks & DirectionMark) != 0;

        /// <summary>The play's direction, when <see cref="PlayArgs.HasDirection"/> is true; Toggle reads and turns the player's own toggle.</summary>
        public PlayerDirection Direction => (PlayerDirection)m_Direction;

        /// <summary>The listener that hears the play's end — hooked before the play starts, so a play that ends inside Play is heard too; null for none.</summary>
        public IPlaybackListener Listener => m_Listener;

        /// <summary>A copy with the intensity set.</summary>
        public PlayArgs WithIntensity(float intensity) => new PlayArgs(intensity, m_Seed, m_Position, m_Direction, (byte)(m_Marks | IntensityMark), m_Listener);

        /// <summary>A copy with the seed set.</summary>
        public PlayArgs WithSeed(uint seed) => new PlayArgs(m_Intensity, seed, m_Position, m_Direction, (byte)(m_Marks | SeedMark), m_Listener);

        /// <summary>A copy with the position set.</summary>
        public PlayArgs WithPosition(Vector3 position) => new PlayArgs(m_Intensity, m_Seed, position, m_Direction, (byte)(m_Marks | PositionMark), m_Listener);

        // a value past a byte stays undefined rather than wrapping onto a defined one
        /// <summary>A copy with the direction set; a value that is not a defined direction makes Play refuse it.</summary>
        public PlayArgs WithDirection(PlayerDirection direction)
            => new PlayArgs(m_Intensity, m_Seed, m_Position, (uint)direction <= byte.MaxValue ? (byte)direction : byte.MaxValue, (byte)(m_Marks | DirectionMark), m_Listener);

        /// <summary>A copy with the listener set; null for none.</summary>
        public PlayArgs WithListener(IPlaybackListener listener) => new PlayArgs(m_Intensity, m_Seed, m_Position, m_Direction, m_Marks, listener);

        internal bool Unconditional => (m_Marks & UnconditionalMark) != 0;

        internal PlayArgs WithUnconditional() => new PlayArgs(m_Intensity, m_Seed, m_Position, m_Direction, (byte)(m_Marks | UnconditionalMark), m_Listener);

        internal bool Driven => (m_Marks & DrivenMark) != 0;

        internal PlayArgs WithDriven() => new PlayArgs(m_Intensity, m_Seed, m_Position, m_Direction, (byte)(m_Marks | DrivenMark), m_Listener);
    }
}
