namespace xpTURN.Feedback
{
    // closed-form sampling of a nominal position, window sweeps in both directions
    // (forward = the queue's simulation, reverse = each instant simulated forward and read backwards),
    // the reverse play head and sampling-side seeks. Allocates at construction only.
    internal sealed class TimelineSampler
    {
        enum Mode : byte
        {
            None,
            Forward,
            Reverse,
        }

        enum Head : byte
        {
            Idle,
            Delay,
            Running,
            Done,
        }

        readonly TimelineProgram _p;
        readonly ResolvedTimeline _r;
        readonly TimelineQueue _queue;
        readonly TimelineInstant _instant;
        readonly TimelineSample _seed;

        Mode _mode;
        long _reverseLo;
        long _reverseHi;

        Head _head;
        double _headElapsed;
        long _headPosition;
        bool _finishPending;

        public TimelineSampler(TimelineProgram program, ResolvedTimeline resolved)
        {
            _p = program;
            _r = resolved;
            _queue = new TimelineQueue(program, resolved, this, false);
            _instant = new TimelineInstant(program);
            _seed = new TimelineSample(program);
        }

        internal TimelineQueue Queue => _queue;
        internal TimelineInstant Instant => _instant;

        public long ReversePosition => _headPosition;
        public bool IsReverseFinished => _head == Head.Done;

        // ---- sampling ----

        public void Sample(long position, bool reverse, TimelineSample into)
        {
            into.Position = position;
            into.Reverse = reverse;
            long total = _r.Total;
            for (int node = 0; node < _p.NodeCount; node++)
            {
                int scope = _p.NodeScope(node);
                long localStart = _r.NodeLocalStart(node);
                long q;
                if (!reverse)
                {
                    q = StartCount(node, Clamp(node, position)) - 1;
                }
                else
                {
                    long valid = StartCount(node, Clamp(node, total));
                    long ended = position > total ? valid : TimelineTime.Min(EndedBefore(scope, _r.NodeLocalEnd(node), position), valid);
                    q = ended < valid ? ended : -1;
                }

                into._occurrence[node] = q;
                if (q < 0)
                {
                    into._stage[node] = TimelineStage.Pending;
                    into._start[node] = 0;
                    into._naturalEnd[node] = 0;
                    into._effectiveEnd[node] = 0;
                }
                else
                {
                    long scopeStart = ScopeIterationStart(scope, q);
                    long start = TimelineTime.Add(scopeStart, localStart);
                    int item = _p.NodeItem(node);
                    long natural = item >= 0 ? TimelineTime.Add(start, _r.ItemDuration(item)) : TimelineTime.Add(scopeStart, _r.NodeLocalEnd(node));
                    long effective = _p.NodeInBackground(node) ? TimelineTime.Min(natural, total) : natural;
                    into._start[node] = start;
                    into._naturalEnd[node] = natural;
                    into._effectiveEnd[node] = effective;
                    bool running = reverse ? position > start : position < effective;
                    into._stage[node] = running ? TimelineStage.Running : TimelineStage.Done;
                }

                int block = _p.NodeBlock(node);
                if (block < 0)
                    continue;
                int inner = TimelineProgram.BlockScope(block);
                if (!reverse)
                {
                    into._scopeOccurrence[block] = IterationCount(inner, Clamp(node, position)) - 1;
                }
                else
                {
                    long valid = IterationCount(inner, Clamp(node, total));
                    long ended = position > total ? valid : TimelineTime.Min(EndedBefore(inner, _r.BlockLength(block), position), valid);
                    into._scopeOccurrence[block] = ended < valid ? ended : -1;
                }
            }
        }

        // start of iteration q (linearized over the enclosing runs) of a scope
        public long ScopeIterationStart(int scope, long q)
        {
            if (scope == TimelineProgram.RootScope)
                return _r.RootDelay;
            int block = scope - 1;
            int blockNode = _p.BlockNode(block);
            int n = _p.BlockIterations(block);
            long run = n > 0 ? q / n : 0;
            long k = n > 0 ? q % n : q;
            long blockStart = TimelineTime.Add(ScopeIterationStart(_p.NodeScope(blockNode), run), _r.NodeLocalStart(blockNode));
            return TimelineTime.Add(blockStart, TimelineTime.Multiply(_r.BlockPeriod(block), k));
        }

