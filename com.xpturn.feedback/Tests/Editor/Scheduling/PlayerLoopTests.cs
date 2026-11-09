using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.TestTools;

namespace xpTURN.Feedback.Tests.Editor
{
    // EditMode — six play entries, the domain reload off.
    // Play entries and exits are yielded in the test body: yielded from an inner IEnumerator they pass silently. TearDown
    // leaves play and puts back the settings, the PlayerLoop and Default, after a red test too — the engine puts the time
    // scale back at the exit, and an edit-mode write would change the project settings (ProjectSettingsCheck)
    public class PlayerLoopTests
    {
        const string OurFronts = "FeedbackRevert/FeedbackUpdate/FeedbackLateApply";
        const string SavedKey = "xpTURN.Feedback.Tests.PlayerLoop.Saved";
        const string OptionsKey = "xpTURN.Feedback.Tests.PlayerLoop.Options";
        const string EnabledKey = "xpTURN.Feedback.Tests.PlayerLoop.Enabled";

        struct Foreign { }
        struct Control { }

        static object s_Token;
        static int s_Control;
        static int s_UserQuit;
        static int s_UserEntered;
        static bool s_UserQuitThrows;
        static FeedbackScheduler s_SavedDefault;
        static bool s_DefaultSwapped;
        static PlayerLoopSystem? s_SavedLoop;
        static ProjectSettingsCheck.State s_Settings;

        static void UserQuit()
        {
            s_UserQuit++;
            if (s_UserQuitThrows)
                throw new InvalidOperationException("test user quitting");
        }

        static void UserPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
                s_UserEntered++;
        }

        // the settings go back through SessionState — a domain reload would clear a static
        static void DisableDomainReload()
        {
            SessionState.SetBool(SavedKey, true);
            SessionState.SetInt(OptionsKey, (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetBool(EnabledKey, EditorSettings.enterPlayModeOptionsEnabled);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        }

        // its own GetCurrentPlayerLoop — a struct copy shares the lists, and changing it would change the saved one
        static void SaveLoop() => s_SavedLoop = PlayerLoop.GetCurrentPlayerLoop();

        static void SwapDefault(FeedbackScheduler scheduler)
        {
            if (!s_DefaultSwapped)
            {
                s_SavedDefault = FeedbackScheduler.Default;
                s_DefaultSwapped = true;
            }
            FeedbackScheduler.Default = scheduler;
        }

        static void RestoreDefault()
        {
            if (!s_DefaultSwapped)
                return;
            FeedbackScheduler.Default = s_SavedDefault;
            s_SavedDefault = null;
            s_DefaultSwapped = false;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            s_Settings = ProjectSettingsCheck.Take();
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();
            if (SessionState.GetBool(SavedKey, false))
            {
                EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(OptionsKey, 0);
                EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(EnabledKey, false);
                SessionState.EraseBool(SavedKey);
                SessionState.EraseInt(OptionsKey);
                SessionState.EraseBool(EnabledKey);
            }
            Application.quitting -= UserQuit;
            EditorApplication.playModeStateChanged -= UserPlayMode;
            s_UserQuitThrows = false;
            RestoreDefault();
            if (s_SavedLoop.HasValue)
            {
                PlayerLoop.SetPlayerLoop(s_SavedLoop.Value);
                s_SavedLoop = null;
            }
            ProjectSettingsCheck.FailIfChanged(s_Settings);
        }

        // ---- the loop, read

        static bool IsOurs(Type type) => type != null && type.DeclaringType == typeof(FeedbackPlayerLoop);

        static int CountOurs() => Count(PlayerLoop.GetCurrentPlayerLoop());

        static int Count(PlayerLoopSystem system)
        {
            int count = IsOurs(system.type) ? 1 : 0;
            if (system.subSystemList != null)
                foreach (var child in system.subSystemList)
                    count += Count(child);
            return count;
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

        static PlayerLoopSystem FindOurs(PlayerLoopSystem loop, Type phase)
        {
            foreach (var system in loop.subSystemList)
                if (system.type == phase)
                    foreach (var child in system.subSystemList)
                        if (IsOurs(child.type))
                            return child;
            throw new InvalidOperationException("ours not found in " + phase.Name);
        }

        // ---- the loop, changed (on a loop just taken from GetCurrentPlayerLoop)

        static void InsertFront(ref PlayerLoopSystem loop, Type phase, PlayerLoopSystem system)
        {
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != phase)
                    continue;
                var list = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList ?? Array.Empty<PlayerLoopSystem>());
                list.Insert(0, system);
                loop.subSystemList[i].subSystemList = list.ToArray();
                return;
            }
        }

