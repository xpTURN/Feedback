using UnityEngine;

namespace xpTURN.Feedback
{
    // the prepare's checks — a controller · a parameter by name and kind. A wrong name warns at every write, so it is refused
    // here. An animator not initialized (its object inactive) lists its parameters in the editor (from the controller) but
    // none in a player — then the parameter is judged at the item's first run on the initialized animator
    internal static class AnimatorPrepare
    {
        internal static string Check(Animator animator)
        {
            if (animator.runtimeAnimatorController == null)
                return "the Animator has no controller";
            return null;
        }

        // the parameter by name and kind — at the prepare (the array allocates; GetParameter throws on an inactive object's
        // animator). later: the animator lists none and is not initialized — nothing is judged now
        internal static string Parameter(Animator animator, string name, AnimatorControllerParameterType type, out int hash, out bool later)
        {
            hash = Animator.StringToHash(name);
            AnimatorControllerParameter[] all = animator.parameters;
            later = all.Length == 0 && !animator.isInitialized;
            if (later)
                return null;
            foreach (AnimatorControllerParameter p in all)
            {
                if (p.nameHash != hash)
                    continue;
                return p.type == type ? null : "'" + name + "' is a " + p.type + " parameter — the item's is " + type;
            }
            return "no parameter '" + name + "'";
        }

        internal static AnimatorControllerParameterType TypeOf(AnimatorParameterKind kind)
            => kind == AnimatorParameterKind.Trigger ? AnimatorControllerParameterType.Trigger
                : kind == AnimatorParameterKind.Bool ? AnimatorControllerParameterType.Bool
                : kind == AnimatorParameterKind.Int ? AnimatorControllerParameterType.Int : AnimatorControllerParameterType.Float;

        internal static string Parameter(Animator animator, string name, AnimatorParameterKind kind, out int hash, out bool later) => Parameter(animator, name, TypeOf(kind), out hash, out later);

        // a judgment put off at the prepare — on the initialized animator, at the first run
        internal static bool Has(Animator animator, int hash, AnimatorControllerParameterType type)
        {
            foreach (AnimatorControllerParameter p in animator.parameters)
                if (p.nameHash == hash)
                    return p.type == type;
            return false;
        }

        // uniform in [0, 1) from a play's item seed
        internal static float Unit(uint seed)
        {
            uint x = seed * 747796405u + 2891336453u;
            x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
            x = (x >> 22) ^ x;
            return (x >> 8) * (1f / 16777216f);
        }
    }

    // Parameter — Set: Bool · Int · Trigger on the state slots, Float on the value slot (the Persist door) · the pick ·
    // Revert writes the record's start (a trigger's start is false — ResetTrigger) · an animator not initialized (its object
    // inactive) is neither read nor written — the engine drops those calls with a warning · a parameter put off at the
    // prepare is judged at the first run on the initialized animator, and not written when it is not there
    internal sealed class AnimatorParameterBinding : ItemBinding
    {
        readonly AnimatorParameterItem m_Item;
        Animator m_Animator;
        bool m_Later;
        bool m_Lacks;
        PropertyLayerRegistry m_Layers;
        AnimatorBoolSlot m_Bool;
        AnimatorIntSlot m_Int;
        AnimatorFloatSlot m_Float;
        int m_Reserved;
        long[] m_Serials;
        bool[] m_HasStart;
        float[] m_Starts;
        float m_Origin;
        float m_First;
        bool m_HasOrigin;
        bool m_HasFirst;

