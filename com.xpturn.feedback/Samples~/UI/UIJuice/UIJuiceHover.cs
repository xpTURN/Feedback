using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using xpTURN.Feedback;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.UI
{
    // a hover by the pointer or by navigation (a D-pad, a stick, the arrows) plays the item's hover player. A click selects too — that
    // selection comes with the pointer's data and plays nothing (the pointer's enter played it); a selection from code is quiet. The
    // frame shows the selection while it lasts (the item's transition is None — the hover is the feedback)
    public sealed class UIJuiceHover : MonoBehaviour, IPointerEnterHandler, ISelectHandler, IDeselectHandler
    {
        [SerializeField] FeedbackPlayer m_Player;
        [SerializeField] Graphic m_Frame;

        public void OnPointerEnter(PointerEventData eventData) => Play();

        public void OnSelect(BaseEventData eventData)
        {
            if (m_Frame != null)
                m_Frame.enabled = true;
            if (eventData is AxisEventData)
                Play();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            if (m_Frame != null)
                m_Frame.enabled = false;
        }

        void Play()
        {
            if (m_Player != null && SampleState.After)
                m_Player.Play();
        }
    }
}
