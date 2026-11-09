using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // what an animator's controller names — its parameters, layers and states (an override controller's base). The editor
    // reads the controller asset: the rules and the pick fields judge without a play
    internal static class AnimatorNames
    {
        internal static AnimatorController Controller(Animator animator)
        {
            RuntimeAnimatorController r = animator != null ? animator.runtimeAnimatorController : null;
            while (r is AnimatorOverrideController o)
                r = o.runtimeAnimatorController;
            return r as AnimatorController;
        }

        internal static AnimatorControllerParameterType TypeOf(AnimatorParameterKind kind)
            => kind == AnimatorParameterKind.Trigger ? AnimatorControllerParameterType.Trigger
                : kind == AnimatorParameterKind.Bool ? AnimatorControllerParameterType.Bool
                : kind == AnimatorParameterKind.Int ? AnimatorControllerParameterType.Int : AnimatorControllerParameterType.Float;

        // the parameters a clip curve drives (an Animator-typed curve named as the parameter) — a write to one is ignored
        internal static HashSet<string> CurveDriven(AnimatorController controller)
        {
            var driven = new HashSet<string>();
            foreach (AnimationClip clip in controller.animationClips)
            {
                if (clip == null)
                    continue;
                foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (b.type == typeof(Animator))
                        driven.Add(b.propertyName);
                }
            }
            return driven;
        }

        // the states of a layer (every layer when none is named) — the short name and the path 'Layer.Machine.State'
        internal static List<string> States(AnimatorController controller, string layer)
        {
            var names = new List<string>();
            foreach (AnimatorControllerLayer l in controller.layers)
            {
                if (string.IsNullOrEmpty(layer) || l.name == layer)
                    Walk(l.stateMachine, l.name, names);
            }
            return names;
        }

        static void Walk(AnimatorStateMachine machine, string path, List<string> into)
        {
            if (machine == null)
                return;
            foreach (ChildAnimatorState s in machine.states)
            {
                into.Add(s.state.name);
                into.Add(path + "." + s.state.name);
            }
            foreach (ChildAnimatorStateMachine m in machine.stateMachines)
                Walk(m.stateMachine, path + "." + m.stateMachine.name, into);
        }

        internal static int LayerIndex(AnimatorController controller, string layer)
        {
            AnimatorControllerLayer[] layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].name == layer)
                    return i;
            }
            return -1;
        }

        // a state machine behaviour on any layer, machine or state
        internal static bool HasBehaviours(AnimatorController controller)
        {
            foreach (AnimatorControllerLayer l in controller.layers)
            {
                if (HasBehaviours(l.stateMachine))
                    return true;
            }
            return false;
        }

        static bool HasBehaviours(AnimatorStateMachine machine)
        {
            if (machine == null)
                return false;
            if (machine.behaviours.Length > 0)
                return true;
            foreach (ChildAnimatorState s in machine.states)
            {
                if (s.state.behaviours.Length > 0)
                    return true;
            }
            foreach (ChildAnimatorStateMachine m in machine.stateMachines)
            {
                if (HasBehaviours(m.stateMachine))
                    return true;
            }
            return false;
        }
    }

    internal enum AnimatorNameKind
    {
        Parameter,
        State,
        Layer,
    }

    // a name an animator item keeps — typed, or picked from the target Animator's controller (a Parameter's of its kind · a
    // Float Parameter's floats · the states of the named layer · the layers)
    internal sealed class AnimatorNameField : VisualElement
    {
        internal const string NoneText = "No Animator with a controller on the target";

        readonly SerializedProperty m_Item;
        readonly SerializedProperty m_Name;
        readonly Component m_Host;
        readonly AnimatorNameKind m_Kind;
        internal readonly TextField Text;
        internal readonly Button Pick;

        internal AnimatorNameField(SerializedProperty item, string field, string label, Component host, AnimatorNameKind kind)
        {
            m_Item = item.Copy();
            m_Name = item.FindPropertyRelative(field).Copy();
            m_Host = host;
            m_Kind = kind;
            style.flexDirection = FlexDirection.Row;
            Text = new TextField(label);
            Text.BindProperty(m_Name);
            Text.style.flexGrow = 1f;
            Add(Text);
            Pick = new Button(Open) { iconImage = Background.FromTexture2D(EditorGUIUtility.IconContent(InspectorStyles.MenuArrowIcon).image as Texture2D) };
            Add(Pick);
        }

        // the target's Animator — the runtime's resolution with the host
        internal Animator Target()
        {
            if (m_Host == null || !(m_Item.managedReferenceValue is TargetItem item))
                return null;
            var context = new PrepareContext(m_Host.gameObject, m_Host as FeedbackPlayer, null, 1);
            return TargetResolver.Find<Animator>(in item.m_Target, in context, out _);
        }

        internal List<string> Candidates()
        {
            var names = new List<string>();
            AnimatorController controller = AnimatorNames.Controller(Target());
            if (controller == null)
                return names;
            switch (m_Kind)
            {
                case AnimatorNameKind.Parameter:
                {
                    AnimatorControllerParameterType type = m_Item.managedReferenceValue is AnimatorParameterItem p ? AnimatorNames.TypeOf(p.m_Kind) : AnimatorControllerParameterType.Float;
                    foreach (AnimatorControllerParameter x in controller.parameters)
                    {
                        if (x.type == type)
                            names.Add(x.name);
                    }
                    break;
                }
                case AnimatorNameKind.State:
                {
                    string layer = m_Item.FindPropertyRelative("m_Layer").stringValue;
                    foreach (string s in AnimatorNames.States(controller, layer))
                    {
                        if (s.IndexOf('.') < 0 && !names.Contains(s))
                            names.Add(s);
                    }
                    break;
                }
                default:
                    foreach (AnimatorControllerLayer l in controller.layers)
                        names.Add(l.name);
                    break;
            }
            return names;
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
