using System;
using System.Collections.Generic;
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
    // the presets' fixtures — a folder of preset assets, players, a window for the inspectors — removed after each test
    internal sealed class PresetFixtures
    {
        internal const string Dir = "Assets/PresetTests";

        internal readonly List<Object> Made = new List<Object>();
        PreviewBarTestWindow m_Window;

        internal PresetFixtures()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
                AssetDatabase.CreateFolder("Assets", "PresetTests");
        }

        internal GameObject Go(string name)
        {
            var go = new GameObject(name);
            Made.Add(go);
            return go;
        }

        internal GameObject Cube(string name)
        {
            GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = name;
            Made.Add(c);
            return c;
        }

        internal FeedbackPlayer Player(string name, params FeedbackItem[] items)
        {
            var p = Go(name).AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            return p;
        }

        internal FeedbackPreset Preset(string name, params FeedbackItem[] items)
        {
            var p = ScriptableObject.CreateInstance<FeedbackPreset>();
            p.m_Items.AddRange(items);
            AssetDatabase.CreateAsset(p, Dir + "/" + name + ".asset");
            AssetDatabase.SaveAssets();
            return p;
        }

        internal static PositionItem Tween(float duration, string label = "", TargetRefMode mode = TargetRefMode.Self)
        {
            var t = new PositionItem { m_Motion = new TweenMotion { m_Params = new TweenParams { m_To = Vector4.one, m_Loops = 1 } }, m_Label = label };
            t.m_Target.m_Mode = mode;
            t.m_Timing.m_Duration = duration;
            return t;
        }

        internal static PositionItem Direct(Object target, string label = "")
        {
            PositionItem t = Tween(0.2f, label, TargetRefMode.Direct);
            t.m_Target.m_Object = target;
            return t;
        }

        // an inspector's elements in a window, bound — the batch run draws no inspector by itself
        internal VisualElement Draw(UnityEditor.Editor editor)
        {
            if (m_Window == null)
            {
                m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
                m_Window.Show();
            }
            m_Window.rootVisualElement.Clear();
            VisualElement root = editor.CreateInspectorGUI();
            m_Window.rootVisualElement.Add(root);
            root.Bind(editor.serializedObject);
            Made.Add(editor);
            return root;
        }

        internal void Clean()
        {
            if (m_Window != null)
                m_Window.Close();
            m_Window = null;
            foreach (Object o in Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            Made.Clear();
            PreviewSession.Current?.Stop();
            DragAndDrop.PrepareStartDrag();
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.path.StartsWith(ValidationFixtures.Dir, StringComparison.Ordinal) && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(s, true);
            }
            AssetDatabase.DeleteAsset(Dir);
            AssetDatabase.DeleteAsset(ValidationFixtures.Dir);
            Undo.ClearAll();
        }
    }
}
