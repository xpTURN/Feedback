using System;
using System.Collections.Generic;

namespace xpTURN.Feedback
{
    internal enum TimelineRefKind : byte
    {
        ScopeStart = 0,
        StartOf = 1,
        EndOf = 2,
        AllPrior = 3,
    }

    internal enum TimelineIssueCode : byte
    {
        UnpairedLoopStart = 0,
        UnpairedLoopEnd = 1,
        LoopTooDeep = 2,
        HoldWithoutNext = 3,
        BackgroundInLoop = 4,
        BackgroundOnFlowItem = 5,
        BackgroundWithoutForeground = 6,
        UnreachableAfterInfinite = 7,
    }

    internal readonly struct TimelineIssue
    {
        public readonly TimelineIssueCode Code;
        public readonly int Item;

        public TimelineIssue(TimelineIssueCode code, int item)
        {
            Code = code;
            Item = item;
        }
    }

    // static structure, compiled once at initialization (allocates).
    // Nodes are the members of scopes — timed items and blocks. Scope 0 is the root; block b owns scope b + 1.
    // Events: node k start = 2k, end = 2k + 1, scope s start = 2 * NodeCount + s.
    internal sealed partial class TimelineProgram
    {
        public const int MaxLoopDepth = 4;
        public const int RootScope = 0;

        TimelineItemKind[] _itemKind;
        float[] _itemDuration;
        TimelineClock[] _itemClock;
        bool[] _itemInstant;
        bool[] _itemOpen;
        int[] _itemNode;

        int[] _nodeItem;
        int[] _nodeBlock;
        int[] _nodeScope;
        StartRule[] _nodeRule;
        float[] _nodeDelay;
        float[] _nodeHoldDelay;
        int[] _nodeScaleItem;
        bool[] _nodeBackground;
        TimelineRefKind[] _nodeRefKind;
        int[] _nodeRefTarget;
        int[] _nodeWaitCount;

        int[] _blockNode;
        int[] _blockIterations;
        float[] _blockGap;
        TimelineClock[] _blockClock;
        bool[] _blockSynthetic;
        int[] _blockDepth;

        bool[] _nodeInBackground;
        int[] _nodeStartKey;
        int[] _nodeEndKey;

        TimelineClock[] _nodeClock;
        TimelineClock[] _nodeHoldClock;

        int[] _memberStart;
        int[] _members;
        int[] _dependentStart;
        int[] _dependents;

        TimelineIssue[] _issues;

        public int ItemCount => _itemKind.Length;
        public int NodeCount => _nodeItem.Length;
        public int BlockCount => _blockNode.Length;
        public int ScopeCount => _blockNode.Length + 1;
        public int EventCount => 2 * _nodeItem.Length + _blockNode.Length + 1;

        public bool IsInfinite { get; private set; }
        public bool HasOpenItem { get; private set; }
        public bool IsSeekable => !IsInfinite && !HasOpenItem;
        public int ClockMask { get; private set; }

        // ν's clock — the one real clock the list uses, else the base clock. The cursor's Begin and the player's reverse
        // play head share it
        public TimelineClock PositionClock(TimelineClock baseClock)
        {
            MathAssert.That(baseClock != TimelineClock.Player, "the base clock is Scaled or Unscaled");
            int used = 0;
            for (int clock = 0; clock <= (int)TimelineClock.Player; clock++)
            {
                if ((ClockMask & (1 << clock)) != 0)
                    used |= 1 << (clock == (int)TimelineClock.Player ? (int)baseClock : clock);
            }
            return used == 1 << (int)TimelineClock.Unscaled ? TimelineClock.Unscaled
                : used == 1 << (int)TimelineClock.Scaled ? TimelineClock.Scaled
                : baseClock;
        }

        public ReadOnlySpan<TimelineIssue> Issues => _issues;

        public TimelineItemKind ItemKind(int item) => _itemKind[item];
        public float ItemDuration(int item) => _itemDuration[item];
        public TimelineClock ItemClock(int item) => _itemClock[item];
        public bool ItemInstant(int item) => _itemInstant[item];

