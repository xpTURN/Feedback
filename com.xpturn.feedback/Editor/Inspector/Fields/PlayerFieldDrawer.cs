using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // a Feedback Player field's dropdown: the players on the listed object — the value's, or the field's own object while an
    // own-object field is empty — in their component order, 'n · alias' (1 based, the list's alias). An own-object field
    // leads with the Empty line: the value while empty, and picked, it empties the field. Shown while the listed object has
    // two or more; never while the targets' values differ, nor on an empty field of several targets. The labels are built
    // again only when the players, their aliases or the Empty line's player change
    internal sealed class PlayerChoices
    {
        internal const string NoAlias = "(no alias)";
        internal const string EmptyText = "Empty";
        internal const string NoneText = "(none)";

        static readonly List<FeedbackPlayer> s_Found = new List<FeedbackPlayer>();

        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<string> m_Aliases = new List<string>();
        bool m_Own;
        bool m_Named;
        FeedbackPlayer m_Empty;
        string m_EmptyAlias;

        internal List<string> Labels { get; private set; } = new List<string>();
        internal bool Shown { get; private set; }
        // the value's line
        internal int Index { get; private set; } = -1;
        // how many times the labels were built
        internal int Builds { get; private set; }

        internal static string Line(int index, string alias) => (index + 1) + " · " + (string.IsNullOrEmpty(alias) ? NoAlias : alias);

        // the player's line among its object's players — null while it is the only one
        internal static string LineOf(FeedbackPlayer player)
        {
            player.GetComponents(s_Found);
            string line = s_Found.Count > 1 ? Line(s_Found.IndexOf(player), player.ListAlias) : null;
            s_Found.Clear();
            return line;
        }

        // host: the field's component (an own-object field's object) · multi: several targets. True when the labels changed
        internal bool Update(FeedbackPlayer value, bool mixed, Object host, bool multi, bool own)
        {
            s_Found.Clear();
            Component component = multi ? null : host as Component;
            if (!mixed)
            {
                if (value != null)
                    value.GetComponents(s_Found);
                else if (own && component != null)
                    component.GetComponents(s_Found);
            }
            bool shown = s_Found.Count > 1;
            bool named = own && component != null;
            FeedbackPlayer empty = null;
            if (shown && named)
                component.TryGetComponent(out empty);
            string emptyAlias = empty != null ? empty.ListAlias : null;
            bool changed = shown && (!Shown || own != m_Own || named != m_Named || !ReferenceEquals(empty, m_Empty) || emptyAlias != m_EmptyAlias || !Same());
            if (changed)
                Build(own, named, empty, emptyAlias);
            Shown = shown;
            Index = !shown ? -1 : value == null ? 0 : s_Found.IndexOf(value) + (own ? 1 : 0);
            s_Found.Clear();
            return changed;
        }

        bool Same()
        {
            if (s_Found.Count != m_Players.Count)
                return false;
            for (int i = 0; i < s_Found.Count; i++)
            {
                if (!ReferenceEquals(s_Found[i], m_Players[i]) || s_Found[i].ListAlias != m_Aliases[i])
                    return false;
            }
            return true;
        }

        void Build(bool own, bool named, FeedbackPlayer empty, string emptyAlias)
        {
            m_Own = own;
            m_Named = named;
            m_Empty = empty;
            m_EmptyAlias = emptyAlias;
            m_Players.Clear();
            m_Aliases.Clear();
            var labels = new List<string>(s_Found.Count + 1);
            if (own)
                labels.Add(named ? EmptyText + " → " + (empty != null ? Line(0, emptyAlias) : NoneText) : EmptyText);
            for (int i = 0; i < s_Found.Count; i++)
            {
                FeedbackPlayer p = s_Found[i];
                string alias = p.ListAlias;
                m_Players.Add(p);
                m_Aliases.Add(alias);
                labels.Add(Line(i, alias));
            }
            Labels = labels;
            Builds++;
        }

        // the player a line picks — null for the Empty line
        internal FeedbackPlayer At(int line) => m_Own ? (line <= 0 ? null : m_Players[line - 1]) : m_Players[line];
    }

    // the field: the default field, and the dropdown on its right. The value is read where the property is current — built,
    // and when the tracked value changes; the objects' players are polled while attached, without reading the property (an
    // inspector rebuilt or a row removed leaves it behind until the element goes)
    internal sealed class PlayerField : VisualElement
    {
        internal const string PickName = "player-pick";
        internal const float PickWidth = 120f;
        internal const long PollMs = 250;
        internal const string PickTooltip = "The Feedback Players on this object, in component order: the number and the list's alias.";
        internal const string OwnPickTooltip = PickTooltip + " Empty plays the first one on this component's object.";

        readonly SerializedProperty m_Property;
        readonly Object m_Host;
        readonly bool m_Multi;
        readonly bool m_Own;
        FeedbackPlayer m_Value;
        bool m_Mixed;
        internal readonly PropertyField Field;
        internal readonly DropdownField Pick;
        internal readonly string Path;
        internal readonly PlayerChoices Choices = new PlayerChoices();

        internal PlayerField(SerializedProperty property, string label, bool own)
        {
            m_Property = property.Copy();
            Path = property.propertyPath;
            SerializedObject so = property.serializedObject;
            m_Host = so.targetObject;
            m_Multi = so.isEditingMultipleObjects;
            m_Own = own;
            style.flexDirection = FlexDirection.Row;
            Field = new PropertyField(property, label);
            Field.style.flexGrow = 1f;
            Field.style.flexShrink = 1f;
            Add(Field);
            Pick = new DropdownField { name = PickName, tooltip = own ? OwnPickTooltip : PickTooltip };
            Pick.style.width = PickWidth;
            Pick.style.flexShrink = 0f;
            Pick.RegisterValueChangedCallback(e =>
            {
                e.StopPropagation();
                Picked(Pick.index);
            });
            Add(Pick);
            Read(property);
            this.TrackPropertyValue(m_Property, Read);
            schedule.Execute(Refresh).Every(PollMs);
        }

        void Read(SerializedProperty property)
        {
            m_Mixed = property.hasMultipleDifferentValues;
            m_Value = m_Mixed ? null : property.objectReferenceValue as FeedbackPlayer;
            Refresh();
        }

        internal void Refresh()
        {
            if (Choices.Update(m_Value, m_Mixed, m_Host, m_Multi, m_Own))
                Pick.choices = Choices.Labels;
            Pick.style.display = Choices.Shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (Choices.Shown && Choices.Index >= 0)
                Pick.SetValueWithoutNotify(Choices.Labels[Choices.Index]);
        }

        void Picked(int line)
        {
            if (!Choices.Shown || line < 0 || line >= Choices.Labels.Count)
                return;
            m_Property.objectReferenceValue = Choices.At(line);
            m_Property.serializedObject.ApplyModifiedProperties();
        }
    }

    // every FeedbackPlayer field drawn through a PropertyField — ours and a user's; the field itself is the default one (the
    // drawer's own PropertyField and EditorGUI.PropertyField draw it without this drawer). An own-object field carries
    // OwnObjectPlayer. UI Toolkit and IMGUI inspectors both
    [CustomPropertyDrawer(typeof(FeedbackPlayer))]
    internal sealed class PlayerFieldDrawer : PropertyDrawer
    {
        const float Gap = 2f;

        readonly PlayerChoices m_Choices = new PlayerChoices();
        string[] m_Labels;

        // observation: IMGUI draws, the last draw's rect and its default field's, whether it had the dropdown, draws inside a
        // draw
        internal static int Draws;
        internal static Rect LastPosition;
        internal static Rect LastField;
        internal static bool LastPopup;
        internal static int Nested;
        static int s_Depth;

        bool Own() => fieldInfo != null && fieldInfo.IsDefined(typeof(OwnObjectPlayerAttribute), false);

        public override VisualElement CreatePropertyGUI(SerializedProperty property) => new PlayerField(property, preferredLabel, Own());

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Draws++;
            if (s_Depth > 0)
                Nested++;
            s_Depth++;
            try
            {
                LastPosition = position;
                SerializedObject so = property.serializedObject;
                bool mixed = property.hasMultipleDifferentValues;
                if (m_Choices.Update(mixed ? null : property.objectReferenceValue as FeedbackPlayer, mixed, so.targetObject, so.isEditingMultipleObjects, Own()) || m_Labels == null)
                    m_Labels = m_Choices.Labels.ToArray();
                LastPopup = m_Choices.Shown;
                if (!m_Choices.Shown)
                {
                    LastField = position;
                    EditorGUI.PropertyField(position, property, label);
                    return;
                }
                var field = new Rect(position.x, position.y, position.width - PlayerField.PickWidth - Gap, position.height);
                var pick = new Rect(field.xMax + Gap, position.y, PlayerField.PickWidth, EditorGUIUtility.singleLineHeight);
                LastField = field;
                EditorGUI.PropertyField(field, property, label);
                int line = EditorGUI.Popup(pick, m_Choices.Index, m_Labels);
                if (line != m_Choices.Index && line >= 0 && line < m_Labels.Length)
                    property.objectReferenceValue = m_Choices.At(line);
            }
            finally
            {
                s_Depth--;
            }
        }
    }
}
