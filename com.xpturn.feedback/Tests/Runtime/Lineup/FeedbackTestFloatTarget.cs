using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // a second implementer of IFeedbackTarget<float> only — the first-of-two and the walk tests
    public sealed class FeedbackTestFloatTarget : MonoBehaviour, IFeedbackTarget<float>
    {
        public float m_Value;

        public float FeedbackValue
        {
            get => m_Value;
            set => m_Value = value;
        }
    }
}
