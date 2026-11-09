using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.Advanced
{
    // S11 Stress & Zero-GC: N copies of the template (a cube with a player — one long Position Punch on itself, the performance
    // test's shape) are built 500 a frame, each played once, and destroyed when N drops (a disabled player would keep playing —
    // the template stops on disable). That steady state is the performance test's; 'Re-trigger' plays every cube again once a
    // second (N / 60 a frame — starts and stops, not the performance test's measure). The meter is this sample's own: the
    // frame's allocation against the baseline taken when the comparison was last switched (the HUD's line subtracts its own
    // minimum, which hides a steady allocation), the three Feedback markers, the main thread, the plays alive (Played −
    // Completed − Stopped — a Restart and a destroy both raise Stopped). The coroutine baseline drives the same cubes with a
    // coroutine per punch instead of the players
    public sealed class StressLab : MonoBehaviour
    {
        const int BuildPerFrame = 500;
        const int Window = 60;
        const int MinWindow = 120;
        static readonly int[] s_Steps = { 0, 100, 1000, 5000 };
        static readonly string[] s_Ms = Numbers(2001, i => (i / 100f).ToString("0.00") + " ms");
        static readonly string[] s_Bytes = Numbers(157, i => "+" + (i * 64) + " B");
        static readonly string[] s_Count = Numbers(5001, i => i.ToString());

        [SerializeField] GameObject m_Template;
        [SerializeField] CoroutineBaseline m_Baseline;
        [SerializeField] Text m_Gc;
        [SerializeField] Text m_Feedback;
        [SerializeField] Text m_Main;
        [SerializeField] Text m_Alive;
        [SerializeField] Text m_Players;
        [SerializeField] Text m_Peak;
        [SerializeField] Text m_Mode;
        [SerializeField] Text m_Retriggered;
        [SerializeField] int m_StartCount = 100;

        readonly List<FeedbackPlayer> m_Made = new List<FeedbackPlayer>(5000);
        [NonSerialized] InputAction[] m_Picks;
        [NonSerialized] InputAction m_Toggle, m_Again;
        Action<FeedbackPlayer, PlaybackHandle> m_OnPlayed, m_OnEnded;
        ProfilerRecorder m_GcRec, m_Revert, m_Update, m_Late, m_MainRec;
        int m_Target;
        int m_Frame;
        int m_Plays;
        bool m_Coroutines;
        bool m_Retrigger;
        long m_GcSum, m_FeedbackSum, m_MainSum, m_Baseline0;
        long m_Min = long.MaxValue, m_PeakBytes;
        readonly long[] m_Recent = new long[MinWindow];
        int m_Filled;

        internal int Target => m_Target;
        internal int Count => m_Made.Count;
        internal int Alive => m_Plays;
        internal bool Coroutines => m_Coroutines;
        internal bool Retrigger => m_Retrigger;
        internal long LastGc => m_GcRec.Valid ? m_GcRec.LastValue : -1;
        internal long Baseline => m_Baseline0;

        static string[] Numbers(int n, Func<int, string> f)
        {
            var s = new string[n];
            for (int i = 0; i < n; i++)
                s[i] = f(i);
            return s;
        }

        void Awake()
        {
            m_OnPlayed = (p, h) => m_Plays++;
            // a destroyed copy's play ends with Stopped at the frame's end (the template stops on disable) — the count comes down by
            // itself
            m_OnEnded = (p, h) => m_Plays--;
        }

        void OnEnable()
        {
            m_GcRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            m_Revert = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Feedback.Revert");
            m_Update = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Feedback.Update");
            m_Late = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Feedback.LateApply");
            m_MainRec = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
        }

        void OnDisable()
        {
            m_GcRec.Dispose();
            m_Revert.Dispose();
            m_Update.Dispose();
            m_Late.Dispose();
            m_MainRec.Dispose();
        }

        void Start() => SetCount(m_StartCount);

        public void SetCount(int n)
        {
            m_Target = Mathf.Clamp(n, 0, 5000);
            while (m_Made.Count > m_Target)
            {
                int last = m_Made.Count - 1;
                if (m_Made[last] != null)
                    Destroy(m_Made[last].gameObject);
                m_Made.RemoveAt(last);
            }
            if (m_Baseline != null)
                m_Baseline.StopAll();
            Regrid();
            if (m_Coroutines)
                KickAll();
        }

        Vector3 Place(int i)
        {
            int side = m_Target <= 100 ? 10 : m_Target <= 1000 ? 32 : 71;
            return new Vector3(i % side - side * 0.5f, 0f, i / side - side * 0.5f) * 0.6f;
        }

        // a stopped coroutine leaves its cube where it was
        void Regrid()
        {
            for (int i = 0; i < m_Made.Count; i++)
                if (m_Made[i] != null)
                    m_Made[i].transform.localPosition = Place(i);
        }

        public void SetStep(int step) => SetCount(s_Steps[Mathf.Clamp(step, 0, s_Steps.Length - 1)]);

        public void ToggleCoroutines() => SetCoroutines(!m_Coroutines);

        public void ToggleRetrigger() => SetRetrigger(!m_Retrigger);

        public void SetRetrigger(bool on) => m_Retrigger = on;

        // the steady state for the coroutines: one long punch each (it allocates when it starts, not after)
        void KickAll()
        {
            if (m_Baseline == null || m_Retrigger)
                return;
            foreach (FeedbackPlayer p in m_Made)
                if (p != null)
                    m_Baseline.Kick(p.transform, 1000f);
        }

        public void SetCoroutines(bool on)
        {
            if (on == m_Coroutines)
                return;
            m_Baseline0 = m_Min == long.MaxValue ? 0 : m_Min;
            m_Coroutines = on;
            if (on)
            {
                foreach (FeedbackPlayer p in m_Made)
                    if (p != null)
                        p.Stop(StopMode.Revert);
                KickAll();
            }
            else
            {
                if (m_Baseline != null)
                    m_Baseline.StopAll();
                Regrid();
                foreach (FeedbackPlayer p in m_Made)
                    if (p != null)
                        p.Play();
            }
        }

        void Update()
        {
            if (m_Picks == null)
            {
                m_Picks = new[] { SampleInput.Find("Option1"), SampleInput.Find("Option2"), SampleInput.Find("Option3"), SampleInput.Find("Option4") };
                m_Toggle = SampleInput.Find("Action2");
                m_Again = SampleInput.Find("Action3");
            }
            for (int i = 0; i < m_Picks.Length; i++)
                if (m_Picks[i] != null && m_Picks[i].WasPressedThisFrame())
                    SetStep(i);
            if (m_Toggle != null && m_Toggle.WasPressedThisFrame())
                ToggleCoroutines();
            if (m_Again != null && m_Again.WasPressedThisFrame())
                ToggleRetrigger();

            Build();
            Replay();
            Measure();
        }

        void Build()
        {
            if (m_Template == null)
                return;
            for (int k = 0; k < BuildPerFrame && m_Made.Count < m_Target; k++)
            {
                int i = m_Made.Count;
                GameObject go = Instantiate(m_Template, transform, false);
                go.transform.localPosition = Place(i);
                go.SetActive(true);
                var p = go.GetComponent<FeedbackPlayer>();
                p.Played += m_OnPlayed;
                p.Completed += m_OnEnded;
                p.Stopped += m_OnEnded;
                m_Made.Add(p);
                if (m_Coroutines)
                {
                    if (m_Baseline != null && !m_Retrigger)
                        m_Baseline.Kick(p.transform, 1000f);
                }
                else
                    p.Play();
            }
        }

        // once a second for every cube: N / 60 a frame
        void Replay()
        {
            int n = m_Made.Count;
            if (n == 0 || !m_Retrigger)
                return;
            int slot = m_Frame % Window;
            int from = slot * n / Window, to = (slot + 1) * n / Window;
            for (int i = from; i < to; i++)
            {
                FeedbackPlayer p = m_Made[i];
                if (p == null)
                    continue;
                if (m_Coroutines)
                {
                    if (m_Baseline != null)
                        m_Baseline.Kick(p.transform, 1f);
                }
                else
                    p.Play();
            }
        }

        void Measure()
        {
            m_Frame++;
            long gc = m_GcRec.Valid ? m_GcRec.LastValue : 0;
            m_Recent[m_Frame % MinWindow] = gc;
            if (m_Filled < MinWindow)
                m_Filled++;
            m_GcSum += gc;
            m_FeedbackSum += (m_Revert.Valid ? m_Revert.LastValue : 0) + (m_Update.Valid ? m_Update.LastValue : 0) + (m_Late.Valid ? m_Late.LastValue : 0);
            m_MainSum += m_MainRec.Valid ? m_MainRec.LastValue : 0;
            if (m_Frame % Window != 0)
                return;
            m_Min = long.MaxValue;
            for (int i = 0; i < m_Filled; i++)
                m_Min = Math.Min(m_Min, m_Recent[i]);
            long avg = m_GcSum / Window - m_Baseline0;
            m_PeakBytes = Math.Max(m_PeakBytes, avg);
            Set(m_Gc, s_Bytes[Mathf.Clamp((int)(Math.Max(0, avg) / 64), 0, s_Bytes.Length - 1)]);
            Set(m_Peak, s_Bytes[Mathf.Clamp((int)(Math.Max(0, m_PeakBytes) / 64), 0, s_Bytes.Length - 1)]);
            Set(m_Feedback, s_Ms[Mathf.Clamp((int)(m_FeedbackSum / Window / 10000), 0, s_Ms.Length - 1)]);
            Set(m_Main, s_Ms[Mathf.Clamp((int)(m_MainSum / Window / 10000), 0, s_Ms.Length - 1)]);
            Set(m_Alive, s_Count[Mathf.Clamp(m_Plays, 0, s_Count.Length - 1)]);
            Set(m_Retriggered, m_Retrigger ? "Re-trigger every second (starts and stops)" : "Steady — each played once (the performance test's form)");
            Set(m_Players, s_Count[Mathf.Clamp(m_Made.Count, 0, s_Count.Length - 1)]);
            Set(m_Mode, m_Coroutines ? "Coroutine baseline (allocates while on)" : SampleState.After ? "Feedback players" : "Before — the players are left out (nothing to measure)");
            m_GcSum = m_FeedbackSum = m_MainSum = 0;
        }

        static void Set(Text t, string s)
        {
            if (t != null && !ReferenceEquals(t.text, s))
                t.text = s;
        }
    }
}
