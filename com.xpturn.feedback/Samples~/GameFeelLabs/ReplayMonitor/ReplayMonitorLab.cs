using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.GameFeelLabs
{
    // S16 Replay Monitor: a video on an in-game monitor, played, paused and moved by the Video items of the scene's players beside
    // flashes and shakes — this script only picks the player. Four states: Loading until the first playing frame (the screen stays
    // hidden, the render texture still holds the last picture), Running, Off (a play waiting for a signal holds the dimmed monitor
    // and its Revert brings it back) and Held. Off and Hold first skip a running freeze, replay or chapter, so that play's remaining
    // Play and Seek happen before the new state and not inside it
    public sealed class ReplayMonitorLab : MonoBehaviour
    {
        public enum LabState
        {
            Loading,
            Running,
            Off,
            Held,
        }

        public const int HitButton = 0, ChapterButton = 1, ReplayButton = 2, PowerButton = 3, HoldButton = 4, RevertButton = 5, RestoreButton = 6;
        const int ChapterSeconds = 2;
        const int MaxTenths = 60;
#if UNITY_WEBGL
        const string LoadingText = "Video clips do not play on Web";
#else
        const string LoadingText = "Loading";
#endif
        static readonly string[] s_Labels = { "1 · Hit Freeze", "2 · Chapter Jump", "3 · Instant Replay", "Z · Power", "X · Hold", "C · Stop (Revert)", "V · Restore" };
        static readonly Color s_Base = new Color(0.15f, 0.2f, 0.3f, 0.9f);
        static string[] s_Times;

        [SerializeField] VideoPlayer m_Video;
        [SerializeField] Renderer m_Screen;
        [SerializeField] FeedbackPlayer m_PowerOn;
        [SerializeField] FeedbackPlayer m_PowerOff;
        [SerializeField] FeedbackPlayer m_HitFreeze;
        [SerializeField] FeedbackPlayer m_Replay;
        [SerializeField] FeedbackPlayer m_Hold;
        [SerializeField] FeedbackPlayer[] m_Chapters = new FeedbackPlayer[3];

        [System.NonSerialized] InputAction[] m_Actions;
        [System.NonSerialized] InputAction[] m_Options;
        LabState m_State;
        int m_Pending = -1;
        GameObject m_Root;
        Text m_StateLabel, m_TimeLabel;
        readonly Button[] m_Buttons = new Button[7];

        public LabState State => m_State;

        void OnEnable()
        {
            m_State = LabState.Loading;
            m_Pending = -1;
            if (m_Screen != null)
                m_Screen.enabled = false;
            if (s_Times == null)
                BuildTimes();
            Build();
        }

        void OnDisable()
        {
            Settle();
            if (m_PowerOff != null)
                m_PowerOff.Stop(StopMode.Revert);
            if (m_Hold != null)
                m_Hold.Stop(StopMode.Revert);
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
            Sync();
            if (Pressed(m_Actions[0]))
                HitFreeze();
            if (Pressed(m_Actions[1]))
                NextChapter();
            if (Pressed(m_Actions[2]))
                InstantReplay();
            if (Pressed(m_Options[0]))
                Power();
            if (Pressed(m_Options[1]))
                HoldFrame();
            if (Pressed(m_Options[2]))
                StopHold();
            if (Pressed(m_Options[3]))
                RestoreHold();
            Refresh();
        }

        // only a pointer's press is refused over the UI — a key or a pad button always goes through
        static bool Pressed(InputAction a) => a != null && a.WasPressedThisFrame() && !(FromPointer(a) && SampleInput.PointerOverUI);

        static bool FromPointer(InputAction a) => a.activeControl != null && a.activeControl.device is Pointer;

        void Sync()
        {
            if (m_State == LabState.Loading && m_Video.isPlaying)
            {
                m_State = LabState.Running;
                m_Screen.enabled = true;
            }
            else if (m_State == LabState.Held && !m_Hold.IsPlaying)
            {
                m_State = LabState.Running;
            }
            if (m_Pending >= 0 && ChapterAt(m_Video.time) == m_Pending)
                m_Pending = -1;
        }

        public bool Accepts(int button)
        {
            switch (button)
            {
                case HitButton:
                case ReplayButton:
                case HoldButton:
                    return m_State == LabState.Running;
                case ChapterButton:
                case PowerButton:
                    return m_State == LabState.Running || m_State == LabState.Off;
                case RevertButton:
                case RestoreButton:
                    return m_State == LabState.Held;
                default:
                    return false;
            }
        }

        public void HitFreeze()
        {
            if (Accepts(HitButton))
                m_HitFreeze.Play();
        }

        // a Seek reaches the video's time a few frames later: until then the next chapter counts from the one just sent
        public void NextChapter()
        {
            if (!Accepts(ChapterButton))
                return;
            int from = m_Pending >= 0 ? m_Pending : ChapterAt(m_Video.time);
            m_Pending = (from + 1) % m_Chapters.Length;
            m_Chapters[m_Pending].Play();
        }

        public void InstantReplay()
        {
            if (!Accepts(ReplayButton))
                return;
            m_Pending = -1;
            m_Replay.Play();
        }

        public void Power()
        {
            if (m_State == LabState.Running)
            {
                Settle();
                m_PowerOff.Play();
                m_State = LabState.Off;
            }
            else if (m_State == LabState.Off)
            {
                m_PowerOff.Stop(StopMode.Revert);
                m_PowerOn.Play();
                m_State = LabState.Running;
            }
        }

        public void HoldFrame()
        {
            if (!Accepts(HoldButton))
                return;
            Settle();
            m_Pending = -1;
            m_Hold.Play();
            m_State = LabState.Held;
        }

        public void StopHold()
        {
            if (!Accepts(RevertButton))
                return;
            m_Hold.Stop(StopMode.Revert);
            m_State = LabState.Running;
        }

        public void RestoreHold()
        {
            if (!Accepts(RestoreButton))
                return;
            m_Hold.Restore();
            m_State = LabState.Running;
        }

        void Settle()
        {
            Skip(m_HitFreeze);
            Skip(m_Replay);
            for (int i = 0; i < m_Chapters.Length; i++)
                Skip(m_Chapters[i]);
        }

        static void Skip(FeedbackPlayer player)
        {
            if (player != null)
                player.Skip();
        }

        int ChapterAt(double time) => Mathf.Clamp((int)(time / ChapterSeconds), 0, m_Chapters.Length - 1);

        static void BuildTimes()
        {
            var times = new string[MaxTenths + 1];
            for (int i = 0; i <= MaxTenths; i++)
                times[i] = i / 10 + "." + i % 10 + " s · Chapter " + (char)('A' + Mathf.Min(i / (ChapterSeconds * 10), 2));
            s_Times = times;
        }

        string StateText()
        {
            switch (m_State)
            {
                case LabState.Loading:
                    return LoadingText;
                case LabState.Off:
                    return "Off";
                case LabState.Held:
                    return "Held";
                default:
                    return m_Video.isPlaying ? "Playing" : "Paused";
            }
        }

        void Refresh()
        {
            Set(m_StateLabel, StateText());
            Set(m_TimeLabel, s_Times[Mathf.Clamp((int)(m_Video.time * 10.0), 0, MaxTenths)]);
            for (int i = 0; i < m_Buttons.Length; i++)
            {
                bool on = Accepts(i);
                if (m_Buttons[i] != null && m_Buttons[i].interactable != on)
                    m_Buttons[i].interactable = on;
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
            m_Root = new GameObject("Replay Monitor Controls", typeof(RectTransform));
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
            m_Buttons[HitButton] = MakeButton(font, HitButton, HitFreeze);
            m_Buttons[ChapterButton] = MakeButton(font, ChapterButton, NextChapter);
            m_Buttons[ReplayButton] = MakeButton(font, ReplayButton, InstantReplay);
            m_Buttons[PowerButton] = MakeButton(font, PowerButton, Power);
            m_Buttons[HoldButton] = MakeButton(font, HoldButton, HoldFrame);
            m_Buttons[RevertButton] = MakeButton(font, RevertButton, StopHold);
            m_Buttons[RestoreButton] = MakeButton(font, RestoreButton, RestoreHold);
            m_StateLabel = Label(font, new Vector2(24f, -716f));
            m_TimeLabel = Label(font, new Vector2(24f, -752f));
            Refresh();
        }

        Button MakeButton(Font font, int index, UnityEngine.Events.UnityAction action)
        {
            RectTransform rt = Rect(m_Root.transform, s_Labels[index], new Vector2(24f, -360f - index * 50f), new Vector2(300f, 44f));
            Image image = rt.gameObject.AddComponent<Image>();
            image.color = s_Base;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Navigation nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            button.onClick.AddListener(action);
            Text label = MakeText(Rect(rt, "Text", new Vector2(10f, -4f), new Vector2(280f, 36f)), font, 20);
            label.alignment = TextAnchor.MiddleLeft;
            label.text = s_Labels[index];
            return button;
        }

        Text Label(Font font, Vector2 at)
        {
            Text t = MakeText(Rect(m_Root.transform, "Label", at, new Vector2(420f, 34f)), font, 24);
            t.alignment = TextAnchor.UpperLeft;
            return t;
        }

        static RectTransform Rect(Transform parent, string name, Vector2 at, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
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
