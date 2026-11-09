using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // the list's structure edits — every new slot holds the instance given (never the bare InsertArrayElementAtIndex: its
    // slot is the next one's object), one undo group each. A missing item type locks delete and move (they drop the missing
    // data); an add goes to the end then. Copy, paste and duplicate are deep copies; a paste clears its references into
    // another scene in the same undo group
    internal static class ItemListEdits
    {
        internal const string ItemsPath = "m_Items";
        internal const string PasteClearedWarning = "Pasted items pointed into another scene — those references were cleared (a reference across scenes is lost on save): ";

        internal static bool Locked(SerializedObject so) => Locked(so.targetObject);

        // the one missing-type check — a scene's prefab instance through its source, a ScriptableObject asset without the
        // prefab query
        internal static bool Locked(Object target) => MissingTypes.Has(target);

        static int Begin(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }

        // the items go in at `at` (−1 or past the end = the end; the end when locked) — the place they went
        internal static int Insert(SerializedObject so, string listPath, int at, IReadOnlyList<FeedbackItem> items, string name)
            => Insert(so, listPath, at, items, name, false, out _);

        // clearOtherScene: the new slots' references into another scene are cleared in the same undo group
        internal static int Insert(SerializedObject so, string listPath, int at, IReadOnlyList<FeedbackItem> items, string name, bool clearOtherScene, out int cleared)
        {
            int group = Begin(name);
            so.Update();
            SerializedProperty list = so.FindProperty(listPath);
            if (at < 0 || at > list.arraySize || Locked(so))
                at = list.arraySize;
            for (int i = 0; i < items.Count; i++)
            {
                list.InsertArrayElementAtIndex(at + i);
                list.GetArrayElementAtIndex(at + i).managedReferenceValue = items[i];
            }
            so.ApplyModifiedProperties();
            cleared = clearOtherScene ? ItemCopier.ClearOtherScene(so, listPath, at, items.Count) : 0;
            Undo.CollapseUndoOperations(group);
            return at;
        }

        // the slots' items in the list's order
        static List<FeedbackItem> Items(SerializedObject so, string listPath, IReadOnlyList<int> indices)
        {
            so.Update();
            SerializedProperty list = so.FindProperty(listPath);
            var sorted = new List<int>(indices);
            sorted.Sort();
            var items = new List<FeedbackItem>(sorted.Count);
            for (int k = 0; k < sorted.Count; k++)
            {
                int i = sorted[k];
                if (i < 0 || i >= list.arraySize || (k > 0 && i == sorted[k - 1]))
                    continue;
                if (list.GetArrayElementAtIndex(i).managedReferenceValue is FeedbackItem item)
                    items.Add(item);
            }
            return items;
        }

        internal static int CopyToClipboard(SerializedObject so, string listPath, IReadOnlyList<int> indices)
        {
            List<FeedbackItem> items = Items(so, listPath, indices);
            if (items.Count > 0)
                ItemClipboard.Set(items);
            return items.Count;
        }

        // the clipboard's copies at `at` (−1 = the end) — the place they went, or −1 when it is empty
        internal static int Paste(SerializedObject so, string listPath, int at, out int cleared)
        {
            cleared = 0;
            List<FeedbackItem> items = ItemClipboard.Take();
            if (items.Count == 0)
                return -1;
            at = Insert(so, listPath, at, items, items.Count > 1 ? "Paste Feedbacks" : "Paste Feedback", true, out cleared);
            if (cleared > 0)
                Debug.LogWarning(PasteClearedWarning + cleared, so.targetObject);
            return at;
        }

        // the slots' copies after the last of them
        internal static int Duplicate(SerializedObject so, string listPath, IReadOnlyList<int> indices)
        {
            List<FeedbackItem> items = Items(so, listPath, indices);
            if (items.Count == 0)
                return -1;
            int last = -1;
            foreach (int i in indices)
                last = last < i ? i : last;
            return Insert(so, listPath, last + 1, ItemCopier.Copies(items), items.Count > 1 ? "Duplicate Feedbacks" : "Duplicate Feedback");
        }

        internal static bool Delete(SerializedObject so, string listPath, IReadOnlyList<int> indices)
        {
            if (Locked(so) || indices.Count == 0)
                return false;
            int group = Begin(indices.Count > 1 ? "Delete Feedbacks" : "Delete Feedback");
            so.Update();
            SerializedProperty list = so.FindProperty(listPath);
            var sorted = new List<int>(indices);
            sorted.Sort();
            for (int k = sorted.Count - 1; k >= 0; k--)
            {
                if (sorted[k] >= 0 && sorted[k] < list.arraySize && (k == sorted.Count - 1 || sorted[k] != sorted[k + 1]))
                    list.DeleteArrayElementAtIndex(sorted[k]);
            }
            so.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(group);
            return true;
        }

        internal static bool Move(SerializedObject so, string listPath, int from, int to)
        {
            if (Locked(so))
                return false;
            so.Update();
            SerializedProperty list = so.FindProperty(listPath);
            if (from < 0 || from >= list.arraySize || to < 0 || to >= list.arraySize || from == to)
                return false;
            int group = Begin("Move Feedback");
            list.MoveArrayElement(from, to);
            so.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(group);
            return true;
        }
    }
}