        // iterations of a scope that start at or before x
        long IterationCount(int scope, long x)
        {
            if (scope == TimelineProgram.RootScope)
                return x >= _r.RootDelay ? 1 : 0;
            int block = scope - 1;
            int blockNode = _p.BlockNode(block);
            long runs = StartCount(blockNode, x);
            if (runs == 0)
                return 0;
            long run = runs - 1;
            long runStart = TimelineTime.Add(ScopeIterationStart(_p.NodeScope(blockNode), run), _r.NodeLocalStart(blockNode));
            int n = _p.BlockIterations(block);
            long period = _r.BlockPeriod(block);
            long inRun = period > 0 ? (x - runStart) / period + 1 : (n > 0 ? n : 1);
            if (n <= 0)
                return inRun;
            return TimelineTime.Add(TimelineTime.Multiply(run, n), TimelineTime.Min(inRun, n));
        }

        // occurrences of a node that start at or before x
        long StartCount(int node, long x)
        {
            long local = _r.NodeLocalStart(node);
            return local == TimelineTime.Infinite ? 0 : IterationCount(_p.NodeScope(node), x - local);
        }

        // iterations whose (local) end is before x
        long EndedBefore(int scope, long localEnd, long x)
            => localEnd == TimelineTime.Infinite ? 0 : IterationCount(scope, x - 1 - localEnd);

        // a background entity has no occurrence starting at or after the total
        long Clamp(int node, long x) => _p.NodeInBackground(node) ? TimelineTime.Min(x, _r.Total - 1) : x;

        // the latest instant before x that can carry an event (a superset is fine — an empty instant costs only time)
        long PreviousInstant(long x)
        {
            long total = _r.Total;
            long best = total < x ? total : long.MinValue;
            for (int node = 0; node < _p.NodeCount; node++)
            {
                int scope = _p.NodeScope(node);
                int block = _p.NodeBlock(node);
                if (block >= 0)
                {
                    if (_p.BlockIsSynthetic(block))
                        continue;
                    int inner = TimelineProgram.BlockScope(block);
                    long length = _r.BlockLength(block);
                    long ends = TimelineTime.Min(EndedBefore(inner, length, x), IterationCount(inner, total));
                    if (ends > 0)
                        best = TimelineTime.Max(best, TimelineTime.Add(ScopeIterationStart(inner, ends - 1), length));
                    continue;
                }

                long starts = StartCount(node, Clamp(node, x - 1));
                if (starts > 0)
                    best = TimelineTime.Max(best, TimelineTime.Add(ScopeIterationStart(scope, starts - 1), _r.NodeLocalStart(node)));

                long localEnd = TimelineTime.Add(_r.NodeLocalStart(node), _r.ItemDuration(_p.NodeItem(node)));
                long valid = StartCount(node, Clamp(node, total));
                long ended = TimelineTime.Min(EndedBefore(scope, localEnd, x), valid);
                if (ended > 0)
                {
                    long end = TimelineTime.Add(ScopeIterationStart(scope, ended - 1), localEnd);
                    best = TimelineTime.Max(best, _p.NodeInBackground(node) ? TimelineTime.Min(end, total) : end);
                }
            }
            return best;
        }

        // ---- sweeps ----

        // forward window (lo, hi]
        public void SweepForward(long lo, long hi)
        {
            _finishPending = false;
            Sample(lo, false, _seed);
            _queue.Seed(_seed);
            _queue.SetLimit(hi);
            _mode = Mode.Forward;
        }

        // reverse window [lo, hi), events in descending time, each instant the flipped forward order
        public void SweepReverse(long lo, long hi)
        {
            _finishPending = false;
            _reverseLo = lo;
            _reverseHi = hi;
            _instant.Clear();
            _instant.BeginReplay();
            _mode = Mode.Reverse;
        }

        // playback: from < to crosses (from, to], from > to crosses [to, from)
        public void SweepBegin(long from, long to)
        {
            if (from < to)
                SweepForward(from, to);
            else if (from > to)
                SweepReverse(to, from);
            else
                _mode = Mode.None;
        }

        // the play direction's state rule picks the window — forward play passes s ≤ ν, reverse play s ≥ ν.
        // Returns the target clamped to [0, total]; the caller samples there.
        public long SeekBegin(long from, long to, bool reversePlay)
        {
            to = TimelineTime.Max(0, TimelineTime.Min(to, _r.Total));
            if (to == from)
                _mode = Mode.None;
            else if (!reversePlay)
            {
                if (to > from)
                    SweepForward(from, to);
                else
                    SweepReverse(to + 1, from + 1);
            }
            else
            {
                if (to < from)
                    SweepReverse(to, from);
                else
                    SweepForward(from - 1, to - 1);
                if (_head == Head.Running || _head == Head.Delay)
                    MoveHead(to);
            }
            return to;
        }

