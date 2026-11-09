using UnityEngine;

namespace xpTURN.Feedback.Samples.Basics
{
    // a Camera entry's stage: while it is shown the main camera draws what the entry's item changes — a solid background (the gallery
    // draws a sky) or an orthographic view framed like the perspective one at the stage (the gallery's camera is a perspective one);
    // put back when the stage hides
    public sealed class GalleryCameraStage : MonoBehaviour
    {
        [SerializeField] bool m_SolidBackground;
        [SerializeField] bool m_Orthographic;

        Camera m_Camera;
        CameraClearFlags m_ClearFlags;
        bool m_WasOrthographic;
        float m_Size;

        void OnEnable()
        {
            m_Camera = Camera.main;
            if (m_Camera == null)
                return;
            m_ClearFlags = m_Camera.clearFlags;
            m_WasOrthographic = m_Camera.orthographic;
            m_Size = m_Camera.orthographicSize;
            if (m_SolidBackground)
                m_Camera.clearFlags = CameraClearFlags.SolidColor;
            if (m_Orthographic)
            {
                Transform view = m_Camera.transform;
                float depth = Vector3.Dot(transform.position - view.position, view.forward);
                m_Camera.orthographic = true;
                m_Camera.orthographicSize = depth * Mathf.Tan(m_Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            }
        }

        void OnDisable()
        {
            if (m_Camera == null)
                return;
            m_Camera.clearFlags = m_ClearFlags;
            m_Camera.orthographic = m_WasOrthographic;
            m_Camera.orthographicSize = m_Size;
            m_Camera = null;
        }
    }
}
