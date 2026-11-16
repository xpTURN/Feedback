using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // the Audio mixer items in Play mode — Edit mode takes no write and runs no transition. The package's test mixer (its
    // build setup puts it under Resources): FeedbackTestVolume (snapshot A 0 dB · B −20 dB) and FeedbackTestPitch (1). A write
    // reads back at once; a clear and a transition read back at the next frame (a clear reads the value the last audio update
    // took until then). Test schedulers
    // ticked by hand; the mixer is put back to A and cleared after each test
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    public sealed class AudioMixerPlayTests
    {
        const float Step = 0.05f;
        const string Vol = "FeedbackTestVolume";
        const string Pit = "FeedbackTestPitch";
        static readonly PlayArgs Back = PlayArgs.Default.WithDirection(PlayerDirection.Backward);
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_S = new List<FeedbackScheduler>();
        AudioMixer m_Mixer;
        AudioMixerSnapshot m_A, m_B;

        [SetUp]
        public void SetUp()
        {
            m_Mixer = Resources.Load<AudioMixer>("FeedbackTestMixer");
            Assert.That(m_Mixer, Is.Not.Null, "the test mixer under Resources");
            m_A = m_Mixer.FindSnapshot("A");
            m_B = m_Mixer.FindSnapshot("B");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (FeedbackScheduler s in m_S)
                if (!s.IsShutdown) s.Shutdown();
            foreach (Object o in m_Made)
                if (o != null) Object.Destroy(o);
            m_Made.Clear();
            m_S.Clear();
            if (m_Mixer != null)
            {
                m_Mixer.ClearFloat(Vol);
                m_Mixer.ClearFloat(Pit);
                m_A.TransitionTo(0f);
            }
        }

        FeedbackScheduler Sched(ContextKind kind = ContextKind.Test)
        {
            var s = new FeedbackScheduler(kind);
            m_S.Add(s);
            return s;
        }

        static void Tick(FeedbackScheduler s, int n) { for (int i = 0; i < n; i++) s.Tick(Step); }

        float Get(string name)
        {
            m_Mixer.GetFloat(name, out float v);
            return v;
        }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            FeedbackPlayer p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            p.Bind(s);
            p.EnsureRuntime();
            return p;
        }

        AudioMixerParameterItem Parameter(string name, FeedbackMotion motion, float duration)
        {
            var item = new AudioMixerParameterItem { m_Mixer = m_Mixer, m_Parameter = name, m_Motion = motion };
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static AudioMixerSnapshotItem Snapshot(AudioMixerSnapshot to, AudioMixerSnapshot back, float time, float delay = 0f)
        {
            var item = new AudioMixerSnapshotItem { m_Snapshot = to, m_BackTo = back, m_TransitionTime = time, m_Motion = new SetMotion { m_Params = new SetParams { m_Value = new Vector4(1f, 0, 0, 0) } } };
            item.m_Timing.m_Delay = delay;
            return item;
        }

        // an offset held at its value — a flat curve at 1
        static CurveMotion Hold(float offset)
            => new CurveMotion { m_Params = new CurveParams { m_Curve = AnimationCurve.Constant(0f, 1f, 1f), m_RemapZero = Vector4.zero, m_RemapOne = Vector4.one * offset } };

        static TweenMotion TweenTo(float v)
        {
            var m = new TweenMotion();
            m.m_Params.m_Mode = TweenMode.To;
            m.m_Params.m_To = new Vector4(v, 0, 0, 0);
            return m;
        }

        // keeps the play running
        PositionItem Keeper(float d)
        {
            var go = new GameObject("Kept");
            m_Made.Add(go);
            var k = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = go.transform, m_Path = "", m_Anchor = "" }, m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(0.01f, 0, 0, 0), m_Vibrato = 1f, m_Decay = 0f } } };
            k.m_Timing.m_Duration = d;
            return k;
        }

        static IEnumerator Real(float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds)
                yield return null;
        }

        // the mixer at A, nothing held
        IEnumerator AtA()
        {
            m_Mixer.ClearFloat(Vol);
            m_Mixer.ClearFloat(Pit);
            m_A.TransitionTo(0f);
            yield return null;
            yield return null;
        }

        // the mixer takes a write here — else every value below would read as the snapshot's
        IEnumerator PositiveControl()
        {
            yield return AtA();
            Assert.That(m_Mixer.SetFloat(Vol, -3f), Is.True, "Play mode takes a write");
            Assert.That(Get(Vol), Is.EqualTo(-3f), "read back at once");
            m_Mixer.ClearFloat(Vol);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(0f), "cleared — the snapshot's");
        }

        // ---- the base and two players' offsets · the same over frames (the value read back is the one written) · the other
        // parameter apart · the category's scale
        [UnityTest]
        public IEnumerator AMP01_TheOffsetsAddOnTheBase()
        {
            yield return PositiveControl();
            FeedbackScheduler s = Sched();
            FeedbackPlayer p1 = Player(s, Parameter(Vol, Hold(-6f), 2f));
            FeedbackPlayer p2 = Player(s, Parameter(Vol, Hold(-4f), 2f));
            p1.Play(); p2.Play(); Tick(s, 3);
            Assert.That(Get(Vol), Is.EqualTo(-10f).Within(1e-4f), "the base and the two offsets");
            for (int i = 0; i < 3; i++)
            {
                yield return null;
                s.Tick(Step);
                Assert.That(Get(Vol), Is.EqualTo(-10f).Within(1e-4f), "frame " + i + " — the same, not taken as another's write");
            }
            Assert.That(Get(Pit), Is.EqualTo(1f), "the pitch apart");
            FeedbackPlayer pitch = Player(s, Parameter(Pit, Hold(0.5f), 2f));
            pitch.Play(); s.Tick(Step);
            Assert.That(Get(Pit), Is.EqualTo(1.5f).Within(1e-4f), "the pitch's own slot");
            Assert.That(Get(Vol), Is.EqualTo(-10f).Within(1e-4f), "the volume as it was");
            p1.Stop(StopMode.Revert); p2.Stop(StopMode.Revert); pitch.Stop(StopMode.Revert); s.Tick(0f);
            yield return null;
            Assert.That((Get(Vol), Get(Pit)), Is.EqualTo((0f, 1f)), "back to the snapshot");
            s.Context.Categories.SetScale(FeedbackCategory.Audio, 0.5f);
            p1.Play(); Tick(s, 2);
            Assert.That(Get(Vol), Is.EqualTo(-3f).Within(1e-4f), "the scale halves the offset");
            s.Context.Categories.SetScale(FeedbackCategory.Audio, 0f);
            s.Tick(Step);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(0f), "the scale 0 — the base");
            p1.Stop(StopMode.Revert); s.Tick(0f);
        }

        // ---- the frame: the revert's clear reads the old value within the frame and the apply's write wins · a frame whose
        // composition is the base leaves the parameter to the snapshot · after the last contribution the snapshot's again,
        // and a transition moves it
        [UnityTest]
        public IEnumerator AMP02_TheRevertClearsAndTheApplyWrites()
        {
            yield return PositiveControl();
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, Parameter(Vol, Hold(-6f), 4f));
            p.Play(); Tick(s, 2);
            Assert.That(Get(Vol), Is.EqualTo(-6f).Within(1e-4f), "composed");
            // a frame on: the last audio update took the composition — a clear reads that until the next one
            yield return null;
            s.RunRevert();
            Assert.That(Get(Vol), Is.EqualTo(-6f).Within(1e-4f), "the revert's clear — the old value within the frame");
            s.RunUpdate(new FrameTime(Step, Step));
            s.RunLateApply();
            Assert.That(Get(Vol), Is.EqualTo(-6f).Within(1e-4f), "the apply writes it");
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-6f).Within(1e-4f), "the next frame — the write won over the clear");
            // another player's offset makes the composition the base: nothing written, the snapshot has the parameter
            FeedbackPlayer q = Player(s, Parameter(Vol, Hold(6f), 4f));
            q.Play(); Tick(s, 2);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(0f), "the composition is the base — the snapshot's");
            m_B.TransitionTo(0f);
            yield return null;
            s.Tick(Step);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-20f).Within(1e-4f), "a transition moves it while nothing is written");
            q.Stop(StopMode.Revert); s.Tick(0f);
            Assert.That(Get(Vol), Is.EqualTo(-26f).Within(1e-4f), "the offset on the new base");
            // the end between the revert and the apply
            s.RunRevert();
            p.Stop(StopMode.Revert);
            s.RunUpdate(new FrameTime(Step, Step));
            s.RunLateApply();
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-20f).Within(1e-4f), "the end — the snapshot's");
            m_A.TransitionTo(0f);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(0f), "a transition after the end moves it");
        }

        // ---- a Tween keeps its value (a transition does not move it) until a Revert or Restore() lets it go · an offset on a
        // Tween's value ends on that value · a Tween begun under an offset lets go at the value before the offset
        [UnityTest]
        public IEnumerator AMP03_ATweenKeepsItsValueUntilItLetsGo()
        {
            yield return PositiveControl();
            FeedbackScheduler s = Sched();
            FeedbackPlayer t = Player(s, Parameter(Vol, TweenTo(-12f), 0.2f), Keeper(2f));
            t.Play(); Tick(s, 6);
            Assert.That(Get(Vol), Is.EqualTo(-12f).Within(1e-4f), "the Tween's value");
            m_B.TransitionTo(0f);
            yield return null;
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-12f).Within(1e-4f), "kept — a transition does not move it");
            m_A.TransitionTo(0f);
            yield return null;
            t.Stop(StopMode.Revert); s.Tick(0f);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(0f), "the Revert lets it go — the snapshot's");
            m_B.TransitionTo(0f);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-20f).Within(1e-4f), "a transition moves it again");
            yield return AtA();
            FeedbackPlayer r = Player(s, Parameter(Vol, TweenTo(-12f), 0.2f));
            r.Play(); Tick(s, 6);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-12f).Within(1e-4f), "a Tween's value after its end");
            r.Restore();
            yield return null;
            m_B.TransitionTo(0f);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-20f).Within(1e-4f), "Restore() lets it go");
            yield return AtA();
            // an offset on a Tween's value
            FeedbackPlayer kept = Player(s, Parameter(Vol, TweenTo(-10f), 0.2f), Keeper(2f));
            kept.Play(); Tick(s, 6);
            FeedbackPlayer on = Player(s, Parameter(Vol, Hold(-5f), 2f));
            on.Play(); Tick(s, 2);
            Assert.That(Get(Vol), Is.EqualTo(-15f).Within(1e-4f), "the offset on the Tween's value");
            on.Stop(StopMode.Revert); s.Tick(0f);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-10f).Within(1e-4f), "the offset's end — the Tween's value kept");
            kept.Stop(StopMode.Revert); s.Tick(0f);
            yield return AtA();
            // a Tween begun under an offset: its way back is the value before the offset
            FeedbackPlayer under = Player(s, Parameter(Vol, Hold(-5f), 4f));
            under.Play(); Tick(s, 2);
            FeedbackPlayer late = Player(s, Parameter(Vol, TweenTo(-12f), 0.2f), Keeper(2f));
            late.Play(); Tick(s, 6);
            under.Stop(StopMode.Revert); s.Tick(0f);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-12f).Within(1e-4f), "the Tween's value under the offset's end");
            late.Stop(StopMode.Revert); s.Tick(0f);
            yield return null;
            m_B.TransitionTo(0f);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-20f).Within(1e-4f), "let go at the value before the offset");
        }

        // ---- a snapshot: the transition at the next frame (time 0) · Back To on a Revert, Restore() and a backward play · no
        // Back To — the mix stays · the scale 0 — no transition, and its Revert goes nowhere · a sweep transitions · a time
        // goes on over frames, Back To's too
        [UnityTest]
        public IEnumerator AMP04_TheSnapshot()
        {
            yield return AtA();
            m_B.TransitionTo(0f);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-20f).Within(1e-4f), "the engine transitions here");
            yield return AtA();
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, Snapshot(m_B, m_A, 0f), Keeper(2f));
            p.Play(); s.Tick(0f);
            Assert.That(Get(Vol), Is.EqualTo(0f), "the same frame — not yet");
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-20f).Within(1e-4f), "the next frame — B");
            p.Stop(StopMode.Revert); s.Tick(0f);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(0f), "the Revert — Back To");
            FeedbackPlayer done = Player(s, Snapshot(m_B, m_A, 0f));
            done.Play(); s.Tick(Step);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-20f).Within(1e-4f), "B");
            done.Restore();
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(0f), "Restore() — Back To");
            FeedbackPlayer none = Player(s, Snapshot(m_B, null, 0f), Keeper(2f));
            none.Play(); s.Tick(0f);
            none.Stop(StopMode.Revert); s.Tick(0f);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-20f).Within(1e-4f), "no Back To — the mix stays");
            yield return AtA();
            // backward — once it has transitioned (the forward play has ended — its end leaves the snapshot)
            FeedbackPlayer back = Player(s, Snapshot(m_B, m_A, 0f, 0.1f), Keeper(0.5f));
            back.Play(); Tick(s, 12);
            yield return null;
            Assert.That((back.IsPlaying, Get(Vol)), Is.EqualTo((false, -20f)), "forward, ended — B");
            back.Play(in Back); Tick(s, 12);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(0f), "backward — Back To");
            // the scale 0: no transition · its Revert leaves a snapshot it did not choose
            s.Context.Categories.SetScale(FeedbackCategory.Audio, 0f);
            FeedbackPlayer off = Player(s, Snapshot(m_B, m_A, 0f), Keeper(2f));
            off.Play(); s.Tick(0f);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(0f), "the scale 0 — no transition");
            m_B.TransitionTo(0f);
            yield return null;
            off.Stop(StopMode.Revert); s.Tick(0f);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-20f).Within(1e-4f), "its Revert goes nowhere");
            s.Context.Categories.SetScale(FeedbackCategory.Audio, 1f);
            yield return AtA();
            // a sweep transitions
            FeedbackPlayer swept = Player(s, Snapshot(m_B, m_A, 0f, 0.5f), Keeper(1f));
            swept.Play(); s.Tick(0f);
            swept.Seek(0.8f); s.Tick(0f);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-20f).Within(1e-4f), "a sweep transitions");
            swept.Stop(StopMode.Revert); s.Tick(0f);
            yield return AtA();
            // a time goes on over frames — Back To's too
            FeedbackPlayer slow = Player(s, Snapshot(m_B, m_A, 0.5f), Keeper(2f));
            slow.Play(); s.Tick(0f);
            yield return Real(0.1f);
            float mid = Get(Vol);
            Assert.That(mid < 0f && mid > -20f, Is.True, "on its way — " + mid);
            float t0 = Time.realtimeSinceStartup;
            while (Get(Vol) > -19.99f && Time.realtimeSinceStartup - t0 < 2f)
                yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-20f).Within(0.01f), "B at the end of its time");
            slow.Stop(StopMode.Revert); s.Tick(0f);
            yield return Real(0.1f);
            float back0 = Get(Vol);
            Assert.That(back0 > -20f && back0 < 0f, Is.True, "Back To on its way — " + back0);
        }

        // ---- a preview: the parameter neither written nor cleared · the snapshot not transitioned
        [UnityTest]
        public IEnumerator AMP05_ThePreviewLeavesTheMixer()
        {
            yield return PositiveControl();
            FeedbackScheduler preview = Sched(ContextKind.Preview);
            m_Mixer.SetFloat(Vol, -7f);
            FeedbackPlayer p = Player(preview, Parameter(Vol, Hold(-6f), 1f));
            p.Play(); Tick(preview, 3);
            Assert.That(Get(Vol), Is.EqualTo(-7f), "no write");
            p.Stop(StopMode.Revert); preview.Tick(0f);
            yield return null;
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-7f), "no clear");
            m_Mixer.ClearFloat(Vol);
            yield return null;
            FeedbackPlayer q = Player(preview, Snapshot(m_B, m_A, 0f));
            q.Play(); preview.Tick(Step);
            yield return null;
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(0f), "no transition");
        }

        // ---- the game's SetFloat on the parameter: the item moves on from it while it plays, and its end lets it go with the
        // item's — the snapshot has the parameter again
        [UnityTest]
        public IEnumerator AMP06_TheGamesSetFloatIsLetGo()
        {
            yield return PositiveControl();
            m_Mixer.SetFloat(Vol, -7f);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-7f), "the game's value");
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, Parameter(Vol, Hold(-6f), 0.2f));
            p.Play(); Tick(s, 2);
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(-13f).Within(1e-4f), "on the game's value");
            Tick(s, 6);
            Assert.That(p.IsPlaying, Is.False, "ended");
            yield return null;
            yield return null;
            Assert.That(Get(Vol), Is.EqualTo(0f), "the end lets the game's value go — the snapshot's");
        }
    }
}
