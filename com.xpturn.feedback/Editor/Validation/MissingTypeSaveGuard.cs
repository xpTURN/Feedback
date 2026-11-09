using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // a save of a scene, a prefab or a preset whose loaded feedback lists hold items of a missing type. A save in the session
    // the type went missing drops them (one loaded from a file with the type already gone keeps them — the two are not told
    // apart): asked first, the answer kept for the path until the next domain reload. A batch run saves and says so
    internal sealed class MissingTypeSaveGuard : AssetModificationProcessor
    {
        internal const string Title = "Feedback items of a missing type";
        internal const string Question = " hold feedback items whose type is missing (a class renamed, moved or deleted). A save now can drop those items for good — restore the type first, or save and lose them.";
        internal const string KeptWarning = "Not saved — it holds feedback items of a missing type (restore the type, or clear the missing entries): ";
        internal const string BatchWarning = "Saved with feedback items of a missing type — a save can drop them: ";

        // the question for the paths not answered yet — true saves them
        internal static Func<IReadOnlyList<string>, bool> Ask = DefaultAsk;

        static readonly Dictionary<string, bool> s_Answers = new Dictionary<string, bool>();

        internal static void Forget() => s_Answers.Clear();

        static bool DefaultAsk(IReadOnlyList<string> paths)
        {
            if (Application.isBatchMode)
            {
                Debug.LogWarning(BatchWarning + string.Join(", ", paths));
                return true;
            }
            return !EditorUtility.DisplayDialog(Title, string.Join(", ", paths) + Question, "Don't Save", "Save");
        }

        static string[] OnWillSaveAssets(string[] paths) => Filter(paths);

        internal static string[] Filter(string[] paths)
        {
            List<string> held = paths.Where(Holds).ToList();
            if (held.Count == 0)
                return paths;
            List<string> open = held.Where(p => !s_Answers.ContainsKey(p)).ToList();
            if (open.Count > 0)
            {
                bool save = Ask(open);
                foreach (string p in open)
                    s_Answers[p] = save;
            }
            List<string> kept = held.Where(p => !s_Answers[p]).ToList();
            if (kept.Count == 0)
                return paths;
            Debug.LogWarning(KeptWarning + string.Join(", ", kept));
            return paths.Where(p => !kept.Contains(p)).ToArray();
        }

        // what a save of the path writes: a loaded scene's objects, the open prefab stage's or the prefab asset, a preset
        internal static bool Holds(string path)
        {
            if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                return scene.IsValid() && scene.isLoaded && scene.GetRootGameObjects().Any(g => Lists(g).Any(SerializationUtility.HasManagedReferencesWithMissingTypes));
            }
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.assetPath == path)
                    return Lists(stage.prefabContentsRoot).Any(SerializationUtility.HasManagedReferencesWithMissingTypes);
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                return asset != null && Lists(asset).Any(MissingTypes.Has);
            }
            return AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(FeedbackPreset) && MissingTypes.Has(AssetDatabase.LoadAssetAtPath<FeedbackPreset>(path));
        }

        static IEnumerable<Object> Lists(GameObject root)
            => root.GetComponentsInChildren<FeedbackPlayer>(true).Cast<Object>().Concat(root.GetComponentsInChildren<MotionDriver>(true));
    }
}
