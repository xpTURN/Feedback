using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the UI items on a TMP text · the TMP items: a test scheduler ticked by hand (0.05 s). TMP's Essential Resources are the
    // project's — without them the tests are skipped, not passed
    public sealed class TMPTargetsTests
    {
        const float Step = 0.05f;
        const string ScenePath = "Assets/TMPTargetsTests.unity";

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();

        [SetUp]
        public void SetUp()
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
                Assert.Ignore("TMP Essential Resources are not in the project");
        }

        [TearDown]
        public void TearDown()
        {
            FloatingTextBinding.s_TmpResources = () => Resources.Load<TMP_Settings>("TMP Settings") != null;
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    p.ReleaseRuntime();
            }
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            for (int i = m_Made.Count - 1; i >= 0; i--)
            {
                if (m_Made[i] != null)
                    Object.DestroyImmediate(m_Made[i]);
            }
            m_Made.Clear();
            m_Schedulers.Clear();
            m_Players.Clear();
        }

        static string B(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");

        static string C(Color c) => B(c.r) + B(c.g) + B(c.b) + B(c.a);

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };

        static TweenMotion TweenTo(Vector4 to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = to;
            return new TweenMotion { m_Params = p };
        }

        static FlashMotion FlashTo(Vector4 target) => new FlashMotion { m_Params = new FlashParams { m_Target = target, m_Attack = 0.25f, m_Hold = 0.5f, m_Release = 0.25f } };

        static PunchMotion Punch(float amplitude) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(amplitude, amplitude, amplitude, amplitude), m_Vibrato = 2f, m_Decay = 1f } };

        static SetMotion SetTo(float v) => new SetMotion { m_Params = new SetParams { m_Value = new Vector4(v, v, v, v) } };

        static T On<T>(T item, Object target, FeedbackMotion motion, float duration) where T : TargetItem
        {
            item.m_Target = Direct(target);
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static PositionItem Keeper(float duration = 3f)
        {
            var keeper = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = Punch(0.5f) };
            keeper.m_Timing.m_Duration = duration;
            return keeper;
        }

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            p.Bind(s);
            p.EnsureRuntime();
            m_Players.Add(p);
            return p;
        }

        static void Tick(FeedbackScheduler s, int n)
        {
            for (int i = 0; i < n; i++)
                s.Tick(Step);
        }

        Canvas MakeCanvas()
        {
            var go = new GameObject("Canvas", typeof(RectTransform));
            m_Made.Add(go);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            return c;
        }

        static TextMeshProUGUI Text(Transform parent, string text = "Hello World", string name = "tmp")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(300f, 80f);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            return t;
        }

        TextMeshPro Text3D(string text = "Hello World")
        {
            var go = new GameObject("tmp3d");
            m_Made.Add(go);
            var t = go.AddComponent<TextMeshPro>();
            t.text = text;
            return t;
        }

        static object Field(Object o, string name) => typeof(TMP_Text).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(o);

        // ---- UI/Color · UI/Alpha on a TMP text in the preview — the rows are m_fontColor.*, the stop gives the color back, a save
        // in the preview writes the original (a TMP text's m_fontColor32 is computed again)

        [Test]
        public void TM01_UIColorOnTMPInThePreview()
        {
            Func<double> clock = PreviewClock.Source;
            IPreviewRecordSource source = PreviewRecordPaths.Source;
            Scene active = SceneManager.GetActiveScene();
            double now = 0;
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                PreviewClock.Source = () => now;
                PreviewRecordPaths.Source = new PreviewRecordPathTable();
                SceneManager.SetActiveScene(scene);
                var canvasGo = new GameObject("Canvas", typeof(RectTransform));
                canvasGo.AddComponent<Canvas>();
                TextMeshProUGUI text = Text(canvasGo.transform);
                text.color = new Color(0.25f, 0.5f, 0.75f, 1f);
                var player = canvasGo.AddComponent<FeedbackPlayer>();
                player.m_Items.Add(On(new GraphicColorItem(), text, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 1f));
                player.m_Items.Add(On(new GraphicAlphaItem(), text, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 1f));
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                string before = C(text.color);
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                for (int i = 0; i < 60; i++)
                {
                    now += 1.0 / 120.0;
                    PreviewSession.Current?.Update();
                }
                string during = C(text.color);
                bool recorded = AnimationMode.IsPropertyAnimated(text, "m_fontColor.r") && AnimationMode.IsPropertyAnimated(text, "m_fontColor.a");
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                string saved = File.ReadAllText(Path.GetFullPath(ScenePath));
                PreviewSession.Current.Stop();
                Assert.That(during, Is.Not.EqualTo(before), "the preview moved the color (the positive)");
                Assert.That(recorded, Is.True, "the rows are the TMP color's");
                Assert.That(C(text.color), Is.EqualTo(before), "the stop gives the color back");
                Assert.That(saved, Does.Contain("m_fontColor: {r: 0.25, g: 0.5, b: 0.75, a: 1}"), "a save in the preview writes the original");
            }
            finally
            {
                PreviewSession.Current?.Stop();
                PreviewClock.Source = clock;
                PreviewRecordPaths.Source = source;
                if (active.IsValid())
                    SceneManager.SetActiveScene(active);
                if (scene.IsValid())
                    EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.DeleteAsset(ScenePath);
            }
        }

        // ---- UI/Texture Offset · Scale on a TMP text — skipped (the shared font material stays) · UI/Material on one — taken,
        // never drawn, given back (FB-P05)

        [Test]
        public void TM02_UIMaterialItemsOnTMP()
        {
            var fails = new List<string>();
            Canvas c = MakeCanvas();
            TextMeshProUGUI text = Text(c.transform);
            Material shared = text.fontSharedMaterial;
            Vector2 offset = shared.mainTextureOffset;
            {
                FeedbackScheduler s = Sched();
                TextMeshPro t3 = Text3D();
                LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(UGUIAcquire.TMPRefusal)));
                LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(UGUIAcquire.TMPRefusal)));
                FeedbackPlayer p = Player(s, On(new GraphicTextureOffsetItem(), text, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 0.5f),
                    On(new GraphicTextureScaleItem(), t3, TweenTo(new Vector4(2f, 2f, 0f, 0f)), 0.5f));
                p.Play();
                Tick(s, 12);
                if (p.Runtime.Binding(0) != null || p.Runtime.Binding(1) != null)
                    fails.Add("not skipped");
                if (shared.mainTextureOffset != offset)
                    fails.Add("the shared font material moved — " + shared.mainTextureOffset);
            }
            {
                FeedbackScheduler s = Sched();
                var swapIn = new Material(Canvas.GetDefaultCanvasMaterial()) { name = "swap-in" };
                m_Made.Add(swapIn);
                FeedbackPlayer p = Player(s, On(new GraphicMaterialItem { m_Material = swapIn }, text, SetTo(1f), 0f), Keeper());
                p.Play();
                Tick(s, 2);
                bool taken = p.Runtime.Binding(0) != null && ReferenceEquals(((Graphic)text).material, swapIn);
                bool drawn = ReferenceEquals(text.canvasRenderer.GetMaterial(0), swapIn);
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                object field = typeof(Graphic).GetField("m_Material", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(text);
                if (!taken || drawn || !ReferenceEquals(field, null))
                    fails.Add("UI/Material — taken " + taken + " · drawn " + drawn + " · the end " + field);
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- a cross fade on a 3D TextMeshPro text — skipped before the CanvasRenderer getter (a player's would add one; in the
        // editor the getter gives a fake null, so the add itself is the player's test) · on a UGUI text it goes and comes back

        [Test]
        public void TM03_CrossFadeOnA3DText()
        {
            var fails = new List<string>();
            TextMeshPro t3 = Text3D();
            {
                FeedbackScheduler s = Sched();
                LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape(UGUIAcquire.TMPRefusal)));
                FeedbackPlayer p = Player(s, On(new GraphicCrossFadeItem(), t3, TweenTo(new Vector4(1f, 1f, 1f, 0f)), 0.5f));
                p.Play();
                Tick(s, 12);
                if (p.Runtime.Binding(0) != null)
                    fails.Add("3D not skipped");
                if (t3.GetComponent<CanvasRenderer>() != null)
                    fails.Add("3D — a CanvasRenderer was added");
            }
            {
                Canvas c = MakeCanvas();
                TextMeshProUGUI text = Text(c.transform);
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, On(new GraphicCrossFadeItem(), text, TweenTo(new Vector4(1f, 1f, 1f, 0f)), 0.5f), Keeper());
                p.Play();
                Tick(s, 12);
                float at = text.canvasRenderer.GetColor().a;
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                if (at > 0.01f || C(text.canvasRenderer.GetColor()) != C(Color.white))
                    fails.Add("UGUI fade — the end " + at + " · back " + text.canvasRenderer.GetColor());
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- the material drawn is ours only: a Graphic · a TMP text drawn before the copy goes in still shows the old material
        // until the canvas updates — a write in that frame would go into it (the default UI material · the font's shared one) and
        // the revert, after the update, would not reach it

        [Test]
        public void TM05_TheDrawnMaterialIsOursOnly()
        {
            var fails = new List<string>();
            Canvas c = MakeCanvas();
            {
                Image g = new GameObject("image", typeof(RectTransform)).AddComponent<Image>();
                g.transform.SetParent(c.transform, false);
                Material shared = g.materialForRendering;
                Vector2 offset0 = shared.mainTextureOffset;
                Canvas.ForceUpdateCanvases();
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, On(new GraphicTextureOffsetItem(), g, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 0.5f), Keeper());
                p.Play();
                Tick(s, 3);
                Canvas.ForceUpdateCanvases();
                Tick(s, 3);
                // the positive — our copy moved
                if (ReferenceEquals(g.material, shared) || g.material.mainTextureOffset == offset0)
                    fails.Add("Image — the copy never moved");
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                Vector2 after = shared.mainTextureOffset;
                shared.mainTextureOffset = offset0;
                if (after != offset0)
                    fails.Add("Image — the drawn " + shared.name + " kept " + after);
            }
            {
                TextMeshProUGUI t = Text(c.transform);
                Material shared = t.fontSharedMaterial;
                float dilate0 = shared.GetFloat("_FaceDilate");
                Canvas.ForceUpdateCanvases();
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, On(new TMPDilateItem(), t, TweenTo(new Vector4(0.4f, 0f, 0f, 0f)), 0.5f), Keeper());
                p.Play();
                Tick(s, 3);
                Canvas.ForceUpdateCanvases();
                Tick(s, 3);
                if (ReferenceEquals(t.fontSharedMaterial, shared) || B(t.fontSharedMaterial.GetFloat("_FaceDilate")) == B(dilate0))
                    fails.Add("TMP — the copy never moved");
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                float after = shared.GetFloat("_FaceDilate");
                shared.SetFloat("_FaceDilate", dilate0);
                if (B(after) != B(dilate0))
                    fails.Add("TMP — the font's shared " + shared.name + " kept " + after);
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- UI/Color · UI/Alpha share the TMP color slot — one slot · the bits back · either reverse order · an alpha flash
        // holds the alpha only · a game write between the revert and the apply stays

        [Test]
        public void TM06_ColorAndAlpha()
        {
            Canvas c = MakeCanvas();
            TextMeshProUGUI g = Text(c.transform);
            var c0 = new Color(0.25f, 0.5f, 0.75f, 0.9f);
            g.color = c0;
            FeedbackScheduler s = Sched();
            FeedbackPlayer pc = Player(s, On(new GraphicColorItem(), g, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 0.3f), Keeper());
            FeedbackPlayer pa = Player(s, On(new GraphicAlphaItem(), g, TweenTo(new Vector4(0.25f, 0f, 0f, 0f)), 0.3f), Keeper());
            ValueSlot<Color, ColorOps> slot = ((GraphicColorBinding)pc.Runtime.Binding(0)).Slot;
            Assert.That(slot, Is.InstanceOf<TMPColorSlot>(), "the TMP slot");
            Assert.That(((GraphicAlphaBinding)pa.Runtime.Binding(0)).Slot, Is.SameAs(slot), "one slot");
            pa.Play();
            Tick(s, 8);
            pc.Play();
            Tick(s, 8);
            pa.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(B(g.color.r) + B(g.color.g) + B(g.color.b), Is.EqualTo(B(1f) + B(0f) + B(0f)), "the alpha's revert leaves the color's rgb");
            pc.Stop(StopMode.Revert);
            s.Tick(0f);
            g.color = c0;
            pc.Play();
            Tick(s, 8);
            pa.Play();
            Tick(s, 8);
            pa.Stop(StopMode.Revert);
            s.Tick(0f);
            pc.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(C(g.color), Is.EqualTo(C(c0)), "reverse order — alpha then color");
            pa.Play();
            Tick(s, 8);
            pc.Play();
            Tick(s, 8);
            pc.Stop(StopMode.Revert);
            s.Tick(0f);
            pa.Stop(StopMode.Revert);
            s.Tick(0f);
            Assert.That(C(g.color), Is.EqualTo(C(c0)), "reverse order — color then alpha");
            Player(s, On(new GraphicAlphaItem(), g, FlashTo(new Vector4(0.1f, 0f, 0f, 0f)), 1f)).Play();
            Tick(s, 8);
            Assert.That((B(g.color.r) + B(g.color.g) + B(g.color.b), B(g.color.a)), Is.EqualTo((B(c0.r) + B(c0.g) + B(c0.b), B(0.1f))), "a flash's hold — the alpha only");
            Tick(s, 30);
            s.Tick(0f);
            Assert.That(C(g.color), Is.EqualTo(C(c0)), "a flash's end");
            Player(s, On(new GraphicColorItem(), g, Punch(0.3f), 1f)).Play();
            Tick(s, 3);
            s.RunRevert();
            g.color = new Color(0.125f, 0.25f, 0.375f, 1f);
            s.RunUpdate(new FrameTime(Step, Step));
            s.RunLateApply();
            Tick(s, 30);
            s.Tick(0f);
            Assert.That(C(g.color), Is.EqualTo(C(new Color(0.125f, 0.25f, 0.375f, 1f))), "the game's color stays");
        }

        // ---- the font size (both rows) · the spacings — the value and the bits back · with auto size TMP writes over the size
        // (FB-P06's ground)

        [Test]
        public void TM07_SizeAndSpacings()
        {
            var fails = new List<string>();
            Canvas c = MakeCanvas();
            foreach ((string name, Func<TMP_Text, TargetItem> item, Func<TMP_Text, float> read) in new (string, Func<TMP_Text, TargetItem>, Func<TMP_Text, float>)[]
            {
                ("font size", t => On(new TMPFontSizeItem(), t, TweenTo(new Vector4(50.5f, 0f, 0f, 0f)), 0.5f), t => t.fontSize),
                ("character spacing", t => On(new TMPCharacterSpacingItem(), t, TweenTo(new Vector4(12.5f, 0f, 0f, 0f)), 0.5f), t => t.characterSpacing),
                ("word spacing", t => On(new TMPWordSpacingItem(), t, TweenTo(new Vector4(12.5f, 0f, 0f, 0f)), 0.5f), t => t.wordSpacing),
                ("line spacing", t => On(new TMPLineSpacingItem(), t, TweenTo(new Vector4(12.5f, 0f, 0f, 0f)), 0.5f), t => t.lineSpacing),
                ("paragraph spacing", t => On(new TMPParagraphSpacingItem(), t, TweenTo(new Vector4(12.5f, 0f, 0f, 0f)), 0.5f), t => t.paragraphSpacing),
            })
            {
                TextMeshProUGUI t = Text(c.transform, "Hello World", name);
                float v0 = read(t);
                float base0 = (float)Field(t, "m_fontSizeBase");
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, item(t), Keeper());
                p.Play();
                Tick(s, 12);
                float at = read(t);
                float baseAt = (float)Field(t, "m_fontSizeBase");
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                if (Math.Abs(at - (name == "font size" ? 50.5f : 12.5f)) > 1e-4f)
                    fails.Add(name + " — at the end " + at);
                if (B(read(t)) != B(v0))
                    fails.Add(name + " — back " + read(t) + " not " + v0);
                if (name == "font size" && (B(baseAt) != B(50.5f) || B((float)Field(t, "m_fontSizeBase")) != B(base0)))
                    fails.Add("font size base — at " + baseAt + " back " + Field(t, "m_fontSizeBase"));
            }
            {
                TextMeshProUGUI t = Text(c.transform, "Hello World", "auto");
                t.enableAutoSizing = true;
                t.fontSizeMin = 10f;
                t.fontSizeMax = 20f;
                Canvas.ForceUpdateCanvases();
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, On(new TMPFontSizeItem(), t, TweenTo(new Vector4(60f, 0f, 0f, 0f)), 0.5f), Keeper());
                p.Play();
                Tick(s, 12);
                Canvas.ForceUpdateCanvases();
                if (t.fontSize > 20.01f)
                    fails.Add("auto size kept our size " + t.fontSize + " (FB-P06's ground expects TMP over it)");
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- the copy — put in through fontSharedMaterial once, the shared asset untouched, no TMP instance (m_fontMaterial), the
        // original back at the end and the copy destroyed · under a Mask the drawn stencil copy takes the value · a 3D text's
        // renderer takes the copy and the original back

        [Test]
        public void TM09_TheCopy()
        {
            var fails = new List<string>();
            Canvas c = MakeCanvas();
            {
                TextMeshProUGUI t = Text(c.transform);
                Material shared = t.fontSharedMaterial;
                float dilate0 = shared.GetFloat("_FaceDilate");
                object instance0 = Field(t, "m_fontMaterial");
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, On(new TMPDilateItem(), t, TweenTo(new Vector4(0.3f, 0f, 0f, 0f)), 0.5f), On(new TMPSoftnessItem(), t, TweenTo(new Vector4(0.2f, 0f, 0f, 0f)), 0.5f), Keeper());
                p.Play();
                Material copy = t.fontSharedMaterial;
                bool once = true;
                for (int i = 0; i < 12; i++)
                {
                    s.Tick(Step);
                    once &= ReferenceEquals(t.fontSharedMaterial, copy);
                }
                if (ReferenceEquals(copy, shared) || !copy.name.EndsWith("(Feedback)", StringComparison.Ordinal))
                    fails.Add("no copy — " + copy.name);
                if (!once)
                    fails.Add("the copy was put in more than once");
                if (Math.Abs(copy.GetFloat("_FaceDilate") - 0.3f) > 1e-5f || Math.Abs(copy.GetFloat("_OutlineSoftness") - 0.2f) > 1e-5f)
                    fails.Add("the copy's values " + copy.GetFloat("_FaceDilate") + " " + copy.GetFloat("_OutlineSoftness"));
                if (B(shared.GetFloat("_FaceDilate")) != B(dilate0))
                    fails.Add("the shared font material moved");
                if (!ReferenceEquals(Field(t, "m_fontMaterial"), instance0))
                    fails.Add("a TMP instance was made (m_fontMaterial)");
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                p.ReleaseRuntime();
                if (!ReferenceEquals(t.fontSharedMaterial, shared) || copy != null)
                    fails.Add("the end — " + (t.fontSharedMaterial != null ? t.fontSharedMaterial.name : "null") + " · copy alive " + (copy != null));
            }
            {
                Image mask = new GameObject("mask", typeof(RectTransform)).AddComponent<Image>();
                mask.transform.SetParent(c.transform, false);
                ((RectTransform)mask.transform).sizeDelta = new Vector2(300f, 100f);
                mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                TextMeshProUGUI t = Text(mask.transform, "Hello World", "masked");
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, On(new TMPDilateItem(), t, TweenTo(new Vector4(0.3f, 0f, 0f, 0f)), 0.5f));
                p.Play();
                Tick(s, 2);
                Canvas.ForceUpdateCanvases();
                Tick(s, 10);
                Material drawn = t.canvasRenderer.materialCount > 0 ? t.canvasRenderer.GetMaterial(0) : null;
                if (drawn == null || ReferenceEquals(drawn, t.fontSharedMaterial) || Math.Abs(drawn.GetFloat("_FaceDilate") - 0.3f) > 1e-5f)
                    fails.Add("the drawn copy under the mask — " + (drawn != null ? drawn.name + " " + drawn.GetFloat("_FaceDilate") : "null"));
            }
            {
                TextMeshPro t = Text3D();
                Material shared = t.fontSharedMaterial;
                var r = t.GetComponent<MeshRenderer>();
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, On(new TMPDilateItem(), t, TweenTo(new Vector4(0.3f, 0f, 0f, 0f)), 0.5f), Keeper());
                p.Play();
                Tick(s, 12);
                bool during = ReferenceEquals(r.sharedMaterial, t.fontSharedMaterial) && !ReferenceEquals(r.sharedMaterial, shared);
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                p.ReleaseRuntime();
                if (!during || !ReferenceEquals(r.sharedMaterial, shared) || t.GetComponent<CanvasRenderer>() != null)
                    fails.Add("3D — the renderer took the copy " + during + " · the end " + (r.sharedMaterial != null ? r.sharedMaterial.name : "null"));
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- the properties on a keyword material (the test's — the default Mobile material shows no outline · underlay) —
        // the values and back · the padding grows with the outline (RT pixels — a camera the test makes: the same lit count as the
        // values set and the padding measured by hand)

        sealed class Rig : IDisposable
        {
            internal readonly Camera Cam;
            internal readonly RenderTexture Rt;
            internal readonly Canvas Canvas;
            readonly Texture2D m_Read;
            readonly GameObject m_Cam, m_Canvas;

            internal Rig()
            {
                m_Cam = new GameObject("tt10 cam");
                Cam = m_Cam.AddComponent<Camera>();
                Cam.orthographic = true;
                Cam.clearFlags = CameraClearFlags.SolidColor;
                Cam.backgroundColor = Color.black;
                Cam.transform.position = new Vector3(0f, 0f, -10f);
                Rt = new RenderTexture(128, 64, 24);
                Cam.targetTexture = Rt;
                m_Canvas = new GameObject("tt10 canvas", typeof(RectTransform));
                Canvas = m_Canvas.AddComponent<Canvas>();
                Canvas.renderMode = RenderMode.ScreenSpaceCamera;
                Canvas.worldCamera = Cam;
                Canvas.planeDistance = 5f;
                m_Read = new Texture2D(128, 64, TextureFormat.RGBA32, false);
            }

            internal (int lit, int red) Count()
            {
                Canvas.ForceUpdateCanvases();
                Cam.Render();
                RenderTexture.active = Rt;
                m_Read.ReadPixels(new Rect(0, 0, 128, 64), 0, 0);
                m_Read.Apply();
                RenderTexture.active = null;
                int lit = 0, red = 0;
                foreach (Color c in m_Read.GetPixels())
                {
                    if (c.r + c.g + c.b > 0.3f)
                        lit++;
                    if (c.r > 0.5f && c.g < 0.3f)
                        red++;
                }
                return (lit, red);
            }

            public void Dispose()
            {
                Cam.targetTexture = null;
                Object.DestroyImmediate(m_Canvas);
                Object.DestroyImmediate(m_Cam);
                Rt.Release();
                Object.DestroyImmediate(Rt);
                Object.DestroyImmediate(m_Read);
            }
        }

        static TextMeshProUGUI Fill(Transform parent, string text)
        {
            var go = new GameObject("tmp", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.sizeDelta = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = 36f;
            t.alignment = TextAlignmentOptions.Center;
            return t;
        }

        [Test]
        public void TM10_PropertiesAndThePadding()
        {
            var fails = new List<string>();
            using var rig = new Rig();
            TextMeshProUGUI t = Fill(rig.Canvas.transform, "WWW");
            var keyed = new Material(t.fontSharedMaterial) { name = "tt10 keyed" };
            m_Made.Add(keyed);
            keyed.EnableKeyword("OUTLINE_ON");
            keyed.EnableKeyword("UNDERLAY_ON");
            t.fontSharedMaterial = keyed;
            (int lit, int red) plain = rig.Count();
            // the reference — the values set by hand, the padding measured by hand
            var hand = new Material(keyed) { name = "tt10 hand" };
            m_Made.Add(hand);
            hand.SetFloat("_OutlineWidth", 0.4f);
            hand.SetColor("_OutlineColor", Color.red);
            hand.SetFloat("_FaceDilate", 0.4f);
            t.fontSharedMaterial = hand;
            t.UpdateMeshPadding();
            t.SetVerticesDirty();
            (int lit, int red) reference = rig.Count();
            t.fontSharedMaterial = keyed;
            t.UpdateMeshPadding();
            t.SetVerticesDirty();
            if (rig.Count() != plain)
                fails.Add("the reference did not come back to plain");
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, On(new TMPOutlineWidthItem(), t, TweenTo(new Vector4(0.4f, 0f, 0f, 0f)), 0.5f),
                On(new TMPOutlineColorItem(), t, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 0.5f),
                On(new TMPDilateItem(), t, TweenTo(new Vector4(0.4f, 0f, 0f, 0f)), 0.5f),
                On(new TMPUnderlayColorItem(), t, TweenTo(new Vector4(0f, 1f, 0f, 1f)), 0.5f),
                On(new TMPUnderlayOffsetItem(), t, TweenTo(new Vector4(0.3f, -0.3f, 0f, 0f)), 0.5f), Keeper());
            p.Play();
            Tick(s, 12);
            Material copy = t.fontSharedMaterial;
            string values = copy.GetFloat("_OutlineWidth") + " " + copy.GetColor("_OutlineColor") + " " + copy.GetFloat("_FaceDilate") + " " + copy.GetColor("_UnderlayColor") + " " + copy.GetFloat("_UnderlayOffsetX") + " " + copy.GetFloat("_UnderlayOffsetY");
            if (values != 0.4f + " " + Color.red + " " + 0.4f + " " + Color.green + " " + 0.3f + " " + (-0.3f))
                fails.Add("the copy's values " + values);
            p.Stop(StopMode.Revert);
            s.Tick(0f);
            // the outline · dilate alone against the reference (the underlay off for the count)
            FeedbackScheduler s2 = Sched();
            FeedbackPlayer q = Player(s2, On(new TMPOutlineWidthItem(), t, TweenTo(new Vector4(0.4f, 0f, 0f, 0f)), 0.5f),
                On(new TMPOutlineColorItem(), t, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 0.5f),
                On(new TMPDilateItem(), t, TweenTo(new Vector4(0.4f, 0f, 0f, 0f)), 0.5f), Keeper());
            q.Play();
            Tick(s2, 12);
            (int lit, int red) ours = rig.Count();
            q.Stop(StopMode.Revert);
            s2.Tick(0f);
            q.ReleaseRuntime();
            p.ReleaseRuntime();
            (int lit, int red) after = rig.Count();
            if (ours.lit < reference.lit - 8 || ours.red == 0)
                fails.Add("ours " + ours + " against the hand's " + reference + " — the padding");
            if (after != plain || !ReferenceEquals(t.fontSharedMaterial, keyed))
                fails.Add("the end " + after + " not " + plain);
            Assert.That(fails, Is.Empty);
        }

        // ---- Set Text — the string and back through the setter (the screen's text too — a SetText before it) · Count To — the
        // formatter against .NET's ToString (an independent truth) · a count above 2^24 · the end stays · the revert gives the
        // play's text · Set Text and Count To on one text (the later start writes)

        [Test]
        public void TM11_SetText()
        {
            Canvas c = MakeCanvas();
            TextMeshProUGUI t = Text(c.transform);
            t.SetText("Score {0}", 5);
            Canvas.ForceUpdateCanvases();
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, On(new TMPTextItem { m_Text = "World" }, t, SetTo(1f), 0f), Keeper());
            p.Play();
            Tick(s, 2);
            Canvas.ForceUpdateCanvases();
            string set = t.text, shown = t.GetParsedText();
            p.Stop(StopMode.Revert);
            s.Tick(0f);
            Canvas.ForceUpdateCanvases();
            Assert.That((set, shown, t.text, t.GetParsedText()), Is.EqualTo(("World", "World", "Score 5", "Score 5")));
        }

        static readonly string[] s_Patterns = { "0", "00.00", "#,0", "#,0.##", "0.0", "0.###", "#.##", "000", "0.00", "#,0.000" };

        static readonly double[] s_Values =
        {
            0, 1, -1, 0.5, 1.5, 2.5, -1.5, -2.5, 1.005, 2.675, 0.995, 0.125, 0.375, 999.999, 12345.678, 16777217, 16777217.5, 123456789012345,
            -0.001, -0.004, 0.0004, 1e15, 9999999.995, 1234567.891, -98765.4321, 0.045, 1.45, 3.14159265358979, 100, 1000000,
        };

        [Test]
        public void TM12_CountTo()
        {
            var fails = new List<string>();
            var buffer = new char[64];
            foreach (string pattern in s_Patterns)
            {
                Assert.That(TMPNumberFormat.TryParse("Score {0:" + pattern + "} pts", out TMPNumberFormat f, out string why), Is.True, why);
                foreach (double v in s_Values)
                {
                    string ours = new string(buffer, 0, f.Write(v, buffer));
                    string truth = "Score " + v.ToString(pattern, CultureInfo.InvariantCulture) + " pts";
                    if (ours != truth)
                        fails.Add(pattern + " " + v.ToString("R", CultureInfo.InvariantCulture) + " — ours '" + ours + "' · ToString '" + truth + "'");
                }
            }
            Canvas c = MakeCanvas();
            TextMeshProUGUI t = Text(c.transform);
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, On(new TMPCountToItem { m_From = 16777000, m_To = 16777217, m_Format = "{0:#,0}" }, t, TweenTo(Vector4.one), 0.5f), Keeper());
                p.Play();
                Tick(s, 5);
                string mid = t.text;
                Tick(s, 10);
                string end = t.text;
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                if (mid == "Hello World" || mid == "16,777,217" || end != "16,777,217" || t.text != "Hello World")
                    fails.Add("count — mid " + mid + " · end " + end + " · back " + t.text);
            }
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, On(new TMPCountToItem { m_From = 0, m_To = 10, m_Format = "{0:0}" }, t, TweenTo(Vector4.one), 0.3f));
                p.Play();
                Tick(s, 10);
                if (t.text != "10")
                    fails.Add("a play's end stays — " + t.text);
                p.ReleaseRuntime();
                t.text = "Hello World";
            }
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, On(new TMPTextItem { m_Text = "World" }, t, SetTo(1f), 0f), Keeper());
                FeedbackPlayer q = Player(s, On(new TMPCountToItem { m_From = 0, m_To = 100, m_Format = "{0:0}" }, t, TweenTo(Vector4.one), 0.5f), Keeper());
                p.Play();
                Tick(s, 2);
                q.Play();
                Tick(s, 12);
                string counted = t.text;
                q.Stop(StopMode.Revert);
                s.Tick(0f);
                string afterCount = t.text;
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                if (counted != "100" || afterCount != "World" || t.text != "Hello World")
                    fails.Add("set + count — " + counted + " · " + afterCount + " · " + t.text);
            }
            Assert.That(TMPNumberFormat.TryParse("{0:0.0#0}", out _, out _), Is.False, "a 0 after a #");
            Assert.That(TMPNumberFormat.TryParse("no number", out _, out _), Is.False, "no {0}");
            Assert.That(fails, Is.Empty);
        }

        // ---- Reveal — the three units · the progress to a count · the end shows all (TMP's 99999) · the revert gives the play's
        // count · a text never drawn (the counts come at the prepare)

        [Test]
        public void TM13_Reveal()
        {
            var fails = new List<string>();
            Canvas c = MakeCanvas();
            foreach ((TMPRevealUnit unit, string text, Func<TMP_Text, int> read) in new (TMPRevealUnit, string, Func<TMP_Text, int>)[]
            {
                (TMPRevealUnit.Character, "Hello World", t => t.maxVisibleCharacters),
                (TMPRevealUnit.Word, "one two three four", t => t.maxVisibleWords),
                (TMPRevealUnit.Line, "a\nb\nc\nd", t => t.maxVisibleLines),
            })
            {
                TextMeshProUGUI t = Text(c.transform, text, unit.ToString());
                int total = unit == TMPRevealUnit.Character ? 11 : 4;
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, On(new TMPRevealItem { m_Unit = unit }, t, TweenTo(Vector4.one), 0.5f), Keeper());
                p.Play();
                Tick(s, 5);
                int mid = read(t);
                Tick(s, 10);
                int end = read(t);
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                if (mid <= 0 || mid >= total || end != TMPRevealSlot.All || read(t) != TMPRevealSlot.All)
                    fails.Add(unit + " — mid " + mid + " · end " + end + " · back " + read(t) + " (count " + t.textInfo.characterCount + ")");
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- the preview — every TMP item moves and comes back bit for bit · the copy's reference recorded · the text kept · no
        // hidden copy left · the scene stays clean

        [Test]
        public void TM15_ThePreview()
        {
            Func<double> clock = PreviewClock.Source;
            IPreviewRecordSource source = PreviewRecordPaths.Source;
            Scene active = SceneManager.GetActiveScene();
            double now = 0;
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                PreviewClock.Source = () => now;
                PreviewRecordPaths.Source = new PreviewRecordPathTable();
                SceneManager.SetActiveScene(scene);
                var canvasGo = new GameObject("Canvas", typeof(RectTransform));
                canvasGo.AddComponent<Canvas>();
                TextMeshProUGUI a = Text(canvasGo.transform, "Hello World", "a");
                TextMeshProUGUI b = Text(canvasGo.transform, "Hello World", "b");
                var player = canvasGo.AddComponent<FeedbackPlayer>();
                foreach (TargetItem item in new TargetItem[]
                {
                    On(new GraphicColorItem(), a, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 1f),
                    On(new TMPFontSizeItem(), a, TweenTo(new Vector4(50f, 0f, 0f, 0f)), 1f),
                    On(new TMPCharacterSpacingItem(), a, TweenTo(new Vector4(10f, 0f, 0f, 0f)), 1f),
                    On(new TMPDilateItem(), a, TweenTo(new Vector4(0.3f, 0f, 0f, 0f)), 1f),
                    On(new TMPRevealItem(), a, TweenTo(Vector4.one), 2f),
                    On(new TMPCountToItem { m_From = 0, m_To = 1000, m_Format = "{0:0}" }, b, TweenTo(Vector4.one), 1f),
                })
                    player.m_Items.Add(item);
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                string Bits() => C(a.color) + "/" + B(a.fontSize) + "/" + B(a.characterSpacing) + "/" + a.fontSharedMaterial.name + "/" + a.maxVisibleCharacters + "/" + b.text;
                string before = Bits();
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                for (int i = 0; i < 60; i++)
                {
                    now += 1.0 / 120.0;
                    PreviewSession.Current?.Update();
                }
                string during = Bits();
                bool recorded = AnimationMode.IsPropertyAnimated(a, "m_sharedMaterial");
                int kept = 0;
                for (int i = 0; i < PreviewSaveState.Count; i++)
                {
                    if (PreviewSaveState.At(i) is SerializedPathState || PreviewSaveState.At(i) is RuntimePropertyState)
                        kept++;
                }
                PreviewSession.Current.Stop();
                string after = Bits();
                int hidden = Resources.FindObjectsOfTypeAll<Material>().Count(m => m != null && m.name.StartsWith(PropertyLayerRegistry.PreviewPrefix, StringComparison.Ordinal));
                string[] parts = before.Split('/'), moved = during.Split('/');
                for (int k = 0; k < parts.Length; k++)
                    Assert.That(moved[k], Is.Not.EqualTo(parts[k]), "the preview moved part " + k);
                Assert.That(after, Is.EqualTo(before), "the stop gives every value back");
                Assert.That(recorded, Is.True, "the copy slot recorded the reference");
                Assert.That(kept, Is.EqualTo(4), "the kept kinds — the text · the reveal's three");
                Assert.That(hidden, Is.Zero, "no hidden copy left");
                Assert.That(scene.isDirty, Is.False, "the scene stays clean");
            }
            finally
            {
                PreviewSession.Current?.Stop();
                PreviewClock.Source = clock;
                PreviewRecordPaths.Source = source;
                if (active.IsValid())
                    SceneManager.SetActiveScene(active);
                if (scene.IsValid())
                    EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.DeleteAsset(ScenePath);
            }
        }

        // ---- every serialized path a slot's write changes — after the text's rebuild too — is in its rows, but the derived
        // ones the rebuild computes again from them (named with the reason)

        static Dictionary<string, string> Serialized(Object o)
        {
            var values = new Dictionary<string, string>();
            SerializedProperty it = new SerializedObject(o).GetIterator();
            while (it.Next(true))
            {
                switch (it.propertyType)
                {
                    case SerializedPropertyType.Float: values[it.propertyPath] = it.floatValue.ToString("R"); break;
                    case SerializedPropertyType.Integer: values[it.propertyPath] = it.longValue.ToString(); break;
                    case SerializedPropertyType.Boolean: values[it.propertyPath] = it.boolValue.ToString(); break;
                    case SerializedPropertyType.String: values[it.propertyPath] = it.stringValue; break;
                    case SerializedPropertyType.ObjectReference: values[it.propertyPath] = it.objectReferenceValue != null ? it.objectReferenceValue.GetHashCode().ToString() : "null"; break;
                }
            }
            return values;
        }

        // the 32-bit copy of the color — no animation binding, computed again from m_fontColor at a rebuild and a load
        static readonly Dictionary<Type, string[]> s_Derived = new Dictionary<Type, string[]>
        {
            { typeof(TMPColorSlot), new[] { "m_fontColor32.rgba" } },
        };

        [Test]
        public void TM16_TheRowsCoverTheWrites()
        {
            var fails = new List<string>();
            Canvas c = MakeCanvas();
            FeedbackScheduler s = Sched();
            PropertyLayerRegistry layers = s.Context.Layers;
            foreach ((Type slot, Action<TMP_Text> write) in new (Type, Action<TMP_Text>)[]
            {
                (typeof(TMPColorSlot), t => layers.GetOrCreate(t, 0, 0, () => new TMPColorSlot(t)).WriteValue(new Color(2f, 0.1f, -0.5f, 0.5f))),
                (typeof(TMPFontSizeSlot), t => layers.GetOrCreate(t, 0, 0, () => new TMPFontSizeSlot(t)).WriteValue(51f)),
                (typeof(TMPCharacterSpacingSlot), t => layers.GetOrCreate(t, 0, 0, () => new TMPCharacterSpacingSlot(t)).WriteValue(5f)),
                (typeof(TMPWordSpacingSlot), t => layers.GetOrCreate(t, 0, 0, () => new TMPWordSpacingSlot(t)).WriteValue(5f)),
                (typeof(TMPLineSpacingSlot), t => layers.GetOrCreate(t, 0, 0, () => new TMPLineSpacingSlot(t)).WriteValue(5f)),
                (typeof(TMPParagraphSpacingSlot), t => layers.GetOrCreate(t, 0, 0, () => new TMPParagraphSpacingSlot(t)).WriteValue(5f)),
                (typeof(TMPCopySlot), t => layers.GetOrCreate(t, 0, -1, () => new TMPCopySlot(t)).Ensure()),
            })
            {
                TextMeshProUGUI t = Text(c.transform, "Hello World", slot.Name);
                Canvas.ForceUpdateCanvases();
                Dictionary<string, string> before = Serialized(t);
                write(t);
                Canvas.ForceUpdateCanvases();
                Dictionary<string, string> after = Serialized(t);
                string[] rows = slot.GetCustomAttribute<RecordPathsAttribute>().Paths;
                s_Derived.TryGetValue(slot, out string[] derived);
                List<string> changed = after.Where(kv => !before.TryGetValue(kv.Key, out string v) || v != kv.Value).Select(kv => kv.Key)
                    .Where(k => !k.EndsWith("m_FileID") && !k.EndsWith("m_PathID")).ToList();
                if (changed.Count == 0)
                    fails.Add(slot.Name + " changed nothing — the positive");
                foreach (string path in changed.Where(x => !rows.Any(r => r == x || r.StartsWith(x + ".", StringComparison.Ordinal) || x.StartsWith(r + ".", StringComparison.Ordinal))))
                {
                    if (derived == null || !derived.Contains(path))
                        fails.Add(slot.Name + " wrote " + path + " outside its rows");
                }
                if (derived != null && !derived.All(changed.Contains))
                    fails.Add(slot.Name + " — a derived path did not change (the net's positive): " + string.Join(", ", derived.Where(d => !changed.Contains(d))));
            }
            foreach (Type t in new[] { typeof(TMPTextSlot) })
            {
                if (t.GetCustomAttribute<PreviewKeepsAttribute>() == null || t.GetCustomAttribute<NotRecordedAttribute>() == null)
                    fails.Add(t.Name + " keeps no kind");
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- FB-P04 ~ FB-P07

        [Test]
        public void TM17_TheRules()
        {
            Canvas c = MakeCanvas();
            var host = c.gameObject.AddComponent<FeedbackPlayer>();
            TextMeshProUGUI text = Text(c.transform, "Hello", "text");
            TextMeshProUGUI auto = Text(c.transform, "Hello", "auto");
            auto.enableAutoSizing = true;
            TextMeshProUGUI keyed = Text(c.transform, "Hello", "keyed");
            var keyedMaterial = new Material(keyed.fontSharedMaterial) { name = "keyed" };
            m_Made.Add(keyedMaterial);
            keyedMaterial.EnableKeyword("OUTLINE_ON");
            keyedMaterial.EnableKeyword("UNDERLAY_ON");
            keyed.fontSharedMaterial = keyedMaterial;
            TextMeshPro t3 = Text3D();
            Image image = new GameObject("image", typeof(RectTransform)).AddComponent<Image>();
            image.transform.SetParent(c.transform, false);
            var swapIn = new Material(Canvas.GetDefaultCanvasMaterial()) { name = "swap-in" };
            m_Made.Add(swapIn);
            host.m_Items.AddRange(new FeedbackItem[]
            {
                On(new GraphicTextureOffsetItem(), text, TweenTo(Vector4.zero), 1f),          // 0 P04
                On(new GraphicCrossFadeItem(), t3, TweenTo(Vector4.zero), 1f),                // 1 P04
                On(new GraphicCrossFadeItem(), text, TweenTo(Vector4.zero), 1f),              // 2
                On(new GraphicMaterialItem { m_Material = swapIn }, text, SetTo(1f), 0f),     // 3 P05
                On(new GraphicMaterialItem { m_Material = swapIn }, image, SetTo(1f), 0f),    // 4
                On(new TMPFontSizeItem(), auto, Punch(10f), 1f),                              // 5 P06
                On(new TMPFontSizeItem(), text, Punch(10f), 1f),                              // 6
                On(new TMPOutlineColorItem(), text, TweenTo(Vector4.one), 1f),                // 7 P07 (Mobile — the outline's keyword)
                On(new TMPUnderlayOffsetItem(), text, TweenTo(Vector4.one), 1f),              // 8 P07
                On(new TMPOutlineWidthItem(), keyed, TweenTo(Vector4.one), 1f),               // 9
                On(new TMPUnderlayColorItem(), keyed, TweenTo(Vector4.one), 1f),              // 10
                On(new TMPDilateItem(), text, TweenTo(Vector4.one), 1f),                      // 11
            });
            List<RuleResult> got = FeedbackValidator.Run(host).Where(r => r.Id == "FB-P04" || r.Id == "FB-P05" || r.Id == "FB-P06" || r.Id == "FB-P07").ToList();
            Assert.That(got.Select(r => r.Id + "@" + r.Item).OrderBy(x => x, StringComparer.Ordinal),
                Is.EqualTo(new[] { "FB-P04@0", "FB-P04@1", "FB-P05@3", "FB-P06@5", "FB-P07@7", "FB-P07@8" }));
        }

        // ---- the add menu's defaults · the name fields resolve · the keep (a play mode record) gives back the saved fields — the
        // string · the enum · the doubles · the format

        [Test]
        public void TM18_DefaultsAndKeep()
        {
            var size = (TweenMotion)MotionDefaults.Create(typeof(TMPFontSizeItem), MotionKind.Tween, out _);
            var outline = (FlashMotion)MotionDefaults.Create(typeof(TMPOutlineColorItem), MotionKind.Flash, out _);
            var spacing = (PunchMotion)MotionDefaults.Create(typeof(TMPCharacterSpacingItem), MotionKind.Punch, out _);
            Assert.That((size.m_Params.m_To.x, outline.m_Params.m_Target, spacing.m_Params.m_Amplitude.x), Is.EqualTo((MotionDefaults.TMPFontSizeTo, MotionDefaults.SpriteColorTo, MotionDefaults.TMPTextSize)));
            Assert.That((TargetChecks.Requires(typeof(TMPFontSizeItem)), TargetChecks.Requires(typeof(TMPCountToItem))), Is.EqualTo((typeof(TMP_Text), typeof(TMP_Text))));
            Canvas c = MakeCanvas();
            var keepGo = new GameObject("keep", typeof(RectTransform));
            keepGo.transform.SetParent(c.transform, false);
            FeedbackPlayer k = keepGo.AddComponent<FeedbackPlayer>();
            TargetRef Self() => new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };
            k.m_Items.AddRange(new FeedbackItem[]
            {
                new TMPTextItem { m_Text = "World", m_Target = Self(), m_Motion = SetTo(1f) },
                new TMPRevealItem { m_Unit = TMPRevealUnit.Line, m_Target = Self(), m_Motion = TweenTo(Vector4.one) },
                new TMPCountToItem { m_From = -2.5, m_To = 123456789012345, m_Format = "{0:#,0.0}", m_Target = Self(), m_Motion = TweenTo(Vector4.one) },
            });
            var into = new GameObject("TM18Into");
            m_Made.Add(into);
            FeedbackPlayer kept = into.AddComponent<FeedbackPlayer>();
            PlayModeRecord.Apply(kept, PlayModeRecord.Capture(k, false, out int unknown), out PlayModeRecord.Stats stats);
            Assert.That((unknown, stats.Dropped), Is.EqualTo((0, 0)));
            var count = (TMPCountToItem)kept.m_Items[2];
            Assert.That((((TMPTextItem)kept.m_Items[0]).m_Text, ((TMPRevealItem)kept.m_Items[1]).m_Unit, count.m_From, count.m_To, count.m_Format),
                Is.EqualTo(("World", TMPRevealUnit.Line, -2.5, 123456789012345.0, "{0:#,0.0}")), "the saved fields");
        }

        // ---- two texts on one font material — a softness on one is its own (the other and the shared material stay)

        [Test]
        public void Reg16_TheSharedFontMaterialStays()
        {
            Canvas c = MakeCanvas();
            TextMeshProUGUI a = Text(c.transform, "Hello", "a");
            TextMeshProUGUI b = Text(c.transform, "Hello", "b");
            Material shared = a.fontSharedMaterial;
            Assume.That(b.fontSharedMaterial, Is.SameAs(shared), "one font material");
            float soft0 = shared.GetFloat("_OutlineSoftness");
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, On(new TMPSoftnessItem(), a, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 0.5f), Keeper());
            p.Play();
            Tick(s, 12);
            Assert.That((a.fontSharedMaterial.GetFloat("_OutlineSoftness"), ReferenceEquals(b.fontSharedMaterial, shared), B(shared.GetFloat("_OutlineSoftness"))),
                Is.EqualTo((0.5f, true, B(soft0))), "a's own copy · b and the shared material stay");
        }

        // ---- without TMP's Essential Resources (the seam false): Count To and a Character item refuse with the reason, a Progress
        // Bar leaves its label with one warning — nothing thrown, the texts as they were

        [Test]
        public void EQ8_WithoutResourcesTheParsingPathsRefuse()
        {
            FloatingTextBinding.s_TmpResources = () => false;
            Canvas c = MakeCanvas();
            TextMeshProUGUI count = Text(c.transform, "Hello", "count");
            TextMeshProUGUI chars = Text(c.transform, "Hello", "chars");
            TextMeshProUGUI label = Text(c.transform, "Hello", "label");
            var logs = new List<string>();
            Application.LogCallback log = (message, stack, type) =>
            {
                if (type != LogType.Log)
                    logs.Add(type + ": " + message);
            };
            Application.logMessageReceived += log;
            try
            {
                FeedbackScheduler s = Sched();
                Player(s, On(new TMPCountToItem { m_From = 0, m_To = 10, m_Format = "{0:0}" }, count, TweenTo(Vector4.one), 0.3f)).Play();
                Player(s, On(new TMPCharacterOffsetItem(), chars, TweenTo(Vector4.one), 0.3f)).Play();
                Tick(s, 10);
                var go = new GameObject("bar", typeof(RectTransform));
                go.transform.SetParent(c.transform, false);
                ProgressBar bar = go.AddComponent<ProgressBar>();
                bar.m_Text = label;
                bar.SetValueImmediate(0.5f);
                bar.SetValueImmediate(0.25f);
            }
            finally
            {
                Application.logMessageReceived -= log;
            }
            Assert.That((count.text, label.text, logs.Count(l => l.StartsWith("Warning: ") && l.Contains("Essential Resources")), logs.Count),
                Is.EqualTo(("Hello", "Hello", 3, 3)), string.Join("\n", logs));
        }
    }
}
