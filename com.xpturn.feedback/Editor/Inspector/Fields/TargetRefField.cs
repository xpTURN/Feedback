using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // a TargetRef — the mode and its fields. Direct takes an object of the player's scene only (TargetChecks) and offers the
    // host's candidates of the required type; Child the path · index · first of the type; Anchor the player's anchors;
    // Channel a number or an asset. A list without a host (a preset asset) hides Direct — a scene reference is not saved
    // there. Enums are written as intValue. The host is the player or the driver
    internal sealed class TargetRefField : VisualElement
    {
        static readonly string[] s_Modes = Enum.GetNames(typeof(TargetRefMode));

        readonly SerializedProperty m_Target;
        readonly Component m_Host;
        readonly Type m_Requires;
        internal readonly PopupField<string> Mode;
        internal readonly VisualElement Details = new VisualElement();
        internal readonly HelpBox Refused = new HelpBox("", HelpBoxMessageType.Warning);
        internal ObjectField Direct;
        internal Button Pick;

        internal TargetRefField(SerializedProperty target, Component host, Type requires, string label = "Target")
        {
            m_Target = target.Copy();
            m_Host = host;
            m_Requires = requires;
            var choices = new List<string>();
            TargetRefMode current = ModeOf(m_Target);
            foreach (string name in s_Modes)
            {
                if (host != null || name != nameof(TargetRefMode.Direct) || current == TargetRefMode.Direct)
                    choices.Add(name);
            }
            Mode = new PopupField<string>(label, choices, current.ToString());
            Mode.RegisterValueChangedCallback(e =>
            {
                if (Enum.TryParse(e.newValue, out TargetRefMode mode))
                    WriteInt(m_Target.FindPropertyRelative("m_Mode"), (int)mode);
            });
            Add(Mode);
            Add(Details);
            Refused.style.display = DisplayStyle.None;
            Add(Refused);
            ShowDetails();
            this.TrackPropertyValue(m_Target.FindPropertyRelative("m_Mode"), _ =>
            {
                Mode.SetValueWithoutNotify(ModeOf(m_Target).ToString());
                ShowDetails();
            });
        }

        static TargetRefMode ModeOf(SerializedProperty target) => (TargetRefMode)target.FindPropertyRelative("m_Mode").intValue;

        static void WriteInt(SerializedProperty p, int value)
        {
            p.serializedObject.Update();
            p.intValue = value;
            p.serializedObject.ApplyModifiedProperties();
        }

        void ShowDetails()
        {
            Details.Clear();
            Direct = null;
            Pick = null;
            Refused.style.display = DisplayStyle.None;
            switch (ModeOf(m_Target))
            {
                case TargetRefMode.Direct:
                    AddDirect();
                    break;
                case TargetRefMode.Child:
                    AddChild();
                    break;
                case TargetRefMode.Anchor:
                    AddAnchor();
                    break;
                case TargetRefMode.Channel:
                    AddChannel();
                    break;
            }
        }

        void AddDirect()
        {
            SerializedProperty o = m_Target.FindPropertyRelative("m_Object");
            var row = new VisualElement();
            row.AddToClassList(InspectorStyles.Row);
            Direct = new ObjectField("Object")
            {
                objectType = m_Requires != null && (typeof(Component).IsAssignableFrom(m_Requires) || m_Requires == typeof(GameObject)) ? m_Requires : typeof(Object),
                allowSceneObjects = true,
                value = o.objectReferenceValue,
            };
            Direct.AddToClassList(InspectorStyles.Grow);
            Direct.RegisterValueChangedCallback(e =>
            {
                TargetRefusal refusal = SetDirect(m_Target, m_Host, e.newValue);
                if (refusal != TargetRefusal.None)
                    Direct.SetValueWithoutNotify(e.previousValue);
                ShowRefusal(refusal);
            });
            Direct.TrackPropertyValue(o, p => Direct?.SetValueWithoutNotify(p.objectReferenceValue));
            row.Add(Direct);
            if (m_Host != null && m_Requires != null)
            {
                Pick = new Button(OpenCandidates) { text = "Pick", tooltip = "The player's and its children's objects of the type this item needs" };
                row.Add(Pick);
            }
            Details.Add(row);
        }

        void ShowRefusal(TargetRefusal refusal)
        {
            Refused.text = TargetChecks.Text(refusal);
            Refused.style.display = refusal == TargetRefusal.None ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void OpenCandidates()
        {
            var menu = new GenericMenu();
            List<Object> found = TargetChecks.Candidates(m_Host, m_Requires);
            foreach (Object c in found)
            {
                Object chosen = c;
                menu.AddItem(new GUIContent(TargetChecks.PathOf(m_Host, c)), false, () => SetDirect(m_Target, m_Host, chosen));
            }
            if (found.Count == 0)
                menu.AddDisabledItem(new GUIContent("No " + TargetChecks.TypeName(m_Requires) + " on the player or its children"));
            menu.DropDown(Pick.worldBound);
        }

        // a Direct target — refused (and nothing written) for an asset or another scene's object
        internal static TargetRefusal SetDirect(SerializedProperty target, Component host, Object value)
        {
            TargetRefusal refusal = TargetChecks.Check(host, value);
            if (refusal != TargetRefusal.None)
                return refusal;
            target.serializedObject.Update();
            target.FindPropertyRelative("m_Mode").intValue = (int)TargetRefMode.Direct;
            target.FindPropertyRelative("m_Object").objectReferenceValue = value;
            target.serializedObject.ApplyModifiedProperties();
            return TargetRefusal.None;
        }

        void AddChild()
        {
            SerializedProperty lookup = m_Target.FindPropertyRelative("m_ChildLookup");
            var choices = new List<string>(Enum.GetNames(typeof(ChildLookup)));
            var by = new PopupField<string>("Find By", choices, ((ChildLookup)lookup.intValue).ToString());
            Details.Add(by);
            var path = new TextField("Path");
            path.BindProperty(m_Target.FindPropertyRelative("m_Path"));
            var index = new IntegerField("Index");
            index.BindProperty(m_Target.FindPropertyRelative("m_Index"));
            Details.Add(path);
            Details.Add(index);
            void Show(ChildLookup l)
            {
                path.style.display = l == ChildLookup.Path ? DisplayStyle.Flex : DisplayStyle.None;
                index.style.display = l == ChildLookup.Index ? DisplayStyle.Flex : DisplayStyle.None;
            }
            Show((ChildLookup)lookup.intValue);
            by.RegisterValueChangedCallback(e =>
            {
                if (!Enum.TryParse(e.newValue, out ChildLookup l))
                    return;
                WriteInt(lookup, (int)l);
                Show(l);
            });
        }

        void AddAnchor()
        {
            SerializedProperty anchor = m_Target.FindPropertyRelative("m_Anchor");
            var player = m_Host as FeedbackPlayer;
            if (player == null)
            {
                var name = new TextField("Anchor");
                name.BindProperty(anchor);
                Details.Add(name);
                return;
            }
            var names = new List<string>();
            foreach (PlayerAnchor a in player.m_Anchors)
            {
                if (!string.IsNullOrEmpty(a.m_Name) && !names.Contains(a.m_Name))
                    names.Add(a.m_Name);
            }
            string current = anchor.stringValue ?? "";
            if (!names.Contains(current))
                names.Insert(0, current);
            var field = new PopupField<string>("Anchor", names, current);
            field.RegisterValueChangedCallback(e =>
            {
                anchor.serializedObject.Update();
                anchor.stringValue = e.newValue;
                anchor.serializedObject.ApplyModifiedProperties();
            });
            Details.Add(field);
        }

        void AddChannel() => Details.Add(new ChannelRefField(m_Target.FindPropertyRelative("m_Channel"), "Channel"));
    }
}
