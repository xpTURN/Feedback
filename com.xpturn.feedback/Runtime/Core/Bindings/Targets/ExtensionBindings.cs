using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    internal static class ExtensionEnds
    {
        internal static CustomEnd Of(EndReason reason)
        {
            switch (reason)
            {
                case EndReason.Completed:
                    return CustomEnd.Completed;
                case EndReason.Hold:
                    return CustomEnd.Hold;
                case EndReason.Revert:
                    return CustomEnd.Revert;
                case EndReason.Complete:
                    return CustomEnd.Complete;
                default:
                    return CustomEnd.Aborted;
            }
        }
    }

    // the debugger's notes on a user's item — a slot a throw stopped, a preview that does not call the type
    internal static class ExtensionNotes
    {
        internal const string Faulted = "faulted";
        internal const string NotInPreview = "not called in the preview";

        internal static string Of(bool skips, bool faulted) => skips ? NotInPreview : faulted ? Faulted : null;
    }

    // a user's action through its CustomAction. The user's calls are caught and logged — the play goes on; a slot whose Begin or
    // Update threw gets no more Update in that occurrence, its End still comes. Prepare's exception goes to the runtime (logged,
    // then Release). A preview the type does not run in calls nothing and counts the skipped occurrences
    internal sealed class CustomActionBinding : ItemBinding, IExtensionTargetSink
    {
        readonly CustomActionItem m_Item;
        CustomAction m_Action;
        FeedbackContext m_Context;
        CategoryGate m_Gate;
        FeedbackCategory m_Category;
        bool m_Skips;
        bool[] m_Started;
        bool[] m_Faulted;
        UnityEngine.Object m_Target;
        bool m_HasTarget;

        internal CustomActionBinding(CustomActionItem item)
        {
            m_Item = item;
        }

        // observation
        internal CustomAction Action => m_Action;
        internal bool Skips => m_Skips;

        void IExtensionTargetSink.Found(UnityEngine.Object target)
        {
            m_Target = target;
            m_HasTarget = true;
        }

        internal override bool IsAlive => !m_HasTarget || m_Target != null;

        internal override string DebugNote(int record) => ExtensionNotes.Of(m_Skips, m_Faulted[record]);

        internal override bool Prepare(in PrepareContext context)
        {
            m_Context = context.Context;
            m_Gate = context.Context.Categories;
            m_Category = m_Item.EffectiveCategory;
            m_Started = new bool[context.MaxOverlap];
            m_Faulted = new bool[context.MaxOverlap];
            if (context.Context.Kind == ContextKind.Preview)
            {
                m_Skips = ExtensionTraits.Preview(m_Item.GetType()) == CustomPreview.Skip;
                if (m_Skips)
                    return true;
                m_Context.RanCustom++;
            }
            m_Action = m_Item.CreateAction();
            if (m_Action == null)
                return Refuse("CreateAction returned null");
            return m_Action.Prepare(new CustomPrepare(in context, in m_Item.m_Target, this)) || Refuse("its Prepare returned false");
        }

        internal override void Execute(int record, in BeginContext context)
        {
            if (m_Skips)
            {
                m_Context.SkippedCustom++;
                return;
            }
            var play = new CustomPlay(record, in context, m_Gate.Scale(m_Category));
            try
            {
                if (context.Reverse)
                    m_Action.Undo(in play);
                else
                    m_Action.Fire(in play);
            }
            catch (Exception e)
            {
                m_Context.LogException(e);
            }
        }

        internal override void Begin(int record, in BeginContext context)
        {
            if (m_Skips)
            {
                m_Context.SkippedCustom++;
                return;
            }
            m_Started[record] = true;
            m_Faulted[record] = false;
            var play = new CustomPlay(record, in context, m_Gate.Scale(m_Category));
            try
            {
                m_Action.Begin(in play);
            }
            catch (Exception e)
            {
                Fault(record, e);
            }
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
            if (m_Skips || !m_Started[record] || m_Faulted[record])
                return;
            try
            {
                m_Action.Update(new CustomTick(record, in time, m_Gate.Scale(m_Category)));
            }
            catch (Exception e)
            {
                Fault(record, e);
            }
        }

        // a timed act's end, and the Revert an executed instant hears
        internal override void End(int record, EndReason reason)
        {
            if (m_Skips || m_Action == null)
                return;
            m_Started[record] = false;
            try
            {
                m_Action.End(record, ExtensionEnds.Of(reason));
            }
            catch (Exception e)
            {
                Fault(record, e);
            }
        }

        internal override void Restore()
        {
            if (m_Skips || m_Action == null)
                return;
            try
            {
                m_Action.Restore();
            }
            catch (Exception e)
            {
                m_Context.LogException(e);
            }
        }

        // after a Prepare that returned false or threw too — only what CreateAction made
        internal override void Release()
        {
            CustomAction action = m_Action;
            m_Action = null;
            if (action == null)
                return;
            try
            {
                action.Release();
            }
            catch (Exception e)
            {
                m_Context.LogException(e);
            }
        }

        void Fault(int record, Exception e)
        {
            if (!m_Faulted[record])
                m_Context.LogException(e);
            m_Faulted[record] = true;
        }
    }

    // a user's effect through its CustomEffect — the value is a Custom Float Event's (the motion on the item's base, an offset's
    // value times the category's scale). A Revert and a Restore after an Apply put the base back (slot −1). A Haptics effect
    // stops when its category is turned off: the base once, then nothing until the next Begin
    internal sealed class CustomEffectBinding : ItemBinding, IExtensionTargetSink
    {
        readonly CustomEffectItem m_Item;
        CustomEffect m_Effect;
        FeedbackContext m_Context;
        MotionEvaluator m_Evaluator;
        MotionWrite m_Write;
        CategoryGate m_Gate;
        FeedbackCategory m_Category;
        MotionState[] m_States;
        bool[] m_Started;
        bool[] m_Faulted;
        bool[] m_Muted;
        bool m_Applied;
        bool m_Skips;
        UnityEngine.Object m_Target;
        bool m_HasTarget;

        internal CustomEffectBinding(CustomEffectItem item)
        {
            m_Item = item;
        }

        // observation
        internal CustomEffect Effect => m_Effect;
        internal bool Skips => m_Skips;

        void IExtensionTargetSink.Found(UnityEngine.Object target)
        {
            m_Target = target;
            m_HasTarget = true;
        }

        internal override bool IsAlive => !m_HasTarget || m_Target != null;

        internal override string DebugNote(int record) => ExtensionNotes.Of(m_Skips, m_Faulted[record]);

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
            m_Faulted = new bool[context.MaxOverlap];
            m_Muted = new bool[context.MaxOverlap];
            if (context.Context.Kind == ContextKind.Preview)
            {
                m_Skips = ExtensionTraits.Preview(m_Item.GetType()) == CustomPreview.Skip;
                if (m_Skips)
                    return true;
                m_Context.RanCustom++;
            }
            m_Effect = m_Item.CreateEffect();
            if (m_Effect == null)
                return Refuse("CreateEffect returned null");
            return m_Effect.Prepare(new CustomPrepare(in context, in m_Item.m_Target, this)) || Refuse("its Prepare returned false");
        }

        float Value(in MotionSample sample) => m_Write == MotionWrite.Offset ? m_Item.m_Base + sample.Value.x * m_Gate.Scale(m_Category) : sample.Value.x;

        // a Haptics effect whose category is off — the base once for the slot, then nothing until its next Begin
        bool Muted(int record)
        {
            if (m_Category != FeedbackCategory.Haptics || m_Gate.IsEnabled(m_Category))
                return false;
            if (!m_Muted[record])
            {
                m_Muted[record] = true;
                Apply(record, m_Item.m_Base);
            }
            return true;
        }

        internal override void Begin(int record, in BeginContext context)
        {
            if (m_Skips)
            {
                m_Context.SkippedCustom++;
                return;
            }
            uint seed = Hash32.Tagged(context.ItemSeed, Hash32.Occurrence, Hash32.Fold(context.Occurrence));
            m_Evaluator.Begin(ref m_States[record], new Vector4(m_Item.m_Base, 0f, 0f, 0f), new MotionBegin { Seed = seed, Intensity = context.Intensity });
            m_Started[record] = true;
            m_Faulted[record] = false;
            m_Muted[record] = false;
            var play = new CustomPlay(record, in context, m_Gate.Scale(m_Category));
            try
            {
                m_Effect.Begin(in play);
            }
            catch (Exception e)
            {
                Fault(record, e);
            }
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
            if (m_Skips || !m_Started[record] || m_Faulted[record] || Muted(record))
                return;
            m_Evaluator.Evaluate(in m_States[record], in time, out MotionSample sample);
            Apply(record, Value(in sample));
        }

        // an instant — the motion's end (its start backward)
        internal override void Execute(int record, in BeginContext context)
        {
            if (m_Skips)
            {
                m_Context.SkippedCustom++;
                return;
            }
            m_Faulted[record] = false;
            m_Muted[record] = false;
            if (Muted(record))
                return;
            uint seed = Hash32.Tagged(context.ItemSeed, Hash32.Occurrence, Hash32.Fold(context.Occurrence));
            m_Evaluator.Begin(ref m_States[record], new Vector4(m_Item.m_Base, 0f, 0f, 0f), new MotionBegin { Seed = seed, Intensity = context.Intensity });
            m_Evaluator.Evaluate(in m_States[record], new MotionTime(context.Reverse ? 0f : 1f, 0.0), out MotionSample sample);
            Apply(record, Value(in sample));
        }

        internal override void End(int record, EndReason reason)
        {
            if (m_Skips || m_Effect == null)
                return;
            m_Started[record] = false;
            if (reason == EndReason.Revert && m_Applied)
                Apply(-1, m_Item.m_Base);
            try
            {
                m_Effect.End(record, ExtensionEnds.Of(reason));
            }
            catch (Exception e)
            {
                Fault(record, e);
            }
        }

        internal override void Restore()
        {
            if (m_Skips || m_Effect == null)
                return;
            if (m_Applied)
                Apply(-1, m_Item.m_Base);
            m_Applied = false;
        }

        internal override void Release()
        {
            CustomEffect effect = m_Effect;
            m_Effect = null;
            if (effect == null)
                return;
            try
            {
                effect.Release();
            }
            catch (Exception e)
            {
                m_Context.LogException(e);
            }
        }

        // a slot's call that throws ends that slot's calls in its occurrence — a base call (−1) logs every time
        void Apply(int record, float value)
        {
            m_Applied = true;
            try
            {
                m_Effect.Apply(record, value);
            }
            catch (Exception e)
            {
                if (record < 0)
                    m_Context.LogException(e);
                else
                    Fault(record, e);
            }
        }

        void Fault(int record, Exception e)
        {
            if (!m_Faulted[record])
                m_Context.LogException(e);
            m_Faulted[record] = true;
        }
    }
}
