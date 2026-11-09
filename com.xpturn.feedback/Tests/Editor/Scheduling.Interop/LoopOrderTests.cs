using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;
using UniTaskItem = Cysharp.Threading.Tasks.IPlayerLoopItem;
using UniTaskLoop = Cysharp.Threading.Tasks.PlayerLoopHelper;
using UniTaskTiming = Cysharp.Threading.Tasks.PlayerLoopTiming;

namespace xpTURN.Feedback.Tests.Editor
{
    // domain loads across play entries — a reload-on entry adds one. A static would reset
    [InitializeOnLoad]
    static class LoopOrderDomainLoads
    {
        const string Key = "xpTURN.Feedback.Tests.LoopOrder.DomainLoads";

        static LoopOrderDomainLoads() => SessionState.SetInt(Key, SessionState.GetInt(Key, 0) + 1);

        internal static int Count => SessionState.GetInt(Key, 0);
    }

    // UniTask and VContainer in the loop with ours. After each entry our three systems are wrapped to record, a window opens
    // each frame at the front of Initialization, and the last whole frame is cut by our three stages into four intervals
    // whose sets must equal the table in every entry. A reload-on entry resets locals and statics after the yield: progress,
    // results and the loop count live in SessionState. The reload expectation is always the default (true) — false with a
    // reload ends the whole run without results
    public class LoopOrderTests
    {
        const string Prefix = "xpTURN.Feedback.Tests.LoopOrder.";
        const string SavedKey = Prefix + "Saved";
        const string OptionsKey = Prefix + "Options";
        const string EnabledKey = Prefix + "Enabled";
        const string HookKey = Prefix + "Hook";
        const string HookCountKey = Prefix + "HookCount";
        const string ProgressKey = Prefix + "Progress";
        const string StepsKey = Prefix + "Steps";

        const string WindowMark = "|";
        const string OurRevert = "FB.FeedbackRevert";
        const string OurUpdate = "FB.FeedbackUpdate";
        const string OurLateApply = "FB.FeedbackLateApply";
        const string OurFronts = "FeedbackRevert/FeedbackUpdate/FeedbackLateApply";

        const EnterPlayModeOptions AllOff = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;

        // the four settings three times each · the UniTask hook in two entries, last
        static readonly EnterPlayModeOptions[] Settings =
        {
            EnterPlayModeOptions.None, EnterPlayModeOptions.None, EnterPlayModeOptions.None,
            EnterPlayModeOptions.DisableDomainReload, EnterPlayModeOptions.DisableDomainReload, EnterPlayModeOptions.DisableDomainReload,
            EnterPlayModeOptions.DisableSceneReload, EnterPlayModeOptions.DisableSceneReload, EnterPlayModeOptions.DisableSceneReload,
            AllOff, AllOff, AllOff,
        };

        static readonly EnterPlayModeOptions[] HookSettings = { EnterPlayModeOptions.None, AllOff };

        // before our Revert · Revert to Update · Update to LateApply · after LateApply
        static readonly string[][] Table =
        {
            new string[0],
            new[] { "UT.EarlyUpdate", "UT.LastEarlyUpdate", "UT.Update", "UT.LastUpdate", "VC.Tick", "VC.PostTick", "MB.Update" },
            new[] { "UT.PreLateUpdate", "UT.LastPreLateUpdate", "VC.LateTick", "VC.PostLateTick", "MB.LateUpdate" },
            new[] { "UT.PostLateUpdate", "UT.LastPostLateUpdate" },
        };

        static readonly UniTaskTiming[] Timings =
        {
            UniTaskTiming.EarlyUpdate, UniTaskTiming.LastEarlyUpdate, UniTaskTiming.Update, UniTaskTiming.LastUpdate,
            UniTaskTiming.PreLateUpdate, UniTaskTiming.LastPreLateUpdate, UniTaskTiming.PostLateUpdate, UniTaskTiming.LastPostLateUpdate,
        };

