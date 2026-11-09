using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace xpTURN.Feedback
{
    // the UI event a row answers — saved values: add members at the end only
    internal enum UITriggerEvent
    {
        PointerEnter = 0,
        PointerExit = 1,
        PointerDown = 2,
        PointerUp = 3,
        PointerClick = 4,
        Select = 5,
        Deselect = 6,
        Submit = 7,
        Cancel = 8,
    }

    // what a row does to its player — saved values
    internal enum UITriggerAction
    {
        Play = 0,
        PlayForward = 1,
        PlayBackward = 2,
        Stop = 3,
        Signal = 4,
    }

    // where a selection came from: a pointer's press, the navigation (a pad, the keys) or another way (code, the first
    // selection) — saved values, 0 takes them all
    internal enum UISelectSource
    {
        Any = 0,
        Pointer = 1,
        Navigation = 2,
        Other = 3,
    }

    // one row: every field's 0 is its default — a row the inspector adds is all zeros
    [Serializable]
    internal struct UITriggerEntry
    {
        [SerializeField] internal UITriggerEvent m_Event;
        [SerializeField] internal UITriggerAction m_Action;
        // Stop's mode
        [SerializeField] internal StopMode m_StopMode;
        // Select's and Deselect's filter
        [SerializeField] internal UISelectSource m_Source;
        // null: the player on the trigger's object
        [SerializeField] internal FeedbackPlayer m_Player;
    }

    // uGUI's handlers for nine events and no drag or scroll handler, so a parent ScrollRect keeps its drags. The press, click
    // and selection handlers take those events from a parent's handlers (uGUI sends them to the first object up the hierarchy
    // that handles them) — the trigger belongs on the pressable object itself. Down, Up and Click answer the left button only,
    // as a Button does. A Selectable on the object that is not interactable mutes the rows of the events that begin something;
    // the ending ones (Exit, Up, Deselect) still run, so a hover that began ends
    /// <summary>Plays, stops or signals players when uGUI's pointer, selection, submit and cancel events reach its object; each row names an event, an action and a player, the player on its object when none is set.</summary>
    [AddComponentMenu("xpTURN/Feedback/Feedback UI Trigger")]
    public sealed class FeedbackUITrigger : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
        ISelectHandler, IDeselectHandler, ISubmitHandler, ICancelHandler
    {
        internal const string NoPlayerWarning = "A Feedback UI Trigger has a row without a player and no FeedbackPlayer on its object; the row does nothing.";

        [SerializeField] internal bool m_RequireInteractable = true;
        [SerializeField] internal List<UITriggerEntry> m_Entries = new List<UITriggerEntry>();

        [NonSerialized] bool m_Found;
        [NonSerialized] FeedbackPlayer m_Own;
        [NonSerialized] Selectable m_Selectable;
#if DEBUG
        [NonSerialized] bool m_WarnedNoPlayer;
#endif

        // gives the inspector its enabled toggle — a component with handlers only has none
        void OnEnable()
        {
        }

        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData) => Raise(UITriggerEvent.PointerEnter, eventData);

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData) => Raise(UITriggerEvent.PointerExit, eventData);

        void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                Raise(UITriggerEvent.PointerDown, eventData);
        }

        void IPointerUpHandler.OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                Raise(UITriggerEvent.PointerUp, eventData);
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                Raise(UITriggerEvent.PointerClick, eventData);
        }

        void ISelectHandler.OnSelect(BaseEventData eventData) => Raise(UITriggerEvent.Select, eventData);

        void IDeselectHandler.OnDeselect(BaseEventData eventData) => Raise(UITriggerEvent.Deselect, eventData);

        void ISubmitHandler.OnSubmit(BaseEventData eventData) => Raise(UITriggerEvent.Submit, eventData);

        void ICancelHandler.OnCancel(BaseEventData eventData) => Raise(UITriggerEvent.Cancel, eventData);

        // the rows of that event in list order
        void Raise(UITriggerEvent kind, BaseEventData eventData)
        {
            List<UITriggerEntry> entries = m_Entries;
            if (entries == null || entries.Count == 0)
                return;
            if (!m_Found)
            {
                m_Found = true;
                TryGetComponent(out m_Selectable);
            }
            if (m_RequireInteractable && Begins(kind) && m_Selectable != null && !m_Selectable.IsInteractable())
                return;
            bool selection = kind == UITriggerEvent.Select || kind == UITriggerEvent.Deselect;
            UISelectSource source = selection ? SourceOf(eventData) : UISelectSource.Any;
            for (int i = 0; i < entries.Count; i++)
            {
                UITriggerEntry entry = entries[i];
                if (entry.m_Event != kind || (selection && entry.m_Source != UISelectSource.Any && entry.m_Source != source))
                    continue;
                FeedbackPlayer player = entry.m_Player != null ? entry.m_Player : Own();
                if (player == null)
                {
#if DEBUG
                    if (!m_WarnedNoPlayer)
                    {
                        m_WarnedNoPlayer = true;
                        Debug.LogWarning(NoPlayerWarning, this);
                    }
#endif
                    continue;
                }
                Run(in entry, player);
            }
        }

        // the player on the trigger's object — looked up again while there is none (TryGetComponent allocates nothing)
        FeedbackPlayer Own()
        {
            if (m_Own == null)
                TryGetComponent(out m_Own);
            return m_Own;
        }

        static bool Begins(UITriggerEvent kind) => kind != UITriggerEvent.PointerExit && kind != UITriggerEvent.PointerUp && kind != UITriggerEvent.Deselect;

        static UISelectSource SourceOf(BaseEventData eventData)
        {
            if (eventData is AxisEventData)
                return UISelectSource.Navigation;
            return eventData is PointerEventData ? UISelectSource.Pointer : UISelectSource.Other;
        }

        // the player's API — its refusals and warnings stay. Play Forward and Play Backward turn a play running the other way
        // around where it is
        static void Run(in UITriggerEntry entry, FeedbackPlayer player)
        {
            switch (entry.m_Action)
            {
                case UITriggerAction.Play:
                    player.Play(default(PlayArgs));
                    return;
                case UITriggerAction.PlayForward:
                    player.PlayTurning(PlayerDirection.Forward);
                    return;
                case UITriggerAction.PlayBackward:
                    player.PlayTurning(PlayerDirection.Backward);
                    return;
                case UITriggerAction.Stop:
                    player.Stop(entry.m_StopMode);
                    return;
                case UITriggerAction.Signal:
                    player.Signal();
                    return;
            }
        }
    }
}
