namespace xpTURN.Feedback
{
    // the forward play head — event-driven over the two clocks, one event per TryNext.
    // Allocates at construction only; Begin refills every piece of state (a play record reuses its cursor).
    internal sealed class TimelineCursor
    {
        // a chunk of a tick moves each clock at most this far, so the cross products of two fractions stay below 2^62
        const long MaxChunk = int.MaxValue;

        enum Mode : byte
        {
            Idle,
            Tick,
            Complete,
        }

        readonly ResolvedTimeline _r;
        readonly TimelineProgram _p;
        readonly TimelineSampler _sampler;
        readonly TimelineQueue _queue;
        readonly TimelineSample _sample;

        readonly long[] _base = new long[TimelineQueue.Clocks];
        readonly double[] _acc = new double[TimelineQueue.Clocks];
        readonly long[] _tau = new long[TimelineQueue.Clocks];
        readonly long[] _tickOld = new long[TimelineQueue.Clocks];
        readonly long[] _tickNew = new long[TimelineQueue.Clocks];
        readonly long[] _chunkOld = new long[TimelineQueue.Clocks];
        readonly long[] _chunkNew = new long[TimelineQueue.Clocks];
        long _chunks;
        long _chunk;
        Mode _mode;
        bool _drained;
        bool _aborted;

        public TimelineCursor(TimelineProgram program, ResolvedTimeline resolved, TimelineSampler sampler)
        {
            _p = program;
            _r = resolved;
            _sampler = sampler;
            _queue = new TimelineQueue(program, resolved, sampler, true);
            _sample = new TimelineSample(program);
        }

        internal TimelineQueue Queue => _queue;

        public long Position => _queue.Position(_tau);
        public bool IsFinished => _queue.IsFinished;
        public bool IsOffNominal => _queue.IsOffNominal;

        // the clock ν counts on — the start delay and the reverse play head
        public TimelineClock PositionClock => (TimelineClock)_queue.RootClock;

        // the Begin window (−1, 0] on every clock — then TryNext · EndAdvance
        public void Begin(TimelineClock baseClock)
        {
            _queue.ResolveClocks(baseClock);
            _queue.Reset();
            for (int c = 0; c < TimelineQueue.Clocks; c++)
            {
                _base[c] = 0;
                _acc[c] = 0;
                _tau[c] = 0;
                _tickOld[c] = -1;
                _tickNew[c] = 0;
            }
            _aborted = false;
            StartTick(Mode.Tick);
        }

        public void BeginAdvance(double dtScaled, double dtUnscaled)
        {
            MathAssert.That(dtScaled >= 0 && dtUnscaled >= 0, "timeline dt must be a non-negative number");
            _acc[(int)TimelineClock.Scaled] += dtScaled;
            _acc[(int)TimelineClock.Unscaled] += dtUnscaled;
            for (int c = 0; c < TimelineQueue.Clocks; c++)
            {
                _tickOld[c] = _tau[c];
                _tickNew[c] = TimelineTime.Add(_base[c], TimelineTime.FromSeconds(_acc[c]));
            }
            _queue.ReleaseDeferred(_tickOld, _tickNew);
            StartTick(Mode.Tick);
        }

        // the same dt on every clock until Finished — then TryNext · EndAdvance. Seekable plays only.
        public void BeginComplete()
        {
            MathAssert.That(_r.IsSeekable, "event-driven completion needs a seekable play");
            NextRound();
        }

        public bool TryNext(out TimelineEvent e)
        {
            if (_mode == Mode.Idle || _drained || _aborted)
            {
                e = default;
                return false;
            }
            while (true)
            {
                if (_queue.TryNext(out e))
                    return true;
                _queue.EndChunk();
                if (_chunk < _chunks)
                {
                    NextChunk();
                    continue;
                }
                if (_mode == Mode.Complete && !_queue.IsFinished)
                {
                    for (int c = 0; c < TimelineQueue.Clocks; c++)
                        _tau[c] = _tickNew[c];
                    NextRound();
                    continue;
                }
                _drained = true;
                return false;
            }
        }

        public void EndAdvance()
        {
            MathAssert.That(_drained || _aborted || _mode == Mode.Idle, "EndAdvance before the tick's events were taken");
            for (int c = 0; c < TimelineQueue.Clocks; c++)
                _tau[c] = _tickNew[c];
            _mode = Mode.Idle;
        }

        // a Stop during event handling: no more events until the next Begin
        public void Abort()
        {
            _aborted = true;
            _mode = Mode.Idle;
        }

        // the number of waits ended
        public int Signal()
        {
            if (_aborted || _queue.IsFinished)
                return 0;
            return _queue.Signal(_mode != Mode.Idle, _tau);
        }

        // an item's node ends now — Signal's moments. 0 = not running
        public int EndItem(int item)
        {
            if (_aborted || _queue.IsFinished)
                return 0;
            int node = _p.NodeOfItem(item);
            return node < 0 ? 0 : _queue.EndNode(node, _mode != Mode.Idle, _tau);
        }

