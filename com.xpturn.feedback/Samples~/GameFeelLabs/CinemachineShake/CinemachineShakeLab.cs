using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.GameFeelLabs
{
    // S5 Cinemachine Shake Lab: one set of preset players shakes a plain camera or a Cinemachine one — the main-camera tag moves between
    // the two cameras and the next play finds the new one. A running endless stays on its camera, so a switch stops it and it starts
    // again on the new one. The Cinemachine items play only on the Cinemachine camera (prepared there — the scene starts on it), and a
    // lens button only when the live camera's lens has that size: field of view in perspective, the size in orthographic
    public sealed class CinemachineShakeLab : MonoBehaviour
    {
        public const int Fov = 2, Ortho = 4;
        static readonly string[] s_Presets = { "1 · Light", "2 · Medium", "3 · Heavy" };
        static readonly string[] s_Items = { "Impulse", "Impulse Clear", "Lens FOV", "Lens Dutch", "Lens Ortho", "Noise Amplitude", "Noise Frequency", "Go Near", "Go Wide", "Go Top" };
        static readonly string[] s_Live = { "Live — CM Near", "Live — CM Wide", "Live — CM Top", "Live — none" };
        const string SwitchText = "Z · Plain ↔ Cinemachine";
        const string OnCm = "Path — Cinemachine (shake extension)";
        const string OnPlain = "Path — plain camera (its transform)";
        const string EndlessOn = "X · Endless — on", EndlessOff = "X · Endless — off";
        static readonly Color s_Base = new Color(0.15f, 0.2f, 0.3f, 0.9f), s_Picked = new Color(0.35f, 0.45f, 0.65f, 0.95f);

        [SerializeField] GameObject m_CmCamera;
        [SerializeField] GameObject m_PlainCamera;
        [SerializeField] CinemachineBrain m_Brain;
        [SerializeField] CinemachineCamera[] m_Vcams = new CinemachineCamera[3];
        [SerializeField] FeedbackPlayer[] m_Presets = new FeedbackPlayer[3];
        [SerializeField] FeedbackPlayer m_Endless;
        [SerializeField] FeedbackPlayer[] m_Items = new FeedbackPlayer[10];

        [System.NonSerialized] InputAction[] m_Actions;
        [System.NonSerialized] InputAction[] m_Options;
        bool m_OnCm = true;
        bool m_Want;
        int m_Selected;
        Camera m_View;
        GameObject m_Root;
        Text m_Path, m_Live, m_EndlessLabel;
        readonly Button[] m_Buttons = new Button[10];
        readonly Image[] m_Images = new Image[10];
        int m_ShownPick = -1;

        public bool OnCinemachine => m_OnCm;
        public bool Wants => m_Want;
        public int Selected => m_Selected;

        void OnEnable()
        {
            m_OnCm = m_CmCamera != null && m_CmCamera.activeSelf;
            m_View = m_CmCamera != null ? m_CmCamera.GetComponent<Camera>() : null;
            m_Want = false;
            m_Selected = 0;
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
            for (int i = 0; i < 3; i++)
                if (Pressed(m_Actions[i]))
                    Preset(i);
            if (Pressed(m_Options[0]))
                Switch();
            if (Pressed(m_Options[1]))
                Toggle();
            if (Pressed(m_Options[2]))
                Next();
            if (Pressed(m_Options[3]))
                PlayItem(m_Selected);
            Keep();
            Refresh();
        }

        // only a pointer's press is refused over the UI — a key or a pad button always goes through
        static bool Pressed(InputAction a) => a != null && a.WasPressedThisFrame() && !(FromPointer(a) && SampleInput.PointerOverUI);

        static bool FromPointer(InputAction a) => a.activeControl != null && a.activeControl.device is Pointer;

        public void Preset(int i)
        {
            if (i >= 0 && i < m_Presets.Length && m_Presets[i] != null)
                m_Presets[i].Play();
        }

        public void Switch()
        {
            GameObject from = m_OnCm ? m_CmCamera : m_PlainCamera, to = m_OnCm ? m_PlainCamera : m_CmCamera;
            if (from == null || to == null)
                return;
            from.SetActive(false);
            from.tag = "Untagged";
            to.tag = "MainCamera";
            to.SetActive(true);
            m_OnCm = !m_OnCm;
            if (m_Endless != null && m_Endless.IsPlaying)
                m_Endless.Stop();
            Keep();
        }

        public void Toggle()
        {
            m_Want = !m_Want;
            if (!m_Want && m_Endless != null)
                m_Endless.Stop();
        }

        // a wanted endless plays once After is on and it isn't running — after a switch it starts on the new camera
        void Keep()
        {
            if (m_Endless != null && m_Want && SampleState.After && !m_Endless.IsPlaying)
                m_Endless.Play();
        }

        public bool Usable(int i)
        {
            if (!m_OnCm || i < 0 || i >= m_Items.Length || m_Items[i] == null)
                return false;
            bool ortho = m_View != null && m_View.orthographic;
            return i == Fov ? !ortho : i == Ortho ? ortho : true;
        }

        public void Next()
        {
            for (int k = 1; k <= m_Items.Length; k++)
            {
                int i = (m_Selected + k) % m_Items.Length;
                if (Usable(i))
                {
                    m_Selected = i;
                    return;
                }
            }
        }

        public void PlayItem(int i)
        {
            if (Usable(i))
                m_Items[i].Play();
        }

        void Refresh()
        {
            Set(m_Path, m_OnCm ? OnCm : OnPlain);
            Set(m_EndlessLabel, m_Want ? EndlessOn : EndlessOff);
            int live = 3;
            if (m_OnCm && m_Brain != null)
            {
                ICinemachineCamera active = m_Brain.ActiveVirtualCamera;
                for (int i = 0; i < m_Vcams.Length; i++)
                    if (m_Vcams[i] != null && ReferenceEquals(active, m_Vcams[i]))
                        live = i;
            }
            Set(m_Live, s_Live[live]);
            for (int i = 0; i < m_Buttons.Length; i++)
            {
                bool on = Usable(i);
                if (m_Buttons[i] != null && m_Buttons[i].interactable != on)
                    m_Buttons[i].interactable = on;
            }
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
            m_Root = new GameObject("Cinemachine Shake Canvas", typeof(RectTransform));
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
            m_Path = Label(font, new Vector2(0f, 0f), new Vector2(24f, 100f));
            m_Live = Label(font, new Vector2(0f, 0f), new Vector2(24f, 64f));
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                MakeButton(font, s_Presets[i], new Vector2(-520f, 400f - i * 50f), out _).onClick.AddListener(() => Preset(k));
            }
            MakeButton(font, SwitchText, new Vector2(-520f, 230f), out _).onClick.AddListener(Switch);
            MakeButton(font, EndlessOff, new Vector2(-520f, 180f), out m_EndlessLabel).onClick.AddListener(Toggle);
            for (int i = 0; i < s_Items.Length; i++)
            {
                int k = i;
                Button b = MakeButton(font, s_Items[i], new Vector2(-260f, 400f - i * 50f), out _);
                b.onClick.AddListener(() => PlayItem(k));
                m_Buttons[i] = b;
                m_Images[i] = b.GetComponent<Image>();
            }
            Refresh();
        }

        Button MakeButton(Font font, string s, Vector2 at, out Text label)
        {
            RectTransform rt = Rect(m_Root.transform, s, new Vector2(1f, 0f), at, new Vector2(240f, 44f));
            Image image = rt.gameObject.AddComponent<Image>();
            image.color = s_Base;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Navigation nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            label = MakeText(Rect(rt, "Text", new Vector2(0f, 1f), new Vector2(10f, -4f), new Vector2(220f, 36f)), font, 18);
            label.alignment = TextAnchor.MiddleLeft;
            label.text = s;
            return button;
        }

        Text Label(Font font, Vector2 anchor, Vector2 at)
        {
            Text t = MakeText(Rect(m_Root.transform, "Label", anchor, at, new Vector2(700f, 40f)), font, 22);
            t.alignment = TextAnchor.UpperLeft;
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
