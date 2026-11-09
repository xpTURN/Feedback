using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // D — settings and playback

    // FB-D01: Lazy with the first frame — written at the first play
    internal sealed class LazyInitialRule : FeedbackRule
    {
        internal override string Id => "FB-D01";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (c.Player == null || c.Player.m_Initialization != PlayerInitialization.Lazy || !c.Player.m_ApplyInitialState)
                return;
            RuleResult r = Result(c, -1, "A Lazy player writes its first frame at the first play");
            Component host = c.Host;
            r.Fixes.Add(c.ItemFix("Initialize on Awake", "m_Initialization", p => p.enumValueIndex = (int)PlayerInitialization.Awake));
            into.Add(r);
        }
    }

    // FB-D02: plays on enable — it prepares inside the enabling call
    internal sealed class OnEnablePlayRule : FeedbackRule
    {
        internal override string Id => "FB-D02";
        internal override RuleSeverity Severity => RuleSeverity.Info;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (c.Player != null && c.Player.m_AutoPlay == PlayerAutoPlay.OnEnable)
                into.Add(Result(c, -1, "Plays on enable — it prepares inside the enabling call, before other objects' Awake made their targets"));
        }
    }

    // FB-D03: with the first frame, two Persist items on one property — playing Forward or Toggle the earlier item's start
    // wins, playing Backward the later item's end (the order the first frame writes them in). The write is
    // the motion's (Set · Tween · Spring MoveTo · Gradient Persist); the property is the target and the slot (position World
    // and Local share one)
    internal sealed class PersistTwiceRule : FeedbackRule
    {
        internal override string Id => "FB-D03";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal static bool Persists(FeedbackMotion motion)
        {
            switch (motion)
            {
                case SetMotion _:
                case TweenMotion _:
                    return true;
                case SpringMotion spring:
                    return spring.m_Params.m_Mode == SpringMode.MoveTo;
                case GradientMotion gradient:
                    return gradient.m_Params.m_Mode == GradientWrite.Persist;
                default:
                    return false;
            }
        }

        static string Slot(TargetItem t)
        {
            switch (t)
            {
                case PositionItem _: return "position";
                case RotationItem _: return "rotation";
                case ScaleItem _: return "scale";
                case MaterialColorItem m: return "color " + m.m_PropertyName + " " + m.m_MaterialIndex;
                default: return ItemWords.Name(t.GetType());
            }
        }

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (c.Player == null || !c.Player.m_ApplyInitialState)
                return;
            bool backward = c.Player.m_Direction == PlayerDirection.Backward;
            var groups = new Dictionary<(Object, string), List<int>>();
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is TargetItem t) || !t.m_Active || t.m_Motion == null || !Persists(t.m_Motion))
                    continue;
                Object target = RuleFixes.Resolve(t, in c.Prepare, out TargetFailure failure);
                if (failure != TargetFailure.None || target == null)
                    continue;
                if (target is Component component && (t is PositionItem || t is RotationItem || t is ScaleItem))
                    target = component.transform;
                var key = (target, Slot(t));
                if (!groups.TryGetValue(key, out List<int> group))
                    groups.Add(key, group = new List<int>());
                group.Add(i);
            }
            var found = new List<(int Item, string Message)>();
            foreach (KeyValuePair<(Object, string), List<int>> pair in groups)
            {
                List<int> group = pair.Value;
                if (group.Count < 2)
                    continue;
                int winner = backward ? group[group.Count - 1] : group[0];
                foreach (int i in group)
                    if (i != winner)
                        found.Add((i, backward
                            ? "The first frame takes slot " + winner + "'s end for this " + pair.Key.Item2 + " — playing Backward, the later item wins"
                            : "The first frame takes slot " + winner + "'s start for this " + pair.Key.Item2 + " — the earlier item wins"));
            }
            foreach ((int Item, string Message) f in found.OrderBy(f => f.Item))
                into.Add(Result(c, f.Item, f.Message));
        }
    }

    // FB-D04: a spring bump under Restart · Ignore — a replay within its settle time cuts it and it jumps
    internal sealed class SpringOverlapRule : FeedbackRule
    {
        internal override string Id => "FB-D04";
        internal override RuleSeverity Severity => RuleSeverity.Info;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (c.Player == null || c.Player.m_Policy == PlayPolicy.Overlap)
                return;
            float settle = -1f;
            foreach (FeedbackItem item in c.Items)
            {
                if (item is TargetItem t && t.m_Motion is SpringMotion s && s.m_Params.m_Mode == SpringMode.Bump)
                    settle = Mathf.Max(settle, s.m_Params.SettleTime());
            }
            if (settle < 0f)
                return;
            RuleResult r = Result(c, -1, "A spring bump replayed within its settle time (" + Text(settle) + " s) is cut by " + c.Player.m_Policy
                + " and jumps — use Overlap with N ≥ settle ÷ replay interval, or a MotionDriver's Follow to move a target");
            Component host = c.Host;
            r.Fixes.Add(c.ItemFix("Use Overlap", "m_Policy", p => p.enumValueIndex = (int)PlayPolicy.Overlap));
            into.Add(r);
        }
    }

    // FB-D05: a motion parameter outside its [Min] · [Range] (or NaN) — the kernel cuts it
    internal sealed class MotionDomainRule : FeedbackRule
    {
        internal override string Id => "FB-D05";
        internal override bool ListOnly => true;
        internal override bool Applies(Component host) => OnItems(host);
        internal override RuleSeverity Severity => RuleSeverity.Info;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is TargetItem t) || t.m_Motion == null)
                    continue;
                FieldInfo paramsField = t.m_Motion.GetType().GetField("m_Params", BindingFlags.Instance | BindingFlags.NonPublic);
                if (paramsField == null)
                    continue;
                object prm = paramsField.GetValue(t.m_Motion);
                foreach (FieldInfo f in prm.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    float v;
                    if (f.FieldType == typeof(float))
                        v = (float)f.GetValue(prm);
                    else if (f.FieldType == typeof(int))
                        v = (int)f.GetValue(prm);
                    else
                        continue;
                    string name = f.Name.StartsWith("m_") ? f.Name.Substring(2) : f.Name;
                    var min = f.GetCustomAttribute<MinAttribute>();
                    var range = f.GetCustomAttribute<RangeAttribute>();
                    if (min != null && !(v >= min.min))
                        into.Add(Result(c, i, t.m_Motion.Kind + " " + name + " " + Text(v) + " is below " + Text(min.min) + " — the motion reads " + Text(min.min)));
                    else if (range != null && !(v >= range.min && v <= range.max))
                        into.Add(Result(c, i, t.m_Motion.Kind + " " + name + " " + Text(v) + " is outside " + Text(range.min) + " ~ " + Text(range.max) + " — the motion cuts it"));
                }
            }
        }
    }

    // FB-D06: an Euler rotation from an authored x outside ±90° — the engine's decomposition differs, an Euler path can
    // detour. Shortest only for a Tween — a Spring MoveTo does not read it
    internal sealed class EulerFlipRule : FeedbackRule
    {
        internal override string Id => "FB-D06";
        internal override bool Applies(Component host) => OnItems(host);
        internal override RuleSeverity Severity => RuleSeverity.Info;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is RotationItem t) || t.m_Interpolation != RotationInterpolation.Euler)
                    continue;
                bool tween = t.m_Motion is TweenMotion tw && (tw.m_Params.m_Mode == TweenMode.To || tw.m_Params.m_Mode == TweenMode.From);
                bool spring = t.m_Motion is SpringMotion s && s.m_Params.m_Mode == SpringMode.MoveTo && !s.m_Params.m_Relative;
                if (!tween && !spring)
                    continue;
                var target = RuleFixes.Resolve(t, in c.Prepare, out TargetFailure failure) as Transform;
                if (failure != TargetFailure.None || target == null)
                    continue;
                Vector3 authored = TransformUtils.GetInspectorRotation(target);
                if (Mathf.Abs(Mathf.DeltaAngle(0f, authored.x)) <= 90f)
                    continue;
                RuleResult r = Result(c, i, "Starts from " + authored + " but the engine reads " + target.localEulerAngles + " — an Euler path can detour 30 ~ 90°"
                    + (tween ? "" : " (author the start within ±90°, or use a Tween with Shortest)"));
                if (tween)
                {
                    Component host = c.Host;
                    string path = c.ItemPath(i, "m_Interpolation");
                    r.Fixes.Add(c.ItemFix("Use Shortest", path, p => p.enumValueIndex = (int)RotationInterpolation.Shortest));
                }
                into.Add(r);
            }
        }
    }

    // FB-D07: an action item's Infinite — ignored, it lasts its own length
    internal sealed class ActionInfiniteRule : FeedbackRule
    {
        internal override string Id => "FB-D07";
        internal override bool ListOnly => true;
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is ActionItem a) || !a.m_Timing.m_Infinite)
                    continue;
                RuleResult r = Result(c, i, "An action's Infinite is ignored — it lasts its own length");
                Component host = c.Host;
                string path = c.ItemPath(i, "m_Timing.m_Infinite");
                r.Fixes.Add(c.ItemFix("Clear Infinite", path, p => p.boolValue = false));
                into.Add(r);
            }
        }
    }

    // FB-D08 (project): two or more runtime settings assets — which one loads is not defined
    internal sealed class SettingsTwiceRule : FeedbackRule
    {
        internal override string Id => "FB-D08";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override RuleScope Scope => RuleScope.Project;

        internal override void CheckProject(List<RuleResult> into)
        {
            List<string> paths = AssetDatabase.FindAssets("t:FeedbackSettings").Select(AssetDatabase.GUIDToAssetPath).Where(AssetMenus.IsRuntimeSettings).ToList();
            if (paths.Count < 2)
                return;
            foreach (string path in paths)
                into.Add(Result(AssetDatabase.LoadAssetAtPath<FeedbackSettings>(path), "Two or more Resources/FeedbackSettings — which one loads is not defined. Keep one"));
        }
    }
}
