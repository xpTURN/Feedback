using UnityEngine;
using UnityEngine.UI;

namespace xpTURN.Feedback.Samples.Basics
{
    // the Blocks Raycasts and Raycast Target entries' subject: tints its graphic by whether the event system's raycaster would meet it
    // at its centre — the raycaster takes only graphics marked Raycast Target and runs Graphic.Raycast on each (the canvas groups'
    // filter: a group that lets raycasts through hides what is under it)
    [RequireComponent(typeof(Graphic))]
    public sealed class GalleryRaycastProbe : MonoBehaviour
    {
        [SerializeField] Color m_Blocks = new Color(0.45f, 0.8f, 0.5f, 1f);
        [SerializeField] Color m_PassesThrough = new Color(0.9f, 0.4f, 0.35f, 1f);

        Graphic m_Graphic;

        void Awake()
        {
            m_Graphic = GetComponent<Graphic>();
        }

        void Update()
        {
            Canvas canvas = m_Graphic.canvas;
            Camera eventCamera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 centre = RectTransformUtility.WorldToScreenPoint(eventCamera, m_Graphic.rectTransform.position);
            m_Graphic.color = m_Graphic.raycastTarget && m_Graphic.Raycast(centre, eventCamera) ? m_Blocks : m_PassesThrough;
        }
    }
}
