using UnityEngine;
using xpTURN.Feedback;

namespace xpTURN.Feedback.Samples.Basics
{
    // the gallery's Custom/Vector3 Value target — a component of the user's that takes the value (IFeedbackTarget<Vector3>) and draws
    // it as its place (where it stood at the first value, plus the value)
    public sealed class GalleryValueVector3 : MonoBehaviour, IFeedbackTarget<Vector3>
    {
        Vector3 m_Value, m_Place;
        bool m_Placed;

        public Vector3 FeedbackValue
        {
            get => m_Value;
            set
            {
                if (!m_Placed)
                {
                    m_Place = transform.localPosition;
                    m_Placed = true;
                }
                m_Value = value;
                transform.localPosition = m_Place + value;
            }
        }
    }
}
