using System.IO;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // the assets' menus — the settings in Assets/Resources (an existing Resources/FeedbackSettings is chosen first; two are
    // the validation's warning) and a channel
    internal static class AssetMenus
    {
        internal const string SettingsMenu = "Assets/Create/xpTURN/Feedback Settings";
        internal const string ChannelMenu = "Assets/Create/xpTURN/Feedback Channel";
        internal const string PresetMenu = "Assets/Create/xpTURN/Feedback Preset";
        internal const string ResourcesFolder = "Assets/Resources";
        internal const string SettingsPath = ResourcesFolder + "/" + FeedbackSettings.ResourcePath + ".asset";

        [MenuItem(SettingsMenu, false, 81)]
        static void SettingsMenuItem()
        {
            FeedbackSettings settings = Settings(out _);
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        [MenuItem(ChannelMenu, false, 82)]
        static void ChannelMenuItem() => ProjectWindowUtil.CreateAsset(ScriptableObject.CreateInstance<FeedbackChannel>(), "New Feedback Channel.asset");

        [MenuItem(PresetMenu, false, 83)]
        static void PresetMenuItem() => ProjectWindowUtil.CreateAsset(ScriptableObject.CreateInstance<FeedbackPreset>(), "New Feedback Preset.asset");

        // a path Resources.Load(FeedbackSettings.ResourcePath) reads — directly in a Resources folder
        internal static bool IsRuntimeSettings(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            path = path.Replace('\\', '/');
            return Path.GetFileNameWithoutExtension(path) == FeedbackSettings.ResourcePath && Path.GetFileName(Path.GetDirectoryName(path)) == "Resources";
        }

        // the settings the runtime reads — the existing one, else a new one in Assets/Resources
        internal static FeedbackSettings Settings(out bool created)
        {
            created = false;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(FeedbackSettings)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsRuntimeSettings(path))
                    return AssetDatabase.LoadAssetAtPath<FeedbackSettings>(path);
            }
            if (!AssetDatabase.IsValidFolder(ResourcesFolder))
                AssetDatabase.CreateFolder("Assets", "Resources");
            var settings = ScriptableObject.CreateInstance<FeedbackSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            created = true;
            return settings;
        }
    }
}
