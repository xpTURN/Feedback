namespace xpTURN.Feedback
{
    // xorshift32 (13 · 17 · 5). State 0 would repeat 0 forever — a seed of 0 and a default
    // struct both start from the fixed constant instead
    internal struct XorShiftRandom
    {
        const uint ZeroSeed = 0x6D2B79F5u;

        uint _state;

        internal XorShiftRandom(uint seed)
        {
            _state = seed != 0u ? seed : ZeroSeed;
        }

        internal uint Next()
        {
            uint x = _state != 0u ? _state : ZeroSeed;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        // [0, 1)
        internal float NextUnit() => Hash32.ToUnit(Next());
    }
}
