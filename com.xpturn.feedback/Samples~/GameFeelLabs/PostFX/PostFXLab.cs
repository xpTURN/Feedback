using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.GameFeelLabs
{
    // S10 PostFX Lab: one button per PostFX item and three combinations — a hit vignette, a low-health pulse that loops until switched
    // off, a slow-mo with a desaturation on the unscaled clock. Each item weighs its own overlay volume above the scene's volume; the
    // pulse prepares at Awake, so the hit (Start) sits above it. Every player stops with Revert when the scene goes (no overlay left
    // in the hub). A low health switched on in Before waits for After: a play started with its category off has no items
    public sealed class PostFXLab : MonoBehaviour
    {
        public const int Effects = 16;
        static readonly string[] s_Effects =
        {
            "Bloom", "Chromatic Aberration", "Color Adjustments", "Depth of Field", "Film Grain", "Lens Distortion", "Motion Blur (camera swing)", "Panini Projection",
            "Vignette", "White Balance", "Channel Mixer", "Split Toning", "Lift Gamma Gain", "Shadows Midtones Highlights", "Volume Blend (Danger profile)", "Volume Weight",
        };
        static readonly string[] s_Combos = { "1 · Hit", "2 · Slow-mo", "3 · Low Health", "V · Stop all" };
        const string LowOn = "Low Health — on", LowOff = "Low Health — off";
        const string Keys = "Z · X — pick an effect · C — play it";
        const string Order = "Overlay — the later prepared on top";
        static readonly string[] s_Scale = new string[41];
        static readonly Color s_Base = new Color(0.15f, 0.2f, 0.3f, 0.9f), s_Picked = new Color(0.35f, 0.45f, 0.65f, 0.95f);

        [SerializeField] FeedbackPlayer[] m_Effects = new FeedbackPlayer[Effects];
        [SerializeField] FeedbackPlayer m_Hit;
        [SerializeField] FeedbackPlayer m_Slow;
        [SerializeField] FeedbackPlayer m_LowHealth;
        [SerializeField] Transform[] m_Spinners = new Transform[0];
        [SerializeField] Transform m_Camera;
        [SerializeField] float m_Sway = 0.8f;

        [System.NonSerialized] InputAction[] m_Actions;
        [System.NonSerialized] InputAction[] m_Options;
        bool m_Want;
        int m_Selected;
        int m_ShownPick = -1;
        float m_Clock;
        Vector3 m_CameraBase;
        GameObject m_Root;
        Text m_Low, m_ScaleLabel;
        readonly Image[] m_Images = new Image[Effects];

        public bool Wants => m_Want;
        public int Selected => m_Selected;

        static PostFXLab()
        {
            for (int i = 0; i < s_Scale.Length; i++)
                s_Scale[i] = "Time scale ×" + (i * 0.05f).ToString("0.00");
        }

        void OnEnable()
        {
            m_Want = false;
            m_Selected = 0;
            m_Clock = 0f;
            if (m_Camera != null)
                m_CameraBase = m_Camera.position;
            Build();
        }

        void OnDisable()
        {
            if (m_Root != null)
                Destroy(m_Root);
        }

        void Update()
        {
            if (m_Actions == null)
            {
                m_Actions = new[] { SampleInput.Find("Action1"), SampleInput.Find("Action2"), SampleInput.Find("Action3") };
                m_Options = new[] { SampleInput.Find("Option1"), SampleInput.Find("Option2"), SampleInput.Find("Option3"), SampleInput.Find("Option4") };
            }
            if (Pressed(m_Actions[0]))
                Hit();
            if (Pressed(m_Actions[1]))
                SlowMo();
            if (Pressed(m_Actions[2]))
                Toggle();
            if (Pressed(m_Options[0]))
                Pick(-1);
            if (Pressed(m_Options[1]))
                Pick(1);
            if (Pressed(m_Options[2]))
                PlayEffect(m_Selected);
            if (Pressed(m_Options[3]))
                StopAll();
            Keep();
            Move(Time.deltaTime);
            Refresh();
        }

        // only a pointer's press is refused over the UI — a key or a pad button always goes through
        static bool Pressed(InputAction a) => a != null && a.WasPressedThisFrame() && !(FromPointer(a) && SampleInput.PointerOverUI);

        static bool FromPointer(InputAction a) => a.activeControl != null && a.activeControl.device is Pointer;

        public void Hit()
        {
            if (m_Hit != null)
                m_Hit.Play();
        }

        public void SlowMo()
        {
            if (m_Slow != null)
                m_Slow.Play();
        }

        public void Toggle()
        {
            m_Want = !m_Want;
            if (!m_Want && m_LowHealth != null)
                m_LowHealth.Stop(StopMode.Revert);
        }

        public void Pick(int step) => m_Selected = (m_Selected + step + Effects) % Effects;

        public void PlayEffect(int i)
        {
            if (i >= 0 && i < m_Effects.Length && m_Effects[i] != null)
                m_Effects[i].Play();
        }

        public void StopAll()
        {
            m_Want = false;
            for (int i = 0; i < m_Effects.Length; i++)
                if (m_Effects[i] != null)
                    m_Effects[i].Stop(StopMode.Revert);
            if (m_Hit != null)
                m_Hit.Stop(StopMode.Revert);
            if (m_Slow != null)
                m_Slow.Stop(StopMode.Revert);
            if (m_LowHealth != null)
                m_LowHealth.Stop(StopMode.Revert);
        }

        // a wanted low health plays once After is on and it isn't running — switched on in Before, it starts at After
        void Keep()
        {
            if (m_LowHealth != null && m_Want && SampleState.After && !m_LowHealth.IsPlaying)
                m_LowHealth.Play();
        }

        // the scene's own motion on the scaled clock — the spinners and the camera's sway show the slow-mo (and the motion blur)
        void Move(float dt)
        {
            m_Clock += dt;
            for (int i = 0; i < m_Spinners.Length; i++)
                if (m_Spinners[i] != null)
                    m_Spinners[i].Rotate(0f, 90f * dt, 0f, Space.World);
            if (m_Camera != null)
                m_Camera.position = m_CameraBase + m_Camera.right * (Mathf.Sin(m_Clock * 0.6f) * m_Sway);
        }

        void Refresh()
        {
            Set(m_Low, m_Want ? LowOn : LowOff);
            Set(m_ScaleLabel, s_Scale[Mathf.Clamp(Mathf.RoundToInt(Time.timeScale / 0.05f), 0, s_Scale.Length - 1)]);
            if (m_ShownPick != m_Selected)
            {
                for (int i = 0; i < m_Images.Length; i++)
                    if (m_Images[i] != null)
                        m_Images[i].color = i == m_Selected ? s_Picked : s_Base;
                m_ShownPick = m_Selected;
            }
        }

        static void Set(Text t, string s)
        {
            if (t != null && !ReferenceEquals(t.text, s))
                t.text = s;
        }

        void Build()
        {
            if (m_Root != null)
                Destroy(m_Root);
            m_ShownPick = -1;
            m_Root = new GameObject("PostFX Canvas", typeof(RectTransform));
            m_Root.transform.SetParent(transform, false);
            var canvas = m_Root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = m_Root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            m_Root.AddComponent<GraphicRaycaster>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < Effects; i++)
            {
                int k = i;
                Image image;
                MakeButton(font, s_Effects[i], new Vector2(0f, 0f), new Vector2(24f + i / 8 * 290f, 490f - i % 8 * 50f), out image).onClick.AddListener(() => PlayEffect(k));
                m_Images[i] = image;
            }
            Label(font, Keys, new Vector2(0f, 0f), new Vector2(24f, 80f));
            for (int i = 0; i < s_Combos.Length; i++)
            {
                int k = i;
                MakeButton(font, s_Combos[i], new Vector2(1f, 0f), new Vector2(-260f, 400f - i * 50f), out _).onClick.AddListener(() => Combo(k));
            }
            m_Low = Label(font, LowOff, new Vector2(1f, 0f), new Vector2(-260f, 180f));
            m_ScaleLabel = Label(font, s_Scale[20], new Vector2(1f, 0f), new Vector2(-260f, 140f));
            Label(font, Order, new Vector2(1f, 0f), new Vector2(-460f, 80f));
            Refresh();
        }

        void Combo(int k)
        {
            if (k == 0)
                Hit();
            else if (k == 1)
                SlowMo();
            else if (k == 2)
                Toggle();
            else
                StopAll();
        }

        Button MakeButton(Font font, string s, Vector2 anchor, Vector2 at, out Image image)
        {
            RectTransform rt = Rect(m_Root.transform, s, anchor, at, new Vector2(280f, 44f));
            image = rt.gameObject.AddComponent<Image>();
            image.color = s_Base;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Navigation nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            Text label = MakeText(Rect(rt, "Text", new Vector2(0f, 1f), new Vector2(10f, -4f), new Vector2(260f, 36f)), font, 18);
            label.alignment = TextAnchor.MiddleLeft;
            label.text = s;
            return button;
        }

        Text Label(Font font, string s, Vector2 anchor, Vector2 at)
        {
            Text t = MakeText(Rect(m_Root.transform, "Label", anchor, at, new Vector2(460f, 40f)), font, 20);
            t.alignment = TextAnchor.UpperLeft;
            t.text = s;
            return t;
        }

        static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 at, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = at;
            rt.sizeDelta = size;
            return rt;
        }

        static Text MakeText(RectTransform rt, Font font, int size)
        {
            Text text = rt.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }
    }
}
