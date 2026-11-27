using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // Source Control in Play mode — what Edit mode does not run: the game's listener pause (it holds sources in Play mode only)
    // reads as playing, and the player's own destruction and disable behaviours (OnDestroy · OnDisable). One listener in the
    // scene; the states are read at once (a place set by hand), a clip's end is waited for
    public sealed class AudioPlayTests
    {
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_S = new List<FeedbackScheduler>();
        AudioClip m_Long;

        [SetUp]
        public void SetUp()
        {
            var listener = new GameObject("Listener", typeof(AudioListener));
            m_Made.Add(listener);
            m_Long = AudioClip.Create("long", 44100 * 4, 1, 44100, false);
            m_Made.Add(m_Long);
        }

        [TearDown]
        public void TearDown()
        {
            AudioListener.pause = false;
            foreach (FeedbackScheduler s in m_S)
                if (!s.IsShutdown) s.Shutdown();
            foreach (Object o in m_Made)
                if (o != null) Object.Destroy(o);
            m_Made.Clear();
            m_S.Clear();
        }

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_S.Add(s);
            return s;
        }

        AudioSource Source(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.clip = m_Long;
            return s;
        }

        FeedbackPlayer Player(FeedbackScheduler s, AudioSource a, AudioSourceAction action, PlayerDisableBehavior disable = PlayerDisableBehavior.Continue)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            FeedbackPlayer p = go.AddComponent<FeedbackPlayer>();
            p.m_DisableBehavior = disable;
            var item = new AudioSourceControlItem
            {
                m_Action = action,
                m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = a, m_Path = "", m_Anchor = "" },
                m_Motion = new SetMotion { m_Params = new SetParams { m_Value = new Vector4(1f, 0, 0, 0) } },
            };
            var keeper = new PositionItem
            {
                m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = a.transform, m_Path = "", m_Anchor = "" },
                m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(0.01f, 0, 0, 0), m_Vibrato = 1f, m_Decay = 0f } },
            };
            keeper.m_Timing.m_Duration = 2f;
            p.m_Items.Add(item);
            p.m_Items.Add(keeper);
            p.Bind(s);
            p.EnsureRuntime();
            return p;
        }

        // the engine plays here — else the reads below say nothing
        static void PositiveControl(AudioSource s)
        {
            s.Play();
            Assert.That(s.isPlaying, Is.True, "the engine plays (Play)");
            s.Stop();
        }

        // ---- under the game's listener pause a source keeping its place reads as playing — a Stop's Revert plays it again
        [UnityTest]
        public IEnumerator AP01_TheListenersPauseReadsAsPlaying()
        {
            yield return null;
            FeedbackScheduler s = Sched();
            AudioSource a = Source("A");
            PositiveControl(a);
            a.Play();
            a.timeSamples = 4410;
            AudioListener.pause = true;
            yield return null;
            Assert.That(a.isPlaying, Is.False, "the listener's pause holds the source");
            FeedbackPlayer stop = Player(s, a, AudioSourceAction.Stop);
            stop.Play(); s.Tick(0f);
            stop.Stop(StopMode.Revert); s.Tick(0f);
            AudioListener.pause = false;
            yield return null;
            Assert.That(a.isPlaying, Is.True, "the Revert played it again — it was playing under the listener's pause");
        }

        // ---- the player destroyed — its sound stops (the play ends Hold, then the release) · disabled with Stop Hold the
        // sound rings on, with Stop Revert the source is put back
        [UnityTest]
        public IEnumerator AP02_TheDestructionAndTheDisableBehaviours()
        {
            yield return null;
            FeedbackScheduler s = Sched();
            AudioSource a = Source("A");
            PositiveControl(a);
            FeedbackPlayer destroyed = Player(s, a, AudioSourceAction.Play, PlayerDisableBehavior.StopHold);
            destroyed.Play(); s.Tick(0f);
            Assert.That(a.isPlaying, Is.True, "played");
            Object.Destroy(destroyed.gameObject);
            yield return null;
            Assert.That(a.isPlaying, Is.False, "the player destroyed — stopped");
            FeedbackPlayer hold = Player(s, a, AudioSourceAction.Play, PlayerDisableBehavior.StopHold);
            hold.Play(); s.Tick(0f);
            hold.enabled = false;
            Assert.That(a.isPlaying, Is.True, "disabled with Stop Hold — rings on");
            a.Stop();
            FeedbackPlayer back = Player(s, a, AudioSourceAction.Play, PlayerDisableBehavior.StopRevert);
            back.Play(); s.Tick(0f);
            back.enabled = false;
            Assert.That(a.isPlaying, Is.False, "disabled with Stop Revert — the start state");
        }

        // ---- a clip's end frame may still hold the clip's length (else 0 — the engine's timing, so it plays again until one
        // does) — it reads as stopped: a Play's Revert from there stops the sound
        [UnityTest]
        public IEnumerator AP03_AClipsEndReadsAsStopped()
        {
            yield return null;
            FeedbackScheduler s = Sched();
            AudioSource a = Source("A");
            PositiveControl(a);
            var clip = AudioClip.Create("short", 11025, 1, 44100, false);
            m_Made.Add(clip);
            a.clip = clip;
            bool held = false;
            for (int i = 0; i < 20 && !held; i++)
            {
                a.Play();
                var wall = System.Diagnostics.Stopwatch.StartNew();
                while (a.isPlaying && wall.Elapsed.TotalSeconds < 2.0)
                    yield return null;
                held = !a.isPlaying && a.timeSamples == clip.samples;
            }
            Assert.That(held, Is.True, "an end frame holding the clip's length");
            Assert.That(AudioPlayStates.Read(a), Is.EqualTo(AudioPlayState.Stopped), "the end frame reads as stopped");
            FeedbackPlayer play = Player(s, a, AudioSourceAction.Play);
            play.Play(); s.Tick(0f);
            var sounding = System.Diagnostics.Stopwatch.StartNew();
            while (a.timeSamples == 0 && sounding.Elapsed.TotalSeconds < 2.0)
                yield return null;
            Assert.That((a.isPlaying, a.timeSamples > 0), Is.EqualTo((true, true)), "the Play sounds from the start");
            play.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That((a.isPlaying, a.timeSamples), Is.EqualTo((false, 0)), "the Revert stopped it — the start state");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
                yield return null;
        }

        static string On(AudioSource a) => a.isPlaying ? "playing" : "silent";

        // ---- under the game's listener pause the player's Pause holds the sound its Source Control started, as it does with
        // none: the listener's pause going off leaves it paused, the player's Resume plays it and a play that ends paused stops
        // it — a sound started after a Pause the play's own event asked for too. A Resume under the listener's pause lets it go
        // on with the listener; an Ignore Listener Pause source pauses with the player alone
        [UnityTest]
        public IEnumerator AP04_ThePlayersPauseUnderTheListeners()
        {
            yield return null;
            FeedbackScheduler s = Sched();
            PositiveControl(Source("Control"));
            var fail = new List<string>();
            var rows = new[]
            {
                ("under the listener's", "before silent · paused with the player · off silent · resumed playing"),
                ("ends paused", "before silent · paused with the player · off silent"),
                ("no listener pause", "before playing · paused with the player · off silent · resumed playing"),
                ("resumed under the listener's", "before silent · off playing"),
                ("ignore listener pause", "before playing · paused with the player · off silent · resumed playing"),
            };
            foreach ((string row, string expected) in rows)
            {
                AudioSource a = Source(row);
                a.ignoreListenerPause = row == "ignore listener pause";
                FeedbackPlayer p = Player(s, a, AudioSourceAction.Play);
                p.Play(); s.Tick(0f);
                yield return null;
                AudioListener.pause = row != "no listener pause";
                yield return null;
                string seen = "before " + On(a);
                p.Pause();
                if (row == "resumed under the listener's")
                    p.Resume();
                else
                    seen += " · " + (p.Runtime.Binding(0).DebugNote(0) ?? "no note");
                if (row == "ends paused")
                    p.Stop(StopMode.Hold);
                AudioListener.pause = false;
                yield return Frames(2);
                seen += " · off " + On(a);
                if (row != "ends paused" && row != "resumed under the listener's")
                {
                    p.Resume();
                    yield return Frames(2);
                    seen += " · resumed " + On(a);
                }
                if (seen != expected)
                    fail.Add(row + ": " + seen);
                p.Stop(StopMode.Hold);
                a.Stop();
            }
            AudioSource e = Source("event");
            FeedbackPlayer ep = Player(s, e, AudioSourceAction.Play);
            var pause = new UnityEventItem();
            pause.m_Event.AddListener(() => ep.Pause());
            ep.m_Items.Insert(0, pause);
            ep.Rebuild();
            AudioListener.pause = true;
            ep.Play(); s.Tick(0f);
            yield return null;
            string evt = "paused " + ep.HasPaused;
            AudioListener.pause = false;
            yield return Frames(2);
            evt += " · off " + On(e);
            ep.Resume();
            yield return Frames(2);
            evt += " · resumed " + On(e);
            if (evt != "paused True · off silent · resumed playing")
                fail.Add("its own event's Pause before the sound: " + evt);
            Assert.That(fail, Is.Empty);
        }

        // ---- under the game's listener pause the engine cannot tell its pause from the source's own: a source the game paused
        // itself, under it or before it, plays at the player's Resume. One the game stopped and a sound at its end stay silent,
        // and an Ignore Listener Pause source the game paused stays paused
        [UnityTest]
        public IEnumerator AP05_TheGamesOwnPauseUnderTheListeners()
        {
            yield return null;
            FeedbackScheduler s = Sched();
            PositiveControl(Source("Control"));
            AudioClip brief = AudioClip.Create("brief", 4410, 1, 44100, false);
            m_Made.Add(brief);
            var fail = new List<string>();
            var rows = new[]
            {
                ("paused under the listener's", "off silent · resumed playing"),
                ("paused before the listener's", "off silent · resumed playing"),
                ("stopped under the listener's", "off silent · resumed silent"),
                ("at its end", "ended · off silent · resumed silent"),
                ("ignore listener pause, paused", "off silent · resumed silent"),
            };
            foreach ((string row, string expected) in rows)
            {
                AudioSource a = Source(row);
                a.ignoreListenerPause = row == "ignore listener pause, paused";
                if (row == "at its end")
                    a.clip = brief;
                FeedbackPlayer p = Player(s, a, AudioSourceAction.Play);
                p.Play(); s.Tick(0f);
                yield return null;
                string seen = "";
                if (row == "at its end")
                {
                    var wall = System.Diagnostics.Stopwatch.StartNew();
                    while (a.isPlaying && wall.Elapsed.TotalSeconds < 2.0)
                        yield return null;
                    seen = a.isPlaying ? "ringing · " : "ended · ";
                }
                if (row == "paused before the listener's")
                    a.Pause();
                AudioListener.pause = true;
                yield return null;
                if (row == "paused under the listener's" || row == "ignore listener pause, paused")
                    a.Pause();
                else if (row == "stopped under the listener's")
                    a.Stop();
                p.Pause();
                AudioListener.pause = false;
                yield return Frames(2);
                seen += "off " + On(a);
                p.Resume();
                yield return Frames(2);
                seen += " · resumed " + On(a);
                if (seen != expected)
                    fail.Add(row + ": " + seen);
                p.Stop(StopMode.Hold);
                a.Stop();
            }
            Assert.That(fail, Is.Empty);
        }
    }
}
