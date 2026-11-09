using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor.Docs
{
    // the preset link the agent skill writes (the yaml block of references/authoring-files.md), put into saved files the way
    // the skill tells an agent to: the editor reads one player that links the preset, with no item, no finding and no log,
    // and keeps the link when it saves; a GUID one character off reads as no preset — no log says so, the validator's FB-B08
    // does. The block is taken from the skill's text, so the test reads what an agent copies. The files are edited while
    // the editor does not hold them (the scene closed) and imported again
    public sealed class SkillSnippetTests
    {
        const string Folder = "Assets/SkillSnippetTests";
        const string ScriptLine = "m_Script: {{fileID: {0}, guid: {1}, type: 3}}";
        readonly List<string> m_Logs = new List<string>();

        static string AuthoringFile => Path.Combine(DocRegions.PackageRoot, "Skills~", "skills", "xpturn-feedback", "references", "authoring-files.md");

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.CreateFolder("Assets", "SkillSnippetTests");
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            m_Logs.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Folder);
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Log)
                m_Logs.Add(type + ": " + message);
        }

        // the component line and the component block of the skill's one yaml block
        static (string component, string block) Snippet()
        {
            string text = File.ReadAllText(AuthoringFile).Replace("\r\n", "\n");
            MatchCollection blocks = Regex.Matches(text, "^```yaml\n(?<b>.*?)^```$", RegexOptions.Singleline | RegexOptions.Multiline);
            Assert.That(blocks.Count, Is.EqualTo(1), "one yaml block in authoring-files.md");
            string[] lines = blocks[0].Groups["b"].Value.Split('\n');
            string component = lines.Single(l => l.StartsWith("  - component: ", StringComparison.Ordinal));
            int head = Array.FindIndex(lines, l => l.StartsWith("--- !u!114 &", StringComparison.Ordinal));
            Assert.That(head, Is.GreaterThanOrEqualTo(0), "the component block");
            string block = string.Join("\n", lines.Skip(head).TakeWhile(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal))) + "\n";
            return (component, block);
        }

        static string ComponentId(string component) => Regex.Match(component, @"fileID: (?<id>\d+)").Groups["id"].Value;

        // the snippet with another component fileID — a second player in the same file
        static (string component, string block) Renumbered((string component, string block) s, string id)
        {
            string old = ComponentId(s.component);
            return (s.component.Replace(old, id), s.block.Replace("&" + old + "\n", "&" + id + "\n"));
        }

        // the snippet put into a saved file: the component line at the end of the object's m_Component list, the block at
        // the end of the file, the placeholders replaced
        static string Write(string text, string objectName, (string component, string block) s, string presetGuid)
        {
            Match go = Regex.Match(text, @"--- !u!1 &(?<id>\d+)\nGameObject:\n(?:  [^\n]*\n)*?  m_Name: " + Regex.Escape(objectName) + "\n");
            Assert.That(go.Success, Is.True, "the block of " + objectName);
            string id = go.Groups["id"].Value;
            Assert.That(text, Does.Not.Contain("&" + ComponentId(s.component) + "\n"), "the snippet's fileID is new in the file");
            int start = go.Index;
            int next = text.IndexOf("\n--- ", start + 1, StringComparison.Ordinal);
            int end = next < 0 ? text.Length : next + 1;
            string block = text.Substring(start, end - start);
            int last = block.LastIndexOf("\n  - component: ", StringComparison.Ordinal);
            Assert.That(last, Is.GreaterThanOrEqualTo(0), "the m_Component list of " + objectName);
            int lineEnd = block.IndexOf('\n', last + 1);
            block = block.Substring(0, lineEnd + 1) + s.component + "\n" + block.Substring(lineEnd + 1);
            string edited = text.Substring(0, start) + block + text.Substring(end);
            if (!edited.EndsWith("\n", StringComparison.Ordinal))
                edited += "\n";
            return edited + s.block.Replace("GAME_OBJECT_ID", id).Replace("PRESET_GUID", presetGuid);
        }

        static string PresetGuid(string name)
            => AssetDatabase.FindAssets("t:FeedbackPreset", new[] { "Packages/com.xpturn.feedback/Presets" })
                .Single(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == name);

        static List<string> Findings(FeedbackPlayer player) => FeedbackValidator.Run(player).Select(r => r.Id).ToList();

        static FeedbackPlayer OnlyPlayer(GameObject go)
        {
            FeedbackPlayer[] players = go.GetComponents<FeedbackPlayer>();
            Assert.That(players, Has.Length.EqualTo(1), "one player on " + go.name);
            return players[0];
        }

        [Test]
        public void SK07_PresetLinkSnippet()
        {
            (string component, string block) snippet = Snippet();
            var probe = new GameObject("Probe");
            try
            {
                MonoScript script = MonoScript.FromMonoBehaviour(probe.AddComponent<FeedbackPlayer>());
                Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out string scriptGuid, out long scriptId), Is.True, "FeedbackPlayer's script");
                Assert.That(snippet.block, Does.Contain(string.Format(ScriptLine, scriptId, scriptGuid)), "the snippet's m_Script is FeedbackPlayer's");
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
            string hitLight = PresetGuid("Hit Light");
            string typo = hitLight.Substring(0, 31) + (hitLight[31] == 'a' ? 'b' : 'a');
            var preset = AssetDatabase.LoadAssetAtPath<FeedbackPreset>(AssetDatabase.GUIDToAssetPath(hitLight));

            // a scene: the preset's GUID on one object, a GUID one character off on another
            Scene made = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject.CreatePrimitive(PrimitiveType.Cube).name = "Enemy";
            GameObject.CreatePrimitive(PrimitiveType.Cube).name = "Typo";
            string scenePath = Folder + "/Agent.unity";
            Assert.That(EditorSceneManager.SaveScene(made, scenePath), Is.True, "the scene saved");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string text = Write(File.ReadAllText(scenePath), "Enemy", snippet, hitLight);
            text = Write(text, "Typo", Renumbered(snippet, ComponentId(snippet.component) + "9"), typo);
            File.WriteAllText(scenePath, text);
            m_Logs.Clear();
            AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceUpdate);
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            GameObject[] roots = scene.GetRootGameObjects();
            FeedbackPlayer enemy = OnlyPlayer(roots.Single(g => g.name == "Enemy"));
            FeedbackPlayer wrong = OnlyPlayer(roots.Single(g => g.name == "Typo"));
            Assert.That(enemy.m_Preset, Is.EqualTo(preset), "the player links the preset");
            Assert.That(enemy.m_Items, Is.Empty, "no item of its own");
            Assert.That(Findings(enemy), Does.Not.Contain("FB-B08"), "no finding on the link");
            Assert.That(wrong.m_Preset == null, Is.True, "a GUID one character off links nothing");
            Assert.That(Findings(wrong), Does.Contain("FB-B08"), "the validator finds the broken link");
            Assert.That(m_Logs, Is.Empty, "no log — the broken link has none either");
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True, "the scene saved by the editor");
            Assert.That(File.ReadAllText(scenePath), Does.Contain("m_Preset: {fileID: 11400000, guid: " + hitLight + ", type: 2}"), "the link stays when the editor saves");

            // a prefab
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = "Enemy";
            string prefabPath = Folder + "/Enemy.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            File.WriteAllText(prefabPath, Write(File.ReadAllText(prefabPath), "Enemy", snippet, hitLight));
            m_Logs.Clear();
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            FeedbackPlayer inPrefab = OnlyPlayer(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            Assert.That(inPrefab.m_Preset, Is.EqualTo(preset), "the prefab's player links the preset");
            Assert.That(inPrefab.m_Items, Is.Empty, "no item of its own in the prefab");
            Assert.That(m_Logs, Is.Empty, "no log for the prefab");
        }
    }
}
