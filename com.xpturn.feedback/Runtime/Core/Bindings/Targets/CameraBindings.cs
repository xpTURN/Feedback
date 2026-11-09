using UnityEngine;

namespace xpTURN.Feedback
{
    // a camera shake — the position as a Self offset (world units on the target's own axes) and, with a rotation amplitude,
    // the same motion again with that amplitude and a copy-tagged seed as a Local rotation offset. One binding writes both:
    // Stop and the cooldown take them together. A transform target takes its slots at the prepare (the main camera as the
    // value targets do); a channel target reaches the channel's receivers at each Begin, in registration order up to M
    // (settings) — more warn once and are left out — and ends a receiver's contributions when it goes. Every motion it takes
    // writes Offset
    internal sealed class CameraShakeBinding : ItemBinding
    {
        internal const string TooManyWarning = "A Feedback camera shake reached more receivers than a play takes (the settings' receivers per play); the rest were left out.";
        internal const string DirectChannelWarning = "A Feedback camera shake on a channel writes to the receivers' shared slots: its direct-write mark is ignored.";

        readonly CameraShakeItem m_Item;
        MotionEvaluator m_Position;
        MotionEvaluator m_Rotation;
        bool m_OnChannel;
        int m_Max;
        PropertyLayerRegistry m_Layers;
        ChannelService m_Channels;
        ChannelKey m_Key;
        CategoryGate m_Gate;
        FeedbackCategory m_Category;
        PrepareContext m_Context;
        // a transform target's pair
        ValueSlot<Vector3, Vector3Ops> m_PositionSlot;
        ValueSlot<Quaternion, QuaternionOps> m_RotationSlot;
        bool m_FollowsCamera;
        Camera m_Camera;
        Transform m_Owner;
        int m_Reserved;
        // per record: the two states, the targets reached (a channel's receivers — one for a transform target), their
        // handles and the receivers' generations at the Begin
        MotionState[] m_PositionStates;
        MotionState[] m_RotationStates;
        ValueSlot<Vector3, Vector3Ops>[] m_PositionSlots;
        ValueSlot<Quaternion, QuaternionOps>[] m_RotationSlots;
        ContributionHandle[] m_PositionHandles;
        ContributionHandle[] m_RotationHandles;
        CameraShakeReceiver[] m_Receivers;
        int[] m_Generations;
        int[] m_Reached;
        bool[] m_Held;
        bool[] m_Running;
        int m_RunningCount;
#if DEBUG
        bool m_WarnedMany;
#endif

        internal CameraShakeBinding(CameraShakeItem item)
        {
            m_Item = item;
        }

        // observation
        internal int Reached(int record) => m_Reached[record];
        internal uint PositionSeed(int record) => m_PositionStates[record].Seed;
        internal uint RotationSeed(int record) => m_RotationStates[record].Seed;
        internal bool HasRotation => m_Rotation != null;
        internal LocalPositionSlot PositionSlot => m_PositionSlot as LocalPositionSlot;
        internal LocalRotationSlot RotationSlot => m_RotationSlot as LocalRotationSlot;
        internal ValueSlot<Vector3, Vector3Ops> AnyPositionSlot => m_PositionSlot;
        internal ValueSlot<Quaternion, QuaternionOps> AnyRotationSlot => m_RotationSlot;

        // the rotation copy: the same parameters with the rotation amplitude (Curve: remapped 0 → the amplitude) — the
        // evaluators made from their parameter structs, outside the factory. None at 0
        static MotionEvaluator RotationCopy(FeedbackMotion motion, Vector3 amplitude)
        {
            if (amplitude == Vector3.zero)
                return null;
            switch (motion)
            {
                case ShakeMotion m:
                {
                    ShakeParams p = m.m_Params;
                    p.m_Amplitude = amplitude;
                    return new ShakeEvaluator(in p, 3);
                }
                case PunchMotion m:
                {
                    PunchParams p = m.m_Params;
                    p.m_Amplitude = amplitude;
                    return new PunchEvaluator(in p, 3);
                }
                case WiggleMotion m:
                {
                    WiggleParams p = m.m_Params;
                    p.m_Amplitude = amplitude;
                    return new WiggleEvaluator(in p, 3);
                }
                case CurveMotion m:
                {
                    CurveParams p = m.m_Params;
                    p.m_RemapZero = Vector4.zero;
                    p.m_RemapOne = amplitude;
                    return new CurveEvaluator(in p, 3);
                }
                default:
                    return null;
            }
        }

        bool Live => m_PositionSlot != null && m_PositionSlot.IsAlive && !m_PositionSlot.Detached;

