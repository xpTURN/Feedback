using System;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // the user target the editor · PlayMode · player tests share — the three value forms, the optional stage, a throwing
    // setter and the setter's count
    public sealed class FeedbackTestTarget : MonoBehaviour, IFeedbackTarget<float>, IFeedbackTarget<Vector3>, IFeedbackTarget<Color>, IFeedbackTargetStage
    {
        public float m_Float;
        public Vector3 m_Vector;
        public Color m_Color = Color.white;
        public FeedbackTargetStage m_Stage;
        public bool m_Throws;
        [NonSerialized] public int Sets;
        [NonSerialized] public int Gets;

        public FeedbackTargetStage Stage => m_Stage;

        float IFeedbackTarget<float>.FeedbackValue
        {
            get
            {
                Gets++;
                return m_Float;
            }
            set
            {
                Sets++;
                if (m_Throws)
                    throw new InvalidOperationException("FeedbackTestTarget throws");
                m_Float = value;
            }
        }

        Vector3 IFeedbackTarget<Vector3>.FeedbackValue
        {
            get => m_Vector;
            set
            {
                Sets++;
                m_Vector = value;
            }
        }

        Color IFeedbackTarget<Color>.FeedbackValue
        {
            get => m_Color;
            set
            {
                Sets++;
                m_Color = value;
            }
        }
    }
}
