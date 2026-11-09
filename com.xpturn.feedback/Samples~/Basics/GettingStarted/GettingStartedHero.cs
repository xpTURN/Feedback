using UnityEngine;
using UnityEngine.InputSystem;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.Basics
{
    // S1 Getting Started: Action1 jumps the Rigidbody root; the visible child 'Body' carries the players (physics moves the root —
    // feedback shakes a child pivot). Jump plays on the jump; Land plays on the landing with the fall speed as the intensity
    public sealed class GettingStartedHero : MonoBehaviour
    {
        [SerializeField] FeedbackPlayer m_Jump;
        [SerializeField] FeedbackPlayer m_Land;
        [SerializeField] float m_JumpSpeed = 6f;
        [SerializeField] float m_HardestFall = 8f;
        [System.NonSerialized] InputAction m_Action1;
        Rigidbody m_Rigidbody;
        bool m_InAir;
        float m_Fall;

        void Awake() => m_Rigidbody = GetComponent<Rigidbody>();

        void Update()
        {
            if (m_Action1 == null)
                m_Action1 = SampleInput.Find("Action1");
            if (m_Action1 == null || !m_Action1.WasPressedThisFrame() || SampleInput.PointerOverUI || m_InAir)
                return;
            m_Rigidbody.linearVelocity = new Vector3(0f, m_JumpSpeed, 0f);
            m_InAir = true;
            m_Fall = 0f;
            if (m_Jump != null)
                m_Jump.Play();
        }

        void FixedUpdate()
        {
            if (m_InAir)
                m_Fall = Mathf.Max(m_Fall, -m_Rigidbody.linearVelocity.y);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!m_InAir || m_Fall <= 0f)
                return;
            m_InAir = false;
            if (m_Land != null)
                m_Land.Play(new PlayArgs().WithPosition(transform.position).WithIntensity(Mathf.Clamp01(m_Fall / m_HardestFall)));
        }
    }
}