        // an effect its binding ends (as the conversion wrote it)
        public bool ItemOpen(int item) => _itemOpen[item];
        public int NodeOfItem(int item) => _itemNode[item];

        public bool IsBlockNode(int node) => _nodeBlock[node] >= 0;
        public int NodeItem(int node) => _nodeItem[node];
        public int NodeBlock(int node) => _nodeBlock[node];
        public int NodeScope(int node) => _nodeScope[node];
        public StartRule NodeRule(int node) => _nodeRule[node];
        public float NodeDelay(int node) => _nodeDelay[node];
        public float NodeHoldDelay(int node) => _nodeHoldDelay[node];
        public int NodeScaleItem(int node) => _nodeScaleItem[node];
        public bool NodeBackground(int node) => _nodeBackground[node];
        public TimelineRefKind NodeRefKind(int node) => _nodeRefKind[node];
        public int NodeRefTarget(int node) => _nodeRefTarget[node];
        public int NodeWaitCount(int node) => _nodeWaitCount[node];

        public int BlockNode(int block) => _blockNode[block];
        public int BlockIterations(int block) => _blockIterations[block];
        public float BlockGap(int block) => _blockGap[block];
        public TimelineClock BlockClock(int block) => _blockClock[block];
        public bool BlockIsSynthetic(int block) => _blockSynthetic[block];
        public int BlockDepth(int block) => _blockDepth[block];
        public static int BlockScope(int block) => block + 1;

        // same-instant order keys. Background entities (a background node and everything inside it)
        // come after every foreground key.
        public bool NodeInBackground(int node) => _nodeInBackground[node];
        public int NodeStartKey(int node) => _nodeStartKey[node];
        public int NodeEndKey(int node) => _nodeEndKey[node];

        // the clock a node's own delay (and an item's duration) counts on — a user block's delay is its Loop Start's. The
        // Hold delay merged into the node counts on the first Hold's clock.
        public TimelineClock NodeClock(int node) => _nodeClock[node];
        public TimelineClock NodeHoldClock(int node) => _nodeHoldClock[node];

        public ReadOnlySpan<int> ScopeMembers(int scope)
            => new ReadOnlySpan<int>(_members, _memberStart[scope], _memberStart[scope + 1] - _memberStart[scope]);

        public static int StartEvent(int node) => 2 * node;
        public static int EndEvent(int node) => 2 * node + 1;
        public int ScopeStartEvent(int scope) => 2 * _nodeItem.Length + scope;

        public ReadOnlySpan<int> Dependents(int evt)
            => new ReadOnlySpan<int>(_dependents, _dependentStart[evt], _dependentStart[evt + 1] - _dependentStart[evt]);

        // a wait's successors see its end as its start.
        public bool IsWaitLike(int node)
        {
            int item = _nodeItem[node];
            if (item < 0)
            {
                int block = _nodeBlock[node];
                if (!_blockSynthetic[block])
                    return false;
                item = _nodeItem[_members[_memberStart[BlockScope(block)]]];
            }
            var kind = _itemKind[item];
            return kind == TimelineItemKind.Wait || kind == TimelineItemKind.SignalWait;
        }

        TimelineProgram()
        {
        }

        public static TimelineProgram Compile(ReadOnlySpan<TimelineItemDesc> items)
        {
            var builder = new Builder(items);
            return builder.Build();
        }

        sealed class Builder
        {
            readonly TimelineItemDesc[] _items;
            readonly List<TimelineIssue> _issues = new List<TimelineIssue>();

            // loop pairs: start -1 = an unpaired Loop End wrapping the root from its start
            readonly List<int> _pairStart = new List<int>();
            readonly List<int> _pairEnd = new List<int>();
            int[] _pairOfStart;
            int[] _pairOfEnd;
            readonly List<int> _rootWrapPairs = new List<int>();

            readonly List<int> _nodeItem = new List<int>();
            readonly List<int> _nodeBlock = new List<int>();
            readonly List<int> _nodeScope = new List<int>();
            readonly List<StartRule> _nodeRule = new List<StartRule>();
            readonly List<float> _nodeDelay = new List<float>();
            readonly List<float> _nodeHoldDelay = new List<float>();
            readonly List<int> _nodeScaleItem = new List<int>();
            readonly List<bool> _nodeBackground = new List<bool>();
            readonly List<TimelineClock> _nodeClock = new List<TimelineClock>();
            readonly List<TimelineClock> _nodeHoldClock = new List<TimelineClock>();

