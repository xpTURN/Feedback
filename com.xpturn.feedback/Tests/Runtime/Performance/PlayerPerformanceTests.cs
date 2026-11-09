using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // N players (drivers · empty runners) each with one item, played at once in the real loop; a warm-up, then Frames frames
    // of our three markers, the main thread and the frame allocation. The per-play value is the three markers' sum / N /
    // frame. Budgets and the allocation are judged in an IL2CPP player only — the editor writes the lines (a trend). The run's
    // first row measures the no-play window twice (the baseline — the smaller) and writes the player's architecture; a window
    // past the baseline by more than 32 B is measured once more and the nearer kept (6000.3 adds a sporadic 36.9 kB to a
    // window). Windows are nested enumerators, the baseline too — the coroutine's shape moves the test runner's own allocation
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    [Category("Performance")]
    public sealed class PlayerPerformanceTests
    {
        const int Warmup = 60;
        internal const int Frames = 300;
        internal const long Equal = 32;

        // judged in an IL2CPP player only — a static field, not a const (a const false makes 6000.6 warn CS0162)
#if ENABLE_IL2CPP && !UNITY_EDITOR
        internal static readonly bool k_Judge = true;
#else
        internal static readonly bool k_Judge = false;
#endif

        internal struct Window
        {
            public double PerPlay, P95, Revert, Update, Late, FrameMs;
            public long Alloc;
            public int Frames, Counted;
        }

        static readonly List<KeyValuePair<string, long>> s_Rows = new List<KeyValuePair<string, long>>();
        static long s_Baseline = -1;
        internal static Window s_Last;
        static FeedbackScheduler s_Saved;
        static FeedbackScheduler s_Test;
        readonly List<Object> m_Made = new List<Object>();
        readonly List<string> m_Fail = new List<string>();

        sealed class EmptyRunner : IPlaybackRunner
        {
            public bool Advance(int state, in FrameTime time) => true;

            public void Abort(int state, AbortReason reason)
            {
            }

            public Object DebugOwner => null;
        }

        // the positive controls of the allocation judgement — an object each frame · 100 kB once, in the window's middle (the
        // first advance falls in the warm-up)
        sealed class AllocatingRunner : IPlaybackRunner
        {
            internal static object Sink;
            readonly bool m_Once;
            int m_Advances;

            internal AllocatingRunner(bool once) => m_Once = once;

            public bool Advance(int state, in FrameTime time)
            {
                if (!m_Once)
                {
                    Sink = new object[1];
                }
                else if (++m_Advances == Warmup + Frames / 2)
                {
                    Sink = new byte[100000];
                }
                return true;
            }

            public void Abort(int state, AbortReason reason)
            {
            }

            public Object DebugOwner => null;
        }

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = 1f / 60f;
            s_Saved = FeedbackScheduler.Default;
            s_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = s_Test;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Made.Clear();
            m_Fail.Clear();
            yield return null;
            FeedbackScheduler.Default = s_Saved;
            if (!s_Test.IsShutdown)
                s_Test.Shutdown();
            Time.captureDeltaTime = 0f;
        }

        static string Where => Application.isEditor ? "editor " : "player ";

        static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        static TargetRef Self => new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };

        static T Item<T>(FeedbackMotion motion) where T : TargetItem, new()
        {
            var item = new T { m_Motion = motion, m_Target = Self };
            item.m_Timing.m_Duration = 1000f;
            return item;
        }

        internal static FlashMotion Flash(Vector4 target) => new FlashMotion { m_Params = new FlashParams { m_Target = target, m_Attack = 0.5f, m_Hold = 0f, m_Release = 0.5f } };

        // a one second flash, again and again
        internal static T Repeated<T>(T item) where T : EffectItem
        {
            item.m_Timing.m_Duration = 1f;
            item.m_Timing.m_RepeatForever = true;
            return item;
        }

        internal static TweenMotion Tween(Vector4 to) => new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.To, m_To = to, m_Loops = 1 } };

        static PunchMotion Punch => new PunchMotion { m_Params = new PunchParams { m_Amplitude = Vector4.one, m_Vibrato = 5f, m_Decay = 0.1f } };

        GameObject Go(string name, bool cube = false)
        {
            GameObject go = cube ? GameObject.CreatePrimitive(PrimitiveType.Cube) : new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        FeedbackPlayer PlayerOn(GameObject go, FeedbackItem item)
        {
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.Add(item);
            p.EnsureRuntime();
            return p;
        }

        static long Heap()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return GC.GetTotalMemory(true);
        }

        // the window — a nested enumerator, the same for the baseline and the rows
        internal static IEnumerator Measure(string name, int n, long heapPer)
        {
            for (int i = 0; i < Warmup; i++)
                yield return null;
            var revert = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Feedback.Revert", Frames + 8);
            var update = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Feedback.Update", Frames + 8);
            var late = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Feedback.LateApply", Frames + 8);
            var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", Frames + 8);
            var alloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", Frames + 8);
            for (int i = 0; i < Frames; i++)
                yield return null;
            var w = new Window();
            var per = new List<double>();
            int count = Math.Min(Math.Min(revert.Count, update.Count), late.Count);
            for (int i = 0; i < count; i++)
            {
                double rv = revert.GetSample(i).Value, uv = update.GetSample(i).Value, lv = late.GetSample(i).Value;
                w.Revert += rv;
                w.Update += uv;
                w.Late += lv;
                per.Add((rv + uv + lv) / n);
            }
            double mainSum = 0;
            for (int i = 0; i < main.Count; i++)
                mainSum += main.GetSample(i).Value;
            for (int i = 0; i < alloc.Count; i++)
                w.Alloc += alloc.GetSample(i).Value;
            w.Frames = alloc.Count;
            w.Counted = count;
            w.FrameMs = main.Count > 0 ? mainSum / main.Count / 1e6 : -1;
            w.PerPlay = count > 0 ? (w.Revert + w.Update + w.Late) / count / n : -1;
            per.Sort();
            w.P95 = per.Count > 0 ? per[Math.Min(per.Count - 1, (int)Math.Ceiling(per.Count * 0.95) - 1)] : -1;
            revert.Dispose();
            update.Dispose();
            late.Dispose();
            main.Dispose();
            alloc.Dispose();
            s_Last = w;
            Debug.Log("PERF " + Where + name + " n=" + n + " perPlayNs=" + F(w.PerPlay) + " p95=" + F(w.P95)
                      + " split=" + F(w.Revert / Math.Max(1, count) / n) + "/" + F(w.Update / Math.Max(1, count) / n) + "/" + F(w.Late / Math.Max(1, count) / n)
                      + " frameMs=" + F(w.FrameMs) + " allocB=" + w.Alloc + "/" + w.Frames + "f heapPerPlayerB=" + heapPer);
        }

        // the run's first row measures the no-play window twice and keeps the smaller — before it makes its players
        static IEnumerator Baseline()
        {
            if (s_Baseline >= 0)
                yield break;
            Debug.Log("PERF " + Where + "arch=" + RuntimeInformation.ProcessArchitecture + " platform=" + Application.platform);
            yield return Measure("(j) no play", 1, 0);
            long first = s_Last.Alloc;
            yield return Measure("(j) no play again", 1, 0);
            s_Baseline = Math.Min(first, s_Last.Alloc);
        }

        // a window past the baseline by more than 32 B, or past its budget, is measured up to twice more — the nearest allocation and
        // the smallest per play kept (judged in an IL2CPP player — 6000.3 adds a sporadic 503 · 14,183 · 14,687 B to about one window
        // in twenty-five; a busy machine slows a whole window)
        static IEnumerator Settled(string name, int n, long heapPer) => Settled(name, n, heapPer, s_Baseline);

        internal static IEnumerator Settled(string name, int n, long heapPer, long baseline, double limitNs = double.MaxValue)
        {
            yield return Measure(name, n, heapPer);
            for (int i = 0; i < 2 && k_Judge && (Math.Abs(s_Last.Alloc - baseline) > Equal || s_Last.PerPlay > limitNs); i++)
            {
                Window first = s_Last;
                yield return Measure(name + " again", n, heapPer);
                if (Math.Abs(first.Alloc - baseline) < Math.Abs(s_Last.Alloc - baseline))
                    s_Last.Alloc = first.Alloc;
                if (first.PerPlay < s_Last.PerPlay)
                    s_Last.PerPlay = first.PerPlay;
            }
        }

        // a row's window — its allocation is judged in GP08_FrameAllocation
        static IEnumerator Row(string name, int n, long heapPer)
        {
            yield return Settled(name, n, heapPer);
            s_Rows.Add(new KeyValuePair<string, long>(name, s_Last.Alloc));
        }

        // every item prepared (a refused motion skips the item — the window would time an empty play) · every play accepted
        void Played(List<FeedbackPlayer> players)
        {
            int prepared = 0, accepted = 0;
            foreach (FeedbackPlayer p in players)
            {
                if (p.Runtime != null && p.Runtime.Binding(0) != null)
                    prepared++;
                if (p.Play() != default)
                    accepted++;
            }
            Check(prepared == players.Count && accepted == players.Count, "prepared " + prepared + " · accepted " + accepted + " of " + players.Count);
        }

        // a stage's share of a play in the last window — a path through the layer costs its stage tens of ns a play or more; a
        // stage's own fixed cost is under 2 ns a play (Persist — the Tween — writes in Update and leaves Revert · LateApply so)
        internal const double LayerFloor = 10;

        internal static double PerPlay(double stageTotal, int n) => stageTotal / Math.Max(1, s_Last.Counted) / n;

        // a row after GP08_FrameAllocation: its window against the baseline
        void Held(string what)
        {
            if (k_Judge && Math.Abs(s_Last.Alloc - s_Baseline) > Equal)
                m_Fail.Add(what + " " + s_Last.Alloc + " vs " + s_Baseline + " B");
        }

        // a module row's window — the recorders live and, in an IL2CPP player, the per-play mean within its budget
        internal static void RowBudget(string what, double limitNs)
        {
            Assert.That(s_Last.Frames + " · " + s_Last.Counted, Is.EqualTo(Frames + " · " + Frames), "the recorders live — frames · markers");
            if (k_Judge)
                Assert.That(s_Last.PerPlay, Is.LessThanOrEqualTo(limitNs), what + " per play ns");
        }

        void Budget(string what, double limitNs)
        {
            Check(s_Last.Frames == Frames && s_Last.Counted == Frames, "the recorders live — frames " + s_Last.Frames + " · markers " + s_Last.Counted);
            if (k_Judge)
                Check(s_Last.PerPlay > 0 && s_Last.PerPlay <= limitNs, what + " " + F(s_Last.PerPlay) + " ns > " + limitNs);
        }

        // ---- (a) leaf Punch — N 100 · 1,000 within 1 µs, N 5,000 written down
        [UnityTest]
        public IEnumerator GP01_LeafPunch([Values(100, 1000, 5000)] int n)
        {
            yield return null;
            yield return Baseline();
            long before = Heap();
            var players = new List<FeedbackPlayer>(n);
            for (int i = 0; i < n; i++)
                players.Add(PlayerOn(Go("p"), Item<PositionItem>(Punch)));
            long heapPer = (Heap() - before) / n;
            foreach (FeedbackPlayer p in players)
                p.Play();
            yield return Row("(a) leaf punch " + n, n, heapPer);
            if (n <= 1000)
                Budget("leaf " + n, 1000);
            else
                Check(s_Last.Frames == Frames, "the recorders live");
            End();
        }

        // ---- (a′) the hierarchy — a root of 100 · 1,000 children (N 100), written down
        [UnityTest]
        public IEnumerator GP02_Hierarchy([Values(100, 1000)] int children)
        {
            yield return null;
            yield return Baseline();
            const int n = 100;
            var players = new List<FeedbackPlayer>(n);
            for (int i = 0; i < n; i++)
            {
                GameObject root = Go("root");
                for (int c = 0; c < children; c++)
                    new GameObject("c").transform.SetParent(root.transform, false);
                players.Add(PlayerOn(root, Item<PositionItem>(Punch)));
            }
            foreach (FeedbackPlayer p in players)
                p.Play();
            yield return Row("(a') children " + children, n, 0);
            Check(s_Last.Frames == Frames, "the recorders live");
            End();
        }

        // ---- (b) MPB Flash — the flash limit lets all N in; the prepared material's shader is in the build
        [UnityTest]
        public IEnumerator GP03_MaterialFlash()
        {
            yield return null;
            yield return Baseline();
            const int n = 1000;
            s_Test.Context.Categories.SetFlashLimit(4 * n);
            var material = Resources.Load<Material>("FeedbackTestMaterial");
            Check(material != null, "the prepared material");
            var players = new List<FeedbackPlayer>(n);
            for (int i = 0; i < n; i++)
            {
                GameObject go = Go("m", true);
                if (material != null)
                    go.GetComponent<Renderer>().sharedMaterial = material;
                go.transform.position = new Vector3(i % 40, i / 40, 30f);
                var item = Item<MaterialColorItem>(new FlashMotion { m_Params = new FlashParams { m_Target = new Vector4(1f, 0f, 0f, 1f), m_Attack = 0.5f, m_Hold = 0f, m_Release = 0.5f } });
                item.m_Timing.m_Duration = 1f;
                item.m_Timing.m_RepeatForever = true;
                item.m_WriteMode = MaterialWriteMode.PropertyBlock;
                players.Add(PlayerOn(go, item));
            }
            int accepted = 0;
            foreach (FeedbackPlayer p in players)
            {
                if (p.Play() != default)
                    accepted++;
            }
            Check(accepted == n, "accepted " + accepted);
            yield return Row("(b) mpb flash", n, 0);
            Check(s_Last.Revert > 0 && s_Last.Late > 0, "the blocks live — revert and apply");
            Budget("mpb", 2000);
            End();
        }

        // ---- (c) light Shake within 1 µs
        [UnityTest]
        public IEnumerator GP04_LightShake()
        {
            yield return null;
            yield return Baseline();
            const int n = 1000;
            var players = new List<FeedbackPlayer>(n);
            for (int i = 0; i < n; i++)
            {
                GameObject go = Go("l");
                Light light = go.AddComponent<Light>();
                light.enabled = false;
                var item = Item<LightIntensityItem>(new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = Vector4.one, m_Frequency = 10f } });
                item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = light, m_Path = "", m_Anchor = "" };
                players.Add(PlayerOn(go, item));
            }
            foreach (FeedbackPlayer p in players)
                p.Play();
            yield return Row("(c) light shake", n, 0);
            Budget("light", 1000);
            End();
        }

        // ---- (d) the empty runner — N 1,000 · 5,000 within 0.1 µs
        [UnityTest]
        public IEnumerator GP05_EmptyRunner([Values(1000, 5000)] int n)
        {
            yield return null;
            yield return Baseline();
            s_Test.Reserve(n);
            for (int i = 0; i < n; i++)
                s_Test.Add(new EmptyRunner(), 0);
            yield return Row("(d) empty runner " + n, n, 0);
            Budget("empty runner " + n, 100);
            End();
        }

        // ---- (r) the drivers · (s) the camera shakes — written down
        [UnityTest]
        public IEnumerator GP06_DriversAndShakes([Values(0, 1, 2, 3, 4)] int kind)
        {
            yield return null;
            yield return Baseline();
            int n = kind < 3 ? 1000 : 100;
            if (kind < 2)
            {
                for (int i = 0; i < n; i++)
                {
                    GameObject go = Go("d");
                    go.SetActive(false);
                    var d = go.AddComponent<MotionDriver>();
                    d.m_Mode = kind == 0 ? MotionDriverMode.Loop : MotionDriverMode.Follow;
                    d.m_Item = kind == 0
                        ? Item<PositionItem>(new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = Vector4.one, m_Frequency = 1f } })
                        : Item<PositionItem>(new SpringMotion { m_Params = new SpringParams { m_Mode = SpringMode.MoveTo, m_Frequency = 6f, m_Damping = 0.4f, m_Settle = 0.001f } });
                    go.SetActive(true);
                    if (kind == 1)
                        d.SetTarget(new Vector4(1000f, 0f, 0f, 0f));
                }
            }
            else
            {
                int receivers = kind == 2 ? 0 : kind == 3 ? 1 : 4;
                for (int i = 0; i < receivers; i++)
                {
                    GameObject cam = Go("cam" + i);
                    cam.SetActive(false);
                    cam.AddComponent<CameraShakeReceiver>().m_Channel = ChannelRef.Of(2);
                    cam.SetActive(true);
                }
                var players = new List<FeedbackPlayer>(n);
                for (int i = 0; i < n; i++)
                {
                    var item = Item<CameraShakeItem>(new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = new Vector4(1f, 0f, 0f, 0f), m_Frequency = 10f } });
                    item.m_RotationAmplitude = new Vector3(0f, 0f, 5f);
                    item.m_Target = receivers == 0
                        ? new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = Go("c").transform, m_Path = "", m_Anchor = "" }
                        : new TargetRef { m_Mode = TargetRefMode.Channel, m_Channel = ChannelRef.Of(2), m_Path = "", m_Anchor = "" };
                    players.Add(PlayerOn(Go("s"), item));
                }
                foreach (FeedbackPlayer p in players)
                    p.Play();
            }
            string[] names = { "(r) driver loop", "(r) driver follow", "(s) shake direct", "(s) shake channel 1", "(s) shake channel 4" };
            yield return Row(names[kind], n, 0);
            Check(s_Last.Frames == Frames, "the recorders live");
            End();
        }

        // ---- (p) one prepare and one Play — items 1 · 16 · 64 (a stopwatch), written down
        [UnityTest]
        public IEnumerator GP07_PrepareAndPlay([Values(1, 16, 64)] int items)
        {
            yield return null;
            int n = items == 1 ? 1000 : items == 16 ? 100 : 25;
            var players = new List<FeedbackPlayer>(n);
            for (int i = 0; i < n; i++)
            {
                var p = Go("pp").AddComponent<FeedbackPlayer>();
                p.m_Initialization = PlayerInitialization.Lazy;
                for (int k = 0; k < items; k++)
                    p.m_Items.Add(Item<PositionItem>(Punch));
                players.Add(p);
            }
            yield return null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (FeedbackPlayer p in players)
                p.EnsureRuntime();
            double prepareUs = sw.Elapsed.TotalMilliseconds * 1000.0 / n;
            yield return null;
            sw.Restart();
            foreach (FeedbackPlayer p in players)
                p.Play();
            double playNs = sw.Elapsed.TotalMilliseconds * 1e6 / n;
            Debug.Log("PERF " + Where + "(p) prepare and play items=" + items + " n=" + n + " prepareUs=" + F(prepareUs) + " playNs=" + F(playNs) + " perItemNs=" + F(playNs / items));
            Check(prepareUs > 0 && playNs > 0, "the stopwatch");
            End();
        }

        // ---- the frame allocation — the rows of this run (GP01 ~ GP06, first in the class's order) against the no-play window
        // measured the same way; the baseline holds to the run's end; the positive controls (a runner that allocates each frame ·
        // 100 kB once) are seen. A play-independent allocation each frame lifts the baseline too — the cycle delegates of
        // PlayerGateTests see that one
        [UnityTest]
        public IEnumerator GP08_FrameAllocation()
        {
            yield return null;
            yield return Baseline();
            yield return Settled("(j) no play at the end", 1, 0);
            long end = s_Last.Alloc;
            s_Test.Reserve(2);
            s_Test.Add(new AllocatingRunner(false), 0);
            yield return Measure("(j) allocating runner", 1, 0);
            long control = s_Last.Alloc;
            Check(s_Last.Frames == Frames, "the counter lives — frames " + s_Last.Frames);
            s_Test.Shutdown();
            s_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = s_Test;
            s_Test.Reserve(2);
            s_Test.Add(new AllocatingRunner(true), 0);
            yield return Measure("(j) 100 kB once", 1, 0);
            long counter = s_Last.Alloc;
            if (k_Judge)
            {
                Check(Math.Abs(end - s_Baseline) <= Equal, "the baseline holds — " + s_Baseline + " → " + end + " B");
                Check(control - s_Baseline >= Frames * 16, "the control is seen — " + (control - s_Baseline) + " B");
                Check(counter - s_Baseline >= 100000 - Equal, "the counter sees 100 kB — " + (counter - s_Baseline) + " B");
                Check(s_Rows.Count > 0, "rows of this run " + s_Rows.Count);
                foreach (KeyValuePair<string, long> row in s_Rows)
                {
                    if (Math.Abs(row.Value - s_Baseline) > Equal)
                        m_Fail.Add(row.Key + " " + row.Value + " vs " + s_Baseline + " B");
                }
            }
            End();
        }

        // ---- the layer's core paths — (t) material Float, an offset through the layer to the block (MPB) at LateApply, within
        // 3 µs · (u) the sprite colour, a flash on the override slot with its component mask, within 1 µs. They run after
        // GP08_FrameAllocation (the names' order), so each judges its own window against the baseline
        [UnityTest]
        public IEnumerator GP17_MaterialFloat([Values(100, 1000)] int n)
        {
            yield return null;
            yield return Baseline();
            var material = Resources.Load<Material>("FeedbackTestMaterial");
            Check(material != null, "the prepared material");
            var players = new List<FeedbackPlayer>(n);
            for (int i = 0; i < n; i++)
            {
                GameObject go = Go("t", true);
                if (material != null)
                    go.GetComponent<Renderer>().sharedMaterial = material;
                go.transform.position = new Vector3(i % 40, i / 40, 30f);
                var item = Item<MaterialFloatItem>(new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(0.4f, 0f, 0f, 0f), m_Frequency = 2f } });
                item.m_PropertyName = "_Cutoff";
                item.m_WriteMode = MaterialWriteMode.PropertyBlock;
                players.Add(PlayerOn(go, item));
            }
            Played(players);
            yield return Settled("(t) material float " + n, n, 0);
            Check(PerPlay(s_Last.Late, n) >= LayerFloor, "the layer applies — LateApply " + F(PerPlay(s_Last.Late, n)) + " ns a play");
            Budget("material float " + n, 3000);
            Held("(t) material float " + n);
            End();
        }

        [UnityTest]
        public IEnumerator GP18_SpriteColor([Values(100, 1000)] int n)
        {
            yield return null;
            yield return Baseline();
            // the flash limit counts the starts of the last unscaled second, and a player's real frames are far shorter than
            // the captured 1/60 s: each one-second flash starts many times in a real second — the limit sits above two windows'
            // starts so that none is held
            s_Test.Context.Categories.SetFlashLimit(16 * n);
            var players = new List<FeedbackPlayer>(n);
            for (int i = 0; i < n; i++)
            {
                GameObject go = Go("u");
                go.AddComponent<SpriteRenderer>();
                players.Add(PlayerOn(go, Repeated(Item<SpriteColorItem>(Flash(new Vector4(1f, 0f, 0f, 1f))))));
            }
            Played(players);
            yield return Settled("(u) sprite color " + n, n, 0);
            Check(PerPlay(s_Last.Late, n) >= LayerFloor, "the layer applies — LateApply " + F(PerPlay(s_Last.Late, n)) + " ns a play");
            Budget("sprite color " + n, 1000);
            Held("(u) sprite color " + n);
            End();
        }
    }
}
