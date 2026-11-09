using Unity.Cinemachine;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the lens · noise values — a main camera target is its brain's live Cinemachine camera (the resolver's hook) and
    // follows it while nothing runs. Each binding gets the service: the preview's Cinemachine time
    internal sealed class CinemachineLensFieldOfViewBinding : ValueTargetBinding<float, FloatOps>
    {
        CinemachineCamera m_Camera;

        internal CinemachineLensFieldOfViewBinding(CinemachineLensFieldOfViewItem item)
            : base(item)
        {
        }

        protected override bool Refollows => m_Item.m_Target.m_Mode == TargetRefMode.MainCamera;

        protected override bool SlotStale => m_Camera != (CinemachineTargets.Live(Camera.main) as CinemachineCamera);

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            CinemachineFeedbackService.Of(context.Context);
            CinemachineCamera cam = TargetResolver.Find<CinemachineCamera>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (cam == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Camera = cam;
            slot = direct ? Layers.CreatePrivate(cam, 0, 0, () => new CinemachineLensFieldOfViewSlot(cam)) : Layers.GetOrCreate(cam, 0, 0, () => new CinemachineLensFieldOfViewSlot(cam));
            return true;
        }
    }

    internal sealed class CinemachineLensOrthographicSizeBinding : ValueTargetBinding<float, FloatOps>
    {
        CinemachineCamera m_Camera;

        internal CinemachineLensOrthographicSizeBinding(CinemachineLensOrthographicSizeItem item)
            : base(item)
        {
        }

        protected override bool Refollows => m_Item.m_Target.m_Mode == TargetRefMode.MainCamera;

        protected override bool SlotStale => m_Camera != (CinemachineTargets.Live(Camera.main) as CinemachineCamera);

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            CinemachineFeedbackService.Of(context.Context);
            CinemachineCamera cam = TargetResolver.Find<CinemachineCamera>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (cam == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Camera = cam;
            slot = direct ? Layers.CreatePrivate(cam, 0, 0, () => new CinemachineLensOrthographicSizeSlot(cam)) : Layers.GetOrCreate(cam, 0, 0, () => new CinemachineLensOrthographicSizeSlot(cam));
            return true;
        }
    }

    internal sealed class CinemachineLensDutchBinding : ValueTargetBinding<float, FloatOps>
    {
        CinemachineCamera m_Camera;

        internal CinemachineLensDutchBinding(CinemachineLensDutchItem item)
            : base(item)
        {
        }

        protected override bool Refollows => m_Item.m_Target.m_Mode == TargetRefMode.MainCamera;

        protected override bool SlotStale => m_Camera != (CinemachineTargets.Live(Camera.main) as CinemachineCamera);

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            CinemachineFeedbackService.Of(context.Context);
            CinemachineCamera cam = TargetResolver.Find<CinemachineCamera>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (cam == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Camera = cam;
            slot = direct ? Layers.CreatePrivate(cam, 0, 0, () => new CinemachineLensDutchSlot(cam)) : Layers.GetOrCreate(cam, 0, 0, () => new CinemachineLensDutchSlot(cam));
            return true;
        }
    }

    internal sealed class CinemachineNoiseAmplitudeGainBinding : ValueTargetBinding<float, FloatOps>
    {
        CinemachineBasicMultiChannelPerlin m_Noise;

        internal CinemachineNoiseAmplitudeGainBinding(CinemachineNoiseAmplitudeGainItem item)
            : base(item)
        {
        }

        protected override bool Refollows => m_Item.m_Target.m_Mode == TargetRefMode.MainCamera;

        protected override bool SlotStale => m_Noise != (CinemachineTargets.MainCameraTarget(Camera.main, typeof(CinemachineBasicMultiChannelPerlin)) as CinemachineBasicMultiChannelPerlin);

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            CinemachineFeedbackService.Of(context.Context);
            CinemachineBasicMultiChannelPerlin noise = TargetResolver.Find<CinemachineBasicMultiChannelPerlin>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (noise == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Noise = noise;
            slot = direct ? Layers.CreatePrivate(noise, 0, 0, () => new CinemachineNoiseAmplitudeGainSlot(noise)) : Layers.GetOrCreate(noise, 0, 0, () => new CinemachineNoiseAmplitudeGainSlot(noise));
            return true;
        }
    }

    internal sealed class CinemachineNoiseFrequencyGainBinding : ValueTargetBinding<float, FloatOps>
    {
        CinemachineBasicMultiChannelPerlin m_Noise;

        internal CinemachineNoiseFrequencyGainBinding(CinemachineNoiseFrequencyGainItem item)
            : base(item)
        {
        }

        protected override bool Refollows => m_Item.m_Target.m_Mode == TargetRefMode.MainCamera;

        protected override bool SlotStale => m_Noise != (CinemachineTargets.MainCameraTarget(Camera.main, typeof(CinemachineBasicMultiChannelPerlin)) as CinemachineBasicMultiChannelPerlin);

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            CinemachineFeedbackService.Of(context.Context);
            CinemachineBasicMultiChannelPerlin noise = TargetResolver.Find<CinemachineBasicMultiChannelPerlin>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (noise == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Noise = noise;
            slot = direct ? Layers.CreatePrivate(noise, 0, 0, () => new CinemachineNoiseFrequencyGainSlot(noise)) : Layers.GetOrCreate(noise, 0, 0, () => new CinemachineNoiseFrequencyGainSlot(noise));
            return true;
        }
    }

    // an impulse — our own signal per play record (the standard curve or the item's), the manager's event taken ahead at
    // the preparation, the manager's clock (its time-scale flag untouched). A stop cancels our event only: a pruned event
    // is recycled with its source cleared, so 'is its source ours' is the identity
    internal sealed class CinemachineImpulseBinding : ItemBinding
    {
        internal const int AheadPerRecord = 2;
        const float BigNumber = 9999999f;

        readonly CinemachineImpulseItem m_Item;
        CinemachineFeedbackService m_Service;
        Transform m_Origin;
        CinemachineImpulseSource m_Source;
        CinemachineImpulseSignal[] m_Signals;
        CinemachineImpulseManager.ImpulseEvent[] m_Events;

        internal CinemachineImpulseBinding(CinemachineImpulseItem item)
        {
            m_Item = item;
        }

        // observation
        internal CinemachineImpulseManager.ImpulseEvent EventOf(int record) => m_Events[record];
        internal CinemachineImpulseSignal SignalOf(int record) => m_Signals[record];

        internal override bool Prepare(in PrepareContext context)
        {
            if (m_Item.m_Mode == CinemachineImpulseMode.Source)
            {
                m_Source = m_Item.m_Source as CinemachineImpulseSource;
                if (m_Source == null)
                {
#if DEBUG
                    return Refuse("the source is not a Cinemachine Impulse Source");
#else
                    return Refuse(null);
#endif
                }
            }
            else if (m_Item.m_Duration <= 0f)
            {
#if DEBUG
                return Refuse("an impulse with no length");
#else
                return Refuse(null);
#endif
            }
            m_Service = CinemachineFeedbackService.Of(context.Context);
            if (m_Service == null)
            {
#if DEBUG
                return Refuse("no Cinemachine service");
#else
                return Refuse(null);
#endif
            }
            m_Origin = TargetResolver.Find<Transform>(in m_Item.m_Origin, in context, out TargetFailure _);
            int n = context.MaxOverlap;
            m_Signals = new CinemachineImpulseSignal[n];
            m_Events = new CinemachineImpulseManager.ImpulseEvent[n];
            AnimationCurve curve = CinemachineImpulseCurves.Of(m_Item.m_Shape, m_Item.m_CustomShape);
            for (int i = 0; i < n; i++)
                m_Signals[i] = new CinemachineImpulseSignal(curve);
            if (m_Source == null)
                m_Service.TakeAhead(n * AheadPerRecord);
            return true;
        }

        internal override void Begin(int record, in BeginContext context)
        {
            if (context.Reverse)
                return;
            Vector3 at = context.HasPosition ? context.Position : m_Origin != null ? m_Origin.position : Vector3.zero;
            Vector3 velocity = m_Item.m_Velocity * context.Intensity;
            if (m_Source != null)
            {
                m_Source.GenerateImpulseAtPositionWithVelocity(at, velocity);
                return;
            }
            CinemachineImpulseSignal s = m_Signals[record];
            s.Velocity = velocity;
            s.Duration = m_Item.m_Duration;
            CinemachineImpulseManager.ImpulseEvent e = m_Service.NewEvent();
            e.Envelope = new CinemachineImpulseManager.EnvelopeDefinition { SustainTime = m_Item.m_Duration };
            e.SignalSource = s;
            e.Position = at;
            bool uniform = m_Item.m_Spread == CinemachineImpulseSpread.Uniform;
            e.Radius = uniform ? BigNumber : 0f;
            e.Channel = m_Item.m_Channel;
            e.DirectionMode = CinemachineImpulseManager.ImpulseEvent.DirectionModes.Fixed;
            e.DissipationDistance = uniform ? 0f : m_Item.m_DissipationDistance;
            e.PropagationSpeed = m_Item.m_Spread == CinemachineImpulseSpread.Propagating ? m_Item.m_PropagationSpeed : BigNumber;
            e.CustomDissipation = m_Item.m_DissipationRate;
            m_Service.Fire(e);
            m_Events[record] = e;
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
            CinemachineImpulseManager.ImpulseEvent e = m_Events[record];
            m_Events[record] = null;
            if (reason == EndReason.Completed || e == null || e.SignalSource != m_Signals[record])
                return;
            e.Cancel(CinemachineImpulseManager.Instance.CurrentTime, false);
        }
    }

    // every impulse gone — the manager's own clear (a live event is dropped, not pooled)
    internal sealed class CinemachineImpulseClearBinding : ItemBinding
    {
        internal CinemachineImpulseClearBinding(CinemachineImpulseClearItem item)
        {
        }

        internal override bool Prepare(in PrepareContext context) => true;

        internal override void Begin(int record, in BeginContext context) => Clear(in context);

        internal override void Execute(int record, in BeginContext context) => Clear(in context);

        static void Clear(in BeginContext context)
        {
            if (!context.Reverse)
                CinemachineImpulseManager.Instance.Clear();
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
        }
    }

    // a Cinemachine camera made live — its priority as a whole (Value's setter turns Enabled on), Prioritize for this frame
    // (a change after the cameras' Update counts at the next), the item's blend through the service's override. The preview
    // shows it only (an edit-mode blend is a cut, a priority written is a scene change)
    internal sealed class CinemachineActivateCameraBinding : ItemBinding
    {
        readonly CinemachineActivateCameraItem m_Item;
        CinemachineFeedbackService m_Service;
        CinemachineCamera m_Camera;
        CinemachineBlendDefinition m_Blend;
        bool m_Preview;

        internal CinemachineActivateCameraBinding(CinemachineActivateCameraItem item)
        {
            m_Item = item;
        }

        internal override bool IsAlive => m_Camera != null;

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
            m_Camera = TargetResolver.Find<CinemachineCamera>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (m_Camera == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Service = CinemachineFeedbackService.Of(context.Context);
            m_Preview = context.Context.Kind == ContextKind.Preview;
            CinemachineBlendSpec b = m_Item.m_Blend;
            m_Blend = new CinemachineBlendDefinition((CinemachineBlendDefinition.Styles)(int)b.m_Style, b.m_Time) { CustomCurve = b.m_Curve };
            return true;
        }

        internal override void Begin(int record, in BeginContext context) => Activate(in context);

        internal override void Execute(int record, in BeginContext context) => Activate(in context);

        void Activate(in BeginContext context)
        {
            if (context.Reverse || m_Camera == null || m_Preview)
                return;
            int value = m_Item.m_Priority;
            if (m_Item.m_ToTop && CinemachineCore.VirtualCameraCount > 0)
            {
                CinemachineVirtualCameraBase top = CinemachineCore.GetVirtualCamera(0);
                value = top == m_Camera ? m_Camera.Priority.Value : top.Priority.Value + 1;
            }
            if (m_Item.m_Blend.m_Override && m_Service != null)
                m_Service.PendBlend(m_Camera, in m_Blend);
            m_Camera.Priority = new PrioritySettings { Enabled = true, Value = value };
            m_Camera.Prioritize();
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
        }
    }
}
