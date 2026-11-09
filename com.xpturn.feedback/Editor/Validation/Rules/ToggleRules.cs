using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // E — toggles, flashes and the MotionDriver

    // the scene rules' shared reads: the written transform, a blink's rate, the candidates under the host
    internal static class SceneTargets
    {
        const int MaxCandidates = 8;

        internal static GameObject GameObjectOf(Object o) => o is GameObject g ? g : o is Component c ? c.gameObject : null;

        internal static bool Pivotable(FeedbackItem item) => item is PositionItem || item is RotationItem || item is ScaleItem;

        // the transform an item writes — a transform item's target, a camera shake's camera (a channel shake writes receivers)
        internal static Transform Written(RuleContext c, int i)
        {
            if (!(c.Items[i] is TargetItem t) || !t.m_Active)
                return null;
            if (Pivotable(t))
                return c.Target(i) as Transform;
            if (t is CameraShakeItem && t.m_Target.m_Mode != TargetRefMode.Channel)
                return (c.Target(i) as Camera)?.transform;
            return null;
        }

        internal static bool IsBlink(TargetItem t) => t.m_Motion != null && t.m_Motion.Kind == MotionKind.Blink;

        internal static float Rate(TargetItem t) => t.m_Motion is BlinkMotion b && b.m_Params.m_Period > 0f ? 1f / b.m_Params.m_Period : float.PositiveInfinity;

        // a SetActive item turns its object off: a blink, a Set to off, a relative Set (a toggle) · a Component Enabled its
        // component
        internal static bool TurnsOff(TargetItem t)
        {
            if (IsBlink(t))
                return true;
            return t.m_Motion is SetMotion set && (set.m_Params.m_Relative ? set.m_Params.m_Value.x != 0f : set.m_Params.m_Value.x == 0f);
        }

        internal static int Descendants(Transform t) => t.GetComponentsInChildren<Transform>(true).Length - 1;

        // the objects under the host (not the host — they cannot hold it)
        internal static IEnumerable<RuleFix> Under(RuleContext c, int i)
        {
            Component host = c.Host;
            string path = c.ItemPath(i, "m_Target");
            int n = 0;
            if (c.Owner is FeedbackPreset)
                yield break;
            foreach (Transform x in host.GetComponentsInChildren<Transform>(true))
            {
                if (x == host.transform)
                    continue;
                if (n++ >= MaxCandidates)
                    yield break;
                GameObject pick = x.gameObject;
                yield return new RuleFix("Use " + TargetChecks.PathOf(host, pick), () => RuleFixes.Write(host, path, p =>
                {
                    p.FindPropertyRelative("m_Mode").enumValueIndex = (int)TargetRefMode.Direct;
                    p.FindPropertyRelative("m_Object").objectReferenceValue = pick;
                }));
            }
        }

        internal static string Text(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    // FB-E01: a SetActive that turns off its own player — the stop on disable ends it, an OnEnable play starts it again
    internal sealed class OwnPlayerToggleRule : FeedbackRule
    {
        internal override string Id => "FB-E01";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            FeedbackPlayer p = c.Player;
            if (p == null)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is SetActiveItem t) || !t.m_Active || !SceneTargets.TurnsOff(t))
                    continue;
                GameObject go = SceneTargets.GameObjectOf(c.Target(i));
                if (go == null || !p.transform.IsChildOf(go.transform))
                    continue;
                bool stops = p.m_DisableBehavior != PlayerDisableBehavior.Continue;
                bool replays = SceneTargets.IsBlink(t) && p.m_AutoPlay == PlayerAutoPlay.OnEnable;
                if (!stops && !replays)
                    continue;
                RuleResult r = Result(c, i, "Turns off its own player — " + (stops ? "the stop on disable ends the effect at once" : "the OnEnable play starts it again each blink"));
                Component host = c.Host;
                if (stops)
                    r.Fixes.Add(c.ItemFix("Keep playing on disable", "m_DisableBehavior", x => x.enumValueIndex = (int)PlayerDisableBehavior.Continue));
                r.Fixes.AddRange(SceneTargets.Under(c, i));
                into.Add(r);
            }
        }
    }

    // FB-E04: a blink over a large hierarchy — the on and off pair grows with it
    internal sealed class LargeBlinkRule : FeedbackRule
    {
        internal const int Threshold = 1000;

        internal override string Id => "FB-E04";
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
                int n = SceneTargets.Descendants(go.transform);
                if (n >= Threshold)
                    into.Add(Result(c, i, "Turning " + n + " objects on and off each blink costs time — blink its renderers' visibility instead"));
            }
        }
    }

    // FB-E05: a player's blink over the flash limit — it plays and holds back the other flashes of that second
    internal sealed class FlashRateRule : FeedbackRule
    {
        internal override string Id => "FB-E05";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is TargetItem t) || !t.m_Active || !t.Flashes || !SceneTargets.IsBlink(t))
                    continue;
                float rate = SceneTargets.Rate(t);
                if (rate > c.FlashLimit)
                    into.Add(Result(c, i, "Flashes " + SceneTargets.Text(rate) + " times a second — over the flash limit " + c.FlashLimit));
            }
        }
    }

    // FB-E06: a MotionDriver that does nothing — no item, a Loop without an endless motion, a Follow without a spring
    internal sealed class DriverIdleRule : FeedbackRule
    {
        internal override string Id => "FB-E06";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => host is MotionDriver;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            var driver = (MotionDriver)c.Host;
            TargetItem item = driver.m_Item;
            if (item == null)
            {
                into.Add(Result(c, 0, "No item — the driver does nothing"));
                return;
            }
            if (!item.m_Active)
                return;
            MotionKind kind = item.m_Motion != null ? item.m_Motion.Kind : MotionKind.Set;
            bool loopKind = item.m_Motion != null && (kind == MotionKind.Oscillate || kind == MotionKind.Wiggle || kind == MotionKind.Shake || kind == MotionKind.Blink);
            bool follows = FeedbackImplementations.Create(item) is IFollowBinding;
            Component host = c.Host;
            if (driver.m_Mode == MotionDriverMode.Loop && !loopKind)
            {
                RuleResult r = Result(c, 0, "Loop takes an endless time-based motion (Oscillate · Wiggle · Shake · Blink) — the driver does nothing");
                if (kind == MotionKind.Spring && follows)
                    r.Fixes.Add(c.ItemFix("Use Follow", "m_Mode", x => x.enumValueIndex = (int)MotionDriverMode.Follow));
                into.Add(r);
            }
            else if (driver.m_Mode == MotionDriverMode.Follow && (kind != MotionKind.Spring || !follows))
            {
                RuleResult r = Result(c, 0, kind != MotionKind.Spring ? "Follow takes a Spring motion — the driver does nothing" : "Follow cannot follow this item — the driver does nothing");
                if (loopKind)
                    r.Fixes.Add(c.ItemFix("Use Loop", "m_Mode", x => x.enumValueIndex = (int)MotionDriverMode.Loop));
                into.Add(r);
            }
        }
    }

    // FB-E07: a driver's SetActive on its own object or a parent — the driver refuses it
    internal sealed class DriverOwnToggleRule : FeedbackRule
    {
        internal override string Id => "FB-E07";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => host is MotionDriver;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (!(c.Items[0] is SetActiveItem t) || !t.m_Active)
                return;
            GameObject go = SceneTargets.GameObjectOf(c.Target(0));
            if (go == null || !c.Host.transform.IsChildOf(go.transform))
                return;
            RuleResult r = Result(c, 0, "It turns off the driver's own object — the driver refuses it");
            r.Fixes.AddRange(SceneTargets.Under(c, 0));
            into.Add(r);
        }
    }

    // FB-E08: a driver's blink faster than the flash limit — a driver does not ask the limit
    internal sealed class DriverFlashRule : FeedbackRule
    {
        internal override string Id => "FB-E08";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => host is MotionDriver;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (!(c.Items[0] is TargetItem t) || !t.m_Active || !t.Flashes || !SceneTargets.IsBlink(t))
                return;
            float rate = SceneTargets.Rate(t);
            if (rate > c.FlashLimit)
                into.Add(Result(c, 0, "Drivers do not ask the flash limit — this blinks " + SceneTargets.Text(rate) + " times a second"));
        }
    }

    // E's next numbers — Component Enabled

    // FB-E09: a Component Enabled that turns off its own player or MotionDriver (E01's form) — the stop on disable ends the effect
    // at once (a StopRevert's revert turns the player on again), an OnEnable play starts it again each blink
    internal sealed class OwnComponentToggleRule : FeedbackRule
    {
        internal override string Id => "FB-E09";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is ComponentEnabledItem t) || !t.m_Active || !SceneTargets.TurnsOff(t))
                    continue;
                if (!ReferenceEquals(ComponentEnabledBinding.Resolve(t, in c.Prepare, out _), c.Host))
                    continue;
                if (c.Host is MotionDriver)
                {
                    into.Add(Result(c, i, "Turns off its own MotionDriver — it stops at once"));
                    continue;
                }
                if (!(c.Host is FeedbackPlayer p))
                    continue;
                bool stops = p.m_DisableBehavior != PlayerDisableBehavior.Continue;
                bool replays = SceneTargets.IsBlink(t) && p.m_AutoPlay == PlayerAutoPlay.OnEnable;
                if (!stops && !replays)
                    continue;
                string why = !stops ? "the OnEnable play starts it again each blink"
                    : p.m_DisableBehavior == PlayerDisableBehavior.StopRevert ? "the stop on disable ends the effect at once and its revert turns the player on again"
                    : "the stop on disable ends the effect at once";
                RuleResult r = Result(c, i, "Turns off its own player — " + why);
                if (stops)
                    r.Fixes.Add(c.ItemFix("Keep playing on disable", "m_DisableBehavior", x => x.enumValueIndex = (int)PlayerDisableBehavior.Continue));
                into.Add(r);
            }
        }
    }

    // FB-E10: a Component Enabled the prepare skips or that points where a conversion would not — no component of the type
    // and index on the target · not a Behaviour, Renderer or Collider · a Collider without the Physics module · two types of
    // the name on the object (the index counts both) · a Direct component whose index is not its own (a reorder — a Self
    // conversion would turn another)
    internal sealed class ComponentEnabledSetupRule : FeedbackRule
    {
        internal override string Id => "FB-E10";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        static readonly List<Component> s_Components = new List<Component>();

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is ComponentEnabledItem t) || !t.m_Active)
                    continue;
                Component found = ComponentEnabledBinding.Resolve(t, in c.Prepare, out _);
                if (found == null)
                {
                    // a target that does not resolve is A01's
                    if (string.IsNullOrEmpty(t.m_ComponentType))
                        into.Add(Result(c, i, "No component chosen — the item is skipped"));
                    else if (TargetResolver.FindObject(in t.m_Target, in c.Prepare, out _) != null)
                        into.Add(Result(c, i, "No " + t.m_ComponentType + " #" + t.m_ComponentIndex + " on the target — the item is skipped"));
                    continue;
                }
                // the editor names Collider with the module off — the hooks tell only whether the Physics assembly is there
                if (!(found is Behaviour) && !(found is Renderer))
                {
                    if (!(found is Collider))
                    {
                        into.Add(Result(c, i, "A " + found.GetType().Name + " is not a Behaviour, Renderer or Collider — the item is skipped"));
                        continue;
                    }
                    if (LayerHooks.ColliderCheck == null)
                    {
                        into.Add(Result(c, i, "A " + found.GetType().Name + " needs the Physics module — the item is skipped"));
                        continue;
                    }
                }
                found.gameObject.GetComponents(s_Components);
                int own = -1, k = 0;
                bool two = false;
                foreach (Component x in s_Components)
                {
                    if (x == null || x.GetType().FullName != t.m_ComponentType)
                        continue;
                    two |= x.GetType() != found.GetType();
                    if (ReferenceEquals(x, found))
                        own = k;
                    k++;
                }
                s_Components.Clear();
                if (two)
                    into.Add(Result(c, i, "Two component types are named " + t.m_ComponentType + " — the index counts both"));
                if (t.m_Target.m_Mode == TargetRefMode.Direct && ReferenceEquals(t.m_Target.m_Object, found) && own >= 0 && own != t.m_ComponentIndex)
                {
                    RuleResult r = Result(c, i, "The component is #" + own + " of its type, the index says #" + t.m_ComponentIndex + " — a Self conversion would turn another");
                    int index = own;
                    r.Fixes.Add(c.ItemFix("Set the index to " + own, c.ItemPath(i, "m_ComponentIndex"), x => x.intValue = index));
                    into.Add(r);
                }
            }
        }
    }
}