        // the waits Signal would end now and how many of them have no timeout — none once aborted or finished
        public int OpenSignalWaits() => OpenSignalWaits(out _);

        public int OpenSignalWaits(out int untimed)
        {
            untimed = 0;
            return _aborted || _queue.IsFinished ? 0 : _queue.OpenSignalWaits(out untimed);
        }

        // after a seek on a nominal play — every clock at ν, state from the sample there
        public void Rebuild(long position)
        {
            _sampler.Sample(position, false, _sample);
            _queue.Seed(_sample);
            for (int c = 0; c < TimelineQueue.Clocks; c++)
            {
                _base[c] = position;
                _acc[c] = 0;
                _tau[c] = position;
            }
            _mode = Mode.Idle;
            _aborted = false;
        }

        // a turned backward play's cursor: the clocks resolved as Begin does, every clock at the position — Rebuild's state
        public void BeginAt(TimelineClock baseClock, long position)
        {
            _queue.ResolveClocks(baseClock);
            _queue.Reset();
            Rebuild(position);
        }

        // the most recently reached occurrence; items without a node (flow markers) are pending
        public TimelineStage Stage(int item)
        {
            int node = _p.NodeOfItem(item);
            return node < 0 ? TimelineStage.Pending : _queue.SeenStage(node);
        }

        public long Occurrence(int item)
        {
            int node = _p.NodeOfItem(item);
            return node < 0 ? -1 : _queue.SeenOccurrence(node);
        }

        // the forward coordinate on the item's own clock, TimelineSample's formula
        public float LocalTime(int item)
        {
            int node = _p.NodeOfItem(item);
            if (node < 0 || _queue.SeenStage(node) == TimelineStage.Pending)
                return 0f;
            long start = _queue.NodeStartAt(node);
            long natural = TimelineTime.Add(start, _r.ItemDuration(item));
            long duration = natural - start;
            if (natural == TimelineTime.Infinite)
                return 0f;
            long now = _tau[_queue.NodeClock(node)];
            if (duration <= 0)
                return now >= start ? 1f : 0f;
            double t = (double)(now - start) / duration;
            return t <= 0 ? 0f : t >= 1 ? 1f : (float)t;
        }

        // the same measured from the natural end — a turned item's time, bit for bit a backward play's
        public float LocalTimeFromEnd(int item)
        {
            int node = _p.NodeOfItem(item);
            if (node < 0 || _queue.SeenStage(node) == TimelineStage.Pending)
                return 1f;
            long start = _queue.NodeStartAt(node);
            long natural = TimelineTime.Add(start, _r.ItemDuration(item));
            long duration = natural - start;
            if (natural == TimelineTime.Infinite)
                return 0f;
            long now = _tau[_queue.NodeClock(node)];
            if (duration <= 0)
                return now >= start ? 0f : 1f;
            double t = (double)(natural - now) / duration;
            return t <= 0 ? 0f : t >= 1 ? 1f : (float)t;
        }

        public double SecondsFromEnd(int item)
        {
            int node = _p.NodeOfItem(item);
            if (node < 0 || _queue.SeenStage(node) == TimelineStage.Pending)
                return TimelineTime.ToSeconds(_r.ItemDuration(item));
            long start = _queue.NodeStartAt(node);
            return (TimelineTime.Add(start, _r.ItemDuration(item)) - _tau[_queue.NodeClock(node)]) / TimelineTime.MicrosPerSecond;
        }

        public double Seconds(int item)
        {
            int node = _p.NodeOfItem(item);
            if (node < 0 || _queue.SeenStage(node) == TimelineStage.Pending)
                return 0.0;
            return (_tau[_queue.NodeClock(node)] - _queue.NodeStartAt(node)) / TimelineTime.MicrosPerSecond;
        }

        void NextRound()
        {
            long step = TimelineTime.Max(1, _queue.MaxDistance(_tau));
            for (int c = 0; c < TimelineQueue.Clocks; c++)
            {
                _tickOld[c] = _tau[c];
                _tickNew[c] = TimelineTime.Add(_tau[c], step);
                _base[c] = TimelineTime.Add(_base[c], step);
            }
            StartTick(Mode.Complete);
        }

        void StartTick(Mode mode)
        {
            _mode = mode;
            _drained = false;
            long longest = 0;
            for (int c = 0; c < TimelineQueue.Clocks; c++)
                longest = TimelineTime.Max(longest, _tickNew[c] - _tickOld[c]);
            _chunks = longest <= MaxChunk ? 1 : (longest - 1) / MaxChunk + 1;
            _chunk = 0;
            NextChunk();
        }

        void NextChunk()
        {
            _chunk++;
            for (int c = 0; c < TimelineQueue.Clocks; c++)
            {
                long span = _tickNew[c] - _tickOld[c];
                _chunkOld[c] = _tickOld[c] + Part(span, _chunk - 1);
                _chunkNew[c] = _tickOld[c] + Part(span, _chunk);
            }
            _queue.BeginChunk(_chunkOld, _chunkNew);
        }

        // j / K of a span without overflowing long
        long Part(long span, long j) => span / _chunks * j + span % _chunks * j / _chunks;
    }
}
