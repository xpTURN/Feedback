using System;
using UnityEngine;
using UnityEngine.Events;

namespace xpTURN.Feedback
{
    // a user's IFeedbackTarget<T> — the slot is keyed by the component (a direct write gets its own)
    internal static class CustomTargets
    {
        internal static bool Acquire<T, TOps, TSlot>(PropertyLayerRegistry layers, TargetItem item, in PrepareContext context, bool direct, Func<Component, IFeedbackTarget<T>, TSlot> make,
            out ValueSlot<T, TOps> slot, out string why)
            where TOps : struct, IValueOps<T> where TSlot : ValueSlot<T, TOps>
        {
            slot = null;
            IFeedbackTarget<T> target = TargetResolver.FindTarget<IFeedbackTarget<T>>(in item.m_Target, in context, out TargetFailure failure);
            if (target == null)
            {
                why = failure == TargetFailure.NoComponent ? "no IFeedbackTarget<" + (typeof(T) == typeof(float) ? "float" : typeof(T).Name) + "> on the target" : TargetResolver.Describe(failure);
                return false;
            }
            var owner = (Component)target;
            slot = direct ? layers.CreatePrivate(owner, 0, 0, () => make(owner, target))
                : layers.GetOrCreate(owner, 0, 0, () => make(owner, target));
            why = null;
            return true;
        }
    }

    internal sealed class CustomFloatBinding : ValueTargetBinding<float, FloatOps>
    {
        internal CustomFloatBinding(CustomFloatItem item) : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
            => CustomTargets.Acquire(Layers, m_Item, in context, direct, (o, t) => new CustomFloatSlot(o, t), out slot, out string why) || Refuse(why);
    }

    internal sealed class CustomVector3Binding : ValueTargetBinding<Vector3, Vector3Ops>
    {
        internal CustomVector3Binding(CustomVector3Item item) : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector3, Vector3Ops> slot)
            => CustomTargets.Acquire(Layers, m_Item, in context, direct, (o, t) => new CustomVector3Slot(o, t), out slot, out string why) || Refuse(why);
    }

    internal sealed class CustomColorBinding : ValueTargetBinding<Color, ColorOps>
    {
        internal CustomColorBinding(CustomColorItem item) : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Color, ColorOps> slot)
            => CustomTargets.Acquire(Layers, m_Item, in context, direct, (o, t) => new CustomColorSlot(o, t), out slot, out string why) || Refuse(why);
    }

    // no slot — the motion's value on m_Base (the 'now' value: a Tween To goes from it, an offset motion rides on it) invoked
    // every frame its play runs. Revert · Restore invoke m_Base once. A preview with the field off counts one skip per occurrence
    // (the notice's count is occurrences, not frames); a throwing listener is logged once an occurrence
    internal sealed class CustomFloatEventBinding : ItemBinding
    {
        readonly CustomFloatEventItem m_Item;
        FeedbackContext m_Context;
        MotionEvaluator m_Evaluator;
        MotionWrite m_Write;
        CategoryGate m_Gate;
        FeedbackCategory m_Category;
        MotionState[] m_States;
        bool[] m_Started;
        bool[] m_Logged;
        bool m_Invoked;

        internal CustomFloatEventBinding(CustomFloatEventItem item)
        {
            m_Item = item;
        }

        // observation
        internal bool Skips => m_Context.Kind == ContextKind.Preview && !m_Item.m_InvokeInPreview;

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
            m_Evaluator = MotionEvaluators.Create(motion, 1);
            if (m_Evaluator == null)
                return Refuse("an unknown motion");
            m_Write = m_Evaluator.Write;
            m_Context = context.Context;
            m_Gate = context.Context.Categories;
            m_Category = m_Item.EffectiveCategory;
            m_States = new MotionState[context.MaxOverlap];
            m_Started = new bool[context.MaxOverlap];
            m_Logged = new bool[context.MaxOverlap];
            return true;
        }

        internal override void Begin(int record, in BeginContext context)
        {
            uint seed = Hash32.Tagged(context.ItemSeed, Hash32.Occurrence, Hash32.Fold(context.Occurrence));
            m_Evaluator.Begin(ref m_States[record], new Vector4(m_Item.m_Base, 0f, 0f, 0f), new MotionBegin { Seed = seed, Intensity = context.Intensity });
            m_Started[record] = true;
            m_Logged[record] = false;
            if (Skips)
                m_Context.SkipUnityEvent(m_Item.m_Event, "Float Event");
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
            if (!m_Started[record] || Skips)
                return;
            m_Evaluator.Evaluate(in m_States[record], in time, out MotionSample sample);
            float value = m_Write == MotionWrite.Offset ? m_Item.m_Base + sample.Value.x * m_Gate.Scale(m_Category) : sample.Value.x;
            Invoke(value, record);
        }

        internal override void Execute(int record, in BeginContext context)
        {
            if (Skips)
            {
                m_Context.SkipUnityEvent(m_Item.m_Event, "Float Event");
                return;
            }
            uint seed = Hash32.Tagged(context.ItemSeed, Hash32.Occurrence, Hash32.Fold(context.Occurrence));
            m_Logged[record] = false;
            m_Evaluator.Begin(ref m_States[record], new Vector4(m_Item.m_Base, 0f, 0f, 0f), new MotionBegin { Seed = seed, Intensity = context.Intensity });
            m_Evaluator.Evaluate(in m_States[record], new MotionTime(context.Reverse ? 0f : 1f, 0.0), out MotionSample sample);
            Invoke(m_Write == MotionWrite.Offset ? m_Item.m_Base + sample.Value.x * m_Gate.Scale(m_Category) : sample.Value.x, record);
        }

        internal override void End(int record, EndReason reason)
        {
            m_Started[record] = false;
            if (reason == EndReason.Revert && m_Invoked && !Skips)
                Invoke(m_Item.m_Base, -1);
        }

        internal override void Restore()
        {
            if (m_Invoked && !Skips)
                Invoke(m_Item.m_Base, -1);
            m_Invoked = false;
        }

        // a record's call logs its occurrence's first exception (the later calls are made all the same) · a revert's call (−1) always
        void Invoke(float value, int record)
        {
            UnityEvent<float> e = m_Item.m_Event;
            if (e == null)
                return;
            m_Invoked = true;
            try
            {
                e.Invoke(value);
            }
            catch (Exception x)
            {
                if (record < 0 || !m_Logged[record])
                    m_Context.LogException(x);
                if (record >= 0)
                    m_Logged[record] = true;
            }
        }
    }
}