        static void RemoveOurs(ref PlayerLoopSystem system)
        {
            if (system.subSystemList == null)
                return;
            var kept = new List<PlayerLoopSystem>();
            foreach (var child in system.subSystemList)
            {
                if (IsOurs(child.type))
                    continue;
                var copy = child;
                RemoveOurs(ref copy);
                kept.Add(copy);
            }
            system.subSystemList = kept.ToArray();
        }

        // StoppedWarning logs made by the action — one text counted, whatever else the play session logs
        static int StoppedWarnings(Action action)
        {
            int count = 0;
            Application.LogCallback onLog = (condition, stackTrace, type) =>
            {
                if (type == LogType.Warning && condition == FeedbackScheduler.StoppedWarning)
                    count++;
            };
            Application.logMessageReceived += onLog;
            try
            {
                action();
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }
            return count;
        }

        // ---- three entries without a reload

        // each entry: no reload (the static token), ours at the three fronts, a new open Default. Each exit: ours stay, the
        // Default is closed, a scene object's OnApplicationQuit saw it open and its OnDestroy closed, refusing an Add. A user
        // quitting handler after ours throws in the second session — ours ran first (subscribed once per domain)
        [UnityTest]
        public IEnumerator PT01_ThreeEntriesWithoutReload()
        {
            DisableDomainReload();
            var token = new object();
            s_Token = token;
            var previous = FeedbackScheduler.Default;
            for (int session = 0; session < 3; session++)
            {
                yield return new EnterPlayMode(false);
                yield return null;
                Assert.That(s_Token, Is.SameAs(token), "no domain reload");
                Assert.That(CountOurs(), Is.EqualTo(3), "ours in the loop");
                Assert.That(Fronts(), Is.EqualTo(OurFronts), "at the fronts");
                var current = FeedbackScheduler.Default;
                Assert.That(current, Is.Not.Null.And.Not.SameAs(previous), "a new Default");
                Assert.That(current.IsShutdown, Is.False, "the new Default is open");
                previous = current;
                ShutdownObserver.ResetRecord();
                new GameObject(nameof(ShutdownObserver)).AddComponent<ShutdownObserver>();
                if (session == 0)
                    Application.quitting += UserQuit;
                s_UserQuitThrows = session == 1;
                if (s_UserQuitThrows)
                    LogAssert.Expect(LogType.Exception, new Regex("test user quitting"));
                yield return null;
                yield return new ExitPlayMode();
                Assert.That(CountOurs(), Is.EqualTo(3), "ours stay after the exit");
                Assert.That(previous.IsShutdown, Is.True, "closed after the exit");
                Assert.That(ShutdownObserver.SawAtQuit, Is.EqualTo("open"), "OnApplicationQuit");
                Assert.That(ShutdownObserver.SawAtDestroy, Is.EqualTo("closed"), "OnDestroy, session " + session);
                Assert.That(ShutdownObserver.AddRefused, Is.True, "an Add in OnDestroy");
            }
        }

        // ---- the real loop's wiring

