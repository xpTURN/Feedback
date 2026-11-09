using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // the menu scans — the open scenes (an open prefab stage too) or the prefabs under Assets, the project's rules with them —
    // and a line in the console
    internal static class ValidatorScan
    {
        internal static List<RuleResult> Last = new List<RuleResult>();

        internal static IEnumerable<Component> OpenSceneHosts()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded)
                    continue;
                foreach (GameObject root in s.GetRootGameObjects())
                {
                    foreach (FeedbackPlayer p in root.GetComponentsInChildren<FeedbackPlayer>(true))
                        yield return p;
                    foreach (MotionDriver d in root.GetComponentsInChildren<MotionDriver>(true))
                        yield return d;
                    foreach (Component b in ProgressBars(root))
                        yield return b;
                    foreach (Component t in UITriggers(root))
                        yield return t;
                }
            }
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                foreach (FeedbackPlayer p in stage.prefabContentsRoot.GetComponentsInChildren<FeedbackPlayer>(true))
                    yield return p;
                foreach (MotionDriver d in stage.prefabContentsRoot.GetComponentsInChildren<MotionDriver>(true))
                    yield return d;
                foreach (Component b in ProgressBars(stage.prefabContentsRoot))
                    yield return b;
                foreach (Component t in UITriggers(stage.prefabContentsRoot))
                    yield return t;
            }
        }

        // a progress bar is FB-P10's host (its type is the UGUI assembly's)
        static Component[] ProgressBars(GameObject root)
            => UGUITypes.ProgressBar != null ? root.GetComponentsInChildren(UGUITypes.ProgressBar, true) : System.Array.Empty<Component>();

        // a UI trigger is FB-P12's and FB-P13's host (its type is the UGUI assembly's)
        static Component[] UITriggers(GameObject root)
            => UGUITypes.UITrigger != null ? root.GetComponentsInChildren(UGUITypes.UITrigger, true) : System.Array.Empty<Component>();

        internal static IEnumerable<Component> PrefabHosts()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go == null)
                    continue;
                foreach (FeedbackPlayer p in go.GetComponentsInChildren<FeedbackPlayer>(true))
                    yield return p;
                foreach (MotionDriver d in go.GetComponentsInChildren<MotionDriver>(true))
                    yield return d;
                foreach (Component b in ProgressBars(go))
                    yield return b;
                foreach (Component t in UITriggers(go))
                    yield return t;
            }
        }

        internal static IEnumerable<Object> PresetHosts()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(FeedbackPreset), new[] { "Assets" }))
            {
                var p = AssetDatabase.LoadAssetAtPath<FeedbackPreset>(AssetDatabase.GUIDToAssetPath(guid));
                if (p != null)
                    yield return p;
            }
        }

        internal static List<RuleResult> Scan(IEnumerable<Object> hosts, string what)
        {
            var results = new List<RuleResult>();
            int count = 0;
            foreach (Object host in hosts)
            {
                count++;
                results.AddRange(FeedbackValidator.Run(host));
            }
            results.AddRange(FeedbackValidator.RunProject());
            Last = results;
            string summary = FeedbackValidator.Summary(results);
            string line = "Feedback validation (" + what + "): " + count + " hosts — " + (summary.Length > 0 ? summary : "nothing to report");
            if (results.Count > 0)
                Debug.LogWarning(line);
            else
                Debug.Log(line);
            return results;
        }

        [MenuItem("Tools/xpTURN/Feedback/Validate Open Scenes")]
        static void ValidateOpenScenes()
        {
            Scan(OpenSceneHosts(), "open scenes");
            FeedbackValidatorWindow.Open();
        }

        [MenuItem("Tools/xpTURN/Feedback/Validate Presets")]
        static void ValidatePresets()
        {
            Scan(PresetHosts(), "presets");
            FeedbackValidatorWindow.Open();
        }

        [MenuItem("Tools/xpTURN/Feedback/Validate Prefabs")]
        static void ValidatePrefabs()
        {
            Scan(PrefabHosts(), "prefabs");
            FeedbackValidatorWindow.Open();
        }

        // a host's rows again — after a fix
        internal static void Refresh(Object host)
        {
            Last.RemoveAll(r => r.Host == host && r.Rule.Scope == RuleScope.Host);
            if (host is Component || host is FeedbackPreset)
                Last.AddRange(FeedbackValidator.Run(host));
        }

        // a row picked: its item's fold opened (the inspector draws it open), its host selected
        internal static void Select(RuleResult r)
        {
            if (r.Host is FeedbackPlayer p && r.Item >= 0 && r.Item < p.Items.Count && p.Items[r.Item] != null)
            {
                long rid = UnityEngine.Serialization.ManagedReferenceUtility.GetManagedReferenceIdForObject(PresetLinks.Owner(p), p.Items[r.Item]);
                SessionState.SetBool(ItemListView.FoldKey(p, rid), true);
            }
            else if (r.Host is FeedbackPreset preset && r.Item >= 0 && r.Item < preset.m_Items.Count && preset.m_Items[r.Item] != null)
            {
                long rid = UnityEngine.Serialization.ManagedReferenceUtility.GetManagedReferenceIdForObject(preset, preset.m_Items[r.Item]);
                SessionState.SetBool(ItemListView.FoldKey(preset, rid), true);
            }
            Selection.activeObject = r.Host;
            if (r.Host != null && EditorUtility.IsPersistent(r.Host))
                EditorGUIUtility.PingObject(r.Host);
        }
    }

    internal sealed class FeedbackValidatorWindow : EditorWindow
    {
        internal const string Title = "Feedback Validator";
        internal readonly bool[] Shown = { true, true, true };

        [MenuItem("Window/xpTURN/Feedback Validator")]
        internal static FeedbackValidatorWindow Open()
        {
            var w = GetWindow<FeedbackValidatorWindow>(Title);
            w.Rebuild();
            return w;
        }

        void CreateGUI() => Rebuild();

        internal void Rebuild()
        {
            rootVisualElement.Clear();
            InspectorStyles.Attach(rootVisualElement);
            var bar = new VisualElement();
            bar.AddToClassList(InspectorStyles.Row);
            foreach (RuleSeverity s in new[] { RuleSeverity.Error, RuleSeverity.Warning, RuleSeverity.Info })
            {
                RuleSeverity severity = s;
                var toggle = new Toggle(s.ToString()) { value = Shown[(int)s] };
                toggle.RegisterValueChangedCallback(e =>
                {
                    Shown[(int)severity] = e.newValue;
                    Rebuild();
                });
                bar.Add(toggle);
            }
            bar.Add(new Button(() =>
            {
                ValidatorScan.Scan(ValidatorScan.OpenSceneHosts(), "open scenes");
                Rebuild();
            }) { text = "Validate Open Scenes" });
            rootVisualElement.Add(bar);
            var scroll = new ScrollView();
            foreach (RuleResult r in ValidatorScan.Last)
            {
                if (!Shown[(int)r.Severity])
                    continue;
                VisualElement row = ResultRows.Row(r, (x, f) =>
                {
                    FeedbackValidator.Apply(x, f);
                    ValidatorScan.Refresh(x.Host);
                    Rebuild();
                });
                RuleResult result = r;
                var pick = new Button(() => ValidatorScan.Select(result)) { text = r.Host != null ? r.Host.name : "(gone)" };
                pick.AddToClassList(InspectorStyles.ValidationPick);
                row.Insert(0, pick);
                scroll.Add(row);
            }
            rootVisualElement.Add(scroll);
        }
    }
}
