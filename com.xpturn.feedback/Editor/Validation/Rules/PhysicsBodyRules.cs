using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // group M — the physics items (the editor names the module's types even when the module is off). G stays as it is: a
    // physics item writes no transform

    // FB-M01: a Force · Velocity item's 3D body is kinematic — nothing moves it (an explosion's target is its centre: not judged)
    internal sealed class PhysicsKinematicRule : FeedbackRule
    {
        internal override string Id => "FB-M01";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if ((item is RigidbodyForceItem || item is RigidbodyVelocityItem) && item.m_Active && c.Target(i) is Rigidbody body && body.isKinematic)
                    into.Add(Result(c, i, "The Rigidbody is kinematic — a force or a velocity does not move it"));
            }
        }
    }

    // FB-M02: a 2D body that is Static (nothing moves it) or Kinematic (a force does not — a velocity does)
    internal sealed class Physics2DBodyTypeRule : FeedbackRule
    {
        internal override string Id => "FB-M02";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if (!(item is Rigidbody2DForceItem || item is Rigidbody2DVelocityItem) || !item.m_Active || !(c.Target(i) is Rigidbody2D body))
                    continue;
                if (body.bodyType == RigidbodyType2D.Static)
                    into.Add(Result(c, i, "The Rigidbody2D is static — a force or a velocity does not move it"));
                else if (body.bodyType == RigidbodyType2D.Kinematic && item is Rigidbody2DForceItem)
                    into.Add(Result(c, i, "The Rigidbody2D is kinematic — a force does not move it (a velocity does)"));
            }
        }
    }

    // FB-M03: a Force · Acceleration mode on an instant — it acts for one physics step (fixedDeltaTime times smaller)
    internal sealed class PhysicsOneStepRule : FeedbackRule
    {
        internal override string Id => "FB-M03";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if (item == null || !item.m_Active)
                    continue;
                PhysicsForceMode mode;
                if (item is RigidbodyForceItem f)
                    mode = f.m_Mode;
                else if (item is RigidbodyExplosionItem e)
                    mode = e.m_Mode;
                else if (item is Rigidbody2DForceItem f2 && f2.m_Mode == PhysicsForceMode2D.Force)
                {
                    RuleResult r2 = Result(c, i, "Force acts for one physics step — the push is fixedDeltaTime times smaller");
                    r2.Fixes.Add(c.ItemFix("Use Impulse", c.ItemPath(i, "m_Mode"), x => x.enumValueIndex = (int)PhysicsForceMode2D.Impulse));
                    into.Add(r2);
                    continue;
                }
                else
                    continue;
                if (mode != PhysicsForceMode.Force && mode != PhysicsForceMode.Acceleration)
                    continue;
                PhysicsForceMode use = mode == PhysicsForceMode.Force ? PhysicsForceMode.Impulse : PhysicsForceMode.VelocityChange;
                RuleResult r = Result(c, i, mode + " acts for one physics step — the push is fixedDeltaTime times smaller");
                r.Fixes.Add(c.ItemFix("Use " + use, c.ItemPath(i, "m_Mode"), x => x.enumValueIndex = (int)use));
                into.Add(r);
            }
        }
    }

    // FB-M04: a range that is zero — the item does nothing
    internal sealed class PhysicsZeroRangeRule : FeedbackRule
    {
        internal override string Id => "FB-M04";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if (item != null && item.m_Active && Zero(item))
                    into.Add(Result(c, i, "The range is zero — the item does nothing"));
            }
        }

        static bool Zero(FeedbackItem item)
        {
            switch (item)
            {
                case RigidbodyForceItem f: return f.m_Min == Vector3.zero && f.m_Max == Vector3.zero;
                case RigidbodyVelocityItem v: return v.m_Write == VelocityWrite.Add && v.m_Min == Vector3.zero && v.m_Max == Vector3.zero;
                case RigidbodyExplosionItem e: return e.m_MinForce == 0f && e.m_MaxForce == 0f;
                case Rigidbody2DForceItem f2: return f2.m_Kind == PhysicsForceKind.Torque ? f2.m_MinTorque == 0f && f2.m_MaxTorque == 0f : f2.m_Min == Vector2.zero && f2.m_Max == Vector2.zero;
                case Rigidbody2DVelocityItem v2: return v2.m_Write == VelocityWrite.Add && v2.m_Min == Vector2.zero && v2.m_Max == Vector2.zero;
                default: return false;
            }
        }
    }

    // FB-M05: an explosion that can find nothing — a radius of 0 or less, no layer — or a buffer below one (the query takes one)
    internal sealed class PhysicsExplosionQueryRule : FeedbackRule
    {
        internal override string Id => "FB-M05";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is RigidbodyExplosionItem e) || !e.m_Active)
                    continue;
                if (!(e.m_Radius > 0f))
                    into.Add(Result(c, i, "The radius is 0 or less — the explosion finds nothing"));
                else if (e.m_Layers.value == 0)
                    into.Add(Result(c, i, "No layer — the explosion finds nothing"));
                else if (e.m_MaxBodies < 1)
                    into.Add(Result(c, i, "Max Bodies is below 1 — the query takes one collider"));
            }
        }
    }
}
