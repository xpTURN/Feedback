using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.Basics
{
    // How a gallery entry leaves its target when the play ends
    public enum GalleryReset
    {
        None = 0,
        Snapshot = 1,   // the target's pose · active state · body velocity as before the play
        Clear = 2,      // another player takes the shared cover back (Screen Fade)
        Respawn = 3,    // a Tween · Set left its end on the child 'Subject' — a copy of the hidden 'Template' becomes it, the player rebuilds
        Globals = 4,    // a Tween · Set on a global or the camera — the scene's values come back
    }

    [Serializable]
    public sealed class GalleryEntry
    {
        public string Tab;
        public string Label;
        [TextArea] public string Note;
        public FeedbackPlayer Player;
        public string PlayerPath;       // a TMP entry: the player's path in the TMP stage made after the resources check
        public GameObject Target;       // shown alone on the stage while its entry plays
        public GalleryReset Reset;
        public FeedbackPlayer Clear;
        public bool NeedsTmp;
    }

    // S2 Feedback Gallery: category tabs → one button per feedback type (the add menu's motion, at least 0.25 s), a Compare tab
    // (the same cube, every motion), a Presets tab (the package's defaults, linked) and the excluded tiles. The players are made in
    // the editor — the public API plays them. The UI is made in code on enable (its listeners are runtime only); the frame loop
    // writes nothing once made
    public sealed class FeedbackGallery : MonoBehaviour
    {
        const float StopAfter = 2f;
        const float SetShownFor = 1.6f;

        [SerializeField] string[] m_Tabs = new string[0];
        [SerializeField] GalleryEntry[] m_Entries = new GalleryEntry[0];
        [SerializeField] GameObject m_TmpStagePrefab;
        [SerializeField] Transform m_TmpStageParent;
        [SerializeField] string[] m_ShaderGlobals = new string[0];

        GameObject m_Root;
        RectTransform m_Grid;
        Text m_Info;
        readonly List<Button> m_TabButtons = new List<Button>();
        readonly List<GameObject> m_EntryButtons = new List<GameObject>();
        static readonly List<FeedbackPlayer> s_StagePlayers = new List<FeedbackPlayer>();
        GameObject m_TmpStage;
        GalleryEntry m_Current;
        int m_Tab;
        // the scene's values a Globals entry puts back
        Color m_Ambient, m_Fog, m_Background;
        float m_AmbientIntensity, m_FogDensity, m_Fov, m_Near, m_Far, m_Ortho;

        public IReadOnlyList<GalleryEntry> Entries => m_Entries;
        public IReadOnlyList<string> Tabs => m_Tabs;

        void OnEnable()
        {
            m_Ambient = RenderSettings.ambientLight;
            m_AmbientIntensity = RenderSettings.ambientIntensity;
            m_Fog = RenderSettings.fogColor;
            m_FogDensity = RenderSettings.fogDensity;
            Camera main = Camera.main;
            if (main != null)
            {
                m_Fov = main.fieldOfView;
                m_Near = main.nearClipPlane;
                m_Far = main.farClipPlane;
                m_Ortho = main.orthographicSize;
                m_Background = main.backgroundColor;
            }
            if (SampleTmp.Ready && m_TmpStagePrefab != null && m_TmpStage == null)
                m_TmpStage = Instantiate(m_TmpStagePrefab, m_TmpStageParent, false);
            foreach (GalleryEntry e in m_Entries)
                if (e.Target != null)
                    e.Target.SetActive(false);
            Build();
            ShowTab(0);
        }

        void OnDisable()
        {
            if (m_Root != null)
                Destroy(m_Root);
        }

        public FeedbackPlayer PlayerOf(GalleryEntry e)
        {
            if (e.Player != null)
                return e.Player;
            if (m_TmpStage == null || string.IsNullOrEmpty(e.PlayerPath))
                return null;
            Transform t = m_TmpStage.transform.Find(e.PlayerPath);
            return t != null ? t.GetComponent<FeedbackPlayer>() : null;
        }

        public bool Available(GalleryEntry e) => !e.NeedsTmp || SampleTmp.Ready;

        // what the stage shows for an entry: its target, else its player's object (a TMP entry's — in the stage made at enable)
        public GameObject TargetOf(GalleryEntry e)
        {
            if (e.Target != null)
                return e.Target;
            FeedbackPlayer p = e.PlayerPath != "" ? PlayerOf(e) : null;
            return p != null ? p.gameObject : null;
        }

        // a button's press: the entry's target alone on the stage, its player played; the reset when the play ends
        public void Press(GalleryEntry e)
        {
            if (!Available(e))
            {
                SampleHud.Notify(SampleTmp.Notice);
                m_Info.text = SampleTmp.Notice;
                return;
            }
            FeedbackPlayer player = PlayerOf(e);
            if (player == null)
                return;
            GameObject previous = m_Current != null && m_Current != e ? TargetOf(m_Current) : null;
            GameObject target = TargetOf(e);
            if (previous != null && previous != target)
                previous.SetActive(false);
            m_Current = e;
            if (target != null)
                target.SetActive(true);
            m_Info.text = e.Note;
            StartCoroutine(PlayAndReset(e, player));
        }

        IEnumerator PlayAndReset(GalleryEntry e, FeedbackPlayer player)
        {
            GameObject target = TargetOf(e);
            Transform t = target != null ? target.transform : player.transform;
            Vector3 position = t.localPosition, scale = t.localScale;
            Quaternion rotation = t.localRotation;
            player.Play();
            float until = Time.unscaledTime + StopAfter;
            float shown = Time.unscaledTime + SetShownFor;
            yield return null;
            // a Set plays at once — its end stays on the stage a moment before a Respawn takes it back
            bool instant = player != null && !player.IsPlaying;
            while (player != null && player.IsPlaying && Time.unscaledTime < until)
                yield return null;
            if (player != null && player.IsPlaying)
                player.Stop(StopMode.Revert);
            switch (e.Reset)
            {
                case GalleryReset.Snapshot:
                    if (t != null)
                    {
                        t.localPosition = position;
                        t.localRotation = rotation;
                        t.localScale = scale;
                        t.gameObject.SetActive(true);
                        if (t.TryGetComponent(out Rigidbody body))
                        {
                            body.linearVelocity = Vector3.zero;
                            body.angularVelocity = Vector3.zero;
                        }
                        if (t.TryGetComponent(out Rigidbody2D body2D))
                        {
                            body2D.linearVelocity = Vector2.zero;
                            body2D.angularVelocity = 0f;
                        }
                    }
                    break;
                case GalleryReset.Clear:
                    yield return new WaitForSecondsRealtime(0.5f);
                    if (e.Clear != null)
                        e.Clear.Play();
                    break;
                case GalleryReset.Respawn:
                    while (instant && Time.unscaledTime < shown)
                        yield return null;
                    if (player != null)
                        Respawn(player.transform);
                    // every player of the stage held the old subject (Compare's twenty-one share one cube)
                    if (player != null)
                    {
                        player.GetComponents(s_StagePlayers);
                        foreach (FeedbackPlayer p in s_StagePlayers)
                            p.Rebuild();
                        s_StagePlayers.Clear();
                    }
                    break;
                case GalleryReset.Globals:
                    PutGlobalsBack();
                    break;
            }
        }

        // the old subject renamed before its Destroy (the end of the frame) — the rebuild finds the new one
        static void Respawn(Transform op)
        {
            Transform template = op.Find("Template");
            Transform old = FindDeep(op, "Subject");
            if (old != null)
            {
                old.name = "Subject (gone)";
                Destroy(old.gameObject);
            }
            if (template == null)
                return;
            GameObject copy = Instantiate(template.gameObject, op, false);
            copy.name = "Subject";
            copy.SetActive(true);
        }

        static Transform FindDeep(Transform t, string name)
        {
            foreach (Transform child in t)
            {
                if (child.name == name)
                    return child;
                Transform deeper = FindDeep(child, name);
                if (deeper != null)
                    return deeper;
            }
            return null;
        }

        void PutGlobalsBack()
        {
            RenderSettings.ambientLight = m_Ambient;
            RenderSettings.ambientIntensity = m_AmbientIntensity;
            RenderSettings.fogColor = m_Fog;
            RenderSettings.fogDensity = m_FogDensity;
            Camera main = Camera.main;
            if (main != null)
            {
                main.fieldOfView = m_Fov;
                main.nearClipPlane = m_Near;
                main.farClipPlane = m_Far;
                main.orthographicSize = m_Ortho;
                main.backgroundColor = m_Background;
            }
            // the three globals in order: a color · a float · a vector (a name keeps the type it was first given)
            if (m_ShaderGlobals.Length > 0)
                Shader.SetGlobalColor(m_ShaderGlobals[0], Color.clear);
            if (m_ShaderGlobals.Length > 1)
                Shader.SetGlobalFloat(m_ShaderGlobals[1], 0f);
            if (m_ShaderGlobals.Length > 2)
                Shader.SetGlobalVector(m_ShaderGlobals[2], Vector4.zero);
        }

        void ShowTab(int tab)
        {
            m_Tab = Mathf.Clamp(tab, 0, Mathf.Max(0, m_Tabs.Length - 1));
            foreach (GameObject b in m_EntryButtons)
                Destroy(b);
            m_EntryButtons.Clear();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            int i = 0;
            foreach (GalleryEntry e in m_Entries)
            {
                if (m_Tabs.Length == 0 || e.Tab != m_Tabs[m_Tab])
                    continue;
                bool usable = (e.Player != null || !string.IsNullOrEmpty(e.PlayerPath)) && Available(e);
                GalleryEntry entry = e;
                Button b = MakeButton(m_Grid, font, e.Label, new Vector2(i % 4 * 236f, -(i / 4) * 44f), new Vector2(228f, 38f), () => Press(entry));
                b.interactable = usable;
                m_EntryButtons.Add(b.gameObject);
                i++;
            }
            for (int k = 0; k < m_TabButtons.Count; k++)
                m_TabButtons[k].GetComponent<Image>().color = k == m_Tab ? new Color(0.3f, 0.45f, 0.65f, 0.95f) : new Color(0.15f, 0.2f, 0.3f, 0.9f);
            m_Info.text = m_Tabs.Length > 0 && m_Tabs[m_Tab] == "TMP" && !SampleTmp.Ready ? SampleTmp.Notice : "Pick a feedback — it plays on the object in the middle.";
            if (m_EntryButtons.Count > 0 && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(m_EntryButtons[0]);
        }

        void Build()
        {
            if (m_Root != null)
                Destroy(m_Root);
            m_TabButtons.Clear();
            m_EntryButtons.Clear();
            m_Root = new GameObject("Gallery Canvas", typeof(RectTransform));
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
            var tabs = Rect(m_Root.transform, "Tabs", new Vector2(0f, 1f), new Vector2(600f, -16f), new Vector2(1300f, 80f));
            for (int k = 0; k < m_Tabs.Length; k++)
            {
                int tab = k;
                m_TabButtons.Add(MakeButton(tabs, font, m_Tabs[k], new Vector2(k % 11 * 118f, -(k / 11) * 40f), new Vector2(112f, 34f), () => ShowTab(tab)));
            }
            m_Grid = Rect(m_Root.transform, "Buttons", new Vector2(0f, 1f), new Vector2(600f, -110f), new Vector2(960f, 560f));
            m_Info = MakeText(Rect(m_Root.transform, "Info", new Vector2(0f, 0f), new Vector2(600f, 120f), new Vector2(1300f, 90f)), font, 20);
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

        static Button MakeButton(RectTransform parent, Font font, string label, Vector2 at, Vector2 size, UnityEngine.Events.UnityAction click)
        {
            RectTransform rt = Rect(parent, label, new Vector2(0f, 1f), at, size);
            Image image = rt.gameObject.AddComponent<Image>();
            image.color = new Color(0.15f, 0.2f, 0.3f, 0.9f);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(click);
            Text text = MakeText(Rect(rt, "Text", new Vector2(0f, 1f), new Vector2(8f, -4f), new Vector2(size.x - 16f, size.y - 8f)), font, 17);
            text.alignment = TextAnchor.MiddleLeft;
            text.text = label;
            return button;
        }
    }
}