        // a recording scheduler put in as Default: Revert → Apply(Update) → Apply(LateApply) each frame, a frame number per
        // Revert; a runner added goes between them from the next frame and gets the engine's deltas of that frame — the
        // unscaled one cut at maximumDeltaTime
        [UnityTest]
        public IEnumerator PT02_RealLoopRunsTheStages()
        {
            DisableDomainReload();
            yield return new EnterPlayMode(false);
            yield return null;
            var record = new CallRecord();
            var test = new FeedbackScheduler(ContextKind.Test, new RecordingLayers(record));
            SwapDefault(test);
            Time.timeScale = 0.5f;
            for (int i = 0; i < 3; i++)
                yield return null;
            string stages = record.Joined;
            Assert.That(stages, Does.Match(@"^(Revert,Apply\(Update\),Apply\(LateApply\)(,|$))+$"), "the stages");
            Assert.That(test.Context.Frame, Is.EqualTo(Regex.Matches(stages, "Revert").Count), "a frame number per Revert");
            var runner = new FakeRunner("R", record);
            test.Reserve(1);
            test.Add(runner, 0);
            string mark = record.Joined;
            for (int i = 0; i < 3; i++)
                yield return null;
            string tail = record.Joined.Substring(mark.Length).TrimStart(',');
            Assert.That(tail, Does.StartWith("Revert,R.Advance,Apply(Update),Apply(LateApply)"), "the runner's place");
            // equality with the values read in the same call — half of the unscaled delta is not exact
            Assert.That(runner.EngineScaled, Is.Not.EqualTo(runner.EngineUnscaled), "the time scale makes the two differ");
            Assert.That(runner.LastTime.ScaledDelta, Is.EqualTo(runner.EngineScaled), "scaled");
            Assert.That(runner.LastTime.UnscaledDelta, Is.EqualTo(Mathf.Min(runner.EngineUnscaled, Time.maximumDeltaTime)), "unscaled");
            RestoreDefault();
            test.Shutdown();
            yield return new ExitPlayMode();
        }

        // ---- Reinstall

        // in edit mode: a copy moved into Update goes, the fronts come back from foreign systems, a second call is the same
        [Test]
        public void PT04_ReinstallTakesTheFrontsBack()
        {
            SaveLoop();
            FeedbackPlayerLoop.Reinstall();
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var revert = FindOurs(loop, typeof(EarlyUpdate));
            InsertFront(ref loop, typeof(EarlyUpdate), new PlayerLoopSystem { type = typeof(Foreign) });
            InsertFront(ref loop, typeof(PreLateUpdate), new PlayerLoopSystem { type = typeof(Foreign) });
            InsertFront(ref loop, typeof(PostLateUpdate), new PlayerLoopSystem { type = typeof(Foreign) });
            InsertFront(ref loop, typeof(Update), revert);
            PlayerLoop.SetPlayerLoop(loop);
            Assert.That(CountOurs(), Is.EqualTo(4), "a copy in Update");
            Assert.That(Fronts(), Is.EqualTo("Foreign/Foreign/Foreign"), "foreign fronts");
            FeedbackPlayerLoop.Reinstall();
            Assert.That(CountOurs(), Is.EqualTo(3), "three after Reinstall");
            Assert.That(Fronts(), Is.EqualTo(OurFronts), "our fronts");
            FeedbackPlayerLoop.Reinstall();
            Assert.That(CountOurs(), Is.EqualTo(3), "three after a second Reinstall");
            Assert.That(Fronts(), Is.EqualTo(OurFronts), "our fronts again");
        }

        // ---- the edit-mode guard

