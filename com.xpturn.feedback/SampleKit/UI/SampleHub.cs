using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace xpTURN.Feedback.Samples
{
    // the hub: the imported bundles' cards as buttons (made in code), opened without the build list in the editor
    // (LoadSceneInPlayMode) and by name in a player. A gamepad starts on the first available card; Automatic navigation
    // stays among the cards (the HUD's widgets take no navigation, a card that is not available is not interactable)
    public sealed class SampleHub : MonoBehaviour
    {
        public static SampleHub Instance { get; private set; }
        // a column's cards before the hub turns to two
        internal const int ColumnCards = 12;

        readonly List<SampleCard> m_Cards = new List<SampleCard>();
        readonly List<Button> m_Buttons = new List<Button>();
        [SerializeField] GameObject m_Root;
        [System.NonSerialized] InputAction m_Navigate;

        public IReadOnlyList<SampleCard> Cards => m_Cards;

        public static bool HubReachable
        {
            get
            {
#if UNITY_EDITOR
                return true;
#else
                return Application.CanStreamedLevelBeLoaded(SampleKitPaths.HubName);
#endif
            }
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // OnEnable also after a recompile during play: the buttons' listeners are runtime only
        void OnEnable()
        {
            Instance = this;
            SampleCards.Gather(m_Cards);
            Build();
        }

        void Start()
        {
            SampleState.Restore();
            SampleHud.Ensure();
            SelectFirst();
        }

        void Update()
        {
            if (m_Navigate == null && SampleInput.Asset != null)
                m_Navigate = SampleInput.Asset.FindAction("UI/Navigate");
            EventSystem es = EventSystem.current;
            if (es != null && !IsCard(es.currentSelectedGameObject) && m_Navigate != null && m_Navigate.ReadValue<Vector2>().sqrMagnitude > 0.25f)
                SelectFirst();
        }

        bool IsCard(GameObject go)
        {
            if (go == null)
                return false;
            foreach (Button b in m_Buttons)
                if (b.gameObject == go)
                    return true;
            return false;
        }

        void SelectFirst()
        {
            EventSystem es = EventSystem.current;
            if (es == null)
                return;
            foreach (Button b in m_Buttons)
                if (b.interactable)
                {
                    es.SetSelectedGameObject(b.gameObject);
                    return;
                }
        }

        public static void Open(SampleCard card)
        {
            if (!SampleCards.Available(card))
                return;
            SampleState.Restore();
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(UnityEditor.AssetDatabase.GUIDToAssetPath(card.SceneGuid), new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(card.SceneName);
#endif
        }

        // the editor leaves play; the editor assembly opens the scene and selects the card
        public static void OpenInEditor(SampleCard card)
        {
#if UNITY_EDITOR
            string cardGuid = UnityEditor.AssetDatabase.AssetPathToGUID(UnityEditor.AssetDatabase.GetAssetPath(card));
            UnityEditor.SessionState.SetString(SampleKitPaths.OpenInEditorKey, UnityEditor.AssetDatabase.GUIDToAssetPath(card.SceneGuid) + "|" + cardGuid);
            UnityEditor.EditorApplication.ExitPlaymode();
#endif
        }

        public static void BackToHub()
        {
            if (!HubReachable)
                return;
            SampleState.Restore();
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(SampleKitPaths.HubScene, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(SampleKitPaths.HubName);
#endif
        }

        void Build()
        {
            if (m_Root != null)
                Destroy(m_Root);
            m_Buttons.Clear();
            m_Root = new GameObject("Hub Canvas", typeof(RectTransform));
            m_Root.transform.SetParent(transform, false);
            var canvas = m_Root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = m_Root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            m_Root.AddComponent<GraphicRaycaster>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Text title = Line(m_Root.transform, font, new Vector2(0f, 420f), new Vector2(900f, 60f), 36);
            title.text = "xpTURN.Feedback Samples";
            if (m_Cards.Count == 0)
            {
                Line(m_Root.transform, font, new Vector2(0f, 300f), new Vector2(1200f, 40f), 22).text = "No sample bundles imported — Window > Package Manager > xpTURN.Feedback > Samples";
                return;
            }
            // one column up to twelve cards; past that two halves, starting below the HUD's panel (at 21:9 a column holds nine at
            // 64 — past nine its rows close up)
            int columns = m_Cards.Count > ColumnCards ? 2 : 1;
            int perColumn = (m_Cards.Count + columns - 1) / columns;
            float width = columns == 1 ? 900f : 800f;
            float top = columns == 1 ? 300f : 100f;
            float step = columns == 1 ? 64f : Mathf.Min(64f, 545f / Mathf.Max(1, perColumn - 1));
            for (int i = 0; i < m_Cards.Count; i++)
            {
                SampleCard card = m_Cards[i];
                int column = i / perColumn, row = i % perColumn;
                bool available = SampleCards.Available(card);
                var go = new GameObject(card.Title, typeof(RectTransform));
                go.transform.SetParent(m_Root.transform, false);
                var rt = (RectTransform)go.transform;
                rt.anchoredPosition = new Vector2(columns == 1 ? 0f : (column == 0 ? -410f : 410f), top - row * step);
                rt.sizeDelta = new Vector2(width, 52f);
                Image image = go.AddComponent<Image>();
                image.color = available ? new Color(0.2f, 0.3f, 0.45f, 0.9f) : new Color(0.25f, 0.25f, 0.25f, 0.6f);
                var button = go.AddComponent<Button>();
                button.targetGraphic = image;
                button.interactable = available;
                button.onClick.AddListener(() => Open(card));
                Text label = Line(go.transform, font, Vector2.zero, new Vector2(width - 20f, 48f), 22);
                label.text = available ? card.Title : card.Title + " — requires " + card.RequiresText;
                m_Buttons.Add(button);
#if UNITY_EDITOR
                if (card.OpenInEditor && available)
                    EditorButton(go.transform, font, card, columns == 1 ? 560f : width * 0.5f - 110f);
#endif
            }
        }

#if UNITY_EDITOR
        // mouse only (Navigation None, not a card): the hub's pad stays among the cards
        static void EditorButton(Transform row, Font font, SampleCard card, float x)
        {
            var go = new GameObject("Open in Editor", typeof(RectTransform));
            go.transform.SetParent(row, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(200f, 44f);
            Image image = go.AddComponent<Image>();
            image.color = new Color(0.3f, 0.4f, 0.3f, 0.9f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => OpenInEditor(card));
            Line(go.transform, font, Vector2.zero, new Vector2(190f, 40f), 18).text = "Open in Editor";
        }
#endif

        static Text Line(Transform parent, Font font, Vector2 at, Vector2 size, int fontSize)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = at;
            rt.sizeDelta = size;
            Text text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }
    }
}
