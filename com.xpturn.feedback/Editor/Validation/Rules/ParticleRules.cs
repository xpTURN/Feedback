using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // group K — the particle items (the editor names the module's types even when the module is off — the rules read the
    // systems through them)

    // FB-K01: a Particle Spawn without a prefab, or a prefab whose root has no particle system — the prepare skips it
    internal sealed class ParticlePrefabRule : FeedbackRule
    {
        internal override string Id => "FB-K01";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is ParticleSpawnItem s) || !s.m_Active)
                    continue;
                if (s.m_Prefab == null)
                    into.Add(Result(c, i, "No prefab to spawn — the item is skipped"));
                else if (!s.m_Prefab.TryGetComponent(out ParticleSystem _))
                    into.Add(Result(c, i, "The prefab's root has no particle system — the item is skipped"));
            }
        }
    }

    // FB-K02: a pooled Particle Spawn whose system destroys itself at its end — each end loses an instance and the pool grows
    internal sealed class ParticleStopActionRule : FeedbackRule
    {
        internal override string Id => "FB-K02";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is ParticleSpawnItem s && s.m_Active && s.m_Pooled && s.m_Prefab != null && s.m_Prefab.TryGetComponent(out ParticleSystem ps)
                    && ps.main.stopAction == ParticleSystemStopAction.Destroy)
                    into.Add(Result(c, i, "The prefab's system destroys itself at its end — the pool loses it and grows"));
            }
        }
    }

    // FB-K03: a looping Particle Spawn without Stop After — it never ends, so it never goes back (off screen it is not even
    // simulated)
    internal sealed class ParticleEndlessSpawnRule : FeedbackRule
    {
        internal override string Id => "FB-K03";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is ParticleSpawnItem s) || !s.m_Active || s.m_StopAfter > 0f || s.m_Prefab == null || !s.m_Prefab.TryGetComponent(out ParticleSystem ps) || !ps.main.loop)
                    continue;
                RuleResult r = Result(c, i, "A looping system without Stop After — its instances never end or go back");
                r.Fixes.Add(c.ItemFix("Stop after 1 s", c.ItemPath(i, "m_StopAfter"), x => x.floatValue = 1f));
                into.Add(r);
            }
        }
    }

    // FB-K04: an Emission Rate on a system whose emission module is off — nothing shows
    internal sealed class ParticleEmissionOffRule : FeedbackRule
    {
        internal override string Id => "FB-K04";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is ParticleEmissionRateItem e) || !e.m_Active || !(c.Target(i) is ParticleSystem ps) || ps.emission.enabled)
                    continue;
                RuleResult r = Result(c, i, "The system's emission module is off — the rate shows nothing");
                r.Fixes.Add(new RuleFix("Turn the emission module on", () => RuleFixes.Write(ps, "EmissionModule.enabled", x => x.boolValue = true)));
                into.Add(r);
            }
        }
    }

    // FB-K05: a Control Stop on a system whose stop action destroys or turns off its object — the stop is not undone
    internal sealed class ParticleStopDestroysRule : FeedbackRule
    {
        internal override string Id => "FB-K05";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is ParticleControlItem t) || !t.m_Active || t.m_Action != ParticleAction.Stop || !(c.Target(i) is ParticleSystem ps))
                    continue;
                ParticleSystemStopAction action = ps.main.stopAction;
                if (action == ParticleSystemStopAction.Destroy || action == ParticleSystemStopAction.Disable)
                    into.Add(Result(c, i, "The system's stop action " + (action == ParticleSystemStopAction.Destroy ? "destroys" : "turns off") + " its object — a revert can't bring it back"));
            }
        }
    }

    // FB-K06: a Simulation Speed's value out of 0 ~ 100, an Emission Rate's below 0 — the engine clamps it
    internal sealed class ParticleValueRangeRule : FeedbackRule
    {
        internal const float MaxSpeed = 100f;

        internal override string Id => "FB-K06";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                bool speed = item is ParticleSimulationSpeedItem;
                if (!(speed || item is ParticleEmissionRateItem) || !item.m_Active || !(item is TargetItem t))
                    continue;
                float max = speed ? MaxSpeed : float.PositiveInfinity;
                foreach (float v in Values(t.m_Motion))
                {
                    if (v >= 0f && v <= max)
                        continue;
                    into.Add(Result(c, i, "A value of " + Text(v) + " — the engine keeps " + (speed ? "the speed in 0 ~ 100" : "the rate at 0 or more")));
                    break;
                }
            }
        }

        static IEnumerable<float> Values(FeedbackMotion motion)
        {
            switch (motion)
            {
                case TweenMotion tw when tw.m_Params.m_Mode != TweenMode.By:
                    if (tw.m_Params.m_Mode != TweenMode.From)
                        yield return tw.m_Params.m_To.x;
                    if (tw.m_Params.m_Mode == TweenMode.From || tw.m_Params.m_Mode == TweenMode.FromTo)
                        yield return tw.m_Params.m_From.x;
                    break;
                case FlashMotion f:
                    yield return f.m_Params.m_Target.x;
                    break;
            }
        }
    }
}