        public bool TryNext(out TimelineEvent e)
        {
            if (_mode == Mode.Forward)
            {
                if (_queue.TryNext(out e))
                    return true;
                _mode = Mode.None;
            }
            else if (_mode == Mode.Reverse)
            {
                if (TryNextReverse(out e))
                    return true;
                _mode = Mode.None;
            }
            if (_finishPending)
            {
                _finishPending = false;
                long start = _r.RootDelay;
                e = new TimelineEvent(TimelineEventKind.Finished, true, false, false, -1, -1, 0, start, start);
                return true;
            }
            e = default;
            return false;
        }

        bool TryNextReverse(out TimelineEvent e)
        {
            while (true)
            {
                if (_instant.TryPrevious(out e))
                    return true;
                long t = PreviousInstant(_reverseHi);
                if (t < _reverseLo)
                    return false;
                _reverseHi = t;
                Sample(t - 1, false, _seed);
                _queue.CollectInstant(_seed, t, _instant);
                _instant.BeginReplay();
            }
        }

        // ---- reverse play head ----

        public void BeginReverse()
        {
            // before Begin the head is just past the total — the mirror of the forward Begin window (−1, 0]
            _headElapsed = 0;
            _headPosition = TimelineTime.Add(_r.Total, 1);
            _mode = Mode.None;
            _finishPending = false;
            _head = Head.Delay;
            if (_r.RootDelay <= 0)
                AdvanceReverse(0);
        }

        public void AdvanceReverse(double dtSeconds)
        {
            if (_head != Head.Delay && _head != Head.Running)
                return;
            _headElapsed += dtSeconds;
            long elapsed = TimelineTime.FromSeconds(_headElapsed);
            long root = _r.RootDelay;
            if (elapsed < root)
                return;
            long target = TimelineTime.Max(_r.Total - (elapsed - root), root);
            long hi = _headPosition;
            _head = Head.Running;
            _headPosition = target;
            if (target < hi)
                SweepReverse(target, hi);
            else
                _mode = Mode.None;
            if (target == root)
            {
                _head = Head.Done;
                _finishPending = true;
            }
        }

        // a turned forward play's head: at the position, the instants at it and above count as undone — nothing swept. At or
        // before the start delay the play ends at the next pass
        public void PlaceReverse(long position)
        {
            _mode = Mode.None;
            _finishPending = false;
            if (position <= _r.RootDelay)
            {
                _head = Head.Done;
                _headPosition = _r.RootDelay;
                _finishPending = true;
                return;
            }
            MoveHead(position);
        }

        void MoveHead(long position)
        {
            _head = Head.Running;
            _headPosition = position;
            _headElapsed = TimelineTime.ToSeconds(_r.RootDelay + (_r.Total - position));
        }
    }

    // one instant's forward events. A zero-period finite block records its first iteration once
    // (a segment) and is replayed N times backwards with the occurrence numbers shifted.
    internal sealed class TimelineInstant
    {
        readonly TimelineProgram _p;
        readonly TimelineEvent[] _events;
        int _count;

        readonly int[] _segmentStart;
        readonly int[] _segmentEnd;
        readonly int[] _segmentBlock;
        readonly int[] _segmentIterations;
        int _segments;

        readonly int[] _openBlock;
        readonly int[] _openStart;
        int _open;

        int _cursor;
        readonly int[] _frameSegment;
        readonly long[] _frameIteration;
        int _frames;

        public int Capacity => _events.Length;
        public int MaxCount { get; private set; }

        public TimelineInstant(TimelineProgram program)
        {
            _p = program;
            _events = new TimelineEvent[4 * program.NodeCount + program.BlockCount + 2];
            _segmentStart = new int[program.BlockCount];
            _segmentEnd = new int[program.BlockCount];
            _segmentBlock = new int[program.BlockCount];
            _segmentIterations = new int[program.BlockCount];
            _openBlock = new int[TimelineProgram.MaxLoopDepth + 1];
            _openStart = new int[TimelineProgram.MaxLoopDepth + 1];
            _frameSegment = new int[TimelineProgram.MaxLoopDepth + 1];
            _frameIteration = new long[TimelineProgram.MaxLoopDepth + 1];
        }

