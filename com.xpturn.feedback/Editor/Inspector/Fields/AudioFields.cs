using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the audio items' asset fields — the item keeps an Object (Core does not name the module's types), the inspector a field of
    // the asset's type: the picker and a drop take that type only. A value of another type set by script stays as it is — the
    // validator reports it (FB-T01 · FB-T03)
    internal static class AudioFields
    {
        internal static Type Typed(FeedbackItem item, string field)
        {
            switch (item)
            {
                case AudioPlayClipItem _ when field == "m_Clip":
                    return typeof(AudioClip);
                case AudioPlayAtPointItem _ when field == "m_Resource":
                    return typeof(AudioResource);
                case AudioPlayAtPointItem _ when field == "m_Output":
                    return typeof(AudioMixerGroup);
                case AudioMixerParameterItem _ when field == "m_Mixer":
                    return typeof(AudioMixer);
                case AudioMixerSnapshotItem _ when field == "m_Snapshot" || field == "m_BackTo":
                    return typeof(AudioMixerSnapshot);
            }
            return null;
        }

        // an Object property's field of one type — no scene objects
        internal static ObjectField Object(SerializedProperty p, string label, Type type)
        {
            var field = new ObjectField(label) { objectType = type, allowSceneObjects = false, bindingPath = p.propertyPath };
            field.BindProperty(p);
            return field;
        }
    }

    // a Mixer Parameter's name — typed, or picked from its mixer's exposed parameters (the mixer's serialized list)
    internal sealed class AudioMixerParameterField : VisualElement
    {
        internal const string NoneText = "No exposed parameter on the item's Audio Mixer";

        readonly SerializedProperty m_Item;
        readonly SerializedProperty m_Name;
        internal readonly TextField Text;
        internal readonly Button Pick;

        internal AudioMixerParameterField(SerializedProperty item, string field, string label)
        {
            m_Item = item.Copy();
            m_Name = item.FindPropertyRelative(field).Copy();
            style.flexDirection = FlexDirection.Row;
            Text = new TextField(label);
            Text.BindProperty(m_Name);
            Text.style.flexGrow = 1f;
            Add(Text);
            Pick = new Button(Open) { iconImage = Background.FromTexture2D(EditorGUIUtility.IconContent(InspectorStyles.MenuArrowIcon).image as Texture2D) };
            Add(Pick);
        }

        internal List<string> Candidates()
        {
            var mixer = (m_Item.managedReferenceValue as AudioMixerParameterItem)?.m_Mixer as AudioMixer;
            return AudioRuleReads.ExposedNames(mixer) ?? new List<string>();
        }

        void Open()
        {
            var menu = new GenericMenu();
            List<string> names = Candidates();
            if (names.Count == 0)
                menu.AddDisabledItem(new GUIContent(NoneText));
            foreach (string name in names)
            {
                string pick = name;
                menu.AddItem(new GUIContent(name), m_Name.stringValue == name, () => Choose(pick));
            }
            menu.ShowAsContext();
        }

        internal void Choose(string name)
        {
            m_Name.stringValue = name;
            m_Name.serializedObject.ApplyModifiedProperties();
        }
    }
}
