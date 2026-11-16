using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // EditMode — the trauma (TraumaService · FeedbackTrauma · Camera/Trauma · Transform/Trauma): each transform's level, its
    // fade, its shake against this test's own formula, the categories, the shake caps, the capacity, the targets, the public
    // API, allocation and the context's close. Schedulers are ticked directly (edit mode writes no time value). A row checks
    // everything and judges at the end
    public sealed class TraumaTests
    {
        static readonly List<string> s_Warnings = new List<string>();
        static readonly List<string> s_Errors = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<Camera> m_Silenced = new List<Camera>();
        readonly List<string> m_Fail = new List<string>();
        FeedbackScheduler m_SavedDefault;

        static readonly Quaternion s_Turn = Quaternion.Euler(10f, 40f, 0f);

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
                s_Warnings.Add(condition);
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                s_Errors.Add(condition);
        }

        static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        [SetUp]
        public void SetUp()
        {
            s_Warnings.Clear();
            s_Errors.Clear();
            m_Fail.Clear();
            PlayerRuntime.ResetSession();
            Application.logMessageReceived += OnLog;
            m_SavedDefault = FeedbackScheduler.Default;
            typeof(FeedbackTrauma).GetField("s_WarnedContext", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.SetValue(null, false);
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            FeedbackScheduler.Default = m_SavedDefault;
            foreach (FeedbackPlayer player in m_Players)
            {
                if (player != null)
                    player.ReleaseRuntime();
            }
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            foreach (Camera c in m_Silenced)
            {
                if (c != null)
                    c.enabled = true;
            }
            m_Made.Clear();
            m_Players.Clear();
            m_Schedulers.Clear();
            m_Silenced.Clear();
        }

        // ---- fixtures

        FeedbackScheduler Sched(SettingsValues values = null)
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            if (values != null)
                s.Context.UseSettings(values);
            return s;
        }

        GameObject Go(string name, Vector3 at = default)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            m_Made.Add(go);
            return go;
        }

        GameObject Turned(string name, bool camera = false)
        {
            GameObject go = Go(name, new Vector3(1f, 2f, 3f));
            go.transform.rotation = s_Turn;
            if (camera)
                go.AddComponent<Camera>();
            return go;
        }

        FeedbackPlayer PlayerOn(GameObject owner, FeedbackScheduler s, params FeedbackItem[] items)
        {
            var player = owner.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Items.AddRange(items);
            player.Bind(s);
            player.EnsureRuntime();
            return player;
        }

        void Silence()
        {
            foreach (Camera c in Camera.allCameras)
            {
                if (!c.CompareTag("MainCamera"))
                    continue;
                c.enabled = false;
                m_Silenced.Add(c);
            }
        }

        GameObject MainCam(string name)
        {
            GameObject go = Go(name);
            go.tag = "MainCamera";
            go.AddComponent<Camera>();
            return go;
        }

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };
        static TargetRef OnChannel(int id) => new TargetRef { m_Mode = TargetRefMode.Channel, m_Channel = ChannelRef.Of(id), m_Path = "", m_Anchor = "" };

        static SetMotion Set() => new SetMotion { m_Params = new SetParams { m_Value = Vector4.one } };

        static CameraTraumaItem CameraTrauma(float amount, TargetRef? target = null, float delay = 0f)
        {
            var item = new CameraTraumaItem { m_Motion = Set(), m_Amount = amount };
            if (target.HasValue)
                item.m_Target = target.Value;
            item.m_Timing.m_Delay = delay;
            return item;
        }

        static TransformTraumaItem TransformTrauma(float amount, TargetRef target, float delay = 0f)
        {
            var item = new TransformTraumaItem { m_Motion = Set(), m_Amount = amount, m_Target = target };
            item.m_Timing.m_Delay = delay;
            return item;
        }

        // this test's own formula: the offset on the transform's own axes and the turn, at a level, a noise time and a scale
        static (Vector3 Offset, Quaternion Turn) Shake(uint seed, TraumaParams p, double tau, float level, float scale)
        {
            float k = (float)Math.Pow(level, p.Exponent) * scale;
            float N(uint c) => Noise.SmoothHz(Hash32.Tagged(seed, Hash32.Component, c), p.Frequency, tau);
            var offset = new Vector3(p.MaxOffset.x * N(0) * k, p.MaxOffset.y * N(1) * k, p.MaxOffset.z * N(2) * k);
            Quaternion turn = Quaternion.Euler(p.MaxAngle.x * N(3) * k, p.MaxAngle.y * N(4) * k, p.MaxAngle.z * N(5) * k);
            return (offset, turn);
        }

        static double Degrees(Quaternion a, Quaternion b)
        {
            Quaternion q = Quaternion.Inverse(a) * b;
            double s = Math.Sqrt((double)q.x * q.x + (double)q.y * q.y + (double)q.z * q.z);
            return 2.0 * Math.Atan2(s, Math.Abs((double)q.w)) * 180.0 / Math.PI;
        }

        // the transform against the formula at its cell — base position · rotation given
        (double Position, double Turn) Miss(TraumaService t, int i, Transform target, Vector3 basePosition, TraumaParams p, float scale)
        {
            (Vector3 offset, Quaternion turn) = Shake(t.SeedAt(i), p, t.NoiseTimeAt(i), t.TraumaAt(i), scale);
            Vector3 position = basePosition + s_Turn * offset;
            return ((target.localPosition - position).magnitude, Degrees(s_Turn * turn, target.localRotation));
        }

        // ---- the level: it fades to 0 at Decay a second, the shake ends and the transform comes back bit for bit

        [Test]
        public void TT01_FadeAndEnd()
        {
            FeedbackScheduler s = Sched();
            TraumaService t = s.Context.Trauma;
            GameObject go = Turned("fade");
            Vector3 p0 = go.transform.localPosition;
            Quaternion r0 = go.transform.localRotation;
            t.Add(go.transform, 0.5f, TraumaKind.Object);
            int i = t.IndexOf(go.transform);
            Check(i == 0 && t.TraumaAt(0) == 0.5f && t.RunningAt(0), "an add — " + (i >= 0 ? F(t.TraumaAt(0)) : "no cell"));
            bool moved = false;
            int ticks = 0;
            while (t.RunningAt(0) && ticks < 100)
            {
                s.Tick(0.05f);
                ticks++;
                moved |= go.transform.localPosition != p0;
                if (ticks == 4)
                    Check(Math.Abs(t.TraumaAt(0) - (0.5 - 4 * 0.05)) < 1e-5, "linear at 1 a second — " + F(t.TraumaAt(0)));
            }
            Check(moved && ticks >= 10 && ticks <= 11, "it shook and ended after 0.5 s — ticks " + ticks);
            Check(new Vector3Ops().BitEquals(go.transform.localPosition, p0) && new QuaternionOps().BitEquals(go.transform.localRotation, r0), "back bit for bit");
            Check(t.Count == 1 && t.TraumaAt(0) == 0f, "the cell stays warm at 0");
            End();
        }

        // ---- the shake is the formula: the object's and the camera's defaults · a shaker's values · three adds stop at 1 · the
        // category of the target scales it

        [Test]
        public void TT02_TheShakeIsTheFormula()
        {
            {
                FeedbackScheduler s = Sched();
                TraumaService t = s.Context.Trauma;
                GameObject go = Turned("object");
                Vector3 p0 = go.transform.localPosition;
                for (int a = 0; a < 3; a++)
                    t.Add(go.transform, 0.5f, TraumaKind.Object);
                Check(t.TraumaAt(0) == 1f && !t.CameraAt(0), "three of 0.5 — " + F(t.TraumaAt(0)) + " · camera " + t.CameraAt(0));
                double worstP = 0, worstR = 0;
                for (int k = 0; k < 8; k++)
                {
                    s.Tick(0.04f);
                    (double mp, double mr) = Miss(t, 0, go.transform, p0, TraumaParams.ObjectDefault, 1f);
                    worstP = Math.Max(worstP, mp);
                    worstR = Math.Max(worstR, mr);
                }
                Check(worstP < 1e-5 && worstR < 1e-3, "an object's defaults — " + F(worstP) + " · " + F(worstR) + "°");
            }
            {
                // a camera's defaults · Motion at 0.5 leaves a camera alone · Camera at 0.5 halves it
                FeedbackScheduler s = Sched();
                s.Context.Categories.SetScale(FeedbackCategory.Motion, 0.5f);
                s.Context.Categories.SetScale(FeedbackCategory.Camera, 0.25f);
                TraumaService t = s.Context.Trauma;
                GameObject cam = Turned("camera", true);
                Vector3 p0 = cam.transform.localPosition;
                t.Add(cam.transform, 0.8f, TraumaKind.Camera);
                double worstP = 0, worstR = 0;
                for (int k = 0; k < 8; k++)
                {
                    s.Tick(0.04f);
                    (double mp, double mr) = Miss(t, 0, cam.transform, p0, TraumaParams.CameraDefault, 0.25f);
                    worstP = Math.Max(worstP, mp);
                    worstR = Math.Max(worstR, mr);
                }
                Check(t.CameraAt(0) && worstP < 1e-5 && worstR < 1e-3, "a camera's defaults under Camera 0.25 — " + F(worstP) + " · " + F(worstR) + "°");
            }
            {
                // an object under Motion 0.5 · a shaker's values · Unscaled time
                FeedbackScheduler s = Sched();
                s.Context.Categories.SetScale(FeedbackCategory.Motion, 0.5f);
                TraumaService t = s.Context.Trauma;
                GameObject go = Turned("shaker");
                var shaker = go.AddComponent<TraumaShaker>();
                shaker.m_MaxOffset = new Vector3(1f, 0.5f, 0.25f);
                shaker.m_MaxAngle = new Vector3(4f, 0f, 8f);
                shaker.m_Frequency = 7f;
                shaker.m_Exponent = 1.5f;
                shaker.m_Decay = 0.5f;
                shaker.m_TimeMode = PlayerTimeMode.Unscaled;
                Vector3 p0 = go.transform.localPosition;
                t.Add(go.transform, 0.9f, TraumaKind.Object);
                double worstP = 0, worstR = 0;
                for (int k = 0; k < 8; k++)
                {
                    s.Tick(new FrameTime(0f, 0.04f));
                    (double mp, double mr) = Miss(t, 0, go.transform, p0, shaker.Params, 0.5f);
                    worstP = Math.Max(worstP, mp);
                    worstR = Math.Max(worstR, mr);
                }
                Check(worstP < 1e-5 && worstR < 1e-3 && Math.Abs(t.TraumaAt(0) - (0.9 - 8 * 0.04 * 0.5)) < 1e-5 && Math.Abs(t.NoiseTimeAt(0) - 8 * 0.04) < 1e-6,
                    "a shaker's values on unscaled time under Motion 0.5 — " + F(worstP) + " · " + F(worstR) + "° · level " + F(t.TraumaAt(0)) + " · noise time " + F(t.NoiseTimeAt(0)));
            }
            End();
        }

        // ---- an add in a play's runner pass shows in that frame · an add keeps the noise time going (no jump)

        [Test]
        public void TT03_SameFrameAndNoJump()
        {
            {
                FeedbackScheduler s = Sched();
                TraumaService t = s.Context.Trauma;
                GameObject go = Turned("frame");
                Vector3 p0 = go.transform.localPosition;
                t.Add(go.transform, 0.5f, TraumaKind.Object);
                for (int k = 0; k < 3; k++)
                    s.Tick(0.05f);
                // a delayed add begins in the runner pass, after the service's Update
                FeedbackPlayer p = PlayerOn(Go("p"), s, TransformTrauma(0.6f, Direct(go.transform), 0.05f));
                p.Play();
                s.Tick(0.05f);
                float level = t.TraumaAt(0);
                (double miss, double _) = Miss(t, 0, go.transform, p0, TraumaParams.ObjectDefault, 1f);
                Check(Math.Abs(level - (0.5 - 4 * 0.05 + 0.6)) < 1e-5 && miss < 1e-5, "the add's frame shows the new level — level " + F(level) + " · miss " + F(miss));
            }
            {
                FeedbackScheduler s = Sched();
                TraumaService t = s.Context.Trauma;
                GameObject go = Turned("jump");
                t.Add(go.transform, 0.5f, TraumaKind.Object);
                for (int k = 0; k < 5; k++)
                    s.Tick(0.05f);
                double before = t.NoiseTimeAt(0);
                t.Add(go.transform, 0.3f, TraumaKind.Object);
                Check(t.NoiseTimeAt(0) == before && before > 0.2, "an add keeps the noise time — " + F(before) + " → " + F(t.NoiseTimeAt(0)));
            }
            End();
        }

        // ---- the settings' shake caps hold a camera's trauma (with the Camera category), not an object's

        [Test]
        public void TT04_TheCapsAreTheCamera()
        {
            var values = new SettingsValues { MaximumShakeOffset = 0.2f, MaximumShakeAngle = 1f };
            double camera = 0, cameraTurn = 0, thing = 0;
            {
                FeedbackScheduler s = Sched(values);
                TraumaService t = s.Context.Trauma;
                GameObject cam = Turned("capped", true);
                var shaker = cam.AddComponent<TraumaShaker>();
                shaker.m_MaxOffset = new Vector3(2f, 2f, 2f);
                shaker.m_MaxAngle = new Vector3(20f, 20f, 20f);
                Vector3 p0 = cam.transform.localPosition;
                Quaternion r0 = cam.transform.localRotation;
                t.Add(cam.transform, 1f, TraumaKind.Camera);
                for (int k = 0; k < 10; k++)
                {
                    s.Tick(0.03f);
                    camera = Math.Max(camera, (cam.transform.localPosition - p0).magnitude);
                    cameraTurn = Math.Max(cameraTurn, Degrees(r0, cam.transform.localRotation));
                }
            }
            {
                FeedbackScheduler s = Sched(values);
                TraumaService t = s.Context.Trauma;
                GameObject go = Turned("uncapped");
                var shaker = go.AddComponent<TraumaShaker>();
                shaker.m_MaxOffset = new Vector3(2f, 2f, 2f);
                Vector3 p0 = go.transform.localPosition;
                t.Add(go.transform, 1f, TraumaKind.Object);
                for (int k = 0; k < 10; k++)
                {
                    s.Tick(0.03f);
                    thing = Math.Max(thing, (go.transform.localPosition - p0).magnitude);
                }
            }
            Check(camera > 0.1 && camera <= 0.2 + 1e-5 && cameraTurn <= 1.0 + 1e-3, "the camera within the caps — " + F(camera) + " · " + F(cameraTurn) + "°");
            Check(thing > 0.2, "the object past the camera's cap — " + F(thing));
            End();
        }

        // ---- the capacity: a full service lets the cell with the least trauma go (a resting one first), warns once and gives
        // its slots back

        [Test]
        public void TT05_TheCapacity()
        {
            FeedbackScheduler s = Sched(new SettingsValues { TraumaCapacity = 2 });
            TraumaService t = s.Context.Trauma;
            GameObject a = Turned("a"), b = Turned("b"), c = Turned("c"), d = Turned("d");
            t.Add(a.transform, 0.5f, TraumaKind.Object);
            t.Warm(b.transform, TraumaKind.Object);
            int w = s_Warnings.Count;
            t.Add(c.transform, 0.2f, TraumaKind.Object);
            bool bGone = t.IndexOf(b.transform) < 0 && !s.Context.Layers.TryFind(b.transform, 0, 0, out LocalPositionSlot _);
            Check(t.Count == 2 && bGone && t.IndexOf(a.transform) >= 0 && t.IndexOf(c.transform) >= 0, "the resting cell went — count " + t.Count + " · b gone " + bGone);
            t.Add(d.transform, 0.9f, TraumaKind.Object);
            Check(t.IndexOf(c.transform) < 0 && t.IndexOf(a.transform) >= 0 && t.IndexOf(d.transform) >= 0, "then the least trauma (0.2) went");
            Check(s_Warnings.Skip(w).Count(x => x == TraumaService.FullWarning) == 1, "one warning");
            s.Tick(0.05f);
            Check(s_Errors.Count == 0, "no errors — " + string.Join(" | ", s_Errors));
            End();
        }

        // ---- the items: Amount × the play's intensity · backward adds nothing · Stop and Restore leave the trauma · Camera/Trauma
        // follows the main camera · Transform/Trauma on any transform · one cell for both on one camera · a channel's receivers ·
        // Transform/Trauma refuses a channel

        [Test]
        public void TT06_TheItems()
        {
            {
                FeedbackScheduler s = Sched();
                TraumaService t = s.Context.Trauma;
                GameObject go = Turned("item");
                // a slow fade, so the trauma outlasts the backward play below
                go.AddComponent<TraumaShaker>().m_Decay = 0.05f;
                // a wait keeps the play running, so a Stop reverts a play with an executed instant
                var wait = new WaitItem();
                wait.m_Timing.m_Duration = 1f;
                FeedbackPlayer p = PlayerOn(Go("p"), s, TransformTrauma(0.4f, Direct(go.transform)), wait);
                Check(t.IndexOf(go.transform) >= 0, "the prepare warmed the target");
                p.Play(new PlayArgs().WithIntensity(0.5f));
                Check(Math.Abs(t.Get(go.transform) - 0.2f) < 1e-6, "Amount × intensity — " + F(t.Get(go.transform)));
                p.Stop(StopMode.Revert);
                p.Restore();
                Check(Math.Abs(t.Get(go.transform) - 0.2f) < 1e-6 && !t.CameraAt(t.IndexOf(go.transform)), "Stop (Revert) · Restore leave it — " + F(t.Get(go.transform)));
                // backward: the instant comes at the end, and adds nothing
                p.Play(new PlayArgs().WithDirection(PlayerDirection.Backward));
                for (int k = 0; k < 25; k++)
                    s.Tick(0.05f);
                Check(!p.IsPlaying && t.Get(go.transform) < 0.2f, "backward adds nothing — " + F(t.Get(go.transform)) + " · playing " + p.IsPlaying);
            }
            {
                // Transform/Trauma first on a camera — the target's category all the same
                FeedbackScheduler s = Sched();
                TraumaService t = s.Context.Trauma;
                GameObject cam = Turned("transform first", true);
                PlayerOn(Go("tf"), s, TransformTrauma(0.4f, Direct(cam.transform))).Play();
                Check(t.CameraAt(t.IndexOf(cam.transform)), "Transform/Trauma first on a camera — the Camera category");
            }
            {
                Silence();
                FeedbackScheduler s = Sched();
                TraumaService t = s.Context.Trauma;
                GameObject first = MainCam("main 1");
                FeedbackPlayer p = PlayerOn(Go("p"), s, CameraTrauma(0.3f));
                p.Play();
                Check(Math.Abs(t.Get(first.transform) - 0.3f) < 1e-6 && t.CameraAt(t.IndexOf(first.transform)), "the main camera — " + F(t.Get(first.transform)));
                first.GetComponent<Camera>().enabled = false;
                first.tag = "Untagged";
                GameObject second = MainCam("main 2");
                p.Play();
                Check(Math.Abs(t.Get(second.transform) - 0.3f) < 1e-6 && Math.Abs(t.Get(first.transform) - 0.3f) < 1e-6, "the next main camera — " + F(t.Get(second.transform)));
                // Transform/Trauma on the same camera: one cell, the Camera category
                FeedbackPlayer q = PlayerOn(Go("q"), s, TransformTrauma(0.5f, Direct(second.transform)));
                q.Play();
                int i = t.IndexOf(second.transform);
                Check(t.Count == 2 && Math.Abs(t.TraumaAt(i) - 0.8f) < 1e-6 && t.CameraAt(i), "one cell for both — " + F(t.TraumaAt(i)) + " · camera " + t.CameraAt(i));
            }
            {
                FeedbackScheduler s = Sched();
                TraumaService t = s.Context.Trauma;
                var near = Go("near", new Vector3(1f, 0f, 0f)).AddComponent<CameraShakeReceiver>();
                near.m_Channel = ChannelRef.Of(7);
                near.m_Range = 3f;
                near.Bind(s);
                var far = Go("far", new Vector3(50f, 0f, 0f)).AddComponent<CameraShakeReceiver>();
                far.m_Channel = ChannelRef.Of(7);
                far.m_Range = 3f;
                far.Bind(s);
                FeedbackPlayer p = PlayerOn(Go("sender"), s, CameraTrauma(0.6f, OnChannel(7)));
                p.Play(new PlayArgs().WithPosition(Vector3.zero));
                Check(Math.Abs(t.Get(near.transform) - 0.6f) < 1e-6 && t.Get(far.transform) == 0f && t.CameraAt(t.IndexOf(near.transform)), "a channel's receivers in range — near " + F(t.Get(near.transform)) + " · far " + F(t.Get(far.transform)));
                near.Bind(null);
                far.Bind(null);
            }
            {
                FeedbackScheduler s = Sched();
                int w = s_Warnings.Count;
                FeedbackPlayer p = PlayerOn(Go("refused"), s, TransformTrauma(0.5f, OnChannel(7)));
                p.Play();
                Check(s_Warnings.Skip(w).Any(x => x.Contains("channel targets need a receiver")) && s.Context.TraumaIfMade == null, "Transform/Trauma refuses a channel");
            }
            {
                // a RectTransform shakes and comes back
                FeedbackScheduler s = Sched();
                TraumaService t = s.Context.Trauma;
                var ui = new GameObject("ui", typeof(RectTransform));
                m_Made.Add(ui);
                Vector3 p0 = ui.transform.localPosition;
                t.Add(ui.transform, 0.5f, TraumaKind.Object);
                bool moved = false;
                for (int k = 0; k < 20 && t.RunningAt(0); k++)
                {
                    s.Tick(0.05f);
                    moved |= ui.transform.localPosition != p0;
                }
                Check(moved && new Vector3Ops().BitEquals(ui.transform.localPosition, p0), "a RectTransform — moved " + moved);
            }
            End();
        }

        // ---- the public API on the loop's context · outside one it warns once and does nothing

        [Test]
        public void TT07_ThePublicApi()
        {
            GameObject go = Turned("api");
            FeedbackScheduler.Default = null;
            int w = s_Warnings.Count;
            FeedbackTrauma.Add(go.transform, 0.5f);
            FeedbackTrauma.Add(go.transform, 0.5f);
            Check(s_Warnings.Skip(w).Count(x => x == FeedbackTrauma.NoContextWarning) == 1 && FeedbackTrauma.Get(go.transform) == 0f, "no context — one warning, nothing");
            FeedbackScheduler s = Sched();
            FeedbackScheduler.Default = s;
            FeedbackTrauma.Add(go.transform, 0.3f);
            FeedbackTrauma.Add(go.transform, 0.3f);
            Check(Math.Abs(FeedbackTrauma.Get(go.transform) - 0.6f) < 1e-6, "Add · Get — " + F(FeedbackTrauma.Get(go.transform)));
            GameObject cam = Turned("api camera", true);
            FeedbackTrauma.Add(cam.GetComponent<Camera>(), 0.4f);
            Check(s.Context.Trauma.CameraAt(s.Context.Trauma.IndexOf(cam.transform)), "a camera's cell");
            Vector3 p0 = new Vector3(1f, 2f, 3f);
            s.Tick(0.05f);
            FeedbackTrauma.Clear(go.transform);
            s.Tick(0.05f);
            Check(FeedbackTrauma.Get(go.transform) == 0f && new Vector3Ops().BitEquals(go.transform.localPosition, p0), "Clear — back at once");
            End();
        }

        // ---- warmed by the prepare, a transform's first add allocates nothing · a frame of shaking cells allocates nothing (a
        // play's own first run is the PlayMode combinations' row)

        [Test]
        public void TT08_Allocation()
        {
            FeedbackScheduler s = Sched();
            TraumaService t = s.Context.Trauma;
            var targets = new List<Transform>();
            for (int i = 0; i < 5; i++)
            {
                targets.Add(Turned("t" + i).transform);
                PlayerOn(Go("p" + i), s, TransformTrauma(0.3f, Direct(targets[i])));
            }
            s.Tick(0.016f);
            int first = 0;
            foreach (Transform target in targets)
            {
                if (UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory().ApplyTo(new TestDelegate(() => t.Add(target, 0.3f, TraumaKind.Object))).IsSuccess)
                    first++;
            }
            s.Tick(0.016f);
            int frames = 0;
            for (int i = 0; i < 5; i++)
            {
                if (UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory().ApplyTo(new TestDelegate(() => s.Tick(0.016f))).IsSuccess)
                    frames++;
            }
            Check(first < 5, "a warmed transform's first add allocates — " + first + "/5");
            Check(frames < 5, "a frame allocates — " + frames + "/5");
            End();
        }

        // ---- the context's close while shaking: no errors, every transform back · a target destroyed while shaking

        [Test]
        public void TT09_TheClose()
        {
            FeedbackScheduler s = Sched();
            TraumaService t = s.Context.Trauma;
            GameObject cam = Turned("close camera", true);
            GameObject go = Turned("close object");
            GameObject gone = Turned("gone");
            Vector3 c0 = cam.transform.localPosition, o0 = go.transform.localPosition;
            t.Add(cam.transform, 0.7f, TraumaKind.Camera);
            t.Add(go.transform, 0.7f, TraumaKind.Object);
            t.Add(gone.transform, 0.7f, TraumaKind.Object);
            s.Tick(0.05f);
            Object.DestroyImmediate(gone);
            s.Tick(0.05f);
            Check(t.Count == 2 && cam.transform.localPosition != c0, "the destroyed one's cell went — count " + t.Count);
            s.Shutdown();
            Check(new Vector3Ops().BitEquals(cam.transform.localPosition, c0) && new Vector3Ops().BitEquals(go.transform.localPosition, o0), "back on the close");
            Check(s_Errors.Count == 0, "no errors — " + string.Join(" | ", s_Errors));
            End();
        }
    }
}
