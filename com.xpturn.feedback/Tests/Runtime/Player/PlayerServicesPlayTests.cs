using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests
{
    // PlayMode — the time values, the real loop, the receiver components, the first runs. Default is a test scheduler on the
    // real loop (captureDeltaTime 1/60 — the unscaled dt stays real time): the time values are judged on hand-ticked test
    // schedulers and emulated frames, the real loop by conditions (real length · scaled time 0 · the frame alignment within
    // one frame). A row checks everything and judges at the end
    public sealed class PlayerServicesPlayTests
    {

        static readonly List<string> s_Errors = new List<string>();
        static readonly List<string> s_Warnings = new List<string>();
        static FeedbackScheduler s_Saved;
        static FeedbackScheduler s_Test;
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<string> m_Fail = new List<string>();
        float m_Fixed;
        float m_Maximum;

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
                s_Warnings.Add(condition);
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                s_Errors.Add(condition);
        }

        static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        static string R(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        [SetUp]
        public void SetUp()
        {
            s_Errors.Clear();
            s_Warnings.Clear();
            m_Fail.Clear();
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            Application.logMessageReceived += OnLog;
            m_Fixed = Time.fixedDeltaTime;
            m_Maximum = Time.maximumDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            s_Saved = FeedbackScheduler.Default;
            s_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = s_Test;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Made.Clear();
            yield return null;
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    p.ReleaseRuntime();
            }
            m_Players.Clear();
            FeedbackScheduler.Default = s_Saved;
            if (!s_Test.IsShutdown)
                s_Test.Shutdown();
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Schedulers.Clear();
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = m_Fixed;
            Time.maximumDeltaTime = m_Maximum;
            Application.logMessageReceived -= OnLog;
            PlayerTestBinding.Reset();
        }

        GameObject Go(string name, Vector3 at = default)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            m_Made.Add(go);
            return go;
        }

        FeedbackPlayer PlayerOn(GameObject owner, params FeedbackItem[] items)
        {
            var player = owner.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Items.AddRange(items);
            player.EnsureRuntime();
            return player;
        }

        FeedbackScheduler Hand(SettingsValues values = null)
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            if (values != null)
                s.Context.UseSettings(values);
            return s;
        }

        FeedbackPlayer HandPlayer(FeedbackScheduler s, params FeedbackItem[] items)
        {
            var p = Go("hand").AddComponent<FeedbackPlayer>();
            m_Players.Add(p);
            p.m_Items.AddRange(items);
            p.Bind(s);
            return p;
        }

        // After previous names Math's StartRule — this assembly takes Math
        static PlayerTestAction Act(string label, float length, bool after = false, ItemClock clock = ItemClock.Player)
        {
            var action = new PlayerTestAction { m_Label = label, m_Length = length };
            if (after)
                action.m_Timing.m_Rule = StartRule.AfterPrevious;
            action.m_Timing.m_Clock = clock;
            return action;
        }

        static HitStopItem Hit(float freeze, float scale = 0f, bool after = false, float minimum = 0.1f)
        {
            var hit = new HitStopItem { m_Label = "hit", m_FreezeDuration = freeze, m_MinimumTimeScale = minimum, m_TimeScale = scale };
            if (after)
                hit.m_Timing.m_Rule = StartRule.AfterPrevious;
            return hit;
        }

        static CameraShakeItem ChannelShake(int channel, float duration, bool after = false)
        {
            var item = new CameraShakeItem { m_Motion = new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = new Vector4(1f, 0f, 0f, 0f), m_Frequency = 10f } } };
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Channel, m_Channel = ChannelRef.Of(channel), m_Path = "", m_Anchor = "" };
            item.m_Timing.m_Duration = duration;
            if (after)
                item.m_Timing.m_Rule = StartRule.AfterPrevious;
            return item;
        }

        static bool Began(string label) => PlayerTestBinding.Log.Any(l => l.StartsWith(label + ".Begin", StringComparison.Ordinal));

        static IEnumerator WaitReal(float seconds)
        {
            float started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < seconds)
                yield return null;
        }

        // one emulated frame: the engine's scaled dt from the time scale at the frame start · Revert · the game's Update ·
        // our Update · LateApply. Returns whether the frame was frozen and which labels began in it
        static (bool Frozen, string Began) Emu(FeedbackScheduler s, float unscaled, Action update = null)
        {
            float ts0 = Time.timeScale;
            PlayerTestBinding.Log.Clear();
            s.RunRevert();
            update?.Invoke();
            s.RunUpdate(new FrameTime(unscaled * ts0, unscaled));
            s.RunLateApply();
            return (ts0 == 0f, string.Join(",", PlayerTestBinding.Log.Where(l => l.Contains(".Begin")).Select(l => l.Substring(0, l.IndexOf(".Begin", StringComparison.Ordinal)))));
        }

        // ---- the time values on hand-ticked frames

        [UnityTest]
        public IEnumerator VP01_TimeValues()
        {
            yield return null;
            Time.fixedDeltaTime = 0.02f;
            Time.maximumDeltaTime = 1f / 3f;
            float fixed0 = Time.fixedDeltaTime, max0 = Time.maximumDeltaTime;
            {
                FeedbackScheduler hand = Hand();
                HandPlayer(hand, Hit(0.1f)).Play();
                var ts = new List<string>();
                for (int k = 0; k < 5; k++)
                {
                    hand.Tick(0.03f);
                    ts.Add(F(Time.timeScale));
                }
                Check(string.Join(" ", ts) == "0 0 0 0 1", "a 0.1 freeze at 0.03 ticks — " + string.Join(" ", ts));
                hand.Shutdown();
                Time.timeScale = 1f;
            }
            {
                FeedbackScheduler hand = Hand(new SettingsValues { AdjustFixedDeltaTime = true });
                HandPlayer(hand, Hit(0.1f, 20f)).Play();
                hand.Tick(0.03f);
                float fixedIn = Time.fixedDeltaTime, maxIn = Time.maximumDeltaTime, tsIn = Time.timeScale;
                for (int k = 0; k < 5; k++)
                    hand.Tick(0.03f);
                Check(tsIn == 20f && Math.Abs(fixedIn - 0.4f) < 1e-6f && Math.Abs(maxIn - 20f / 3f) < 1e-4f && Time.timeScale == 1f && Math.Abs(Time.fixedDeltaTime - fixed0) < 1e-8f && Time.maximumDeltaTime == max0,
                    "the option at 20 — in " + F(tsIn) + "/" + R(fixedIn) + "/" + R(maxIn) + " · after " + F(Time.timeScale) + "/" + R(Time.fixedDeltaTime) + "/" + R(Time.maximumDeltaTime));
                HandPlayer(hand, Hit(0.1f)).Play();
                hand.Tick(0.03f);
                float fixedZero = Time.fixedDeltaTime, maxZero = Time.maximumDeltaTime;
                for (int k = 0; k < 5; k++)
                    hand.Tick(0.03f);
                Check(Math.Abs(fixedZero - fixed0) < 1e-8f && maxZero == max0, "the option at 0 keeps both — " + R(fixedZero) + "/" + R(maxZero));
                hand.Shutdown();
                Time.timeScale = 1f;
                Time.fixedDeltaTime = 0.02f;
                Time.maximumDeltaTime = 1f / 3f;
            }
            {
                // a game write in a freeze is the new base · the base through the API — at once when idle, the next tick in a request
                FeedbackScheduler hand = Hand();
                HandPlayer(hand, Hit(0.2f)).Play();
                hand.Tick(0.05f);
                Time.timeScale = 0.5f;
                for (int k = 0; k < 6; k++)
                    hand.Tick(0.05f);
                Check(Time.timeScale == 0.5f, "a game write of 0.5 in a freeze — after it " + F(Time.timeScale));
                Time.timeScale = 1f;
                hand.Tick(0.05f);
                hand.Context.TimeScale.SetBaseScale(0f);
                Check(Time.timeScale == 0f, "the idle base 0 — " + F(Time.timeScale));
                hand.Context.TimeScale.SetBaseScale(1f);
                HandPlayer(hand, Hit(0.3f, 0.5f)).Play();
                hand.Tick(0.05f);
                hand.Context.TimeScale.SetBaseScale(0.4f);
                float same = Time.timeScale;
                hand.Tick(0.05f);
                Check(same == 0.5f && Math.Abs(Time.timeScale - 0.2f) < 1e-6f, "the base in a request — at once " + F(same) + " · the next tick " + F(Time.timeScale));
                hand.Shutdown();
                Time.timeScale = 1f;
            }
            {
                // the game's idle write is the next freeze's base · a same-frame game write before a new request
                FeedbackScheduler hand = Hand();
                FeedbackPlayer a = HandPlayer(hand, Hit(0.1f));
                a.Play();
                for (int k = 0; k < 4; k++)
                    hand.Tick(0.05f);
                Time.timeScale = 0.7f;
                a.Play();
                for (int k = 0; k < 4; k++)
                    hand.Tick(0.05f);
                Check(Time.timeScale == 0.7f, "the game's idle 0.7 under a freeze — after it " + F(Time.timeScale));
                Time.timeScale = 1f;
                hand.Tick(0.05f);
                a.Play();
                hand.Tick(0.05f);
                hand.RunRevert();
                Time.timeScale = 0.5f;
                hand.Context.TimeScale.Request(0f, 0.05);
                hand.RunUpdate(new FrameTime(0.05f, 0.05f));
                hand.RunLateApply();
                for (int k = 0; k < 4; k++)
                    hand.Tick(0.05f);
                Check(Time.timeScale == 0.5f, "a same-frame game write before a request — after " + F(Time.timeScale));
                hand.Shutdown();
                Time.timeScale = 1f;
            }
            {
                // a Shutdown in a request puts the values back
                FeedbackScheduler hand = Hand(new SettingsValues { AdjustFixedDeltaTime = true });
                HandPlayer(hand, Hit(5f, 20f)).Play();
                hand.Tick(0.03f);
                hand.Shutdown();
                Check(Time.timeScale == 1f && Math.Abs(Time.fixedDeltaTime - fixed0) < 1e-8f && Time.maximumDeltaTime == max0, "Shutdown in a request — " + F(Time.timeScale) + "/" + R(Time.fixedDeltaTime) + "/" + R(Time.maximumDeltaTime));
                Time.timeScale = 1f;
                Time.fixedDeltaTime = 0.02f;
                Time.maximumDeltaTime = 1f / 3f;
            }
            {
                // emulated frames of 1/30 — the frozen frames and the frame the next item begins in
                const float dt = 1f / 30f;
                string Timeline(Func<FeedbackScheduler, FeedbackPlayer> make, int frames, string label, out int frozen, out int began)
                {
                    FeedbackScheduler s = Hand();
                    FeedbackPlayer p = make(s);
                    frozen = 0;
                    began = -1;
                    var row = new List<string>();
                    for (int k = 0; k < frames; k++)
                    {
                        (bool fz, string b) = Emu(s, dt, k == 0 ? (Action)(() => p.Play()) : null);
                        if (fz)
                            frozen++;
                        if (began < 0 && b.Split(',').Contains(label))
                            began = k;
                        row.Add((fz ? "0" : "1") + (b.Length > 0 ? "[" + b + "]" : ""));
                    }
                    s.Shutdown();
                    Time.timeScale = 1f;
                    return string.Join(" ", row);
                }
                string t1 = Timeline(s => HandPlayer(s, Hit(0.1f), Act("a1", 0.1f, true)), 8, "a1", out int f1, out int b1);
                Check(f1 == 3 && b1 == 3, "T1 — " + t1);
                string t2 = Timeline(s => HandPlayer(s, Act("l2", 0.1f), Hit(0.1f, 0f, true), Act("t2", 0.1f, true)), 10, "t2", out int f2, out int b2);
                Check(f2 == 3 && b2 == 6, "T2 — " + t2);
                string t5 = Timeline(s => HandPlayer(s, Act("l5", dt), Hit(1f / 60f, 0f, true), Act("t5", 0.05f, true)), 5, "t5", out int f5, out int b5);
                Check(f5 == 1, "T5 — " + t5);
            }
            End();
        }

        // ---- the real loop

        [UnityTest]
        public IEnumerator VP02_RealLoop()
        {
            yield return null;
            {
                FeedbackPlayer p = PlayerOn(Go("p"), Hit(0.1f), Act("after", 0.05f, true));
                PlayerTestBinding.Log.Clear();
                p.Play();
                float right = Time.timeScale;
                int frame = 0, frozen = 0, began = -1, back = -1;
                double real = 0, scaled = 0, lastDt = 0;
                float started = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - started < 3f)
                {
                    yield return null;
                    frame++;
                    if (began < 0 && Began("after"))
                        began = frame;
                    if (Time.timeScale != 1f)
                    {
                        frozen++;
                        real += Time.unscaledDeltaTime;
                        scaled += Time.deltaTime;
                        lastDt = Time.unscaledDeltaTime;
                    }
                    else if (frozen > 0 && back < 0)
                    {
                        back = frame;
                        if (began >= 0)
                            break;
                    }
                }
                Check(right == 1f && scaled == 0 && real >= 0.1 - 1e-6 && real < 0.1 + lastDt + 1e-6 && (began >= 0 && Math.Abs(began - back) <= 1),
                    "a t = 0 freeze — right after the Play " + F(right) + " · real " + F(real) + " · scaled " + F(scaled) + " · after began " + began + " · back " + back);
                yield return WaitReal(0.1f);
            }
            {
                // two in one coroutine step — one request
                FeedbackPlayer p = PlayerOn(Go("p1"), Hit(0.1f));
                FeedbackPlayer q = PlayerOn(Go("q1"), Hit(0.2f));
                p.Play();
                q.Play();
                Check(s_Test.Context.TimeScale.Count == 1, "two in one step — requests " + s_Test.Context.TimeScale.Count);
                yield return WaitReal(0.3f);
            }
            {
                // the settings' dt cap: a 60 ms frame gives an Unscaled item 0.01
                FeedbackScheduler capped = Hand(new SettingsValues { MaximumUnscaledDelta = 0.01f });
                FeedbackScheduler.Default = capped;
                FeedbackPlayer p = PlayerOn(Go("cap"), Act("u", 10f, false, ItemClock.Unscaled));
                p.Play();
                yield return null;
                yield return null;
                PlayerTestBinding b = PlayerTestBinding.Made.Last(x => x.Item.m_Label == "u");
                double before = b.LastTime.Seconds;
                System.Threading.Thread.Sleep(60);
                double step = 0;
                for (int k = 0; k < 3; k++)
                {
                    yield return null;
                    step = Math.Max(step, b.LastTime.Seconds - before);
                    before = b.LastTime.Seconds;
                }
                Check(step <= 0.0101, "the dt cap 0.01 — a frame after a 60 ms sleep advanced " + F(step));
                p.Stop();
                FeedbackScheduler.Default = s_Test;
            }
            End();
        }

        // ---- the receivers on the real loop

        [UnityTest]
        public IEnumerator VP03_Receivers()
        {
            yield return null;
            ChannelService ch = s_Test.Context.Channels;
            {
                GameObject go = Go("rx");
                go.SetActive(false);
                FeedbackPlayer p = PlayerOn(go, Act("rx.a", 0.5f));
                var rx = go.AddComponent<FeedbackReceiver>();
                rx.m_Channel = ChannelRef.Of(1);
                go.SetActive(true);
                Check(rx.IsRegistered && ch.CommandReceivers(ChannelRef.Of(1)) == 1, "OnEnable — count " + ch.CommandReceivers(ChannelRef.Of(1)));
                int taken = FeedbackChannels.Send(FeedbackCommand.Play(ChannelRef.Of(1)));
                Check(taken == 1 && p.IsPlaying, "a Send plays — taken " + taken);
                for (int i = 0; i < 100; i++)
                {
                    go.SetActive(false);
                    go.SetActive(true);
                }
                Check(ch.CommandReceivers(ChannelRef.Of(1)) == 1, "100 off · on — count " + ch.CommandReceivers(ChannelRef.Of(1)));
                go.SetActive(false);
                p.Stop();
                Check(FeedbackChannels.Send(FeedbackCommand.Play(ChannelRef.Of(1))) == 0, "a disabled receiver took it");
            }
            {
                GameObject cam = Go("cam");
                cam.SetActive(false);
                var shake = cam.AddComponent<CameraShakeReceiver>();
                shake.m_Channel = ChannelRef.Of(2);
                cam.SetActive(true);
                int reserved = shake.PositionSlot != null ? shake.PositionSlot.Reserved : -1;
                FeedbackPlayer sp = PlayerOn(Go("sp"), ChannelShake(2, 0.5f));
                sp.Play();
                yield return null;
                Check(shake.PositionSlot != null && shake.RotationSlot != null && reserved == 8 && shake.PositionSlot.LiveContributions == 1, "the camera receiver — reserved " + reserved + " · live " + (shake.PositionSlot != null ? shake.PositionSlot.LiveContributions : -1));
                sp.Stop();
                LocalPositionSlot slot = shake.PositionSlot;
                cam.SetActive(false);
                Check(slot.Detached || slot.Reserved == 0, "the disabled receiver let its slot go — reserved " + slot.Reserved + " · detached " + slot.Detached);
            }
            {
                // a real disable while another binding holds the slot
                GameObject cam = Go("held");
                cam.SetActive(false);
                var shake = cam.AddComponent<CameraShakeReceiver>();
                shake.m_Channel = ChannelRef.Of(3);
                cam.SetActive(true);
                var holder = new PositionItem { m_Motion = new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = Vector4.zero, m_Frequency = 10f } }, m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = cam.transform, m_Path = "", m_Anchor = "" } };
                holder.m_Timing.m_Duration = 1f;
                PlayerOn(Go("holder"), holder);
                FeedbackPlayer hp = PlayerOn(Go("hp"), ChannelShake(3, 1f));
                hp.Play();
                yield return null;
                yield return null;
                cam.SetActive(false);
                yield return null;
                yield return null;
                Check(cam.transform.localPosition.x == 0f, "the disabled receiver's camera at " + F(cam.transform.localPosition.x));
                hp.Stop();
            }
            {
                // a destroyed player's delayed channel shake measures from the Play's place
                GameObject near = Go("near", new Vector3(50f, 0f, 0f));
                near.SetActive(false);
                var nr = near.AddComponent<CameraShakeReceiver>();
                nr.m_Channel = ChannelRef.Of(4);
                nr.m_Range = 5f;
                near.SetActive(true);
                GameObject origin = Go("origin", Vector3.zero);
                origin.SetActive(false);
                var orr = origin.AddComponent<CameraShakeReceiver>();
                orr.m_Channel = ChannelRef.Of(4);
                orr.m_Range = 5f;
                origin.SetActive(true);
                GameObject owner = Go("owner", new Vector3(50f, 0f, 0f));
                FeedbackPlayer p = PlayerOn(owner, Act("wait", 0.05f), ChannelShake(4, 0.3f, true));
                p.Play();
                Object.Destroy(owner);
                for (int k = 0; k < 8; k++)
                    yield return null;
                Check(nr.PositionSlot.LiveContributions == 1 && orr.PositionSlot.LiveContributions == 0, "the Play's place — near " + nr.PositionSlot.LiveContributions + " · origin " + orr.PositionSlot.LiveContributions);
                yield return WaitReal(0.4f);
            }
            End();
        }

        // ---- the service paths' first runs — five fresh fixtures

        sealed class Fixture
        {
            public FeedbackScheduler Scheduler;
            public FeedbackPlayer Player;
        }

        static Fixture s_Fixture;

        static void Tick(int n)
        {
            for (int i = 0; i < n; i++)
                s_Fixture.Scheduler.Tick(0.05f);
        }

        Fixture Fresh(string kind)
        {
            FeedbackScheduler s = Hand();
            var player = Go("fixture").AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            switch (kind)
            {
                case "hit":
                    player.m_Items.Add(Hit(0.05f));
                    break;
                case "send":
                    for (int i = 0; i < 3; i++)
                    {
                        GameObject r = Go("r" + i);
                        FeedbackPlayer rp = r.AddComponent<FeedbackPlayer>();
                        m_Players.Add(rp);
                        rp.m_Items.Add(Act("r", 0.1f));
                        rp.Bind(s);
                        rp.EnsureRuntime();
                        var rx = r.AddComponent<FeedbackReceiver>();
                        rx.m_Channel = ChannelRef.Of(1);
                        rx.Bind(s);
                    }
                    player.m_Items.Add(Act("a", 0.1f));
                    break;
                case "shake":
                    for (int i = 0; i < 2; i++)
                    {
                        var cr = Go("c" + i).AddComponent<CameraShakeReceiver>();
                        cr.m_Channel = ChannelRef.Of(2);
                        cr.Bind(s);
                    }
                    CameraShakeItem item = ChannelShake(2, 0.2f);
                    item.m_RotationAmplitude = new Vector3(0f, 0f, 5f);
                    player.m_Items.Add(item);
                    break;
                case "direct":
                    var d = new CameraShakeItem { m_Motion = new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = new Vector4(1f, 0f, 0f, 0f), m_Frequency = 10f } } };
                    d.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = Go("cam").transform, m_Path = "", m_Anchor = "" };
                    d.m_RotationAmplitude = new Vector3(0f, 0f, 5f);
                    d.m_Timing.m_Duration = 0.2f;
                    player.m_Items.Add(d);
                    break;
                case "off":
                case "flash":
                    var light = Go("light").AddComponent<Light>();
                    var li = new LightIntensityItem { m_Motion = new FlashMotion { m_Params = new FlashParams { m_Target = new Vector4(5f, 0, 0, 0), m_Attack = 0.05f, m_Hold = 0.05f, m_Release = 0.05f } } };
                    li.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = light, m_Path = "", m_Anchor = "" };
                    li.m_Timing.m_Duration = 0.15f;
                    player.m_Items.Add(li);
                    if (kind == "off")
                        s.Context.Categories.SetEnabled(FeedbackCategory.Flash, false);
                    else
                        s.Context.Categories.SetFlashLimit(0);
                    break;
                case "global":
                    player.m_Items.Add(Act("g", 0.1f));
                    player.m_UseDistanceGate = true;
                    player.m_GateReference = DistanceReference.Global;
                    player.m_GateDistance = 5f;
                    s.Context.RangeCenter = Go("center").transform;
                    break;
            }
            player.Bind(s);
            player.EnsureRuntime();
            return new Fixture { Scheduler = s, Player = player };
        }

        static readonly (string Name, string Kind, TestDelegate Run)[] s_Paths =
        {
            ("hit stop", "hit", () => { s_Fixture.Player.Play(); Tick(3); }),
            ("send to three receivers", "send", () => { s_Fixture.Scheduler.Context.Channels.Send(FeedbackCommand.Play(ChannelRef.Of(1)), true, Vector3.zero); Tick(1); }),
            ("channel shake", "shake", () => { s_Fixture.Player.Play(); Tick(3); }),
            ("direct shake", "direct", () => { s_Fixture.Player.Play(); Tick(3); }),
            ("category off", "off", () => { s_Fixture.Player.Play(); Tick(2); }),
            ("flash refused", "flash", () => { s_Fixture.Player.Play(); Tick(2); }),
            ("global gate", "global", () => { s_Fixture.Player.Play(new PlayArgs().WithPosition(new Vector3(3f, 0f, 0f))); Tick(1); }),
        };

        [UnityTest]
        public IEnumerator VP04_FirstRuns()
        {
            yield return null;
            PlayerTestBinding.Logging = false;
            var rows = new List<string>();
            foreach ((string name, string kind, TestDelegate run) in s_Paths)
            {
                s_Fixture = Fresh(kind);
                run();
                int hits = 0;
                for (int i = 0; i < 5; i++)
                {
                    s_Fixture = Fresh(kind);
                    if (UIs.AllocatingGCMemory().ApplyTo(run).IsSuccess)
                        hits++;
                }
                if (hits == 5)
                    rows.Add(name);
            }
            int positive = 0;
            for (int i = 0; i < 5; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => s_Errors.Add(new string('x', 3)))).IsSuccess)
                    positive++;
            }
            PlayerTestBinding.Logging = true;
            Check(rows.Count == 0 && positive == 5, "first runs allocate — " + string.Join(", ", rows) + " · positive " + positive + "/5");
            End();
        }
    }
}
