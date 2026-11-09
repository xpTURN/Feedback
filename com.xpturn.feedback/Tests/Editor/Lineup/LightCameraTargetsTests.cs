using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the light's and the camera's value targets on a test scheduler ticked by hand (0.05 s). A Tween is Persist — its end
    // stays, a Revert stop and a Restore give the start back; the other motions contribute — the end is the base, bit for bit
    public sealed class LightCameraTargetsTests
    {
        const float Step = 0.05f;

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();

        [TearDown]
        public void TearDown()
        {
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    p.ReleaseRuntime();
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
            m_Made.Clear();
            m_Schedulers.Clear();
            m_Players.Clear();
        }

        static string B(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };

        static TweenMotion TweenTo(Vector4 to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = to;
            return new TweenMotion { m_Params = p };
        }

        static PunchMotion PunchBy(Vector4 amplitude) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = amplitude, m_Vibrato = 1f, m_Decay = 0f } };

        static FlashMotion FlashTo(Vector4 target) => new FlashMotion { m_Params = new FlashParams { m_Target = target, m_Attack = 0.25f, m_Hold = 0.5f, m_Release = 0.25f } };

        static T On<T>(T item, Object target, FeedbackMotion motion, float duration) where T : TargetItem
        {
            item.m_Target = Direct(target);
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            p.Bind(s);
            p.EnsureRuntime();
            m_Players.Add(p);
            return p;
        }

        static void Tick(FeedbackScheduler s, int n)
        {
            for (int i = 0; i < n; i++)
                s.Tick(Step);
        }

        Light Lamp(LightType type = LightType.Point)
        {
            var go = new GameObject("Light");
            m_Made.Add(go);
            Light light = go.AddComponent<Light>();
            light.type = type;
            light.shadows = LightShadows.Soft;
            return light;
        }

        Light Spot()
        {
            Light light = Lamp(LightType.Spot);
            light.spotAngle = 60f;
            light.innerSpotAngle = 30f;
            return light;
        }

        Camera Cam(bool orthographic = false, bool physical = false)
        {
            var go = new GameObject("Camera");
            m_Made.Add(go);
            Camera camera = go.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.orthographicSize = 5f;
            camera.orthographic = orthographic;
            camera.usePhysicalProperties = physical;
            return camera;
        }

        static string SpotBits(Light l) => B(l.spotAngle) + "/" + B(l.innerSpotAngle);

        static string SpotValues(Light l) => l.spotAngle + "/" + l.innerSpotAngle;

        // a contribution's end: the base bit for bit — and, where the engine clamps, a frame at the clamp (the positive)
        string EndsAtTheBase<TC>(string name, Func<TC> make, Func<TC, string> read, Func<TC, FeedbackItem[]> items, Func<TC, bool> clamped = null) where TC : Component
        {
            FeedbackScheduler s = Sched();
            TC c = make();
            string before = read(c);
            Player(s, items(c)).Play();
            bool reached = false;
            for (int i = 0; i < 30; i++)
            {
                s.Tick(Step);
                if (clamped != null && clamped(c))
                    reached = true;
            }
            s.Tick(0f);
            if (clamped != null && !reached)
                return name + " never reached the clamp";
            string after = read(c);
            return after == before ? null : name + " " + before + " → " + after;
        }

        // a game write between the revert and the apply stays after the end
        string GameWriteStays<TC>(string name, Func<TC> make, Func<TC, float> read, Action<TC> write, Func<TC, FeedbackItem> item) where TC : Component
        {
            FeedbackScheduler s = Sched();
            TC c = make();
            Player(s, item(c)).Play();
            Tick(s, 3);
            s.RunRevert();
            write(c);
            float written = read(c);
            s.RunUpdate(new FrameTime(Step, Step));
            s.RunLateApply();
            Tick(s, 30);
            s.Tick(0f);
            return B(read(c)) == B(written) ? null : name + " — the game's " + written + " became " + read(c);
        }

        // a Persist play (Tween): a Revert stop in the middle and a Restore after the end both give the start back, bit for bit
        IEnumerable<string> PersistBack<TC>(string name, Func<TC> make, Func<TC, string> read, Func<TargetItem> item, Vector4 to, bool direct = false) where TC : Component
        {
            foreach (bool midway in new[] { true, false })
            {
                FeedbackScheduler s = Sched();
                TC c = make();
                string before = read(c);
                TargetItem it = On(item(), c, TweenTo(to), 1f);
                it.m_DirectWrite = direct;
                FeedbackPlayer p = Player(s, it);
                p.Play();
                if (midway)
                {
                    Tick(s, 16);
                    p.Stop(StopMode.Revert);
                }
                else
                {
                    Tick(s, 30);
                    p.Restore();
                }
                s.Tick(0f);
                string after = read(c);
                if (after != before)
                    yield return name + (midway ? " — a Revert stop " : " — a Restore ") + before + " → " + after;
            }
        }

        static TargetItem[] Nine() => new TargetItem[]
        {
            new LightRangeItem(), new LightColorItem(), new LightSpotAngleItem(), new LightShadowStrengthItem(),
            new CameraFieldOfViewItem(), new CameraOrthographicSizeItem(), new CameraZoomItem(), new CameraClippingPlanesItem(), new CameraBackgroundColorItem(),
        };

        // ---- each item makes its own binding (the registry's completeness by name is DataModelReadersTests')

        [Test]
        public void LQ02_EachMakesItsBinding()
        {
            var want = new Dictionary<Type, Type>
            {
                { typeof(LightRangeItem), typeof(LightRangeBinding) }, { typeof(LightColorItem), typeof(LightColorBinding) },
                { typeof(LightSpotAngleItem), typeof(LightSpotAngleBinding) }, { typeof(LightShadowStrengthItem), typeof(LightShadowStrengthBinding) },
                { typeof(CameraFieldOfViewItem), typeof(CameraFieldOfViewBinding) }, { typeof(CameraOrthographicSizeItem), typeof(CameraOrthographicSizeBinding) },
                { typeof(CameraZoomItem), typeof(CameraZoomBinding) }, { typeof(CameraClippingPlanesItem), typeof(CameraClippingPlanesBinding) },
                { typeof(CameraBackgroundColorItem), typeof(CameraBackgroundColorBinding) },
            };
            foreach (TargetItem item in Nine())
                Assert.That(FeedbackImplementations.Create(item)?.GetType(), Is.EqualTo(want[item.GetType()]), item.GetType().Name);
        }

        [Test]
        public void LQ03a_ContributionsEndAtTheBase()
        {
            var fails = new List<string>
            {
                EndsAtTheBase("range punch −20", () => Lamp(), l => B(l.range), l => new FeedbackItem[] { On(new LightRangeItem(), l, PunchBy(new Vector4(-20f, 0f, 0f, 0f)), 1f) }, l => l.range == 0f),
                EndsAtTheBase("field of view punch +300", () => Cam(), c => B(c.fieldOfView), c => new FeedbackItem[] { On(new CameraFieldOfViewItem(), c, PunchBy(new Vector4(300f, 0f, 0f, 0f)), 1f) }, c => c.fieldOfView >= 179f),
                EndsAtTheBase("light color flash", () => Lamp(), l => B(l.color.r) + B(l.color.g) + B(l.color.b) + B(l.color.a), l => new FeedbackItem[] { On(new LightColorItem(), l, FlashTo(new Vector4(1f, 0f, 0f, 1f)), 1f) }),
                EndsAtTheBase("background flash", () => Cam(), c => B(c.backgroundColor.r) + B(c.backgroundColor.g) + B(c.backgroundColor.b) + B(c.backgroundColor.a), c => new FeedbackItem[] { On(new CameraBackgroundColorItem(), c, FlashTo(new Vector4(1f, 0f, 0f, 1f)), 1f) }),
                EndsAtTheBase("zoom punch", () => Cam(), c => B(c.fieldOfView), c => new FeedbackItem[] { On(new CameraZoomItem(), c, PunchBy(new Vector4(1f, 0f, 0f, 0f)), 1f) }),
                GameWriteStays("range punch −20", () => Lamp(), l => l.range, l => l.range = 3.125f, l => On(new LightRangeItem(), l, PunchBy(new Vector4(-20f, 0f, 0f, 0f)), 1f)),
                GameWriteStays("field of view punch +300", () => Cam(), c => c.fieldOfView, c => c.fieldOfView = 40.125f, c => On(new CameraFieldOfViewItem(), c, PunchBy(new Vector4(300f, 0f, 0f, 0f)), 1f)),
            };
            Assert.That(fails.Where(f => f != null), Is.Empty);
        }

        [Test]
        public void LQ03b_PersistComesBackWithRevertAndRestore()
        {
            var fails = new List<string>();
            fails.AddRange(PersistBack("spot angle → 10", Spot, SpotBits, () => new LightSpotAngleItem(), new Vector4(10f, 0f, 0f, 0f)));
            fails.AddRange(PersistBack("shadow strength → 1.5", () => { Light l = Lamp(); l.shadowStrength = 0.4f; return l; }, l => B(l.shadowStrength), () => new LightShadowStrengthItem(), new Vector4(1.5f, 0f, 0f, 0f)));
            fails.AddRange(PersistBack("range → −3", () => Lamp(), l => B(l.range), () => new LightRangeItem(), new Vector4(-3f, 0f, 0f, 0f)));
            fails.AddRange(PersistBack("physical field of view → 30", () => Cam(false, true), c => B(c.fieldOfView) + "/" + B(c.focalLength), () => new CameraFieldOfViewItem(), new Vector4(30f, 0f, 0f, 0f)));
            fails.AddRange(PersistBack("clipping planes → 2000/5", () => Cam(), c => B(c.nearClipPlane) + "/" + B(c.farClipPlane), () => new CameraClippingPlanesItem(), new Vector4(2000f, 5f, 0f, 0f)));
            fails.AddRange(PersistBack("zoom → 1", () => Cam(), c => B(c.fieldOfView), () => new CameraZoomItem(), new Vector4(1f, 0f, 0f, 0f)));
            Assert.That(fails, Is.Empty);
        }

        // ---- the spot's inner angle follows the outer down, opens again with it, and keeps a game's write

        [Test]
        public void LQ04_SpotInnerAngle()
        {
            var fails = new List<string>();
            string start = B(60f) + "/" + B(30f);
            {
                FeedbackScheduler s = Sched();
                Light l = Spot();
                FeedbackPlayer down = Player(s, On(new LightSpotAngleItem(), l, TweenTo(new Vector4(20f, 0f, 0f, 0f)), 0.5f));
                FeedbackPlayer up = Player(s, On(new LightSpotAngleItem(), l, TweenTo(new Vector4(60f, 0f, 0f, 0f)), 0.5f));
                down.Play();
                Tick(s, 15);
                if (SpotBits(l) != B(20f) + "/" + B(20f))
                    fails.Add("down to 20 → " + SpotValues(l));
                up.Play();
                Tick(s, 15);
                if (SpotBits(l) != start)
                    fails.Add("up to 60 → " + SpotValues(l));
                down.Play();
                Tick(s, 5);
                l.innerSpotAngle = 12f;
                Tick(s, 10);
                if (SpotBits(l) != B(20f) + "/" + B(12f))
                    fails.Add("down with the game's inner 12 → " + SpotValues(l));
                up.Play();
                Tick(s, 15);
                if (SpotBits(l) != B(60f) + "/" + B(12f))
                    fails.Add("up after the game's inner → " + SpotValues(l));
            }
            fails.AddRange(PersistBack("below the inner", Spot, SpotBits, () => new LightSpotAngleItem(), new Vector4(10f, 0f, 0f, 0f)));
            fails.AddRange(PersistBack("below the inner, a direct write", Spot, SpotBits, () => new LightSpotAngleItem(), new Vector4(10f, 0f, 0f, 0f), true));
            Assert.That(fails, Is.Empty);
        }

        // ---- a physical camera's contribution — the field of view and the focal length come back bit for bit

        [Test]
        public void LQ05_PhysicalCamera()
        {
            string fail = EndsAtTheBase("physical field of view punch", () => Cam(false, true), c => B(c.fieldOfView) + "/" + B(c.focalLength),
                c => new FeedbackItem[] { On(new CameraFieldOfViewItem(), c, PunchBy(new Vector4(10f, 0f, 0f, 0f)), 1f) });
            Assert.That(fail, Is.Null);
        }

        // ---- the zoom in stops

        static float Fov(float fov, float k) => 2f * Mathf.Rad2Deg * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / k);

        static float Stops(float from, float to) => Mathf.Log(Mathf.Tan(from * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(to * 0.5f * Mathf.Deg2Rad), 2f);

        float ZoomTween(bool orthographic, float stops, bool restore = false)
        {
            FeedbackScheduler s = Sched();
            Camera c = Cam(orthographic);
            FeedbackPlayer p = Player(s, On(new CameraZoomItem(), c, TweenTo(new Vector4(stops, 0f, 0f, 0f)), 0.5f));
            p.Play();
            Tick(s, 15);
            if (restore)
            {
                p.Restore();
                s.Tick(0f);
            }
            return orthographic ? c.orthographicSize : c.fieldOfView;
        }

        [Test]
        public void LQ06_ZoomInStops()
        {
            Assert.That(ZoomTween(false, 1f), Is.EqualTo(Fov(60f, 2f)).Within(1e-4f), "perspective +1 — twice as close");
            Assert.That(ZoomTween(false, -1f), Is.EqualTo(Fov(60f, 0.5f)).Within(1e-4f), "perspective −1 — half");
            Assert.That(ZoomTween(true, 1f), Is.EqualTo(2.5f).Within(1e-4f), "orthographic +1");
            Assert.That(ZoomTween(true, -1f), Is.EqualTo(10f).Within(1e-4f), "orthographic −1");
            Assert.That(B(ZoomTween(false, 1f, true)), Is.EqualTo(B(60f)), "the tween's Restore");

            FeedbackScheduler s = Sched();
            Camera camera = Cam();
            Player(s, On(new CameraZoomItem(), camera, PunchBy(new Vector4(1f, 0f, 0f, 0f)), 1f)).Play();
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 22; i++)
            {
                s.Tick(Step);
                min = Mathf.Min(min, camera.fieldOfView);
                max = Mathf.Max(max, camera.fieldOfView);
            }
            s.Tick(0f);
            Assert.That(Stops(60f, min), Is.InRange(0.9f, 1.05f), "a one-stop punch's near end — " + min);
            Assert.That(Stops(max, 60f), Is.InRange(0.9f, 1.05f), "a one-stop punch's far end — " + max);
            Assert.That(B(camera.fieldOfView), Is.EqualTo(B(60f)), "the punch's end");

            string fail = EndsAtTheBase("zoom and field of view in one slot", () => Cam(), c => B(c.fieldOfView), c => new FeedbackItem[]
            {
                On(new CameraZoomItem(), c, PunchBy(new Vector4(0.5f, 0f, 0f, 0f)), 1f), On(new CameraFieldOfViewItem(), c, PunchBy(new Vector4(10f, 0f, 0f, 0f)), 1f),
            });
            Assert.That(fail, Is.Null);
        }

        // ---- the clipping planes move together; the written pair stays drawable; the revert is the engine's own pair

        [Test]
        public void LQ07_ClippingPlanes()
        {
            {
                FeedbackScheduler s = Sched();
                Camera c = Cam();
                FeedbackPlayer p = Player(s, On(new CameraClippingPlanesItem(), c, TweenTo(new Vector4(2f, 50f, 0f, 0f)), 1f));
                p.Play();
                Tick(s, 10);
                Assert.That(B(c.nearClipPlane) != B(0.3f) && B(c.farClipPlane) != B(1000f), Is.True, "both move — " + c.nearClipPlane + "/" + c.farClipPlane);
                Tick(s, 30);
                p.Restore();
                s.Tick(0f);
                Assert.That(B(c.nearClipPlane) + B(c.farClipPlane), Is.EqualTo(B(0.3f) + B(1000f)), "the Restore");
            }
            var cases = new[]
            {
                (new Vector4(2000f, 1000f, 0f, 0f), new Vector2(2000f, 2000.01f)),
                (new Vector4(0f, 1000f, 0f, 0f), new Vector2(0.01f, 1000f)),
                (new Vector4(5f, 5f, 0f, 0f), new Vector2(5f, 5.01f)),
            };
            foreach ((Vector4 to, Vector2 want) in cases)
            {
                FeedbackScheduler s = Sched();
                Camera c = Cam();
                var texture = new RenderTexture(16, 16, 24);
                m_Made.Add(texture);
                c.targetTexture = texture;
                var logs = new List<string>();
                Application.LogCallback heard = (message, trace, type) =>
                {
                    if (type != LogType.Log)
                        logs.Add(message);
                };
                Application.logMessageReceived += heard;
                FeedbackPlayer p = Player(s, On(new CameraClippingPlanesItem(), c, TweenTo(to), 0.5f));
                p.Play();
                Tick(s, 12);
                var written = new Vector2(c.nearClipPlane, c.farClipPlane);
                c.Render();
                c.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                Application.logMessageReceived -= heard;
                p.Restore();
                s.Tick(0f);
                string name = to.x + "/" + to.y;
                Assert.That(written.x, Is.EqualTo(want.x).Within(1e-3f), name + " — near");
                Assert.That(written.y, Is.EqualTo(want.y).Within(1e-3f), name + " — far");
                Assert.That(logs, Is.Empty, name);
                Assert.That(B(c.nearClipPlane) + B(c.farClipPlane), Is.EqualTo(B(0.3f) + B(1000f)), name + " — the Restore");
            }
        }

        // ---- the main camera found at the first Begin; a channel refused. The test scene's Main Camera is untagged for it

        [Test]
        public void LQ12_MainCameraAndChannel()
        {
            var untagged = new List<GameObject>();
            foreach (Camera other in Camera.allCameras)
            {
                if (other.CompareTag("MainCamera"))
                {
                    other.gameObject.tag = "Untagged";
                    untagged.Add(other.gameObject);
                }
            }
            try
            {
                Assert.That(Camera.main, Is.Null, "no main camera at the prepare");
                FeedbackScheduler s = Sched();
                var item = new CameraFieldOfViewItem { m_Motion = PunchBy(new Vector4(20f, 0f, 0f, 0f)) };
                item.m_Timing.m_Duration = 1f;
                FeedbackPlayer p = Player(s, item);
                Assert.That(p.Runtime.Binding(0), Is.InstanceOf<CameraFieldOfViewBinding>(), "prepared without a camera");
                Camera camera = Cam();
                camera.gameObject.tag = "MainCamera";
                p.Play();
                Tick(s, 5);
                Assert.That(B(camera.fieldOfView), Is.Not.EqualTo(B(60f)), "the first Begin took the camera");
                Tick(s, 30);
                s.Tick(0f);
                Assert.That(B(camera.fieldOfView), Is.EqualTo(B(60f)), "the end");
                camera.gameObject.tag = "Untagged";

                var channel = new CameraFieldOfViewItem { m_Motion = PunchBy(new Vector4(20f, 0f, 0f, 0f)) };
                channel.m_Target = new TargetRef { m_Mode = TargetRefMode.Channel, m_Path = "", m_Anchor = "" };
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("channel targets need a receiver"));
                FeedbackPlayer onChannel = Player(Sched(), channel);
                Assert.That(onChannel.Runtime.Binding(0), Is.Null, "a channel is refused");
            }
            finally
            {
                foreach (GameObject o in untagged)
                {
                    if (o != null)
                        o.tag = "MainCamera";
                }
            }
        }

        // ---- the camera category · a flash's own category and the flash limit

        [Test]
        public void LQ13_CategoriesAndFlashes()
        {
            {
                FeedbackScheduler s = Sched();
                s.Context.Categories.SetEnabled(FeedbackCategory.Camera, false);
                Camera c1 = Cam(), c2 = Cam(true), c3 = Cam(), c4 = Cam(), c5 = Cam();
                Func<string> read = () => B(c1.fieldOfView) + B(c2.orthographicSize) + B(c3.fieldOfView) + B(c4.nearClipPlane) + B(c5.backgroundColor.r);
                string before = read();
                Player(s, On(new CameraFieldOfViewItem(), c1, PunchBy(new Vector4(20f, 0f, 0f, 0f)), 1f)).Play();
                Player(s, On(new CameraOrthographicSizeItem(), c2, PunchBy(new Vector4(2f, 0f, 0f, 0f)), 1f)).Play();
                Player(s, On(new CameraZoomItem(), c3, PunchBy(new Vector4(1f, 0f, 0f, 0f)), 1f)).Play();
                Player(s, On(new CameraClippingPlanesItem(), c4, TweenTo(new Vector4(2f, 50f, 0f, 0f)), 1f)).Play();
                Player(s, On(new CameraBackgroundColorItem(), c5, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 1f)).Play();
                Tick(s, 6);
                Assert.That(read(), Is.EqualTo(before), "the camera category off — no camera item moves");

                // a flash's category is Flash — the camera category off leaves it playing
                Camera flashed = Cam();
                float r = flashed.backgroundColor.r;
                Player(s, On(new CameraBackgroundColorItem(), flashed, FlashTo(new Vector4(1f, 0f, 0f, 1f)), 1f)).Play();
                Tick(s, 8);
                Assert.That(B(flashed.backgroundColor.r), Is.Not.EqualTo(B(r)), "the camera category off — a background flash plays");
            }
            {
                FeedbackScheduler s = Sched();
                CategoryGate gate = s.Context.Categories;
                int entries = gate.FlashEntries;
                Player(s, On(new LightColorItem(), Lamp(), FlashTo(new Vector4(1f, 0f, 0f, 1f)), 1f)).Play();
                s.Tick(Step);
                Assert.That(gate.FlashEntries, Is.EqualTo(entries + 1), "a light color flash counts");
                Player(s, On(new CameraBackgroundColorItem(), Cam(), FlashTo(new Vector4(1f, 0f, 0f, 1f)), 1f)).Play();
                s.Tick(Step);
                Assert.That(gate.FlashEntries, Is.EqualTo(entries + 2), "a background flash counts");
            }
            {
                FeedbackScheduler s = Sched();
                s.Context.Categories.SetEnabled(FeedbackCategory.Flash, false);
                Camera flashed = Cam(), tweened = Cam();
                float r = flashed.backgroundColor.r;
                Player(s, On(new CameraBackgroundColorItem(), flashed, FlashTo(new Vector4(1f, 0f, 0f, 1f)), 1f)).Play();
                Player(s, On(new CameraBackgroundColorItem(), tweened, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 1f)).Play();
                Tick(s, 8);
                Assert.That(B(flashed.backgroundColor.r), Is.EqualTo(B(r)), "the flash category off — the background flash is skipped");
                Assert.That(B(tweened.backgroundColor.r), Is.Not.EqualTo(B(r)), "the flash category off — the background tween plays");
            }
        }
    }
}
