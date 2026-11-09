using System;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace xpTURN.Feedback.Samples
{
    // the common HUD: one for the whole play (made by Ensure — never placed in a scene), drawn under the feedback overlay. Its
    // controls write through SampleState; its own input is shortcuts (the widgets take no navigation — the sample's Move keeps
    // the left stick). Resources are taken in OnEnable and let go in OnDisable, so a recompile during play keeps one HUD
    public sealed class SampleHud : MonoBehaviour
    {
        public const int SortingOrder = 30000;
        const float NoticeSeconds = 1.5f;
        const int Window = 120;
        static readonly float[] s_Times = { 0f, 0.1f, 0.25f, 0.5f, 1f };
        static readonly string[] s_TimeText = { ", . · LT RT — Time scale 0", ", . · LT RT — Time scale 0.1", ", . · LT RT — Time scale 0.25", ", . · LT RT — Time scale 0.5", ", . · LT RT — Time scale 1" };
        static readonly string[] s_IntensityText = { "[ ] · LB RB — Intensity 0.00", "[ ] · LB RB — Intensity 0.25", "[ ] · LB RB — Intensity 0.50", "[ ] · LB RB — Intensity 0.75", "[ ] · LB RB — Intensity 1.00", "[ ] · LB RB — Intensity 1.25", "[ ] · LB RB — Intensity 1.50", "[ ] · LB RB — Intensity 1.75", "[ ] · LB RB — Intensity 2.00" };
        // the HUD shows its own keys — a card's Controls are the sample's only
        internal const string Keys = "Esc · Start · Select — back · H · L3 — fold the HUD";
        static readonly string[] s_CategoryNames = { "", "Motion", "Camera", "Flash", "PostFX", "Time", "Particles", "UI", "Haptics" };

        public static SampleHud Instance { get; private set; }
        internal static int Made { get; private set; }

        [SerializeField] Canvas m_Canvas;
        [SerializeField] Text m_Card;
        [SerializeField] Text m_State;
        [SerializeField] Text m_Gc;
        [SerializeField] Text m_Notice;
        [SerializeField] Text m_IntensityLabel;
        [SerializeField] Text m_TimeLabel;
        [SerializeField] Toggle m_After;
        [SerializeField] Slider m_Intensity;
        [SerializeField] Slider m_Time;
        [SerializeField] Button m_Select;
        [SerializeField] Text m_SelectLabel;

        ProfilerRecorder m_Recorder;
        readonly long[] m_Window = new long[Window];
        int m_Index;
        int m_Filled;
        long m_ShownBucket = long.MinValue;
        Dictionary<long, string> m_GcStrings;
        SampleCard m_ShownCard;
        bool m_ShownHub;
        int m_ShownOff = -1;
        bool m_ShownAfter;
        string m_ShownNotice;
        string m_NoticeText;
        float m_NoticeUntil;
        // [NonSerialized]: InputAction is serializable — across a recompile during play it would come back as a detached copy
        [NonSerialized] InputAction m_Back, m_AfterKey, m_Less, m_More, m_Slower, m_Faster, m_Fold;
        UnityEngine.Events.UnityAction<bool> m_OnAfter;
        UnityEngine.Events.UnityAction<float> m_OnIntensity;
        UnityEngine.Events.UnityAction<float> m_OnTime;
#if UNITY_EDITOR
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<GameObject> m_Roots = new List<GameObject>();
        Action<FeedbackPlayer, PlaybackHandle> m_OnPlayed;
        UnityEngine.Events.UnityAction m_OnSelect;
        FeedbackPlayer m_LastPlayed;
        FeedbackPlayer m_ShownPlayer;
        Scene m_PlayersScene;
        // a player's label is built once
        readonly Dictionary<FeedbackPlayer, string> m_SelectTexts = new Dictionary<FeedbackPlayer, string>();
#endif

        internal long GcBytes { get; private set; }
        internal bool GcAvailable => m_Recorder.Valid;

        public static SampleHud Ensure()
        {
            if (Instance != null)
                return Instance;
            return new GameObject("Sample HUD").AddComponent<SampleHud>();
        }

        // a short line under the HUD — a sample passes a constant string (a refused input, a missing optional part)
        public static void Notify(string text)
        {
            if (Instance == null)
                return;
            Instance.m_NoticeText = text;
            Instance.m_NoticeUntil = Time.unscaledTime + NoticeSeconds;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            Made = 0;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Made++;
            DontDestroyOnLoad(gameObject);
            Build();
        }

        void OnEnable()
        {
            if (Instance != null && Instance != this)
                return;
            Instance = this;
            m_Recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            m_GcStrings = new Dictionary<long, string>();
            m_ShownBucket = long.MinValue;
            m_ShownCard = null;
            m_ShownOff = -1;
            m_ShownNotice = null;
            m_OnAfter = OnAfter;
            m_OnIntensity = OnIntensity;
            m_OnTime = OnTime;
            m_After.onValueChanged.AddListener(m_OnAfter);
            m_Intensity.onValueChanged.AddListener(m_OnIntensity);
            m_Time.onValueChanged.AddListener(m_OnTime);
#if UNITY_EDITOR
            m_OnPlayed = OnPlayed;
            m_OnSelect = OnSelect;
            if (m_Select != null)
                m_Select.onClick.AddListener(m_OnSelect);
            m_PlayersScene = default;
#endif
        }

        void OnDisable()
        {
            if (Instance != this)
                return;
            Instance = null;
            m_Recorder.Dispose();
            m_After.onValueChanged.RemoveListener(m_OnAfter);
            m_Intensity.onValueChanged.RemoveListener(m_OnIntensity);
            m_Time.onValueChanged.RemoveListener(m_OnTime);
            m_Back = m_AfterKey = m_Less = m_More = m_Slower = m_Faster = m_Fold = null;
#if UNITY_EDITOR
            if (m_Select != null)
                m_Select.onClick.RemoveListener(m_OnSelect);
            Unsubscribe();
#endif
        }

        // the samples' actions are let go with the HUD (the play's end), not when it is only switched off
        void OnDestroy()
        {
            if (Instance == null || Instance == this)
                SampleInput.Release();
        }

        void Update()
        {
            Shortcuts();
            Sync();
            Gc();
#if UNITY_EDITOR
            Players();
#endif
        }

        void Shortcuts()
        {
            if (m_Back == null && SampleInput.Asset != null)
            {
                m_Back = SampleInput.Find("Back");
                m_AfterKey = SampleInput.Find("After");
                m_Less = SampleInput.Find("IntensityDown");
                m_More = SampleInput.Find("IntensityUp");
                m_Slower = SampleInput.Find("TimeDown");
                m_Faster = SampleInput.Find("TimeUp");
                m_Fold = SampleInput.Find("HudFold");
            }
            if (m_Back == null)
                return;
            if (m_AfterKey.WasPressedThisFrame())
                SampleState.SetAfter(!SampleState.After);
            if (m_Less.WasPressedThisFrame())
                SampleState.SetIntensity(SampleState.Intensity - 0.25f);
            if (m_More.WasPressedThisFrame())
                SampleState.SetIntensity(SampleState.Intensity + 0.25f);
            if (m_Slower.WasPressedThisFrame())
                SampleState.SetTimeScale(s_Times[Math.Max(0, TimeIndex(SampleState.TimeScale) - 1)]);
            if (m_Faster.WasPressedThisFrame())
                SampleState.SetTimeScale(s_Times[Math.Min(s_Times.Length - 1, TimeIndex(SampleState.TimeScale) + 1)]);
            if (m_Fold.WasPressedThisFrame())
                m_Canvas.enabled = !m_Canvas.enabled;
            if (m_Back.WasPressedThisFrame() && SceneManager.GetActiveScene().name != SampleKitPaths.HubName && SampleHub.HubReachable)
                SampleHub.BackToHub();
        }

        static int TimeIndex(float scale)
        {
            int best = 0;
            for (int i = 1; i < s_Times.Length; i++)
                if (Mathf.Abs(s_Times[i] - scale) < Mathf.Abs(s_Times[best] - scale))
                    best = i;
            return best;
        }

        // the widgets and lines follow the state (a sample, a test or S12 may write it) — written only when they change
        void Sync()
        {
            SampleSceneMarker marker = SampleSceneMarker.Current;
            SampleCard card = marker != null ? marker.Card : null;
            bool hub = SampleHub.Instance != null;
            if (card != m_ShownCard || hub != m_ShownHub)
            {
                m_ShownCard = card;
                m_ShownHub = hub;
                m_Card.text = card != null ? card.Title + "\n" + card.Controls + "\n" + Keys : hub ? "Samples — pick a card" : "";
            }
            int off = 0;
            for (int i = 1; i < s_CategoryNames.Length; i++)
                if (!SampleState.BaseEnabled((FeedbackCategory)i))
                    off |= 1 << i;
            if (off != m_ShownOff || SampleState.After != m_ShownAfter)
            {
                m_ShownOff = off;
                m_ShownAfter = SampleState.After;
                m_State.text = StateLine(off, m_ShownAfter);
            }
            if (m_After.isOn != SampleState.After)
                m_After.SetIsOnWithoutNotify(SampleState.After);
            int intensity = Mathf.RoundToInt(SampleState.Intensity * 4f);
            if ((int)m_Intensity.value != intensity)
            {
                m_Intensity.SetValueWithoutNotify(intensity);
                m_IntensityLabel.text = s_IntensityText[intensity];
            }
            int time = TimeIndex(SampleState.TimeScale);
            if ((int)m_Time.value != time)
            {
                m_Time.SetValueWithoutNotify(time);
                m_TimeLabel.text = s_TimeText[time];
            }
            string notice = Notice();
            if (!ReferenceEquals(notice, m_ShownNotice))
            {
                m_ShownNotice = notice;
                m_Notice.text = notice ?? "";
            }
        }

        static string StateLine(int off, bool after)
        {
            var sb = new StringBuilder(after ? "Tab · R3 — feedback on" : "Tab · R3 — feedback off (Before)");
            if (off != 0)
            {
                sb.Append("\nOff in the settings:");
                for (int i = 1; i < s_CategoryNames.Length; i++)
                    if ((off & (1 << i)) != 0)
                        sb.Append(' ').Append(s_CategoryNames[i]);
            }
            return sb.ToString();
        }

        // a project on the old input only comes first (nothing else works), then the Game view's focus (the editor), then a sample's line
        string Notice()
        {
#if !ENABLE_INPUT_SYSTEM
            return "Feedback samples use the Input System — Player Settings > Active Input Handling";
#else
#if UNITY_EDITOR
            if (!Application.isFocused)
                return "Click the Game view to give it input";
#endif
            if (m_NoticeText != null && Time.unscaledTime < m_NoticeUntil)
                return m_NoticeText;
            return null;
#endif
        }

        void OnAfter(bool on) => SampleState.SetAfter(on);
        void OnIntensity(float step) => SampleState.SetIntensity(step * 0.25f);
        void OnTime(float step) => SampleState.SetTimeScale(s_Times[Mathf.Clamp((int)step, 0, s_Times.Length - 1)]);

        // the frame's allocation: in the editor over the editor's rest (the least of the last 120 frames — the editor allocates
        // each frame), absolute in a development player, 'development build only' in a release player (no counter there)
        void Gc()
        {
            if (!m_Recorder.Valid)
            {
                if (m_ShownBucket != long.MaxValue)
                {
                    m_ShownBucket = long.MaxValue;
                    m_Gc.text = "GC: development build only";
                }
                return;
            }
            long v = m_Recorder.LastValue;
            long shown = v;
            if (Application.isEditor)
            {
                m_Window[m_Index] = v;
                m_Index = (m_Index + 1) % Window;
                if (m_Filled < Window)
                    m_Filled++;
                long min = long.MaxValue;
                for (int i = 0; i < m_Filled; i++)
                    if (m_Window[i] < min)
                        min = m_Window[i];
                shown = v - min;
            }
            GcBytes = shown;
            long bucket = shown / 64;
            if (bucket == m_ShownBucket)
                return;
            m_ShownBucket = bucket;
            if (!m_GcStrings.TryGetValue(bucket, out string s))
            {
                s = "GC " + (bucket * 64) + " B/frame" + (Application.isEditor ? " (over the editor's rest)" : "");
                m_GcStrings[bucket] = s;
            }
            m_Gc.text = s;
        }

#if UNITY_EDITOR
        // the editor's 'Select Player': the last player that played in the active scene (subscribed when the scene changes)
        void Players()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene != m_PlayersScene)
            {
                m_PlayersScene = scene;
                Unsubscribe();
                m_LastPlayed = null;
                m_SelectTexts.Clear();
                scene.GetRootGameObjects(m_Roots);
                var found = new List<FeedbackPlayer>();
                foreach (GameObject root in m_Roots)
                {
                    root.GetComponentsInChildren(true, found);
                    m_Players.AddRange(found);
                }
                foreach (FeedbackPlayer p in m_Players)
                    p.Played += m_OnPlayed;
            }
            if (m_LastPlayed != m_ShownPlayer && m_SelectLabel != null)
            {
                m_ShownPlayer = m_LastPlayed;
                string text = "Select Player";
                if (m_LastPlayed != null && !m_SelectTexts.TryGetValue(m_LastPlayed, out text))
                    m_SelectTexts.Add(m_LastPlayed, text = "Select Player: " + m_LastPlayed.name);
                m_SelectLabel.text = text;
            }
        }

        void Unsubscribe()
        {
            foreach (FeedbackPlayer p in m_Players)
                if (p != null)
                    p.Played -= m_OnPlayed;
            m_Players.Clear();
        }

        void OnPlayed(FeedbackPlayer player, PlaybackHandle handle) => m_LastPlayed = player;

        void OnSelect()
        {
            if (m_LastPlayed == null)
                return;
            UnityEditor.Selection.activeObject = m_LastPlayed.gameObject;
            UnityEditor.EditorGUIUtility.PingObject(m_LastPlayed.gameObject);
        }
