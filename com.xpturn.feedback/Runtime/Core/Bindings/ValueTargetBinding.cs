using UnityEngine;

namespace xpTURN.Feedback
{
    // the value targets' common part. The motion's four components become the slot's value and go the way the motion
    // writes — Offset and Override as the record's one contribution (the contribution an occurrence ended in this pass is
    // handed to the next Begin, Settle lets it go), Persist through the slot's door. Per record: the motion state, the
    // contribution and the play's start value (Revert)
    internal abstract class ValueTargetBinding<T, TOps> : ItemBinding, IFollowBinding where TOps : struct, IValueOps<T>
    {
        protected readonly TargetItem m_Item;
        PropertyLayerRegistry m_Layers;
        PrepareContext m_Context;
        MotionEvaluator m_Evaluator;
        MotionWrite m_Write;
        bool m_TowardTarget;
        ValueSlot<T, TOps> m_Slot;
        int m_Reserved;
        MotionState[] m_States;
        ContributionHandle[] m_Handles;
        bool[] m_Held;
        bool[] m_Running;
        int m_RunningCount;
        long[] m_Serials;
        T[] m_Starts;
        bool[] m_HasStart;
        // per record the occurrences' start value and the play's direction; per binding the origin (the start value of the
        // latest forward play — a backward play's start value) and the first value (before the first play that reached it —
        // Restore), with the marks the next play reads
        T[] m_From;
        bool[] m_Reverse;
        T m_Origin;
        T m_First;
        bool m_HasOrigin;
        bool m_HasFirst;
        bool m_Applied;         // the first frame wrote and no play came yet — the next play starts at the origin
        bool m_AfterReverse;    // the latest play ran backward
        bool m_ReverseCut;      // and did not run to its end
        long m_LastSerial = -1;
        bool m_Arrival;         // the motion arrives at the current value (Tween From)
        MotionState m_Initial;
        // a main camera target: the camera found at the prepare (or the last Begin)
        bool m_FollowsCamera;
        Camera m_Camera;
        // the category's scale, read at each evaluation — on the output, after the motion's clamp. Persist takes none: a
        // running Tween reaches its end with the category off
        CategoryGate m_Gate;
        FeedbackCategory m_Category;

        protected ValueTargetBinding(TargetItem item)
        {
            m_Item = item;
        }

        protected PropertyLayerRegistry Layers => m_Layers;

        protected MotionEvaluator Evaluator => m_Evaluator;

        // observation
        internal ValueSlot<T, TOps> Slot => m_Slot;
        internal uint EvaluationSeed(int record) => m_States[record].Seed;
        internal Vector4 StartValue(int record) => m_States[record].Start;
        internal T Origin => m_Origin;
        internal bool HasOrigin => m_HasOrigin;
        internal T First => m_First;
        internal bool HasFirst => m_HasFirst;

        // finds the target and gets its slot — shared, or private for a direct write. false refuses with the reason
        protected abstract bool Acquire(in PrepareContext context, bool direct, out ValueSlot<T, TOps> slot);

        // the space the offsets take
        protected virtual LayerSpace Space => LayerSpace.Local;

        // Persist and its start value in the motion's space — a World position or rotation
        protected virtual Vector4 ToMotion(in T value) => default(TOps).ToVector4(value);

        protected virtual T FromMotion(in Vector4 value) => default(TOps).FromVector4(value);

        // WeightTowardTarget: the target an offset motion's shape weighs toward
        protected virtual T TowardTarget => default(TOps).FromVector4(Vector4.zero);

        // an offset in the motion's units as the property's offset (the zoom's stops)
        protected virtual T MapOffset(in T offset) => offset;

        // an override's target in the property's units (the emission's multiplier)
        protected virtual T MapOverride(in T target) => target;

        // the components an override takes — a binding sharing a slot's value overrides its own part only (the composition's
        // other components stay)
        protected virtual Vector4 OverrideMask => Vector4.one;

        // a remembered value written back (the play's start · the first value · the origin) — a binding sharing a slot's value
        // puts its own part over the slot's other part now
        protected virtual T Stored(in T value) => value;

        // a global target reads no target — no main camera to follow
        protected virtual bool ReadsTarget => true;

        // a slot following the active scene — with nothing running, the next Begin takes the slot of the scene active then
        // when the held one is stale
        protected virtual bool Refollows => false;

        protected virtual bool SlotStale => false;