            readonly List<int> _blockNode = new List<int>();
            readonly List<int> _blockIterations = new List<int>();
            readonly List<float> _blockGap = new List<float>();
            readonly List<TimelineClock> _blockClock = new List<TimelineClock>();
            readonly List<bool> _blockSynthetic = new List<bool>();
            readonly List<int> _blockDepth = new List<int>();
            readonly List<int> _blockIssueItem = new List<int>();
            readonly List<int> _blockStartKeyItem = new List<int>();
            readonly List<int> _blockEndKeyItem = new List<int>();

            readonly List<List<int>> _scopeMembers = new List<List<int>>();
            readonly List<int> _scopeHold = new List<int>();          // first pending Hold item, -1 = none
            readonly List<float> _scopeHoldDelay = new List<float>();
            readonly Stack<int> _scopeStack = new Stack<int>();
            int[] _itemNode;

            public Builder(ReadOnlySpan<TimelineItemDesc> items)
            {
                _items = items.ToArray();
            }

            public TimelineProgram Build()
            {
                var p = new TimelineProgram();
                FillItems(p);
                PairLoops();
                BuildStructure();
                ResolveRootBackground();
                p._itemNode = _itemNode;
                p._nodeItem = _nodeItem.ToArray();
                p._nodeBlock = _nodeBlock.ToArray();
                p._nodeScope = _nodeScope.ToArray();
                p._nodeRule = _nodeRule.ToArray();
                p._nodeDelay = _nodeDelay.ToArray();
                p._nodeHoldDelay = _nodeHoldDelay.ToArray();
                p._nodeScaleItem = _nodeScaleItem.ToArray();
                p._nodeBackground = _nodeBackground.ToArray();
                p._nodeClock = _nodeClock.ToArray();
                p._nodeHoldClock = _nodeHoldClock.ToArray();
                p._blockNode = _blockNode.ToArray();
                p._blockIterations = _blockIterations.ToArray();
                p._blockGap = _blockGap.ToArray();
                p._blockClock = _blockClock.ToArray();
                p._blockSynthetic = _blockSynthetic.ToArray();
                p._blockDepth = _blockDepth.ToArray();
                FillMembers(p);
                ComputeKeys(p);
                BuildReferences(p);
                AnalyzeInfinity(p);
                p._issues = _issues.ToArray();
                return p;
            }

            void FillItems(TimelineProgram p)
            {
                int n = _items.Length;
                p._itemKind = new TimelineItemKind[n];
                p._itemDuration = new float[n];
                p._itemClock = new TimelineClock[n];
                p._itemInstant = new bool[n];
                p._itemOpen = new bool[n];
                _itemNode = new int[n];
                for (int i = 0; i < n; i++)
                {
                    ref readonly var d = ref _items[i];
                    p._itemKind[i] = d.Kind;
                    p._itemClock[i] = d.Clock;
                    p._itemInstant[i] = d.Kind == TimelineItemKind.Effect && d.Instant;
                    p._itemOpen[i] = d.Kind == TimelineItemKind.Effect && d.Open;
                    p._itemDuration[i] = d.Kind switch
                    {
                        TimelineItemKind.Effect => d.Instant ? 0f : d.Duration,
                        TimelineItemKind.Wait => d.Duration,
                        TimelineItemKind.SignalWait => d.Timeout > 0f ? d.Timeout : float.PositiveInfinity,
                        _ => 0f,
                    };
                    _itemNode[i] = -1;
                }
            }

