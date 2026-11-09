using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using xpTURN.Feedback.Samples;

namespace xpTURN.Feedback.Samples.UI
{
    // S15 UI Trigger: a settings menu whose feedback is all in the scene — each control's Feedback UI Trigger rows and its own
    // UnityEvent call the players, so nothing here plays, stops or signals one. This keeps the selection for a pad: the first
    // menu button is selected at the start, and a navigation with no selection of this screen picks the last one (the hub's rule)
    public sealed class UITriggerLab : MonoBehaviour
    {
        [SerializeField] Selectable m_First;
        [SerializeField] Transform m_Screen;

        [System.NonSerialized] InputAction m_Navigate;
        GameObject m_LastSelected;

        void Start()
        {
            if (m_First != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(m_First.gameObject);
        }

        void Update()
        {
            if (m_Navigate == null && SampleInput.Asset != null)
                m_Navigate = SampleInput.Asset.FindAction("UI/Navigate");
            EventSystem es = EventSystem.current;
            if (es == null)
                return;
            GameObject now = es.currentSelectedGameObject;
            if (now != null && m_Screen != null && now.transform.IsChildOf(m_Screen))
            {
                m_LastSelected = now;
                return;
            }
            if (m_Navigate == null || m_Navigate.ReadValue<Vector2>().sqrMagnitude <= 0.25f)
                return;
            Selectable last = m_LastSelected != null ? m_LastSelected.GetComponent<Selectable>() : null;
            GameObject pick = last != null && last.isActiveAndEnabled && last.IsInteractable() ? last.gameObject : m_First != null ? m_First.gameObject : null;
            if (pick != null)
                es.SetSelectedGameObject(pick);
        }
    }
}
