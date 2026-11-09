using System;
using Unity.Profiling;

namespace xpTURN.Feedback
{
    // runs playbacks on a slot map in three stages. RunRevert raises the frame number and records the slot count; RunUpdate
    // runs services → runners up to that count → layers → compaction; RunLateApply applies the late layers. Each stage runs
    // once per RunRevert. A runner's exception ends that playback alone and the pass goes on from the next slot. Shutdown
    // ends every playback and closes the services; nothing new is accepted after it. Main thread only
    internal sealed class FeedbackScheduler
    {
        internal const string ThreadMessage = "Feedback scheduler called off the main thread.";
        internal const string PassLimitMessage = "Feedback scheduler pass limit is past the slot count.";
        internal const string ShortageWarning = "Feedback playbacks outgrew the reservation and the slots grew. Reserve more (the players' maximum overlap).";
        internal const string ChurnWarning = "Feedback slots grew because playbacks were replaced several times in one frame.";
        internal const string ReentryWarning = "A Feedback scheduler stage was called from inside a stage and ignored.";
        internal const string StoppedWarning = "Feedback stages have not run for two frames; the PlayerLoop may have been replaced. Call FeedbackPlayerLoop.Reinstall() after changing it.";

        // the instance the PlayerLoop runs — FeedbackPlayerLoop sets it. A new one each session and each recompile during play:
        // read it every time, never keep it
        internal static FeedbackScheduler Default { get; set; }

        static readonly ProfilerMarker s_RevertMarker = new ProfilerMarker("Feedback.Revert");
        static readonly ProfilerMarker s_UpdateMarker = new ProfilerMarker("Feedback.Update");
        static readonly ProfilerMarker s_LateApplyMarker = new ProfilerMarker("Feedback.LateApply");

        [ThreadStatic] static bool t_IsMainThread;

        readonly SlotMap<PlaybackSlot> m_Slots = new SlotMap<PlaybackSlot>();
        readonly ILayerStages m_Layers;
        int m_PassLimit;
        int m_Cursor;
        bool m_InStage;
        bool m_IsShutdown;
        bool m_UpdatePending;
        bool m_LateApplyPending;
#if DEBUG
        int m_LastRevertFrame;
        bool m_WarnedStopped;
        bool m_WarnedShortage;
        bool m_WarnedChurn;
        bool m_WarnedReentry;
#endif

        internal FeedbackScheduler(ContextKind kind)
            : this(kind, null)
        {
        }

        // tests: a recording layer in place of the context's registry
        internal FeedbackScheduler(ContextKind kind, ILayerStages layers)
        {
            Context = new FeedbackContext(kind);
            m_Layers = layers ?? Context.Layers;
            t_IsMainThread = true;
#if DEBUG
            // before the first Revert the created frame is the baseline — a scheduler the loop never ran warns too
            if (kind == ContextKind.Runtime)
                m_LastRevertFrame = UnityEngine.Time.frameCount;
#endif
        }

        internal FeedbackContext Context { get; }

        internal bool IsShutdown => m_IsShutdown;

        // a token's callback runs on the canceling thread
        internal static bool OnMainThread => t_IsMainThread;

        // slots, the dead included — the bound of the query
        internal int SlotCount => m_Slots.Count;

        internal int ActiveCount => m_Slots.LiveCount;

        // what the players reserved — a test sees a player give its reservation back
        internal int ReservedCount => m_Slots.Reserved;

        // ---- stages. The checks run in this order: closed → inside a stage (warn) → this frame's flag

        internal void RunRevert()
        {
            if (m_IsShutdown)
                return;
            if (m_InStage)
            {
                WarnReentry();
                return;
            }
            using (s_RevertMarker.Auto())
            {
                m_InStage = true;
                try
                {
#if DEBUG
                    if (Context.Kind == ContextKind.Runtime)
                        m_LastRevertFrame = UnityEngine.Time.frameCount;
#endif
                    Context.BeginFrame();
                    m_PassLimit = m_Slots.Count;
                    m_Layers.Revert();
                    // after the layers' Revert — if it throws, this frame applies nothing onto unreverted values
                    m_UpdatePending = true;
                    m_LateApplyPending = true;
                }
                finally
                {
                    m_InStage = false;
                }
            }
        }

        internal void RunUpdate(in FrameTime time)
        {
            if (m_IsShutdown)
                return;
            if (m_InStage)
            {
                WarnReentry();
                return;
            }
            if (!m_UpdatePending)
                return;
            m_UpdatePending = false;
            using (s_UpdateMarker.Auto())
            {
                m_InStage = true;
                try
                {
                    UnityEngine.Debug.Assert(m_PassLimit <= m_Slots.Count, PassLimitMessage);
                    Context.UpdateServices(time);
                    // playbacks added after RunRevert — in this pass too — start next frame
                    int limit = m_PassLimit;
                    int i = 0;
                    while (i < limit)
                    {
                        try
                        {
                            RunFrom(i, limit, time);
                            break;
                        }
                        catch (Exception e)
                        {
                            i = m_Cursor;
                            Fault(i, e);
                            i++;
                        }
                    }
                    m_Layers.Apply(ConsumptionStage.Update);
                    // a time scale the stage changed is written this frame
                    Context.TimeScaleIfMade?.Flush();
                    m_Slots.Compact();
                }
                finally
                {
                    m_InStage = false;
                }
            }
        }

        // no try here — Mono pays per slot for a loop inside a try region
        void RunFrom(int i, int limit, in FrameTime time)
        {
            for (; i < limit; i++)
            {
                if (!m_Slots.IsAlive(i))
                    continue;
                m_Cursor = i;
                ref readonly PlaybackSlot slot = ref m_Slots.ItemAt(i);
                if (!slot.Runner.Advance(slot.State, time))
                    m_Slots.RemoveAt(i);
            }
        }

