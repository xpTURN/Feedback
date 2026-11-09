using System;

namespace xpTURN.Feedback.Tests
{
    // the test target's value as a slot — the tests' record source has no rows for it, as a user's extension that came
    // without them
    internal sealed class PreviewTestSlot : ValueSlot<float, FloatOps>
    {
        readonly PreviewTestTarget m_Target;

        internal PreviewTestSlot(PreviewTestTarget target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.Value;

        protected override void Write(in float value) => m_Target.Value = value;
    }

    [Serializable]
    public sealed class PreviewTestItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Set | MotionMask.Tween | MotionMask.Shake,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    internal sealed class PreviewTestBinding : ValueTargetBinding<float, FloatOps>
    {
        internal PreviewTestBinding(PreviewTestItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            PreviewTestTarget target = TargetResolver.Find<PreviewTestTarget>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (target == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(target, 0, 0, () => new PreviewTestSlot(target));
            else
                slot = Layers.GetOrCreate(target, 0, 0, () => new PreviewTestSlot(target));
            return true;
        }

        // the registry is never cleared — registering again overwrites with the same factory
        internal static void Register() => FeedbackImplementations.Register<PreviewTestItem>(item => new PreviewTestBinding(item));
    }
}
