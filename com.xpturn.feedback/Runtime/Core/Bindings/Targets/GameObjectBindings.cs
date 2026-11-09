using UnityEngine;

namespace xpTURN.Feedback
{
    // a toggle target — the toggle slot (no ValueTargetBinding, no direct mode — a direct write takes the shared toggle). Set
    // is an instant through the door (snapshot by Base), Blink a contribution lit at weight > 0. Revert puts the play's start
    // state back for Set. A concrete toggle target finds its slot
    internal abstract class ToggleTargetBinding<TSlot> : ItemBinding where TSlot : ToggleSlot
    {
        protected readonly TargetItem m_Item;
        PropertyLayerRegistry m_Layers;
        MotionEvaluator m_Evaluator;
        TSlot m_Slot;
        int m_Reserved;
        MotionState[] m_States;
        ContributionHandle[] m_Handles;
        bool[] m_Held;
        long[] m_Serials;
        bool[] m_Starts;
        bool[] m_HasStart;
        // the instants' origin (the state before the latest forward play's first Set — a backward Set writes it) and first
        // value (Restore)
        bool m_Origin;
        bool m_First;
        bool m_HasOrigin;
        bool m_HasFirst;
        // Blink's weight takes the category's scale (Set does not — an instant)
        CategoryGate m_Gate;
        FeedbackCategory m_Category;

        protected ToggleTargetBinding(TargetItem item)
        {
            m_Item = item;
        }

        protected PropertyLayerRegistry Layers => m_Layers;

        // observation
        internal TSlot Slot => m_Slot;
        internal bool Origin => m_Origin;
        internal bool HasOrigin => m_HasOrigin;
        internal bool First => m_First;
        internal bool HasFirst => m_HasFirst;

        // finds the target and gets its toggle — false refuses with the reason
        protected abstract bool Acquire(in PrepareContext context, out TSlot slot);

        // a Revert's start state not to write (Set Active: turning the disabling player on)
        protected virtual bool HoldsStart(int record) => false;

        protected bool StartOf(int record) => m_Starts[record];

        internal sealed override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            m_Evaluator = MotionEvaluators.Create(motion, 1);
            if (m_Evaluator == null)
                return Refuse("an unknown motion");
            m_Layers = context.Context.Layers;
            m_Gate = context.Context.Categories;
            m_Category = m_Item.EffectiveCategory;
            if (!Acquire(in context, out m_Slot))
                return false;
            int n = context.MaxOverlap;
            m_Slot.Reserve(n);
            m_Reserved = n;
            m_States = new MotionState[n];
            m_Handles = new ContributionHandle[n];
            m_Held = new bool[n];
            m_Serials = new long[n];
            m_Starts = new bool[n];
            m_HasStart = new bool[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        bool Live => m_Slot != null && m_Slot.IsAlive && !m_Slot.Detached;

        internal sealed override bool IsAlive => Live;

        void Start(int record, in BeginContext context)
        {
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_HasStart[record] = false;
            }
            if (!m_HasStart[record])
            {
                m_Starts[record] = m_Slot.Base;
                m_HasStart[record] = true;
            }
            var current = new Vector4(m_Starts[record] ? 1f : 0f, 0f, 0f, 0f);
            uint seed = Hash32.Tagged(context.ItemSeed, Hash32.Occurrence, Hash32.Fold(context.Occurrence));
            m_Evaluator.Begin(ref m_States[record], current, new MotionBegin { Seed = seed, Intensity = context.Intensity });
        }

        // Set — the value at any time. Relative toggles the play's start state when its value is not 0 — the intensity does
        // not enter. Backward it writes the origin, a Relative one too — nothing before a forward play
        internal sealed override void Execute(int record, in BeginContext context)
        {
            bool fresh = m_Serials[record] != context.Serial || !m_HasStart[record];
            Start(record, in context);
            if (fresh)
            {
                if (!m_HasFirst)
                {
                    m_First = m_Starts[record];
                    m_HasFirst = true;
                }
                if (!context.Reverse)
                {
                    m_Origin = m_Starts[record];
                    m_HasOrigin = true;
                }
            }
            if (context.Reverse)
            {
                if (m_HasOrigin && Live)
                    m_Slot.WriteValue(m_Origin);
                return;
            }
            m_Evaluator.Evaluate(in m_States[record], new MotionTime(0f, 0.0), out MotionSample sample);
            bool value = sample.Value.x != 0f;
            if (m_Item.m_Motion is SetMotion set && set.m_Params.m_Relative)
                value = set.m_Params.m_Value.x != 0f ? !m_Starts[record] : m_Starts[record];
            if (Live)
                m_Slot.WriteValue(value);
        }

