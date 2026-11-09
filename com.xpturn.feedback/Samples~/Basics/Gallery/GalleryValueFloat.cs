using UnityEngine;
using xpTURN.Feedback;

namespace xpTURN.Feedback.Samples.Basics
{
    // the gallery's Custom/Float Value target — a component of the user's that takes the value (IFeedbackTarget<float>) and draws it
    // as its height (the height it had at the first value, plus the value); Custom/Float Event's bar takes the event's value through it
    public sealed class GalleryValueFloat : MonoBehaviour, IFeedbackTarget<float>
    {
        float m_Value, m_Height;
        bool m_Measured;

        public float FeedbackValue
        {
            get => m_Value;
            set
            {
                if (!m_Measured)
                {
                    m_Height = transform.localScale.y;
                    m_Measured = true;
                }
                m_Value = value;
                Vector3 scale = transform.localScale;
                scale.y = m_Height + value;
                transform.localScale = scale;
            }
        }
    }
}
