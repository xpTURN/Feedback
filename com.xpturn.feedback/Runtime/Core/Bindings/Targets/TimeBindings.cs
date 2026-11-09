namespace xpTURN.Feedback
{
    // a hit stop asks the context's time scale for its occurrence's real length (the resolved length / Speed). Backward,
    // under the minimum condition (the smaller of the engine's and the computed scale) or with no end (Speed 0) it asks
    // nothing. The service counts the lifetime; the item's end takes the request back whatever the reason (a Skip leaves no
    // freeze behind)
    internal sealed class HitStopBinding : ItemBinding
    {
        internal const string EndlessWarning = "A Feedback hit stop with no end (its player's Speed is 0) was not requested.";

        readonly HitStopItem m_Item;
        TimeScaleService m_Service;
        TimeScaleHandle[] m_Handles;
#if DEBUG
        bool m_WarnedEndless;
#endif

        internal HitStopBinding(HitStopItem item)
        {
            m_Item = item;
        }

        // observation
        internal TimeScaleService Service => m_Service;
        internal TimeScaleHandle Handle(int record) => m_Handles[record];

        internal override bool IsAlive => true;

        internal override bool Prepare(in PrepareContext context)
        {
            m_Service = context.Context.TimeScale;
            m_Handles = new TimeScaleHandle[context.MaxOverlap];
            return true;
        }

        internal override void Begin(int record, in BeginContext context)
        {
            if (context.Reverse)
                return;
            if (m_Service.Current < m_Item.m_MinimumTimeScale)
                return;
            // a freeze with no end is not asked for — a destroyed player's play would never let it go
            if (double.IsInfinity(context.Seconds))
            {
#if DEBUG
                if (!m_WarnedEndless)
                {
                    m_WarnedEndless = true;
                    UnityEngine.Debug.LogWarning(EndlessWarning);
                }
#endif
                return;
            }
            m_Handles[record] = m_Service.Request(m_Item.m_TimeScale, context.Seconds);
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        // a zero freeze is an instant: no request
        internal override void Execute(int record, in BeginContext context)
        {
        }

        internal override void End(int record, EndReason reason)
        {
            if (m_Handles[record].IsValid)
                m_Service.Release(m_Handles[record]);
            m_Handles[record] = default;
        }
    }

    // the context's one time-scale slot — a Flash overrides it (back to 1), a Tween · Set writes it and it stays (the play's
    // Revert · Restore put it back)
    internal sealed class TimeScaleBinding : ValueTargetBinding<float, FloatOps>
    {
        internal TimeScaleBinding(TimeScaleItem item) : base(item)
        {
        }

        protected override bool ReadsTarget => false;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            TimeScaleService service = context.Context.TimeScale;
            slot = Layers.GetOrCreate(null, 0, 0, () => new TimeScaleSlot(service));
            return true;
        }
    }
}
