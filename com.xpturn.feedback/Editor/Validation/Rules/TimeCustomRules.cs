using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // group N — the time scale, the user targets and the user's feedbacks. A target without the item's IFeedbackTarget<T> is A01's (the rules
    // resolve an interface the binding's way)

    // FB-N01: a Float Event listener with a static argument of another type — boxed on every call, every frame (a dynamic
    // float listener, a float · a void one allocate nothing)
    internal sealed class FloatEventBoxingRule : FeedbackRule
    {
        internal override string Id => "FB-N01";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is CustomFloatEventItem e) || !e.m_Active)
                    continue;
                SerializedProperty calls = c.Serialized.FindProperty(c.ItemPath(i, "m_Event.m_PersistentCalls.m_Calls"));
                if (calls == null || !calls.isArray)
                    continue;
                int boxed = 0;
                for (int k = 0; k < calls.arraySize; k++)
                {
                    // PersistentListenerMode: Object 2 · Int 3 · String 5 · Bool 6 (EventDefined 0 · Void 1 · Float 4 do not box)
                    int mode = calls.GetArrayElementAtIndex(k).FindPropertyRelative("m_Mode").enumValueIndex;
                    if (mode == 2 || mode == 3 || mode == 5 || mode == 6)
                        boxed++;
                }
                if (boxed > 0)
                    into.Add(Result(c, i, boxed + " listener(s) with a static argument of another type — boxed on every call, every frame; use a dynamic float or a static float"));
            }
        }
    }

    // FB-N02: more than one component on the target implements the item's IFeedbackTarget<T> — the first is used. A dragged
    // implementer is the one chosen
    internal sealed class CustomTargetImplementersRule : FeedbackRule
    {
        static readonly List<Component> s_Found = new List<Component>();

        internal override string Id => "FB-N02";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is TargetItem t) || !t.m_Active || !(t is CustomFloatItem || t is CustomVector3Item || t is CustomColorItem))
                    continue;
                if (t.m_Target.m_Mode == TargetRefMode.Direct && RuleFixes.Requires(t).IsInstanceOfType(t.m_Target.m_Object))
                    continue;
                if (!(c.Target(i) is Component used))
                    continue;
                used.GetComponents(RuleFixes.Requires(t), s_Found);
                if (s_Found.Count > 1)
                    into.Add(Result(c, i, s_Found.Count + " components implement " + TargetChecks.TypeName(RuleFixes.Requires(t)) + " — the first (" + used.GetType().Name + ") is used"));
                s_Found.Clear();
            }
        }
    }

    // FB-N03: a Time Scale Tween · Set — the scale stays after the play (until a revert · a restore); a Flash comes back
    internal sealed class TimeScaleStaysRule : FeedbackRule
    {
        internal const float FlashDuration = 0.5f;

        internal override string Id => "FB-N03";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is TimeScaleItem t) || !t.m_Active || !(t.m_Motion is TweenMotion || t.m_Motion is SetMotion))
                    continue;
                float to = t.m_Motion is TweenMotion tween ? tween.m_Params.m_To.x : ((SetMotion)t.m_Motion).m_Params.m_Value.x;
                RuleResult r = Result(c, i, "The time scale stays at " + to.ToString("0.##", CultureInfo.InvariantCulture) + " after the play — until a revert or a restore");
                r.Fixes.Add(c.ItemFix("Use Flash", c.ItemPath(i), p =>
                {
                    var flash = (FlashMotion)MotionDefaults.Create(typeof(TimeScaleItem), MotionKind.Flash, out _);
                    flash.m_Params.m_Target = Vector4.one * to;
                    p.FindPropertyRelative("m_Motion").managedReferenceValue = flash;
                    SerializedProperty duration = p.FindPropertyRelative("m_Timing.m_Duration");
                    if (duration.floatValue <= 0f)
                        duration.floatValue = FlashDuration;
                }));
                into.Add(r);
            }
        }
    }

    // FB-N04: a Time Scale value out of 0 ~ 100 — the service clamps it
    internal sealed class TimeScaleRangeRule : FeedbackRule
    {
        internal override string Id => "FB-N04";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is TimeScaleItem t) || !t.m_Active || !Value(t.m_Motion, out float v) || (v >= 0f && v <= 100f))
                    continue;
                into.Add(Result(c, i, "The time scale " + v.ToString("0.##", CultureInfo.InvariantCulture) + " is out of 0 ~ 100 — it is clamped"));
            }
        }

        // the scale the motion goes to (a Tween By adds to the scale now — not judged)
        static bool Value(FeedbackMotion motion, out float v)
        {
            switch (motion)
            {
                case SetMotion s:
                    v = s.m_Params.m_Value.x;
                    return true;
                case FlashMotion f:
                    v = f.m_Params.m_Target.x;
                    return true;
                case TweenMotion w when w.m_Params.m_Mode == TweenMode.FromTo:
                    v = w.m_Params.m_From.x >= 0f && w.m_Params.m_From.x <= 100f ? w.m_Params.m_To.x : w.m_Params.m_From.x;
                    return true;
                case TweenMotion w when w.m_Params.m_Mode == TweenMode.To:
                    v = w.m_Params.m_To.x;
                    return true;
                case TweenMotion w when w.m_Params.m_Mode == TweenMode.From:
                    v = w.m_Params.m_From.x;
                    return true;
                default:
                    v = 1f;
                    return false;
            }
        }
    }

    // FB-N05: a user's feedback declares Flash — it reads as None: the flash limit would not count it
    internal sealed class CustomFlashRule : FeedbackRule
    {
        internal override string Id => "FB-N05";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackCategory declared;
                switch (c.Items[i])
                {
                    case CustomActionItem a:
                        declared = a.Category;
                        break;
                    case CustomEffectItem e:
                        declared = e.Category;
                        break;
                    default:
                        continue;
                }
                if (declared == FeedbackCategory.Flash)
                    into.Add(Result(c, i, "Declares the Flash category — it counts as None: the flash limit cannot count a custom feedback"));
            }
        }
    }
}
