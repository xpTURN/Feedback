namespace xpTURN.Feedback
{
    public enum StartRule : byte
    {
        WithPrevious = 0,
        AfterPrevious = 1,
        AfterAll = 2,
    }

    internal enum TimelineItemKind : byte
    {
        Effect = 0,
        Wait = 1,
        SignalWait = 2,
        LoopStart = 3,
        LoopEnd = 4,
        Hold = 5,
    }

    internal enum TimelineClock : byte
    {
        Scaled = 0,
        Unscaled = 1,
        Player = 2,
    }

    // Times are seconds; Duration may be +inf.
    internal struct TimelineItemDesc
    {
        public TimelineItemKind Kind;
        public bool Instant;
        public StartRule Rule;
        public float Delay;
        public float Duration;
        public TimelineClock Clock;
        public int Repeat;       // additional runs, -1 = infinite
        public float RepeatGap;
        public int LoopCount;    // LoopEnd: total iterations, 0 = infinite
        public float LoopGap;
        public float Timeout;    // SignalWait: 0 = none
        public bool Background;
        // an effect its binding ends — Duration is its time limit or +inf. It opens the program like an untimed signal
        // wait, with a time limit too
        public bool Open;
    }
}
