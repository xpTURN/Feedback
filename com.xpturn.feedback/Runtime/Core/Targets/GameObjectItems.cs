using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a state toggle — the value to set is the motion's (Bool = x ≠ 0)
    [FeedbackTarget("GameObject/Set Active", PreviewGrade.Full, Aliases = "enable disable show hide", Requires = typeof(GameObject))]
    [Serializable]
    public sealed class SetActiveItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool,
            supportedMotions: MotionMask.Set | MotionMask.Blink,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // where a spawn goes — the target's place with the offset in its space, or the offset as a world place
    public enum SpawnPlace
    {
        Target = 0,
        World = 1,
    }

    // what a spawn is put under — the scene root (the active scene) or the target
    public enum SpawnParent
    {
        None = 0,
        Target = 1,
    }

    // a component's enabled — a toggle. The component is the target's of the type's full name, the index-th of that type
    // (a Direct or Anchor component of the name is that component). Behaviour (Collider2D too) · Renderer · Collider
    // (the Physics assembly's hooks)
    [FeedbackTarget("GameObject/Component Enabled", PreviewGrade.Full, Aliases = "enable disable behaviour component collider", Requires = typeof(Component))]
    [Serializable]
    public sealed class ComponentEnabledItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool,
            supportedMotions: MotionMask.Set | MotionMask.Blink,
            category: FeedbackCategory.None, flashSensitive: true, applyMode: ApplyMode.Offset);

        [SerializeField] internal string m_ComponentType = "";
        [SerializeField] internal int m_ComponentIndex;

        internal override TargetTraits Traits => s_Traits;
    }

    // an instance of the prefab at the target — instant. Pooled by the context's pools (filled at the prepare) or made each
    // play; given back by its lifetime (0 = a despawn · a revert · the close), a Revert, a Restore and a backward play. The
    // value is not read
    [FeedbackTarget("GameObject/Spawn", PreviewGrade.DisplayOnly, Aliases = "spawn instantiate pool prefab", Requires = typeof(Transform))]
    [Serializable]
    public sealed class SpawnItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal GameObject m_Prefab;
        [SerializeField] internal bool m_Pooled = true;
        [SerializeField] internal int m_PoolSize = 5;
        [SerializeField] internal SpawnPlace m_Place;
        [SerializeField] internal Vector3 m_Offset;
        [SerializeField] internal bool m_MatchRotation;
        [SerializeField] internal SpawnParent m_Parent;
        [SerializeField] internal float m_Lifetime = 1f;

        internal override TargetTraits Traits => s_Traits;
    }

    // the target gone — a pooled live one goes back to its pool; otherwise destroyed in play, hidden in the edit mode. Not
    // undone. The value is not read
    [FeedbackTarget("GameObject/Despawn", PreviewGrade.DisplayOnly, Aliases = "despawn destroy pool return", Requires = typeof(GameObject))]
    [Serializable]
    public sealed class DespawnItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // a GameObject's layer (0 ~ 31 — the root only) — a state, Set. The value is not read
    [FeedbackTarget("GameObject/Set Layer", PreviewGrade.Full, Aliases = "layer", Requires = typeof(GameObject))]
    [Serializable]
    public sealed class SetLayerItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal int m_Layer;

        internal override TargetTraits Traits => s_Traits;
    }
}
