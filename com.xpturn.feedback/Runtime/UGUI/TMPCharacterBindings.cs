using TMPro;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the three Character items' binding — no value slot. A record's motion goes to the text's shared slot as a contribution
    // (its state and time); the slot evaluates each character where it writes. A Tween is Persist: it writes the channel's
    // kept value — its start is the kept value when it begins, its end stays (Completed), Revert puts the start back,
    // Restore the first value. A category turned off keeps a Persist record from writing; its scale multiplies the offsets
    // only (a Persist value takes none — the framework's rule)
    internal sealed class TMPCharacterBinding : ItemBinding
    {
        readonly TargetItem m_Item;
        readonly TMPCharacterChannel m_Channel;
        PropertyLayerRegistry m_Layers;
        TMPCharacterSlot m_Slot;
        MotionEvaluator m_Evaluator;
        MotionWrite m_Write;
        CategoryGate m_Gate;
        FeedbackCategory m_Category;
        float m_Stagger;
        bool m_Backward;
        float m_Frequency;
        bool m_OwnSeeds;
        int m_Reserved;
        MotionState[] m_States;
        MotionTime[] m_Times;
        int[] m_Handles;
        bool[] m_Reverse;
        Vector4[] m_Starts;
        long[] m_Serials;
        bool[] m_HasStart;
        Vector4 m_First;
        bool m_HasFirst;

        internal TMPCharacterBinding(TargetItem item, TMPCharacterChannel channel)
        {
            m_Item = item;
            m_Channel = channel;
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
            TMP_Text text = TargetResolver.Find<TMP_Text>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (text == null)
                return Refuse(TargetResolver.Describe(failure));
            if (!FloatingTextBinding.s_TmpResources())
                return Refuse(FloatingTextBinding.NoTmpResources);
            m_Evaluator = MotionEvaluators.Create(motion, m_Channel == TMPCharacterChannel.Offset ? 2 : 1);
            if (m_Evaluator == null)
                return Refuse("an unknown motion");
            switch (m_Item)
            {
                case TMPCharacterOffsetItem o:
                    m_Stagger = o.m_Stagger;
                    m_Backward = o.m_Order == TMPCharacterOrder.Backward;
                    break;
                case TMPCharacterScaleItem s:
                    m_Stagger = s.m_Stagger;
                    m_Backward = s.m_Order == TMPCharacterOrder.Backward;
                    break;
                case TMPCharacterAlphaItem a:
                    m_Stagger = a.m_Stagger;
                    m_Backward = a.m_Order == TMPCharacterOrder.Backward;
                    break;
            }
            m_Write = m_Evaluator.Write;
            m_Frequency = motion is OscillateMotion oscillate ? oscillate.m_Params.m_Frequency : 0f;
            m_OwnSeeds = motion is WiggleMotion;
            // a text never drawn has no mesh yet — a UGUI text under no canvas gets none at all
            if (text.textInfo == null || text.textInfo.characterCount == 0)
                text.ForceMeshUpdate();
            m_Layers = context.Context.Layers;
            m_Gate = context.Context.Categories;
            m_Category = m_Item.EffectiveCategory;
            m_Slot = m_Layers.GetOrCreate(text, 0, 0, () => new TMPCharacterSlot(text));
            int n = context.MaxOverlap;
            m_Slot.Reserve(n);
            m_Reserved = n;
            m_States = new MotionState[n];
            m_Times = new MotionTime[n];
            m_Handles = new int[n];
            m_Reverse = new bool[n];
            m_Starts = new Vector4[n];
            m_Serials = new long[n];
            m_HasStart = new bool[n];
            for (int i = 0; i < n; i++)
                m_Handles[i] = -1;
            return true;
        }

        internal override bool IsAlive => m_Slot != null && m_Slot.IsAlive;

        internal override void Begin(int record, in BeginContext context)
        {
            if (m_Handles[record] >= 0)
                m_Slot.End(m_Handles[record]);
            Vector4 start = Vector4.zero;
            if (m_Write == MotionWrite.Persist)
            {
                Vector4 now = m_Slot.Kept(m_Channel);
                if (!m_HasFirst)
                {
                    m_First = now;
                    m_HasFirst = true;
                }
                if (!m_HasStart[record] || m_Serials[record] != context.Serial)
                {
                    m_Starts[record] = now;
                    m_HasStart[record] = true;
                    m_Serials[record] = context.Serial;
                }
                start = now;
            }
            m_Reverse[record] = context.Reverse;
            uint seed = Hash32.Tagged(context.ItemSeed, Hash32.Occurrence, Hash32.Fold(context.Occurrence));
            m_Evaluator.Begin(ref m_States[record], start, new MotionBegin { Seed = seed, Intensity = context.Intensity });
            m_Handles[record] = m_Slot.Open(m_Channel, m_Write, m_Evaluator, m_Stagger, m_Backward, m_Frequency, m_OwnSeeds);
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
            int h = m_Handles[record];
            if (h < 0)
                return;
            m_Times[record] = time;
            m_Slot.Set(h, in m_States[record], in time, m_Gate.Scale(m_Category));
        }

        internal override void End(int record, EndReason reason)
        {
            int h = m_Handles[record];
            m_Handles[record] = -1;
            // a Revert comes to the records that ended earlier in the play too (ItemBinding.End)
            if (m_Write == MotionWrite.Persist && m_Slot != null)
            {
                if (reason == EndReason.Revert)
                {
                    if (m_HasStart[record])
                        m_Slot.Keep(m_Channel, m_Starts[record]);
                }
                else if (h >= 0 && m_Gate.Scale(m_Category) > 0f)
                {
                    // Completed · Complete: the end · Hold · Aborted: the item's value where it stopped — one value for every character
                    float t = reason == EndReason.Completed || reason == EndReason.Complete ? (m_Reverse[record] ? 0f : 1f) : m_Times[record].T;
                    m_Slot.Keep(m_Channel, At(record, t));
                }
            }
            if (h >= 0)
                m_Slot?.End(h);
        }

        Vector4 At(int record, float t)
        {
            m_Evaluator.Evaluate(in m_States[record], new MotionTime(t, m_Times[record].Seconds), out MotionSample sample);
            return sample.Value;
        }

        internal override bool ApplyInitial(in MotionTime time, float intensity)
        {
            if (m_Write != MotionWrite.Persist || m_Slot == null)
                return false;
            Vector4 now = m_Slot.Kept(m_Channel);
            if (!m_HasFirst)
            {
                m_First = now;
                m_HasFirst = true;
            }
            var state = default(MotionState);
            m_Evaluator.Begin(ref state, now, new MotionBegin { Seed = 0, Intensity = intensity });
            m_Evaluator.Evaluate(in state, in time, out MotionSample sample);
            m_Slot.Keep(m_Channel, sample.Value);
            return true;
        }

        internal override void Restore()
        {
            if (m_Write == MotionWrite.Persist && m_HasFirst && m_Slot != null)
                m_Slot.Keep(m_Channel, m_First);
            m_HasFirst = false;
            if (m_HasStart != null)
                for (int i = 0; i < m_HasStart.Length; i++)
                    m_HasStart[i] = false;
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
