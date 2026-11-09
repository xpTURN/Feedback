using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    // the screen overlay: a test scheduler ticked by hand (1/60 s). The cover's pixels through a camera of the test's own
    // (RenderForTest — batch draws no screen)
    public sealed class ScreenOverlayTargetsTests
    {
        const float Step = 1f / 60f;
        const string ScenePath = "Assets/ScreenOverlayTargetsTests.unity";

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();

        [TearDown]
        public void TearDown()
        {
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

        static void NeedTMP()
        {
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
                Assert.Ignore("TMP Essential Resources are not in the project");
        }

        FeedbackScheduler Sched(ContextKind kind = ContextKind.Test)
        {
            var s = new FeedbackScheduler(kind);
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

        static string Prefix(ContextKind kind) => kind == ContextKind.Preview ? PropertyLayerRegistry.PreviewPrefix : PropertyLayerRegistry.RuntimePrefix;

        static ScreenFlashItem Flash(Color c, float duration = 0.5f)
        {
            var item = new ScreenFlashItem { m_Color = c, m_Motion = new FlashMotion { m_Params = new FlashParams { m_Target = Vector4.one, m_Attack = 0.25f, m_Hold = 0.5f, m_Release = 0.25f } } };
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static ScreenFadeItem Fade(float to, ScreenFadeShape shape, float duration = 0.1f, ScreenSide from = ScreenSide.Left)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = new Vector4(to, to, to, to);
            var item = new ScreenFadeItem { m_Shape = shape, m_From = from, m_Color = Color.red, m_Motion = new TweenMotion { m_Params = p } };
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static FloatingTextItem Text(ScriptableObject style, FloatingTextSource source = FloatingTextSource.Text, string format = "{0:0}")
            => new FloatingTextItem
            {
                m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" },
                m_Motion = new SetMotion { m_Params = new SetParams { m_Value = Vector4.one } },
                m_Style = style,
                m_Source = source,
                m_Text = "Hi",
                m_Format = format,
                m_PoolSize = 2,
            };

        static int PrefixedLeft(string prefix)
            => Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g != null && g.name.StartsWith(prefix, StringComparison.Ordinal))
               + Resources.FindObjectsOfTypeAll<Texture2D>().Count(t => t != null && t.name.StartsWith(prefix, StringComparison.Ordinal));

        static int CoverSlots(FeedbackScheduler s)
        {
            int n = 0;
            for (int i = 0; i < s.Context.Layers.SlotCount; i++)
            {
                if (s.Context.Layers.TryGetSlot(i, out LayerSlotInfo info) && info.SlotType == typeof(ScreenFadeSlot))
                    n++;
            }
            return n;
        }

        // ---- made whole at the preparation (before a first write) · the same instance · Shutdown leaves nothing · closed refuses

        [Test]
        public void OT03a_MadeAtThePreparationGoneAtShutdown()
        {
            foreach (ContextKind kind in new[] { ContextKind.Test, ContextKind.Preview })
            {
                FeedbackScheduler s = Sched(kind);
                string prefix = Prefix(kind);
                int before = PrefixedLeft(prefix);
                FeedbackPlayer p = Player(s, Flash(Color.white));
                ScreenOverlayService svc = ScreenOverlayService.Of(s.Context);
                Assert.That(svc.Root != null && svc.Texts != null && svc.Cover != null && svc.Iris != null && svc.Hole != null, Is.True, kind + ": whole at the preparation");
                Assert.That(svc.RentedCount, Is.EqualTo(1), kind + ": the flash's image rented at the preparation");
                Assert.That(ScreenOverlayService.Of(s.Context), Is.SameAs(svc), kind + ": one per context");
                Assert.That(PrefixedLeft(prefix), Is.GreaterThan(before), kind + ": the prefixed objects");
                p.ReleaseRuntime();
                s.Shutdown();
                Assert.That(svc.Root == null && svc.Hole == null, Is.True, kind + ": Shutdown destroys the root and the hole");
                Assert.That(svc.IsClosed && svc.Refuses(), Is.True, kind + ": closed — it refuses");
                Assert.That(PrefixedLeft(prefix), Is.EqualTo(before), kind + ": nothing prefixed left");
            }
        }

        [Test]
        public void OT03b_AClosedContextsServiceClosesAtOnce()
        {
            FeedbackScheduler s = Sched();
            int before = PrefixedLeft(PropertyLayerRegistry.RuntimePrefix);
            s.Shutdown();
            ScreenOverlayService svc = ScreenOverlayService.Of(s.Context);
            Assert.That(svc.IsClosed && svc.Root == null, Is.True, "made and closed at once");
            Assert.That(PrefixedLeft(PropertyLayerRegistry.RuntimePrefix), Is.EqualTo(before), "nothing left");
        }

        // ---- names · flags by kind · the UI layer · the sorting order from the settings (its range kept) · the scaler · no
        // raycaster · the children's order (Texts under Fade under Flash)

        [Test]
        public void OT04a_NamesFlagsAndTheCanvas()
        {
            foreach (ContextKind kind in new[] { ContextKind.Test, ContextKind.Preview })
            {
                FeedbackScheduler s = Sched(kind);
                string prefix = Prefix(kind);
                ScreenOverlayService svc = ScreenOverlayService.Of(s.Context);
                GameObject root = svc.Root;
                HideFlags flags = kind == ContextKind.Preview ? HideFlags.HideAndDontSave : HideFlags.None;
                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                Assert.That(all.Select(t => t.name.StartsWith(prefix, StringComparison.Ordinal) && t.gameObject.hideFlags == flags && t.gameObject.layer == ScreenOverlayService.UILayer).Distinct(),
                    Is.EqualTo(new[] { true }), kind + ": every object prefixed · flagged · on the UI layer");
                Assert.That(Enumerable.Range(0, root.transform.childCount).Select(i => root.transform.GetChild(i).name.Substring(prefix.Length)),
                    Is.EqualTo(new[] { "Texts", "Fade", "Flash", "Anchor" }), kind + ": the children's order");
                Assert.That((svc.Canvas.renderMode, svc.Canvas.sortingOrder), Is.EqualTo((RenderMode.ScreenSpaceOverlay, 32000)), kind + ": the canvas");
                var scaler = root.GetComponent<CanvasScaler>();
                Assert.That((scaler.uiScaleMode, scaler.referenceResolution, scaler.matchWidthOrHeight), Is.EqualTo((CanvasScaler.ScaleMode.ScaleWithScreenSize, new Vector2(1920f, 1080f), 0.5f)), kind + ": the scaler");
                Assert.That(root.GetComponentInChildren<GraphicRaycaster>(true), Is.Null, kind + ": no raycaster");
                Assert.That(root.GetComponentsInChildren<Graphic>(true).Where(g => g.raycastTarget), Is.Empty, kind + ": no graphic takes a raycast");
                Assert.That(svc.Texts.GetComponent<Canvas>(), Is.Not.Null, kind + ": the texts' own canvas");
            }
        }

        [Test]
        public void OT04b_TheSortingOrderReadsTheSettings()
        {
            var asset = ScriptableObject.CreateInstance<FeedbackSettings>();
            m_Made.Add(asset);
            var reads = new List<int>();
            foreach (int v in new[] { 1000, 32767, -32768, 32768, 40000, -40000 })
            {
                asset.m_OverlaySortingOrder = v;
                reads.Add(SettingsValues.From(asset).OverlaySortingOrder);
            }
            Assert.That(reads, Is.EqualTo(new[] { 1000, 32767, -32768, 32000, 32000, 32000 }), "past 16 bits reads as the default (a Canvas wraps it)");
            FeedbackScheduler s = Sched();
            asset.m_OverlaySortingOrder = 1000;
            s.Context.UseSettings(SettingsValues.From(asset));
            Assert.That(ScreenOverlayService.Of(s.Context).Canvas.sortingOrder, Is.EqualTo(1000), "the settings reach the canvas");
        }

        // ---- in play the root is in DontDestroyOnLoad (visible); after the exit nothing prefixed — a visible
        // DontDestroyOnLoad object nobody destroyed goes too, a hidden one stays (the control)

        [UnityTest]
        public IEnumerator OT05a_ThePlayExitLeavesNothing()
        {
            yield return new EnterPlayMode();
            ScreenOverlayService svc = ScreenOverlayService.Of(FeedbackScheduler.Default.Context);
            string scene = svc.Root.scene.name;
            HideFlags flags = svc.Root.hideFlags;
            var visible = new GameObject(PropertyLayerRegistry.RuntimePrefix + "OT05Visible");
            Object.DontDestroyOnLoad(visible);
            var hidden = new GameObject(PropertyLayerRegistry.RuntimePrefix + "OT05Hidden") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(hidden);
            yield return null;
            yield return new ExitPlayMode();
            string[] left = Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && g.name.StartsWith(PropertyLayerRegistry.RuntimePrefix, StringComparison.Ordinal)).Select(g => g.name).ToArray();
            foreach (GameObject g in Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g != null && g.name.StartsWith(PropertyLayerRegistry.RuntimePrefix + "OT05", StringComparison.Ordinal)).ToArray())
                Object.DestroyImmediate(g);
            Assert.That((scene, flags), Is.EqualTo(("DontDestroyOnLoad", HideFlags.None)), "in play");
            Assert.That(left, Is.EqualTo(new[] { PropertyLayerRegistry.RuntimePrefix + "OT05Hidden" }), "after the exit — the hidden control alone");
        }

        // ---- the debugger reads the extensions made, never makes one — the overlay's name and rows

        [Test]
        public void OT07a_TheDebuggerReadsTheExtensionsMade()
        {
            FeedbackScheduler s = Sched();
            var reader = new DebuggerReader();
            int services = s.Context.ServiceCount;
            reader.Read(s);
            Assert.That((reader.ExtensionNames.Count, s.Context.ServiceCount), Is.EqualTo((0, services)), "none made — none read, the read makes none");
            Player(s, Flash(Color.white));
            reader.Read(s);
            Assert.That(reader.ExtensionNames, Is.EqualTo(new[] { "Screen overlay" }));
            List<ServiceRow> rows = reader.ExtensionRows[0];
            Assert.That(rows.Select(r => r.Label + " " + r.Value), Is.EqualTo(new[] { "sorting order 32000", "flash images rented 1", "flash images resting 0", "cover 0", "texts live 0", "templates 0" }));
        }

        // ---- an image per item (rented at the preparation) · the item's color · alpha at LateApply — after the layers (the
        // ramp's value of the frame) · off at the end · back to rest when its player goes

        [Test]
        public void OT08a_AFlashImagePerItem()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer a = Player(s, Flash(Color.red));
            FeedbackPlayer b = Player(s, Flash(Color.blue));
            ScreenOverlayService svc = ScreenOverlayService.Of(s.Context);
            int rented = svc.RentedCount;
            Image ia = svc.RentedAt(0), ib = svc.RentedAt(1);
            Assert.That(ia.enabled || ib.enabled, Is.False, "off before a play");
            a.Play();
            b.Play();
            Tick(s, 3);
            float rampImage = ia.canvasRenderer.GetAlpha(), rampLate = svc.LateApplyAlpha;
            Tick(s, 12);
            float aa = ia.canvasRenderer.GetAlpha(), ab = ib.canvasRenderer.GetAlpha(), late = svc.LateApplyAlpha;
            bool onMid = ia.enabled && ib.enabled;
            Tick(s, 40);
            bool offEnd = !ia.enabled && !ib.enabled;
            a.ReleaseRuntime();
            Assert.That(rented, Is.EqualTo(2));
            Assert.That(ia, Is.Not.SameAs(ib), "an image per item");
            Assert.That((ia.color, ib.color), Is.EqualTo((Color.red, Color.blue)), "the items' colors (opaque — the alpha is the CanvasRenderer's)");
            Assert.That(rampImage, Is.GreaterThan(0f).And.LessThan(1f), "on the ramp");
            Assert.That(rampLate, Is.EqualTo(rampImage), "LateApply after the layers — the ramp's value of this frame");
            Assert.That(onMid, Is.True, "on in the hold");
            Assert.That(new[] { aa, ab, late }, Is.All.EqualTo(1f).Within(1e-4f), "the hold");
            Assert.That(offEnd, Is.True, "off at the end");
            Assert.That((svc.RentedCount, svc.RestingCount), Is.EqualTo((1, 1)), "a released player's image rests");
        }

        // ---- the flash's category is Flash (declared None · flash-sensitive) — the category off and the flash limit hold it

        [Test]
        public void OT09a_TheFlashCategory()
        {
            var shown = new List<bool>();
            foreach (string gate in new[] { "open", "off", "limit" })
            {
                FeedbackScheduler s = Sched();
                if (gate == "off")
                    s.Context.Categories.SetEnabled(FeedbackCategory.Flash, false);
                else if (gate == "limit")
                    s.Context.Categories.SetFlashLimit(0);
                FeedbackPlayer p = Player(s, Flash(Color.white));
                ScreenOverlayService svc = ScreenOverlayService.Of(s.Context);
                p.Play();
                Tick(s, 12);
                shown.Add(svc.RentedAt(0).enabled);
            }
            Assert.That(Flash(Color.white).EffectiveCategory, Is.EqualTo(FeedbackCategory.Flash), "a flash's effective category");
            Assert.That(shown, Is.EqualTo(new[] { true, false, false }), "open · the category off · the flash limit");
        }

        // ---- one cover for the context (counted right after the preparation) · a Tween's end stays (Persist) · another item's
        // fade takes the same cover back · its look is the last begun's · the cover stays when its player goes

        [Test]
        public void OT10a_OneCoverForEveryFade()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer cover = Player(s, Fade(1f, ScreenFadeShape.Plain));
            FeedbackPlayer reveal = Player(s, Fade(0f, ScreenFadeShape.Wipe));
            ScreenOverlayService svc = ScreenOverlayService.Of(s.Context);
            Assert.That(CoverSlots(s), Is.EqualTo(1), "one cover slot for the context");
            cover.Play();
            Tick(s, 12);
            float afterCover = svc.Fade, alpha = svc.Cover.canvasRenderer.GetAlpha();
            bool coverOn = svc.Cover.enabled;
            ScreenFadeShape first = svc.Shape;
            Tick(s, 30);
            float stays = svc.Fade;
            reveal.Play();
            Tick(s, 3);
            ScreenFadeShape look = svc.Shape;
            float mid = svc.Fade, anchor = svc.Cover.rectTransform.anchorMax.x;
            Tick(s, 12);
            float afterReveal = svc.Fade;
            bool off = !svc.Cover.enabled;
            cover.Play();
            Tick(s, 12);
            ScreenFadeShape again = svc.Shape;
            cover.ReleaseRuntime();
            Tick(s, 3);
            Assert.That((coverOn, first), Is.EqualTo((true, ScreenFadeShape.Plain)), "covered");
            Assert.That(new[] { afterCover, alpha, stays }, Is.All.EqualTo(1f).Within(1e-4f), "covered · the end stays");
            Assert.That(look, Is.EqualTo(ScreenFadeShape.Wipe), "the last begun's look");
            Assert.That(mid, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(anchor, Is.EqualTo(mid).Within(1e-5f), "a wipe from the left — the cover's right edge is the value");
            Assert.That(afterReveal, Is.EqualTo(0f).Within(1e-4f), "another item's reveal takes the cover back");
            Assert.That(off, Is.True, "off at 0");
            Assert.That(again, Is.EqualTo(ScreenFadeShape.Plain), "each start takes the look");
            Assert.That(svc.Fade, Is.EqualTo(1f).Within(1e-4f), "the cover stays when its player goes");
        }

        // ---- the cover's pixels — plain at a half · a wipe from each side · the iris (inside clear · outside covered) · the
        // iris at 1 is the plain cover · 0 draws nothing

        [Test]
        public void OT11a_TheCoverPixels()
        {
            FeedbackScheduler s = Sched();
            ScreenOverlayService svc = ScreenOverlayService.Of(s.Context);
            var camGo = new GameObject("OT11 camera");
            m_Made.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var rt = new RenderTexture(128, 64, 24);
            m_Made.Add(rt);
            var read = new Texture2D(128, 64, TextureFormat.RGBA32, false);
            m_Made.Add(read);
            cam.targetTexture = rt;
            try
            {
                svc.RenderForTest(cam);
                void Render()
                {
                    Canvas.ForceUpdateCanvases();
                    cam.Render();
                    RenderTexture.active = rt;
                    read.ReadPixels(new Rect(0, 0, 128, 64), 0, 0);
                    read.Apply();
                    RenderTexture.active = null;
                }
                Color At(float x, float y) => read.GetPixel(Mathf.RoundToInt(x), Mathf.RoundToInt(y));
                bool Clear(Color c) => c.r + c.g + c.b < 0.15f;
                svc.SetLook(Fade(1f, ScreenFadeShape.Plain), false, Vector3.zero);
                svc.WriteFade(0.5f);
                Render();
                Assert.That(At(64, 32).r, Is.InRange(0.6f, 0.85f), "plain at a half (sRGB of a half red)");
                foreach (ScreenSide side in Enum.GetValues(typeof(ScreenSide)))
                {
                    svc.SetLook(Fade(1f, ScreenFadeShape.Wipe, from: side), false, Vector3.zero);
                    svc.WriteFade(0.3f);
                    Render();
                    (Vector2 inside, Vector2 outside) = side switch
                    {
                        ScreenSide.Left => (new Vector2(20f, 32f), new Vector2(50f, 32f)),
                        ScreenSide.Right => (new Vector2(107f, 32f), new Vector2(77f, 32f)),
                        ScreenSide.Bottom => (new Vector2(64f, 8f), new Vector2(64f, 26f)),
                        _ => (new Vector2(64f, 55f), new Vector2(64f, 37f)),
                    };
                    Assert.That((At(inside.x, inside.y).r > 0.9f, Clear(At(outside.x, outside.y))), Is.EqualTo((true, true)), "the wipe from " + side);
                }
                svc.SetLook(Fade(1f, ScreenFadeShape.Iris), false, Vector3.zero);
                svc.WriteFade(0.7f);
                Render();
                Rect r = svc.Iris.rectTransform.rect;
                float radius = 0.3f * Mathf.Sqrt(r.width * r.width + r.height * r.height) * (128f / r.width);
                Assert.That((Clear(At(64, 32)), Clear(At(64 + 0.8f * radius, 32)), At(Mathf.Min(127, 64 + 1.25f * radius), 32).r > 0.8f, At(1, 1).r > 0.8f),
                    Is.EqualTo((true, true, true, true)), "the iris — center · 0.8 r clear, 1.25 r · the corner covered");
                svc.WriteFade(1f);
                Render();
                Assert.That((At(64, 32).r > 0.9f, svc.Cover.enabled, svc.Iris.enabled), Is.EqualTo((true, true, false)), "the iris at 1 is the plain cover");
                svc.WriteFade(0f);
                Render();
                Assert.That((Clear(At(64, 32)), svc.Cover.enabled, svc.Iris.enabled), Is.EqualTo((true, false, false)), "nothing at 0");
            }
            finally
            {
                cam.targetTexture = null;
            }
        }

        // ---- the default style is code values — no ScriptableObject made · a template of TMP's default font · a style's font
        // · a style's prefab is the template · another ScriptableObject in the style is refused

        [Test]
        public void OT16a_TheStyles()
        {
            NeedTMP();
            FeedbackScheduler s = Sched();
            ScreenOverlayService svc = ScreenOverlayService.Of(s.Context);
            int styles = Resources.FindObjectsOfTypeAll<FloatingTextStyle>().Length;
            FloatingTextLook look = FloatingTextLook.Of(null);
            GameObject template = svc.TemplateOf(null);
            Assert.That(Resources.FindObjectsOfTypeAll<FloatingTextStyle>().Length, Is.EqualTo(styles), "no style made for the default");
            Assert.That((look.Lifetime, look.Distance, look.ScaleStart, look.AlphaStart, look.Alpha.Evaluate(1f), look.MoveY.Evaluate(0.5f)),
                Is.EqualTo((new Vector2(0.8f, 0.8f), new Vector2(0f, 80f), Vector2.one, Vector2.one, 0f, 0.5f)), "the code's defaults — up 80 px over 0.8 s, fading at the end");
            var text = template.GetComponent<TextMeshProUGUI>();
            Assert.That((text.font, text.fontSize, text.raycastTarget, template.activeSelf), Is.EqualTo((TMP_Settings.defaultFontAsset, FloatingTextLook.DefaultFontSize, false, false)));
            Assert.That(svc.TemplateOf(null), Is.SameAs(template), "one default template");

            var style = ScriptableObject.CreateInstance<FloatingTextStyle>();
            m_Made.Add(style);
            style.m_FontSize = 50f;
            style.m_Lifetime = new Vector2(1.5f, 1.5f);
            GameObject own = svc.TemplateOf(style);
            Assert.That((own != template, own.GetComponent<TextMeshProUGUI>().fontSize, FloatingTextLook.Of(style).Lifetime), Is.EqualTo((true, 50f, new Vector2(1.5f, 1.5f))), "a style's own");
            var prefab = new GameObject("OT16 prefab", typeof(RectTransform));
            m_Made.Add(prefab);
            prefab.AddComponent<TextMeshProUGUI>();
            var styled = ScriptableObject.CreateInstance<FloatingTextStyle>();
            m_Made.Add(styled);
            styled.m_Prefab = prefab;
            Assert.That(svc.TemplateOf(styled), Is.SameAs(prefab), "a style's prefab is the template");

            var other = ScriptableObject.CreateInstance<FeedbackSettings>();
            m_Made.Add(other);
            FeedbackPlayer p = Player(s, Text(other), Text(style));
            Assert.That((p.Runtime.Binding(0) == null, p.Runtime.Binding(1) != null), Is.EqualTo((true, true)), "another ScriptableObject is refused");
        }

        // ---- the scale's start range is the scale's own — the opacity's changing leaves it

        [Test]
        public void OT17a_Reg18_TheScaleStartRangeIsItsOwn()
        {
            var style = ScriptableObject.CreateInstance<FloatingTextStyle>();
            m_Made.Add(style);
            style.m_ScaleStart = new Vector2(2f, 3f);
            style.m_AlphaStart = new Vector2(0.1f, 0.2f);
            FloatingTextLook look = FloatingTextLook.Of(style);
            var a = new float[64];
            for (uint i = 0; i < a.Length; i++)
                a[i] = ScreenOverlayService.ScaleStartOf(in look, 1234u + i * 7919u);
            style.m_AlphaStart = new Vector2(0.9f, 5f);
            look = FloatingTextLook.Of(style);
            int same = 0;
            for (uint i = 0; i < a.Length; i++)
            {
                if (BitConverter.SingleToInt32Bits(ScreenOverlayService.ScaleStartOf(in look, 1234u + i * 7919u)) == BitConverter.SingleToInt32Bits(a[i]))
                    same++;
            }
            Assert.That(a.Min(), Is.GreaterThanOrEqualTo(2f));
            Assert.That(a.Max(), Is.LessThanOrEqualTo(3f));
            Assert.That(a.Max() - a.Min(), Is.GreaterThan(0.5f), "spread over the range");
            Assert.That(same, Is.EqualTo(64), "unchanged by the opacity");
        }

        // ---- the progress bar — the five fills from each side (a bar's own size) · its flow on a test scheduler
        // · the edit mode has no runner (a value written at once)

        GameObject BarCanvas()
        {
            var go = new GameObject("OT bar canvas", typeof(RectTransform));
            m_Made.Add(go);
            go.AddComponent<Canvas>();
            return go;
        }

        static ProgressBar Bar(GameObject canvas, ProgressBarFill fill, ScreenSide side)
        {
            var root = new GameObject("bar", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            ((RectTransform)root.transform).sizeDelta = new Vector2(200f, 20f);
            RectTransform Child(string name)
            {
                var c = new GameObject(name, typeof(RectTransform));
                c.transform.SetParent(root.transform, false);
                var r = (RectTransform)c.transform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = Vector2.zero;
                r.offsetMax = Vector2.zero;
                c.AddComponent<Image>().type = Image.Type.Filled;
                return r;
            }
            RectTransform fg = Child("fg"), dec = Child("dec"), inc = Child("inc");
            var bar = root.AddComponent<ProgressBar>();
            bar.m_Foreground = fg;
            bar.m_DelayedDecrease = dec;
            bar.m_DelayedIncrease = inc;
            bar.m_Fill = fill;
            bar.m_From = side;
            return bar;
        }

        [Test]
        public void OT18a_TheFiveFillsFromEachSide()
        {
            GameObject canvas = BarCanvas();
            var fails = new List<string>();
            foreach (ProgressBarFill fill in Enum.GetValues(typeof(ProgressBarFill)))
            {
                foreach (ScreenSide side in Enum.GetValues(typeof(ScreenSide)))
                {
                    ProgressBar bar = Bar(canvas, fill, side);
                    bar.SetValueImmediate(0.25f);
                    RectTransform fg = bar.m_Foreground;
                    bool across = side == ScreenSide.Left || side == ScreenSide.Right;
                    string got = fill switch
                    {
                        ProgressBarFill.LocalScale => $"{fg.localScale.x:0.00},{fg.localScale.y:0.00}",
                        ProgressBarFill.FillAmount => $"{fg.GetComponent<Image>().fillAmount:0.00}",
                        ProgressBarFill.Width => $"{fg.rect.width:0.0}",
                        ProgressBarFill.Height => $"{fg.rect.height:0.0}",
                        _ => $"{fg.anchorMin.x:0.00},{fg.anchorMin.y:0.00},{fg.anchorMax.x:0.00},{fg.anchorMax.y:0.00}",
                    };
                    string want = fill switch
                    {
                        ProgressBarFill.LocalScale => across ? "0.25,1.00" : "1.00,0.25",
                        ProgressBarFill.FillAmount => "0.25",
                        ProgressBarFill.Width => "50.0",
                        ProgressBarFill.Height => "5.0",
                        _ => side switch
                        {
                            ScreenSide.Left => "0.00,0.00,0.25,1.00",
                            ScreenSide.Right => "0.75,0.00,1.00,1.00",
                            ScreenSide.Bottom => "0.00,0.00,1.00,0.25",
                            _ => "0.00,0.75,1.00,1.00",
                        },
                    };
                    if (got != want)
                        fails.Add(fill + "/" + side + " " + got + " (want " + want + ")");
                }
            }
            ProgressBar narrow = Bar(canvas, ProgressBarFill.Width, ScreenSide.Left);
            narrow.m_DelayedDecrease.offsetMax = new Vector2(-100f, 0f);
            narrow.SetValueImmediate(0.25f);
            if (Mathf.Abs(narrow.m_DelayedDecrease.rect.width - 25f) > 0.01f || Mathf.Abs(narrow.m_Foreground.rect.width - 50f) > 0.01f)
                fails.Add("a bar's own size — dec " + narrow.m_DelayedDecrease.rect.width + " fg " + narrow.m_Foreground.rect.width);
            Assert.That(fails, Is.Empty);
        }

        [Test]
        public void OT19a_TheFlow()
        {
            GameObject canvas = BarCanvas();
            ProgressBar bar = Bar(canvas, ProgressBarFill.LocalScale, ScreenSide.Left);
            FeedbackScheduler s = Sched();
            bar.SetValueImmediate(1f);
            bar.m_Unscaled = false;
            bar.Bind(s);
            int started = 0, ended = 0, bumps = 0;
            bar.MoveStarted += _ => started++;
            bar.MoveEnded += _ => ended++;
            bar.Bumped += _ => bumps++;
            bar.SetValue(0.5f);
            Tick(s, 6);
            float fg6 = bar.ShownOf(0), dec6 = bar.ShownOf(1);
            Tick(s, 60);
            float fgEnd = bar.ShownOf(0), decEnd = bar.ShownOf(1);
            bar.SetValue(0.8f);
            Tick(s, 2);
            float inc2 = bar.ShownOf(2), fg2 = bar.ShownOf(0);
            Vector3 bumped = bar.transform.localScale;
            Tick(s, 80);
            Assert.That(fg6, Is.GreaterThan(0.5f).And.LessThan(1f), "a loss — the foreground moves at once");
            Assert.That(dec6, Is.EqualTo(1f), "the decrease bar keeps the loss for its delay");
            Assert.That(new[] { fgEnd, decEnd }, Is.All.EqualTo(0.5f).Within(1e-4f));
            Assert.That((inc2, fg2), Is.EqualTo((0.8f, 0.5f)), "a gain — the increase bar at once, the foreground waits");
            Assert.That(bumped.x, Is.GreaterThan(1f), "the gain's bump");
            Assert.That(new[] { bar.ShownOf(0), bar.transform.localScale.x }, Is.EqualTo(new[] { 0.8f, 1f }).Within(1e-4f), "the end · the bump's scale back");
            Assert.That((started, ended, bumps), Is.EqualTo((2, 2, 1)), "the events");
        }

        [Test]
        public void OT20a_TheEditModeHasNoRunner()
        {
            ProgressBar bar = Bar(BarCanvas(), ProgressBarFill.LocalScale, ScreenSide.Left);
            bar.gameObject.SetActive(false);
            bar.gameObject.SetActive(true);
            bool running = bar.Running;
            bar.SetValue(0.5f);
            Assert.That((running, bar.Running), Is.EqualTo((false, false)), "no runner in the edit mode");
            Assert.That((bar.ShownOf(0), bar.m_Foreground.localScale.x), Is.EqualTo((0.5f, 0.5f)), "a value written at once (the user's call)");
        }

        // ---- the preview — the flash · the cover move and come back · the notice 'Game view' · the scene clean · nothing hidden
        // left · a missed reload's recovery finds the root and the hole

        [Test]
        public void OT21a_ThePreview()
        {
            NeedTMP();
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
                var go = new GameObject("OT21 player");
                var player = go.AddComponent<FeedbackPlayer>();
                player.m_Items.Add(Flash(Color.white, 1f));
                player.m_Items.Add(Fade(1f, ScreenFadeShape.Iris, 1f));
                player.m_Items.Add(Text(null));
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                for (int i = 0; i < 60; i++)
                {
                    now += 1.0 / 120.0;
                    PreviewSession.Current?.Update();
                }
                ScreenOverlayService svc = ScreenOverlayService.Of(PreviewSession.Current.Scheduler.Context);
                bool flashOn = svc.RentedCount == 1 && svc.RentedAt(0).enabled && svc.RentedAt(0).canvasRenderer.GetAlpha() > 0f;
                float cover = svc.Fade;
                bool hiddenRoot = svc.Root.hideFlags == HideFlags.HideAndDontSave;
                var notices = new List<PreviewNotice>();
                PreviewNotices.Collect(PreviewSession.Current, notices);
                PreviewSession.Current.Stop();
                Assert.That((flashOn, hiddenRoot), Is.EqualTo((true, true)), "the flash moved on the hidden overlay");
                Assert.That(cover, Is.GreaterThan(0f).And.LessThan(1f), "the cover moved");
                Assert.That(notices.Where(n => n.Kind == PreviewNoticeKind.ScreenGameView), Is.Not.Empty, "the notice 'Game view'");
                Assert.That(PrefixedLeft(PropertyLayerRegistry.PreviewPrefix), Is.Zero, "nothing hidden left");
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

        [Test]
        public void OT21b_AMissedReloadsRecovery()
        {
            FeedbackScheduler s = Sched(ContextKind.Preview);
            ScreenOverlayService svc = ScreenOverlayService.Of(s.Context);
            int made = PrefixedLeft(PropertyLayerRegistry.PreviewPrefix);
            PreviewRecovery.Run();
            Assert.That(made, Is.GreaterThan(0), "the overlay's hidden objects");
            Assert.That((svc.Root == null, svc.Hole == null), Is.EqualTo((true, true)), "the root and the hole found by their prefix");
            Assert.That(PrefixedLeft(PropertyLayerRegistry.PreviewPrefix), Is.Zero);
        }

        // ---- FB-P08 ~ FB-P11 (quiet without their items) · the add menu's defaults · the keep (the string · the enums · a
        // style reference — Self)

        [Test]
        public void OT22a_TheRules()
        {
            NeedTMP();
            var go = new GameObject("OT22 host");
            m_Made.Add(go);
            var host = go.AddComponent<FeedbackPlayer>();
            Assert.That(FeedbackValidator.Run(host).Where(r => r.Id.StartsWith("FB-P", StringComparison.Ordinal)), Is.Empty, "quiet without their items");
            var other = ScriptableObject.CreateInstance<FeedbackSettings>();
            m_Made.Add(other);
            var style = ScriptableObject.CreateInstance<FloatingTextStyle>();
            m_Made.Add(style);
            // a font asset of its own (a copy's OnDestroy would destroy the original's atlas and material)
            var dynamicFont = ScriptableObject.CreateInstance<TMP_FontAsset>();
            m_Made.Add(dynamicFont);
            dynamicFont.name = "OT22 dynamic";
            dynamicFont.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            var dynamicStyle = ScriptableObject.CreateInstance<FloatingTextStyle>();
            m_Made.Add(dynamicStyle);
            dynamicStyle.m_Font = dynamicFont;
            FloatingTextItem Item(ScriptableObject st, FloatingTextSource source, string text, string format)
            {
                FloatingTextItem t = Text(st, source, format);
                t.m_Text = text;
                return t;
            }
            host.m_Items.AddRange(new FeedbackItem[]
            {
                Item(other, FloatingTextSource.Text, "Hi", "{0:0}"),                    // 0 P08
                Item(style, FloatingTextSource.Text, "Hi", "{0:0}"),                    // 1
                Item(null, FloatingTextSource.Text, "\u2603", "{0:0}"),                 // 2 P09 (the default font has no snowman)
                Item(null, FloatingTextSource.Intensity, "", "{0:#,0.0} pts"),          // 3
                Item(dynamicStyle, FloatingTextSource.Text, "Hi", "{0:0}"),             // 4 P09 (dynamic)
                Item(null, FloatingTextSource.Intensity, "", "{1}"),                    // 5 P11
                Item(null, FloatingTextSource.Text, "Hi", "{1}"),                       // 6 (the text writes no number)
            });
            Assert.That(TMP_Settings.defaultFontAsset.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static), "the default font is static");
            List<RuleResult> got = FeedbackValidator.Run(host).Where(r => r.Id == "FB-P08" || r.Id == "FB-P09" || r.Id == "FB-P11").ToList();
            Assert.That(got.Select(r => r.Id + "@" + r.Item + ":" + r.Severity).OrderBy(x => x, StringComparer.Ordinal),
                Is.EqualTo(new[] { "FB-P08@0:Warning", "FB-P09@2:Info", "FB-P09@4:Info", "FB-P11@5:Warning" }));

            GameObject canvas = BarCanvas();
            ProgressBar fits = Bar(canvas, ProgressBarFill.FillAmount, ScreenSide.Left);
            foreach (Image image in fits.GetComponentsInChildren<Image>())
                image.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), Vector2.zero);
            ProgressBar noSprite = Bar(canvas, ProgressBarFill.FillAmount, ScreenSide.Left);
            ProgressBar centered = Bar(canvas, ProgressBarFill.LocalScale, ScreenSide.Right);
            ProgressBar pivoted = Bar(canvas, ProgressBarFill.Width, ScreenSide.Right);
            foreach (RectTransform r in new[] { pivoted.m_Foreground, pivoted.m_DelayedDecrease, pivoted.m_DelayedIncrease })
                r.pivot = new Vector2(1f, 0.5f);
            ProgressBar across = Bar(canvas, ProgressBarFill.Height, ScreenSide.Left);
            ProgressBar anchors = Bar(canvas, ProgressBarFill.Anchor, ScreenSide.Top);
            int P10(ProgressBar b) => FeedbackValidator.Run(b).Count(r => r.Id == "FB-P10" && r.Severity == RuleSeverity.Warning);
            Assert.That(new[] { P10(fits), P10(noSprite), P10(centered), P10(pivoted), P10(across), P10(anchors) }, Is.EqualTo(new[] { 0, 3, 3, 0, 3, 0 }), "FB-P10 per bar");
            Assert.That(ValidatorScan.OpenSceneHosts().Count(h => h is ProgressBar), Is.EqualTo(6), "the scan finds the bars");
        }

        [Test]
        public void OT22b_DefaultsAndKeep()
        {
            const string stylePath = "Assets/OT22Style.asset";
            var flash = (FlashMotion)MotionDefaults.Create(typeof(ScreenFlashItem), MotionKind.Flash, out float flashDuration);
            var fade = (TweenMotion)MotionDefaults.Create(typeof(ScreenFadeItem), MotionKind.Tween, out float fadeDuration);
            FeedbackMotion text = MotionDefaults.Create(typeof(FloatingTextItem), MotionKind.Set, out _);
            Assert.That((flash.m_Params.m_Target.x, flashDuration), Is.EqualTo((1f, MotionDefaults.ScreenFlashDuration)), "a flash to 1 for 0.2 s");
            Assert.That((fade.m_Params.m_To.x, fadeDuration), Is.EqualTo((1f, -1f)), "a fade covers (the default length)");
            Assert.That(text, Is.TypeOf<SetMotion>());

            var style = ScriptableObject.CreateInstance<FloatingTextStyle>();
            AssetDatabase.CreateAsset(style, stylePath);
            try
            {
                var go = new GameObject("OT22 keep");
                m_Made.Add(go);
                FeedbackPlayer k = go.AddComponent<FeedbackPlayer>();
                ScreenFadeItem f = Fade(1f, ScreenFadeShape.Iris, 1f, ScreenSide.Top);
                f.m_Center = ScreenFadeCenter.PlayPosition;
                FloatingTextItem t = Text(style, FloatingTextSource.Intensity, "{0:0.0}");
                t.m_Text = "Crit";
                k.m_Items.AddRange(new FeedbackItem[] { Flash(Color.green), f, t });
                var into = new GameObject("OT22 into");
                m_Made.Add(into);
                FeedbackPlayer kept = into.AddComponent<FeedbackPlayer>();
                PlayModeRecord.Apply(kept, PlayModeRecord.Capture(k, false, out int unknown), out PlayModeRecord.Stats stats);
                Assert.That((unknown, stats.Dropped), Is.EqualTo((0, 0)));
                var kf = (ScreenFadeItem)kept.m_Items[1];
                var kt = (FloatingTextItem)kept.m_Items[2];
                Assert.That((((ScreenFlashItem)kept.m_Items[0]).m_Color, kf.m_Shape, kf.m_From, kf.m_Center), Is.EqualTo((Color.green, ScreenFadeShape.Iris, ScreenSide.Top, ScreenFadeCenter.PlayPosition)), "the enums · the color");
                Assert.That((kt.m_Style, kt.m_Source, kt.m_Text, kt.m_Format), Is.EqualTo(((ScriptableObject)style, FloatingTextSource.Intensity, "Crit", "{0:0.0}")), "the style reference (an asset's) · the strings");
            }
            finally
            {
                AssetDatabase.DeleteAsset(stylePath);
            }
        }

        // the fill's values are saved: add values at the end and rows here
        [Test]
        public void OT18b_TheFillValues()
        {
            var golden = new[] { ("LocalScale", 0), ("FillAmount", 1), ("Width", 2), ("Height", 3), ("Anchor", 4) };
            Assert.That(Enum.GetNames(typeof(ProgressBarFill)).Length, Is.EqualTo(golden.Length), "a value without a row");
            foreach ((string name, int value) in golden)
                Assert.That((int)Enum.Parse(typeof(ProgressBarFill), name), Is.EqualTo(value), name);
        }
    }
}
