using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the uGUI items: a test scheduler ticked by hand (0.05 s); this assembly names uGUI (a package — the main test assemblies
    // can't)
    public sealed class UGUITargetsTests
    {
        const float Step = 0.05f;
        const string ScenePath = "Assets/UGUITargetsTests.unity";

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

        static string B(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");

        static string C(Color c) => B(c.r) + B(c.g) + B(c.b) + B(c.a);

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };

        static TweenMotion TweenTo(Vector4 to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = to;
            return new TweenMotion { m_Params = p };
        }

        static OscillateMotion Swing(float amplitude) => new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(amplitude, amplitude, amplitude, amplitude), m_Frequency = 2f } };

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

        // a punch on the player's own position keeps a play running (a Revert stop · a Restore have a play to stop)
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

        T UI<T>(Transform parent, string name = "ui") where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(100f, 100f);
            return go.AddComponent<T>();
        }

        Sprite MakeSprite(Color c)
        {
            var t = new Texture2D(4, 4);
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++)
                px[i] = c;
            t.SetPixels(px);
            t.Apply();
            m_Made.Add(t);
            var sprite = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
            m_Made.Add(sprite);
            return sprite;
        }

        // ---- the color · alpha — a contribution ends at the base bit for bit · the alpha's override is the alpha only · a Text's
        // color · a game write between the revert and the apply stays · the light moved before the end (the positive)

        [Test]
        public void UG03_ColorAndAlpha()
        {
            var fails = new List<string>();
            Canvas c = MakeCanvas();
            foreach ((string name, Func<Graphic> make, Func<Graphic, TargetItem> item) in new (string, Func<Graphic>, Func<Graphic, TargetItem>)[]
            {
                ("image color punch", () => UI<Image>(c.transform), g => On(new GraphicColorItem(), g, Punch(0.4f), 1f)),
                ("image color flash HDR", () => UI<Image>(c.transform), g => On(new GraphicColorItem(), g, FlashTo(new Vector4(2f, 0.1f, -0.5f, 1f)), 1f)),
                ("text color punch", () => UI<Text>(c.transform), g => On(new GraphicColorItem(), g, Punch(0.5f), 1f)),
                ("alpha flash", () => UI<Image>(c.transform), g => On(new GraphicAlphaItem(), g, FlashTo(Vector4.zero), 1f)),
                ("raw image alpha spring", () => UI<RawImage>(c.transform), g => On(new GraphicAlphaItem(), g, new SpringMotion(), 1f)),
            })
            {
                FeedbackScheduler s = Sched();
                Graphic g = make();
                g.color = new Color(0.25f, 0.5f, 0.75f, 0.9f);
                string before = C(g.color);
                Player(s, item(g)).Play();
                bool moved = false;
                for (int i = 0; i < 30; i++)
                {
                    s.Tick(Step);
                    moved |= C(g.color) != before;
                }
                s.Tick(0f);
                if (!moved)
                    fails.Add(name + " never moved (the positive)");
                if (C(g.color) != before)
                    fails.Add(name + " ended at " + g.color + " not the base");
            }
            {
                // the alpha's flash keeps a color contribution's rgb
                FeedbackScheduler s = Sched();
                Image g = UI<Image>(c.transform);
                g.color = Color.white;
                Player(s, On(new GraphicColorItem(), g, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 0.2f), On(new GraphicAlphaItem(), g, FlashTo(Vector4.zero), 1f)).Play();
                Tick(s, 8);
                if (!(g.color.g < 0.01f && g.color.a < 1f))
                    fails.Add("alpha over a color tween — " + g.color);
            }
            {
                FeedbackScheduler s = Sched();
                Image g = UI<Image>(c.transform);
                Player(s, On(new GraphicColorItem(), g, Punch(0.3f), 1f)).Play();
                Tick(s, 3);
                s.RunRevert();
                g.color = new Color(0.125f, 0.25f, 0.375f, 1f);
                s.RunUpdate(new FrameTime(Step, Step));
                s.RunLateApply();
                Tick(s, 30);
                s.Tick(0f);
                if (C(g.color) != C(new Color(0.125f, 0.25f, 0.375f, 1f)))
                    fails.Add("the game's color became " + g.color);
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- Graphic Alpha shares the color's slot — one slot · the alpha's revert leaves the color's rgb · the reverts give the
        // original in either reverse order (two slots on one property wipe each other's Persist) · an alpha flash holds the alpha
        // only
        [Test]
        public void UG03b_AlphaSharesTheColorsSlot()
        {
            Canvas c = MakeCanvas();
            Image g = UI<Image>(c.transform);
            var c0 = new Color(0.25f, 0.5f, 0.75f, 0.9f);
            g.color = c0;
            FeedbackScheduler s = Sched();
            FeedbackPlayer pc = Player(s, On(new GraphicColorItem(), g, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 0.3f), Keeper());
            FeedbackPlayer pa = Player(s, On(new GraphicAlphaItem(), g, TweenTo(new Vector4(0.25f, 0f, 0f, 0f)), 0.3f), Keeper());
            Assert.That(((GraphicAlphaBinding)pa.Runtime.Binding(0)).Slot, Is.SameAs(((GraphicColorBinding)pc.Runtime.Binding(0)).Slot), "one slot");
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
        }

        // ---- the cross fade — the CanvasRenderer's color goes and comes back · a Selectable's tint and the fade write over each
        // other (FB-P01): the tint's write, our next frame over it, the revert to the fade's start (the tint's state lost)

        [Test]
        public void UG04_CrossFade()
        {
            var fails = new List<string>();
            Canvas c = MakeCanvas();
            {
                FeedbackScheduler s = Sched();
                Image g = UI<Image>(c.transform);
                FeedbackPlayer p = Player(s, On(new GraphicCrossFadeItem(), g, TweenTo(new Vector4(1f, 1f, 1f, 0f)), 0.5f), Keeper());
                p.Play();
                Tick(s, 12);
                Color at = g.canvasRenderer.GetColor();
                if (at.a > 0.01f)
                    fails.Add("the fade's end " + at);
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                if (C(g.canvasRenderer.GetColor()) != C(Color.white))
                    fails.Add("the revert " + g.canvasRenderer.GetColor());
            }
            {
                FeedbackScheduler s = Sched();
                Image g = UI<Image>(c.transform, "tinted");
                var button = g.gameObject.AddComponent<Button>();
                button.targetGraphic = g;
                button.transition = Selectable.Transition.ColorTint;
                FeedbackPlayer p = Player(s, On(new GraphicCrossFadeItem(), g, TweenTo(new Vector4(1f, 1f, 1f, 0.5f)), 0.5f), Keeper());
                p.Play();
                Tick(s, 4);
                button.interactable = false;
                Color tint = g.canvasRenderer.GetColor();
                Tick(s, 1);
                Color next = g.canvasRenderer.GetColor();
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                Color back = g.canvasRenderer.GetColor();
                if (tint.r > 0.9f)
                    fails.Add("the tint did not write — " + tint);
                if (B(next.r) != B(1f) || next.a >= 1f)
                    fails.Add("the fade's next frame is not over the tint — " + next);
                if (C(back) != C(Color.white))
                    fails.Add("the revert is not the fade's start — " + back);
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- the copy — put in once, the default UI material untouched, the original back at the end (the field itself), the
        // copy destroyed · under a Mask the material drawn takes the offset · a material swap replaces the copy

        [Test]
        public void UG05_TheCopy()
        {
            var fails = new List<string>();
            Canvas c = MakeCanvas();
            Material shared = Canvas.GetDefaultCanvasMaterial();
            Vector2 sharedOffset = shared.mainTextureOffset;
            {
                FeedbackScheduler s = Sched();
                Image g = UI<Image>(c.transform);
                g.sprite = MakeSprite(Color.red);
                FeedbackPlayer p = Player(s, On(new GraphicTextureOffsetItem(), g, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 0.5f), On(new GraphicTextureScaleItem(), g, TweenTo(new Vector4(2f, 1f, 0f, 0f)), 0.5f), Keeper());
                p.Play();
                Material copy = g.material;
                bool once = true;
                for (int i = 0; i < 12; i++)
                {
                    s.Tick(Step);
                    once &= ReferenceEquals(g.material, copy);
                }
                if (ReferenceEquals(copy, shared) || !copy.name.EndsWith("(Feedback)"))
                    fails.Add("no copy — " + copy.name);
                if (!once)
                    fails.Add("the copy was put in more than once");
                if (copy.mainTextureOffset != new Vector2(0.5f, 0f) || copy.mainTextureScale != new Vector2(2f, 1f))
                    fails.Add("the copy's ST " + copy.mainTextureScale + " " + copy.mainTextureOffset);
                if (shared.mainTextureOffset != sharedOffset)
                    fails.Add("the default UI material moved");
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                p.ReleaseRuntime();
                if (!ReferenceEquals(g.material, g.defaultMaterial) || copy != null)
                    fails.Add("the end — " + (g.material != null ? g.material.name : "null") + " · copy alive " + (copy != null));
                // the field itself: a destroyed copy left in it reads back as the default material
                object field = typeof(Graphic).GetField("m_Material", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(g);
                if (!ReferenceEquals(field, null))
                    fails.Add("the end — the field still holds " + (field is Object o && o != null ? o.name : "a destroyed material"));
            }
            {
                // under a Mask: the material drawn is the stencil copy of ours
                FeedbackScheduler s = Sched();
                Image mask = UI<Image>(c.transform, "mask");
                mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                Image g = UI<Image>(mask.transform, "masked");
                g.sprite = MakeSprite(Color.red);
                FeedbackPlayer p = Player(s, On(new GraphicTextureOffsetItem(), g, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 0.5f));
                p.Play();
                Tick(s, 2);
                Canvas.ForceUpdateCanvases();
                Tick(s, 10);
                Material drawn = g.canvasRenderer.materialCount > 0 ? g.canvasRenderer.GetMaterial(0) : null;
                if (drawn == null || ReferenceEquals(drawn, g.material) || drawn.mainTextureOffset != new Vector2(0.5f, 0f))
                    fails.Add("the drawn copy under the mask — " + (drawn != null ? drawn.name + " " + drawn.mainTextureOffset : "null"));
            }
            {
                // a swap and the copy on one Graphic
                FeedbackScheduler s = Sched();
                Image g = UI<Image>(c.transform, "swapped");
                var swapIn = new Material(shared) { name = "swap-in" };
                m_Made.Add(swapIn);
                FeedbackPlayer p = Player(s, On(new GraphicMaterialItem { m_Material = swapIn }, g, SetTo(1f), 0f), On(new GraphicTextureOffsetItem(), g, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 0.5f), Keeper());
                p.Play();
                Tick(s, 12);
                string during = g.material.name;
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                p.ReleaseRuntime();
                // the swap replaces the copy (FB-P03) · both come back
                if (during != "swap-in" || !ReferenceEquals(g.material, g.defaultMaterial))
                    fails.Add("swap + copy — during " + during + " · after " + (g.material != null ? g.material.name : "null"));
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- the render — RT pixels of a camera the test makes (a Screen Space - Camera canvas, a red | green sprite): the
        // color · the cross fade · the texture offset (the UI shader takes _MainTex_ST) · under a Mask (the material drawn takes it)

        [Test]
        public void UG06_TheRender()
        {
            var fails = new List<string>();
            var camGo = new GameObject("UG06 camera");
            m_Made.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            var rt = new RenderTexture(64, 64, 24);
            m_Made.Add(rt);
            cam.targetTexture = rt;
            var canvasGo = new GameObject("UG06 canvas", typeof(RectTransform));
            m_Made.Add(canvasGo);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 5f;
            var read = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            m_Made.Add(read);
            var two = new Texture2D(2, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            two.SetPixels(new[] { Color.red, Color.green });
            two.Apply();
            m_Made.Add(two);
            Sprite sprite = Sprite.Create(two, new Rect(0, 0, 2, 1), new Vector2(0.5f, 0.5f), 1);
            m_Made.Add(sprite);
            Color Left()
            {
                Canvas.ForceUpdateCanvases();
                cam.Render();
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                return read.GetPixel(16, 32);
            }
            Image Stretch(Transform parent, string name)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var r = (RectTransform)go.transform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.sizeDelta = Vector2.zero;
                var image = go.AddComponent<Image>();
                image.sprite = sprite;
                return image;
            }
            bool Red(Color c) => c.r > 0.9f && c.g < 0.1f;
            bool Green(Color c) => c.g > 0.9f && c.r < 0.1f;
            bool Black(Color c) => c.r < 0.1f && c.g < 0.1f;
            try
            {
                foreach ((string name, Func<Transform, (Image drawn, TargetItem item)> make, Func<Color, bool> shown) in new (string, Func<Transform, (Image, TargetItem)>, Func<Color, bool>)[]
                {
                    ("color", t => { Image g = Stretch(t, "color"); return (g, On(new GraphicColorItem(), g, TweenTo(new Vector4(0f, 1f, 0f, 1f)), 0.5f)); }, Black),
                    ("cross fade", t => { Image g = Stretch(t, "fade"); return (g, On(new GraphicCrossFadeItem(), g, TweenTo(new Vector4(1f, 1f, 1f, 0f)), 0.5f)); }, Black),
                    ("texture offset", t => { Image g = Stretch(t, "offset"); return (g, On(new GraphicTextureOffsetItem(), g, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 0.5f)); }, Green),
                    ("under a mask", t =>
                    {
                        Image mask = Stretch(t, "mask");
                        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                        Image g = Stretch(mask.transform, "masked");
                        return (g, On(new GraphicTextureOffsetItem(), g, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 0.5f));
                    }, Green),
                })
                {
                    var holder = new GameObject(name, typeof(RectTransform));
                    holder.transform.SetParent(canvas.transform, false);
                    var hr = (RectTransform)holder.transform;
                    hr.anchorMin = Vector2.zero;
                    hr.anchorMax = Vector2.one;
                    hr.sizeDelta = Vector2.zero;
                    (Image drawn, TargetItem item) = make(holder.transform);
                    Color before = Left();
                    FeedbackScheduler s = Sched();
                    FeedbackPlayer p = Player(s, item, Keeper());
                    p.Play();
                    Tick(s, 2);
                    Canvas.ForceUpdateCanvases();
                    Tick(s, 10);
                    Color during = Left();
                    p.Stop(StopMode.Revert);
                    s.Tick(0f);
                    p.ReleaseRuntime();
                    Color after = Left();
                    if (!Red(before) || !shown(during) || !Red(after))
                        fails.Add(name + " — before " + before + " · during " + during + " · after " + after + " (" + drawn.name + ")");
                    Object.DestroyImmediate(holder);
                }
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release();
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- the Image — the fill clamps (read back) · the sprite swaps and comes back · the sheet's frames

        [Test]
        public void UG07_Image()
        {
            var fails = new List<string>();
            Canvas c = MakeCanvas();
            Sprite a = MakeSprite(Color.red), b = MakeSprite(Color.green);
            {
                FeedbackScheduler s = Sched();
                Image g = UI<Image>(c.transform);
                g.type = Image.Type.Filled;
                g.fillAmount = 0.9f;
                FeedbackPlayer p = Player(s, On(new ImageFillItem(), g, Punch(0.5f), 1f));
                p.Play();
                bool clamped = false;
                for (int i = 0; i < 30; i++)
                {
                    s.Tick(Step);
                    clamped |= g.fillAmount == 1f;
                }
                s.Tick(0f);
                if (!clamped || B(g.fillAmount) != B(0.9f))
                    fails.Add("fill — clamped " + clamped + " end " + g.fillAmount);
            }
            {
                FeedbackScheduler s = Sched();
                Image g = UI<Image>(c.transform);
                g.sprite = a;
                FeedbackPlayer p = Player(s, On(new ImageSpriteItem { m_Sprite = b }, g, SetTo(1f), 0f), Keeper());
                p.Play();
                Tick(s, 2);
                bool swapped = g.sprite == b;
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                if (!swapped || g.sprite != a)
                    fails.Add("sprite — swapped " + swapped + " end " + (g.sprite == a));
            }
            {
                FeedbackScheduler s = Sched();
                Image g = UI<Image>(c.transform);
                g.sprite = a;
                var sheet = new ImageSpriteSheetItem();
                sheet.m_Frames.Add(b);
                sheet.m_Frames.Add(a);
                FeedbackPlayer p = Player(s, On(sheet, g, TweenTo(Vector4.one), 0.5f), Keeper());
                p.Play();
                Tick(s, 2);
                bool first = g.sprite == b;
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                if (!first || g.sprite != a)
                    fails.Add("sheet — first frame " + first + " end " + (g.sprite == a));
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- the toggles — the value and back (the selection a Selectable turned off loses is the play-mode tests' — the
        // EventSystem is play mode's)

        [Test]
        public void UG08_Toggles()
        {
            var fails = new List<string>();
            Canvas c = MakeCanvas();
            Image image = UI<Image>(c.transform);
            CanvasGroup group = image.gameObject.AddComponent<CanvasGroup>();
            Button button = image.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            foreach ((string name, TargetItem item, Object target, Func<bool> read) in new (string, TargetItem, Object, Func<bool>)[]
            {
                ("raycast target", new GraphicRaycastTargetItem(), image, () => image.raycastTarget),
                ("group interactable", new CanvasGroupInteractableItem(), group, () => group.interactable),
                ("group blocks raycasts", new CanvasGroupBlocksRaycastsItem(), group, () => group.blocksRaycasts),
                ("selectable interactable", new SelectableInteractableItem(), button, () => button.interactable),
            })
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, On(item, target, SetTo(0f), 0f), Keeper());
                p.Play();
                Tick(s, 2);
                bool off = !read();
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                if (!off || !read())
                    fails.Add(name + " — off " + off + " back " + read());
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- the Text — the content and back (no string made) · the font size rounded

        [Test]
        public void UG09_Text()
        {
            var fails = new List<string>();
            Canvas c = MakeCanvas();
            Text t = UI<Text>(c.transform);
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = "Hello";
            t.fontSize = 14;
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, On(new TextContentItem { m_Text = "World" }, t, SetTo(1f), 0f), Keeper());
                p.Play();
                Tick(s, 2);
                bool set = t.text == "World";
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                if (!set || t.text != "Hello")
                    fails.Add("content — set " + set + " end " + t.text);
            }
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, On(new TextFontSizeItem(), t, TweenTo(new Vector4(40.6f, 0f, 0f, 0f)), 0.5f), Keeper());
                p.Play();
                Tick(s, 12);
                int at = t.fontSize;
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                if (at != 41 || t.fontSize != 14)
                    fails.Add("font size — at the end " + at + " back " + t.fontSize);
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- the stage in edit mode (the play-mode tests run the real loop): an offset contribution shows after LateApply, not
        // after the Update stage

        [Test]
        public void UG03c_TheStage()
        {
            var fails = new List<string>();
            Canvas c = MakeCanvas();
            foreach ((string name, Func<TargetItem> make, Func<string> read) in Stages(c))
            {
                FeedbackScheduler s = Sched();
                TargetItem item = make();
                Player(s, item).Play();
                Tick(s, 3);
                s.RunRevert();
                string reverted = read();
                s.RunUpdate(new FrameTime(Step, Step));
                string updated = read();
                s.RunLateApply();
                string applied = read();
                if (updated != reverted || applied == reverted)
                    fails.Add(name + " — revert " + reverted + " · update " + updated + " · late apply " + applied);
            }
            Assert.That(fails, Is.Empty, "the values write in LateApply");
        }

        IEnumerable<(string, Func<TargetItem>, Func<string>)> Stages(Canvas c)
        {
            Image image = UI<Image>(c.transform, "stage image");
            image.type = Image.Type.Filled;
            image.fillAmount = 0.5f;
            CanvasGroup group = image.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0.5f;
            Text text = UI<Text>(c.transform, "stage text");
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 20;
            // an offset motion each (a Tween — Persist — writes whatever the stage: Cross Fade has only the Tween)
            yield return ("color", () => On(new GraphicColorItem(), image, Punch(0.3f), 1f), () => C(image.color));
            yield return ("alpha", () => On(new GraphicAlphaItem(), image, Punch(0.3f), 1f), () => C(image.color));
            yield return ("fill", () => On(new ImageFillItem(), image, Punch(0.3f), 1f), () => B(image.fillAmount));
            yield return ("group alpha", () => On(new CanvasGroupAlphaItem(), group, FlashTo(Vector4.zero), 1f), () => B(group.alpha));
            yield return ("font size", () => On(new TextFontSizeItem(), text, Punch(10f), 1f), () => text.fontSize.ToString());
        }

        // ---- every serialized path a slot's write changes is in its rows (a NotRecorded slot keeps its kind)

        static Dictionary<string, string> Serialized(Object o)
        {
            var values = new Dictionary<string, string>();
            SerializedProperty it = new SerializedObject(o).GetIterator();
            while (it.Next(true))
            {
                switch (it.propertyType)
                {
                    case SerializedPropertyType.Float: values[it.propertyPath] = it.floatValue.ToString("R"); break;
                    case SerializedPropertyType.Integer: values[it.propertyPath] = it.intValue.ToString(); break;
                    case SerializedPropertyType.Boolean: values[it.propertyPath] = it.boolValue.ToString(); break;
                    case SerializedPropertyType.String: values[it.propertyPath] = it.stringValue; break;
                    case SerializedPropertyType.ObjectReference: values[it.propertyPath] = it.objectReferenceValue != null ? it.objectReferenceValue.GetHashCode().ToString() : "null"; break;
                }
            }
            return values;
        }

        [Test]
        public void UG14_TheRowsCoverTheWrites()
        {
            var fails = new List<string>();
            Canvas c = MakeCanvas();
            FeedbackScheduler s = Sched();
            PropertyLayerRegistry layers = s.Context.Layers;
            Sprite other = MakeSprite(Color.green);
            var mat = new Material(Canvas.GetDefaultCanvasMaterial()) { name = "rows" };
            m_Made.Add(mat);
            foreach ((Type slot, Func<Component> host, Action<Component> write) in new (Type, Func<Component>, Action<Component>)[]
            {
                (typeof(GraphicColorSlot), () => UI<Image>(c.transform), g => layers.GetOrCreate(g, 0, 0, () => new GraphicColorSlot((Graphic)g)).WriteValue(new Color(2f, 0.1f, -0.5f, 0.5f))),
                (typeof(ImageFillSlot), () => UI<Image>(c.transform), g => layers.GetOrCreate(g, 0, 0, () => new ImageFillSlot((Image)g)).WriteValue(0.25f)),
                (typeof(CanvasGroupAlphaSlot), () => UI<CanvasGroup>(c.transform), g => layers.GetOrCreate(g, 0, 0, () => new CanvasGroupAlphaSlot((CanvasGroup)g)).WriteValue(0.25f)),
                (typeof(GraphicRaycastTargetSlot), () => UI<Image>(c.transform), g => layers.GetOrCreate(g, 0, 0, () => new GraphicRaycastTargetSlot((Graphic)g)).WriteValue(false)),
                (typeof(CanvasGroupInteractableSlot), () => UI<CanvasGroup>(c.transform), g => layers.GetOrCreate(g, 0, 0, () => new CanvasGroupInteractableSlot((CanvasGroup)g)).WriteValue(false)),
                (typeof(CanvasGroupBlocksRaycastsSlot), () => UI<CanvasGroup>(c.transform), g => layers.GetOrCreate(g, 0, 0, () => new CanvasGroupBlocksRaycastsSlot((CanvasGroup)g)).WriteValue(false)),
                (typeof(SelectableInteractableSlot), () => UI<Button>(c.transform), g => layers.GetOrCreate(g, 0, 0, () => new SelectableInteractableSlot((Selectable)g)).WriteValue(false)),
                (typeof(ImageSpriteSlot), () => UI<Image>(c.transform), g => layers.GetOrCreate(g, 0, 0, () => new ImageSpriteSlot((Image)g)).WriteState(other)),
                (typeof(GraphicMaterialSlot), () => UI<Image>(c.transform), g => layers.GetOrCreate(g, 0, 2, () => new GraphicMaterialSlot((Graphic)g)).WriteState(mat)),
            })
            {
                Component g = host();
                Dictionary<string, string> before = Serialized(g);
                write(g);
                Dictionary<string, string> after = Serialized(g);
                string[] rows = slot.GetCustomAttribute<RecordPathsAttribute>().Paths;
                List<string> changed = after.Where(kv => !before.TryGetValue(kv.Key, out string v) || v != kv.Value).Select(kv => kv.Key)
                    .Where(k => !k.EndsWith("m_FileID") && !k.EndsWith("m_PathID")).ToList();
                if (changed.Count == 0)
                    fails.Add(slot.Name + " changed nothing — the positive");
                foreach (string path in changed.Where(x => !rows.Any(r => r == x || r.StartsWith(x + ".", StringComparison.Ordinal) || x.StartsWith(r + ".", StringComparison.Ordinal))))
                    fails.Add(slot.Name + " wrote " + path + " outside its rows");
            }
            foreach (Type t in new[] { typeof(TextContentSlot), typeof(TextFontSizeSlot), typeof(CanvasRendererColorSlot) })
            {
                if (t.GetCustomAttribute<PreviewKeepsAttribute>() == null || t.GetCustomAttribute<NotRecordedAttribute>() == null)
                    fails.Add(t.Name + " keeps no kind");
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- the preview — every UI item moves and comes back bit for bit · the scene stays clean · no hidden copy left

        [Test]
        public void UG13_ThePreview()
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
                var imageGo = new GameObject("image", typeof(RectTransform));
                imageGo.transform.SetParent(canvasGo.transform, false);
                var image = imageGo.AddComponent<Image>();
                image.type = Image.Type.Filled;
                var group = imageGo.AddComponent<CanvasGroup>();
                var textGo = new GameObject("text", typeof(RectTransform));
                textGo.transform.SetParent(canvasGo.transform, false);
                var text = textGo.AddComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.text = "Hello";
                text.fontSize = 14;
                var player = canvasGo.AddComponent<FeedbackPlayer>();
                foreach (TargetItem item in new TargetItem[]
                {
                    On(new GraphicColorItem(), image, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 1f),
                    On(new ImageFillItem(), image, TweenTo(new Vector4(0.25f, 0f, 0f, 0f)), 1f),
                    On(new CanvasGroupAlphaItem(), group, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 1f),
                    On(new GraphicCrossFadeItem(), image, TweenTo(new Vector4(1f, 1f, 1f, 0.25f)), 1f),
                    On(new GraphicTextureOffsetItem(), image, TweenTo(new Vector4(0.5f, 0f, 0f, 0f)), 1f),
                    On(new TextContentItem { m_Text = "World" }, text, SetTo(1f), 1f),
                    On(new TextFontSizeItem(), text, TweenTo(new Vector4(30f, 0f, 0f, 0f)), 1f),
                })
                    player.m_Items.Add(item);
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                string Bits() => C(image.color) + "/" + B(image.fillAmount) + "/" + B(group.alpha) + "/" + C(image.canvasRenderer.GetColor()) + "/" + text.text + "/" + text.fontSize + "/" + (image.material != null ? image.material.name : "null");
                string before = Bits();
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                for (int i = 0; i < 60; i++)
                {
                    now += 1.0 / 120.0;
                    PreviewSession.Current?.Update();
                }
                string during = Bits();
                bool recorded = AnimationMode.IsPropertyAnimated(image, "m_Material");
                int kept = 0;
                for (int i = 0; i < PreviewSaveState.Count; i++)
                {
                    if (PreviewSaveState.At(i) is SerializedPathState || PreviewSaveState.At(i) is CanvasRendererColorState)
                        kept++;
                }
                PreviewSession.Current.Stop();
                string after = Bits();
                int hidden = Resources.FindObjectsOfTypeAll<Material>().Count(m => m != null && m.name.StartsWith(PropertyLayerRegistry.PreviewPrefix, StringComparison.Ordinal));
                string[] parts = before.Split('/'), moved = during.Split('/');
                for (int k = 0; k < parts.Length; k++)
                    Assert.That(moved[k], Is.Not.EqualTo(parts[k]), "the preview moved part " + k + " — each item's positive (the copy alone changes the whole)");
                Assert.That(after, Is.EqualTo(before), "the stop gives every value back");
                Assert.That(recorded, Is.True, "the copy slot recorded the reference (the stop's own revert hides it — a missed end's recovery needs it)");
                Assert.That(kept, Is.EqualTo(3), "the kept kinds — text · font size · the CanvasRenderer's color");
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

        // ---- the kept values — a reload whose hook was missed finds them in SessionState and puts them back

        [Test]
        public void UG15_TheKeptValues()
        {
            Scene active = SceneManager.GetActiveScene();
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var canvasGo = new GameObject("Canvas", typeof(RectTransform));
                canvasGo.AddComponent<Canvas>();
                var textGo = new GameObject("text", typeof(RectTransform));
                textGo.transform.SetParent(canvasGo.transform, false);
                var text = textGo.AddComponent<Text>();
                text.text = "Hello";
                text.fontSize = 14;
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                SessionState.EraseString(PreviewKeptValues.Key);
                var content = SerializedPathState.Take(text, "m_Text");
                var size = SerializedPathState.Take(text, "m_FontData.m_FontSize");
                var colour = new CanvasRendererColorState(text.canvasRenderer);
                text.text = "World";
                text.fontSize = 30;
                text.canvasRenderer.SetColor(Color.red);
                // the recovery's entry (a missed end's) — not the kept values' own call
                PreviewRecovery.Run();
                Assert.That((text.text, text.fontSize, text.canvasRenderer.GetColor()), Is.EqualTo(("Hello", 14, Color.white)), "the recovery");
                Assert.That(SessionState.GetString(PreviewKeptValues.Key, ""), Is.Empty, "all found — none left");
                Assert.That((content.Before, size.Before), Is.EqualTo(("Hello", "14")));
                _ = colour;
            }
            finally
            {
                SessionState.EraseString(PreviewKeptValues.Key);
                if (active.IsValid())
                    SceneManager.SetActiveScene(active);
                if (scene.IsValid())
                    EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.DeleteAsset(ScenePath);
            }
        }

        // ---- FB-P01 · FB-P02 · FB-P03

        [Test]
        public void UG16_TheRules()
        {
            Canvas c = MakeCanvas();
            var host = c.gameObject.AddComponent<FeedbackPlayer>();
            Image tinted = UI<Image>(c.transform, "tinted");
            var b1 = tinted.gameObject.AddComponent<Button>();
            b1.targetGraphic = tinted;
            b1.transition = Selectable.Transition.ColorTint;
            Image plain = UI<Image>(c.transform, "plain");
            var b2 = plain.gameObject.AddComponent<Button>();
            b2.targetGraphic = plain;
            b2.transition = Selectable.Transition.None;
            Text text = UI<Text>(c.transform, "text");
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var swapIn = new Material(Canvas.GetDefaultCanvasMaterial()) { name = "swap-in" };
            m_Made.Add(swapIn);
            host.m_Items.AddRange(new FeedbackItem[]
            {
                On(new GraphicCrossFadeItem(), tinted, TweenTo(Vector4.zero), 1f),          // 0 P01
                On(new GraphicCrossFadeItem(), plain, TweenTo(Vector4.zero), 1f),           // 1
                On(new TextFontSizeItem(), text, Punch(10f), 1f),                           // 2 P02 (the built-in font is dynamic)
                On(new GraphicColorItem(), tinted, TweenTo(Vector4.zero), 1f),              // 3
                On(new GraphicMaterialItem { m_Material = swapIn }, plain, SetTo(1f), 0f),  // 4
                On(new GraphicTextureOffsetItem(), plain, TweenTo(Vector4.zero), 1f),       // 5 P03
                On(new GraphicTextureScaleItem(), tinted, TweenTo(Vector4.one), 1f),        // 6 (no swap on it)
            });
            List<RuleResult> got = FeedbackValidator.Run(host).Where(r => r.Id == "FB-P01" || r.Id == "FB-P02" || r.Id == "FB-P03").ToList();
            Assert.That(text.font.dynamic, Is.True, "the built-in font is dynamic");
            Assert.That(got.Select(r => r.Id + "@" + r.Item).OrderBy(x => x), Is.EqualTo(new[] { "FB-P01@0", "FB-P02@2", "FB-P03@5" }));
        }

        // ---- the add menu's defaults · the name fields resolve · a preset holds Self and a copy moves its own Image · the keep (a
        // play mode record) gives back the saved fields — the string · the enum · the float · the Anchored space (Self targets — an
        // untitled scene's objects have no id)

        [Test]
        public void UG17_DefaultsPresetAndKeep()
        {
            var flash = (FlashMotion)MotionDefaults.Create(typeof(GraphicColorItem), MotionKind.Flash, out _);
            var fade = (TweenMotion)MotionDefaults.Create(typeof(GraphicCrossFadeItem), MotionKind.Tween, out _);
            var size = (PunchMotion)MotionDefaults.Create(typeof(RectSizeDeltaItem), MotionKind.Punch, out _);
            Assert.That((flash.m_Params.m_Target, fade.m_Params.m_To, size.m_Params.m_Amplitude.x), Is.EqualTo((MotionDefaults.SpriteColorTo, MotionDefaults.CrossFadeTo, MotionDefaults.RectSizeSize)));
            Assert.That((TargetChecks.Requires(typeof(GraphicColorItem)), TargetChecks.Requires(typeof(TextContentItem)), TargetChecks.Requires(typeof(CanvasGroupAlphaItem))),
                Is.EqualTo((typeof(Graphic), typeof(Text), typeof(CanvasGroup))));
            const string presetPath = "Assets/UG17Preset.asset";
            Canvas c = MakeCanvas();
            Image image = UI<Image>(c.transform);
            FeedbackPlayer a = image.gameObject.AddComponent<FeedbackPlayer>();
            a.m_Items.Add(On(new GraphicColorItem(), image, TweenTo(new Vector4(1f, 0f, 0f, 1f)), 0.5f));
            try
            {
                FeedbackPreset preset = PresetOps.SaveAs(a, presetPath, out int cleared);
                Assert.That(((TargetItem)preset.m_Items[0]).m_Target.m_Mode, Is.EqualTo(TargetRefMode.Self));
                Image other = UI<Image>(c.transform, "other");
                FeedbackPlayer b = other.gameObject.AddComponent<FeedbackPlayer>();
                PresetOps.CopyInto(b, preset);
                FeedbackScheduler s = Sched();
                b.Bind(s);
                b.EnsureRuntime();
                m_Players.Add(b);
                b.Play();
                Tick(s, 5);
                Assert.That((other.color != Color.white, image.color == Color.white), Is.EqualTo((true, true)));

                var keepGo = new GameObject("keep", typeof(RectTransform));
                keepGo.transform.SetParent(c.transform, false);
                FeedbackPlayer k = keepGo.AddComponent<FeedbackPlayer>();
                TargetRef Self() => new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };
                k.m_Items.AddRange(new FeedbackItem[]
                {
                    new TextContentItem { m_Text = "World", m_Target = Self(), m_Motion = SetTo(1f) },
                    new VideoPlayerControlItem { m_Action = VideoAction.Seek, m_Time = 1.5f, m_Target = Self(), m_Motion = SetTo(1f) },
                    new PositionItem { m_Space = PositionSpace.Anchored, m_Target = Self(), m_Motion = TweenTo(new Vector4(100f, 50f, 0f, 0f)) },
                    new RectOffsetsItem { m_Target = Self(), m_Motion = TweenTo(new Vector4(-10f, -5f, 10f, 5f)) },
                });
                var into = new GameObject("UG17Into");
                m_Made.Add(into);
                FeedbackPlayer kept = into.AddComponent<FeedbackPlayer>();
                PlayModeRecord.Apply(kept, PlayModeRecord.Capture(k, false, out int unknown), out PlayModeRecord.Stats stats);
                Assert.That((unknown, stats.Dropped), Is.EqualTo((0, 0)));
                Assert.That(kept.m_Items.Select(i => i.GetType()), Is.EqualTo(k.m_Items.Select(i => i.GetType())), "the types");
                var video = (VideoPlayerControlItem)kept.m_Items[1];
                Assert.That((((TextContentItem)kept.m_Items[0]).m_Text, video.m_Action, video.m_Time, ((PositionItem)kept.m_Items[2]).m_Space,
                    ((TweenMotion)((TargetItem)kept.m_Items[3]).m_Motion).m_Params.m_To),
                    Is.EqualTo(("World", VideoAction.Seek, 1.5f, PositionSpace.Anchored, new Vector4(-10f, -5f, 10f, 5f))), "the saved fields");
            }
            finally
            {
                AssetDatabase.DeleteAsset(presetPath);
            }
        }
    }
}
