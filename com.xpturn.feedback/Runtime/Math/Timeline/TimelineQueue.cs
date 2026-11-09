namespace xpTURN.Feedback
{
    // the event simulation shared by the sweep and the cursor — a fixed-size queue, the causal rules (every schedule is
    // the cause's moment + a delay) and seeding from a sample.
    // Nominal mode (the sweep) keeps every node on one axis and orders by (time, key, insertion). Clocked mode (the
    // cursor) keeps each entry on its node's clock and orders a tick chunk by (effective f, key, insertion).
    internal sealed class TimelineQueue
    {
        enum EntryKind : byte
        {
            RootStart,
            NodeStart,
            HoldElapsed,
            NodeEnd,
            IterationStart,
            IterationEnd,
            RootEnd,
            ForcedEnd,
            Finished,
        }

        enum Phase : byte
        {
            Waiting,
            Scheduled,
            Running,
            Done,
        }

        public const int Clocks = 2;
        const int RootStartKey = int.MinValue + 1;
        const int RootEndKey = int.MinValue;
        const int FinishedKey = int.MaxValue;

        readonly TimelineProgram _p;
        readonly ResolvedTimeline _r;
        readonly TimelineSampler _sampler;
        readonly bool _clocked;

        readonly long[] _entryTime;
        readonly int[] _entryKey;
        readonly long[] _entrySeq;
        readonly EntryKind[] _entryKind;
        readonly int[] _entryTarget;
        readonly bool[] _entryBackground;
        readonly byte[] _entryClock;
        readonly long[] _entryFloorNum;
        readonly long[] _entryFloorDen;
        readonly bool[] _entryDeferred;
        int _count;
        long _seq;

        readonly Phase[] _phase;
        readonly int[] _waitLeft;
        readonly long[] _occurrence;
        readonly long[] _startAt;       // clock time of the current occurrence's start
        readonly long[] _startNu;       // its nominal start
        readonly bool[] _signaled;
        readonly long[] _scopeOccurrence;
        readonly long[] _scopeStart;    // nominal start of the scope's current iteration
        readonly int[] _scopeUnfinished;

        // the most recently reached occurrence — changed by events only
        readonly TimelineStage[] _seenStage;
        readonly long[] _seenOccurrence;

        // clocks resolved per play (nominal mode leaves everything on clock 0)
        readonly byte[] _nodeClock;
        readonly byte[] _holdClock;
        readonly byte[] _gapClock;
        byte _rootClock;
        readonly long[] _tauOld = new long[Clocks];
        readonly long[] _tauNew = new long[Clocks];
        readonly long[] _dt = new long[Clocks];
        readonly long[] _offset = new long[Clocks];
        readonly int[] _live = new int[Clocks];
        long _prevNum;
        long _prevDen = 1;
        bool _offNominal;
        long _lastNu;

        // the entry being processed — the cause of everything it schedules
        bool _hasCause;
        int _causeClock;
        long _causeTime;
        long _causeNum;
        long _causeDen = 1;
        long _causeNu;

        long _limit;
        bool _finished;
        bool _hasFollowUp;
        TimelineEvent _followUp;

        TimelineInstant _recorder;
        int _pendingClose = -1;
        long _seedPosition;

        public int Capacity => _entryTime.Length;
        public int MaxCount { get; private set; }
        public bool IsFinished => _finished;
        public bool IsOffNominal => _offNominal;
        public int RootClock => _rootClock;

        public TimelineQueue(TimelineProgram program, ResolvedTimeline resolved, TimelineSampler sampler, bool clocked)
        {
            _p = program;
            _r = resolved;
            _sampler = sampler;
            _clocked = clocked;
            int capacity = program.NodeCount + 2 * program.BlockCount + 2;
            _entryTime = new long[capacity];
            _entryKey = new int[capacity];
            _entrySeq = new long[capacity];
            _entryKind = new EntryKind[capacity];
            _entryTarget = new int[capacity];
            _entryBackground = new bool[capacity];
            _entryClock = new byte[capacity];
            _entryFloorNum = new long[capacity];
            _entryFloorDen = new long[capacity];
            _entryDeferred = new bool[capacity];
            int nodes = program.NodeCount;
            _phase = new Phase[nodes];
            _waitLeft = new int[nodes];
            _occurrence = new long[nodes];
            _startAt = new long[nodes];
            _startNu = new long[nodes];
            _signaled = new bool[nodes];
            _seenStage = new TimelineStage[nodes];
            _seenOccurrence = new long[nodes];
            _nodeClock = new byte[nodes];
            _holdClock = new byte[nodes];
            _gapClock = new byte[program.BlockCount];
            _scopeOccurrence = new long[program.ScopeCount];
            _scopeStart = new long[program.ScopeCount];
            _scopeUnfinished = new int[program.ScopeCount];
        }

        public void SetLimit(long limit) => _limit = limit;

        // ---- clocked mode ----

        // 'follow the player' becomes the base clock; the root (start delay) counts on ν's clock
        public void ResolveClocks(TimelineClock baseClock)
        {
            MathAssert.That(baseClock != TimelineClock.Player, "the base clock is Scaled or Unscaled");
            for (int node = 0; node < _nodeClock.Length; node++)
            {
                _nodeClock[node] = Real(_p.NodeClock(node), baseClock);
                _holdClock[node] = Real(_p.NodeHoldClock(node), baseClock);
            }
            for (int block = 0; block < _gapClock.Length; block++)
                _gapClock[block] = Real(_p.BlockClock(block), baseClock);

            _rootClock = (byte)_p.PositionClock(baseClock);
        }

        static byte Real(TimelineClock clock, TimelineClock baseClock)
            => (byte)(clock == TimelineClock.Player ? baseClock : clock);

        // Begin: everything pending, only the root start on ν's clock
        public void Reset()
        {
            ClearState();
            for (int node = 0; node < _phase.Length; node++)
            {
                _seenStage[node] = TimelineStage.Pending;
                _seenOccurrence[node] = -1;
            }
            Push(EntryKind.RootStart, -1, _rootClock, _r.RootDelay, RootStartKey, false, 0, 1, false);
        }

        // one chunk of a tick: every clock moves from tauOld to tauNew; inherited f restarts at 0
        public void BeginChunk(long[] tauOld, long[] tauNew)
        {
            for (int c = 0; c < Clocks; c++)
            {
                _tauOld[c] = tauOld[c];
                _tauNew[c] = tauNew[c];
                _dt[c] = tauNew[c] - tauOld[c];
            }
            for (int i = 0; i < _count; i++)
            {
                _entryFloorNum[i] = 0;
                _entryFloorDen[i] = 1;
            }
            _prevNum = 0;
            _prevDen = 1;
            _hasCause = false;
        }

        // the interval from the last event to the end of the chunk
        public void EndChunk() => CheckInterval(1, 1);

        // a zero-period infinite block restarts once per tick, and not while its clock stands still
        public void ReleaseDeferred(long[] tickOld, long[] tickNew)
        {
            for (int i = 0; i < _count; i++)
            {
                if (!_entryDeferred[i])
                    continue;
                int c = _entryClock[i];
                if (tickNew[c] <= tickOld[c])
                    continue;
                _entryDeferred[i] = false;
                _entryTime[i] = tickOld[c];
            }
        }

        // the farthest scheduled entry from now — the step of an event-driven completion
        public long MaxDistance(long[] now)
        {
            long max = 0;
            for (int i = 0; i < _count; i++)
            {
                long t = _entryTime[i];
                if (t == TimelineTime.Infinite || _entryDeferred[i])
                    continue;
                max = TimelineTime.Max(max, t - now[_entryClock[i]]);
            }
            return max;
        }

        // ν = τ + offset of a clock that has live entries
        public long Position(long[] now)
        {
            if (_live[_rootClock] > 0)
                return now[_rootClock] + _offset[_rootClock];
            for (int c = 0; c < Clocks; c++)
            {
                if (_live[c] > 0)
                    return now[c] + _offset[c];
            }
            return _lastNu;
        }

        // ends every running signal wait now — in a tick at the current event's moment, between ticks at the clock's τ
        // (the next tick's f = 0). Returns the number of waits ended.
        public int Signal(bool inTick, long[] now)
        {
            int count = 0;
            for (int node = 0; node < _phase.Length; node++)
            {
                int item = _p.NodeItem(node);
                if (item < 0 || _p.ItemKind(item) != TimelineItemKind.SignalWait || _phase[node] != Phase.Running || _signaled[node])
                    continue;
                int index = FindEntry(EntryKind.NodeEnd, node);
                if (index < 0)
                    continue;
                int clock = _nodeClock[node];
                long end = _entryTime[index];
                RemoveAt(index);
                if (!inTick || !_hasCause)
                {
                    long at = inTick ? _tauOld[clock] : now[clock];
                    _causeClock = clock;
                    _causeTime = at;
                    _causeNum = 0;
                    _causeDen = 1;
                    _causeNu = at + _offset[clock];
                    _hasCause = inTick;
                }
                Place(EntryKind.NodeEnd, node, clock, 0, _p.NodeEndKey(node), _p.NodeInBackground(node));
                if (end != TimelineTime.Infinite && _entryTime[_count - 1] < end)
                    _offNominal = true;
                _signaled[node] = true;
                count++;
            }
            return count;
        }

        // Signal's path for one running node of any kind — an open effect its binding ends. Once a run
        public int EndNode(int node, bool inTick, long[] now)
        {
            if (_phase[node] != Phase.Running || _signaled[node])
                return 0;
            int index = FindEntry(EntryKind.NodeEnd, node);
            if (index < 0)
                return 0;
            int clock = _nodeClock[node];
            long end = _entryTime[index];
            RemoveAt(index);
            if (!inTick || !_hasCause)
            {
                long at = inTick ? _tauOld[clock] : now[clock];
                _causeClock = clock;
                _causeTime = at;
                _causeNum = 0;
                _causeDen = 1;
                _causeNu = at + _offset[clock];
                _hasCause = inTick;
            }
            Place(EntryKind.NodeEnd, node, clock, 0, _p.NodeEndKey(node), _p.NodeInBackground(node));
            if (end != TimelineTime.Infinite && _entryTime[_count - 1] < end)
                _offNominal = true;
            _signaled[node] = true;
            return 1;
        }

        // the waits Signal would end now — untimed: those of them whose end is at Infinite (no timeout)
        public int OpenSignalWaits(out int untimed)
        {
            int count = 0;
            untimed = 0;
            for (int node = 0; node < _phase.Length; node++)
            {
                int item = _p.NodeItem(node);
                if (item < 0 || _p.ItemKind(item) != TimelineItemKind.SignalWait || _phase[node] != Phase.Running || _signaled[node])
                    continue;
                int index = FindEntry(EntryKind.NodeEnd, node);
                if (index < 0)
                    continue;
                count++;
                if (_entryTime[index] == TimelineTime.Infinite)
                    untimed++;
            }
            return count;
        }

        public TimelineStage SeenStage(int node) => _seenStage[node];
        public long SeenOccurrence(int node) => _seenOccurrence[node];
        public long NodeStartAt(int node) => _startAt[node];
        public int NodeClock(int node) => _nodeClock[node];

        // ---- seeding ----

        // State after every event at or before sample.Position (forward sample). Clocked mode: every clock is at ν.
        public void Seed(TimelineSample sample)
        {
            long lo = sample.Position;
            ClearState();
            _seedPosition = lo;
            _lastNu = lo;
            for (int node = 0; node < _phase.Length; node++)
            {
                var stage = sample.NodeStage(node);
                _seenStage[node] = stage;
                _seenOccurrence[node] = sample.NodeOccurrence(node);
                if (stage == TimelineStage.Pending)
                    continue;
                _startAt[node] = sample.NodeStart(node);
                _startNu[node] = _startAt[node];
            }

            long root = _r.RootDelay;
            if (lo < root)
            {
                Push(EntryKind.RootStart, -1, _rootClock, root, RootStartKey, false, 0, 1, false);
                return;
            }
            if (lo >= _r.Total)
            {
                for (int node = 0; node < _phase.Length; node++)
                    _phase[node] = Phase.Done;
                _finished = true;
                return;
            }
            _scopeOccurrence[TimelineProgram.RootScope] = 0;
            _scopeStart[TimelineProgram.RootScope] = root;
            SeedScope(TimelineProgram.RootScope, sample);
        }

        void ClearState()
        {
            _count = 0;
            _seq = 0;
            _finished = false;
            _hasFollowUp = false;
            _pendingClose = -1;
            _hasCause = false;
            _causeClock = 0;
            _causeNum = 0;
            _causeDen = 1;
            _offNominal = false;
            _lastNu = 0;
            _prevNum = 0;
            _prevDen = 1;
            for (int c = 0; c < Clocks; c++)
            {
                _offset[c] = 0;
                _live[c] = 0;
            }
            for (int node = 0; node < _phase.Length; node++)
            {
                _phase[node] = Phase.Waiting;
                _waitLeft[node] = 0;
                _occurrence[node] = -1;
                _startAt[node] = 0;
                _startNu[node] = 0;
                _signaled[node] = false;
            }
        }

        void SeedScope(int scope, TimelineSample sample)
        {
            long q = _scopeOccurrence[scope];
            int unfinished = 0;
            foreach (int node in _p.ScopeMembers(scope))
            {
                var stage = sample.NodeStage(node);
                _occurrence[node] = q;
                if (stage != TimelineStage.Pending && sample.NodeOccurrence(node) == q)
                {
                    if (stage == TimelineStage.Running)
                    {
                        _phase[node] = Phase.Running;
                        int block = _p.NodeBlock(node);
                        if (block < 0)
                            Push(EntryKind.NodeEnd, node, _nodeClock[node], TimelineTime.Add(_startAt[node], _r.ItemDuration(_p.NodeItem(node))), _p.NodeEndKey(node), _p.NodeInBackground(node), 0, 1, false);
                        else
                            SeedBlock(block, sample);
                    }
                    else
                    {
                        _phase[node] = Phase.Done;
                    }
                }
                else
                {
                    int left = 1;
                    bool satisfied;
                    int target = _p.NodeRefTarget(node);
                    switch (_p.NodeRefKind(node))
                    {
                        case TimelineRefKind.StartOf:
                            satisfied = _phase[target] == Phase.Running || _phase[target] == Phase.Done;
                            break;
                        case TimelineRefKind.EndOf:
                            satisfied = _phase[target] == Phase.Done;
                            break;
                        case TimelineRefKind.AllPrior:
                            left = 0;
                            foreach (int prior in _p.ScopeMembers(scope))
                            {
                                if (prior == node)
                                    break;
                                if (!_p.NodeBackground(prior) && _phase[prior] != Phase.Done)
                                    left++;
                            }
                            satisfied = left == 0;
                            break;
                        default:
                            satisfied = true;
                            break;
                    }
                    if (satisfied)
                    {
                        SeedSchedule(node);
                    }
                    else
                    {
                        _phase[node] = Phase.Waiting;
                        _waitLeft[node] = left;
                    }
                }

                if (_phase[node] != Phase.Done && !(scope == TimelineProgram.RootScope && _p.NodeBackground(node)))
                    unfinished++;
            }
            _scopeUnfinished[scope] = unfinished;
        }

        void SeedBlock(int block, TimelineSample sample)
        {
            int inner = TimelineProgram.BlockScope(block);
            long q = sample.BlockScopeOccurrence(block);
            _scopeOccurrence[inner] = q;
            _scopeStart[inner] = _sampler.ScopeIterationStart(inner, q);
            SeedScope(inner, sample);
            if (_scopeUnfinished[inner] == 0)
            {
                // in the gap after iteration q — its end was processed already
                int blockNode = _p.BlockNode(block);
                Push(EntryKind.IterationStart, block, _gapClock[block], _sampler.ScopeIterationStart(inner, q + 1), _p.NodeEndKey(blockNode), _p.NodeInBackground(blockNode), 0, 1, false);
            }
        }

        // a pending node whose reference has passed — its start from the closed form; a Hold on another clock that has
        // not elapsed at the seed position is seeded as the Hold stage
        void SeedSchedule(int node)
        {
            int scope = _p.NodeScope(node);
            _phase[node] = Phase.Scheduled;
            _occurrence[node] = _scopeOccurrence[scope];
            long start = TimelineTime.Add(_scopeStart[scope], _r.NodeLocalStart(node));
            int key = _p.NodeStartKey(node);
            bool background = _p.NodeInBackground(node);
            if (_r.NodeHoldDelay(node) > 0 && _holdClock[node] != _nodeClock[node])
            {
                long holdEnd = start == TimelineTime.Infinite ? start : start - OwnDelay(node);
                if (holdEnd > _seedPosition)
                {
                    Push(EntryKind.HoldElapsed, node, _holdClock[node], holdEnd, key, background, 0, 1, false);
                    return;
                }
            }
            Push(EntryKind.NodeStart, node, _nodeClock[node], start, key, background, 0, 1, false);
        }

        // ---- the simulation ----

        public bool TryNext(out TimelineEvent e)
        {
            while (true)
            {
                int result = Step(out e);
                if (result == 2)
                    return true;
                if (result == 0)
                    return false;
            }
        }

        // every event of the instant `time`, forward order, zero-period blocks recorded once (× N).
        public void CollectInstant(TimelineSample before, long time, TimelineInstant into)
        {
            Seed(before);
            _limit = time;
            into.Clear();
            _recorder = into;
            while (true)
            {
                int result = Step(out var e);
                if (result == 0)
                    break;
                if (result == 2 && e.Kind != TimelineEventKind.Finished && e.Kind != TimelineEventKind.SignalWaitBegan)
                    into.Add(e);
                if (_pendingClose >= 0)
                {
                    into.CloseSegment(_pendingClose, _p.BlockIterations(_pendingClose));
                    _pendingClose = -1;
                }
            }
            _recorder = null;
        }

        // 0 = nothing within the limit, 1 = processed silently, 2 = produced e
        int Step(out TimelineEvent e)
        {
            e = default;
            if (_hasFollowUp)
            {
                e = _followUp;
                _hasFollowUp = false;
                return 2;
            }
            int index;
            if (_clocked)
            {
                index = FindDue(out long num, out long den);
                if (index < 0)
                    return 0;
                CheckInterval(num, den);
                _causeNum = num;
                _causeDen = den;
            }
            else
            {
                index = FindMin();
                if (index < 0 || _entryTime[index] > _limit)
                    return 0;
            }

            var kind = _entryKind[index];
            int target = _entryTarget[index];
            long time = _entryTime[index];
            int clock = _entryClock[index];
            _hasCause = true;
            _causeClock = clock;
            _causeTime = time;
            _causeNu = time + _offset[clock];
            _lastNu = _causeNu;
            RemoveAt(index);

            switch (kind)
            {
                case EntryKind.RootStart:
                    StartRoot();
                    return 1;
                case EntryKind.NodeStart:
                    return StartNode(target, time, out e);
                case EntryKind.HoldElapsed:
                    Place(EntryKind.NodeStart, target, _nodeClock[target], OwnDelay(target), _p.NodeStartKey(target), _p.NodeInBackground(target));
                    return 1;
                case EntryKind.NodeEnd:
                    return EndNode(target, time, out e);
                case EntryKind.IterationStart:
                {
                    int inner = TimelineProgram.BlockScope(target);
                    StartIteration(target, _scopeOccurrence[inner] + 1);
                    return 1;
                }
                case EntryKind.IterationEnd:
                    return EndIteration(target, out e);
                case EntryKind.RootEnd:
                    EndRoot();
                    return 1;
                case EntryKind.ForcedEnd:
                {
                    _phase[target] = Phase.Done;
                    _seenStage[target] = TimelineStage.Done;
                    int item = _p.NodeItem(target);
                    bool truncated = TimelineTime.Add(_startAt[target], _r.ItemDuration(item)) > time;
                    e = new TimelineEvent(TimelineEventKind.Ended, false, _r.ItemSkipped(item), truncated, item, -1, _occurrence[target], NominalAt(target, time), _startNu[target]);
                    return 2;
                }
                default:
                    _finished = true;
                    e = new TimelineEvent(TimelineEventKind.Finished, false, false, false, -1, -1, 0, _causeNu, _causeNu);
                    return 2;
            }
        }

        void StartRoot()
        {
            int scope = TimelineProgram.RootScope;
            _scopeOccurrence[scope] = 0;
            _scopeStart[scope] = _causeNu;
            int foreground = 0;
            foreach (int node in _p.ScopeMembers(scope))
            {
                _phase[node] = Phase.Waiting;
                _waitLeft[node] = _p.NodeWaitCount(node);
                _occurrence[node] = 0;
                if (!_p.NodeBackground(node))
                    foreground++;
            }
            _scopeUnfinished[scope] = foreground;
            foreach (int dependent in _p.Dependents(_p.ScopeStartEvent(scope)))
                ScheduleStart(dependent);
            if (foreground == 0)
                PlaceMoment(EntryKind.RootEnd, -1, RootEndKey, false);
        }

        void StartIteration(int block, long q)
        {
            int inner = TimelineProgram.BlockScope(block);
            _scopeOccurrence[inner] = q;
            _scopeStart[inner] = _causeNu;
            int members = 0;
            foreach (int node in _p.ScopeMembers(inner))
            {
                _phase[node] = Phase.Waiting;
                _waitLeft[node] = _p.NodeWaitCount(node);
                _occurrence[node] = q;
                members++;
            }
            _scopeUnfinished[inner] = members;
            int n = _p.BlockIterations(block);
            if (_recorder != null && n > 0 && _r.BlockPeriod(block) == 0 && q % n == 0)
                _recorder.OpenSegment(block);
            foreach (int dependent in _p.Dependents(_p.ScopeStartEvent(inner)))
                ScheduleStart(dependent);
            if (members == 0)
            {
                int blockNode = _p.BlockNode(block);
                PlaceMoment(EntryKind.IterationEnd, block, _p.NodeEndKey(blockNode), _p.NodeInBackground(blockNode));
            }
        }

        int StartNode(int node, long time, out TimelineEvent e)
        {
            e = default;
            _phase[node] = Phase.Running;
            _startAt[node] = time;
            _startNu[node] = _causeNu;
            _signaled[node] = false;
            _seenStage[node] = TimelineStage.Running;
            _seenOccurrence[node] = _occurrence[node];
            foreach (int dependent in _p.Dependents(TimelineProgram.StartEvent(node)))
                ScheduleStart(dependent);

            int block = _p.NodeBlock(node);
            if (block >= 0)
            {
                int n = _p.BlockIterations(block);
                StartIteration(block, n > 0 ? TimelineTime.Multiply(_occurrence[node], n) : 0);
                return 1;
            }

            int item = _p.NodeItem(node);
            Place(EntryKind.NodeEnd, node, _nodeClock[node], _r.ItemDuration(item), _p.NodeEndKey(node), _p.NodeInBackground(node));
            bool skipped = _r.ItemSkipped(item);
            var kind = _p.ItemInstant(item) ? TimelineEventKind.Fired : TimelineEventKind.Started;
            long nu = _startNu[node];
            e = new TimelineEvent(kind, false, skipped, false, item, -1, _occurrence[node], nu, nu);
            if (_p.ItemKind(item) == TimelineItemKind.SignalWait)
            {
                _followUp = new TimelineEvent(TimelineEventKind.SignalWaitBegan, false, skipped, false, item, -1, _occurrence[node], nu, nu);
                _hasFollowUp = true;
            }
            return 2;
        }

        int EndNode(int node, long time, out TimelineEvent e)
        {
            e = default;
            _phase[node] = Phase.Done;
            _seenStage[node] = TimelineStage.Done;
            OnFinished(node);
            if (_p.NodeBlock(node) >= 0)
                return 1;
            int item = _p.NodeItem(node);
            if (_p.ItemInstant(item))
                return 1;
            e = new TimelineEvent(TimelineEventKind.Ended, false, _r.ItemSkipped(item), false, item, -1, _occurrence[node], NominalAt(node, time), _startNu[node]);
            return 2;
        }

        void OnFinished(int node)
        {
            foreach (int dependent in _p.Dependents(TimelineProgram.EndEvent(node)))
            {
                if (_p.NodeRefKind(dependent) == TimelineRefKind.AllPrior)
                {
                    if (--_waitLeft[dependent] == 0)
                        ScheduleStart(dependent);
                }
                else
                {
                    ScheduleStart(dependent);
                }
            }

            int scope = _p.NodeScope(node);
            if (scope == TimelineProgram.RootScope)
            {
                if (!_p.NodeBackground(node) && --_scopeUnfinished[scope] == 0)
                    PlaceMoment(EntryKind.RootEnd, -1, RootEndKey, false);
            }
            else if (--_scopeUnfinished[scope] == 0)
            {
                int block = scope - 1;
                int blockNode = _p.BlockNode(block);
                PlaceMoment(EntryKind.IterationEnd, block, _p.NodeEndKey(blockNode), _p.NodeInBackground(blockNode));
            }
        }

        int EndIteration(int block, out TimelineEvent e)
        {
            e = default;
            int blockNode = _p.BlockNode(block);
            int inner = TimelineProgram.BlockScope(block);
            int n = _p.BlockIterations(block);
            long q = _scopeOccurrence[inner];
            long k = n > 0 ? q % n : q;
            bool emit = !_p.BlockIsSynthetic(block);
            if (emit)
                e = new TimelineEvent(TimelineEventKind.LoopIteration, false, false, false, -1, block, k, _causeNu, _scopeStart[inner]);

            if (_recorder != null && n > 0 && _r.BlockPeriod(block) == 0 && k == 0)
            {
                // run-length: iteration 0 is recorded once, the rest of the run is identical and at the same instant
                _pendingClose = block;
                q += n - 1;
                k = n - 1;
                _scopeOccurrence[inner] = q;
            }

            bool last = n > 0 && k == n - 1;
            bool background = _p.NodeInBackground(blockNode);
            if (last)
            {
                PlaceMoment(EntryKind.NodeEnd, blockNode, _p.NodeEndKey(blockNode), background);
            }
            else
            {
                // a zero-period infinite block restarts on the next tick
                bool deferred = _clocked && n <= 0 && _r.BlockPeriod(block) == 0;
                Place(EntryKind.IterationStart, block, _gapClock[block], _r.BlockGap(block), _p.NodeEndKey(blockNode), background, deferred);
            }
            return emit ? 2 : 1;
        }

        void EndRoot()
        {
            // the background ends with the foreground; what has not started never runs
            int write = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_entryBackground[i])
                {
                    _live[_entryClock[i]]--;
                    continue;
                }
                CopyEntry(i, write++);
            }
            _count = write;
            for (int node = 0; node < _phase.Length; node++)
            {
                if (!_p.NodeInBackground(node) || _phase[node] != Phase.Running)
                    continue;
                if (_p.IsBlockNode(node))
                {
                    _phase[node] = Phase.Done;
                    _seenStage[node] = TimelineStage.Done;
                }
                else
                {
                    Place(EntryKind.ForcedEnd, node, _nodeClock[node], 0, _p.NodeEndKey(node), true);
                }
            }
            PlaceMoment(EntryKind.Finished, -1, FinishedKey, false);
        }

        // a Hold on another clock is its own stage before the node's own delay
        void ScheduleStart(int node)
        {
            int scope = _p.NodeScope(node);
            _phase[node] = Phase.Scheduled;
            _occurrence[node] = _scopeOccurrence[scope];
            int key = _p.NodeStartKey(node);
            bool background = _p.NodeInBackground(node);
            long hold = _r.NodeHoldDelay(node);
            if (hold > 0 && _holdClock[node] != _nodeClock[node])
                Place(EntryKind.HoldElapsed, node, _holdClock[node], hold, key, background);
            else
                Place(EntryKind.NodeStart, node, _nodeClock[node], _r.NodeDelay(node), key, background);
        }

        long OwnDelay(int node)
        {
            long delay = _r.NodeDelay(node);
            return delay == TimelineTime.Infinite ? delay : delay - _r.NodeHoldDelay(node);
        }

        // occurrence events: the occurrence's nominal start + the elapsed time on its own clock
        long NominalAt(int node, long time) => _startNu[node] + (time - _startAt[node]);

        // the cause's moment on `clock` (exact on the same clock, floored across clocks) + delay.
        // The first live entry of a clock fixes its offset from the cause's nominal moment.
        void Place(EntryKind kind, int target, int clock, long delay, int key, bool background, bool deferred = false)
        {
            long moment = clock == _causeClock ? _causeTime : _tauOld[clock] + _causeNum * _dt[clock] / _causeDen;
            if (_live[clock] == 0)
                _offset[clock] = _causeNu - moment;
            Push(kind, target, clock, TimelineTime.Add(moment, delay), key, background, _causeNum, _causeDen, deferred);
        }

        void PlaceMoment(EntryKind kind, int target, int key, bool background)
            => Place(kind, target, _causeClock, 0, key, background);

        // two clocks live through a positive interval at different rates — the play leaves the nominal axis
        void CheckInterval(long num, long den)
        {
            if (!_offNominal && _dt[0] != _dt[1] && _live[0] > 0 && _live[1] > 0 && CompareF(num, den, _prevNum, _prevDen) > 0)
                _offNominal = true;
            _prevNum = num;
            _prevDen = den;
        }

        void Push(EntryKind kind, int target, int clock, long time, int key, bool background, long floorNum, long floorDen, bool deferred)
        {
            MathAssert.That(_count < _entryTime.Length, "timeline queue overflow");
            int i = _count++;
            _entryKind[i] = kind;
            _entryTarget[i] = target;
            _entryTime[i] = time;
            _entryKey[i] = key;
            _entrySeq[i] = _seq++;
            _entryBackground[i] = background;
            _entryClock[i] = (byte)clock;
            _entryFloorNum[i] = floorNum;
            _entryFloorDen[i] = floorDen;
            _entryDeferred[i] = deferred;
            _live[clock]++;
            if (_count > MaxCount)
                MaxCount = _count;
        }

        int FindEntry(EntryKind kind, int target)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_entryKind[i] == kind && _entryTarget[i] == target)
                    return i;
            }
            return -1;
        }

        int FindMin()
        {
            int best = -1;
            for (int i = 0; i < _count; i++)
            {
                if (best < 0)
                {
                    best = i;
                    continue;
                }
                if (_entryTime[i] != _entryTime[best])
                {
                    if (_entryTime[i] < _entryTime[best])
                        best = i;
                    continue;
                }
                if (_entryKey[i] != _entryKey[best])
                {
                    if (_entryKey[i] < _entryKey[best])
                        best = i;
                    continue;
                }
                if (_entrySeq[i] < _entrySeq[best])
                    best = i;
            }
            return best;
        }

        // the due entry with the smallest effective f = max(own f, inherited f), then key, then insertion
        int FindDue(out long bestNum, out long bestDen)
        {
            int best = -1;
            bestNum = 0;
            bestDen = 1;
            for (int i = 0; i < _count; i++)
            {
                if (_entryDeferred[i])
                    continue;
                int c = _entryClock[i];
                long t = _entryTime[i];
                if (t > _tauNew[c])
                    continue;
                long num = 0;
                long den = 1;
                if (_dt[c] > 0)
                {
                    num = t > _tauOld[c] ? t - _tauOld[c] : 0;
                    den = _dt[c];
                }
                if (CompareF(_entryFloorNum[i], _entryFloorDen[i], num, den) > 0)
                {
                    num = _entryFloorNum[i];
                    den = _entryFloorDen[i];
                }
                if (best >= 0)
                {
                    int order = CompareF(num, den, bestNum, bestDen);
                    if (order > 0)
                        continue;
                    if (order == 0)
                    {
                        if (_entryKey[i] > _entryKey[best])
                            continue;
                        if (_entryKey[i] == _entryKey[best] && _entrySeq[i] > _entrySeq[best])
                            continue;
                    }
                }
                best = i;
                bestNum = num;
                bestDen = den;
            }
            return best;
        }

        // fractions of a chunk: numerators ≤ denominators ≤ 2^31 − 1, so the cross products stay below 2^62
        static int CompareF(long aNum, long aDen, long bNum, long bDen)
        {
            long left = aNum * bDen;
            long right = bNum * aDen;
            return left < right ? -1 : left > right ? 1 : 0;
        }

        void RemoveAt(int index)
        {
            _live[_entryClock[index]]--;
            int last = --_count;
            if (index != last)
                CopyEntry(last, index);
        }

        void CopyEntry(int from, int to)
        {
            _entryKind[to] = _entryKind[from];
            _entryTarget[to] = _entryTarget[from];
            _entryTime[to] = _entryTime[from];
            _entryKey[to] = _entryKey[from];
            _entrySeq[to] = _entrySeq[from];
            _entryBackground[to] = _entryBackground[from];
            _entryClock[to] = _entryClock[from];
            _entryFloorNum[to] = _entryFloorNum[from];
            _entryFloorDen[to] = _entryFloorDen[from];
            _entryDeferred[to] = _entryDeferred[from];
        }
    }
}