        // a channel target lives without a slot of its own; a main camera target with nothing running finds the camera at
        // the next Begin
        internal override bool IsAlive => m_OnChannel || Live || (m_FollowsCamera && m_RunningCount == 0);

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
            m_Position = MotionEvaluators.Create(motion, 3);
            if (m_Position == null)
                return Refuse("an unknown motion");
            m_Rotation = RotationCopy(motion, m_Item.m_RotationAmplitude);
            m_Gate = context.Context.Categories;
            m_Category = m_Item.EffectiveCategory;
            m_Layers = context.Context.Layers;
            m_Context = context;
            m_Owner = context.Player != null ? context.Player.transform : null;
            int n = context.MaxOverlap;
            m_OnChannel = m_Item.m_Target.m_Mode == TargetRefMode.Channel;
            if (m_OnChannel)
            {
                // a channel target writes the receivers' shared slots
#if DEBUG
                if (m_Item.m_DirectWrite)
                    Debug.LogWarning(DirectChannelWarning, context.Player);
#endif
                m_Channels = context.Context.Channels;
                m_Key = new ChannelKey(in m_Item.m_Target.m_Channel);
                m_Max = context.Context.Settings.ShakeReceiversPerPlay;
            }
            else
            {
                m_Max = 1;
                m_FollowsCamera = m_Item.m_Target.m_Mode == TargetRefMode.MainCamera;
                if (m_FollowsCamera)
                    m_Camera = Camera.main;
                // no main camera yet — the first Begin finds it (that play takes the slots: allocation once)
                if (!(m_FollowsCamera && m_Camera == null) && !Acquire())
                    return false;
                m_Reserved = n;
                m_PositionSlot?.Reserve(n);
                m_RotationSlot?.Reserve(n);
            }
            m_PositionStates = new MotionState[n];
            m_RotationStates = new MotionState[n];
            m_PositionSlots = new ValueSlot<Vector3, Vector3Ops>[n * m_Max];
            m_RotationSlots = new ValueSlot<Quaternion, QuaternionOps>[n * m_Max];
            m_PositionHandles = new ContributionHandle[n * m_Max];
            m_RotationHandles = new ContributionHandle[n * m_Max];
            m_Receivers = new CameraShakeReceiver[n * m_Max];
            m_Generations = new int[n * m_Max];
            m_Reached = new int[n];
            m_Held = new bool[n];
            m_Running = new bool[n];
            return true;
        }

        // the slots: shared, or the direct-write pair — Self turns by the rotation slot's Base
        bool Acquire()
        {
            Transform target;
            if (m_FollowsCamera)
                target = m_Camera.transform;
            else
            {
                target = TargetResolver.Find<Transform>(in m_Item.m_Target, in m_Context, out TargetFailure failure);
                if (target == null)
                    return Refuse(TargetResolver.Describe(failure));
            }
            _ = target.parent;
            // a camera Cinemachine drives with a shake extension on its live camera — the extension's pair (the Cinemachine
            // assembly's hook) · else the transform's
            if (CameraHooks.ShakeSlots != null && CameraHooks.ShakeSlots(target, m_Layers, m_Item.m_DirectWrite, m_Rotation != null, out m_PositionSlot, out m_RotationSlot))
                return true;
            if (m_Item.m_DirectWrite)
            {
                var position = m_Layers.CreatePrivate(target, 0, 0, () => new LocalPositionSlot(target));
                m_PositionSlot = position;
                if (m_Rotation != null)
                {
                    var rotation = m_Layers.CreatePrivate(target, 0, 0, () => new LocalRotationSlot(target));
                    m_RotationSlot = rotation;
                    position.SelfPair = rotation;
                }
            }
            else
            {
                m_PositionSlot = m_Layers.GetOrCreate(target, 0, 0, () => new LocalPositionSlot(target));
                if (m_Rotation != null)
                    m_RotationSlot = m_Layers.GetOrCreate(target, 0, 0, () => new LocalRotationSlot(target));
            }
            return true;
        }

        // a resting binding finds the main camera again: a new one takes new slots, the old ones go
        void FollowCamera()
        {
            Camera main = Camera.main;
            if (main == m_Camera && Live)
                return;
            ReleaseSlots();
            m_Camera = main;
            if (main != null && Acquire())
            {
                m_PositionSlot.Reserve(m_Reserved);
                m_RotationSlot?.Reserve(m_Reserved);
            }
        }