        // ---- a user's BeforeSceneLoad putting UniTask back at the fronts. Counted when the EarlyUpdate front's delegate is a
        // new object — Initialize makes new runners each call. A hook that does not re-initialize leaves the hook test green on
        // the injection it is there to catch, and a count after the call alone would still count it

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ReinitializeUniTask()
        {
            if (!SessionState.GetBool(HookKey, false))
                return;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            object before = FrontTarget(loop, typeof(EarlyUpdate));
            UniTaskLoop.Initialize(ref loop);
            object after = FrontTarget(loop, typeof(EarlyUpdate));
            if (after != null && !ReferenceEquals(before, after))
                SessionState.SetInt(HookCountKey, SessionState.GetInt(HookCountKey, 0) + 1);
        }

        static object FrontTarget(PlayerLoopSystem loop, Type phase)
        {
            foreach (var system in loop.subSystemList)
                if (system.type == phase)
                    return system.subSystemList != null && system.subSystemList.Length > 0 ? system.subSystemList[0].updateDelegate?.Target : null;
            return null;
        }

        // ---- recorders. Each entry's UniTask items see that entry's stop object

        sealed class Stop
        {
            internal bool Stopped;
        }

        sealed class Item : UniTaskItem
        {
            readonly string m_Name;
            readonly Stop m_Stop;

            internal Item(string name, Stop stop)
            {
                m_Name = name;
                m_Stop = stop;
            }

            public bool MoveNext()
            {
                if (m_Stop.Stopped)
                    return false;
                FrameRecorder.Add(m_Name);
                return true;
            }
        }

        sealed class Entry : ITickable, IPostTickable, ILateTickable, IPostLateTickable
        {
            public void Tick() => FrameRecorder.Add("VC.Tick");

            public void PostTick() => FrameRecorder.Add("VC.PostTick");

            public void LateTick() => FrameRecorder.Add("VC.LateTick");

            public void PostLateTick() => FrameRecorder.Add("VC.PostLateTick");
        }

        struct WindowSystem
        {
        }

        static Stop s_Stop;
        static IObjectResolver s_Container;
        static GameObject s_Recorder;

        // ---- the loop

        // ours record, then run as before; the type stays, so the next Reinstall takes them out
        static void Wrap(ref PlayerLoopSystem system)
        {
            if (system.subSystemList == null)
                return;
            for (int i = 0; i < system.subSystemList.Length; i++)
            {
                PlayerLoopSystem child = system.subSystemList[i];
                if (child.type != null && child.type.DeclaringType == typeof(FeedbackPlayerLoop))
                {
                    PlayerLoopSystem.UpdateFunction original = child.updateDelegate;
                    string name = "FB." + child.type.Name;
                    child.updateDelegate = () =>
                    {
                        FrameRecorder.Add(name);
                        original();
                    };
                }
                else
                {
                    Wrap(ref child);
                }
                system.subSystemList[i] = child;
            }
        }

        // the window is a test type — Reinstall leaves it, and a reload-off entry keeps the last entry's loop
        static void RemoveWindows(ref PlayerLoopSystem system)
        {
            if (system.subSystemList == null)
                return;
            var kept = new List<PlayerLoopSystem>(system.subSystemList.Length);
            foreach (var child in system.subSystemList)
            {
                if (child.type == typeof(WindowSystem))
                    continue;
                var copy = child;
                RemoveWindows(ref copy);
                kept.Add(copy);
            }
            system.subSystemList = kept.ToArray();
        }