        // the original exception first, then the playback ends. A runner that stopped itself before throwing gets no Abort
        void Fault(int i, Exception e)
        {
            Context.LogException(e);
            if (!m_Slots.IsAlive(i))
                return;
            PlaybackSlot slot = m_Slots.ItemAt(i);
            m_Slots.RemoveAt(i);
            Abort(slot, AbortReason.Faulted);
        }

        // the slot is already dead: a Remove in the runner's cleanup is false, and a listener's new Play goes to the end
        void Abort(PlaybackSlot slot, AbortReason reason)
        {
            try
            {
                slot.Runner.Abort(slot.State, reason);
            }
            catch (Exception e)
            {
                Context.LogException(e);
            }
        }

        internal void RunLateApply()
        {
            if (m_IsShutdown)
                return;
            if (m_InStage)
            {
                WarnReentry();
                return;
            }
            if (!m_LateApplyPending)
                return;
            m_LateApplyPending = false;
            using (s_LateApplyMarker.Auto())
            {
                m_InStage = true;
                try
                {
                    m_Layers.Apply(ConsumptionStage.LateApply);
                    Context.LateApplyServices();
                }
                finally
                {
                    m_InStage = false;
                }
            }
        }

        internal void Tick(float dt) => Tick(new FrameTime(dt, dt));

        internal void Tick(in FrameTime time)
        {
            RunRevert();
            RunUpdate(time);
            RunLateApply();
        }

        // ---- runner API

        internal PlaybackHandle Add(IPlaybackRunner runner, int state)
        {
            UnityEngine.Debug.Assert(t_IsMainThread, ThreadMessage);
            if (m_IsShutdown)
                return default;
#if DEBUG
            if (Context.Kind == ContextKind.Runtime && t_IsMainThread)
                WarnStopped();
#endif
            int growth = m_Slots.GrowthCount;
            PlaybackHandle handle = new PlaybackHandle(m_Slots.Add(new PlaybackSlot(runner, state)));
            if (m_Slots.GrowthCount != growth)
                WarnGrowth();
            return handle;
        }

        // once per slot — an ended or foreign handle is false. The same after Shutdown
        internal bool Remove(PlaybackHandle handle)
        {
            UnityEngine.Debug.Assert(t_IsMainThread, ThreadMessage);
            return m_Slots.Remove(handle.Slot);
        }

        internal void Reserve(int count)
        {
            UnityEngine.Debug.Assert(t_IsMainThread, ThreadMessage);
            m_Slots.Reserve(count);
        }

        internal void Unreserve(int count)
        {
            UnityEngine.Debug.Assert(t_IsMainThread, ThreadMessage);
            m_Slots.Unreserve(count);
        }

        // Closed first, so a listener's new Play is refused. Inside a stage the running pass compacts
        internal void Shutdown()
        {
            if (m_IsShutdown)
                return;
            m_IsShutdown = true;
            int count = m_Slots.Count;
            for (int i = 0; i < count; i++)
            {
                if (!m_Slots.IsAlive(i))
                    continue;
                PlaybackSlot slot = m_Slots.ItemAt(i);
                m_Slots.RemoveAt(i);
                Abort(slot, AbortReason.ContextShutdown);
            }
            if (!m_InStage)
                m_Slots.Compact();
            Context.Shutdown();
        }

        // ---- query: walk slot numbers up to SlotCount. The debugger keeps a handle, not a slot — compaction moves slots

        internal bool TryGetActive(int slot, out ActivePlayback info)
        {
            if ((uint)slot >= (uint)m_Slots.Count || !m_Slots.IsAlive(slot))
            {
                info = default;
                return false;
            }
            ref readonly PlaybackSlot contents = ref m_Slots.ItemAt(slot);
            info = new ActivePlayback(new PlaybackHandle(m_Slots.HandleAt(slot)), contents.Runner, contents.Runner.DebugOwner, contents.State);
            return true;
        }

        // ---- warnings (development builds)

        // once per cause per scheduler. More live playbacks than reserved = a shortage; otherwise dead slots filled the
        // doubled array before compaction = the same playbacks replaced several times in one frame. Previews stay quiet
        void WarnGrowth()
        {
#if DEBUG
            if (Context.Kind == ContextKind.Preview)
                return;
            if (m_Slots.LiveCount > m_Slots.Reserved)
            {
                if (m_WarnedShortage)
                    return;
                m_WarnedShortage = true;
                UnityEngine.Debug.LogWarning(ShortageWarning);
            }
            else
            {
                if (m_WarnedChurn)
                    return;
                m_WarnedChurn = true;
                UnityEngine.Debug.LogWarning(ChurnWarning);
            }
#endif
        }

        void WarnReentry()
        {
#if DEBUG
            if (m_WarnedReentry)
                return;
            m_WarnedReentry = true;
            UnityEngine.Debug.LogWarning(ReentryWarning);
#endif
        }

        // an Add while Revert has not run this frame or the last — the PlayerLoop was replaced without ours. The loop's
        // kind only (test and preview schedulers run their own stages), on the main thread only (Time.frameCount throws
        // elsewhere)
        void WarnStopped()
        {
#if DEBUG
            if (m_WarnedStopped || UnityEngine.Time.frameCount - m_LastRevertFrame < 2)
                return;
            m_WarnedStopped = true;
            UnityEngine.Debug.LogWarning(StoppedWarning);
#endif
        }
    }
}
