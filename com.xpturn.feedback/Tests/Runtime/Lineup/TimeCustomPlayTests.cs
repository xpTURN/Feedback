using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // PlayMode — the service writes while playing: the time values on hand-ticked test schedulers, emulated frames (the
    // engine's scaled dt from the scale at the frame start — a hand tick's scaled dt is its unscaled one) and the real loop's
    // frames. A row checks everything and judges at the end
    public sealed class TimeCustomPlayTests
    {
        static FeedbackScheduler s_Saved;
        static FeedbackScheduler s_Test;
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<string> m_Fail = new List<string>();
        float m_Fixed;
        float m_Maximum;

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
            m_Fail.Clear();
            m_Fixed = Time.fixedDeltaTime;
            m_Maximum = Time.maximumDeltaTime;
            // the rows read the engine's scale from 1 — not the project's saved scale or a test before
            Time.timeScale = 1f;
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
        }

        GameObject Go(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
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

        FeedbackPlayer LoopPlayer(params FeedbackItem[] items)
        {
            var p = Go("loop").AddComponent<FeedbackPlayer>();
            m_Players.Add(p);
            p.m_Items.AddRange(items);
            p.EnsureRuntime();
            return p;
        }

        static TimeScaleItem Scale(FeedbackMotion motion, float duration)
        {
            var item = new TimeScaleItem { m_Motion = motion };
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static TweenMotion TweenTo(float to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = new Vector4(to, 0f, 0f, 0f);
            return new TweenMotion { m_Params = p };
        }

        static SetMotion SetTo(float v) => new SetMotion { m_Params = new SetParams { m_Value = new Vector4(v, 0f, 0f, 0f) } };

        static FlashMotion Held(float v)
        {
            var flash = new FlashMotion();
            flash.m_Params.m_Target = new Vector4(v, v, v, v);
            flash.m_Params.m_Attack = 0f;
            flash.m_Params.m_Hold = 1f;
            flash.m_Params.m_Release = 0f;
            return flash;
        }

        static HitStopItem Hit(float freeze) => new HitStopItem { m_FreezeDuration = freeze, m_MinimumTimeScale = 0.1f, m_TimeScale = 0f };

        // one emulated frame: the engine's scaled dt from the time scale at the frame start
        static void Emu(FeedbackScheduler s, float unscaled)
        {
            float ts = Time.timeScale;
            s.RunRevert();
            s.RunUpdate(new FrameTime(unscaled * ts, unscaled));
            s.RunLateApply();
        }

        // ---- a tick writes the scale its stage changed (the flush after the playbacks) · the tween's end · Revert · the delta-time
        // option follows and hands off after a flash (a game's later fixedDeltaTime stays) · the game's base under it · a tween back
        // to 1 hands off in its last tick · the Time category's unscaled clock under its own slow motion
        [UnityTest]
        public IEnumerator CQ04a_TheTimeValues()
        {
            yield return null;
            {
                FeedbackScheduler hand = Hand();
                TimeScaleService t = hand.Context.TimeScale;
                var keeper = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = Go("keeper").transform, m_Path = "", m_Anchor = "" }, m_Motion = new PunchMotion() };
                keeper.m_Timing.m_Duration = 3f;
                FeedbackPlayer p = HandPlayer(hand, Scale(TweenTo(0.5f), 0.2f), keeper);
                p.Play();
                var row = new List<string>();
                bool same = true;
                for (int k = 0; k < 6; k++)
                {
                    hand.Tick(0.05f);
                    row.Add(F(Time.timeScale));
                    same &= Time.timeScale == t.Effective;
                }
                Check(same && row[0] == "0.875" && Time.timeScale == 0.5f, "the tick writes the engine — " + string.Join(" ", row));
                p.Stop(StopMode.Revert);
                hand.Tick(0.05f);
                Check(Time.timeScale == 1f && !t.IsOwning, "Revert — " + F(Time.timeScale) + " · owning " + t.IsOwning);
                hand.Shutdown();
                Time.timeScale = 1f;
            }
            {
                Time.fixedDeltaTime = 0.02f;
                FeedbackScheduler hand = Hand(new SettingsValues { AdjustFixedDeltaTime = true });
                HandPlayer(hand, Scale(Held(0.5f), 0.2f)).Play();
                hand.Tick(0.05f);
                float during = Time.fixedDeltaTime;
                for (int k = 0; k < 8; k++)
                    hand.Tick(0.05f);
                float after = Time.fixedDeltaTime;
                bool owning = hand.Context.TimeScale.IsOwning;
                Time.fixedDeltaTime = 0.015f;
                for (int k = 0; k < 4; k++)
                    hand.Tick(0.05f);
                Check(Math.Abs(during - 0.01f) < 1e-7f && Math.Abs(after - 0.02f) < 1e-7f && !owning && Math.Abs(Time.fixedDeltaTime - 0.015f) < 1e-7f,
                    "the option — during " + F(during) + " · after " + F(after) + " · owning " + owning + " · the game's 0.015 → " + F(Time.fixedDeltaTime));
                hand.Shutdown();
                Time.timeScale = 1f;
                Time.fixedDeltaTime = 0.02f;
            }
            {
                FeedbackScheduler hand = Hand();
                FeedbackPlayer slow = HandPlayer(hand, Scale(SetTo(0.5f), 0f));
                slow.Play();
                hand.Tick(0.05f);
                Time.timeScale = 0.8f;
                hand.Tick(0.05f);
                float game = Time.timeScale;
                hand.Context.TimeScale.SetBaseScale(0.6f);
                hand.Tick(0.05f);
                float api = Time.timeScale;
                slow.Restore();
                hand.Tick(0.05f);
                Check(Math.Abs(game - 0.4f) < 1e-6f && Math.Abs(api - 0.3f) < 1e-6f && Math.Abs(Time.timeScale - 0.6f) < 1e-6f,
                    "the base under it — the game's 0.8 " + F(game) + " · the API's 0.6 " + F(api) + " · restored " + F(Time.timeScale));
                hand.Shutdown();
                Time.timeScale = 1f;
            }
            {
                // a tween back to 1 lets the request go in its last tick — the flush hands off then
                FeedbackScheduler hand = Hand();
                HandPlayer(hand, Scale(SetTo(0.5f), 0f)).Play();
                hand.Tick(0.05f);
                HandPlayer(hand, Scale(TweenTo(1f), 0.1f)).Play();
                hand.Tick(0.05f);
                float mid = Time.timeScale;
                hand.Tick(0.05f);
                Check(mid == 0.75f && Time.timeScale == 1f && !hand.Context.TimeScale.IsOwning && !hand.Context.TimeScale.HasOpen,
                    "a tween back to 1 — " + F(mid) + " → its last tick " + F(Time.timeScale) + " · owning " + hand.Context.TimeScale.IsOwning);
                hand.Shutdown();
                Time.timeScale = 1f;
            }
            {
                FeedbackScheduler hand = Hand();
                HandPlayer(hand, Scale(TweenTo(0.1f), 0.2f)).Play();
                for (int k = 0; k < 6; k++)
                    Emu(hand, 0.05f);
                Check(Math.Abs(Time.timeScale - 0.1f) < 1e-6f, "the unscaled clock — a tween to 0.1 ends in its length " + F(Time.timeScale));
                hand.Shutdown();
                Time.timeScale = 1f;
            }
            End();
        }

        // ---- the real loop — a Set in a game Update is seen the next frame (as a hit stop) · a Tween begun there the frame after
        // (its first step is the next frame's stage — the flush writes it then)
        [UnityTest]
        public IEnumerator CQ04b_TheRealLoop()
        {
            yield return null;
            var rec = Go("rec").AddComponent<TimeCustomFrameRecorder>();
            FeedbackPlayer set = LoopPlayer(Scale(SetTo(0.5f), 0f));
            FeedbackPlayer tween = LoopPlayer(Scale(TweenTo(0.5f), 0.1f));
            int setAt = Time.frameCount + 3;
            rec.ActAt = setAt;
            rec.Act = () => set.Play();
            for (int i = 0; i < 8; i++)
                yield return null;
            set.Restore();
            for (int i = 0; i < 4; i++)
                yield return null;
            int tweenAt = Time.frameCount + 3;
            rec.ActAt = tweenAt;
            rec.Act = () => tween.Play();
            for (int i = 0; i < 12; i++)
                yield return null;
            var setSeen = rec.Rows.FirstOrDefault(r => r.Frame > setAt && r.Scale == 0.5f);
            Check(setSeen.Frame == setAt + 1 && Math.Abs(setSeen.Delta - 0.5f / 60f) < 1e-6f, "a Set in a game Update — f" + (setSeen.Frame - setAt) + " · dt×60 " + F(setSeen.Delta * 60f));
            var tweenSeen = rec.Rows.FirstOrDefault(r => r.Frame > tweenAt && r.Scale < 1f);
            Check(tweenSeen.Frame == tweenAt + 2, "a Tween begun in a game Update — f" + (tweenSeen.Frame - tweenAt));
            End();
        }

        // ---- a hit stop over the slow motion wins, the slow motion goes on after it · the minimum condition reads the scale now (a
        // hit stop under a slow motion below its minimum is skipped) · Restore hands off
        [UnityTest]
        public IEnumerator CQ05a_TheHitStop()
        {
            yield return null;
            {
                FeedbackScheduler hand = Hand();
                FeedbackPlayer slow = HandPlayer(hand, Scale(SetTo(0.5f), 0f));
                slow.Play();
                hand.Tick(0.05f);
                HandPlayer(hand, Hit(0.1f)).Play();
                hand.Tick(0.05f);
                float during = Time.timeScale;
                for (int k = 0; k < 4; k++)
                    hand.Tick(0.05f);
                float after = Time.timeScale;
                slow.Restore();
                hand.Tick(0.05f);
                Check(during == 0f && after == 0.5f && Time.timeScale == 1f && !hand.Context.TimeScale.IsOwning,
                    "slow · freeze · slow · 1 — " + F(during) + " " + F(after) + " " + F(Time.timeScale));
                hand.Shutdown();
                Time.timeScale = 1f;
            }
            {
                FeedbackScheduler hand = Hand();
                HandPlayer(hand, Scale(SetTo(0.05f), 0f)).Play();
                hand.Tick(0.05f);
                HandPlayer(hand, Hit(0.1f)).Play();
                hand.Tick(0.05f);
                Check(Math.Abs(Time.timeScale - 0.05f) < 1e-6f && hand.Context.TimeScale.Count == 0, "below the minimum 0.1 the hit stop is skipped — " + F(Time.timeScale));
                hand.Shutdown();
                Time.timeScale = 1f;
            }
            End();
        }
    }
}
