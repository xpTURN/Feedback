using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // a saved Vector4 drawn as the target's value kind — the components the kind reads, and the hidden ones never written
    internal sealed class ValueField : VisualElement
    {
        static readonly string[] s_Components = { "x", "y", "z", "w" };

        readonly SerializedProperty m_Property;
        readonly ValueKind m_Kind;
        internal readonly ColorField ColorInput;
        internal readonly Toggle FlagInput;

        internal ValueField(SerializedProperty property, ValueKind kind, string label)
        {
            m_Property = property.Copy();
            m_Kind = kind;
            AddToClassList(InspectorStyles.Row);
            switch (kind)
            {
                case ValueKind.Color:
                    ColorInput = new ColorField(label) { value = Read() };
                    ColorInput.AddToClassList(InspectorStyles.Grow);
                    ColorInput.RegisterValueChangedCallback(e => Commit(e.newValue));
                    ColorInput.TrackPropertyValue(m_Property, _ => ColorInput.SetValueWithoutNotify(Read()));
                    Add(ColorInput);
                    break;
                case ValueKind.Bool:
                    FlagInput = new Toggle(label) { value = Read().x != 0f };
                    FlagInput.RegisterValueChangedCallback(e => Commit(new Vector4(e.newValue ? 1f : 0f, 0f, 0f, 0f)));
                    FlagInput.TrackPropertyValue(m_Property, _ => FlagInput.SetValueWithoutNotify(Read().x != 0f));
                    Add(FlagInput);
                    break;
                default:
                    Add(new Label(label) { style = { minWidth = 120 } });
                    for (int i = 0; i < Components(kind); i++)
                    {
                        var f = new FloatField(Components(kind) == 1 ? "" : s_Components[i].ToUpperInvariant());
                        f.AddToClassList(InspectorStyles.Grow);
                        f.BindProperty(m_Property.FindPropertyRelative(s_Components[i]));
                        Add(f);
                    }
                    break;
            }
        }

        // the components a kind reads — Float x · Vector2 xy · Vector3 xyz (a rotation is Euler xyz) · Vector4 · Color rgba · Bool x
        internal static int Components(ValueKind kind)
        {
            switch (kind)
            {
                case ValueKind.Float:
                case ValueKind.Bool:
                    return 1;
                case ValueKind.Vector2:
                    return 2;
                case ValueKind.Vector3:
                    return 3;
                default:
                    return 4;
            }
        }

        // the edit kept to the kind's components — the hidden ones stay as saved
        internal static Vector4 Write(Vector4 saved, ValueKind kind, Vector4 edited)
        {
            int n = Components(kind);
            for (int i = n; i < 4; i++)
                edited[i] = saved[i];
            return edited;
        }

        Vector4 Read() => m_Property.vector4Value;

        void Commit(Vector4 edited)
        {
            m_Property.serializedObject.Update();
            m_Property.vector4Value = Write(m_Property.vector4Value, m_Kind, edited);
            m_Property.serializedObject.ApplyModifiedProperties();
        }
    }
}
