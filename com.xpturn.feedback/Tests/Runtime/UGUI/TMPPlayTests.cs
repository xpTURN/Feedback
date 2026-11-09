using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
    // the real loop (read where it shows — the canvas update, after LateApply) · the frame allocation in real frames (the
    // frames that allocate against the run's median idle frame — Count To · Set Text are the editor's TMP string) · the cost
    // records
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    public sealed class TMPPlayTests
    {
        static long s_Baseline = -1;
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<Object> m_Made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
                Assert.Ignore("TMP Essential Resources are not in the project");
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

        static TextMeshProUGUI Text(Transform parent, string text, Vector2 size)
        {
            var go = new GameObject("tmp", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = size;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            return t;
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

        static TweenMotion TweenTo(Vector4 to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = to;
            return new TweenMotion { m_Params = p };
        }

        static PunchMotion Punch(Vector4 a) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = a, m_Vibrato = 2f, m_Decay = 1f } };

        static OscillateMotion Swing(Vector4 a) => new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = a, m_Frequency = 2f } };

        // ---- a TMP text's color · size · reveal · count move where they show and end at their base (the count's end stays — a
        // Persist; the revert gives the text back)

        [UnityTest]
        public IEnumerator TM19_TheRealLoop()
        {
            Time.captureDeltaTime = 1f / 60f;
            Canvas c = MakeCanvas();
            TextMeshProUGUI t = Text(c.transform, "Hello World", new Vector2(300f, 80f));
            TextMeshProUGUI n = Text(c.transform, "Score", new Vector2(300f, 80f));
            ((RectTransform)n.transform).anchoredPosition = new Vector2(0f, -100f);
            for (int i = 0; i < 3; i++)
                yield return null;
            Color color0 = t.color;
            float size0 = t.fontSize;
            FeedbackPlayer color = Player(t.gameObject, new GraphicColorItem(), t, Punch(new Vector4(0.3f, 0.3f, 0.3f, 0f)), 0.5f);
            FeedbackPlayer size = Player(t.gameObject, new TMPFontSizeItem(), t, Punch(new Vector4(10f, 0f, 0f, 0f)), 0.5f);
            FeedbackPlayer reveal = Player(t.gameObject, new TMPRevealItem(), t, TweenTo(Vector4.one), 0.5f);
            FeedbackPlayer count = Player(n.gameObject, new TMPCountToItem { m_From = 0, m_To = 500, m_Format = "{0:0}" }, n, TweenTo(Vector4.one), 0.5f);
            // a punch on the counter's position keeps its play running (a Revert stop has a play to stop)
            var keeper = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = Punch(new Vector4(0.5f, 0.5f, 0.5f, 0f)) };
            keeper.m_Timing.m_Duration = 3f;
            count.m_Items.Add(keeper);
            float colorMoved = 0f, sizeMoved = 0f;
            int fewest = int.MaxValue;
            string countShown = null;
            void Sample()
            {
                colorMoved = Mathf.Max(colorMoved, Mathf.Abs(t.color.r - color0.r));
                sizeMoved = Mathf.Max(sizeMoved, Mathf.Abs(t.fontSize - size0));
                fewest = Mathf.Min(fewest, t.maxVisibleCharacters);
                if (countShown == null && n.text != "Score")
                    countShown = n.text;
            }
            Canvas.willRenderCanvases += Sample;
            try
            {
                yield return null;
                color.Play();
                size.Play();
                reveal.Play();
                count.Play();
                for (int f = 0; f < 60; f++)
                    yield return null;
            }
            finally
            {
                Canvas.willRenderCanvases -= Sample;
            }
            Assert.That((colorMoved > 0.1f, sizeMoved > 1f, fewest < 11, countShown != null), Is.EqualTo((true, true, true, true)), "each moved where it shows — after LateApply");
            Assert.That((t.color, t.fontSize, t.maxVisibleCharacters, n.text), Is.EqualTo((color0, size0, TMPRevealSlot.All, "500")), "the ends — the bases · all shown · the count's end");
            count.Stop(StopMode.Revert);
            yield return null;
            Assert.That(n.text, Is.EqualTo("Score"), "the revert gives the text back");
        }

        // ---- the frame allocation in real frames — the TMP plays (the color slot · the size · a spacing · the copy's dilate with
        // its padding · the underlay offset · a reveal) allocate in no more frames past the run's median idle frame than the idle
        // window itself does, three to spare · the writes in the window · the positive control. Count To · Set Text are logged
        // apart (the editor's SetText makes TMP's m_text string)

        [UnityTest]
        public IEnumerator TM21_FrameAllocation([Values(false, true)] bool strings)
        {
            LogAssert.ignoreFailingMessages = true;
            Time.captureDeltaTime = 1f / 60f;
            Canvas c = MakeCanvas();
            TextMeshProUGUI t = Text(c.transform, "G1 text", new Vector2(300f, 80f));
            TextMeshProUGUI m = Text(c.transform, "G1 material", new Vector2(300f, 80f));
            ((RectTransform)m.transform).anchoredPosition = new Vector2(0f, -100f);
            var players = new List<FeedbackPlayer>();
            if (!strings)
            {
                players.Add(Player(t.gameObject, new GraphicColorItem(), t, Punch(new Vector4(0.3f, 0.3f, 0.3f, 0f)), 3f));
                players.Add(Player(t.gameObject, new TMPFontSizeItem(), t, Punch(new Vector4(4f, 0f, 0f, 0f)), 3f));
                players.Add(Player(t.gameObject, new TMPCharacterSpacingItem(), t, Swing(new Vector4(5f, 0f, 0f, 0f)), 3f));
                players.Add(Player(t.gameObject, new TMPRevealItem(), t, TweenTo(Vector4.one), 3f));
                players.Add(Player(m.gameObject, new TMPDilateItem(), m, TweenTo(new Vector4(0.4f, 0f, 0f, 0f)), 3f));
                players.Add(Player(m.gameObject, new TMPUnderlayOffsetItem(), m, Swing(new Vector4(0.3f, 0.3f, 0f, 0f)), 3f));
            }
            else
            {
                players.Add(Player(t.gameObject, new TMPCountToItem { m_From = 0, m_To = 100000, m_Format = "{0:#,0}" }, t, TweenTo(Vector4.one), 3f));
                FeedbackPlayer set = Player(m.gameObject, new TMPTextItem { m_Text = "G1 other" }, m, new SetMotion(), 0f);
                // a punch on its position keeps the play running (the warm-up's Revert stop has a play to stop)
                var keeper = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = Punch(new Vector4(0.5f, 0.5f, 0.5f, 0f)) };
                keeper.m_Timing.m_Duration = 5f;
                set.m_Items.Add(keeper);
                players.Add(set);
            }
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
                p.Stop(StopMode.Revert);
            for (int f = 0; f < 5; f++)
                yield return null;
            Color color0 = t.color;
            float size0 = t.fontSize, spacing0 = t.characterSpacing;
            // the characters' count, not the text getter — a player's getter after a SetText makes a string each read
            int count0 = t.textInfo.characterCount, other0 = m.textInfo.characterCount;
            int shown0 = t.maxVisibleCharacters;
            int dilate = Shader.PropertyToID("_FaceDilate"), underlay = Shader.PropertyToID("_UnderlayOffsetX");
            float dilate0 = m.fontSharedMaterial.GetFloat(dilate), underlay0 = m.fontSharedMaterial.GetFloat(underlay);
            int moved = 0;
            bool watching = false;
            Canvas.WillRenderCanvases watch = () =>
            {
                if (!watching)
                    return;
                if (strings)
                {
                    moved |= (t.textInfo.characterCount != count0 ? 1 : 0) | (m.textInfo.characterCount != other0 ? 2 : 0);
                    return;
                }
                moved |= (t.color != color0 ? 1 : 0) | (t.fontSize != size0 ? 2 : 0) | (t.characterSpacing != spacing0 ? 4 : 0) | (t.maxVisibleCharacters != shown0 ? 8 : 0)
                    | (m.fontSharedMaterial.GetFloat(dilate) != dilate0 ? 16 : 0) | (m.fontSharedMaterial.GetFloat(underlay) != underlay0 ? 32 : 0);
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
            long over = 0;
            for (int f = 0; f < 120; f++)
            {
                yield return null;
                long d = rec.LastValue - baseline;
                if (f > 2 && d >= 64)
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
            Debug.Log($"PROBE TM21 strings {strings} · baseline {baseline} · idle allocating frames {idleAllocating}/31 · allocating frames {allocating}/117 ({over} B) · positive {positive}/5 · moved {moved}");
            Assert.That(moved, Is.EqualTo(strings ? 3 : 63), "every play wrote in the window — the positive");
            Assert.That(positive, Is.GreaterThanOrEqualTo(4), "the positive control");
            // the strings are TMP's in the editor only (SetText(char[]) keeps no string in a player)
            if (!strings || !Application.isEditor)
            {
#if UNITY_6000_3_OR_NEWER || !UNITY_EDITOR
                Assert.That(allocating, Is.LessThanOrEqualTo(idleAllocating + 3), "the TMP plays allocate nothing once warm");
#else
                Assert.Ignore("6000.0's editor loop allocates in FlushDirty (EditorCompilationInterface.IsCompiling) in each frame the scene changed — the IL2CPP player judges the allocation");
#endif
            }
        }

        // ---- the cost: Count To writes the text when its characters change — a count of 0 → 3 over a second regenerates the
        // text about four times, not each frame (TMP's SetText has no equality check)

        [UnityTest]
        public IEnumerator TM25_CountToWritesOnAChange()
        {
            Time.captureDeltaTime = 1f / 60f;
            Canvas c = MakeCanvas();
            TextMeshProUGUI t = Text(c.transform, "Score", new Vector2(300f, 80f));
            yield return null;
            int regenerations = 0;
            Action<TMP_TextInfo> heard = info => regenerations++;
            t.OnPreRenderText += heard;
            FeedbackPlayer p = Player(t.gameObject, new TMPCountToItem { m_From = 0, m_To = 3, m_Format = "{0:0}" }, t, TweenTo(Vector4.one), 1f);
            p.Play();
            for (int f = 0; f < 70; f++)
                yield return null;
            t.OnPreRenderText -= heard;
            Assert.That(t.text, Is.EqualTo("3"), "the count's end (the positive)");
            Assert.That(regenerations, Is.InRange(3, 8), "a regeneration per change of the characters");
        }

        // ---- a cross fade · a texture offset on a 3D TextMeshPro text are skipped before the CanvasRenderer getter — a player's
        // getter would add one (the editor's GetComponent gives a fake null — green there for nothing)

        [UnityTest]
        public IEnumerator TM23_A3DTextGetsNoCanvasRenderer()
        {
            var go = new GameObject("tmp3d");
            m_Made.Add(go);
            var t = go.AddComponent<TextMeshPro>();
            t.text = "Hello";
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(UGUIAcquire.TMPRefusal)));
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(UGUIAcquire.TMPRefusal)));
            FeedbackPlayer p = Player(go, new GraphicCrossFadeItem(), t, TweenTo(new Vector4(1f, 1f, 1f, 0f)), 0.5f);
            var offset = new GraphicTextureOffsetItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = t, m_Path = "", m_Anchor = "" }, m_Motion = TweenTo(new Vector4(0.5f, 0f, 0f, 0f)) };
            offset.m_Timing.m_Duration = 0.5f;
            p.m_Items.Add(offset);
            p.EnsureRuntime();
            p.Play();
            for (int f = 0; f < 5; f++)
                yield return null;
            Assert.That(go.GetComponent<CanvasRenderer>() == null, Is.True, "no CanvasRenderer on a 3D text");
        }

        // ---- a player's text getter after a SetText makes the string once and keeps it nowhere — a game read before ours leaves
        // our start the old text (TMP's — the documentation's ground, pinned: a TMP that fixes it turns this red). The editor syncs
        // m_text at the SetText (the revert gives the SetText's text)

        [UnityTest]
        public IEnumerator TM24_TheGetterAfterASetText()
        {
            Canvas c = MakeCanvas();
            TextMeshProUGUI t = Text(c.transform, "Hello", new Vector2(300f, 80f));
            yield return null;
            t.SetText("Score {0}", 5);
            yield return null;
            string first = t.text, second = t.text;
            FeedbackPlayer p = Player(t.gameObject, new TMPTextItem { m_Text = "World" }, t, new SetMotion(), 0f);
            var keeper = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = Punch(new Vector4(0.5f, 0.5f, 0.5f, 0f)) };
            keeper.m_Timing.m_Duration = 3f;
            p.m_Items.Add(keeper);
            p.Play();
            yield return null;
            string during = t.text;
            p.Stop(StopMode.Revert);
            yield return null;
            Assert.That(during, Is.EqualTo("World"), "the Set Text wrote (the positive)");
            if (Application.isEditor)
                Assert.That((first, second, t.text), Is.EqualTo(("Score 5", "Score 5", "Score 5")), "the editor syncs the string");
            else
                Assert.That((first, second, t.text), Is.EqualTo(("Score 5", "Hello", "Hello")), "a player's getter keeps nothing — the revert writes the old text");
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

        // ---- the cost records — a TMP text's color · size · spacing (the mesh's regeneration) · the copy's dilate (a material
        // write) · a reveal · a count (the text's regeneration from characters) · per play ns within each row's budget and the
        // window's allocation against the baseline in an IL2CPP player (the count read through the character count: the text
        // getter allocates)

        static readonly string[] s_Costs = { "(x) tmp color", "(x) tmp font size", "(x) tmp character spacing", "(x) tmp dilate", "(x) tmp reveal", "(x) tmp count to" };
        static readonly double[] s_Budgets = { 2250, 4000, 4000, 2000, 500, 4750 };

        static PunchMotion Shake(Vector4 a) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = a, m_Vibrato = 30f, m_Decay = 0.01f } };

        [UnityTest]
        public IEnumerator TM22_Costs([Values(0, 1, 2, 3, 4, 5)] int kind, [Values(100, 1000)] int n)
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
                var players = new List<FeedbackPlayer>(n);
                Func<float> read = null;
                for (int i = 0; i < n; i++)
                {
                    TextMeshProUGUI text = Text(c.transform, "G11", new Vector2(40f, 20f));
                    TargetItem item;
                    TextMeshProUGUI at = text;
                    switch (kind)
                    {
                        case 0:
                            item = new GraphicColorItem { m_Motion = Shake(new Vector4(0.3f, 0.3f, 0.3f, 0f)) };
                            read = read ?? (() => at.color.r);
                            break;
                        case 1:
                            item = new TMPFontSizeItem { m_Motion = Shake(new Vector4(5f, 0f, 0f, 0f)) };
                            read = read ?? (() => at.fontSize);
                            break;
                        case 2:
                            item = new TMPCharacterSpacingItem { m_Motion = Shake(new Vector4(5f, 0f, 0f, 0f)) };
                            read = read ?? (() => at.characterSpacing);
                            break;
                        case 3:
                            item = new TMPDilateItem { m_Motion = new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(0.2f, 0f, 0f, 0f), m_Frequency = 10f } } };
                            read = read ?? (() => at.fontSharedMaterial.GetFloat("_FaceDilate"));
                            break;
                        case 4:
                            item = new TMPRevealItem { m_Motion = TweenTo(Vector4.one) };
                            read = read ?? (() => at.maxVisibleCharacters == TMPRevealSlot.All ? 3 : at.maxVisibleCharacters);
                            break;
                        default:
                            item = new TMPCountToItem { m_From = 0, m_To = 1000000, m_Format = "{0:0}", m_Motion = TweenTo(Vector4.one) };
                            read = read ?? (() => at.textInfo.characterCount);
                            break;
                    }
                    item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = text, m_Path = "", m_Anchor = "" };
                    item.m_Timing.m_Duration = 20f;
                    var p = text.gameObject.AddComponent<FeedbackPlayer>();
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
