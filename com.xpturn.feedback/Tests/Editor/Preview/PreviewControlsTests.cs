using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the replays, the unconditional play and the seed, the scrub, the masks and the item's ▶, the repeat and the live
    // tuning. The session runs on the test's clock — an update is 1/120 s, a tick 1/60 s; a value is read at set times (two
    // updates may hold no tick). The live tuning's setting is on for each test and put back after
    public sealed class PreviewControlsTests
    {
        const string ScenePath = "Assets/PreviewControlsTests.unity";
        const double UpdateStep = 1.0 / 120.0;

        static double s_Now;

        IPreviewRecordSource m_Source;
        Func<double> m_Clock;
        Scene m_Scene;
        Scene m_Active;
        bool m_HadTuning;
        bool m_Tuning;

        [SetUp]
        public void SetUp()
        {
            m_Source = PreviewRecordPaths.Source;
            m_Clock = PreviewClock.Source;
            PreviewRecordPaths.Source = new PreviewRecordPathTable();
            s_Now = 0;
            PreviewClock.Source = () => s_Now;
            m_HadTuning = EditorPrefs.HasKey(PreviewSession.LiveTuningKey);
            m_Tuning = PreviewSession.LiveTuning;
            PreviewSession.LiveTuning = true;
            m_Active = SceneManager.GetActiveScene();
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            m_Scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(m_Scene);
        }

        [TearDown]
        public void TearDown()
        {
            PreviewSession.Current?.Stop();
            PreviewRecovery.Run();
            if (m_Scene.IsValid())
            {
                foreach (GameObject root in m_Scene.GetRootGameObjects())
                {
                    foreach (FeedbackPlayer player in root.GetComponentsInChildren<FeedbackPlayer>(true))
                        PreviewMasks.Clear(player);
                }
            }
            if (m_HadTuning)
                PreviewSession.LiveTuning = m_Tuning;
            else
                EditorPrefs.DeleteKey(PreviewSession.LiveTuningKey);
            PreviewRecordPaths.Source = m_Source;
            PreviewClock.Source = m_Clock;
            if (m_Active.IsValid())
                SceneManager.SetActiveScene(m_Active);
            if (m_Scene.IsValid())
                EditorSceneManager.CloseScene(m_Scene, true);
            AssetDatabase.DeleteAsset(ScenePath);
        }

        static void Pump(int updates)
        {
            for (int i = 0; i < updates; i++)
            {
                s_Now += UpdateStep;
                PreviewSession.Current?.Update();
            }
        }

        static void Seconds(double seconds) => Pump((int)Math.Round(seconds / UpdateStep));

        static PreviewSession S => PreviewSession.Current;

        static PlayArgs Free => PlayArgs.Default.WithUnconditional();

        static FeedbackPlayer Player(string name, float x = 0f)
        {
            var go = new GameObject(name);
            go.transform.position = new Vector3(x, 0f, 0f);
            return go.AddComponent<FeedbackPlayer>();
        }

        static T On<T>(FeedbackPlayer player, Object target, FeedbackMotion motion, float duration) where T : TargetItem, new()
        {
            var item = new T { m_Motion = motion };
            item.m_Target.m_Mode = target != null ? TargetRefMode.Direct : TargetRefMode.Self;
            item.m_Target.m_Object = target;
            item.m_Timing.m_Duration = duration;
            player.m_Items.Add(item);
            return item;
        }

        static TweenMotion Tween(float to) => new TweenMotion { m_Params = new TweenParams { m_To = new Vector4(to, to, to, to), m_Loops = 1 } };

        static ShakeMotion Shake() => new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = new Vector4(1f, 1f, 0f, 0f), m_Frequency = 7f } };

        static float X(Component c) => c.transform.position.x;

        // a 1 s tween of its own position from x 1 to 5
        static FeedbackPlayer Mover(float length = 1f)
        {
            FeedbackPlayer p = Player("Mover", 1f);
            On<PositionItem>(p, null, Tween(5f), length);
            return p;
        }

        // A (position 0.3 s) then B (scale 0.3 s, after A)
        static FeedbackPlayer TwoItems(out Transform a, out Transform b)
        {
            a = new GameObject("A").transform;
            b = new GameObject("B").transform;
            FeedbackPlayer p = Player("Items");
            On<PositionItem>(p, a, Tween(5f), 0.3f);
            On<ScaleItem>(p, b, Tween(2f), 0.3f).m_Timing.m_Rule = StartRule.AfterPrevious;
            return p;
        }

        // a shake of its own position with the output random (0.25 ~ 2 times)
        static FeedbackPlayer OutputRandom()
        {
            FeedbackPlayer p = Player("Out");
            PositionItem item = On<PositionItem>(p, null, Shake(), 1f);
            item.m_Random.m_RandomizeOutput = true;
            item.m_Random.m_OutputRange = new Vector2(0.25f, 2f);
            return p;
        }

        static MotionDriver Driver()
        {
            var driver = new GameObject("Driver").AddComponent<MotionDriver>();
            var item = new PositionItem { m_Motion = Shake() };
            item.m_Target.m_Mode = TargetRefMode.Self;
            item.m_Timing.m_Duration = 1f;
            driver.m_Item = item;
            return driver;
        }

        static MeshRenderer Renderer(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            return renderer;
        }

        // an edit of the player's duration through the inspector's path (Undo — the tuning's signal)
        static void EditDuration(FeedbackPlayer p, float value)
            => UserEdit.Inspector(p, "m_Items.Array.data[0].m_Timing.m_Duration", sp => sp.floatValue = value);

        // ---- the replays

        // a replay starts over from the start values — not from the value of the moment (Restart), not refused (Ignore)
        [Test]
        public void PX21a_TheSameSubjectStartsOverFromItsStartValues([Values(PlayPolicy.Restart, PlayPolicy.Ignore)] PlayPolicy policy)
        {
            FeedbackPlayer p = Mover();
            p.m_Policy = policy;
            PreviewSession.Start(p, PlayArgs.Default);
            Seconds(0.5);
            PreviewSession.Start(p, PlayArgs.Default);
            Seconds(0.25);
            Assert.That(X(p), Is.EqualTo(2f).Within(0.12f));
        }

        [Test]
        public void PX21b_APausedPlayStartedAgainStartsOver()
        {
            FeedbackPlayer p = Mover();
            PreviewSession.Start(p, Free);
            S.Scrub(0.6);
            PreviewSession.Start(p, Free);
            Assert.That(p.HasPaused, Is.False, "the pause goes");
            Seconds(0.25);
            Assert.That(X(p), Is.EqualTo(2f).Within(0.12f));
        }

        [Test]
        public void PX21c_PlayWithoutAPausedPlayStartsOver()
        {
            FeedbackPlayer p = Mover();
            PreviewSession.Start(p, Free);
            Seconds(0.5);
            S.PlayButton();
            Seconds(0.25);
            Assert.That(X(p), Is.EqualTo(2f).Within(0.12f));
        }

        [Test]
        public void PX21d_ADriversPlayStartsItOver()
        {
            MotionDriver driver = Driver();
            PreviewSession.Start(driver);
            Seconds(0.4);
            S.PlayButton();
            Assert.That(S, Is.Not.Null);
            Assert.That(driver.Seconds, Is.LessThan(0.05));
        }

        // the mark off: a replay refused by the cooldown leaves nothing playing — the session keeps the refusal and ends
        [Test]
        public void PX21e_ARefusedReplayIsKeptAndTheSessionEnds()
        {
            FeedbackPlayer p = Mover();
            p.m_Cooldown = 10f;
            PreviewSession.Start(p, PlayArgs.Default);
            Seconds(0.2);
            PreviewSession s = S;
            s.PlayButton();
            Assert.That(s.LastReplayRefused, Is.True);
            Seconds(0.05);
            Assert.That(S, Is.Null);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.AutoEnd));
            Assert.That(X(p), Is.EqualTo(1f));
        }

        // ▶ inside the tuning's wait: the tuning first — one replay, with the new length
        [Test]
        public void PX21f_APlayInsideTheTuningsWaitTunesFirst()
        {
            FeedbackPlayer p = Mover();
            int played = 0;
            p.Played += (pl, h) => played++;
            PreviewSession.Start(p, Free);
            Seconds(0.3);
            EditDuration(p, 2f);
            Seconds(0.05);
            S.PlayButton();
            int after = played;
            Assert.That(p.GetDuration(S.Handle), Is.EqualTo(2f).Within(0.01f));
            Seconds(0.2);
            Assert.That(played, Is.EqualTo(after), "no second replay");
        }

        // ---- the unconditional play and the seed

        [Test]
        public void PX12a_TheMarkPassesThePlayersAndTheItemsConditions(
            [Values("player chance", "player cooldown", "distance", "item chance", "item cooldown", "max plays")] string condition)
        {
            FeedbackPlayer p = Player("Cond");
            PositionItem item = On<PositionItem>(p, null, Tween(5f), 0.5f);
            bool replay = false;
            PlayArgs args = Free;
            switch (condition)
            {
                case "player chance": p.m_Chance = 0f; break;
                case "player cooldown": p.m_Cooldown = 10f; replay = true; break;
                case "distance":
                    p.m_UseDistanceGate = true;
                    p.m_GateReference = DistanceReference.PlayPosition;
                    p.m_GateDistance = 1f;
                    args = Free.WithPosition(new Vector3(100f, 0f, 0f));
                    break;
                case "item chance": item.m_Conditions.m_Chance = 0f; break;
                case "item cooldown": item.m_Conditions.m_Cooldown = 10f; replay = true; break;
                case "max plays": item.m_Conditions.m_MaxPlays = 1; replay = true; break;
            }
            PreviewSession.Start(p, args);
            Seconds(0.1);
            Assert.That(p.IsPlaying, Is.True);
            Assert.That(X(p), Is.GreaterThan(0.3f), "the item runs");
            if (!replay)
                return;
            S.PlayButton();
            Seconds(0.1);
            Assert.That(p.IsPlaying, Is.True, "the replay");
            Assert.That(X(p), Is.InRange(0.3f, 1.5f), "the replay's item runs from its start");
        }

        [Test]
        public void PX12b_TheMarkKeepsTheNominalLength([Values(true, false)] bool onTheItem)
        {
            FeedbackPlayer p = Player("Random");
            PositionItem item = On<PositionItem>(p, null, Tween(5f), 0.5f);
            if (onTheItem)
            {
                item.m_Random.m_RandomizeDuration = true;
                item.m_Random.m_DurationRange = new Vector2(2f, 3f);
            }
            else
            {
                p.m_RandomizeDuration = true;
                p.m_DurationRange = new Vector2(2f, 3f);
            }
            PreviewSession.Start(p, Free);
            Assert.That(p.GetDuration(S.Handle), Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void PX12c_TheDirectionAndIntensityConditionsStay([Values(true, false)] bool direction)
        {
            FeedbackPlayer p = Player("Cond");
            PositionItem item = On<PositionItem>(p, null, Tween(5f), 0.5f);
            if (direction)
                item.m_Conditions.m_Direction = DirectionCondition.ReverseOnly;
            else
            {
                item.m_Conditions.m_UseIntensityRange = true;
                item.m_Conditions.m_IntensityRange = new Vector2(2f, 3f);
            }
            PreviewSession.Start(p, Free);
            Seconds(0.1);
            Assert.That(X(p), Is.EqualTo(0f));
        }

        // the refusal is seen both ways — the kept refusal and nothing playing
        [Test]
        public void PX12d_WithoutTheMarkTheConditionsRefuseAsInPlay()
        {
            FeedbackPlayer p = Player("Plain");
            p.m_Chance = 0f;
            On<PositionItem>(p, null, Tween(5f), 0.5f);
            PreviewSession.Start(p, PlayArgs.Default);
            Assert.That(S.LastReplayRefused, Is.True);
            Assert.That(p.IsPlaying, Is.False);
            S.Stop();
            FeedbackPlayer q = Player("Skipped");
            On<PositionItem>(q, null, Tween(5f), 0.5f).m_Conditions.m_Chance = 0f;
            PreviewSession.Start(q, PlayArgs.Default);
            Seconds(0.1);
            Assert.That(X(q), Is.EqualTo(0f), "the item is skipped");
        }

        // the output random's value at 0.5 s: the session seed's replay is the same, a new seed another, the caller's twice
        [Test]
        public void PX12e_TheSessionSeedNewSeedAndTheCallersSeed()
        {
            FeedbackPlayer p = OutputRandom();
            PreviewSession.Start(p, Free);
            S.Scrub(0.5);
            float a = X(p);
            PreviewSession.Start(p, Free);
            S.Scrub(0.5);
            Assert.That(X(p), Is.EqualTo(a), "the replay");
            S.NewSeed();
            S.Scrub(0.5);
            Assert.That(X(p), Is.Not.EqualTo(a), "a new seed");
            PreviewSession.Start(p, Free.WithSeed(7u));
            S.Scrub(0.5);
            float d = X(p);
            PreviewSession.Start(p, Free.WithSeed(7u));
            S.Scrub(0.5);
            Assert.That(X(p), Is.EqualTo(d), "the caller's seed");
        }

        [Test]
        public void PX12f_TheMarkCrossesARebuild()
        {
            FeedbackPlayer p = Mover(0.5f);
            p.m_Chance = 0f;
            PreviewSession.Start(p, Free);
            Seconds(0.1);
            PlaybackHandle h = S.Handle;
            EditDuration(p, 0.6f);
            Seconds(0.2);
            Assert.That(S, Is.Not.Null);
            Assert.That(S.Handle, Is.Not.EqualTo(h), "replayed");
            Assert.That(p.IsPlaying, Is.True);
        }

        [Test]
        public void PX12g_ATestContextDoesNotReadTheMark()
        {
            FeedbackPlayer p = Mover(0.5f);
            p.m_Chance = 0f;
            var scheduler = new FeedbackScheduler(ContextKind.Test);
            p.Bind(scheduler);
            try
            {
                p.EnsureRuntime();
                Assert.That(p.Play(Free), Is.EqualTo(default(PlaybackHandle)));
            }
            finally
            {
                p.ReleaseRuntime();
                p.Bind(null);
                scheduler.Shutdown();
            }
        }

        // the gate's falloff (0.5 at 5 of 10): with the mark the shake is twice the one without
        [Test]
        public void PX12h_TheMarkTakesTheFalloffOffToo()
        {
            var x = new float[2];
            for (int k = 0; k < 2; k++)
            {
                FeedbackPlayer p = Player("Far" + k);
                p.m_UseDistanceGate = true;
                p.m_GateReference = DistanceReference.PlayPosition;
                p.m_GateDistance = 10f;
                p.m_UseGateFalloff = true;
                p.m_GateFalloff = AnimationCurve.Constant(0f, 1f, 0.5f);
                On<PositionItem>(p, null, Shake(), 1f);
                PlayArgs args = PlayArgs.Default.WithSeed(7u).WithPosition(new Vector3(5f, 0f, 0f));
                PreviewSession.Start(p, k == 0 ? args.WithUnconditional() : args);
                S.Scrub(0.137);
                x[k] = X(p);
                S.Stop();
            }
            Assert.That(x[1], Is.Not.EqualTo(0f));
            Assert.That(x[0] / x[1], Is.EqualTo(2f).Within(0.01f));
        }

        // the session never adds the mark: the arguments without it refuse at the next replay
        [Test]
        public void PX12i_TheSessionDoesNotAddTheMark()
        {
            FeedbackPlayer p = Mover(0.5f);
            p.m_Chance = 0f;
            PreviewSession.Start(p, Free);
            Seconds(0.1);
            S.Args = PlayArgs.Default;
            S.PlayButton();
            Assert.That(S.LastReplayRefused, Is.True);
        }

        [Test]
        public void PX12j_ANewSeedWhilePausedStaysAtItsPlace()
        {
            FeedbackPlayer p = OutputRandom();
            PreviewSession.Start(p, Free);
            S.Scrub(0.3);
            S.NewSeed();
            S.ScrubState(out _, out _, out double place);
            Assert.That(p.HasPaused, Is.True);
            Assert.That(place, Is.EqualTo(0.3).Within(0.001));
        }

        // the output random comes by the seed with the mark or without it
        [Test]
        public void PX12k_TheOutputRandomStaysWithTheMark()
        {
            FeedbackPlayer p = OutputRandom();
            PreviewSession.Start(p, Free.WithSeed(11u));
            S.Scrub(0.5);
            float marked = X(p);
            PreviewSession.Start(p, PlayArgs.Default.WithSeed(11u));
            S.Scrub(0.5);
            Assert.That(X(p), Is.EqualTo(marked));
        }

        [Test]
        public void PX12l_EachSessionHasItsOwnSeed()
        {
            FeedbackPlayer p = OutputRandom();
            PreviewSession.Start(p, Free);
            uint first = S.Seed;
            S.Stop();
            PreviewSession.Start(p, Free);
            Assert.That(S.Seed, Is.Not.EqualTo(first));
        }

        // ---- the scrub

        [Test]
        public void PX13a_TheValueAtTheTime()
        {
            FeedbackPlayer p = Mover();
            PreviewSession.Start(p, Free);
            Assert.That(S.Scrub(0.25), Is.EqualTo(SeekRefusal.None));
            Assert.That(X(p), Is.EqualTo(2f).Within(0.01f));
            Assert.That(S.Scrub(0.75), Is.EqualTo(SeekRefusal.None));
            Assert.That(X(p), Is.EqualTo(4f).Within(0.01f));
        }

        [Test]
        public void PX13b_ABackwardPlayHasTheSameValues()
        {
            FeedbackPlayer p = Mover();
            PreviewSession.Start(p, Free.WithDirection(PlayerDirection.Backward));
            Assert.That(S.Scrub(0.25), Is.EqualTo(SeekRefusal.None));
            Assert.That(X(p), Is.EqualTo(2f).Within(0.01f));
            Assert.That(S.Scrub(0.75), Is.EqualTo(SeekRefusal.None));
            Assert.That(X(p), Is.EqualTo(4f).Within(0.01f));
        }

        [Test]
        public void PX13c_TheRefusals()
        {
            FeedbackPlayer e = Player("Endless");
            e.m_Items.Add(new LoopStartItem());
            On<PositionItem>(e, null, Tween(1f), 0.1f);
            e.m_Items.Add(new LoopEndItem { m_LoopCount = 0 });
            PreviewSession.Start(e, Free);
            Assert.That(S.Scrub(0.05), Is.EqualTo(SeekRefusal.Endless));
            S.Stop();
            // a wait without a timeout — its program is endless too
            FeedbackPlayer u = Player("Untimed");
            On<PositionItem>(u, null, Tween(2f), 0.1f);
            var wait = new WaitForSignalItem();
            wait.m_Timing.m_Rule = StartRule.AfterPrevious;
            u.m_Items.Add(wait);
            PreviewSession.Start(u, Free);
            Assert.That(S.Scrub(0.05), Is.EqualTo(SeekRefusal.UntimedWait));
            S.Stop();
            // the play has ended — the repeat holds the session
            FeedbackPlayer f = Mover(0.2f);
            PreviewSession.Start(f, Free);
            S.Repeat = true;
            Seconds(0.3);
            Assert.That(S.Scrub(0.1), Is.EqualTo(SeekRefusal.NotLive));
        }

        // off the nominal axis — a hit stop's freeze, a signal before its timeout: refused, nothing moved
        [Test]
        public void PX13d_OffTheNominalAxisIsRefused()
        {
            FeedbackPlayer p = Player("Stopper", 1f);
            var stop = new HitStopItem { m_FreezeDuration = 0.2f, m_TimeScale = 0f, m_MinimumTimeScale = 0f };
            stop.m_Timing.m_Delay = 0.1f;
            p.m_Items.Add(stop);
            On<PositionItem>(p, null, Tween(5f), 1f).m_Timing.m_Clock = ItemClock.Scaled;
            PreviewSession.Start(p, Free);
            Seconds(0.25);
            float x = X(p);
            Assert.That(S.Scrub(0.6), Is.EqualTo(SeekRefusal.OffNominal));
            Assert.That(X(p), Is.EqualTo(x));
            Assert.That(p.HasPaused, Is.False);
            S.Stop();
            FeedbackPlayer w = Player("Waiter", 1f);
            On<PositionItem>(w, null, Tween(2f), 0.1f);
            var wait = new WaitForSignalItem { m_Timeout = 0.5f };
            wait.m_Timing.m_Rule = StartRule.AfterPrevious;
            w.m_Items.Add(wait);
            On<PositionItem>(w, null, Tween(5f), 0.3f).m_Timing.m_Rule = StartRule.AfterPrevious;
            PreviewSession.Start(w, Free);
            Seconds(0.2);
            w.Signal();
            Seconds(0.05);
            Assert.That(S.Scrub(0.3), Is.EqualTo(SeekRefusal.OffNominal));
        }

        // the end: 1 µs before it — the session lives (a 40 s list too: float seconds would end it)
        [Test]
        public void PX13e_TheEndIsCutAMicrosecondBefore([Values(1f, 40f)] float length)
        {
            FeedbackPlayer p = Mover(length);
            PreviewSession.Start(p, Free);
            S.Scrub(length + 1f);
            Seconds(0.05);
            Assert.That(S, Is.Not.Null);
            Assert.That(p.IsPlaying, Is.True);
            Assert.That(X(p), Is.EqualTo(5f).Within(0.01f));
        }

        [Test]
        public void PX13f_PlayAfterAScrubGoesOn()
        {
            FeedbackPlayer p = Mover();
            PreviewSession.Start(p, Free);
            S.Scrub(0.4);
            PlaybackHandle h = S.Handle;
            S.PlayButton();
            Assert.That(p.HasPaused, Is.False);
            Assert.That(S.Handle, Is.EqualTo(h), "the same play");
            Seconds(0.1);
            Assert.That(X(p), Is.EqualTo(3f).Within(0.12f));
        }

        // a backward play's 0 is cut to the start delay + 1 µs — the session lives
        [Test]
        public void PX13g_ABackwardPlaysStartIsCut()
        {
            FeedbackPlayer p = Mover();
            PreviewSession.Start(p, Free.WithDirection(PlayerDirection.Backward));
            S.Scrub(0);
            Seconds(0.05);
            Assert.That(S, Is.Not.Null);
            Assert.That(p.IsPlaying, Is.True);
            Assert.That(X(p), Is.EqualTo(1f).Within(0.01f));
        }

        [Test]
        public void PX13h_TheBarsMaterial()
        {
            FeedbackPlayer p = Mover();
            PreviewSession.Start(p, Free);
            S.Scrub(0.25);
            Assert.That(S.ScrubState(out double low, out double high, out double place), Is.EqualTo(SeekRefusal.None));
            Assert.That(low, Is.EqualTo(0));
            Assert.That(high, Is.EqualTo(0.999999).Within(1e-9));
            Assert.That(place, Is.EqualTo(0.25).Within(1e-6));
        }

        [Test]
        public void PX13i_AStartDelayAndALoop()
        {
            FeedbackPlayer p = Mover();
            p.m_StartDelay = 0.2f;
            PreviewSession.Start(p, Free);
            S.Scrub(0.1);
            Assert.That(X(p), Is.EqualTo(1f).Within(0.01f), "inside the delay");
            S.Scrub(0.7);
            Assert.That(X(p), Is.EqualTo(3f).Within(0.01f));
            S.Stop();
            FeedbackPlayer l = Player("Looped", 1f);
            l.m_Items.Add(new LoopStartItem());
            On<PositionItem>(l, null, Tween(5f), 0.2f);
            l.m_Items.Add(new LoopEndItem { m_LoopCount = 3 });
            PreviewSession.Start(l, Free);
            S.Scrub(0.3);
            Assert.That(X(l), Is.EqualTo(3f).Within(0.01f), "the second run");
            S.Scrub(0.55);
            Assert.That(X(l), Is.EqualTo(4f).Within(0.01f), "the third run");
        }

        // an offset motion's value at a scrubbed place is the forward play's value there — the zero tick draws it
        [Test]
        public void PX13j_AnOffsetMotionsScrubbedValue()
        {
            FeedbackPlayer p = Player("Shaker");
            On<PositionItem>(p, null, Shake(), 1f);
            PreviewSession.Start(p, Free.WithSeed(5u));
            Seconds(0.3);
            float forward = X(p);
            S.ScrubState(out _, out _, out double place);
            Assert.That(forward, Is.Not.EqualTo(0f), "the shake is off its start");
            // a new session from the start values — the place's value is not left over from the forward play
            S.Stop();
            PreviewSession.Start(p, Free.WithSeed(5u));
            Assert.That(X(p), Is.Not.EqualTo(forward).Within(1e-4f), "the value before the scrub");
            S.Scrub(place);
            Assert.That(X(p), Is.EqualTo(forward).Within(1e-4f));
        }

        // ---- the masks and the item's ▶

        [Test]
        public void PX14a_AMutedItemKeepsItsPlace()
        {
            FeedbackPlayer p = TwoItems(out Transform a, out Transform b);
            PreviewMasks.Set(p, 0, PreviewMask.Mute);
            PreviewSession.Start(p, Free);
            Seconds(0.4);
            Assert.That(a.position.x, Is.EqualTo(0f));
            Assert.That(b.localScale.x, Is.GreaterThan(1.1f), "B at its own time");
        }

        [Test]
        public void PX14b_ASoloHidesTheOtherEffectItems()
        {
            FeedbackPlayer p = TwoItems(out Transform a, out Transform b);
            PreviewMasks.Set(p, 1, PreviewMask.Solo);
            PreviewSession.Start(p, Free);
            Seconds(0.4);
            Assert.That(a.position.x, Is.EqualTo(0f));
            Assert.That(b.localScale.x, Is.GreaterThan(1.1f));
        }

        [Test]
        public void PX14c_TheItemsPlayWithItsDelayIgnoredOrKept()
        {
            FeedbackPlayer p = TwoItems(out Transform a, out Transform b);
            PreviewSession.Start(p, Free);
            S.PlayItem(1, true);
            Seconds(0.1);
            Assert.That(a.position.x, Is.EqualTo(0f));
            Assert.That(b.localScale.x, Is.GreaterThan(1.2f), "at once");
            S.PlayItem(1, false);
            Seconds(0.1);
            Assert.That(a.position.x, Is.EqualTo(0f));
            Assert.That(b.localScale.x, Is.EqualTo(1f), "after its delay");
        }

        [Test]
        public void PX14d_TheMasksAreNotSaved()
        {
            FeedbackPlayer p = TwoItems(out _, out _);
            EditorSceneManager.SaveScene(m_Scene);
            string before = File.ReadAllText(Path.GetFullPath(ScenePath));
            PreviewMasks.Set(p, 0, PreviewMask.Mute);
            EditorSceneManager.MarkSceneDirty(m_Scene);
            EditorSceneManager.SaveScene(m_Scene);
            Assert.That(File.ReadAllText(Path.GetFullPath(ScenePath)), Is.EqualTo(before));
        }

        // a copy has the item's managed reference id — the player's entity id keeps the tables apart
        [Test]
        public void PX14e_ACopyHasItsOwnMasks()
        {
            FeedbackPlayer p = TwoItems(out _, out _);
            new SerializedObject(p).Update();
            FeedbackPlayer copy = Object.Instantiate(p.gameObject).GetComponent<FeedbackPlayer>();
            new SerializedObject(copy).Update();
            PreviewMasks.Set(p, 0, PreviewMask.Mute);
            Assert.That(PreviewMasks.Get(p, 0), Is.EqualTo(PreviewMask.Mute));
            Assert.That(PreviewMasks.Get(copy, 0), Is.EqualTo(PreviewMask.None));
        }

        [Test]
        public void PX14f_ATestContextDoesNotReadTheMasks()
        {
            FeedbackPlayer p = Mover(0.5f);
            p.m_PreviewMasks = new byte[] { 1 };
            var scheduler = new FeedbackScheduler(ContextKind.Test);
            p.Bind(scheduler);
            try
            {
                p.EnsureRuntime();
                p.Play();
                scheduler.Tick(0.1f);
                Assert.That(X(p), Is.GreaterThan(1.2f));
            }
            finally
            {
                p.ReleaseRuntime();
                p.Bind(null);
                scheduler.Shutdown();
                p.m_PreviewMasks = null;
            }
        }

        [Test]
        public void PX14g_AReorderFollowsTheItem()
        {
            FeedbackPlayer p = TwoItems(out _, out _);
            PreviewMasks.Set(p, 0, PreviewMask.Mute);
            (p.m_Items[0], p.m_Items[1]) = (p.m_Items[1], p.m_Items[0]);
            new SerializedObject(p).Update();
            PreviewMasks.Apply(p);
            Assert.That(p.m_PreviewMasks, Is.EqualTo(new byte[] { 0, 1 }));
        }

        // a backward play meets the item at its end — the seek goes there and it plays at once
        [Test]
        public void PX14h_ABackwardItemsPlayGoesToItsEnd()
        {
            FeedbackPlayer p = TwoItems(out Transform a, out Transform b);
            a.position = new Vector3(1f, 0f, 0f);
            PreviewSession.Start(p, Free.WithDirection(PlayerDirection.Backward));
            S.PlayItem(0, true);
            Seconds(0.1);
            Assert.That(a.position.x, Is.GreaterThan(3f));
            Assert.That(b.localScale.x, Is.EqualTo(1f));
        }

        // an item put before it: the tuning plays the same item — found by its managed reference id
        [Test]
        public void PX14i_TheItemModeFollowsItsItem()
        {
            FeedbackPlayer p = TwoItems(out Transform a, out Transform b);
            Transform c = new GameObject("C").transform;
            PreviewSession.Start(p, Free);
            S.PlayItem(1, true);
            Seconds(0.1);
            UserEdit.Inspector(p, "m_Items", sp =>
            {
                sp.InsertArrayElementAtIndex(0);
                var rotation = new RotationItem { m_Motion = Tween(90f) };
                rotation.m_Target.m_Mode = TargetRefMode.Direct;
                rotation.m_Target.m_Object = c;
                rotation.m_Timing.m_Duration = 0.3f;
                sp.GetArrayElementAtIndex(0).managedReferenceValue = rotation;
            });
            Seconds(0.3);
            Assert.That(a.position.x, Is.EqualTo(0f));
            Assert.That(c.eulerAngles.z, Is.EqualTo(0f));
            Assert.That(b.localScale.x, Is.GreaterThan(1.1f));
        }

        // a flow item is never masked — the Wait keeps its place under a Solo
        [Test]
        public void PX14j_AFlowItemKeepsItsPlaceUnderASolo()
        {
            Transform a = new GameObject("A").transform;
            Transform b = new GameObject("B").transform;
            FeedbackPlayer f = Player("Flow");
            On<PositionItem>(f, a, Tween(5f), 0.3f);
            var wait = new WaitItem();
            wait.m_Timing.m_Duration = 0.2f;
            wait.m_Timing.m_Rule = StartRule.AfterPrevious;
            f.m_Items.Add(wait);
            On<ScaleItem>(f, b, Tween(2f), 0.3f).m_Timing.m_Rule = StartRule.AfterPrevious;
            PreviewMasks.Set(f, 2, PreviewMask.Solo);
            PreviewSession.Start(f, Free);
            Seconds(0.4);
            Assert.That(b.localScale.x, Is.EqualTo(1f), "B waits for A and the Wait");
            Seconds(0.2);
            Assert.That(b.localScale.x, Is.GreaterThan(1.1f));
            Assert.That(a.position.x, Is.EqualTo(0f));
        }

        [Test]
        public void PX14k_TheEndClearsThePlayersTable()
        {
            FeedbackPlayer p = TwoItems(out _, out _);
            PreviewMasks.Set(p, 0, PreviewMask.Mute);
            PreviewSession.Start(p, Free);
            Seconds(0.1);
            S.Stop();
            Assert.That(p.m_PreviewMasks, Is.Null);
            Assert.That(PreviewMasks.Get(p, 0), Is.EqualTo(PreviewMask.Mute), "the choice stays");
        }

        [Test]
        public void PX14l_PlayAfterTheItemsPlayPlaysTheWholeList()
        {
            FeedbackPlayer p = TwoItems(out Transform a, out _);
            PreviewSession.Start(p, Free);
            S.PlayItem(1, true);
            Seconds(0.1);
            S.PlayButton();
            Seconds(0.1);
            Assert.That(a.position.x, Is.GreaterThan(1f));
        }

        // a mask chosen during the session comes in at the next play
        [Test]
        public void PX14m_AMaskChosenDuringTheSessionComesNextPlay()
        {
            FeedbackPlayer p = TwoItems(out Transform a, out _);
            PreviewSession.Start(p, Free);
            Seconds(0.1);
            float x = a.position.x;
            PreviewMasks.Set(p, 0, PreviewMask.Mute);
            Seconds(0.05);
            Assert.That(a.position.x, Is.GreaterThan(x), "this play goes on");
            S.PlayButton();
            Seconds(0.1);
            Assert.That(a.position.x, Is.EqualTo(0f), "the next play");
        }

        // ---- the live tuning and the repeat

        // the wait (editor clock), then in the tuning's update the new runtime and the replay from the start values
        [Test]
        public void PX15a_ATuningPlaysAgainWithTheNewValue()
        {
            FeedbackPlayer p = Mover();
            PreviewSession.Start(p, Free);
            Seconds(0.3);
            PlaybackHandle h = S.Handle;
            PlayerRuntime runtime = p.Runtime;
            double editAt = s_Now;
            EditDuration(p, 2f);
            while (p.Runtime == runtime && s_Now < editAt + 0.5)
            {
                Assert.That(S.Handle, Is.EqualTo(h), "waiting");
                Pump(1);
            }
            Assert.That(s_Now - editAt, Is.GreaterThanOrEqualTo(PreviewSession.TuneWait - 1e-9), "after the wait");
            Assert.That(p.Runtime, Is.Not.SameAs(runtime), "rebuilt");
            Assert.That(S.Handle, Is.Not.EqualTo(h), "replayed in the same update");
            Assert.That(p.GetDuration(S.Handle), Is.EqualTo(2f).Within(0.001f));
            Seconds(0.1);
            Assert.That(X(p), Is.LessThan(1.6f), "from the start values");
        }

        [Test]
        public void PX15b_ASecondEditRestartsTheWait()
        {
            FeedbackPlayer p = Mover();
            PreviewSession.Start(p, Free);
            Seconds(0.3);
            PlaybackHandle h = S.Handle;
            EditDuration(p, 2f);
            Seconds(0.1);
            EditDuration(p, 3f);
            Seconds(0.1);
            Assert.That(S.Handle, Is.EqualTo(h), "still waiting");
            Seconds(0.1);
            Assert.That(p.GetDuration(S.Handle), Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void PX15c_TheEndPutsTheStartValuesBack()
        {
            FeedbackPlayer p = Mover();
            PreviewSession.Start(p, Free);
            Seconds(0.3);
            EditDuration(p, 2f);
            Seconds(0.4);
            S.Stop();
            Assert.That(X(p), Is.EqualTo(1f));
        }

        [Test]
        public void PX15d_TheTuningOff()
        {
            FeedbackPlayer p = Mover();
            PreviewSession.LiveTuning = false;
            PreviewSession.Start(p, Free);
            Seconds(0.3);
            PlaybackHandle h = S.Handle;
            EditDuration(p, 2f);
            Seconds(0.3);
            Assert.That(S.Handle, Is.EqualTo(h));
        }

        // a play that ends inside the wait: no auto end before the tuning
        [Test]
        public void PX15e_TheWaitHoldsTheAutoEnd()
        {
            FeedbackPlayer p = Mover(0.1f);
            PreviewSession.Start(p, Free);
            Seconds(0.05);
            EditDuration(p, 0.3f);
            Seconds(0.1);
            Assert.That(S, Is.Not.Null, "waiting");
            Seconds(0.1);
            Assert.That(S, Is.Not.Null);
            Assert.That(p.IsPlaying, Is.True, "replayed");
        }

        // a paused play's tuning shows the new value at its place, paused
        [Test]
        public void PX15f_ATuningWhilePausedStaysAtItsPlace()
        {
            FeedbackPlayer p = Mover();
            PreviewSession.Start(p, Free);
            S.Scrub(0.5);
            UserEdit.Inspector(p, "m_Items.Array.data[0].m_Motion.m_Params.m_To.x", sp => sp.floatValue = 9f);
            Seconds(0.2);
            Assert.That(p.HasPaused, Is.True);
            Assert.That(X(p), Is.EqualTo(5f).Within(0.01f));
        }

        [Test]
        public void PX15g_TheRepeatPlaysAgainFromTheStartValues()
        {
            FeedbackPlayer p = Mover(0.3f);
            PreviewSession.Start(p, Free);
            S.Repeat = true;
            Seconds(0.35);
            Assert.That(X(p), Is.EqualTo(5f).Within(0.01f), "the end");
            Seconds(0.25);
            Assert.That(S, Is.Not.Null);
            Assert.That(X(p), Is.LessThan(3f));
        }

        [Test]
        public void PX15h_TheRepeatsGapHoldsTheAutoEnd()
        {
            FeedbackPlayer p = Mover(0.3f);
            PreviewSession.Start(p, Free);
            S.Repeat = true;
            Seconds(0.42);
            Assert.That(S, Is.Not.Null);
        }

        // the gap is on the session clock: at half speed a 0.4 s list and its 0.2 s gap take 1.2 s — two plays in 2.3 s
        [Test]
        public void PX15i_TheGapFollowsTheSpeed()
        {
            Transform a = new GameObject("A").transform;
            Transform b = new GameObject("B").transform;
            FeedbackPlayer p = Player("Rep");
            On<PositionItem>(p, a, Tween(5f), 0.2f);
            On<ScaleItem>(p, b, Tween(2f), 0.2f).m_Timing.m_Rule = StartRule.AfterPrevious;
            int played = 0;
            p.Played += (pl, h) => played++;
            PreviewSession.Start(p, Free);
            S.Repeat = true;
            S.Speed = 0.5f;
            Seconds(2.3);
            Assert.That(played, Is.EqualTo(2));
        }

        [Test]
        public void PX15j_TheRepeatKeepsTheItemMode()
        {
            FeedbackPlayer p = TwoItems(out Transform a, out Transform b);
            p.m_Items[0].m_Timing.m_Duration = 0.2f;
            p.m_Items[1].m_Timing.m_Duration = 0.2f;
            PreviewSession.Start(p, Free);
            S.Repeat = true;
            S.PlayItem(1, true);
            Seconds(0.5);
            Assert.That(a.position.x, Is.EqualTo(0f));
            Assert.That(b.localScale.x, Is.InRange(1.2f, 1.9f), "B again, its delay ignored");
        }

        // a renderer the tuning's Rebuild records late, then the lock lost: the zero tick took our block off before it
        [Test]
        public void PX15k_ALateRecordedRendererAfterALostLock()
        {
            MeshRenderer renderer = Renderer("Late");
            var flash = new FlashMotion();
            flash.m_Params.m_Target = new Vector4(1f, 0f, 0f, 1f);
            var player = renderer.gameObject.AddComponent<FeedbackPlayer>();
            On<MaterialColorItem>(player, null, flash, 1f);
            PreviewSession.Start(player, Free);
            PreviewSession s = S;
            Seconds(0.1);
            UserEdit.Inspector(player, "m_Items", sp =>
            {
                sp.InsertArrayElementAtIndex(1);
                var late = new MaterialColorItem { m_Motion = Tween(0f), m_WriteMode = MaterialWriteMode.Instance, m_PropertyName = "_EmissionColor" };
                late.m_Target.m_Mode = TargetRefMode.Self;
                late.m_Timing.m_Duration = 1f;
                sp.GetArrayElementAtIndex(1).managedReferenceValue = late;
            });
            Seconds(0.2);
            Assert.That(AnimationMode.IsPropertyAnimated(renderer, "m_Materials.Array.data[0]"), Is.True, "recorded late");
            Object.DestroyImmediate(s.Driver.Instance);
            Pump(2);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.LockLost));
            Assert.That(renderer.HasPropertyBlock(), Is.False);
        }

        [Test]
        public void PX15l_AnEditOfATargetIsNoTuning()
        {
            var light = new GameObject("Lamp").AddComponent<Light>();
            light.intensity = 1f;
            FeedbackPlayer p = Player("Lighter");
            On<LightIntensityItem>(p, light, Tween(3f), 1f);
            PreviewSession.Start(p, Free);
            Seconds(0.2);
            PlaybackHandle h = S.Handle;
            UserEdit.Inspector(light, "m_Color.r", sp => sp.floatValue = 0.25f);
            Seconds(0.3);
            Assert.That(S, Is.Not.Null);
            Assert.That(S.Handle, Is.EqualTo(h));
        }

        [Test]
        public void PX15m_TheTuningEditsUndoEndsTheSession()
        {
            FeedbackPlayer p = Mover();
            PreviewSession.Start(p, Free);
            Seconds(0.2);
            EditDuration(p, 3f);
            Undo.PerformUndo();
            Pump(2);
            Assert.That(S, Is.Null);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.UndoRedo));
        }

        // a blink repeated inside a second: the flash limit holds plays back and the notice tells it
        [Test]
        public void PX15n_TheHeldBackFlashesAreTold()
        {
            var light = new GameObject("Lamp").AddComponent<Light>();
            light.intensity = 1f;
            var player = light.gameObject.AddComponent<FeedbackPlayer>();
            var blink = new BlinkMotion();
            blink.m_Params.m_Value = new Vector4(3f, 3f, 3f, 3f);
            blink.m_Params.m_Period = 0.1f;
            On<LightIntensityItem>(player, null, blink, 0.5f);
            var notices = new List<PreviewNotice>();
            PreviewSession.Start(player, Free);
            Seconds(0.2);
            PreviewNotices.Collect(S, notices);
            Assert.That(notices.Any(n => n.Kind == PreviewNoticeKind.FlashHeld), Is.False, "the first play");
            S.Repeat = true;
            Seconds(1.0);
            PreviewNotices.Collect(S, notices);
            Assert.That(notices.Any(n => n.Kind == PreviewNoticeKind.FlashHeld), Is.True);
        }

        // the services' work crosses a tuning: the receivers stay bound, an event logs once, the hit stop's service stays
        [Test]
        public void PX15o_TheSecondStagesWorkCrossesATuning()
        {
            var receiver = new GameObject("Cam").AddComponent<CameraShakeReceiver>();
            FeedbackPlayer p = Player("Shaker");
            var shake = new CameraShakeItem { m_Motion = Shake() };
            shake.m_Target.m_Mode = TargetRefMode.Channel;
            shake.m_Timing.m_Duration = 2f;
            p.m_Items.Add(shake);
            var target = new GameObject("Target");
            var item = new UnityEventItem();
            UnityEventTools.AddBoolPersistentListener(item.m_Event, target.SetActive, false);
            item.m_Event.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
            p.m_Items.Add(item);
            var stop = new HitStopItem { m_FreezeDuration = 0.5f, m_TimeScale = 0f, m_MinimumTimeScale = 0f };
            p.m_Items.Add(stop);
            var logs = new List<string>();
            Application.LogCallback onLog = (message, stack, type) => logs.Add(message);
            Application.logMessageReceived += onLog;
            try
            {
                PreviewSession.Start(p, Free);
                Seconds(0.1);
                TimeScaleService service = S.Scheduler.Context.TimeScaleIfMade;
                EditDuration(p, 3f);
                Seconds(0.25);
                Assert.That(S.Receivers.Bound, Is.EqualTo(new[] { receiver }), "the receiver bound");
                Assert.That(S.Scheduler.Context.SkippedUnityEvents, Is.EqualTo(2), "skipped twice");
                Assert.That(logs.Count(l => l.StartsWith("Feedback preview did not invoke", StringComparison.Ordinal)), Is.EqualTo(1), "logged once");
                Assert.That(target.activeSelf, Is.True);
                Assert.That(S.Scheduler.Context.TimeScaleIfMade, Is.SameAs(service), "the same service");
            }
            finally
            {
                Application.logMessageReceived -= onLog;
            }
        }

        // a driver re-prepares itself on an edit of its fields — the session does not tune it
        [Test]
        public void PX15p_ADriversEditIsNoTuning()
        {
            MotionDriver driver = Driver();
            PreviewSession.Start(driver);
            Seconds(0.4);
            UserEdit.Inspector(driver, "m_Intensity", sp => sp.floatValue = 2f);
            Seconds(0.3);
            Assert.That(S, Is.Not.Null);
            Assert.That(driver.IsRunning, Is.True);
            Assert.That(driver.Seconds, Is.LessThan(0.35), "prepared again by itself");
        }
    }
}
