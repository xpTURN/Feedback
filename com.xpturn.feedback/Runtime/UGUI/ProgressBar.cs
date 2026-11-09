using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace xpTURN.Feedback
{
    // what a bar writes — five kinds
    public enum ProgressBarFill
    {
        LocalScale = 0,
        FillAmount = 1,
        Width = 2,
        Height = 3,
        Anchor = 4,
    }

    // a bar with delayed followers and a bump — a runner of the scheduler (MotionDriver's form): it runs while enabled in play, on
    // our clock. The side is where the fill starts (not a value flip)
    /// <summary>A bar that moves to a value with a delayed decrease bar, a delayed increase bar and a bump; it runs while enabled in play.</summary>
    [AddComponentMenu("xpTURN/Feedback/Progress Bar")]
    public sealed class ProgressBar : MonoBehaviour, IPlaybackRunner
    {
        internal const string NoLabelWarning = "A Progress Bar leaves its label: " + FloatingTextBinding.NoTmpResources;

        [SerializeField] internal RectTransform m_Foreground;
        [SerializeField] internal RectTransform m_DelayedDecrease;
        [SerializeField] internal RectTransform m_DelayedIncrease;
        [SerializeField] internal ProgressBarFill m_Fill;
        [SerializeField] internal ScreenSide m_From;
        [SerializeField] internal Vector2 m_FillRange = new Vector2(0f, 1f);
        [SerializeField] internal float m_Value = 1f;
        [SerializeField] internal bool m_Unscaled = true;
        [SerializeField] internal float m_MoveDuration = 0.25f;
        [SerializeField] internal float m_DecreaseDelay = 0.5f;
        [SerializeField] internal float m_IncreaseDelay = 0.5f;
        [SerializeField] internal bool m_BumpOnIncrease = true;
        [SerializeField] internal bool m_BumpOnDecrease;
        [SerializeField] internal float m_BumpDuration = 0.2f;
        [SerializeField] internal AnimationCurve m_BumpScale = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.3f, 1.05f), new Keyframe(1f, 1f));
        [SerializeField] internal TMP_Text m_Text;
        [SerializeField] internal string m_Format = "{0:0}";
        [SerializeField] internal float m_TextMultiplier = 100f;
        [SerializeField] internal UnityEvent m_OnBump = new UnityEvent();

        /// <summary>Raised when a change bumps the bar.</summary>
        public event Action<ProgressBar> Bumped;
        /// <summary>Raised when the bars start moving toward a new value.</summary>
        public event Action<ProgressBar> MoveStarted;
        /// <summary>Raised when every bar has reached the value and a bump has settled.</summary>
        public event Action<ProgressBar> MoveEnded;

        struct Bar
        {
            internal RectTransform Rect;
            internal Image Image;
            internal Vector2 Size;
            internal float Shown;
            internal float From;
            internal float To;
            internal float Wait;
            internal float Time;
            internal bool Moving;
        }

        readonly Bar[] m_Bars = new Bar[3];
        float m_Target;
        float m_BumpTime = -1f;
        Vector3 m_BaseScale = Vector3.one;
        FeedbackScheduler m_Bound;
        FeedbackScheduler m_Scheduler;
        PlaybackHandle m_Handle;
        bool m_Running;
        bool m_Moving;
        bool m_Awake;
        TMPNumberFormat m_TextFormat;
        char[] m_Chars;

        /// <summary>The value the bar moves toward, 0 to 1.</summary>
        public float Value => m_Target;

        // observation
        internal float ShownOf(int bar) => m_Bars[bar].Shown;
        internal bool Running => m_Running;

        void Awake() => Init();

        void Init()
        {
            if (m_Awake)
                return;
            m_Awake = true;
            m_Bars[0].Rect = m_Foreground;
            m_Bars[1].Rect = m_DelayedDecrease;
            m_Bars[2].Rect = m_DelayedIncrease;
            for (int i = 0; i < m_Bars.Length; i++)
            {
                RectTransform r = m_Bars[i].Rect;
                if (r == null)
                    continue;
                m_Bars[i].Image = r.GetComponent<Image>();
                m_Bars[i].Size = r.rect.size;
            }
            m_BaseScale = transform.localScale;
            m_Target = Mathf.Clamp01(m_Value);
            for (int i = 0; i < m_Bars.Length; i++)
                m_Bars[i].Shown = m_Target;
            bool tmp = m_Text != null && FloatingTextBinding.s_TmpResources();
#if DEBUG
            if (m_Text != null && !tmp)
                Debug.LogWarning(NoLabelWarning, this);
#endif
            if (tmp && TMPNumberFormat.TryParse(m_Format, out m_TextFormat, out _))
                m_Chars = new char[m_TextFormat.Capacity];
            WriteAll();
            WriteText();
        }

        void OnEnable() => Run();

        void OnDisable() => Halt();

        // an edit-mode test drives it with its own scheduler (MotionDriver's Bind)
        internal void Bind(FeedbackScheduler scheduler)
        {
            Halt();
            m_Bound = scheduler;
            Init();
            Run();
        }

        void Run()
        {
            if (m_Running)
                return;
            FeedbackScheduler s = m_Bound != null && !m_Bound.IsShutdown ? m_Bound : (Application.isPlaying ? FeedbackScheduler.Default : null);
            if (s == null || s.IsShutdown)
                return;
            s.Reserve(1);
            m_Scheduler = s;
            m_Handle = s.Add(this, 0);
            m_Running = true;
        }

        void Halt()
        {
            if (!m_Running)
                return;
            m_Running = false;
            m_Scheduler.Remove(m_Handle);
            m_Scheduler.Unreserve(1);
            m_Handle = default;
        }

        /// <summary>Shows <paramref name="value"/>, clamped to 0 to 1, on every bar at once.</summary>
        public void SetValueImmediate(float value)
        {
            Init();
            m_Target = Mathf.Clamp01(value);
            for (int i = 0; i < m_Bars.Length; i++)
            {
                m_Bars[i].Shown = m_Target;
                m_Bars[i].Moving = false;
            }
            WriteAll();
            WriteText();
        }

        /// <summary>Moves the bar to <paramref name="value"/>, clamped to 0 to 1, with its delays and bump; without <paramref name="animate"/>, or while it does not run, it shows the value at once.</summary>
        public void SetValue(float value, bool animate = true)
        {
            Init();
            value = Mathf.Clamp01(value);
            if (!animate || !m_Running)
            {
                SetValueImmediate(value);
                return;
            }
            if (value == m_Target)
                return;
            bool increase = value > m_Target;
            m_Target = value;
            // the foreground follows a gain after the delay (the increase bar shows the gain at once) and a loss at once (the
            // decrease bar keeps the loss for the delay)
            Move(0, increase ? m_IncreaseDelay : 0f);
            Move(1, increase ? 0f : m_DecreaseDelay);
            if (increase)
                Snap(2);
            else
                Move(2, 0f);
            if (increase ? m_BumpOnIncrease : m_BumpOnDecrease)
            {
                m_BumpTime = 0f;
                Bumped?.Invoke(this);
                m_OnBump?.Invoke();
            }
            if (!m_Moving)
            {
                m_Moving = true;
                MoveStarted?.Invoke(this);
            }
            WriteText();
        }

        void Move(int i, float wait)
        {
            ref Bar b = ref m_Bars[i];
            if (b.Rect == null)
                return;
            b.From = b.Shown;
            b.To = m_Target;
            b.Wait = wait;
            b.Time = 0f;
            b.Moving = true;
        }

        void Snap(int i)
        {
            ref Bar b = ref m_Bars[i];
            b.Shown = m_Target;
            b.Moving = false;
        }

        bool IPlaybackRunner.Advance(int state, in FrameTime time)
        {
            if (this == null)
                return false;
            float dt = m_Unscaled ? time.UnscaledDelta : time.ScaledDelta;
            bool any = false;
            for (int i = 0; i < m_Bars.Length; i++)
            {
                ref Bar b = ref m_Bars[i];
                if (!b.Moving)
                    continue;
                if (b.Wait > 0f)
                {
                    b.Wait -= dt;
                    any = true;
                    continue;
                }
                b.Time += dt;
                float t = m_MoveDuration > 0f ? Mathf.Clamp01(b.Time / m_MoveDuration) : 1f;
                b.Shown = Mathf.Lerp(b.From, b.To, t);
                if (t >= 1f)
                    b.Moving = false;
                else
                    any = true;
            }
            if (m_BumpTime >= 0f)
            {
                m_BumpTime += dt;
                float bt = m_BumpDuration > 0f ? m_BumpTime / m_BumpDuration : 1f;
                if (bt >= 1f)
                {
                    m_BumpTime = -1f;
                    transform.localScale = m_BaseScale;
                }
                else
                {
                    transform.localScale = m_BaseScale * m_BumpScale.Evaluate(bt);
                    any = true;
                }
            }
            WriteAll();
            if (m_Moving && !any)
            {
                m_Moving = false;
                MoveEnded?.Invoke(this);
            }
            return true;
        }

        void IPlaybackRunner.Abort(int state, AbortReason reason)
        {
            m_Running = false;
            m_Handle = default;
        }

        UnityEngine.Object IPlaybackRunner.DebugOwner => this;

        bool Horizontal => m_From == ScreenSide.Left || m_From == ScreenSide.Right;

        void WriteAll()
        {
            for (int i = 0; i < m_Bars.Length; i++)
                Write(ref m_Bars[i]);
        }

        void Write(ref Bar b)
        {
            if (b.Rect == null)
                return;
            float f = Mathf.Lerp(m_FillRange.x, m_FillRange.y, b.Shown);
            switch (m_Fill)
            {
                case ProgressBarFill.LocalScale:
                {
                    Vector3 s = b.Rect.localScale;
                    if (Horizontal)
                        s.x = f;
                    else
                        s.y = f;
                    b.Rect.localScale = s;
                    break;
                }
                case ProgressBarFill.FillAmount:
                    if (b.Image != null)
                        b.Image.fillAmount = f;
                    break;
                case ProgressBarFill.Width:
                    b.Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, f * b.Size.x);
                    break;
                case ProgressBarFill.Height:
                    b.Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, f * b.Size.y);
                    break;
                case ProgressBarFill.Anchor:
                {
                    Vector2 min = b.Rect.anchorMin, max = b.Rect.anchorMax;
                    switch (m_From)
                    {
                        case ScreenSide.Left: min.x = 0f; max.x = f; break;
                        case ScreenSide.Right: min.x = 1f - f; max.x = 1f; break;
                        case ScreenSide.Bottom: min.y = 0f; max.y = f; break;
                        case ScreenSide.Top: min.y = 1f - f; max.y = 1f; break;
                    }
                    b.Rect.anchorMin = min;
                    b.Rect.anchorMax = max;
                    break;
                }
            }
        }

        void WriteText()
        {
            if (m_Text == null || m_TextFormat == null)
                return;
            int n = m_TextFormat.Write(m_Target * m_TextMultiplier, m_Chars);
            m_Text.SetText(m_Chars, 0, n);
        }
    }
}