            void PairLoops()
            {
                int n = _items.Length;
                _pairOfStart = new int[n];
                _pairOfEnd = new int[n];
                for (int i = 0; i < n; i++)
                {
                    _pairOfStart[i] = -1;
                    _pairOfEnd[i] = -1;
                }

                var open = new Stack<int>();
                var pairs = new List<(int start, int end)>();
                for (int i = 0; i < n; i++)
                {
                    var kind = _items[i].Kind;
                    if (kind == TimelineItemKind.LoopStart)
                    {
                        open.Push(i);
                    }
                    else if (kind == TimelineItemKind.LoopEnd)
                    {
                        if (open.Count > 0)
                        {
                            pairs.Add((open.Pop(), i));
                        }
                        else
                        {
                            pairs.Add((-1, i));
                            _issues.Add(new TimelineIssue(TimelineIssueCode.UnpairedLoopEnd, i));
                        }
                    }
                }
                while (open.Count > 0)
                    _issues.Add(new TimelineIssue(TimelineIssueCode.UnpairedLoopStart, open.Pop()));

                // depth: order by start, then outer (later end) first
                pairs.Sort((a, b) => a.start != b.start ? a.start.CompareTo(b.start) : b.end.CompareTo(a.end));
                var ends = new Stack<int>();
                foreach (var (start, end) in pairs)
                {
                    while (ends.Count > 0 && ends.Peek() < start)
                        ends.Pop();
                    int depth = ends.Count + 1;
                    ends.Push(end);
                    if (depth > MaxLoopDepth)
                    {
                        _issues.Add(new TimelineIssue(TimelineIssueCode.LoopTooDeep, start >= 0 ? start : end));
                        continue;
                    }
                    int pair = _pairStart.Count;
                    _pairStart.Add(start);
                    _pairEnd.Add(end);
                    if (start >= 0)
                        _pairOfStart[start] = pair;
                    else
                        _rootWrapPairs.Add(pair);
                    _pairOfEnd[end] = pair;
                }
            }

            void BuildStructure()
            {
                OpenScope();

                // unpaired Loop Ends wrap the root from its start, outermost first
                foreach (int pair in _rootWrapPairs)
                    OpenUserBlock(pair, StartRule.WithPrevious, 0f, -1, _pairEnd[pair]);

                for (int i = 0; i < _items.Length; i++)
                {
                    ref readonly var d = ref _items[i];
                    switch (d.Kind)
                    {
                        case TimelineItemKind.LoopStart:
                            if (_pairOfStart[i] >= 0)
                            {
                                if (d.Background)
                                    _issues.Add(new TimelineIssue(TimelineIssueCode.BackgroundOnFlowItem, i));
                                OpenUserBlock(_pairOfStart[i], d.Rule, d.Delay, i, i);
                            }
                            break;
                        case TimelineItemKind.LoopEnd:
                            if (_pairOfEnd[i] >= 0)
                                CloseScope();
                            break;
                        case TimelineItemKind.Hold:
                        {
                            int scope = _scopeStack.Peek();
                            if (_scopeHold[scope] < 0)
                                _scopeHold[scope] = i;
                            _scopeHoldDelay[scope] += d.Delay;
                            break;
                        }
                        default:
                            AddItem(i);
                            break;
                    }
                }

                CloseScope();
            }

            void AddItem(int item)
            {
                ref readonly var d = ref _items[item];
                bool background = ResolveBackground(item);
                if (d.Repeat == 0)
                {
                    _itemNode[item] = AddNode(item, -1, d.Rule, d.Delay, item, background, d.Clock);
                    return;
                }

                // an item repeat is a one-item block; the block takes the item's rule, delay and background
                int block = OpenBlock(synthetic: true, d.Rule, d.Delay, item, background, item, item, item);
                _blockIterations[block] = d.Repeat < 0 ? 0 : d.Repeat + 1;
                _blockGap[block] = d.RepeatGap;
                _blockClock[block] = d.Clock;
                _itemNode[item] = AddNode(item, -1, StartRule.WithPrevious, 0f, item, false, d.Clock);
                CloseScope();
            }

            // background only at the root and only on effect items
            bool ResolveBackground(int item)
            {
                ref readonly var d = ref _items[item];
                if (!d.Background)
                    return false;
                if (d.Kind != TimelineItemKind.Effect)
                {
                    _issues.Add(new TimelineIssue(TimelineIssueCode.BackgroundOnFlowItem, item));
                    return false;
                }
                if (_scopeStack.Peek() != RootScope)
                {
                    _issues.Add(new TimelineIssue(TimelineIssueCode.BackgroundInLoop, item));
                    return false;
                }
                return true;
            }

