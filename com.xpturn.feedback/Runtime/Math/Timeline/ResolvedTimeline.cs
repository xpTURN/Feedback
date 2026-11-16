namespace xpTURN.Feedback
{
    // one play's resolved times (microseconds). Allocated once per play record; Resolve refills it. Node times: Local*
    // are relative to the node's scope (iteration) start, Start/End are the first occurrence on the nominal axis (clock
    // ratio 1, root delay included).
    internal sealed class ResolvedTimeline
    {
        internal readonly long[] _itemDuration;
        internal readonly bool[] _itemSkipped;
        internal readonly long[] _nodeDelay;
        internal readonly long[] _nodeHoldDelay;
        internal readonly long[] _nodeLocalStart;
        internal readonly long[] _nodeLocalEnd;
        internal readonly long[] _nodeStart;
        internal readonly long[] _nodeEnd;
        internal readonly bool[] _nodeCancelled;
        internal readonly long[] _blockLength;
        internal readonly long[] _blockGap;

        public TimelineProgram Program { get; }
        public long RootDelay { get; internal set; }
        public long Total { get; internal set; }
        public bool IsSeekable { get; internal set; }

        public ResolvedTimeline(TimelineProgram program)
        {
            Program = program;
            _itemDuration = new long[program.ItemCount];
            _itemSkipped = new bool[program.ItemCount];
            _nodeDelay = new long[program.NodeCount];
            _nodeHoldDelay = new long[program.NodeCount];
            _nodeLocalStart = new long[program.NodeCount];
            _nodeLocalEnd = new long[program.NodeCount];
            _nodeStart = new long[program.NodeCount];
            _nodeEnd = new long[program.NodeCount];
            _nodeCancelled = new bool[program.NodeCount];
            _blockLength = new long[program.BlockCount];
            _blockGap = new long[program.BlockCount];
        }

        public double TotalSeconds => TimelineTime.ToSeconds(Total);

        // every time into another of the same program's shape
        internal void CopyTo(ResolvedTimeline to)
        {
            System.Array.Copy(_itemDuration, to._itemDuration, _itemDuration.Length);
            System.Array.Copy(_itemSkipped, to._itemSkipped, _itemSkipped.Length);
            System.Array.Copy(_nodeDelay, to._nodeDelay, _nodeDelay.Length);
            System.Array.Copy(_nodeHoldDelay, to._nodeHoldDelay, _nodeHoldDelay.Length);
            System.Array.Copy(_nodeLocalStart, to._nodeLocalStart, _nodeLocalStart.Length);
            System.Array.Copy(_nodeLocalEnd, to._nodeLocalEnd, _nodeLocalEnd.Length);
            System.Array.Copy(_nodeStart, to._nodeStart, _nodeStart.Length);
            System.Array.Copy(_nodeEnd, to._nodeEnd, _nodeEnd.Length);
            System.Array.Copy(_nodeCancelled, to._nodeCancelled, _nodeCancelled.Length);
            System.Array.Copy(_blockLength, to._blockLength, _blockLength.Length);
            System.Array.Copy(_blockGap, to._blockGap, _blockGap.Length);
            to.RootDelay = RootDelay;
            to.Total = Total;
            to.IsSeekable = IsSeekable;
        }

        // the same counts of items, nodes and blocks
        internal bool SameShape(ResolvedTimeline other)
            => other != null && other._itemDuration.Length == _itemDuration.Length && other._nodeDelay.Length == _nodeDelay.Length
               && other._blockLength.Length == _blockLength.Length;

        public long ItemDuration(int item) => _itemDuration[item];
        public bool ItemSkipped(int item) => _itemSkipped[item];

        public long NodeDelay(int node) => _nodeDelay[node];

        // the Hold part of NodeDelay — counted on the Hold's clock
        public long NodeHoldDelay(int node) => _nodeHoldDelay[node];
        public long NodeLocalStart(int node) => _nodeLocalStart[node];
        public long NodeLocalEnd(int node) => _nodeLocalEnd[node];
        public long NodeStart(int node) => _nodeStart[node];
        public long NodeEnd(int node) => _nodeEnd[node];

        // a background node that has not started when the foreground ends never runs.
        public bool NodeCancelled(int node) => _nodeCancelled[node];

        // iteration length = latest member end within one iteration; period = length + gap
        public long BlockLength(int block) => _blockLength[block];
        public long BlockGap(int block) => _blockGap[block];
        public long BlockPeriod(int block) => TimelineTime.Add(_blockLength[block], _blockGap[block]);
    }
}
