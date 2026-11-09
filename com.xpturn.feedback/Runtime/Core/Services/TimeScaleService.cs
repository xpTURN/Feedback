using UnityEngine;

namespace xpTURN.Feedback
{
    // a request's ticket — the number it was given (0 = none)
    internal readonly struct TimeScaleHandle
    {
        internal readonly int Id;

        internal TimeScaleHandle(int id)
        {
            Id = id;
        }

        internal bool IsValid => Id != 0;
    }

    // the requests' stack (fixed capacity, the top wins) × the base scale. Every request runs out on its own unscaled
    // lifetime, and a new one is not shortened by the first Update it meets (a request made after our Update gets its whole
    // length too). Time.timeScale is written here, after the others'-write check, and only while playing (the editor's
    // settings stay clean) — an idle service's base scale is the one write outside Update. The two delta-time values follow
    // the effective scale when the option is on
    internal sealed class TimeScaleService : FeedbackService
    {
        internal const string FullWarning = "The Feedback time-scale stack was full: the oldest request was dropped.";
        internal const string RangeWarning = "A Feedback time-scale request was out of 0 ~ 100 and was clamped.";

        struct Entry
        {
            internal float Scale;
            internal double Remaining;
            internal int Id;
            // made since the last Update: that Update writes it and does not shorten it
            internal bool Fresh;
        }

        readonly Entry[] m_Stack;
        // the open request — no lifetime, a changing scale, one holder (the context's time-scale slot). Out of the stack (its
        // capacity · ageing · dropping are the timed ones') and under it: a timed request wins, else the open one
        int m_OpenId;
        float m_OpenScale = 1f;
        bool m_OpenChanged;
        readonly bool m_Adjust;
        int m_Count;
        int m_NextId;
        float m_Base = 1f;
        bool m_HasBase;
        // set through the API in a request — written at the next Update
        bool m_BaseDirty;
        // what the last write left (read back) — a different value at Update is the game's write
        bool m_Owning;
        float m_Written;
        // the delta-time values before the stack filled — the empty stack and Shutdown put them back
        float m_FixedBefore;
        float m_MaximumBefore;
#if DEBUG
        bool m_WarnedFull;
        bool m_WarnedRange;
#endif

        internal TimeScaleService(FeedbackContext context, SettingsValues settings)
            : base(context)
        {
            m_Stack = new Entry[Mathf.Max(1, settings.TimeScaleCapacity)];
            m_Adjust = settings.AdjustFixedDeltaTime;
        }

        // edit mode writes no time value — a request and a restore there change no setting (the direct-tick tests read the
        // computed scale)
        static bool Writes => Application.isPlaying;

        // observation
        internal int Count => m_Count;
        internal double RemainingAt(int i) => m_Stack[i].Remaining;
        internal float ScaleAt(int i) => m_Stack[i].Scale;
        internal bool FreshAt(int i) => m_Stack[i].Fresh;
        internal bool IsOwning => m_Owning;
        internal float BaseScale => m_HasBase ? m_Base : (Writes ? Time.timeScale : 1f);
        internal float Effective => Mathf.Clamp(m_Count > 0 ? BaseScale * m_Stack[m_Count - 1].Scale : m_OpenId != 0 ? BaseScale * m_OpenScale : BaseScale, 0f, 100f);
        internal bool HasOpen => m_OpenId != 0;
        internal float OpenValue => m_OpenScale;

        internal float OpenScaleOf(TimeScaleHandle handle) => handle.IsValid && handle.Id == m_OpenId ? m_OpenScale : 1f;

        // the scale a minimum condition compares: playing, the smaller of what the engine runs at and the computed one (a
        // request of this frame not written yet), else the computed one — a second freeze in a frame is skipped in every
        // stage and layer
        internal float Current => Writes ? Mathf.Min(Time.timeScale, Effective) : Effective;

        internal void SetBaseScale(float scale)
        {
            if (Refuses())
                return;
            // an idle service holds no base: the game's write, now
            if (Writes && m_Count == 0 && m_OpenId == 0 && !m_Owning)
            {
                m_HasBase = false;
                m_BaseDirty = false;
                if (Time.timeScale != scale)
                    Time.timeScale = scale;
                return;
            }
            m_Base = scale;
            m_HasBase = true;
            m_BaseDirty = true;
        }

