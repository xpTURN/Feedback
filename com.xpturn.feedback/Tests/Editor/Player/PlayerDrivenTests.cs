using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the internal paths a track drives a play with — a driven play stands on the clock and is unconditional and nominal, one
    // play's seek and hold, Pause · Resume · Detach around a held play, a driven play's time scale. A player is bound to a test
    // scheduler that the row ticks
    public sealed class PlayerDrivenTests
    {
        static int s_Paused;
        static int s_Resumed;

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();

        static void OnPaused(FeedbackPlayer player) => s_Paused++;

        static void OnResumed(FeedbackPlayer player) => s_Resumed++;

        [SetUp]
        public void SetUp()
        {
            s_Paused = 0;
            s_Resumed = 0;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (FeedbackPlayer player in m_Players)
            {
                if (player != null)
                    player.ReleaseRuntime();
            }
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Players.Clear();
            m_Made.Clear();
            m_Schedulers.Clear();
        }

        FeedbackScheduler Scheduler()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            var go = new GameObject("Driven");
            m_Made.Add(go);
            var player = go.AddComponent<FeedbackPlayer>();
            player.m_Items.AddRange(items);
            player.Bind(s);
            Assert.That(player.EnsureRuntime(), Is.True, "prepared");
            player.Paused += OnPaused;
            player.Resumed += OnResumed;
            m_Players.Add(player);
            return player;
        }

        // x from 0 to 1 over the seconds, linear
        static PositionItem Line(float seconds)
        {
            TweenParams tween = TweenParams.Default;
            tween.m_To = new Vector4(1f, 0f, 0f, 0f);
            tween.m_Ease = Ease.Of(EaseType.Linear);
            var item = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = new TweenMotion { m_Params = tween } };
            item.m_Timing.m_Duration = seconds;
            return item;
        }

        [Test]
        public void PD01_ADrivenPlayStandsOnTheClock()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer driven = Player(s, Line(1f));
            FeedbackPlayer ticked = Player(s, Line(1f));
            PlaybackHandle d = driven.Play(PlayArgs.Default.WithDriven());
            PlaybackHandle t = ticked.Play();
            for (int i = 0; i < 3; i++)
                s.Tick(0.25f);
            Assert.That((d != default, driven.Position(d), driven.IsPlaying), Is.EqualTo((true, 0L, true)), "the driven play stands and plays");
            Assert.That(ticked.Position(t), Is.EqualTo(750000L), "the clock moves the other");
            Assert.That(driven.transform.localPosition.x, Is.EqualTo(0f));
        }

        [Test]
        public void PD02_SeekAtMovesOnePlay()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Line(1f));
            p.m_Policy = PlayPolicy.Overlap;
            p.Rebuild();
            PlaybackHandle d = p.Play(PlayArgs.Default.WithDriven());
            PlaybackHandle n = p.Play();
            s.Tick(0.1f);
            Assert.That(p.SeekAt(d, 500000), Is.True);
            Assert.That((p.Position(d), p.Position(n)), Is.EqualTo((500000L, 100000L)), "the driven play moved, the other did not");
            Assert.That(p.SeekAt(default, 0), Is.False, "no handle");
            p.Stop(d, StopMode.Hold);
            Assert.That(p.SeekAt(d, 0), Is.False, "an ended play");
        }

        [Test]
        public void PD03_SetHeldHoldsAndLetsGo()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Line(1f));
            PlaybackHandle n = p.Play();
            Assert.That(p.SetHeld(n, true), Is.True);
            s.Tick(0.2f);
            Assert.That(p.Position(n), Is.EqualTo(0L), "held");
            Assert.That(p.SetHeld(n, false), Is.True);
            s.Tick(0.2f);
            Assert.That(p.Position(n), Is.EqualTo(200000L), "let go");
            Assert.That(p.SetHeld(default, true), Is.False, "no handle");
            Assert.That((s_Paused, s_Resumed), Is.EqualTo((0, 0)), "no event");
        }

        [Test]
        public void PD04_PauseAndResumePassTheHeldPlay()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Line(1f));
            p.m_Policy = PlayPolicy.Overlap;
            p.Rebuild();
            PlaybackHandle d = p.Play(PlayArgs.Default.WithDriven());
            p.Pause();
            Assert.That((s_Paused, p.HasPaused), Is.EqualTo((0, false)), "a held play alone: nothing paused");
            PlaybackHandle n = p.Play();
            p.Pause();
            Assert.That((s_Paused, p.HasPaused), Is.EqualTo((1, true)), "the other paused once");
            p.Resume();
            s.Tick(0.2f);
            Assert.That((s_Resumed, p.Position(d), p.Position(n)), Is.EqualTo((1, 0L, 200000L)), "Resume lets the other run, not the driven one");
        }

        [Test]
        public void PD05_DetachEndsAHeldPlay()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer p = Player(s, Line(1f));
            p.m_Policy = PlayPolicy.Overlap;
            p.Rebuild();
            PlaybackHandle d = p.Play(PlayArgs.Default.WithDriven());
            PlaybackHandle n = p.Play();
            Assert.That(p.Runtime.Detach(), Is.True, "a play goes on");
            Assert.That((p.Position(d), p.Position(n)), Is.EqualTo((-1L, 0L)), "the held play ended, the other goes on");
        }

        [Test]
        public void PD06_ADrivenPlayIsUnconditional()
        {
            FeedbackScheduler s = Scheduler();
            FeedbackPlayer never = Player(s, Line(1f));
            never.m_Chance = 0f;
            never.Rebuild();
            Assert.That(never.Play(), Is.EqualTo(default(PlaybackHandle)), "chance 0 refuses a play");
            Assert.That(never.Play(PlayArgs.Default.WithUnconditional()), Is.EqualTo(default(PlaybackHandle)), "unconditional alone is a preview's");
            Assert.That(never.Play(PlayArgs.Default.WithDriven()), Is.Not.EqualTo(default(PlaybackHandle)), "a driven play passes the chance");
            FeedbackPlayer cooled = Player(s, Line(1f));
            cooled.m_Cooldown = 10f;
            cooled.m_Policy = PlayPolicy.Overlap;
            cooled.Rebuild();
            Assert.That(cooled.Play(), Is.Not.EqualTo(default(PlaybackHandle)));
            Assert.That(cooled.Play(), Is.EqualTo(default(PlaybackHandle)), "the cooldown refuses the second");
            Assert.That(cooled.Play(PlayArgs.Default.WithDriven()), Is.Not.EqualTo(default(PlaybackHandle)), "a driven play passes the cooldown");
        }

        [Test]
        public void PD07_ADrivenPlayIsNominal()
        {
            FeedbackScheduler s = Scheduler();
            PositionItem item = Line(1f);
            item.m_Random.m_RandomizeDuration = true;
            item.m_Random.m_DurationRange = new Vector2(2f, 3f);
            FeedbackPlayer p = Player(s, item);
            p.m_Policy = PlayPolicy.Overlap;
            p.Rebuild();
            PlaybackHandle n = p.Play(PlayArgs.Default.WithSeed(5));
            PlaybackHandle d = p.Play(PlayArgs.Default.WithSeed(5).WithDriven());
            Assert.That(p.GetDuration(n), Is.GreaterThan(1.5f), "a play's random length");
            Assert.That(p.GetDuration(d), Is.EqualTo(1f).Within(1e-6f), "the driven play's nominal length");
        }

        [Test]
        public void PD08_ADrivenPlayWritesNoTimeScale()
        {
            FeedbackScheduler s = Scheduler();
            TweenParams tween = TweenParams.Default;
            tween.m_To = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
            tween.m_Ease = Ease.Of(EaseType.Linear);
            var scale = new TimeScaleItem { m_Motion = new TweenMotion { m_Params = tween } };
            scale.m_Timing.m_Duration = 1f;
            FeedbackPlayer p = Player(s, scale, Line(1f));
            PlaybackHandle n = p.Play();
            s.Tick(0.5f);
            float played = s.Context.TimeScale.Current;
            p.Stop(n, StopMode.Revert);
            s.Tick(0f);
            Assert.That((played, s.Context.TimeScale.Current), Is.EqualTo((0.75f, 1f)).Using(new TupleTolerance()), "a play writes the scale, Revert puts it back");
            PlaybackHandle d = p.Play(PlayArgs.Default.WithDriven());
            Assert.That(p.SeekAt(d, 999999), Is.True);
            s.Tick(0f);
            Assert.That((s.Context.TimeScale.Current, p.transform.localPosition.x), Is.EqualTo((1f, 0.999999f)).Using(new TupleTolerance()), "the driven play moves the position, not the scale");
        }

        // (float, float) within 1e-5
        sealed class TupleTolerance : IEqualityComparer<(float, float)>
        {
            public bool Equals((float, float) a, (float, float) b) => Mathf.Abs(a.Item1 - b.Item1) < 1e-5f && Mathf.Abs(a.Item2 - b.Item2) < 1e-5f;

            public int GetHashCode((float, float) v) => 0;
        }
    }
}
