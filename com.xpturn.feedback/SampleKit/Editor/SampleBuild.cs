using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Samples.Editor
{
    // 'Build Samples': the hub first, then the available cards' scenes, the scenes there before kept after them, the
    // catalogs preloaded (a player's hub finds them). What was there before is written once to Library and 'Restore Build
    // Settings' puts it back exactly. The menus only ask; the work is Apply and Restore
    public static class SampleBuild
    {
        internal const string RestorePath = "Library/xpTURN.Feedback/SampleBuildRestore.json";

        [Serializable]
        sealed class Saved
        {
            public string[] ScenePaths;
            public bool[] SceneEnabled;
            public string[] PreloadedGuids;
        }

        public static List<SampleCatalog> Catalogs()
            => AssetDatabase.FindAssets("t:" + nameof(SampleCatalog)).Select(g => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(g))).OfType<SampleCatalog>().ToList();

        [MenuItem("Tools/xpTURN/Feedback/Samples/Build Samples")]
        static void BuildSamplesMenu()
        {
            if (EditorUtility.DisplayDialog("Build Samples", "Puts the samples hub first in the build list (of the active build profile if one is set), adds the imported samples' scenes after it, keeps the scenes already there, and preloads the sample catalogs. 'Restore Build Settings' puts the previous list back.", "Build Samples", "Cancel"))
                Apply();
        }

        [MenuItem("Tools/xpTURN/Feedback/Samples/Restore Build Settings")]
        static void RestoreMenu()
        {
            if (!File.Exists(RestorePath))
                EditorUtility.DisplayDialog("Restore Build Settings", "There is nothing to restore — 'Build Samples' has not changed the build list.", "OK");
            else if (EditorUtility.DisplayDialog("Restore Build Settings", "Puts back the build list and the preloaded assets from before 'Build Samples'.", "Restore", "Cancel"))
                Restore();
        }

        public static void Apply()
        {
            if (!File.Exists(RestorePath))
            {
                var saved = new Saved
                {
                    ScenePaths = EditorBuildSettings.scenes.Select(s => s.path).ToArray(),
                    SceneEnabled = EditorBuildSettings.scenes.Select(s => s.enabled).ToArray(),
                    PreloadedGuids = PlayerSettings.GetPreloadedAssets().Where(a => a != null).Select(a => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(a))).ToArray(),
                };
                Directory.CreateDirectory(Path.GetDirectoryName(RestorePath));
                File.WriteAllText(RestorePath, JsonUtility.ToJson(saved));
            }
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(SampleKitPaths.HubScene, true) };
            List<SampleCatalog> catalogs = Catalogs();
            foreach (SampleCatalog catalog in catalogs)
                foreach (SampleCard card in catalog.Cards ?? Array.Empty<SampleCard>())
                {
                    string path = card != null ? AssetDatabase.GUIDToAssetPath(card.SceneGuid) : null;
                    if (!string.IsNullOrEmpty(path) && SampleCards.Available(card) && scenes.All(s => s.path != path))
                        scenes.Add(new EditorBuildSettingsScene(path, true));
                }
            foreach (EditorBuildSettingsScene old in EditorBuildSettings.scenes)
                if (scenes.All(s => s.path != old.path))
                    scenes.Add(old);
            EditorBuildSettings.scenes = scenes.ToArray();
            List<UnityEngine.Object> preloaded = PlayerSettings.GetPreloadedAssets().Where(a => a != null).ToList();
            foreach (SampleCatalog catalog in catalogs)
                if (!preloaded.Contains(catalog))
                    preloaded.Add(catalog);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
        }

        public static void Restore()
        {
            if (!File.Exists(RestorePath))
                return;
            var saved = JsonUtility.FromJson<Saved>(File.ReadAllText(RestorePath));
            EditorBuildSettings.scenes = saved.ScenePaths.Select((p, i) => new EditorBuildSettingsScene(p, saved.SceneEnabled[i])).ToArray();
            PlayerSettings.SetPreloadedAssets(saved.PreloadedGuids.Select(g => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(g))).Where(a => a != null).ToArray());
            File.Delete(RestorePath);
        }
    }
}
