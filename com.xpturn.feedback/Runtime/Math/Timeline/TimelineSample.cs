namespace xpTURN.Feedback
{
    internal enum TimelineStage : byte
    {
        Pending = 0,
        Running = 1,
        Done = 2,
    }

    // the state at a nominal position. A node's occurrence is the most recently reached one —
    // forward: the latest start ≤ ν, reverse: the earliest effective end ≥ ν. Pending = none reached (occurrence -1).
    internal sealed class TimelineSample
    {
        internal readonly TimelineStage[] _stage;
        internal readonly long[] _occurrence;
        internal readonly long[] _start;
        internal readonly long[] _naturalEnd;
        internal readonly long[] _effectiveEnd;
        internal readonly long[] _scopeOccurrence;   // per block: the latest started iteration of its scope (linearized)

        public TimelineProgram Program { get; }
        public long Position { get; internal set; }
        public bool Reverse { get; internal set; }

        public TimelineSample(TimelineProgram program)
        {
            Program = program;
            int nodes = program.NodeCount;
            _stage = new TimelineStage[nodes];
            _occurrence = new long[nodes];
            _start = new long[nodes];
            _naturalEnd = new long[nodes];
            _effectiveEnd = new long[nodes];
            _scopeOccurrence = new long[program.BlockCount];
        }

        public TimelineStage NodeStage(int node) => _stage[node];
        public long NodeOccurrence(int node) => _occurrence[node];
        public long NodeStart(int node) => _start[node];
        public long NodeNaturalEnd(int node) => _naturalEnd[node];
        public long NodeEffectiveEnd(int node) => _effectiveEnd[node];
        public long BlockScopeOccurrence(int block) => _scopeOccurrence[block];

        public TimelineStage Stage(int item) => _stage[Program.NodeOfItem(item)];
        public long Occurrence(int item) => _occurrence[Program.NodeOfItem(item)];

        // Forward coordinate in both directions; the denominator is the natural duration.
        public float LocalTime(int item)
        {
            int node = Program.NodeOfItem(item);
            if (_stage[node] == TimelineStage.Pending)
                return 0f;
            long duration = _naturalEnd[node] - _start[node];
            if (_naturalEnd[node] == TimelineTime.Infinite)
                return 0f;
            if (duration <= 0)
                return Position >= _start[node] ? 1f : 0f;
            double t = (double)(Position - _start[node]) / duration;
            return t <= 0 ? 0f : t >= 1 ? 1f : (float)t;
        }

        // measured from the natural end
        public float LocalTimeFromEnd(int item)
        {
            int node = Program.NodeOfItem(item);
            if (_stage[node] == TimelineStage.Pending)
                return 1f;
            long duration = _naturalEnd[node] - _start[node];
            if (_naturalEnd[node] == TimelineTime.Infinite)
                return 0f;
            if (duration <= 0)
                return Position >= _start[node] ? 0f : 1f;
            double t = (double)(_naturalEnd[node] - Position) / duration;
            return t <= 0 ? 0f : t >= 1 ? 1f : (float)t;
        }

        public double SecondsFromEnd(int item)
        {
            int node = Program.NodeOfItem(item);
            return _stage[node] == TimelineStage.Pending ? (_naturalEnd[node] - _start[node]) / TimelineTime.MicrosPerSecond : (_naturalEnd[node] - Position) / TimelineTime.MicrosPerSecond;
        }

        public double Seconds(int item)
        {
            int node = Program.NodeOfItem(item);
            return _stage[node] == TimelineStage.Pending ? 0.0 : (Position - _start[node]) / TimelineTime.MicrosPerSecond;
        }
    }
}
