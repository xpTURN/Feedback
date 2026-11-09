using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the trigger — Require Interactable, the rows, and its validation (FB-P12 · FB-P13) for one trigger
    [CustomEditor(typeof(FeedbackUITrigger))]
    [CanEditMultipleObjects]
    internal sealed class FeedbackUITriggerEditor : UnityEditor.Editor
    {
        static readonly string[] s_Fields = { "m_RequireInteractable", "m_Entries" };

        internal ValidationView Validation;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.AddToClassList(InspectorStyles.Root);
            InspectorStyles.Attach(root);
            foreach (string name in s_Fields)
            {
                SerializedProperty p = serializedObject.FindProperty(name);
                var field = new PropertyField(p);
                field.BindProperty(p);
                root.Add(field);
            }
            if (targets.Length == 1)
            {
                Validation = new ValidationView(target, serializedObject, null, null);
                root.Add(Validation);
            }
            return root;
        }
    }

    // a row: the event, the action, Stop's mode on a Stop row, the source on a Select or Deselect row, and the player
    [CustomPropertyDrawer(typeof(UITriggerEntry))]
    internal sealed class UITriggerEntryDrawer : PropertyDrawer
    {
        internal const string StopModeName = "stop-mode";
        internal const string SourceName = "source";
        internal const string PlayerTooltip = "Empty: the Feedback Player on the trigger's object.";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var row = new VisualElement();
            SerializedProperty kind = property.FindPropertyRelative("m_Event");
            SerializedProperty action = property.FindPropertyRelative("m_Action");
            var stop = new PropertyField(property.FindPropertyRelative("m_StopMode")) { name = StopModeName };
            var source = new PropertyField(property.FindPropertyRelative("m_Source")) { name = SourceName };
            row.Add(new PropertyField(kind));
            row.Add(new PropertyField(action));
            row.Add(stop);
            row.Add(source);
            row.Add(new PropertyField(property.FindPropertyRelative("m_Player")) { tooltip = PlayerTooltip });
            void Refresh()
            {
                var e = (UITriggerEvent)kind.intValue;
                stop.style.display = (UITriggerAction)action.intValue == UITriggerAction.Stop ? DisplayStyle.Flex : DisplayStyle.None;
                source.style.display = e == UITriggerEvent.Select || e == UITriggerEvent.Deselect ? DisplayStyle.Flex : DisplayStyle.None;
            }
            Refresh();
            row.TrackPropertyValue(kind, _ => Refresh());
            row.TrackPropertyValue(action, _ => Refresh());
            return row;
        }
    }
}
