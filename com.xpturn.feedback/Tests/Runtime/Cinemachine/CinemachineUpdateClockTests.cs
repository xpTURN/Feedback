using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using UM = Unity.Cinemachine.CinemachineBrain.UpdateMethods;

namespace xpTURN.Feedback.Tests
{
    // a value written at the Update stage reaches the brain's output on every frame, whichever clock updates the live camera.
    // A constant value held over the whole window, read after the brain's LateUpdate: "written" is the feedback's own value
    // (the control), "seen" is what the brain put out
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    public sealed class CinemachineUpdateClockTests
    {
        public enum Channel { Shake, Lens, Noise }
        public enum CameraMotion { Still, MovedByPipeline, RigidbodyTarget, InterpolatedTarget }

        const int Warm = 120;
        const int Skip = 5;
        const int Measured = 240;
        const float BaseFov = 40f;

        readonly List<Object> m_Made = new List<Object>();
        FeedbackScheduler m_Saved;
        FeedbackScheduler m_Test;
        float m_SavedFixed;

        [SetUp]
        public void SetUp()
        {
            CinemachineCore.UniformDeltaTimeOverride = -1f;
            CinemachineCore.CurrentTimeOverride = -1f;
            m_Saved = FeedbackScheduler.Default;
            m_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = m_Test;
            m_SavedFixed = Time.fixedDeltaTime;
            Time.fixedDeltaTime = 0.02f;
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
            FeedbackScheduler.Default = m_Saved;
            if (!m_Test.IsShutdown)
                m_Test.Shutdown();
            Time.captureDeltaTime = 0f;
            Time.fixedDeltaTime = m_SavedFixed;
        }

        GameObject Made(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        FeedbackPlayer Player(FeedbackItem item)
        {
            GameObject go = Made("CI30 player");
            go.SetActive(false);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.Add(item);
            go.SetActive(true);
            p.EnsureRuntime();
            return p;
        }

        static TargetItem Item(Channel channel, float duration)
        {
            TargetItem item = channel switch
            {
                Channel.Shake => new CameraShakeItem(),
                Channel.Lens => new CinemachineLensFieldOfViewItem(),
                _ => new CinemachineNoiseAmplitudeGainItem(),
            };
            if (channel == Channel.Shake)
                item.m_Motion = new CurveMotion { m_Params = new CurveParams { m_Curve = AnimationCurve.Constant(0f, 1f, 1f), m_RemapZero = Vector4.zero, m_RemapOne = new Vector4(1f, 0f, 0f, 0f) } };
            else if (channel == Channel.Lens)
                item.m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(10f, 10f, 10f, 10f), m_Vibrato = 5f, m_Decay = 0f } };
            else
            {
                item.m_Motion = new FlashMotion { m_Params = new FlashParams { m_Target = new Vector4(2f, 2f, 2f, 2f), m_Attack = 0.01f, m_Hold = duration, m_Release = 0.01f } };
                duration += 0.02f;
            }
            item.m_Timing.m_Duration = duration;
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.MainCamera, m_Path = "", m_Anchor = "" };
            return item;
        }

        Transform Target(CameraMotion motion)
        {
            GameObject go = Made("CI30 target");
            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.linearDamping = 0f;
            body.interpolation = motion == CameraMotion.InterpolatedTarget ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            body.linearVelocity = Vector3.right;
            return go.transform;
        }

        [UnityTest]
        public IEnumerator CI30a_TheValueReachesTheBrain(
            [Values] Channel channel,
            [Values(UM.SmartUpdate, UM.FixedUpdate, UM.LateUpdate)] UM update,
            [Values] CameraMotion motion,
            [Values(30, 60, 75)] int fps)
        {
            float dt = 1f / fps;
            Time.captureDeltaTime = dt;

            GameObject rig = Made("CI30 camera");
            rig.tag = "MainCamera";
            rig.AddComponent<Camera>();
            var brain = rig.AddComponent<Unity.Cinemachine.CinemachineBrain>();
            brain.UpdateMethod = update;
            brain.BlendUpdateMethod = Unity.Cinemachine.CinemachineBrain.BrainUpdateMethods.LateUpdate;

            GameObject vcamGo = Made("CI30 vcam");
            vcamGo.transform.position = new Vector3(0f, 0f, -10f);
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Priority = new PrioritySettings { Value = 10 };
            vcam.Lens.FieldOfView = BaseFov;
            vcamGo.AddComponent<CinemachineShakeExtension>();

            CinemachineBasicMultiChannelPerlin perlin = null;
            ClockGainProbe probe = null;
            if (channel == Channel.Noise)
            {
                perlin = vcamGo.AddComponent<CinemachineBasicMultiChannelPerlin>();
                perlin.NoiseProfile = CinemachineFixtures.Profile();
                perlin.AmplitudeGain = 0f;
                perlin.FrequencyGain = 1f;
                probe = vcamGo.AddComponent<ClockGainProbe>();
                probe.Perlin = perlin;
            }

            Transform moving = vcamGo.transform;
            if (motion == CameraMotion.MovedByPipeline)
                vcamGo.AddComponent<ClockPipelineMover>();
            else if (motion != CameraMotion.Still)
            {
                moving = Target(motion);
                vcam.Target.TrackingTarget = moving;
                vcamGo.AddComponent<CinemachineFollow>();
            }

            for (int i = 0; i < Warm; i++)
                yield return null;

            Vector3 start = moving.position;
            FeedbackPlayer player = Player(Item(channel, (Skip + Measured + 10) * dt));

            int frame = 0, written = 0, seen = 0, run = 0, longest = 0;
            float lens1 = BaseFov, lens2 = BaseFov;
            var recorderGo = Made("CI30 recorder");
            var recorder = recorderGo.AddComponent<CinemachineLateRecorder>();
            recorder.OnLate = f =>
            {
                if (++frame <= Skip || frame > Skip + Measured)
                    return;
                bool wrote, shown;
                switch (channel)
                {
                    case Channel.Shake:
                        wrote = CinemachineShakeExtension.PositionOf(brain).magnitude > 0.5f;
                        shown = brain.State.PositionCorrection.magnitude > 0.5f;
                        break;
                    case Channel.Lens:
                        // a fixed clock shows the value of a frame or two ago, and this one moves every frame
                        float fov = vcam.Lens.FieldOfView, output = brain.State.Lens.FieldOfView;
                        wrote = Mathf.Abs(fov - BaseFov) > 0.5f && Mathf.Abs(lens1 - BaseFov) > 0.5f && Mathf.Abs(lens2 - BaseFov) > 0.5f;
                        shown = Mathf.Abs(output - fov) < 0.01f || Mathf.Abs(output - lens1) < 0.01f || Mathf.Abs(output - lens2) < 0.01f;
                        lens2 = lens1;
                        lens1 = fov;
                        break;
                    default:
                        wrote = perlin.AmplitudeGain > 0.5f;
                        shown = probe.Gain > 0.5f;
                        break;
                }
                if (!wrote)
                    return;
                written++;
                if (shown)
                {
                    seen++;
                    run = 0;
                }
                else
                    longest = Mathf.Max(longest, ++run);
            };

            player.Play();
            for (int i = 0; i < Skip + Measured + 1; i++)
                yield return null;
            recorder.OnLate = null;

            float moved = (moving.position - start).magnitude;
            Debug.Log($"CI30 | {channel} | {update} | {motion} | {fps} | written {written}/{Measured} | seen {seen}/{Measured} | longest lost {longest} | moved {moved:F3}");
            if (motion != CameraMotion.Still)
                Assert.That(moved, Is.GreaterThan(1e-3f), "control: the camera or its target moved");
            Assert.That(written, Is.GreaterThanOrEqualTo(channel == Channel.Lens ? Measured / 2 : Measured), "control: the feedback held its value");
            Assert.That(seen, Is.EqualTo(written), "the brain's output carries the value on every frame it is written");
        }

        // the game's Update reads the value without the offset, so a read-modify-write there does not make the offset its base
        [UnityTest]
        public IEnumerator CI30b_UpdateCodeReadsTheBase([Values(UM.SmartUpdate, UM.FixedUpdate, UM.LateUpdate)] UM update)
        {
            float dt = 1f / 60f;
            Time.captureDeltaTime = dt;

            GameObject rig = Made("CI30 camera");
            rig.tag = "MainCamera";
            rig.AddComponent<Camera>();
            var brain = rig.AddComponent<Unity.Cinemachine.CinemachineBrain>();
            brain.UpdateMethod = update;
            brain.BlendUpdateMethod = Unity.Cinemachine.CinemachineBrain.BrainUpdateMethods.LateUpdate;

            GameObject vcamGo = Made("CI30 vcam");
            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Priority = new PrioritySettings { Value = 10 };
            vcam.Lens.FieldOfView = BaseFov;
            var reader = vcamGo.AddComponent<ClockLensRewriter>();
            reader.Target = vcam;
            reader.Base = BaseFov;

            for (int i = 0; i < Warm; i++)
                yield return null;

            FeedbackPlayer player = Player(Item(Channel.Lens, 60 * dt));
            float largest = 0f;
            var recorder = Made("CI30 recorder").AddComponent<CinemachineLateRecorder>();
            recorder.OnLate = f => largest = Mathf.Max(largest, Mathf.Abs(vcam.Lens.FieldOfView - BaseFov));
            reader.Worst = 0f;
            player.Play();
            for (int i = 0; i < 70; i++)
                yield return null;
            recorder.OnLate = null;

            Assert.That(largest, Is.GreaterThan(1f), "control: the feedback moved the lens");
            Assert.That(reader.Worst, Is.LessThan(1e-3f), "Update reads the value without the offset");
            Assert.That(vcam.Lens.FieldOfView, Is.EqualTo(BaseFov).Within(1e-3f), "the base is back after the play");
        }
    }

    public sealed class ClockLensRewriter : MonoBehaviour
    {
        public CinemachineCamera Target;
        public float Base;
        public float Worst;

        void Update()
        {
            float fov = Target.Lens.FieldOfView;
            Worst = Mathf.Max(Worst, Mathf.Abs(fov - Base));
            Target.Lens.FieldOfView = fov;
        }
    }

    public sealed class ClockPipelineMover : CinemachineExtension
    {
        protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage == CinemachineCore.Stage.Body)
                state.RawPosition += Vector3.right * 0.001f;
        }
    }

    public sealed class ClockGainProbe : CinemachineExtension
    {
        public CinemachineBasicMultiChannelPerlin Perlin;
        public float Gain;

        protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage == CinemachineCore.Stage.Noise && Perlin != null)
                Gain = Perlin.AmplitudeGain;
        }
    }
}