            void OpenUserBlock(int pair, StartRule rule, float delay, int scaleItem, int issueItem)
            {
                ref readonly var end = ref _items[_pairEnd[pair]];
                int block = OpenBlock(synthetic: false, rule, delay, scaleItem, false, issueItem, scaleItem, _pairEnd[pair]);
                _blockIterations[block] = end.LoopCount;
                _blockGap[block] = end.LoopGap;
                _blockClock[block] = end.Clock;
            }

            int OpenBlock(bool synthetic, StartRule rule, float delay, int scaleItem, bool background, int issueItem, int startKeyItem, int endKeyItem)
            {
                int block = _blockNode.Count;
                _blockNode.Add(-1);
                _blockIterations.Add(0);
                _blockGap.Add(0f);
                _blockClock.Add(TimelineClock.Scaled);
                _blockSynthetic.Add(synthetic);
                _blockDepth.Add(_scopeStack.Count);
                _blockIssueItem.Add(issueItem);
                _blockStartKeyItem.Add(startKeyItem);
                _blockEndKeyItem.Add(endKeyItem);
                _blockNode[block] = AddNode(-1, block, rule, delay, scaleItem, background, _items[scaleItem >= 0 ? scaleItem : endKeyItem].Clock);
                OpenScope();
                return block;
            }

            void OpenScope()
            {
                _scopeStack.Push(_scopeMembers.Count);
                _scopeMembers.Add(new List<int>());
                _scopeHold.Add(-1);
                _scopeHoldDelay.Add(0f);
            }

            void CloseScope()
            {
                int scope = _scopeStack.Pop();
                if (_scopeHold[scope] >= 0)
                {
                    _issues.Add(new TimelineIssue(TimelineIssueCode.HoldWithoutNext, _scopeHold[scope]));
                    _scopeHold[scope] = -1;
                    _scopeHoldDelay[scope] = 0f;
                }
            }

            // scaleItem: whose random scale applies to the node's own delay (-1 = none)
            int AddNode(int item, int block, StartRule rule, float delay, int scaleItem, bool background, TimelineClock clock)
            {
                int scope = _scopeStack.Peek();
                // Hold turns the next member into After all and adds its delay (kept apart — only globalScale applies)
                float holdDelay = 0f;
                TimelineClock holdClock = clock;
                if (_scopeHold[scope] >= 0)
                {
                    rule = StartRule.AfterAll;
                    holdDelay = _scopeHoldDelay[scope];
                    holdClock = _items[_scopeHold[scope]].Clock;
                    _scopeHold[scope] = -1;
                    _scopeHoldDelay[scope] = 0f;
                }

                int node = _nodeItem.Count;
                _nodeItem.Add(item);
                _nodeBlock.Add(block);
                _nodeScope.Add(scope);
                _nodeRule.Add(rule);
                _nodeDelay.Add(delay);
                _nodeHoldDelay.Add(holdDelay);
                _nodeScaleItem.Add(scaleItem);
                _nodeBackground.Add(background);
                _nodeClock.Add(clock);
                _nodeHoldClock.Add(holdClock);
                _scopeMembers[scope].Add(node);
                return node;
            }

            // with no foreground at the root, background marks are ignored
            void ResolveRootBackground()
            {
                var root = _scopeMembers[RootScope];
                bool anyForeground = false;
                foreach (int node in root)
                    anyForeground |= !_nodeBackground[node];
                if (anyForeground)
                    return;
                foreach (int node in root)
                {
                    if (!_nodeBackground[node])
                        continue;
                    _nodeBackground[node] = false;
                    int block = _nodeBlock[node];
                    int item = block >= 0 ? _blockIssueItem[block] : _nodeItem[node];
                    _issues.Add(new TimelineIssue(TimelineIssueCode.BackgroundWithoutForeground, item));
                }
            }

