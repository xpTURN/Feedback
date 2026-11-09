using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the Cinemachine items in edit mode: test schedulers ticked by hand · the preview on a saved additive scene with its own
    // clock. The batch scene's 'Main Camera' is untagged while a test's camera is the main one
    public sealed class CinemachineTargetsTests
    {
        const string ScenePath = "Assets/CinemachineTargetsTests.unity";
        const string SdpKey = "SaveDuringPlay_Enabled";
        const string DialogLine = "DisplayDialog: Save changes made in Play Mode";
        static readonly Vector3 At = new Vector3(0f, 0f, -10f);

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        List<GameObject> m_Mains;
        Scene m_Active;
        Scene m_Scene;
        Func<double> m_Clock;
        IPreviewRecordSource m_Source;
        double m_Now;

        [TearDown]
        public void TearDown()
        {
            PreviewSession.Current?.Stop();
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Schedulers.Clear();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            if (m_Mains != null)
            {
                foreach (GameObject g in m_Mains)
                {
                    if (g != null)
                        g.tag = "MainCamera";
                }
                m_Mains = null;
            }
            if (m_Clock != null)
            {
                PreviewClock.Source = m_Clock;
                PreviewRecordPaths.Source = m_Source;
                m_Clock = null;
            }
            if (m_Scene.IsValid())
            {
                if (m_Active.IsValid())
                    SceneManager.SetActiveScene(m_Active);
                EditorSceneManager.CloseScene(m_Scene, true);
                AssetDatabase.DeleteAsset(ScenePath);
                m_Scene = default;
            }
            CinemachineCore.UniformDeltaTimeOverride = -1f;
            CinemachineCore.CurrentTimeOverride = -1f;
            CinemachineCore.GetBlendOverride = null;
            CinemachineImpulseManager.Instance.Clear();
            CinemachineImpulseManager.Instance.IgnoreTimeScale = false;
        }

        FeedbackScheduler Sched(ContextKind kind = ContextKind.Test)
        {
            var s = new FeedbackScheduler(kind);
            m_Schedulers.Add(s);
            return s;
        }

        void UntagMains()
        {
            m_Mains ??= new List<GameObject>();
            foreach (GameObject g in GameObject.FindGameObjectsWithTag("MainCamera"))
            {
                g.tag = "Untagged";
                m_Mains.Add(g);
            }
        }

        // a saved additive scene, active · the preview's own clock and record table — the tear-down closes it
        void OpenScene()
        {
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            m_Active = SceneManager.GetActiveScene();
            m_Scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(m_Scene);
            UntagMains();
            m_Clock = PreviewClock.Source;
            m_Source = PreviewRecordPaths.Source;
            m_Now = 0;
            PreviewClock.Source = () => m_Now;
            PreviewRecordPaths.Source = new PreviewRecordPathTable();
        }

        GameObject Made(string name)
        {
            var go = new GameObject(name);
            if (!m_Scene.IsValid())
                m_Made.Add(go);
            return go;
        }

        Camera MainRig()
        {
            UntagMains();
            GameObject go = Made("CI camera");
            go.tag = "MainCamera";
            Camera cam = go.AddComponent<Camera>();
            go.AddComponent<Unity.Cinemachine.CinemachineBrain>();
            return cam;
        }

        CinemachineCamera Vcam(string name, int priority = 10)
        {
            GameObject go = Made(name);
            go.transform.position = At;
            var v = go.AddComponent<CinemachineCamera>();
            v.Priority = new PrioritySettings { Value = priority };
            return v;
        }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            GameObject go = Made("CI player");
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            if (s != null)
            {
                p.Bind(s);
                p.EnsureRuntime();
            }
            return p;
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

        static IEnumerator Step(int n)
        {
            for (int i = 0; i < n; i++)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }
        }

        // the preview's ticks — the clock 1/60 s each, the session's update, then a frame
        IEnumerator Preview(int frames, Action each = null)
        {
            for (int i = 0; i < frames; i++)
            {
                m_Now += 1.0 / 60.0;
                PreviewSession.Current?.Update();
                yield return null;
                each?.Invoke();
            }
        }

        static Vector3 Seeds(CinemachineBasicMultiChannelPerlin perlin)
            => (Vector3)typeof(CinemachineBasicMultiChannelPerlin).GetField("m_NoiseOffsets", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(perlin);

        static int ActiveEvents()
            => typeof(CinemachineImpulseManager).GetField("m_ActiveEvents", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(CinemachineImpulseManager.Instance) is ICollection c ? c.Count : -1;

        static FieldInfo HotSave() => Type.GetType("Unity.Cinemachine.Editor.SaveDuringPlay, Unity.Cinemachine.Editor")?.GetField("OnHotSave", BindingFlags.Static | BindingFlags.Public);

        static int Hooks()
            => HotSave()?.GetValue(null) is Delegate d ? d.GetInvocationList().Count(x => x.Method.DeclaringType == typeof(CinemachineSaved)) : 0;

        // ---- the service — made at the first preparation · one per context · the preview's only writes the time · Shutdown closes
        // it and puts the blend override back only when ours is on top (a user's set after ours stays)

        [Test]
        public void CI03a_TheServicesLife()
        {
            GameObject vgo = Made("CI vcam");
            var vcam = vgo.AddComponent<CinemachineCamera>();
            foreach (ContextKind kind in new[] { ContextKind.Test, ContextKind.Preview })
            {
                FeedbackScheduler s = Sched(kind);
                int services = s.Context.ServiceCount;
                Player(s, With(new CinemachineLensFieldOfViewItem(), TweenTo(60f), 0.1f, TargetRefMode.Direct, vcam));
                Assert.That(s.Context.ServiceCount, Is.EqualTo(services + 1), kind + ": made at the preparation");
                CinemachineFeedbackService svc = CinemachineFeedbackService.Of(s.Context);
                Assert.That(s.Context.ServiceCount, Is.EqualTo(services + 1), kind + ": the same one after");
                Assert.That(svc.WritesTime, Is.EqualTo(kind == ContextKind.Preview), kind + ": the preview's writes the time");
                s.Shutdown();
                Assert.That(svc.IsClosed && svc.Refuses(), Is.True, kind + ": closed — it refuses");
            }
            FeedbackScheduler closed = Sched();
            closed.Shutdown();
            Assert.That(CinemachineFeedbackService.Of(closed.Context).IsClosed, Is.True, "a closed context's service closes at once");
        }

        [Test]
        public void CI03b_TheShutdownLeavesOthers()
        {
            CinemachineCore.GetBlendOverride = null;
            GameObject vgo = Made("CI vcam");
            var vcam = vgo.AddComponent<CinemachineCamera>();
            FeedbackScheduler s1 = Sched();
            CinemachineFeedbackService.Of(s1.Context).PendBlend(vcam, new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Linear, 0.5f));
            bool installed = CinemachineFeedbackService.Of(s1.Context).Installed;
            s1.Shutdown();
            bool back = CinemachineCore.GetBlendOverride == null;
            CinemachineCore.GetBlendOverrideDelegate before = (f, to, d, o) => d;
            CinemachineCore.GetBlendOverride = before;
            FeedbackScheduler s2 = Sched();
            CinemachineFeedbackService.Of(s2.Context).PendBlend(vcam, new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Linear, 0.5f));
            s2.Shutdown();
            bool previous = CinemachineCore.GetBlendOverride == before;
            FeedbackScheduler s3 = Sched();
            CinemachineFeedbackService.Of(s3.Context).PendBlend(vcam, new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Linear, 0.5f));
            CinemachineCore.GetBlendOverrideDelegate ours = CinemachineCore.GetBlendOverride;
            CinemachineCore.GetBlendOverrideDelegate user = (f, to, d, o) => ours(f, to, d, o);
            CinemachineCore.GetBlendOverride = user;
            s3.Shutdown();
            bool kept = CinemachineCore.GetBlendOverride == user;
            Assert.That(installed, Is.True, "ours installed");
            Assert.That((back, previous), Is.EqualTo((true, true)), "ours on top goes back to the one before");
            Assert.That(kept, Is.True, "a user's on top of ours stays");
        }

        // ---- the lens's ranges — the field of view 1 ~ 179 · the size above 0 · the dutch −180 ~ 180 (the package does not
        // clamp a written lens)

        [Test]
        public void CI04c_TheLensRanges()
        {
            FeedbackScheduler s = Sched();
            GameObject vgo = Made("CI vcam");
            var vcam = vgo.AddComponent<CinemachineCamera>();
            FeedbackPlayer p = Player(s, With(new CinemachineLensFieldOfViewItem(), TweenTo(500f), 0.1f, TargetRefMode.Direct, vcam),
                With(new CinemachineLensOrthographicSizeItem(), TweenTo(-5f), 0.1f, TargetRefMode.Direct, vcam), With(new CinemachineLensDutchItem(), TweenTo(400f), 0.1f, TargetRefMode.Direct, vcam));
            p.Play();
            for (int i = 0; i < 10; i++)
                s.Tick(0.05f);
            var high = (vcam.Lens.FieldOfView, vcam.Lens.OrthographicSize, vcam.Lens.Dutch);
            var q = Player(s, With(new CinemachineLensFieldOfViewItem(), TweenTo(-50f), 0.1f, TargetRefMode.Direct, vcam), With(new CinemachineLensDutchItem(), TweenTo(-400f), 0.1f, TargetRefMode.Direct, vcam));
            q.Play();
            for (int i = 0; i < 10; i++)
                s.Tick(0.05f);
            Assert.That(high, Is.EqualTo((179f, CinemachineLensOrthographicSizeSlot.MinSize, 180f)), "the upper ends · the size above 0");
            Assert.That((vcam.Lens.FieldOfView, vcam.Lens.Dutch), Is.EqualTo((1f, -180f)), "the lower ends");
        }

        // ---- the hooks, the registrations, the global state and the hot-save hook come from the editor's load — after a reload
        // with no play since (a play entry's SubsystemRegistration would fill them too)

        [UnityTest]
        public IEnumerator CI16a_TheHooksAtTheEditorsLoad()
        {
            CameraHooks.ShakeSlots = null;
            CameraHooks.MainCameraTarget = null;
            CameraHooks.TimeCapture = null;
            CameraHooks.TimeRestore = null;
            CameraHooks.TimeDescribe = null;
            EditorUtility.RequestScriptReload();
            yield return new WaitForDomainReload();
            var types = new[] { typeof(CinemachineImpulseItem), typeof(CinemachineImpulseClearItem), typeof(CinemachineLensFieldOfViewItem), typeof(CinemachineLensOrthographicSizeItem),
                typeof(CinemachineLensDutchItem), typeof(CinemachineNoiseAmplitudeGainItem), typeof(CinemachineNoiseFrequencyGainItem), typeof(CinemachineActivateCameraItem) };
            int registered = types.Count(t => FeedbackImplementations.RegisteredTypes.Contains(t));
            var s = new FeedbackScheduler(ContextKind.Test);
            bool service = CinemachineFeedbackService.Of(s.Context) != null;
            s.Shutdown();
            Assert.That(registered, Is.EqualTo(8), "the eight items");
            Assert.That(service, Is.True, "the extension service");
            Assert.That((CameraHooks.ShakeSlots != null, CameraHooks.MainCameraTarget != null, CameraHooks.TimeCapture != null, CameraHooks.TimeRestore != null, CameraHooks.TimeDescribe != null),
                Is.EqualTo((true, true, true, true, true)), "Core's five hooks");
            Assert.That(PreviewGlobalState.Registered, Has.Member(CinemachineTimeState.Instance), "the global state");
            Assert.That(Hooks(), Is.EqualTo(1), "the hot-save hook once");
        }

        // ---- the preview — the lens on the brain's camera, the noise and the impulse on the Cinemachine time · at the end the lens,
        // the gain, the noise seeds and the time back, the scene clean, nothing unrecorded

        [UnityTest]
        public IEnumerator CI17a_ThePreview()
        {
            OpenScene();
            Camera cam = MainRig();
            CinemachineCamera vcam = Vcam("CI vcam");
            vcam.Lens.FieldOfView = 40f;
            var perlin = vcam.gameObject.AddComponent<CinemachineBasicMultiChannelPerlin>();
            perlin.NoiseProfile = AssetDatabase.LoadAssetAtPath<NoiseSettings>("Packages/com.unity.cinemachine/Presets/Noise/6D Shake.asset");
            perlin.AmplitudeGain = 0f;
            var listener = vcam.gameObject.AddComponent<CinemachineImpulseListener>();
            listener.ChannelMask = 1;
            listener.Gain = 1f;
            var noise = With(new CinemachineNoiseAmplitudeGainItem(), new FlashMotion { m_Params = new FlashParams { m_Target = Vector4.one, m_Attack = 0.05f, m_Hold = 0.5f, m_Release = 0.05f } }, 0.6f);
            FeedbackPlayer player = Player(null, With(new CinemachineLensFieldOfViewItem(), TweenTo(60f), 0.3f), noise, new CinemachineImpulseItem { m_Duration = 0.5f, m_Velocity = new Vector3(0f, -1f, 0f) });
            Assert.That(EditorSceneManager.SaveScene(m_Scene), Is.True);
            Vector3 seeds = Seeds(perlin);
            string before = CinemachineTimeState.Instance.Describe();
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            List<Type> unrecorded = PreviewSession.Current.Unrecorded.ToList();
            float maxFov = 0f, maxDev = 0f, minY = 0f;
            bool reseeded = false;
            yield return Preview(40, () =>
            {
                reseeded |= Seeds(perlin) != seeds;
                maxFov = Mathf.Max(maxFov, cam.fieldOfView);
                maxDev = Mathf.Max(maxDev, Mathf.Abs(cam.transform.position.x) + Mathf.Abs(cam.transform.position.z + 10f));
                minY = Mathf.Min(minY, cam.transform.position.y);
            });
            PreviewSession.Current?.Stop();
            yield return Step(2);
            string after = CinemachineTimeState.Instance.Describe();
            Debug.Log($"CI17a preview · fov {maxFov} · noise {maxDev:F3} · impulse {minY:F3} · lens {vcam.Lens.FieldOfView} · gain {perlin.AmplitudeGain} · seeds {seeds} → {Seeds(perlin)} · time '{before}' → '{after}' · dirty {m_Scene.isDirty}");
            Assert.That(unrecorded, Is.Empty, "the lens and the gain have record paths");
            Assert.That(maxFov, Is.GreaterThan(45f), "the lens moved on the brain's camera");
            Assert.That(maxDev, Is.GreaterThan(1e-3f), "the noise ran on the Cinemachine time");
            Assert.That(reseeded, Is.True, "the noise moved its seeds — the global state's to put back");
            Assert.That(minY, Is.LessThan(-0.05f), "the impulse showed");
            Assert.That((vcam.Lens.FieldOfView, perlin.AmplitudeGain, Seeds(perlin) == seeds, after == before, m_Scene.isDirty), Is.EqualTo((40f, 0f, true, true, false)), "everything back, the scene clean");
        }

        // a camera shake's preview on a camera Cinemachine drives — without the extension the brain writes over it · with it the shake
        // shows after the player loop, through the extension's own [ExecuteAlways]
        [UnityTest]
        public IEnumerator CI17b_TheShakePreviewThroughTheExtension()
        {
            OpenScene();
            Camera cam = MainRig();
            CinemachineCamera vcam = Vcam("CI vcam");
            var shake = new CameraShakeItem { m_Motion = new ShakeMotion() };
            shake.m_Timing.m_Duration = 0.5f;
            shake.m_Target = new TargetRef { m_Mode = TargetRefMode.MainCamera, m_Path = "", m_Anchor = "" };
            FeedbackPlayer player = Player(null, shake);
            Assert.That(EditorSceneManager.SaveScene(m_Scene), Is.True);
            var moved = new float[2];
            var dirty = new bool[2];
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    vcam.gameObject.AddComponent<CinemachineShakeExtension>();
                    EditorSceneManager.SaveScene(m_Scene);
                }
                yield return Step(2);
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                float max = 0f;
                yield return Preview(20, () => max = Mathf.Max(max, (cam.transform.position - At).magnitude));
                moved[pass] = max;
                PreviewSession.Current?.Stop();
                yield return Step(2);
                dirty[pass] = m_Scene.isDirty;
            }
            Debug.Log($"CI17b shake preview · without {moved[0]} · with {moved[1]} · dirty {dirty[0]} {dirty[1]}");
            Assert.That(moved[0], Is.LessThan(1e-4f), "without the extension the brain writes over the shake");
            Assert.That(moved[1], Is.GreaterThan(0.1f), "with the extension the shake shows");
            Assert.That(dirty[1], Is.False, "the scene clean");
        }

        // ---- the Cinemachine time's global state — registered · the time written during a session, the same strings after · the
        // end clears the manager only after a session that fired

        [UnityTest]
        public IEnumerator CI18a_TheTimeState()
        {
            OpenScene();
            CinemachineCamera vcam = Vcam("CI vcam");
            FeedbackPlayer lens = Player(null, With(new CinemachineLensFieldOfViewItem(), TweenTo(60f), 1f, TargetRefMode.Direct, vcam));
            FeedbackPlayer impulse = Player(null, new CinemachineImpulseItem { m_Duration = 1f });
            Assert.That(EditorSceneManager.SaveScene(m_Scene), Is.True);
            Assert.That(PreviewGlobalState.Registered, Has.Member(CinemachineTimeState.Instance), "registered at the editor's load");
            var alive = new bool[2];
            string before = CinemachineTimeState.Instance.Describe();
            var during = new string[2];
            var after = new string[2];
            int pass = 0;
            foreach (FeedbackPlayer player in new[] { lens, impulse })
            {
                CinemachineImpulseManager.ImpulseEvent fe = CinemachineImpulseManager.Instance.NewImpulseEvent();
                fe.Envelope = new CinemachineImpulseManager.EnvelopeDefinition { SustainTime = 100f };
                fe.SignalSource = new CinemachineImpulseSignal(CinemachineImpulseCurves.Of(CinemachineImpulseShape.Bump, null)) { Velocity = Vector3.down, Duration = 100f };
                fe.Channel = 1;
                CinemachineImpulseManager.Instance.AddImpulseEvent(fe);
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                yield return Preview(20);
                during[pass] = CinemachineTimeState.Instance.Describe();
                PreviewSession.Current?.Stop();
                yield return null;
                after[pass] = CinemachineTimeState.Instance.Describe();
                alive[pass] = ActiveEvents() > 0 && fe.SignalSource != null;
                CinemachineImpulseManager.Instance.Clear();
                pass++;
            }
            Debug.Log($"CI18a time · '{before}' · during '{during[0]}' '{during[1]}' · after '{after[0]}' '{after[1]}' · the foreign impulse alive {alive[0]} {alive[1]}");
            Assert.That(during[0], Is.Not.EqualTo(before), "a session writes the time");
            Assert.That(after, Is.EqualTo(new[] { before, before }), "the same after");
            Assert.That(alive, Is.EqualTo(new[] { true, false }), "the end clears the manager after a session that fired only");
        }

        // ---- the notices — a shake on a camera Cinemachine drives without the extension (in place of the Game view's line) · with
        // it the Game view's · an activation shows only (its priority untouched)

        [UnityTest]
        public IEnumerator CI19a_TheNotices()
        {
            OpenScene();
            MainRig();
            CinemachineCamera a = Vcam("CI A", 10);
            CinemachineCamera b = Vcam("CI B", 3);
            var shake = new CameraShakeItem { m_Motion = new ShakeMotion() };
            shake.m_Timing.m_Duration = 0.3f;
            shake.m_Target = new TargetRef { m_Mode = TargetRefMode.MainCamera, m_Path = "", m_Anchor = "" };
            FeedbackPlayer shaker = Player(null, shake);
            FeedbackPlayer activate = Player(null, With(new CinemachineActivateCameraItem(), new SetMotion(), 0f, TargetRefMode.Direct, b));
            Assert.That(EditorSceneManager.SaveScene(m_Scene), Is.True);
            var kinds = new List<PreviewNoticeKind>[3];
            var notices = new List<PreviewNotice>();
            for (int pass = 0; pass < 3; pass++)
            {
                if (pass == 1)
                {
                    a.gameObject.AddComponent<CinemachineShakeExtension>();
                    EditorSceneManager.SaveScene(m_Scene);
                }
                Assert.That(PreviewSession.Start(pass < 2 ? shaker : activate, default), Is.EqualTo(PreviewRefusal.None));
                PreviewNotices.Collect(PreviewSession.Current, notices);
                yield return Preview(5);
                kinds[pass] = notices.Select(n => n.Kind).ToList();
                PreviewSession.Current?.Stop();
                yield return Step(1);
            }
            Assert.That((kinds[0].Contains(PreviewNoticeKind.CinemachineNoExtension), kinds[0].Contains(PreviewNoticeKind.CameraGameView)), Is.EqualTo((true, false)), "no extension — its line for the Game view's");
            Assert.That((kinds[1].Contains(PreviewNoticeKind.CinemachineNoExtension), kinds[1].Contains(PreviewNoticeKind.CameraGameView)), Is.EqualTo((false, true)), "the extension — the Game view's");
            Assert.That(kinds[2], Has.Member(PreviewNoticeKind.CinemachinePlayOnly), "an activation — play only");
            Assert.That(b.Priority.Value, Is.EqualTo(3), "the preview shows the activation only");
        }

        // ---- FB-R01 ~ FB-R04 — quiet without their items · the fixes · FB-H05's text on an offset item too

        [Test]
        public void CI20a_TheRules()
        {
            Camera cam = MainRig();
            CinemachineCamera vcam = Vcam("CI vcam");
            var perlin = vcam.gameObject.AddComponent<CinemachineBasicMultiChannelPerlin>();
            GameObject hostGo = Made("CI host");
            var host = hostGo.AddComponent<FeedbackPlayer>();
            Assert.That(FeedbackValidator.Run(host).Where(r => r.Id.StartsWith("FB-R", StringComparison.Ordinal)), Is.Empty, "quiet without their items");
            var shake = new CameraShakeItem { m_Motion = new ShakeMotion() };
            shake.m_Target = new TargetRef { m_Mode = TargetRefMode.MainCamera, m_Path = "", m_Anchor = "" };
            host.m_Items.AddRange(new FeedbackItem[]
            {
                shake,                                                                                          // 0 R01
                With(new CinemachineNoiseAmplitudeGainItem(), TweenTo(1f), 0.1f),                               // 1 R02
                new CinemachineImpulseItem(),                                                                   // 2 R03
                With(new CinemachineLensFieldOfViewItem(), TweenTo(60f), 0.1f),                                 // 3
                With(new CameraFieldOfViewItem(), new PunchMotion { m_Params = PunchParams.Default }, 0.1f),    // 4 H05
            });
            List<RuleResult> got = FeedbackValidator.Run(host).Where(r => r.Id.StartsWith("FB-R", StringComparison.Ordinal) || r.Id == "FB-H05").ToList();
            Assert.That(got.Select(r => r.Id + "@" + r.Item + ":" + r.Severity).OrderBy(x => x, StringComparer.Ordinal),
                Is.EqualTo(new[] { "FB-H05@4:Warning", "FB-R01@0:Info", "FB-R02@1:Info", "FB-R03@2:Warning" }));
            Assert.That(got.Single(r => r.Id == "FB-H05").Message, Does.Contain("an offset shows in play only"), "H05's text tells the offset's");
            Assert.That(got.Single(r => r.Id == "FB-R03").Message, Does.StartWith("No Cinemachine camera has a Cinemachine Impulse Listener"), "no listener at all");

            RuleResult r01 = got.Single(r => r.Id == "FB-R01");
            Undo.ClearAll();
            FeedbackValidator.Apply(r01, r01.Fixes.Single());
            bool added = vcam.TryGetComponent(out CinemachineShakeExtension _);
            bool quiet = FeedbackValidator.Run(host).All(r => r.Id != "FB-R01");
            Undo.PerformUndo();
            Assert.That((added, quiet, vcam.TryGetComponent(out CinemachineShakeExtension _)), Is.EqualTo((true, true, false)), "FB-R01's fix adds the extension — one undo takes it away");

            perlin.NoiseProfile = ScriptableObject.CreateInstance<NoiseSettings>();
            m_Made.Add(perlin.NoiseProfile);
            var listener = vcam.gameObject.AddComponent<CinemachineImpulseListener>();
            listener.ChannelMask = 2;
            vcam.gameObject.AddComponent<CinemachineFollowZoom>();
            got = FeedbackValidator.Run(host).Where(r => r.Id.StartsWith("FB-R", StringComparison.Ordinal)).ToList();
            Assert.That(got.Select(r => r.Id + "@" + r.Item).OrderBy(x => x, StringComparer.Ordinal), Is.EqualTo(new[] { "FB-R01@0", "FB-R03@2", "FB-R04@3" }), "a profile · a listener off the channel · a lens writer");
            Assert.That(got.Single(r => r.Id == "FB-R03").Message, Does.Contain("channel mask 1"));
            listener.ChannelMask = 1;
            Assert.That(FeedbackValidator.Run(host).Where(r => r.Id == "FB-R03"), Is.Empty, "a listener on the channel");
        }

        // ---- the defaults and their keeping — the impulse's fields · the activation's blend · the mirrors' values through a copy
        // (an enum, the struct, a curve, a reference) · the lens's motion defaults (the camera's)

        [Test]
        public void CI21a_TheDefaultsAndTheirKeeping()
        {
            var impulse = new CinemachineImpulseItem();
            var activate = new CinemachineActivateCameraItem();
            Assert.That((impulse.m_Origin.m_Mode, impulse.m_Mode, impulse.m_Shape, impulse.m_Spread, impulse.m_Duration, impulse.m_Channel, impulse.m_Velocity),
                Is.EqualTo((TargetRefMode.Self, CinemachineImpulseMode.Definition, CinemachineImpulseShape.Bump, CinemachineImpulseSpread.Uniform, 0.2f, 1, new Vector3(0f, -1f, 0f))));
            Assert.That((activate.m_ToTop, activate.m_Priority, activate.m_Blend.m_Override, activate.m_Blend.m_Style, activate.m_Blend.m_Time), Is.EqualTo((true, 10, false, CinemachineBlendStyle.EaseInOut, 1f)));

            GameObject sgo = Made("CI source");
            var source = sgo.AddComponent<CinemachineImpulseSource>();
            GameObject go = Made("CI keeping");
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.Add(new CinemachineImpulseItem { m_Mode = CinemachineImpulseMode.Source, m_Source = source, m_Shape = CinemachineImpulseShape.Explosion, m_Spread = CinemachineImpulseSpread.Propagating, m_CustomShape = AnimationCurve.Linear(0f, 0f, 1f, 1f) });
            p.m_Items.Add(new CinemachineActivateCameraItem { m_ToTop = false, m_Priority = 7, m_Blend = new CinemachineBlendSpec { m_Override = true, m_Style = CinemachineBlendStyle.Custom, m_Time = 0.25f, m_Curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f) } });
            FeedbackPlayer copy = Object.Instantiate(p);
            m_Made.Add(copy.gameObject);
            var i2 = (CinemachineImpulseItem)copy.m_Items[0];
            var a2 = (CinemachineActivateCameraItem)copy.m_Items[1];
            Assert.That((i2.m_Mode, i2.m_Source == source, i2.m_Shape, i2.m_Spread, i2.m_CustomShape.length), Is.EqualTo((CinemachineImpulseMode.Source, true, CinemachineImpulseShape.Explosion, CinemachineImpulseSpread.Propagating, 2)), "the impulse kept");
            Assert.That((a2.m_ToTop, a2.m_Priority, a2.m_Blend.m_Override, a2.m_Blend.m_Style, a2.m_Blend.m_Time, a2.m_Blend.m_Curve.length), Is.EqualTo((false, 7, true, CinemachineBlendStyle.Custom, 0.25f, 2)), "the activation kept");

            var punch = (PunchMotion)MotionDefaults.Create(typeof(CinemachineLensFieldOfViewItem), MotionKind.Punch, out _);
            var tween = (TweenMotion)MotionDefaults.Create(typeof(CinemachineLensFieldOfViewItem), MotionKind.Tween, out _);
            var spring = (SpringMotion)MotionDefaults.Create(typeof(CinemachineLensOrthographicSizeItem), MotionKind.Spring, out _);
            var size = (TweenMotion)MotionDefaults.Create(typeof(CinemachineLensOrthographicSizeItem), MotionKind.Tween, out _);
            Assert.That((punch.m_Params.m_Amplitude.x, tween.m_Params.m_To.x, spring.m_Params.m_Value.x, size.m_Params.m_To.x),
                Is.EqualTo((MotionDefaults.FieldOfViewSize, MotionDefaults.FieldOfViewTo, MotionDefaults.OrthographicSizeBump, MotionDefaults.OrthographicSizeTo)), "the camera lens's defaults");
        }

        // Cinemachine's standard impulse shapes — our copies of its keys give its values bit for bit
        [Test]
        public void CI21b_TheStandardCurvesAreCinemachines()
        {
            MethodInfo m = typeof(CinemachineImpulseDefinition).GetMethod("GetStandardCurve", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(m, Is.Not.Null, "Cinemachine's own curves");
            var bad = new List<string>();
            foreach (CinemachineImpulseShape shape in new[] { CinemachineImpulseShape.Recoil, CinemachineImpulseShape.Bump, CinemachineImpulseShape.Explosion, CinemachineImpulseShape.Rumble })
            {
                var theirs = (AnimationCurve)m.Invoke(null, new object[] { (CinemachineImpulseDefinition.ImpulseShapes)(int)shape });
                AnimationCurve ours = CinemachineImpulseCurves.Of(shape, null);
                for (int i = 0; i <= 100; i++)
                {
                    float t = i / 100f;
                    if (BitConverter.SingleToInt32Bits(theirs.Evaluate(t)) != BitConverter.SingleToInt32Bits(ours.Evaluate(t)))
                        bad.Add(shape + "@" + t);
                }
            }
            Assert.That(bad, Is.Empty);
        }

        // ---- a replay leaves the manager's time-scale flag · a stop leaves a foreign impulse · an activation is not undone at its
        // end and keeps the camera enabled

        [Test]
        public void CI22a_KnownDefectsStayFixed()
        {
            FeedbackScheduler s = Sched();
            CinemachineCamera b = Vcam("CI B", 3);
            CinemachineImpulseManager.Instance.IgnoreTimeScale = true;
            FeedbackPlayer impulse = Player(s, new CinemachineImpulseItem { m_Duration = 1f });
            impulse.Play();
            s.Tick(0.05f);
            CinemachineImpulseManager.ImpulseEvent fe = CinemachineImpulseManager.Instance.NewImpulseEvent();
            fe.Envelope = new CinemachineImpulseManager.EnvelopeDefinition { SustainTime = 10f };
            fe.SignalSource = new CinemachineImpulseSignal(CinemachineImpulseCurves.Of(CinemachineImpulseShape.Bump, null)) { Velocity = Vector3.down, Duration = 10f };
            CinemachineImpulseManager.Instance.AddImpulseEvent(fe);
            float foreign = fe.Envelope.Duration;
            impulse.Play();
            s.Tick(0.05f);
            impulse.Stop();
            s.Tick(0.05f);
            bool flag = CinemachineImpulseManager.Instance.IgnoreTimeScale;
            b.Priority = new PrioritySettings { Enabled = false, Value = 3 };
            FeedbackPlayer activate = Player(s, With(new CinemachineActivateCameraItem { m_ToTop = false, m_Priority = 25 }, new SetMotion(), 0.2f, TargetRefMode.Direct, b));
            activate.Play();
            for (int i = 0; i < 10; i++)
                s.Tick(0.05f);
            Assert.That(flag, Is.True, "a replay leaves the manager's flag");
            Assert.That((fe.Envelope.Duration, fe.SignalSource != null), Is.EqualTo((foreign, true)), "a stop leaves a foreign impulse");
            Assert.That((activate.IsPlaying, b.Priority.Enabled, b.Priority.Value), Is.EqualTo((false, true, 25)), "the activation stays after its end — enabled");
        }

        // ---- the debugger reads the extensions made, never makes one — the service's name and rows

        [Test]
        public void CI23a_TheDebuggerRow()
        {
            FeedbackScheduler s = Sched();
            var reader = new DebuggerReader();
            int services = s.Context.ServiceCount;
            reader.Read(s);
            Assert.That((reader.ExtensionNames.Count, s.Context.ServiceCount), Is.EqualTo((0, services)), "none made — none read, the read makes none");
            Player(s, new CinemachineImpulseItem { m_Duration = 0.2f });
            reader.Read(s);
            Assert.That(reader.ExtensionNames, Is.EqualTo(new[] { "Cinemachine" }));
            Assert.That(reader.ExtensionRows[0].Select(r => r.Label + " " + r.Value),
                Is.EqualTo(new[] { "impulses fired 0", "taken ahead " + CinemachineImpulseBinding.AheadPerRecord, "blend override 0", "pending 0", "preview time 0" }));
        }

        // ---- Save During Play — on, a play's exit keeps no Feedback value: no 'save changes' dialog (the batch cancels it and logs
        // a line that passes no log callback — counted in the log file, which takes it a little later) and the edit-mode lens and
        // gain are their first values · the control without our hook gets the dialog · a play that changes nothing gets none · the hook
        // once in play. Three play entries — each reloads the domain: the rounds keep their state in SessionState

        static int DialogLines()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-logFile");
            string path = at >= 0 && at + 1 < args.Length ? args[at + 1] : Application.consoleLogPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return -1;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string text = reader.ReadToEnd();
            int n = 0;
            for (int i = text.IndexOf(DialogLine, StringComparison.Ordinal); i >= 0; i = text.IndexOf(DialogLine, i + 1, StringComparison.Ordinal))
                n++;
            return n;
        }

        // the log file takes the batch's dialog line a little later — up to the given count or the time
        static IEnumerator Dialogs(int atLeast, double seconds)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (DialogLines() < atLeast && EditorApplication.timeSinceStartup < until)
                yield return null;
        }

        static void Mark(string key, string value) => SessionState.SetString("ci29." + key, value);

        static string Read(string key) => SessionState.GetString("ci29." + key, "");

        static CinemachineCamera SdpCamera() => GameObject.Find("CI29 vcam")?.GetComponent<CinemachineCamera>();

        static void PlayAndDrop(bool unhook)
        {
            if (unhook)
            {
                FieldInfo f = HotSave();
                Delegate mine = Delegate.CreateDelegate(f.FieldType, typeof(CinemachineSaved).GetMethod("PutBack", BindingFlags.Static | BindingFlags.NonPublic));
                f.SetValue(null, Delegate.Remove((Delegate)f.GetValue(null), mine));
            }
            Mark("hooks", Hooks().ToString());
            Time.captureDeltaTime = 1f / 60f;
            GameObject.Find("CI29 player").GetComponent<FeedbackPlayer>().Play();
        }

        static void After(string round)
        {
            CinemachineCamera v = SdpCamera();
            var perlin = v.GetComponent<CinemachineBasicMultiChannelPerlin>();
            Mark(round, $"dialogs {DialogLines()} · lens {v.Lens.FieldOfView} · gain {perlin.AmplitudeGain} · hooks in play {Read("hooks")}");
        }

        [UnityTest]
        public IEnumerator CI29a_SaveDuringPlayKeepsNoFeedbackValue()
        {
            Mark("prev", EditorPrefs.GetBool(SdpKey, false) ? "1" : "0");
            EditorPrefs.SetBool(SdpKey, true);
            var vgo = new GameObject("CI29 vcam");
            var vcam = vgo.AddComponent<CinemachineCamera>();
            vcam.Lens.FieldOfView = 40f;
            var perlin = vgo.AddComponent<CinemachineBasicMultiChannelPerlin>();
            perlin.AmplitudeGain = 0.5f;
            var pgo = new GameObject("CI29 player");
            pgo.AddComponent<FeedbackPlayer>().m_Items.AddRange(new FeedbackItem[]
            {
                With(new CinemachineLensFieldOfViewItem(), TweenTo(60f), 0.1f, TargetRefMode.Direct, vcam),
                With(new CinemachineNoiseAmplitudeGainItem(), TweenTo(2f), 0.1f, TargetRefMode.Direct, perlin),
            });
            Mark("start", DialogLines().ToString());
            yield return new EnterPlayMode();
            PlayAndDrop(false);
            int end = Time.frameCount + 20;
            while (Time.frameCount < end)
                yield return null;
            Mark("inplay", $"lens {SdpCamera().Lens.FieldOfView}");
            yield return new ExitPlayMode();
            yield return null;
            After("ours");
            if (!Application.isBatchMode)
            {
                EditorPrefs.SetBool(SdpKey, Read("prev") == "1");
                Object.DestroyImmediate(GameObject.Find("CI29 vcam"));
                Object.DestroyImmediate(GameObject.Find("CI29 player"));
                Assert.That(Read("inplay"), Is.EqualTo("lens 60"), "the play moved the lens");
                Assert.That(Read("ours"), Does.EndWith(" · lens 40 · gain 0.5 · hooks in play 1"), "the first values · the hook once");
                yield break;
            }
            yield return new EnterPlayMode();
            PlayAndDrop(true);
            for (int i = 0; i < 20; i++)
                yield return null;
            yield return new ExitPlayMode();
            yield return Dialogs(int.Parse(Read("start")) + 1, 10);
            After("control");
            typeof(CinemachineSaved).GetMethod("Hook", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            yield return new EnterPlayMode();
            Mark("hooks", Hooks().ToString());
            yield return null;
            yield return new ExitPlayMode();
            yield return Dialogs(int.Parse(Read("start")) + 2, 3);
            After("still");
            EditorPrefs.SetBool(SdpKey, Read("prev") == "1");
            Object.DestroyImmediate(GameObject.Find("CI29 vcam"));
            Object.DestroyImmediate(GameObject.Find("CI29 player"));
            int start = int.Parse(Read("start"));
            string ours = Read("ours"), control = Read("control"), still = Read("still");
            Debug.Log($"CI29a save during play · start {start} · in play {Read("inplay")} | ours {ours} | control {control} | still {still}");
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "the log file read");
            Assert.That(Read("inplay"), Is.EqualTo("lens 60"), "the play moved the lens");
            Assert.That(ours, Does.EndWith(" · lens 40 · gain 0.5 · hooks in play 1"), "the first values · the hook once");
            Assert.That(control, Does.StartWith($"dialogs {start + 1} "), "without the hook the dialog — the control");
            Assert.That(still, Does.StartWith($"dialogs {start + 1} "), "ours and a play that changes nothing — none (the control's only, 3 s after the last exit)");
            Assert.That(still, Does.EndWith("hooks in play 1"), "the hook once again after the reloads");
        }
    }
}