        // an occurrence starts — the play's place and its look
        protected virtual void Started(int record, in BeginContext context)
        {
        }

        // a Persist value the binding makes itself (the rotation's Shortest tween)
        protected virtual bool TryPersist(in MotionState s, in MotionTime time, out T value)
        {
            value = default;
            return false;
        }

        static int Dimension(ValueKind kind)
        {
            switch (kind)
            {
                case ValueKind.Vector2: return 2;
                case ValueKind.Vector3: return 3;
                case ValueKind.Vector4:
                case ValueKind.Color: return 4;
                default: return 1;
            }
        }

        internal sealed override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            TargetTraits traits = m_Item.Traits;
            if (motion == null)
                return Refuse("no motion");
            if (!traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            m_Evaluator = MotionEvaluators.Create(motion, Dimension(traits.ValueKind));
            if (m_Evaluator == null)
                return Refuse("an unknown motion");
            m_Write = m_Evaluator.Write;
            m_TowardTarget = traits.ApplyMode == ApplyMode.WeightTowardTarget && m_Write == MotionWrite.Offset;
            m_Arrival = motion is TweenMotion tween && tween.m_Params.m_Mode == TweenMode.From;
            m_Layers = context.Context.Layers;
            m_Context = context;
            m_Gate = context.Context.Categories;
            m_Category = m_Item.EffectiveCategory;
            m_FollowsCamera = ReadsTarget && m_Item.m_Target.m_Mode == TargetRefMode.MainCamera;
            if (m_FollowsCamera)
                m_Camera = Camera.main;
            // no main camera yet — the first Begin finds it (that play takes the slot: allocation once)
            bool waits = m_FollowsCamera && m_Camera == null;
            if (!waits && !Acquire(in context, m_Item.m_DirectWrite, out m_Slot))
                return false;
            int n = context.MaxOverlap;
            m_Slot?.Reserve(n);
            m_Reserved = n;
            m_States = new MotionState[n];
            m_Handles = new ContributionHandle[n];
            m_Held = new bool[n];
            m_Running = new bool[n];
            m_Serials = new long[n];
            m_Starts = new T[n];
            m_HasStart = new bool[n];
            m_From = new T[n];
            m_Reverse = new bool[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        ContributionKind Kind => m_Write == MotionWrite.Offset && !m_TowardTarget ? ContributionKind.Offset : ContributionKind.Override;

        bool Live => m_Slot != null && m_Slot.IsAlive && !m_Slot.Detached;

        // a main camera target · a slot following the active scene, with nothing running, finds its slot again at the next Begin
        internal override bool IsAlive => Live || ((m_FollowsCamera || Refollows) && m_RunningCount == 0);

        internal sealed override void Begin(int record, in BeginContext context)
        {
            if ((m_FollowsCamera || (Refollows && (SlotStale || !Live))) && m_RunningCount == 0)
                FollowCamera();
            if (!m_Running[record])
            {
                m_Running[record] = true;
                m_RunningCount++;
            }
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_HasStart[record] = false;
                m_Reverse[record] = context.Reverse;
            }
            Started(record, in context);
            Vector4 current = default;
            if (m_Write == MotionWrite.Persist && m_Slot != null)
            {
                if (!m_HasStart[record])
                    StartPlay(record, context.Serial);
                current = ToMotion(m_From[record]);
            }
            uint seed = Hash32.Tagged(context.ItemSeed, Hash32.Occurrence, Hash32.Fold(context.Occurrence));
            m_Evaluator.Begin(ref m_States[record], current, new MotionBegin { Seed = seed, Intensity = context.Intensity });
            if (m_Write == MotionWrite.Persist || m_Slot == null)
                return;
            if (m_Held[record])
            {
                // the pass's earlier occurrence hands its contribution over
                m_Held[record] = false;
                return;
            }
            m_Handles[record] = m_Slot.Begin(Kind, Space);
        }

        // the play's direction for its end (a cut backward play) and the next play's start — the latest play ran this way.
        // The start values and the origin stay
        internal sealed override void Turn(int record, long serial, bool reverse)
        {
            if (m_Serials == null || m_Serials[record] != serial)
                return;
            m_Reverse[record] = reverse;
            if (m_LastSerial == serial)
                m_AfterReverse = reverse;
        }

        // a play's first Begin: its start (Revert), the first value, and where its occurrences start. A backward play starts
        // at the origin — with none yet, this value is the origin. A forward play starts at the value it finds and takes it
        // for the origin — at the origin after the first frame, and when its motion arrives at the current value (From) after
        // a backward play; after a backward play that was cut a To-like motion goes on from where it was cut and keeps the
        // origin
        void StartPlay(int record, long serial)
        {
            T value = m_Slot.Base;
            m_Starts[record] = value;
            m_HasStart[record] = true;
            if (!m_HasFirst)
            {
                m_First = value;
                m_HasFirst = true;
            }
            m_LastSerial = serial;
            if (m_Reverse[record])
            {
                if (!m_HasOrigin)
                {
                    m_Origin = value;
                    m_HasOrigin = true;
                }
                m_From[record] = m_Origin;
                m_AfterReverse = true;
            }
            else if (m_HasOrigin && (m_Applied || (m_AfterReverse && m_Arrival)))
            {
                m_From[record] = m_Origin;
                m_AfterReverse = false;
            }
            else
            {
                m_From[record] = value;
                if (!(m_AfterReverse && m_ReverseCut))
                {
                    m_Origin = value;
                    m_HasOrigin = true;
                }
                m_AfterReverse = false;
            }
            m_ReverseCut = false;
            m_Applied = false;
        }

        // an instant (Set) — the play's start as Begin takes it, then the Persist value once. Backward it writes the origin —
        // nothing before a forward play
        internal sealed override void Execute(int record, in BeginContext context)
        {
            if (!Live)
                return;
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_HasStart[record] = false;
                m_Reverse[record] = context.Reverse;
            }
            if (!m_HasStart[record])
                StartPlay(record, context.Serial);
            Started(record, in context);
            if (context.Reverse)
            {
                if (m_HasOrigin)
                    m_Slot.WriteValue(Stored(m_Origin));
                return;
            }
            uint seed = Hash32.Tagged(context.ItemSeed, Hash32.Occurrence, Hash32.Fold(context.Occurrence));
            m_Evaluator.Begin(ref m_States[record], ToMotion(m_From[record]), new MotionBegin { Seed = seed, Intensity = context.Intensity });
            var time = new MotionTime(0f, 0.0);
            if (!TryPersist(in m_States[record], in time, out T value))
            {
                m_Evaluator.Evaluate(in m_States[record], in time, out MotionSample sample);
                value = FromMotion(sample.Value);
            }
            m_Slot.WriteValue(value);
        }

