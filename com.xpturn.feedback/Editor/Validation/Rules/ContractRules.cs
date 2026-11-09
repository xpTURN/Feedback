using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // FB-C05 — values outside their contract. The runtime cuts them where it reads them; the fix writes the value that plays
    // now. A hit stop's freeze of +∞ plays as an endless freeze — an error, its fix the default
    internal sealed class ContractRule : FeedbackRule
    {
        internal override string Id => "FB-C05";
        internal override bool ListOnly => true;
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        const float DefaultFreeze = 0.1f;

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        static bool NonNegativeFinite(float v) => v >= 0f && v < float.PositiveInfinity;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            Component host = c.Host;
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem it = c.Items[i];
                if (it == null)
                    continue;
                ItemTiming t = it.m_Timing;
                Value(c, into, i, c.ItemPath(i, "m_Timing.m_Delay"), "Delay", t.m_Delay, !NonNegativeFinite(t.m_Delay), 0f);
                Value(c, into, i, c.ItemPath(i, "m_Timing.m_Duration"), "Duration", t.m_Duration, !(t.m_Duration >= 0f), 0f);
                Value(c, into, i, c.ItemPath(i, "m_Timing.m_RepeatGap"), "Repeat gap", t.m_RepeatGap, !NonNegativeFinite(t.m_RepeatGap), 0f);
                Count(c, into, i, c.ItemPath(i, "m_Timing.m_RepeatCount"), "Repeat count", t.m_RepeatCount, "0 extra runs", "Set to 0 (what plays now)");
                if (it is LoopEndItem end)
                {
                    Value(c, into, i, c.ItemPath(i, "m_LoopGap"), "Loop gap", end.m_LoopGap, !NonNegativeFinite(end.m_LoopGap), 0f);
                    Count(c, into, i, c.ItemPath(i, "m_LoopCount"), "Loop count", end.m_LoopCount, "endless", "Make it endless (0 — what plays now)");
                }
                if (it is WaitForSignalItem wait)
                    Value(c, into, i, c.ItemPath(i, "m_Timeout"), "Timeout", wait.m_Timeout, !NonNegativeFinite(wait.m_Timeout), 0f);
                if (it is HitStopItem stop)
                {
                    if (float.IsPositiveInfinity(stop.m_FreezeDuration))
                    {
                        RuleResult r = Result(c, i, "An endless freeze stops the game until the play stops");
                        r.Severity = RuleSeverity.Error;
                        string path = c.ItemPath(i, "m_FreezeDuration");
                        r.Fixes.Add(c.ItemFix("Set to " + Text(DefaultFreeze) + " (the default — changes what plays)", path, p => p.floatValue = DefaultFreeze));
                        into.Add(r);
                    }
                    else
                        Value(c, into, i, c.ItemPath(i, "m_FreezeDuration"), "Freeze", stop.m_FreezeDuration, !(stop.m_FreezeDuration >= 0f), 0f);
                    Value(c, into, i, c.ItemPath(i, "m_TimeScale"), "Time scale", stop.m_TimeScale, !(stop.m_TimeScale >= 0f && stop.m_TimeScale <= 100f), stop.m_TimeScale > 100f ? 100f : 0f);
                    Value(c, into, i, c.ItemPath(i, "m_MinimumTimeScale"), "Minimum time scale", stop.m_MinimumTimeScale, !NonNegativeFinite(stop.m_MinimumTimeScale), 0f);
                }
                if (it is EffectItem e)
                {
                    float chance = e.m_Conditions.m_Chance;
                    Value(c, into, i, c.ItemPath(i, "m_Conditions.m_Chance"), "Chance", chance, !(chance >= 0f && chance <= 100f), chance < 0f ? 0f : 100f);
                    Value(c, into, i, c.ItemPath(i, "m_Conditions.m_Cooldown"), "Cooldown", e.m_Conditions.m_Cooldown, !NonNegativeFinite(e.m_Conditions.m_Cooldown), 0f);
                    if (e.m_Random.m_RandomizeDuration)
                        Range(c, into, i, c.ItemPath(i, "m_Random.m_DurationRange"), e.m_Random.m_DurationRange);
                }
            }
            FeedbackPlayer p = c.Player;
            if (p == null)
                return;
            Value(c, into, -1, "m_Chance", "Chance", p.m_Chance, !(p.m_Chance >= 0f && p.m_Chance <= 100f), p.m_Chance < 0f ? 0f : 100f);
            Value(c, into, -1, "m_Cooldown", "Cooldown", p.m_Cooldown, !NonNegativeFinite(p.m_Cooldown), 0f);
            Value(c, into, -1, "m_DurationMultiplier", "Duration multiplier", p.m_DurationMultiplier, !NonNegativeFinite(p.m_DurationMultiplier), 1f);
            Value(c, into, -1, "m_Speed", "Speed", p.m_Speed, !NonNegativeFinite(p.m_Speed), 1f);
            Value(c, into, -1, "m_Intensity", "Intensity", p.m_Intensity, !NonNegativeFinite(p.m_Intensity), 1f);
            Value(c, into, -1, "m_StartDelay", "Start delay", p.m_StartDelay, !NonNegativeFinite(p.m_StartDelay), 0f);
            if (p.m_RandomizeDuration)
                Range(c, into, -1, "m_DurationRange", p.m_DurationRange);
            if (p.m_UseDistanceGate && !(p.m_GateDistance > 0f))
            {
                RuleResult r = Result(c, -1, "The gate distance " + Text(p.m_GateDistance) + " is not above 0 — the gate lets every play through");
                r.Fixes.Add(c.ItemFix("Turn the gate off (what plays now)", "m_UseDistanceGate", x => x.boolValue = false));
                into.Add(r);
            }
        }

        void Value(RuleContext c, List<RuleResult> into, int item, string path, string what, float value, bool outside, float plays)
        {
            if (!outside)
                return;
            RuleResult r = Result(c, item, what + " " + Text(value) + " is outside its contract — it plays as " + Text(plays));
            Component host = c.Host;
            r.Fixes.Add(c.ItemFix("Set to " + Text(plays) + " (what plays now)", path, p => p.floatValue = plays));
            into.Add(r);
        }

        void Count(RuleContext c, List<RuleResult> into, int item, string path, string what, int value, string plays, string fix)
        {
            if (value >= 0)
                return;
            RuleResult r = Result(c, item, what + " " + value + " is outside its contract — it plays as " + plays);
            Component host = c.Host;
            r.Fixes.Add(c.ItemFix(fix, path, p => p.intValue = 0));
            into.Add(r);
        }

        // a random length's range: a negative or NaN draw plays as 1 — each draw differs, so the fix changes what plays
        void Range(RuleContext c, List<RuleResult> into, int item, string path, Vector2 range)
        {
            if (Finite(range.x) && Finite(range.y) && range.x >= 0f && range.y >= 0f)
                return;
            RuleResult r = Result(c, item, "Random duration (" + Text(range.x) + ", " + Text(range.y) + ") is outside its contract — a negative or NaN draw plays as 1");
            Component host = c.Host;
            r.Fixes.Add(c.ItemFix("Clamp the range (changes what plays)", path, x =>
            {
                Vector2 v = x.vector2Value;
                v.x = float.IsNaN(v.x) ? 1f : Mathf.Max(0f, v.x);
                v.y = float.IsNaN(v.y) ? 1f : Mathf.Max(0f, v.y);
                x.vector2Value = v;
            }));
            into.Add(r);
        }
    }
}
