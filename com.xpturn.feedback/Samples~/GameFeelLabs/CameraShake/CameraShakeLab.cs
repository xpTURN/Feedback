using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.GameFeelLabs
{
    // S5 Camera Shake Lab: two screens — the left camera is the main one (the presets' target) and channel 1, the right one channel 2.
    // An endless shake runs on its screen until stopped and a preset on top adds to it. An endless switched on in Before waits for
    // After: a play started with its category off has no items
    public sealed class CameraShakeLab : MonoBehaviour
    {
        static readonly string[] s_Buttons = { "1 · Light", "2 · Medium", "3 · Heavy", "Z · Shake screen 1", "X · Shake screen 2" };
        static readonly string[] s_EndlessOn = { "C · Endless 1 — on", "V · Endless 2 — on" };
        static readonly string[] s_EndlessOff = { "C · Endless 1 — off", "V · Endless 2 — off" };

        [SerializeField] FeedbackPlayer[] m_Presets = new FeedbackPlayer[3];
        [SerializeField] FeedbackPlayer m_Shake1;
        [SerializeField] FeedbackPlayer m_Shake2;
        [SerializeField] FeedbackPlayer m_Endless1;
        [SerializeField] FeedbackPlayer m_Endless2;

        [System.NonSerialized] InputAction[] m_Actions;
        [System.NonSerialized] InputAction[] m_Options;
        readonly bool[] m_Want = new bool[2];
        readonly Text[] m_EndlessLabels = new Text[2];
        GameObject m_Root;

        public bool Wants(int screen) => m_Want[screen];

        void OnEnable()
        {
            m_Want[0] = m_Want[1] = false;
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
                Shake(0);
            if (Pressed(m_Options[1]))
                Shake(1);
            if (Pressed(m_Options[2]))
                Toggle(0);
            if (Pressed(m_Options[3]))
                Toggle(1);
            Keep(0, m_Endless1);
            Keep(1, m_Endless2);
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

        public void Shake(int screen)
        {
            FeedbackPlayer p = screen == 0 ? m_Shake1 : m_Shake2;
            if (p != null)
                p.Play();
        }

        public void Toggle(int screen)
        {
            m_Want[screen] = !m_Want[screen];
            FeedbackPlayer p = screen == 0 ? m_Endless1 : m_Endless2;
            if (!m_Want[screen] && p != null)
                p.Stop();
        }

        // a wanted endless plays once After is on and it isn't running — switched on in Before, it starts at After
        void Keep(int screen, FeedbackPlayer p)
        {
            if (p != null && m_Want[screen] && SampleState.After && !p.IsPlaying)
                p.Play();
        }

        void Refresh()
        {
            for (int i = 0; i < 2; i++)
                if (m_EndlessLabels[i] != null)
                {
                    string s = m_Want[i] ? s_EndlessOn[i] : s_EndlessOff[i];
                    if (!ReferenceEquals(m_EndlessLabels[i].text, s))
                        m_EndlessLabels[i].text = s;
                }
        }

        void Build()
        {
            if (m_Root != null)
                Destroy(m_Root);
            m_Root = new GameObject("Camera Shake Canvas", typeof(RectTransform));
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
            RectTransform divider = Rect(m_Root.transform, "Divider", new Vector2(0.5f, 0f), new Vector2(-2f, 0f), new Vector2(4f, 1080f));
            divider.anchorMax = new Vector2(0.5f, 1f);
            divider.pivot = new Vector2(0f, 0f);
            divider.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.6f);
            Label(font, "Screen 1 — main camera · channel 1", new Vector2(0f, 0f), new Vector2(24f, 64f));
            Label(font, "Screen 2 — channel 2", new Vector2(0.5f, 0f), new Vector2(24f, 64f));
            for (int i = 0; i < 7; i++)
            {
                int k = i;
                RectTransform rt = Rect(m_Root.transform, i < 5 ? s_Buttons[i] : s_EndlessOff[i - 5], new Vector2(1f, 0f), new Vector2(-260f, 400f - i * 50f), new Vector2(240f, 44f));
                Image image = rt.gameObject.AddComponent<Image>();
                image.color = new Color(0.15f, 0.2f, 0.3f, 0.9f);
                var button = rt.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                Navigation nav = button.navigation;
                nav.mode = Navigation.Mode.None;
                button.navigation = nav;
                button.onClick.AddListener(() => Click(k));
                Text text = MakeText(Rect(rt, "Text", new Vector2(0f, 1f), new Vector2(10f, -4f), new Vector2(220f, 36f)), font, 18);
                text.alignment = TextAnchor.MiddleLeft;
                text.text = i < 5 ? s_Buttons[i] : s_EndlessOff[i - 5];
                if (i >= 5)
                    m_EndlessLabels[i - 5] = text;
            }
            Refresh();
        }

        void Click(int k)
        {
            if (k < 3)
                Preset(k);
            else if (k < 5)
                Shake(k - 3);
            else
                Toggle(k - 5);
        }

        void Label(Font font, string s, Vector2 anchor, Vector2 at)
        {
            Text t = MakeText(Rect(m_Root.transform, s, anchor, at, new Vector2(600f, 40f)), font, 22);
            t.alignment = TextAnchor.UpperLeft;
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
