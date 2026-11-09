using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.GameFeelLabs
{
    // S4 Combat Hit Lab: Action1 · 2 · 3 hit the dummy light · heavy · critical. The tier's preset plays as it is (the presets carry
    // the tiers); only the damage number takes the damage — intensity = damage / 100 with the preset's ×100 (a raw damage would
    // grow every item of a player). The numbers live in a prefab made after the TMP check; the dummy faces the camera so the
    // critical's local −Z punch pushes it back
    public sealed class CombatHitLab : MonoBehaviour
    {
        public const int Light = 0, Heavy = 1, Critical = 2;
        static readonly Vector2Int[] s_Damage = { new Vector2Int(8, 14), new Vector2Int(22, 34), new Vector2Int(60, 90) };
        // a test fixes a tier's damage here
        internal static System.Func<int, int> s_DamageOf;

        [SerializeField] FeedbackPlayer m_Light;
        [SerializeField] FeedbackPlayer m_Heavy;
        [SerializeField] FeedbackPlayer m_Critical;
        [SerializeField] FeedbackPlayer m_Rumble;
        [SerializeField] GameObject m_NumbersPrefab;
        [SerializeField] Transform m_Chest;
        [SerializeField] float m_Jitter = 0.15f;

        [System.NonSerialized] InputAction[] m_Attacks;
        GameObject m_Numbers;
        FeedbackPlayer m_Number;
        FeedbackPlayer m_NumberCritical;
        GameObject m_Notice;

        void OnEnable()
        {
            if (SampleTmp.Ready && m_NumbersPrefab != null && m_Numbers == null)
            {
                m_Numbers = Instantiate(m_NumbersPrefab, transform, false);
                m_Number = Child("Number");
                m_NumberCritical = Child("NumberCritical");
            }
        }

        void Start()
        {
            if (SampleTmp.Ready)
                return;
            SampleHud.Notify(SampleTmp.Notice);
            m_Notice = new GameObject("TMP Notice", typeof(RectTransform));
            m_Notice.transform.SetParent(transform, false);
            var canvas = m_Notice.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = m_Notice.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            var line = new GameObject("Line", typeof(RectTransform));
            line.transform.SetParent(m_Notice.transform, false);
            var rt = (RectTransform)line.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 60f);
            rt.sizeDelta = new Vector2(1400f, 40f);
            Text text = line.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = SampleTmp.Notice;
        }

        void OnDestroy()
        {
            if (m_Notice != null)
                Destroy(m_Notice);
        }

        FeedbackPlayer Child(string name)
        {
            Transform t = m_Numbers.transform.Find(name);
            return t != null ? t.GetComponent<FeedbackPlayer>() : null;
        }

        void Update()
        {
            if (m_Attacks == null)
                m_Attacks = new[] { SampleInput.Find("Action1"), SampleInput.Find("Action2"), SampleInput.Find("Action3") };
            for (int tier = 0; tier < 3; tier++)
                if (m_Attacks[tier] != null && m_Attacks[tier].WasPressedThisFrame() && !(FromPointer(m_Attacks[tier]) && SampleInput.PointerOverUI))
                    Attack(tier);
        }

        static bool FromPointer(InputAction a) => a.activeControl != null && a.activeControl.device is Pointer;

        public void Attack(int tier)
        {
            tier = Mathf.Clamp(tier, Light, Critical);
            FeedbackPlayer hit = tier == Light ? m_Light : tier == Heavy ? m_Heavy : m_Critical;
            if (hit != null)
                hit.Play();
            if (tier != Light && m_Rumble != null)
                m_Rumble.Play(new PlayArgs().WithIntensity(tier == Critical ? 1f : 0.6f));
            int damage = s_DamageOf != null ? s_DamageOf(tier) : Random.Range(s_Damage[tier].x, s_Damage[tier].y + 1);
            FeedbackPlayer number = tier == Critical ? m_NumberCritical : m_Number;
            if (number != null && m_Chest != null)
                number.Play(new PlayArgs().WithIntensity(damage / 100f).WithPosition(m_Chest.position + Random.insideUnitSphere * m_Jitter));
        }
    }
}
