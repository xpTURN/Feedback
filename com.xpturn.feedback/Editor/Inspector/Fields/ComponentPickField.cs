using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // the components a Component Enabled turns — a Behaviour (a Collider2D too), a Renderer, a Collider (the editor names it
    // with the module off — without the Physics assembly the prepare refuses and E10 says why) — and a component's index
    // among its object's components of its type
    internal static class ComponentPick
    {
        static readonly List<Component> s_Components = new List<Component>();

        internal static bool Toggled(Component c) => c is Behaviour || c is Renderer || c is Collider;

        // a dropped object's component: the component itself, a GameObject's first one — null when none is turned
        internal static Component Of(Object o)
        {
            if (o is Component c)
                return Toggled(c) ? c : null;
            if (!(o is GameObject go))
                return null;
            foreach (Component x in go.GetComponents<Component>())
            {
                if (Toggled(x))
                    return x;
            }
            return null;
        }

        internal static int IndexOf(Component c)
        {
            string type = c.GetType().FullName;
            c.gameObject.GetComponents(s_Components);
            int k = 0, found = -1;
            foreach (Component x in s_Components)
            {
                if (x == null || x.GetType().FullName != type)
                    continue;
                if (ReferenceEquals(x, c))
                    found = k;
                k++;
            }
            s_Components.Clear();
            return found;
        }

        // the object's turned components with their names 'Type (index)', in the object's order
        internal static List<(Component, string)> Candidates(GameObject go)
        {
            var found = new List<(Component, string)>();
            if (go == null)
                return found;
            foreach (Component c in go.GetComponents<Component>())
            {
                if (Toggled(c))
                    found.Add((c, c.GetType().Name + " (" + IndexOf(c) + ")"));
            }
            return found;
        }

        // the item's two fields from a component — a Direct target takes the component (a reorder keeps it)
        internal static void Write(SerializedProperty item, Component c)
        {
            item.FindPropertyRelative("m_ComponentType").stringValue = c.GetType().FullName;
            item.FindPropertyRelative("m_ComponentIndex").intValue = IndexOf(c);
            SerializedProperty target = item.FindPropertyRelative("m_Target");
            if (target.FindPropertyRelative("m_Mode").intValue == (int)TargetRefMode.Direct)
                target.FindPropertyRelative("m_Object").objectReferenceValue = c;
            item.serializedObject.ApplyModifiedProperties();
        }

        internal static void Write(ComponentEnabledItem item, Component c)
        {
            item.m_ComponentType = c.GetType().FullName;
            item.m_ComponentIndex = IndexOf(c);
        }
    }

    // a Component Enabled's component — 'Type (index)' and a menu of the target object's turned components (the index is not
    // drawn on its own)
    internal sealed class ComponentPickField : VisualElement
    {
        internal const string NoneText = "(none)";

        readonly SerializedProperty m_Item;
        readonly Component m_Host;
        internal readonly Button Pick;

        internal ComponentPickField(SerializedProperty item, Component host)
        {
            m_Item = item.Copy();
            m_Host = host;
            AddToClassList(BaseField<string>.ussClassName);
            AddToClassList(BaseField<string>.alignedFieldUssClassName);
            style.flexDirection = FlexDirection.Row;
            var label = new Label("Component");
            label.AddToClassList(BaseField<string>.labelUssClassName);
            Add(label);
            Pick = new Button(Open) { text = Text() };
            Pick.style.flexGrow = 1f;
            Add(Pick);
        }

        internal string Text()
        {
            string type = m_Item.FindPropertyRelative("m_ComponentType").stringValue;
            if (string.IsNullOrEmpty(type))
                return NoneText;
            int dot = type.LastIndexOf('.');
            return (dot >= 0 ? type.Substring(dot + 1) : type) + " (" + m_Item.FindPropertyRelative("m_ComponentIndex").intValue + ")";
        }

        // the target's object — the runtime's resolution with the host
        internal GameObject Target()
        {
            if (m_Host == null || !(m_Item.managedReferenceValue is ComponentEnabledItem item))
                return null;
            var context = new PrepareContext(m_Host.gameObject, m_Host as FeedbackPlayer, null, 1);
            return TargetResolver.FindObject(in item.m_Target, in context, out _);
        }

        void Open()
        {
            var menu = new GenericMenu();
            List<(Component, string)> found = ComponentPick.Candidates(Target());
            if (found.Count == 0)
                menu.AddDisabledItem(new GUIContent("No Behaviour, Renderer or Collider on the target"));
            foreach ((Component c, string name) in found)
            {
                Component pick = c;
                menu.AddItem(new GUIContent(name), false, () => Choose(pick));
            }
            menu.ShowAsContext();
        }

        internal void Choose(Component c)
        {
            ComponentPick.Write(m_Item, c);
            Pick.text = Text();
        }
    }
}
