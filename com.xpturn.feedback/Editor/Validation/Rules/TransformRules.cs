using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // G — the written transform

    // FB-G06: a rotation under a mirrored or non-uniform parent — World: either · Local: a mirror
    internal sealed class SkewedParentRule : FeedbackRule
    {
        internal override string Id => "FB-G06";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is RotationItem t) || !t.m_Active || !(c.Target(i) is Transform target) || target.parent == null)
                    continue;
                Transform parent = target.parent;
                bool mirror = parent.localToWorldMatrix.determinant < 0f;
                Vector3 s = parent.lossyScale;
                bool uneven = !Near(Mathf.Abs(s.x), Mathf.Abs(s.y)) || !Near(Mathf.Abs(s.y), Mathf.Abs(s.z));
                if (t.m_Space == TransformSpace.World ? mirror || uneven : mirror)
                    into.Add(Result(c, i, (mirror ? "Under a mirrored parent" : "Under a non-uniformly scaled parent") + " the rotation does not turn about the axes you see"));
            }
        }

        static bool Near(float a, float b) => Mathf.Abs(a - b) <= 1e-3f * Mathf.Max(1f, Mathf.Max(a, b));
    }

    // FB-G07: a deep hierarchy's root — writing it touches every child each frame
    internal sealed class DeepRootRule : FeedbackRule
    {
        internal const int Threshold = 10000;

        internal override string Id => "FB-G07";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                Transform t = SceneTargets.Written(c, i);
                if (t == null)
                    continue;
                int n = SceneTargets.Descendants(t);
                if (n >= Threshold)
                    into.Add(Result(c, i, "Writing it touches " + n + " children each frame — shake a visible child"));
            }
        }
    }

    // FB-G08: a layout drives the RectTransform — it writes over the effect (which properties is not public)
    internal sealed class DrivenRectRule : FeedbackRule
    {
        internal override string Id => "FB-G08";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is PositionItem || c.Items[i] is ScaleItem || c.Items[i] is RectSizeDeltaItem || c.Items[i] is RectPivotItem || c.Items[i] is RectAnchorsItem
                    || c.Items[i] is RectOffsetsItem) || !((TargetItem)c.Items[i]).m_Active)
                    continue;
                if (c.Target(i) is RectTransform rt && rt.drivenByObject != null)
                    into.Add(Result(c, i, "A layout drives this RectTransform — it writes over the effect"));
            }
        }
    }

    // FB-G10: a shaken transform that draws nothing while its parent draws — a pivot's left renderer
    internal sealed class NothingDrawnRule : FeedbackRule
    {
        internal override string Id => "FB-G10";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!SceneTargets.Pivotable(c.Items[i]) || !((TargetItem)c.Items[i]).m_Active || !(c.Target(i) is Transform t) || t.parent == null)
                    continue;
                if (t.GetComponentInChildren<Renderer>(true) == null && t.parent.GetComponent<Renderer>() != null)
                    into.Add(Result(c, i, "Nothing it moves is drawn — the parent's renderer stays (move it under the pivot)"));
            }
        }
    }

    // the second reference an item reads, under its field's name — Look At's in Transform mode, Set Parent's unless an empty
    // Direct (the scene's root)
    internal static class TransformReferences
    {
        internal static bool Of(TargetItem item, out string name, out TargetRef reference)
        {
            switch (item)
            {
                case LookAtItem look when look.m_Mode == LookAtMode.Transform:
                    name = "Look Target";
                    reference = look.m_LookTarget;
                    return true;
                case RotateAroundItem around:
                    name = "Pivot";
                    reference = around.m_Pivot;
                    return true;
                case MatchTransformItem match:
                    name = "Destination";
                    reference = match.m_Destination;
                    return true;
                case SetParentItem set when !(set.m_NewParent.m_Mode == TargetRefMode.Direct && set.m_NewParent.m_Object == null):
                    name = "New Parent";
                    reference = set.m_NewParent;
                    return true;
                default:
                    name = null;
                    reference = default;
                    return false;
            }
        }

        internal static Transform Find(RuleContext c, TargetItem item, out string name)
        {
            if (!Of(item, out name, out TargetRef reference))
                return null;
            return Resolve(c, in reference, out _);
        }

        // RuleContext.Target's way: in the menu's prefab scan a prefab asset's own object stands
        internal static Transform Resolve(RuleContext c, in TargetRef reference, out TargetFailure failure)
        {
            Transform found = TargetResolver.Find<Transform>(in reference, in c.Prepare, out failure);
            if (found != null || failure != TargetFailure.NotInScene || reference.m_Mode != TargetRefMode.Direct || reference.m_Object == null
                || TargetChecks.Check(c.Host, reference.m_Object) != TargetRefusal.None)
                return found;
            failure = TargetFailure.None;
            return reference.m_Object is GameObject g ? g.transform : reference.m_Object is Component k ? k.transform : null;
        }
    }

    // FB-G11: Look At's, Rotate Around's or Match Transform's reference is the target itself — nothing moves
    internal sealed class SelfReferenceRule : FeedbackRule
    {
        internal override string Id => "FB-G11";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is TargetItem t) || t is SetParentItem || !t.m_Active || !(c.Target(i) is Transform target))
                    continue;
                Transform reference = TransformReferences.Find(c, t, out string name);
                if (reference != null && reference == target)
                    into.Add(Result(c, i, "The " + name + " is the target itself — nothing moves"));
            }
        }
    }

    // FB-G12: Set Parent's new parent is the target or under it — the engine refuses a cycle, the item is skipped
    internal sealed class ParentCycleRule : FeedbackRule
    {
        internal override string Id => "FB-G12";
        internal override RuleSeverity Severity => RuleSeverity.Error;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is SetParentItem t) || !t.m_Active || !(c.Target(i) is Transform target))
                    continue;
                Transform parent = TransformReferences.Find(c, t, out _);
                if (parent != null && (parent == target || parent.IsChildOf(target)))
                    into.Add(Result(c, i, "The new parent is the target or under it — the item is skipped"));
            }
        }
    }

    // FB-G13: Set Parent of a prefab instance's own child — edit mode cannot move it, so the preview leaves it out
    internal sealed class PrefabChildParentRule : FeedbackRule
    {
        internal override string Id => "FB-G13";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is SetParentItem t && t.m_Active && c.Target(i) is Transform target && PreviewMasks.IsPrefabChild(target))
                    into.Add(Result(c, i, "A prefab instance's child — the edit-mode preview leaves this Set Parent out, play moves it"));
            }
        }
    }

    // FB-G14: an Anchored position on a target that is not a RectTransform — it moves as Local
    internal sealed class AnchoredSpaceRule : FeedbackRule
    {
        internal override string Id => "FB-G14";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is PositionItem p) || !p.m_Active || p.m_Space != PositionSpace.Anchored)
                    continue;
                if (c.Target(i) is Transform t && !(t is RectTransform))
                    into.Add(Result(c, i, "Anchored needs a RectTransform — this one moves as Local"));
            }
        }
    }
}
