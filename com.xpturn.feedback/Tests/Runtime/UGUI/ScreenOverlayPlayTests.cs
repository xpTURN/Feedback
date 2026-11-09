using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Perf = xpTURN.Feedback.Tests.PlayerPerformanceTests;

namespace xpTURN.Feedback.Tests
{
    // the floating text in play (read where it shows — the canvas update, after LateApply) · the progress bar's runner · the
    // frame allocation in real frames (the frames that allocate against the run's median idle frame — the Intensity text is the
    // editor's TMP string) · the cost records. Each test runs on a fresh test scheduler set as the default (the loop runs it) —
    // an overlay of its own, no text of another test alive
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    public sealed class ScreenOverlayPlayTests
    {
        static long s_Baseline = -1;
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<Object> m_Made = new List<Object>();
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
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Players.Clear();
            m_Made.Clear();
            yield return null;
            FeedbackScheduler.Default = m_Saved;
            if (!m_Test.IsShutdown)
                m_Test.Shutdown();
            Time.captureDeltaTime = 0f;
        }

        static void NeedTMP()
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
                Assert.Ignore("TMP Essential Resources are not in the project");
        }

        sealed class CameraMover : MonoBehaviour
        {
            void LateUpdate() => transform.position += new Vector3(0.05f, 0f, 0f);
        }

        Camera MainCamera(bool moves)
        {
            var go = new GameObject("OT main camera");
            m_Made.Add(go);
            var cam = go.AddComponent<Camera>();
            go.tag = "MainCamera";
            go.transform.position = new Vector3(0f, 0f, -10f);
            if (moves)
                go.AddComponent<CameraMover>();
            return cam;
        }

        FeedbackPlayer Player(params FeedbackItem[] items)
        {
            var go = new GameObject("OT player");
            m_Made.Add(go);
            go.SetActive(false);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            go.SetActive(true);
            p.EnsureRuntime();
            m_Players.Add(p);
            return p;
        }

        FloatingTextStyle Style(float life, float distance = 0f)
        {
            var st = ScriptableObject.CreateInstance<FloatingTextStyle>();
            m_Made.Add(st);
            st.m_Distance = new Vector2(0f, distance);
            st.m_Lifetime = new Vector2(life, life);
            return st;
        }

        static FloatingTextItem Text(FloatingTextStyle style, FloatingTextSource source = FloatingTextSource.Text, int pool = 4)
            => new FloatingTextItem
            {
                m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" },
                m_Motion = new SetMotion { m_Params = new SetParams { m_Value = Vector4.one } },
                m_Style = style,
                m_Source = source,
                m_Text = "Hi",
                m_PoolSize = pool,
            };

        static ScreenFlashItem Flash(float duration)
        {
            var item = new ScreenFlashItem { m_Color = Color.white, m_Motion = new FlashMotion { m_Params = new FlashParams { m_Target = Vector4.one, m_Attack = 0.25f, m_Hold = 0.5f, m_Release = 0.25f } } };
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static ScreenFadeItem Fade(float to, ScreenFadeShape shape, float duration)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = new Vector4(to, to, to, to);
            var item = new ScreenFadeItem { m_Shape = shape, m_Motion = new TweenMotion { m_Params = p } };
            item.m_Timing.m_Duration = duration;
            return item;
        }

        // a punch on the player's own position keeps a play running (a Revert stop has a play to stop)
        static PositionItem Keeper(float duration)
        {
            var item = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(0.5f, 0.5f, 0.5f, 0f), m_Vibrato = 2f, m_Decay = 1f } } };
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static Vector2 Projected(ScreenOverlayService svc, Camera cam, Vector3 world)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(svc.Texts, cam.WorldToScreenPoint(world), null, out Vector2 local);
            return local;
        }

        // ---- the place at render time while the camera moves in LateUpdate — from the overlay's first frame (the scaler's
        // factor) · behind the camera hidden · no play position: the target + the offset · following · no main camera hidden

        [UnityTest]
        public IEnumerator OT12a_ThePlaceAtRenderTime()
        {
            NeedTMP();
            LogAssert.ignoreFailingMessages = true;
            Time.captureDeltaTime = 1f / 60f;
            Camera cam = MainCamera(true);
            FeedbackPlayer p = Player(Text(Style(2f)));
            ScreenOverlayService svc = ScreenOverlayService.Of(m_Test.Context);
            var world = new Vector3(1f, 0f, 0f);
            p.Play(PlayArgs.Default.WithPosition(world));
            int frames = 0, match = 0;
            void Check()
            {
                if (svc.LiveCount == 0)
                    return;
                frames++;
                if ((svc.LiveAt(0).Rect.anchoredPosition - Projected(svc, cam, world)).magnitude < 0.5f)
                    match++;
            }
            Canvas.willRenderCanvases += Check;
            try
            {
                for (int f = 0; f < 20; f++)
                    yield return null;
            }
            finally
            {
                Canvas.willRenderCanvases -= Check;
            }
            float shown = svc.LiveAt(0).Group.alpha;
            p.Play(PlayArgs.Default.WithPosition(cam.transform.position + new Vector3(0f, 0f, -5f)));
            yield return null;
            yield return null;
            float behind = svc.LiveAt(svc.LiveCount - 1).Group.alpha;
            Assert.That(frames, Is.GreaterThanOrEqualTo(19), "the frames seen");
            Assert.That(match, Is.EqualTo(frames), "the place at render time — from the overlay's first frame");
            Assert.That(shown, Is.GreaterThan(0.9f), "the group shows the text");
            Assert.That(behind, Is.EqualTo(0f), "behind the camera hidden");
        }

        [UnityTest]
        public IEnumerator OT12b_TheTargetFollowingAndNoCamera()
        {
            NeedTMP();
            LogAssert.ignoreFailingMessages = true;
            Time.captureDeltaTime = 1f / 60f;
            Camera cam = MainCamera(false);
            FloatingTextItem still = Text(Style(2f));
            still.m_Offset = new Vector3(0f, 1f, 0f);
            FloatingTextItem follows = Text(Style(2f));
            follows.m_Follow = true;
            FeedbackPlayer a = Player(still);
            FeedbackPlayer b = Player(follows);
            a.transform.position = new Vector3(-1f, 0f, 0f);
            b.transform.position = new Vector3(1f, 0f, 0f);
            ScreenOverlayService svc = ScreenOverlayService.Of(FeedbackScheduler.Default.Context);
            int live = svc.LiveCount;
            a.Play();
            b.Play();
            yield return null;
            b.transform.position = new Vector3(1f, -1f, 0f);
            Vector2 placeA = default, placeB = default, wantA = default, wantB = default;
            void Read()
            {
                placeA = svc.LiveAt(live).Rect.anchoredPosition;
                placeB = svc.LiveAt(live + 1).Rect.anchoredPosition;
                wantA = Projected(svc, cam, new Vector3(-1f, 1f, 0f));
                wantB = Projected(svc, cam, new Vector3(1f, -1f, 0f));
            }
            Canvas.willRenderCanvases += Read;
            yield return null;
            Canvas.willRenderCanvases -= Read;
            Assert.That((placeA - wantA).magnitude, Is.LessThan(0.5f), "no play position — the target + the offset");
            Assert.That((placeB - wantB).magnitude, Is.LessThan(0.5f), "following the target");
            cam.gameObject.tag = "Untagged";
            yield return null;
            Assert.That((svc.LiveAt(live).Group.alpha, svc.LiveAt(live + 1).Group.alpha), Is.EqualTo((0f, 0f)), "no main camera hidden");
            cam.gameObject.tag = "MainCamera";
        }

        // ---- the characters — the text · the intensity × the multiplier in the format (the truth from .NET's ToString) · a
        // format that does not read is refused · no raycast

        [UnityTest]
        public IEnumerator OT13a_TheCharacters()
        {
            NeedTMP();
            LogAssert.ignoreFailingMessages = true;
            Time.captureDeltaTime = 1f / 60f;
            MainCamera(false);
            FloatingTextItem number = Text(Style(2f), FloatingTextSource.Intensity);
            number.m_Format = "{0:#,0.0} pts";
            number.m_Multiplier = 2f;
            FeedbackPlayer t = Player(Text(Style(2f)));
            FeedbackPlayer n = Player(number);
            FeedbackPlayer bad = Player(new FloatingTextItem
            {
                m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" },
                m_Motion = new SetMotion(),
                m_Source = FloatingTextSource.Intensity,
                m_Format = "{1}",
            });
            ScreenOverlayService svc = ScreenOverlayService.Of(FeedbackScheduler.Default.Context);
            int live = svc.LiveCount;
            t.Play();
            n.Play(PlayArgs.Default.WithIntensity(1234.56f));
            yield return null;
            string want = (1234.56f * 2f).ToString("#,0.0", CultureInfo.InvariantCulture) + " pts";
            Assert.That((svc.LiveAt(live).Text.text, svc.LiveAt(live + 1).Text.text), Is.EqualTo(("Hi", want)));
            Assert.That((svc.LiveAt(live).Text.raycastTarget, svc.LiveAt(live + 1).Text.raycastTarget), Is.EqualTo((false, false)), "no raycast");
            Assert.That(bad.Runtime.Binding(0), Is.Null, "a format that does not read is refused");
        }

        // ---- the pool — reserved at the preparation (the template's own count · awake · not hidden · the UI layer) · back at the
        // lifetime's end · used again · one more when it runs out · a style's prefab warmed · a Revert stop (with a keeper) · a
        // Restore

        [UnityTest]
        public IEnumerator OT14a_ThePool()
        {
            NeedTMP();
            LogAssert.ignoreFailingMessages = true;
            Time.captureDeltaTime = 1f / 60f;
            MainCamera(false);
            FloatingTextStyle style = Style(0.2f);
            FeedbackPlayer p = Player(Text(style, FloatingTextSource.Text, 4));
            FeedbackPlayer q = Player(Text(style, FloatingTextSource.Intensity, 4));
            FeedbackPlayer keeper = Player(Text(style, FloatingTextSource.Text, 2), Keeper(2f));
            FeedbackContext context = FeedbackScheduler.Default.Context;
            ScreenOverlayService svc = ScreenOverlayService.Of(context);
            PoolService pools = context.PoolsIfMade;
            GameObject template = svc.TemplateOf(style);
            FloatingTextInstance[] made = pools.Container.GetComponentsInChildren<FloatingTextInstance>(true).Where(i => i.name.StartsWith(template.name, StringComparison.Ordinal)).ToArray();
            Assert.That(pools.MadeOf(template), Is.EqualTo(4), "the largest pool size");
            Assert.That(svc.LiveCapacity, Is.GreaterThanOrEqualTo(10), "the service's live list holds what the preparations reserved");
            Assert.That(made.Length, Is.EqualTo(4));
            Assert.That(made.Select(i => i.Text.mesh != null && i.gameObject.hideFlags == HideFlags.None && i.gameObject.layer == ScreenOverlayService.UILayer && !i.gameObject.activeSelf).Distinct(),
                Is.EqualTo(new[] { true }), "warmed — awake · not hidden · the UI layer · asleep");
            int live = svc.LiveCount;
            p.Play();
            yield return null;
            Assert.That((svc.LiveCount - live, pools.MadeOf(template) - pools.RestingOf(template)), Is.EqualTo((1, 1)), "one spawned — the service's list and the pool agree");
            Assert.That(svc.LiveAt(live).transform.parent, Is.SameAs(svc.Texts));
            for (int f = 0; f < 30; f++)
                yield return null;
            Assert.That((svc.LiveCount, pools.RestingOf(template)), Is.EqualTo((live, 4)), "back at the lifetime's end");
            for (int i = 0; i < 5; i++)
            {
                p.Play();
                yield return null;
            }
            Assert.That((pools.MadeOf(template), pools.GrownOf(template)), Is.EqualTo((5, 1)), "one more when it runs out");
            for (int f = 0; f < 30; f++)
                yield return null;
            keeper.Play();
            yield return null;
            int spawned = svc.LiveCount - live;
            keeper.Stop(StopMode.Revert);
            yield return null;
            int afterRevert = svc.LiveCount - live;
            q.Play(PlayArgs.Default.WithIntensity(7f));
            yield return null;
            int afterNumber = svc.LiveCount - live;
            q.Restore();
            yield return null;
            Assert.That((spawned, afterRevert), Is.EqualTo((1, 0)), "a Revert stop gives the text back");
            Assert.That((afterNumber, svc.LiveCount - live), Is.EqualTo((1, 0)), "a Restore gives the text back");

            var prefab = new GameObject("OT14 prefab", typeof(RectTransform));
            m_Made.Add(prefab);
            prefab.AddComponent<TextMeshProUGUI>();
            prefab.SetActive(false);
            FloatingTextStyle styled = Style(0.2f);
            styled.m_Prefab = prefab;
            Player(Text(styled, FloatingTextSource.Text, 2));
            FloatingTextInstance[] warmed = pools.Container.GetComponentsInChildren<FloatingTextInstance>(true).Where(i => i.name.StartsWith(prefab.name, StringComparison.Ordinal)).ToArray();
            Assert.That((pools.MadeOf(prefab), warmed.Length, warmed.All(i => i.GetComponent<CanvasGroup>() != null && i.Text != null)), Is.EqualTo((2, 2, true)), "a style's prefab is the template — warmed with its parts");
        }

        // ---- the motion over the lifetime — the place · the scale · the group alpha · the tint follow one t of the style's
        // curves (read by the test) · t moves by the frame's step · the text's own color is not written

        [UnityTest]
        public IEnumerator OT15a_TheMotion()
        {
            NeedTMP();
            LogAssert.ignoreFailingMessages = true;
            Time.captureDeltaTime = 1f / 60f;
            Camera cam = MainCamera(false);
            FloatingTextStyle style = Style(1f, 100f);
            style.m_Scale = AnimationCurve.Linear(0f, 1f, 1f, 2f);
            style.m_Alpha = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            style.m_Color = new Gradient();
            style.m_Color.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.red, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            FeedbackPlayer p = Player(Text(style));
            ScreenOverlayService svc = ScreenOverlayService.Of(FeedbackScheduler.Default.Context);
            int live = svc.LiveCount;
            var world = new Vector3(0f, -1f, 0f);
            p.Play(PlayArgs.Default.WithPosition(world));
            var ts = new List<float>();
            var fails = new List<string>();
            void Check()
            {
                if (svc.LiveCount <= live)
                    return;
                FloatingTextInstance fi = svc.LiveAt(live);
                float t = (fi.Rect.anchoredPosition.y - Projected(svc, cam, world).y) / 100f;
                ts.Add(t);
                Color tint = Color.Lerp(Color.white, Color.red, t);
                if (Mathf.Abs(fi.Rect.localScale.x - (1f + t)) > 1e-3f || Mathf.Abs(fi.Group.alpha - (1f - t)) > 1e-3f)
                    fails.Add($"t {t:0.000} scale {fi.Rect.localScale.x:0.000} alpha {fi.Group.alpha:0.000}");
                foreach (CanvasRenderer r in fi.Renderers)
                {
                    if (Mathf.Abs(r.GetColor().g - tint.g) > 0.01f)
                        fails.Add($"t {t:0.000} tint {r.GetColor()}");
                }
                if (fi.Text.color != Color.white || fi.Text.alpha != 1f)
                    fails.Add("the text's own color written " + fi.Text.color);
            }
            Canvas.willRenderCanvases += Check;
            try
            {
                for (int f = 0; f < 30; f++)
                    yield return null;
            }
            finally
            {
                Canvas.willRenderCanvases -= Check;
            }
            var steps = new List<float>();
            for (int i = 1; i < ts.Count; i++)
                steps.Add(ts[i] - ts[i - 1]);
            Assert.That(ts.Count, Is.GreaterThanOrEqualTo(28), "the frames seen");
            Assert.That(ts.Last(), Is.GreaterThan(0.3f).And.LessThan(0.7f), "t over half a lifetime");
            Assert.That(steps, Is.All.EqualTo(1f / 60f).Within(1e-3f), "t moves by the frame's step");
            Assert.That(fails, Is.Empty);
        }

        // ---- the progress bar in play — a runner of the default scheduler (scaled time: batch's unscaled time is the wall clock)

        [UnityTest]
        public IEnumerator OT19b_TheRunnerInPlay()
        {
            Time.captureDeltaTime = 1f / 60f;
            var canvas = new GameObject("OT bar canvas", typeof(RectTransform));
            m_Made.Add(canvas);
            canvas.AddComponent<Canvas>();
            var root = new GameObject("bar", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            var fg = new GameObject("fg", typeof(RectTransform));
            fg.transform.SetParent(root.transform, false);
            root.SetActive(false);
            var bar = root.AddComponent<ProgressBar>();
            bar.m_Foreground = (RectTransform)fg.transform;
            bar.m_Unscaled = false;
            root.SetActive(true);
            int ended = 0;
            bar.MoveEnded += _ => ended++;
            yield return null;
            bool running = bar.Running;
            bar.SetValue(0.5f);
            float first = bar.ShownOf(0);
            for (int f = 0; f < 60; f++)
                yield return null;
            root.SetActive(false);
            bool halted = !bar.Running;
            Assert.That(running, Is.True, "a runner while enabled in play");
            Assert.That(first, Is.EqualTo(1f), "animated — not written at once");
            Assert.That(new[] { bar.ShownOf(0), fg.transform.localScale.x }, Is.All.EqualTo(0.5f).Within(1e-4f), "moved to the value");
            Assert.That((ended, halted), Is.EqualTo((1, true)), "its event · off when disabled");
        }

        // ---- the real loop's frames — flashes · covers · text spawns from rest (everything off, the anchors on) — against the
        // run's median idle frame, with a write in the window from each part (the positive) and the positive control

        [UnityTest]
        public IEnumerator OT23a_FrameAllocation([Values(false, true)] bool numbers)
        {
            NeedTMP();
            LogAssert.ignoreFailingMessages = true;
            Time.captureDeltaTime = 1f / 60f;
            MainCamera(false);
            FloatingTextStyle style = Style(0.3f, 50f);
            FeedbackPlayer flash = Player(Flash(0.3f));
            FeedbackPlayer cover = Player(Fade(1f, ScreenFadeShape.Iris, 0.2f));
            FeedbackPlayer reveal = Player(Fade(0f, ScreenFadeShape.Wipe, 0.2f));
            FeedbackPlayer text = Player(Text(style, numbers ? FloatingTextSource.Intensity : FloatingTextSource.Text, 8), Keeper(5f));
            ScreenOverlayService svc = ScreenOverlayService.Of(FeedbackScheduler.Default.Context);
            using ProfilerRecorder rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            yield return null;
            flash.Play();
            cover.Play();
            text.Play(PlayArgs.Default.WithIntensity(1234f));
            for (int f = 0; f < 20; f++)
                yield return null;
            reveal.Play();
            text.Stop(StopMode.Revert);
            for (int f = 0; f < 40; f++)
                yield return null;
            Image image = svc.RentedAt(svc.RentedCount - 1);
            int live = svc.LiveCount;
            bool rest = !image.enabled && !svc.Cover.enabled && !svc.Iris.enabled && live == 0;
            int moved = 0;
            bool watching = false;
            Canvas.WillRenderCanvases watch = () =>
            {
                if (watching)
                    moved |= (image.enabled && image.canvasRenderer.GetAlpha() > 0f ? 1 : 0) | (svc.Fade > 0f ? 2 : 0) | (svc.LiveCount > live ? 4 : 0);
            };
            Canvas.willRenderCanvases += watch;
            var idle = new List<long>();
            for (int f = 0; f < 31; f++)
            {
                yield return null;
                idle.Add(rec.LastValue);
            }
            idle.Sort();
            long baseline = idle[15];
            int idleAllocating = idle.Count(v => v - baseline >= 64);
            watching = true;
            int allocating = 0;
            long over = 0;
            for (int f = 0; f < 120; f++)
            {
                if (f % 30 == 0)
                    flash.Play();
                if (f % 60 == 0)
                    cover.Play();
                if (f % 60 == 30)
                    reveal.Play();
                if (f % 6 == 0)
                    text.Play(PlayArgs.Default.WithIntensity(1000f + f));
                yield return null;
                long d = rec.LastValue - baseline;
                if (d >= 64)
                {
                    allocating++;
                    over += d;
                }
            }
            watching = false;
            int positive = 0;
            for (int f = 0; f < 5; f++)
            {
                _ = new byte[8192];
                yield return null;
                if (rec.LastValue - baseline >= 4000)
                    positive++;
            }
            Canvas.willRenderCanvases -= watch;
            Debug.Log($"PROBE OT23 numbers {numbers} · rest {rest} · baseline {baseline} · idle allocating frames {idleAllocating}/31 · allocating frames {allocating}/120 ({over} B) · positive {positive}/5 · moved {moved}");
            Assert.That(rest, Is.True, "the window starts from rest — every graphic off but the anchors");
            Assert.That(moved, Is.EqualTo(7), "every part wrote in the window — the positive");
            Assert.That(positive, Is.GreaterThanOrEqualTo(4), "the positive control");
            // the number's string is TMP's in the editor only (SetText(char[]) keeps no string in a player)
            if (!numbers || !Application.isEditor)
            {
#if UNITY_6000_3_OR_NEWER || !UNITY_EDITOR
                Assert.That(allocating, Is.LessThanOrEqualTo(idleAllocating + 3), "from rest the screen plays allocate nothing");
#else
                Assert.Ignore("6000.0's editor loop allocates in FlushDirty (EditorCompilationInterface.IsCompiling) in each frame the scene changed — the IL2CPP player judges the allocation");
#endif
            }
        }

        static IEnumerator Baseline()
        {
            if (s_Baseline >= 0)
                yield break;
            yield return Perf.Measure("(x) no play", 1, 0);
            long first = Perf.s_Last.Alloc;
            yield return Perf.Measure("(x) no play again", 1, 0);
            s_Baseline = Math.Min(first, Perf.s_Last.Alloc);
        }

        // ---- the records — a screen flash (an image's alpha per play) · a floating text (its place each frame per spawn) · per
        // play ns within each row's budget and the window's allocation against the baseline in an IL2CPP player

        static readonly string[] s_Costs = { "(x) screen flash", "(x) floating text" };
        static readonly double[] s_Budgets = { 900, 1250 };

        [UnityTest]
        public IEnumerator OT24a_Costs([Values(0, 1)] int kind, [Values(100, 1000)] int n)
        {
            if (kind == 1)
                NeedTMP();
            FeedbackScheduler saved = FeedbackScheduler.Default;
            var test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = test;
            Time.captureDeltaTime = 1f / 60f;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                yield return null;
                yield return Baseline();
                test.Context.Categories.SetFlashLimit(4 * n);
                MainCamera(false);
                FloatingTextStyle style = Style(30f, 100f);
                var players = new List<FeedbackPlayer>(n);
                for (int i = 0; i < n; i++)
                {
                    FeedbackItem item;
                    if (kind == 0)
                    {
                        var f = new ScreenFlashItem { m_Motion = new FlashMotion { m_Params = new FlashParams { m_Target = Vector4.one, m_Attack = 0.5f, m_Hold = 0f, m_Release = 0.5f } } };
                        f.m_Timing.m_Duration = 20f;
                        item = f;
                    }
                    else
                        item = Text(style, FloatingTextSource.Text, i == 0 ? n : 1);
                    players.Add(Player(item));
                }
                ScreenOverlayService svc = ScreenOverlayService.Of(test.Context);
                // the flashes' canvas off: a thousand full-screen images is the canvas's draw and its window allocation, not the
                // per-play cost (the writes go on — the positive below)
                if (kind == 0)
                    svc.Canvas.enabled = false;
                int prepared = 0, accepted = 0;
                foreach (FeedbackPlayer p in players)
                {
                    if (p.Runtime != null && p.Runtime.Binding(0) != null)
                        prepared++;
                    if (p.Play() != default)
                        accepted++;
                }
                int seen = 0;
                void Sample()
                {
                    if (kind == 0)
                        seen = Math.Max(seen, svc.RentedCount > 0 && svc.RentedAt(0).enabled ? 1 : 0);
                    else
                        seen = Math.Max(seen, svc.LiveCount);
                }
                Canvas.willRenderCanvases += Sample;
                try
                {
                    yield return Perf.Settled(s_Costs[kind] + " " + n, n, 0, s_Baseline, s_Budgets[kind]);
                }
                finally
                {
                    Canvas.willRenderCanvases -= Sample;
                }
                Perf.Window w = Perf.s_Last;
                Assert.That(seen, Is.EqualTo(kind == 0 ? 1 : n), "the plays write in the window — the positive");
                Assert.That(prepared + " · " + accepted, Is.EqualTo(n + " · " + n), "prepared · accepted");
                Assert.That(w.PerPlay, Is.GreaterThan(0), "per play ns (written down)");
                Perf.RowBudget(s_Costs[kind] + " " + n, s_Budgets[kind]);
                if (Perf.k_Judge)
                    Assert.That(Math.Abs(w.Alloc - s_Baseline), Is.LessThanOrEqualTo(Perf.Equal), "the window " + w.Alloc + " vs " + s_Baseline + " B");
                foreach (FeedbackPlayer p in players)
                    p.Stop();
            }
            finally
            {
                FeedbackScheduler.Default = saved;
                if (!test.IsShutdown)
                    test.Shutdown();
            }
        }
    }
}
