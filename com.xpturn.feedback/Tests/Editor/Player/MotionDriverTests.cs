using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests.Editor
{
    // EditMode — Loop is the player's endless item, the Loop motions, the enable and the disable, Follow, the rotation's wrap,
    // the follow transform, the ends, the clocks, what it reads, the flash limit, the seeds, the format version, the warnings,
    // two on one property, an edit, the surface, the tick's allocation. Drivers are bound to test schedulers the row ticks
    // directly (Bind — an enable and a disable are Run and Halt). A row checks everything and judges at the end. Warnings and
    // errors are counted by a log listener; a logged exception is expected with LogAssert. Values are the same by their bits
    // or within a bound, the seconds within 1e-6 — a float dt's double sum
    public sealed class MotionDriverTests
    {
        [Serializable]
        public sealed class DriverTestUnregistered : TargetItem
        {
            static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Oscillate,
                category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

            internal override TargetTraits Traits => s_Traits;
        }

        static readonly List<string> s_Warnings = new List<string>();
        static readonly List<string> s_Errors = new List<string>();
        readonly List<string> m_Fail = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<MotionDriver> m_Drivers = new List<MotionDriver>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<GameObject> m_Untagged = new List<GameObject>();
        readonly List<CameraShakeReceiver> m_Receivers = new List<CameraShakeReceiver>();

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
                s_Warnings.Add(condition);
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                s_Errors.Add(type + ": " + condition);
        }

        [SetUp]
        public void SetUp()
        {
            m_Fail.Clear();
            s_Warnings.Clear();
            s_Errors.Clear();
            PlayerRuntime.ResetSession();
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            foreach (CameraShakeReceiver r in m_Receivers)
            {
                if (r != null)
                    r.Bind(null);
            }
            foreach (MotionDriver d in m_Drivers)
            {
                if (d != null)
                {
                    try
                    {
                        d.Bind(null);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
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
            foreach (GameObject g in m_Untagged)
            {
                if (g != null)
                    g.tag = "MainCamera";
            }
            m_Untagged.Clear();
            m_Receivers.Clear();
            m_Made.Clear();
            m_Drivers.Clear();
            m_Players.Clear();
            m_Schedulers.Clear();
            PlayerTestBinding.Reset();
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        static string F(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        static bool Bits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        GameObject Go(string name, Vector3 at = default, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null)
                go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            m_Made.Add(go);
            return go;
        }

        MotionDriver Driver(GameObject owner, TargetItem item, MotionDriverMode mode, FeedbackScheduler s, Action<MotionDriver> set = null)
        {
            var d = owner.AddComponent<MotionDriver>();
            d.m_Item = item;
            d.m_Mode = mode;
            set?.Invoke(d);
            m_Drivers.Add(d);
            if (s != null)
                d.Bind(s);
            return d;
        }

        FeedbackPlayer PlayerOn(GameObject owner, FeedbackScheduler s, params FeedbackItem[] items)
        {
            var p = owner.AddComponent<FeedbackPlayer>();
            m_Players.Add(p);
            p.m_Items.AddRange(items);
            p.Bind(s);
            p.EnsureRuntime();
            return p;
        }

        static TargetRef Self => new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };
        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };
        static TargetRef MainCam => new TargetRef { m_Mode = TargetRefMode.MainCamera, m_Path = "", m_Anchor = "" };

        static T Item<T>(FeedbackMotion motion, TargetRef? target = null) where T : TargetItem, new()
        {
            var item = new T { m_Motion = motion, m_Target = target ?? Self };
            item.m_Timing.m_Duration = 1f;
            return item;
        }

        static OscillateMotion Osc(float x, float f = 1f, float phase = 0f) => new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(x, 0f, 0f, 0f), m_Frequency = f, m_Phase = phase } };
        static WiggleMotion Wiggle() => new WiggleMotion { m_Params = new WiggleParams { m_Amplitude = Vector4.one, m_Frequency = 1f, m_Octaves = 2 } };
        static ShakeMotion Shake(Vector4 a) => new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = a, m_Frequency = 10f } };
        static BlinkMotion Blink(Vector4 v, float period = 0.2f) => new BlinkMotion { m_Params = new BlinkParams { m_Value = v, m_Period = period, m_Duty = 0.5f, m_StartOn = true } };
        static SpringMotion Spring(float f = 6f, float d = 0.4f) => new SpringMotion { m_Params = new SpringParams { m_Mode = SpringMode.MoveTo, m_Value = Vector4.zero, m_Frequency = f, m_Damping = d, m_Settle = 0.001f } };

        static void Tick(FeedbackScheduler s, int n, float dt = 0.05f)
        {
            for (int i = 0; i < n; i++)
                s.Tick(dt);
        }

        int Warned(int from) => s_Warnings.Count - from;

        // Loop is the player's endless item — within an ulp (the player's time is the timeline's integer microseconds, the
        // driver's a float dt's double sum)
        [Test]
        public void RE01_LoopIsTheEndlessItem()
        {
            FeedbackScheduler s = Sched();
            var a = Go("driver");
            Driver(a, Item<PositionItem>(Osc(1f)), MotionDriverMode.Loop, s);
            var b = Go("player");
            var item = Item<PositionItem>(Osc(1f));
            item.m_Timing.m_Infinite = true;
            PlayerOn(b, s, item).Play();
            double worst = 0.0;
            int differ = 0;
            for (int i = 0; i < 8; i++)
            {
                s.Tick(0.05f);
                worst = Math.Max(worst, Math.Abs(a.transform.localPosition.x - b.transform.localPosition.x));
                differ += Bits(a.transform.localPosition.x, b.transform.localPosition.x) ? 0 : 1;
            }
            Check(worst < 1e-5, "the driver is the player's endless item — worst " + R(worst) + " · ticks whose bits differ " + differ + "/8");
            var t = Go("tb", new Vector3(3f, 4f, 5f));
            Driver(t, new PlayerTestTarget { m_Motion = Osc(1f), m_Target = Self, m_Label = "tb" }, MotionDriverMode.Loop, s);
            PlayerTestBinding tb = PlayerTestBinding.Made.Last();
            BeginContext c = tb.LastBegin;
            Check(c.Intensity == 1f && c.Occurrence == 0 && double.IsPositiveInfinity(c.Seconds) && !c.Reverse, "the begin context");
            Check(c.HasPosition && c.Position == new Vector3(3f, 4f, 5f), "the start's place is the driver's");
            Check(tb.Context.Player == null && tb.Context.MaxOverlap == 1 && tb.Context.Owner == t, "the prepare context");
            List<string> log = PlayerTestBinding.Log.Where(l => l.StartsWith("tb.", StringComparison.Ordinal)).ToList();
            Check(log.Count >= 2 && log[0] == "tb.Begin#0" && log[1] == "tb.Evaluate(0)" && tb.LastTime.Seconds == 0.0, "t = 0 at the enable — " + string.Join(" ", log));
            Tick(s, 3);
            Check(s.ActiveCount == 3 && s.ReservedCount == 3, "active · reserved one each — " + s.ActiveCount + "/" + s.ReservedCount);
            // the channel shake's reach from the driver's place
            var nearOrigin = Go("cam at 0");
            var nearDriver = Go("cam at 100", new Vector3(100f, 0f, 0f));
            foreach (GameObject cam in new[] { nearOrigin, nearDriver })
            {
                CameraShakeReceiver r = cam.AddComponent<CameraShakeReceiver>();
                r.m_Channel = ChannelRef.Of(3);
                r.m_Range = 5f;
                r.Bind(s);
                m_Receivers.Add(r);
            }
            var shake = new CameraShakeItem { m_Motion = Shake(new Vector4(1f, 1f, 0f, 0f)), m_Target = new TargetRef { m_Mode = TargetRefMode.Channel, m_Channel = ChannelRef.Of(3), m_Path = "", m_Anchor = "" } };
            Driver(Go("shaker", new Vector3(100f, 0f, 0f)), shake, MotionDriverMode.Loop, s);
            Tick(s, 4);
            Check(nearDriver.transform.localPosition != new Vector3(100f, 0f, 0f) && nearOrigin.transform.localPosition == Vector3.zero, "a channel shake reaches from the driver's place");
            Check(s_Errors.Count == 0, "errors " + s_Errors.Count);
            End();
        }

        // the Loop motions — a refusal warns once a driver and reserves nothing
        [Test]
        public void RE02_LoopMotions()
        {
            FeedbackScheduler s = Sched();
            int running = 0;
            foreach ((string name, FeedbackMotion motion) in new (string, FeedbackMotion)[] { ("Oscillate", Osc(1f)), ("Wiggle", Wiggle()), ("Shake", Shake(Vector4.one)) })
            {
                MotionDriver d = Driver(Go(name), Item<PositionItem>(motion), MotionDriverMode.Loop, s);
                Check(d.IsRunning, "Loop runs " + name);
                running += d.IsRunning ? 1 : 0;
            }
            var light = Go("light").AddComponent<Light>();
            MotionDriver blink = Driver(light.gameObject, Item<LightIntensityItem>(Blink(new Vector4(5f, 0f, 0f, 0f)), Direct(light)), MotionDriverMode.Loop, s);
            Check(blink.IsRunning, "Loop runs Blink");
            running += blink.IsRunning ? 1 : 0;
            foreach ((string name, FeedbackMotion motion) in new (string, FeedbackMotion)[]
                     {
                         ("Set", new SetMotion()), ("Tween", new TweenMotion()), ("Punch", new PunchMotion()), ("Curve", new CurveMotion()), ("Flash", new FlashMotion()),
                         ("Gradient", new GradientMotion()), ("Spring", Spring()), ("null", null),
                     })
            {
                int w = s_Warnings.Count;
                MotionDriver d = Driver(Go(name), Item<PositionItem>(motion), MotionDriverMode.Loop, s);
                Check(!d.IsRunning && Warned(w) == 1, "Loop refuses " + name + " with one warning — running " + d.IsRunning + " warnings " + Warned(w));
                running += d.IsRunning ? 1 : 0;
            }
            Check(s.ReservedCount == running, "reserved only for the running — " + s.ReservedCount + " vs " + running);
            End();
        }

        // the enable and the disable — the binding and the reservation stay while off
        [Test]
        public void RE03_EnableDisable()
        {
            FeedbackScheduler s = Sched();
            var go = Go("ed", new Vector3(2f, 0f, 0f));
            MotionDriver d = Driver(go, Item<PositionItem>(Osc(1f)), MotionDriverMode.Loop, s);
            Tick(s, 3);
            ItemBinding binding = d.Binding;
            int r0 = s.ReservedCount, a0 = s.ActiveCount, w0 = s_Warnings.Count;
            d.Halt(true);
            s.Tick(0.02f);
            Check(Bits(go.transform.localPosition.x, 2f), "off — home " + F(go.transform.localPosition.x));
            Check(s.ReservedCount == r0 && s.ActiveCount == 0, "off — the reservation stays, no slot (" + s.ReservedCount + "/" + s.ActiveCount + ")");
            d.Run();
            s.Tick(0.02f);
            Check(Math.Abs(d.Seconds - 0.02) < 1e-6, "the seconds from 0 at the enable — " + F(d.Seconds));
            for (int i = 0; i < 100; i++)
            {
                d.Halt(true);
                s.Tick(0.02f);
                d.Run();
                s.Tick(0.02f);
            }
            Check(s.ReservedCount == r0 && s.ActiveCount == a0 && Warned(w0) == 0, "100 cycles a frame apart — reserved " + s.ReservedCount + " active " + s.ActiveCount + " warnings " + Warned(w0));
            Check(ReferenceEquals(d.Binding, binding), "the binding is kept — no new one");
            int w1 = s_Warnings.Count;
            for (int i = 0; i < 100; i++)
            {
                d.Halt(true);
                d.Run();
            }
            s.Tick(0.02f);
            Check(Warned(w1) == 2 && s.ReservedCount == r0 && s.ActiveCount == a0, "100 cycles in a frame — the two churn warnings, the counts the same (" + Warned(w1) + ")");
            d.Bind(null);
            Check(s.ReservedCount == 0, "Bind(null) releases — reserved " + s.ReservedCount);
            Check(s_Errors.Count == 0, "errors " + s_Errors.Count);
            End();
        }

        // Follow is SpringVector3's step through the slot's door
        [Test]
        public void RE04_Follow()
        {
            FeedbackScheduler s = Sched();
            var go = Go("f");
            MotionDriver d = Driver(go, Item<PositionItem>(Spring()), MotionDriverMode.Follow, s);
            d.SetTarget(new Vector4(10f, 0f, 0f, 0f));
            var spring = new SpringVector3(Vector3.zero, 6f, 0.4f) { Target = new Vector3(10f, 0f, 0f) };
            bool same = true;
            for (int i = 0; i < 60; i++)
            {
                s.Tick(0.05f);
                spring.Step(0.05f);
                same &= Bits(go.transform.localPosition.x, spring.Value.x);
            }
            Check(same, "the same as SpringVector3");
            Check(Mathf.Abs(go.transform.localPosition.x - 10f) < 0.01f, "settles — " + F(go.transform.localPosition.x));
            // a game's write is overwritten
            go.transform.localPosition = new Vector3(20f, 0f, 0f);
            s.Tick(0.05f);
            Check(Mathf.Abs(go.transform.localPosition.x - 10f) < 0.01f, "a game's write is overwritten — " + F(go.transform.localPosition.x));
            // mid-flight a new target keeps the velocity
            d.SetTarget(new Vector4(0f, 0f, 0f, 0f));
            s.Tick(0.05f);
            Vector4 v = d.FollowVelocity;
            d.SetTarget(new Vector4(-5f, 0f, 0f, 0f));
            Check(d.FollowVelocity == v, "a new target keeps the velocity");
            // a re-enable starts where the value is, at rest
            d.Halt(true);
            go.transform.localPosition = new Vector3(20f, 0f, 0f);
            d.Run();
            Check(d.FollowVelocity == Vector4.zero && Bits(d.FollowValue.x, 20f), "a re-enable starts where the value is, at rest — " + F(d.FollowValue.x) + " v " + F(d.FollowVelocity.x));
            Tick(s, 100);
            Check(Mathf.Abs(go.transform.localPosition.x + 5f) < 0.01f, "SetTarget's value outlives a disable — " + F(go.transform.localPosition.x));
            // SetTarget before the first enable · no target stays
            var g2 = Go("f2", new Vector3(3f, 0f, 0f));
            MotionDriver d2 = Driver(g2, Item<PositionItem>(Spring()), MotionDriverMode.Follow, null);
            d2.SetTarget(new Vector4(5f, 0f, 0f, 0f));
            d2.Bind(s);
            var g3 = Go("f3", new Vector3(3f, 0f, 0f));
            Driver(g3, Item<PositionItem>(Spring()), MotionDriverMode.Follow, s);
            Tick(s, 60);
            Check(Mathf.Abs(g2.transform.localPosition.x - 5f) < 0.01f, "SetTarget before the enable is kept — " + F(g2.transform.localPosition.x));
            Check(Bits(g3.transform.localPosition.x, 3f), "no target — it stays — " + F(g3.transform.localPosition.x));
            // World under a turned, scaled parent
            var parent = Go("parent", new Vector3(5f, 0f, 0f));
            parent.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            parent.transform.localScale = new Vector3(2f, 2f, 2f);
            var child = Go("child", Vector3.zero, parent.transform);
            var world = Item<PositionItem>(Spring());
            world.m_Space = PositionSpace.World;
            Driver(child, world, MotionDriverMode.Follow, s).SetTarget(new Vector4(10f, 0f, 0f, 0f));
            Tick(s, 200);
            Check((child.transform.position - new Vector3(10f, 0f, 0f)).magnitude < 0.01f, "World — " + child.transform.position);
            // a non-finite target pauses; a finite one resumes
            var g4 = Go("f4");
            MotionDriver d4 = Driver(g4, Item<PositionItem>(Spring()), MotionDriverMode.Follow, s);
            d4.SetTarget(new Vector4(4f, 0f, 0f, 0f));
            Tick(s, 2);
            float before = g4.transform.localPosition.x;
            d4.SetTarget(new Vector4(float.NaN, 0f, 0f, 0f));
            Tick(s, 3);
            float paused = g4.transform.localPosition.x;
            d4.SetTarget(new Vector4(4f, 0f, 0f, 0f));
            Tick(s, 100);
            Check(Bits(before, paused) && Mathf.Abs(g4.transform.localPosition.x - 4f) < 0.01f, "a NaN target pauses, a finite one resumes — " + F(before) + " " + F(paused) + " " + F(g4.transform.localPosition.x));
            Check(s_Errors.Count == 0, "errors " + s_Errors.Count);
            End();
        }

        // the rotation — each angle within ±180° and the nearer of one rotation's two Euler triples
        [Test]
        public void RE05_Rotation()
        {
            FeedbackScheduler s = Sched();
            var go = Go("rot");
            go.transform.localRotation = Quaternion.Euler(0f, 170f, 0f);
            MotionDriver d = Driver(go, Item<RotationItem>(Spring()), MotionDriverMode.Follow, s);
            d.SetTarget(new Vector4(0f, -170f, 0f, 0f));
            float min = 999f, max = -999f;
            for (int i = 0; i < 60; i++)
            {
                s.Tick(0.05f);
                min = Mathf.Min(min, d.FollowValue.y);
                max = Mathf.Max(max, d.FollowValue.y);
            }
            Check(min >= 169f && max <= 200f && Mathf.Abs(Mathf.DeltaAngle(go.transform.localEulerAngles.y, 190f)) < 0.1f, "170° → −170° the short way — " + F(min) + " ~ " + F(max));
            foreach ((string name, Vector3 axis) in new[] { ("pitch", Vector3.right), ("yaw", Vector3.up), ("roll", Vector3.forward) })
            {
                var other = Go("o" + name);
                var me = Go("m" + name);
                Driver(me, Item<RotationItem>(Spring(2f, 1f)), MotionDriverMode.Follow, s, x => x.m_FollowTarget = other.transform);
                s.Tick(0.05f);
                other.transform.localRotation = Quaternion.AngleAxis(120f, axis);
                float path = 0f, off = 0f;
                Quaternion last = me.transform.localRotation;
                for (int i = 0; i < 100; i++)
                {
                    s.Tick(0.05f);
                    Quaternion q = me.transform.localRotation;
                    path += Quaternion.Angle(last, q);
                    last = q;
                    q.ToAngleAxis(out float angle, out Vector3 turn);
                    if (angle > 1f)
                        off = Mathf.Max(off, Vector3.Angle(turn, axis));
                }
                Check(path < 125f && off < 1f && Quaternion.Angle(me.transform.localRotation, other.transform.localRotation) < 0.5f, "a 120° " + name + " jump turns about its axis — " + F(path) + "° off " + F(off) + "°");
            }
            var o2 = Go("sweep");
            var m2 = Go("sweeper");
            Driver(m2, Item<RotationItem>(Spring(8f, 1f)), MotionDriverMode.Follow, s, x => x.m_FollowTarget = o2.transform);
            float worst = 0f;
            for (int i = 0; i < 120; i++)
            {
                o2.transform.localRotation = Quaternion.AngleAxis(Mathf.Min(i, 60) * 3f, Vector3.right);
                s.Tick(0.05f);
                worst = Mathf.Max(worst, Quaternion.Angle(m2.transform.localRotation, o2.transform.localRotation));
            }
            Check(worst < 1.5f, "a pitch sweep through 90° — worst " + F(worst) + "°");
            End();
        }

        // the follow transform — its same property in the item's space; SetTarget(Transform) at run time
        [Test]
        public void RE06_FollowTransform()
        {
            FeedbackScheduler s = Sched();
            var other = Go("other", new Vector3(3f, 0f, 0f));
            var go = Go("me");
            MotionDriver d = Driver(go, Item<PositionItem>(Spring()), MotionDriverMode.Follow, s, x => x.m_FollowTarget = other.transform);
            Tick(s, 100);
            float a = go.transform.localPosition.x;
            other.transform.localPosition = new Vector3(6f, 0f, 0f);
            Tick(s, 100);
            Check(Mathf.Abs(a - 3f) < 0.01f && Mathf.Abs(go.transform.localPosition.x - 6f) < 0.01f, "follows the transform — " + F(a) + " " + F(go.transform.localPosition.x));
            // rotation (World) and scale
            var turnerParent = Go("turner parent");
            turnerParent.transform.rotation = Quaternion.Euler(20f, 45f, 0f);
            var turner = Go("turner", Vector3.zero, turnerParent.transform);
            turner.transform.localRotation = Quaternion.Euler(0f, 30f, 45f);
            var parent = Go("parent");
            parent.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var child = Go("child", Vector3.zero, parent.transform);
            var ri = Item<RotationItem>(Spring());
            ri.m_Space = TransformSpace.World;
            Driver(child, ri, MotionDriverMode.Follow, s, x => x.m_FollowTarget = turner.transform);
            var big = Go("big");
            big.transform.localScale = new Vector3(3f, 2f, 1f);
            var sc = Go("scaled");
            Driver(sc, Item<ScaleItem>(Spring()), MotionDriverMode.Follow, s, x => x.m_FollowTarget = big.transform);
            Tick(s, 100);
            Check(Quaternion.Angle(child.transform.rotation, turner.transform.rotation) < 0.1f, "a World rotation follows");
            Check((sc.transform.localScale - new Vector3(3f, 2f, 1f)).magnitude < 0.01f, "a scale follows");
            // the transform destroyed — the last target stays
            Object.DestroyImmediate(other);
            other = null;
            Tick(s, 50);
            Check(Mathf.Abs(go.transform.localPosition.x - 6f) < 0.01f && d.IsRunning, "a destroyed follow transform — the last target stays — " + F(go.transform.localPosition.x));
            // a light with a follow transform: one warning, SetTarget's value
            var light = Go("light").AddComponent<Light>();
            light.intensity = 1f;
            int w = s_Warnings.Count;
            MotionDriver dl = Driver(light.gameObject, Item<LightIntensityItem>(Spring(), Direct(light)), MotionDriverMode.Follow, s, x => x.m_FollowTarget = big.transform);
            dl.SetTarget(new Vector4(5f, 0f, 0f, 0f));
            Tick(s, 100);
            Check(Warned(w) == 1 && Mathf.Abs(light.intensity - 5f) < 0.01f, "a light with a follow transform — one warning, SetTarget's value — " + Warned(w) + " " + F(light.intensity));
            // SetTarget(Transform) at run time · null takes it off
            var pet = Go("pet");
            var player = Go("player", new Vector3(7f, 0f, 0f));
            MotionDriver dp = Driver(pet, Item<PositionItem>(Spring()), MotionDriverMode.Follow, s);
            dp.SetTarget(player.transform);
            Tick(s, 100);
            float follows = pet.transform.localPosition.x;
            dp.SetTarget((Transform)null);
            player.transform.localPosition = new Vector3(-7f, 0f, 0f);
            Tick(s, 100);
            Check(Mathf.Abs(follows - 7f) < 0.01f && Mathf.Abs(pet.transform.localPosition.x - 7f) < 0.01f, "SetTarget(Transform) follows it · null takes it off — " + F(follows) + " " + F(pet.transform.localPosition.x));
            Check(s_Errors.Count == 0, "errors " + s_Errors.Count);
            End();
        }

        // the ends — a shutdown, a binding's exception, a dead target, a target destroyed while off, no
        // main camera, a SetActive on the driver's own object or an ancestor
        [Test]
        public void RE07_Ends()
        {
            FeedbackScheduler s = Sched();
            MotionDriver a = Driver(Go("a"), Item<PositionItem>(Osc(1f)), MotionDriverMode.Loop, s);
            Tick(s, 1);
            s.Shutdown();
            Check(!a.IsRunning && a.IsPrepared, "a shutdown stops it — the binding stays");
            a.Halt(true);
            a.Run();
            Check(!a.IsRunning, "no enable under a closed scheduler");
            FeedbackScheduler s2 = Sched();
            var ti = new PlayerTestTarget { m_Motion = Osc(1f), m_Target = Self, m_Label = "x" };
            MotionDriver x = Driver(Go("x"), ti, MotionDriverMode.Loop, s2);
            int made = PlayerTestBinding.Made.Count;
            PlayerTestBinding.ThrowsIn.Add("x.Evaluate");
            int e0 = s_Errors.Count;
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: test x.Evaluate");
            Tick(s2, 2);
            PlayerTestBinding.ThrowsIn.Clear();
            Check(s_Errors.Count - e0 == 1 && !x.IsRunning && x.IsPrepared && s2.ReservedCount == 1 && s2.ActiveCount == 0, "a binding's exception — one log, it stops, the binding stays (" + (s_Errors.Count - e0) + " " + s2.ReservedCount + "/" + s2.ActiveCount + ")");
            x.Halt(true);
            x.Run();
            Tick(s2, 1);
            Check(x.IsRunning && PlayerTestBinding.Made.Count == made, "the re-enable reuses the binding");
            MotionDriver dd = Driver(Go("dead"), new PlayerTestTarget { m_Motion = Osc(1f), m_Target = Self, m_Label = "dead" }, MotionDriverMode.Loop, s2);
            PlayerTestBinding db = PlayerTestBinding.Made.Last();
            int r1 = s2.ReservedCount, w1 = s_Warnings.Count, e1 = s_Errors.Count;
            PlayerTestBinding.Dead.Add("dead");
            Tick(s2, 2);
            Check(!dd.IsRunning && !dd.IsPrepared && db.Released == 1 && Warned(w1) == 0 && s_Errors.Count == e1 && s2.ReservedCount == r1 - 1, "a dead target — it stops quietly and lets go (" + s2.ReservedCount + ")");
            // the target destroyed while off
            var host = Go("host");
            var target = Go("t", new Vector3(1f, 0f, 0f));
            MotionDriver dt = Driver(host, Item<PositionItem>(Osc(1f), Direct(target.transform)), MotionDriverMode.Loop, s2);
            Tick(s2, 1);
            dt.Halt(true);
            Object.DestroyImmediate(target);
            int w2 = s_Warnings.Count, r2 = s2.ReservedCount;
            dt.Run();
            Check(!dt.IsRunning && Warned(w2) == 1 && s2.ReservedCount == r2 - 1, "the target destroyed while off — the enable warns once, lets go (" + Warned(w2) + " " + s2.ReservedCount + ")");
            // no main camera — a warning each; a camera made later runs after a re-enable
            foreach (GameObject g in GameObject.FindGameObjectsWithTag("MainCamera"))
            {
                g.tag = "Untagged";
                m_Untagged.Add(g);
            }
            int w3 = s_Warnings.Count;
            MotionDriver fc = Driver(Go("fc"), Item<PositionItem>(Spring(), MainCam), MotionDriverMode.Follow, s2);
            MotionDriver lc = Driver(Go("lc"), Item<PositionItem>(Osc(1f), MainCam), MotionDriverMode.Loop, s2);
            Check(!fc.IsRunning && !lc.IsRunning && Warned(w3) == 2, "no main camera — a warning each (" + Warned(w3) + ")");
            var cam = Go("late camera").AddComponent<Camera>();
            cam.gameObject.tag = "MainCamera";
            fc.Halt(true);
            fc.Run();
            lc.Halt(true);
            lc.Run();
            Tick(s2, 2);
            Check(fc.IsRunning && lc.IsRunning, "a camera made later — a re-enable runs");
            // a SetActive item on the driver's own object or an ancestor is refused
            var self = Go("self");
            int w4 = s_Warnings.Count, r4 = s2.ReservedCount;
            MotionDriver ds = Driver(self, Item<SetActiveItem>(Blink(Vector4.zero)), MotionDriverMode.Loop, s2);
            var parent = Go("parent");
            var kid = Go("kid", Vector3.zero, parent.transform);
            MotionDriver dk = Driver(kid, Item<SetActiveItem>(Blink(Vector4.zero), Direct(parent)), MotionDriverMode.Loop, s2);
            Check(!ds.IsRunning && !dk.IsRunning && Warned(w4) == 2 && s2.ReservedCount == r4, "a SetActive on its own object or an ancestor is refused (" + Warned(w4) + ")");
            End();
        }

        // the clocks — a scale 0 · unscaled dt
        [Test]
        public void RE08_Clocks()
        {
            FeedbackScheduler s = Sched();
            MotionDriver scaled = Driver(Go("sc"), Item<PositionItem>(Osc(1f)), MotionDriverMode.Loop, s);
            MotionDriver unscaled = Driver(Go("un"), Item<PositionItem>(Osc(1f)), MotionDriverMode.Loop, s, d => d.m_TimeMode = PlayerTimeMode.Unscaled);
            var ii = Item<PositionItem>(Osc(1f));
            ii.m_Timing.m_Clock = ItemClock.Unscaled;
            MotionDriver itemClock = Driver(Go("ic"), ii, MotionDriverMode.Loop, s);
            var fs = Go("fs");
            Driver(fs, Item<PositionItem>(Spring()), MotionDriverMode.Follow, s).SetTarget(new Vector4(5f, 0f, 0f, 0f));
            for (int i = 0; i < 4; i++)
                s.Tick(new FrameTime(0f, 0.05f));
            Check(scaled.Seconds == 0.0 && fs.transform.localPosition.x == 0f, "Scaled stands — Loop " + F(scaled.Seconds) + " · Follow " + F(fs.transform.localPosition.x));
            Check(Math.Abs(unscaled.Seconds - 0.2) < 1e-6, "the driver's Unscaled runs — " + F(unscaled.Seconds));
            Check(Math.Abs(itemClock.Seconds - 0.2) < 1e-6, "the item's Unscaled clock runs — " + F(itemClock.Seconds));
            End();
        }

        // what it reads — the category's scale and switch (Loop only), the intensity response, the random output,
        // an unchecked item, a finite item
        [Test]
        public void RE09_WhatItReads()
        {
            FeedbackScheduler s = Sched();
            var go = Go("cat");
            Driver(go, Item<PositionItem>(Osc(1f)), MotionDriverMode.Loop, s);
            Tick(s, 5);
            float on = go.transform.localPosition.x;
            s.Context.Categories.SetScale(FeedbackCategory.Motion, 0.5f);
            Tick(s, 20);
            float half = go.transform.localPosition.x;
            s.Context.Categories.SetScale(FeedbackCategory.Motion, 1f);
            s.Context.Categories.SetEnabled(FeedbackCategory.Motion, false);
            Tick(s, 20);
            float off = go.transform.localPosition.x;
            Check(Mathf.Abs(on - 1f) < 1e-4f && Mathf.Abs(half - 0.5f) < 1e-4f && off == 0f, "the category's scale · off — " + F(on) + " " + F(half) + " " + F(off));
            var g2 = Go("late");
            MotionDriver late = Driver(g2, Item<PositionItem>(Osc(1f)), MotionDriverMode.Loop, s);
            Tick(s, 5);
            float lateOff = g2.transform.localPosition.x;
            s.Context.Categories.SetEnabled(FeedbackCategory.Motion, true);
            Tick(s, 20);
            Check(late.IsRunning && lateOff == 0f && Mathf.Abs(g2.transform.localPosition.x - 1f) < 1e-4f, "enabled while the category is off — runs, shows when on — " + late.IsRunning + " " + F(lateOff) + " " + F(g2.transform.localPosition.x));
            s.Context.Categories.SetEnabled(FeedbackCategory.Motion, false);
            var g3 = Go("follow");
            Driver(g3, Item<PositionItem>(Spring()), MotionDriverMode.Follow, s).SetTarget(new Vector4(4f, 0f, 0f, 0f));
            Tick(s, 100);
            Check(Mathf.Abs(g3.transform.localPosition.x - 4f) < 0.01f, "Follow takes no category — " + F(g3.transform.localPosition.x));
            s.Context.Categories.SetEnabled(FeedbackCategory.Motion, true);
            var rows = new List<float>();
            foreach ((IntensityResponse response, bool random) in new[] { (IntensityResponse.Receive, false), (IntensityResponse.Ignore, false), (IntensityResponse.Receive, true) })
            {
                var g = Go("i" + response + random);
                var item = Item<PositionItem>(Osc(1f));
                item.m_Intensity = response;
                if (random)
                {
                    item.m_Random.m_RandomizeOutput = true;
                    item.m_Random.m_OutputRange = new Vector2(0.5f, 0.5f);
                }
                Driver(g, item, MotionDriverMode.Loop, s, d => d.m_Intensity = 0.4f);
                Tick(s, 5);
                rows.Add(g.transform.localPosition.x);
            }
            Check(Mathf.Abs(rows[0] - 0.4f) < 1e-4f && Mathf.Abs(rows[1] - 1f) < 1e-4f && Mathf.Abs(rows[2] - 0.2f) < 1e-4f, "the intensity response · the random output — " + string.Join(" ", rows.Select(r => F(r))));
            var unchecked_ = Item<PositionItem>(Osc(1f));
            unchecked_.m_Active = false;
            int w = s_Warnings.Count;
            MotionDriver un = Driver(Go("unchecked"), unchecked_, MotionDriverMode.Loop, s);
            Check(!un.IsRunning && Warned(w) == 0, "an unchecked item — nothing, no warning");
            var g5 = Go("finite");
            var finite = Item<PositionItem>(Osc(1f));
            finite.m_Timing.m_Duration = 0.5f;
            MotionDriver df = Driver(g5, finite, MotionDriverMode.Loop, s);
            Tick(s, 65);
            Check(df.IsRunning && Mathf.Abs(g5.transform.localPosition.x - 1f) < 1e-4f, "a finite item runs endlessly — " + F(g5.transform.localPosition.x));
            End();
        }

        // the flash limit is not asked — a driver's Blink stays out of the window
        [Test]
        public void RE10_FlashLimit()
        {
            FeedbackScheduler s = Sched();
            s.Context.Categories.SetFlashLimit(3);
            var light = Go("l1").AddComponent<Light>();
            light.intensity = 1f;
            Driver(light.gameObject, Item<LightIntensityItem>(Blink(new Vector4(5f, 0f, 0f, 0f)), Direct(light)), MotionDriverMode.Loop, s);
            s.Tick(0.05f);
            int entries = s.Context.Categories.FlashEntries;
            var l2 = Go("l2").AddComponent<Light>();
            l2.intensity = 1f;
            var flash = Item<LightIntensityItem>(new FlashMotion { m_Params = new FlashParams { m_Target = new Vector4(5f, 0f, 0f, 0f), m_Attack = 0.1f, m_Hold = 0.2f, m_Release = 0.7f } }, Direct(l2));
            flash.m_Timing.m_Duration = 0.5f;
            PlayerOn(l2.gameObject, s, flash).Play();
            Tick(s, 3);
            Check(entries == 0 && l2.intensity > 1.5f, "a driver's Blink stays out of the window · a player's flash right after passes — " + entries + " " + F(l2.intensity));
            FeedbackScheduler s2 = Sched();
            s2.Context.Categories.SetFlashLimit(3);
            s2.Context.Categories.TryFlash(3);
            var l3 = Go("l3").AddComponent<Light>();
            l3.intensity = 1f;
            Driver(l3.gameObject, Item<LightIntensityItem>(Blink(new Vector4(5f, 0f, 0f, 0f)), Direct(l3)), MotionDriverMode.Loop, s2);
            s2.Tick(0.05f);
            Check(Mathf.Abs(l3.intensity - 5f) < 1e-4f, "with the window full it blinks at once — " + F(l3.intensity));
            End();
        }

        // the seeds — the session's counter, a new one each enable
        [Test]
        public void RE11_Seeds()
        {
            FeedbackScheduler s = Sched();
            PlayerRuntime.ResetSession();
            MotionDriver a = Driver(Go("a"), Item<PositionItem>(Wiggle()), MotionDriverMode.Loop, s);
            MotionDriver b = Driver(Go("b"), Item<PositionItem>(Wiggle()), MotionDriverMode.Loop, s);
            uint sa = a.Seed, sb = b.Seed;
            a.Halt(true);
            a.Run();
            Check(sa != sb && a.Seed != sa, "a new seed each enable · two drivers differ");
            PlayerRuntime.ResetSession();
            MotionDriver c = Driver(Go("c"), Item<PositionItem>(Wiggle()), MotionDriverMode.Loop, s);
            Check(c.Seed == sa, "after a session reset the first seed is the same — " + c.Seed + " / " + sa);
            End();
        }

        // the format version — a container of one item
        [Test]
        public void RE12_Format()
        {
            var src = Go("src").AddComponent<MotionDriver>();
            src.m_Item = Item<PositionItem>(Osc(1f));
            src.m_FormatVersion = 0;
            string json = EditorJsonUtility.ToJson(src);
            var dst = Go("dst").AddComponent<MotionDriver>();
            EditorJsonUtility.FromJsonOverwrite(json, dst);
            Check(dst.m_FormatVersion == FeedbackFormat.Current && dst.m_Item is PositionItem, "a version 0 driver read back is the current one — " + dst.m_FormatVersion);
            FeedbackScheduler s = Sched();
            int w = s_Warnings.Count;
            MotionDriver n = Driver(Go("newer"), Item<PositionItem>(Osc(1f)), MotionDriverMode.Loop, s, x => x.m_FormatVersion = FeedbackFormat.Current + 1);
            n.Halt(true);
            n.Bind(null);
            n.Bind(s);
            Check(n.IsRunning && Warned(w) == 1 && s_Warnings.Last().StartsWith(MotionDriver.NewerFormatWarning, StringComparison.Ordinal), "a newer format — one warning, it runs (" + Warned(w) + ")");
            End();
        }

        // the warnings — once per cause; an unregistered type once per type and session with
        // the players; SetTarget on a Loop
        [Test]
        public void RE13_Warnings()
        {
            FeedbackScheduler s = Sched();
            int w = s_Warnings.Count;
            var anchored = Item<PositionItem>(Osc(1f));
            anchored.m_Target = new TargetRef { m_Mode = TargetRefMode.Anchor, m_Path = "", m_Anchor = "spawn" };
            MotionDriver an = Driver(Go("an"), anchored, MotionDriverMode.Loop, s);
            Check(!an.IsRunning && Warned(w) == 1 && s_Warnings.Last().Contains("no anchor of that name"), "an anchor — not running, its warning");
            w = s_Warnings.Count;
            MotionDriver none = Driver(Go("none"), null, MotionDriverMode.Loop, s);
            Check(!none.IsRunning && Warned(w) == 1 && s_Warnings.Last() == MotionDriver.NoItemWarning, "no item — its warning");
            // a second cause after the first is shown; the same cause twice is not
            w = s_Warnings.Count;
            none.m_Item = Item<PositionItem>(new TweenMotion());
            none.Halt(true);
            none.Run();
            int second = Warned(w);
            none.Halt(true);
            none.Run();
            Check(second == 1 && Warned(w) == 1, "once per cause — the second cause " + second + " · its repeat " + (Warned(w) - second));
            // the unregistered type: once per type and session, with the players
            w = s_Warnings.Count;
            Driver(Go("u1"), new DriverTestUnregistered { m_Motion = Osc(1f), m_Target = Self }, MotionDriverMode.Loop, s);
            Driver(Go("u2"), new DriverTestUnregistered { m_Motion = Osc(1f), m_Target = Self }, MotionDriverMode.Loop, s);
            Check(Warned(w) == 1, "an unregistered type — once per type and session (" + Warned(w) + ")");
            // SetTarget on a Loop warns once
            MotionDriver loop = Driver(Go("loop"), Item<PositionItem>(Osc(1f)), MotionDriverMode.Loop, s);
            w = s_Warnings.Count;
            loop.SetTarget(new Vector4(1f, 0f, 0f, 0f));
            loop.SetTarget(new Vector4(2f, 0f, 0f, 0f));
            Check(Warned(w) == 1 && s_Warnings.Last() == MotionDriver.LoopTargetWarning, "SetTarget on a Loop — one warning (" + Warned(w) + ")");
            End();
        }

        // two on one property — Loops add, Loop and Follow compose, two Follows: the later slot's write
        [Test]
        public void RE14_Together()
        {
            FeedbackScheduler s = Sched();
            var go = Go("two");
            Driver(go, Item<PositionItem>(Osc(1f)), MotionDriverMode.Loop, s);
            Driver(go, Item<PositionItem>(Osc(2f)), MotionDriverMode.Loop, s);
            Tick(s, 5);
            Check(Mathf.Abs(go.transform.localPosition.x - 3f) < 1e-4f, "two Loops add — " + F(go.transform.localPosition.x));
            var fl = Go("fl");
            Driver(fl, Item<PositionItem>(Spring()), MotionDriverMode.Follow, s).SetTarget(new Vector4(5f, 0f, 0f, 0f));
            Driver(fl, Item<PositionItem>(Osc(1f)), MotionDriverMode.Loop, s);
            Tick(s, 205);
            Check(Mathf.Abs(fl.transform.localPosition.x - 6f) < 0.01f, "Loop and Follow compose — " + F(fl.transform.localPosition.x));
            var g2 = Go("two follow");
            MotionDriver f1 = Driver(g2, Item<PositionItem>(Spring()), MotionDriverMode.Follow, s);
            MotionDriver f2 = Driver(g2, Item<PositionItem>(Spring()), MotionDriverMode.Follow, s);
            f1.SetTarget(new Vector4(5f, 0f, 0f, 0f));
            f2.SetTarget(new Vector4(-5f, 0f, 0f, 0f));
            Tick(s, 200);
            Check(Mathf.Abs(g2.transform.localPosition.x + 5f) < 0.01f, "two Follows — the later slot's write — " + F(g2.transform.localPosition.x));
            End();
        }

        // an edit — SerializedObject's apply marks it (OnValidate); the next tick prepares again in the same slot
        [Test]
        public void RE15_Edit()
        {
            FeedbackScheduler s = Sched();
            var go = Go("ed");
            MotionDriver d = Driver(go, Item<PositionItem>(Osc(1f)), MotionDriverMode.Loop, s);
            Tick(s, 5);
            ((OscillateMotion)d.m_Item.m_Motion).m_Params.m_Amplitude = new Vector4(3f, 0f, 0f, 0f);
            Tick(s, 20);
            Check(Mathf.Abs(go.transform.localPosition.x - 1f) < 1e-4f, "a change without the mark is not seen — " + F(go.transform.localPosition.x));
            var so = new SerializedObject(d);
            SerializedProperty amp = so.FindProperty("m_Item.m_Motion.m_Params.m_Amplitude");
            amp.vector4Value = new Vector4(4f, 0f, 0f, 0f);
            so.ApplyModifiedProperties();
            Check(d.IsDirty, "SerializedObject's apply marks it (OnValidate)");
            int reserved = s.ReservedCount;
            Tick(s, 5);
            Check(Mathf.Abs(go.transform.localPosition.x - 4f) < 1e-3f && Math.Abs(d.Seconds - 0.25) < 1e-6 && s.ReservedCount == reserved && d.IsRunning, "the next tick prepares again — from 0 s — " + F(go.transform.localPosition.x) + " " + F(d.Seconds));
            End();
        }

        // the surface — two public types, the mode's values, SetTarget twice, six saved fields
        [Test]
        public void RE16_Surface()
        {
            Assembly core = typeof(MotionDriver).Assembly;
            string[] mine = core.GetExportedTypes().Where(t => t.Name.StartsWith("MotionDriver", StringComparison.Ordinal)).Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Check(mine.SequenceEqual(new[] { "MotionDriver", "MotionDriverMode" }), "two public types — " + string.Join(" ", mine));
            Check((int)MotionDriverMode.Loop == 0 && (int)MotionDriverMode.Follow == 1 && Enum.GetNames(typeof(MotionDriverMode)).Length == 2, "MotionDriverMode 0 · 1");
            string[] methods = typeof(MotionDriver).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(x => x.Name + "(" + string.Join(",", x.GetParameters().Select(p => p.ParameterType.Name)) + ")").OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Check(methods.SequenceEqual(new[] { "SetTarget(Transform)", "SetTarget(Vector4)" }), "public methods — " + string.Join(" ", methods));
            string[] fields = typeof(MotionDriver).GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
                .Where(f => f.IsDefined(typeof(SerializeField)) || f.IsDefined(typeof(SerializeReference))).Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Check(fields.SequenceEqual(new[] { "m_FollowTarget", "m_FormatVersion", "m_Intensity", "m_Item", "m_Mode", "m_TimeMode" }), "six saved fields — " + string.Join(" ", fields));
            Type follow = core.GetType("xpTURN.Feedback.IFollowBinding", true);
            Check(!follow.IsPublic, "IFollowBinding is internal");
            End();
        }

        // the tick of five drivers allocates nothing (the first runs are MotionDriverPlayTests'). The code's own allocation is
        // there every time, so "allocates" takes five reports in a row — 6000.3 also counts allocations that are not the
        // delegate's
        [Test]
        public void RE17_TickAllocation()
        {
            PlayerTestBinding.Logging = false;
            FeedbackScheduler s = Sched();
            Driver(Go("lo"), Item<PositionItem>(Osc(1f)), MotionDriverMode.Loop, s);
            Driver(Go("lw"), Item<PositionItem>(Wiggle()), MotionDriverMode.Loop, s);
            Driver(Go("ls"), Item<PositionItem>(Shake(Vector4.one)), MotionDriverMode.Loop, s);
            Driver(Go("fo"), Item<PositionItem>(Spring()), MotionDriverMode.Follow, s).SetTarget(new Vector4(5f, 1f, 0f, 0f));
            Driver(Go("fr"), Item<RotationItem>(Spring()), MotionDriverMode.Follow, s).SetTarget(new Vector4(0f, 90f, 0f, 0f));
            Tick(s, 5);
            int hits = 0;
            for (int i = 0; i < 5; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => s.Tick(0.05f))).IsSuccess)
                    hits++;
            }
            int positive = 0;
            var junk = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => junk.Add(new string('x', 3)))).IsSuccess)
                    positive++;
            }
            Check(hits < 5 && positive == 5, "the tick of five drivers allocates 5/5 · positive 5/5 — " + hits + "/" + positive);
            End();
        }
    }
}
