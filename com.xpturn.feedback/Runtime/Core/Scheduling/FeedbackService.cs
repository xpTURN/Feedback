namespace xpTURN.Feedback
{
    // a service holds global state for its context (time scale, channels, overlays). Closed once — when its Update throws,
    // when the context shuts down, or when it is added to a closed context
    internal abstract class FeedbackService
    {
        internal const string StaleWarning = "A Feedback service of an earlier play session was asked for work. Get services from the current context each time instead of keeping them.";

#if DEBUG
        bool m_WarnedStale;
#endif

        protected FeedbackService(FeedbackContext context)
        {
            Context = context;
        }

        internal FeedbackContext Context { get; }

        // why it closed early: its Update threw
        internal bool IsDisabled { get; set; }

        // its Shutdown has been called — no Update from then on, and request APIs refuse
        internal bool IsClosed { get; set; }

        internal virtual void Update(in FrameTime time)
        {
        }

        // restores global state
        internal virtual void Shutdown()
        {
        }

        // after the layers' LateApply each frame — what must follow the cameras' LateUpdate
        internal virtual void LateApply()
        {
        }

        // the debugger's row of an extension — null: a Core service (its own typed row)
        internal virtual string DebugName => null;

        internal virtual void Describe(System.Collections.Generic.List<ServiceRow> into)
        {
            into.Clear();
        }

        // request APIs call this first — true refuses (closed). In development builds a closed service of a runtime context
        // while a different open Default runs was kept from an earlier session: warn once. A refusal at the cleanup (the same
        // closed Default), in edit mode or in tests and previews stays quiet
        internal bool Refuses()
        {
            if (!IsClosed)
                return false;
#if DEBUG
            if (!m_WarnedStale && Context.Kind == ContextKind.Runtime)
            {
                FeedbackScheduler current = FeedbackScheduler.Default;
                if (current != null && !current.IsShutdown && current.Context != Context)
                {
                    m_WarnedStale = true;
                    UnityEngine.Debug.LogWarning(StaleWarning);
                }
            }
#endif
            return true;
        }
    }

    // one line of an extension's debugger row — a label and a value
    internal readonly struct ServiceRow
    {
        internal readonly string Label;
        internal readonly float Value;

        internal ServiceRow(string label, float value)
        {
            Label = label;
            Value = value;
        }
    }
}
