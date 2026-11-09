using UnityEngine;
using UnityEngine.InputSystem;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.Fixture
{
    // the template sample: plays its player once at the start and on Action1 (not while the pointer is over UI)
    public sealed class TemplateSample : MonoBehaviour
    {
        public FeedbackPlayer Player;
        [System.NonSerialized] InputAction m_Action1;

        void Start()
        {
            if (Player != null)
                Player.Play();
        }

        void Update()
        {
            if (m_Action1 == null)
                m_Action1 = SampleInput.Find("Action1");
            if (m_Action1 != null && m_Action1.WasPressedThisFrame() && !SampleInput.PointerOverUI && Player != null)
                Player.Play();
        }
    }
}