        internal sealed override void Begin(int record, in BeginContext context)
        {
            Start(record, in context);
            if (m_Held[record])
            {
                m_Held[record] = false;
                return;
            }
            m_Handles[record] = m_Slot.Begin();
        }

        internal sealed override void Evaluate(int record, in MotionTime time)
        {
            m_Evaluator.Evaluate(in m_States[record], in time, out MotionSample sample);
            m_Slot.SetOverride(m_Handles[record], sample.Value.x != 0f, sample.Weight * m_Gate.Scale(m_Category));
        }

        internal sealed override void End(int record, EndReason reason)
        {
            if (m_Evaluator.IsInstant)
            {
                if (reason == EndReason.Revert && m_HasStart[record] && Live && !HoldsStart(record))
                    m_Slot.WriteValue(m_Starts[record]);
                return;
            }
            if (reason == EndReason.Completed)
            {
                if (m_Handles[record].IsValid)
                    m_Held[record] = true;
                return;
            }
            Let(record);
        }

        internal sealed override void Settle(int record)
        {
            if (m_Held[record])
                Let(record);
        }

        void Let(int record)
        {
            if (m_Handles[record].IsValid)
                m_Slot.End(m_Handles[record]);
            m_Handles[record] = default;
            m_Held[record] = false;
        }

        internal sealed override void Release()
        {
            if (m_Slot == null)
                return;
            m_Layers.Release(m_Slot, m_Reserved);
            m_Slot = null;
        }

        // Set writes the first value through the door; Blink has nothing to do (Hold let it go)
        internal sealed override void Restore()
        {
            if (m_Evaluator.IsInstant && m_HasFirst && Live)
                m_Slot.WriteValue(m_First);
            m_HasOrigin = false;
            m_HasFirst = false;
        }

        // the same item's old binding of the same object
        internal sealed override void Inherit(ItemBinding old)
        {
            if (!(old is ToggleTargetBinding<TSlot> from) || from.GetType() != GetType() || m_Slot == null || from.m_Slot == null || !m_Slot.Key.Equals(from.m_Slot.Key))
                return;
            m_Origin = from.m_Origin;
            m_HasOrigin = from.m_HasOrigin;
            m_First = from.m_First;
            m_HasFirst = from.m_HasFirst;
        }
    }

    // a GameObject's active state. The player and the target: a revert inside the player's OnDisable does not turn its own
    // object or an ancestor on
    internal sealed class SetActiveBinding : ToggleTargetBinding<GameObjectActiveSlot>
    {
        FeedbackPlayer m_Player;
        GameObject m_Target;

        internal SetActiveBinding(SetActiveItem item)
            : base(item)
        {
        }

        // the object it turns — MotionDriver refuses its own object or an ancestor
        internal GameObject Target => m_Target;

        protected override bool Acquire(in PrepareContext context, out GameObjectActiveSlot slot)
        {
            slot = null;
            GameObject target = TargetResolver.FindObject(in m_Item.m_Target, in context, out TargetFailure failure);
            if (target == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Player = context.Player;
            m_Target = target;
            slot = Layers.GetOrCreate(target, 0, 0, () => new GameObjectActiveSlot(target));
            return true;
        }

        // inside the player's OnDisable the engine refuses to turn the player's object or an ancestor on — and leaves it half
        // off: active, its components disabled
        protected override bool HoldsStart(int record) => StartOf(record) && m_Player != null && m_Player.IsDisabling && m_Player.transform.IsChildOf(m_Target.transform);
    }

    // a component's enabled — one toggle binding over three slot kinds, each got by its own type (the type argument is the
    // key: a Renderer's is Visibility's slot). A Direct or Anchor component of the named type is that component (a reorder
    // keeps it); otherwise the target object's components of the name, the index-th
    internal sealed class ComponentEnabledBinding : ToggleTargetBinding<ToggleSlot>
    {
        static readonly System.Collections.Generic.List<Component> s_Components = new System.Collections.Generic.List<Component>(8);

