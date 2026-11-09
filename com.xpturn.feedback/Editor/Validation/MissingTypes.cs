using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // one missing-type check for every place a list lives. A scene object, a prefab stage's object and a ScriptableObject
    // asset answer through SerializationUtility; a prefab asset's component only through PrefabUtility (which throws on
    // anything not a prefab asset); a scene's prefab instance through its source
    internal static class MissingTypes
    {
        internal static bool Has(Object owner)
        {
            if (owner == null)
                return false;
            if (SerializationUtility.HasManagedReferencesWithMissingTypes(owner))
                return true;
            if (EditorUtility.IsPersistent(owner))
                return PrefabUtility.IsPartOfPrefabAsset(owner) && PrefabUtility.HasManagedReferencesWithMissingTypes(owner);
            if (PrefabUtility.IsPartOfPrefabInstance(owner))
            {
                Object source = PrefabUtility.GetCorrespondingObjectFromSource(owner);
                return source != null && PrefabUtility.HasManagedReferencesWithMissingTypes(source);
            }
            return false;
        }

        // the entries cleared — in place for a scene's or a stage's object (its scene marked dirty: the clear neither records
        // an undo nor dirties the scene itself) and a ScriptableObject asset; in a copy of the prefab asset (saved) for a
        // prefab asset or a prefab instance. Not undoable
        internal static int Clear(Object owner)
        {
            if (owner == null)
                return 0;
            if (SerializationUtility.HasManagedReferencesWithMissingTypes(owner))
            {
                var entries = SerializationUtility.GetManagedReferencesWithMissingTypes(owner);
                foreach (var e in entries)
                    SerializationUtility.ClearManagedReferenceWithMissingType(owner, e.referenceId);
                if (owner is Component c && !EditorUtility.IsPersistent(c) && c.gameObject.scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
                else
                    EditorUtility.SetDirty(owner);
                return entries.Length;
            }
            Object asset = EditorUtility.IsPersistent(owner) ? owner : PrefabUtility.GetCorrespondingObjectFromSource(owner);
            string path = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
            if (string.IsNullOrEmpty(path))
                return 0;
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            int cleared = 0;
            try
            {
                foreach (FeedbackPlayer p in contents.GetComponentsInChildren<FeedbackPlayer>(true))
                {
                    var entries = SerializationUtility.GetManagedReferencesWithMissingTypes(p);
                    foreach (var e in entries)
                        SerializationUtility.ClearManagedReferenceWithMissingType(p, e.referenceId);
                    cleared += entries.Length;
                }
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            return cleared;
        }
    }
}
