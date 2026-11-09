using System;

namespace xpTURN.Feedback
{
    // the single resolve function — play, total duration, inspector and scrub all call it.
    internal sealed partial class TimelineProgram
    {
        // itemScale: per-item random duration scale (empty = all 1). skip: per-item effect skip (empty = none).
        // globalScale: player duration scale x player random duration scale. rootDelay: player start delay (unscaled).
        public void Resolve(ReadOnlySpan<float> itemScale, ReadOnlySpan<bool> skip, float globalScale, float rootDelay, ResolvedTimeline result)
        {
            for (int i = 0; i < _itemKind.Length; i++)
            {
                result._itemDuration[i] = _itemInstant[i] ? 0L : Scaled(_itemDuration[i], ScaleOf(itemScale, i), globalScale);
                result._itemSkipped[i] = skip.Length > 0 && skip[i];
            }

            for (int node = 0; node < _nodeItem.Length; node++)
            {
                int scaleItem = _nodeScaleItem[node];
                long own = Scaled(_nodeDelay[node], scaleItem >= 0 ? ScaleOf(itemScale, scaleItem) : 1f, globalScale);
                long hold = Scaled(_nodeHoldDelay[node], 1f, globalScale);
                result._nodeDelay[node] = TimelineTime.Add(own, hold);
                result._nodeHoldDelay[node] = hold;
            }

            // Loop gaps take globalScale; an item repeat's gap takes the item's scale too
            for (int block = 0; block < _blockNode.Length; block++)
            {
                float scale = _blockSynthetic[block] ? ScaleOf(itemScale, _nodeScaleItem[_blockNode[block]]) : 1f;
                result._blockGap[block] = Scaled(_blockGap[block], scale, globalScale);
            }

            long root = TimelineTime.FromSeconds(rootDelay);
            result.RootDelay = root;
            ResolveScope(RootScope, root, result);

            long total = root;
            ReadOnlySpan<int> rootMembers = ScopeMembers(RootScope);
            foreach (int node in rootMembers)
            {
                if (!_nodeBackground[node])
                    total = TimelineTime.Max(total, result._nodeEnd[node]);
            }
            result.Total = total;

            // background ends with the foreground; one not started by then never runs
            foreach (int node in rootMembers)
            {
                if (!_nodeBackground[node])
                {
                    result._nodeCancelled[node] = false;
                    continue;
                }
                bool cancelled = result._nodeStart[node] >= total;
                result._nodeCancelled[node] = cancelled;
                if (cancelled || result._nodeEnd[node] <= total)
                    continue;
                result._nodeEnd[node] = total;
                result._nodeLocalEnd[node] = total - root;
            }

            // a zero-period infinite block (possible as a clamped background) cannot be sampled or swept
            // nor can a finite play whose total saturates to the marker (infinite is infinite).
            bool seekable = IsSeekable && total != TimelineTime.Infinite;
            for (int block = 0; block < _blockNode.Length && seekable; block++)
            {
                if (_blockIterations[block] <= 0 && result.BlockPeriod(block) == 0)
                    seekable = false;
            }
            result.IsSeekable = seekable;
        }

        void ResolveScope(int scope, long scopeStart, ResolvedTimeline r)
        {
            long priorEnd = 0;
            foreach (int node in ScopeMembers(scope))
            {
                int target = _nodeRefTarget[node];
                long reference = _nodeRefKind[node] switch
                {
                    TimelineRefKind.StartOf => r._nodeLocalStart[target],
                    TimelineRefKind.EndOf => r._nodeLocalEnd[target],
                    TimelineRefKind.AllPrior => priorEnd,
                    _ => 0L,
                };
                long localStart = TimelineTime.Add(reference, r._nodeDelay[node]);
                long start = TimelineTime.Add(scopeStart, localStart);
                r._nodeLocalStart[node] = localStart;
                r._nodeStart[node] = start;
                r._nodeCancelled[node] = false;

                long localEnd;
                int block = _nodeBlock[node];
                if (block >= 0)
                {
                    int inner = BlockScope(block);
                    ResolveScope(inner, start, r);
                    long length = 0;
                    foreach (int member in ScopeMembers(inner))
                        length = TimelineTime.Max(length, r._nodeLocalEnd[member]);
                    r._blockLength[block] = length;

                    int iterations = _blockIterations[block];
                    long span = iterations <= 0
                        ? TimelineTime.Infinite
                        : TimelineTime.Add(TimelineTime.Multiply(length, iterations), TimelineTime.Multiply(r._blockGap[block], iterations - 1));
                    localEnd = TimelineTime.Add(localStart, span);
                }
                else
                {
                    localEnd = TimelineTime.Add(localStart, r._itemDuration[_nodeItem[node]]);
                }
                r._nodeLocalEnd[node] = localEnd;
                r._nodeEnd[node] = TimelineTime.Add(scopeStart, localEnd);

                if (!_nodeBackground[node])
                    priorEnd = TimelineTime.Max(priorEnd, localEnd);
            }
        }

        static float ScaleOf(ReadOnlySpan<float> itemScale, int item) => itemScale.Length > 0 ? itemScale[item] : 1f;

        // multiply in double (float intermediates may carry extra precision per runtime), then round to microseconds
        static long Scaled(float seconds, float scale, float globalScale)
            => TimelineTime.FromSeconds((double)seconds * scale * globalScale);
    }
}