        // after play a closed Default hides a missing guard, so Default is swapped — a null, then a live recording scheduler.
        // A control system without a guard shows the editor ran the loop; a callback meeting the null would log and fail
        [UnityTest]
        public IEnumerator PT05_EditModeGuard()
        {
            SaveLoop();
            FeedbackPlayerLoop.Reinstall();
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            InsertFront(ref loop, typeof(EarlyUpdate), new PlayerLoopSystem { type = typeof(Control), updateDelegate = () => s_Control++ });
            PlayerLoop.SetPlayerLoop(loop);
            Assert.That(CountOurs(), Is.EqualTo(3), "ours in the loop");
            SwapDefault(null);
            int control = s_Control;
            for (int i = 0; i < 10; i++)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }
            Assert.That(s_Control - control, Is.EqualTo(10), "the loop ran (a null Default)");
            var record = new CallRecord();
            var test = new FeedbackScheduler(ContextKind.Test, new RecordingLayers(record));
            var runner = new FakeRunner("R", record);
            test.Reserve(1);
            test.Add(runner, 0);
            SwapDefault(test);
            control = s_Control;
            for (int i = 0; i < 10; i++)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }
            Assert.That(s_Control - control, Is.EqualTo(10), "the loop ran (a live Default)");
            Assert.That(record.Joined, Is.Empty, "no stage");
            Assert.That(test.Context.Frame, Is.EqualTo(0), "no frame");
            Assert.That(runner.Advances, Is.EqualTo(0), "no advance");
        }

        // ---- a missing phase

        [Test]
        public void PT07_MissingPhase()
        {
            var synthetic = new PlayerLoopSystem
            {
                subSystemList = new[]
                {
                    new PlayerLoopSystem { type = typeof(EarlyUpdate), subSystemList = new[] { new PlayerLoopSystem { type = typeof(Foreign) } } },
                    new PlayerLoopSystem { type = typeof(PostLateUpdate), subSystemList = new[] { new PlayerLoopSystem { type = typeof(Foreign) } } },
                },
            };
            Assert.That(FeedbackPlayerLoop.Install(ref synthetic), Is.False, "a phase missing");
            Assert.That(Front(synthetic, typeof(EarlyUpdate)), Is.EqualTo("FeedbackRevert"));
            Assert.That(Front(synthetic, typeof(PostLateUpdate)), Is.EqualTo("FeedbackLateApply"));
            SaveLoop();
            var cut = PlayerLoop.GetCurrentPlayerLoop();
            cut.subSystemList = cut.subSystemList.Where(phase => phase.type != typeof(PreLateUpdate)).ToArray();
            PlayerLoop.SetPlayerLoop(cut);
            LogAssert.Expect(LogType.Warning, FeedbackPlayerLoop.MissingPhaseWarning);
            FeedbackPlayerLoop.Reinstall();
            LogAssert.NoUnexpectedReceived();
        }

        // ---- the stop diagnostic, one session

        // while the stages run an Add is quiet; off the main thread the check is skipped — the thread assert alone, no
        // exception; with ours out of the loop two Adds warn once; a Runtime scheduler the loop never ran warns too — a
        // failed recompile backup
        [UnityTest]
        public IEnumerator PT09_StopDiagnostic()
        {
            DisableDomainReload();
            yield return new EnterPlayMode(false);
            yield return null;
            yield return null;
            var scheduler = FeedbackScheduler.Default;
            scheduler.Reserve(8);
            var runner = new CountingRunner();
            var orphan = new FeedbackScheduler(ContextKind.Runtime);
            orphan.Reserve(2);
            LogAssert.Expect(LogType.Assert, FeedbackScheduler.ThreadMessage);
            Task.Run(() => scheduler.Add(runner, 0)).Wait();
            Assert.That(StoppedWarnings(() => scheduler.Add(runner, 1)), Is.EqualTo(0), "quiet while the stages run");
            SaveLoop();
            var cut = PlayerLoop.GetCurrentPlayerLoop();
            RemoveOurs(ref cut);
            PlayerLoop.SetPlayerLoop(cut);
            for (int i = 0; i < 3; i++)
                yield return null;
            Assert.That(StoppedWarnings(() =>
            {
                scheduler.Add(runner, 2);
                scheduler.Add(runner, 3);
            }), Is.EqualTo(1), "once after the stages stopped");
            Assert.That(StoppedWarnings(() => orphan.Add(runner, 0)), Is.EqualTo(1), "a scheduler the loop never ran");
            orphan.Shutdown();
            yield return new ExitPlayMode();
        }

        // ---- the hooks do not throw

        // Default null at the exit: the user's quitting and EnteredEditMode handlers after ours still run, and nothing is
        // logged. Ours leave the loop first — a callback meeting the null would throw each frame until the exit
        [UnityTest]
        public IEnumerator PT10_HooksDoNotThrow()
        {
            DisableDomainReload();
            yield return new EnterPlayMode(false);
            yield return null;
            s_UserQuit = 0;
            s_UserEntered = 0;
            Application.quitting += UserQuit;
            EditorApplication.playModeStateChanged += UserPlayMode;
            var real = FeedbackScheduler.Default;
            SaveLoop();
            var cut = PlayerLoop.GetCurrentPlayerLoop();
            RemoveOurs(ref cut);
            PlayerLoop.SetPlayerLoop(cut);
            SwapDefault(null);
            yield return new ExitPlayMode();
            Assert.That(s_UserQuit, Is.EqualTo(1), "the user's quitting handler ran");
            Assert.That(s_UserEntered, Is.EqualTo(1), "the user's EnteredEditMode handler ran");
            real.Shutdown();
        }
    }
}