            void ComputeKeys(TimelineProgram p)
            {
                int nodes = _nodeItem.Count;
                p._nodeInBackground = new bool[nodes];
                p._nodeStartKey = new int[nodes];
                p._nodeEndKey = new int[nodes];
                int items = _items.Length;
                // nodes are created parent-first, so a block node is set before its members
                for (int node = 0; node < nodes; node++)
                {
                    int scope = _nodeScope[node];
                    bool background = _nodeBackground[node] || (scope != RootScope && p._nodeInBackground[_blockNode[scope - 1]]);
                    p._nodeInBackground[node] = background;
                    int block = _nodeBlock[node];
                    int startRaw = block >= 0 ? _blockStartKeyItem[block] : _nodeItem[node];
                    int endRaw = block >= 0 ? _blockEndKeyItem[block] : _nodeItem[node];
                    p._nodeStartKey[node] = background ? items + startRaw : startRaw;
                    p._nodeEndKey[node] = background ? items + endRaw : endRaw;
                }
            }

            void FillMembers(TimelineProgram p)
            {
                int scopes = _scopeMembers.Count;
                p._memberStart = new int[scopes + 1];
                p._members = new int[_nodeItem.Count];
                int at = 0;
                for (int s = 0; s < scopes; s++)
                {
                    p._memberStart[s] = at;
                    foreach (int node in _scopeMembers[s])
                        p._members[at++] = node;
                }
                p._memberStart[scopes] = at;
            }

            void BuildReferences(TimelineProgram p)
            {
                int nodes = _nodeItem.Count;
                p._nodeRefKind = new TimelineRefKind[nodes];
                p._nodeRefTarget = new int[nodes];
                p._nodeWaitCount = new int[nodes];

                var edgeEvent = new List<int>();
                var edgeNode = new List<int>();
                var priors = new List<int>();
                for (int s = 0; s < p.ScopeCount; s++)
                {
                    int previous = -1;
                    int previousForeground = -1;
                    priors.Clear();
                    foreach (int node in p.ScopeMembers(s))
                    {
                        var kind = TimelineRefKind.ScopeStart;
                        int target = -1;
                        switch (p._nodeRule[node])
                        {
                            case StartRule.WithPrevious:
                                if (previous >= 0)
                                {
                                    kind = p.IsWaitLike(previous) ? TimelineRefKind.EndOf : TimelineRefKind.StartOf;
                                    target = previous;
                                }
                                break;
                            case StartRule.AfterPrevious:
                                if (previousForeground >= 0)
                                {
                                    kind = TimelineRefKind.EndOf;
                                    target = previousForeground;
                                }
                                break;
                            case StartRule.AfterAll:
                                if (priors.Count > 0)
                                    kind = TimelineRefKind.AllPrior;
                                break;
                        }

                        p._nodeRefKind[node] = kind;
                        p._nodeRefTarget[node] = target;
                        switch (kind)
                        {
                            case TimelineRefKind.ScopeStart:
                                p._nodeWaitCount[node] = 1;
                                edgeEvent.Add(p.ScopeStartEvent(s));
                                edgeNode.Add(node);
                                break;
                            case TimelineRefKind.StartOf:
                                p._nodeWaitCount[node] = 1;
                                edgeEvent.Add(StartEvent(target));
                                edgeNode.Add(node);
                                break;
                            case TimelineRefKind.EndOf:
                                p._nodeWaitCount[node] = 1;
                                edgeEvent.Add(EndEvent(target));
                                edgeNode.Add(node);
                                break;
                            case TimelineRefKind.AllPrior:
                                p._nodeWaitCount[node] = priors.Count;
                                foreach (int prior in priors)
                                {
                                    edgeEvent.Add(EndEvent(prior));
                                    edgeNode.Add(node);
                                }
                                break;
                        }

                        previous = node;
                        if (!p._nodeBackground[node])
                        {
                            previousForeground = node;
                            priors.Add(node);
                        }
                    }
                }

                int events = p.EventCount;
                p._dependentStart = new int[events + 1];
                foreach (int e in edgeEvent)
                    p._dependentStart[e + 1]++;
                for (int e = 0; e < events; e++)
                    p._dependentStart[e + 1] += p._dependentStart[e];
                p._dependents = new int[edgeNode.Count];
                var fill = new int[events];
                for (int i = 0; i < edgeNode.Count; i++)
                {
                    int e = edgeEvent[i];
                    p._dependents[p._dependentStart[e] + fill[e]++] = edgeNode[i];
                }
            }

