using UnityEngine;

namespace xpTURN.Feedback
{
    // the context's time scale as a value — the slot holds one open request of the service and reads its scale back (the
    // game's write is the service's base, outside this value). Scale 1 holds no request: an open request at 1 would keep the
    // service owning (the delta-time values pinned) after every play ended
    [NotRecorded("the time scale is the service's — edit mode writes no time value")]
    internal sealed class TimeScaleSlot : ValueSlot<float, FloatOps>
    {
        readonly TimeScaleService m_Service;
        TimeScaleHandle m_Handle;

        internal TimeScaleSlot(TimeScaleService service) : base(ConsumptionStage.Update, Composition.Multiplicative, false) => m_Service = service;

        // observation
        internal TimeScaleHandle Handle => m_Handle;

        internal override bool IsAlive => true;

        protected override float Read() => m_Service.OpenScaleOf(m_Handle);

        protected override void Write(in float value)
        {
            if (value == 1f)
            {
                Let();
                return;
            }
            if (!m_Service.IsAlive(m_Handle))
                m_Handle = m_Service.Open();
            m_Service.SetScale(m_Handle, value);
        }

        void Let()
        {
            if (m_Handle.IsValid)
                m_Service.Release(m_Handle);
            m_Handle = default;
        }

        internal override void Dropped() => Let();
    }

    // a user's IFeedbackTarget<T> — the getter is the base, the setter the composed value. The stage is the target's
    // (IFeedbackTargetStage, read once — Update without it). Nothing serialized for the recorder: a preview keeps the base
    // in a save state (the editor's maker for the three types). One sealed slot per form
    internal static class CustomSlots
    {
        internal static ConsumptionStage StageOf(object target)
            => target is IFeedbackTargetStage s && s.Stage == FeedbackTargetStage.LateUpdate ? ConsumptionStage.LateApply : ConsumptionStage.Update;
    }

    [NotRecorded("a user's value — the preview's save state puts the base back")]
    internal sealed class CustomFloatSlot : ValueSlot<float, FloatOps>
    {
        readonly Component m_Owner;
        readonly IFeedbackTarget<float> m_Target;

        internal CustomFloatSlot(Component owner, IFeedbackTarget<float> target) : base(CustomSlots.StageOf(target), Composition.Additive, false)
        {
            m_Owner = owner;
            m_Target = target;
        }

        internal Component Owner => m_Owner;
        internal override bool IsAlive => m_Owner != null;
        protected override float Read() => m_Target.FeedbackValue;
        protected override void Write(in float value) => m_Target.FeedbackValue = value;
    }

    [NotRecorded("a user's value — the preview's save state puts the base back")]
    internal sealed class CustomVector3Slot : ValueSlot<Vector3, Vector3Ops>
    {
        readonly Component m_Owner;
        readonly IFeedbackTarget<Vector3> m_Target;

        internal CustomVector3Slot(Component owner, IFeedbackTarget<Vector3> target) : base(CustomSlots.StageOf(target), Composition.Additive, false)
        {
            m_Owner = owner;
            m_Target = target;
        }

        internal Component Owner => m_Owner;
        internal override bool IsAlive => m_Owner != null;
        protected override Vector3 Read() => m_Target.FeedbackValue;
        protected override void Write(in Vector3 value) => m_Target.FeedbackValue = value;
    }

    [NotRecorded("a user's value — the preview's save state puts the base back")]
    internal sealed class CustomColorSlot : ValueSlot<Color, ColorOps>
    {
        readonly Component m_Owner;
        readonly IFeedbackTarget<Color> m_Target;

        internal CustomColorSlot(Component owner, IFeedbackTarget<Color> target) : base(CustomSlots.StageOf(target), Composition.Additive, false)
        {
            m_Owner = owner;
            m_Target = target;
        }

        internal Component Owner => m_Owner;
        internal override bool IsAlive => m_Owner != null;
        protected override Color Read() => m_Target.FeedbackValue;
        protected override void Write(in Color value) => m_Target.FeedbackValue = value;
    }
}
