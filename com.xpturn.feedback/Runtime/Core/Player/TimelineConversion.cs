using System;
using System.Collections.Generic;

namespace xpTURN.Feedback
{
    // a list item as the timeline's input. The flow items are told apart by a type switch: the type names here keep a type
    // that only bundle data uses from being stripped. The timeline does not filter its input — the caller cuts it, here
    internal static class TimelineConversion
    {
        // the list → the program's items and their list indices — what the prepare compiles and the inspector's strip draws.
        // An inactive flow item is left out; a null slot (a missing type) takes the nearest item's clock. Returns the null
        // slots
        internal static int DescribeList(List<FeedbackItem> items, List<TimelineItemDesc> descs, List<int> listIndex)
        {
            descs.Clear();
            listIndex.Clear();
            int missing = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (!TryDescribe(items[i], out TimelineItemDesc desc))
                    continue;
                if (items[i] == null)
                    missing++;
                descs.Add(desc);
                listIndex.Add(i);
            }
            if (missing > 0)
                NeighbourClocks(descs, items, listIndex);
            return missing;
        }

        // a null slot (a missing type) has no clock of its own: it takes the nearest item's — the one before, else the one
        // after — so it never brings a clock the list does not have (the timeline picks its own clock by the clocks in use)
        static void NeighbourClocks(List<TimelineItemDesc> descs, List<FeedbackItem> items, List<int> listIndex)
        {
            int first = -1;
            for (int i = 0; i < descs.Count && first < 0; i++)
            {
                if (items[listIndex[i]] != null)
                    first = i;
            }
            if (first < 0)
                return;
            TimelineClock clock = descs[first].Clock;
            for (int i = 0; i < descs.Count; i++)
            {
                if (items[listIndex[i]] != null)
                {
                    clock = descs[i].Clock;
                    continue;
                }
                TimelineItemDesc desc = descs[i];
                desc.Clock = clock;
                descs[i] = desc;
            }
        }

        // false = the item is not in the program: an inactive flow item. A null item (a missing type) and an
        // inactive effect keep their place — the caller skips their effect
        internal static bool TryDescribe(FeedbackItem item, out TimelineItemDesc desc)
        {
            desc = default;
            if (item == null)
            {
                desc.Instant = true;
                // the runtime gives it the clock of the nearest item of the list
                desc.Clock = TimelineClock.Player;
                return true;
            }
            ItemTiming timing = item.m_Timing;
            desc.Rule = timing.m_Rule;
            desc.Delay = Span(timing.m_Delay);
            desc.Clock = ClockOf(item.EffectiveClock);
            desc.Repeat = timing.m_RepeatForever ? -1 : Math.Max(0, timing.m_RepeatCount);
            desc.RepeatGap = Span(timing.m_RepeatGap);
            desc.Background = timing.m_Background;
            float duration = timing.m_Infinite ? float.PositiveInfinity : Length(timing.m_Duration);
            float natural = -1f;
            switch (item)
            {
                case WaitItem _:
                    desc.Kind = TimelineItemKind.Wait;
                    desc.Duration = duration;
                    return item.m_Active;
                case WaitForSignalItem wait:
                    desc.Kind = TimelineItemKind.SignalWait;
                    desc.Timeout = Span(wait.m_Timeout);
                    return item.m_Active;
                case LoopStartItem _:
                    desc.Kind = TimelineItemKind.LoopStart;
                    return item.m_Active;
                case LoopEndItem end:
                    desc.Kind = TimelineItemKind.LoopEnd;
                    desc.LoopCount = end.m_LoopCount;
                    desc.LoopGap = Span(end.m_LoopGap);
                    return item.m_Active;
                case HoldItem _:
                    desc.Kind = TimelineItemKind.Hold;
                    return item.m_Active;
                case TargetItem target:
                    natural = target.m_Motion != null ? target.m_Motion.NaturalDuration : -1f;
                    break;
                case ActionItem action:
                    // an action has no "none": a length outside the contract is cut like a duration (0 = instant). An open
                    // action's length is its time limit or +inf
                    natural = Length(action.NaturalDuration);
                    desc.Open = action.Opens;
                    break;
            }
            // the motion's or the action's own length wins over the timing's — an infinite flag too
            desc.Duration = natural >= 0f ? natural : duration;
            desc.Instant = natural == 0f && !desc.Open;
            return true;
        }

        // by name — the values differ (ItemClock 0 follows the player, TimelineClock 2)
        static TimelineClock ClockOf(ItemClock clock)
        {
            switch (clock)
            {
                case ItemClock.Scaled: return TimelineClock.Scaled;
                case ItemClock.Unscaled: return TimelineClock.Unscaled;
                default: return TimelineClock.Player;
            }
        }

        // a delay, a gap or a timeout: finite and positive, else 0 (a NaN compares false)
        static float Span(float seconds) => seconds > 0f && seconds < float.PositiveInfinity ? seconds : 0f;

        // a duration: not negative, +∞ allowed, else 0
        static float Length(float seconds) => seconds >= 0f ? seconds : 0f;
    }
}