            // infinite foreground makes the nominal total +inf; background infinity does not count
            void AnalyzeInfinity(TimelineProgram p)
            {
                int nodes = p.NodeCount;
                var startInfinite = new bool[nodes];
                var endInfinite = new bool[nodes];
                EvaluateScope(p, RootScope, startInfinite, endInfinite);

                bool infinite = false;
                foreach (int node in p.ScopeMembers(RootScope))
                    infinite |= !p._nodeBackground[node] && endInfinite[node];
                p.IsInfinite = infinite;

                bool open = false;
                int clocks = 0;
                for (int i = 0; i < p.ItemCount; i++)
                {
                    if (_itemNode[i] < 0)
                        continue;
                    clocks |= 1 << (int)p._itemClock[i];
                    open |= (p._itemKind[i] == TimelineItemKind.SignalWait && !(_items[i].Timeout > 0f)) || (p._itemKind[i] == TimelineItemKind.Effect && _items[i].Open);
                }
                for (int b = 0; b < p.BlockCount; b++)
                {
                    if (p._blockGap[b] != 0f)
                        clocks |= 1 << (int)p._blockClock[b];
                    int blockNode = p._blockNode[b];
                    if (!p._blockSynthetic[b] && p._nodeDelay[blockNode] != 0f)
                        clocks |= 1 << (int)p._nodeClock[blockNode];
                }
                for (int node = 0; node < nodes; node++)
                {
                    if (p._nodeHoldDelay[node] != 0f)
                        clocks |= 1 << (int)p._nodeHoldClock[node];
                }
                p.HasOpenItem = open;
                p.ClockMask = clocks;
            }

            void EvaluateScope(TimelineProgram p, int scope, bool[] startInfinite, bool[] endInfinite)
            {
                bool scopeStartInfinite = scope != RootScope && startInfinite[p._blockNode[scope - 1]];
                bool anyPriorEndInfinite = false;
                bool anyPriorEndInfiniteFromSelf = false;
                foreach (int node in p.ScopeMembers(scope))
                {
                    bool start;
                    bool rootCause;
                    int target = p._nodeRefTarget[node];
                    switch (p._nodeRefKind[node])
                    {
                        case TimelineRefKind.StartOf:
                            start = startInfinite[target];
                            rootCause = false;
                            break;
                        case TimelineRefKind.EndOf:
                            start = endInfinite[target];
                            rootCause = start && !startInfinite[target];
                            break;
                        case TimelineRefKind.AllPrior:
                            start = anyPriorEndInfinite;
                            rootCause = anyPriorEndInfiniteFromSelf;
                            break;
                        default:
                            start = scopeStartInfinite;
                            rootCause = false;
                            break;
                    }
                    startInfinite[node] = start;
                    if (rootCause)
                        _issues.Add(new TimelineIssue(TimelineIssueCode.UnreachableAfterInfinite, IssueItem(node)));

                    int block = p._nodeBlock[node];
                    bool end;
                    if (block >= 0)
                    {
                        int inner = BlockScope(block);
                        EvaluateScope(p, inner, startInfinite, endInfinite);
                        end = start || p._blockIterations[block] <= 0;
                        foreach (int member in p.ScopeMembers(inner))
                            end |= endInfinite[member];
                    }
                    else
                    {
                        end = start || float.IsPositiveInfinity(p._itemDuration[p._nodeItem[node]]);
                    }
                    endInfinite[node] = end;

                    if (!p._nodeBackground[node])
                    {
                        anyPriorEndInfinite |= end;
                        anyPriorEndInfiniteFromSelf |= end && !start;
                    }
                }
            }

            int IssueItem(int node)
            {
                int block = _nodeBlock[node];
                return block >= 0 ? _blockIssueItem[block] : _nodeItem[node];
            }
        }
    }
}
