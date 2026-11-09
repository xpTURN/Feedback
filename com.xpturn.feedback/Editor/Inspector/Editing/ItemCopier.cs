using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // a deep copy by the serialization — a new instance of the type and its fields copied over
    // (CopySerializedManagedFieldsOnly): the motion, the curves, the gradients and the UnityEvents are their own, a scene
    // reference is the same object. A pasted slot's references into another scene than the host's are cleared — kept, they
    // are lost on save
    internal static class ItemCopier
    {
        internal static FeedbackItem Copy(FeedbackItem source)
        {
            if (source == null)
                return null;
            var copy = (FeedbackItem)System.Activator.CreateInstance(source.GetType(), true);
            EditorUtility.CopySerializedManagedFieldsOnly(source, copy);
            return copy;
        }

        internal static List<FeedbackItem> Copies(IEnumerable<FeedbackItem> sources)
        {
            var copies = new List<FeedbackItem>();
            foreach (FeedbackItem item in sources)
                copies.Add(Copy(item));
            return copies;
        }

        // the object references in the slots [at, at + count) that point into another scene than the host's (a listener's
        // target too) — cleared, each slot applied (an Update between would drop the clears not applied)
        internal static int ClearOtherScene(SerializedObject so, string listPath, int at, int count)
        {
            int cleared = 0;
            for (int i = at; i < at + count; i++)
                cleared += ClearOtherScene(so, listPath + ".Array.data[" + i + "]");
            return cleared;
        }

        // one slot's references into another scene — a list's slot or a MotionDriver's item
        internal static int ClearOtherScene(SerializedObject so, string slotPath)
        {
            var host = so.targetObject as Component;
            SerializedProperty slot = so.FindProperty(slotPath);
            if (slot == null)
                return 0;
            int cleared = 0;
            SerializedProperty p = slot.Copy();
            SerializedProperty end = slot.GetEndProperty();
            bool enter = true;
            while (p.Next(enter) && !SerializedProperty.EqualContents(p, end))
            {
                enter = true;
                if (p.propertyType != SerializedPropertyType.ObjectReference)
                    continue;
                Object o = p.objectReferenceValue;
                if (o != null && TargetChecks.Check(host, o) == TargetRefusal.OtherScene)
                {
                    p.objectReferenceValue = null;
                    cleared++;
                }
            }
            so.ApplyModifiedProperties();
            return cleared;
        }
    }
}
