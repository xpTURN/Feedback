using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests.Editor
{
    // the IL walk's positive controls — a making read and a runtime write it must find
    internal static class DebuggerIlControl
    {
        internal static bool Make() => FeedbackCategories.IsEnabled(FeedbackCategory.Motion);

        internal static void Write(FeedbackPlayer player) => player.Stop();
    }

    // a command receiver whose Receive runs a callback — a channel walked with a hole in it
    internal sealed class DebuggerTestReceiver : ICommandReceiver
    {
        internal Action OnReceive;

        public Vector3 Position => default;

        public float Range => 0f;

        public void Receive(in FeedbackCommand command, bool hasPosition, Vector3 position) => OnReceive?.Invoke();
    }

    internal sealed class DebuggerTestListener : IPlaybackListener
    {
        public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end)
        {
        }
    }

    // the fixtures — direct-tick schedulers, players, the window (opened by GetWindow — a batch run builds it too), the test
    // source, the play mode options (enterPlayModeOptions, put back from SessionState) and the allocation gate's rule (the
    // first run of five fresh fixtures, a positive control). A closed window is the engine's null: its fields are read
    // through a reference kept before the close
    internal static class DebuggerTesting
    {
        const string Key = "xpTURN.Feedback.DebuggerTests.";

        static readonly List<Object> s_Made = new List<Object>();
        static readonly List<FeedbackScheduler> s_Schedulers = new List<FeedbackScheduler>();
        static int s_Sink;

        internal static TargetRef Self => new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };

        internal static TargetRef Grand => new TargetRef { m_Mode = TargetRefMode.Child, m_ChildLookup = ChildLookup.Path, m_Path = "c/g", m_Anchor = "" };

        internal static TargetRef Direct(Object target) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };

        internal static FeedbackScheduler NewScheduler()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            s_Schedulers.Add(s);
            return s;
        }

        internal static GameObject Go(string name)
        {
            var go = new GameObject(name);
            s_Made.Add(go);
            return go;
        }

        internal static void Keep(Object o) => s_Made.Add(o);

        internal static PositionItem Tween(float duration, TargetRef target)
        {
            var item = new PositionItem { m_Motion = new TweenMotion { m_Params = TweenParams.Default }, m_Target = target };
            item.m_Timing.m_Duration = duration;
            return item;
        }

        internal static RotationItem Turn(float duration, TargetRef target)
        {
            var item = new RotationItem { m_Motion = new TweenMotion { m_Params = TweenParams.Default }, m_Target = target };
            item.m_Timing.m_Duration = duration;
            return item;
        }

        // bound to s when it is given
        internal static FeedbackPlayer Player(string name, FeedbackScheduler s, params FeedbackItem[] items)
        {
            var p = Go(name).AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            if (s != null)
                p.Bind(s);
            return p;
        }

        internal static FeedbackDebuggerWindow Window()
        {
            FeedbackDebuggerWindow w = FeedbackDebuggerWindow.Open();
            w.position = new Rect(0, 0, 700, 900);
            return w;
        }

        // the tables' saved state goes with the window — the next test's window starts from the first sort, all columns shown
        internal static void CloseWindows()
        {
            foreach (FeedbackDebuggerWindow w in Resources.FindObjectsOfTypeAll<FeedbackDebuggerWindow>())
            {
                ClearView(w);
                w.Close();
            }
        }

        // the window's view data (the tables' sort, order, widths, hidden columns) — memory and file
        internal static void ClearView(EditorWindow w) => typeof(EditorWindow).GetMethod("ClearPersistentViewData", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(w, null);

        internal static void SetOptions(EnterPlayModeOptions options)
        {
            if (!SessionState.GetBool(Key + "Saved", false))
            {
                SessionState.SetBool(Key + "Saved", true);
                SessionState.SetInt(Key + "Options", (int)EditorSettings.enterPlayModeOptions);
                SessionState.SetBool(Key + "Enabled", EditorSettings.enterPlayModeOptionsEnabled);
            }
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = options;
        }

        static void RestoreOptions()
        {
            if (!SessionState.GetBool(Key + "Saved", false))
                return;
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(Key + "Options", 0);
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(Key + "Enabled", false);
            SessionState.EraseBool(Key + "Saved");
        }

        internal static void Clean()
        {
            CloseWindows();
            foreach (Object o in s_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            foreach (FeedbackScheduler s in s_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            s_Made.Clear();
            s_Schedulers.Clear();
            DebuggerSource.Override = null;
            foreach (string panel in FeedbackDebuggerWindow.Panels)
                EditorPrefs.DeleteKey(FeedbackDebuggerWindow.FoldKey + panel);
            EditorPrefs.DeleteKey(DebuggerCallSites.Key);
            DebuggerCallSites.Load();
            DebuggerCallSites.Clear();
            RestoreOptions();
        }

        // ---- the allocation gate: a fresh fixture — a template's copy whose grandchild has no C# transform yet, prepared, and
        // its first missed Signal (a development build's warning allocates — made outside the run)

        internal sealed class Fixture
        {
            internal FeedbackScheduler Scheduler;
            internal FeedbackPlayer Player;
        }

        internal static Fixture Current;
        static readonly DebuggerTestListener s_Listener = new DebuggerTestListener();

        internal static Fixture Fresh()
        {
            FeedbackScheduler s = NewScheduler();
            GameObject template = Go("template");
            var mid = new GameObject("c");
            mid.transform.SetParent(template.transform, false);
            var leaf = new GameObject("g");
            leaf.transform.SetParent(mid.transform, false);
            leaf.AddComponent<Light>();
            GameObject owner = Object.Instantiate(template);
            s_Made.Add(owner);
            var player = owner.AddComponent<FeedbackPlayer>();
            player.m_Items.Add(Tween(0.3f, Grand));
            player.Bind(s);
            player.EnsureRuntime();
            player.Signal();
            return new Fixture { Scheduler = s, Player = player };
        }

        static void Tick(int n)
        {
            for (int i = 0; i < n; i++)
                Current.Scheduler.Tick(0.05f);
        }

        // the seek cycle and the listener of the allocation gate's harness, a second missed Signal of each way
        internal static readonly TestDelegate Cycle = () =>
        {
            FeedbackPlayer p = Current.Player;
            p.Play();
            Tick(3);
            p.Pause();
            p.Seek(0.1f);
            p.Resume();
            Tick(1);
            p.Skip();
            Tick(1);
            p.Play(PlayArgs.Default.WithListener(s_Listener));
            Tick(2);
            p.Stop(StopMode.Revert);
            Tick(1);
            p.Signal();
            p.SignalQuietly();
        };

        // hits of five — the first run of five fresh fixtures (a warm-up on another fixture first)
        internal static int FirstRuns(TestDelegate run)
        {
            Current = Fresh();
            run();
            int hits = 0;
            for (int i = 0; i < 5; i++)
            {
                Current = Fresh();
                if (UIs.AllocatingGCMemory().ApplyTo(run).IsSuccess)
                    hits++;
            }
            Current = null;
            return hits;
        }

        internal static int Positive()
        {
            int hits = 0;
            for (int i = 0; i < 5; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => s_Sink += new int[4].Length)).IsSuccess)
                    hits++;
            }
            return hits;
        }

        // allocating runs of five
        internal static int Allocating(TestDelegate run)
        {
            int hits = 0;
            for (int i = 0; i < 5; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(run).IsSuccess)
                    hits++;
            }
            return hits;
        }
    }
}