#endif

        // ——— the UI, made once in code: the built-in font · sprite-less images (the built-in UI sprites are editor-only)
        void Build()
        {
            m_Canvas = gameObject.AddComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.sortingOrder = SortingOrder;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            RectTransform panel = Rect("Panel", transform, new Vector2(12f, -12f), new Vector2(560f, 330f));
            panel.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            m_Card = Label(panel, font, new Vector2(12f, -10f), new Vector2(536f, 96f));
            m_State = Label(panel, font, new Vector2(12f, -108f), new Vector2(536f, 48f));
            m_After = MakeToggle(panel, font, "Feedback (After)", new Vector2(12f, -160f));
            m_Intensity = MakeSlider(panel, new Vector2(12f, -196f), 8);
            m_IntensityLabel = Label(panel, font, new Vector2(232f, -192f), new Vector2(300f, 26f));
            m_Time = MakeSlider(panel, new Vector2(12f, -228f), s_Times.Length - 1);
            m_TimeLabel = Label(panel, font, new Vector2(232f, -224f), new Vector2(300f, 26f));
            m_Gc = Label(panel, font, new Vector2(12f, -258f), new Vector2(536f, 26f));
            m_Notice = Label(panel, font, new Vector2(12f, -284f), new Vector2(536f, 26f));
            m_Notice.color = new Color(1f, 0.85f, 0.3f);
            m_Intensity.SetValueWithoutNotify(4f);
            m_IntensityLabel.text = s_IntensityText[4];
            m_Time.SetValueWithoutNotify(s_Times.Length - 1);
            m_TimeLabel.text = s_TimeText[s_Times.Length - 1];
            m_After.SetIsOnWithoutNotify(true);
#if UNITY_EDITOR
            RectTransform select = Rect("Select Player", panel, new Vector2(330f, -158f), new Vector2(218f, 28f));
            Image image = select.gameObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.2f);
            m_Select = select.gameObject.AddComponent<Button>();
            m_Select.targetGraphic = image;
            m_Select.navigation = new Navigation { mode = Navigation.Mode.None };
            m_SelectLabel = Label(select, font, new Vector2(8f, -2f), new Vector2(206f, 24f));
            m_SelectLabel.text = "Select Player";
