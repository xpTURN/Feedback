using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // group J — spawns, despawns and layers (the GameObject items and the pools)

    // J02 · J03 · J04 read both spawns — the same field names
    internal static class SpawnFields
    {
        internal static bool Of(FeedbackItem item, out GameObject prefab, out bool pooled, out int size)
        {
            switch (item)
            {
                case SpawnItem s when s.m_Active:
                    (prefab, pooled, size) = (s.m_Prefab, s.m_Pooled, s.m_PoolSize);
                    return true;
                case ParticleSpawnItem p when p.m_Active:
                    (prefab, pooled, size) = (p.m_Prefab, p.m_Pooled, p.m_PoolSize);
                    return true;
                default:
                    (prefab, pooled, size) = (null, false, 0);
                    return false;
            }
        }
    }

    // FB-J01: a Spawn without a prefab — the prepare skips it
    internal sealed class SpawnPrefabRule : FeedbackRule
    {
        internal override string Id => "FB-J01";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is SpawnItem s && s.m_Active && s.m_Prefab == null)
                    into.Add(Result(c, i, "No prefab to spawn — the item is skipped"));
            }
        }
    }

    // FB-J02: a pooled Spawn with a pool of nothing — the first play grows it (an allocation and a warning)
    internal sealed class PoolSizeRule : FeedbackRule
    {
        internal const int DefaultSize = 5;

        internal override string Id => "FB-J02";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!SpawnFields.Of(c.Items[i], out GameObject prefab, out bool pooled, out int size) || prefab == null || !pooled || size > 0)
                    continue;
                RuleResult r = Result(c, i, "A pool of " + size + " — the first play grows it");
                r.Fixes.Add(c.ItemFix("Set the pool size to " + DefaultSize, c.ItemPath(i, "m_PoolSize"), x => x.intValue = DefaultSize));
                into.Add(r);
            }
        }
    }

    // FB-J03: a Spawn without a pool — each play makes an instance (an allocation)
    internal sealed class UnpooledSpawnRule : FeedbackRule
    {
        internal override string Id => "FB-J03";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!SpawnFields.Of(c.Items[i], out GameObject prefab, out bool pooled, out _) || prefab == null || pooled)
                    continue;
                RuleResult r = Result(c, i, "Not pooled — each play makes an instance");
                r.Fixes.Add(c.ItemFix("Use a pool", c.ItemPath(i, "m_Pooled"), x => x.boolValue = true));
                into.Add(r);
            }
        }
    }

    // FB-J04: a Spawn whose prefab is an object of a scene — its copies are the object as it is then
    internal sealed class ScenePrefabRule : FeedbackRule
    {
        internal override string Id => "FB-J04";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (SpawnFields.Of(c.Items[i], out GameObject prefab, out _, out _) && prefab != null && !EditorUtility.IsPersistent(prefab))
                    into.Add(Result(c, i, "Spawns copies of a scene object, not of a prefab asset"));
            }
        }
    }

    // FB-J05: a Despawn of its own player's object or an ancestor — the player goes with it (back to its pool, or destroyed):
    // the rest of the play does not run. A pooled prefab's own despawn is the usual use
    internal sealed class OwnDespawnRule : FeedbackRule
    {
        internal override string Id => "FB-J05";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is DespawnItem d) || !d.m_Active)
                    continue;
                GameObject go = SceneTargets.GameObjectOf(c.Target(i));
                if (go != null && c.Host.transform.IsChildOf(go.transform))
                    into.Add(Result(c, i, "Despawns its own " + (c.Host is MotionDriver ? "MotionDriver" : "player") + " — the rest of its play does not run"));
            }
        }
    }

    // FB-J06: a Set Layer's layer out of 0 ~ 31 (the prepare skips it) · a layer without a name
    internal sealed class LayerRule : FeedbackRule
    {
        internal override string Id => "FB-J06";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is SetLayerItem l) || !l.m_Active)
                    continue;
                if ((uint)l.m_Layer > 31u)
                {
                    into.Add(Result(c, i, "Layer " + l.m_Layer + " is out of 0 ~ 31 — the item is skipped"));
                    continue;
                }
                if (string.IsNullOrEmpty(LayerMask.LayerToName(l.m_Layer)))
                {
                    RuleResult r = Result(c, i, "Layer " + l.m_Layer + " has no name");
                    r.Severity = RuleSeverity.Info;
                    into.Add(r);
                }
            }
        }
    }

    // FB-J07: a pooled Spawn without a lifetime in a list without a Despawn — nothing gives its instances back but a revert or the
    // close: the pool grows each play
    internal sealed class SpawnLifetimeRule : FeedbackRule
    {
        internal override string Id => "FB-J07";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            bool despawn = false;
            foreach (FeedbackItem item in c.Items)
                despawn |= item is DespawnItem d && d.m_Active;
            if (despawn)
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is SpawnItem s && s.m_Active && s.m_Prefab != null && s.m_Pooled && s.m_Lifetime <= 0f)
                    into.Add(Result(c, i, "Its spawns live until a revert or the close — without a lifetime or a despawn the pool grows each play"));
            }
        }
    }
}
