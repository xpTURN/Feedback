using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // presets dropped on the hierarchy — on an object (the DropUpon flag) a player is added if none and filled; anywhere else
    // a new object named after the preset. One undo group
    [InitializeOnLoad]
    internal static class PresetDrop
    {
        internal const string LinkKey = "xpTURN.Feedback.Presets.DropLinks";
        internal const string UndoName = "Add Feedback Preset";
        internal const string LinkedWarning = "The player is linked to a preset — unlink it first: ";

#if UNITY_6000_3_OR_NEWER
        static PresetDrop() => DragAndDrop.AddDropHandlerV2(OnHierarchyDrop);

        static Object ToObject(EntityId id) => EditorUtility.EntityIdToObject(id);
#else
        static PresetDrop() => DragAndDrop.AddDropHandler(OnHierarchyDrop);

        static Object ToObject(int id) => EditorUtility.InstanceIDToObject(id);
#endif


        internal static bool Links
        {
            get => EditorPrefs.GetBool(LinkKey, false);
            set => EditorPrefs.SetBool(LinkKey, value);
        }

#if UNITY_6000_3_OR_NEWER
        internal static DragAndDropVisualMode OnHierarchyDrop(EntityId target, HierarchyDropFlags flags, Transform parent, bool perform)
#else
        internal static DragAndDropVisualMode OnHierarchyDrop(int target, HierarchyDropFlags flags, Transform parent, bool perform)
#endif
        {
            List<FeedbackPreset> presets = DragAndDrop.objectReferences.OfType<FeedbackPreset>().ToList();
            if (presets.Count == 0)
                return DragAndDropVisualMode.None;
            bool between = (flags & HierarchyDropFlags.DropBetween) != 0;
            // the hierarchy's own drop refuses a place between items while searching
            if (between && (flags & HierarchyDropFlags.SearchActive) != 0)
                return DragAndDropVisualMode.Rejected;
            var go = (flags & HierarchyDropFlags.DropUpon) != 0 ? ToObject(target) as GameObject : null;
            FeedbackPlayer existing = go != null ? go.GetComponent<FeedbackPlayer>() : null;
            if (existing != null && existing.m_Preset != null)
            {
                if (perform)
                    Debug.LogWarning(LinkedWarning + existing.name, existing);
                return DragAndDropVisualMode.Rejected;
            }
            bool link = Links && presets.Count == 1 && existing == null;
            if (!link && presets.Any(p => !PresetOps.CanCopy(p)))
                return DragAndDropVisualMode.Rejected;
            if (!perform)
                return link ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Copy;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            int group = Undo.GetCurrentGroup();
            if (go == null)
            {
                go = new GameObject(presets[0].name);
                Undo.RegisterCreatedObjectUndo(go, UndoName);
                GameObject near = between ? ToObject(target) as GameObject : null;
                if (near != null)
                    PlaceAt(go.transform, near.transform, flags, parent);
                else if (parent != null)
                    Undo.SetTransformParent(go.transform, parent, UndoName);
                else
                    StageUtility.PlaceGameObjectInCurrentStage(go);
            }
            FeedbackPlayer player = existing != null ? existing : Undo.AddComponent<FeedbackPlayer>(go);
            if (link)
                PresetOps.Link(player, presets[0]);
            else
            {
                var so = new SerializedObject(player);
                foreach (FeedbackPreset p in presets)
                    PresetOps.CopyInto(so, ItemListEdits.ItemsPath, -1, p);
            }
            // inside the group: a selection after it is an undo step of its own
            Selection.activeGameObject = go;
            Undo.CollapseUndoOperations(group);
            return link ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Copy;
        }

        // where the hierarchy draws the line, as its own drop places an object — under the target's parent after the target,
        // before it above, the parent's first place after the parent, in the target's scene. The parent the hierarchy passes is
        // the default one (a prefab stage's root), not the place: it takes the new object only where the target has no parent
        // inside a prefab stage
        static void PlaceAt(Transform made, Transform near, HierarchyDropFlags flags, Transform parent)
        {
            Transform under = near.parent;
            int index = (flags & HierarchyDropFlags.DropAfterParent) != 0 ? 0
                : near.GetSiblingIndex() + ((flags & HierarchyDropFlags.DropAbove) != 0 ? 0 : 1);
            if (under == null && StageUtility.GetStage(near.gameObject) is PrefabStage stage)
            {
                under = parent != null ? parent : stage.prefabContentsRoot.transform;
                index = -1;
            }
            if (under != null)
                Undo.SetTransformParent(made, under, UndoName);
            else if (made.gameObject.scene != near.gameObject.scene)
                Undo.MoveGameObjectToScene(made.gameObject, near.gameObject.scene, UndoName);
            if (index >= 0)
                Undo.SetSiblingIndex(made, index, UndoName);
        }
    }
}