        public void Clear()
        {
            _count = 0;
            _segments = 0;
            _open = 0;
            _cursor = 0;
            _frames = 0;
        }

        public void Add(in TimelineEvent e)
        {
            MathAssert.That(_count < _events.Length, "timeline instant overflow");
            // a segment starts at its block's first event — a background block's foreground dependents run before it
            for (int i = 0; i < _open; i++)
            {
                if (_openStart[i] < 0 && Contains(_openBlock[i], e))
                    _openStart[i] = _count;
            }
            _events[_count++] = e;
            if (_count > MaxCount)
                MaxCount = _count;
        }

        public void OpenSegment(int block)
        {
            _openBlock[_open] = block;
            _openStart[_open] = -1;
            _open++;
        }

        public void CloseSegment(int block, int iterations)
        {
            MathAssert.That(_open > 0 && _openBlock[_open - 1] == block, "timeline segment mismatch");
            _open--;
            int start = _openStart[_open];
            if (start < 0)
                return;
            _segmentStart[_segments] = start;
            _segmentEnd[_segments] = _count;
            _segmentBlock[_segments] = block;
            _segmentIterations[_segments] = iterations;
            _segments++;
        }

        public void BeginReplay()
        {
            _cursor = _count;
            _frames = 0;
            _open = 0;
        }

        // the next event backwards, flipped — segments replay k = N − 1 … 0
        public bool TryPrevious(out TimelineEvent e)
        {
            while (true)
            {
                if (_frames > 0 && _cursor == _segmentStart[_frameSegment[_frames - 1]])
                {
                    int top = _frames - 1;
                    if (_frameIteration[top] > 0)
                    {
                        _frameIteration[top]--;
                        _cursor = _segmentEnd[_frameSegment[top]];
                    }
                    else
                    {
                        _frames--;
                    }
                    continue;
                }
                if (_cursor == 0)
                {
                    e = default;
                    return false;
                }
                EnterSegments();
                _cursor--;
                e = Shift(_events[_cursor]).Flipped();
                return true;
            }
        }

        void EnterSegments()
        {
            // outermost first: segments closed later are outer
            for (int s = _segments - 1; s >= 0; s--)
            {
                if (_segmentEnd[s] != _cursor || IsActive(s))
                    continue;
                _frameSegment[_frames] = s;
                _frameIteration[_frames] = _segmentIterations[s] - 1;
                _frames++;
            }
        }

        bool IsActive(int segment)
        {
            for (int f = 0; f < _frames; f++)
            {
                if (_frameSegment[f] == segment)
                    return true;
            }
            return false;
        }

        TimelineEvent Shift(in TimelineEvent e)
        {
            long offset = 0;
            for (int f = 0; f < _frames; f++)
            {
                long k = _frameIteration[f];
                if (k == 0)
                    continue;
                int block = _segmentBlock[_frameSegment[f]];
                if (e.Kind == TimelineEventKind.LoopIteration)
                {
                    if (e.Block == block)
                        offset += k;
                }
                else
                {
                    offset += k * Stride(block, _p.NodeOfItem(e.Item));
                }
            }
            return offset == 0 ? e : e.WithOccurrence(e.Occurrence + offset);
        }

        // how much an item's occurrence grows per iteration of an enclosing block
        long Stride(int block, int node)
        {
            long stride = 1;
            int scope = _p.NodeScope(node);
            int target = TimelineProgram.BlockScope(block);
            while (scope != target)
            {
                int inner = scope - 1;
                int n = _p.BlockIterations(inner);
                if (n > 0)
                    stride *= n;
                scope = _p.NodeScope(_p.BlockNode(inner));
            }
            return stride;
        }

        bool Contains(int block, in TimelineEvent e)
        {
            int scope;
            if (e.Kind == TimelineEventKind.LoopIteration)
            {
                if (e.Block == block)
                    return true;
                scope = _p.NodeScope(_p.BlockNode(e.Block));
            }
            else if (e.Item >= 0)
            {
                scope = _p.NodeScope(_p.NodeOfItem(e.Item));
            }
            else
            {
                return false;
            }
            int target = TimelineProgram.BlockScope(block);
            while (scope != TimelineProgram.RootScope)
            {
                if (scope == target)
                    return true;
                scope = _p.NodeScope(_p.BlockNode(scope - 1));
            }
            return false;
        }
    }
}
