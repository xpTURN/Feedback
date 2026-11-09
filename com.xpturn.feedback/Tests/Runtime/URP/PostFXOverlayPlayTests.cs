using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Perf = xpTURN.Feedback.Tests.PlayerPerformanceTests;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests
{
    // Via Scripting · the frame allocation (the real loop · a fresh fixture's first cycle with many cameras · many from rest) ·
    // the cost record · the stack's types (a player's too). Each on a fresh test scheduler set as the default (the loop runs
    // it)
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    public sealed class PostFXOverlayPlayTests
    {
        static long s_Baseline = -1;
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        FeedbackScheduler m_Saved;
        FeedbackScheduler m_Test;

        [SetUp]
        public void SetUp()
        {
            m_Saved = FeedbackScheduler.Default;
            m_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = m_Test;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    p.Stop();
            }
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            foreach (Object o in m_Made)
            {
                if (o is GameObject g && g != null && g.TryGetComponent(out Camera c))
                    c.targetTexture = null;
            }
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Players.Clear();
            m_Schedulers.Clear();
            m_Made.Clear();
            yield return null;
            FeedbackScheduler.Default = m_Saved;
            if (!m_Test.IsShutdown)
                m_Test.Shutdown();
            Time.captureDeltaTime = 0f;
        }

        Camera MakeCamera()
        {
            var go = new GameObject("UO camera");
            m_Made.Add(go);
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            var rt = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGBHalf);
            m_Made.Add(rt);
            cam.targetTexture = rt;
            UniversalAdditionalCameraData d = cam.GetUniversalAdditionalCameraData();
            d.renderPostProcessing = true;
            d.volumeLayerMask = 1;
            return cam;
        }

        // a render brings the volume manager up
        static IEnumerator Initialized()
        {
            for (int i = 0; i < 60 && !VolumeManager.instance.isInitialized; i++)
                yield return null;
            Assert.That(VolumeManager.instance.isInitialized, Is.True, "a rendering camera initializes the volume manager");
        }

        FeedbackPlayer Player(params FeedbackItem[] items)
        {
            var go = new GameObject("UO player");
            m_Made.Add(go);
            go.SetActive(false);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            go.SetActive(true);
            p.EnsureRuntime();
            m_Players.Add(p);
            return p;
        }

        static T With<T>(T item, FeedbackMotion motion, float duration) where T : TargetItem
        {
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static FlashMotion Flash() => new FlashMotion { m_Params = new FlashParams { m_Target = Vector4.one, m_Attack = 0.25f, m_Hold = 0.5f, m_Release = 0.25f } };

        // ---- a Via Scripting camera sees the overlay while it is on and loses it after (the service's update on the first
        // frame off) — the mode set after the manager is up (URP's own update logs an error before)

        [UnityTest]
        public IEnumerator UO11a_ViaScripting()
        {
            Time.captureDeltaTime = 1f / 60f;
            Camera cam = MakeCamera();
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            yield return Initialized();
            cam.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting);
            cam.UpdateVolumeStack(data);
            yield return null;
            float b = data.volumeStack.GetComponent<Vignette>().intensity.value;
            FeedbackPlayer p = Player(With(new PostFXVignetteItem(), Flash(), 0.3f));
            PostFXOverlayService svc = PostFXOverlayService.Of(m_Test.Context);
            p.Play();
            float max = 0f;
            for (int f = 0; f < 10; f++)
            {
                yield return null;
                max = Mathf.Max(max, data.volumeStack.GetComponent<Vignette>().intensity.value);
            }
            for (int f = 0; f < 30; f++)
                yield return null;
            float after = data.volumeStack.GetComponent<Vignette>().intensity.value;
            Assert.That(max, Is.GreaterThan(b + 0.1f), "the overlay seen while on");
            Assert.That(after, Is.EqualTo(b).Within(1e-4f), "lost after — the first frame off updated");
            Assert.That(svc.ViaScriptingUpdates, Is.GreaterThan(0));
        }

        // ---- the real loop from rest — flashes · punches · tweens (a Revert mid-play) against the run's median idle frame;
        // the three volumes each on in the window

        [UnityTest]
        public IEnumerator UO21a_TheRealLoopFromRest()
        {
            LogAssert.ignoreFailingMessages = true;
            Time.captureDeltaTime = 1f / 60f;
            MakeCamera();
            FeedbackPlayer bloom = Player(With(new PostFXBloomItem(), Flash(), 0.3f));
            FeedbackPlayer vig = Player(With(new PostFXVignetteItem(), new PunchMotion { m_Params = new PunchParams { m_Amplitude = Vector4.one, m_Vibrato = 3f, m_Decay = 1f } }, 0.4f));
            TweenParams tp = TweenParams.Default;
            tp.m_To = Vector4.one;
            FeedbackPlayer adj = Player(With(new PostFXColorAdjustmentsItem(), new TweenMotion { m_Params = tp }, 1f));
            PostFXOverlayService svc = PostFXOverlayService.Of(m_Test.Context);
            using ProfilerRecorder rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            yield return null;
            bloom.Play();
            vig.Play();
            adj.Play();
            for (int f = 0; f < 30; f++)
                yield return null;
            adj.Stop(StopMode.Revert);
            for (int f = 0; f < 30; f++)
                yield return null;
            bool rest = svc.OnCount == 0;
            var idle = new List<long>();
            for (int f = 0; f < 31; f++)
            {
                yield return null;
                idle.Add(rec.LastValue);
            }
            idle.Sort();
            long baseline = idle[15];
            int idleAllocating = idle.FindAll(v => v - baseline >= 64).Count;
            int allocating = 0, moved = 0;
            for (int f = 0; f < 120; f++)
            {
                if (f % 30 == 0)
                    bloom.Play();
                if (f % 40 == 0)
                    vig.Play();
                if (f % 60 == 0)
                    adj.Play();
                if (f % 60 == 30)
                    adj.Stop(StopMode.Revert);
                yield return null;
                moved |= (svc.RentedAt(0).enabled ? 1 : 0) | (svc.RentedAt(1).enabled ? 2 : 0) | (svc.RentedAt(2).enabled ? 4 : 0);
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
            Debug.Log($"UO21a postfx real loop · idle allocating {idleAllocating}/31 · allocating {allocating}/120");
            Assert.That((rest, moved), Is.EqualTo((true, 7)), "from rest · every volume on in the window");
            Assert.That(positive, Is.GreaterThanOrEqualTo(4), "the recorder sees an allocation — the positive");
#if UNITY_6000_3_OR_NEWER || !UNITY_EDITOR
            Assert.That(allocating, Is.LessThanOrEqualTo(idleAllocating + 3), "the plays from rest allocate nothing");
#else
            Assert.Ignore("6000.0's editor loop allocates in FlushDirty (EditorCompilationInterface.IsCompiling) in each frame the scene changed — the IL2CPP player judges the allocation");
#endif
        }

        // ---- a fresh fixture's first cycle with the manager up — few cameras · many (the scan's array grows at a rent, not in a
        // play) · a rented volume's toggle · its weight

        static FeedbackScheduler s_S;
        static FeedbackPlayer s_P;
        static Volume s_V;
        static int[] s_Sink;

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
            s_S.Tick(0.05f);
        };

        static readonly TestDelegate s_Toggle = () =>
        {
            s_V.enabled = true;
            s_V.enabled = false;
        };

        static readonly TestDelegate s_Weight = () =>
        {
            s_V.weight = 0.5f;
            s_V.weight = 0f;
        };

        void Fresh(int items)
        {
            s_S = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s_S);
            var go = new GameObject("UO fresh");
            m_Made.Add(go);
            go.SetActive(false);
            s_P = go.AddComponent<FeedbackPlayer>();
            for (int i = 0; i < items; i++)
                s_P.m_Items.Add(With(new PostFXVignetteItem(), Flash(), 0.3f));
            go.SetActive(true);
            s_P.Bind(s_S);
            s_P.EnsureRuntime();
        }

        void FreshVolume()
        {
            Fresh(1);
            s_V = PostFXOverlayService.Of(s_S.Context).RentedAt(0);
        }

        // the first runs of five fresh fixtures — the warm-up is another fixture with the same delegate
        static int Hits(TestDelegate d, Action fresh)
        {
            fresh();
            d();
            int hits = 0;
            for (int i = 0; i < 5; i++)
            {
                fresh();
                if (UIs.AllocatingGCMemory().ApplyTo(d).IsSuccess)
                    hits++;
            }
            return hits;
        }

        [UnityTest]
        public IEnumerator UO21b_AFreshFixturesFirstCycle()
        {
            MakeCamera();
            yield return Initialized();
            int few = Hits(s_Cycle, () => Fresh(1));
            int toggle = Hits(s_Toggle, FreshVolume);
            int weight = Hits(s_Weight, FreshVolume);
            for (int i = 0; i < 20; i++)
            {
                var go = new GameObject("UO camera " + i);
                m_Made.Add(go);
                go.AddComponent<Camera>();
            }
            int many = Hits(s_Cycle, () => Fresh(1));
            int positive = 0;
            for (int i = 0; i < 5; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => s_Sink = new int[4])).IsSuccess)
                    positive++;
            }
            Assert.That(positive, Is.EqualTo(5), "the positive");
            Assert.That(new[] { few, toggle, weight, many }, Is.All.EqualTo(0), "few cameras · a toggle · a weight · " + Camera.allCamerasCount + " cameras");
        }

        // ---- many from rest — more volumes on at once than the volume manager's list has room for (its capacity read by
        // reflection: the earlier tests grew it — the names are Core RP's, both versions'): the preparation's warm grew it

        [UnityTest]
        public IEnumerator UO21c_ManyFromRest()
        {
            yield return null;
            object collection = typeof(VolumeManager).GetField("m_VolumeCollection", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(VolumeManager.instance);
            var list = (List<Volume>)collection.GetType().GetField("m_Volumes", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(collection);
            int capacity = list.Capacity;
            int n = capacity - list.Count + 4;
            Fresh(2);
            s_Cycle();
            Fresh(n);
            int grown = list.Capacity;
            bool clean = !UIs.AllocatingGCMemory().ApplyTo(s_Cycle).IsSuccess;
            bool positive = UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => s_Sink = new int[4])).IsSuccess;
            Assert.That(positive, Is.True, "the positive");
            Assert.That(grown, Is.GreaterThan(capacity), "the preparation grew the list for " + n);
            Assert.That(clean, Is.True, "the first play of many from rest allocates nothing");
        }

        // ---- the per-play ns within its budget in an IL2CPP player (no camera: the volume manager's update is not ours)

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
        public IEnumerator UO22a_PostFXWeight([Values(100, 1000)] int n)
        {
            Time.captureDeltaTime = 1f / 60f;
            yield return null;
            yield return Baseline();
            var players = new List<FeedbackPlayer>(n);
            for (int i = 0; i < n; i++)
            {
                var item = new PostFXVignetteItem { m_Motion = new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = Vector4.one, m_Frequency = 2f } } };
                item.m_Timing.m_Duration = 1000f;
                players.Add(Player(item));
            }
            PostFXOverlayService svc = PostFXOverlayService.Of(m_Test.Context);
            Volume first = svc.RentedAt(0);
            int prepared = 0, accepted = 0;
            float moved = 0f;
            void Sample() => moved = Mathf.Max(moved, first.weight);
            Canvas.willRenderCanvases += Sample;
            foreach (FeedbackPlayer p in players)
            {
                if (p.Runtime != null && p.Runtime.Binding(0) != null)
                    prepared++;
                if (p.Play() != default)
                    accepted++;
            }
            try
            {
                yield return Perf.Settled("(x) postfx weight " + n, n, 0, s_Baseline, 1750);
            }
            finally
            {
                Canvas.willRenderCanvases -= Sample;
            }
            Perf.Window w = Perf.s_Last;
            Assert.That(moved, Is.GreaterThan(0.1f), "the plays weigh the volume — the positive");
            Assert.That(prepared + " · " + accepted, Is.EqualTo(n + " · " + n), "prepared · accepted");
            Assert.That(w.Frames + " · " + w.Counted, Is.EqualTo(Perf.Frames + " · " + Perf.Frames), "the recorders live — frames · markers");
            Assert.That(w.PerPlay, Is.GreaterThan(0), "per play ns (written down)");
            Perf.RowBudget("(x) postfx weight " + n, 1750);
            if (Perf.k_Judge)
                Assert.That(Math.Abs(w.Alloc - s_Baseline), Is.LessThanOrEqualTo(Perf.Equal), "the window " + w.Alloc + " vs " + s_Baseline + " B");
        }

        // ---- the stack has every overlay component type — the editor's all; a player's (17.6: the default profile's — the
        // build fills it) written down

        [UnityTest]
        public IEnumerator UO25a_TheStacksTypes()
        {
            Time.captureDeltaTime = 1f / 60f;
            MakeCamera();
            yield return Initialized();
            yield return null;
            var missing = new List<string>();
            foreach (Type t in new[] { typeof(Bloom), typeof(ChromaticAberration), typeof(ColorAdjustments), typeof(DepthOfField), typeof(FilmGrain), typeof(LensDistortion),
                         typeof(MotionBlur), typeof(PaniniProjection), typeof(Vignette), typeof(WhiteBalance), typeof(ChannelMixer), typeof(SplitToning), typeof(LiftGammaGain),
                         typeof(ShadowsMidtonesHighlights) })
            {
                if (VolumeManager.instance.stack.GetComponent(t) == null)
                    missing.Add(t.Name);
            }
            Debug.Log("UO25a postfx stack types missing [" + string.Join(" ", missing) + "] editor=" + Application.isEditor);
            Assert.That(missing, Is.Empty, "every overlay component type in the stack");
        }
    }
}
