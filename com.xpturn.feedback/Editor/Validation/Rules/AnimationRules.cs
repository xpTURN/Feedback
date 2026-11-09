using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // the animation module's rules — in the one editor assembly (the editor has a module's types whether the module is on or
    // not)

    // FB-E02: a blink's Animator goes back to its default state each time it turns on
    internal sealed class AnimatorStateRule : FeedbackRule
    {
        internal override string Id => "FB-E02";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is SetActiveItem t) || !t.m_Active || !SceneTargets.IsBlink(t))
                    continue;
                GameObject go = SceneTargets.GameObjectOf(c.Target(i));
                if (go == null)
                    continue;
                List<Animator> animators = go.GetComponentsInChildren<Animator>(true).Where(a => !a.keepAnimatorStateOnDisable).ToList();
                if (animators.Count == 0)
                    continue;
                RuleResult r = Result(c, i, "An Animator under it goes back to its default state each time it turns on");
                r.Fixes.Add(new RuleFix("Keep animator state on disable", () =>
                {
                    foreach (Animator a in animators)
                        RuleFixes.Write(a, "m_KeepAnimatorStateOnDisable", x => x.boolValue = true);
                }));
                into.Add(r);
            }
        }
    }

    // FB-E03: a blink whose object's active state an animation drives — the animation wins each frame
    internal sealed class AnimatedActiveRule : FeedbackRule
    {
        internal override string Id => "FB-E03";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is SetActiveItem t) || !t.m_Active || !SceneTargets.IsBlink(t))
                    continue;
                GameObject go = SceneTargets.GameObjectOf(c.Target(i));
                if (go != null && Animated(c, go.transform))
                    into.Add(Result(c, i, "Its active state is animated — the animation wins each frame"));
            }
        }

        static bool Animated(RuleContext c, Transform target)
        {
            for (Transform a = target; a != null; a = a.parent)
            {
                if (a.GetComponent<Animator>() == null && a.GetComponent<Animation>() == null)
                    continue;
                string path = AnimationUtility.CalculateTransformPath(target, a);
                foreach (AnimationClip clip in AnimationUtility.GetAnimationClips(a.gameObject))
                {
                    if (clip == null)
                        continue;
                    foreach (EditorCurveBinding b in c.Curves(clip))
                    {
                        if (b.type == typeof(GameObject) && b.propertyName == "m_IsActive" && b.path == path)
                            return true;
                    }
                }
            }
            return false;
        }
    }

    // FB-G09: the source of a constraint — its followers do not follow the effect (written after them)
    internal sealed class ConstraintSourceRule : FeedbackRule
    {
        internal override string Id => "FB-G09";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                Transform t = SceneTargets.Written(c, i);
                if (t != null && c.ConstraintSources(t).Contains(t))
                    into.Add(Result(c, i, "Constraint followers do not follow — the effect writes after them"));
            }
        }
    }
}
