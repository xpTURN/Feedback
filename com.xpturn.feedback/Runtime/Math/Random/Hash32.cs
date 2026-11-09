namespace xpTURN.Feedback
{
    // lowbias32 (Chris Wellons, hash-prospector — public domain) and a pair mix.
    // Every use of a seed goes through its own tag; the tag values are the saved look of every shake — fixed
    internal static class Hash32
    {
        internal const uint Component = 0x100;
        internal const uint Octave = 0x200;
        internal const uint Copy = 0x300;
        internal const uint Occurrence = 0x400;
        internal const uint Chance = 0x500;
        internal const uint Duration = 0x600;
        internal const uint Output = 0x700;
        // a weighted pick of an occurrence
        internal const uint Pick = 0x800;

        internal static uint Mix(uint x)
        {
            unchecked
            {
                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                x *= 0x846CA68Bu;
                x ^= x >> 16;
                return x;
            }
        }

        internal static uint Of(uint seed, uint i)
        {
            unchecked
            {
                return Mix(seed ^ Mix(i + 0x9E3779B9u));
            }
        }

        internal static uint Tagged(uint seed, uint tag, uint i) => Of(Of(seed, tag), i);

        // a floored lattice or period number as a hash index — the high half folded in
        internal static uint Fold(long n)
        {
            unchecked
            {
                return (uint)(n ^ (n >> 32));
            }
        }

        // [0, 1) from the top 24 bits
        internal static double Unit(uint seed, uint i) => (Of(seed, i) >> 8) * (1.0 / 16777216.0);

        internal static float ToUnit(uint x) => (x >> 8) * (1f / 16777216f);
    }
}
