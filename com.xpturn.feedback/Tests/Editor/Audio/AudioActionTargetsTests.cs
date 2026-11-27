using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the Audio action items — the one-shot's volume, Play Clip, Source Control, the player's pause, the ends, one-shots and
    // their source, the preview. The engine plays in Edit mode too: a state is read at once (isPlaying · timeSamples, a place
    // set by hand — no clock is waited for). The positive control (a source's own Play) comes first where a test reads a sound.
    // The Audio assembly's types are not named: the items play through players (DevProject)
    public sealed class AudioActionTargetsTests
    {
        const float Step = 0.05f;
        static readonly PlayArgs Back = PlayArgs.Default.WithDirection(PlayerDirection.Backward);
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_S = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_P = new List<FeedbackPlayer>();
        readonly List<string> m_Logs = new List<string>();
        static double s_Now;
        Func<double> m_Clock;
        AudioClip m_Long;

        [SetUp]
        public void SetUp()
        {
            m_Clock = PreviewClock.Source;
            PreviewClock.Source = () => s_Now;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Application.logMessageReceived += OnLog;
            m_Long = AudioClip.Create("long", 44100 * 4, 1, 44100, false);
            m_Made.Add(m_Long);
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            PreviewSession.Current?.Stop();
            AudioListener.pause = false;
            foreach (FeedbackPlayer p in m_P)
                if (p != null) p.ReleaseRuntime();
            foreach (FeedbackScheduler s in m_S)
                if (!s.IsShutdown) s.Shutdown();
            foreach (Object o in m_Made)
                if (o != null) Object.DestroyImmediate(o);
            m_Made.Clear(); m_S.Clear(); m_P.Clear(); m_Logs.Clear();
            PreviewClock.Source = m_Clock;
        }

        void OnLog(string message, string stack, LogType type) => m_Logs.Add(message);

        int Logged(string part) => m_Logs.Count(m => m.Contains(part));

        static void Pump(int n) { for (int i = 0; i < n; i++) { s_Now += 1.0 / 120.0; PreviewSession.Current?.Update(); } }
        static void Tick(FeedbackScheduler s, int n) { for (int i = 0; i < n; i++) s.Tick(Step); }
        FeedbackScheduler Sched() { var s = new FeedbackScheduler(ContextKind.Test); m_S.Add(s); return s; }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            FeedbackPlayer p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            if (s != null)
            {
                p.Bind(s);
                p.EnsureRuntime();
            }
            m_P.Add(p);
            return p;
        }

        // a source not playing, its clip a long one
        AudioSource Source(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.clip = m_Long;
            return s;
        }

        // the source playing at a place past 0 (a pause keeps it — a stop goes back to 0)
        static void Playing(AudioSource s)
        {
            s.Play();
            s.timeSamples = 4410;
        }

        static void Paused(AudioSource s)
        {
            Playing(s);
            s.Pause();
        }

        static T With<T>(T item, AudioSource target) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = new SetMotion { m_Params = new SetParams { m_Value = new Vector4(1f, 0, 0, 0) } };
            return item;
        }

        static AudioSourceControlItem Control(AudioSource s, AudioSourceAction action) => With(new AudioSourceControlItem { m_Action = action }, s);

        AudioPlayClipItem Clip(AudioSource s, float volume = 1f)
        {
            AudioPlayClipItem item = With(new AudioPlayClipItem { m_Volume = volume }, s);
            item.m_Clip = AudioClip.Create("shot", 44100, 1, 44100, false);
            m_Made.Add(item.m_Clip);
            return item;
        }

        // keeps the play running — a Stop or the player's Pause reaches a play that is still on
        static PositionItem Keeper(Component target, float d = 2f)
        {
            var k = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target.transform, m_Path = "", m_Anchor = "" }, m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(0.01f, 0, 0, 0), m_Vibrato = 1f, m_Decay = 0f } } };
            k.m_Timing.m_Duration = d;
            return k;
        }

        static string St(AudioSource s) => (s.isPlaying ? "playing" : "not playing") + " at " + s.timeSamples.ToString(CultureInfo.InvariantCulture);

        // the engine plays here: a source's own Play is heard at once — else every sound below would read as silent
        static void PositiveControl(AudioSource s)
        {
            s.Play();
            Assert.That(s.isPlaying, Is.True, "the engine plays (Play) — the sound checks below need it");
            s.Stop();
        }

        static UnityEventItem Call(UnityEngine.Events.UnityAction action, float delay = 0f)
        {
            var item = new UnityEventItem();
            item.m_Event.AddListener(action);
            item.m_Timing.m_Delay = delay;
            return item;
        }

        static T Delayed<T>(T item, float delay) where T : FeedbackItem
        {
            item.m_Timing.m_Delay = delay;
            return item;
        }

        static T ReverseOnly<T>(T item) where T : EffectItem
        {
            item.m_Conditions.m_Direction = DirectionCondition.ReverseOnly;
            return item;
        }

        static string Note(FeedbackPlayer p, int item) => p.Runtime.Binding(item).DebugNote(0) ?? "none";

        // ---- the one-shot's volume — the item's × the intensity × the category's scale · above 1 louder · below 0 silent
        [Test]
        public void AA01_TheOneShotVolume()
        {
            Assert.That(AudioMath.OneShotVolume(1f, 1f, 1f), Is.EqualTo(1f));
            Assert.That(AudioMath.OneShotVolume(0.5f, 2f, 1f), Is.EqualTo(1f));
            Assert.That(AudioMath.OneShotVolume(1f, 3f, 1f), Is.EqualTo(3f), "not kept to 1 — the engine plays it louder");
            Assert.That(AudioMath.OneShotVolume(1f, 2f, 0.5f), Is.EqualTo(1f));
            Assert.That(AudioMath.OneShotVolume(1f, -0.5f, 1f), Is.EqualTo(0f), "below 0 — silence");
            Assert.That(AudioMath.OneShotVolume(1f, 1f, 0f), Is.EqualTo(0f));
        }

        // ---- Play Clip — a one-shot on the source (a paused one too) · none for a sweep, a backward play, the scale 0, a
        // disabled source or an inactive object (no engine warning) · no clip, another type: refused with the reason
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AA02_PlayClip()
        {
            FeedbackScheduler s = Sched();
            AudioSource a = Source("A");
            PositiveControl(a);
            a.clip = null;
            FeedbackPlayer p = Player(s, Clip(a));
            p.Play(); s.Tick(0f);
            Assert.That(a.isPlaying, Is.True, "the one-shot");
            a.Stop();
            AudioSource paused = Source("Paused");
            Paused(paused);
            FeedbackPlayer pp = Player(s, Clip(paused));
            pp.Play(); s.Tick(0f);
            Assert.That(paused.isPlaying, Is.True, "a paused source plays a one-shot");
            paused.Stop();
            AudioPlayClipItem late = Clip(a);
            late.m_Timing.m_Delay = 0.5f;
            FeedbackPlayer swept = Player(s, late, Keeper(a));
            swept.Play(); s.Tick(0f);
            swept.Seek(1f); s.Tick(0f);
            Assert.That(a.isPlaying, Is.False, "a sweep plays nothing");
            FeedbackPlayer back = Player(s, Clip(a));
            back.Play(in Back); Tick(s, 2);
            Assert.That(a.isPlaying, Is.False, "a backward play plays nothing");
            s.Context.Categories.SetScale(FeedbackCategory.Audio, 0f);
            p.Play(); s.Tick(0f);
            Assert.That(a.isPlaying, Is.False, "the scale 0 plays nothing");
            s.Context.Categories.SetScale(FeedbackCategory.Audio, 1f);
            a.enabled = false;
            p.Play(); s.Tick(0f);
            a.enabled = true;
            Assert.That(a.isPlaying, Is.False, "a disabled source");
            a.gameObject.SetActive(false);
            p.Play(); s.Tick(0f);
            a.gameObject.SetActive(true);
            Assert.That(a.isPlaying, Is.False, "an inactive object");
            Assert.That(Logged("disabled audio source"), Is.Zero, "no engine warning");
            AudioPlayClipItem none = With(new AudioPlayClipItem(), a);
            AudioPlayClipItem texture = With(new AudioPlayClipItem { m_Clip = new Texture2D(1, 1) }, a);
            m_Made.Add(texture.m_Clip);
            Assert.That((Player(s, none).Runtime.Binding(0), Player(s, texture).Runtime.Binding(0)), Is.EqualTo(((ItemBinding)null, (ItemBinding)null)), "refused");
            Assert.That((Logged("no clip"), Logged("the clip is not an AudioClip")), Is.EqualTo((1, 1)), "one reason each");
        }

        // ---- Source Control — the four actions · Play goes on from a pause, starts over a playing source · a Revert puts the
        // start state back (a pause by Pause), Restore the first · a backward play the origin · a sweep skips Play and Unpause
        // but writes Stop and Pause · a disabled source is not played
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AA03_SourceControl()
        {
            FeedbackScheduler s = Sched();
            AudioSource a = Source("A");
            PositiveControl(a);
            FeedbackPlayer play = Player(s, Control(a, AudioSourceAction.Play), Keeper(a));
            play.Play(); s.Tick(0f);
            Assert.That(a.isPlaying, Is.True, "Play");
            play.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(St(a), Is.EqualTo("not playing at 0"), "Revert — the start state (stopped)");
            Paused(a);
            play.Play(); s.Tick(0f);
            Assert.That(St(a), Is.EqualTo("playing at 4410"), "Play on a paused source goes on");
            play.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(St(a), Is.EqualTo("not playing at 4410"), "Revert — the start state (paused, by Pause)");
            Playing(a);
            play.Play(); s.Tick(0f);
            Assert.That((a.isPlaying, a.timeSamples < 4410), Is.EqualTo((true, true)), "Play on a playing source starts over");
            play.Stop(StopMode.Revert); s.Tick(0f);
            a.Stop();
            Playing(a);
            FeedbackPlayer pause = Player(s, Control(a, AudioSourceAction.Pause), Keeper(a));
            pause.Play(); s.Tick(0f);
            Assert.That(St(a), Is.EqualTo("not playing at 4410"), "Pause");
            pause.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(St(a), Is.EqualTo("playing at 4410"), "Revert — playing again");
            FeedbackPlayer stop = Player(s, Control(a, AudioSourceAction.Stop), Keeper(a));
            stop.Play(); s.Tick(0f);
            Assert.That(St(a), Is.EqualTo("not playing at 0"), "Stop");
            stop.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(a.isPlaying, Is.True, "Revert — playing (from the start: the place is not kept)");
            a.Pause();
            FeedbackPlayer unpause = Player(s, Control(a, AudioSourceAction.Unpause), Keeper(a));
            unpause.Play(); s.Tick(0f);
            Assert.That(a.isPlaying, Is.True, "Unpause");
            unpause.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(a.isPlaying, Is.False, "Revert — paused");
            a.Stop();
            FeedbackPlayer first = Player(s, Control(a, AudioSourceAction.Play));
            first.Play(); s.Tick(0f);
            first.Restore();
            Assert.That(a.isPlaying, Is.False, "Restore — the first state");
            FeedbackPlayer turn = Player(s, Control(a, AudioSourceAction.Play), Keeper(a));
            turn.Play(); s.Tick(0f);
            turn.Play(in Back); Tick(s, 45);
            Assert.That(a.isPlaying, Is.False, "a backward play — the origin (stopped)");
            AudioSourceControlItem latePlay = Control(a, AudioSourceAction.Play);
            latePlay.m_Timing.m_Delay = 0.5f;
            FeedbackPlayer sweptPlay = Player(s, latePlay, Keeper(a));
            sweptPlay.Play(); s.Tick(0f);
            sweptPlay.Seek(1f); s.Tick(0f);
            Assert.That(a.isPlaying, Is.False, "a swept Play does not play");
            sweptPlay.Stop(StopMode.Hold);
            Playing(a);
            AudioSourceControlItem lateStop = Control(a, AudioSourceAction.Stop);
            lateStop.m_Timing.m_Delay = 0.5f;
            FeedbackPlayer sweptStop = Player(s, lateStop, Keeper(a));
            sweptStop.Play(); s.Tick(0f);
            sweptStop.Seek(1f); s.Tick(0f);
            Assert.That(a.isPlaying, Is.False, "a swept Stop stops");
            sweptStop.Stop(StopMode.Hold);
            a.Stop();
            a.enabled = false;
            Player(s, Control(a, AudioSourceAction.Play)).Play(); s.Tick(0f);
            a.enabled = true;
            Assert.That((a.isPlaying, Logged("disabled audio source")), Is.EqualTo((false, 0)), "a disabled source is not played");
        }

        // ---- the player's pause — the sound it started pauses and goes on · a one-shot on another source rings on · on the
        // same source the one-shot pauses with it · Hold and the speed 0 leave it · a source with no sound is not started · a
        // source someone else paused stays paused · two plays of one source pause it once
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AA05_ThePlayersPause()
        {
            FeedbackScheduler s = Sched();
            AudioSource a = Source("A");
            PositiveControl(a);
            FeedbackPlayer p = Player(s, Control(a, AudioSourceAction.Play), Keeper(a));
            p.Play(); s.Tick(0f);
            a.timeSamples = 4410;
            Assert.That(a.isPlaying, Is.True, "played");
            p.Pause();
            Assert.That(St(a), Is.EqualTo("not playing at 4410"), "paused with the player");
            Tick(s, 2);
            p.Resume();
            Assert.That(St(a), Is.EqualTo("playing at 4410"), "goes on with the player");
            AudioSource b = Source("B");
            b.clip = null;
            FeedbackPlayer shot = Player(s, Clip(b), Keeper(b));
            shot.Play(); s.Tick(0f);
            Assert.That(b.isPlaying, Is.True, "the one-shot");
            shot.Pause();
            Assert.That(b.isPlaying, Is.True, "a one-shot is not paused");
            shot.Resume();
            p.Stop(StopMode.Hold);
            a.Stop();
            FeedbackPlayer both = Player(s, Control(a, AudioSourceAction.Play), Clip(a), Keeper(a));
            both.Play(); s.Tick(0f);
            both.Pause();
            Assert.That(a.isPlaying, Is.False, "on the same source the one-shot pauses too");
            both.Resume();
            Assert.That(a.isPlaying, Is.True, "and goes on");
            both.Stop(StopMode.Hold);
            a.Stop();
            FeedbackPlayer held = Player(s, Control(a, AudioSourceAction.Play), new HoldItem(), Keeper(a));
            held.Play(); Tick(s, 2);
            Assert.That(a.isPlaying, Is.True, "Hold is no pause");
            held.Stop(StopMode.Hold);
            a.Stop();
            FeedbackPlayer slow = Player(s, Control(a, AudioSourceAction.Play), Keeper(a));
            slow.m_Speed = 0f;
            slow.Play(); Tick(s, 2);
            Assert.That(a.isPlaying, Is.True, "the speed 0 is no pause");
            slow.Stop(StopMode.Hold);
            a.Stop();
            a.clip = null;
            FeedbackPlayer silent = Player(s, Control(a, AudioSourceAction.Play), Keeper(a));
            silent.Play(); s.Tick(0f);
            silent.Pause();
            silent.Resume();
            Assert.That(a.isPlaying, Is.False, "no sound — the resume starts nothing");
            silent.Stop(StopMode.Hold);
            a.clip = m_Long;
            FeedbackPlayer other = Player(s, Control(a, AudioSourceAction.Play), Keeper(a));
            other.Play(); s.Tick(0f);
            a.Pause();
            other.Pause();
            other.Resume();
            Assert.That(a.isPlaying, Is.False, "paused by someone else — stays paused");
            other.Stop(StopMode.Hold);
            a.Stop();
            FeedbackPlayer twice = Player(s, Control(a, AudioSourceAction.Play), Keeper(a));
            twice.m_Policy = PlayPolicy.Overlap;
            twice.Play(); s.Tick(0f);
            twice.Play(); s.Tick(0f);
            twice.Pause();
            Assert.That(a.isPlaying, Is.False, "two plays — paused");
            twice.Resume();
            Assert.That(a.isPlaying, Is.True, "two plays — on again");
        }

        // ---- the ends — Completed · Hold · Complete leave the sound ringing · one paused stops · Revert puts the start back ·
        // an abort (the scheduler's shutdown) and the release (a rebuild · the destruction's — Edit mode calls no OnDestroy: the
        // destruction and the disable behaviours are the PlayMode tests') stop it
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AA06_TheEnds()
        {
            AudioSource a = Source("A");
            PositiveControl(a);
            FeedbackScheduler s = Sched();
            FeedbackPlayer done = Player(s, Control(a, AudioSourceAction.Play));
            done.Play(); Tick(s, 2);
            Assert.That(a.isPlaying, Is.True, "Completed rings on");
            a.Stop();
            foreach (StopMode mode in new[] { StopMode.Hold, StopMode.Complete })
            {
                FeedbackPlayer p = Player(s, Control(a, AudioSourceAction.Play), Keeper(a));
                p.Play(); s.Tick(0f);
                p.Stop(mode); s.Tick(0f);
                Assert.That(a.isPlaying, Is.True, mode + " rings on");
                a.Stop();
                FeedbackPlayer q = Player(s, Control(a, AudioSourceAction.Play), Keeper(a));
                q.Play(); s.Tick(0f);
                a.timeSamples = 4410;
                q.Pause();
                q.Stop(mode); s.Tick(0f);
                Assert.That(St(a), Is.EqualTo("not playing at 0"), mode + " while paused — stopped");
            }
            FeedbackPlayer revert = Player(s, Control(a, AudioSourceAction.Play), Keeper(a));
            revert.Play(); s.Tick(0f);
            revert.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(a.isPlaying, Is.False, "Revert — the start state");
            FeedbackScheduler shut = Sched();
            FeedbackPlayer aborted = Player(shut, Control(a, AudioSourceAction.Play), Keeper(a));
            aborted.Play(); shut.Tick(0f);
            Assert.That(a.isPlaying, Is.True, "played");
            shut.Shutdown();
            Assert.That(a.isPlaying, Is.False, "an abort stops it");
            FeedbackPlayer released = Player(s, Control(a, AudioSourceAction.Play));
            released.Play(); Tick(s, 2);
            Assert.That(a.isPlaying, Is.True, "Completed — ringing");
            released.ReleaseRuntime();
            Assert.That(a.isPlaying, Is.False, "released — stopped");
        }

        // ---- one-shots and their source — the game's Pause · Stop of the source take the one-shot too; the player's Pause does
        // not (no sound of its own on that source)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AA07_OneShotsAndTheirSource()
        {
            FeedbackScheduler s = Sched();
            AudioSource a = Source("A");
            PositiveControl(a);
            a.clip = null;
            FeedbackPlayer p = Player(s, Clip(a), Keeper(a));
            p.Play(); s.Tick(0f);
            a.Pause();
            Assert.That(a.isPlaying, Is.False, "the source's Pause takes the one-shot");
            a.UnPause();
            Assert.That(a.isPlaying, Is.True, "and gives it back");
            a.Stop();
            Assert.That(a.isPlaying, Is.False, "the source's Stop ends it");
            p.Play(); s.Tick(0f);
            p.Pause();
            Assert.That(a.isPlaying, Is.True, "the player's Pause leaves it");
        }

        // ---- the preview (Simulated) — Source Control plays and the end puts the first state back · Play Clip's one-shot stops
        // at the end on a source that was idle, a playing one plays on · the player's pause reaches the sound · no serialized
        // change, the scene clean · a Timeline track refuses the player (the bar's scrub sweeps the items: no sound)
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AA08_ThePreview()
        {
            AudioSource a = Source("A");
            PositiveControl(a);
            Dictionary<string, string> before = SnapOf(a);
            FeedbackPlayer control = Player(null, Control(a, AudioSourceAction.Play), Keeper(a));
            s_Now += 1.0;
            Assert.That(PreviewSession.Start(control, default), Is.EqualTo(PreviewRefusal.None));
            Pump(6);
            Assert.That(a.isPlaying, Is.True, "played in the preview");
            a.timeSamples = 4410;
            control.Pause();
            Assert.That(St(a), Is.EqualTo("not playing at 4410"), "the preview's pause");
            control.Resume();
            Assert.That(a.isPlaying, Is.True, "and on");
            Assert.That(PreviewSession.Simulates(control), Is.True, "Simulated — a Timeline track does not drive it");
            PreviewSession.Current?.Stop();
            Assert.That(a.isPlaying, Is.False, "the end — the first state");
            AudioSource b = Source("B");
            b.clip = null;
            FeedbackPlayer shot = Player(null, Clip(b), Keeper(b));
            s_Now += 1.0;
            Assert.That(PreviewSession.Start(shot, default), Is.EqualTo(PreviewRefusal.None));
            Pump(6);
            Assert.That(b.isPlaying, Is.True, "the one-shot in the preview");
            PreviewSession.Current?.Stop();
            Assert.That(b.isPlaying, Is.False, "the end stops it — the source was idle");
            AudioSource c = Source("C");
            c.loop = true;
            c.Play();
            FeedbackPlayer onPlaying = Player(null, Clip(c), Keeper(c));
            s_Now += 1.0;
            Assert.That(PreviewSession.Start(onPlaying, default), Is.EqualTo(PreviewRefusal.None));
            Pump(6);
            PreviewSession.Current?.Stop();
            Assert.That(c.isPlaying, Is.True, "a source that was playing plays on");
            c.Stop();
            Assert.That(Diff(before, SnapOf(a)), Is.Empty, "serialized");
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.False, "clean");
        }

        static Dictionary<string, string> SnapOf(Object o)
        {
            var d = new Dictionary<string, string>();
            SerializedProperty it = new SerializedObject(o).GetIterator();
            while (it.Next(true))
            {
                switch (it.propertyType)
                {
                    case SerializedPropertyType.Float: d[it.propertyPath] = it.floatValue.ToString("R", CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Integer: d[it.propertyPath] = it.longValue.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Boolean: d[it.propertyPath] = it.boolValue.ToString(); break;
                    case SerializedPropertyType.Enum: d[it.propertyPath] = it.intValue.ToString(CultureInfo.InvariantCulture); break;
                }
            }
            return d;
        }

        static string Diff(Dictionary<string, string> a, Dictionary<string, string> b)
            => string.Join(" · ", a.Where(kv => !b.TryGetValue(kv.Key, out string v) || v != kv.Value).Select(kv => kv.Key + " " + kv.Value + " → " + (b.TryGetValue(kv.Key, out string w) ? w : "none")));

        // ---- a play that ends while paused stops the sound it paused — by Skip, a Seek to its end, Played's subscriber on a
        // play that ends at its start, a backward play sought to its start — and a Resume has nothing to go on
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AA09_APlayEndingPaused()
        {
            FeedbackScheduler s = Sched();
            AudioSource a = Source("A");
            PositiveControl(a);
            foreach (bool skip in new[] { true, false })
            {
                FeedbackPlayer p = Player(s, Control(a, AudioSourceAction.Play), Keeper(a));
                p.Play(); s.Tick(0f);
                a.timeSamples = 4410;
                p.Pause();
                if (skip)
                    p.Skip();
                else
                    p.Seek(2f);
                Assert.That((St(a), p.IsPlaying, Note(p, 0)), Is.EqualTo(("not playing at 0", false, "none")), skip ? "Skip" : "a Seek to the end");
                p.Resume();
                Assert.That(a.isPlaying, Is.False, "the Resume has nothing to go on");
            }
            FeedbackPlayer atStart = Player(s, Control(a, AudioSourceAction.Play));
            atStart.Played += (pl, h) =>
            {
                a.timeSamples = 4410;
                pl.Pause();
            };
            atStart.Play();
            Assert.That((St(a), atStart.IsPlaying), Is.EqualTo(("not playing at 0", false)), "Played's subscriber pauses a play ending at its start");
            FeedbackPlayer back = Player(s, ReverseOnly(Delayed(Control(a, AudioSourceAction.Play), 0.1f)), Keeper(a, 0.5f));
            back.Play(in Back);
            s.Tick(0.55f);
            Assert.That(a.isPlaying, Is.True, "a Reverse Only sound");
            a.timeSamples = 4410;
            back.Pause();
            back.Seek(0f);
            Assert.That((St(a), back.IsPlaying), Is.EqualTo(("not playing at 0", false)), "a backward play sought to its start");
        }

        // ---- a sound started after a Pause in the same pass pauses with the play and goes on with its Resume — a tick, the
        // Begin window, a backward pass
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AA10_APauseInsideThePlaysEvents()
        {
            FeedbackScheduler s = Sched();
            AudioSource a = Source("A");
            PositiveControl(a);
            FeedbackPlayer tick = null;
            tick = Player(s, Call(() => tick.Pause(), 0.1f), Control(a, AudioSourceAction.Play), Keeper(a));
            tick.Play(); s.Tick(0f); s.Tick(0.2f);
            Assert.That((a.isPlaying, tick.HasPaused, Note(tick, 1)), Is.EqualTo((false, true, "paused with the player")), "a tick");
            tick.Resume();
            Assert.That(a.isPlaying, Is.True, "a tick — goes on");
            AudioSource b = Source("B");
            FeedbackPlayer begin = null;
            begin = Player(s, Call(() => begin.Pause()), Control(b, AudioSourceAction.Play), Keeper(b));
            begin.Play();
            Assert.That((b.isPlaying, begin.HasPaused, Note(begin, 1)), Is.EqualTo((false, true, "paused with the player")), "the Begin window");
            begin.Resume();
            Assert.That(b.isPlaying, Is.True, "the Begin window — goes on");
            AudioSource c = Source("C");
            FeedbackPlayer back = null;
            back = Player(s, ReverseOnly(Delayed(Control(c, AudioSourceAction.Play), 0.1f)), ReverseOnly(Call(() => back.Pause(), 0.1f)), Keeper(c, 0.5f));
            back.Play(in Back);
            s.Tick(0.65f);
            Assert.That((c.isPlaying, back.HasPaused, Note(back, 0)), Is.EqualTo((false, true, "paused with the player")), "a backward pass");
            back.Resume();
            Assert.That(c.isPlaying, Is.True, "a backward pass — goes on");
        }

        // ---- a play that ends in the pass of its Pause stops the sound whichever came first, and with a Skip beside the Pause
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AA11_APauseInThePassThatEnds()
        {
            FeedbackScheduler s = Sched();
            AudioSource a = Source("A");
            PositiveControl(a);
            FeedbackPlayer before = null;
            before = Player(s, Control(a, AudioSourceAction.Play), Call(() => before.Pause(), 0.1f));
            before.Play(); s.Tick(0f);
            a.timeSamples = 4410;
            s.Tick(0.2f);
            Assert.That((St(a), before.IsPlaying), Is.EqualTo(("not playing at 0", false)), "the sound before the Pause");
            FeedbackPlayer after = null;
            after = Player(s, Call(() => after.Pause(), 0.1f), Control(a, AudioSourceAction.Play));
            after.Play(); s.Tick(0f); s.Tick(0.2f);
            Assert.That((St(a), after.IsPlaying), Is.EqualTo(("not playing at 0", false)), "the sound after the Pause");
            FeedbackPlayer skip = null;
            skip = Player(s, Call(() =>
            {
                skip.Pause();
                skip.Skip();
            }, 0.1f), Control(a, AudioSourceAction.Play), Keeper(a));
            skip.Play(); s.Tick(0f); s.Tick(0.2f);
            Assert.That((St(a), skip.IsPlaying), Is.EqualTo(("not playing at 0", false)), "a Skip beside the Pause");
        }

        // ---- a Pause and a Resume before the sound leave it ringing · a plain Completed rings on · a paused play's Seek keeps
        // the pause, and leaves a sound the game started itself on that source
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AA12_WhatAPauseLeavesAlone()
        {
            FeedbackScheduler s = Sched();
            AudioSource a = Source("A");
            PositiveControl(a);
            FeedbackPlayer both = null;
            both = Player(s, Call(() =>
            {
                both.Pause();
                both.Resume();
            }, 0.1f), Control(a, AudioSourceAction.Play), Keeper(a));
            both.Play(); s.Tick(0f); s.Tick(0.2f);
            Assert.That((a.isPlaying, both.HasPaused), Is.EqualTo((true, false)), "a Pause and a Resume before the sound");
            both.Stop(StopMode.Hold);
            a.Stop();
            FeedbackPlayer done = Player(s, Control(a, AudioSourceAction.Play));
            done.Play(); Tick(s, 2);
            Assert.That((a.isPlaying, done.IsPlaying), Is.EqualTo((true, false)), "a plain Completed rings on");
            a.Stop();
            FeedbackPlayer inside = Player(s, Control(a, AudioSourceAction.Play), Keeper(a));
            inside.Play(); s.Tick(0f);
            a.timeSamples = 4410;
            inside.Pause();
            inside.Seek(0.5f);
            Assert.That((St(a), inside.IsPlaying), Is.EqualTo(("not playing at 4410", true)), "a Seek inside keeps the pause");
            inside.Resume();
            Assert.That(a.isPlaying, Is.True, "and goes on");
            inside.Stop(StopMode.Hold);
            a.Stop();
            FeedbackPlayer theirs = Player(s, Control(a, AudioSourceAction.Play), Keeper(a));
            theirs.Play(); s.Tick(0f);
            a.Stop();
            theirs.Pause();
            a.Play();
            theirs.Seek(0.5f);
            Assert.That(a.isPlaying, Is.True, "a paused play's Seek leaves the game's own sound");
        }

        // ---- a preview whose paused play is skipped gives its voice back, so the session ends by itself
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void AA13_APausedPreviewEnds()
        {
            AudioSource a = Source("A");
            PositiveControl(a);
            var voice = new AudioPlayAtPointItem
            {
                m_Resource = m_Long,
                m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = a.transform, m_Path = "", m_Anchor = "" },
                m_Motion = new SetMotion { m_Params = new SetParams { m_Value = new Vector4(1f, 0, 0, 0) } },
            };
            FeedbackPlayer p = Player(null, voice, Keeper(a));
            s_Now += 1.0;
            Assert.That(PreviewSession.Start(p, default), Is.EqualTo(PreviewRefusal.None));
            Pump(6);
            p.Pause();
            p.Skip();
            Pump(300);
            Assert.That(PreviewSession.Current, Is.Null, "the session ended");
        }
    }
}