        internal TimeScaleHandle Request(float scale, double seconds)
        {
            if (Refuses())
                return default;
            if (!(scale >= 0f && scale <= 100f))
            {
#if DEBUG
                if (!m_WarnedRange)
                {
                    m_WarnedRange = true;
                    Debug.LogWarning(RangeWarning);
                }
#endif
                scale = scale > 100f ? 100f : 0f;
            }
            if (!m_HasBase)
            {
                m_Base = Writes ? Time.timeScale : 1f;
                m_HasBase = true;
            }
            if (m_Count == m_Stack.Length)
            {
#if DEBUG
                if (!m_WarnedFull)
                {
                    m_WarnedFull = true;
                    Debug.LogWarning(FullWarning);
                }
#endif
                RemoveAt(0);
            }
            int id = ++m_NextId;
            if (id == 0)
                id = ++m_NextId;
            m_Stack[m_Count++] = new Entry { Scale = scale, Remaining = seconds, Id = id, Fresh = true };
            return new TimeScaleHandle(id);
        }

        // the open request at scale 1 — a second open takes the first one's place
        internal TimeScaleHandle Open()
        {
            if (Refuses())
                return default;
            if (!m_HasBase)
            {
                m_Base = Writes ? Time.timeScale : 1f;
                m_HasBase = true;
            }
            int id = ++m_NextId;
            if (id == 0)
                id = ++m_NextId;
            m_OpenId = id;
            m_OpenScale = 1f;
            m_OpenChanged = true;
            return new TimeScaleHandle(id);
        }

        internal void SetScale(TimeScaleHandle handle, float scale)
        {
            if (!handle.IsValid || handle.Id != m_OpenId || Refuses())
                return;
            if (!(scale >= 0f && scale <= 100f))
            {
#if DEBUG
                if (!m_WarnedRange)
                {
                    m_WarnedRange = true;
                    Debug.LogWarning(RangeWarning);
                }
#endif
                scale = scale > 100f ? 100f : 0f;
            }
            if (m_OpenScale == scale)
                return;
            m_OpenScale = scale;
            m_OpenChanged = true;
        }

        internal bool Release(TimeScaleHandle handle)
        {
            if (!handle.IsValid || Refuses())
                return false;
            for (int i = 0; i < m_Count; i++)
            {
                if (m_Stack[i].Id == handle.Id)
                {
                    RemoveAt(i);
                    return true;
                }
            }
            if (handle.Id != m_OpenId)
                return false;
            m_OpenId = 0;
            m_OpenScale = 1f;
            m_OpenChanged = true;
            return true;
        }

        internal bool IsAlive(TimeScaleHandle handle)
        {
            for (int i = 0; i < m_Count; i++)
            {
                if (m_Stack[i].Id == handle.Id)
                    return true;
            }
            return handle.IsValid && handle.Id == m_OpenId;
        }

        void RemoveAt(int index)
        {
            for (int i = index + 1; i < m_Count; i++)
                m_Stack[i - 1] = m_Stack[i];
            m_Count--;
        }

        internal override void Update(in FrameTime time)
        {
            // ① the game's write since ours is the new base
            if (m_Owning && Writes && Time.timeScale != m_Written)
                m_Base = Time.timeScale;
            // ② every request ages — a new one only loses its mark
            for (int i = m_Count - 1; i >= 0; i--)
            {
                if (m_Stack[i].Fresh)
                {
                    m_Stack[i].Fresh = false;
                    continue;
                }
                m_Stack[i].Remaining -= time.UnscaledDelta;
                if (m_Stack[i].Remaining <= 0.0)
                    RemoveAt(i);
            }
            // ③ edit mode ends here
            if (!Writes)
                return;
            m_OpenChanged = false;
            if (m_Count > 0 || m_OpenId != 0)
            {
                // ④ the delta-time values before the first write
                if (!m_Owning)
                {
                    m_Owning = true;
                    m_FixedBefore = Time.fixedDeltaTime;
                    m_MaximumBefore = Time.maximumDeltaTime;
                }
                Write(Effective);
            }
            else if (m_Owning)
            {
                // ⑤ the last request ended: the base, the delta-time values as before — then hands off (the next request reads
                // the base again)
                Write(m_Base);
                PutBackDeltas();
                m_Owning = false;
                m_HasBase = false;
            }
            else if (m_BaseDirty)
            {
                if (Time.timeScale != m_Base)
                    Time.timeScale = m_Base;
                m_BaseDirty = false;
            }
        }

