using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.Advanced
{
    // S12 Accessibility: a settings menu over a small fight — screen shake (the Camera category's scale), motion (Motion's scale),
    // flashes (the per-second limit, or the Flash category off), hit stop (Time on · off), rumble (Haptics on · off). The kit is the
    // one writer: SampleState composes these with the HUD's Before and intensity, and puts the scene's values back when the sample
    // closes (session only). The settings are written from Start — the scene marker's Awake restores the kit first. The panel under
    // the menu reads the effective values back from FeedbackCategories (the HUD's line shows the project's values only)
    public sealed class AccessibilityLab : MonoBehaviour
    {
        public const int Shake = 0, Motion = 1, Flashes = 2, HitStop = 3, Rumble = 4, Rows = 5;
        public const int Light = 0, Heavy = 1, Critical = 2;
        static readonly float[] s_Shake = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        static readonly float[] s_Motion = { 0f, 0.5f, 1f };
        static readonly string[][] s_Labels =
        {
            new[] { "0 %", "25 %", "50 %", "75 %", "100 %" },
            new[] { "0 %", "50 %", "100 %" },
            new[] { "Normal", "Reduced (1 / s)", "Off" },
            new[] { "On", "Off" },
            new[] { "On", "Off" },
        };
        static readonly string[] s_Scale = Scales();
        static readonly string[] s_Limit = { "0 / s", "1 / s", "2 / s", "3 / s", "4 / s", "5 / s", "6 / s", "7 / s", "8 / s", "9 / s", "10+ / s" };

        [SerializeField] FeedbackPlayer m_Light;
        [SerializeField] FeedbackPlayer m_Heavy;
        [SerializeField] FeedbackPlayer m_Critical;
        [SerializeField] FeedbackPlayer m_Rumble;
        [SerializeField] Transform m_Dummy;
        [SerializeField] Transform m_Witness;
        [SerializeField] float m_WitnessSpeed = 90f;
        [SerializeField] Text[] m_Values = new Text[0];
        [SerializeField] GameObject[] m_Frames = new GameObject[0];
        [SerializeField] Text m_CameraScale;
        [SerializeField] Text m_MotionScale;
        [SerializeField] Text m_FlashLimit;
        [SerializeField] Text m_TimeOn;
        [SerializeField] Text m_HapticsOn;

        readonly int[] m_Choice = { 4, 2, 0, 0, 0 };
        [NonSerialized] InputAction[] m_Attacks;
        [NonSerialized] InputAction m_Up, m_Down, m_Less, m_More;
        int m_Row;
        int m_Refresh;
        // the project's flash limit (read after the scene marker restored the kit) — Normal keeps it, Reduced is at most 1
        int m_BaseLimit = 3;

        internal int Choice(int row) => m_Choice[Mathf.Clamp(row, 0, Rows - 1)];
        internal int Row => m_Row;

        static string[] Scales()
        {
            var s = new string[201];
            for (int i = 0; i < s.Length; i++)
                s[i] = (i / 100f).ToString("0.00");
            return s;
        }

        void Start()
        {
            m_BaseLimit = FeedbackCategories.FlashLimit;
            Apply();
            ShowMenu();
        }

        void Update()
        {
            if (m_Attacks == null)
            {
                m_Attacks = new[] { SampleInput.Find("Action1"), SampleInput.Find("Action2"), SampleInput.Find("Action3") };
                m_Up = SampleInput.Find("Option4");
                m_Down = SampleInput.Find("Option3");
                m_Less = SampleInput.Find("Option1");
                m_More = SampleInput.Find("Option2");
            }
            for (int tier = 0; tier < 3; tier++)
                if (m_Attacks[tier] != null && m_Attacks[tier].WasPressedThisFrame() && !(FromPointer(m_Attacks[tier]) && SampleInput.PointerOverUI))
                    Attack(tier);
            if (m_Up != null && m_Up.WasPressedThisFrame())
                Select(m_Row - 1);
            if (m_Down != null && m_Down.WasPressedThisFrame())
                Select(m_Row + 1);
            if (m_Less != null && m_Less.WasPressedThisFrame())
                Step(m_Row, -1);
            if (m_More != null && m_More.WasPressedThisFrame())
                Step(m_Row, 1);
            if (m_Witness != null)
                m_Witness.Rotate(0f, m_WitnessSpeed * Time.deltaTime, 0f, Space.World);
            // the HUD's Before and intensity change the effective values too
            if (++m_Refresh % 15 == 0)
                ShowEffective();
        }

        static bool FromPointer(InputAction a) => a.activeControl != null && a.activeControl.device is Pointer;

        public void Attack(int tier)
        {
            tier = Mathf.Clamp(tier, Light, Critical);
            FeedbackPlayer hit = tier == Light ? m_Light : tier == Heavy ? m_Heavy : m_Critical;
            Vector3 at = m_Dummy != null ? m_Dummy.position : Vector3.zero;
            if (hit != null)
                hit.Play(new PlayArgs().WithPosition(at));
            if (tier != Light && m_Rumble != null)
                m_Rumble.Play(new PlayArgs().WithIntensity(tier == Critical ? 1f : 0.6f));
        }

        public void Select(int row)
        {
            m_Row = (row + Rows) % Rows;
            ShowMenu();
        }

        // a value button: −1 · +1 on a row
        public void Less(int row) => Step(row, -1);

        public void More(int row) => Step(row, 1);

        public void Step(int row, int by)
        {
            row = Mathf.Clamp(row, 0, Rows - 1);
            m_Row = row;
            int n = s_Labels[row].Length;
            m_Choice[row] = Mathf.Clamp(m_Choice[row] + by, 0, n - 1);
            Apply();
            ShowMenu();
        }

        void Apply()
        {
            float shake = s_Shake[m_Choice[Shake]];
            SampleState.SetCategory(FeedbackCategory.Camera, shake > 0f, shake);
            float motion = s_Motion[m_Choice[Motion]];
            SampleState.SetCategory(FeedbackCategory.Motion, motion > 0f, motion);
            int flashes = m_Choice[Flashes];
            SampleState.SetCategory(FeedbackCategory.Flash, flashes != 2, 1f);
            SampleState.SetFlashLimit(flashes == 1 ? Mathf.Min(1, m_BaseLimit) : m_BaseLimit);
            SampleState.SetCategory(FeedbackCategory.Time, m_Choice[HitStop] == 0, 1f);
            SampleState.SetCategory(FeedbackCategory.Haptics, m_Choice[Rumble] == 0, 1f);
            ShowEffective();
        }

        void ShowMenu()
        {
            for (int r = 0; r < Rows; r++)
            {
                if (r < m_Values.Length && m_Values[r] != null)
                    Set(m_Values[r], s_Labels[r][m_Choice[r]]);
                if (r < m_Frames.Length && m_Frames[r] != null)
                    m_Frames[r].SetActive(r == m_Row);
            }
        }

        void ShowEffective()
        {
            Set(m_CameraScale, Scale(FeedbackCategory.Camera));
            Set(m_MotionScale, Scale(FeedbackCategory.Motion));
            Set(m_FlashLimit, FeedbackCategories.IsEnabled(FeedbackCategory.Flash) ? s_Limit[Mathf.Clamp(FeedbackCategories.FlashLimit, 0, s_Limit.Length - 1)] : "Off");
            Set(m_TimeOn, FeedbackCategories.IsEnabled(FeedbackCategory.Time) ? "On" : "Off");
            Set(m_HapticsOn, FeedbackCategories.IsEnabled(FeedbackCategory.Haptics) ? "On" : "Off");
        }

        static string Scale(FeedbackCategory c) => FeedbackCategories.IsEnabled(c) ? s_Scale[Mathf.Clamp(Mathf.RoundToInt(FeedbackCategories.GetScale(c) * 100f), 0, s_Scale.Length - 1)] : "Off";

        static void Set(Text t, string s)
        {
            if (t != null && !ReferenceEquals(t.text, s))
                t.text = s;
        }
    }
}
