using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.GameFeelLabs
{
    // S6 Spring Playground: one target on a 7 × 7 grid — dragged (the pointer on the floor plane) or stepped a cell — followed by
    // the code springs (SpringFloat · SpringVector2 · SpringVector3, stepped here as fields — a copy would move alone) and by a
    // MotionDriver (Follow · its spring fixed in the scene). The sliders set the code springs only; Bump and MoveTo are players
    public sealed class SpringPlayground : MonoBehaviour
    {
        static readonly float[] s_Frequency = new float[19];
        static readonly float[] s_Damping = new float[20];
        static readonly string[] s_FrequencyText = new string[19];
        static readonly string[] s_DampingText = new string[20];

        [SerializeField] Transform m_Target;
        [SerializeField] Transform m_Bar;
        [SerializeField] Transform m_Disc;
        [SerializeField] Transform m_Cube;
        [SerializeField] LineRenderer m_Arrow;
        [SerializeField] MotionDriver m_Driver;
        [SerializeField] FeedbackPlayer m_Bump;
        [SerializeField] FeedbackPlayer m_MoveUp;
        [SerializeField] FeedbackPlayer m_MoveDown;
        [SerializeField] int m_Half = 3;
        [SerializeField] float m_CubeLift = 0.5f;
        [SerializeField] float m_DriverLift = 1.6f;

        [System.NonSerialized] InputAction m_Move;
        [System.NonSerialized] InputAction m_Point;
        [System.NonSerialized] InputAction m_Hold;
        [System.NonSerialized] InputAction m_Bumper;
        [System.NonSerialized] InputAction m_Mover;
        [System.NonSerialized] InputAction[] m_Options;
        SpringFloat m_Spring1;
        SpringVector2 m_Spring2;
        SpringVector3 m_Spring3;
        int m_FrequencyIndex = 10;
        int m_DampingIndex = 7;
        Vector3 m_Goal;
        Vector2Int m_Cell;
        Vector2 m_LastMove;
        bool m_Dragging;
        bool m_Up;
        GameObject m_Root;
        Text m_FrequencyLabel;
        Text m_DampingLabel;

        public float Frequency => s_Frequency[m_FrequencyIndex];
        public float Damping => s_Damping[m_DampingIndex];
        public Vector3 Goal => m_Goal;
        public SpringVector3 CubeSpring => m_Spring3;

        static SpringPlayground()
        {
            for (int i = 0; i < s_Frequency.Length; i++)
            {
                s_Frequency[i] = 1f + i * 0.5f;
                s_FrequencyText[i] = "Z · X — frequency " + s_Frequency[i].ToString("0.0") + " Hz";
            }
            for (int i = 0; i < s_Damping.Length; i++)
            {
                s_Damping[i] = 0.05f * (i + 1);
                s_DampingText[i] = "C · V — damping " + s_Damping[i].ToString("0.00");
            }
        }

        void OnEnable()
        {
            m_Goal = Vector3.zero;
            m_Cell = Vector2Int.zero;
            m_Spring1 = new SpringFloat(Height(0f), Frequency, Damping);
            m_Spring2 = new SpringVector2(Vector2.zero, Frequency, Damping);
            m_Spring3 = new SpringVector3(new Vector3(0f, m_CubeLift, 0f), Frequency, Damping);
            Build();
            Place();
        }

        void OnDisable()
        {
            if (m_Root != null)
                Destroy(m_Root);
        }

        float Height(float x) => 1.6f + x * 0.4f;

        void Update()
        {
            if (m_Move == null)
            {
                m_Move = SampleInput.Find("Move");
                m_Point = SampleInput.Find("Point");
                m_Hold = SampleInput.Find("Action1");
                m_Bumper = SampleInput.Find("Action2");
                m_Mover = SampleInput.Find("Action3");
                m_Options = new[] { SampleInput.Find("Option1"), SampleInput.Find("Option2"), SampleInput.Find("Option3"), SampleInput.Find("Option4") };
            }
            Options();
            Grid();
            Drag();
            if (m_Bumper != null && m_Bumper.WasPressedThisFrame() && m_Bump != null)
                m_Bump.Play();
            if (m_Mover != null && m_Mover.WasPressedThisFrame())
            {
                FeedbackPlayer p = m_Up ? m_MoveDown : m_MoveUp, other = m_Up ? m_MoveUp : m_MoveDown;
                // one Persist write on the cube at a time — the other stops where it is first
                if (other != null)
                    other.Stop(StopMode.Hold);
                if (p != null)
                    p.Play();
                m_Up = !m_Up;
            }
            Step(Time.deltaTime);
            Place();
        }

        void Options()
        {
            int f = m_FrequencyIndex, d = m_DampingIndex;
            if (Pressed(0)) f--;
            if (Pressed(1)) f++;
            if (Pressed(2)) d--;
            if (Pressed(3)) d++;
            SetParameters(f, d);
        }

        bool Pressed(int i) => m_Options[i] != null && m_Options[i].WasPressedThisFrame();

        public void SetParameters(int frequencyIndex, int dampingIndex)
        {
            frequencyIndex = Mathf.Clamp(frequencyIndex, 0, s_Frequency.Length - 1);
            dampingIndex = Mathf.Clamp(dampingIndex, 0, s_Damping.Length - 1);
            if (frequencyIndex == m_FrequencyIndex && dampingIndex == m_DampingIndex)
                return;
            m_FrequencyIndex = frequencyIndex;
            m_DampingIndex = dampingIndex;
            m_Spring1.Frequency = m_Spring2.Frequency = m_Spring3.Frequency = Frequency;
            m_Spring1.Damping = m_Spring2.Damping = m_Spring3.Damping = Damping;
            if (m_FrequencyLabel != null)
            {
                m_FrequencyLabel.text = s_FrequencyText[m_FrequencyIndex];
                m_DampingLabel.text = s_DampingText[m_DampingIndex];
            }
        }

        void Grid()
        {
            Vector2 v = m_Move != null ? m_Move.ReadValue<Vector2>() : Vector2.zero;
            bool was = m_LastMove.sqrMagnitude > 0.25f;
            m_LastMove = v;
            if (was || v.sqrMagnitude <= 0.25f)
                return;
            Vector2Int step = Mathf.Abs(v.x) >= Mathf.Abs(v.y) ? new Vector2Int(v.x > 0f ? 1 : -1, 0) : new Vector2Int(0, v.y > 0f ? 1 : -1);
            MoveTo(m_Cell + step);
        }

        public void MoveTo(Vector2Int cell)
        {
            m_Cell = new Vector2Int(Mathf.Clamp(cell.x, -m_Half, m_Half), Mathf.Clamp(cell.y, -m_Half, m_Half));
            SetGoal(new Vector3(m_Cell.x, 0f, m_Cell.y));
        }

        void Drag()
        {
            if (m_Hold == null || m_Point == null)
                return;
            // a pointer drags — Space · 1 · South (Action1 too) do not move the target
            if (m_Hold.WasPressedThisFrame())
                m_Dragging = m_Hold.activeControl != null && m_Hold.activeControl.device is Pointer && !SampleInput.PointerOverUI;
            if (!m_Hold.IsPressed())
                m_Dragging = false;
            if (!m_Dragging)
                return;
            Camera cam = Camera.main;
            if (cam == null)
                return;
            Ray ray = cam.ScreenPointToRay(m_Point.ReadValue<Vector2>());
            if (Mathf.Abs(ray.direction.y) < 1e-4f)
                return;
            float t = -ray.origin.y / ray.direction.y;
            if (t <= 0f)
                return;
            Vector3 hit = ray.origin + ray.direction * t;
            hit.x = Mathf.Clamp(hit.x, -m_Half, m_Half);
            hit.z = Mathf.Clamp(hit.z, -m_Half, m_Half);
            m_Cell = new Vector2Int(Mathf.RoundToInt(hit.x), Mathf.RoundToInt(hit.z));
            SetGoal(new Vector3(hit.x, 0f, hit.z));
        }

        public void SetGoal(Vector3 goal)
        {
            m_Goal = goal;
            m_Spring1.Target = Height(goal.x);
            m_Spring2.Target = new Vector2(goal.x, goal.z);
            m_Spring3.Target = goal + new Vector3(0f, m_CubeLift, 0f);
            if (m_Driver != null)
                m_Driver.SetTarget(new Vector4(goal.x, goal.y + m_DriverLift, goal.z, 0f));
        }

        public void Step(float dt)
        {
            if (dt <= 0f)
                return;
            m_Spring1.Step(dt);
            m_Spring2.Step(dt);
            m_Spring3.Step(dt);
        }

        void Place()
        {
            if (m_Target != null)
                m_Target.position = m_Goal;
            if (m_Bar != null)
            {
                float h = Mathf.Max(0.05f, m_Spring1.Value);
                m_Bar.localScale = new Vector3(0.4f, h, 0.4f);
                m_Bar.localPosition = new Vector3(m_Bar.localPosition.x, h * 0.5f, m_Bar.localPosition.z);
            }
            if (m_Disc != null)
                m_Disc.position = new Vector3(m_Spring2.Value.x, 0.02f, m_Spring2.Value.y);
            if (m_Cube != null)
                m_Cube.position = m_Spring3.Value;
            if (m_Arrow != null && m_Cube != null)
            {
                m_Arrow.SetPosition(0, m_Spring3.Value);
                m_Arrow.SetPosition(1, m_Spring3.Value + m_Spring3.Velocity * 0.15f);
            }
        }

        void Build()
        {
            if (m_Root != null)
                Destroy(m_Root);
            m_Root = new GameObject("Spring Canvas", typeof(RectTransform));
            m_Root.transform.SetParent(transform, false);
            var canvas = m_Root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = m_Root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_FrequencyLabel = Line(font, new Vector2(-560f, 96f));
            m_DampingLabel = Line(font, new Vector2(-560f, 60f));
            m_FrequencyLabel.text = s_FrequencyText[m_FrequencyIndex];
            m_DampingLabel.text = s_DampingText[m_DampingIndex];
            Line(font, new Vector2(40f, 96f)).text = "grey — the code springs (the sliders) · blue — MotionDriver (6 Hz · 0.40, fixed)";
            Line(font, new Vector2(40f, 60f)).text = "2 · West — Bump · 3 · North — MoveTo (the cubes on the right)";
        }

        Text Line(Font font, Vector2 at)
        {
            var go = new GameObject("Line", typeof(RectTransform));
            go.transform.SetParent(m_Root.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = at;
            rt.sizeDelta = new Vector2(860f, 34f);
            Text text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = 20;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }
    }
}