        internal sealed override void Evaluate(int record, in MotionTime time)
        {
            if (m_Slot == null)
                return;
            ref readonly MotionState s = ref m_States[record];
            switch (m_Write)
            {
                case MotionWrite.Offset:
                {
                    float scale = m_Gate.Scale(m_Category);
                    if (m_TowardTarget)
                    {
                        m_Slot.SetOverride(m_Handles[record], TowardTarget, m_Evaluator.EvaluateShape(in s, in time) * scale);
                        return;
                    }
                    m_Evaluator.Evaluate(in s, in time, out MotionSample offset);
                    m_Slot.SetOffset(m_Handles[record], MapOffset(default(TOps).FromVector4(offset.Value * scale)));
                    return;
                }
                case MotionWrite.Override:
                {
                    m_Evaluator.Evaluate(in s, in time, out MotionSample over);
                    Vector4 mask = OverrideMask;
                    if (mask != Vector4.one)
                        m_Slot.SetOverride(m_Handles[record], MapOverride(default(TOps).FromVector4(over.Value)), over.Weight * m_Gate.Scale(m_Category), in mask);
                    else
                        m_Slot.SetOverride(m_Handles[record], MapOverride(default(TOps).FromVector4(over.Value)), over.Weight * m_Gate.Scale(m_Category));
                    return;
                }
                default:
                {
                    if (!Live)
                        return;
                    if (!TryPersist(in s, in time, out T value))
                    {
                        m_Evaluator.Evaluate(in s, in time, out MotionSample persist);
                        value = FromMotion(persist.Value);
                    }
                    m_Slot.WriteValue(value);
                    return;
                }
            }
        }

