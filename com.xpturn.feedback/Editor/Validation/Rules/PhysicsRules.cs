using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // G — the written transform's rigidbody and colliders, the runtime check's five cases (RigidbodyLayerCheck) in the one
    // editor assembly
    internal abstract class BodyRule : FeedbackRule
    {
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                Transform t = SceneTargets.Written(c, i);
                if (t == null)
                    continue;
                string message = Message(t, ((TargetItem)c.Items[i]).m_DirectWrite);
                if (message == null)
                    continue;
                RuleResult r = Result(c, i, message);
                AddFixes(c, i, t, r);
                r.Fixes.AddRange(VisualPivot.Fixes(c, i, t));
                into.Add(r);
            }
        }

        protected abstract string Message(Transform t, bool direct);

        protected virtual void AddFixes(RuleContext c, int i, Transform t, RuleResult r)
        {
        }

        // a collider in the shaken subtree on a body outside it
        protected static Collider CompoundCollider(Transform t)
        {
            foreach (Collider col in t.GetComponentsInChildren<Collider>(true))
            {
                Rigidbody body = col.attachedRigidbody;
                if (body != null && !body.transform.IsChildOf(t))
                    return col;
            }
            return null;
        }
    }

    // FB-G01: an interpolating rigidbody — the revert reaches physics as a teleport
    internal sealed class InterpolatedBodyRule : BodyRule
    {
        internal override string Id => "FB-G01";
        internal override RuleSeverity Severity => RuleSeverity.Error;

        protected override string Message(Transform t, bool direct)
        {
            var body = t.GetComponent<Rigidbody>();
            return !direct && body != null && body.interpolation != RigidbodyInterpolation.None
                ? "Its Rigidbody interpolates — the revert reaches physics as a teleport and other bodies drift. Shake a child pivot" : null;
        }
    }

    // FB-G02: a rigidbody without interpolation — physics resyncs it (its path stays)
    internal sealed class PlainBodyRule : BodyRule
    {
        internal override string Id => "FB-G02";
        internal override RuleSeverity Severity => RuleSeverity.Info;

        protected override string Message(Transform t, bool direct)
        {
            var body = t.GetComponent<Rigidbody>();
            return !direct && body != null && body.interpolation == RigidbodyInterpolation.None
                ? "It has a Rigidbody — physics resyncs it each step (its path stays the same)" : null;
        }
    }

    // FB-G03: a static collider — a body resting on it drifts slightly
    internal sealed class StaticColliderRule : BodyRule
    {
        internal override string Id => "FB-G03";
        internal override RuleSeverity Severity => RuleSeverity.Info;

        protected override string Message(Transform t, bool direct)
        {
            if (t.GetComponent<Rigidbody>() != null)
                return null;
            var col = t.GetComponent<Collider>();
            return col != null && col.attachedRigidbody == null ? "It has a static Collider — physics resyncs it and a body resting on it drifts" : null;
        }
    }

    // FB-G04: a collider of another object's rigidbody in the shaken subtree — moving it changes that body
    internal sealed class CompoundColliderRule : BodyRule
    {
        internal override string Id => "FB-G04";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        protected override string Message(Transform t, bool direct)
        {
            if (t.GetComponent<Rigidbody>() != null)
                return null;
            Collider col = CompoundCollider(t);
            return col != null ? "Moving it moves '" + col.name + "', a collider of the Rigidbody '" + col.attachedRigidbody.name + "' — that body's physics changes" : null;
        }
    }

    // FB-G05: a direct write on a rigidbody — physics cannot move it during the effect
    internal sealed class DirectBodyRule : BodyRule
    {
        internal override string Id => "FB-G05";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        protected override string Message(Transform t, bool direct)
            => direct && t.GetComponent<Rigidbody>() != null ? "A direct write on a Rigidbody — physics cannot move it during the effect and its velocity piles up" : null;

        protected override void AddFixes(RuleContext c, int i, Transform t, RuleResult r)
        {
            Component host = c.Host;
            string path = c.ItemPath(i, "m_DirectWrite");
            r.Fixes.Add(c.ItemFix("Clear the direct-write mark", path, x => x.boolValue = false));
        }
    }
}