#endif
        }

        static RectTransform Rect(string name, Transform parent, Vector2 at, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = at;
            rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Stretch(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static Text Label(Transform parent, Font font, Vector2 at, Vector2 size)
        {
            Text text = Rect("Text", parent, at, size).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = 18;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        static Toggle MakeToggle(Transform parent, Font font, string label, Vector2 at)
        {
            RectTransform root = Rect("Toggle", parent, at, new Vector2(300f, 28f));
            RectTransform box = Rect("Box", root, Vector2.zero, new Vector2(24f, 24f));
            Image boxImage = box.gameObject.AddComponent<Image>();
            boxImage.color = new Color(1f, 1f, 1f, 0.9f);
            Image check = Rect("Check", box, new Vector2(4f, -4f), new Vector2(16f, 16f)).gameObject.AddComponent<Image>();
            check.color = new Color(0.25f, 0.75f, 0.35f);
            Label(root, font, new Vector2(32f, -1f), new Vector2(260f, 26f)).text = label;
            var toggle = root.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = boxImage;
            toggle.graphic = check;
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };
            return toggle;
        }

        static Slider MakeSlider(Transform parent, Vector2 at, int steps)
        {
            RectTransform root = Rect("Slider", parent, at, new Vector2(210f, 20f));
            root.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);
            RectTransform fill = Stretch("Fill", Stretch("Fill Area", root));
            fill.gameObject.AddComponent<Image>().color = new Color(0.35f, 0.6f, 1f, 0.8f);
            RectTransform handle = Stretch("Handle", Stretch("Handle Slide Area", root));
            handle.sizeDelta = new Vector2(14f, 0f);
            Image handleImage = handle.gameObject.AddComponent<Image>();
            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = steps;
            slider.wholeNumbers = true;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            return slider;
        }
    }
}