        Component m_Component;

        internal ComponentEnabledBinding(ComponentEnabledItem item)
            : base(item)
        {
        }

        // observation
        internal Component Component => m_Component;

        protected override bool Acquire(in PrepareContext context, out ToggleSlot slot)
        {
            slot = null;
            Component c = Resolve((ComponentEnabledItem)m_Item, in context, out string reason);
            if (c == null)
                return Refuse(reason);
            m_Component = c;
            switch (c)
            {
                case Behaviour b:
                    slot = Layers.GetOrCreate(b, 0, 0, () => new BehaviourEnabledSlot(b));
                    return true;
                case Renderer r:
                    slot = Layers.GetOrCreate(r, 0, 0, () => new RendererEnabledSlot(r));
                    return true;
            }
            if (LayerHooks.ColliderCheck == null)
                return Refuse(PhysicsOff);
            if (!LayerHooks.ColliderCheck(c))
                return Refuse(NotToggled);
            slot = Layers.GetOrCreate(c, 0, 0, () => new ColliderEnabledSlot(c));
            return true;
        }

        internal const string PhysicsOff = "the Physics module is off";
        internal const string NotToggled = "not a Behaviour, Renderer or Collider";

        // the item's component — null with the reason (the validation's E10 asks the same, the prepare path: a type's first
        // FullName allocates once)
        internal static Component Resolve(ComponentEnabledItem item, in PrepareContext context, out string reason)
        {
            reason = null;
            string type = item.m_ComponentType;
            if (string.IsNullOrEmpty(type))
            {
                reason = "no component type";
                return null;
            }
            Object direct = null;
            if (item.m_Target.m_Mode == TargetRefMode.Direct)
                direct = item.m_Target.m_Object;
            else if (item.m_Target.m_Mode == TargetRefMode.Anchor && context.Player != null)
                context.Player.TryGetAnchor(item.m_Target.m_Anchor, out direct);
            if (direct is Component dc && dc.GetType().FullName == type)
                return dc;
            GameObject go = TargetResolver.FindObject(in item.m_Target, in context, out TargetFailure failure);
            if (go == null)
            {
                reason = TargetResolver.Describe(failure);
                return null;
            }
            go.GetComponents(s_Components);
            int k = 0;
            Component found = null;
            for (int i = 0; i < s_Components.Count; i++)
            {
                Component c = s_Components[i];
                if (c == null || c.GetType().FullName != type)
                    continue;
                if (k++ == item.m_ComponentIndex)
                {
                    found = c;
                    break;
                }
            }
            s_Components.Clear();
            if (found == null)
            {
#if DEBUG
                reason = "no " + type + " #" + item.m_ComponentIndex + " on the target";
#else
                reason = null;
#endif
            }
            return found;
        }
    }

    // a GameObject's layer — Set (Material Swap's form): the play's start, the first value (Restore), the origin (a backward
    // play). 0 ~ 31 only — the engine logs an error and keeps the layer
    internal sealed class SetLayerBinding : ItemBinding
    {
        readonly SetLayerItem m_Item;
        PropertyLayerRegistry m_Layers;
        GameObjectLayerSlot m_Slot;
        int m_Reserved;
        long[] m_Serials;
        bool[] m_HasStart;
        int[] m_Starts;
        int m_Origin;
        int m_First;
        bool m_HasOrigin;
        bool m_HasFirst;

        internal SetLayerBinding(SetLayerItem item)
        {
            m_Item = item;
        }

