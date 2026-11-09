using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace xpTURN.Feedback.Tests.Editor
{
    // extensions across entries · the restore at the exit · an edit-mode restore, and their shape with the real TimeScale —
    // six play entries, the domain reload off; the project settings check at the end of TearDown. Time values are written in
    // play only. Play entries and exits are yielded in the test body.
    // TearDown leaves play and puts back the settings, the shader global and the hidden objects, after a red test too
    public class ServiceLifetimeTests
    {
        const string SavedKey = "xpTURN.Feedback.Tests.ServiceLifetime.Saved";
        const string OptionsKey = "xpTURN.Feedback.Tests.ServiceLifetime.Options";
        const string EnabledKey = "xpTURN.Feedback.Tests.ServiceLifetime.Enabled";

        static ProjectSettingsCheck.State s_Settings;
        static float s_Global;

        // the settings go back through SessionState — a domain reload would clear a static
        static void DisableDomainReload()
        {
            SessionState.SetBool(SavedKey, true);
            SessionState.SetInt(OptionsKey, (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetBool(EnabledKey, EditorSettings.enterPlayModeOptionsEnabled);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            s_Settings = ProjectSettingsCheck.Take();
            s_Global = Shader.GetGlobalFloat(GlobalStateService.GlobalId);
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
            DestroyCallback.Action = null;
            Shader.SetGlobalFloat(GlobalStateService.GlobalId, s_Global);
            GlobalStateService.DestroyHidden();
            ProjectSettingsCheck.FailIfChanged(s_Settings);
        }

        // StaleWarning logs made by the action — one text counted, whatever else the play session logs
        static int StaleWarnings(Action action)
        {
            int count = 0;
            Application.LogCallback onLog = (condition, stackTrace, type) =>
            {
                if (type == LogType.Warning && condition == FeedbackService.StaleWarning)
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

        // ---- extensions across three entries

        // registered once in the test: each session a new open instance of that session's context. The second key is made in
        // the second session — its number is above the first (the counter did not go back). The first session's instance,
        // asked in the second, refuses and warns once
        [UnityTest]
        public IEnumerator VT09_ExtensionsAcrossThreeEntries()
        {
            DisableDomainReload();
            var first = new ServiceKey();
            FeedbackContext.RegisterExtension(first, c => new TestService(c, "first"));
            TestService previous = null;
            TestService sessionOne = null;
            for (int session = 0; session < 3; session++)
            {
                yield return new EnterPlayMode(false);
                yield return null;
                var context = FeedbackScheduler.Default.Context;
                var current = context.GetExtension(first) as TestService;
                Assert.That(current, Is.Not.Null, "session " + session);
                Assert.That(current.IsClosed, Is.False, "open");
                Assert.That(current.Context, Is.SameAs(context), "this session's context");
                Assert.That(current, Is.Not.SameAs(previous), "a new instance");
                if (session == 0)
                    sessionOne = current;
                if (session == 1)
                {
                    var second = new ServiceKey();
                    FeedbackContext.RegisterExtension(second, c => new TestService(c, "second"));
                    Assert.That(second.Id, Is.GreaterThan(first.Id), "the counter did not go back");
                    Assert.That((context.GetExtension(second) as TestService)?.Name, Is.EqualTo("second"));
                    Assert.That((context.GetExtension(first) as TestService)?.Name, Is.EqualTo("first"));
                    int warnings = StaleWarnings(() =>
                    {
                        Assert.That(sessionOne.Request(), Is.False, "the kept instance refuses");
                        Assert.That(sessionOne.Request(), Is.False);
                    });
                    Assert.That(warnings, Is.EqualTo(1), "one stale warning");
                }
                previous = current;
                yield return new ExitPlayMode();
            }
        }

        // ---- the restore, one session

        // first a test scheduler's service put back by its Shutdown before the exit — the time values too, which the engine
        // would hide after it. A service of this session that threw refuses quietly. Then the Default's service, and a cleanup
        // that asks it again, closed by then and quiet; after the exit the shader global and the hidden objects are as before.
        // The positive controls come first
        [UnityTest]
        public IEnumerator VT10_RestoreAtTheExit()
        {
            DisableDomainReload();
            var key = new ServiceKey();
            FeedbackContext.RegisterExtension(key, c => new GlobalStateService(c));
            float global = Shader.GetGlobalFloat(GlobalStateService.GlobalId);
            int hidden = GlobalStateService.HiddenCount();
            yield return new EnterPlayMode(false);
            yield return null;

            float timeScale = Time.timeScale, fixedDelta = Time.fixedDeltaTime, maximumDelta = Time.maximumDeltaTime;
            var test = new FeedbackScheduler(ContextKind.Test);
            var probe = (GlobalStateService)test.Context.GetExtension(key);
            Assert.That(probe.Apply(), Is.True);
            Assert.That(Shader.GetGlobalFloat(GlobalStateService.GlobalId), Is.EqualTo(GlobalStateService.AppliedGlobal), "applied (positive control)");
            Assert.That(GlobalStateService.HiddenCount(), Is.EqualTo(hidden + 1), "a hidden object (positive control)");
            test.Shutdown();
            Assert.That(Time.timeScale, Is.EqualTo(timeScale), "timeScale");
            Assert.That(Time.maximumDeltaTime, Is.EqualTo(maximumDelta), "maximumDeltaTime");
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(fixedDelta).Within(1f / 141120000f), "fixedDeltaTime — within a step");
            Assert.That(Shader.GetGlobalFloat(GlobalStateService.GlobalId), Is.EqualTo(global), "shader global");
            Assert.That(GlobalStateService.HiddenCount(), Is.EqualTo(hidden), "hidden object");

            var throwKey = new ServiceKey();
            FeedbackContext.RegisterExtension(throwKey, c => new TestService(c, "T") { ThrowOnUpdate = true });
            var thrower = (TestService)FeedbackScheduler.Default.Context.GetExtension(throwKey);
            LogAssert.Expect(LogType.Exception, new Regex("test service update T"));
            yield return null;
            yield return null;
            Assert.That(thrower.IsClosed, Is.True, "closed after throwing (positive control)");
            Assert.That(StaleWarnings(() => Assert.That(thrower.Request(), Is.False)), Is.EqualTo(0), "no stale warning in this session");

            var service = (GlobalStateService)FeedbackScheduler.Default.Context.GetExtension(key);
            Assert.That(service.Apply(), Is.True);
            bool called = false, closedAtCleanup = false;
            int cleanupWarnings = 0;
            DestroyCallback.Action = () =>
            {
                called = true;
                closedAtCleanup = service.IsClosed;
                cleanupWarnings = StaleWarnings(() => service.Apply());
            };
            new GameObject(nameof(DestroyCallback)).AddComponent<DestroyCallback>();
            yield return null;
            yield return new ExitPlayMode();
            Assert.That(called, Is.True, "the cleanup ran (positive control)");
            Assert.That(closedAtCleanup, Is.True, "closed before the cleanup");
            Assert.That(cleanupWarnings, Is.EqualTo(0), "no stale warning at the cleanup");
            Assert.That(Shader.GetGlobalFloat(GlobalStateService.GlobalId), Is.EqualTo(global), "shader global after the exit");
            Assert.That(GlobalStateService.HiddenCount(), Is.EqualTo(hidden), "hidden objects after the exit");
        }

        // ---- an edit-mode restore changes no setting

        // a test scheduler's service applied in play and left open; the engine puts the time values back at the exit, so the
        // Shutdown in edit mode — only when different — writes none of them, and still restores the rest
        [UnityTest]
        public IEnumerator VT13_EditModeRestoreChangesNoSetting()
        {
            DisableDomainReload();
            var key = new ServiceKey();
            FeedbackContext.RegisterExtension(key, c => new GlobalStateService(c));
            float global = Shader.GetGlobalFloat(GlobalStateService.GlobalId);
            int hidden = GlobalStateService.HiddenCount();
            yield return new EnterPlayMode(false);
            yield return null;
            var test = new FeedbackScheduler(ContextKind.Test);
            var probe = (GlobalStateService)test.Context.GetExtension(key);
            Assert.That(probe.Apply(), Is.True);
            yield return new ExitPlayMode();
            bool dirty = ProjectSettingsCheck.TimeManagerDirty();
            int fixedBits = BitConverter.SingleToInt32Bits(Time.fixedDeltaTime);
            float timeScale = Time.timeScale, maximumDelta = Time.maximumDeltaTime;
            test.Shutdown();
            Assert.That(!dirty && ProjectSettingsCheck.TimeManagerDirty(), Is.False, "the restore did not dirty the settings");
            Assert.That(BitConverter.SingleToInt32Bits(Time.fixedDeltaTime), Is.EqualTo(fixedBits), "fixedDeltaTime not written");
            Assert.That(Time.timeScale, Is.EqualTo(timeScale));
            Assert.That(Time.maximumDeltaTime, Is.EqualTo(maximumDelta));
            Assert.That(Shader.GetGlobalFloat(GlobalStateService.GlobalId), Is.EqualTo(global), "the shader global restored");
            Assert.That(GlobalStateService.HiddenCount(), Is.EqualTo(hidden), "the hidden object gone");
        }

        // ---- the restore at the exit and the edit-mode restore with the real TimeScale

        // before the exit: a test scheduler in play with the test service (made first) and the real TimeScale — a request at 20
        // with the option on (injected settings) — ticked once, then Shutdown: the three values are the entry ones (fixed within
        // a step) in each order (alone · the test service first · the TimeScale first) — the reverse close and the
        // TimeScale's restore order (fixed first: 0.4 × 20 is above the entry max 0.333, so a wrong order leaves the max). The
        // control: closed forward by hand, the test service's values stay (the shape sees the order). After the exit:
        // a request left open, closed in edit mode, writes nothing — fixedDeltaTime is changed in play first, or the engine's
        // restore and the service's value are equal and hide a write
        [UnityTest]
        public IEnumerator VE15_RealTimeScaleRestore()
        {
            DisableDomainReload();
            var key = new ServiceKey();
            FeedbackContext.RegisterExtension(key, c => new GlobalStateService(c));
            yield return new EnterPlayMode(false);
            yield return null;
            var fails = new System.Collections.Generic.List<string>();
            float ts0 = Time.timeScale, fixed0 = Time.fixedDeltaTime, max0 = Time.maximumDeltaTime;
            bool AtEntry() => Time.timeScale == ts0 && Time.maximumDeltaTime == max0 && Math.Abs(Time.fixedDeltaTime - fixed0) < 1e-8f;
            string Now() => Time.timeScale + "/" + Time.fixedDeltaTime + "/" + Time.maximumDeltaTime;
            void PutBack()
            {
                Time.timeScale = ts0;
                Time.fixedDeltaTime = fixed0;
                Time.maximumDeltaTime = max0;
            }
            foreach (string order in new[] { "alone", "the test service first", "the time scale first" })
            {
                var s = new FeedbackScheduler(ContextKind.Test);
                s.Context.UseSettings(new SettingsValues { AdjustFixedDeltaTime = true });
                if (order == "the test service first")
                    ((GlobalStateService)s.Context.GetExtension(key)).Apply();
                s.Context.TimeScale.Request(20f, 5.0);
                s.Tick(0.01f);
                if (order == "the time scale first")
                    ((GlobalStateService)s.Context.GetExtension(key)).Apply();
                string during = Now();
                s.Shutdown();
                if (!AtEntry())
                    fails.Add(order + " — during " + during + " · after " + Now());
                PutBack();
            }
            {
                var s = new FeedbackScheduler(ContextKind.Test);
                s.Context.UseSettings(new SettingsValues { AdjustFixedDeltaTime = true });
                var probe = (GlobalStateService)s.Context.GetExtension(key);
                probe.Apply();
                TimeScaleService t = s.Context.TimeScale;
                t.Request(20f, 5.0);
                s.Tick(0.01f);
                probe.Shutdown();
                t.Shutdown();
                if (!(Time.timeScale == GlobalStateService.AppliedTimeScale && Time.maximumDeltaTime == GlobalStateService.AppliedMaximumDelta))
                    fails.Add("the forward-close control (positive) — " + Now());
                PutBack();
                s.Shutdown();
                PutBack();
            }
            // a play-only fixed step: the service's value then differs from what the engine puts back at the exit
            Time.fixedDeltaTime = 0.025f;
            var open = new FeedbackScheduler(ContextKind.Test);
            open.Context.UseSettings(new SettingsValues { AdjustFixedDeltaTime = true });
            open.Context.TimeScale.Request(20f, 5.0);
            open.Tick(0.01f);
            yield return new ExitPlayMode();
            bool dirty = ProjectSettingsCheck.TimeManagerDirty();
            int bits = BitConverter.SingleToInt32Bits(Time.fixedDeltaTime);
            open.Shutdown();
            if ((!dirty && ProjectSettingsCheck.TimeManagerDirty()) || bits != BitConverter.SingleToInt32Bits(Time.fixedDeltaTime))
                fails.Add("the edit-mode close wrote — dirty " + ProjectSettingsCheck.TimeManagerDirty() + " · " + Now());
            Assert.That(fails, Is.Empty);
        }
    }
}
