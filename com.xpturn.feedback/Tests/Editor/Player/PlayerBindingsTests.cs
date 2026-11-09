using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // EditMode — the registry, the resolution, the refusals, the main camera, the targets, the per-play cells, the ends, the
    // warning. A player is bound to a test scheduler that the row ticks directly. A row checks everything and judges at the
    // end. Warnings and errors are counted by a log listener; a logged exception is expected with LogAssert. The rows that
    // read Camera.main turn the open scene's main camera off and use their own. The material color row is the development
    // project's (URP — the DevProject category)
    public sealed class PlayerBindingsTests
    {
        const string PrefabPath = "Assets/PlayerBindingsTests.prefab";
        static readonly List<string> s_Warnings = new List<string>();
        static readonly List<string> s_Errors = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<Camera> m_Silenced = new List<Camera>();
        readonly List<string> m_Fails = new List<string>();
        static readonly int s_BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int s_Color = Shader.PropertyToID("_Color");

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Warning)
                s_Warnings.Add(condition);
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                s_Errors.Add(type + ": " + condition);
        }

        [SetUp]
        public void SetUp()
        {
            s_Warnings.Clear();
            s_Errors.Clear();
            m_Fails.Clear();
            PlayerRuntime.ResetSession();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            Clean();
            AssetDatabase.DeleteAsset(PrefabPath);
        }

        void Clean()
        {
            foreach (FeedbackPlayer player in m_Players)
            {
                if (player != null)
                    player.ReleaseRuntime();
            }
            m_Players.Clear();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            foreach (Camera camera in m_Silenced)
            {
                if (camera != null)
                    camera.enabled = true;
            }
            m_Silenced.Clear();
            foreach (FeedbackScheduler scheduler in m_Schedulers)
            {
                if (!scheduler.IsShutdown)
                    scheduler.Shutdown();
            }
            m_Schedulers.Clear();
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fails.Add(what);
        }

        void Done()
        {
            if (m_Fails.Count > 0)
                Assert.Fail(string.Join(" · ", m_Fails));
        }

        static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
        static string F(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";
        static bool Bits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        static bool Bits(Vector3 a, Vector3 b) => Bits(a.x, b.x) && Bits(a.y, b.y) && Bits(a.z, b.z);
        static bool Bits(Quaternion a, Quaternion b) => Bits(a.x, b.x) && Bits(a.y, b.y) && Bits(a.z, b.z) && Bits(a.w, b.w);
        static bool Near(Vector3 a, Vector3 b, float e = 1e-4f) => (a - b).magnitude <= e;

        // ---- fixtures

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        GameObject Go(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            if (parent != null)
                go.transform.SetParent(parent, false);
            return go;
        }

        FeedbackPlayer PlayerOn(GameObject owner, FeedbackScheduler s, params FeedbackItem[] items)
        {
            var player = owner.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Items.AddRange(items);
            player.Bind(s);
            return player;
        }

        void SilenceSceneCameras()
        {
            foreach (Camera other in Camera.allCameras)
            {
                if (!other.CompareTag("MainCamera"))
                    continue;
                other.enabled = false;
                m_Silenced.Add(other);
            }
        }

        GameObject MainCam(string name)
        {
            SilenceSceneCameras();
            GameObject camera = Go(name);
            camera.tag = "MainCamera";
            camera.AddComponent<Camera>();
            return camera;
        }

        static TargetRef Ref(TargetRefMode mode) => new TargetRef { m_Mode = mode, m_Path = "", m_Anchor = "" };
        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };
        static TargetRef ChildPath(string path) => new TargetRef { m_Mode = TargetRefMode.Child, m_ChildLookup = ChildLookup.Path, m_Path = path, m_Anchor = "" };
        static TargetRef ChildIndex(int i) => new TargetRef { m_Mode = TargetRefMode.Child, m_ChildLookup = ChildLookup.Index, m_Index = i, m_Path = "", m_Anchor = "" };
        static TargetRef ChildFirst() => new TargetRef { m_Mode = TargetRefMode.Child, m_ChildLookup = ChildLookup.FirstOfType, m_Path = "", m_Anchor = "" };
        static TargetRef AnchorRef(string name) => new TargetRef { m_Mode = TargetRefMode.Anchor, m_Path = "", m_Anchor = name };

        static T Item<T>(FeedbackMotion motion, TargetRef target, float duration = 1f) where T : TargetItem, new()
        {
            var item = new T { m_Motion = motion, m_Target = target };
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static PunchMotion Punch(Vector4 a) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = a, m_Vibrato = 5f, m_Decay = 1f } };
        static ShakeMotion Shake(Vector4 a) => new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = a, m_Frequency = 10f } };
        static OscillateMotion Oscillate(Vector4 a, float phase = 0f) => new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = a, m_Frequency = 1f, m_Phase = phase } };
        static CurveMotion Curve(Vector4 one) => new CurveMotion { m_Params = new CurveParams { m_Curve = AnimationCurve.Linear(0f, 0f, 1f, 1f), m_RemapOne = one } };
        static TweenMotion Tween(TweenMode mode, Vector4 value, EaseType ease = EaseType.Linear)
            => new TweenMotion { m_Params = new TweenParams { m_Mode = mode, m_From = value, m_To = value, m_Ease = Ease.Of(ease), m_Loops = 1 } };
        static SpringMotion Spring(SpringMode mode, Vector4 v) => new SpringMotion { m_Params = new SpringParams { m_Mode = mode, m_Value = v, m_Frequency = 3f, m_Damping = 0.6f, m_Settle = 0.001f } };
        static FlashMotion Flash(Vector4 target) => new FlashMotion { m_Params = new FlashParams { m_Target = target, m_Attack = 0.25f, m_Hold = 0.5f, m_Release = 0.25f } };
        static BlinkMotion Blink(Vector4 v) => new BlinkMotion { m_Params = new BlinkParams { m_Value = v, m_Period = 0.2f, m_Duty = 0.5f, m_StartOn = true } };
        static SetMotion Set(Vector4 v, bool relative = false) => new SetMotion { m_Params = new SetParams { m_Value = v, m_Relative = relative } };

        static void Tick(FeedbackScheduler s, int n = 1, float dt = 0.02f)
        {
            for (int i = 0; i < n; i++)
                s.Tick(dt);
        }

        static Object SlotTarget(ItemBinding b)
        {
            switch (b)
            {
                case PositionBinding p: return p.Slot?.Key.Target;
                case RotationBinding r: return r.Slot?.Key.Target;
                case ScaleBinding s: return s.Slot?.Key.Target;
                case LightIntensityBinding l: return l.Slot?.Key.Target;
                case MaterialColorBinding m: return m.Slot?.Key.Target;
                case SetActiveBinding a: return a.Slot?.Key.Target;
                default: return null;
            }
        }

        static List<string> Skipped() => s_Warnings.Where(w => w.StartsWith(PlayerRuntime.SkippedWarning, StringComparison.Ordinal)).ToList();

        // one item on the owner: the target found (expected) or the item skipped with its reason
        void Expect(GameObject owner, FeedbackScheduler s, FeedbackItem item, Object expected, string reason, string label, Action<FeedbackPlayer> setup = null)
        {
            var player = owner.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.Bind(s);
            setup?.Invoke(player);
            player.m_Items.Add(item);
            s_Warnings.Clear();
            s_Errors.Clear();
            bool ok = player.EnsureRuntime();
            ItemBinding b = ok ? player.Runtime.Binding(0) : null;
            List<string> skips = Skipped();
            if (expected != null)
            {
                Object got = SlotTarget(b);
                Check(b != null && got == expected, label + " → " + (got == null ? "none" : got.name) + " (want " + expected.name + ")");
                Check(skips.Count == 0, label + " warned");
            }
            else
            {
                Check(b == null, label + " prepared");
                Check(skips.Count == 1 && skips[0].EndsWith(": " + reason, StringComparison.Ordinal), label + " warning '" + (skips.FirstOrDefault() ?? "none") + "'");
                Check(ok && player.Runtime.ItemCount == 1, label + " lost its place");
            }
            Check(s_Errors.Count == 0, label + " errors " + s_Errors.Count);
            player.ReleaseRuntime();
            Object.DestroyImmediate(player);
        }

        // ---- the registry

        [Test]
        public void NE01_Registry()
        {
            // the camera shake and the hit stop too — nine
            var nine = new[] { typeof(PositionItem), typeof(RotationItem), typeof(ScaleItem), typeof(MaterialColorItem), typeof(LightIntensityItem), typeof(SetActiveItem), typeof(UnityEventItem), typeof(CameraShakeItem), typeof(HitStopItem) };
            IReadOnlyCollection<Type> registered = FeedbackImplementations.RegisteredTypes;
            foreach (Type t in nine)
                Check(registered.Contains(t), t.Name + " not registered");
            Check(FeedbackImplementations.Create(new PositionItem()) is PositionBinding, "Position");
            Check(FeedbackImplementations.Create(new RotationItem()) is RotationBinding, "Rotation");
            Check(FeedbackImplementations.Create(new ScaleItem()) is ScaleBinding, "Scale");
            Check(FeedbackImplementations.Create(new MaterialColorItem()) is MaterialColorBinding, "MaterialColor");
            Check(FeedbackImplementations.Create(new LightIntensityItem()) is LightIntensityBinding, "LightIntensity");
            Check(FeedbackImplementations.Create(new SetActiveItem()) is SetActiveBinding, "SetActive");
            Check(FeedbackImplementations.Create(new UnityEventItem()) is UnityEventBinding, "UnityEvent");
            Check(FeedbackImplementations.Create(new CameraShakeItem()) is CameraShakeBinding, "CameraShake");
            Check(FeedbackImplementations.Create(new HitStopItem()) is HitStopBinding, "HitStop");
            PlayerRuntime.ResetSession();
            s_Warnings.Clear();
            FeedbackPlayer p = PlayerOn(Go("nine"), Sched(), new PositionItem(), new RotationItem(), new ScaleItem(), new MaterialColorItem(), new LightIntensityItem(),
                new SetActiveItem(), new UnityEventItem(), new CameraShakeItem(), new HitStopItem());
            p.EnsureRuntime();
            int unregistered = s_Warnings.Count(w => w.StartsWith(PlayerRuntime.UnregisteredWarning, StringComparison.Ordinal));
            Check(unregistered == 0, "unregistered warnings " + unregistered);
            Done();
        }

        // ---- the resolution

        [Test]
        public void NE02_Reg10_Reg25_Resolution()
        {
            FeedbackScheduler s = Sched();
            GameObject parent = Go("parent");
            Light parentLight = parent.AddComponent<Light>();
            GameObject root = Go("root", parent.transform);
            GameObject a = Go("a", root.transform);
            Light aLight = a.AddComponent<Light>();
            GameObject b = Go("b", a.transform);
            b.AddComponent<Light>();
            GameObject c = Go("c", root.transform);
            c.SetActive(false);
            GameObject d = Go("d", root.transform);
            d.AddComponent<Light>();
            var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
            m_Made.Add(material);
            Action<FeedbackPlayer> anchor = p => p.SetAnchor("ref", d);
            FeedbackItem Pos(TargetRef t) => Item<PositionItem>(Punch(Vector4.one), t);
            Expect(root, s, Pos(Direct(a.transform)), a.transform, null, "Direct Transform");
            Expect(root, s, Pos(Direct(a)), a.transform, null, "Direct GameObject");
            Expect(root, s, Pos(Direct(aLight)), a.transform, null, "Direct Light");
            Expect(root, s, Pos(Direct(material)), null, "the target has no such component", "Direct material");
            Expect(root, s, Pos(Direct(null)), null, "no target object", "Direct null");
            Expect(root, s, Pos(Ref(TargetRefMode.Self)), root.transform, null, "Self");
            Expect(root, s, Pos(Ref(TargetRefMode.Parent)), parent.transform, null, "Parent");
            Expect(root, s, Pos(ChildPath("a")), a.transform, null, "path a");
            Expect(root, s, Pos(ChildPath("a/b")), b.transform, null, "path a/b");
            Expect(root, s, Pos(ChildPath("")), null, "no such child", "path empty");
            Expect(root, s, Pos(ChildPath("x")), null, "no such child", "path x");
            Expect(root, s, Pos(ChildPath("c")), c.transform, null, "path c inactive");
            Expect(root, s, Pos(ChildIndex(0)), a.transform, null, "index 0");
            Expect(root, s, Pos(ChildIndex(1)), c.transform, null, "index 1 inactive");
            Expect(root, s, Pos(ChildIndex(3)), null, "child index out of range", "index 3");
            Expect(root, s, Pos(ChildIndex(-1)), null, "child index out of range", "index -1");
            Expect(root, s, Pos(ChildFirst()), a.transform, null, "first of type");
            Expect(root, s, Pos(AnchorRef("ref")), d.transform, null, "anchor", anchor);
            Expect(root, s, Pos(AnchorRef("missing")), null, "no anchor of that name", "anchor missing", anchor);
            Expect(root, s, Pos(Ref(TargetRefMode.Channel)), null, "channel targets need a receiver", "channel");
            Expect(Go("loner"), s, Pos(Ref(TargetRefMode.Parent)), null, "no parent", "no parent");
            FeedbackItem L(TargetRef t) => Item<LightIntensityItem>(Punch(Vector4.one), t);
            Expect(root, s, L(Ref(TargetRefMode.Self)), null, "the target has no such component", "light Self");
            Expect(root, s, L(Ref(TargetRefMode.Parent)), parentLight, null, "light Parent");
            Expect(root, s, L(ChildFirst()), aLight, null, "light first");
            Expect(root, s, L(ChildPath("c")), null, "the target has no such component", "light path c");
            Light cLight = c.AddComponent<Light>();
            Object.DestroyImmediate(a.GetComponent<Light>());
            Object.DestroyImmediate(b.GetComponent<Light>());
            Expect(root, s, L(ChildFirst()), cLight, null, "light first inactive");
            FeedbackItem A(TargetRef t) => Item<SetActiveItem>(Set(Vector4.zero), t, 0f);
            Expect(root, s, A(ChildPath("c")), c, null, "SetActive path c");
            Expect(root, s, A(Direct(d.GetComponent<Light>())), d, null, "SetActive Direct Light");
            Expect(root, s, A(ChildFirst()), a, null, "SetActive first");
            Expect(root, s, A(Ref(TargetRefMode.Self)), root, null, "SetActive Self");
            // MainCamera — found, and missing (a SetActive item: a value target waits for its camera)
            SilenceSceneCameras();
            Expect(Go("nocam"), s, A(Ref(TargetRefMode.MainCamera)), null, "no main camera", "main camera missing");
            GameObject cam = MainCam("cam");
            Expect(Go("cam-owner"), s, Pos(Ref(TargetRefMode.MainCamera)), cam.transform, null, "main camera");
            // a prefab asset — an object outside a scene — is no target, and stays as it was
            var source = new GameObject("assetTarget");
            source.AddComponent<Light>().intensity = 0.5f;
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(source, PrefabPath);
            Object.DestroyImmediate(source);
            const string reason = "the target is not in a scene (an asset)";
            Expect(Go("o1"), s, Item<PositionItem>(Tween(TweenMode.To, new Vector4(5f, 0f, 0f, 0f)), Direct(asset), 0.1f), null, reason, "asset GameObject");
            Expect(Go("o2"), s, Item<LightIntensityItem>(Flash(Vector4.one), Direct(asset.GetComponent<Light>()), 0.1f), null, reason, "asset Light");
            Expect(Go("o3"), s, Item<PositionItem>(Punch(Vector4.one), AnchorRef("a"), 0.1f), null, reason, "asset anchor", p => p.SetAnchor("a", asset));
            Expect(Go("o4"), s, Item<SetActiveItem>(Set(Vector4.zero), Direct(asset), 0f), null, reason, "asset SetActive");
            Check(asset.transform.localPosition == Vector3.zero && asset.activeSelf, "the asset changed");
            // an inactive grandchild under an active child: the first of type finds it (the engine looks at an inactive
            // object itself)
            GameObject root2 = Go("root2");
            GameObject e = Go("e", root2.transform);
            GameObject f = Go("f", e.transform);
            Light fLight = f.AddComponent<Light>();
            f.SetActive(false);
            Expect(root2, s, Item<LightIntensityItem>(Punch(Vector4.one), ChildFirst()), fLight, null, "light first — an inactive grandchild");
            Done();
        }

        // ---- refused motions

        sealed class UnknownMotion : FeedbackMotion
        {
            internal override MotionKind Kind => MotionKind.Tween;
        }

        [Test]
        public void NE03_RefusedMotions()
        {
            FeedbackScheduler s = Sched();
            GameObject o = Go("o");
            Expect(o, s, Item<PositionItem>(null, Ref(TargetRefMode.Self)), null, "no motion", "null motion");
            Expect(o, s, Item<PositionItem>(Flash(Vector4.one), Ref(TargetRefMode.Self)), null, "the target does not take Flash", "Flash on a position");
            Expect(o, s, Item<PositionItem>(new UnknownMotion(), Ref(TargetRefMode.Self)), null, "an unknown motion", "unknown motion");
            Done();
        }

        // ---- the main camera

        [Test]
        public void NE04_MainCamera()
        {
            {
                FeedbackScheduler s = Sched();
                GameObject camA = MainCam("camA");
                FeedbackPlayer p = PlayerOn(Go("owner"), s, Item<PositionItem>(Oscillate(new Vector4(1f, 0f, 0f, 0f), 0.25f), Ref(TargetRefMode.MainCamera), 0.5f));
                p.EnsureRuntime();
                var binding = (PositionBinding)p.Runtime.Binding(0);
                p.Play();
                Tick(s, 3);
                Check(camA.transform.localPosition != Vector3.zero, "the first play does not move camA");
                // switched while the play runs: it ends on camA
                camA.tag = "Untagged";
                GameObject camB = Go("camB");
                camB.tag = "MainCamera";
                camB.AddComponent<Camera>();
                Tick(s, 3);
                Check(camA.transform.localPosition != Vector3.zero && camB.transform.localPosition == Vector3.zero, "the running play left camA");
                Tick(s, 30);
                s.RunRevert();
                p.Play();
                Tick(s, 3);
                Check(camB.transform.localPosition != Vector3.zero, "the next play does not move camB");
                Check(binding.Slot != null && binding.Slot.Key.Target == camB.transform, "the slot's key is not camB");
                LayerSlot slotB = binding.Slot;
                Tick(s, 30);
                s.RunRevert();
                Check(Bits(camA.transform.localPosition, Vector3.zero), "camA not back to its base");
                p.Play();
                Tick(s, 3);
                Check(ReferenceEquals(binding.Slot, slotB), "the play after took another slot");
                Tick(s, 30);
                // gone, none tagged: the play stands, the item ends
                Object.DestroyImmediate(camB);
                s_Errors.Clear();
                PlaybackHandle h = p.Play();
                Tick(s, 3);
                Check(h != default && s_Errors.Count == 0, "no camera — handle " + (h != default) + " · errors " + s_Errors.Count);
                Check(!p.Runtime.Record(0).Running[0], "no camera — the item still runs");
                Tick(s, 30);
                camA.tag = "MainCamera";
                Vector3 before = camA.transform.localPosition;
                p.Play();
                Tick(s, 3);
                Check(camA.transform.localPosition != before, "camA tagged again — not moved");
                Clean();
            }
            // Overlap: a play that runs keeps the new play on the old camera
            {
                FeedbackScheduler s = Sched();
                GameObject camA = MainCam("camA");
                FeedbackPlayer p = PlayerOn(Go("owner"), s, Item<PositionItem>(Oscillate(new Vector4(1f, 0f, 0f, 0f), 0.25f), Ref(TargetRefMode.MainCamera), 1f));
                p.m_Policy = PlayPolicy.Overlap;
                p.m_MaxOverlap = 2;
                p.Play();
                Tick(s, 5);
                camA.tag = "Untagged";
                GameObject camB = Go("camB");
                camB.tag = "MainCamera";
                camB.AddComponent<Camera>();
                p.Play();
                Tick(s, 5);
                Check(camB.transform.localPosition == Vector3.zero, "Overlap — the new play moved camB while a play ran");
                Clean();
            }
            // Overlap: a Revert of the play that began on the old camera does not write on the new one; the old slot goes
            {
                FeedbackScheduler s = Sched();
                GameObject camA = MainCam("camA");
                GameObject other = Go("other");
                FeedbackPlayer p = PlayerOn(Go("owner"), s, Item<PositionItem>(Tween(TweenMode.To, new Vector4(5f, 0f, 0f, 0f)), Ref(TargetRefMode.MainCamera), 0.1f),
                    Item<PositionItem>(Oscillate(new Vector4(0.01f, 0f, 0f, 0f)), Direct(other), 1f));
                p.m_Policy = PlayPolicy.Overlap;
                p.m_MaxOverlap = 2;
                PlaybackHandle first = p.Play();
                Tick(s, 10);
                camA.tag = "Untagged";
                GameObject camB = Go("camB");
                camB.tag = "MainCamera";
                camB.AddComponent<Camera>();
                camB.transform.localPosition = new Vector3(7f, 7f, 7f);
                int slots = s.Context.Layers.SlotCount;
                p.Play();
                Tick(s, 1);
                Vector3 during = camB.transform.localPosition;
                p.Stop(first, StopMode.Revert);
                Check(camB.transform.localPosition == during, "the old play's Revert moved camB to " + F(camB.transform.localPosition));
                Check(s.Context.Layers.SlotCount == slots && !s.Context.Layers.TryFind(camA.transform, 0, 0, out LocalPositionSlot _), "the old camera's slot kept — slots " + s.Context.Layers.SlotCount + " (was " + slots + ")");
                Clean();
            }
            // no main camera at the prepare: the play after one appears moves it
            {
                FeedbackScheduler s = Sched();
                SilenceSceneCameras();
                FeedbackPlayer p = PlayerOn(Go("owner"), s, Item<PositionItem>(Oscillate(new Vector4(1f, 0f, 0f, 0f), 0.25f), Ref(TargetRefMode.MainCamera), 0.5f));
                p.EnsureRuntime();
                p.Play();
                Tick(s, 30);
                GameObject cam = Go("spawned");
                cam.tag = "MainCamera";
                cam.AddComponent<Camera>();
                p.Play();
                Tick(s, 3);
                Check(cam.transform.localPosition != Vector3.zero, "the camera made after the prepare is not moved");
            }
            Done();
        }

        // ---- a target destroyed during the play

        [Test]
        public void NE05_TargetDestroyed()
        {
            {
                FeedbackScheduler s = Sched();
                GameObject root = Go("root");
                GameObject a = Go("a", root.transform);
                GameObject d = Go("d", root.transform);
                FeedbackPlayer p = PlayerOn(root, s,
                    Item<PositionItem>(Oscillate(new Vector4(1f, 0f, 0f, 0f)), ChildPath("a"), 1f),
                    Item<PositionItem>(Tween(TweenMode.To, new Vector4(3f, 0f, 0f, 0f)), ChildPath("a"), 1f),
                    Item<PositionItem>(Oscillate(new Vector4(0f, 1f, 0f, 0f), 0.25f), ChildPath("d"), 1f));
                p.Play();
                Tick(s, 5);
                s_Errors.Clear();
                Object.DestroyImmediate(a);
                Tick(s, 5);
                PlaybackRecord r = p.Runtime.Record(0);
                Check(s_Errors.Count == 0, "errors " + s_Errors.Count);
                Check(!r.Running[0] && !r.Running[1] && r.Running[2], "running " + r.Running[0] + " " + r.Running[1] + " " + r.Running[2]);
                Check(d.transform.localPosition != Vector3.zero, "d does not move");
                Tick(s, 46);
                Check(!p.IsPlaying, "the play did not end on time");
            }
            // Stop(Complete) right after the destruction: Persist's door is behind IsAlive (Complete evaluates unchecked)
            {
                FeedbackScheduler s = Sched();
                GameObject root = Go("root");
                GameObject a = Go("a", root.transform);
                FeedbackPlayer p = PlayerOn(root, s, Item<PositionItem>(Tween(TweenMode.To, new Vector4(3f, 0f, 0f, 0f)), ChildPath("a"), 1f));
                PlaybackHandle h = p.Play();
                Tick(s, 5);
                s_Errors.Clear();
                Object.DestroyImmediate(a);
                p.Stop(h, StopMode.Complete);
                Check(s_Errors.Count == 0, "Stop(Complete) right after the destruction — errors " + s_Errors.Count);
            }
            Done();
        }

        // ---- position

        [Test]
        public void NE06_Position()
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("t");
            t.transform.localPosition = new Vector3(1f, 2f, 3f);
            FeedbackPlayer p = PlayerOn(t, s, Item<PositionItem>(Punch(new Vector4(1f, 0f, 0f, 0f)), Ref(TargetRefMode.Self), 0.5f));
            p.Play();
            Tick(s, 3);
            Check(t.transform.localPosition != new Vector3(1f, 2f, 3f), "Local Punch does not move");
            Tick(s, 30);
            s.RunRevert();
            Check(Bits(t.transform.localPosition, new Vector3(1f, 2f, 3f)), "Local Punch end " + F(t.transform.localPosition));
            // a World offset under a turned, scaled parent: the world displacement is the motion's value
            GameObject parent = Go("parent");
            parent.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            parent.transform.localScale = new Vector3(2f, 2f, 2f);
            GameObject w = Go("w", parent.transform);
            w.transform.localPosition = new Vector3(1f, 0f, 0f);
            Vector3 worldBase = w.transform.position;
            var world = Item<PositionItem>(Oscillate(new Vector4(0f, 1f, 0f, 0f)), Direct(w.transform), 1f);
            world.m_Space = PositionSpace.World;
            FeedbackPlayer pw = PlayerOn(Go("pw"), s, world);
            pw.Play();
            Tick(s, 12);
            var probe = new OscillateEvaluator(new OscillateParams { m_Amplitude = new Vector4(0f, 1f, 0f, 0f), m_Frequency = 1f }, 3);
            MotionState st = default;
            probe.Begin(ref st, Vector4.zero, new MotionBegin { Intensity = 1f });
            probe.Evaluate(st, new MotionTime(0.24f, 0.24), out MotionSample expect);
            Check(Near(w.transform.position - worldBase, (Vector3)expect.Value), "World offset — displacement " + F(w.transform.position - worldBase) + " want " + F((Vector3)expect.Value));
            Tick(s, 60);
            s.RunRevert();
            Check(Bits(w.transform.localPosition, new Vector3(1f, 0f, 0f)), "World offset end " + F(w.transform.localPosition));
            // Persist To — local and world
            GameObject l = Go("l");
            l.transform.localPosition = new Vector3(1f, 0f, 0f);
            FeedbackPlayer pl = PlayerOn(l, s, Item<PositionItem>(Tween(TweenMode.To, new Vector4(5f, 0f, 0f, 0f)), Ref(TargetRefMode.Self), 0.2f));
            pl.Play();
            Tick(s, 5);
            Check(Mathf.Abs(l.transform.localPosition.x - 3f) < 1e-4f, "Local Persist at the half " + F(l.transform.localPosition));
            pl.Stop(StopMode.Revert);
            Check(Bits(l.transform.localPosition, new Vector3(1f, 0f, 0f)), "Local Persist Revert " + F(l.transform.localPosition));
            GameObject pw2 = Go("pw2", parent.transform);
            var persist = Item<PositionItem>(Tween(TweenMode.To, new Vector4(10f, 0f, 0f, 0f)), Direct(pw2.transform), 0.2f);
            persist.m_Space = PositionSpace.World;
            FeedbackPlayer pp = PlayerOn(Go("pp"), s, persist);
            pp.Play();
            Tick(s, 15);
            Check(Near(pw2.transform.position, new Vector3(10f, 0f, 0f)) && Near(pw2.transform.localPosition, new Vector3(0f, -5f, 0f)), "World Persist — world " + F(pw2.transform.position) + " local " + F(pw2.transform.localPosition));
            Done();
        }

        // ---- rotation

        static Quaternion N(Quaternion q, GameObject once)
        {
            once.transform.localRotation = q;
            return once.transform.localRotation;
        }

        Quaternion RotationAt(FeedbackScheduler s, Quaternion start, FeedbackMotion motion, RotationInterpolation interpolation, float duration, float at, out float farthest)
        {
            GameObject go = Go("r");
            go.transform.localRotation = start;
            var item = Item<RotationItem>(motion, Ref(TargetRefMode.Self), duration);
            item.m_Interpolation = interpolation;
            FeedbackPlayer p = PlayerOn(go, s, item);
            p.Play();
            farthest = 0f;
            int steps = Mathf.RoundToInt(at / 0.02f);
            for (int i = 0; i < steps; i++)
            {
                Tick(s, 1);
                farthest = Mathf.Max(farthest, Quaternion.Angle(go.transform.localRotation, start));
            }
            Quaternion q = go.transform.localRotation;
            p.Stop(StopMode.Revert);
            return q;
        }

        [Test]
        public void NE07_Rotation()
        {
            FeedbackScheduler s = Sched();
            GameObject once = Go("once");
            GameObject r = Go("r0");
            r.transform.localRotation = Quaternion.Euler(13f, 47f, -71f);
            Quaternion q0 = r.transform.localRotation;
            FeedbackPlayer p = PlayerOn(r, s, Item<RotationItem>(Shake(new Vector4(30f, 30f, 30f, 0f)), Ref(TargetRefMode.Self), 0.3f));
            p.Play();
            Tick(s, 5);
            Check(Quaternion.Angle(r.transform.localRotation, q0) > 0.1f, "Local Shake does not turn");
            Tick(s, 20);
            s.RunRevert();
            Check(Bits(r.transform.localRotation, N(q0, once)), "Local Shake end is not N(q0)");
            // 350° → 10°: Euler (the start unwrapped — 0°) · Shortest 0°
            Quaternion a350 = Quaternion.Euler(0f, 350f, 0f);
            var to10 = new Vector4(0f, 10f, 0f, 0f);
            Quaternion euler = RotationAt(s, a350, Tween(TweenMode.To, to10), RotationInterpolation.Euler, 1f, 0.5f, out _);
            Quaternion shortest = RotationAt(s, a350, Tween(TweenMode.To, to10), RotationInterpolation.Shortest, 1f, 0.5f, out _);
            Check(Quaternion.Angle(euler, Quaternion.identity) < 0.5f, "Euler 350 → 10 at the half " + F(euler.eulerAngles));
            Check(Quaternion.Angle(shortest, Quaternion.identity) < 0.5f, "Shortest 350 → 10 at the half " + F(shortest.eulerAngles));
            // Shortest keeps an overshoot (BackOut To 90°)
            RotationAt(s, Quaternion.identity, Tween(TweenMode.To, new Vector4(0f, 90f, 0f, 0f), EaseType.BackOut), RotationInterpolation.Shortest, 1f, 1f, out float far);
            Check(far > 92f, "Shortest BackOut does not pass 90° — " + F(far));
            // World Persist under a turned parent: the world rotation is the target
            GameObject parent = Go("parent");
            parent.transform.localRotation = Quaternion.Euler(0f, 0f, 37f);
            GameObject rw = Go("rw", parent.transform);
            var world = Item<RotationItem>(Tween(TweenMode.To, new Vector4(10f, 20f, 30f, 0f)), Direct(rw.transform), 0.2f);
            world.m_Space = TransformSpace.World;
            FeedbackPlayer pw = PlayerOn(Go("pw"), s, world);
            pw.Play();
            Tick(s, 15);
            Check(Quaternion.Angle(rw.transform.rotation, Quaternion.Euler(10f, 20f, 30f)) < 0.01f, "World Persist " + F(rw.transform.rotation.eulerAngles));
            // a negative target does not spin the axis
            Quaternion start = Quaternion.Euler(-30f, 0f, 45f);
            RotationAt(s, start, Tween(TweenMode.To, new Vector4(-30f, 90f, 45f, 0f)), RotationInterpolation.Euler, 1f, 1f, out float negative);
            Check(negative < 95f, "To a negative angle — " + F(negative) + "° from the start");
            // Spring MoveTo 350° → 10° · Tween From −30° to −10°
            RotationAt(s, Quaternion.Euler(0f, 350f, 0f), Spring(SpringMode.MoveTo, new Vector4(0f, 10f, 0f, 0f)), RotationInterpolation.Euler, 2f, 2f, out float spring);
            Check(spring < 30f, "Spring MoveTo 350 → 10 — " + F(spring) + "°");
            var from = new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.From, m_From = new Vector4(0f, -30f, 0f, 0f), m_Ease = Ease.Of(EaseType.Linear), m_Loops = 1 } };
            RotationAt(s, Quaternion.Euler(0f, -10f, 0f), from, RotationInterpolation.Euler, 1f, 1f, out float fromFar);
            Check(fromFar < 25f, "From −30 to −10 — " + F(fromFar) + "°");
            // By 360°: Euler turns round · Shortest stays (what tells the two apart)
            Quaternion eulerBy = RotationAt(s, Quaternion.identity, Tween(TweenMode.By, new Vector4(0f, 360f, 0f, 0f)), RotationInterpolation.Euler, 1f, 0.5f, out _);
            Quaternion shortBy = RotationAt(s, Quaternion.identity, Tween(TweenMode.By, new Vector4(0f, 360f, 0f, 0f)), RotationInterpolation.Shortest, 1f, 0.5f, out _);
            Check(Quaternion.Angle(eulerBy, Quaternion.identity) > 170f, "Euler By 360 at the half " + F(eulerBy.eulerAngles));
            Check(Quaternion.Angle(shortBy, Quaternion.identity) < 1f, "Shortest By 360 at the half " + F(shortBy.eulerAngles));
            // an upside-down start: Shortest keeps to the authored path
            Quaternion upside = Quaternion.Euler(180f, 0f, 0f);
            Quaternion mid = RotationAt(s, upside, Tween(TweenMode.To, new Vector4(180f, 90f, 0f, 0f)), RotationInterpolation.Shortest, 1f, 0.5f, out _);
            Check(Quaternion.Angle(mid, Quaternion.Euler(180f, 45f, 0f)) < 1f, "Shortest from upside down — " + F(Quaternion.Angle(mid, Quaternion.Euler(180f, 45f, 0f))) + "°");
            Done();
        }

        // ---- scale

        [Test]
        public void NE08_Scale()
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("t");
            t.transform.localScale = new Vector3(2f, 2f, 2f);
            FeedbackPlayer p = PlayerOn(t, s, Item<ScaleItem>(Curve(new Vector4(0.5f, 0.5f, 0.5f, 0f)), Ref(TargetRefMode.Self), 1f));
            p.Play();
            Tick(s, 25);
            Check(Near(t.transform.localScale, new Vector3(2.5f, 2.5f, 2.5f)), "scale at the half " + F(t.transform.localScale));
            Tick(s, 30);
            s.RunRevert();
            Check(Bits(t.transform.localScale, new Vector3(2f, 2f, 2f)), "scale end " + F(t.transform.localScale));
            Done();
        }

        // ---- light

        [Test]
        public void NE09_Light()
        {
            FeedbackScheduler s = Sched();
            GameObject lg = Go("light");
            Light light = lg.AddComponent<Light>();
            light.intensity = 0.2f;
            FeedbackPlayer p = PlayerOn(lg, s, Item<LightIntensityItem>(Shake(new Vector4(5f, 0f, 0f, 0f)), Ref(TargetRefMode.Self), 0.4f));
            p.Play();
            float min = float.MaxValue;
            for (int i = 0; i < 20; i++)
            {
                Tick(s, 1);
                min = Math.Min(min, light.intensity);
            }
            Tick(s, 5);
            s.RunRevert();
            Check(min == 0f, "Shake's smallest " + F(min));
            Check(Bits(light.intensity, 0.2f), "Shake end " + F(light.intensity));
            FeedbackPlayer f = PlayerOn(Go("flash"), s, Item<LightIntensityItem>(Flash(new Vector4(5f, 0f, 0f, 0f)), Direct(light), 1f));
            f.Play();
            Tick(s, 6);
            Check(light.intensity > 0.3f && light.intensity < 4.9f, "Flash in its attack " + F(light.intensity));
            Tick(s, 19);
            Check(light.intensity == 5f, "Flash hold " + F(light.intensity));
            Tick(s, 30);
            s.RunRevert();
            Check(Bits(light.intensity, 0.2f), "Flash end " + F(light.intensity));
            FeedbackPlayer t = PlayerOn(Go("tween"), s, Item<LightIntensityItem>(Tween(TweenMode.To, new Vector4(2f, 0f, 0f, 0f)), Direct(light), 0.2f));
            t.Play();
            Tick(s, 15);
            Check(light.intensity == 2f, "Persist To 2 " + F(light.intensity));
            Done();
        }

        // ---- material color — the development project's URP

        MeshRenderer Cube(string name, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            m_Made.Add(go);
            var r = go.GetComponent<MeshRenderer>();
            var mat = new Material(r.sharedMaterial) { name = name + "-m" };
            mat.SetColor(s_BaseColor, color);
            m_Made.Add(mat);
            r.sharedMaterial = mat;
            return r;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static WeakReference OwnMaterial(Renderer r)
        {
            var mat = new Material(Shader.Find("Unlit/Color")) { name = "bindings-own" };
            r.sharedMaterial = mat;
            return new WeakReference(mat);
        }

        static void Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void NE10_MaterialColor()
        {
            FeedbackScheduler s = Sched();
            var c0 = new Color(0.8f, 0.3f, 0.2f, 1f);
            MeshRenderer a = Cube("a", c0);
            Color shown = a.sharedMaterial.GetColor(s_BaseColor);
            FeedbackPlayer f = PlayerOn(a.gameObject, s, Item<MaterialColorItem>(Flash(Vector4.one), Ref(TargetRefMode.Self), 1f));
            f.EnsureRuntime();
            var fb = (MaterialColorBinding)f.Runtime.Binding(0);
            Check(fb != null && fb.PropertyId == s_BaseColor, "URP Lit's main color is not _BaseColor");
            var unlit = new Material(Shader.Find("Unlit/Color"));
            m_Made.Add(unlit);
            Check(MaterialColorBinding.MainColor(unlit.shader, out int u) && u == s_Color, "Unlit/Color's main color is not _Color");
            var err = new Material(Shader.Find("Hidden/InternalErrorShader"));
            m_Made.Add(err);
            Check(!MaterialColorBinding.MainColor(err.shader, out _), "the error shader has a main color");
            f.Play();
            Tick(s, 25);
            Check(a.HasPropertyBlock(), "MPB Flash — no block at the hold");
            Tick(s, 30);
            s.RunRevert();
            Check(!a.HasPropertyBlock() && Bits(a.sharedMaterial.GetColor(s_BaseColor).r, shown.r), "MPB Flash end — a block " + a.HasPropertyBlock());
            var persist = Item<MaterialColorItem>(Tween(TweenMode.To, new Vector4(0f, 0f, 1f, 1f)), Direct(a), 0.2f);
            FeedbackPlayer t = PlayerOn(Go("tween"), s, persist);
            t.Play();
            Tick(s, 15);
            Check(a.HasPropertyBlock(), "MPB Persist — no block left");
            MeshRenderer b = Cube("b", c0);
            Material original = b.sharedMaterial;
            var inst = Item<MaterialColorItem>(Oscillate(new Vector4(0.1f, 0.1f, 0.1f, 0f)), Ref(TargetRefMode.Self), 0.5f);
            inst.m_WriteMode = MaterialWriteMode.Instance;
            FeedbackPlayer pi = PlayerOn(b.gameObject, s, inst);
            pi.EnsureRuntime();
            Check(b.sharedMaterial != original && b.sharedMaterial.name.EndsWith(" (Feedback)", StringComparison.Ordinal), "Instance — no copy after the prepare");
            pi.ReleaseRuntime();
            Tick(s, 2);
            Check(b.sharedMaterial == original, "Instance — the original not back");
            var named = Item<MaterialColorItem>(Flash(Vector4.one), Ref(TargetRefMode.Self), 1f);
            named.m_PropertyName = "_NoSuchColor";
            Expect(Cube("c", c0).gameObject, s, named, null, "the material has no such color property", "missing property");
            var index = Item<MaterialColorItem>(Flash(Vector4.one), Ref(TargetRefMode.Self), 1f);
            index.m_MaterialIndex = 3;
            Expect(Cube("d", c0).gameObject, s, index, null, "material index out of range", "index out of range");
            // −1 with an empty material slot: refused before any copy
            int Copies() => Resources.FindObjectsOfTypeAll<Material>().Count(x => x.name.EndsWith(" (Feedback)", StringComparison.Ordinal));
            int copies = Copies();
            MeshRenderer holes = Cube("holes", c0);
            holes.sharedMaterials = new[] { holes.sharedMaterial, null };
            var instHoles = Item<MaterialColorItem>(Flash(Vector4.one), Ref(TargetRefMode.Self), 0.3f);
            instHoles.m_WriteMode = MaterialWriteMode.Instance;
            s_Errors.Clear();
            Expect(holes.gameObject, s, instHoles, null, "no material at an index", "−1 with an empty slot");
            Check(Copies() == copies, "−1 with an empty slot left copies " + (Copies() - copies));
            // a refused prepare holds no material
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            WeakReference weak = OwnMaterial(cube.GetComponent<MeshRenderer>());
            var outOfRange = Item<MaterialColorItem>(Flash(Vector4.one), Ref(TargetRefMode.Self), 0.3f);
            outOfRange.m_MaterialIndex = 3;
            var refused = cube.AddComponent<FeedbackPlayer>();
            refused.m_Items.Add(outOfRange);
            refused.Bind(s);
            refused.EnsureRuntime();
            refused.ReleaseRuntime();
            Object.DestroyImmediate(cube);
            Collect();
            EditorUtility.UnloadUnusedAssetsImmediate();
            Collect();
            bool alive = weak.Target is Material left && left != null;
            Check(!alive, "a refused renderer's material is held");
            MeshRenderer other = Cube("other", c0);
            PlayerOn(other.gameObject, s, Item<MaterialColorItem>(Flash(Vector4.one), Ref(TargetRefMode.Self), 0.3f)).EnsureRuntime();
            if (weak.Target is Material still && still != null)
                Object.DestroyImmediate(still);
            // −1 is the whole renderer's block, not material 0's
            MeshRenderer whole = Cube("whole", c0);
            FeedbackPlayer w = PlayerOn(whole.gameObject, s, Item<MaterialColorItem>(Flash(Vector4.one), Ref(TargetRefMode.Self), 1f));
            w.Play();
            Tick(s, 25);
            var block = new MaterialPropertyBlock();
            whole.GetPropertyBlock(block);
            var indexBlock = new MaterialPropertyBlock();
            whole.GetPropertyBlock(indexBlock, 0);
            Check(block.HasColor(s_BaseColor) && indexBlock.isEmpty, "−1's block — whole " + block.HasColor(s_BaseColor) + " · index 0 empty " + indexBlock.isEmpty);
            Done();
        }

        // ---- SetActive

        [Test]
        public void NE11_SetActive()
        {
            FeedbackScheduler s = Sched();
            GameObject target = Go("target");
            var hold = new WaitItem();
            hold.m_Timing.m_Duration = 1f;
            FeedbackPlayer set = PlayerOn(Go("set"), s, Item<SetActiveItem>(Set(Vector4.zero), Direct(target), 0f), hold);
            set.Play();
            Check(!target.activeSelf, "Set(0) did not turn it off in the Play");
            Tick(s, 5);
            set.Stop(StopMode.Revert);
            Check(target.activeSelf, "Stop(Revert) during the play did not bring the start state back");
            FeedbackPlayer blink = PlayerOn(Go("blink"), s, Item<SetActiveItem>(Blink(Vector4.zero), Direct(target), 1f));
            blink.Play();
            var states = new StringBuilder();
            for (int i = 0; i < 12; i++)
            {
                Tick(s, 1, 0.05f);
                states.Append(target.activeSelf ? '1' : '0');
            }
            Tick(s, 20, 0.05f);
            Check(states.ToString() == "001100110011", "Blink " + states);
            Check(target.activeSelf, "Blink did not end on");
            var direct = Item<SetActiveItem>(Blink(Vector4.zero), Direct(target), 0.3f);
            direct.m_DirectWrite = true;
            s_Errors.Clear();
            FeedbackPlayer d = PlayerOn(Go("direct"), s, direct);
            d.Play();
            Tick(s, 20, 0.05f);
            Check(s_Errors.Count == 0, "a direct write flag — errors " + s_Errors.Count);
            Check(d.Runtime.Binding(0) is SetActiveBinding sb && !sb.Slot.Direct, "a direct write flag took a private toggle");
            // Relative is a toggle of the play's start state — the value · the start state · the intensity
            var cells = new StringBuilder();
            bool all = true;
            foreach (float value in new[] { 1f, -1f, 0.5f, 0f })
            {
                foreach (bool on in new[] { true, false })
                {
                    foreach (float intensity in new[] { 1f, 0.5f })
                    {
                        GameObject t = Go("t");
                        t.SetActive(on);
                        FeedbackPlayer p = PlayerOn(Go("p"), s, Item<SetActiveItem>(Set(new Vector4(value, 0f, 0f, 0f), true), Direct(t), 0f));
                        p.Play(PlayArgs.Default.WithIntensity(intensity));
                        bool want = value != 0f ? !on : on;
                        all &= t.activeSelf == want;
                        cells.Append(t.activeSelf ? '1' : '0');
                    }
                }
            }
            Check(all, "Relative " + cells);
            // a Set in another player's captured episode moves the before only; its Revert writes that time's before
            GameObject t2 = Go("t2");
            FeedbackPlayer a = PlayerOn(Go("a"), s, Item<SetActiveItem>(Blink(Vector4.zero), Direct(t2), 1f));
            var wait = new WaitItem();
            wait.m_Timing.m_Duration = 3f;
            FeedbackPlayer b = PlayerOn(Go("b"), s, Item<SetActiveItem>(Set(Vector4.zero), Direct(t2), 0f), wait);
            a.Play();
            Tick(s, 3, 0.05f);
            PlaybackHandle bh = b.Play();
            Check(t2.activeSelf, "the Set wrote during the episode");
            Tick(s, 24, 0.05f);
            Check(!t2.activeSelf, "after the Blink the before is not off");
            b.Stop(bh, StopMode.Revert);
            Check(t2.activeSelf, "the Revert did not write the Set's before");
            Done();
        }

        // ---- UnityEvent

        [Test]
        public void NE12_UnityEvent()
        {
            {
                FeedbackScheduler s = Sched();
                int calls = 0;
                var item = new UnityEventItem();
                item.m_Event.AddListener(() => calls++);
                item.m_Event.AddListener(() => throw new InvalidOperationException("bindings listener"));
                var after = new UnityEventItem();
                int afterCalls = 0;
                after.m_Event.AddListener(() => afterCalls++);
                var hold = new WaitItem();
                hold.m_Timing.m_Duration = 0.5f;
                FeedbackPlayer p = PlayerOn(Go("ue"), s, item, after, hold);
                LogAssert.Expect(LogType.Exception, new Regex("bindings listener"));
                PlaybackHandle h = p.Play();
                Check(calls == 1 && afterCalls == 1, "listeners ran " + calls + " · " + afterCalls);
                Check(h != default && p.IsPlaying, "the play did not go on");
                foreach (StopMode mode in new[] { StopMode.Hold, StopMode.Revert, StopMode.Complete })
                {
                    LogAssert.Expect(LogType.Exception, new Regex("bindings listener"));
                    p.Play();
                    p.Stop(mode);
                }
                Check(calls == 4 && afterCalls == 4, "Stop called the listeners — " + calls);
            }
            // a listener re-enters its player — Stop · Rebuild · Play at t = 0 and from a tick
            string Run(string what, float delay)
            {
                FeedbackScheduler s = Sched();
                GameObject later = Go("later");
                FeedbackPlayer p = null;
                string result = "";
                var ue = new UnityEventItem();
                ue.m_Timing.m_Delay = delay;
                ue.m_Event.AddListener(() =>
                {
                    switch (what)
                    {
                        case "Stop": p.Stop(); break;
                        case "Rebuild": result = p.Rebuild() ? "T" : "F"; break;
                        case "Play": result = p.Play() != default ? "T" : "F"; break;
                    }
                });
                var move = Item<PositionItem>(Oscillate(new Vector4(1f, 0f, 0f, 0f), 0.25f), Direct(later), 0.3f);
                move.m_Timing.m_Delay = 0.05f;
                p = PlayerOn(Go("owner"), s, ue, move);
                int stopped = 0;
                p.Stopped += (pl, hh) => stopped++;
                p.EnsureRuntime();
                s_Errors.Clear();
                s_Warnings.Clear();
                p.Play();
                bool moved = false;
                for (int i = 0; i < 6; i++)
                {
                    Tick(s, 1);
                    moved |= later.transform.localPosition != Vector3.zero;
                }
                return what + (delay == 0f ? "@0" : "@tick") + " " + result + " stopped " + stopped + " moved " + moved + " errors " + s_Errors.Count + " warnings " + s_Warnings.Count;
            }
            var lines = new[]
            {
                Run("Stop", 0f), Run("Stop", 0.03f), Run("Rebuild", 0f), Run("Rebuild", 0.03f), Run("Play", 0f), Run("Play", 0.03f),
            };
            var want = new[]
            {
                "Stop@0  stopped 1 moved False errors 0 warnings 0", "Stop@tick  stopped 1 moved False errors 0 warnings 0",
                "Rebuild@0 F stopped 0 moved True errors 0 warnings 0", "Rebuild@tick T stopped 1 moved False errors 0 warnings 0",
                "Play@0 F stopped 0 moved True errors 0 warnings 1", "Play@tick T stopped 3 moved False errors 0 warnings 0",
            };
            for (int i = 0; i < lines.Length; i++)
                Check(lines[i] == want[i], lines[i]);
            Done();
        }

        // ---- the evaluation seeds (the occurrences differ, reproduced)

        string Seeds(uint seed)
        {
            FeedbackScheduler s = Sched();
            var item = Item<PositionItem>(Shake(new Vector4(1f, 1f, 1f, 0f)), Ref(TargetRefMode.Self), 0.1f);
            item.m_Timing.m_RepeatCount = 2;
            FeedbackPlayer p = PlayerOn(Go("seed"), s, item);
            p.Play(PlayArgs.Default.WithSeed(seed));
            var binding = (PositionBinding)p.Runtime.Binding(0);
            var seen = new List<uint> { binding.EvaluationSeed(0) };
            for (int i = 0; i < 20; i++)
            {
                Tick(s, 1, 0.025f);
                uint now = binding.EvaluationSeed(0);
                if (seen[seen.Count - 1] != now)
                    seen.Add(now);
            }
            return string.Join(" ", seen.Select(x => x.ToString("X8")));
        }

        [Test]
        public void NE13_EvaluationSeeds()
        {
            string a = Seeds(42);
            string b = Seeds(42);
            string c = Seeds(43);
            Check(a.Split(' ').Distinct().Count() == 3, "three occurrences, seeds " + a);
            Check(a == b, "seed 42 again " + b);
            Check(a.Split(' ').Intersect(c.Split(' ')).Count() == 0, "seed 43 " + c);
            Done();
        }

        // ---- the start value

        [Test]
        public void NE14_StartValue()
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("t");
            FeedbackPlayer punch = PlayerOn(Go("punch"), s, Item<PositionItem>(Oscillate(new Vector4(100f, 0f, 0f, 0f)), Direct(t.transform), 1f));
            FeedbackPlayer tween = PlayerOn(Go("tween"), s, Item<PositionItem>(Tween(TweenMode.To, new Vector4(0f, 5f, 0f, 0f)), Direct(t.transform), 0.5f));
            tween.EnsureRuntime();
            punch.Play();
            Tick(s, 6);
            tween.Play();
            Vector4 start = ((PositionBinding)tween.Runtime.Binding(0)).StartValue(0);
            Check(start == Vector4.zero, "the tween's start after LateApply " + F((Vector3)start));
            tween.Stop(StopMode.Hold);
            GameObject r = Go("r");
            var item = Item<PositionItem>(Tween(TweenMode.To, new Vector4(4f, 0f, 0f, 0f)), Ref(TargetRefMode.Self), 0.2f);
            item.m_Timing.m_RepeatCount = 2;
            FeedbackPlayer p = PlayerOn(r, s, item);
            p.Play();
            var xs = new List<string>();
            for (int i = 0; i < 12; i++)
            {
                Tick(s, 1, 0.05f);
                xs.Add(F(r.transform.localPosition.x));
            }
            Check(string.Join(" ", xs) == "1 2 3 0 1 2 3 0 1 2 3 4", "repeated To " + string.Join(" ", xs));
            Done();
        }

        // ---- the contribution's lifetime

        [Test]
        public void NE15_ContributionLifetime()
        {
            FeedbackScheduler s = Sched();
            {
                GameObject t = Go("t");
                var item = Item<PositionItem>(Punch(new Vector4(1f, 0f, 0f, 0f)), Ref(TargetRefMode.Self), 0.02f);
                item.m_Timing.m_RepeatCount = 9;
                FeedbackPlayer p = PlayerOn(t, s, item);
                p.EnsureRuntime();
                var slot = (LocalPositionSlot)((PositionBinding)p.Runtime.Binding(0)).Slot;
                int growth = slot.GrowthCount;
                s_Warnings.Clear();
                p.Play();
                s.Tick(0.1f);
                s.Tick(0.1f);
                s.Tick(0.1f);
                Check(slot.GrowthCount == growth, "ledger growth " + (slot.GrowthCount - growth));
                Check(s_Warnings.Count == 0, "warnings " + s_Warnings.Count);
                Check(slot.LiveContributions == 0, "live after " + slot.LiveContributions);
            }
            {
                // the effect is seen while the occurrences hand the contribution on
                GameObject t = Go("t2");
                var item = Item<PositionItem>(Oscillate(new Vector4(1f, 0f, 0f, 0f), 0.25f), Ref(TargetRefMode.Self), 0.02f);
                item.m_Timing.m_RepeatCount = 9;
                FeedbackPlayer p = PlayerOn(t, s, item);
                p.Play();
                s.Tick(0.05f);
                bool seen = t.transform.localPosition != Vector3.zero;
                s.Tick(0.05f);
                seen &= t.transform.localPosition != Vector3.zero;
                Check(seen, "the repeats' effect is not seen");
                // the release gives the reservation back and the slot goes
                var slot = ((PositionBinding)p.Runtime.Binding(0)).Slot;
                Tick(s, 10);
                p.ReleaseRuntime();
                s.RunRevert();
                Check(slot.Reserved == 0 && !s.Context.Layers.TryFind(t.transform, 0, 0, out LocalPositionSlot _), "after the release — reserved " + slot.Reserved);
            }
            // two players on one slot: releasing one keeps the other's contribution
            GameObject shared = Go("shared");
            FeedbackPlayer one = PlayerOn(Go("one"), s, Item<PositionItem>(Oscillate(new Vector4(1f, 0f, 0f, 0f), 0.25f), Direct(shared.transform), 1f));
            FeedbackPlayer two = PlayerOn(Go("two"), s, Item<PositionItem>(Oscillate(new Vector4(0f, 1f, 0f, 0f), 0.25f), Direct(shared.transform), 1f));
            two.Play();
            one.EnsureRuntime();
            one.ReleaseRuntime();
            Tick(s, 2);
            Check(shared.transform.localPosition.y != 0f, "releasing one player ended the other's contribution");
            Done();
        }

        // ---- the end reasons (the binding's part)

        [Test]
        public void NE16_EndReasons()
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("t");
            t.transform.localPosition = new Vector3(1f, 0f, 0f);
            var to = Item<PositionItem>(Tween(TweenMode.To, new Vector4(5f, 0f, 0f, 0f)), Ref(TargetRefMode.Self), 0.1f);
            var by = Item<PositionItem>(Tween(TweenMode.By, new Vector4(0f, 2f, 0f, 0f)), Ref(TargetRefMode.Self), 1f);
            by.m_Timing.m_Rule = StartRule.AfterPrevious;
            FeedbackPlayer p = PlayerOn(t, s, to, by);
            p.Play();
            Tick(s, 10);
            p.Stop(StopMode.Revert);
            Check(t.transform.localPosition == new Vector3(1f, 0f, 0f), "Revert " + F(t.transform.localPosition));
            p.Play();
            Tick(s, 10);
            p.Stop(StopMode.Complete);
            Check(Near(t.transform.localPosition, new Vector3(5f, 2f, 0f)), "Complete " + F(t.transform.localPosition));
            p.Play();
            Tick(s, 10);
            p.Stop(StopMode.Hold);
            Vector3 held = t.transform.localPosition;
            Tick(s, 3);
            Check(t.transform.localPosition == held, "Hold moved " + F(t.transform.localPosition));
            // the record written again by a second play: its Revert goes to the second play's start
            t.transform.localPosition = new Vector3(9f, 0f, 0f);
            p.Play();
            Tick(s, 10);
            p.Stop(StopMode.Revert);
            Check(t.transform.localPosition == new Vector3(9f, 0f, 0f), "the second play's Revert " + F(t.transform.localPosition));
            // an Offset after Stop(Hold) is the base
            GameObject o = Go("o");
            FeedbackPlayer po = PlayerOn(o, s, Item<PositionItem>(Oscillate(new Vector4(1f, 0f, 0f, 0f), 0.25f), Ref(TargetRefMode.Self), 1f));
            po.Play();
            Tick(s, 3);
            po.Stop(StopMode.Hold);
            Tick(s, 1);
            Check(Bits(o.transform.localPosition, Vector3.zero), "an Offset after Stop(Hold) " + F(o.transform.localPosition));
            Done();
        }

        // ---- WeightTowardTarget

        sealed class TowardItem : TargetItem
        {
            static readonly TargetTraits s_Traits = new TargetTraits(ValueKind.Float, MotionMask.Punch | MotionMask.Oscillate, FeedbackCategory.None, false, ApplyMode.WeightTowardTarget);
            internal LayerTestTarget Host;

            internal override TargetTraits Traits => s_Traits;
        }

        sealed class TowardBinding : ValueTargetBinding<float, FloatOps>
        {
            readonly TowardItem m_Toward;

            internal TowardBinding(TowardItem item)
                : base(item)
            {
                m_Toward = item;
            }

            protected override float TowardTarget => 10f;

            protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
            {
                LayerTestTarget host = m_Toward.Host;
                slot = Layers.GetOrCreate(host, 0, 0, () => new TestFloatSlot(host, ConsumptionStage.LateApply, Composition.Additive, false));
                return true;
            }
        }

        [Test]
        public void NE17_WeightTowardTarget()
        {
            FeedbackImplementations.Register<TowardItem>(i => new TowardBinding(i));
            FeedbackScheduler s = Sched();
            GameObject go = Go("toward");
            var host = go.AddComponent<LayerTestTarget>();
            host.Value = 1f;
            var item = new TowardItem { m_Motion = Oscillate(new Vector4(1f, 0f, 0f, 0f)), m_Target = Ref(TargetRefMode.Self), Host = host };
            item.m_Timing.m_Duration = 1f;
            FeedbackPlayer p = PlayerOn(go, s, item);
            p.Play();
            Tick(s, 1, 0.125f);
            var probe = new OscillateEvaluator(new OscillateParams { m_Amplitude = new Vector4(1f, 0f, 0f, 0f), m_Frequency = 1f }, 1);
            MotionState st = default;
            probe.Begin(ref st, Vector4.zero, new MotionBegin { Intensity = 1f });
            float shape = probe.EvaluateShape(st, new MotionTime(0.125f, 0.125));
            Check(Mathf.Abs(host.Value - (1f + 9f * shape)) < 1e-4f, "WeightTowardTarget " + F(host.Value) + " want " + F(1f + 9f * shape));
            Done();
        }

        // ---- the refusal's warning

        [Test]
        public void NE18_RefusalWarning()
        {
            FeedbackScheduler s = Sched();
            s_Warnings.Clear();
            FeedbackPlayer p1 = PlayerOn(Go("w1"), s, Item<PositionItem>(Punch(Vector4.one), ChildPath("x")), Item<PositionItem>(Punch(Vector4.one), ChildPath("x")));
            FeedbackPlayer p2 = PlayerOn(Go("w2"), s, Item<PositionItem>(Punch(Vector4.one), ChildPath("x")));
            p1.EnsureRuntime();
            p2.EnsureRuntime();
            List<string> first = Skipped();
            Check(first.Count == 3, "warnings " + first.Count);
            Check(first.Count > 0 && first[0] == PlayerRuntime.SkippedWarning + "0 (PositionItem): no such child", "text '" + first.FirstOrDefault() + "'");
            p1.Rebuild();
            Check(Skipped().Count == 5, "after the Rebuild " + Skipped().Count);
            Done();
        }
    }
}
