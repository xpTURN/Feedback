using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // a motion with no public constructor — the record makes it all the same
    [Serializable]
    internal sealed class KeepTestMotion : FeedbackMotion
    {
        KeepTestMotion()
        {
        }

        [SerializeField] internal float m_X = 0.5f;

        internal override MotionKind Kind => MotionKind.Set;
    }

    // a motion whose constructor throws while `Throw` is set — a player whose record cannot be written
    [Serializable]
    internal sealed class KeepThrowingMotion : FeedbackMotion
    {
        internal static bool Throw;

        KeepThrowingMotion()
        {
            if (Throw)
                throw new InvalidOperationException("KeepThrowingMotion");
        }

        internal override MotionKind Kind => MotionKind.Set;
    }

    // the fixtures — a saved scene (an unsaved scene's objects have no id), a prefab with a child, presets; the play mode
    // options set by enterPlayModeOptions (its Enabled flag is ignored) and put back from SessionState; a reload empties a
    // test's locals — the values are read again by name
    internal static class KeepTesting
    {
        internal const string Dir = "Assets/KeepTests";
        internal const string ScenePath = Dir + "/Keep.unity";
        internal const string PrefabPath = Dir + "/Keep.prefab";
        internal const string PresetPath = Dir + "/Keep.asset";
        internal const string Preset2Path = Dir + "/Keep2.asset";
        const string Key = "xpTURN.Feedback.KeepTests.";

        static PreviewBarTestWindow s_Window;

        internal static Scene NewScene()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "KeepTests");
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            Scene s = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(s);
            return s;
        }

        internal static void Clean()
        {
            if (s_Window != null)
                s_Window.Close();
            s_Window = null;
            StageUtility.GoToMainStage();
            if (SceneManager.sceneCount > 0)
                SceneManager.SetActiveScene(SceneManager.GetSceneAt(0));
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.path == ScenePath && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(s, true);
            }
            AssetDatabase.DeleteAsset(Dir);
            SessionState.EraseString(KeepPlayModeChanges.MarksKey);
            SessionState.EraseString(KeepPlayModeChanges.DataKey);
            EditorPrefs.DeleteKey(KeepPlayModeChanges.TurnOffKey);
            KeepThrowingMotion.Throw = false;
            RestoreOptions();
            Undo.ClearAll();
        }

        internal static PositionItem Tween(float duration, string label = "")
        {
            var item = new PositionItem { m_Motion = new TweenMotion { m_Params = new TweenParams { m_To = Vector4.one, m_Loops = 1 } }, m_Label = label };
            item.m_Target.m_Mode = TargetRefMode.Self;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        internal static FeedbackPlayer Player(string name, params FeedbackItem[] items)
        {
            var p = new GameObject(name).AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            return p;
        }

        internal static GameObject Prefab(string name)
        {
            var source = new GameObject(name);
            new GameObject("Child").transform.SetParent(source.transform);
            source.AddComponent<FeedbackPlayer>().m_Items.Add(Tween(0.5f));
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, PrefabPath);
            Object.DestroyImmediate(source);
            return prefab;
        }

        internal static FeedbackPreset Preset(string path, params FeedbackItem[] items)
        {
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            preset.m_Items.AddRange(items);
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            return preset;
        }

        // in play mode, by name
        internal static FeedbackPlayer Find(string name)
        {
            GameObject go = GameObject.Find(name);
            return go != null ? go.GetComponent<FeedbackPlayer>() : null;
        }

        // in edit mode, a root of the saved scene
        internal static GameObject Root(string name)
        {
            Scene s = SceneManager.GetSceneByPath(ScenePath);
            return s.IsValid() ? s.GetRootGameObjects().FirstOrDefault(g => g.name == name) : null;
        }

        internal static FeedbackPlayer InScene(string name) => Root(name)?.GetComponent<FeedbackPlayer>();

        internal static void SetOptions(EnterPlayModeOptions options)
        {
            if (!SessionState.GetBool(Key + "Saved", false))
            {
                SessionState.SetBool(Key + "Saved", true);
                SessionState.SetInt(Key + "Options", (int)EditorSettings.enterPlayModeOptions);
                SessionState.SetBool(Key + "Enabled", EditorSettings.enterPlayModeOptionsEnabled);
            }
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = options;
        }

        static void RestoreOptions()
        {
            if (!SessionState.GetBool(Key + "Saved", false))
                return;
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(Key + "Options", 0);
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(Key + "Enabled", false);
            SessionState.EraseBool(Key + "Saved");
        }

        // an inspector's elements in a window, bound — the batch run draws no inspector by itself
        internal static VisualElement Draw(params Object[] targets)
        {
            if (s_Window == null)
            {
                s_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
                s_Window.Show();
            }
            s_Window.rootVisualElement.Clear();
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(targets);
            VisualElement root = editor.CreateInspectorGUI();
            s_Window.rootVisualElement.Add(root);
            root.Bind(editor.serializedObject);
            return root;
        }

        internal static void CloseWindow()
        {
            if (s_Window != null)
                s_Window.Close();
            s_Window = null;
        }

        // hidden · on · off
        internal static string Shown(VisualElement e) => e == null ? "absent" : e.style.display == DisplayStyle.None || e.resolvedStyle.display == DisplayStyle.None ? "hidden" : e.enabledSelf ? "on" : "off";

        // every property but the header: path = value (a managed reference by its type, a reference by its entity id)
        internal static List<string> Snap(Object target)
        {
            var header = new HashSet<string> { "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject", "m_Enabled", "m_EditorHideFlags", "m_Script", "m_Name", "m_EditorClassIdentifier", "m_FormatVersion" };
            var lines = new List<string>();
            SerializedProperty p = new SerializedObject(target).GetIterator();
            bool enter = true;
            while (p.Next(enter))
            {
                enter = true;
                if (p.depth == 0 && header.Contains(p.name))
                {
                    enter = false;
                    continue;
                }
                string v;
                switch (p.propertyType)
                {
                    case SerializedPropertyType.ObjectReference: enter = false; v = p.objectReferenceValue != null ? p.objectReferenceValue.GetEntityId().ToString() : "null"; break;
                    case SerializedPropertyType.ManagedReference: v = p.managedReferenceFullTypename; break;
                    case SerializedPropertyType.Float: v = BitConverter.SingleToInt32Bits(p.floatValue).ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Enum:
                    case SerializedPropertyType.ArraySize: v = p.longValue.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Boolean: v = p.boolValue.ToString(); break;
                    case SerializedPropertyType.String: v = p.stringValue; break;
                    default: v = p.propertyType.ToString(); break;
                }
                lines.Add(p.propertyPath + " = " + v);
            }
            return lines;
        }
    }
}
