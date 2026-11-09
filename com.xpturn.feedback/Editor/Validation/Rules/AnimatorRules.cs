using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // group L — the animator items (the editor names the module's types even when the module is off — the rules read the
    // controllers through them)
    internal static class AnimatorItems
    {
        internal static bool Is(FeedbackItem item) => item is AnimatorParameterItem || item is AnimatorFloatItem || item is AnimatorSpeedItem
            || item is AnimatorLayerWeightItem || item is AnimatorStateItem;
    }

    // FB-L01: an animator item on an Animator without a controller — the prepare skips it
    internal sealed class AnimatorControllerRule : FeedbackRule
    {
        internal override string Id => "FB-L01";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (AnimatorItems.Is(c.Items[i]) && c.Items[i].m_Active && c.Target(i) is Animator a && a.runtimeAnimatorController == null)
                    into.Add(Result(c, i, "The Animator has no controller — the item is skipped"));
            }
        }
    }

    // FB-L02: a parameter the controller lacks, of another kind, or driven by a clip curve (a write is ignored) — one name of
    // the right kind that differs in case only is offered
    internal sealed class AnimatorParameterRule : FeedbackRule
    {
        internal override string Id => "FB-L02";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                string name;
                AnimatorControllerParameterType type;
                if (item is AnimatorParameterItem p)
                {
                    name = p.m_Parameter;
                    type = AnimatorNames.TypeOf(p.m_Kind);
                }
                else if (item is AnimatorFloatItem f)
                {
                    name = f.m_Parameter;
                    type = AnimatorControllerParameterType.Float;
                }
                else
                    continue;
                AnimatorController controller = AnimatorNames.Controller(c.Target(i) as Animator);
                if (!item.m_Active || controller == null)
                    continue;
                AnimatorControllerParameter found = null;
                string near = null;
                int nears = 0;
                foreach (AnimatorControllerParameter x in controller.parameters)
                {
                    if (x.name == name)
                        found = x;
                    else if (x.type == type && string.Equals(x.name, name, System.StringComparison.OrdinalIgnoreCase))
                    {
                        near = x.name;
                        nears++;
                    }
                }
                if (found == null)
                {
                    RuleResult r = Result(c, i, "No parameter '" + name + "' in the controller — the item is skipped");
                    if (nears == 1)
                    {
                        string use = near;
                        r.Fixes.Add(c.ItemFix("Use '" + use + "'", c.ItemPath(i, "m_Parameter"), x => x.stringValue = use));
                    }
                    into.Add(r);
                }
                else if (found.type != type)
                    into.Add(Result(c, i, "'" + name + "' is a " + found.type + " parameter — the item's is " + type + " — the item is skipped"));
                else if (AnimatorNames.CurveDriven(controller).Contains(name))
                    into.Add(Result(c, i, "'" + name + "' is driven by a clip's curve — a write is ignored"));
            }
        }
    }

    // FB-L03: a Play State's state the layer lacks (every layer when none is named)
    internal sealed class AnimatorPlayStateRule : FeedbackRule
    {
        internal override string Id => "FB-L03";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is AnimatorStateItem s) || !s.m_Active)
                    continue;
                AnimatorController controller = AnimatorNames.Controller(c.Target(i) as Animator);
                if (controller == null || (!string.IsNullOrEmpty(s.m_Layer) && AnimatorNames.LayerIndex(controller, s.m_Layer) < 0))
                    continue;
                if (!AnimatorNames.States(controller, s.m_Layer).Contains(s.m_State))
                    into.Add(Result(c, i, "No state '" + s.m_State + "'" + (string.IsNullOrEmpty(s.m_Layer) ? "" : " on " + s.m_Layer) + " — the item is skipped"));
            }
        }
    }

    // FB-L04: a layer the controller lacks · a Layer Weight on the base layer (its weight is always 1)
    internal sealed class AnimatorLayerRule : FeedbackRule
    {
        internal override string Id => "FB-L04";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                string layer = item is AnimatorLayerWeightItem w ? w.m_Layer : item is AnimatorStateItem s ? s.m_Layer : null;
                if (layer == null || !item.m_Active || (item is AnimatorStateItem && layer.Length == 0))
                    continue;
                AnimatorController controller = AnimatorNames.Controller(c.Target(i) as Animator);
                if (controller == null)
                    continue;
                int index = AnimatorNames.LayerIndex(controller, layer);
                if (index < 0)
                    into.Add(Result(c, i, "No layer '" + layer + "' in the controller — the item is skipped"));
                else if (index == 0 && item is AnimatorLayerWeightItem)
                    into.Add(Result(c, i, "The base layer's weight is always 1 — the weight shows nothing"));
            }
        }
    }

    // FB-L05: an offset speed (Punch · a Spring's bump) on a Fixed (Animate Physics) animator — it evaluates after the next
    // revert, so only the direct write shows it (a Tween or a Spring's move is Persist — it shows)
    internal sealed class AnimatorFixedRule : FeedbackRule
    {
        internal override string Id => "FB-L05";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is AnimatorSpeedItem s) || !s.m_Active || s.m_DirectWrite || !(c.Target(i) is Animator a) || a.updateMode != AnimatorUpdateMode.Fixed)
                    continue;
                bool offset = s.m_Motion is PunchMotion || (s.m_Motion is SpringMotion spring && spring.m_Params.m_Mode != SpringMode.MoveTo);
                if (!offset)
                    continue;
                RuleResult r = Result(c, i, "A Fixed (Animate Physics) animator evaluates after the revert — the speed's offsets don't show");
                r.Fixes.Add(c.ItemFix("Write directly", c.ItemPath(i, "m_DirectWrite"), x => x.boolValue = true));
                into.Add(r);
            }
        }
    }

    // FB-L06: an animator item on an Animator that culls completely — off screen nothing shows
    internal sealed class AnimatorCullingRule : FeedbackRule
    {
        internal override string Id => "FB-L06";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (AnimatorItems.Is(c.Items[i]) && c.Items[i].m_Active && c.Target(i) is Animator a && a.cullingMode == AnimatorCullingMode.CullCompletely)
                    into.Add(Result(c, i, "The Animator culls completely — off screen its values show nothing"));
            }
        }
    }

    // FB-L07: a Layer Weight's value out of 0 ~ 1, a Speed's below 0 — the engine keeps a weight as written and a speed at 0
    // or more
    internal sealed class AnimatorValueRangeRule : FeedbackRule
    {
        internal override string Id => "FB-L07";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                bool weight = item is AnimatorLayerWeightItem;
                if (!(weight || item is AnimatorSpeedItem) || !item.m_Active || !(((TargetItem)item).m_Motion is TweenMotion tw) || tw.m_Params.m_Mode == TweenMode.By)
                    continue;
                foreach (float v in Values(tw))
                {
                    if (weight ? v >= 0f && v <= 1f : v >= 0f)
                        continue;
                    into.Add(Result(c, i, weight ? "A weight of " + Text(v) + " — a layer's weight is meant for 0 ~ 1" : "A speed of " + Text(v) + " — the engine keeps the speed at 0 or more"));
                    break;
                }
            }
        }

        static IEnumerable<float> Values(TweenMotion tw)
        {
            if (tw.m_Params.m_Mode != TweenMode.From)
                yield return tw.m_Params.m_To.x;
            if (tw.m_Params.m_Mode == TweenMode.From || tw.m_Params.m_Mode == TweenMode.FromTo)
                yield return tw.m_Params.m_From.x;
        }
    }

    // FB-L08: the controller's state machine behaviours run in the preview — the editor's animator update calls them
    internal sealed class AnimatorBehavioursRule : FeedbackRule
    {
        internal override string Id => "FB-L08";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!AnimatorItems.Is(c.Items[i]) || !c.Items[i].m_Active)
                    continue;
                AnimatorController controller = AnimatorNames.Controller(c.Target(i) as Animator);
                if (controller != null && AnimatorNames.HasBehaviours(controller))
                    into.Add(Result(c, i, "The controller's state machine behaviours run in the preview — the editor's update calls them"));
            }
        }
    }
}
