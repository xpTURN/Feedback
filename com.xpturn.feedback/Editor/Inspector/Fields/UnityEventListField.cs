using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the player's UnityEvents — the saved list holds only the kinds in use, (kind, event) rows. 'Add Event' adds a row of a
    // kind (with no listeners — the new slot would copy the last row's), a row is shown under its kind's name and removed by
    // its button. Rows of the same kind are all called, in order
    internal sealed class UnityEventListField : VisualElement
    {
        internal const string AddText = "Add Event";
        internal const string AddUndo = "Add Feedback Event";
        internal const string RemoveUndo = "Remove Feedback Event";

        readonly SerializedProperty m_List;
        internal readonly VisualElement Rows = new VisualElement();
        internal readonly Button AddButton;

        // the list's path — the field stands for it in the settings view
        internal string Path => m_List.propertyPath;

        internal UnityEventListField(SerializedProperty list)
        {
            m_List = list.Copy();
            AddToClassList(InspectorStyles.Events);
            var header = new VisualElement();
            header.AddToClassList(InspectorStyles.Row);
            var label = new Label(list.displayName);
            label.AddToClassList(InspectorStyles.Grow);
            AddButton = new Button(OpenMenu) { text = AddText };
            header.Add(label);
            header.Add(AddButton);
            Add(header);
            Add(Rows);
            Rebuild();
            this.TrackPropertyValue(m_List.FindPropertyRelative("Array.size"), _ => Rebuild());
        }

        internal void Rebuild()
        {
            Rows.Clear();
            m_List.serializedObject.Update();
            for (int i = 0; i < m_List.arraySize; i++)
            {
                SerializedProperty row = m_List.GetArrayElementAtIndex(i);
                var kind = (PlayerEventKind)row.FindPropertyRelative("m_Kind").intValue;
                var line = new VisualElement();
                line.AddToClassList(InspectorStyles.Row);
                SerializedProperty ev = row.FindPropertyRelative("m_Event");
                var field = new PropertyField(ev, kind.ToString());
                field.BindProperty(ev);
                field.AddToClassList(InspectorStyles.Grow);
                int index = i;
                var remove = new Button(() => Remove(m_List, index)) { text = "−", tooltip = "Remove this event" };
                line.Add(field);
                line.Add(remove);
                Rows.Add(line);
            }
        }

        void OpenMenu()
        {
            var menu = new GenericMenu();
            foreach (PlayerEventKind kind in Enum.GetValues(typeof(PlayerEventKind)))
            {
                PlayerEventKind k = kind;
                menu.AddItem(new GUIContent(kind.ToString()), false, () => Add(m_List, k));
            }
            menu.DropDown(AddButton.worldBound);
        }

        // a new row of the kind at the end, no listeners — one undo
        internal static void Add(SerializedProperty list, PlayerEventKind kind)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(AddUndo);
            list.serializedObject.Update();
            int i = list.arraySize;
            list.arraySize = i + 1;
            SerializedProperty row = list.GetArrayElementAtIndex(i);
            row.FindPropertyRelative("m_Kind").intValue = (int)kind;
            row.FindPropertyRelative("m_Event.m_PersistentCalls.m_Calls").arraySize = 0;
            list.serializedObject.ApplyModifiedProperties();
        }

        internal static void Remove(SerializedProperty list, int index)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(RemoveUndo);
            list.serializedObject.Update();
            if (index >= 0 && index < list.arraySize)
                list.DeleteArrayElementAtIndex(index);
            list.serializedObject.ApplyModifiedProperties();
        }
    }
}
