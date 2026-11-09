using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // a spring field of a user's component — its fields, and a note while Frequency and Damping are both 0: a field without
    // an initialiser (a struct has no field initialisers in C# 9) sits at the domain's bottom and barely moves. UI Toolkit
    // and IMGUI inspectors both
    [CustomPropertyDrawer(typeof(SpringFloat))]
    [CustomPropertyDrawer(typeof(SpringVector2))]
    [CustomPropertyDrawer(typeof(SpringVector3))]
    [CustomPropertyDrawer(typeof(SpringColor))]
    internal sealed class SpringDrawer : PropertyDrawer
    {
        internal const string UninitialisedText = "An uninitialised spring — Frequency and Damping are 0, so it barely moves. Initialise the field: = new {0}(…).";

        internal static bool Uninitialised(SerializedProperty spring)
            => spring.FindPropertyRelative("Frequency")?.floatValue == 0f && spring.FindPropertyRelative("Damping")?.floatValue == 0f;

        internal static string Text(SerializedProperty spring) => string.Format(UninitialisedText, spring.type);

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new Foldout { text = property.displayName, value = property.isExpanded };
            root.RegisterValueChangedCallback(e =>
            {
                if (e.target == root)
                    property.isExpanded = e.newValue;
            });
            var note = new HelpBox(Text(property), HelpBoxMessageType.Warning);
            root.Add(note);
            SerializedProperty it = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = false;
                var field = new PropertyField(it.Copy());
                field.BindProperty(it.Copy());
                root.Add(field);
            }
            void Show() => note.style.display = Uninitialised(property) ? DisplayStyle.Flex : DisplayStyle.None;
            Show();
            root.TrackPropertyValue(property, _ => Show());
            return root;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUI.GetPropertyHeight(property, label, true);
            return Uninitialised(property) ? height + NoteHeight + EditorGUIUtility.standardVerticalSpacing : height;
        }

        const float NoteHeight = 32f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (Uninitialised(property))
            {
                var note = new Rect(position.x, position.y, position.width, NoteHeight);
                EditorGUI.HelpBox(note, Text(property), MessageType.Warning);
                position.yMin += NoteHeight + EditorGUIUtility.standardVerticalSpacing;
            }
            EditorGUI.PropertyField(position, property, label, true);
        }
    }
}
