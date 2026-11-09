using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.GameFeelLabs
{
    // S3 Before / After: one input moves one body state that both tracks show — the left track has no feedback, the right one plays
    // its layers (squash · dust · camera · trail) through the layer gate. The motion is kinematic in track-local coordinates, so the
    // two bodies are the same to the bit. A trail's 'off' is played outside the gate (a finished Set stays — the trail would not end)
    public sealed class BeforeAfterLab : MonoBehaviour
    {
        public const int Squash = 0, Dust = 1, Shake = 2, Trail = 3;
        static readonly string[] s_Names = { "Squash", "Dust", "Camera", "Trail" };
        static readonly string[] s_Keys = { "Z · ←", "X · →", "C · ↓", "V · ↑" };
        static readonly string[] s_On = new string[4], s_Off = new string[4];

        [SerializeField] Transform m_BeforeBody;
        [SerializeField] Transform m_AfterBody;
        [SerializeField] Transform m_BeforeRig;
        [SerializeField] Transform m_AfterRig;
        [SerializeField] Vector3 m_BeforeOrigin;
        [SerializeField] Vector3 m_AfterOrigin;
        [SerializeField] Rect[] m_Platforms = new Rect[0];
        [SerializeField] float m_MinX = -2f;
        [SerializeField] float m_MaxX = 30f;
        [SerializeField] float m_Speed = 6f;
        [SerializeField] float m_JumpSpeed = 10f;
        [SerializeField] float m_Gravity = 28f;
        [SerializeField] float m_HardestFall = 12f;
        [SerializeField] Vector3 m_RigOffset = new Vector3(4f, 3f, -11f);
        [SerializeField] FeedbackPlayer m_JumpSquash;
        [SerializeField] FeedbackPlayer m_LandSquash;
        [SerializeField] FeedbackPlayer m_JumpDust;
        [SerializeField] FeedbackPlayer m_LandDust;
        [SerializeField] FeedbackPlayer m_LandShake;
        [SerializeField] FeedbackPlayer m_TrailOn;
        [SerializeField] FeedbackPlayer m_TrailOff;

        [System.NonSerialized] InputAction m_Move;
        [System.NonSerialized] InputAction m_Jump;
        [System.NonSerialized] InputAction[] m_Options;
        Vector2 m_Position;
        Vector2 m_Velocity;
        bool m_Grounded = true;
        GameObject m_Root;
        readonly Text[] m_Labels = new Text[4];

        public Vector2 LocalPosition => m_Position;
        public bool Grounded => m_Grounded;

        static BeforeAfterLab()
        {
            for (int i = 0; i < 4; i++)
            {
                s_On[i] = s_Keys[i] + " · " + s_Names[i] + " — on";
                s_Off[i] = s_Keys[i] + " · " + s_Names[i] + " — off";
            }
        }

        void OnEnable()
        {
            m_Position = new Vector2(0f, 0f);
            m_Velocity = Vector2.zero;
            m_Grounded = true;
            Build();
            Place();
        }

        void OnDisable()
        {
            if (m_Root != null)
                Destroy(m_Root);
        }

        void Update()
        {
            if (m_Move == null)
            {
                m_Move = SampleInput.Find("Move");
                m_Jump = SampleInput.Find("Action1");
                m_Options = new[] { SampleInput.Find("Option1"), SampleInput.Find("Option2"), SampleInput.Find("Option3"), SampleInput.Find("Option4") };
            }
            for (int i = 0; i < 4; i++)
                if (m_Options[i] != null && m_Options[i].WasPressedThisFrame())
                    Toggle(i);
            float move = m_Move != null ? m_Move.ReadValue<Vector2>().x : 0f;
            bool jump = m_Jump != null && m_Jump.WasPressedThisFrame() && !(FromPointer(m_Jump) && SampleInput.PointerOverUI);
            Step(Time.deltaTime, move, jump);
            Place();
            RefreshLabels();
        }

        // only a pointer's press is refused over the UI — a key or a pad button always goes through (no silent block)
        internal static bool FromPointer(InputAction a) => a.activeControl != null && a.activeControl.device is Pointer;

        public void Toggle(int layer)
        {
            bool on = !SampleLayers.Enabled(layer);
            SampleLayers.Set(layer, on);
            if (!on && layer == Trail && m_TrailOff != null)
                m_TrailOff.Play();
        }

        // one step of the shared body: run · jump · gravity · one-way platforms and the ground (y 0)
        public void Step(float dt, float move, bool jump)
        {
            if (dt <= 0f)
                return;
            m_Velocity.x = Mathf.Clamp(move, -1f, 1f) * m_Speed;
            if (jump && m_Grounded)
            {
                m_Velocity.y = m_JumpSpeed;
                m_Grounded = false;
                if (SampleLayers.IsOn(Squash) && m_JumpSquash != null)
                    m_JumpSquash.Play();
                if (SampleLayers.IsOn(Dust) && m_JumpDust != null)
                    m_JumpDust.Play();
                if (SampleLayers.IsOn(Trail) && m_TrailOn != null)
                    m_TrailOn.Play();
            }
            float y0 = m_Position.y;
            m_Velocity.y -= m_Gravity * dt;
            Vector2 next = m_Position + m_Velocity * dt;
            next.x = Mathf.Clamp(next.x, m_MinX, m_MaxX);
            float floor = 0f;
            bool landed = false;
            if (m_Velocity.y <= 0f)
            {
                for (int i = 0; i < m_Platforms.Length; i++)
                {
                    Rect r = m_Platforms[i];
                    if (next.x >= r.xMin && next.x <= r.xMax && y0 >= r.yMax - 1e-4f && next.y <= r.yMax)
                        floor = Mathf.Max(floor, r.yMax);
                }
                if (next.y <= floor)
                {
                    landed = !m_Grounded;
                    float fall = -m_Velocity.y;
                    next.y = floor;
                    m_Velocity.y = 0f;
                    m_Grounded = true;
                    if (landed)
                        Land(fall);
                }
            }
            if (m_Grounded && !landed && !Supported(next))
                m_Grounded = false;
            m_Position = next;
        }

        bool Supported(Vector2 p)
        {
            if (p.y <= 1e-4f)
                return true;
            for (int i = 0; i < m_Platforms.Length; i++)
            {
                Rect r = m_Platforms[i];
                if (p.x >= r.xMin && p.x <= r.xMax && Mathf.Abs(p.y - r.yMax) <= 1e-4f)
                    return true;
            }
            return false;
        }

        void Land(float fall)
        {
            var args = new PlayArgs().WithIntensity(Mathf.Clamp01(fall / m_HardestFall));
            if (SampleLayers.IsOn(Squash) && m_LandSquash != null)
                m_LandSquash.Play(in args);
            if (SampleLayers.IsOn(Dust) && m_LandDust != null)
                m_LandDust.Play();
            if (SampleLayers.IsOn(Shake) && m_LandShake != null)
                m_LandShake.Play(in args);
            if (m_TrailOff != null)
                m_TrailOff.Play();
        }

        void Place()
        {
            var local = new Vector3(m_Position.x, m_Position.y, 0f);
            if (m_BeforeBody != null)
                m_BeforeBody.position = m_BeforeOrigin + local;
            if (m_AfterBody != null)
                m_AfterBody.position = m_AfterOrigin + local;
            var rig = new Vector3(m_Position.x, 0f, 0f) + m_RigOffset;
            if (m_BeforeRig != null)
                m_BeforeRig.position = m_BeforeOrigin + rig;
            if (m_AfterRig != null)
                m_AfterRig.position = m_AfterOrigin + rig;
        }

        void RefreshLabels()
        {
            for (int i = 0; i < 4; i++)
                if (m_Labels[i] != null)
                {
                    string s = SampleLayers.Enabled(i) ? s_On[i] : s_Off[i];
                    if (!ReferenceEquals(m_Labels[i].text, s))
                        m_Labels[i].text = s;
                }
        }

        void Build()
        {
            if (m_Root != null)
                Destroy(m_Root);
            m_Root = new GameObject("Before After Canvas", typeof(RectTransform));
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
            var divider = Rect(m_Root.transform, "Divider", new Vector2(0.5f, 0f), new Vector2(-2f, 0f), new Vector2(4f, 1080f));
            divider.anchorMax = new Vector2(0.5f, 1f);
            divider.pivot = new Vector2(0f, 0f);
            divider.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.6f);
            Label(font, "Before — no feedback", new Vector2(0.5f, 1f), new Vector2(-420f, -16f));
            Label(font, "After — the layers below", new Vector2(1f, 1f), new Vector2(-420f, -16f));
            for (int i = 0; i < 4; i++)
            {
                int layer = i;
                RectTransform rt = Rect(m_Root.transform, s_Names[i], new Vector2(0.5f, 0f), new Vector2(40f + i * 230f, 96f), new Vector2(220f, 44f));
                Image image = rt.gameObject.AddComponent<Image>();
                image.color = new Color(0.15f, 0.2f, 0.3f, 0.9f);
                var button = rt.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                var nav = button.navigation;
                nav.mode = Navigation.Mode.None;
                button.navigation = nav;
                button.onClick.AddListener(() => Toggle(layer));
                Text text = MakeText(Rect(rt, "Text", new Vector2(0f, 1f), new Vector2(10f, -4f), new Vector2(200f, 36f)), font, 18);
                text.alignment = TextAnchor.MiddleLeft;
                m_Labels[i] = text;
            }
            RefreshLabels();
        }

        void Label(Font font, string s, Vector2 anchor, Vector2 at)
        {
            Text t = MakeText(Rect(m_Root.transform, s, anchor, at, new Vector2(400f, 40f)), font, 24);
            t.alignment = TextAnchor.UpperRight;
            t.text = s;
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
