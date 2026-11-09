using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // the strip's data — the list described as the prepare does (TimelineConversion.DescribeList), compiled and resolved
    // nominally (no skip · the multiplier and the start delay as the runtime reads them). No prepare, no scheduler: the total
    // is the prepared runtime's TotalDuration. A bar per program item (a repeat or a loop once); an inactive effect item keeps
    // its time (dimmed). The heads are the live plays' places
    internal sealed class TimelineModel
    {
        internal readonly struct Bar
        {
            internal readonly int ListIndex;
            internal readonly double DelayStart;    // the delay's start — the bar's body moves the delay
            internal readonly double Start;
            internal readonly double RunEnd;        // the first run's end — the right edge moves the duration
            internal readonly double End;           // the last run's end (repeats)
            internal readonly bool Background;
            internal readonly bool Off;
            internal readonly bool Flow;
            internal readonly FeedbackCategory Category;
            internal readonly bool DelayEditable;
            internal readonly bool DurationEditable;

            internal Bar(int listIndex, double delayStart, double start, double runEnd, double end, bool background, bool off, bool flow,
                FeedbackCategory category, bool delayEditable, bool durationEditable)
            {
                ListIndex = listIndex;
                DelayStart = delayStart;
                Start = start;
                RunEnd = runEnd;
                End = end;
                Background = background;
                Off = off;
                Flow = flow;
                Category = category;
                DelayEditable = delayEditable;
                DurationEditable = durationEditable;
            }
        }

        // a loop block (LoopStart … LoopEnd) — its runs (0 = endless)
        internal readonly struct Band
        {
            internal readonly double Start;
            internal readonly double End;
            internal readonly int Runs;

            internal Band(double start, double end, int runs)
            {
                Start = start;
                End = end;
                Runs = runs;
            }
        }

        internal readonly struct Head
        {
            internal readonly double Seconds;
            internal readonly bool OffNominal;

            internal Head(double seconds, bool offNominal)
            {
                Seconds = seconds;
                OffNominal = offNominal;
            }
        }

        internal double Total;
        internal bool Seekable;
        internal float Factor = 1f;
        internal readonly List<Bar> Bars = new List<Bar>();
        internal readonly List<Band> Bands = new List<Band>();

        static double Seconds(long micros) => TimelineTime.IsInfinite(micros) ? double.PositiveInfinity : TimelineTime.ToSeconds(micros);

        internal static TimelineModel Build(List<FeedbackItem> items, float multiplier, float startDelay)
        {
            var descs = new List<TimelineItemDesc>(items.Count);
            var listIndex = new List<int>(items.Count);
            TimelineConversion.DescribeList(items, descs, listIndex);
            TimelineProgram program = TimelineProgram.Compile(descs.ToArray());
            var resolved = new ResolvedTimeline(program);
            float factor = PlayerRuntime.Factor(multiplier);
            program.Resolve(default, default, factor, PlayerRuntime.Offset(startDelay), resolved);
            var model = new TimelineModel { Total = Seconds(resolved.Total), Seekable = resolved.IsSeekable, Factor = factor };
            for (int k = 0; k < descs.Count; k++)
            {
                int node = program.NodeOfItem(k);
                if (node < 0)
                    continue;
                FeedbackItem item = items[listIndex[k]];
                double start = Seconds(resolved.NodeStart(node));
                double delayStart = start - Seconds(resolved.NodeDelay(node));
                double runEnd = Seconds(resolved.NodeEnd(node));
                double end = runEnd;
                int scope = program.NodeScope(node);
                if (scope > 0 && program.BlockIsSynthetic(scope - 1))
                    end = Seconds(resolved.NodeEnd(program.BlockNode(scope - 1)));
                var effect = item as EffectItem;
                bool durationEditable = item != null && ItemRules.Visible(item, ItemRules.Duration) && !item.m_Timing.m_Infinite
                    && !(item is TargetItem t && t.m_Motion != null && t.m_Motion.NaturalDuration >= 0f);
                model.Bars.Add(new Bar(listIndex[k], delayStart, start, runEnd, end, descs[k].Background, effect != null && !effect.m_Active,
                    effect == null, effect != null ? effect.EffectiveCategory : default, item != null && ItemRules.Visible(item, ItemRules.Delay),
                    durationEditable));
            }
            for (int b = 0; b < program.BlockCount; b++)
            {
                if (program.BlockIsSynthetic(b))
                    continue;
                int node = program.BlockNode(b);
                model.Bands.Add(new Band(Seconds(resolved.NodeStart(node)), Seconds(resolved.NodeEnd(node)), program.BlockIterations(b)));
            }
            return model;
        }

        // the list a SerializedObject holds at the path — a player's or a preset's
        internal static List<FeedbackItem> Items(SerializedObject so, string listPath)
        {
            SerializedProperty list = so.FindProperty(listPath);
            var items = new List<FeedbackItem>(list != null ? list.arraySize : 0);
            for (int i = 0; list != null && i < list.arraySize; i++)
                items.Add(list.GetArrayElementAtIndex(i).managedReferenceValue as FeedbackItem);
            return items;
        }

        // the live plays' places on the nominal axis — off-nominal ones dimmed
        internal static void Heads(FeedbackPlayer player, List<Head> heads)
        {
            heads.Clear();
            PlayerRuntime runtime = player != null ? player.Runtime : null;
            if (runtime == null || runtime.IsReleased)
                return;
            for (int r = 0; r < runtime.RecordCount; r++)
            {
                PlaybackRecord record = runtime.Record(r);
                if (record.Active)
                    heads.Add(new Head(TimelineTime.ToSeconds(runtime.Position(r)), record.Cursor.IsOffNominal));
            }
        }

        // in play mode a list edit that waits for a Rebuild puts the bars on the data's axis and the heads on the old one — the
        // heads are hidden then. A preview's tuning builds again at once: not hidden
        internal static bool HeadsShown(FeedbackPlayer player) => !(Application.isPlaying && PlayModeEdits.Pending(player));
    }
}
