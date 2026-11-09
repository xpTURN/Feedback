using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.Advanced
{
    // S8 Timeline & Flow: the Flight list counts down, charges, waits for the launch signal (2 s — then it launches itself), lifts off
    // with a stage loop ×3 and ends at altitude; its Player Control items start the endless Flame forward and stop it on the way back.
    // The bottom timeline is this sample's table of the Flight's items (a test checks it against the list) and its own clock: the
    // play's position has no public read, so the clock adds Time.deltaTime from the Play (the Flight is Scaled — the HUD's time scale
    // moves both alike), stands while paused, takes a seek's value and, after an early launch, the pull of the wait cut short
    public sealed class TimelineFlowLab : MonoBehaviour
    {
        [Serializable]
        public struct Row
        {
            public string Name;
            public float Start;
            public float Duration;
            // 0 with previous · 1 after previous · 2 after all (the list's start rule — its colour on the timeline)
            public int Rule;
            public bool Wait;
        }

        const float SeekStep = 0.5f;
        // the slider's end — a seek right at the end finishes the play
        const float EndMargin = 0.05f;
        static readonly string[] s_Seconds = Seconds();

        [SerializeField] FeedbackPlayer m_Flight;
        [SerializeField] FeedbackPlayer m_Flame;
        [SerializeField] FeedbackChannel m_Launch;
        [SerializeField] Row[] m_Rows = new Row[0];
        [SerializeField] float m_Total = 8.7f;
        // lift-off on the nominal axis — the Flame's state follows a seek across it
        [SerializeField] float m_Ignition = 5.8f;
        [SerializeField] RectTransform m_Track;
        [SerializeField] RectTransform m_Head;
        [SerializeField] Slider m_Seek;
        [SerializeField] Text m_Time;
        [SerializeField] Text m_Status;
        [SerializeField] Text m_PauseLabel;

        [NonSerialized] InputAction m_Act, m_Pause, m_Land, m_Back, m_Forth;
        double m_Clock;
        double m_Pull;
        bool m_Running, m_Backward, m_Paused, m_OffNominal, m_AtTop, m_WasAfter = true;
        int m_PlayedFrame = -1;
        int m_ShownSecond = -1;
        string m_ShownStatus;

        internal double Clock => m_Clock + m_Pull;
        internal bool OffNominal => m_OffNominal;
        internal bool Backward => m_Backward;

        static string[] Seconds()
        {
            var s = new string[201];
            for (int i = 0; i < s.Length; i++)
                s[i] = (i / 10f).ToString("0.0") + " s";
            return s;
        }

        void OnEnable()
        {
            if (m_Flight == null)
                return;
            m_Flight.Played += OnPlayed;
            m_Flight.Completed += OnCompleted;
            m_Flight.Stopped += OnEnded;
        }

        void OnDisable()
        {
            if (m_Flight == null)
                return;
            m_Flight.Played -= OnPlayed;
            m_Flight.Completed -= OnCompleted;
            m_Flight.Stopped -= OnEnded;
        }

        void OnPlayed(FeedbackPlayer p, PlaybackHandle h)
        {
            m_Running = true;
            m_Paused = false;
            m_Pull = 0;
            m_OffNominal = false;
            // the scheduler moves a play taken in this frame from the next one
            m_PlayedFrame = Time.frameCount;
            m_Clock = m_Backward ? m_Total : 0;
        }

        // a forward play that completes is at the top — the event comes before LateUpdate adds the last frame to the clock
        void OnCompleted(FeedbackPlayer p, PlaybackHandle h)
        {
            m_Running = p.IsPlaying;
            if (!m_Running)
                m_AtTop = !m_Backward;
        }

        // a Restart's stop of the forward play comes inside the backward Play — either order leaves IsPlaying right
        void OnEnded(FeedbackPlayer p, PlaybackHandle h)
        {
            m_Running = p.IsPlaying;
            if (!m_Running)
                m_AtTop = false;
        }

        void Update()
        {
            if (m_Act == null)
            {
                m_Act = SampleInput.Find("Action1");
                m_Pause = SampleInput.Find("Action2");
                m_Land = SampleInput.Find("Action3");
                m_Back = SampleInput.Find("Option1");
                m_Forth = SampleInput.Find("Option2");
            }
            // Action1's mouse button is a click anywhere — the buttons are the mouse's way
            if (m_Act != null && m_Act.WasPressedThisFrame() && !(m_Act.activeControl != null && m_Act.activeControl.device is Pointer))
                PlayOrLaunch();
            if (Pressed(m_Pause))
                TogglePause();
            if (Pressed(m_Land))
                Land();
            if (Pressed(m_Back))
                Nudge(-SeekStep);
            if (Pressed(m_Forth))
                Nudge(SeekStep);
            // an endless play begun under Before keeps its items out after After comes back — the Flame starts over
            bool after = SampleState.After;
            if (after && !m_WasAfter && m_Flame != null && m_Flame.IsPlaying)
            {
                m_Flame.Stop(StopMode.Hold);
                m_Flame.Play();
                if (m_Paused)
                    m_Flame.Pause();
            }
            m_WasAfter = after;
        }

        // after the scheduler's step (the start of PreLateUpdate): a pause, a resume or a play from any Update or coroutine of this
        // frame is in it — the clock takes the frame's delta exactly when the play did
        void LateUpdate()
        {
            if (m_Running && !m_Paused && Time.frameCount > m_PlayedFrame)
            {
                m_Clock += (m_Backward ? -1.0 : 1.0) * Time.deltaTime;
                m_Clock = Math.Max(0.0, Math.Min(m_Total, m_Clock));
            }
            Show();
        }

        static bool Pressed(InputAction a) => a != null && a.WasPressedThisFrame() && !(a.activeControl != null && a.activeControl.device is Pointer && SampleInput.PointerOverUI);

        bool Armed
        {
            get
            {
                if (!m_Running || m_Backward || m_Paused || m_Pull > 0)
                    return false;
                foreach (Row r in m_Rows)
                    if (r.Wait)
                        return m_Clock >= r.Start && m_Clock < r.Start + r.Duration;
                return false;
            }
        }

        // Action1: while the Flight flies forward a launch only (outside the wait it is dropped), else the Flight from the start
        public void PlayOrLaunch()
        {
            if (m_Running && !m_Backward)
                Launch();
            else
                PlayForward();
        }

        public void PlayForward()
        {
            if (m_Flight == null)
                return;
            m_Backward = false;
            m_AtTop = false;
            // a Restart ends the play before with Hold, which leaves the rise's height, and a completed play holds the top (a Stop
            // does nothing to it) — Restore puts the targets back: the Flight starts from the pad
            m_Flight.Restore();
            if (m_Flame != null)
                m_Flame.Stop(StopMode.Revert);
            m_Flight.Play();
        }

        // the channel's signal command — a press outside the wait is dropped without a warning
        public void Launch()
        {
            if (m_Launch == null)
                return;
            if (Armed)
            {
                foreach (Row r in m_Rows)
                    if (r.Wait)
                    {
                        // the wait ends now — the rest of the list comes this much early on the nominal axis
                        m_Pull = r.Start + r.Duration - m_Clock;
                        m_OffNominal = m_Pull > 0.001;
                        break;
                    }
            }
            FeedbackChannels.Send(new FeedbackCommand(FeedbackCommandKind.Signal, ChannelRef.Of(m_Launch)));
        }

        public void TogglePause()
        {
            if (!m_Running || m_Flight == null)
                return;
            m_Paused = !m_Paused;
            if (m_Paused)
            {
                m_Flight.Pause();
                if (m_Flame != null)
                    m_Flame.Pause();
            }
            else
            {
                m_Flight.Resume();
                if (m_Flame != null)
                    m_Flame.Resume();
            }
        }

        // the way back: from the top when the Flight ended there, mid-flight from where it is (Backward + a seek in the same
        // frame)
        public void Land()
        {
            if (m_Flight == null || m_Backward && m_Running)
                return;
            if (!m_Running && !m_AtTop)
                return;
            double from = m_Running ? m_Clock + m_Pull : m_Total;
            // before lift-off there is nothing to land
            if (from < m_Ignition)
                return;
            if (m_Flame != null && !m_Flame.IsPlaying)
                m_Flame.Play();
            else if (m_Flame != null)
                m_Flame.Resume();
            bool paused = m_Paused;
            m_Backward = true;
            m_Flight.Play(new PlayArgs().WithDirection(PlayerDirection.Backward));
            if (from < m_Total - 0.001)
            {
                m_Flight.Seek((float)from);
                m_Clock = from;
            }
            m_Pull = 0;
            m_OffNominal = false;
            m_AtTop = false;
            if (paused)
            {
                m_Paused = true;
                m_Flight.Pause();
            }
        }

        // the slider and Z · X: a forward Flight on its nominal axis, paused. A seek across lift-off leaves the Flame as it was (its
        // Player Control items do nothing when a seek crosses them backward), so the sample puts it where the head is
        public void OnSeek(float seconds)
        {
            if (!m_Running || m_Backward || !m_Paused || m_Flight == null)
                return;
            float to = Mathf.Clamp(seconds, 0f, m_Total - EndMargin);
            if (!m_OffNominal && m_Flight.Seek(to))
            {
                m_Clock = to;
                FlameAt(to);
            }
            else
                m_OffNominal = true;
        }

        void FlameAt(double head)
        {
            if (m_Flame == null)
                return;
            if (head < m_Ignition)
                m_Flame.Stop(StopMode.Revert);
            else if (!m_Flame.IsPlaying)
            {
                m_Flame.Play();
                m_Flame.Pause();
            }
        }

        void Nudge(float by)
        {
            if (m_Running && m_Paused && !m_Backward)
                OnSeek((float)(m_Clock + m_Pull) + by);
        }

        void Show()
        {
            double head = m_Clock + m_Pull;
            if (m_Head != null && m_Track != null && m_Total > 0f)
            {
                float x = m_Track.rect.width * (float)(head / m_Total);
                m_Head.anchoredPosition = new Vector2(x, m_Head.anchoredPosition.y);
            }
            int tenth = Mathf.Clamp((int)(head * 10.0 + 0.5), 0, s_Seconds.Length - 1);
            if (m_Time != null && tenth != m_ShownSecond)
            {
                m_ShownSecond = tenth;
                m_Time.text = s_Seconds[tenth];
            }
            if (m_Seek != null)
            {
                m_Seek.SetValueWithoutNotify((float)head);
                m_Seek.interactable = m_Running && m_Paused && !m_Backward && !m_OffNominal;
            }
            string status = !m_Running ? (m_AtTop ? "At altitude — Land (3) flies it back down" : "Ready — Play (Space) starts the countdown")
                : m_Backward ? (m_Paused ? "Landing — paused" : "Landing — the list plays backward")
                : m_OffNominal ? "Early launch — the play left its nominal axis: seek is refused"
                : Armed ? "Launch armed — press Launch (Space) or wait 2 s"
                : m_Paused ? "Paused — drag the slider or Z · X to seek"
                : "Flying";
            if (m_Status != null && !ReferenceEquals(status, m_ShownStatus))
            {
                m_ShownStatus = status;
                m_Status.text = status;
            }
            if (m_PauseLabel != null)
            {
                string label = m_Paused ? "Resume" : "Pause";
                if (!ReferenceEquals(m_PauseLabel.text, label))
                    m_PauseLabel.text = label;
            }
        }
    }
}
