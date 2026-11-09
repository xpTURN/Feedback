using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Cinemachine;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Perf = xpTURN.Feedback.Tests.PlayerPerformanceTests;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests
{
    // the real loop on a fresh test scheduler set as the default. The brain's camera is read after the brain's LateUpdate (a
    // recorder at order 32000); a stage is judged by the first frame a change shows (the brain copies the Cinemachine camera
    // every frame — its camera against the field proves nothing). The test assembly's stand-in brain hides the package's — full
    // names
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    public sealed class CinemachinePlayTests
    {
        static long s_Baseline = -1;
        static readonly Vector3 At = new Vector3(0f, 0f, -10f);
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        FeedbackScheduler m_Saved;
        FeedbackScheduler m_Test;

        [SetUp]
        public void SetUp()
        {
            CinemachineCore.UniformDeltaTimeOverride = -1f;
            CinemachineCore.CurrentTimeOverride = -1f;
            CinemachineCore.GetBlendOverride = null;
            CinemachineImpulseManager.Instance.Clear();
            CinemachineImpulseManager.Instance.IgnoreTimeScale = false;
            m_Saved = FeedbackScheduler.Default;
            m_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = m_Test;
            Time.captureDeltaTime = 1f / 60f;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Object o in m_Made)
            {
                if (o is GameObject g && g != null && g.TryGetComponent(out FeedbackPlayer p))
                    p.Stop();
            }
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Made.Clear();
            m_Schedulers.Clear();
            yield return null;
            FeedbackScheduler.Default = m_Saved;
            if (!m_Test.IsShutdown)
                m_Test.Shutdown();
            Time.captureDeltaTime = 0f;
            CinemachineImpulseManager.Instance.Clear();
            CinemachineImpulseManager.Instance.IgnoreTimeScale = false;
            CinemachineCore.GetBlendOverride = null;
        }

        Camera MainRig(out Unity.Cinemachine.CinemachineBrain brain)
        {
            var go = new GameObject("CI camera");
            m_Made.Add(go);
            go.tag = "MainCamera";
            Camera cam = go.AddComponent<Camera>();
            brain = go.AddComponent<Unity.Cinemachine.CinemachineBrain>();
            return cam;
        }

        CinemachineCamera Vcam(string name, Vector3 at, int priority = 10)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            go.transform.position = at;
            var v = go.AddComponent<CinemachineCamera>();
            v.Priority = new PrioritySettings { Value = priority };
            return v;
        }

        static CinemachineImpulseListener Listen(CinemachineCamera vcam)
        {
            var listener = vcam.gameObject.AddComponent<CinemachineImpulseListener>();
            listener.ChannelMask = 1;
            listener.Gain = 1f;
            return listener;
        }

        FeedbackPlayer Player(params FeedbackItem[] items)
        {
            var go = new GameObject("CI player");
            m_Made.Add(go);
            go.SetActive(false);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            go.SetActive(true);
            p.EnsureRuntime();
            return p;
        }

        CinemachineLateRecorder Recorder(Action<int> late)
        {
            var go = new GameObject("CI recorder");
            m_Made.Add(go);
            var r = go.AddComponent<CinemachineLateRecorder>();
            r.OnLate = late;
            return r;
        }

        static T With<T>(T item, FeedbackMotion motion, float duration, TargetRefMode mode = TargetRefMode.MainCamera, Object direct = null) where T : TargetItem
        {
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            item.m_Target = new TargetRef { m_Mode = mode, m_Object = direct, m_Path = "", m_Anchor = "" };
            return item;
        }

        static TweenMotion TweenTo(float to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = new Vector4(to, to, to, to);
            return new TweenMotion { m_Params = p };
        }

        static PunchMotion Punch(float amplitude) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(amplitude, amplitude, amplitude, amplitude), m_Vibrato = 5f, m_Decay = 1f } };

        static SpringMotion Bump(float value)
        {
            SpringParams p = SpringParams.Default;
            p.m_Value = Vector4.one * value;
            return new SpringMotion { m_Params = p };
        }

        static FlashMotion Flash(float target) => new FlashMotion { m_Params = new FlashParams { m_Target = new Vector4(target, target, target, target), m_Attack = 0.05f, m_Hold = 0.2f, m_Release = 0.05f } };

        static float Deviation(Camera cam) => (cam.transform.position - At).magnitude + Quaternion.Angle(cam.transform.rotation, Quaternion.identity);

        static int ActiveEvents()
            => typeof(CinemachineImpulseManager).GetField("m_ActiveEvents", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(CinemachineImpulseManager.Instance) is ICollection c ? c.Count : -1;

        static CinemachineImpulseManager.ImpulseEvent Foreign(float length)
        {
            CinemachineImpulseManager.ImpulseEvent e = CinemachineImpulseManager.Instance.NewImpulseEvent();
            e.Envelope = new CinemachineImpulseManager.EnvelopeDefinition { SustainTime = length };
            e.SignalSource = new CinemachineImpulseSignal(CinemachineImpulseCurves.Of(CinemachineImpulseShape.Bump, null)) { Velocity = Vector3.down, Duration = length };
            e.Radius = 9999999f;
            e.PropagationSpeed = 9999999f;
            e.Channel = 1;
            CinemachineImpulseManager.Instance.AddImpulseEvent(e);
            return e;
        }

        // ---- the lens through the main camera (the resolver's hook) — the brain's camera takes a lens written by a play begun this
        // frame one frame later (the timed Tween leaves its start at its second evaluation — the Update stage: the frame it is
        // written · LateApply would be one more) · a Tween's end stays · a punch shows (written before the brain) and comes back

        [UnityTest]
        public IEnumerator CI04a_TheLensShowsOnTheBrainsCamera()
        {
            Camera cam = MainRig(out _);
            CinemachineCamera vcam = Vcam("CI vcam", At);
            vcam.Lens.FieldOfView = 40f;
            yield return null;
            FeedbackPlayer jump = Player(With(new CinemachineLensFieldOfViewItem(), TweenTo(50f), 0.1f));
            int playFrame = -1, seen = -1;
            CinemachineLateRecorder first = Recorder(f =>
            {
                if (seen < 0 && playFrame >= 0 && Mathf.Abs(cam.fieldOfView - 40f) > 1e-3f)
                    seen = f - playFrame;
            });
            playFrame = Time.frameCount;
            jump.Play();
            for (int i = 0; i < 12; i++)
                yield return null;
            first.OnLate = null;
            vcam.Lens.FieldOfView = 40f;
            yield return null;
            FeedbackPlayer p = Player(With(new CinemachineLensFieldOfViewItem(), TweenTo(60f), 0.2f));
            bool bound = p.Runtime.Binding(0) != null;
            p.Play();
            for (int i = 0; i < 20; i++)
                yield return null;
            float end = vcam.Lens.FieldOfView, camEnd = cam.fieldOfView;
            FeedbackPlayer kick = Player(With(new CinemachineLensFieldOfViewItem(), Punch(10f), 0.3f));
            float kicked = 0f;
            CinemachineLateRecorder punch = Recorder(f => kicked = Mathf.Max(kicked, Mathf.Abs(cam.fieldOfView - 60f)));
            kick.Play();
            for (int i = 0; i < 25; i++)
                yield return null;
            punch.OnLate = null;
            float back = vcam.Lens.FieldOfView;
            Debug.Log($"CI04a lens · bound {bound} · seen {seen} · Tween end vcam {end} camera {camEnd} · punch {kicked} · back {back}");
            Assert.That(bound, Is.True, "the main camera target resolves to the live Cinemachine camera");
            Assert.That(seen, Is.EqualTo(1), "the brain's camera takes the lens in the frame it is written");
            Assert.That((end, camEnd), Is.EqualTo((60f, 60f)), "the Tween's end stays on the camera's lens and the brain's camera");
            Assert.That(kicked, Is.GreaterThan(1f), "a lens punch shows on the brain's camera");
            Assert.That(back, Is.EqualTo(60f), "the punch comes back to the base");
            Assert.That((CinemachineCore.UniformDeltaTimeOverride, CinemachineCore.CurrentTimeOverride), Is.EqualTo((-1f, -1f)), "the runtime writes no Cinemachine time");
        }

        // the orthographic size and the dutch the same way — a Spring moves the size, a punch the dutch, each back to its base
        [UnityTest]
        public IEnumerator CI04b_TheSizeAndTheDutch()
        {
            Camera cam = MainRig(out _);
            CinemachineCamera vcam = Vcam("CI vcam", At);
            vcam.Lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            vcam.Lens.OrthographicSize = 5f;
            yield return null;
            FeedbackPlayer size = Player(With(new CinemachineLensOrthographicSizeItem(), Bump(60f), 0.5f));
            FeedbackPlayer dutch = Player(With(new CinemachineLensDutchItem(), Punch(10f), 0.3f));
            float maxSize = 0f, maxDutch = 0f;
            Recorder(f =>
            {
                maxSize = Mathf.Max(maxSize, Mathf.Abs(cam.orthographicSize - 5f));
                maxDutch = Mathf.Max(maxDutch, Mathf.Abs(vcam.Lens.Dutch));
            });
            size.Play();
            dutch.Play();
            for (int i = 0; i < 60; i++)
                yield return null;
            Debug.Log($"CI04b size {maxSize} · dutch {maxDutch} · after size {vcam.Lens.OrthographicSize} dutch {vcam.Lens.Dutch}");
            Assert.That((maxSize > 0.1f, maxDutch > 1f), Is.EqualTo((true, true)), "the size on the brain's camera · the dutch moved");
            Assert.That((vcam.Lens.OrthographicSize, vcam.Lens.Dutch), Is.EqualTo((5f, 0f)), "back to the base");
        }

        // ---- the noise's gains — an amplitude flash moves the camera and comes back · a frequency flash comes back while the noise
        // runs on

        [UnityTest]
        public IEnumerator CI05a_TheNoiseGains()
        {
            Camera cam = MainRig(out _);
            CinemachineCamera vcam = Vcam("CI vcam", At);
            var perlin = vcam.gameObject.AddComponent<CinemachineBasicMultiChannelPerlin>();
            perlin.NoiseProfile = CinemachineFixtures.Profile();
            perlin.AmplitudeGain = 0f;
            perlin.FrequencyGain = 1f;
            yield return null;
            FeedbackPlayer amp = Player(With(new CinemachineNoiseAmplitudeGainItem(), Flash(2f), 0.3f));
            float max = 0f;
            CinemachineLateRecorder rec = Recorder(f => max = Mathf.Max(max, Deviation(cam)));
            amp.Play();
            for (int i = 0; i < 30; i++)
                yield return null;
            rec.OnLate = null;
            float ampAfter = perlin.AmplitudeGain;
            yield return null;
            float rest = Deviation(cam);
            perlin.AmplitudeGain = 1f;
            FeedbackPlayer freq = Player(With(new CinemachineNoiseFrequencyGainItem(), Flash(3f), 0.3f));
            float maxFreq = 0f, moved = 0f;
            Vector3 last = cam.transform.position;
            rec.OnLate = f =>
            {
                maxFreq = Mathf.Max(maxFreq, perlin.FrequencyGain);
                moved = Mathf.Max(moved, (cam.transform.position - last).magnitude);
                last = cam.transform.position;
            };
            freq.Play();
            for (int i = 0; i < 30; i++)
                yield return null;
            Debug.Log($"CI05a noise · amplitude flash deviation {max} · after {ampAfter} · rest {rest} · frequency max {maxFreq} after {perlin.FrequencyGain} · moved {moved}");
            Assert.That((max > 1e-3f, ampAfter, rest < 1e-4f), Is.EqualTo((true, 0f, true)), "the amplitude moved the camera and came back");
            Assert.That((maxFreq > 2f, perlin.FrequencyGain, moved > 1e-4f), Is.EqualTo((true, 1f, true)), "the frequency flash came back, the noise ran");
        }

        // ---- a main camera target follows the live camera while it rests · a play holds the camera it began on

        [UnityTest]
        public IEnumerator CI06a_TheMainCameraTargetFollowsTheLiveCamera()
        {
            MainRig(out _);
            CinemachineCamera a = Vcam("CI A", At, 10);
            CinemachineCamera b = Vcam("CI B", new Vector3(5f, 0f, -10f), 5);
            a.Lens.FieldOfView = 40f;
            b.Lens.FieldOfView = 40f;
            yield return null;
            FeedbackPlayer p = Player(With(new CinemachineLensFieldOfViewItem(), TweenTo(70f), 0.3f));
            p.Play();
            for (int i = 0; i < 5; i++)
                yield return null;
            b.Priority = new PrioritySettings { Enabled = true, Value = 20 };
            b.Prioritize();
            for (int i = 0; i < 25; i++)
                yield return null;
            float heldA = a.Lens.FieldOfView, heldB = b.Lens.FieldOfView;
            p.Play();
            for (int i = 0; i < 25; i++)
                yield return null;
            Debug.Log($"CI06a follow · during the play A {heldA} B {heldB} · the next play A {a.Lens.FieldOfView} B {b.Lens.FieldOfView}");
            Assert.That((heldA, heldB), Is.EqualTo((70f, 40f)), "the play held the camera it began on");
            Assert.That((a.Lens.FieldOfView, b.Lens.FieldOfView), Is.EqualTo((70f, 70f)), "the next play took the camera live then");
        }

        // ---- an impulse — the listener's camera moves for the item's length · the intensity scales it · a channel outside the
        // listener's mask moves nothing

        [UnityTest]
        public IEnumerator CI07a_TheImpulseMovesTheListenersCamera()
        {
            Camera cam = MainRig(out _);
            Listen(Vcam("CI vcam", At));
            yield return null;
            FeedbackPlayer p = Player(new CinemachineImpulseItem { m_Duration = 0.3f, m_Velocity = new Vector3(0f, -1f, 0f) });
            var minY = new float[2];
            int playing = 0;
            int round = 0;
            Recorder(f =>
            {
                minY[round] = Mathf.Min(minY[round], cam.transform.position.y);
                if (round == 0 && p.IsPlaying)
                    playing++;
            });
            p.Play();
            for (int i = 0; i < 120; i++)
                yield return null;
            round = 1;
            p.Play(PlayArgs.Default.WithIntensity(2f));
            for (int i = 0; i < 30; i++)
                yield return null;
            Debug.Log($"CI07a impulse · min y {minY[0]} · ×2 {minY[1]} · playing frames {playing}");
            Assert.That(minY[0], Is.LessThan(-0.05f), "the listener's camera moved");
            Assert.That(playing, Is.InRange(16, 20), "the play lives the item's length (0.3 s)");
            Assert.That(minY[1] / minY[0], Is.EqualTo(2f).Within(0.05f), "twice the intensity, twice the velocity");
        }

        [UnityTest]
        public IEnumerator CI07b_AChannelOutsideTheMaskMovesNothing()
        {
            Camera cam = MainRig(out _);
            Listen(Vcam("CI vcam", At));
            yield return null;
            FeedbackPlayer other = Player(new CinemachineImpulseItem { m_Duration = 0.3f, m_Channel = 2 });
            FeedbackPlayer ours = Player(new CinemachineImpulseItem { m_Duration = 0.3f, m_Channel = 3 });
            float moved = 0f;
            CinemachineLateRecorder rec = Recorder(f => moved = Mathf.Max(moved, Mathf.Abs(cam.transform.position.y)));
            other.Play();
            for (int i = 0; i < 15; i++)
                yield return null;
            float outside = moved;
            ours.Play();
            for (int i = 0; i < 15; i++)
                yield return null;
            Debug.Log($"CI07b channels · 2 moved {outside} · 3 moved {moved}");
            Assert.That(outside, Is.LessThan(1e-4f), "a channel outside the mask moves nothing");
            Assert.That(moved, Is.GreaterThan(0.05f), "a mask that shares a bit moves the camera");
        }

        // ---- a stop cancels our event only — a foreign live event stays · after the manager recycled ours (a paused play outlives
        // its event — the manager's clock runs on) the object is a foreign event's and the stop leaves it

        [UnityTest]
        public IEnumerator CI08a_AStopCancelsOursOnly()
        {
            MainRig(out _);
            Listen(Vcam("CI vcam", At));
            yield return null;
            CinemachineImpulseManager.ImpulseEvent fe = Foreign(1f);
            FeedbackPlayer q = Player(new CinemachineImpulseItem { m_Duration = 1f });
            var binding = (CinemachineImpulseBinding)q.Runtime.Binding(0);
            q.Play();
            yield return null;
            CinemachineImpulseManager.ImpulseEvent ours = binding.EventOf(0);
            float oursBefore = ours.Envelope.Duration, foreignBefore = fe.Envelope.Duration;
            q.Stop();
            yield return null;
            Debug.Log($"CI08a stop · ours {oursBefore} → {ours.Envelope.Duration} · foreign {foreignBefore} → {fe.Envelope.Duration}");
            Assert.That(ours.Envelope.Duration, Is.LessThan(oursBefore), "the stop cancels ours");
            Assert.That(fe.Envelope.Duration, Is.EqualTo(foreignBefore), "not the foreign event");
        }

        [UnityTest]
        public IEnumerator CI08b_AStopAfterTheRecycleLeavesTheForeignEvent()
        {
            MainRig(out _);
            Listen(Vcam("CI vcam", At));
            yield return null;
            FeedbackPlayer p = Player(new CinemachineImpulseItem { m_Duration = 0.2f, m_Velocity = new Vector3(0f, -0.01f, 0f) });
            var binding = (CinemachineImpulseBinding)p.Runtime.Binding(0);
            p.Play();
            yield return null;
            p.Pause();
            CinemachineImpulseManager.ImpulseEvent held = binding.EventOf(0);
            for (int i = 0; i < 100; i++)
                yield return null;
            CinemachineImpulseManager.ImpulseEvent fe = Foreign(1f);
            bool same = ReferenceEquals(fe, held);
            float before = fe.Envelope.Duration;
            p.Stop(StopMode.Revert);
            yield return null;
            Debug.Log($"CI08b recycle · the foreign event is our old object {same} · its length {before} → {fe.Envelope.Duration}");
            Assert.That(same, Is.True, "the pool handed our old event to the foreign one");
            Assert.That(fe.Envelope.Duration, Is.EqualTo(before), "the stop left the foreign event");
        }

        // ---- the manager's time-scale flag is the user's — a play writes neither value

        [UnityTest]
        public IEnumerator CI09a_TheManagersFlagIsTheUsers()
        {
            MainRig(out _);
            Listen(Vcam("CI vcam", At));
            yield return null;
            var kept = new List<bool>();
            foreach (bool flag in new[] { true, false })
            {
                CinemachineImpulseManager.Instance.IgnoreTimeScale = flag;
                FeedbackPlayer p = Player(new CinemachineImpulseItem { m_Duration = 0.2f });
                p.Play();
                yield return null;
                kept.Add(CinemachineImpulseManager.Instance.IgnoreTimeScale);
                p.Stop();
                CinemachineImpulseManager.Instance.Clear();
            }
            Assert.That(kept, Is.EqualTo(new[] { true, false }), "the play leaves the manager's flag");
        }

        // ---- Clear empties the manager — ours and the others'

        [UnityTest]
        public IEnumerator CI10a_ClearEmptiesTheManager()
        {
            MainRig(out _);
            Listen(Vcam("CI vcam", At));
            yield return null;
            Foreign(10f);
            FeedbackPlayer longOne = Player(new CinemachineImpulseItem { m_Duration = 1f });
            FeedbackPlayer clear = Player(new CinemachineImpulseClearItem());
            longOne.Play();
            for (int i = 0; i < 5; i++)
                yield return null;
            int before = ActiveEvents();
            clear.Play();
            yield return null;
            int after = ActiveEvents();
            Debug.Log($"CI10a clear · active {before} → {after}");
            Assert.That((before, after), Is.EqualTo((2, 0)), "Clear empties the manager");
        }

        // ---- the source mode — the user's impulse source fires · none is refused (the reason a development build's only)

        [UnityTest]
        public IEnumerator CI11a_TheSourceMode()
        {
            Camera cam = MainRig(out _);
            Listen(Vcam("CI vcam", At));
            var sgo = new GameObject("CI source");
            m_Made.Add(sgo);
            var source = sgo.AddComponent<CinemachineImpulseSource>();
            source.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            source.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            source.ImpulseDefinition.ImpulseChannel = 1;
            source.ImpulseDefinition.ImpulseDuration = 0.3f;
            source.DefaultVelocity = Vector3.down;
            yield return null;
            ItemBinding none = FeedbackImplementations.Create(new CinemachineImpulseItem { m_Mode = CinemachineImpulseMode.Source });
            var context = new PrepareContext(sgo, null, m_Test.Context, 1);
            bool prepared = none.Prepare(in context);
            string reason = none.SkipReason;
            none.Release();
            FeedbackPlayer p = Player(new CinemachineImpulseItem { m_Mode = CinemachineImpulseMode.Source, m_Source = source, m_Velocity = Vector3.down });
            float minY = 0f;
            Recorder(f => minY = Mathf.Min(minY, cam.transform.position.y));
            p.Play();
            for (int i = 0; i < 20; i++)
                yield return null;
            Debug.Log($"CI11a source · min y {minY} · none prepared {prepared} '{reason}'");
            Assert.That(minY, Is.LessThan(-0.05f), "the user's source fired");
            Assert.That(prepared, Is.False, "no source — refused");
#if DEBUG
            Assert.That(reason, Is.EqualTo("the source is not a Cinemachine Impulse Source"), "the reason — a development build's");
#else
            Assert.That(reason, Is.Null, "no reason in a release build");
#endif
        }

        // ---- an activation — live in the same frame (Prioritize) · over the queue's top (+1), enabled · again on the top keeps its
        // value · a set value (not the top's) wins a tie

        [UnityTest]
        public IEnumerator CI12a_TheActivation()
        {
            MainRig(out Unity.Cinemachine.CinemachineBrain brain);
            CinemachineCamera a = Vcam("CI A", At, 10);
            CinemachineCamera b = Vcam("CI B", new Vector3(5f, 0f, -10f), 3);
            CinemachineCamera c = Vcam("CI C", new Vector3(-5f, 0f, -10f), 3);
            b.Priority = new PrioritySettings { Enabled = false, Value = 3 };
            for (int i = 0; i < 3; i++)
                yield return null;
            FeedbackPlayer pb = Player(With(new CinemachineActivateCameraItem(), new SetMotion(), 0f, TargetRefMode.Direct, b));
            var live = new List<string>();
            CinemachineLateRecorder rec = Recorder(f => live.Add(brain.ActiveVirtualCamera?.Name ?? "none"));
            pb.Play();
            yield return null;
            rec.OnLate = null;
            PrioritySettings first = b.Priority;
            pb.Play();
            yield return null;
            int again = b.Priority.Value;
            var set = With(new CinemachineActivateCameraItem { m_ToTop = false, m_Priority = again }, new SetMotion(), 0f, TargetRefMode.Direct, c);
            Player(set).Play();
            yield return null;
            yield return null;
            Debug.Log($"CI12a activate · live {string.Join(",", live)} · B {first.Value} enabled {first.Enabled} · again {again} · C {c.Priority.Value} live {brain.ActiveVirtualCamera?.Name}");
            Assert.That(live[0], Is.EqualTo("CI B"), "live in the same frame");
            Assert.That((first.Enabled, first.Value), Is.EqualTo((true, 11)), "over the top, enabled");
            Assert.That(again, Is.EqualTo(11), "the top again keeps its value");
            Assert.That(brain.ActiveVirtualCamera?.Name, Is.EqualTo("CI C"), "a set value wins a tie");
        }

        // ---- the blend — the item's beats a custom blend · the user's delegate runs behind ours · used once · a cleared delegate
        // (3.1's editor at a scene load) comes back at the next activation

        [UnityTest]
        public IEnumerator CI13a_TheBlendOverride()
        {
            MainRig(out Unity.Cinemachine.CinemachineBrain brain);
            CinemachineCamera a = Vcam("CI A", At, 10);
            CinemachineCamera b = Vcam("CI B", new Vector3(5f, 0f, -10f), 3);
            var settings = ScriptableObject.CreateInstance<CinemachineBlenderSettings>();
            m_Made.Add(settings);
            settings.CustomBlends = new[] { new CinemachineBlenderSettings.CustomBlend { From = "CI A", To = "CI B", Blend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Linear, 3f) } };
            brain.CustomBlends = settings;
            for (int i = 0; i < 3; i++)
                yield return null;
            ICinemachineCamera ai = a;
            CinemachineCore.GetBlendOverride = (from, to, d, owner) => to == ai ? new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Linear, 1.5f) : d;
            var item = With(new CinemachineActivateCameraItem(), new SetMotion(), 0f, TargetRefMode.Direct, b);
            item.m_Blend = new CinemachineBlendSpec { m_Override = true, m_Style = CinemachineBlendStyle.Linear, m_Time = 0.5f };
            FeedbackPlayer p = Player(item);
            CinemachineFeedbackService svc = CinemachineFeedbackService.Of(m_Test.Context);
            p.Play();
            yield return null;
            yield return null;
            float duration = brain.ActiveBlend != null ? brain.ActiveBlend.Duration : -1f;
            bool pending = svc.Pending;
            for (int i = 0; i < 40; i++)
                yield return null;
            a.Priority = new PrioritySettings { Enabled = true, Value = 30 };
            a.Prioritize();
            yield return null;
            yield return null;
            float chained = brain.ActiveBlend != null ? brain.ActiveBlend.Duration : -1f;
            for (int i = 0; i < 100; i++)
                yield return null;
            CinemachineCore.GetBlendOverride = null;
            p.Play();
            yield return null;
            yield return null;
            float again = brain.ActiveBlend != null ? brain.ActiveBlend.Duration : -1f;
            Debug.Log($"CI13a blend · {duration} · pending after {pending} · the user's to A {chained} · after a cleared delegate {again}");
            Assert.That(duration, Is.EqualTo(0.5f), "the item's blend wins over the custom blend");
            Assert.That(pending, Is.False, "used once");
            Assert.That(chained, Is.EqualTo(1.5f), "the user's delegate runs behind ours");
            Assert.That(again, Is.EqualTo(0.5f), "the next activation installs again");
        }

        // ---- a camera shake on a camera Cinemachine drives — with a shake extension on the live camera the extension's slots (no
        // transform slot) and the camera moves in the frame the shake begins (the Update stage) along the camera's own axes ·
        // without the extension, or without a brain, the transform's slots

        [UnityTest]
        public IEnumerator CI14a_TheShakeThroughTheExtension()
        {
            Camera cam = MainRig(out _);
            CinemachineCamera vcam = Vcam("CI vcam", At);
            vcam.gameObject.AddComponent<CinemachineShakeExtension>();
            yield return null;
            var shake = new CameraShakeItem { m_Motion = new ShakeMotion() };
            shake.m_Timing.m_Duration = 0.3f;
            shake.m_Target = new TargetRef { m_Mode = TargetRefMode.MainCamera, m_Path = "", m_Anchor = "" };
            FeedbackPlayer p = Player(shake);
            var binding = (CameraShakeBinding)p.Runtime.Binding(0);
            float max = 0f;
            Recorder(f => max = Mathf.Max(max, (cam.transform.position - At).magnitude));
            p.Play();
            for (int i = 0; i < 20; i++)
                yield return null;
            string withExt = binding.AnyPositionSlot?.GetType().Name;
            Object.Destroy(vcam.GetComponent<CinemachineShakeExtension>());
            yield return null;
            var plain = (CameraShakeBinding)Player(shake).Runtime.Binding(0);
            string without = plain.AnyPositionSlot?.GetType().Name;
            Object.Destroy(cam.GetComponent<Unity.Cinemachine.CinemachineBrain>());
            vcam.gameObject.AddComponent<CinemachineShakeExtension>();
            yield return null;
            var brainless = (CameraShakeBinding)Player(shake).Runtime.Binding(0);
            string noBrain = brainless.AnyPositionSlot?.GetType().Name;
            Debug.Log($"CI14a shake · with an extension {withExt} · moved {max} · transform slot {binding.PositionSlot != null} | without {without} | no brain {noBrain}");
            Assert.That((withExt, binding.PositionSlot == null, max > 0.01f), Is.EqualTo(("CinemachineShakePositionSlot", true, true)), "the extension's path: seen, no transform slot");
            Assert.That((without, noBrain), Is.EqualTo(("LocalPositionSlot", "LocalPositionSlot")), "without an extension · a brain the transform's slot");
        }

        [UnityTest]
        public IEnumerator CI15a_TheExtensionsStageAndAxes()
        {
            Camera cam = MainRig(out _);
            CinemachineCamera vcam = Vcam("CI vcam", At);
            vcam.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            vcam.gameObject.AddComponent<CinemachineShakeExtension>();
            yield return null;
            Vector3 rest = cam.transform.position;
            var jolt = new CameraShakeItem { m_Motion = new CurveMotion { m_Params = new CurveParams { m_Curve = AnimationCurve.Constant(0f, 1f, 1f), m_RemapZero = Vector4.zero, m_RemapOne = new Vector4(1f, 0f, 0f, 0f) } } };
            jolt.m_Timing.m_Duration = 0.1f;
            jolt.m_Target = new TargetRef { m_Mode = TargetRefMode.MainCamera, m_Path = "", m_Anchor = "" };
            FeedbackPlayer j = Player(jolt);
            int playFrame = -1, seen = -1;
            Vector3 offset = Vector3.zero;
            CinemachineLateRecorder first = Recorder(f =>
            {
                if (seen < 0 && playFrame >= 0 && (cam.transform.position - rest).magnitude > 0.5f)
                {
                    seen = f - playFrame;
                    offset = cam.transform.position - rest;
                }
            });
            playFrame = Time.frameCount;
            j.Play();
            for (int i = 0; i < 10; i++)
                yield return null;
            first.OnLate = null;
            Debug.Log($"CI15a extension · seen {seen} · offset {offset} · right {vcam.transform.right}");
            Assert.That(seen, Is.EqualTo(0), "the camera moves in the frame the shake begins");
            Assert.That(Vector3.Distance(offset.normalized, vcam.transform.right), Is.LessThan(1e-3f), "along the camera's own x");
        }

        // ---- a fresh fixture's first cycles — the impulse (events taken ahead · the manager's pool drained first) and the clear,
        // five fresh fixtures after a warm one, the cycle synchronous (NP04's way). Red when all five allocate

        static FeedbackScheduler s_S;
        static FeedbackPlayer s_P;

        static readonly TestDelegate s_Cycle = () =>
        {
            s_P.Play();
            for (int i = 0; i < 6; i++)
                s_S.Tick(0.05f);
            s_P.Stop(StopMode.Revert);
            s_P.Play();
            for (int i = 0; i < 12; i++)
                s_S.Tick(0.05f);
            s_P.Stop(StopMode.Complete);
        };

        void Fresh(bool clear)
        {
            s_S = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s_S);
            var go = new GameObject("CI fresh");
            m_Made.Add(go);
            s_P = go.AddComponent<FeedbackPlayer>();
            s_P.m_Items.Add(clear ? new CinemachineImpulseClearItem() : (FeedbackItem)new CinemachineImpulseItem { m_Duration = 0.3f, m_Velocity = new Vector3(0f, -0.01f, 0f) });
            s_P.Bind(s_S);
            s_P.EnsureRuntime();
        }

        [UnityTest]
        public IEnumerator CI24a_TheActionsFirstCycles()
        {
            MainRig(out _);
            Listen(Vcam("CI vcam", At));
            yield return null;
            var pool = typeof(CinemachineImpulseManager).GetField("m_ExpiredEvents", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(CinemachineImpulseManager.Instance) as ICollection;
            for (int i = pool != null ? pool.Count : 0; i > 0; i--)
                CinemachineImpulseManager.Instance.NewImpulseEvent();
            var line = new List<string>();
            foreach (bool clear in new[] { false, true })
            {
                Fresh(clear);
                s_Cycle();
                int hits = 0, prepared = 0;
                for (int k = 0; k < 5; k++)
                {
                    Fresh(clear);
                    if (s_P.Runtime != null && s_P.Runtime.Binding(0) != null)
                        prepared++;
                    if (UIs.AllocatingGCMemory().ApplyTo(s_Cycle).IsSuccess)
                        hits++;
                }
                line.Add($"{(clear ? "clear" : "impulse")} allocated {hits}/5 · prepared {prepared}/5");
                Assert.That((hits < 5, prepared), Is.EqualTo((true, 5)), line[line.Count - 1]);
            }
            int positive = 0;
            for (int k = 0; k < 5; k++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo((TestDelegate)(() => { var junk = new byte[64]; junk[0] = 1; })).IsSuccess)
                    positive++;
            }
            Debug.Log($"CI24a first cycles · {string.Join(" | ", line)} · positive {positive}/5");
            Assert.That(positive, Is.EqualTo(5), "the positive");
        }

        // the real loop from rest — lens Tween · punch, a noise flash, a shake through the extension, an impulse: against the run's
        // median idle frame
        [UnityTest]
        public IEnumerator CI24b_TheRealLoopFromRest()
        {
            LogAssert.ignoreFailingMessages = true;
            Camera cam = MainRig(out _);
            CinemachineCamera vcam = Vcam("CI vcam", At);
            Listen(vcam);
            vcam.gameObject.AddComponent<CinemachineShakeExtension>();
            var perlin = vcam.gameObject.AddComponent<CinemachineBasicMultiChannelPerlin>();
            perlin.NoiseProfile = CinemachineFixtures.Profile();
            perlin.AmplitudeGain = 0f;
            FeedbackPlayer lens = Player(With(new CinemachineLensFieldOfViewItem(), TweenTo(70f), 0.4f), With(new CinemachineLensDutchItem(), Punch(5f), 0.3f));
            FeedbackPlayer noise = Player(With(new CinemachineNoiseAmplitudeGainItem(), Flash(1f), 0.3f));
            var shakeItem = new CameraShakeItem { m_Motion = new ShakeMotion() };
            shakeItem.m_Timing.m_Duration = 0.3f;
            shakeItem.m_Target = new TargetRef { m_Mode = TargetRefMode.MainCamera, m_Path = "", m_Anchor = "" };
            FeedbackPlayer shake = Player(shakeItem);
            FeedbackPlayer impulse = Player(new CinemachineImpulseItem { m_Duration = 0.2f, m_Velocity = new Vector3(0f, -0.05f, 0f) });
            using ProfilerRecorder rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            yield return null;
            foreach (FeedbackPlayer p in new[] { lens, noise, shake, impulse })
                p.Play();
            for (int f = 0; f < 90; f++)
                yield return null;
            var idle = new List<long>();
            for (int f = 0; f < 31; f++)
            {
                yield return null;
                idle.Add(rec.LastValue);
            }
            idle.Sort();
            long baseline = idle[15];
            int idleAllocating = idle.FindAll(v => v - baseline >= 64).Count;
            int allocating = 0;
            float moved = 0f;
            for (int f = 0; f < 120; f++)
            {
                if (f % 30 == 0)
                {
                    lens.Play();
                    noise.Play();
                    shake.Play();
                    impulse.Play();
                }
                yield return null;
                moved = Mathf.Max(moved, (cam.transform.position - At).magnitude);
                if (rec.LastValue - baseline >= 64)
                    allocating++;
            }
            int positive = 0;
            for (int f = 0; f < 5; f++)
            {
                _ = new byte[8192];
                yield return null;
                if (rec.LastValue - baseline >= 4000)
                    positive++;
            }
            Debug.Log($"CI24b cinemachine real loop · idle allocating {idleAllocating}/31 · allocating {allocating}/120 · moved {moved}");
            Assert.That(moved, Is.GreaterThan(0.01f), "the camera moved in the window");
            Assert.That(positive, Is.GreaterThanOrEqualTo(4), "the recorder sees an allocation — the positive");
#if UNITY_6000_3_OR_NEWER || !UNITY_EDITOR
            Assert.That(allocating, Is.LessThanOrEqualTo(idleAllocating + 3), "the plays from rest allocate nothing");
#else
            Assert.Ignore("6000.0's editor loop allocates in FlushDirty (EditorCompilationInterface.IsCompiling) in each frame the scene changed — the IL2CPP player judges the allocation");
#endif
        }

        // ---- the per-play ns within each row's budget in an IL2CPP player (the brain's work is not ours) · the impulse's window
        // against the baseline

        static IEnumerator Baseline()
        {
            if (s_Baseline >= 0)
                yield break;
            yield return Perf.Measure("(x) no play", 1, 0);
            long first = Perf.s_Last.Alloc;
            yield return Perf.Measure("(x) no play again", 1, 0);
            s_Baseline = Math.Min(first, Perf.s_Last.Alloc);
        }

        [UnityTest]
        public IEnumerator CI25a_TheLens([Values(100, 1000)] int n)
        {
            yield return null;
            yield return Baseline();
            MainRig(out _);
            CinemachineCamera vcam = Vcam("CI vcam", At);
            var players = new List<FeedbackPlayer>(n);
            for (int i = 0; i < n; i++)
                players.Add(Player(With(new CinemachineLensFieldOfViewItem(), Punch(1f), 1000f, TargetRefMode.Direct, vcam)));
            int prepared = players.Count(p => p.Runtime != null && p.Runtime.Binding(0) != null);
            int accepted = players.Count(p => p.Play() != default);
            yield return Perf.Settled("(x) cinemachine lens " + n, n, 0, s_Baseline, 700);
            Perf.Window w = Perf.s_Last;
            Assert.That(prepared + " · " + accepted, Is.EqualTo(n + " · " + n), "prepared · accepted");
            Assert.That(w.Frames + " · " + w.Counted, Is.EqualTo(Perf.Frames + " · " + Perf.Frames), "the recorders live — frames · markers");
            Assert.That(w.PerPlay, Is.GreaterThan(0), "per play ns (written down)");
            Perf.RowBudget("(x) cinemachine lens " + n, 700);
            if (Perf.k_Judge)
                Assert.That(Math.Abs(w.Alloc - s_Baseline), Is.LessThanOrEqualTo(Perf.Equal), "the window " + w.Alloc + " vs " + s_Baseline + " B");
        }

        [UnityTest]
        public IEnumerator CI25b_TheImpulse([Values(100, 1000)] int n)
        {
            yield return null;
            yield return Baseline();
            MainRig(out _);
            Listen(Vcam("CI vcam", At));
            var players = new List<FeedbackPlayer>(n);
            for (int i = 0; i < n; i++)
                players.Add(Player(new CinemachineImpulseItem { m_Duration = 1000f, m_Velocity = new Vector3(0f, -0.0001f, 0f) }));
            int prepared = players.Count(p => p.Runtime != null && p.Runtime.Binding(0) != null);
            int accepted = players.Count(p => p.Play() != default);
            yield return Perf.Settled("(x) impulse " + n, n, 0, s_Baseline, 300);
            Perf.Window w = Perf.s_Last;
            Assert.That(prepared + " · " + accepted, Is.EqualTo(n + " · " + n), "prepared · accepted");
            Assert.That(ActiveEvents(), Is.EqualTo(n), "every play's event live");
            Assert.That(w.Frames + " · " + w.Counted, Is.EqualTo(Perf.Frames + " · " + Perf.Frames), "the recorders live — frames · markers");
            Assert.That(w.PerPlay, Is.GreaterThan(0), "per play ns (written down)");
            Perf.RowBudget("(x) impulse " + n, 300);
            if (Perf.k_Judge)
                Assert.That(Math.Abs(w.Alloc - s_Baseline), Is.LessThanOrEqualTo(Perf.Equal), "the window " + w.Alloc + " vs " + s_Baseline + " B");
        }

        // ---- the context's close takes the blend override back and cancels ours · a user's delegate set after ours stays · the
        // runtime wrote no Cinemachine time

        [UnityTest]
        public IEnumerator CI26a_TheCloseLeavesCinemachineAsItWas()
        {
            MainRig(out _);
            CinemachineCamera b = Vcam("CI B", At, 3);
            Listen(b);
            yield return null;
            var item = With(new CinemachineActivateCameraItem(), new SetMotion(), 0f, TargetRefMode.Direct, b);
            item.m_Blend = new CinemachineBlendSpec { m_Override = true, m_Style = CinemachineBlendStyle.Linear, m_Time = 0.5f };
            FeedbackPlayer p = Player(item, new CinemachineImpulseItem { m_Duration = 100f });
            p.Play();
            yield return null;
            CinemachineFeedbackService svc = CinemachineFeedbackService.Of(m_Test.Context);
            bool installed = svc.Installed;
            int live = ActiveEvents();
            m_Test.Shutdown();
            yield return null;
            int after = ActiveEvents();
            CinemachineCore.GetBlendOverrideDelegate left = CinemachineCore.GetBlendOverride;
            Debug.Log($"CI26a close · installed {installed} · live {live} → {after} · override {(left == null ? "null" : "set")}");
            Assert.That((installed, live), Is.EqualTo((true, 1)), "in play — ours installed · our impulse live");
            Assert.That(left, Is.Null, "the close put the one before back");
            Assert.That((CinemachineCore.UniformDeltaTimeOverride, CinemachineCore.CurrentTimeOverride), Is.EqualTo((-1f, -1f)), "no Cinemachine time written");
        }

        // ---- Save During Play's hot save — the hook on Cinemachine's delegate once · it puts the lens and the gain back to their
        // first values after a Tween's end became the base. The editor's only — a player skips

        [UnityTest]
        public IEnumerator CI29b_TheHotSavePutsTheFirstValuesBack()
        {
            if (!Application.isEditor)
                Assert.Ignore("Save During Play is the editor's — no player");
            MainRig(out _);
            CinemachineCamera vcam = Vcam("CI vcam", At);
            vcam.Lens.FieldOfView = 40f;
            var perlin = vcam.gameObject.AddComponent<CinemachineBasicMultiChannelPerlin>();
            perlin.AmplitudeGain = 0.5f;
            yield return null;
            FeedbackPlayer p = Player(With(new CinemachineLensFieldOfViewItem(), TweenTo(60f), 0.1f, TargetRefMode.Direct, vcam),
                With(new CinemachineNoiseAmplitudeGainItem(), TweenTo(2f), 0.1f, TargetRefMode.Direct, perlin));
            p.Play();
            for (int i = 0; i < 12; i++)
                yield return null;
            float moved = vcam.Lens.FieldOfView, gain = perlin.AmplitudeGain;
            FieldInfo f = Type.GetType("Unity.Cinemachine.Editor.SaveDuringPlay, Unity.Cinemachine.Editor")?.GetField("OnHotSave", BindingFlags.Static | BindingFlags.Public);
            var hot = f?.GetValue(null) as Delegate;
            int hooks = hot == null ? 0 : hot.GetInvocationList().Count(d => d.Method.DeclaringType == typeof(CinemachineSaved));
            hot?.DynamicInvoke();
            Debug.Log($"CI29b hot save · hooks {hooks} · lens {moved} → {vcam.Lens.FieldOfView} · gain {gain} → {perlin.AmplitudeGain}");
            Assert.That(hooks, Is.EqualTo(1), "the hook on Cinemachine's delegate once");
            Assert.That((moved, gain), Is.EqualTo((60f, 2f)), "the Tweens' ends became the base");
            Assert.That((vcam.Lens.FieldOfView, perlin.AmplitudeGain), Is.EqualTo((40f, 0.5f)), "the hot save puts the first values back");
        }
    }
}
