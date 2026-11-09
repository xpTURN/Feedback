namespace xpTURN.Feedback
{
    // a slot's (handle number, generation). default is invalid — generation 0 is never handed out. Core's PlaybackHandle
    // wraps the same two values.
    internal readonly struct SlotHandle
    {
        public readonly int Index;
        public readonly uint Generation;

        public SlotHandle(int index, uint generation)
        {
            Index = index;
            Generation = generation;
        }
    }
}
