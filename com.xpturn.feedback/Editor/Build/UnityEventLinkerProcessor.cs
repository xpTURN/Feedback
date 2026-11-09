using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace xpTURN.Feedback.Editor
{
    // Unity does not hand the linker the persistent calls of a UnityEvent inside [SerializeReference] items — an engine
    // method that nothing else calls vanishes from Minimal, with no error. The build's scenes, the prefabs they use and the
    // Resources prefabs (with what those use) are read with SerializedObject; a link.xml keeps (assembly, type, method) by
    // name. A full build keeps its set in Library per build target and a scripts-only build (it processes no scene, wherever
    // it goes) merges it. A callback never throws nor logs an error — a strict build would fail on it; a bad asset is skipped
    // with a warning. The user's feedback types are kept whole too — one only an asset bundle holds would be stripped from
    // Medium with the asset it is in
    internal sealed class UnityEventLinkerProcessor : IPreprocessBuildWithReport, IProcessSceneWithReport, IUnityLinkerProcessor
    {
        internal const string LinkXmlPath = "Temp/xpTURN.Feedback/UnityEventItem.link.xml";
        static readonly SortedSet<string> s_Methods = new SortedSet<string>(StringComparer.Ordinal);
        static readonly HashSet<string> s_ReadAssets = new HashSet<string>(StringComparer.Ordinal);
        static bool s_ScriptsOnly;
        static string s_Saved;

        public int callbackOrder => 0;

        internal static IReadOnlyCollection<string> Methods => s_Methods;

        // the set a full build of this build target left
        internal static string SavedPath(string key) => Path.Combine("Library", "xpTURN.Feedback", "UnityEventLinker-" + key + ".txt");

        public void OnPreprocessBuild(BuildReport report)
        {
            bool scriptsOnly = report != null && (report.summary.options & BuildOptions.BuildScriptsOnly) != 0;
            Begin(scriptsOnly, report != null ? report.summary.platform.ToString() : "NoTarget");
        }

        // the start of a build — the scene processing and the link.xml follow
        internal static void Begin(bool scriptsOnly, string key)
        {
            s_Methods.Clear();
            s_ReadAssets.Clear();
            s_ScriptsOnly = scriptsOnly;
            s_Saved = SavedPath(key);
            if (s_ScriptsOnly)
            {
                try
                {
                    if (File.Exists(s_Saved))
                        s_Methods.UnionWith(File.ReadAllLines(s_Saved));
                    else
                        Debug.LogWarning("Feedback: a scripts-only build with no full build of this target — the UnityEvent methods of the build's scenes are not kept from stripping. Make a full build first.");
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Feedback: the saved UnityEvent methods could not be read — " + e.Message);
                }
            }
            CollectResources();
        }

        // the prefabs in Resources folders are in every build — no scene names them; and what they use
        static void CollectResources()
        {
            var roots = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0)
                    roots.Add(path);
            }
            if (roots.Count > 0)
                CollectPrefabs(AssetDatabase.GetDependencies(roots.ToArray(), true));
        }

        static void CollectPrefabs(string[] paths)
        {
            foreach (string path in paths)
            {
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || !s_ReadAssets.Add(path))
                    continue;
                try
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                        continue;
                    foreach (FeedbackPlayer player in prefab.GetComponentsInChildren<FeedbackPlayer>(true))
                        Collect(new SerializedObject(player), s_Methods);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Feedback: the UnityEvent calls of '" + path + "' could not be read — " + e.Message);
                }
            }
        }

        // entering play mode calls this too — no report then
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null)
                return;
            ProcessScene(scene);
        }

        internal static void ProcessScene(Scene scene)
        {
            try
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (FeedbackPlayer player in root.GetComponentsInChildren<FeedbackPlayer>(true))
                        Collect(new SerializedObject(player), s_Methods);
                }
                if (!string.IsNullOrEmpty(scene.path))
                    CollectPrefabs(AssetDatabase.GetDependencies(scene.path, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Feedback: the UnityEvent calls of the scene '" + scene.path + "' could not be read — " + e.Message);
            }
        }

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data) => End();

        internal static string End()
        {
            try
            {
                if (!s_ScriptsOnly && s_Saved != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(s_Saved));
                    File.WriteAllLines(s_Saved, s_Methods);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(LinkXmlPath));
                File.WriteAllText(LinkXmlPath, LinkXml(s_Methods, ExtensionTypes()));
                return Path.GetFullPath(LinkXmlPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Feedback: the UnityEvent link.xml could not be written — " + e.Message);
                return null;
            }
        }

        // the persistent calls of every UnityEvent under a player's items — "assembly|type|method"
        internal static void Collect(SerializedObject player, ISet<string> into)
        {
            // the linked preset's list too — once a preset
            SerializedProperty link = player.FindProperty("m_Preset");
            if (link != null && link.objectReferenceValue is FeedbackPreset preset && s_ReadAssets.Add(AssetDatabase.GetAssetPath(preset)))
                Collect(new SerializedObject(preset), into);
            SerializedProperty items = player.FindProperty("m_Items");
            if (items == null || !items.isArray)
                return;
            for (int i = 0; i < items.arraySize; i++)
            {
                SerializedProperty item = items.GetArrayElementAtIndex(i);
                if (item.propertyType != SerializedPropertyType.ManagedReference)
                    continue;
                // every UnityEvent under the item — any field name, a list, a nested class, UnityEvent<T> (the hidden
                // m_PersistentCalls — Next(true)); strings and references are not entered
                SerializedProperty walk = item.Copy();
                SerializedProperty end = item.GetEndProperty();
                bool enter = true;
                while (walk.Next(enter) && !SerializedProperty.EqualContents(walk, end))
                {
                    enter = walk.propertyType == SerializedPropertyType.Generic || walk.propertyType == SerializedPropertyType.ManagedReference;
                    if (walk.name != "m_PersistentCalls")
                        continue;
                    enter = false;
                    SerializedProperty calls = walk.FindPropertyRelative("m_Calls");
                    if (calls != null && calls.isArray)
                        AddCalls(calls, into);
                }
            }
        }

        static void AddCalls(SerializedProperty calls, ISet<string> into)
        {
            for (int c = 0; c < calls.arraySize; c++)
            {
                SerializedProperty call = calls.GetArrayElementAtIndex(c);
                string method = call.FindPropertyRelative("m_MethodName").stringValue;
                string typeName = call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue;
                UnityEngine.Object target = call.FindPropertyRelative("m_Target").objectReferenceValue;
                if (string.IsNullOrEmpty(method))
                    continue;
                if (TryName(typeName, target, out string assembly, out string type))
                    into.Add(assembly + "|" + type + "|" + method);
            }
        }

        // the defining assembly when the written name resolves (Unity writes engine types under the facade "UnityEngine" — the
        // type forwarders); nested types in Cecil's form. Else the written name
        internal static bool TryName(string typeName, UnityEngine.Object target, out string assembly, out string type)
        {
            Type resolved = !string.IsNullOrEmpty(typeName) ? Type.GetType(typeName, false) : null;
            if (resolved == null && target != null)
                resolved = target.GetType();
            if (resolved != null)
            {
                type = resolved.FullName.Replace('+', '/');
                assembly = resolved.Assembly.GetName().Name;
                return true;
            }
            int comma = typeName != null ? typeName.IndexOf(',') : -1;
            if (comma > 0)
            {
                type = typeName.Substring(0, comma).Trim();
                assembly = typeName.Substring(comma + 1).Trim();
                return true;
            }
            type = assembly = null;
            return false;
        }

        // the user's feedback types — "assembly|type", every assembly TypeCache knows (one a player lacks does not fail the link)
        internal static IEnumerable<string> ExtensionTypes()
            => TypeCache.GetTypesDerivedFrom<CustomActionItem>().Concat(TypeCache.GetTypesDerivedFrom<CustomEffectItem>())
                .Where(t => !t.IsAbstract && !t.ContainsGenericParameters).Select(t => t.Assembly.GetName().Name + "|" + t.FullName);

        internal static string LinkXml(IEnumerable<string> methods) => LinkXml(methods, Array.Empty<string>());

        // the methods by (assembly, type), then the kept types — preserve="all"
        internal static string LinkXml(IEnumerable<string> methods, IEnumerable<string> keptTypes)
        {
            var kept = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (string entry in keptTypes)
            {
                string[] parts = entry.Split('|');
                if (parts.Length != 2)
                    continue;
                if (!kept.TryGetValue(parts[0], out SortedSet<string> types))
                    kept[parts[0]] = types = new SortedSet<string>(StringComparer.Ordinal);
                types.Add(parts[1]);
            }
            var byAssembly = new SortedDictionary<string, SortedDictionary<string, SortedSet<string>>>(StringComparer.Ordinal);
            foreach (string entry in methods)
            {
                string[] parts = entry.Split('|');
                if (parts.Length != 3)
                    continue;
                if (!byAssembly.TryGetValue(parts[0], out SortedDictionary<string, SortedSet<string>> types))
                    byAssembly[parts[0]] = types = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
                if (!types.TryGetValue(parts[1], out SortedSet<string> names))
                    types[parts[1]] = names = new SortedSet<string>(StringComparer.Ordinal);
                names.Add(parts[2]);
            }
            var xml = new StringBuilder("<linker>\n");
            foreach (KeyValuePair<string, SortedDictionary<string, SortedSet<string>>> assembly in byAssembly)
            {
                xml.Append("  <assembly fullname=\"").Append(Escape(assembly.Key)).Append("\">\n");
                foreach (KeyValuePair<string, SortedSet<string>> type in assembly.Value)
                {
                    xml.Append("    <type fullname=\"").Append(Escape(type.Key)).Append("\">\n");
                    foreach (string name in type.Value)
                        xml.Append("      <method name=\"").Append(Escape(name)).Append("\"/>\n");
                    xml.Append("    </type>\n");
                }
                xml.Append("  </assembly>\n");
            }
            foreach (KeyValuePair<string, SortedSet<string>> assembly in kept)
            {
                xml.Append("  <assembly fullname=\"").Append(Escape(assembly.Key)).Append("\">\n");
                foreach (string type in assembly.Value)
                    xml.Append("    <type fullname=\"").Append(Escape(type)).Append("\" preserve=\"all\"/>\n");
                xml.Append("  </assembly>\n");
            }
            return xml.Append("</linker>\n").ToString();
        }

        static string Escape(string s) => System.Security.SecurityElement.Escape(s);
    }
}
