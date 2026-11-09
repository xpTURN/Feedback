using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the motion of a target item — only the motions the target supports. A change puts a new motion from the defaults
    // table; the target and the timing stay
    internal sealed class MotionField : VisualElement
    {
        internal const string UndoName = "Change Motion";
        const string None = "(none)";

        internal readonly PopupField<string> Choice;
        readonly SerializedObject m_So;
        readonly string m_ItemPath;

        internal MotionField(SerializedProperty item, Action changed)
        {
            m_So = item.serializedObject;
            m_ItemPath = item.propertyPath;
            var value = item.managedReferenceValue as TargetItem;
            var choices = new List<string>();
            if (value != null)
            {
                foreach (MotionKind k in Supported(value))
                    choices.Add(k.ToString());
            }
            string current = value?.m_Motion != null ? value.m_Motion.Kind.ToString() : None;
            if (!choices.Contains(current))
                choices.Insert(0, current);
            Choice = new PopupField<string>("Motion", choices, current);
            Choice.RegisterValueChangedCallback(e =>
            {
                if (e.newValue == None || !Enum.TryParse(e.newValue, out MotionKind kind))
                    return;
                Set(m_So, m_ItemPath, kind);
                changed?.Invoke();
            });
            Add(Choice);
        }

        internal static List<MotionKind> Supported(TargetItem item)
        {
            var kinds = new List<MotionKind>();
            foreach (MotionKind k in Enum.GetValues(typeof(MotionKind)))
            {
                if (item.Traits.Supports(k))
                    kinds.Add(k);
            }
            return kinds;
        }

        // a new motion instance from the table, one undo — the target, the timing and the other fields are the item's still
        internal static bool Set(SerializedObject so, string itemPath, MotionKind kind)
        {
            so.Update();
            SerializedProperty item = so.FindProperty(itemPath);
            if (!(item?.managedReferenceValue is TargetItem value) || !value.Traits.Supports(kind))
                return false;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            int group = Undo.GetCurrentGroup();
            item.FindPropertyRelative("m_Motion").managedReferenceValue = MotionDefaults.Create(value.GetType(), kind, out _);
            so.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(group);
            return true;
        }
    }
}
