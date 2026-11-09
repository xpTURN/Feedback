using System.Collections.Generic;
using System.Globalization;

namespace xpTURN.Feedback.Editor
{
    // C — the timeline

    // FB-C01: the compile's issues (eight codes) at their list slots
    internal sealed class TimelineIssueRule : FeedbackRule
    {
        internal override string Id => "FB-C01";
        internal override bool ListOnly => true;
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal static string Text(TimelineIssueCode code)
        {
            switch (code)
            {
                case TimelineIssueCode.UnpairedLoopStart: return "A Loop Start with no Loop End after it.";
                case TimelineIssueCode.UnpairedLoopEnd: return "A Loop End with no Loop Start before it — it repeats from the top.";
                case TimelineIssueCode.LoopTooDeep: return "Loops nested deeper than 4 — this pair is ignored.";
                case TimelineIssueCode.HoldWithoutNext: return "A Hold with nothing after it in its block.";
                case TimelineIssueCode.BackgroundInLoop: return "Background is ignored inside a loop.";
                case TimelineIssueCode.BackgroundOnFlowItem: return "Background is ignored on a flow item.";
                case TimelineIssueCode.BackgroundWithoutForeground: return "Background is ignored — nothing plays in the foreground.";
                case TimelineIssueCode.UnreachableAfterInfinite: return "Never reached — it starts after something that never ends.";
                default: return code.ToString();
            }
        }

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            List<int> index = c.ListIndex;
            foreach (TimelineIssue issue in c.Program.Issues)
                into.Add(Result(c, issue.Item >= 0 && issue.Item < index.Count ? index[issue.Item] : -1, Text(issue.Code)));
        }
    }

    // FB-C02: Backward · Toggle over a list that cannot play backward (static structure)
    internal sealed class BackwardRule : FeedbackRule
    {
        internal override string Id => "FB-C02";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (c.Player == null || c.Player.m_Direction == PlayerDirection.Forward || c.Program.IsSeekable)
                return;
            RuleResult r = Result(c, -1, "The list cannot play backward (an endless repeat or item, a signal wait without a timeout, or a wait on another player) — backward plays are refused");
            UnityEngine.Component host = c.Host;
            r.Fixes.Add(c.ItemFix("Play forward", "m_Direction", p => p.enumValueIndex = (int)PlayerDirection.Forward));
            into.Add(r);
        }
    }

    // FB-C06: an item's own way that is not read — the direction condition decides, it waits, it has no end
    internal sealed class UnreadDirectionRule : FeedbackRule
    {
        internal override string Id => "FB-C06";
        internal override RuleSeverity Severity => RuleSeverity.Info;

        internal static string Unread(EffectItem e)
        {
            if (e.m_Conditions.m_Direction != DirectionCondition.Always)
                return "the direction condition decides it";
            if (e is ActionItem a && a.Opens)
                return "it waits for another player";
            if ((e is ActionItem b && float.IsPositiveInfinity(b.NaturalDuration)) || (e is TargetItem && e.m_Timing.m_Infinite))
                return "it has no end";
            return null;
        }

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is EffectItem e) || e.m_PlayDirection == ItemPlayDirection.FollowPlayer)
                    continue;
                string why = Unread(e);
                if (why == null)
                    continue;
                RuleResult r = Result(c, i, "The play direction is not read — " + why + ".");
                r.Fixes.Add(c.ItemFix("Follow the player", c.ItemPath(i, "m_PlayDirection"), p => p.enumValueIndex = (int)ItemPlayDirection.FollowPlayer));
                into.Add(r);
            }
        }
    }

    // FB-C07: Backward on an item whose reverse only undoes its forward act — no play runs it
    internal sealed class InertBackwardRule : FeedbackRule
    {
        internal override string Id => "FB-C07";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is EffectItem e) || e.m_PlayDirection != ItemPlayDirection.Backward || e.ActsBackward || UnreadDirectionRule.Unread(e) != null)
                    continue;
                RuleResult r = Result(c, i, "Backward does nothing when played — this item only undoes its own forward act.");
                r.Fixes.Add(c.ItemFix("Follow the player", c.ItemPath(i, "m_PlayDirection"), p => p.enumValueIndex = (int)ItemPlayDirection.FollowPlayer));
                into.Add(r);
            }
        }
    }

    // FB-C03: an endless repeat with a short period · many runs in one pass (a seek or a zero-length block runs them in one
    // frame: about 1.1 µs a run in the editor)
    internal sealed class RepeatCostRule : FeedbackRule
    {
        internal const long ShortPeriod = 1000;
        internal const long ManyRuns = 10000;

        internal override string Id => "FB-C03";
        internal override bool ListOnly => true;
        internal override RuleSeverity Severity => RuleSeverity.Info;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            TimelineProgram program = c.Program;
            ResolvedTimeline resolved = c.Resolved;
            List<int> index = c.ListIndex;
            for (int b = 0; b < program.BlockCount; b++)
            {
                if (program.BlockIterations(b) > 0)
                    continue;
                long period = resolved.BlockPeriod(b);
                if (period < 0 || period >= ShortPeriod)
                    continue;
                into.Add(Result(c, FirstSlot(program, index, b), "An endless repeat every " + Text(period / 1000f) + " ms — many runs in each frame"));
            }
            long runs = 0;
            for (int k = 0; k < index.Count; k++)
            {
                int node = program.NodeOfItem(k);
                if (node < 0)
                    continue;
                long product = 1;
                bool endless = false;
                for (int scope = program.NodeScope(node); scope > 0; scope = program.NodeScope(program.BlockNode(scope - 1)))
                {
                    int iterations = program.BlockIterations(scope - 1);
                    if (iterations <= 0)
                    {
                        endless = true;
                        break;
                    }
                    product *= iterations;
                    if (product > ManyRuns)
                        break;
                }
                if (!endless)
                    runs += product;
                if (runs >= ManyRuns)
                    break;
            }
            if (runs >= ManyRuns)
                into.Add(Result(c, -1, "At least " + ManyRuns.ToString("N0", CultureInfo.InvariantCulture) + " runs in one pass — seeking it or a zero-length block runs them in one frame"));
        }

        static int FirstSlot(TimelineProgram program, List<int> index, int block)
        {
            for (int k = 0; k < index.Count; k++)
            {
                int node = program.NodeOfItem(k);
                if (node >= 0 && program.NodeScope(node) == block + 1)
                    return index[k];
            }
            return -1;
        }
    }

    // FB-C04: consecutive Holds on different clocks — the delays count in the first Hold's clock
    internal sealed class HoldClockRule : FeedbackRule
    {
        internal override string Id => "FB-C04";
        internal override bool ListOnly => true;
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            HoldItem first = null;
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if (item == null || !item.m_Active)
                    continue;
                if (item is HoldItem hold)
                {
                    if (first != null && hold.EffectiveClock != first.EffectiveClock)
                        into.Add(Result(c, i, "Consecutive Holds count their delays in the first Hold's clock (" + first.EffectiveClock + ")"));
                    first ??= hold;
                }
                else
                    first = null;
            }
        }
    }
}
