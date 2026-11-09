using UnityEngine;

namespace xpTURN.Feedback
{
    // the animator's runtime values — nothing serialized (the preview's simulation puts the animator back at its end —
    // its speed written back, then Rebind) · the Update stage (the animators evaluate at PreLateUpdate 01 ~ 03). The
    // judgment on the value read back takes the engine's own (a weight is not clamped · a negative speed reads 0)
    [NotRecorded("an animator's speed — not serialized; the preview's simulation puts it back")]
    internal sealed class AnimatorSpeedSlot : ValueSlot<float, FloatOps>
    {
        readonly Animator m_Target;

        internal AnimatorSpeedSlot(Animator target) : base(ConsumptionStage.Update, Composition.Additive, false) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.speed;

        protected override void Write(in float value) => m_Target.speed = value;
    }

    [NotRecorded("an animator layer's weight — runtime state; the preview's simulation rebinds")]
    internal sealed class AnimatorLayerWeightSlot : ValueSlot<float, FloatOps>
    {
        readonly Animator m_Target;
        readonly int m_Layer;

        internal AnimatorLayerWeightSlot(Animator target, int layer) : base(ConsumptionStage.Update, Composition.Additive, false)
        {
            m_Target = target;
            m_Layer = layer;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.GetLayerWeight(m_Layer);

        protected override void Write(in float value) => m_Target.SetLayerWeight(m_Layer, value);
    }

    // an animator not initialized (its object inactive) is not written and reads the value last written — the engine drops
    // those calls with a warning, and starts from the controller's defaults once it is on. A parameter put off at the prepare
    // (later — a player's inactive object lists none) is judged when the initialized animator is first read or written, and
    // not written when the controller has no Float of that name
    [NotRecorded("an animator parameter — runtime state; the preview's simulation rebinds")]
    internal sealed class AnimatorFloatSlot : ValueSlot<float, FloatOps>
    {
        readonly Animator m_Target;
        readonly int m_Hash;
        float m_Last;
        bool m_Later;
        bool m_Lacks;

        internal AnimatorFloatSlot(Animator target, int hash, bool later) : base(ConsumptionStage.Update, Composition.Additive, false)
        {
            m_Target = target;
            m_Hash = hash;
            m_Later = later;
        }

        internal override bool IsAlive => m_Target != null;

        bool Usable()
        {
            if (!m_Target.isInitialized)
                return false;
            if (m_Later)
            {
                m_Later = false;
                m_Lacks = !AnimatorPrepare.Has(m_Target, m_Hash, AnimatorControllerParameterType.Float);
            }
            return !m_Lacks;
        }

        protected override float Read() => Usable() ? m_Target.GetFloat(m_Hash) : m_Last;

        protected override void Write(in float value)
        {
            m_Last = value;
            if (Usable())
                m_Target.SetFloat(m_Hash, value);
        }
    }

    // the state slots — Persist only · a trigger is the bool slot's other way of writing (true = SetTrigger ·
    // false = ResetTrigger — a Revert to the start clears a trigger still set)
    internal abstract class AnimatorStateSlotBase : LayerSlot
    {
        protected readonly Animator m_Target;
        int m_Reserved;

        protected AnimatorStateSlotBase(Animator target) : base(ConsumptionStage.Update) => m_Target = target;

        internal override bool IsAlive => m_Target != null;

        internal override bool Pending => false;

        internal override int LiveContributions => 0;

        internal override int Reserved => m_Reserved;

        internal override Vector4 BaseVector => Vector4.zero;

        internal Animator Target => m_Target;

        internal void Reserve(int count) => m_Reserved += count;

        internal override void Unreserve(int count) => m_Reserved -= System.Math.Min(count, m_Reserved);

        internal override void Revert()
        {
        }

        internal override void Apply()
        {
        }

        internal override void Close()
        {
        }

        internal override void EndAll()
        {
        }

        // the record before the first read — the preview's simulation learns the animator
        internal void Touch() => RecordOnce();
    }

    [NotRecorded("an animator bool or trigger — runtime state; the preview's simulation rebinds")]
    internal sealed class AnimatorBoolSlot : AnimatorStateSlotBase
    {
        readonly int m_Hash;
        readonly bool m_Trigger;

        internal AnimatorBoolSlot(Animator target, int hash, bool trigger) : base(target)
        {
            m_Hash = hash;
            m_Trigger = trigger;
        }

        internal bool Read() => m_Target.GetBool(m_Hash);

        internal void Write(bool value)
        {
            RecordOnce();
            if (!m_Trigger)
                m_Target.SetBool(m_Hash, value);
            else if (value)
                m_Target.SetTrigger(m_Hash);
            else
                m_Target.ResetTrigger(m_Hash);
        }
    }

    [NotRecorded("an animator int — runtime state; the preview's simulation rebinds")]
    internal sealed class AnimatorIntSlot : AnimatorStateSlotBase
    {
        readonly int m_Hash;

        internal AnimatorIntSlot(Animator target, int hash) : base(target) => m_Hash = hash;

        internal int Read() => m_Target.GetInteger(m_Hash);

        internal void Write(int value)
        {
            RecordOnce();
            m_Target.SetInteger(m_Hash, value);
        }
    }

    internal struct AnimatorPlace
    {
        internal int Hash;
        internal float Time;
    }

    // a layer's state — read before a Play · CrossFade, written back by a Play (it cuts a transition)
    [NotRecorded("an animator layer's state — runtime state; the preview's simulation rebinds")]
    internal sealed class AnimatorStateSlot : AnimatorStateSlotBase
    {
        readonly int m_Layer;

        internal AnimatorStateSlot(Animator target, int layer) : base(target) => m_Layer = layer;

        internal int Layer => m_Layer;

        internal AnimatorPlace Read()
        {
            AnimatorStateInfo s = m_Target.GetCurrentAnimatorStateInfo(m_Layer);
            return new AnimatorPlace { Hash = s.fullPathHash, Time = s.normalizedTime };
        }

        internal void Write(in AnimatorPlace place)
        {
            RecordOnce();
            m_Target.Play(place.Hash, m_Layer, place.Time);
        }

        internal void Play(int hash, float transition, float offset)
        {
            RecordOnce();
            if (transition <= 0f)
                m_Target.PlayInFixedTime(hash, m_Layer, offset);
            else
                m_Target.CrossFadeInFixedTime(hash, transition, m_Layer, offset);
        }
    }
}
