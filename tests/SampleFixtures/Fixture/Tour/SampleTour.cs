using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace xpTURN.Feedback.Samples.Fixture
{
    // a player run with -sampleTour (clones only — never shipped): the hub opens each available card and comes back. At each
    // stop its own recorder (an oracle apart from the HUD) reads the frame allocation with the HUD on, then off (the same run's
    // baseline), and once a window that allocates 1 KB a frame (the positive control); a stop whose on − off passes 32 B reads
    // both windows again, twice at most. Stress Zero GC is raised to 1,000 players before its windows; the Showcase plays its
    // game instead — 1,500 frames at 60 fps with the HUD off and Launch every frame — counting the plays and listing the frames
    // that allocated. Each stop counts the scene's persistent UnityEvent calls — a component's or a feedback item's — and names
    // those whose method the player lacks, and its scene's plays (its players' Played from the stop's first frame · the players
    // playing at its end); then it quits
    public sealed class SampleTour : MonoBehaviour
    {
        const int Retries = 2;
        const int GameFrames = 1500;
        const string StressLab = "xpTURN.Feedback.Samples.Advanced.StressLab, xpTURN.Feedback.Samples.Advanced.StressZeroGC";
        const string BrickLab = "xpTURN.Feedback.Samples.Showcase.BrickBreakerLab, xpTURN.Feedback.Samples.Showcase.BrickBreaker";
        static int s_Next;
        static bool s_Positive;
        int m_Frame;
        int m_QuitAt = -1;
        int m_Retry;
        ProfilerRecorder m_Recorder;
        SampleHud m_Hud;
        long m_OnSum, m_OffSum, m_PosSum, m_OnMax, m_OffMax;
        int m_On, m_Off, m_Pos;
        byte[] m_Sink;
        bool m_Stress, m_Game;
        Action m_Launch;
        Action<FeedbackPlayer, PlaybackHandle> m_OnPlayed;
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly int[] m_AllocFrames = new int[32];
        readonly long[] m_AllocBytes = new long[32];
        int m_Played, m_Allocs;
        Action<FeedbackPlayer, PlaybackHandle> m_OnStopPlayed;
        readonly List<FeedbackPlayer> m_Watched = new List<FeedbackPlayer>();
        int m_StopPlays;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-sampleTour") >= 0)
                DontDestroyOnLoad(new GameObject("Sample Tour", typeof(SampleTour)));
        }

        void OnEnable() => m_Recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        void OnDisable() => m_Recorder.Dispose();

        void Update()
        {
            m_Frame++;
            if (m_QuitAt >= 0)
            {
                if (m_Frame >= m_QuitAt)
                    Application.Quit(0);
                return;
            }
            long v = m_Recorder.Valid ? m_Recorder.LastValue : -1;
            if (m_Frame == 1)
                Arrive();
            if (m_Game)
            {
                Game(v);
                return;
            }
            if (m_Frame > 120 && m_Frame <= 180)
            {
                m_OnSum += v; m_On++; m_OnMax = Math.Max(m_OnMax, v);
            }
            else if (m_Frame == 181)
            {
                m_Hud = SampleHud.Instance;
                if (m_Hud != null)
                    m_Hud.enabled = false;
            }
            else if (m_Frame > 184 && m_Frame <= 244)
            {
                m_OffSum += v; m_Off++; m_OffMax = Math.Max(m_OffMax, v);
            }
            else if (m_Frame == 245)
            {
                if (m_Hud != null)
                    m_Hud.enabled = true;
            }
            else if (!s_Positive && SampleHub.Instance != null && m_Frame > 250 && m_Frame <= 310)
            {
                m_Sink = new byte[1024];
                m_PosSum += v; m_Pos++;
            }
            else if (m_Frame == 330)
            {
                if (m_Recorder.Valid && m_Retry < Retries && m_On > 0 && m_Off > 0 && m_OnSum / m_On - m_OffSum / m_Off > 32)
                    Again();
                else
                    Stop();
            }
        }

        // a stop's first frame: Stress Zero GC up to 1,000 players (it builds them within two frames — its windows come at 121),
        // the Showcase's game begins
        void Arrive()
        {
            m_Retry = 0;
            Watch();
            Component lab = Find(StressLab);
            m_Stress = lab != null;
            if (m_Stress)
                ((Action<int>)Delegate.CreateDelegate(typeof(Action<int>), lab, "SetCount"))(1000);
            lab = Find(BrickLab);
            if (lab == null)
                return;
            m_Game = true;
            m_Launch = (Action)Delegate.CreateDelegate(typeof(Action), lab, "Launch");
            if (m_OnPlayed == null)
                m_OnPlayed = OnPlayed;
            m_Players.Clear();
            foreach (FeedbackPlayer p in Resources.FindObjectsOfTypeAll<FeedbackPlayer>())
                if (p.gameObject.scene == SceneManager.GetActiveScene())
                {
                    m_Players.Add(p);
                    p.Played += m_OnPlayed;
                }
            m_Played = m_Allocs = 0;
            m_Hud = SampleHud.Instance;
            if (m_Hud != null)
                m_Hud.enabled = false;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
        }

        // the Showcase's own window: Launch every frame (nothing while the paddle holds no ball · a new game after Game Over); the
        // frames that allocated after the stop's first two (the scene's load and Arrive) are listed
        void Game(long v)
        {
            if (m_Frame > 2 && v > 0)
            {
                if (m_Allocs < m_AllocFrames.Length)
                {
                    m_AllocFrames[m_Allocs] = m_Frame;
                    m_AllocBytes[m_Allocs] = v;
                }
                m_Allocs++;
            }
            if (m_Frame > 10)
                m_Launch();
            if (m_Frame < GameFrames)
                return;
            foreach (FeedbackPlayer p in m_Players)
                if (p != null)
                    p.Played -= m_OnPlayed;
            var list = new StringBuilder();
            for (int i = 0; i < Math.Min(m_Allocs, m_AllocFrames.Length); i++)
                list.Append(m_AllocFrames[i]).Append(':').Append(m_AllocBytes[i]).Append(' ');
            Debug.Log($"SAMPLES tour game played={m_Played} allocFrames={m_Allocs} list=[{list.ToString().TrimEnd()}]");
            Application.targetFrameRate = -1;
            if (m_Hud != null)
                m_Hud.enabled = true;
            m_Game = false;
            Stop();
        }

        void OnPlayed(FeedbackPlayer player, PlaybackHandle handle) => m_Played++;

        void Again()
        {
            Debug.Log($"SAMPLES tour again scene={SceneManager.GetActiveScene().name} on={m_OnSum / m_On} off={m_OffSum / m_Off} onMax={m_OnMax} offMax={m_OffMax}");
            m_Retry++;
            m_OnSum = m_OffSum = m_OnMax = m_OffMax = 0;
            m_On = m_Off = 0;
            m_Frame = 120;
        }

        void Stop()
        {
            string gcLine = "";
            if (SampleHud.Instance != null)
                foreach (Text t in SampleHud.Instance.GetComponentsInChildren<Text>(true))
                    if (t.text.StartsWith("GC"))
                        gcLine = t.text;
            long positive = m_Pos > 0 ? m_PosSum / m_Pos : -1;
            int playing = 0;
            foreach (FeedbackPlayer p in m_Watched)
                if (p != null && p.IsPlaying)
                    playing++;
            Debug.Log($"SAMPLES tour scene={SceneManager.GetActiveScene().name} huds={Resources.FindObjectsOfTypeAll<SampleHud>().Length} recorder={m_Recorder.Valid} on={(m_On > 0 ? m_OnSum / m_On : -1)} off={(m_Off > 0 ? m_OffSum / m_Off : -1)} onMax={m_OnMax} offMax={m_OffMax} positive={positive} hudLine='{gcLine}' sink={(m_Sink != null ? m_Sink.Length : 0)} plays={m_StopPlays} playing={playing}");
            Unwatch();
            if (m_Stress)
                Debug.Log($"SAMPLES tour stress players={Count()}");
            Events();
            if (m_Pos > 0)
                s_Positive = true;
            m_OnSum = m_OffSum = m_PosSum = m_OnMax = m_OffMax = 0;
            m_On = m_Off = m_Pos = 0;
            m_Frame = 0;
            m_Stress = false;
            if (SampleHub.Instance != null)
            {
                while (s_Next < SampleHub.Instance.Cards.Count && !SampleCards.Available(SampleHub.Instance.Cards[s_Next]))
                    s_Next++;
                if (s_Next < SampleHub.Instance.Cards.Count)
                    SampleHub.Open(SampleHub.Instance.Cards[s_Next++]);
                else
                {
                    Debug.Log($"SAMPLES tour done cards={SampleHub.Instance.Cards.Count}");
                    m_QuitAt = 60;
                }
            }
            else
                SampleHub.BackToHub();
        }

        // the stop's scene's players — their plays counted from the stop's first frame (the delegates made there, before the
        // allocation windows)
        void Watch()
        {
            if (m_OnStopPlayed == null)
                m_OnStopPlayed = OnStopPlayed;
            Unwatch();
            m_StopPlays = 0;
            foreach (FeedbackPlayer p in Resources.FindObjectsOfTypeAll<FeedbackPlayer>())
                if (p.gameObject.scene == SceneManager.GetActiveScene())
                {
                    m_Watched.Add(p);
                    p.Played += m_OnStopPlayed;
                }
        }

        void Unwatch()
        {
            foreach (FeedbackPlayer p in m_Watched)
                if (p != null)
                    p.Played -= m_OnStopPlayed;
            m_Watched.Clear();
        }

        void OnStopPlayed(FeedbackPlayer p, PlaybackHandle h) => m_StopPlays++;

        // the scene's persistent UnityEvent calls — in a component's serialized fields and what they hold (a feedback item's
        // event in a [SerializeReference] list), never through another Object — found by name, not called: a method the linker
        // took away is missing
        static void Events()
        {
            int calls = 0, missing = 0;
            var lacking = new StringBuilder();
            Scene scene = SceneManager.GetActiveScene();
            foreach (MonoBehaviour b in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
                if (b != null && b.gameObject.scene == scene)
                    Walk(b, 0, ref calls, ref missing, lacking);
            Debug.Log($"SAMPLES tour events scene={scene.name} calls={calls} missing={missing} lacking=[{lacking.ToString().TrimEnd()}]");
        }

        static void Walk(object o, int depth, ref int calls, ref int missing, StringBuilder lacking)
        {
            if (o == null || depth > 6 || depth > 0 && o is UnityEngine.Object)
                return;
            if (o is UnityEventBase e)
            {
                for (int i = 0; i < e.GetPersistentEventCount(); i++)
                {
                    UnityEngine.Object target = e.GetPersistentTarget(i);
                    string method = e.GetPersistentMethodName(i);
                    if (target == null || string.IsNullOrEmpty(method))
                        continue;
                    calls++;
                    if (!Has(target.GetType(), method))
                    {
                        missing++;
                        lacking.Append(target.GetType().Name).Append('.').Append(method).Append(' ');
                    }
                }
                return;
            }
            if (o is System.Collections.IList list)
            {
                foreach (object x in list)
                    Walk(x, depth + 1, ref calls, ref missing, lacking);
                return;
            }
            for (Type t = o.GetType(); t != null && t != typeof(object) && t != typeof(MonoBehaviour); t = t.BaseType)
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    Type ft = f.FieldType;
                    if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string) || typeof(UnityEngine.Object).IsAssignableFrom(ft))
                        continue;
                    if (f.IsNotSerialized || !(f.IsPublic || f.IsDefined(typeof(SerializeField), false) || f.IsDefined(typeof(SerializeReference), false)))
                        continue;
                    Walk(f.GetValue(o), depth + 1, ref calls, ref missing, lacking);
                }
        }

        static bool Has(Type type, string method)
        {
            for (Type t = type; t != null; t = t.BaseType)
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (m.Name == method)
                        return true;
            return false;
        }

        // the active scene's component of a type named by its assembly-qualified name (a sample's — this assembly does not
        // reference the samples)
        static Component Find(string type)
        {
            Type t = Type.GetType(type);
            if (t == null)
                return null;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(t))
                if (o is Component c && c.gameObject.scene == SceneManager.GetActiveScene())
                    return c;
            return null;
        }

        static int Count()
        {
            int n = 0;
            foreach (FeedbackPlayer p in Resources.FindObjectsOfTypeAll<FeedbackPlayer>())
                if (p.gameObject.scene == SceneManager.GetActiveScene() && p.gameObject.activeInHierarchy)
                    n++;
            return n;
        }
    }
}
