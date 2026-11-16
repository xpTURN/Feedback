using UnityEngine;

namespace xpTURN.Feedback
{
    // how trauma shakes this transform — read when its trauma starts (a later change takes effect after the trauma has faded)
    /// <summary>Sets how trauma shakes this transform, a camera or any object; without it the trauma feedbacks' defaults apply.</summary>
    [AddComponentMenu("xpTURN/Feedback/Trauma Shaker")]
    public sealed class TraumaShaker : MonoBehaviour
    {
        // world units on the transform's own axes, at a trauma of 1
        [SerializeField] internal Vector3 m_MaxOffset = TraumaParams.CameraDefault.MaxOffset;
        // degrees around the transform's own axes, at a trauma of 1
        [SerializeField] internal Vector3 m_MaxAngle = TraumaParams.CameraDefault.MaxAngle;
        [SerializeField, Range(0.1f, 60f)] internal float m_Frequency = TraumaParams.CameraDefault.Frequency;
        // the shake grows as trauma to this power — 2: a light trauma barely shows
        [SerializeField, Range(1f, 3f)] internal float m_Exponent = TraumaParams.CameraDefault.Exponent;
        // trauma lost a second — 1: a full trauma fades in a second
        [SerializeField, Min(0.05f)] internal float m_Decay = TraumaParams.CameraDefault.Decay;
        [SerializeField] internal PlayerTimeMode m_TimeMode;

        // a new shaker starts from the camera's or the object's defaults
        void Reset()
        {
            TraumaParams p = TryGetComponent(out Camera _) ? TraumaParams.CameraDefault : TraumaParams.ObjectDefault;
            m_MaxOffset = p.MaxOffset;
            m_MaxAngle = p.MaxAngle;
            m_Frequency = p.Frequency;
            m_Exponent = p.Exponent;
            m_Decay = p.Decay;
            m_TimeMode = PlayerTimeMode.Scaled;
        }

        internal TraumaParams Params => new TraumaParams
        {
            MaxOffset = Vector3.Max(m_MaxOffset, Vector3.zero),
            MaxAngle = Vector3.Max(m_MaxAngle, Vector3.zero),
            Frequency = MotionKernels.Within(m_Frequency, 0.1f, 60f),
            Exponent = MotionKernels.Within(m_Exponent, 1f, 3f),
            Decay = m_Decay >= 0.05f ? m_Decay : 0.05f,
            Unscaled = m_TimeMode == PlayerTimeMode.Unscaled,
        };
    }
}
