using UnityEngine;

namespace xpTURN.Feedback.Samples.Basics
{
    // a Trail entry's subject: circles in its parent's XY plane (the plane the gallery's camera faces) starting where it was placed,
    // so its trail has a path to draw
    public sealed class GalleryOrbit : MonoBehaviour
    {
        [SerializeField] float m_Radius = 0.8f;
        [SerializeField] float m_TurnsPerSecond = 1f;

        Vector3 m_Center;
        float m_Angle;

        void Awake()
        {
            m_Center = transform.localPosition - new Vector3(m_Radius, 0f, 0f);
        }

        void Update()
        {
            m_Angle = Mathf.Repeat(m_Angle + m_TurnsPerSecond * 2f * Mathf.PI * Time.deltaTime, 2f * Mathf.PI);
            transform.localPosition = m_Center + new Vector3(Mathf.Cos(m_Angle), Mathf.Sin(m_Angle), 0f) * m_Radius;
        }
    }
}