        // observation
        internal GameObjectLayerSlot Slot => m_Slot;

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            GameObject target = TargetResolver.FindObject(in m_Item.m_Target, in context, out TargetFailure failure);
            if (target == null)
                return Refuse(TargetResolver.Describe(failure));
            if ((uint)m_Item.m_Layer > 31u)
                return Refuse("the layer is out of 0 ~ 31");
            m_Layers = context.Context.Layers;
            m_Slot = m_Layers.GetOrCreate(target, 0, 0, () => new GameObjectLayerSlot(target));
            int n = context.MaxOverlap;
            m_Slot.Reserve(n);
            m_Reserved = n;
            m_Serials = new long[n];
            m_HasStart = new bool[n];
            m_Starts = new int[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        bool Live => m_Slot != null && m_Slot.IsAlive && !m_Slot.Detached;

        internal override bool IsAlive => Live;

        // Set — the layer at any time; backward it writes the origin (nothing before a forward play)
        internal override void Execute(int record, in BeginContext context)
        {
            bool fresh = m_Serials[record] != context.Serial || !m_HasStart[record];
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_HasStart[record] = false;
            }
            if (!Live)
                return;
            if (!m_HasStart[record])
            {
                m_Starts[record] = m_Slot.Read();
                m_HasStart[record] = true;
            }
            if (fresh)
            {
                if (!m_HasFirst)
                {
                    m_First = m_Starts[record];
                    m_HasFirst = true;
                }
                if (!context.Reverse)
                {
                    m_Origin = m_Starts[record];
                    m_HasOrigin = true;
                }
            }
            if (context.Reverse)
            {
                if (m_HasOrigin)
                    m_Slot.WriteLayer(m_Origin);
                return;
            }
            m_Slot.WriteLayer(m_Item.m_Layer);
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
            if (reason == EndReason.Revert && m_HasStart[record] && Live)
                m_Slot.WriteLayer(m_Starts[record]);
        }

        internal override void Restore()
        {
            if (m_HasFirst && Live)
                m_Slot.WriteLayer(m_First);
            m_HasOrigin = false;
            m_HasFirst = false;
        }

        // the same item's old binding of the same object
        internal override void Inherit(ItemBinding old)
        {
            if (!(old is SetLayerBinding from) || m_Slot == null || from.m_Slot == null || !m_Slot.Key.Equals(from.m_Slot.Key))
                return;
            m_Origin = from.m_Origin;
            m_HasOrigin = from.m_HasOrigin;
            m_First = from.m_First;
            m_HasFirst = from.m_HasFirst;
        }

        internal override void Release()
        {
            if (m_Slot == null)
                return;
            m_Layers.Release(m_Slot, m_Reserved);
            m_Slot = null;
        }
    }

    // a spawn — instant, one an execution (a repeating timeline takes several in a record). The pools keep who took what
    // (this binding · the record · the play): a Revert gives back its record's play's, Restore every one, a backward play the
    // origin play's (Set's origin is none). The lifetime runs on the item's clock. A Restart · Rebuild holds the play — its
    // spawns stay
    internal sealed class SpawnBinding : ItemBinding
    {
        readonly SpawnItem m_Item;
        FeedbackContext m_Context;
        Transform m_Target;
        bool m_Unscaled;
        long[] m_Serials;
        long m_Origin = -1;

        internal SpawnBinding(SpawnItem item)
        {
            m_Item = item;
        }

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            m_Target = TargetResolver.Find<Transform>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (m_Target == null)
                return Refuse(TargetResolver.Describe(failure));
            if (m_Item.m_Prefab == null)
                return Refuse("no prefab to spawn");
            m_Context = context.Context;
            PoolService pools = m_Context.Pools;
            if (m_Item.m_Pooled)
                pools.Reserve(m_Item.m_Prefab, m_Item.m_PoolSize);
            ItemClock clock = m_Item.EffectiveClock;
            m_Unscaled = clock == ItemClock.Unscaled || (clock == ItemClock.Player && context.Player != null && context.Player.m_TimeMode == PlayerTimeMode.Unscaled);
            int n = context.MaxOverlap;
            m_Serials = new long[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        internal override bool IsAlive => m_Target != null;

        internal override void Execute(int record, in BeginContext context)
        {
            PoolService pools = m_Context.PoolsIfMade;
            if (pools == null)
                return;
            if (context.Reverse)
            {
                if (m_Origin >= 0)
                    pools.ReturnOwned(this, -1, m_Origin);
                return;
            }
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_Origin = context.Serial;
            }
            if (m_Target == null)
                return;
            Vector3 position = m_Item.m_Place == SpawnPlace.Target ? m_Target.TransformPoint(m_Item.m_Offset) : m_Item.m_Offset;
            Quaternion rotation = m_Item.m_MatchRotation ? m_Target.rotation : m_Item.m_Prefab.transform.rotation;
            Transform parent = m_Item.m_Parent == SpawnParent.Target ? m_Target : null;
            pools.Spawn(m_Item.m_Prefab, m_Item.m_Pooled, position, rotation, parent, m_Item.m_Lifetime, m_Unscaled, this, record, context.Serial);
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
            if (reason == EndReason.Revert && m_Serials[record] >= 0)
                m_Context.PoolsIfMade?.ReturnOwned(this, record, m_Serials[record]);
        }