        internal AnimatorParameterBinding(AnimatorParameterItem item) => m_Item = item;

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
            Animator animator = TargetResolver.Find<Animator>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (animator == null)
                return Refuse(TargetResolver.Describe(failure));
            bool later = false;
            string why = AnimatorPrepare.Check(animator) ?? AnimatorPrepare.Parameter(animator, m_Item.m_Parameter, m_Item.m_Kind, out _, out later);
            if (why != null)
                return Refuse(why);
            m_Animator = animator;
            m_Later = later;
            m_Layers = context.Context.Layers;
            int h = Animator.StringToHash(m_Item.m_Parameter);
            switch (m_Item.m_Kind)
            {
                case AnimatorParameterKind.Int:
                    m_Int = m_Layers.GetOrCreate(animator, h, 0, () => new AnimatorIntSlot(animator, h));
                    m_Int.Reserve(context.MaxOverlap);
                    break;
                case AnimatorParameterKind.Float:
                    m_Float = m_Layers.GetOrCreate(animator, h, 0, () => new AnimatorFloatSlot(animator, h, later));
                    break;
                default:
                    bool trigger = m_Item.m_Kind == AnimatorParameterKind.Trigger;
                    m_Bool = m_Layers.GetOrCreate(animator, h, 0, () => new AnimatorBoolSlot(animator, h, trigger));
                    m_Bool.Reserve(context.MaxOverlap);
                    break;
            }
            m_Reserved = context.MaxOverlap;
            int n = context.MaxOverlap;
            m_Serials = new long[n];
            m_HasStart = new bool[n];
            m_Starts = new float[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        LayerSlot Slot => m_Bool != null ? m_Bool : m_Int != null ? m_Int : (LayerSlot)m_Float;

        bool Live => Slot != null && Slot.IsAlive && !Slot.Detached;

        internal override bool IsAlive => Live;

        float Read() => m_Bool != null ? (m_Bool.Read() ? 1f : 0f) : m_Int != null ? m_Int.Read() : m_Float.ReadValue();

        void Write(float v)
        {
            if (m_Bool != null) m_Bool.Write(v > 0.5f);
            else if (m_Int != null) m_Int.Write(Mathf.RoundToInt(v));
            else m_Float.WriteValue(v);
        }

        float Pick(float current, uint seed)
        {
            switch (m_Item.m_Kind)
            {
                case AnimatorParameterKind.Trigger:
                    return 1f;
                case AnimatorParameterKind.Bool:
                    return m_Item.m_Pick == AnimatorValuePick.Random ? (AnimatorPrepare.Unit(seed) < 0.5f ? 1f : 0f) : (m_Item.m_Bool ? 1f : 0f);
                case AnimatorParameterKind.Int:
                    if (m_Item.m_Pick == AnimatorValuePick.Random)
                    {
                        int lo = Mathf.RoundToInt(m_Item.m_Min), hi = Mathf.RoundToInt(m_Item.m_Max);
                        return lo + Mathf.Min(hi - lo, Mathf.FloorToInt(AnimatorPrepare.Unit(seed) * (hi - lo + 1)));
                    }
                    return m_Item.m_Pick == AnimatorValuePick.Increment ? current + Mathf.RoundToInt(m_Item.m_Step) : m_Item.m_Int;
                default:
                    if (m_Item.m_Pick == AnimatorValuePick.Random)
                        return m_Item.m_Min + AnimatorPrepare.Unit(seed) * (m_Item.m_Max - m_Item.m_Min);
                    return m_Item.m_Pick == AnimatorValuePick.Increment ? current + m_Item.m_Step : m_Item.m_Float;
            }
        }

        internal override void Execute(int record, in BeginContext context)
        {
            bool fresh = m_Serials[record] != context.Serial || !m_HasStart[record];
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_HasStart[record] = false;
            }
            if (!Live || !m_Animator.isInitialized || !Judged())
                return;
            if (!m_HasStart[record])
            {
                if (m_Bool != null) m_Bool.Touch();
                else if (m_Int != null) m_Int.Touch();
                m_Starts[record] = Read();
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
                    Write(m_Origin);
                return;
            }
            Write(Pick(Read(), context.ItemSeed));
        }

        // the judgment put off at the prepare, made once the animator is initialized
        bool Judged()
        {
            if (m_Later)
            {
                m_Later = false;
                m_Lacks = !AnimatorPrepare.Has(m_Animator, Animator.StringToHash(m_Item.m_Parameter), AnimatorPrepare.TypeOf(m_Item.m_Kind));
            }
            return !m_Lacks;
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
            if (reason == EndReason.Revert && m_HasStart[record] && Live && m_Animator.isInitialized)
                Write(m_Starts[record]);
        }

        internal override void Restore()
        {
            if (m_HasFirst && Live && m_Animator.isInitialized)
                Write(m_First);
            m_HasOrigin = false;
            m_HasFirst = false;
        }

        internal override void Release()
        {
            if (Slot == null)
                return;
            m_Layers.Release(Slot, m_Bool != null || m_Int != null ? m_Reserved : 0);
            m_Bool = null;
            m_Int = null;
            m_Float = null;
        }
    }