        internal override void Begin(int record, in BeginContext context)
        {
            if (!m_OnChannel && m_FollowsCamera && m_RunningCount == 0)
                FollowCamera();
            if (!m_Running[record])
            {
                m_Running[record] = true;
                m_RunningCount++;
            }
            uint seed = Hash32.Tagged(context.ItemSeed, Hash32.Occurrence, Hash32.Fold(context.Occurrence));
            m_Position.Begin(ref m_PositionStates[record], default, new MotionBegin { Seed = seed, Intensity = context.Intensity });
            // the copy's seed is tagged as a copy — a use of its own
            m_Rotation?.Begin(ref m_RotationStates[record], default, new MotionBegin { Seed = Hash32.Tagged(seed, Hash32.Copy, 1u), Intensity = context.Intensity });
            if (m_Held[record])
            {
                // the pass's earlier occurrence hands its contributions over
                m_Held[record] = false;
                return;
            }
            int at = record * m_Max;
            if (!m_OnChannel)
            {
                if (!Live)
                {
                    m_Reached[record] = 0;
                    return;
                }
                Reach(at, m_PositionSlot, m_RotationSlot);
                m_Reached[record] = 1;
                return;
            }
            ReceiverList<CameraShakeReceiver> list = m_Channels.Shakes(in m_Key);
            int reached = 0;
            if (list != null)
            {
                // from the play's place: its position, else the player's — the Play's place once the player is gone
                Vector3 from = context.HasPosition ? context.Position : (m_Owner != null ? m_Owner.position : Vector3.zero);
                for (int i = 0; i < list.Count; i++)
                {
                    CameraShakeReceiver receiver = list.Items[i];
                    if (receiver == null || !receiver.IsLive)
                        continue;
                    float range = receiver.Range;
                    if (range > 0f && (receiver.Transform.position - from).sqrMagnitude > range * range)
                        continue;
                    if (reached == m_Max)
                    {
#if DEBUG
                        if (!m_WarnedMany)
                        {
                            m_WarnedMany = true;
                            Debug.LogWarning(TooManyWarning, m_Context.Player);
                        }
#endif
                        break;
                    }
                    Reach(at + reached, receiver.PositionSlot, m_Rotation != null ? receiver.RotationSlot : null);
                    m_Receivers[at + reached] = receiver;
                    m_Generations[at + reached] = receiver.Generation;
                    reached++;
                }
            }
            m_Reached[record] = reached;
        }

        void Reach(int at, ValueSlot<Vector3, Vector3Ops> position, ValueSlot<Quaternion, QuaternionOps> rotation)
        {
            m_PositionSlots[at] = position;
            m_PositionHandles[at] = position.Begin(ContributionKind.Offset, LayerSpace.Self);
            m_RotationSlots[at] = rotation;
            m_RotationHandles[at] = rotation != null ? rotation.Begin(ContributionKind.Offset, LayerSpace.Local) : default;
        }

        // the Camera category's scale on both, read at each evaluation
        internal override void Evaluate(int record, in MotionTime time)
        {
            int n = m_Reached[record];
            if (n == 0)
                return;
            float scale = m_Gate.Scale(m_Category);
            m_Position.Evaluate(in m_PositionStates[record], in time, out MotionSample p);
            Vector3 offset = (Vector3)p.Value * scale;
            Quaternion turn = Quaternion.identity;
            if (m_Rotation != null)
            {
                m_Rotation.Evaluate(in m_RotationStates[record], in time, out MotionSample r);
                Vector3 e = (Vector3)r.Value * scale;
                turn = Quaternion.Euler(e.x, e.y, e.z);
            }
            int at = record * m_Max;
            for (int k = 0; k < n; k++)
            {
                CameraShakeReceiver receiver = m_Receivers[at + k];
                if (receiver != null && receiver.Generation != m_Generations[at + k])
                {
                    // the receiver went: its contributions end (the slot may live on with another holder)
                    m_PositionSlots[at + k]?.End(m_PositionHandles[at + k]);
                    m_RotationSlots[at + k]?.End(m_RotationHandles[at + k]);
                    m_PositionSlots[at + k] = null;
                    m_RotationSlots[at + k] = null;
                    m_Receivers[at + k] = null;
                    continue;
                }
                if (m_PositionSlots[at + k] == null)
                    continue;
                m_PositionSlots[at + k].SetOffset(m_PositionHandles[at + k], in offset);
                if (m_RotationSlots[at + k] != null)
                    m_RotationSlots[at + k].SetOffset(m_RotationHandles[at + k], in turn);
            }
        }

        // Completed keeps the contributions to the pass's end (a restart takes them over); the other reasons let go now
        internal override void End(int record, EndReason reason)
        {
            if (m_Running[record])
            {
                m_Running[record] = false;
                m_RunningCount--;
            }
            if (reason == EndReason.Completed && m_Reached[record] > 0)
            {
                m_Held[record] = true;
                return;
            }
            Let(record);
        }

        internal override void Settle(int record)
        {
            if (m_Held[record])
                Let(record);
        }

        void Let(int record)
        {
            int at = record * m_Max;
            for (int k = 0; k < m_Reached[record]; k++)
            {
                m_PositionSlots[at + k]?.End(m_PositionHandles[at + k]);
                m_RotationSlots[at + k]?.End(m_RotationHandles[at + k]);
                m_PositionSlots[at + k] = null;
                m_RotationSlots[at + k] = null;
                m_PositionHandles[at + k] = default;
                m_RotationHandles[at + k] = default;
                m_Receivers[at + k] = null;
            }
            m_Reached[record] = 0;
            m_Held[record] = false;
        }

        void ReleaseSlots()
        {
            if (m_PositionSlot != null)
                m_Layers.Release(m_PositionSlot, m_Reserved);
            if (m_RotationSlot != null)
                m_Layers.Release(m_RotationSlot, m_Reserved);
            m_PositionSlot = null;
            m_RotationSlot = null;
        }

        internal override void Release() => ReleaseSlots();
    }
}
