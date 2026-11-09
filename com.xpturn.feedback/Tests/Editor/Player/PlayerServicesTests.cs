using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // EditMode — the channel commands and their registry, the camera shake's paths, the time-scale stack, the hit stop, the
    // categories, the flash limit, the global center, the settings, the static APIs outside play, the surface. Players and
    // receivers are bound to test schedulers the row ticks directly — edit mode writes no time value, the service's computed
    // scale is read. A row checks everything and judges at the end. Warnings and errors are counted by a log listener; a
    // logged exception is expected with LogAssert. The rows that read Camera.main turn the open scene's main camera off and
    // make their own, each block removing its cameras
    public sealed class PlayerServicesTests
    {

        static readonly List<string> s_Warnings = new List<string>();
        static readonly List<string> s_Errors = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<Camera> m_Silenced = new List<Camera>();
        readonly List<string> m_Fail = new List<string>();
        ProjectSettingsCheck.State m_Time;
        float m_TimeScale;

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

        static void SetStatic(Type t, string field, object value) => t.GetField(field, BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, value);

        // the static APIs warn once a domain (development builds): each row starts with the marks down
        static void Begin()
        {
            foreach ((Type t, string f) in new[] { (typeof(FeedbackCategories), "s_Warned"), (typeof(FeedbackCategories), "s_WarnedArgument"), (typeof(FeedbackTimeScale), "s_WarnedContext"), (typeof(FeedbackTimeScale), "s_WarnedRange"), (typeof(FeedbackChannels), "s_Warned"), (typeof(FeedbackRange), "s_Warned") })
                SetStatic(t, f, false);
        }

        [SetUp]
        public void SetUp()
        {
            s_Warnings.Clear();
            s_Errors.Clear();
            m_Fail.Clear();
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerRuntime.ResetSession();
            Application.logMessageReceived += OnLog;
            m_Time = ProjectSettingsCheck.Take();
            m_TimeScale = Time.timeScale;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            // edit mode's destruction brings no OnDestroy — the one that bound releases first
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
            // a red row's time values (edit mode writes none): put them back and keep the settings file clean
            if (Time.timeScale != m_TimeScale)
                Time.timeScale = m_TimeScale;
            if (BitConverter.SingleToInt32Bits(Time.fixedDeltaTime) != m_Time.FixedDeltaBits)
                Time.fixedDeltaTime = BitConverter.Int32BitsToSingle(m_Time.FixedDeltaBits);
            if (BitConverter.SingleToInt32Bits(Time.maximumDeltaTime) != m_Time.MaximumDeltaBits)
                Time.maximumDeltaTime = BitConverter.Int32BitsToSingle(m_Time.MaximumDeltaBits);
            if (!m_Time.Dirty && ProjectSettingsCheck.TimeManagerDirty())
                EditorUtility.ClearDirty(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
            PlayerTestBinding.Reset();
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

        GameObject Go(string name, Vector3 at = default, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null)
                go.transform.SetParent(parent, false);
            go.transform.position = at;
            m_Made.Add(go);
            return go;
        }

        FeedbackPlayer PlayerOn(GameObject owner, FeedbackScheduler s, params FeedbackItem[] items)
        {
            var player = owner.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Items.AddRange(items);
            if (s != null)
                player.Bind(s);
            return player;
        }

        static PlayerTestAction Act(string label, float length, bool after = false, ItemClock clock = ItemClock.Player)
        {
            var action = new PlayerTestAction { m_Label = label, m_Length = length };
            action.m_Timing.m_Rule = after ? StartRule.AfterPrevious : StartRule.WithPrevious;
            action.m_Timing.m_Clock = clock;
            return action;
        }

        static HitStopItem Hit(float freeze, float minimum = 0.1f, float scale = 0f, bool after = false)
        {
            var hit = new HitStopItem { m_Label = "hit", m_FreezeDuration = freeze, m_MinimumTimeScale = minimum, m_TimeScale = scale };
            hit.m_Timing.m_Rule = after ? StartRule.AfterPrevious : StartRule.WithPrevious;
            return hit;
        }

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };
        static TargetRef OnChannel(int id) => new TargetRef { m_Mode = TargetRefMode.Channel, m_Channel = ChannelRef.Of(id), m_Path = "", m_Anchor = "" };
        static TargetRef MainCameraRef() => new TargetRef { m_Mode = TargetRefMode.MainCamera, m_Path = "", m_Anchor = "" };

        static T Item<T>(FeedbackMotion motion, TargetRef target, float duration, bool after = false) where T : TargetItem, new()
        {
            var item = new T { m_Motion = motion, m_Target = target };
            item.m_Timing.m_Duration = duration;
            item.m_Timing.m_Rule = after ? StartRule.AfterPrevious : StartRule.WithPrevious;
            return item;
        }

        static ShakeMotion Shake(Vector4 a, float frequency = 10f) => new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = a, m_Frequency = frequency } };
        static FlashMotion Flash(Vector4 target) => new FlashMotion { m_Params = new FlashParams { m_Target = target, m_Attack = 0.25f, m_Hold = 0.5f, m_Release = 0.25f } };
        static BlinkMotion Blink(Vector4 v, float period = 0.2f) => new BlinkMotion { m_Params = new BlinkParams { m_Value = v, m_Period = period, m_Duty = 0.5f, m_StartOn = false } };
        static TweenMotion Tween(Vector3 to) => new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.To, m_From = to, m_To = to, m_Ease = Ease.Of(EaseType.Linear), m_Loops = 1 } };

        Light LightAt(string name)
        {
            var light = Go(name).AddComponent<Light>();
            light.intensity = 1f;
            return light;
        }

        static LightIntensityItem LightFlash(Light light, float duration = 1f) => Item<LightIntensityItem>(Flash(new Vector4(5f, 0, 0, 0)), Direct(light), duration);

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

        GameObject Cam(string name)
        {
            GameObject go = Go(name);
            go.tag = "MainCamera";
            go.AddComponent<Camera>();
            return go;
        }

        static string Logs() => string.Join(" ", PlayerTestBinding.Log);

        static bool Began(string label) => PlayerTestBinding.Log.Any(l => l.StartsWith(label + ".Begin", StringComparison.Ordinal));

        static PlayerTestBinding Binding(string label) => PlayerTestBinding.Made.Last(b => b.Item.m_Label == label);

        sealed class Rx
        {
            internal FeedbackPlayer Player;
            internal FeedbackReceiver Receiver;
            internal readonly List<string> Events = new List<string>();
        }

        // a receiver on its player: a 0.5 action, a wait for a signal (5 s — the list stays seekable), then a 0.1 action
        Rx Receiving(FeedbackScheduler s, string name, int channel, Vector3 at = default, float range = 0f)
        {
            GameObject go = Go(name, at);
            FeedbackPlayer p = PlayerOn(go, s, Act(name + ".a", 0.5f), new WaitForSignalItem { m_Timeout = 5f }, Act(name + ".b", 0.1f, true));
            var r = go.AddComponent<FeedbackReceiver>();
            r.m_Channel = ChannelRef.Of(channel);
            r.m_Range = range;
            r.Bind(s);
            var rx = new Rx { Player = p, Receiver = r };
            p.Played += (pl, h) => rx.Events.Add("Played");
            p.Stopped += (pl, h) => rx.Events.Add("Stopped");
            p.Completed += (pl, h) => rx.Events.Add("Completed");
            p.Paused += pl => rx.Events.Add("Paused");
            p.Resumed += pl => rx.Events.Add("Resumed");
            return rx;
        }

        static FeedbackCommand Command(FeedbackCommandKind kind, int channel)
            => kind == FeedbackCommandKind.Seek ? FeedbackCommand.Seek(ChannelRef.Of(channel), 0.2f)
            : kind == FeedbackCommandKind.Play ? FeedbackCommand.Play(ChannelRef.Of(channel))
            : kind == FeedbackCommandKind.Stop ? FeedbackCommand.Stop(ChannelRef.Of(channel), StopMode.Revert)
            : new FeedbackCommand(kind, ChannelRef.Of(channel));

        // ---- the eight kinds reach channel A's receivers and only them

        [Test]
        public void VE01_Reg01_ChannelKinds()
        {
            Begin();
            FeedbackScheduler s = Sched();
            ChannelService ch = s.Context.Channels;
            Rx a1 = Receiving(s, "a1", 1), a2 = Receiving(s, "a2", 1), b = Receiving(s, "b", 2);
            b.Player.Play();
            s.Tick(0.05f);
            foreach (FeedbackCommandKind kind in (FeedbackCommandKind[])Enum.GetValues(typeof(FeedbackCommandKind)))
            {
                // A's state for the kind: stopped for Play · at its wait for Signal · paused for Resume · playing otherwise
                foreach (Rx a in new[] { a1, a2 })
                {
                    a.Player.Stop();
                    if (kind != FeedbackCommandKind.Play)
                        a.Player.Play();
                }
                s.Tick(kind == FeedbackCommandKind.Signal ? 0.6f : 0.05f);
                if (kind == FeedbackCommandKind.Resume)
                {
                    a1.Player.Pause();
                    a2.Player.Pause();
                }
                a1.Events.Clear();
                b.Events.Clear();
                PlayerTestBinding.Log.Clear();
                bool bPlaying = b.Player.IsPlaying;
                int taken = ch.Send(Command(kind, 1), false, default);
                Check(taken == 2, kind + " taken " + taken);
                Check(b.Events.Count == 0 && b.Player.IsPlaying == bPlaying && !PlayerTestBinding.Log.Any(l => l.StartsWith("b.", StringComparison.Ordinal)), kind + " reached B: " + string.Join(",", b.Events));
                if (kind == FeedbackCommandKind.Signal)
                    s.Tick(0.01f);
                string effect = string.Join(",", a1.Events);
                bool ok;
                switch (kind)
                {
                    case FeedbackCommandKind.Play: ok = effect.Contains("Played") && a1.Player.IsPlaying; break;
                    case FeedbackCommandKind.Stop: ok = effect.Contains("Stopped") && !a1.Player.IsPlaying; break;
                    case FeedbackCommandKind.Pause: ok = effect.Contains("Paused"); break;
                    case FeedbackCommandKind.Resume: ok = effect.Contains("Resumed"); break;
                    case FeedbackCommandKind.Seek: ok = Math.Abs(Binding("a1.a").LastTime.Seconds - 0.2) < 1e-4; break;
                    case FeedbackCommandKind.Skip: ok = effect.Contains("Completed") && !a1.Player.IsPlaying; break;
                    case FeedbackCommandKind.Restore: ok = effect.Contains("Stopped") && !a1.Player.IsPlaying; break;
                    default: ok = Began("a1.b"); break;
                }
                Check(ok, kind + " did not act on a1: " + effect);
            }
            End();
        }

        // ---- registration — cycles, the missing asset, the walk, a nested send, a throwing receiver, no player, the mode

        sealed class TestReceiver : ICommandReceiver
        {
            public Vector3 Position => Vector3.zero;
            public float Range => 0f;
            public int Taken;
            public bool Throws;

            public void Receive(in FeedbackCommand command, bool hasPosition, Vector3 position)
            {
                Taken++;
                if (Throws)
                    throw new InvalidOperationException("test receiver throws");
            }
        }

        [Test]
        public void VE02_Reg11_Registration()
        {
            Begin();
            FeedbackScheduler s = Sched();
            ChannelService ch = s.Context.Channels;
            Rx a = Receiving(s, "c1", 1);
            for (int i = 0; i < 100; i++)
            {
                a.Receiver.Bind(null);
                a.Receiver.Bind(s);
            }
            Check(ch.CommandReceivers(ChannelRef.Of(1)) == 1, "after 100 cycles " + ch.CommandReceivers(ChannelRef.Of(1)));
            GameObject none = Go("none");
            PlayerOn(none, s, Act("none.a", 0.1f));
            var nr = none.AddComponent<FeedbackReceiver>();
            nr.m_Channel = ChannelRef.Of((FeedbackChannel)null);
            nr.m_Channel.m_Mode = ChannelRefMode.Asset;
            nr.Bind(s);
            Check(!nr.IsRegistered, "a missing asset registered");
            // the walk: x1's Played unregisters x2 (after it) and registers x4
            Rx x1 = Receiving(s, "x1", 5), x2 = Receiving(s, "x2", 5), x3 = Receiving(s, "x3", 5);
            Rx x4 = null;
            x1.Player.Played += (pl, h) =>
            {
                x2.Receiver.Bind(null);
                if (x4 == null)
                    x4 = Receiving(s, "x4", 5);
            };
            int walked = ch.Send(FeedbackCommand.Play(ChannelRef.Of(5)), false, default);
            Check(walked == 2 && !x2.Player.IsPlaying && x3.Player.IsPlaying && x4 != null && !x4.Player.IsPlaying && ch.CommandReceivers(ChannelRef.Of(5)) == 3,
                "the walk — taken " + walked + " · x2 " + x2.Player.IsPlaying + " · x3 " + x3.Player.IsPlaying + " · x4 " + (x4 != null && x4.Player.IsPlaying) + " · count " + ch.CommandReceivers(ChannelRef.Of(5)));
            // a nested send from a handler
            Rx n1 = Receiving(s, "n1", 6), n2 = Receiving(s, "n2", 6);
            n1.Player.Played += (pl, h) => ch.Send(FeedbackCommand.Stop(ChannelRef.Of(6)), false, default);
            ch.Send(FeedbackCommand.Play(ChannelRef.Of(6)), false, default);
            Check(!n1.Player.IsPlaying && n2.Player.IsPlaying, "the nested Stop — n1 " + n1.Player.IsPlaying + " · n2 " + n2.Player.IsPlaying);
            // a throwing receiver first, then one that takes
            var t1 = new TestReceiver { Throws = true };
            var t2 = new TestReceiver();
            var key = new ChannelKey(ChannelRef.Of(9));
            ch.Register(t1, in key);
            ch.Register(t2, in key);
            int errors = s_Errors.Count;
            LogAssert.Expect(LogType.Exception, new Regex("test receiver throws"));
            int taken = ch.Send(new FeedbackCommand(FeedbackCommandKind.Stop, ChannelRef.Of(9)), false, default);
            Check(t2.Taken == 1 && s_Errors.Count - errors == 1, "a throwing receiver — the next took " + t2.Taken + " · logged " + (s_Errors.Count - errors) + " · taken " + taken);
            // a receiver with no player
            int w = s_Warnings.Count;
            var lone = Go("lone").AddComponent<FeedbackReceiver>();
            lone.m_Channel = ChannelRef.Of(7);
            lone.Bind(s);
            lone.Bind(null);
            lone.Bind(s);
            Check(!lone.IsRegistered && s_Warnings.Skip(w).Count(x => x == FeedbackReceiver.NoPlayerWarning) == 1, "no player — registered " + lone.IsRegistered + " · warnings " + s_Warnings.Skip(w).Count(x => x == FeedbackReceiver.NoPlayerWarning));
            // the mode: a missing asset sent does not reach channel 0
            Rx zero = Receiving(s, "z0", 0);
            var missing = new ChannelRef { m_Mode = ChannelRefMode.Asset };
            int toMissing = ch.Send(new FeedbackCommand(FeedbackCommandKind.Play, missing), false, default);
            Check(toMissing == 0 && !zero.Player.IsPlaying, "a missing asset reached channel 0 — taken " + toMissing);
            End();
        }

        // ---- the camera shake's one path — an endless shake stops both slots, a cooldown skips both

        [Test]
        public void VE03_Reg06_Reg19_CameraOnePath()
        {
            Begin();
            foreach (bool channel in new[] { false, true })
            {
                FeedbackScheduler s = Sched();
                GameObject cam = Go("cam" + channel);
                CameraShakeReceiver r = null;
                if (channel)
                {
                    r = cam.AddComponent<CameraShakeReceiver>();
                    r.m_Channel = ChannelRef.Of(1);
                    r.Bind(s);
                }
                var item = Item<CameraShakeItem>(Shake(new Vector4(1f, 1f, 0f, 0f)), channel ? OnChannel(1) : Direct(cam.transform), 0.5f);
                item.m_Timing.m_Infinite = true;
                item.m_RotationAmplitude = new Vector3(0f, 0f, 5f);
                item.m_Conditions.m_Cooldown = 10f;
                FeedbackPlayer p = PlayerOn(Go("p" + channel), s, item);
                p.Play();
                for (int k = 0; k < 20; k++)
                    s.Tick(0.1f);
                var b = (CameraShakeBinding)p.Runtime.Binding(0);
                LocalPositionSlot ps = channel ? r.PositionSlot : b.PositionSlot;
                LocalRotationSlot rs = channel ? r.RotationSlot : b.RotationSlot;
                Check(p.IsPlaying && ps.LiveContributions == 1 && rs != null && rs.LiveContributions == 1, (channel ? "channel" : "direct") + " — endless after 2 s: playing " + p.IsPlaying + " · live " + ps.LiveContributions + "/" + (rs != null ? rs.LiveContributions : -1));
                p.Stop();
                s.Tick(0.1f);
                Check(ps.LiveContributions == 0 && (rs == null || rs.LiveContributions == 0), (channel ? "channel" : "direct") + " — after Stop live " + ps.LiveContributions + "/" + (rs != null ? rs.LiveContributions : -1));
                p.Play();
                s.Tick(0.1f);
                Check(ps.LiveContributions == 0 && (rs == null || rs.LiveContributions == 0), (channel ? "channel" : "direct") + " — in the cooldown live " + ps.LiveContributions + "/" + (rs != null ? rs.LiveContributions : -1));
            }
            End();
        }

        // ---- the stack

        [Test]
        public void VE04_Stack()
        {
            Begin();
            float timeScale = Time.timeScale;
            bool dirty = ProjectSettingsCheck.TimeManagerDirty();
            FeedbackScheduler s = Sched();
            TimeScaleService ts = s.Context.TimeScale;
            // a request between ticks is not shortened by the first Update it meets
            ts.Request(0.5f, 0.3);
            s.Tick(0.1f);
            Check(ts.Count == 1 && Math.Abs(ts.RemainingAt(0) - 0.3) < 1e-6, "between ticks — remaining " + (ts.Count > 0 ? F(ts.RemainingAt(0)) : "none"));
            s.Tick(0.1f);
            Check(ts.Count == 1 && Math.Abs(ts.RemainingAt(0) - 0.2) < 1e-6, "then it ages — " + (ts.Count > 0 ? F(ts.RemainingAt(0)) : "none"));
            // after this frame's Revert: not shortened by this Update
            s.RunRevert();
            ts.Request(0f, 0.15);
            s.RunUpdate(new FrameTime(0.1f, 0.1f));
            s.RunLateApply();
            Check(ts.Count == 2 && Math.Abs(ts.RemainingAt(1) - 0.15) < 1e-6, "after the Revert — " + (ts.Count > 1 ? F(ts.RemainingAt(1)) : "none"));
            Check(ts.Count == 2 && Math.Abs(ts.RemainingAt(0) - 0.1) < 1e-6, "the lower one ages under the top — " + (ts.Count > 0 ? F(ts.RemainingAt(0)) : "none"));
            Check(ts.Effective == 0f, "the top wins — " + F(ts.Effective));
            s.Tick(0.1f);
            Check(ts.Count == 1 && Math.Abs(ts.RemainingAt(0) - 0.05) < 1e-6, "the lower one gone, the top ages — count " + ts.Count + " · " + (ts.Count > 0 ? F(ts.RemainingAt(0)) : "none"));
            s.Tick(0.1f);
            Check(ts.Count == 0 && ts.Effective == 1f, "both gone — count " + ts.Count);
            // the base × the top
            ts.SetBaseScale(0.5f);
            ts.Request(0.2f, 1.0);
            Check(Math.Abs(ts.Effective - 0.1f) < 1e-6, "base 0.5 × top 0.2 — " + F(ts.Effective));
            // the effective scale stays in 0 ~ 100
            ts.SetBaseScale(10f);
            ts.Request(20f, 1.0);
            Check(ts.Effective == 100f, "base 10 × 20 — " + F(ts.Effective));
            ts.SetBaseScale(1f);
            // the stack full: the oldest goes, one warning
            int w = s_Warnings.Count;
            for (int i = 0; i < 9; i++)
                ts.Request(0.01f * (i + 1), 5.0);
            Check(ts.Count == 8 && Math.Abs(ts.ScaleAt(7) - 0.09f) < 1e-6 && s_Warnings.Skip(w).Count(x => x == TimeScaleService.FullWarning) == 1, "full — count " + ts.Count + " · top " + F(ts.Count > 0 ? ts.ScaleAt(ts.Count - 1) : -1) + " · warnings " + s_Warnings.Skip(w).Count(x => x == TimeScaleService.FullWarning));
            // out of 0 ~ 100: clamped, one warning
            w = s_Warnings.Count;
            ts.Request(200f, 1.0);
            ts.Request(-1f, 1.0);
            bool two = ts.Count >= 2;
            Check(two && ts.ScaleAt(ts.Count - 2) == 100f && ts.ScaleAt(ts.Count - 1) == 0f && s_Warnings.Skip(w).Count(x => x == TimeScaleService.RangeWarning) == 1, "clamp — " + (two ? F(ts.ScaleAt(ts.Count - 2)) + " · " + F(ts.ScaleAt(ts.Count - 1)) : "count " + ts.Count));
            // edit mode writes nothing
            Check(Time.timeScale == timeScale && ProjectSettingsCheck.TimeManagerDirty() == dirty, "edit mode — Time.timeScale " + F(Time.timeScale) + " · dirty " + ProjectSettingsCheck.TimeManagerDirty());
            s.Shutdown();
            Check(!ts.Request(0f, 1.0).IsValid && ts.Count == 0, "after Shutdown — count " + ts.Count);
            End();
        }

        // ---- the hit stop

        [Test]
        public void VE05_HitStop()
        {
            Begin();
            foreach (float dt in new[] { 0.025f, 0.03f })
            {
                FeedbackScheduler s = Sched();
                TimeScaleService ts = s.Context.TimeScale;
                FeedbackPlayer p = PlayerOn(Go("p"), s, Hit(0.1f), Act("after" + dt, 0.1f, true));
                p.Play();
                bool atPlay = ts.Count == 1 && Math.Abs(ts.RemainingAt(0) - 0.1) < 1e-6;
                var trace = new List<string>();
                int began = -1;
                for (int k = 0; k < 6; k++)
                {
                    PlayerTestBinding.Log.Clear();
                    s.Tick(dt);
                    trace.Add(ts.Count.ToString());
                    if (began < 0 && Began("after" + dt))
                        began = k + 1;
                }
                Check(atPlay && string.Join("", trace) == "111000" && began == 4, "dt " + F(dt) + " — at the Play " + atPlay + " · per tick " + string.Join("", trace) + " · after began at tick " + began);
            }
            {
                FeedbackScheduler s = Sched();
                TimeScaleService ts = s.Context.TimeScale;
                FeedbackPlayer fast = PlayerOn(Go("fast"), s, Hit(0.1f));
                fast.m_Speed = 2f;
                fast.Play();
                Check(ts.Count == 1 && Math.Abs(ts.RemainingAt(0) - 0.05) < 1e-6, "Speed 2 — " + (ts.Count > 0 ? F(ts.RemainingAt(0)) : "none"));
                fast.Stop();
                PlayerOn(Go("inst"), s, Hit(0f)).Play();
                Check(ts.Count == 0, "an instant — requests " + ts.Count);
                FeedbackPlayer back = PlayerOn(Go("back"), s, Hit(0.1f), Act("tail", 0.1f, true));
                back.Play(new PlayArgs().WithDirection(PlayerDirection.Backward));
                int most = ts.Count;
                for (int k = 0; k < 10; k++)
                {
                    s.Tick(0.025f);
                    most = Math.Max(most, ts.Count);
                }
                Check(most == 0, "backward — requests at most " + most);
                ts.SetBaseScale(0.05f);
                PlayerOn(Go("low"), s, Hit(0.1f)).Play();
                Check(ts.Count == 0, "base 0.05 · minimum 0.1 — requests " + ts.Count);
                ts.SetBaseScale(1f);
                s.Context.Categories.SetEnabled(FeedbackCategory.Time, false);
                PlayerOn(Go("off"), s, Hit(0.1f)).Play();
                Check(ts.Count == 0, "Time off — requests " + ts.Count);
                s.Context.Categories.SetEnabled(FeedbackCategory.Time, true);
                foreach (StopMode mode in new[] { StopMode.Hold, StopMode.Revert, StopMode.Complete })
                {
                    FeedbackPlayer st = PlayerOn(Go("st" + mode), s, Hit(0.5f));
                    st.Play();
                    st.Stop(mode);
                    Check(ts.Count == 0, "Stop(" + mode + ") — requests " + ts.Count);
                }
                FeedbackPlayer sk = PlayerOn(Go("sk"), s, Hit(0.5f), Act("sk.t", 0.1f, true));
                sk.Play();
                s.Tick(0.02f);
                sk.Skip();
                Check(ts.Count == 0, "Skip — requests " + ts.Count);
                FeedbackPlayer rs = PlayerOn(Go("rs"), s, Hit(0.5f));
                rs.Play();
                rs.Restore();
                Check(ts.Count == 0, "Restore — requests " + ts.Count);
                FeedbackPlayer pa = PlayerOn(Go("pa"), s, Hit(0.2f));
                pa.Play();
                s.Tick(0.05f);
                pa.Pause();
                for (int k = 0; k < 6; k++)
                    s.Tick(0.05f);
                Check(ts.Count == 0 && pa.IsPlaying, "a paused play's request ages — requests " + ts.Count);
                pa.Stop();
                // no end, no request — one warning
                int w = s_Warnings.Count;
                FeedbackPlayer zero = PlayerOn(Go("zero"), s, Hit(0.1f));
                zero.m_Speed = 0f;
                zero.Play();
                zero.Stop();
                zero.Play();
                Check(ts.Count == 0 && s_Warnings.Skip(w).Count(x => x == HitStopBinding.EndlessWarning) == 1, "Speed 0 — requests " + ts.Count + " · warnings " + s_Warnings.Skip(w).Count(x => x == HitStopBinding.EndlessWarning));
                zero.Stop();
                // the Time category's scale does not touch the freeze
                s.Context.Categories.SetScale(FeedbackCategory.Time, 0.5f);
                FeedbackPlayer half = PlayerOn(Go("half"), s, Hit(0.1f));
                half.Play();
                Check(ts.Count == 1 && Math.Abs(ts.RemainingAt(0) - 0.1) < 1e-6, "Time scale 0.5 — " + (ts.Count > 0 ? F(ts.RemainingAt(0)) : "none"));
                half.Stop();
                s.Context.Categories.SetScale(FeedbackCategory.Time, 1f);
                // two in a frame: the second is refused (edit mode reads the computed scale) and keeps its place
                FeedbackPlayer two1 = PlayerOn(Go("two1"), s, Hit(0.1f));
                FeedbackPlayer two2 = PlayerOn(Go("two2"), s, Hit(0.2f), Act("two2.t", 0.05f, true));
                two1.Play();
                two2.Play();
                int both = ts.Count;
                int place = -1;
                for (int k = 0; k < 12; k++)
                {
                    PlayerTestBinding.Log.Clear();
                    s.Tick(0.025f);
                    if (place < 0 && Began("two2.t"))
                        place = k + 1;
                }
                Check(both == 1 && place == 8, "two in a frame — requests " + both + " · the refused one's next began at tick " + place);
            }
            End();
        }

        // ---- the distance · the broadcast Signal · the send's position

        [Test]
        public void VE06_DistanceSignalPosition()
        {
            Begin();
            FeedbackScheduler s = Sched();
            ChannelService ch = s.Context.Channels;
            Rx far = Receiving(s, "rg", 3, Vector3.zero, 5f);
            int near = ch.Send(FeedbackCommand.Play(ChannelRef.Of(3)), true, new Vector3(3f, 0f, 0f));
            far.Player.Stop();
            int away = ch.Send(FeedbackCommand.Play(ChannelRef.Of(3)), true, new Vector3(10f, 0f, 0f));
            far.Player.Stop();
            int any = ch.Send(FeedbackCommand.Play(ChannelRef.Of(3)), false, default);
            far.Player.Stop();
            Check(near == 1 && away == 0 && any == 1, "range 5 — at 3 " + near + " · at 10 " + away + " · no position " + any);
            Rx always = Receiving(s, "al", 4, new Vector3(100f, 0f, 0f), 0f);
            Check(ch.Send(FeedbackCommand.Play(ChannelRef.Of(4)), true, Vector3.zero) == 1, "range 0 takes from anywhere");
            always.Player.Stop();
            // the broadcast Signal is quiet; a direct one warns once
            Rx q = Receiving(s, "q", 8);
            int w = s_Warnings.Count;
            ch.Send(new FeedbackCommand(FeedbackCommandKind.Signal, ChannelRef.Of(8)), false, default);
            int quiet = s_Warnings.Count - w;
            q.Player.Signal();
            Check(quiet == 0 && s_Warnings.Count - w == 1, "Signal with no wait — broadcast " + quiet + " · direct " + (s_Warnings.Count - w - quiet));
            q.Player.Play();
            s.Tick(0.6f);
            PlayerTestBinding.Log.Clear();
            ch.Send(new FeedbackCommand(FeedbackCommandKind.Signal, ChannelRef.Of(8)), false, default);
            s.Tick(0.01f);
            Check(Began("q.b"), "the broadcast Signal did not end the wait");
            // the send's position reaches the receiving player's gate
            Silence();
            Cam("gatecam");
            GameObject gr = Go("gr");
            FeedbackPlayer gp = PlayerOn(gr, s, Act("g", 0.5f));
            gp.m_UseDistanceGate = true;
            gp.m_GateReference = DistanceReference.MainCamera;
            gp.m_GateDistance = 5f;
            var grx = gr.AddComponent<FeedbackReceiver>();
            grx.m_Channel = ChannelRef.Of(11);
            grx.Bind(s);
            ch.Send(FeedbackCommand.Play(ChannelRef.Of(11)), true, new Vector3(100f, 0f, 0f));
            bool farPlay = gp.IsPlaying;
            ch.Send(FeedbackCommand.Play(ChannelRef.Of(11), new PlayArgs().WithPosition(Vector3.zero)), true, new Vector3(100f, 0f, 0f));
            Check(!farPlay && gp.IsPlaying, "the send's position — played at 100 " + farPlay + " · the args' own position wins " + gp.IsPlaying);
            End();
        }

        // ---- the camera shake on a transform

        [Test]
        public void VE07_CameraDirect()
        {
            Begin();
            {
                FeedbackScheduler s = Sched();
                GameObject rig = Go("rig");
                rig.transform.localScale = new Vector3(2f, 2f, 2f);
                GameObject cam = Go("cam", default, rig.transform);
                var item = Item<CameraShakeItem>(Shake(new Vector4(1f, 0f, 0f, 0f)), Direct(cam.transform), 1f);
                item.m_RotationAmplitude = new Vector3(0f, 0f, 10f);
                FeedbackPlayer p = PlayerOn(Go("p"), s, item);
                p.Play(new PlayArgs().WithSeed(3));
                var b = (CameraShakeBinding)p.Runtime.Binding(0);
                double maxWorld = 0, maxZ = 0, maxX = 0;
                bool halved = true;
                for (int k = 0; k < 12; k++)
                {
                    s.Tick(0.037f);
                    Vector3 lp = cam.transform.localPosition;
                    Vector3 e = cam.transform.localEulerAngles;
                    maxWorld = Math.Max(maxWorld, Math.Abs(cam.transform.position.x));
                    halved &= Math.Abs(cam.transform.position.x - 2f * lp.x) < 1e-4;
                    maxZ = Math.Max(maxZ, Math.Abs(Mathf.DeltaAngle(0f, e.z)));
                    maxX = Math.Max(maxX, Math.Abs(Mathf.DeltaAngle(0f, e.x)));
                }
                Check(halved && maxWorld > 0.05 && maxWorld <= 1.0001, "Self — world x within the amplitude: max " + F(maxWorld));
                Check(maxZ > 0.5 && maxZ <= 10.01 && maxX < 0.01, "the rotation copy — max z " + F(maxZ) + " · max x " + F(maxX));
                uint pos = b.PositionSeed(0), rot = b.RotationSeed(0);
                Check(rot != pos && rot != Hash32.Tagged(pos, Hash32.Component, 1u), "seeds — " + pos.ToString("X8") + " · " + rot.ToString("X8"));
                Check(rot == Hash32.Tagged(pos, Hash32.Copy, 1u), "the copy's seed is the copy tag's");
                p.Stop();
                p.Play(new PlayArgs().WithSeed(3));
                Check(b.PositionSeed(0) == pos && b.RotationSeed(0) == rot, "the same seed and occurrence give the same seeds");
                p.Stop();
            }
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer flat = PlayerOn(Go("flat"), s, Item<CameraShakeItem>(Shake(new Vector4(1f, 0f, 0f, 0f)), Direct(Go("cam2").transform), 1f));
                flat.Play();
                var bf = (CameraShakeBinding)flat.Runtime.Binding(0);
                Check(!bf.HasRotation && bf.RotationSlot == null, "no rotation amplitude — copy " + bf.HasRotation + " · slot " + (bf.RotationSlot != null));
                var direct = Item<CameraShakeItem>(Shake(new Vector4(1f, 0f, 0f, 0f)), Direct(Go("cam3").transform), 1f);
                direct.m_RotationAmplitude = new Vector3(5f, 0f, 0f);
                direct.m_DirectWrite = true;
                FeedbackPlayer pd = PlayerOn(Go("pd"), s, direct);
                pd.Play();
                var bd = (CameraShakeBinding)pd.Runtime.Binding(0);
                Check(bd.RotationSlot != null && ReferenceEquals(bd.PositionSlot.SelfPair, bd.RotationSlot), "the direct pair");
                // a Curve's rotation copy maps 0 → the amplitude
                var curve = new CurveMotion { m_Params = new CurveParams { m_Curve = AnimationCurve.Linear(0f, 0f, 1f, 1f), m_RemapZero = Vector4.zero, m_RemapOne = new Vector4(1f, 0f, 0f, 0f) } };
                var ci = Item<CameraShakeItem>(curve, Direct(Go("cam4").transform), 1f);
                ci.m_RotationAmplitude = new Vector3(0f, 0f, 10f);
                GameObject cc = ((Transform)ci.m_Target.m_Object).gameObject;
                FeedbackPlayer pc = PlayerOn(Go("pc"), s, ci);
                pc.Play();
                for (int k = 0; k < 9; k++)
                    s.Tick(0.1f);
                Check(Math.Abs(Mathf.DeltaAngle(0f, cc.transform.localEulerAngles.z) - 9f) < 0.2f && Math.Abs(cc.transform.localPosition.x - 0.9f) < 0.02f, "the Curve copy at 0.9 — z " + F(Mathf.DeltaAngle(0f, cc.transform.localEulerAngles.z)) + " · x " + F(cc.transform.localPosition.x));
                // the Camera category's scale on both
                GameObject c5 = Go("cam5");
                var i5 = Item<CameraShakeItem>(Shake(new Vector4(1f, 1f, 1f, 0f)), Direct(c5.transform), 1f);
                i5.m_RotationAmplitude = new Vector3(0f, 0f, 10f);
                FeedbackPlayer p5 = PlayerOn(Go("p5"), s, i5);
                p5.Play(new PlayArgs().WithSeed(5));
                s.Tick(0.11f);
                Vector3 full = c5.transform.localPosition;
                float fullZ = Mathf.DeltaAngle(0f, c5.transform.localEulerAngles.z);
                p5.Stop();
                s.Tick(0.01f);
                s.Context.Categories.SetScale(FeedbackCategory.Camera, 0.5f);
                p5.Play(new PlayArgs().WithSeed(5));
                s.Tick(0.11f);
                Check(Vector3.Distance(c5.transform.localPosition, full * 0.5f) < 1e-4 && Math.Abs(Mathf.DeltaAngle(0f, c5.transform.localEulerAngles.z) - fullZ * 0.5f) < 0.01f, "the Camera scale 0.5 — " + c5.transform.localPosition.x + " vs " + full.x * 0.5f);
            }
            {
                // the main camera: each block makes and removes its cameras
                Silence();
                FeedbackScheduler s = Sched();
                GameObject camA = Cam("camA");
                FeedbackPlayer p = PlayerOn(Go("mp"), s, Item<CameraShakeItem>(Shake(new Vector4(1f, 0f, 0f, 0f)), MainCameraRef(), 0.3f));
                p.Play();
                s.Tick(0.05f);
                bool movedA = camA.transform.localPosition != Vector3.zero;
                for (int k = 0; k < 8; k++)
                    s.Tick(0.05f);
                camA.tag = "Untagged";
                GameObject camB = Cam("camB");
                p.Play();
                s.Tick(0.05f);
                Check(movedA && camB.transform.localPosition != Vector3.zero && camA.transform.localPosition == Vector3.zero, "the main camera's switch — A " + movedA + " · B " + (camB.transform.localPosition != Vector3.zero));
                Object.DestroyImmediate(camA);
                Object.DestroyImmediate(camB);
            }
            End();
        }

        // ---- the camera shake on a channel

        [Test]
        public void VE08_CameraChannel()
        {
            Begin();
            {
                FeedbackScheduler s = Sched();
                var cams = new List<CameraShakeReceiver>();
                for (int i = 0; i < 5; i++)
                {
                    var r = Go("rc" + i, new Vector3(i, 0f, 0f)).AddComponent<CameraShakeReceiver>();
                    r.m_Channel = ChannelRef.Of(1);
                    r.Bind(s);
                    cams.Add(r);
                }
                int w = s_Warnings.Count;
                FeedbackPlayer p = PlayerOn(Go("p"), s, Item<CameraShakeItem>(Shake(new Vector4(1f, 0f, 0f, 0f)), OnChannel(1), 1f));
                p.Play();
                var b = (CameraShakeBinding)p.Runtime.Binding(0);
                s.Tick(0.05f);
                int moved = cams.Count(c => c.PositionSlot.LiveContributions == 1);
                Check(b.Reached(0) == 4 && moved == 4 && cams[4].PositionSlot.LiveContributions == 0 && s_Warnings.Skip(w).Count(x => x == CameraShakeBinding.TooManyWarning) == 1, "M 4 · five — reached " + b.Reached(0) + " · live " + moved + " · warnings " + s_Warnings.Skip(w).Count(x => x == CameraShakeBinding.TooManyWarning));
                int e = s_Errors.Count;
                cams[1].Bind(null);
                s.Tick(0.05f);
                Check(s_Errors.Count == e && cams[1].transform.localPosition.x == 1f, "a receiver gone mid-play — errors " + (s_Errors.Count - e) + " · x " + F(cams[1].transform.localPosition.x));
                p.Stop();
                // one enabled after the Begin waits for the next play
                p.Play();
                var late = Go("late", new Vector3(9f, 0f, 0f)).AddComponent<CameraShakeReceiver>();
                late.m_Channel = ChannelRef.Of(1);
                late.Bind(s);
                s.Tick(0.05f);
                bool lateNow = late.PositionSlot.LiveContributions > 0;
                p.Stop();
                Check(!lateNow, "a receiver enabled after the Begin took this play");
            }
            {
                FeedbackScheduler s = Sched();
                var fr = Go("far", Vector3.zero).AddComponent<CameraShakeReceiver>();
                fr.m_Channel = ChannelRef.Of(2);
                fr.m_Range = 5f;
                fr.Bind(s);
                FeedbackPlayer p2 = PlayerOn(Go("p2", new Vector3(100f, 0f, 0f)), s, Item<CameraShakeItem>(Shake(new Vector4(1f, 0f, 0f, 0f)), OnChannel(2), 1f));
                p2.Play(new PlayArgs().WithPosition(new Vector3(3f, 0f, 0f)));
                var b2 = (CameraShakeBinding)p2.Runtime.Binding(0);
                int nearR = b2.Reached(0);
                p2.Stop();
                p2.Play(new PlayArgs().WithPosition(new Vector3(10f, 0f, 0f)));
                int farR = b2.Reached(0);
                p2.Stop();
                p2.Play();
                int selfR = b2.Reached(0);
                p2.Stop();
                Check(nearR == 1 && farR == 0 && selfR == 0, "range 5 — play at 3 " + nearR + " · at 10 " + farR + " · no position (player at 100) " + selfR);
                int w = s_Warnings.Count;
                var direct = Item<CameraShakeItem>(Shake(new Vector4(1f, 0f, 0f, 0f)), OnChannel(2), 1f);
                direct.m_DirectWrite = true;
                FeedbackPlayer pd = PlayerOn(Go("pd", Vector3.zero), s, direct);
                pd.Play();
                s.Tick(0.05f);
                Check(s_Warnings.Skip(w).Count(x => x == CameraShakeBinding.DirectChannelWarning) == 1 && fr.PositionSlot.LiveContributions == 1, "a direct-write mark on a channel — warnings " + s_Warnings.Skip(w).Count(x => x == CameraShakeBinding.DirectChannelWarning) + " · the shared slot " + fr.PositionSlot.LiveContributions);
                pd.Stop();
                w = s_Warnings.Count;
                FeedbackPlayer pp = PlayerOn(Go("pp"), s, Item<PositionItem>(Shake(new Vector4(1f, 0f, 0f, 0f)), OnChannel(2), 1f));
                pp.EnsureRuntime();
                Check(pp.Runtime.Binding(0) == null && s_Warnings.Count - w == 1, "a position item on a channel — binding " + (pp.Runtime.Binding(0) != null) + " · warnings " + (s_Warnings.Count - w));
            }
            {
                // the receiver goes while another binding holds its slot
                FeedbackScheduler s = Sched();
                GameObject cam = Go("held");
                var r = cam.AddComponent<CameraShakeReceiver>();
                r.m_Channel = ChannelRef.Of(3);
                r.Bind(s);
                PlayerOn(Go("holder"), s, Item<PositionItem>(Shake(new Vector4(0f, 0f, 0f, 0f)), Direct(cam.transform), 1f)).EnsureRuntime();
                FeedbackPlayer p = PlayerOn(Go("hp"), s, Item<CameraShakeItem>(Shake(new Vector4(1f, 0f, 0f, 0f)), OnChannel(3), 1f));
                p.Play();
                s.Tick(0.05f);
                r.Bind(null);
                s.Tick(0.05f);
                s.Tick(0.05f);
                Check(cam.transform.localPosition.x == 0f, "the gone receiver's camera at " + F(cam.transform.localPosition.x));
            }
            {
                // the player is gone before a delayed shake — from the Play's place
                FeedbackScheduler s = Sched();
                var near = Go("nearc", new Vector3(50f, 0f, 0f)).AddComponent<CameraShakeReceiver>();
                near.m_Channel = ChannelRef.Of(4);
                near.m_Range = 5f;
                near.Bind(s);
                var origin = Go("originc", Vector3.zero).AddComponent<CameraShakeReceiver>();
                origin.m_Channel = ChannelRef.Of(4);
                origin.m_Range = 5f;
                origin.Bind(s);
                GameObject owner = Go("owner", new Vector3(50f, 0f, 0f));
                FeedbackPlayer p = PlayerOn(owner, s, Act("wait", 0.05f), Item<CameraShakeItem>(Shake(new Vector4(1f, 0f, 0f, 0f)), OnChannel(4), 0.3f, true));
                p.Play();
                Object.DestroyImmediate(owner);
                for (int k = 0; k < 3; k++)
                    s.Tick(0.03f);
                Check(near.PositionSlot.LiveContributions == 1 && origin.PositionSlot.LiveContributions == 0, "a gone player — near " + near.PositionSlot.LiveContributions + " · origin " + origin.PositionSlot.LiveContributions);
            }
            {
                // short repeats in one pass hand the contribution over (C 1 — no growth, no warning)
                FeedbackScheduler s = Sched(new SettingsValues { ReceiverContributions = 1 });
                var r = Go("rep").AddComponent<CameraShakeReceiver>();
                r.m_Channel = ChannelRef.Of(5);
                r.Bind(s);
                var rep = Item<CameraShakeItem>(Shake(new Vector4(1f, 0f, 0f, 0f)), OnChannel(5), 0.02f);
                rep.m_Timing.m_RepeatCount = 3;
                FeedbackPlayer p = PlayerOn(Go("rp"), s, rep);
                p.Play();
                int w = s_Warnings.Count;
                s.Tick(0.1f);
                s.Tick(0.1f);
                Check(r.PositionSlot.GrowthCount == 0 && s_Warnings.Count == w, "short repeats in a pass — growth " + r.PositionSlot.GrowthCount + " · warnings " + (s_Warnings.Count - w));
            }
            End();
        }

        // ---- the categories

        [Test]
        public void VE09_Categories()
        {
            Begin();
            FeedbackScheduler s = Sched();
            CategoryGate gate = s.Context.Categories;
            Light l1 = LightAt("l1");
            FeedbackPlayer p1 = PlayerOn(Go("p1"), s, LightFlash(l1));
            p1.Play();
            s.Tick(0.4f);
            float full = l1.intensity;
            gate.SetScale(FeedbackCategory.Flash, 0.5f);
            s.Tick(0.01f);
            float half = l1.intensity;
            gate.SetEnabled(FeedbackCategory.Flash, false);
            s.Tick(0.01f);
            float off = l1.intensity;
            Check(F(full) == "5" && F(half) == "3" && F(off) == "1", "a running Flash — " + F(full) + " → " + F(half) + " → " + F(off));
            gate.SetEnabled(FeedbackCategory.Flash, true);
            Light l2 = LightAt("l2");
            FeedbackPlayer p2 = PlayerOn(Go("p2"), s, LightFlash(l2));
            p2.Play(new PlayArgs().WithIntensity(2f));
            s.Tick(0.4f);
            Check(F(l2.intensity) == "3", "intensity 2 · scale 0.5 — " + F(l2.intensity));
            gate.SetEnabled(FeedbackCategory.Flash, false);
            Light l3 = LightAt("l3");
            FeedbackPlayer p3 = PlayerOn(Go("p3"), s, LightFlash(l3), Act("after", 0.1f, true));
            PlayerTestBinding.Log.Clear();
            p3.Play();
            int began = -1;
            for (int k = 0; k < 12; k++)
            {
                s.Tick(0.1f);
                if (began < 0 && Began("after"))
                    began = k + 1;
            }
            Check(F(l3.intensity) == "1" && began == 10, "off at the Play — light " + F(l3.intensity) + " · the next item at tick " + began);
            gate.SetEnabled(FeedbackCategory.Flash, true);
            gate.SetScale(FeedbackCategory.Flash, 1f);
            // an offset halves, a Persist goes on
            GameObject t1 = Go("t1");
            FeedbackPlayer shaker = PlayerOn(Go("sh"), s, Item<PositionItem>(Shake(new Vector4(1f, 1f, 1f, 0f)), Direct(t1.transform), 1f));
            shaker.Play(new PlayArgs().WithSeed(7));
            s.Tick(0.13f);
            Vector3 a = t1.transform.position;
            shaker.Stop();
            s.Tick(0.01f);
            gate.SetScale(FeedbackCategory.Motion, 0.5f);
            shaker.Play(new PlayArgs().WithSeed(7));
            s.Tick(0.13f);
            Check(Vector3.Distance(t1.transform.position, a * 0.5f) < 1e-4, "a Shake offset at 0.5 — " + t1.transform.position + " vs " + a);
            shaker.Stop();
            gate.SetScale(FeedbackCategory.Motion, 0f);
            GameObject t2 = Go("t2");
            FeedbackPlayer mover = PlayerOn(Go("mv"), s, Item<PositionItem>(Tween(new Vector3(10f, 0, 0)), Direct(t2.transform), 0.2f));
            mover.Play();
            for (int k = 0; k < 3; k++)
                s.Tick(0.1f);
            Check(t2.transform.position.x == 10f, "a Persist at Motion 0 — " + F(t2.transform.position.x));
            gate.SetScale(FeedbackCategory.Motion, 1f);
            // another context keeps its own state
            FeedbackScheduler other = Sched();
            gate.SetEnabled(FeedbackCategory.Camera, false);
            Check(!gate.IsEnabled(FeedbackCategory.Camera) && other.Context.Categories.IsEnabled(FeedbackCategory.Camera), "contexts — the other's Camera " + other.Context.Categories.IsEnabled(FeedbackCategory.Camera));
            gate.SetEnabled(FeedbackCategory.Camera, true);
            // None stays out of the gate
            gate.SetEnabled(FeedbackCategory.None, false);
            FeedbackPlayer pn = PlayerOn(Go("pn"), s, Act("none", 0.1f));
            PlayerTestBinding.Log.Clear();
            pn.Play();
            Check(Began("none"), "None — the item was skipped");
            gate.SetEnabled(FeedbackCategory.None, true);
            // a Blink's weight takes its category's scale (a flashing SetActive counts as Flash)
            GameObject blinker = Go("blinker");
            gate.SetScale(FeedbackCategory.Flash, 0f);
            FeedbackPlayer bp = PlayerOn(Go("bp"), s, Item<SetActiveItem>(Blink(new Vector4(0f, 0, 0, 0)), Direct(blinker), 1f));
            bp.Play();
            bool stayed = true;
            for (int k = 0; k < 8; k++)
            {
                s.Tick(0.05f);
                stayed &= blinker.activeSelf;
            }
            Check(stayed, "a Blink at Flash 0 — it went off");
            bp.Stop();
            gate.SetScale(FeedbackCategory.Flash, 1f);
            End();
        }

        // ---- the flash limit

        [Test]
        public void VE10_FlashLimit()
        {
            Begin();
            {
                FeedbackScheduler s = Sched();
                var lights = new List<Light>();
                for (int i = 0; i < 4; i++)
                {
                    Light l = LightAt("f" + i);
                    lights.Add(l);
                    PlayerOn(Go("pf" + i), s, LightFlash(l, 0.6f)).Play();
                    s.Tick(0.05f);
                }
                s.Tick(0.2f);
                Check(string.Join(" ", lights.Select(l => F(l.intensity))) == "5 5 5 1", "four starts — " + string.Join(" ", lights.Select(l => F(l.intensity))));
                CategoryGate gate = s.Context.Categories;
                int entries = gate.FlashEntries;
                gate.SetFlashLimit(10);
                Check(gate.FlashEntries == entries && entries == 3, "a raised limit keeps the window — " + gate.FlashEntries + " of " + entries);
            }
            {
                FeedbackScheduler s = Sched();
                GameObject blinker = Go("blinker");
                PlayerOn(Go("bp"), s, Item<SetActiveItem>(Blink(new Vector4(1f, 0, 0, 0)), Direct(blinker), 1f)).Play();
                Light after = LightAt("after");
                FeedbackPlayer ap = PlayerOn(Go("ap"), s, LightFlash(after, 0.6f));
                s.Tick(0.5f);
                ap.Play();
                s.Tick(0.3f);
                float within = after.intensity;
                ap.Stop();
                s.Tick(0.3f);
                ap.Play();
                s.Tick(0.3f);
                Check(F(within) == "1" && F(after.intensity) == "5", "a Blink counts five — 0.5 s after " + F(within) + " · 1.1 s after " + F(after.intensity));
            }
            {
                FeedbackScheduler s = Sched(new SettingsValues { FlashLimit = 5 });
                GameObject blinker = Go("blinker2");
                FeedbackPlayer bp = PlayerOn(Go("bp2"), s, Item<SetActiveItem>(Blink(new Vector4(1f, 0, 0, 0)), Direct(blinker), 1f));
                bp.m_Speed = 2f;
                bp.Play();
                Light l = LightAt("ls");
                FeedbackPlayer lp = PlayerOn(Go("lp"), s, LightFlash(l, 0.6f));
                s.Tick(0.05f);
                lp.Play();
                s.Tick(0.3f);
                Check(F(l.intensity) == "1", "a Blink at Speed 2 counts five — the Flash after it " + F(l.intensity));
            }
            {
                FeedbackScheduler s = Sched();
                Light rl = LightAt("rl");
                LightIntensityItem rep = LightFlash(rl, 0.1f);
                rep.m_Timing.m_RepeatCount = 3;
                PlayerOn(Go("rp"), s, rep, Act("rp.after", 0.1f, true)).Play();
                var log = new List<string>();
                for (int k = 0; k < 9; k++)
                {
                    s.Tick(0.05f);
                    log.Add(F(rl.intensity));
                }
                Check(string.Join(" ", log) == "5 1 5 1 5 1 1 1 1", "repeats each count — " + string.Join(" ", log));
            }
            {
                // an item whose override hides Flash still counts (Flashes)
                FeedbackScheduler s = Sched(new SettingsValues { FlashLimit = 1 });
                Light a = LightAt("oa");
                Light b = LightAt("ob");
                LightIntensityItem moved = LightFlash(a, 0.6f);
                moved.m_Category.m_Override = true;
                moved.m_Category.m_Category = FeedbackCategory.Motion;
                PlayerOn(Go("op1"), s, moved).Play();
                s.Tick(0.05f);
                FeedbackPlayer second = PlayerOn(Go("op2"), s, LightFlash(b, 0.6f), Act("op2.after", 0.1f, true));
                PlayerTestBinding.Log.Clear();
                second.Play();
                int began = -1;
                float most = b.intensity;
                for (int k = 0; k < 8; k++)
                {
                    s.Tick(0.1f);
                    most = Mathf.Max(most, b.intensity);
                    if (began < 0 && Began("op2.after"))
                        began = k + 1;
                }
                Check(F(most) == "1" && began == 6, "an overridden Flash counts — the next Flash reached " + F(most) + " · its next item at tick " + began);
            }
            End();
        }

        // ---- the global range center

        [Test]
        public void VE11_GlobalCenter()
        {
            Begin();
            FeedbackScheduler s = Sched();
            GameObject center = Go("center", Vector3.zero);
            FeedbackPlayer p = PlayerOn(Go("p", new Vector3(50f, 0f, 0f)), s, Act("a", 0.1f));
            p.m_UseDistanceGate = true;
            p.m_GateReference = DistanceReference.Global;
            p.m_GateDistance = 5f;
            bool noCenter = p.Play(new PlayArgs().WithPosition(new Vector3(10f, 0f, 0f))) != default;
            p.Stop();
            s.Context.RangeCenter = center.transform;
            bool far = p.Play(new PlayArgs().WithPosition(new Vector3(10f, 0f, 0f))) != default;
            bool near = p.Play(new PlayArgs().WithPosition(new Vector3(3f, 0f, 0f))) != default;
            p.Stop();
            bool self = p.Play() != default;
            p.Stop();
            Object.DestroyImmediate(center);
            bool destroyed = p.Play(new PlayArgs().WithPosition(new Vector3(10f, 0f, 0f))) != default;
            Check(noCenter && !far && near && !self && destroyed, "no center " + noCenter + " · at 10 " + far + " · at 3 " + near + " · no position (player at 50) " + self + " · a destroyed center " + destroyed);
            End();
        }

        // ---- the settings

        const string TestFolder = "Assets/xpTURNFeedbackServicesTests";

        [Test]
        public void VE12_Settings()
        {
            Begin();
            SettingsValues none = SettingsValues.Load(ContextKind.Runtime);
            Check(none.FlashLimit == 3 && none.TimeScaleCapacity == 8 && none.ShakeReceiversPerPlay == 4 && none.ReceiverContributions == 8 && !none.AdjustFixedDeltaTime && none.MaximumUnscaledDelta == 0f, "no asset — " + none.FlashLimit + "/" + none.TimeScaleCapacity + "/" + none.ShakeReceiversPerPlay + "/" + none.ReceiverContributions);
            {
                FeedbackScheduler s = Sched(new SettingsValues { FlashLimit = 2, TimeScaleCapacity = 3, ShakeReceiversPerPlay = 2, ReceiverContributions = 3 });
                Check(s.Context.Categories.FlashLimit == 2, "the injected flash limit");
                TimeScaleService ts = s.Context.TimeScale;
                for (int i = 0; i < 4; i++)
                    ts.Request(0.5f, 1.0);
                Check(ts.Count == 3, "the injected stack — " + ts.Count);
                var rs = new List<CameraShakeReceiver>();
                for (int i = 0; i < 3; i++)
                {
                    var r = Go("sr" + i).AddComponent<CameraShakeReceiver>();
                    r.m_Channel = ChannelRef.Of(1);
                    r.Bind(s);
                    rs.Add(r);
                }
                FeedbackPlayer p = PlayerOn(Go("sp"), s, Item<CameraShakeItem>(Shake(new Vector4(1f, 0f, 0f, 0f)), OnChannel(1), 1f));
                p.Play();
                Check(((CameraShakeBinding)p.Runtime.Binding(0)).Reached(0) == 2 && rs[0].PositionSlot.Reserved == 3, "the injected M and C — reached " + ((CameraShakeBinding)p.Runtime.Binding(0)).Reached(0) + " · reserved " + rs[0].PositionSlot.Reserved);
                p.Stop();
            }
            Check(SettingsValues.Load(ContextKind.Test).FlashLimit == 3, "a test context reads the defaults");
            // a Resources asset made by the test
            try
            {
                if (!AssetDatabase.IsValidFolder(TestFolder))
                    AssetDatabase.CreateFolder("Assets", "xpTURNFeedbackServicesTests");
                AssetDatabase.CreateFolder(TestFolder, "Resources");
                var asset = ScriptableObject.CreateInstance<FeedbackSettings>();
                asset.m_FlashLimit = 7;
                asset.m_Categories = new[] { new CategoryDefault { m_Category = FeedbackCategory.Camera, m_Enabled = false, m_Scale = 0.25f } };
                AssetDatabase.CreateAsset(asset, TestFolder + "/Resources/FeedbackSettings.asset");
                SettingsValues read = SettingsValues.Load(ContextKind.Runtime);
                Check(read.FlashLimit == 7 && !read.Enabled[(int)FeedbackCategory.Camera] && read.Scale[(int)FeedbackCategory.Camera] == 0.25f, "the Resources asset — flash " + read.FlashLimit + " · Camera " + read.Enabled[(int)FeedbackCategory.Camera]);
                // out of range reads as the default
                asset.m_FlashLimit = -3;
                asset.m_TimeScaleCapacity = 0;
                asset.m_ShakeReceiversPerPlay = 0;
                asset.m_ReceiverContributions = -1;
                asset.m_MaximumUnscaledDelta = float.NaN;
                SettingsValues bad = SettingsValues.From(asset);
                Check(bad.FlashLimit == 3 && bad.TimeScaleCapacity == 8 && bad.ShakeReceiversPerPlay == 4 && bad.ReceiverContributions == 8 && bad.MaximumUnscaledDelta == 0f, "out of range — " + bad.FlashLimit + "/" + bad.TimeScaleCapacity + "/" + bad.ShakeReceiversPerPlay + "/" + bad.ReceiverContributions + "/" + bad.MaximumUnscaledDelta);
            }
            finally
            {
                AssetDatabase.DeleteAsset(TestFolder);
            }
            End();
        }

        // ---- the static APIs outside play

        [Test]
        public void VE13_StaticOutside()
        {
            Begin();
            int w = s_Warnings.Count;
            FeedbackTimeScale.BaseScale = -1f;
            FeedbackTimeScale.BaseScale = float.NaN;
            FeedbackCategories.SetScale(FeedbackCategory.None, 1f);
            FeedbackCategories.SetFlashLimit(-1);
            int args = s_Warnings.Count - w;
            Check(args == 2, "bad arguments — warnings " + args);
            w = s_Warnings.Count;
            FeedbackCategories.SetEnabled(FeedbackCategory.Flash, false);
            FeedbackCategories.SetEnabled(FeedbackCategory.Flash, false);
            FeedbackTimeScale.BaseScale = 0.5f;
            FeedbackTimeScale.BaseScale = 0.5f;
            int taken = FeedbackChannels.Send(FeedbackCommand.Play(ChannelRef.Of(1)));
            FeedbackChannels.Send(FeedbackCommand.Play(ChannelRef.Of(1)));
            FeedbackRange.Center = null;
            FeedbackRange.Center = null;
            Check(s_Warnings.Count - w == 4 && taken == 0, "outside play — warnings " + (s_Warnings.Count - w) + " · taken " + taken);
            Check(FeedbackCategories.IsEnabled(FeedbackCategory.Flash) && FeedbackCategories.GetScale(FeedbackCategory.Flash) == 1f && FeedbackCategories.FlashLimit == 3 && FeedbackTimeScale.BaseScale == Time.timeScale && FeedbackRange.Center == null, "the reads outside play");
            End();
        }

        // ---- the surface — the public names, the enum values, the saved fields and their defaults, the registry

        static string Values(Type type) => string.Join(" ", Enum.GetNames(type).Select(n => n + "=" + (int)Enum.Parse(type, n)));

        static bool HasSaved(Type type, string fields)
            => fields.Split(' ').All(n => DataModelTraitsTests.SavedFieldInfos(type).Any(f => f.Name == n)) && DataModelTraitsTests.SavedFieldInfos(type).Count() == fields.Split(' ').Length;

        [Test]
        public void VE14_Surface()
        {
            Begin();
            Type[] publics = { typeof(FeedbackReceiver), typeof(CameraShakeReceiver), typeof(FeedbackChannels), typeof(FeedbackCommand), typeof(FeedbackCommandKind), typeof(FeedbackSettings), typeof(FeedbackCategories), typeof(FeedbackRange), typeof(FeedbackTimeScale) };
            Check(publics.All(t => t.IsPublic), "a type not public");
            Check(Values(typeof(FeedbackCommandKind)) == "Play=0 Stop=1 Pause=2 Resume=3 Seek=4 Skip=5 Restore=6 Signal=7", Values(typeof(FeedbackCommandKind)));
            Check(Values(typeof(DistanceReference)) == "MainCamera=0 Anchor=1 PlayPosition=2 Global=3", Values(typeof(DistanceReference)));
            // the saved fields and the defaults a new component · asset reads
            Check(HasSaved(typeof(FeedbackReceiver), "m_Channel m_Player m_Range") && HasSaved(typeof(CameraShakeReceiver), "m_Channel m_Range"), "the receivers' saved fields");
            Check(HasSaved(typeof(FeedbackSettings), "m_Categories m_FlashLimit m_TimeScaleCapacity m_AdjustFixedDeltaTime m_ShakeReceiversPerPlay m_ReceiverContributions m_MaximumUnscaledDelta m_OverlaySortingOrder m_PostFXLayer m_PostFXPriority")
                  && HasSaved(typeof(CategoryDefault), "m_Category m_Enabled m_Scale"), "the settings' saved fields");
            var asset = ScriptableObject.CreateInstance<FeedbackSettings>();
            SettingsValues read = SettingsValues.From(asset);
            Object.DestroyImmediate(asset);
            Check(read.FlashLimit == 3 && read.TimeScaleCapacity == 8 && !read.AdjustFixedDeltaTime && read.ShakeReceiversPerPlay == 4 && read.ReceiverContributions == 8 && read.MaximumUnscaledDelta == 0f
                  && read.Enabled.All(e => e) && read.Scale.All(v => v == 1f), "a new asset's values");
            GameObject rg = Go("rg");
            var receiver = rg.AddComponent<FeedbackReceiver>();
            var camera = rg.AddComponent<CameraShakeReceiver>();
            Check(receiver.m_Channel.m_Mode == ChannelRefMode.Int && receiver.m_Channel.m_Id == 0 && receiver.m_Player == null && receiver.m_Range == 0f && camera.m_Range == 0f, "a new receiver's values");
            // the registry: the Core item types with Core's own implementations — an optional assembly's items are Core's data too
            Check(FeedbackImplementations.RegisteredTypes.Count(t => t.Assembly == typeof(FeedbackPlayer).Assembly && OptionalAssemblyTests.ImplementationOf(t).Length == 0) == 71, "the registered Core types");
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = PlayerOn(Go("p"), s, Item<CameraShakeItem>(Shake(new Vector4(1f, 0f, 0f, 0f)), Direct(Go("c").transform), 1f), Hit(0.1f));
            p.EnsureRuntime();
            Check(p.Runtime.Binding(0) is CameraShakeBinding && p.Runtime.Binding(1) is HitStopBinding, "the two registered");
            End();
        }
    }
}
