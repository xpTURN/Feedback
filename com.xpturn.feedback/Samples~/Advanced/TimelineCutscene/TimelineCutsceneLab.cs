using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.UI;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.Advanced
{
    // S14 Unity Timeline Cutscene: one director plays a timeline of Feedback tracks — a Driven camera shake and the hero's
    // looping spring follow the timeline's time (a scrub, a pause and the director's speed show the same values), a Triggered
    // screen flash starts with its clip and runs on its player's clock, and a Signal calls the burst's PlayFromEvent through a
    // Signal Receiver. The lab moves the director only: play from the start, pause and resume, scrub (a scrub pauses it). A
    // play keeps the categories it began with — Before and After start the cutscene over (its clips entered again)
    public sealed class TimelineCutsceneLab : MonoBehaviour
    {
        const float SeekStep = 0.25f;
        const string PlayingText = "Playing — the cutscene loops";
        const string PausedText = "Paused — drag the slider or Z · X: the Driven clips follow, the flash holds";
        static readonly string[] s_Seconds = Seconds();

        [SerializeField] PlayableDirector m_Director;
        [SerializeField] Slider m_Seek;
        [SerializeField] Text m_Time;
        [SerializeField] Text m_Status;
        [SerializeField] Text m_PauseLabel;

        [NonSerialized] InputAction m_Restart, m_Pause, m_Back, m_Forth;
        int m_ShownTenth = -1;
        string m_ShownStatus;
        bool m_WasAfter = true;

        internal bool Paused => m_Director != null && m_Director.state != PlayState.Playing;

        static string[] Seconds()
        {
            var s = new string[101];
            for (int i = 0; i < s.Length; i++)
                s[i] = (i / 10f).ToString("0.0") + " s";
            return s;
        }

        void Start()
        {
            if (m_Seek != null && m_Director != null)
            {
                m_Seek.minValue = 0f;
                m_Seek.maxValue = (float)m_Director.duration;
            }
        }

        void Update()
        {
            if (m_Restart == null)
            {
                m_Restart = SampleInput.Find("Action1");
                m_Pause = SampleInput.Find("Action2");
                m_Back = SampleInput.Find("Option1");
                m_Forth = SampleInput.Find("Option2");
            }
            if (Pressed(m_Restart))
                PlayFromStart();
            if (Pressed(m_Pause))
                TogglePause();
            if (Pressed(m_Back))
                Nudge(-SeekStep);
            if (Pressed(m_Forth))
                Nudge(SeekStep);
            bool after = SampleState.After;
            if (after != m_WasAfter)
            {
                m_WasAfter = after;
                PlayFromStart();
            }
            Show();
        }

        // a key or the pad — a mouse click is the buttons' (Action1's mouse button is a click anywhere)
        static bool Pressed(InputAction a) => a != null && a.WasPressedThisFrame() && !(a.activeControl != null && a.activeControl.device is Pointer);

        public void PlayFromStart()
        {
            if (m_Director == null)
                return;
            m_Director.Stop();
            m_Director.time = 0.0;
            m_Director.Play();
        }

        public void TogglePause()
        {
            if (m_Director == null)
                return;
            if (m_Director.state == PlayState.Playing)
                m_Director.Pause();
            else if (m_Director.playableGraph.IsValid())
                m_Director.Resume();
            else
                m_Director.Play();
        }

        // the slider and Z · X: the director's time — the Driven clips seek to it, a Triggered clip's play stands with the
        // paused director
        public void OnSeek(float seconds)
        {
            if (m_Director == null)
                return;
            if (m_Director.state == PlayState.Playing)
                m_Director.Pause();
            m_Director.time = Mathf.Clamp(seconds, 0f, (float)m_Director.duration);
            m_Director.Evaluate();
        }

        void Nudge(float by)
        {
            if (m_Director != null)
                OnSeek((float)m_Director.time + by);
        }

        void Show()
        {
            if (m_Director == null)
                return;
            double t = m_Director.time;
            int tenth = Mathf.Clamp((int)(t * 10.0 + 0.5), 0, s_Seconds.Length - 1);
            if (m_Time != null && tenth != m_ShownTenth)
            {
                m_ShownTenth = tenth;
                m_Time.text = s_Seconds[tenth];
            }
            if (m_Seek != null)
                m_Seek.SetValueWithoutNotify((float)t);
            bool paused = m_Director.state != PlayState.Playing;
            string status = paused ? PausedText : PlayingText;
            if (m_Status != null && !ReferenceEquals(status, m_ShownStatus))
            {
                m_ShownStatus = status;
                m_Status.text = status;
            }
            if (m_PauseLabel != null)
            {
                string label = paused ? "Resume" : "Pause";
                if (!ReferenceEquals(m_PauseLabel.text, label))
                    m_PauseLabel.text = label;
            }
        }
    }
}
