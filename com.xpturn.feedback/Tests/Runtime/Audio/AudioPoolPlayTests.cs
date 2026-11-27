using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests
{
    // Play At Point in Play mode — the voices the pools keep and AudioVoiceService looks after: the take, the end (the sound's
    // own — real time), the listener's pause, Stop After and the fade out (the scheduler's clock), the ends, the player's
    // pause and destruction, the output chain, the allocations, a scene's unload. One listener in the scene; test schedulers
    // (a runtime one for the unload) ticked by hand — the frames that wait for a sound tick them with the frame's time. The test mixers are under Resources (the build setup)
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    public sealed class AudioPoolPlayTests
    {
        const float Step = 0.05f;
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly PlayArgs Back = PlayArgs.Default.WithDirection(PlayerDirection.Backward);
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_S = new List<FeedbackScheduler>();
        readonly List<string> m_Warnings = new List<string>();

        [SetUp]
        public void SetUp()
        {
            m_Made.Add(new GameObject("Listener", typeof(AudioListener)));
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            AudioListener.pause = false;
            Time.timeScale = 1f;
            foreach (FeedbackScheduler s in m_S)
                if (!s.IsShutdown) s.Shutdown();
            foreach (Object o in m_Made)
                if (o != null) Object.Destroy(o);
            m_Made.Clear();
            m_S.Clear();
            m_Warnings.Clear();
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Log)
                m_Warnings.Add(message);
        }

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_S.Add(s);
            return s;
        }

        static AudioVoiceService Voices(FeedbackScheduler s) => AudioVoiceService.Of(s.Context);

        FeedbackPlayer Player(FeedbackScheduler s, Object output, PlayerDisableBehavior disable, params FeedbackItem[] items)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            FeedbackPlayer p = go.AddComponent<FeedbackPlayer>();
            p.m_DisableBehavior = disable;
            p.m_AudioOutput = output;
            p.m_Items.AddRange(items);
            p.Bind(s);
            p.EnsureRuntime();
            return p;
        }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items) => Player(s, null, PlayerDisableBehavior.Continue, items);

        AudioClip Clip(float seconds)
        {
            AudioClip clip = AudioClip.Create("pool", Mathf.CeilToInt(seconds * 44100), 1, 44100, false);
            m_Made.Add(clip);
            return clip;
        }

        Transform Anchor(Vector3 at)
        {
            var go = new GameObject("Anchor");
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 90f, 0f));
            m_Made.Add(go);
            return go.transform;
        }

        static AudioPlayAtPointItem Point(Object resource, Transform target, Action<AudioPlayAtPointItem> set = null)
        {
            var item = new AudioPlayAtPointItem
            {
                m_Resource = resource,
                m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" },
                m_Motion = new SetMotion { m_Params = new SetParams { m_Value = new Vector4(1f, 0, 0, 0) } },
            };
            set?.Invoke(item);
            return item;
        }

        // a timed item that keeps the play running
        static PositionItem Keeper(Transform target, float d = 2f)
        {
            var keeper = new PositionItem
            {
                m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" },
                m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(0.01f, 0, 0, 0), m_Vibrato = 1f, m_Decay = 0f } },
            };
            keeper.m_Timing.m_Duration = d;
            return keeper;
        }

        // an Audio Random Container made in code — the type is internal; two elements of the clip, once each or endless. A
        // player's linker strips the types nothing names: none there (the editor makes one)
        AudioResource Container(AudioClip clip, bool endless)
        {
            Type t = typeof(AudioResource).Assembly.GetType("UnityEngine.Audio.AudioRandomContainer");
            Type e = typeof(AudioResource).Assembly.GetType("UnityEngine.Audio.AudioContainerElement");
            Assert.That(t != null && e != null || !Application.isEditor, Is.True, "the editor knows the container's types");
            if (t == null || e == null)
                return null;
            var arc = (AudioResource)Activator.CreateInstance(t, true);
            Array list = Array.CreateInstance(e, 2);
            for (int i = 0; i < 2; i++)
            {
                var el = (Object)Activator.CreateInstance(e, true);
                e.GetProperty("audioClip", All).SetValue(el, clip);
                e.GetProperty("enabled", All)?.SetValue(el, true);
                list.SetValue(el, i);
                m_Made.Add(el);
            }
            t.GetProperty("elements", All).SetValue(arc, list);
            if (endless)
            {
                PropertyInfo loop = t.GetProperty("loopMode", All);
                loop.SetValue(arc, Enum.Parse(loop.PropertyType, "Infinite"));
            }
            m_Made.Add(arc);
            return arc;
        }

        static AudioMixerGroup Master(string mixer) => Resources.Load<AudioMixer>(mixer).FindMatchingGroups("Master")[0];

        static void Tick(FeedbackScheduler s, int n)
        {
            for (int i = 0; i < n; i++)
                s.Tick(Step);
        }

        // real frames — the scheduler ticked with each frame's time — until the condition or the time
        static IEnumerator Frames(FeedbackScheduler s, double seconds, Func<bool> until = null)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds && (until == null || !until()))
            {
                yield return null;
                s.Tick(Time.unscaledDeltaTime);
            }
        }

        // the engine plays here — else the reads below say nothing
        void PositiveControl()
        {
            var go = new GameObject("Control");
            m_Made.Add(go);
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.clip = Clip(0.1f);
            a.Play();
            Assert.That(a.isPlaying, Is.True, "the engine plays (Play)");
            a.Stop();
        }

        static AudioSource Source(GameObject voice) => voice.GetComponent<AudioSource>();

        static UnityEventItem Call(UnityEngine.Events.UnityAction action, float delay = 0f)
        {
            var item = new UnityEventItem();
            item.m_Event.AddListener(action);
            item.m_Timing.m_Delay = delay;
            return item;
        }

        // live · paused with the player · sounding
        static (int, int, int) Counts(AudioVoiceService v)
        {
            int paused = 0, sounding = 0;
            for (int i = 0; i < v.LiveCount; i++)
            {
                if (v.PausedAt(i))
                    paused++;
                if (Source(v.LiveAt(i)).isPlaying)
                    sounding++;
            }
            return (v.LiveCount, paused, sounding);
        }

        // true only when five runs in a row allocate
        static bool Allocates(TestDelegate run)
        {
            for (int i = 0; i < 5; i++)
            {
                if (!UIs.AllocatingGCMemory().ApplyTo(run).IsSuccess)
                    return false;
            }
            return true;
        }

        // ---- the take: reserved at the prepare · the target's place with the offset in its space (the play's place without
        // the offset) · the eight fields written · playing · in the active scene, no flags · one template, the pool at the
        // largest size asked · a stronger play is louder up to the full volume
        [UnityTest]
        public IEnumerator APP01_TheTake()
        {
            yield return null;
            PositiveControl();
            FeedbackScheduler s = Sched();
            AudioClip clip = Clip(2f);
            Transform at = Anchor(new Vector3(1f, 2f, 3f));
            AudioMixerGroup group = Master("FeedbackTestMixer");
            FeedbackPlayer p = Player(s, Point(clip, at, i =>
            {
                i.m_Output = group; i.m_Volume = 0.8f; i.m_Pitch = 1.2f; i.m_SpatialBlend = 0.5f; i.m_MinDistance = 2f; i.m_MaxDistance = 40f;
                i.m_IgnoreListenerPause = true; i.m_Offset = new Vector3(0f, 0f, 1f); i.m_PoolSize = 4;
            }));
            AudioVoiceService voices = Voices(s);
            GameObject template = voices.TemplateIfMade;
            PoolService pools = s.Context.PoolsIfMade;
            Assert.That((pools.MadeOf(template), pools.RestingOf(template)), Is.EqualTo((4, 4)), "reserved at the prepare");
            p.Play();
            s.Tick(Step);
            Assert.That(voices.LiveCount, Is.EqualTo(1), "a voice");
            GameObject v = voices.LiveAt(0);
            AudioSource src = Source(v);
            Assert.That(Vector3.Distance(v.transform.position, at.TransformPoint(new Vector3(0f, 0f, 1f))), Is.LessThan(1e-4f), "the target's place, the offset in its space");
            Assert.That((src.resource, src.outputAudioMixerGroup, src.volume, src.pitch, src.spatialBlend, src.minDistance, src.maxDistance, src.ignoreListenerPause, src.isPlaying),
                Is.EqualTo(((AudioResource)clip, group, 0.8f, 1.2f, 0.5f, 2f, 40f, true, true)), "the fields");
            Assert.That((v.scene == SceneManager.GetActiveScene(), v.hideFlags, pools.RestingOf(template)), Is.EqualTo((true, HideFlags.None, 3)), "the active scene · no flags · one taken");
            p.Play(PlayArgs.Default.WithPosition(new Vector3(5f, 0f, 0f)).WithIntensity(2f));
            s.Tick(Step);
            GameObject w = voices.LiveAt(voices.LiveCount - 1);
            Assert.That((Vector3.Distance(w.transform.position, new Vector3(5f, 0f, 0f)) < 1e-4f, Source(w).volume), Is.EqualTo((true, 1f)), "the play's place · louder up to full");
            p.Play(PlayArgs.Default.WithIntensity(0.5f));
            s.Tick(Step);
            Assert.That(Source(voices.LiveAt(voices.LiveCount - 1)).volume, Is.EqualTo(0.4f).Within(1e-6f), "a weaker play is quieter");
            Player(s, Point(clip, at, i => i.m_PoolSize = 6));
            Assert.That((voices.TemplateIfMade, pools.MadeOf(template)), Is.EqualTo((template, 6)), "one template · the largest size");
            Assert.That(m_Warnings, Is.Empty, "no warning");
        }

        // ---- a voice taken again keeps nothing of its last take
        [UnityTest]
        public IEnumerator APP02_TheOverwrite()
        {
            yield return null;
            PositiveControl();
            FeedbackScheduler s = Sched();
            AudioClip clip = Clip(2f), other = Clip(1f);
            Transform at = Anchor(Vector3.zero);
            FeedbackPlayer a = Player(s, Point(clip, at, i =>
            {
                i.m_Output = Master("FeedbackTestMixer"); i.m_Volume = 0.3f; i.m_Pitch = 2f; i.m_SpatialBlend = 0f; i.m_MinDistance = 3f; i.m_MaxDistance = 9f;
                i.m_IgnoreListenerPause = true; i.m_PoolSize = 1;
            }), Keeper(at));
            FeedbackPlayer b = Player(s, Point(other, at, i => i.m_PoolSize = 1), Keeper(at));
            a.Play();
            s.Tick(Step);
            GameObject first = Voices(s).LiveAt(0);
            a.Stop(StopMode.Revert);
            s.Tick(0f);
            b.Play();
            s.Tick(Step);
            GameObject second = Voices(s).LiveAt(0);
            AudioSource src = Source(second);
            Assert.That(second, Is.SameAs(first), "the same voice");
            Assert.That((src.resource, src.outputAudioMixerGroup, src.volume, src.pitch, src.spatialBlend, src.minDistance, src.maxDistance, src.ignoreListenerPause),
                Is.EqualTo(((AudioResource)other, (AudioMixerGroup)null, 1f, 1f, 1f, 1f, 500f, false)), "nothing of the last take");
        }

        // ---- the end: a clip's end gives the voice back (at time scale 0 too — the sound's clock) · a container's end too, with
        // no warning (its place is never read) · the voice given back is stopped
        [UnityTest]
        public IEnumerator APP03_TheEnd()
        {
            yield return null;
            PositiveControl();
            FeedbackScheduler s = Sched();
            Transform at = Anchor(Vector3.zero);
            AudioVoiceService voices = Voices(s);
            FeedbackPlayer clip = Player(s, Point(Clip(0.2f), at));
            clip.Play();
            s.Tick(Step);
            GameObject v = voices.LiveAt(0);
            yield return Frames(s, 2.0, () => voices.LiveCount == 0);
            Assert.That((voices.LiveCount, s.Context.PoolsIfMade.LiveCount), Is.EqualTo((0, 0)), "the clip's end gives it back");
            Assert.That((Source(v).isPlaying, Source(v).time), Is.EqualTo((false, 0f)), "stopped");
            Time.timeScale = 0f;
            clip.Play();
            s.Tick(Step);
            yield return Frames(s, 2.0, () => voices.LiveCount == 0);
            Assert.That(voices.LiveCount, Is.Zero, "at time scale 0 too");
            Time.timeScale = 1f;
            AudioResource arc = Container(Clip(0.1f), false);
            if (arc != null)
            {
                FeedbackPlayer container = Player(s, Point(arc, at));
                container.Play();
                s.Tick(Step);
                Assert.That(Source(voices.LiveAt(0)).isPlaying, Is.True, "a container plays");
                yield return Frames(s, 2.0, () => voices.LiveCount == 0);
                Assert.That(voices.LiveCount, Is.Zero, "a container's end gives it back");
            }
            Assert.That(m_Warnings, Is.Empty, "no warning");
        }

        // ---- the game's listener pause holds the voice (not given back) and it goes on after · an Ignore Listener Pause voice
        // plays through it · the player's pause under the listener's holds it past the listener's
        [UnityTest]
        public IEnumerator APP04_TheListenersPause()
        {
            yield return null;
            PositiveControl();
            FeedbackScheduler s = Sched();
            Transform at = Anchor(Vector3.zero);
            AudioVoiceService voices = Voices(s);
            AudioClip clip = Clip(0.3f);
            FeedbackPlayer p = Player(s, Point(clip, at), Keeper(at, 10f));
            p.Play();
            s.Tick(Step);
            AudioListener.pause = true;
            yield return Frames(s, 0.6);
            Assert.That((voices.LiveCount, Source(voices.LiveAt(0)).isPlaying), Is.EqualTo((1, false)), "held, not given back");
            AudioListener.pause = false;
            yield return Frames(s, 0.1);
            Assert.That(Source(voices.LiveAt(0)).isPlaying, Is.True, "goes on");
            yield return Frames(s, 2.0, () => voices.LiveCount == 0);
            Assert.That(voices.LiveCount, Is.Zero, "then its end");
            FeedbackPlayer ignore = Player(s, Point(clip, at, i => i.m_IgnoreListenerPause = true));
            AudioListener.pause = true;
            ignore.Play();
            s.Tick(Step);
            yield return Frames(s, 2.0, () => voices.LiveCount == 0);
            Assert.That(voices.LiveCount, Is.Zero, "an Ignore Listener Pause voice plays through it to its end");
            AudioListener.pause = false;
            p.Play();
            s.Tick(Step);
            AudioListener.pause = true;
            yield return null;
            p.Pause();
            AudioListener.pause = false;
            yield return Frames(s, 0.5);
            Assert.That((voices.LiveCount, Source(voices.LiveAt(0)).isPlaying, voices.PausedAt(0)), Is.EqualTo((1, false, true)), "the player's pause holds it past the listener's");
            p.Resume();
            yield return Frames(s, 0.1);
            Assert.That(Source(voices.LiveAt(0)).isPlaying, Is.True, "the Resume plays it");
        }

        // ---- Stop After: counted while the voice plays (the scheduler's clock), then the fade out — half way at half the fade —
        // and given back · held by the player's pause and the listener's · no fade: at once · an endless container ends so. The
        // ends fall between the ticks
        static float Volume(FeedbackScheduler s) => Source(Voices(s).LiveAt(0)).volume;

        [UnityTest]
        public IEnumerator APP05_StopAfterAndTheFade()
        {
            yield return null;
            PositiveControl();
            FeedbackScheduler s = Sched();
            Transform at = Anchor(Vector3.zero);
            AudioVoiceService voices = Voices(s);
            AudioClip clip = Clip(4f);
            FeedbackPlayer p = Player(s, Point(clip, at, i => { i.m_Volume = 0.8f; i.m_StopAfter = 0.125f; i.m_FadeOut = 0.2f; }), Keeper(at, 10f));
            p.Play();
            s.Tick(0f);
            Tick(s, 2);
            Assert.That(Volume(s), Is.EqualTo(0.8f), "0.1 s — the full volume");
            p.Pause();
            yield return null;
            Tick(s, 10);
            p.Resume();
            yield return null;
            Assert.That((voices.LiveCount, Volume(s)), Is.EqualTo((1, 0.8f)), "the player's pause does not count");
            AudioListener.pause = true;
            yield return null;
            Tick(s, 10);
            AudioListener.pause = false;
            yield return null;
            Assert.That((voices.LiveCount, Volume(s)), Is.EqualTo((1, 0.8f)), "the listener's pause does not count");
            Tick(s, 2);
            Assert.That(Volume(s), Is.EqualTo(0.5f).Within(1e-4f), "0.2 s — 0.075 s into the 0.2 s fade: five eighths");
            Tick(s, 2);
            Assert.That(voices.LiveCount, Is.EqualTo(1), "0.3 s — still fading");
            Tick(s, 1);
            Assert.That(voices.LiveCount, Is.Zero, "0.35 s — faded out, given back");
            FeedbackPlayer cut = Player(s, Point(clip, at, i => { i.m_StopAfter = 0.075f; i.m_FadeOut = 0f; }));
            cut.Play();
            s.Tick(0f);
            Tick(s, 1);
            Assert.That(voices.LiveCount, Is.EqualTo(1), "0.05 s");
            Tick(s, 1);
            Assert.That(voices.LiveCount, Is.Zero, "no fade — at Stop After");
            AudioResource arc = Container(Clip(0.2f), true);
            if (arc == null)
                yield break;
            FeedbackPlayer endless = Player(s, Point(arc, at, i => { i.m_StopAfter = 0.5f; i.m_FadeOut = 0.1f; }));
            endless.Play();
            s.Tick(0f);
            yield return Frames(s, 0.3);
            Assert.That(voices.LiveCount, Is.EqualTo(1), "an endless container rings");
            yield return Frames(s, 2.0, () => voices.LiveCount == 0);
            Assert.That(voices.LiveCount, Is.Zero, "and ends by Stop After");
        }

        // ---- the ends: Revert stops the record's voices at once (another player's go on) · Completed · Hold · Complete leave them
        // ringing, a paused one stops · Restore() stops all · a backward play stops the origin play's · an abort (the
        // scheduler's shutdown) stops them
        [UnityTest]
        public IEnumerator APP06_TheEnds()
        {
            yield return null;
            PositiveControl();
            FeedbackScheduler s = Sched();
            Transform at = Anchor(Vector3.zero);
            AudioVoiceService voices = Voices(s);
            AudioClip clip = Clip(4f);
            FeedbackPlayer other = Player(s, Point(clip, at));
            other.Play();
            s.Tick(Step);
            GameObject others = voices.LiveAt(0);
            FeedbackPlayer revert = Player(s, Point(clip, at), Keeper(at));
            revert.Play();
            s.Tick(Step);
            revert.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That((voices.LiveCount, voices.LiveAt(0)), Is.EqualTo((1, others)), "Revert — its voice stopped, another player's on");
            foreach (StopMode mode in new[] { StopMode.Hold, StopMode.Complete })
            {
                FeedbackPlayer p = Player(s, Point(clip, at), Keeper(at));
                p.Play();
                s.Tick(Step);
                p.Stop(mode);
                s.Tick(0f);
                Assert.That(voices.LiveCount, Is.EqualTo(2), mode + " rings on");
                FeedbackPlayer q = Player(s, Point(clip, at), Keeper(at));
                q.Play();
                s.Tick(Step);
                q.Pause();
                q.Stop(mode);
                s.Tick(0f);
                Assert.That(voices.LiveCount, Is.EqualTo(2), mode + " while paused — stopped");
                p.Restore();
                Assert.That(voices.LiveCount, Is.EqualTo(1), "Restore() — all its voices");
            }
            FeedbackPlayer done = Player(s, Point(clip, at));
            done.Play();
            s.Tick(Step);
            Assert.That(voices.LiveCount, Is.EqualTo(2), "Completed rings on");
            done.Play(Back);
            s.Tick(Step);
            Assert.That(voices.LiveCount, Is.EqualTo(1), "a backward play stops the origin play's");
            FeedbackScheduler shut = Sched();
            FeedbackPlayer aborted = Player(shut, Point(clip, at), Keeper(at));
            aborted.Play();
            shut.Tick(Step);
            GameObject voice = Voices(shut).LiveAt(0);
            AudioSource src = Source(voice);
            shut.Shutdown();
            Assert.That(voice == null || !src.isPlaying, Is.True, "an abort stops it");
        }

        // ---- the player's pause: its record's voices only · two pauses, one resume · a voice of an ended play is not reached
        [UnityTest]
        public IEnumerator APP07_ThePlayersPause()
        {
            yield return null;
            PositiveControl();
            FeedbackScheduler s = Sched();
            Transform at = Anchor(Vector3.zero);
            AudioVoiceService voices = Voices(s);
            AudioClip clip = Clip(4f);
            FeedbackPlayer a = Player(s, Point(clip, at), Keeper(at));
            FeedbackPlayer b = Player(s, Point(clip, at), Keeper(at));
            FeedbackPlayer ended = Player(s, Point(clip, at));
            a.Play(); b.Play(); ended.Play();
            s.Tick(Step);
            yield return null;
            GameObject va = voices.LiveAt(0), vb = voices.LiveAt(1), ve = voices.LiveAt(2);
            a.Pause();
            ended.Pause();
            s.Tick(0f);
            yield return null;
            Assert.That((Source(va).isPlaying, Source(vb).isPlaying, Source(ve).isPlaying), Is.EqualTo((false, true, true)), "its record's voice only · an ended play's is not reached");
            a.Pause();
            a.Resume();
            s.Tick(0f);
            yield return null;
            Assert.That(Source(va).isPlaying, Is.True, "two pauses, one resume");
        }

        // ---- the player destroyed: Continue — an ended play's voice rings to its end · Stop Revert — a running play's voice
        // stops, an ended one's rings · a Rebuild keeps the voices (Restore() then stops them)
        [UnityTest]
        public IEnumerator APP08_TheRelease()
        {
            yield return null;
            PositiveControl();
            FeedbackScheduler s = Sched();
            Transform at = Anchor(Vector3.zero);
            AudioVoiceService voices = Voices(s);
            AudioClip clip = Clip(0.4f);
            FeedbackPlayer dying = Player(s, Point(clip, at));
            dying.Play();
            s.Tick(Step);
            Object.Destroy(dying.gameObject);
            yield return null;
            s.Tick(Step);
            Assert.That((voices.LiveCount, Source(voices.LiveAt(0)).isPlaying), Is.EqualTo((1, true)), "Continue — the voice rings on");
            yield return Frames(s, 2.0, () => voices.LiveCount == 0);
            Assert.That(voices.LiveCount, Is.Zero, "to its end");
            AudioClip longer = Clip(4f);
            FeedbackPlayer stop = Player(s, null, PlayerDisableBehavior.StopRevert, Point(longer, at), Keeper(at));
            stop.Play();
            s.Tick(Step);
            FeedbackPlayer quick = Player(s, null, PlayerDisableBehavior.StopRevert, Point(longer, at));
            quick.Play();
            s.Tick(Step);
            Object.Destroy(stop.gameObject);
            Object.Destroy(quick.gameObject);
            yield return null;
            s.Tick(0f);
            Assert.That(voices.LiveCount, Is.EqualTo(1), "Stop Revert — the running play's voice stopped, the ended one's rings");
            FeedbackPlayer kept = Player(s, Point(longer, at));
            kept.Play();
            s.Tick(Step);
            Assert.That(kept.Rebuild(), Is.True, "rebuilt");
            Assert.That(voices.LiveCount, Is.EqualTo(2), "a Rebuild keeps the voice");
            kept.Restore();
            Assert.That(voices.LiveCount, Is.EqualTo(1), "the new binding owns it — Restore() stops it");
        }

        // ---- a swept start takes no voice · the Audio category's scale 0 takes none, 0.5 halves the volume · the pitch is the
        // same for a seed and an occurrence
        [UnityTest]
        public IEnumerator APP09_TheSweepTheCategoryThePitch()
        {
            yield return null;
            PositiveControl();
            FeedbackScheduler s = Sched();
            Transform at = Anchor(Vector3.zero);
            AudioVoiceService voices = Voices(s);
            AudioClip clip = Clip(4f);
            var swept = new GameObject("Swept").AddComponent<FeedbackPlayer>();
            m_Made.Add(swept.gameObject);
            AudioPlayAtPointItem late = Point(clip, at);
            late.m_Timing.m_Delay = 0.5f;
            swept.m_Items.Add(late);
            swept.m_Items.Add(Keeper(at));
            swept.Bind(s);
            swept.EnsureRuntime();
            swept.Play();
            s.Tick(0f);
            Assert.That(swept.Seek(1f), Is.True, "seeked past it");
            s.Tick(0f);
            Assert.That(voices.LiveCount, Is.Zero, "a swept start — no voice");
            s.Context.Categories.SetScale(FeedbackCategory.Audio, 0f);
            FeedbackPlayer p = Player(s, Point(clip, at, i => { i.m_Volume = 0.8f; i.m_PitchRandom = 0.5f; }));
            p.Play();
            s.Tick(Step);
            Assert.That(voices.LiveCount, Is.Zero, "scale 0 — no voice");
            s.Context.Categories.SetScale(FeedbackCategory.Audio, 0.5f);
            p.Play(PlayArgs.Default.WithSeed(11u));
            s.Tick(Step);
            AudioSource first = Source(voices.LiveAt(0));
            Assert.That(first.volume, Is.EqualTo(0.4f).Within(1e-6f), "scale 0.5 — half the volume");
            p.Play(PlayArgs.Default.WithSeed(11u));
            s.Tick(Step);
            Assert.That(Source(voices.LiveAt(1)).pitch, Is.EqualTo(first.pitch), "the same seed — the same pitch");
            Assert.That(first.pitch, Is.InRange(0.5f, 1.5f), "within the range");
        }

        // ---- the output chain: the item's, else the player's, else the project's, else none · one preset for two players —
        // two groups
        [UnityTest]
        public IEnumerator APP10_TheOutputChain()
        {
            yield return null;
            PositiveControl();
            FeedbackScheduler s = Sched();
            Transform at = Anchor(Vector3.zero);
            AudioVoiceService voices = Voices(s);
            AudioClip clip = Clip(4f);
            AudioMixerGroup g1 = Master("FeedbackTestMixer"), g2 = Master("FeedbackTestMixerOther");
            AudioMixerGroup Taken(FeedbackPlayer p)
            {
                p.Play();
                s.Tick(Step);
                return Source(voices.LiveAt(voices.LiveCount - 1)).outputAudioMixerGroup;
            }
            s.Context.Settings.AudioOutput = g2;
            Assert.That(Taken(Player(s, g2, PlayerDisableBehavior.Continue, Point(clip, at, i => i.m_Output = g1))), Is.SameAs(g1), "the item's first");
            Assert.That(Taken(Player(s, g1, PlayerDisableBehavior.Continue, Point(clip, at))), Is.SameAs(g1), "then the player's");
            Assert.That(Taken(Player(s, Point(clip, at))), Is.SameAs(g2), "then the project's");
            s.Context.Settings.AudioOutput = null;
            Assert.That(Taken(Player(s, Point(clip, at))), Is.Null, "else none");
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            m_Made.Add(preset);
            preset.m_Items.Add(Point(clip, at));
            FeedbackPlayer vo = Player(s, g1, PlayerDisableBehavior.Continue);
            FeedbackPlayer sfx = Player(s, g2, PlayerDisableBehavior.Continue);
            vo.m_Preset = preset;
            sfx.m_Preset = preset;
            vo.Rebuild();
            sfx.Rebuild();
            Assert.That((Taken(vo), Taken(sfx)), Is.EqualTo((g1, g2)), "one preset, two players' groups");
        }

        // ---- warmed, a take and its Revert allocate nothing · Stop After's fade and the give-back too · the end of a sound — the
        // tick that gives it back — too · never grown; a pool of one grows once with a warning
        [UnityTest]
        public IEnumerator APP11_TheCyclesAllocateNothing()
        {
            yield return null;
            PositiveControl();
            float f = 0f;
            Assert.That(UIs.AllocatingGCMemory().ApplyTo((TestDelegate)(() => { var x = new int[4]; f += x.Length; })).IsSuccess, Is.True, "the positive control");
            FeedbackScheduler s = Sched();
            Transform at = Anchor(Vector3.zero);
            AudioVoiceService voices = Voices(s);
            AudioClip clip = Clip(4f);
            FeedbackPlayer p = Player(s, Point(clip, at, i => { i.m_PoolSize = 2; i.m_PitchRandom = 0.2f; }), Keeper(at));
            TestDelegate take = () =>
            {
                p.Play();
                s.Tick(Step);
                p.Stop(StopMode.Revert);
                s.Tick(0f);
            };
            FeedbackPlayer faded = Player(s, Point(clip, at, i => { i.m_PoolSize = 2; i.m_StopAfter = 0.125f; i.m_FadeOut = 0.125f; }));
            TestDelegate fade = () =>
            {
                faded.Play();
                for (int i = 0; i < 8; i++)
                    s.Tick(Step);
            };
            for (int i = 0; i < 3; i++)
            {
                take();
                fade();
            }
            int hits = 0;
            for (int i = 0; i < 5; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(take).IsSuccess)
                    hits++;
                if (UIs.AllocatingGCMemory().ApplyTo(fade).IsSuccess)
                    hits++;
            }
            Assert.That((hits, voices.LiveCount), Is.EqualTo((0, 0)), "the take · the fade allocate nothing, given back");
            FeedbackPlayer end = Player(s, Point(Clip(0.1f), at, i => i.m_PoolSize = 2));
            int endHits = 0;
            TestDelegate tick = () => s.Tick(Step);
            for (int round = 0; round < 3; round++)
            {
                end.Play();
                s.Tick(Step);
                AudioSource src = Source(voices.LiveAt(0));
                var sw = Stopwatch.StartNew();
                while (src.isPlaying && sw.Elapsed.TotalSeconds < 1.0)
                    yield return null;
                if (round > 0 && UIs.AllocatingGCMemory().ApplyTo(tick).IsSuccess)
                    endHits++;
                else if (round == 0)
                    tick();
                Assert.That(voices.LiveCount, Is.Zero, "the end's tick gives it back");
            }
            PoolService pools = s.Context.PoolsIfMade;
            GameObject template = voices.TemplateIfMade;
            Assert.That((endHits, pools.GrownOf(template)), Is.EqualTo((0, 0)), "the end's tick allocates nothing · never grown");
            FeedbackScheduler alone = Sched();
            FeedbackPlayer grow = Player(alone, Point(clip, at, i => i.m_PoolSize = 1));
            grow.Play(); alone.Tick(Step);
            grow.Play(); alone.Tick(Step);
            Assert.That(alone.Context.PoolsIfMade.GrownOf(Voices(alone).TemplateIfMade), Is.EqualTo(1), "a pool of one grows once");
            Assert.That(m_Warnings.FindAll(w => w.Contains("grew to")).Count, Is.EqualTo(UnityEngine.Debug.isDebugBuild ? 1 : 0), "with a warning once");
        }

        // ---- a scene that unloads: a runtime context's voice sounds in the active scene, so unloading that scene cuts it and
        // the service lets it go; the next sound makes its voice again — the pool counts it as growing
        [UnityTest]
        public IEnumerator APP12_AVoiceInAnUnloadedScene()
        {
            yield return null;
            PositiveControl();
            var rt = new FeedbackScheduler(ContextKind.Runtime);
            m_S.Add(rt);
            AudioClip clip = Clip(3f);
            Transform at = Anchor(Vector3.zero);
            FeedbackPlayer p = Player(rt, Point(clip, at, i => { i.m_PoolSize = 1; i.m_SpatialBlend = 0f; }));
            AudioVoiceService voices = Voices(rt);
            GameObject template = voices.TemplateIfMade;
            PoolService pools = rt.Context.PoolsIfMade;
            Scene home = SceneManager.GetActiveScene();
            Scene other = SceneManager.CreateScene("Unloaded");
            SceneManager.SetActiveScene(other);
            p.Play();
            rt.Tick(Step);
            Assert.That(voices.LiveCount, Is.EqualTo(1), "a voice");
            GameObject v = voices.LiveAt(0);
            Assert.That((v.scene == other, Source(v).isPlaying), Is.EqualTo((true, true)), "it sounds in the active scene");
            SceneManager.SetActiveScene(home);
            AsyncOperation op = SceneManager.UnloadSceneAsync(other);
            while (!op.isDone)
                yield return null;
            rt.Tick(Step);
            Assert.That((v == null, voices.LiveCount), Is.EqualTo((true, 0)), "the unload cut it — let go");
            int grown = pools.GrownOf(template);
            p.Play();
            rt.Tick(Step);
            Assert.That(voices.LiveCount, Is.EqualTo(1), "the next sound");
            Assert.That(Source(voices.LiveAt(0)).isPlaying, Is.True, "plays");
            Assert.That(pools.GrownOf(template), Is.EqualTo(grown + 1), "its voice made again — counted as growing");
            Assert.That(m_Warnings.FindAll(w => w.Contains("grew to")).Count, Is.EqualTo(UnityEngine.Debug.isDebugBuild ? 1 : 0), "with a warning once");
        }

        // ---- a play that ends while paused gives its paused voice back — by Skip, by a Seek to its end
        [UnityTest]
        public IEnumerator APP13_APlayEndingPaused()
        {
            yield return null;
            PositiveControl();
            FeedbackScheduler s = Sched();
            Transform at = Anchor(Vector3.zero);
            AudioVoiceService voices = Voices(s);
            AudioClip clip = Clip(4f);
            foreach (bool skip in new[] { true, false })
            {
                FeedbackPlayer p = Player(s, Point(clip, at), Keeper(at));
                p.Play();
                s.Tick(Step);
                p.Pause();
                if (skip)
                    p.Skip();
                else
                    p.Seek(2f);
                yield return null;
                Tick(s, 2);
                Assert.That((Counts(voices), p.IsPlaying), Is.EqualTo(((0, 0, 0), false)), skip ? "Skip" : "a Seek to the end");
                p.Resume();
                Tick(s, 1);
                Assert.That(voices.LiveCount, Is.Zero, "the Resume has nothing to go on");
            }
        }

        // ---- a voice started after a Pause in the same pass pauses with the play and goes on with its Resume — a tick, a
        // backward pass
        [UnityTest]
        public IEnumerator APP14_APauseInsideThePlaysEvents()
        {
            yield return null;
            PositiveControl();
            FeedbackScheduler s = Sched();
            Transform at = Anchor(Vector3.zero);
            AudioVoiceService voices = Voices(s);
            AudioClip clip = Clip(4f);
            FeedbackPlayer p = null;
            p = Player(s, Call(() => p.Pause(), 0.1f), Point(clip, at), Keeper(at));
            p.Play();
            s.Tick(0f);
            s.Tick(0.2f);
            yield return null;
            Assert.That((Counts(voices), p.HasPaused), Is.EqualTo(((1, 1, 0), true)), "a tick");
            p.Resume();
            yield return null;
            Assert.That(Counts(voices), Is.EqualTo((1, 0, 1)), "a tick — goes on");
            p.Stop(StopMode.Revert);
            s.Tick(0f);
            FeedbackPlayer q = null;
            AudioPlayAtPointItem only = Point(clip, at, i =>
            {
                i.m_Timing.m_Delay = 0.1f;
                i.m_Conditions.m_Direction = DirectionCondition.ReverseOnly;
            });
            UnityEventItem pause = Call(() => q.Pause(), 0.1f);
            pause.m_Conditions.m_Direction = DirectionCondition.ReverseOnly;
            q = Player(s, only, pause, Keeper(at, 0.5f));
            q.Play(Back);
            s.Tick(0.65f);
            yield return null;
            Assert.That((Counts(voices), q.HasPaused), Is.EqualTo(((1, 1, 0), true)), "a backward pass");
            q.Resume();
            yield return null;
            Assert.That(Counts(voices), Is.EqualTo((1, 0, 1)), "a backward pass — goes on");
        }

        // ---- a play that ends in the pass of its Pause gives the voice back whichever came first
        [UnityTest]
        public IEnumerator APP15_APauseInThePassThatEnds()
        {
            yield return null;
            PositiveControl();
            FeedbackScheduler s = Sched();
            Transform at = Anchor(Vector3.zero);
            AudioVoiceService voices = Voices(s);
            AudioClip clip = Clip(4f);
            FeedbackPlayer before = null;
            before = Player(s, Point(clip, at), Call(() => before.Pause(), 0.1f));
            before.Play();
            s.Tick(0f);
            s.Tick(0.2f);
            yield return null;
            Tick(s, 2);
            Assert.That((Counts(voices), before.IsPlaying), Is.EqualTo(((0, 0, 0), false)), "the voice before the Pause");
            FeedbackPlayer after = null;
            after = Player(s, Call(() => after.Pause(), 0.1f), Point(clip, at));
            after.Play();
            s.Tick(0f);
            s.Tick(0.2f);
            yield return null;
            Tick(s, 2);
            Assert.That((Counts(voices), after.IsPlaying), Is.EqualTo(((0, 0, 0), false)), "the voice after the Pause");
        }

        // ---- an ended play's voice is not reached by a later backward play on its record — its Pause and its Skip
        [UnityTest]
        public IEnumerator APP16_AnEndedPlaysVoice()
        {
            yield return null;
            PositiveControl();
            FeedbackScheduler s = Sched();
            Transform at = Anchor(Vector3.zero);
            AudioVoiceService voices = Voices(s);
            AudioClip clip = Clip(4f);
            FeedbackPlayer p = Player(s, Keeper(at, 1f), Point(clip, at, i => i.m_Timing.m_Delay = 0.5f));
            p.m_Policy = PlayPolicy.Overlap;
            p.Rebuild();
            p.Play();
            Tick(s, 2);
            p.Play();
            Tick(s, 32);
            yield return null;
            Assert.That((Counts(voices), p.IsPlaying), Is.EqualTo(((2, 0, 2), false)), "two ended plays ring");
            p.Play(Back);
            Tick(s, 14);
            yield return null;
            Assert.That(Counts(voices), Is.EqualTo((1, 0, 1)), "the origin play's voice stopped");
            p.Pause();
            yield return null;
            Assert.That(Counts(voices), Is.EqualTo((1, 0, 1)), "the Pause does not reach the ended play's voice");
            p.Skip();
            yield return null;
            Tick(s, 2);
            Assert.That((Counts(voices), p.IsPlaying), Is.EqualTo(((1, 0, 1), false)), "nor does the Skip");
        }

        // ---- a turned play keeps its voices: one a backward play made after turning forward hears Pause and the next backward
        // play stops it · an Overlap play turned while a later play is the origin still hears Pause on its own voice
        [UnityTest]
        public IEnumerator APP17_TurnedPlaysKeepTheirVoices()
        {
            yield return null;
            PositiveControl();
            Transform at = Anchor(Vector3.zero);
            AudioClip clip = Clip(4f);
            FeedbackScheduler s = Sched();
            AudioVoiceService voices = Voices(s);
            FeedbackPlayer t = Player(s, Keeper(at, 1f), Point(clip, at, i => i.m_Timing.m_Delay = 0.5f));
            t.Play(Back);
            Tick(s, 12);
            Assert.That(Counts(voices), Is.EqualTo((0, 0, 0)), "backward past it — no voice");
            t.PlayTurning(PlayerDirection.Forward);
            Tick(s, 4);
            yield return null;
            Assert.That(Counts(voices), Is.EqualTo((1, 0, 1)), "turned forward — its voice");
            t.Pause();
            yield return null;
            Assert.That(Counts(voices), Is.EqualTo((1, 1, 0)), "the Pause reaches it");
            t.Resume();
            Tick(s, 12);
            yield return null;
            Assert.That((Counts(voices), t.IsPlaying), Is.EqualTo(((1, 0, 1), false)), "it rings past the end");
            t.Play(Back);
            Tick(s, 14);
            yield return null;
            Assert.That(Counts(voices), Is.EqualTo((0, 0, 0)), "the next backward play stops it");
            FeedbackScheduler o = Sched();
            AudioVoiceService own = Voices(o);
            FeedbackPlayer p = Player(o, Keeper(at, 1f), Point(clip, at, i => i.m_Timing.m_Delay = 0.5f));
            p.m_Policy = PlayPolicy.Overlap;
            p.Rebuild();
            p.Play();
            Tick(o, 2);
            PlaybackHandle later = p.Play();
            Tick(o, 12);
            yield return null;
            Assert.That(Counts(own), Is.EqualTo((2, 0, 2)), "two plays ring");
            p.Stop(later, StopMode.Hold);
            p.PlayTurning(PlayerDirection.Backward);
            Tick(o, 6);
            yield return null;
            Assert.That((Counts(own), p.IsPlaying), Is.EqualTo(((1, 0, 1), true)), "turned past it — the later play's voice stopped");
            p.Pause();
            yield return null;
            Assert.That(Counts(own), Is.EqualTo((1, 1, 0)), "the Pause reaches its own voice");
            p.Resume();
            yield return null;
            Assert.That(Counts(own), Is.EqualTo((1, 0, 1)), "and it goes on");
        }

        // ---- a paused play's end and a Pause inside a pass allocate nothing once warm
        [UnityTest]
        public IEnumerator APP18_ThePausedPathsAllocateNothing()
        {
            yield return null;
            PositiveControl();
            FeedbackScheduler s = Sched();
            Transform at = Anchor(Vector3.zero);
            AudioVoiceService voices = Voices(s);
            AudioClip clip = Clip(4f);
            FeedbackPlayer p = Player(s, Point(clip, at, i => i.m_PoolSize = 2), Keeper(at));
            TestDelegate skip = () =>
            {
                p.Play();
                s.Tick(Step);
                p.Pause();
                p.Skip();
                s.Tick(0f);
            };
            FeedbackPlayer q = null;
            q = Player(s, Call(() => q.Pause(), 0.05f), Point(clip, at, i => i.m_PoolSize = 2), Keeper(at));
            TestDelegate inside = () =>
            {
                q.Play();
                s.Tick(0.1f);
                q.Skip();
                s.Tick(0f);
            };
            for (int i = 0; i < 3; i++)
            {
                skip();
                inside();
            }
            Assert.That(Allocates(skip), Is.False, "a paused play's Skip");
            Assert.That(Allocates(inside), Is.False, "a Pause inside a pass");
            Assert.That(voices.LiveCount, Is.Zero, "the voices given back");
        }
    }
}
