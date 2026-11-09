using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback
{
    // the first extension service — a context's screen overlay canvas, made whole at the first Of (a binding's
    // preparation) and destroyed at Shutdown. Runtime · test: visible (a runtime play's root is DontDestroyOnLoad);
    // preview: hidden with the preview prefix. The UI layer (the Scene view draws it)
    internal sealed class ScreenOverlayService : FeedbackService
    {
        internal static readonly ServiceKey Key = new ServiceKey();
        internal const int UILayer = 5;
        internal const int HoleSize = 128;
        internal const float HoleRadius = 0.375f;
        internal const string WarmString = "-0123456789.,%";

        internal static ScreenOverlayService Of(FeedbackContext context) => context.GetExtension(Key) as ScreenOverlayService;

        readonly GameObject m_Root;
        readonly Canvas m_Canvas;
        readonly RectTransform m_Texts;
        readonly RectTransform m_FadeRect;
        readonly RectTransform m_FlashRect;
        readonly Image m_Cover;
        readonly RawImage m_Iris;
        readonly Texture2D m_Hole;
        readonly List<Image> m_Rented = new List<Image>(4);
        readonly List<Image> m_Resting = new List<Image>(4);
        readonly Dictionary<FloatingTextStyle, GameObject> m_Templates = new Dictionary<FloatingTextStyle, GameObject>();
        GameObject m_DefaultTemplate;
        readonly List<GameObject> m_Made = new List<GameObject>(4);
        readonly List<bool> m_Was = new List<bool>(4);
        Camera m_UICamera;

        ScreenFadeShape m_Shape;
        ScreenSide m_From;
        Color m_FadeColor = Color.black;
        Vector2 m_Center;
        bool m_HasCenter;
        float m_Fade;

        struct Live
        {
            internal FloatingTextInstance Instance;
            internal Vector3 World;
            internal Transform Follow;
            internal Vector3 Offset;
            internal FloatingTextLook Look;
            internal float Life;
            internal float Age;
            internal float Angle;
            internal Vector2 Scatter;
            internal float ScaleStart;
            internal float AlphaStart;
            internal bool Unscaled;
        }

        Live[] m_Live = new Live[8];
        int m_LiveCount;
        int m_Reserved;

        // observation
        internal GameObject Root => m_Root;
        internal Canvas Canvas => m_Canvas;
        internal RectTransform Texts => m_Texts;
        internal Image Cover => m_Cover;
        internal RawImage Iris => m_Iris;
        internal Texture2D Hole => m_Hole;
        internal int RentedCount => m_Rented.Count;
        internal int RestingCount => m_Resting.Count;
        internal Image RentedAt(int i) => m_Rented[i];
        internal float Fade => m_Fade;
        internal ScreenFadeShape Shape => m_Shape;
        internal Color FadeColor => m_FadeColor;
        internal int LiveCount => m_LiveCount;
        internal int LiveCapacity => m_Live.Length;
        internal FloatingTextInstance LiveAt(int i) => m_Live[i].Instance;
        internal float LateApplyAlpha;     // the first rented image's alpha as LateApply saw it (the order's net)
        internal int LateApplies;

        bool IsPreview => Context.Kind == ContextKind.Preview;

        string Prefix => IsPreview ? PropertyLayerRegistry.PreviewPrefix : PropertyLayerRegistry.RuntimePrefix;

        internal ScreenOverlayService(FeedbackContext context)
            : base(context)
        {
            m_Root = Node("ScreenOverlay", null);
            if (context.Kind == ContextKind.Runtime && Application.isPlaying)
                Object.DontDestroyOnLoad(m_Root);
            m_Canvas = m_Root.AddComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.sortingOrder = context.Settings.OverlaySortingOrder;
            var scaler = m_Root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            m_Texts = (RectTransform)Node("Texts", m_Root.transform).transform;
            m_Texts.gameObject.AddComponent<Canvas>();
            m_FadeRect = (RectTransform)Node("Fade", m_Root.transform).transform;
            m_Cover = Node("Cover", m_FadeRect).AddComponent<Image>();
            m_Cover.raycastTarget = false;
            m_Cover.enabled = false;
            m_Hole = MakeHole();
            m_Iris = Node("Iris", m_FadeRect).AddComponent<RawImage>();
            m_Iris.texture = m_Hole;
            m_Iris.raycastTarget = false;
            m_Iris.enabled = false;
            m_FlashRect = (RectTransform)Node("Flash", m_Root.transform).transform;
            // an anchor per canvas: uGUI's registry drops a canvas's set when its last graphic goes off — the next on makes a new
            // set (an allocation each time the overlay wakes from rest). A graphic that never draws keeps it (GraphicRegistry)
            Anchor(m_Root.transform);
            Anchor(m_Texts);
            // the scaler sets the factor just before a render — a text placed this frame would use 1
            Canvas.ForceUpdateCanvases();
        }

        void Anchor(Transform parent)
        {
            GameObject go = Node("Anchor", parent);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0.5f, 0.5f);
            r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = Vector2.zero;
            var image = go.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = false;
        }

        GameObject Node(string name, Transform parent)
        {
            var go = new GameObject(Prefix + name, typeof(RectTransform));
            go.layer = UILayer;
            if (IsPreview)
                go.hideFlags = HideFlags.HideAndDontSave;
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
                var r = (RectTransform)go.transform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = Vector2.zero;
                r.offsetMax = Vector2.zero;
            }
            return go;
        }

        // opaque out of a disc of radius 0.375 — under clamp the uvRect's outside is the cover
        Texture2D MakeHole()
        {
            var tex = new Texture2D(HoleSize, HoleSize, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave, name = Prefix + "ScreenOverlay.Hole" };
            var px = new Color32[HoleSize * HoleSize];
            const float edge = 0.01f;
            for (int y = 0; y < HoleSize; y++)
                for (int x = 0; x < HoleSize; x++)
                {
                    float dx = (x + 0.5f) / HoleSize - 0.5f, dy = (y + 0.5f) / HoleSize - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * HoleSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01((d - (HoleRadius - edge)) / (2f * edge))));
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        // a test's pixels: the canvas through a camera of its own (batch draws no screen)
        internal void RenderForTest(Camera camera)
        {
            m_UICamera = camera;
            m_Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            m_Canvas.worldCamera = camera;
            m_Canvas.planeDistance = 5f;
        }

        // ---- flash: an image of its own per item — rented at the preparation, back when the slot goes

        internal Image Rent(Color color)
        {
            Image image;
            if (m_Resting.Count > 0)
            {
                image = m_Resting[m_Resting.Count - 1];
                m_Resting.RemoveAt(m_Resting.Count - 1);
            }
            else
            {
                image = Node("Flash.Image", m_FlashRect).AddComponent<Image>();
                image.raycastTarget = false;
                m_Made.Add(image.gameObject);
            }
            image.color = new Color(color.r, color.g, color.b, 1f);
            image.canvasRenderer.SetAlpha(0f);
            image.enabled = false;
            image.transform.SetAsLastSibling();
            m_Rented.Add(image);
            // the warm: every image on at once, then back — the canvas lists grow to the count
            int n = m_Rented.Count;
            bool cover = m_Cover.enabled;
            m_Was.Clear();
            for (int i = 0; i < n; i++)
            {
                m_Was.Add(m_Rented[i].enabled);
                m_Rented[i].enabled = true;
            }
            m_Cover.enabled = true;
            for (int i = 0; i < n; i++)
                m_Rented[i].enabled = m_Was[i];
            m_Cover.enabled = cover;
            return image;
        }

        internal void Return(Image image)
        {
            if (image == null || !m_Rented.Remove(image))
                return;
            image.enabled = false;
            m_Resting.Add(image);
        }

        // ---- fade: one cover; its look is the last begun's

        internal void SetLook(ScreenFadeItem item, bool hasPlace, Vector3 world)
        {
            m_Shape = item.m_Shape;
            m_From = item.m_From;
            m_FadeColor = item.m_Color;
            m_HasCenter = false;
            if (item.m_Center == ScreenFadeCenter.PlayPosition && hasPlace)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 sp = cam.WorldToScreenPoint(world);
                    if (sp.z > 0f && RectTransformUtility.ScreenPointToLocalPointInRectangle(m_FadeRect, sp, m_UICamera, out Vector2 local))
                    {
                        m_Center = local - m_FadeRect.rect.min;
                        m_HasCenter = true;
                    }
                }
            }
            ApplyFade();
        }

        internal void WriteFade(float value)
        {
            m_Fade = value;
            ApplyFade();
        }

        void ApplyFade()
        {
            float v = Mathf.Clamp01(m_Fade);
            if (v <= 0f)
            {
                if (m_Cover.enabled)
                    m_Cover.enabled = false;
                if (m_Iris.enabled)
                    m_Iris.enabled = false;
                return;
            }
            var opaque = new Color(m_FadeColor.r, m_FadeColor.g, m_FadeColor.b, 1f);
            RectTransform cr = m_Cover.rectTransform;
            if (m_Shape == ScreenFadeShape.Iris && v < 1f)
            {
                if (m_Cover.enabled)
                    m_Cover.enabled = false;
                m_Iris.color = opaque;
                Rect r = m_Iris.rectTransform.rect;
                float diag = Mathf.Sqrt(r.width * r.width + r.height * r.height);
                float radius = (1f - v) * diag;
                Vector2 c = m_HasCenter ? m_Center : new Vector2(r.width * 0.5f, r.height * 0.5f);
                float k = HoleRadius / Mathf.Max(radius, 1e-3f);
                m_Iris.uvRect = new Rect(0.5f - c.x * k, 0.5f - c.y * k, r.width * k, r.height * k);
                if (!m_Iris.enabled)
                    m_Iris.enabled = true;
                return;
            }
            if (m_Iris.enabled)
                m_Iris.enabled = false;
            Vector2 min = Vector2.zero, max = Vector2.one;
            if (m_Shape == ScreenFadeShape.Wipe)
            {
                switch (m_From)
                {
                    case ScreenSide.Left: max.x = v; break;
                    case ScreenSide.Right: min.x = 1f - v; break;
                    case ScreenSide.Bottom: max.y = v; break;
                    case ScreenSide.Top: min.y = 1f - v; break;
                }
            }
            if (cr.anchorMin != min)
                cr.anchorMin = min;
            if (cr.anchorMax != max)
                cr.anchorMax = max;
            cr.offsetMin = Vector2.zero;
            cr.offsetMax = Vector2.zero;
            m_Cover.color = opaque;
            m_Cover.canvasRenderer.SetAlpha(m_Shape == ScreenFadeShape.Plain ? v : 1f);
            if (!m_Cover.enabled)
                m_Cover.enabled = true;
        }

        // ---- floating text: a template per style and one for the defaults (made under Texts, then off), Pools' key

        internal GameObject TemplateOf(FloatingTextStyle style)
        {
            if (style == null)
            {
                if (m_DefaultTemplate == null)
                    m_DefaultTemplate = MakeTemplate(null, FloatingTextLook.DefaultFontSize);
                return m_DefaultTemplate;
            }
            if (m_Templates.TryGetValue(style, out GameObject t) && t != null)
                return t;
            t = style.m_Prefab != null ? style.m_Prefab : MakeTemplate(style.m_Font, style.m_FontSize);
            m_Templates[style] = t;
            return t;
        }

        GameObject MakeTemplate(TMP_FontAsset font, float fontSize)
        {
            GameObject go = Node("FloatingText.Template", m_Texts);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0.5f, 0.5f);
            r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(400f, 100f);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
                text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            go.AddComponent<CanvasGroup>();
            go.AddComponent<FloatingTextInstance>();
            go.SetActive(false);
            return go;
        }

        // Pools' warm of each instance made: woken once under Texts (a text made under the inactive container has no
        // Awake), warmed with the longest digits, put back to sleep
        internal static readonly System.Action<GameObject, FeedbackContext> Warm = WarmText;

        static void WarmText(GameObject instance, FeedbackContext context)
        {
            ScreenOverlayService s = Of(context);
            if (s == null || s.IsClosed)
                return;
            instance.hideFlags = context.Kind == ContextKind.Preview ? HideFlags.HideAndDontSave : HideFlags.None;
            instance.layer = UILayer;
            FloatingTextInstance fi = instance.GetComponent<FloatingTextInstance>();
            if (fi == null)
                fi = instance.AddComponent<FloatingTextInstance>();
            if (instance.GetComponent<CanvasGroup>() == null)
                instance.AddComponent<CanvasGroup>();
            Transform back = instance.transform.parent;
            instance.transform.SetParent(s.m_Texts, false);
            instance.SetActive(true);
            fi.Bind();
            fi.Text.raycastTarget = false;
            fi.Text.text = WarmString;
            fi.Text.GetPreferredValues(0f, 0f);
            char[] chars = s_WarmChars;
            fi.Text.SetText(chars, 0, chars.Length);
            fi.Text.GetPreferredValues(0f, 0f);
            fi.Group.alpha = 0f;
            instance.SetActive(false);
            instance.transform.SetParent(back, false);
        }

        static readonly char[] s_WarmChars = WarmString.ToCharArray();

        static float U(uint seed, uint i) => Hash32.ToUnit(Hash32.Of(seed, i));

        // the live list holds every text the preparations reserved — a spawn does not grow it (Pools' form)
        internal void ReserveLive(int count)
        {
            m_Reserved += count;
            if (m_Live.Length < m_Reserved)
                System.Array.Resize(ref m_Live, m_Reserved);
        }

        internal static float LifeOf(in FloatingTextLook look, uint seed) => Mathf.Max(0.01f, Mathf.Lerp(look.Lifetime.x, look.Lifetime.y, U(seed, 0)));

        // the scale's start — its own range only (not the opacity's)
        internal static float ScaleStartOf(in FloatingTextLook look, uint seed) => Mathf.Lerp(look.ScaleStart.x, look.ScaleStart.y, U(seed, 4));

        internal static float AlphaStartOf(in FloatingTextLook look, uint seed) => Mathf.Lerp(look.AlphaStart.x, look.AlphaStart.y, U(seed, 5));

        internal void Launch(FloatingTextInstance fi, Vector3 world, Transform follow, Vector3 offset, in FloatingTextLook st, uint seed, float life, bool unscaled)
        {
            fi.RefreshRenderers();
            if (m_LiveCount == m_Live.Length)
                System.Array.Resize(ref m_Live, m_Live.Length * 2);
            m_Live[m_LiveCount] = new Live
            {
                Instance = fi,
                World = world,
                Follow = follow,
                Offset = offset,
                Look = st,
                Life = life,
                Age = 0f,
                Angle = (U(seed, 1) - 0.5f) * st.Spread * Mathf.Deg2Rad,
                Scatter = new Vector2(U(seed, 2) - 0.5f, U(seed, 3) - 0.5f) * (2f * st.Scatter),
                ScaleStart = ScaleStartOf(st, seed),
                AlphaStart = AlphaStartOf(st, seed),
                Unscaled = unscaled,
            };
            Place(m_LiveCount);
            m_LiveCount++;
        }

        // RunUpdate's services step: a text Pools took back (under its container) leaves · the ages on the spawn's clock
        internal override void Update(in FrameTime time)
        {
            for (int i = m_LiveCount - 1; i >= 0; i--)
            {
                FloatingTextInstance fi = m_Live[i].Instance;
                if (fi == null || fi.transform.parent != m_Texts || !fi.gameObject.activeInHierarchy)
                {
                    m_Live[i] = m_Live[--m_LiveCount];
                    m_Live[m_LiveCount] = default;
                    continue;
                }
                m_Live[i].Age += m_Live[i].Unscaled ? time.UnscaledDelta : time.ScaledDelta;
            }
        }

        // after the layers' LateApply: the cameras have moved
        internal override void LateApply()
        {
            LateApplies++;
            if (m_Rented.Count > 0)
                LateApplyAlpha = m_Rented[0].enabled ? m_Rented[0].canvasRenderer.GetAlpha() : 0f;
            for (int i = 0; i < m_LiveCount; i++)
                Place(i);
        }

        void Place(int i)
        {
            ref Live live = ref m_Live[i];
            FloatingTextInstance fi = live.Instance;
            if (fi == null)
                return;
            ref FloatingTextLook st = ref live.Look;
            float t = Mathf.Clamp01(live.Age / live.Life);
            Vector3 world = live.Follow != null ? live.Follow.TransformPoint(live.Offset) : live.World;
            Camera cam = Camera.main;
            if (cam == null)
            {
                fi.Group.alpha = 0f;
                return;
            }
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Texts, sp, m_UICamera, out Vector2 local))
            {
                fi.Group.alpha = 0f;
                return;
            }
            float mx = st.MoveX.Evaluate(t) * st.Distance.x, my = st.MoveY.Evaluate(t) * st.Distance.y;
            float cos = Mathf.Cos(live.Angle), sin = Mathf.Sin(live.Angle);
            fi.Rect.anchoredPosition = local + live.Scatter + new Vector2(mx * cos - my * sin, mx * sin + my * cos);
            float scale = live.ScaleStart * st.Scale.Evaluate(t);
            fi.Rect.localScale = new Vector3(scale, scale, 1f);
            fi.Group.alpha = live.AlphaStart * st.Alpha.Evaluate(t);
            Color tint = st.Color.Evaluate(t);
            List<CanvasRenderer> renderers = fi.Renderers;
            for (int k = 0; k < renderers.Count; k++)
                renderers[k].SetColor(tint);
        }

        internal override string DebugName => "Screen overlay";

        internal override void Describe(List<ServiceRow> into)
        {
            into.Clear();
            into.Add(new ServiceRow("sorting order", m_Canvas != null ? m_Canvas.sortingOrder : 0));
            into.Add(new ServiceRow("flash images rented", m_Rented.Count));
            into.Add(new ServiceRow("flash images resting", m_Resting.Count));
            into.Add(new ServiceRow("cover", m_Fade));
            into.Add(new ServiceRow("texts live", m_LiveCount));
            into.Add(new ServiceRow("templates", m_Templates.Count + (m_DefaultTemplate != null ? 1 : 0)));
        }

        internal override void Shutdown()
        {
            m_LiveCount = 0;
            m_Rented.Clear();
            m_Resting.Clear();
            m_Templates.Clear();
            m_DefaultTemplate = null;
            m_Made.Clear();
            if (m_Root != null)
                Object.DestroyImmediate(m_Root);
            if (m_Hole != null)
                Object.DestroyImmediate(m_Hole);
        }
    }

    // a flash's image: its alpha written at LateApply (CanvasRenderer — no rebuild); off at 0
    [NotRecorded("the screen overlay's own image")]
    internal sealed class ScreenFlashSlot : ValueSlot<float, FloatOps>
    {
        readonly ScreenOverlayService m_Service;
        readonly Color m_Color;
        Image m_Image;
        float m_Value;

        internal ScreenFlashSlot(ScreenOverlayService service, Color color)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Service = service;
            m_Color = color;
        }

        internal Image Image => m_Image;

        internal override bool IsAlive => !m_Service.IsClosed;

        internal override void Attached() => m_Image = m_Service.Rent(m_Color);

        internal override void Dropped()
        {
            m_Service.Return(m_Image);
            m_Image = null;
        }

        protected override float Read() => m_Value;

        protected override void Write(in float value)
        {
            m_Value = value;
            if (m_Image == null)
                return;
            float a = Mathf.Clamp01(value);
            if (a <= 0f)
            {
                if (m_Image.enabled)
                    m_Image.enabled = false;
                return;
            }
            m_Image.canvasRenderer.SetAlpha(a);
            if (!m_Image.enabled)
                m_Image.enabled = true;
        }
    }

    // the cover: one for every fade of the context (Persist — its end stays)
    [NotRecorded("the screen overlay's own cover")]
    internal sealed class ScreenFadeSlot : ValueSlot<float, FloatOps>
    {
        readonly ScreenOverlayService m_Service;

        internal ScreenFadeSlot(ScreenOverlayService service)
            : base(ConsumptionStage.LateApply, Composition.Additive, false) => m_Service = service;

        internal override bool IsAlive => !m_Service.IsClosed;

        protected override float Read() => m_Service.Fade;

        protected override void Write(in float value) => m_Service.WriteFade(value);
    }

    internal sealed class ScreenFlashBinding : ValueTargetBinding<float, FloatOps>
    {
        internal ScreenFlashBinding(ScreenFlashItem item)
            : base(item)
        {
        }

        protected override bool ReadsTarget => false;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            ScreenOverlayService service = ScreenOverlayService.Of(context.Context);
            if (service == null)
                return Refuse("no screen overlay service");
            Color color = ((ScreenFlashItem)m_Item).m_Color;
            slot = Layers.CreatePrivate(null, 0, 0, () => new ScreenFlashSlot(service, color));
            return true;
        }
    }

    internal sealed class ScreenFadeBinding : ValueTargetBinding<float, FloatOps>
    {
        ScreenOverlayService m_Service;
        Transform m_Owner;

        internal ScreenFadeBinding(ScreenFadeItem item)
            : base(item)
        {
        }

        protected override bool ReadsTarget => false;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            ScreenOverlayService service = ScreenOverlayService.Of(context.Context);
            if (service == null)
                return Refuse("no screen overlay service");
            m_Service = service;
            m_Owner = context.Owner != null ? context.Owner.transform : null;
            slot = Layers.GetOrCreate(null, 0, 0, () => new ScreenFadeSlot(service));
            return true;
        }

        protected override void Started(int record, in BeginContext context)
        {
            if (m_Service == null || m_Service.IsClosed)
                return;
            Vector3 world = context.HasPosition ? context.Position : (m_Owner != null ? m_Owner.position : Vector3.zero);
            m_Service.SetLook((ScreenFadeItem)m_Item, context.HasPosition || m_Owner != null, world);
        }
    }

    // a floating text: Spawn's form over Pools (the owner's return on a Revert · a Restore · a backward play)
    internal sealed class FloatingTextBinding : ItemBinding
    {
        // TMP's Essential Resources (its settings) — without them the warm-up's sizing throws in
        // TMP_Settings.defaultStyleSheet, so the item refuses; so do Count To and the Character items (a parse · a mesh that
        // never comes), and a Progress Bar leaves its label (a seam for the tests)
        internal static System.Func<bool> s_TmpResources = () => Resources.Load<TMP_Settings>("TMP Settings") != null;
        internal const string NoTmpResources = "TextMesh Pro's Essential Resources are not in the project — Window > TextMeshPro > Import TMP Essential Resources";

        readonly FloatingTextItem m_Item;
        FeedbackContext m_Context;
        ScreenOverlayService m_Service;
        Transform m_Target;
        FloatingTextStyle m_Style;
        FloatingTextLook m_Look;
        GameObject m_Template;
        TMPNumberFormat m_Format;
        char[] m_Chars;
        bool m_Unscaled;
        long[] m_Serials;
        long m_Origin = -1;
        uint m_Spawns;

        internal FloatingTextBinding(FloatingTextItem item)
        {
            m_Item = item;
        }

        internal GameObject Template => m_Template;

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            m_Target = TargetResolver.Find<Transform>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (m_Target == null)
                return Refuse(TargetResolver.Describe(failure));
            if (m_Item.m_Style != null && !(m_Item.m_Style is FloatingTextStyle))
                return Refuse("the style is not a Floating Text Style");
            m_Style = m_Item.m_Style as FloatingTextStyle;
            if (m_Item.m_Source == FloatingTextSource.Intensity)
            {
                if (!TMPNumberFormat.TryParse(m_Item.m_Format, out m_Format, out string reason))
                    return Refuse(reason);
                m_Chars = new char[m_Format.Capacity];
            }
            m_Context = context.Context;
            m_Service = ScreenOverlayService.Of(m_Context);
            if (m_Service == null)
                return Refuse("no screen overlay service");
            if (!s_TmpResources())
                return Refuse(NoTmpResources);
            m_Look = FloatingTextLook.Of(m_Style);
            m_Template = m_Service.TemplateOf(m_Style);
            m_Context.Pools.Reserve(m_Template, Mathf.Max(1, m_Item.m_PoolSize), ScreenOverlayService.Warm);
            m_Service.ReserveLive(Mathf.Max(1, m_Item.m_PoolSize));
            ItemClock clock = m_Item.EffectiveClock;
            m_Unscaled = clock == ItemClock.Unscaled || (clock == ItemClock.Player && context.Player != null && context.Player.m_TimeMode == PlayerTimeMode.Unscaled);
            int n = context.MaxOverlap;
            m_Serials = new long[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        internal override bool IsAlive => m_Target != null && m_Service != null && !m_Service.IsClosed;

        internal override void Execute(int record, in BeginContext context)
        {
            PoolService pools = m_Context.PoolsIfMade;
            if (pools == null || m_Service.IsClosed)
                return;
            if (context.Reverse)
            {
                if (m_Origin >= 0)
                    pools.ReturnOwned(this, -1, m_Origin);
                return;
            }
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_Origin = context.Serial;
            }
            if (m_Target == null)
                return;
            Vector3 world = context.HasPosition ? context.Position : m_Target.TransformPoint(m_Item.m_Offset);
            uint seed = Hash32.Tagged(context.ItemSeed, 0x46544558u, m_Spawns++);
            float life = ScreenOverlayService.LifeOf(in m_Look, seed);
            GameObject go = pools.Spawn(m_Template, true, Vector3.zero, Quaternion.identity, m_Service.Texts, life, m_Unscaled, this, record, context.Serial);
            if (go == null)
                return;
            FloatingTextInstance fi = go.GetComponent<FloatingTextInstance>();
            if (m_Item.m_Source == FloatingTextSource.Text)
                fi.Text.text = m_Item.m_Text;
            else
            {
                int n = m_Format.Write(context.Intensity * m_Item.m_Multiplier, m_Chars);
                fi.Text.SetText(m_Chars, 0, n);
            }
            m_Service.Launch(fi, world, m_Item.m_Follow && !context.HasPosition ? m_Target : null, m_Item.m_Offset, in m_Look, seed, life, m_Unscaled);
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
            if (reason == EndReason.Revert && m_Serials[record] >= 0)
                m_Context.PoolsIfMade?.ReturnOwned(this, record, m_Serials[record]);
        }

        internal override void Restore()
        {
            m_Context?.PoolsIfMade?.ReturnOwned(this, -1, -1);
            m_Origin = -1;
        }

        internal override void Inherit(ItemBinding old)
        {
            if (!(old is FloatingTextBinding from) || m_Context == null || from.m_Context != m_Context)
                return;
            m_Context.PoolsIfMade?.Reown(from, this);
            m_Origin = from.m_Origin;
        }

        internal override void Release()
        {
            m_Context?.PoolsIfMade?.Disown(this);
        }
    }
}
