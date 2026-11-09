using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace RecompileCheck
{
    // a script recompile during play in the sample fixtures' template scene (a second session of
    // check-recompile-during-play.sh; SessionState carries the phases over the reload): 60 frames → the HUD's state changed
    // (Before · intensity 0.5) → a script edited → the reload → the HUD's Tab pressed on a recording keyboard → 60 frames →
    // exit play → quit. Each step writes a line to SampleRecompileCheck.txt
    [InitializeOnLoad]
    public static class SampleRecompileDriver
    {
        const string PhaseKey = "RecompileCheck.Samples.Phase";
        const string FrameKey = "RecompileCheck.Samples.Frame";
        const string Template = "Packages/com.xpturn.feedback.samples-fixtures/Fixture/Template/Template.unity";
        const string TouchPath = "Assets/RecompileCheck/SampleTouch.cs";
        const string Record = "SampleRecompileCheck.txt";
        const string KeyboardName = "SampleRecompileKeyboard";
        const int EnterPlay = 1, BeforeEdit = 2, WaitReload = 3, AfterReload = 4, WaitExit = 5, Done = 6;
        const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static SampleRecompileDriver() => EditorApplication.update += Tick;

        static void Write(string s) => File.AppendAllText(Record, s + "\n");

        public static void Run()
        {
            File.WriteAllText(Record, "");
            Write("option " + EditorPrefs.GetInt("ScriptCompilationDuringPlay", 0));
            EditorSceneManager.OpenScene(Template, OpenSceneMode.Single);
            SessionState.SetInt(PhaseKey, EnterPlay);
        }

        static int Huds() => Resources.FindObjectsOfTypeAll<SampleHud>().Count(h => h.gameObject.scene.isLoaded);

        static string State()
        {
            object gc = SampleHud.Instance != null ? typeof(SampleHud).GetProperty("GcAvailable", All)?.GetValue(SampleHud.Instance) : null;
            InputAction after = SampleInput.Find("After");
            return $"huds={Huds()} instance={SampleHud.Instance != null} gc={gc} actions={SampleInput.Asset != null} afterEnabled={after != null && after.enabled} after={SampleState.After} intensity={SampleState.Intensity} camera={FeedbackCategories.IsEnabled(FeedbackCategory.Camera)} cameraScale={FeedbackCategories.GetScale(FeedbackCategory.Camera)}";
        }

        // the Console list since the play's entry: errors · warnings, and the rows of the engine's QuickSearch index (the one
        // engine line of a non-batch reload)
        static string ConsoleCounts()
        {
            Type entries = Type.GetType("UnityEditor.LogEntries, UnityEditor.CoreModule");
            Type entry = Type.GetType("UnityEditor.LogEntry, UnityEditor.CoreModule");
            var counts = new object[] { 0, 0, 0 };
            entries?.GetMethod("GetCountsByType", All)?.Invoke(null, counts);
            int engine = 0;
            if (entries != null && entry != null)
            {
                int n = (int)entries.GetMethod("StartGettingEntries", All).Invoke(null, null);
                try
                {
                    object e = Activator.CreateInstance(entry, true);
                    FieldInfo message = entry.GetField("message", All);
                    for (int i = 0; i < n; i++)
                    {
                        entries.GetMethod("GetEntryInternal", All).Invoke(null, new[] { (object)i, e });
                        if (((string)message.GetValue(e) ?? "").Contains("UnityEditor.Search.SearchDatabase"))
                            engine++;
                    }
                }
                finally
                {
                    entries.GetMethod("EndGettingEntries", All).Invoke(null, null);
                }
            }
            return $"errors={counts[0]} warnings={counts[1]} engine={engine}";
        }

        static void Tick()
        {
            int phase = SessionState.GetInt(PhaseKey, 0);
            if (phase == 0 || phase == Done)
                return;
            switch (phase)
            {
                case EnterPlay:
                    SessionState.SetInt(PhaseKey, BeforeEdit);
                    SessionState.SetInt(FrameKey, -1);
                    EditorApplication.EnterPlaymode();
                    break;
                case BeforeEdit:
                    if (!EditorApplication.isPlaying)
                        return;
                    if (SessionState.GetInt(FrameKey, -1) < 0)
                        SessionState.SetInt(FrameKey, Time.frameCount);
                    if (Time.frameCount - SessionState.GetInt(FrameKey, 0) < 60)
                        return;
                    SampleState.SetAfter(false);
                    SampleState.SetIntensity(0.5f);
                    Write("before " + State());
                    File.WriteAllText(TouchPath, "public static class SampleRecompileTouch { public const long Value = " + DateTime.UtcNow.Ticks + "; }\n");
                    SessionState.SetInt(PhaseKey, WaitReload);
                    AssetDatabase.Refresh();
                    break;
                case AfterReload:
                    if (!EditorApplication.isPlaying)
                    {
                        Write("after-reload not playing");
                        SessionState.SetInt(PhaseKey, WaitExit);
                        return;
                    }
                    if (SessionState.GetInt(FrameKey, -1) < 0)
                    {
                        SessionState.SetInt(FrameKey, Time.frameCount);
                        Write("first " + State());
                        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    }
                    int n = Time.frameCount - SessionState.GetInt(FrameKey, 0);
                    if (n == 30)
                    {
                        SampleHud.Ensure();
                        InputSystem.QueueStateEvent(InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>(KeyboardName), new KeyboardState(Key.Tab));
                    }
                    if (n == 34 && InputSystem.GetDevice(KeyboardName) is UnityEngine.InputSystem.Keyboard kb)
                        InputSystem.QueueStateEvent(kb, new KeyboardState());
                    if (n == 40)
                        Write("tab " + State());
                    if (n < 60)
                        return;
                    Write("after " + State() + " " + ConsoleCounts());
                    SessionState.SetInt(PhaseKey, WaitExit);
                    EditorApplication.ExitPlaymode();
                    break;
                case WaitExit:
                    if (EditorApplication.isPlaying)
                        return;
                    Write("exit huds=" + Huds());
                    SessionState.SetInt(PhaseKey, Done);
                    EditorApplication.Exit(0);
                    break;
            }
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        static void Reloaded()
        {
            if (SessionState.GetInt(PhaseKey, 0) != WaitReload)
                return;
            Write("reloaded playing=" + EditorApplication.isPlaying);
            SessionState.SetInt(FrameKey, -1);
            SessionState.SetInt(PhaseKey, AfterReload);
        }
    }
}