        static void InsertWindow(ref PlayerLoopSystem loop)
        {
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(Initialization))
                    continue;
                var list = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList ?? Array.Empty<PlayerLoopSystem>());
                list.Insert(0, new PlayerLoopSystem { type = typeof(WindowSystem), updateDelegate = () => FrameRecorder.Add(WindowMark) });
                loop.subSystemList[i].subSystemList = list.ToArray();
                return;
            }
        }

        static string Front(PlayerLoopSystem loop, Type phase)
        {
            foreach (var system in loop.subSystemList)
                if (system.type == phase)
                    return system.subSystemList != null && system.subSystemList.Length > 0 && system.subSystemList[0].type != null ? system.subSystemList[0].type.Name : "-";
            return "?";
        }

        static string Fronts()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            return Front(loop, typeof(EarlyUpdate)) + "/" + Front(loop, typeof(PreLateUpdate)) + "/" + Front(loop, typeof(PostLateUpdate));
        }

        // ---- one entry. Code after a yield reads SessionState, not locals

        static int Progress => SessionState.GetInt(ProgressKey, 0);

        static string Key(string test, string what, int index) => Prefix + test + "." + what + "." + index;

        static void Begin(bool hook)
        {
            SessionState.SetBool(SavedKey, true);
            SessionState.SetInt(OptionsKey, (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetBool(EnabledKey, EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(ProgressKey, 0);
            SessionState.SetInt(StepsKey, 0);
            SessionState.SetBool(HookKey, hook);
        }

        // before an entry: the guard of a loop that does not end, the setting, the domain loads so far
        static void Step(string test, EnterPlayModeOptions[] plan)
        {
            int steps = SessionState.GetInt(StepsKey, 0) + 1;
            SessionState.SetInt(StepsKey, steps);
            Assert.That(steps, Is.LessThanOrEqualTo(2 * plan.Length), "the entries did not end");
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = plan[Progress];
            SessionState.SetInt(HookCountKey, 0);
            SessionState.SetInt(Prefix + test + ".LoadsBefore", LoopOrderDomainLoads.Count);
        }

        // after an entry: what the entry did, then this entry's window · wraps · items · container · component
        static void Entered(string test)
        {
            int index = Progress;
            SessionState.SetInt(Key(test, "Loads", index), LoopOrderDomainLoads.Count - SessionState.GetInt(Prefix + test + ".LoadsBefore", 0));
            SessionState.SetString(Key(test, "Fronts", index), Fronts());
            SessionState.SetInt(Key(test, "Hook", index), SessionState.GetInt(HookCountKey, 0));

            FrameRecorder.Clear();
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Wrap(ref loop);
            RemoveWindows(ref loop);
            InsertWindow(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
            s_Stop = new Stop();
            foreach (var timing in Timings)
                UniTaskLoop.AddAction(timing, new Item("UT." + timing, s_Stop));
            var builder = new ContainerBuilder();
            builder.RegisterEntryPoint<Entry>();
            s_Container = builder.Build();
            s_Recorder = new GameObject(nameof(FrameRecorder), typeof(FrameRecorder));
        }

        // the last whole frame — between the last two windows
        static void Judge(string test)
        {
            List<string> record = FrameRecorder.Record;
            int last = record.LastIndexOf(WindowMark);
            int previous = last > 0 ? record.LastIndexOf(WindowMark, last - 1) : -1;
            var frame = previous < 0 ? new List<string>() : record.GetRange(previous + 1, last - previous - 1);
            SessionState.SetString(Key(test, "Intervals", Progress), Intervals(frame));
        }

        // the sets, in order: FixedUpdate is left out — how many run differs by frame
        static string Intervals(List<string> frame)
        {
            var sets = new SortedSet<string>[4];
            for (int i = 0; i < sets.Length; i++)
                sets[i] = new SortedSet<string>(StringComparer.Ordinal);
            int at = 0;
            foreach (string entry in frame)
            {
                if (entry == OurRevert)
                    at = 1;
                else if (entry == OurUpdate)
                    at = 2;
                else if (entry == OurLateApply)
                    at = 3;
                else if (entry != "MB.FixedUpdate")
                    sets[at].Add(entry);
            }
            return string.Join(" / ", sets.Select(s => string.Join(",", s)));
        }

        static string ExpectedIntervals => string.Join(" / ", Table.Select(s => string.Join(",", s.OrderBy(e => e, StringComparer.Ordinal))));

        // before the exit: items stop, the container goes, the component goes (a reload-off entry keeps the loop)
        static void EndEntry()
        {
            if (s_Stop != null)
                s_Stop.Stopped = true;
            s_Stop = null;
            s_Container?.Dispose();
            s_Container = null;
            if (s_Recorder != null)
                UnityEngine.Object.Destroy(s_Recorder);
            s_Recorder = null;
        }

        static void Verify(string test, EnterPlayModeOptions[] plan, bool hook)
        {
            string expected = ExpectedIntervals;
            var wrong = new List<string>();
            for (int i = 0; i < plan.Length; i++)
            {
                string intervals = SessionState.GetString(Key(test, "Intervals", i), "?");
                if (intervals != expected)
                    wrong.Add($"entry {i} ({plan[i]}): {intervals}");
                int loads = SessionState.GetInt(Key(test, "Loads", i), -1);
                int expectedLoads = (plan[i] & EnterPlayModeOptions.DisableDomainReload) == 0 ? 1 : 0;
                if (loads != expectedLoads)
                    wrong.Add($"entry {i} ({plan[i]}): domain loads {loads}, expected {expectedLoads}");
                if (!hook)
                    continue;
                int reinitialized = SessionState.GetInt(Key(test, "Hook", i), -1);
                if (reinitialized != 1)
                    wrong.Add($"entry {i} ({plan[i]}): the hook re-initialized UniTask {reinitialized} times");
                string fronts = SessionState.GetString(Key(test, "Fronts", i), "?");
                if (fronts != OurFronts)
                    wrong.Add($"entry {i} ({plan[i]}): fronts {fronts}");
            }
            Assert.That(wrong, Is.Empty, "expected intervals " + expected);
        }

        static void EraseResults(string test, int count)
        {
            SessionState.EraseInt(Prefix + test + ".LoadsBefore");
            for (int i = 0; i < count; i++)
            {
                SessionState.EraseString(Key(test, "Intervals", i));
                SessionState.EraseInt(Key(test, "Loads", i));
                SessionState.EraseString(Key(test, "Fronts", i));
                SessionState.EraseInt(Key(test, "Hook", i));
            }
        }

        // leaves play, puts the settings back, takes the windows out and our wrapped systems back to new ones — after a red
        // test and across a reload too
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();
            EndEntry();
            if (SessionState.GetBool(SavedKey, false))
            {
                EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(OptionsKey, 0);
                EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(EnabledKey, false);
                SessionState.EraseBool(SavedKey);
                SessionState.EraseInt(OptionsKey);
                SessionState.EraseBool(EnabledKey);
            }
            SessionState.EraseBool(HookKey);
            SessionState.EraseInt(HookCountKey);
            SessionState.EraseInt(ProgressKey);
            SessionState.EraseInt(StepsKey);
            EraseResults("RT1", Settings.Length);
            EraseResults("RT2", HookSettings.Length);
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            RemoveWindows(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
            FeedbackPlayerLoop.Reinstall();
            FrameRecorder.Clear();
        }

        // ---- twelve entries, every one's intervals equal the table, the domain loads follow the setting

        [UnityTest]
        public IEnumerator RT01_IntervalsAcrossSettings()
        {
            Begin(false);
            while (Progress < Settings.Length)
            {
                Step("RT1", Settings);
                yield return new EnterPlayMode();
                Entered("RT1");
                for (int i = 0; i < 4; i++)
                    yield return null;
                Judge("RT1");
                EndEntry();
                yield return new ExitPlayMode();
                SessionState.SetInt(ProgressKey, Progress + 1);
            }
            Verify("RT1", Settings, false);
        }

        // ---- the hook re-initialized UniTask in each entry, ours are at the fronts after it, and the intervals equal the same
        // table

        [UnityTest]
        public IEnumerator RT02_UniTaskReinitializedBeforeSceneLoad()
        {
            Begin(true);
            while (Progress < HookSettings.Length)
            {
                Step("RT2", HookSettings);
                yield return new EnterPlayMode();
                Entered("RT2");
                for (int i = 0; i < 4; i++)
                    yield return null;
                Judge("RT2");
                EndEntry();
                yield return new ExitPlayMode();
                SessionState.SetInt(ProgressKey, Progress + 1);
            }
            Verify("RT2", HookSettings, true);
        }
    }
}