        // after the Update stage's playbacks — an open request changed there is written now, not at the next frame's Update
        // (this pass's Update made the others'-write check)
        internal void Flush()
        {
            if (!m_OpenChanged || !Writes || Refuses())
                return;
            m_OpenChanged = false;
            if (m_Count > 0 || m_OpenId != 0)
            {
                if (!m_Owning)
                {
                    m_Owning = true;
                    m_FixedBefore = Time.fixedDeltaTime;
                    m_MaximumBefore = Time.maximumDeltaTime;
                }
                Write(Effective);
            }
            else if (m_Owning)
            {
                Write(m_Base);
                PutBackDeltas();
                m_Owning = false;
                m_HasBase = false;
            }
        }

        void Write(float scale)
        {
            m_BaseDirty = false;
            if (Time.timeScale != scale)
                Time.timeScale = scale;
            m_Written = Time.timeScale;
            // at 0 both stay — a frozen frame's dt is 0 already
            if (m_Adjust && scale > 0f)
            {
                float fixedDelta = m_FixedBefore * scale;
                if (Time.fixedDeltaTime != fixedDelta)
                    Time.fixedDeltaTime = fixedDelta;
                float maximum = m_MaximumBefore * scale;
                if (Time.maximumDeltaTime != maximum)
                    Time.maximumDeltaTime = maximum;
            }
        }

        // fixedDeltaTime first — maximumDeltaTime written under a still-large fixedDeltaTime rises to it
        void PutBackDeltas()
        {
            if (!m_Adjust)
                return;
            if (Time.fixedDeltaTime != m_FixedBefore)
                Time.fixedDeltaTime = m_FixedBefore;
            if (Time.maximumDeltaTime != m_MaximumBefore)
                Time.maximumDeltaTime = m_MaximumBefore;
        }

        // a close outside play (the exit's EnteredEditMode) writes nothing: the engine put the values back
        internal override void Shutdown()
        {
            m_Count = 0;
            m_OpenId = 0;
            m_OpenScale = 1f;
            if (!m_Owning || !Writes)
                return;
            if (Time.timeScale != m_Base)
                Time.timeScale = m_Base;
            PutBackDeltas();
            m_Owning = false;
        }
    }

    // the public base scale — the loop's context (Default). A pausing game sets it here: a direct write of the same value (0
    // during a freeze) cannot be told from ours, and a write after ours (LateUpdate) runs the next frame
    /// <summary>The game's own time scale that hit stops and slow motion work from.</summary>
    public static class FeedbackTimeScale
    {
        internal const string NoContextWarning = "FeedbackTimeScale.BaseScale was set with no running Feedback context (outside play) and was ignored.";
        internal const string RangeWarning = "FeedbackTimeScale.BaseScale takes 0 ~ 100; the value was ignored.";
#if DEBUG
        static bool s_WarnedContext;
        static bool s_WarnedRange;
#endif

        static TimeScaleService Service()
        {
            FeedbackScheduler s = FeedbackScheduler.Default;
            return s != null && !s.IsShutdown ? s.Context.TimeScale : null;
        }

        /// <summary>The game's own time scale (0 to 100) that time effects work from; set it here instead of Time.timeScale to pause or slow the game.</summary>
        public static float BaseScale
        {
            get => Service()?.BaseScale ?? Time.timeScale;
            set
            {
                if (!(value >= 0f && value <= 100f))
                {
#if DEBUG
                    if (!s_WarnedRange)
                    {
                        s_WarnedRange = true;
                        Debug.LogWarning(RangeWarning);
                    }
#endif
                    return;
                }
                TimeScaleService service = Service();
                if (service == null)
                {
#if DEBUG
                    if (!s_WarnedContext)
                    {
                        s_WarnedContext = true;
                        Debug.LogWarning(NoContextWarning);
                    }
#endif
                    return;
                }
                service.SetBaseScale(value);
            }
        }
    }
}
