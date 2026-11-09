using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Perf = xpTURN.Feedback.Tests.PlayerPerformanceTests;

namespace xpTURN.Feedback.Tests
{
    // the real loop (read where it shows — the canvas update, after LateApply) · the layout window · the selection a Selectable
    // turned off loses (the EventSystem — play mode only) · the frame allocation in real frames (the frames that allocate
    // against the run's median idle frame) · the cost records
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    public sealed class UGUIPlayTests
    {
        static long s_Baseline = -1;
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<Object> m_Made = new List<Object>();

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
            Time.captureDeltaTime = 0f;
        }

        Canvas MakeCanvas()
        {
            var go = new GameObject("Canvas", typeof(RectTransform));
            m_Made.Add(go);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            return c;
        }

        static T UI<T>(Transform parent, Vector2 size) where T : Component
        {
            var go = new GameObject(typeof(T).Name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = size;
            return go.AddComponent<T>();
        }

        FeedbackPlayer Player(GameObject go, TargetItem item, Object target, FeedbackMotion motion, float duration)
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.Add(item);
            m_Players.Add(p);
            return p;
        }

        static OscillateMotion Swing(Vector4 a) => new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = a, m_Frequency = 2f } };

        static TweenMotion TweenTo(Vector4 to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = to;
            return new TweenMotion { m_Params = p };
        }

        static PunchMotion Punch(Vector4 a) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = a, m_Vibrato = 2f, m_Decay = 1f } };

        // ---- a color oscillates where it shows and ends at its base · the layout window: a size punch on a child the layout
        // sizes (seen after the layout — the frame's layout writes over it) and on a free one

        [UnityTest]
        public IEnumerator UG19_TheRealLoop()
        {
            Time.captureDeltaTime = 1f / 60f;
            Canvas c = MakeCanvas();
            Image image = UI<Image>(c.transform, new Vector2(80f, 80f));
            image.color = new Color(0.25f, 0.5f, 0.75f, 1f);
            Color base0 = image.color;
            var row = UI<HorizontalLayoutGroup>(c.transform, new Vector2(300f, 100f));
            row.childControlWidth = true;
            row.childControlHeight = false;
            row.childForceExpandWidth = true;
            var sized = UI<Image>(row.transform, new Vector2(50f, 50f));
            UI<Image>(row.transform, new Vector2(50f, 50f));
            var free = UI<Image>(c.transform, new Vector2(50f, 50f));
            ((RectTransform)free.transform).anchoredPosition = new Vector2(0f, -150f);
            for (int i = 0; i < 3; i++)
                yield return null;
            var sr = (RectTransform)sized.transform;
            var fr = (RectTransform)free.transform;
            Vector2 s0 = sr.sizeDelta, f0 = fr.sizeDelta;
            FeedbackPlayer color = Player(image.gameObject, new GraphicColorItem(), image, Punch(new Vector4(0.3f, 0.3f, 0.3f, 0f)), 0.5f);
            FeedbackPlayer sizePunch = Player(sized.gameObject, new RectSizeDeltaItem(), sr, Punch(new Vector4(30f, 30f, 0f, 0f)), 0.5f);
            FeedbackPlayer freePunch = Player(free.gameObject, new RectSizeDeltaItem(), fr, Punch(new Vector4(30f, 30f, 0f, 0f)), 0.5f);
            float colorMoved = 0f, sizedMoved = 0f, freeMoved = 0f;
            void Sample()
            {
                colorMoved = Mathf.Max(colorMoved, Mathf.Abs(image.color.r - base0.r));
                sizedMoved = Mathf.Max(sizedMoved, Mathf.Abs(sr.sizeDelta.y - s0.y));
                freeMoved = Mathf.Max(freeMoved, Mathf.Abs(fr.sizeDelta.x - f0.x));
            }
            Canvas.willRenderCanvases += Sample;
            try
            {
                yield return null;
                color.Play();
                sizePunch.Play();
                freePunch.Play();
                for (int f = 0; f < 60; f++)
                    yield return null;
            }
            finally
            {
                Canvas.willRenderCanvases -= Sample;
            }
            Assert.That(colorMoved, Is.GreaterThan(0.1f), "the color moved where it shows — after LateApply");
            Assert.That(image.color, Is.EqualTo(base0), "the end gives the base back");
            Assert.That(fr.sizeDelta, Is.EqualTo(f0), "a free size ends at its base");
            Assert.That(sr.sizeDelta.y, Is.EqualTo(s0.y), "the axis the layout does not drive ends at its base — no offset kept (judged by component)");
        }

        // ---- the Interactable item turns a selected Selectable off — the EventSystem drops the selection — and the revert gives
        // the interactable back (the selection stays dropped — uGUI's)

        [UnityTest]
        public IEnumerator UG20_TheSelectionGoes()
        {
            Canvas c = MakeCanvas();
            var es = new GameObject("EventSystem", typeof(EventSystem));
            m_Made.Add(es);
            Button button = UI<Button>(c.transform, new Vector2(80f, 40f));
            button.transition = Selectable.Transition.None;
            FeedbackPlayer p = Player(button.gameObject, new SelectableInteractableItem(), button, new SetMotion { m_Params = new SetParams { m_Value = Vector4.zero } }, 0f);
            var keeper = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = Punch(new Vector4(0.5f, 0.5f, 0.5f, 0f)) };
            keeper.m_Timing.m_Duration = 3f;
            p.m_Items.Add(keeper);
            yield return null;
            EventSystem.current.SetSelectedGameObject(button.gameObject);
            yield return null;
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(button.gameObject), "selected");
            p.Play();
            yield return null;
            yield return null;
            bool off = !button.interactable;
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            p.Stop(StopMode.Revert);
            yield return null;
            Assert.That((off, selected == null, button.interactable), Is.EqualTo((true, true, true)), "off · the selection dropped · back on");
        }

        // ---- the frame allocation in real frames — the UI plays allocate in no more frames past the run's median idle frame than
        // the idle window itself does, three to spare (6000.3's idle frames carry stray chunks) · the writes in the window · the
        // positive control

        [UnityTest]
        public IEnumerator UG21_FrameAllocation()
        {
            LogAssert.ignoreFailingMessages = true;
            Time.captureDeltaTime = 1f / 60f;
            Canvas c = MakeCanvas();
            Image image = UI<Image>(c.transform, new Vector2(80f, 80f));
            image.sprite = UGUIFixtures.MakeSprite(Color.red);
            image.type = Image.Type.Filled;
            CanvasGroup group = image.gameObject.AddComponent<CanvasGroup>();
            Text text = UI<Text>(c.transform, new Vector2(200f, 60f));
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = "G1";
            var rect = (RectTransform)image.transform;
            var players = new List<FeedbackPlayer>
            {
                Player(image.gameObject, new GraphicColorItem(), image, Punch(new Vector4(0.3f, 0.3f, 0.3f, 0f)), 3f),
                Player(image.gameObject, new ImageFillItem(), image, Punch(new Vector4(0.3f, 0f, 0f, 0f)), 3f),
                Player(image.gameObject, new CanvasGroupAlphaItem(), group, new FlashMotion { m_Params = new FlashParams { m_Target = Vector4.zero, m_Attack = 1f, m_Hold = 1f, m_Release = 1f } }, 3f),
                Player(image.gameObject, new GraphicCrossFadeItem(), image, TweenTo(new Vector4(1f, 1f, 1f, 0.2f)), 3f),
                Player(image.gameObject, new GraphicTextureOffsetItem(), image, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 3f),
                Player(image.gameObject, new RectSizeDeltaItem(), rect, Punch(new Vector4(20f, 20f, 0f, 0f)), 3f),
                Player(text.gameObject, new TextFontSizeItem(), text, Punch(new Vector4(1f, 0f, 0f, 0f)), 3f),
            };
            using ProfilerRecorder rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            yield return null;
            foreach (FeedbackPlayer p in players)
            {
                p.EnsureRuntime();
                p.Play();
            }
            for (int f = 0; f < 20; f++)
                yield return null;
            foreach (FeedbackPlayer p in players)
                p.Stop();
            for (int f = 0; f < 5; f++)
                yield return null;
            // the writes in the window, read where the layer values are (the canvas update — after LateApply): an allocation count
            // over plays that write nothing is green for nothing. Made and hooked before the idle baseline
            CanvasRenderer cr = image.canvasRenderer;
            int mainTex = Shader.PropertyToID("_MainTex");
            Color color0 = image.color, cr0 = cr.GetColor();
            float fill0 = image.fillAmount, alpha0 = group.alpha;
            Vector2 offset0 = image.material.GetTextureOffset(mainTex), size0 = rect.sizeDelta;
            int font0 = text.fontSize;
            int moved = 0;
            bool watching = false;
            Canvas.WillRenderCanvases watch = () =>
            {
                if (watching)
                    moved |= (image.color != color0 ? 1 : 0) | (image.fillAmount != fill0 ? 2 : 0) | (group.alpha != alpha0 ? 4 : 0) | (cr.GetColor() != cr0 ? 8 : 0)
                        | (image.material.GetTextureOffset(mainTex) != offset0 ? 16 : 0) | (rect.sizeDelta != size0 ? 32 : 0) | (text.fontSize != font0 ? 64 : 0);
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
            int idleAllocating = 0;
            foreach (long v in idle)
            {
                if (v - baseline >= 64)
                    idleAllocating++;
            }
            watching = true;
            foreach (FeedbackPlayer p in players)
                p.Play();
            int allocating = 0;
            for (int f = 0; f < 120; f++)
            {
                yield return null;
                if (f > 2 && rec.LastValue - baseline >= 64)
                    allocating++;
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
            Debug.Log($"PROBE UG21 baseline {baseline} · idle allocating frames {idleAllocating}/31 · allocating frames {allocating}/117 · positive {positive}/5 · moved {moved}");
            Assert.That(moved, Is.EqualTo(127), "every play wrote in the window — the positive");
#if UNITY_6000_3_OR_NEWER || !UNITY_EDITOR
            Assert.That(allocating, Is.LessThanOrEqualTo(idleAllocating + 3), "the UI plays allocate nothing once warm");
            Assert.That(positive, Is.GreaterThanOrEqualTo(4), "the positive control");
#else
            Assert.That(positive, Is.GreaterThanOrEqualTo(4), "the positive control");
            Assert.Ignore("6000.0's editor loop allocates in FlushDirty (EditorCompilationInterface.IsCompiling) in each frame the scene changed — the IL2CPP player judges the allocation");
#endif
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

        // ---- the cost records — a Graphic's color (the mesh rebuild at the canvas update) · a CanvasGroup's alpha over a
        // Selectable (its group check walks the parents on each change) · a size delta (the layout's dimension callback) · a
        // Text's font size (the text's rebuild) · per play ns within each row's budget and the window's allocation against the
        // baseline in an IL2CPP player

        static readonly string[] s_Costs = { "(x) ui graphic color", "(x) ui canvas group alpha", "(x) rect size delta", "(x) ui font size" };
        static readonly double[] s_Budgets = { 2250, 2250, 3750, 3500 };

        static PunchMotion Shake(Vector4 a) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = a, m_Vibrato = 30f, m_Decay = 0.01f } };

        [UnityTest]
        public IEnumerator UG22_Costs([Values(0, 1, 2, 3)] int kind, [Values(100, 1000)] int n)
        {
            FeedbackScheduler saved = FeedbackScheduler.Default;
            var test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = test;
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                yield return null;
                yield return Baseline();
                Canvas c = MakeCanvas();
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var players = new List<FeedbackPlayer>(n);
                Func<float> read = null;
                for (int i = 0; i < n; i++)
                {
                    TargetItem item;
                    Object target;
                    GameObject host;
                    switch (kind)
                    {
                        case 0:
                        {
                            Image image = UI<Image>(c.transform, new Vector2(10f, 10f));
                            item = new GraphicColorItem { m_Motion = Shake(new Vector4(0.3f, 0.3f, 0.3f, 0f)) };
                            target = image;
                            host = image.gameObject;
                            read = read ?? (() => image.color.r);
                            break;
                        }
                        case 1:
                        {
                            Image image = UI<Image>(c.transform, new Vector2(10f, 10f));
                            CanvasGroup group = image.gameObject.AddComponent<CanvasGroup>();
                            image.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
                            item = new CanvasGroupAlphaItem { m_Motion = TweenTo(Vector4.zero) };
                            target = group;
                            host = image.gameObject;
                            read = read ?? (() => group.alpha);
                            break;
                        }
                        case 2:
                        {
                            Image image = UI<Image>(c.transform, new Vector2(10f, 10f));
                            var rect = (RectTransform)image.transform;
                            item = new RectSizeDeltaItem { m_Motion = Shake(new Vector4(20f, 20f, 0f, 0f)) };
                            target = rect;
                            host = image.gameObject;
                            read = read ?? (() => rect.sizeDelta.x);
                            break;
                        }
                        default:
                        {
                            Text text = UI<Text>(c.transform, new Vector2(40f, 20f));
                            text.font = font;
                            text.text = "G11";
                            text.fontSize = 14;
                            item = new TextFontSizeItem { m_Motion = Shake(new Vector4(5f, 0f, 0f, 0f)) };
                            target = text;
                            host = text.gameObject;
                            read = read ?? (() => text.fontSize);
                            break;
                        }
                    }
                    item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
                    item.m_Timing.m_Duration = 20f;
                    var p = host.AddComponent<FeedbackPlayer>();
                    p.m_Items.Add(item);
                    p.EnsureRuntime();
                    players.Add(p);
                }
                int prepared = 0, accepted = 0;
                float start = read(), moved = 0f;
                void Sample() => moved = Mathf.Max(moved, Mathf.Abs(read() - start));
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
                    yield return Perf.Settled(s_Costs[kind] + " " + n, n, 0, s_Baseline, s_Budgets[kind]);
                }
                finally
                {
                    Canvas.willRenderCanvases -= Sample;
                }
                Perf.Window w = Perf.s_Last;
                Assert.That(moved, Is.GreaterThan(0.005f), "the plays write the value — the positive");
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