        // Completed keeps the contribution to the pass's end (a restart takes it over); the other reasons let it go now.
        // Revert puts the play's start value back for Persist — the items that ended earlier in the play too
        internal sealed override void End(int record, EndReason reason)
        {
            if (m_Running[record])
            {
                m_Running[record] = false;
                m_RunningCount--;
            }
            if (m_Slot == null)
                return;
            if (m_Write != MotionWrite.Persist)
            {
                if (reason == EndReason.Completed)
                {
                    if (m_Handles[record].IsValid)
                        m_Held[record] = true;
                    return;
                }
                Let(record);
                return;
            }
            // the latest play ran backward and did not reach its end
            if (reason != EndReason.Completed && m_Reverse[record] && m_Serials[record] == m_LastSerial)
                m_ReverseCut = true;
            if (reason == EndReason.Revert && m_HasStart[record] && Live)
                m_Slot.WriteValue(Stored(m_Starts[record]));
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

        // the value before it becomes the origin and the first value — a handed-over binding that wrote already keeps its
        // origin. Offsets, overrides and instants write nothing
        internal sealed override bool ApplyInitial(in MotionTime time, float intensity)
        {
            if (m_Write != MotionWrite.Persist || m_Evaluator.IsInstant || !Live)
                return false;
            T value = m_Slot.Base;
            if (!m_HasFirst)
            {
                m_First = value;
                m_HasFirst = true;
            }
            if (!m_Applied)
            {
                m_Origin = value;
                m_HasOrigin = true;
                m_Applied = true;
            }
            m_Evaluator.Begin(ref m_Initial, ToMotion(m_Origin), new MotionBegin { Seed = 0, Intensity = intensity });
            if (!TryPersist(in m_Initial, in time, out T written))
            {
                m_Evaluator.Evaluate(in m_Initial, in time, out MotionSample sample);
                written = FromMotion(sample.Value);
            }
            m_Slot.WriteValue(written);
            return true;
        }

        // through the slot's door like Revert — offsets and overrides have nothing to do (Hold let them go)
        internal sealed override void Restore()
        {
            if (m_Write == MotionWrite.Persist && m_HasFirst && Live)
                m_Slot.WriteValue(Stored(m_First));
            ForgetSnapshots();
        }

        // the same item's old binding of the same property — the first value, the origin and its marks
        internal sealed override void Inherit(ItemBinding old)
        {
            if (!(old is ValueTargetBinding<T, TOps> from) || from.GetType() != GetType() || m_Slot == null || from.m_Slot == null || !m_Slot.Key.Equals(from.m_Slot.Key))
                return;
            m_First = from.m_First;
            m_HasFirst = from.m_HasFirst;
            m_Origin = from.m_Origin;
            m_HasOrigin = from.m_HasOrigin;
            m_Applied = from.m_Applied;
            m_AfterReverse = from.m_AfterReverse;
            m_ReverseCut = from.m_ReverseCut;
        }

        void ForgetSnapshots()
        {
            m_Origin = default;
            m_First = default;
            m_HasOrigin = false;
            m_HasFirst = false;
            m_Applied = false;
            m_AfterReverse = false;
            m_ReverseCut = false;
        }

        // MotionDriver's Follow reads the value once and writes its spring's value through the slot's door, in the motion's
        // space (a World position · rotation like Persist)
        int IFollowBinding.MotionDimension => Dimension(m_Item.Traits.ValueKind);

        bool IFollowBinding.TryReadMotion(out Vector4 value)
        {
            if (!Live)
            {
                value = default;
                return false;
            }
            value = ToMotion(m_Slot.Base);
            return true;
        }

        bool IFollowBinding.WriteMotion(in Vector4 value)
        {
            if (!Live)
                return false;
            m_Slot.WriteValue(FromMotion(value));
            return true;
        }

        // the camera changed or is gone — this Begin finds the main camera again and takes its slot (allocation once). A slot
        // following the active scene takes the slot of the scene active now
        void FollowCamera()
        {
            Camera main = m_FollowsCamera ? Camera.main : null;
            if (main == m_Camera && Live && !(Refollows && SlotStale))
                return;
            if (m_Slot != null)
            {
                m_Layers.Release(m_Slot, m_Reserved);
                m_Slot = null;
            }
            for (int i = 0; i < m_Handles.Length; i++)
            {
                m_Handles[i] = default;
                m_Held[i] = false;
                // a later Revert of a play that began on the old camera writes nothing
                m_HasStart[i] = false;
            }
            // the old camera's values — the origin and the first value go too
            ForgetSnapshots();
            m_Camera = main;
            if ((m_FollowsCamera && main == null) || !Acquire(in m_Context, m_Item.m_DirectWrite, out m_Slot))
            {
                m_Slot = null;
                return;
            }
            m_Slot.Reserve(m_Reserved);
        }
    }
}
