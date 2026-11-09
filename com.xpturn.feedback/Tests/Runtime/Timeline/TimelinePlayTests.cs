using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests
{
    // a Feedback track in play mode on a fresh test scheduler set as the default, 60 frames a second. A Manual director is moved
    // with Evaluate; the values are seen at PostLateUpdate's end (a loop system — a coroutine sees the reverted ones), read at the
    // next frame. A GameTime director runs on the frames.
    // A driven clip's value is checked against a twin player played and seeked by hand at the time the clip's own numbers give
    public sealed class TimelinePlayTests
    {
        const float Frame = 1f / 60f;
        const uint Seed = 12345u;

        static int s_Played;
        static int s_Paused;

        struct TimelineWindow { }

        static readonly List<Transform> s_Watched = new List<Transform>();
        static readonly Dictionary<Transform, Vector3> s_Seen = new Dictionary<Transform, Vector3>();

        readonly List<Object> m_Made = new List<Object>();
        FeedbackScheduler m_Saved;
        FeedbackScheduler m_Test;

        static void OnPlayed(FeedbackPlayer p, PlaybackHandle h) => s_Played++;

        static void OnPaused(FeedbackPlayer p) => s_Paused++;

        static void OnWindow()
        {
            foreach (Transform t in s_Watched)
            {
                if (t != null)
                    s_Seen[t] = t.localPosition;
            }
        }

        static void SetWindow(bool on)
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate))
                    continue;
                var list = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList);
                list.RemoveAll(x => x.type == typeof(TimelineWindow));
                if (on)
                    list.Add(new PlayerLoopSystem { type = typeof(TimelineWindow), updateDelegate = OnWindow });
                loop.subSystemList[i].subSystemList = list.ToArray();
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        // the player's place at the last frame's window
        static Vector3 Seen(FeedbackPlayer p) => s_Seen.TryGetValue(p.transform, out Vector3 v) ? v : new Vector3(float.NaN, 0f, 0f);

        [SetUp]
        public void SetUp()
        {
            s_Played = 0;
            s_Paused = 0;
            m_Saved = FeedbackScheduler.Default;
            m_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = m_Test;
            Time.captureDeltaTime = Frame;
            Time.timeScale = 1f;
            s_Watched.Clear();
            s_Seen.Clear();
            SetWindow(true);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Object o in m_Made)
            {
                if (o is GameObject g && g != null && g.TryGetComponent(out FeedbackPlayer p))
                    p.Stop();
            }
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Made.Clear();
            yield return null;
            SetWindow(false);
            s_Watched.Clear();
            s_Seen.Clear();
            FeedbackScheduler.Default = m_Saved;
            if (!m_Test.IsShutdown)
                m_Test.Shutdown();
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
        }

        // ---- fixtures

        FeedbackPlayer Player(string name, params FeedbackItem[] items)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            p.m_Policy = PlayPolicy.Overlap;
            p.Played += OnPlayed;
            p.Paused += OnPaused;
            s_Watched.Add(go.transform);
            return p;
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

        // a seeded shake — the value depends on the time and the seed
        static PositionItem Shake(float seconds)
        {
            var item = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = new ShakeMotion() };
            item.m_Timing.m_Duration = seconds;
            return item;
        }

        PlayableDirector Director(DirectorUpdateMode mode, FeedbackPlayer player, out FeedbackTrack track)
        {
            var go = new GameObject("Director");
            m_Made.Add(go);
            var d = go.AddComponent<PlayableDirector>();
            d.playOnAwake = false;
            d.timeUpdateMode = mode;
            d.extrapolationMode = DirectorWrapMode.Hold;
            var asset = ScriptableObject.CreateInstance<TimelineAsset>();
            m_Made.Add(asset);
            track = asset.CreateTrack<FeedbackTrack>(null, "Feedback");
            d.playableAsset = asset;
            d.SetGenericBinding(track, player);
            return d;
        }

        static FeedbackClip Clip(FeedbackTrack track, double start, double duration, FeedbackClipTiming timing, out TimelineClip clip)
        {
            clip = track.CreateClip<FeedbackClip>();
            clip.start = start;
            clip.duration = duration;
            var asset = (FeedbackClip)clip.asset;
            asset.m_Timing = timing;
            asset.m_Seed = Seed;
            return asset;
        }

        static void PostHold(TimelineClip clip)
            => typeof(TimelineClip).GetProperty("postExtrapolationMode").GetSetMethod(true).Invoke(clip, new object[] { TimelineClip.ClipExtrapolation.Hold });

        static IEnumerator EvaluateAt(PlayableDirector d, double t)
        {
            d.time = t;
            d.Evaluate();
            yield return null;
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
                yield return null;
        }

        // the twin seeked to the clip's local time — in the same frame as the clip
        static void Twin(FeedbackPlayer twin, ref PlaybackHandle h, double local)
        {
            if (h == default || twin.Position(h) < 0)
                h = twin.Play(PlayArgs.Default.WithSeed(Seed).WithDirection(PlayerDirection.Forward).WithDriven());
            twin.SeekRange(h, out long low, out long high);
            twin.SeekAt(h, Math.Max(low, Math.Min((long)Math.Round(local * 1e6), high)));
        }

        // ---- RV-3 · RV-18 — a driven clip's value is the seeked value

        [UnityTest]
        public IEnumerator TL01_ADrivenClipIsTheSeekedValue()
        {
            FeedbackPlayer p = Player("clip", Line(1f), Shake(1f));
            FeedbackPlayer twin = Player("twin", Line(1f), Shake(1f));
            PlayableDirector d = Director(DirectorUpdateMode.Manual, p, out FeedbackTrack track);
            FeedbackClip a = Clip(track, 0.5, 2.0, FeedbackClipTiming.Driven, out TimelineClip clip);
            clip.clipIn = 0.2;
            clip.timeScale = 0.5;
            PostHold(clip);
            FeedbackClip b = Clip(track, 4.0, 3.0, FeedbackClipTiming.Auto, out TimelineClip looped);
            b.m_Loop = true;
            d.RebuildGraph();
            PlaybackHandle h = default;
            var fails = new List<string>();
            // the clip's local time by its own numbers: clip-in + (t − start) × speed, held after its end; the looped one wraps
            // by the list's second
            var rows = new (double t, double local)[]
            {
                (0.9, 0.2 + 0.4 * 0.5), (1.6, 0.2 + 1.1 * 0.5), (1.1, 0.2 + 0.6 * 0.5), (3.2, 0.2 + 2.0 * 0.5 - 1e-6),
            };
            foreach ((double t, double local) in rows)
            {
                d.time = t;
                d.Evaluate();
                Twin(twin, ref h, local);
                yield return null;
                if (Seen(p) != Seen(twin))
                    fails.Add($"t={t} {Seen(p):F5} vs {Seen(twin):F5}");
                if (Seen(p).x == 0f)
                    fails.Add($"t={t} not moved");
            }
            twin.Stop(h, StopMode.Revert);
            h = default;
            foreach ((double t, double local) in new[] { (4.3, 0.3), (5.6, 0.6), (6.2, 0.2) })
            {
                d.time = t;
                d.Evaluate();
                Twin(twin, ref h, local);
                yield return null;
                if (Seen(p) != Seen(twin))
                    fails.Add($"looped t={t} {Seen(p):F5} vs {Seen(twin):F5}");
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- RV-4 · RV-17 — a triggered clip

        [UnityTest]
        public IEnumerator TL02_ATriggeredClipPlaysOnThePlayersClock()
        {
            FeedbackPlayer p = Player("clip", Line(2f));
            PlayableDirector d = Director(DirectorUpdateMode.GameTime, p, out FeedbackTrack track);
            Clip(track, 0.2, 1.0, FeedbackClipTiming.Triggered, out _);
            Clip(track, 3.0, 0.1, FeedbackClipTiming.Driven, out _);
            d.Play();
            yield return Frames(6);
            Assert.That(p.IsPlaying, Is.False, "not before the clip");
            yield return Frames(12);
            Assert.That((p.IsPlaying, s_Played), Is.EqualTo((true, 1)), "it plays from the clip's start");
            d.Pause();
            yield return null;
            float x = Seen(p).x;
            yield return Frames(10);
            yield return null;
            Assert.That(Seen(p).x, Is.EqualTo(x), "held while the director stands");
            d.Resume();
            yield return Frames(10);
            yield return null;
            Assert.That(Seen(p).x, Is.GreaterThan(x), "it goes on with the director");
            while (d.time < 1.4)
                yield return null;
            yield return null;
            Assert.That((p.IsPlaying, Seen(p).x), Is.EqualTo((false, 0f)), "leaving ends it with Revert");
        }

        [UnityTest]
        public IEnumerator TL03_ATriggeredClipEnteredWhilePausedGoesOnWithTheDirector()
        {
            FeedbackPlayer p = Player("clip", Line(2f));
            PlayableDirector d = Director(DirectorUpdateMode.GameTime, p, out FeedbackTrack track);
            Clip(track, 0.5, 1.5, FeedbackClipTiming.Triggered, out _);
            Clip(track, 3.0, 0.1, FeedbackClipTiming.Driven, out _);
            d.Play();
            yield return Frames(3);
            d.Pause();
            d.time = 0.6;
            d.Evaluate();
            yield return Frames(10);
            yield return null;
            float x = Seen(p).x;
            Assert.That((p.IsPlaying, x), Is.EqualTo((true, 0f)), "started, held");
            d.Resume();
            yield return Frames(10);
            yield return null;
            Assert.That(Seen(p).x, Is.GreaterThan(0.05f), "no OnBehaviourPlay, yet it goes on");
        }

        [UnityTest]
        public IEnumerator TL04_ALoopedTriggeredClipPlaysAgain()
        {
            FeedbackPlayer p = Player("clip", Line(0.25f));
            PlayableDirector d = Director(DirectorUpdateMode.GameTime, p, out FeedbackTrack track);
            Clip(track, 0.1, 1.0, FeedbackClipTiming.Triggered, out _).m_Loop = true;
            Clip(track, 3.0, 0.1, FeedbackClipTiming.Driven, out _);
            d.Play();
            while (d.time < 1.2)
                yield return null;
            Assert.That(s_Played, Is.InRange(3, 5), "a play each quarter second in the clip");
        }

        // ---- RV-5 — one play, the driven mark

        [UnityTest]
        public IEnumerator TL05_ADrivenClipMovesItsPlayOnly()
        {
            FeedbackPlayer p = Player("clip", Line(2f));
            PlayableDirector d = Director(DirectorUpdateMode.Manual, p, out FeedbackTrack track);
            Clip(track, 0.0, 2.0, FeedbackClipTiming.Driven, out _);
            d.RebuildGraph();
            yield return EvaluateAt(d, 1.0);
            p.Pause();
            Assert.That((s_Played, s_Paused, p.HasPaused), Is.EqualTo((1, 0, false)), "the driven play alone: Pause pauses nothing");
            PlaybackHandle code = p.Play();
            yield return Frames(6);
            yield return EvaluateAt(d, 1.5);
            Assert.That(p.Position(code), Is.InRange(6 * 16000L, 8 * 17000L), "the code's play runs on the clock, the clip's seek does not move it");
            p.Pause();
            Assert.That(s_Paused, Is.EqualTo(1), "Pause pauses the code's play");
            p.Resume();
            p.Stop(code, StopMode.Revert);
            yield return EvaluateAt(d, 0.5);
            float x = Seen(p).x;
            yield return Frames(10);
            yield return null;
            Assert.That((x, Seen(p).x), Is.EqualTo((0.25f, 0.25f)), "Resume did not let the driven play run");
        }

        // ---- RV-6 — Auto

        [UnityTest]
        public IEnumerator TL06_AutoPlaysAnUnscrubbableListAsTriggered()
        {
            FeedbackPlayer open = Player("open", Line(0.5f), new WaitForSignalItem());
            PlayableDirector d = Director(DirectorUpdateMode.GameTime, open, out FeedbackTrack track);
            Clip(track, 0.0, 2.0, FeedbackClipTiming.Auto, out _);
            Clip(track, 3.0, 0.1, FeedbackClipTiming.Auto, out _);
            d.Play();
            yield return Frames(40);
            yield return null;
            Assert.That((open.IsPlaying, Seen(open).x), Is.EqualTo((true, 1f)).Using(new Near()), "on the player's clock: the line ran, the wait holds it");
            d.Stop();
            yield return null;
            FeedbackPlayer forced = Player("forced", Line(0.5f), new WaitForSignalItem());
            PlayableDirector e = Director(DirectorUpdateMode.GameTime, forced, out FeedbackTrack t2);
            Clip(t2, 0.0, 2.0, FeedbackClipTiming.Driven, out _);
            Clip(t2, 3.0, 0.1, FeedbackClipTiming.Auto, out _);
            LogAssert.Expect(LogType.Warning, FeedbackClipBehaviour.NotScrubbedWarning);
            e.Play();
            yield return Frames(20);
            Assert.That(forced.IsPlaying, Is.True, "a Driven clip of it plays as Triggered, with a warning");
        }

        // ---- RV-7 — the ends

        [UnityTest]
        public IEnumerator TL07_TheEnds()
        {
            var got = new List<string>();
            foreach (FeedbackClipEnd end in new[] { FeedbackClipEnd.Revert, FeedbackClipEnd.Hold, FeedbackClipEnd.Complete })
            {
                FeedbackPlayer p = Player("end " + end, Line(1f));
                PlayableDirector d = Director(DirectorUpdateMode.Manual, p, out FeedbackTrack track);
                Clip(track, 0.2, 0.5, FeedbackClipTiming.Driven, out _).m_End = end;
                Clip(track, 3.0, 0.1, FeedbackClipTiming.Driven, out _);
                d.RebuildGraph();
                yield return EvaluateAt(d, 0.5);
                yield return EvaluateAt(d, 1.0);
                got.Add(end + " " + Seen(p).x.ToString("F2"));
            }
            FeedbackPlayer stopped = Player("stopped", Line(1f));
            PlayableDirector s = Director(DirectorUpdateMode.Manual, stopped, out FeedbackTrack st);
            Clip(st, 0.0, 1.0, FeedbackClipTiming.Driven, out _).m_End = FeedbackClipEnd.Hold;
            s.RebuildGraph();
            yield return EvaluateAt(s, 0.5);
            s.Stop();
            yield return null;
            got.Add("stop " + Seen(stopped).x.ToString("F2"));
            FeedbackPlayer destroyed = Player("destroyed", Line(1f));
            PlayableDirector x = Director(DirectorUpdateMode.Manual, destroyed, out FeedbackTrack xt);
            Clip(xt, 0.0, 1.0, FeedbackClipTiming.Driven, out _).m_End = FeedbackClipEnd.Hold;
            x.RebuildGraph();
            yield return EvaluateAt(x, 0.5);
            Object.Destroy(x.gameObject);
            yield return null;
            yield return null;
            got.Add("destroy " + Seen(destroyed).x.ToString("F2"));
            FeedbackPlayer gone = Player("gone", Line(1f));
            PlayableDirector g = Director(DirectorUpdateMode.Manual, gone, out FeedbackTrack gt);
            Clip(gt, 0.0, 1.0, FeedbackClipTiming.Driven, out _);
            g.RebuildGraph();
            yield return EvaluateAt(g, 0.5);
            Object.Destroy(gone.gameObject);
            yield return null;
            yield return EvaluateAt(g, 0.6);
            yield return EvaluateAt(g, 2.0);
            Assert.That(got, Is.EqualTo(new[] { "Revert 0.00", "Hold 0.30", "Complete 1.00", "stop 0.00", "destroy 0.00" }));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- RV-8 — the time scale

        [UnityTest]
        public IEnumerator TL08_ADrivenClipLeavesTheTimeScale()
        {
            FlashParams flash = FlashParams.Default;
            flash.m_Target = Vector4.zero;
            flash.m_Attack = 0.5f;
            flash.m_Hold = 0.2f;
            flash.m_Release = 0.5f;
            var scale = new TimeScaleItem { m_Motion = new FlashMotion { m_Params = flash } };
            scale.m_Timing.m_Duration = 1.2f;
            FeedbackPlayer p = Player("scale", scale);
            PlayableDirector d = Director(DirectorUpdateMode.GameTime, p, out FeedbackTrack track);
            Clip(track, 0.2, 2.0, FeedbackClipTiming.Driven, out _);
            Clip(track, 3.0, 0.1, FeedbackClipTiming.Driven, out _);
            d.Play();
            float least = 1f;
            for (int f = 0; f < 150 && d.time < 2.5; f++)
            {
                yield return null;
                least = Mathf.Min(least, Time.timeScale);
            }
            Assert.That((d.time > 2.2, least), Is.EqualTo((true, 1f)), "the director passed the clip and the scale stayed");
            var stop = new HitStopItem { m_FreezeDuration = 0.4f, m_TimeScale = 0f, m_MinimumTimeScale = 0f };
            stop.m_Timing.m_Delay = 0.3f;
            FeedbackPlayer h = Player("hit stop", stop, Line(1.5f));
            PlayableDirector e = Director(DirectorUpdateMode.GameTime, h, out FeedbackTrack t2);
            Clip(t2, 0.2, 2.0, FeedbackClipTiming.Driven, out _);
            Clip(t2, 3.0, 0.1, FeedbackClipTiming.Driven, out _);
            // the hit stop's length is unscaled — real time (the capture delta fixes the scaled frame only)
            e.Play();
            float t0 = Time.realtimeSinceStartup, from = -1f, to = -1f;
            double last = -1;
            while (Time.realtimeSinceStartup - t0 < 3f && to < 0f)
            {
                yield return null;
                if (e.time > 0.0 && e.time == last && from < 0f)
                    from = Time.realtimeSinceStartup;
                else if (from >= 0f && e.time != last)
                    to = Time.realtimeSinceStartup;
                last = e.time;
            }
            Assert.That((from >= 0f, to - from), Is.EqualTo((true, 0.4f)).Using(new Within(0.15f)), "the hit stop stands the director for its 0.4 s, then lets it go");
            var triggered = new TimeScaleItem { m_Motion = new FlashMotion { m_Params = flash } };
            triggered.m_Timing.m_Duration = 1.2f;
            FeedbackPlayer q = Player("triggered scale", triggered);
            PlayableDirector t = Director(DirectorUpdateMode.GameTime, q, out FeedbackTrack t3);
            // the frames are short in real time: the clip outlasts the flash's real second on the 60 fps director
            Clip(t3, 0.0, 600.0, FeedbackClipTiming.Triggered, out _);
            Clip(t3, 700.0, 0.1, FeedbackClipTiming.Driven, out _);
            t.Play();
            float t1 = Time.realtimeSinceStartup, lowest = 1f;
            int frames = 0, played = s_Played;
            while (Time.realtimeSinceStartup - t1 < 0.3f)
            {
                yield return null;
                frames++;
                lowest = Mathf.Min(lowest, Time.timeScale);
            }
            Assert.That(lowest, Is.LessThan(0.8f), $"a triggered clip's time scale is written — playing {q.IsPlaying} · played {s_Played - played} · frames {frames} · now {Time.timeScale} · director {t.time:F3}");
        }

        // ---- RV-9 — no allocation a frame

        [UnityTest]
        public IEnumerator TL09_NoAllocationAFrame()
        {
            FeedbackPlayer p = Player("driven", Line(2f));
            PlayableDirector d = Director(DirectorUpdateMode.Manual, p, out FeedbackTrack track);
            Clip(track, 0.0, 2.0, FeedbackClipTiming.Driven, out _);
            d.RebuildGraph();
            FeedbackPlayer q = Player("triggered", Line(2f));
            PlayableDirector e = Director(DirectorUpdateMode.Manual, q, out FeedbackTrack t2);
            Clip(t2, 0.0, 2.0, FeedbackClipTiming.Triggered, out _);
            e.RebuildGraph();
            e.Play();
            double t = 0.1;
            for (int i = 0; i < 4; i++)
            {
                t += 0.05;
                yield return EvaluateAt(d, t);
                yield return EvaluateAt(e, t);
            }
            Assert.That((p.IsPlaying, q.IsPlaying), Is.EqualTo((true, true)), "both play");
            int driven = 0, triggered = 0, positive = 0;
            for (int k = 0; k < 5; k++)
            {
                t += 0.05;
                double at = t;
                if (UIs.AllocatingGCMemory().ApplyTo((TestDelegate)(() => { d.time = at; d.Evaluate(); })).IsSuccess)
                    driven++;
                if (UIs.AllocatingGCMemory().ApplyTo((TestDelegate)(() => { e.time = at; e.Evaluate(); })).IsSuccess)
                    triggered++;
                if (UIs.AllocatingGCMemory().ApplyTo((TestDelegate)(() => { var junk = new byte[64]; junk[0] = 1; })).IsSuccess)
                    positive++;
                yield return null;
            }
            Assert.That((driven, triggered, positive), Is.EqualTo((0, 0, 5)), "allocating evaluations of 5 · the positive");
        }

        // ---- RV-10 · RV-12 — the conditions, a lost play

        [UnityTest]
        public IEnumerator TL10_ADrivenClipIsUnconditionalATriggeredOneIsNot()
        {
            FeedbackPlayer p = Player("never", Line(1f));
            p.m_Chance = 0f;
            PlayableDirector d = Director(DirectorUpdateMode.Manual, p, out FeedbackTrack track);
            Clip(track, 0.0, 1.0, FeedbackClipTiming.Driven, out _);
            Clip(track, 2.0, 1.0, FeedbackClipTiming.Triggered, out _);
            d.Play();
            yield return EvaluateAt(d, 0.5);
            bool driven = p.IsPlaying;
            yield return EvaluateAt(d, 2.5);
            Assert.That((driven, p.IsPlaying, s_Played), Is.EqualTo((true, false, 1)));
        }

        [UnityTest]
        public IEnumerator TL11_ALostDrivenPlayWaitsForTheNextEntry()
        {
            FeedbackPlayer p = Player("restart", Line(1f));
            p.m_Policy = PlayPolicy.Restart;
            PlayableDirector d = Director(DirectorUpdateMode.Manual, p, out FeedbackTrack track);
            Clip(track, 0.0, 1.0, FeedbackClipTiming.Driven, out _);
            Clip(track, 2.0, 0.1, FeedbackClipTiming.Driven, out _);
            d.RebuildGraph();
            yield return EvaluateAt(d, 0.3);
            p.Play();
            yield return EvaluateAt(d, 0.4);
            yield return EvaluateAt(d, 0.5);
            int lost = s_Played;
            yield return EvaluateAt(d, 1.5);
            yield return EvaluateAt(d, 0.5);
            Assert.That((lost, s_Played), Is.EqualTo((2, 3)), "no play again until the clip is entered again");
        }

        // ---- RV-16 — a frame's leaving comes first

        [UnityTest]
        public IEnumerator TL12_LeavingComesBeforeEnteringInOneEvaluation()
        {
            FeedbackPlayer p = Player("ignore", Line(1f));
            p.m_Policy = PlayPolicy.Ignore;
            PlayableDirector d = Director(DirectorUpdateMode.Manual, p, out FeedbackTrack track);
            Clip(track, 0.2, 0.4, FeedbackClipTiming.Driven, out _);
            Clip(track, 0.8, 0.4, FeedbackClipTiming.Driven, out _);
            d.RebuildGraph();
            // each clip entered once first — both know their player
            yield return EvaluateAt(d, 0.4);
            yield return EvaluateAt(d, 1.0);
            yield return EvaluateAt(d, 0.4);
            int backward = s_Played;
            yield return EvaluateAt(d, 1.0);
            Assert.That((backward, s_Played, p.IsPlaying), Is.EqualTo((3, 4, true)), "the later clip's play ended before the earlier one played — backward and forward");
        }

        // ---- a triggered play that ends inside its clip takes its end at the clip's end: Revert puts the player back as it was
        //      before the plays, unless it plays something else then · Hold leaves what it persisted, and entered again the play
        //      starts from it

        [UnityTest]
        public IEnumerator TL13_ATriggeredPlayThatEndsInsideItsClipTakesItsEndAtTheClipsEnd()
        {
            FeedbackPlayer p = Player("revert", Line(0.4f));
            FeedbackPlayer q = Player("hold", Line(0.4f));
            PlayableDirector dp = Director(DirectorUpdateMode.GameTime, p, out FeedbackTrack tp);
            PlayableDirector dq = Director(DirectorUpdateMode.GameTime, q, out FeedbackTrack tq);
            Clip(tp, 0.2, 1.0, FeedbackClipTiming.Triggered, out _);
            Clip(tq, 0.2, 1.0, FeedbackClipTiming.Triggered, out _).m_End = FeedbackClipEnd.Hold;
            Clip(tp, 3.0, 0.1, FeedbackClipTiming.Driven, out _);
            Clip(tq, 3.0, 0.1, FeedbackClipTiming.Driven, out _);
            // the same player on a second track, its play running when the first clip ends
            FeedbackPlayer r = Player("busy", Line(0.4f));
            PlayableDirector dr = Director(DirectorUpdateMode.GameTime, r, out FeedbackTrack tr);
            Clip(tr, 0.2, 1.0, FeedbackClipTiming.Triggered, out _);
            FeedbackTrack second = ((TimelineAsset)dr.playableAsset).CreateTrack<FeedbackTrack>(null, "Second");
            dr.SetGenericBinding(second, r);
            Clip(second, 1.0, 2.0, FeedbackClipTiming.Driven, out _);
            dp.Play();
            dq.Play();
            dr.Play();
            float until = Time.realtimeSinceStartup + 10f;
            while (dp.time < 1.5 && Time.realtimeSinceStartup < until)
                yield return null;
            string left = $"{p.transform.localPosition.x:F2} {q.transform.localPosition.x:F2} busy={r.IsPlaying}";
            dp.time = 0.1;
            dq.time = 0.1;
            while (dp.time < 0.45 && Time.realtimeSinceStartup < until)
                yield return null;
            float moving = p.transform.localPosition.x;
            Assert.That((left, moving > 0.05f && moving < 0.95f, q.IsPlaying, q.transform.localPosition.x), Is.EqualTo(("0.00 1.00 busy=True", true, true, 1f)));
        }

        // ---- play mode asks the editor for no clip length — an asset's is the base's (infinite), a new clip Timeline's 5 s

        sealed class Lengths : ITrackPreview
        {
            internal int Asked;

            public void Gather(PlayableDirector director, FeedbackPlayer player) { }

            public bool Owns(FeedbackPlayer player) => false;

            public SeekRefusal Scrub(FeedbackPlayer player, PlaybackHandle handle) => SeekRefusal.NotLive;

            public bool Drives(FeedbackPlayer player) => false;

            public void Seeked() { }

            public void GraphDestroyed() { }

            public double Length(FeedbackClip clip)
            {
                Asked++;
                return 2.5;
            }
        }

        [UnityTest]
        public IEnumerator TL14_PlayModeAsksNoClipLength()
        {
            FeedbackPlayer p = Player("length", Line(1f));
            Director(DirectorUpdateMode.Manual, p, out FeedbackTrack track);
            ITrackPreview saved = TrackPreview.Hook;
            var lengths = new Lengths();
            TrackPreview.Hook = lengths;
            try
            {
                TimelineClip clip = track.CreateClip<FeedbackClip>();
                double length = ((FeedbackClip)clip.asset).duration;
                Assert.That((double.IsPositiveInfinity(length), clip.duration, lengths.Asked), Is.EqualTo((true, 5.0, 0)));
            }
            finally
            {
                TrackPreview.Hook = saved;
            }
            yield return null;
        }

        // ---- a list that cannot be scrubbed (a signal wait without a timeout): an Auto or Driven clip plays it as a Triggered
        //      one does — the player's chance, cooldown and direction, the Time Scale items, a seed a play — and a scrubbable
        //      list still drives (no chance). The multiplier changed since the prepare is read again

        static IEnumerator Playing(PlayableDirector d, int frames)
        {
            d.Play();
            yield return Frames(frames);
        }

        static uint LiveSeed(FeedbackPlayer p)
        {
            for (int r = 0; p.Runtime != null && r < p.Runtime.RecordCount; r++)
            {
                if (p.Runtime.Record(r).Active)
                    return p.Runtime.Record(r).Seed;
            }
            return 0u;
        }

        [UnityTest]
        public IEnumerator TL15_AnUnscrubbableListPlaysAsATriggeredClipDoes()
        {
            var warnings = new List<string>();
            Application.LogCallback log = (message, stack, type) =>
            {
                if (type == LogType.Warning)
                    warnings.Add(message);
            };
            Application.logMessageReceived += log;
            var got = new List<string>();
            int drivenClips = 0;
            try
            {
                foreach (FeedbackClipTiming timing in new[] { FeedbackClipTiming.Auto, FeedbackClipTiming.Driven, FeedbackClipTiming.Triggered })
                {
                    int clips = 0;
                    FeedbackClip Open(PlayableDirector director, FeedbackTrack track, bool loop = false)
                    {
                        clips++;
                        FeedbackClip clip = Clip(track, 0.0, 600.0, timing, out _);
                        clip.m_Loop = loop;
                        return clip;
                    }
                    FeedbackPlayer chance = Player("chance " + timing, Line(0.5f), new WaitForSignalItem());
                    chance.m_Chance = 0f;
                    PlayableDirector dc = Director(DirectorUpdateMode.GameTime, chance, out FeedbackTrack tc);
                    Open(dc, tc);
                    FeedbackPlayer looped = Player("loop " + timing, Line(0.1f), new WaitForSignalItem());
                    looped.m_Chance = 0f;
                    PlayableDirector dl = Director(DirectorUpdateMode.GameTime, looped, out FeedbackTrack tl);
                    Open(dl, tl, true);
                    FeedbackPlayer cooled = Player("cooldown " + timing, Line(0.5f), new WaitForSignalItem());
                    cooled.m_Cooldown = 100f;
                    cooled.Play();
                    cooled.Stop();
                    PlayableDirector dk = Director(DirectorUpdateMode.GameTime, cooled, out FeedbackTrack tk);
                    Open(dk, tk);
                    FeedbackPlayer backward = Player("backward " + timing, Line(0.5f), new WaitForSignalItem());
                    backward.m_Direction = PlayerDirection.Backward;
                    PlayableDirector db = Director(DirectorUpdateMode.GameTime, backward, out FeedbackTrack tb);
                    Open(db, tb);
                    dc.Play();
                    dl.Play();
                    dk.Play();
                    db.Play();
                    yield return Frames(10);
                    got.Add($"{timing}: chance0 playing={chance.IsPlaying} · loop chance0 playing={looped.IsPlaying} · cooldown playing={cooled.IsPlaying} · backward playing={backward.IsPlaying}");
                    dc.Stop();
                    dl.Stop();
                    dk.Stop();
                    db.Stop();
                    // the time scale: a flash to 0 over half a second of the player's clock
                    FlashParams flash = FlashParams.Default;
                    flash.m_Target = Vector4.zero;
                    flash.m_Attack = 0.5f;
                    flash.m_Hold = 0.2f;
                    flash.m_Release = 0.5f;
                    var scale = new TimeScaleItem { m_Motion = new FlashMotion { m_Params = flash } };
                    scale.m_Timing.m_Duration = 1.2f;
                    FeedbackPlayer slow = Player("time scale " + timing, scale, new WaitForSignalItem());
                    PlayableDirector ds = Director(DirectorUpdateMode.GameTime, slow, out FeedbackTrack ts);
                    Open(ds, ts);
                    ds.Play();
                    float t0 = Time.realtimeSinceStartup, lowest = 1f;
                    while (Time.realtimeSinceStartup - t0 < 0.3f)
                    {
                        yield return null;
                        lowest = Mathf.Min(lowest, Time.timeScale);
                    }
                    ds.Stop();
                    slow.Stop();
                    Time.timeScale = 1f;
                    // the seed: two entries
                    FeedbackPlayer seeded = Player("seed " + timing, Shake(1f), new WaitForSignalItem());
                    PlayableDirector dd = Director(DirectorUpdateMode.GameTime, seeded, out FeedbackTrack td);
                    Open(dd, td);
                    var seeds = new List<uint>();
                    for (int k = 0; k < 2; k++)
                    {
                        dd.time = 0.0;
                        yield return Playing(dd, 5);
                        seeds.Add(LiveSeed(seeded));
                        dd.Stop();
                        seeded.Stop();
                        yield return null;
                    }
                    got.Add($"{timing}: time scale written={lowest < 0.8f} · seeds of its own={seeds[0] != Seed && seeds[1] != Seed && seeds[0] != seeds[1]}");
                    if (timing == FeedbackClipTiming.Driven)
                        drivenClips = clips;
                }
                // a scrubbable list drives — the chance passed · a list made unscrubbable by a multiplier changed since the
                // prepare plays as Triggered
                FeedbackPlayer drives = Player("drives", Line(1f));
                drives.m_Chance = 0f;
                PlayableDirector dv = Director(DirectorUpdateMode.GameTime, drives, out FeedbackTrack tv);
                Clip(tv, 0.0, 600.0, FeedbackClipTiming.Auto, out _);
                FeedbackPlayer saturated = Player("saturated", Line(1f));
                saturated.m_Chance = 0f;
                yield return null;
                saturated.EnsureRuntime();
                saturated.m_DurationMultiplier = 3e37f;
                PlayableDirector dz = Director(DirectorUpdateMode.GameTime, saturated, out FeedbackTrack tz);
                Clip(tz, 0.0, 600.0, FeedbackClipTiming.Auto, out _);
                dv.Play();
                dz.Play();
                yield return Frames(5);
                got.Add($"scrubbable chance0 playing={drives.IsPlaying} · saturated since the prepare playing={saturated.IsPlaying}");
                dv.Stop();
                dz.Stop();
            }
            finally
            {
                Application.logMessageReceived -= log;
                Time.timeScale = 1f;
            }
            int notScrubbed = warnings.FindAll(w => w == FeedbackClipBehaviour.NotScrubbedWarning).Count;
            int reverse = warnings.FindAll(w => w == FeedbackPlayer.ReverseWarning).Count;
            Assert.That(got, Is.EqualTo(new[]
            {
                "Auto: chance0 playing=False · loop chance0 playing=False · cooldown playing=False · backward playing=False",
                "Auto: time scale written=True · seeds of its own=True",
                "Driven: chance0 playing=False · loop chance0 playing=False · cooldown playing=False · backward playing=False",
                "Driven: time scale written=True · seeds of its own=True",
                "Triggered: chance0 playing=False · loop chance0 playing=False · cooldown playing=False · backward playing=False",
                "Triggered: time scale written=True · seeds of its own=True",
                "scrubbable chance0 playing=True · saturated since the prepare playing=False",
            }));
            Assert.That((notScrubbed, reverse), Is.EqualTo((drivenClips, 3)), "a Driven clip warns once · each backward play is refused with its warning");
        }

        sealed class Within : IEqualityComparer<(bool, float)>
        {
            readonly float m_Tolerance;

            internal Within(float tolerance) => m_Tolerance = tolerance;

            public bool Equals((bool, float) a, (bool, float) b) => a.Item1 == b.Item1 && Mathf.Abs(a.Item2 - b.Item2) <= m_Tolerance;

            public int GetHashCode((bool, float) v) => 0;
        }

        sealed class Near : IEqualityComparer<(bool, float)>
        {
            public bool Equals((bool, float) a, (bool, float) b) => a.Item1 == b.Item1 && Mathf.Abs(a.Item2 - b.Item2) < 1e-4f;

            public int GetHashCode((bool, float) v) => 0;
        }
    }
}