        internal override void Restore()
        {
            m_Context?.PoolsIfMade?.ReturnOwned(this, -1, -1);
            m_Origin = -1;
        }

        // the same item's old binding: its live ones and its origin
        internal override void Inherit(ItemBinding old)
        {
            if (!(old is SpawnBinding from) || m_Context == null || from.m_Context != m_Context)
                return;
            m_Context.PoolsIfMade?.Reown(from, this);
            m_Origin = from.m_Origin;
        }

        // let go: the live ones keep their lifetime
        internal override void Release()
        {
            m_Context?.PoolsIfMade?.Disown(this);
        }
    }

    // a despawn — a live one of the context's pools goes back; otherwise destroyed in play and hidden in the
    // edit mode (a preview, an edit-mode test — through Set Active's slot: a Revert · Restore · backward play shows it again —
    // the Set rule of one slot). A destroy is not undone
    internal sealed class DespawnBinding : ItemBinding
    {
        readonly DespawnItem m_Item;
        FeedbackContext m_Context;
        PropertyLayerRegistry m_Layers;
        GameObject m_Target;
        GameObjectActiveSlot m_Hide;
        int m_Reserved;
        long[] m_Serials;
        bool[] m_HasStart;
        bool[] m_Starts;
        bool m_Origin;
        bool m_HasOrigin;
        bool m_First;
        bool m_HasFirst;

        internal DespawnBinding(DespawnItem item)
        {
            m_Item = item;
        }

        // observation
        internal GameObjectActiveSlot Hide => m_Hide;

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            m_Target = TargetResolver.FindObject(in m_Item.m_Target, in context, out TargetFailure failure);
            if (m_Target == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Context = context.Context;
            int n = context.MaxOverlap;
            if (!Application.isPlaying)
            {
                GameObject target = m_Target;
                m_Layers = m_Context.Layers;
                m_Hide = m_Layers.GetOrCreate(target, 0, 0, () => new GameObjectActiveSlot(target));
                m_Hide.Reserve(n);
                m_Reserved = n;
            }
            m_Serials = new long[n];
            m_HasStart = new bool[n];
            m_Starts = new bool[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        bool HideLive => m_Hide != null && m_Hide.IsAlive && !m_Hide.Detached;

        internal override bool IsAlive => m_Target != null;

        internal override void Execute(int record, in BeginContext context)
        {
            bool fresh = m_Serials[record] != context.Serial || !m_HasStart[record];
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_HasStart[record] = false;
            }
            if (m_Target == null)
                return;
            if (HideLive && !m_HasStart[record])
            {
                m_Starts[record] = m_Hide.Base;
                m_HasStart[record] = true;
            }
            if (fresh && HideLive)
            {
                if (!m_HasFirst)
                {
                    m_First = m_Starts[record];
                    m_HasFirst = true;
                }
                if (!context.Reverse)
                {
                    m_Origin = m_Starts[record];
                    m_HasOrigin = true;
                }
            }
            if (context.Reverse)
            {
                if (HideLive && m_HasOrigin)
                    m_Hide.WriteValue(m_Origin);
                return;
            }
            PoolService pools = m_Context.PoolsIfMade;
            if (pools != null && pools.TryReturn(m_Target))
                return;
            if (Application.isPlaying)
                Object.Destroy(m_Target);
            else if (HideLive)
                m_Hide.WriteValue(false);
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
            if (reason == EndReason.Revert && HideLive && m_HasStart[record])
                m_Hide.WriteValue(m_Starts[record]);
        }

        internal override void Restore()
        {
            if (HideLive && m_HasFirst)
                m_Hide.WriteValue(m_First);
            m_HasOrigin = false;
            m_HasFirst = false;
        }

        internal override void Release()
        {
            if (m_Hide == null)
                return;
            m_Layers.Release(m_Hide, m_Reserved);
            m_Hide = null;
        }
    }
}
