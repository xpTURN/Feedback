using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.UI
{
    // S7 UI Juice: a shop screen — three items (hover · buy · denied), an HP bar and an XP bar (ProgressBar — delayed followers), a
    // level-up reward popup (fade · pop · a TMP reveal and count-up) and a pause (the sample's own time scale 0). Every UI player runs
    // on the unscaled clock, so the screen keeps moving while paused — the Scaled ornament and the cube stop. Before plays nothing:
    // the bars jump and the popup shows its end at once (Play + Skip). The buttons call the public methods (the scene's listeners).
    // A pad finds its way back: a navigation with no selection of this screen picks the last one (the hub's rule)
    public sealed class UIJuiceLab : MonoBehaviour
    {
        public const int MaxGold = 2000;
        public const int Reward = 250;
        const string PauseText = "Pause", ResumeText = "Resume";
        const string FixedUpdateNotice = "The Input System processes events in FixedUpdate — a time scale of 0 would stop all input, so the pause is off";
        static readonly string[] s_Gold = new string[MaxGold / 10 + 1];
        static readonly string[] s_Hp = new string[101];
        static readonly string[] s_Xp = new string[101];
        static readonly string[] s_Level = new string[100];

        [SerializeField] Button[] m_Items = new Button[0];
        [SerializeField] int[] m_Prices = { 40, 90, 300 };
        [SerializeField] FeedbackPlayer[] m_Press = new FeedbackPlayer[0];
        [SerializeField] FeedbackPlayer[] m_Denied = new FeedbackPlayer[0];
        [SerializeField] Text m_Gold;
        [SerializeField] FeedbackPlayer m_GoldPop;
        [SerializeField] ProgressBar m_Hp;
        [SerializeField] Text m_HpText;
        [SerializeField] FeedbackPlayer m_Hurt;
        [SerializeField] FeedbackPlayer m_HpDenied;
        [SerializeField] ProgressBar m_Xp;
        [SerializeField] Text m_XpText;
        [SerializeField] Text m_LevelText;
        [SerializeField] FeedbackPlayer m_XpDenied;
        [SerializeField] CanvasGroup m_Popup;
        [SerializeField] FeedbackPlayer m_PopupShow;
        [SerializeField] FeedbackPlayer m_PopupHide;
        [SerializeField] Transform m_PopupTmpSlot;
        [SerializeField] GameObject m_PopupTmpPrefab;
        [SerializeField] GameObject m_PopupTitle;
        [SerializeField] Button m_PopupClose;
        [SerializeField] float m_PopupSeconds = 2.5f;
        [SerializeField] Text m_PauseLabel;
        [SerializeField] CanvasGroup m_PausedBanner;
        [SerializeField] FeedbackPlayer m_PausedPulse;
        [SerializeField] GameObject m_TmpNotice;
        [SerializeField] Transform m_Cube;
        [SerializeField] Transform m_Screen;

        [System.NonSerialized] InputAction m_PauseAction;
        [System.NonSerialized] InputAction m_Navigate;
        int m_GoldValue = 120;
        int m_HpValue = 100;
        int m_XpValue;
        int m_LevelValue = 1;
        bool m_Paused;
        float m_PopupLeft;
        GameObject m_PopupTmp;
        FeedbackPlayer m_PopupReveal;
        GameObject m_Before;
        GameObject m_LastSelected;

        public int Gold => m_GoldValue;
        public int Hp => m_HpValue;
        public int Xp => m_XpValue;
        public int Level => m_LevelValue;
        public bool Paused => m_Paused;
        public bool PopupOpen => m_PopupLeft > 0f;

        static UIJuiceLab()
        {
            for (int i = 0; i < s_Gold.Length; i++)
                s_Gold[i] = "Gold " + i * 10;
            for (int i = 0; i < s_Hp.Length; i++)
                s_Hp[i] = "HP " + i + " / 100";
            for (int i = 0; i < s_Xp.Length; i++)
                s_Xp[i] = "XP " + i + " %";
            for (int i = 0; i < s_Level.Length; i++)
                s_Level[i] = "Lv " + i;
        }

        void OnEnable()
        {
            if (SampleTmp.Ready && m_PopupTmpPrefab != null && m_PopupTmp == null && m_PopupTmpSlot != null)
            {
                m_PopupTmp = Instantiate(m_PopupTmpPrefab, m_PopupTmpSlot, false);
                m_PopupReveal = m_PopupTmp.GetComponent<FeedbackPlayer>();
                if (m_PopupTitle != null)
                    m_PopupTitle.SetActive(false);
            }
        }

        void Start()
        {
            if (m_Items.Length > 0 && m_Items[0] != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(m_Items[0].gameObject);
            Refresh();
            if (SampleTmp.Ready)
                return;
            SampleHud.Notify(SampleTmp.Notice);
            if (m_TmpNotice != null)
                m_TmpNotice.SetActive(true);
        }

        void OnDestroy()
        {
            if (m_PopupTmp != null)
                Destroy(m_PopupTmp);
        }

        void Update()
        {
            if (m_PauseAction == null)
                m_PauseAction = SampleInput.Find("Action3");
            if (m_Navigate == null && SampleInput.Asset != null)
                m_Navigate = SampleInput.Asset.FindAction("UI/Navigate");
            if (m_PauseAction != null && m_PauseAction.WasPressedThisFrame())
                TogglePause();
            KeepSelection();
            // the banner's pulse while paused in After — a play started in Before has no items and waits here
            if (m_PausedPulse != null && m_Paused && SampleState.After && !m_PausedPulse.IsPlaying)
                m_PausedPulse.Play();
            if (m_PopupLeft > 0f)
            {
                m_PopupLeft -= Time.unscaledDeltaTime;
                if (m_PopupLeft <= 0f)
                    CloseReward();
            }
            if (m_Cube != null)
                m_Cube.Rotate(20f * Time.deltaTime, 60f * Time.deltaTime, 0f, Space.World);
        }

        // a play only in After — Before shows the same state with no feedback
        static void Play(FeedbackPlayer p)
        {
            if (p != null && SampleState.After)
                p.Play();
        }

        static void Play(FeedbackPlayer p, float intensity)
        {
            if (p != null && SampleState.After)
                p.Play(PlayArgs.Default.WithIntensity(intensity));
        }

        // the popup's players reach their end at once in Before; the other side's Tween stops where it is first (both write the alpha)
        static void Show(FeedbackPlayer p, FeedbackPlayer other)
        {
            if (p == null)
                return;
            if (other != null)
                other.Stop(StopMode.Hold);
            p.Play();
            if (!SampleState.After)
                p.Skip();
        }

        void KeepSelection()
        {
            EventSystem es = EventSystem.current;
            if (es == null)
                return;
            GameObject now = es.currentSelectedGameObject;
            if (now != null && m_Screen != null && now.transform.IsChildOf(m_Screen))
            {
                m_LastSelected = now;
                return;
            }
            if (m_Navigate == null || m_Navigate.ReadValue<Vector2>().sqrMagnitude <= 0.25f)
                return;
            Selectable last = m_LastSelected != null ? m_LastSelected.GetComponent<Selectable>() : null;
            GameObject pick = last != null && last.isActiveAndEnabled && last.IsInteractable() ? last.gameObject : m_Items.Length > 0 && m_Items[0] != null ? m_Items[0].gameObject : null;
            if (pick != null)
                es.SetSelectedGameObject(pick);
        }

        static void Bar(ProgressBar bar, int value)
        {
            if (bar == null)
                return;
            if (SampleState.After)
                bar.SetValue(value / 100f);
            else
                bar.SetValueImmediate(value / 100f);
        }

        public void Buy(int item)
        {
            if (item < 0 || item >= m_Prices.Length)
                return;
            if (m_GoldValue < m_Prices[item])
            {
                Play(item < m_Denied.Length ? m_Denied[item] : null);
                return;
            }
            m_GoldValue -= m_Prices[item];
            Play(item < m_Press.Length ? m_Press[item] : null);
            Play(m_GoldPop);
            Refresh();
        }

        public void Hit(int damage)
        {
            if (m_HpValue == 0)
            {
                Play(m_HpDenied);
                return;
            }
            m_HpValue = Mathf.Max(0, m_HpValue - damage);
            Bar(m_Hp, m_HpValue);
            Play(m_Hurt, damage / 15f);
            Refresh();
        }

        public void Heal(int amount)
        {
            if (m_HpValue == 100)
            {
                Play(m_HpDenied);
                return;
            }
            m_HpValue = Mathf.Min(100, m_HpValue + amount);
            Bar(m_Hp, m_HpValue);
            Refresh();
        }

        public void AddXp(int amount)
        {
            if (PopupOpen)
            {
                Play(m_XpDenied);
                return;
            }
            m_XpValue = Mathf.Min(100, m_XpValue + amount);
            Bar(m_Xp, m_XpValue);
            if (m_XpValue == 100)
                OpenReward();
            Refresh();
        }

        void OpenReward()
        {
            m_LevelValue = Mathf.Min(s_Level.Length - 1, m_LevelValue + 1);
            m_GoldValue = Mathf.Min(MaxGold, m_GoldValue + Reward);
            m_PopupLeft = m_PopupSeconds;
            if (m_Popup != null)
            {
                m_Popup.interactable = true;
                m_Popup.blocksRaycasts = true;
            }
            Show(m_PopupShow, m_PopupHide);
            Show(m_PopupReveal, null);
            Play(m_GoldPop);
            if (EventSystem.current != null && m_PopupClose != null)
            {
                m_Before = EventSystem.current.currentSelectedGameObject;
                EventSystem.current.SetSelectedGameObject(m_PopupClose.gameObject);
            }
        }

        public void CloseReward()
        {
            if (m_Popup == null || !m_Popup.interactable)
                return;
            m_PopupLeft = 0f;
            m_Popup.interactable = false;
            m_Popup.blocksRaycasts = false;
            Show(m_PopupHide, m_PopupShow);
            m_XpValue = 0;
            if (m_Xp != null)
                m_Xp.SetValueImmediate(0f);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(m_Before != null ? m_Before : m_Items.Length > 0 && m_Items[0] != null ? m_Items[0].gameObject : null);
            Refresh();
        }

        public void TogglePause()
        {
            if (!m_Paused && InputSystem.settings.updateMode == InputSettings.UpdateMode.ProcessEventsInFixedUpdate)
            {
                SampleHud.Notify(FixedUpdateNotice);
                return;
            }
            m_Paused = !m_Paused;
            SampleState.SetSampleTimeScale(m_Paused ? 0f : 1f);
            if (m_PausedBanner != null)
                m_PausedBanner.alpha = m_Paused ? 1f : 0f;
            if (m_PausedPulse != null && !m_Paused)
                m_PausedPulse.Stop(StopMode.Revert);
            Refresh();
        }

        void Refresh()
        {
            Set(m_Gold, s_Gold[Mathf.Clamp(m_GoldValue / 10, 0, s_Gold.Length - 1)]);
            Set(m_HpText, s_Hp[m_HpValue]);
            Set(m_XpText, s_Xp[m_XpValue]);
            Set(m_LevelText, s_Level[m_LevelValue]);
            Set(m_PauseLabel, m_Paused ? ResumeText : PauseText);
        }

        static void Set(Text t, string s)
        {
            if (t != null && !ReferenceEquals(t.text, s))
                t.text = s;
        }
    }
}
