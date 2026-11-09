#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using xpTURN.Feedback.Samples;

// the samples' feature tour for a recording — tools/tour/record-tour.sh copies it into the dev project's Assets for one run.
// The hub opens each card, keys go through a virtual keyboard, Esc goes back; the dev project's fixture cards are hidden.
// Temp/PRTour/log.txt has a line per segment, in seconds since the recording began
public sealed class PRTourRunner : MonoBehaviour
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static string Dir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "PRTour");

    Keyboard m_Kb;
    readonly HashSet<Key> m_Down = new HashSet<Key>();
    float m_T0;
    InputSettings.BackgroundBehavior m_Background;
    InputSettings.EditorInputBehaviorInPlayMode m_Editor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (!File.Exists(Path.Combine(Dir, "active")) || FindAnyObjectByType<PRTourRunner>() != null)
            return;
        var go = new GameObject("PR Tour");
        DontDestroyOnLoad(go);
        go.AddComponent<PRTourRunner>();
    }

    void Log(string s) => File.AppendAllText(Path.Combine(Dir, "log.txt"), (Time.realtimeSinceStartup - m_T0).ToString("0.0", CultureInfo.InvariantCulture) + " " + s + "\n");

    static IEnumerator Wait(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until)
            yield return null;
    }

    void Send() => InputSystem.QueueStateEvent(m_Kb, new KeyboardState(m_Down.ToArray()));

    void Hold(Key k)
    {
        if (m_Down.Add(k))
            Send();
    }

    void Release(Key k)
    {
        if (m_Down.Remove(k))
            Send();
    }

    IEnumerator Tap(Key k, float after = 0.6f)
    {
        Hold(k);
        yield return Wait(0.12f);
        Release(k);
        yield return Wait(after);
    }

    IEnumerator Taps(Key k, int n, float gap, float after)
    {
        for (int i = 0; i < n; i++)
            yield return Tap(k, gap);
        yield return Wait(after);
    }

    IEnumerator HoldFor(Key k, float seconds)
    {
        Hold(k);
        yield return Wait(seconds);
        Release(k);
    }

    static MonoBehaviour Lab(string type) => FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).FirstOrDefault(m => m != null && m.GetType().Name == type);

    static object Get(object o, string member)
    {
        Type t = o.GetType();
        FieldInfo f = t.GetField(member, Any);
        if (f != null)
            return f.GetValue(o);
        PropertyInfo p = t.GetProperty(member, Any);
        return p != null ? p.GetValue(o) : null;
    }

    static object Call(object o, string method, params object[] args)
    {
        MethodInfo m = o.GetType().GetMethods(Any).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
        return m != null ? m.Invoke(o, args) : null;
    }

    static List<string> Texts(Button b)
    {
        var texts = new List<string>();
        foreach (Component c in b.GetComponentsInChildren<Component>(true))
        {
            PropertyInfo p = c.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(x => x.Name == "text" && x.PropertyType == typeof(string));
            if (p != null && p.GetValue(c) is string s)
                texts.Add(s);
        }
        return texts;
    }

    // the dev project's fixture cards are not part of the samples a user gets
    static void HideFixtures()
    {
        var cards = new List<SampleCard>();
        SampleCards.Gather(cards);
        foreach (SampleCard card in cards)
        {
            if (card == null || !UnityEditor.AssetDatabase.GetAssetPath(card).Contains("samples-fixtures"))
                continue;
            foreach (Button b in FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (Texts(b).Any(t => t.StartsWith(card.Title, StringComparison.Ordinal)))
                    b.gameObject.SetActive(false);
        }
    }

    static IEnumerator Loaded(string scene)
    {
        float until = Time.realtimeSinceStartup + 15f;
        while (SceneManager.GetActiveScene().name != scene && Time.realtimeSinceStartup < until)
            yield return null;
        yield return null;
    }

    IEnumerator Open(string scene)
    {
        var cards = new List<SampleCard>();
        SampleCards.Gather(cards);
        SampleCard card = cards.FirstOrDefault(c => c != null && c.SceneName == scene);
        if (card == null)
        {
            Log("no card " + scene);
            yield break;
        }
        Button b = FindObjectsByType<Button>(FindObjectsSortMode.None).FirstOrDefault(x => Texts(x).Contains(card.Title));
        if (b != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(b.gameObject);
        yield return Wait(1.0f);
        Log("open " + card.Title);
        if (card.OpenInEditor)
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(UnityEditor.AssetDatabase.GUIDToAssetPath(card.SceneGuid), new LoadSceneParameters(LoadSceneMode.Single));
        else
            SampleHub.Open(card);
        yield return Loaded(scene);
        yield return Wait(1.2f);
    }

    IEnumerator Back()
    {
        yield return Tap(Key.Escape, 0f);
        yield return Loaded(SampleKitPaths.HubName);
        yield return null;
        HideFixtures();
        yield return Wait(1.0f);
    }

    IEnumerator Start()
    {
        m_Kb = InputSystem.AddDevice<Keyboard>("PRTourKeyboard");
        m_Background = InputSystem.settings.backgroundBehavior;
        m_Editor = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        yield return null;
        yield return null;
        HideFixtures();
        File.WriteAllText(Path.Combine(Dir, "waiting"), "1");
        float give = Time.realtimeSinceStartup + 300f;
        while (!File.Exists(Path.Combine(Dir, "rec")) && Time.realtimeSinceStartup < give)
            yield return null;
        m_T0 = Time.realtimeSinceStartup;
        Log("start");
        yield return Wait(2.0f);
        foreach (Func<IEnumerator> segment in new Func<IEnumerator>[] { GettingStarted, Gallery, BeforeAfter, CombatHit, CameraShake, Cinemachine, PostFX, Springs, UIJuice, Timeline, Channels, Accessibility, Stress, Showcase })
        {
            IEnumerator run = segment();
            while (true)
            {
                bool next;
                try
                {
                    next = run.MoveNext();
                }
                catch (Exception e)
                {
                    Log("error " + segment.Method.Name + ": " + e.GetType().Name + " " + e.Message);
                    break;
                }
                if (!next)
                    break;
                yield return run.Current;
            }
            m_Down.Clear();
            Send();
            if (SceneManager.GetActiveScene().name != SampleKitPaths.HubName)
                yield return Back();
        }
        yield return Wait(2.5f);
        Log("end");
        InputSystem.settings.backgroundBehavior = m_Background;
        InputSystem.settings.editorInputBehaviorInPlayMode = m_Editor;
        InputSystem.RemoveDevice(m_Kb);
        File.WriteAllText(Path.Combine(Dir, "done"), "1");
    }

    IEnumerator GettingStarted()
    {
        yield return Open("GettingStarted");
        yield return Taps(Key.Space, 3, 1.3f, 0.5f);
    }

    IEnumerator Gallery()
    {
        yield return Open("FeedbackGallery");
        MonoBehaviour g = Lab("FeedbackGallery");
        if (g == null)
            yield break;
        var tabs = ((IEnumerable)Get(g, "Tabs")).Cast<string>().ToList();
        var entries = ((IEnumerable)Get(g, "Entries")).Cast<object>().ToList();
        for (int i = 0; i < tabs.Count && i < 7; i++)
        {
            Call(g, "ShowTab", i);
            Log("tab " + tabs[i]);
            yield return Wait(0.8f);
            int n = 0;
            foreach (object e in entries)
            {
                if ((string)Get(e, "Tab") != tabs[i] || !(bool)Call(g, "Available", e))
                    continue;
                Call(g, "Press", e);
                yield return Wait(1.4f);
                if (++n == 2)
                    break;
            }
        }
    }

    IEnumerator BeforeAfter()
    {
        yield return Open("BeforeAfter");
        MonoBehaviour lab = Lab("BeforeAfterLab");
        if (lab == null)
            yield break;
        float[] jumps = { 2.8f, 6.4f, 10.5f, 16.6f };
        int j = 0;
        Hold(Key.D);
        float until = Time.realtimeSinceStartup + 8f;
        while (((Vector2)Get(lab, "m_Position")).x < 21.5f && Time.realtimeSinceStartup < until)
        {
            if (j < jumps.Length && (bool)Get(lab, "Grounded") && ((Vector2)Get(lab, "m_Position")).x >= jumps[j])
            {
                Hold(Key.Space);
                yield return null;
                yield return null;
                Release(Key.Space);
                j++;
            }
            yield return null;
        }
        Release(Key.D);
        yield return Wait(0.8f);
        yield return Tap(Key.Space, 1.3f);
        Log("feedback off");
        yield return Tap(Key.Tab, 0.6f);
        yield return Taps(Key.Space, 2, 1.1f, 0.2f);
        Log("feedback on");
        yield return Tap(Key.Tab, 0.6f);
        yield return Tap(Key.Space, 1.3f);
        foreach (Key layer in new[] { Key.Z, Key.X, Key.C, Key.V })
        {
            yield return Tap(layer, 0.4f);
            yield return Tap(Key.Space, 1.1f);
        }
        foreach (Key layer in new[] { Key.Z, Key.X, Key.C, Key.V })
            yield return Tap(layer, 0.25f);
        yield return HoldFor(Key.A, 2.6f);
        yield return Wait(1.0f);
    }

    IEnumerator CombatHit()
    {
        yield return Open("CombatHit");
        yield return Tap(Key.Digit1, 1.0f);
        yield return Tap(Key.Digit2, 1.3f);
        yield return Tap(Key.Digit3, 2.0f);
        Log("slow time");
        yield return Taps(Key.Comma, 3, 0.25f, 0.4f);
        yield return Tap(Key.Digit3, 3.2f);
        yield return Taps(Key.Period, 3, 0.25f, 0.4f);
        yield return Tap(Key.Digit2, 1.4f);
    }

    IEnumerator CameraShake()
    {
        yield return Open("CameraShakeLab");
        yield return Tap(Key.Digit1, 1.2f);
        yield return Tap(Key.Digit2, 1.4f);
        yield return Tap(Key.Digit3, 1.8f);
        yield return Tap(Key.Z, 1.2f);
        yield return Tap(Key.X, 1.2f);
        yield return Tap(Key.C, 2.2f);
        yield return Tap(Key.C, 0.8f);
    }

    IEnumerator Cinemachine()
    {
        yield return Open("CinemachineShakeLab");
        yield return Tap(Key.Digit1, 1.2f);
        yield return Tap(Key.Digit3, 1.6f);
        yield return Tap(Key.Z, 0.8f);
        yield return Tap(Key.Digit2, 1.5f);
        yield return Tap(Key.Z, 0.6f);
        yield return Tap(Key.C, 0.5f);
        yield return Tap(Key.V, 1.6f);
    }

    IEnumerator PostFX()
    {
        yield return Open("PostFXLab");
        yield return Tap(Key.Digit1, 1.5f);
        yield return Tap(Key.Digit2, 2.5f);
        yield return Tap(Key.Digit3, 2.5f);
        yield return Tap(Key.V, 0.8f);
        yield return Tap(Key.X, 0.4f);
        yield return Tap(Key.C, 1.6f);
        yield return Tap(Key.X, 0.4f);
        yield return Tap(Key.C, 1.6f);
        yield return Tap(Key.V, 0.6f);
    }

    IEnumerator Springs()
    {
        yield return Open("SpringPlayground");
        yield return Taps(Key.RightArrow, 2, 0.8f, 0f);
        yield return Taps(Key.UpArrow, 2, 0.8f, 0f);
        yield return Taps(Key.LeftArrow, 3, 0.8f, 0.4f);
        Log("lower frequency");
        yield return Taps(Key.Z, 5, 0.15f, 0.2f);
        yield return Taps(Key.RightArrow, 2, 1.2f, 0f);
        Log("higher frequency, damping");
        yield return Taps(Key.X, 9, 0.12f, 0.1f);
        yield return Taps(Key.V, 6, 0.12f, 0.2f);
        yield return Taps(Key.DownArrow, 2, 1.0f, 0f);
        yield return Tap(Key.Digit2, 1.4f);
        yield return Tap(Key.Digit3, 1.6f);
        yield return Tap(Key.Digit3, 1.6f);
    }

    IEnumerator UIJuice()
    {
        yield return Open("UIJuice");
        MonoBehaviour lab = Lab("UIJuiceLab");
        if (lab == null)
            yield break;
        Call(lab, "Buy", 0);
        yield return Wait(1.1f);
        Call(lab, "Buy", 1);
        yield return Wait(1.1f);
        Call(lab, "Hit", 25);
        yield return Wait(1.0f);
        Call(lab, "Hit", 25);
        yield return Wait(1.0f);
        Call(lab, "Heal", 20);
        yield return Wait(1.0f);
        Call(lab, "AddXp", 60);
        yield return Wait(1.3f);
        Call(lab, "AddXp", 60);
        yield return Wait(2.2f);
        Call(lab, "CloseReward");
        yield return Wait(0.8f);
        Log("paused");
        Call(lab, "TogglePause");
        yield return Wait(0.8f);
        Call(lab, "Hit", 20);
        yield return Wait(1.0f);
        Call(lab, "AddXp", 30);
        yield return Wait(1.2f);
        Call(lab, "TogglePause");
        yield return Wait(1.0f);
    }

    IEnumerator Timeline()
    {
        yield return Open("TimelineFlow");
        yield return Tap(Key.Space, 3.2f);
        yield return Tap(Key.Space, 2.0f);
        yield return Tap(Key.Digit2, 0.8f);
        yield return Taps(Key.Z, 3, 0.4f, 0f);
        yield return Taps(Key.X, 2, 0.4f, 0f);
        yield return Tap(Key.Digit3, 2.8f);
    }

    IEnumerator Channels()
    {
        yield return Open("ChannelsRange");
        yield return Tap(Key.Space, 1.3f);
        yield return HoldFor(Key.D, 0.6f);
        yield return Tap(Key.Space, 1.3f);
        yield return Tap(Key.Z, 0.4f);
        yield return Tap(Key.Space, 1.3f);
        yield return Tap(Key.X, 0.4f);
        yield return Tap(Key.Space, 1.3f);
        yield return Tap(Key.C, 0.4f);
        yield return HoldFor(Key.A, 0.9f);
        yield return Tap(Key.Space, 1.5f);
    }

    IEnumerator Accessibility()
    {
        yield return Open("Accessibility");
        yield return Tap(Key.Space, 1.0f);
        yield return Tap(Key.Digit2, 1.3f);
        yield return Tap(Key.Digit3, 1.8f);
        Log("settings down");
        yield return Taps(Key.Z, 4, 0.2f, 0.2f);
        yield return Tap(Key.C, 0.3f);
        yield return Taps(Key.Z, 4, 0.2f, 0.2f);
        yield return Tap(Key.C, 0.3f);
        yield return Taps(Key.Z, 3, 0.2f, 0.3f);
        yield return Tap(Key.Space, 1.0f);
        yield return Tap(Key.Digit2, 1.3f);
        yield return Tap(Key.Digit3, 1.8f);
    }

    IEnumerator Stress()
    {
        yield return Open("StressZeroGC");
        yield return Tap(Key.Z, 1.5f);
        yield return Tap(Key.X, 1.5f);
        yield return Tap(Key.C, 1.5f);
        yield return Tap(Key.V, 2.0f);
        Log("re-trigger");
        yield return Tap(Key.Digit3, 3.0f);
        Log("coroutine baseline");
        yield return Tap(Key.Digit2, 2.2f);
        yield return Tap(Key.Digit2, 1.0f);
        yield return Tap(Key.Digit3, 0.5f);
        yield return Tap(Key.Z, 0.8f);
    }

    IEnumerator Showcase()
    {
        yield return Open("BrickBreaker");
        MonoBehaviour lab = Lab("BrickBreakerLab");
        if (lab == null)
            yield break;
        object board = Get(lab, "m_Board");
        yield return Tap(Key.Space, 0.2f);
        yield return Autopilot(board, 14f);
        Log("boss");
        yield return Tap(Key.Digit3, 0.8f);
        yield return Autopilot(board, 10f);
    }

    IEnumerator Autopilot(object board, float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        float serve = 0f;
        while (Time.realtimeSinceStartup < until)
        {
            Vector2 ball = (Vector2)Get(board, "Ball");
            float paddle = (float)Get(board, "PaddleX");
            if (ball.x > paddle + 0.25f)
            {
                Release(Key.A);
                Hold(Key.D);
            }
            else if (ball.x < paddle - 0.25f)
            {
                Release(Key.D);
                Hold(Key.A);
            }
            else
            {
                Release(Key.A);
                Release(Key.D);
            }
            if ((bool)Get(board, "Held") && Time.realtimeSinceStartup > serve)
            {
                serve = Time.realtimeSinceStartup + 0.8f;
                Hold(Key.Space);
                yield return null;
                yield return null;
                Release(Key.Space);
            }
            yield return null;
        }
        Release(Key.A);
        Release(Key.D);
    }
}
#endif
