using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace xpTURN.Feedback.Samples.Fixture
{
    // a player run with -stressTable (clones only — never shipped; -sampleTour's tour stays apart): the hub opens Stress Zero
    // GC and this walks its matrix at 60 fps with the HUD off, -stressPasses times (two by default). A pass: N 0 twice (the
    // baseline — the smaller), N 100 · 1,000 · 5,000 × played once · played again every second × players · coroutines, N 0
    // again (the baseline holds). Each cell is set in the sample's own order (coroutines off, the count, re-trigger, the
    // drive), waits for its players, warms 120 frames and reads 300 with its own recorders — the frame allocation and the
    // package's and the coroutines' markers; a players cell, or the pass's last N 0, past the baseline by more than 32 B
    // reads its window again, twice at most. A line a cell (tools/samples/stress_table.py judges them); then it quits
    public sealed class StressTable : MonoBehaviour
    {
        const string StressLab = "xpTURN.Feedback.Samples.Advanced.StressLab, xpTURN.Feedback.Samples.Advanced.StressZeroGC";
        const int Warm = 120;
        const int Window = 300;
        const int Retries = 2;
        const int PassCells = 15;
        static readonly string[] s_Markers =
        {
            "Feedback.Revert", "Feedback.Update", "Feedback.LateApply", "CoroutinesDelayedCalls", "PlayerLoop", "WaitForTargetFPS",
            "GC.Collect",
        };

        struct Cell
        {
            public int N;
            public bool Retrigger, Coroutines;
        }

        readonly List<Cell> m_Cells = new List<Cell>();
        int m_Cell = -1, m_Passes = 2, m_Retry;
        int m_Phase, m_Frame, m_QuitAt = -1;
        Component m_Lab;
        Action<int> m_SetCount;
        Action<bool> m_SetRetrigger, m_SetCoroutines;
        ProfilerRecorder m_Gc;
        ProfilerRecorder[] m_Recorders;
        long[] m_RecorderSum;
        long m_GcSum, m_GcMax, m_FirstZero, m_Base;
        int m_Allocs;
        double m_Seconds;
        readonly int[] m_AllocAt = new int[16];
        readonly long[] m_AllocBytes = new long[16];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-stressTable") < 0)
                return;
            var go = new GameObject("Stress Table", typeof(StressTable));
            DontDestroyOnLoad(go);
            int i = Array.IndexOf(args, "-stressPasses");
            if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int n))
                go.GetComponent<StressTable>().m_Passes = Math.Max(1, n);
        }

        // the markers by name — a missing one is written down as absent (a release player has none)
        void OnEnable()
        {
            m_Gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var found = new Dictionary<string, ProfilerRecorderDescription>();
            foreach (ProfilerRecorderHandle h in handles)
            {
                ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);
                if (Array.IndexOf(s_Markers, d.Name) >= 0 && !found.ContainsKey(d.Name))
                    found.Add(d.Name, d);
            }
            m_Recorders = new ProfilerRecorder[s_Markers.Length];
            m_RecorderSum = new long[s_Markers.Length];
            var line = new StringBuilder("STRESS markers");
            for (int i = 0; i < s_Markers.Length; i++)
            {
                bool has = found.TryGetValue(s_Markers[i], out ProfilerRecorderDescription d);
                if (has)
                    m_Recorders[i] = ProfilerRecorder.StartNew(d.Category, d.Name);
                line.Append(' ').Append(s_Markers[i]).Append('=').Append(has ? (m_Recorders[i].Valid ? "ok" : "invalid") : "absent");
            }
            Debug.Log(line.ToString());
        }

        void OnDisable()
        {
            m_Gc.Dispose();
            foreach (ProfilerRecorder r in m_Recorders)
                r.Dispose();
        }

        void Update()
        {
            m_Frame++;
            if (m_QuitAt >= 0)
            {
                if (m_Frame >= m_QuitAt)
                    Application.Quit(0);
                return;
            }
            switch (m_Phase)
            {
                case 0: Hub(); break;
                case 1: Load(); break;
                case 2: Build(); break;
                case 3:
                    if (m_Frame >= Warm)
                    {
                        m_Phase = 4;
                        m_Frame = 0;
                        Clear();
                    }
                    break;
                case 4: Read(); break;
            }
        }

        void Hub()
        {
            if (m_Frame < 30 || SampleHub.Instance == null)
                return;
            foreach (SampleCard card in SampleHub.Instance.Cards)
                if (card != null && card.SceneName == "StressZeroGC" && SampleCards.Available(card))
                {
                    SampleHub.Open(card);
                    m_Phase = 1;
                    m_Frame = 0;
                    return;
                }
            Debug.Log("STRESS no card");
            m_QuitAt = m_Frame + 30;
        }

        void Load()
        {
            Type t = Type.GetType(StressLab);
            if (t == null)
                return;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(t))
                if (o is Component c && c.gameObject.scene == SceneManager.GetActiveScene())
                    m_Lab = c;
            if (m_Lab == null || m_Frame < 10)
                return;
            m_SetCount = (Action<int>)Delegate.CreateDelegate(typeof(Action<int>), m_Lab, "SetCount");
            m_SetRetrigger = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), m_Lab, "SetRetrigger");
            m_SetCoroutines = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), m_Lab, "SetCoroutines");
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            if (SampleHud.Instance != null)
                SampleHud.Instance.enabled = false;
            Debug.Log($"STRESS env unity={Application.unityVersion} cpu='{SystemInfo.processorType}' model={SystemInfo.deviceModel} arch={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} debug={Debug.isDebugBuild} il2cpp={Il2Cpp()} passes={m_Passes}");
            for (int p = 0; p < m_Passes; p++)
            {
                m_Cells.Add(new Cell());
                m_Cells.Add(new Cell());
                foreach (int n in new[] { 100, 1000, 5000 })
                {
                    m_Cells.Add(new Cell { N = n });
                    m_Cells.Add(new Cell { N = n, Coroutines = true });
                    m_Cells.Add(new Cell { N = n, Retrigger = true });
                    m_Cells.Add(new Cell { N = n, Retrigger = true, Coroutines = true });
                }
                m_Cells.Add(new Cell());
            }
            Next();
        }

        static bool Il2Cpp()
        {
#if ENABLE_IL2CPP
            return true;
#else
            return false;
#endif
        }

        void Next()
        {
            m_Cell++;
            m_Retry = 0;
            if (m_Cell >= m_Cells.Count)
            {
                Debug.Log("STRESS done");
                m_QuitAt = m_Frame + 30;
                return;
            }
            Cell c = m_Cells[m_Cell];
            m_SetCoroutines(false);
            m_SetCount(c.N);
            m_SetRetrigger(c.Retrigger);
            m_SetCoroutines(c.Coroutines);
            m_Phase = 2;
            m_Frame = 0;
        }

        void Build()
        {
            int count = Count(out _);
            if (count == m_Cells[m_Cell].N || m_Frame > 120)
            {
                Debug.Log($"STRESS built n={m_Cells[m_Cell].N} count={count} frames={m_Frame} heap={GC.GetTotalMemory(false)}");
                m_Phase = 3;
                m_Frame = 0;
            }
        }

        // the scene's active players and those playing — read outside the windows (it allocates)
        static int Count(out int playing)
        {
            int n = 0;
            playing = 0;
            foreach (FeedbackPlayer p in Resources.FindObjectsOfTypeAll<FeedbackPlayer>())
                if (p.gameObject.scene == SceneManager.GetActiveScene() && p.gameObject.activeInHierarchy)
                {
                    n++;
                    if (p.IsPlaying)
                        playing++;
                }
            return n;
        }

        void Clear()
        {
            m_GcSum = m_GcMax = 0;
            m_Allocs = 0;
            m_Seconds = 0;
            Array.Clear(m_RecorderSum, 0, m_RecorderSum.Length);
        }

        void Read()
        {
            long v = m_Gc.Valid ? m_Gc.LastValue : -1;
            m_GcSum += v;
            m_GcMax = Math.Max(m_GcMax, v);
            if (v > 0)
            {
                if (m_Allocs < m_AllocAt.Length)
                {
                    m_AllocAt[m_Allocs] = m_Frame;
                    m_AllocBytes[m_Allocs] = v;
                }
                m_Allocs++;
            }
            for (int i = 0; i < m_Recorders.Length; i++)
                if (m_Recorders[i].Valid)
                    m_RecorderSum[i] += m_Recorders[i].LastValue;
            m_Seconds += Time.unscaledDeltaTime;
            if (m_Frame < Window)
                return;
            Cell c = m_Cells[m_Cell];
            int slot = m_Cell % PassCells;
            if (slot == 0)
                m_FirstZero = m_GcSum;
            else if (slot == 1)
                m_Base = Math.Min(m_FirstZero, m_GcSum);
            var line = new StringBuilder();
            line.Append($"STRESS cell pass={m_Cell / PassCells + 1} i={slot} n={c.N} mode={(c.Retrigger ? "retrigger" : "steady")} drive={(c.Coroutines ? "coroutines" : "players")} retry={m_Retry} recorder={m_Gc.Valid} gcSum={m_GcSum} gcAvg={m_GcSum / Window} gcMax={m_GcMax} allocFrames={m_Allocs} fps={Window / m_Seconds:0.0}");
            for (int i = 0; i < m_Recorders.Length; i++)
                if (m_Recorders[i].Valid)
                    line.Append(' ').Append(s_Markers[i]).Append('=').Append((m_RecorderSum[i] / (double)Window / 1e6).ToString("0.000"));
            int players = Count(out int playing);
            line.Append($" count={players} playing={playing} heap={GC.GetTotalMemory(false)} list=[");
            for (int i = 0; i < Math.Min(m_Allocs, m_AllocAt.Length); i++)
                line.Append(m_AllocAt[i]).Append(':').Append(m_AllocBytes[i]).Append(' ');
            line.Append(']');
            Debug.Log(line.ToString());
            // a window read again starts after a short settle — the line above allocates in this frame
            bool judged = !c.Coroutines && (c.N > 0 || slot == PassCells - 1);
            if (judged && Math.Abs(m_GcSum - m_Base) > 32 && m_Retry < Retries)
            {
                m_Retry++;
                m_Phase = 3;
                m_Frame = Warm - 10;
                return;
            }
            Next();
        }
    }
}
