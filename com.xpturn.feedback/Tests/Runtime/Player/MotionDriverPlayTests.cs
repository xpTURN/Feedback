using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests
{
    // PlayMode — the lifetime in real frames, Follow's frames, timeScale 0, the first runs, a new Default. Default is a test
    // scheduler on the real loop (captureDeltaTime 1/90); values are seen at the window at PostLateUpdate's end (a loop
    // system). Drivers are added to an inactive object, set, then activated (AddComponent's OnEnable runs before the item is
    // set). A row checks everything and judges at the end. Warnings and errors are counted by a log listener
    public sealed class MotionDriverPlayTests
    {
        struct DriverWindow { }

        static Action s_Window;
        static readonly List<string> s_Warnings = new List<string>();
        static readonly List<string> s_Errors = new List<string>();
        static FeedbackScheduler s_Saved;
        static FeedbackScheduler s_Test;
        readonly List<string> m_Fail = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
                s_Warnings.Add(condition);
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                s_Errors.Add(type + ": " + condition);
        }

        static void OnWindow() => s_Window?.Invoke();

        static void SetWindow(bool on)
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate))
                    continue;
                var list = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList);
                list.RemoveAll(s => s.type == typeof(DriverWindow));
                if (on)
                    list.Add(new PlayerLoopSystem { type = typeof(DriverWindow), updateDelegate = OnWindow });
                loop.subSystemList[i].subSystemList = list.ToArray();
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        [SetUp]
        public void SetUp()
        {
            m_Fail.Clear();
            s_Warnings.Clear();
            s_Errors.Clear();
            PlayerRuntime.ResetSession();
            Application.logMessageReceived += OnLog;
            Time.captureDeltaTime = 1f / 90f;
            s_Saved = FeedbackScheduler.Default;
            s_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = s_Test;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SetWindow(false);
            s_Window = null;
            MotionDriverFrames.OnUpdate = null;
            MotionDriverFrames.OnLateUpdate = null;
            Time.timeScale = 1f;
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Made.Clear();
            yield return null;
            FeedbackScheduler.Default = s_Saved;
            if (s_Test != null && !s_Test.IsShutdown)
                s_Test.Shutdown();
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Schedulers.Clear();
            Time.captureDeltaTime = 0f;
            Application.logMessageReceived -= OnLog;
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        static TargetRef Self => new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };

        static OscillateMotion Osc(float f = 1f, float phase = 0f) => new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(1f, 0f, 0f, 0f), m_Frequency = f, m_Phase = phase } };
        static SpringMotion Spring(float f = 6f, float d = 0.4f) => new SpringMotion { m_Params = new SpringParams { m_Mode = SpringMode.MoveTo, m_Frequency = f, m_Damping = d, m_Settle = 0.001f } };
        static BlinkMotion Blink() => new BlinkMotion { m_Params = new BlinkParams { m_Value = Vector4.zero, m_Period = 0.1f, m_Duty = 0.5f, m_StartOn = true } };

        // made inactive, set, then activated — OnEnable sees the item
        MotionDriver Make(string name, TargetItem item, MotionDriverMode mode, Vector3 at = default, Action<MotionDriver> set = null)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.localPosition = at;
            m_Made.Add(go);
            var d = go.AddComponent<MotionDriver>();
            d.m_Item = item;
            d.m_Mode = mode;
            set?.Invoke(d);
            go.SetActive(true);
            return d;
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
                yield return null;
        }

        // the lifetime in real frames
        [UnityTest]
        public IEnumerator RP01_Lifetime()
        {
            Vector3 home = new Vector3(2f, 0f, 0f);
            // phase 0.25 at 0.5 Hz: 1 at 0 s (an enable's t = 0 shows)
            MotionDriver d = Make("loop", new PositionItem { m_Motion = Osc(0.5f, 0.25f), m_Target = Self }, MotionDriverMode.Loop, home);
            Transform t = d.transform;
            int moved = 0, frames = 0, atHome = 0;
            bool watch = true;
            s_Window = () =>
            {
                if (!watch)
                    return;
                frames++;
                if (t.localPosition != home)
                    moved++;
                else
                    atHome++;
            };
            SetWindow(true);
            yield return Frames(10);
            Check(moved == frames && frames >= 9 && s_Test.ActiveCount == 1 && s_Test.ReservedCount == 1, "on — moves in the window · active 1 · reserved 1 (" + moved + "/" + frames + " · " + s_Test.ActiveCount + "/" + s_Test.ReservedCount + ")");
            moved = frames = atHome = 0;
            d.enabled = false;
            yield return Frames(5);
            Check(atHome == frames && s_Test.ActiveCount == 0 && s_Test.ReservedCount == 1, "off — home in the window · active 0 · reserved 1 (" + atHome + "/" + frames + " · " + s_Test.ActiveCount + "/" + s_Test.ReservedCount + ")");
            // the enable's frame shows t = 0; the slot advances from the next frame
            var trace = new List<double>();
            watch = false;
            s_Window = () =>
            {
                if (trace.Count < 2)
                    trace.Add(t.localPosition.x - home.x);
            };
            d.enabled = true;
            double seconds0 = d.Seconds;
            yield return Frames(2);
            Check(trace.Count == 2 && Math.Abs(trace[0] - 1.0) < 1e-3, "the enable's frame shows t = 0 (" + string.Join(" ", trace) + ")");
            Check(seconds0 == 0.0 && Math.Abs(d.Seconds - 1.0 / 90.0) < 1e-6, "the seconds from 0 — the slot advances from the next frame (" + F(d.Seconds * 90) + " frames)");
            // the game's Update · LateUpdate read the base; an enable · a disable in LateUpdate shows in that frame's render
            var rec = new GameObject("frames").AddComponent<MotionDriverFrames>();
            m_Made.Add(rec.gameObject);
            int start = Time.frameCount + 1;
            var seen = new List<string>();
            MotionDriverFrames.OnUpdate = f => seen.Add("U" + (f - start) + ":" + F(t.localPosition.x - home.x));
            MotionDriverFrames.OnLateUpdate = f =>
            {
                seen.Add("L" + (f - start) + ":" + F(t.localPosition.x - home.x));
                if (f - start == 1)
                    d.enabled = false;
                if (f - start == 3)
                    d.enabled = true;
            };
            var window = new List<string>();
            s_Window = () => window.Add("W" + (Time.frameCount - start) + ":" + F(t.localPosition.x - home.x));
            yield return Frames(5);
            MotionDriverFrames.OnUpdate = null;
            MotionDriverFrames.OnLateUpdate = null;
            s_Window = null;
            string all = string.Join(" ", seen) + " | " + string.Join(" ", window);
            Check(!seen.Exists(e => !e.EndsWith(":0", StringComparison.Ordinal)), "the game's Update · LateUpdate read the base — " + all);
            Check(window.Contains("W1:0") && window.Contains("W3:1"), "a disable · an enable in LateUpdate shows in that frame's render — " + all);
            // 100 cycles a frame apart · in one frame
            int r0 = s_Test.ReservedCount, a0 = s_Test.ActiveCount, w0 = s_Warnings.Count;
            for (int i = 0; i < 100; i++)
            {
                d.enabled = false;
                yield return null;
                d.enabled = true;
                yield return null;
            }
            Check(s_Test.ReservedCount == r0 && s_Test.ActiveCount == a0 && s_Warnings.Count == w0, "100 cycles a frame apart — the counts, no warning (" + s_Test.ReservedCount + "/" + s_Test.ActiveCount + " · " + (s_Warnings.Count - w0) + ")");
            w0 = s_Warnings.Count;
            for (int i = 0; i < 100; i++)
            {
                d.enabled = false;
                d.enabled = true;
            }
            yield return Frames(2);
            Check(s_Warnings.Count - w0 == 2 && s_Test.ReservedCount == r0 && s_Test.ActiveCount == a0, "100 cycles in a frame — the two churn warnings (" + (s_Warnings.Count - w0) + ")");
            // a SetActive on its own object — no flicker, one warning
            int w1 = s_Warnings.Count;
            MotionDriver self = Make("self blink", new SetActiveItem { m_Motion = Blink(), m_Target = Self }, MotionDriverMode.Loop);
            int offFrames = 0;
            for (int i = 0; i < 12; i++)
            {
                yield return null;
                if (!self.gameObject.activeSelf)
                    offFrames++;
            }
            Check(offFrames == 0 && s_Warnings.Count - w1 == 1 && !self.IsRunning, "a SetActive on its own object is refused — off frames " + offFrames + " · warnings " + (s_Warnings.Count - w1));
            // a driver on an object another driver blinks runs only while on
            MotionDriver b = Make("b", new PositionItem { m_Motion = Osc(), m_Target = Self }, MotionDriverMode.Loop);
            Make("a", new SetActiveItem { m_Motion = Blink(), m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = b.gameObject, m_Path = "", m_Anchor = "" } }, MotionDriverMode.Loop);
            int mismatch = 0, onFrames = 0;
            for (int i = 0; i < 27; i++)
            {
                yield return null;
                if (b.gameObject.activeSelf != b.IsRunning)
                    mismatch++;
                if (b.gameObject.activeSelf)
                    onFrames++;
            }
            Check(mismatch == 0 && onFrames >= 8 && onFrames <= 16, "a blinked object's driver runs only while on — mismatch " + mismatch + " · on " + onFrames);
            // the destroy — a lets go too: its target b is gone
            Object.Destroy(d.gameObject);
            Object.Destroy(b.gameObject);
            yield return Frames(2);
            Check(s_Test.ReservedCount == 0 && s_Test.ActiveCount == 0, "a destroy lets go — reserved " + s_Test.ReservedCount + " active " + s_Test.ActiveCount);
            Check(s_Errors.Count == 0, "errors " + s_Errors.Count + " " + string.Join(" | ", s_Errors));
            End();
        }

        // Follow's frames — a target moved in Update is read that frame, in LateUpdate a frame behind; SetTarget likewise
        [UnityTest]
        public IEnumerator RP02_FollowFrames()
        {
            foreach (bool late in new[] { false, true })
            {
                var other = new GameObject("other");
                m_Made.Add(other);
                MotionDriverMover mover = other.AddComponent<MotionDriverMover>();
                mover.Late = late;
                mover.Step = new Vector3(0.1f, 0f, 0f);
                MotionDriver d = Make("follower", new PositionItem { m_Motion = Spring(), m_Target = Self }, MotionDriverMode.Follow, default, x => x.m_FollowTarget = other.transform);
                var rows = new List<float>();
                s_Window = () =>
                {
                    if (rows.Count < 4)
                        rows.Add(other.transform.localPosition.x - d.FollowTarget.x);
                };
                SetWindow(true);
                yield return Frames(6);
                SetWindow(false);
                s_Window = null;
                bool ok = rows.Count == 4 && rows.TrueForAll(r => Mathf.Abs(r - (late ? 0.1f : 0f)) < 1e-4f);
                Check(ok, "a target moved in " + (late ? "LateUpdate is read a frame behind" : "Update is read that frame") + " — " + string.Join(" ", rows));
                Object.Destroy(other);
                Object.Destroy(d.gameObject);
                yield return null;
            }
            // SetTarget in the game's Update — that frame's LateUpdate sees the step; in LateUpdate — the next frame
            MotionDriver f = Make("target", new PositionItem { m_Motion = Spring(30f, 1f), m_Target = Self }, MotionDriverMode.Follow);
            var rec = new GameObject("frames").AddComponent<MotionDriverFrames>();
            m_Made.Add(rec.gameObject);
            yield return null;
            int start = Time.frameCount + 1;
            float lateSeen = -1f, nextSeen = -1f, sameAfterLate = -1f;
            MotionDriverFrames.OnUpdate = fr =>
            {
                if (fr - start == 0)
                    f.SetTarget(new Vector4(10f, 0f, 0f, 0f));
            };
            MotionDriverFrames.OnLateUpdate = fr =>
            {
                if (fr - start == 0)
                    lateSeen = f.transform.localPosition.x;
                if (fr - start == 3)
                {
                    sameAfterLate = f.transform.localPosition.x;
                    f.SetTarget(new Vector4(20f, 0f, 0f, 0f));
                }
                if (fr - start == 4)
                    nextSeen = f.transform.localPosition.x;
            };
            float windowAt3 = -1f;
            s_Window = () =>
            {
                if (Time.frameCount - start == 3)
                    windowAt3 = f.transform.localPosition.x;
            };
            SetWindow(true);
            yield return Frames(6);
            MotionDriverFrames.OnUpdate = null;
            MotionDriverFrames.OnLateUpdate = null;
            s_Window = null;
            Check(lateSeen > 1f && Mathf.Abs(windowAt3 - sameAfterLate) < 1e-5f && nextSeen > sameAfterLate + 1f, "SetTarget in Update — that frame; in LateUpdate — the next (" + F(lateSeen) + " · " + F(sameAfterLate) + " → " + F(windowAt3) + " → " + F(nextSeen) + ")");
            Check(s_Errors.Count == 0, "errors " + s_Errors.Count);
            End();
        }

        // timeScale 0 — Scaled stands after the frame that set it (Follow skips the zero step), Unscaled runs, back to 1 it
        // goes on
        [UnityTest]
        public IEnumerator RP03_TimeScaleZero()
        {
            MotionDriver scaled = Make("scaled", new PositionItem { m_Motion = Osc(), m_Target = Self }, MotionDriverMode.Loop);
            MotionDriver unscaled = Make("unscaled", new PositionItem { m_Motion = Osc(), m_Target = Self }, MotionDriverMode.Loop, default, x => x.m_TimeMode = PlayerTimeMode.Unscaled);
            MotionDriver follow = Make("follow", new PositionItem { m_Motion = Spring(1f, 0.4f), m_Target = Self }, MotionDriverMode.Follow);
            follow.SetTarget(new Vector4(5f, 0f, 0f, 0f));
            yield return Frames(5);
            Time.timeScale = 0f;
            yield return null;
            double s0 = scaled.Seconds, u0 = unscaled.Seconds;
            float f0 = follow.FollowValue.x;
            yield return Frames(15);
            double s1 = scaled.Seconds, u1 = unscaled.Seconds;
            float f1 = follow.FollowValue.x;
            Time.timeScale = 1f;
            yield return Frames(3);
            Check(s1 == s0 && f1 == f0, "timeScale 0 — Scaled stands after the frame that set it (" + R(s0) + "→" + R(s1) + " · " + R(f0) + "→" + R(f1) + ")");
            Check(u1 > u0, "Unscaled runs (" + F(u0) + "→" + F(u1) + ")");
            Check(scaled.Seconds > s1, "back to 1 — it goes on");
            Check(s_Errors.Count == 0, "errors " + s_Errors.Count);
            End();
        }

        // the first runs — five new drivers' first tick after the enable, the first re-enables (Loop · Follow) allocate
        // nothing; a positive control allocates. Red is all five fresh drivers' first runs allocating (6000.3 also counts
        // allocations that are not the delegate's)
        [UnityTest]
        public IEnumerator RP04_FirstRuns()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            for (int i = 0; i < 2; i++)
            {
                var w = new GameObject("warm" + i);
                m_Made.Add(w);
                var dw = w.AddComponent<MotionDriver>();
                dw.m_Item = new PositionItem { m_Motion = new WiggleMotion { m_Params = new WiggleParams { m_Amplitude = Vector4.one, m_Frequency = 1f, m_Octaves = 2 } }, m_Target = Self };
                dw.Bind(s);
                var wf = new GameObject("warmf" + i);
                m_Made.Add(wf);
                var df = wf.AddComponent<MotionDriver>();
                df.m_Item = new RotationItem { m_Motion = Spring(), m_Target = Self };
                df.m_Mode = MotionDriverMode.Follow;
                df.SetTarget(new Vector4(0f, 90f, 0f, 0f));
                df.Bind(s);
                s.Tick(1f / 90f);
                dw.Halt(true);
                df.Halt(true);
                dw.Run();
                df.Run();
                s.Tick(1f / 90f);
            }
            int reLoop = 0, reFollow = 0, tickNew = 0;
            for (int i = 0; i < 5; i++)
            {
                var g = new GameObject("fresh" + i);
                m_Made.Add(g);
                var d = g.AddComponent<MotionDriver>();
                d.m_Item = new PositionItem { m_Motion = new WiggleMotion { m_Params = new WiggleParams { m_Amplitude = Vector4.one, m_Frequency = 1f, m_Octaves = 2 } }, m_Target = Self };
                var gf = new GameObject("freshf" + i);
                m_Made.Add(gf);
                var f = gf.AddComponent<MotionDriver>();
                f.m_Item = new RotationItem { m_Motion = Spring(), m_Target = Self };
                f.m_Mode = MotionDriverMode.Follow;
                f.SetTarget(new Vector4(0f, 45f, 0f, 0f));
                d.Bind(s);
                f.Bind(s);
                if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => s.Tick(1f / 90f))).IsSuccess)
                    tickNew++;
                d.Halt(true);
                f.Halt(true);
                s.Tick(1f / 90f);
                if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => d.Run())).IsSuccess)
                    reLoop++;
                if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => f.Run())).IsSuccess)
                    reFollow++;
                yield return null;
            }
            int positive = 0;
            var junk = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => junk.Add(new string('x', 3)))).IsSuccess)
                    positive++;
            }
            foreach (Object o in m_Made)
            {
                if (o is GameObject go && go.TryGetComponent(out MotionDriver md))
                    md.Bind(null);
            }
            Check(tickNew < 5 && reLoop < 5 && reFollow < 5 && positive == 5, "first runs allocate 5/5 — the tick " + tickNew + "/5 · a re-enable Loop " + reLoop + "/5 · Follow " + reFollow + "/5 · positive " + positive + "/5");
            End();
        }

        // a new Default — while off it runs on the new one (the old lets go); shut down while running it
        // stops, a third Default runs it
        [UnityTest]
        public IEnumerator RP05_NewDefault()
        {
            MotionDriver d = Make("d", new PositionItem { m_Motion = Osc(), m_Target = Self }, MotionDriverMode.Loop);
            yield return Frames(3);
            d.enabled = false;
            yield return null;
            FeedbackScheduler old = s_Test;
            old.Shutdown();
            var next = new FeedbackScheduler(ContextKind.Test);
            s_Test = next;
            FeedbackScheduler.Default = next;
            d.enabled = true;
            yield return Frames(3);
            Check(d.IsRunning && next.ActiveCount == 1 && next.ReservedCount == 1 && old.ReservedCount == 0, "a new Default while off — it runs on the new one, the old lets go (" + d.IsRunning + " " + next.ActiveCount + "/" + next.ReservedCount + " · old " + old.ReservedCount + ")");
            next.Shutdown();
            yield return null;
            bool after = d.IsRunning;
            var third = new FeedbackScheduler(ContextKind.Test);
            s_Test = third;
            FeedbackScheduler.Default = third;
            d.enabled = false;
            d.enabled = true;
            yield return Frames(2);
            Check(!after && d.IsRunning && third.ActiveCount == 1 && third.ReservedCount == 1, "shut down while running — it stops; a third Default runs it (" + after + " " + d.IsRunning + " " + third.ActiveCount + "/" + third.ReservedCount + ")");
            Check(s_Errors.Count == 0, "errors " + s_Errors.Count + " " + string.Join(" | ", s_Errors));
            End();
        }
    }
}