    // Float Parameter · Speed · Layer Weight — the value form (Light Range's)
    internal sealed class AnimatorFloatBinding : ValueTargetBinding<float, FloatOps>
    {
        internal AnimatorFloatBinding(AnimatorFloatItem item) : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Animator animator = TargetResolver.Find<Animator>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (animator == null)
                return Refuse(TargetResolver.Describe(failure));
            string name = ((AnimatorFloatItem)m_Item).m_Parameter;
            int hash = 0;
            bool later = false;
            string why = AnimatorPrepare.Check(animator) ?? AnimatorPrepare.Parameter(animator, name, AnimatorControllerParameterType.Float, out hash, out later);
            if (why != null)
                return Refuse(why);
            slot = direct ? Layers.CreatePrivate(animator, hash, 0, () => new AnimatorFloatSlot(animator, hash, later))
                : Layers.GetOrCreate(animator, hash, 0, () => new AnimatorFloatSlot(animator, hash, later));
            return true;
        }
    }

    internal sealed class AnimatorSpeedBinding : ValueTargetBinding<float, FloatOps>
    {
        internal AnimatorSpeedBinding(AnimatorSpeedItem item) : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Animator animator = TargetResolver.Find<Animator>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (animator == null)
                return Refuse(TargetResolver.Describe(failure));
            string why = AnimatorPrepare.Check(animator);
            if (why != null)
                return Refuse(why);
            slot = direct ? Layers.CreatePrivate(animator, 0, 0, () => new AnimatorSpeedSlot(animator))
                : Layers.GetOrCreate(animator, 0, 0, () => new AnimatorSpeedSlot(animator));
            return true;
        }
    }

    internal sealed class AnimatorLayerWeightBinding : ValueTargetBinding<float, FloatOps>
    {
        internal AnimatorLayerWeightBinding(AnimatorLayerWeightItem item) : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            Animator animator = TargetResolver.Find<Animator>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (animator == null)
                return Refuse(TargetResolver.Describe(failure));
            string why = AnimatorPrepare.Check(animator);
            if (why != null)
                return Refuse(why);
            // a layer by name needs an initialized animator — the slot's key is the layer (an inactive one reads −1)
            if (!animator.isInitialized)
                return Refuse("the Animator is not initialized (inactive)");
            int layer = animator.GetLayerIndex(((AnimatorLayerWeightItem)m_Item).m_Layer);
            if (layer < 0)
                return Refuse("no layer '" + ((AnimatorLayerWeightItem)m_Item).m_Layer + "'");
            slot = direct ? Layers.CreatePrivate(animator, layer, 0, () => new AnimatorLayerWeightSlot(animator, layer))
                : Layers.GetOrCreate(animator, layer, 0, () => new AnimatorLayerWeightSlot(animator, layer));
            return true;
        }
    }

    // Play State — transition 0 plays (fixed time) · else a fixed-time CrossFade · the offset in seconds ·
    // Revert writes the layer's start state (a Play cuts a transition)
    internal sealed class AnimatorStateBinding : ItemBinding
    {
        readonly AnimatorStateItem m_Item;
        PropertyLayerRegistry m_Layers;
        Animator m_Animator;
        AnimatorStateSlot m_Slot;
        int m_Hash;
        int m_Reserved;
        long[] m_Serials;
        bool[] m_HasStart;
        AnimatorPlace[] m_Starts;
        AnimatorPlace m_Origin;
        AnimatorPlace m_First;
        bool m_HasOrigin;
        bool m_HasFirst;

        internal AnimatorStateBinding(AnimatorStateItem item) => m_Item = item;

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
            m_Animator = TargetResolver.Find<Animator>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (m_Animator == null)
                return Refuse(TargetResolver.Describe(failure));
            string why = AnimatorPrepare.Check(m_Animator);
            if (why != null)
                return Refuse(why);
            m_Layers = context.Context.Layers;
            m_Hash = Animator.StringToHash(m_Item.m_State);
            m_Reserved = context.MaxOverlap;
            int n = context.MaxOverlap;
            m_Serials = new long[n];
            m_HasStart = new bool[n];
            m_Starts = new AnimatorPlace[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            if (m_Animator.isInitialized)
                return Resolve() || Refuse(m_Why);
            return true;
        }

        string m_Why;

        // the layer and the state — at the prepare, or at the first play of an animator not yet initialized
        bool Resolve()
        {
            int layer = -1;
            if (!string.IsNullOrEmpty(m_Item.m_Layer))
            {
                layer = m_Animator.GetLayerIndex(m_Item.m_Layer);
                if (layer < 0)
                {
                    m_Why = "no layer '" + m_Item.m_Layer + "'";
                    return false;
                }
                if (!m_Animator.HasState(layer, m_Hash))
                {
                    m_Why = "no state '" + m_Item.m_State + "' on " + m_Item.m_Layer;
                    return false;
                }
            }
            else
            {
                for (int i = 0; i < m_Animator.layerCount && layer < 0; i++)
                    if (m_Animator.HasState(i, m_Hash))
                        layer = i;
                if (layer < 0)
                {
                    m_Why = "no state '" + m_Item.m_State + "'";
                    return false;
                }
            }
            int l = layer;
            Animator a = m_Animator;
            m_Slot = m_Layers.GetOrCreate(a, l, 0, () => new AnimatorStateSlot(a, l));
            m_Slot.Reserve(m_Reserved);
            return true;
        }

        bool Live => m_Slot != null && m_Slot.IsAlive && !m_Slot.Detached;

        internal override bool IsAlive => m_Animator != null;

        internal override void Execute(int record, in BeginContext context)
        {
            if (m_Slot == null && (m_Animator == null || !m_Animator.isInitialized || !Resolve()))
                return;
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
                m_Slot.Touch();
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
                    m_Slot.Write(m_Origin);
                return;
            }
            m_Slot.Play(m_Hash, m_Item.m_Transition, m_Item.m_Offset);
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
                m_Slot.Write(m_Starts[record]);
        }

        internal override void Restore()
        {
            if (m_HasFirst && Live)
                m_Slot.Write(m_First);
            m_HasOrigin = false;
            m_HasFirst = false;
        }

        internal override void Release()
        {
            if (m_Slot == null)
                return;
            m_Layers.Release(m_Slot, m_Reserved);
            m_Slot = null;
        }
    }
}
