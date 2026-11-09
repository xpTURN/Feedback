using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using xpTURN.Feedback.Samples;
using xpTURN.Feedback.Samples.Editor;
using static xpTURN.Feedback.Tests.Editor.Samples.SampleKitTestSupport;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace xpTURN.Feedback.Tests.Editor.Samples
{
    // the static rows over the kit and every bundle the project holds (imported or a dev package)
    public sealed class SampleStaticTests
    {
        const string LegacyModule = "4f231c4fb786f3946a6b90b886c48677";

        [Serializable]
        sealed class Named
        {
            public string name;
        }

        [Serializable]
        sealed class PackageJson
        {
            public SampleEntry[] samples;
        }

        [Serializable]
        sealed class SampleEntry
        {
            public string displayName;
            public string path;
        }

        [Test]
        public void KQ1_ScenesFollowTheRules()
        {
            string marker = ScriptGuid(typeof(SampleSceneMarker));
            string hub = ScriptGuid(typeof(SampleHub));
            string es = ScriptGuid(typeof(EventSystem));
            string volume = ScriptGuid(Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime"));
            string prefab = AssetDatabase.AssetPathToGUID(EventSystemPrefab);
            Assert.That(new[] { marker, hub, es, prefab }.Where(string.IsNullOrEmpty), Is.Empty, "a script or the prefab was not found");
            var problems = new List<string>();
            int scenes = 0;
            foreach (string file in Files("*.unity").Concat(Files("*.prefab")))
            {
                string text = File.ReadAllText(file);
                string name = Path.GetFileName(file);
                if (text.Contains(LegacyModule))
                    problems.Add(name + ": StandaloneInputModule");
                if (!file.EndsWith(".unity"))
                    continue;
                scenes++;
                bool isHub = name == SampleKitPaths.HubName + ".unity";
                int markers = Count(text, "guid: " + marker);
                int hubs = Count(text, "guid: " + hub);
                if (isHub ? hubs != 1 || markers != 0 : markers != 1 || hubs != 0)
                    problems.Add($"{name}: markers {markers} hubs {hubs}");
                int instances = Count(text, "m_SourcePrefab: {fileID: 100100000, guid: " + prefab);
                int bare = Count(text, "guid: " + es);
                if (instances != 1 || bare != 0)
                    problems.Add($"{name}: Common event systems {instances} · other event systems {bare}");
                int cameras = Count(text, "m_TagString: MainCamera");
                if (cameras != 1)
                    problems.Add($"{name}: main cameras {cameras}");
                int listeners = Count(text, "--- !u!81 &");
                if (listeners != 1)
                    problems.Add($"{name}: audio listeners {listeners}");
                if (!string.IsNullOrEmpty(volume))
                    foreach (Match m in Regex.Matches(text, "guid: " + volume + "[\\s\\S]*?priority: ([-0-9.]+)"))
                        if (float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) >= 1000f)
                            problems.Add($"{name}: a volume at priority {m.Groups[1].Value}");
            }
            Assert.That(scenes, Is.GreaterThan(1), "the hub and at least one sample scene");
            Assert.That(problems, Is.Empty);
        }

        [Test]
        public void KQ2_CardsScenesAndSamplesEntries()
        {
            var compiled = new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.Editor).Select(a => a.name));
            List<SampleCatalog> catalogs = Catalogs();
            Assert.That(catalogs, Is.Not.Empty);
            PackageInfo package = PackageInfo.FindForAssetPath("Packages/com.xpturn.feedback");
            SampleEntry[] entries = JsonUtility.FromJson<PackageJson>(File.ReadAllText(Path.Combine(package.resolvedPath, "package.json"))).samples ?? new SampleEntry[0];
            var names = new Dictionary<string, string>();
            var problems = new List<string>();
            int cards = 0;
            foreach (SampleCatalog catalog in catalogs)
            {
                string root = BundleRoot(catalog);
                string disk = Disk(root);
                var asmdefs = new HashSet<string>(Directory.GetFiles(disk, "*.asmdef", SearchOption.AllDirectories).Select(p => JsonUtility.FromJson<Named>(File.ReadAllText(p)).name));
                var cardScenes = new HashSet<string>();
                foreach (SampleCard card in catalog.Cards)
                {
                    cards++;
                    string path = AssetDatabase.GUIDToAssetPath(card.SceneGuid);
                    if (!path.EndsWith(".unity") || !path.StartsWith(root + "/"))
                        problems.Add($"{card.Title}: scene '{path}' is not a scene of {root}");
                    else
                        cardScenes.Add(path);
                    if (Path.GetFileNameWithoutExtension(path) != card.SceneName)
                        problems.Add($"{card.Title}: name {card.SceneName} vs {path}");
                    if (names.TryGetValue(card.SceneName ?? "", out string other))
                        problems.Add($"{card.Title}: scene name {card.SceneName} also on {other}");
                    else
                        names[card.SceneName ?? ""] = card.Title;
                    string assembly = card.RequiredType?.Split(',').Skip(1).FirstOrDefault()?.Trim();
                    if (string.IsNullOrEmpty(assembly) || !asmdefs.Contains(assembly))
                        problems.Add($"{card.Title}: required assembly '{assembly}' is not an asmdef of {root}");
                    else if (SampleCards.Available(card) != compiled.Contains(assembly))
                        problems.Add($"{card.Title}: available {SampleCards.Available(card)} vs compiled {compiled.Contains(assembly)}");
                }
                foreach (string scene in Directory.GetFiles(disk, "*.unity", SearchOption.AllDirectories))
                {
                    string asset = root + scene.Substring(disk.Length).Replace('\\', '/');
                    if (!cardScenes.Contains(asset))
                        problems.Add(asset + ": no card");
                }
                string leaf = Path.GetFileName(root);
                if (root.StartsWith("Assets/Samples/") && !entries.Any(e => e.displayName == leaf))
                    problems.Add($"{root}: no samples entry named {leaf}");
                if (root.StartsWith("Packages/com.xpturn.feedback.samples-dev/") && !entries.Any(e => e.path == "Samples~/" + leaf))
                    problems.Add($"{root}: no samples entry at Samples~/{leaf}");
            }
            foreach (SampleEntry e in entries)
                if (!Directory.Exists(Path.Combine(package.resolvedPath, e.path)))
                    problems.Add($"samples entry {e.displayName}: {e.path} is missing");
            Debug.Log($"KQ2 catalogs={catalogs.Count} cards={cards} entries={entries.Length}");
            Assert.That(problems, Is.Empty);
        }

        // the HUD's line is checked in the round trip
        [Test]
        public void KQ4_ReadmeIsTheCard()
        {
            var problems = new List<string>();
            int n = 0;
            foreach (SampleCatalog catalog in Catalogs())
                foreach (SampleCard card in catalog.Cards)
                {
                    n++;
                    string readme = Disk(SampleReadme.PathOf(card).Replace('\\', '/'));
                    if (!File.Exists(readme))
                        problems.Add($"{card.Title}: no README");
                    else if (File.ReadAllText(readme).Replace("\r\n", "\n") != SampleReadme.Text(card))
                        problems.Add($"{card.Title}: README differs from the card");
                }
            Assert.That(n, Is.GreaterThan(0));
            Assert.That(problems, Is.Empty);
        }

        [Test]
        public void KQ8_Forbidden()
        {
            var problems = new List<string>();
            foreach (string root in Roots().Where(Directory.Exists))
                foreach (string dir in Directory.GetDirectories(root, "Resources", SearchOption.AllDirectories))
                    problems.Add(dir + ": a Resources folder (shipped in every build)");
            string settings = ScriptGuid(typeof(FeedbackSettings));
            foreach (string asset in Files("*.asset"))
                if (Path.GetFileName(asset) == "FeedbackSettings.asset" || (settings != null && File.ReadAllText(asset).Contains("guid: " + settings)))
                    problems.Add(asset + ": a FeedbackSettings asset");
            foreach (string cs in Files("*.cs"))
            {
                string text = File.ReadAllText(cs);
                string norm = cs.Replace('\\', '/');
                bool editor = norm.Contains("/Editor/");
                if (!editor && (text.Contains("File.Write") || text.Contains("AssetDatabase.Create")))
                    problems.Add(cs + ": writes files or assets outside an editor folder");
                if (Path.GetFileName(cs) != "SampleState.cs" && (text.Contains("FeedbackCategories.Set") || Regex.IsMatch(text, @"FeedbackTimeScale\.BaseScale\s*=[^=]")))
                    problems.Add(cs + ": writes the categories or the base time scale (Common's SampleState is the one writer)");
                if (Regex.IsMatch(text, @"Time\.timeScale\s*=[^=]"))
                    problems.Add(cs + ": writes Time.timeScale (a pause or a slow-mo goes through SampleState — FeedbackTimeScale.BaseScale)");
                if (!editor && !norm.Contains("/Tour/") && text.Contains("Debug.Log"))
                    problems.Add(cs + ": logs");
            }
            var roots = new[] { typeof(FeedbackItem), typeof(FeedbackMotion) };
            foreach (string name in CompilationPipeline.GetAssemblies(AssembliesType.Editor).Select(a => a.name).Where(n => n.StartsWith("xpTURN.Feedback.Samples")))
                foreach (Type type in System.Reflection.Assembly.Load(name).GetTypes().Where(t => roots.Any(r => r.IsAssignableFrom(t))))
                    problems.Add($"{name}: {type.FullName} defines an item or a motion");
            Assert.That(problems, Is.Empty);
        }

        static readonly string[] s_Pointer = { "Point", "Click", "ScrollWheel", "MiddleClick", "RightClick", "TrackedDevicePosition", "TrackedDeviceOrientation" };
        static readonly string[] s_SampleActions = { "Move", "Point", "Action1", "Action2", "Action3", "Back", "After", "IntensityDown", "IntensityUp", "TimeDown", "TimeUp", "HudFold", "Option1", "Option2", "Option3", "Option4" };
        static readonly string[] s_Fields = { "m_PointAction", "m_LeftClickAction", "m_MiddleClickAction", "m_RightClickAction", "m_ScrollWheelAction", "m_MoveAction", "m_SubmitAction", "m_CancelAction", "m_TrackedDevicePositionAction", "m_TrackedDeviceOrientationAction" };

        static int LiveReferences(InputSystemUIInputModule m)
            => new[] { m.point, m.leftClick, m.middleClick, m.rightClick, m.scrollWheel, m.move, m.submit, m.cancel, m.trackedDevicePosition, m.trackedDeviceOrientation }.Count(r => r != null && r.action != null);

        // the hand-written actions: every id set · pointer actions PassThrough · the Sample map · the prefab's ten references
        // saved (a setter-built prefab saves fileID 0) · no scene overrides them · they survive a forced reimport
        [Test]
        public void KQ12_ActionsAndTheEventSystemPrefab()
        {
            var problems = new List<string>();
            string json = File.ReadAllText(Disk(ActionsPath));
            int ids = Count(json, "\"id\":");
            int empty = Regex.Matches(json, "\"id\":\\s*\"\"").Count;
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            int expected = asset.actionMaps.Sum(m => 1 + m.actions.Count + m.bindings.Count);
            if (ids != expected || empty != 0)
                problems.Add($"ids {ids} of {expected} · empty {empty}");
            foreach (string a in s_Pointer)
                if (asset.FindAction("UI/" + a)?.type != InputActionType.PassThrough)
                    problems.Add($"UI/{a} is not PassThrough");
            string[] sample = asset.FindActionMap("Sample", true).actions.Select(a => a.name).ToArray();
            if (!sample.OrderBy(x => x).SequenceEqual(s_SampleActions.OrderBy(x => x)))
                problems.Add("Sample map: " + string.Join(",", sample));
            string guid = AssetDatabase.AssetPathToGUID(ActionsPath);
            string[] yaml = File.ReadAllLines(Disk(EventSystemPrefab));
            foreach (string field in s_Fields)
            {
                string line = yaml.FirstOrDefault(l => l.Contains(field + ":"));
                if (line == null || !line.Contains("guid: " + guid) || line.Contains("fileID: 0,") || line.Contains("{fileID: 0}"))
                    problems.Add($"prefab {field}: {line?.Trim()}");
            }
            foreach (string scene in Files("*.unity"))
                if (File.ReadAllLines(scene).Any(l => l.Contains("propertyPath: m_") && l.Contains("Action")))
                    problems.Add(Path.GetFileName(scene) + ": overrides a module action");
            var module = AssetDatabase.LoadAssetAtPath<GameObject>(EventSystemPrefab).GetComponent<InputSystemUIInputModule>();
            int before = LiveReferences(module);
            AssetDatabase.ImportAsset(ActionsPath, ImportAssetOptions.ForceUpdate);
            module = AssetDatabase.LoadAssetAtPath<GameObject>(EventSystemPrefab).GetComponent<InputSystemUIInputModule>();
            int after = LiveReferences(module);
            if (before != 10 || after != 10)
                problems.Add($"prefab references {before} → after a forced reimport {after}");
            Assert.That(problems, Is.Empty);
        }

        // the dev project's build list and preloaded assets hold nothing of the kit ('Restore Build Settings' forgotten
        // before a commit would ship the hub in the gates' release build)
        [Test, Category("DevProject")]
        public void KQ16_DevProjectBuildSettingsHoldNoSample()
        {
            Assert.That(EditorBuildSettings.scenes.Select(s => s.path).Where(p => p.Contains("/SampleKit/") || p.StartsWith("Assets/Samples/")), Is.Empty);
            Assert.That(PlayerSettings.GetPreloadedAssets().OfType<SampleCatalog>(), Is.Empty);
            Assert.That(File.Exists(SampleBuild.RestorePath), Is.False, "a restore file left behind");
        }

        // the bytes of the settings files are the tool's (the run's end saves them)
        [Test]
        public void KQ5_BuildSamplesAndRestore()
        {
            if (File.Exists(SampleBuild.RestorePath))
                File.Delete(SampleBuild.RestorePath);
            string Snapshot() => string.Join("|", EditorBuildSettings.scenes.Select(s => s.path + ":" + s.enabled)) + "#" + string.Join("|", PlayerSettings.GetPreloadedAssets().Select(a => a != null ? AssetDatabase.GetAssetPath(a) : "null"));
            var before = EditorBuildSettings.scenes.Select(s => s.path).ToList();
            string snapshot = Snapshot();
            SampleBuild.Apply();
            var scenes = EditorBuildSettings.scenes.Select(s => s.path).ToList();
            var preloaded = PlayerSettings.GetPreloadedAssets().OfType<SampleCatalog>().ToList();
            List<string> expected = Catalogs().SelectMany(c => c.Cards).Where(SampleCards.Available).Select(c => AssetDatabase.GUIDToAssetPath(c.SceneGuid)).ToList();
            string file = File.ReadAllText(SampleBuild.RestorePath);
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene("Assets/KQ5-extra.unity", true) }).ToArray();
            SampleBuild.Apply();
            string fileAgain = File.ReadAllText(SampleBuild.RestorePath);
            SampleBuild.Restore();
            Assert.That(scenes.First(), Is.EqualTo(SampleKitPaths.HubScene));
            Assert.That(expected.All(scenes.Contains), Is.True, "the available cards' scenes");
            Assert.That(scenes.IndexOf(before.FirstOrDefault() ?? SampleKitPaths.HubScene), Is.GreaterThanOrEqualTo(before.Count == 0 ? 0 : expected.Count + 1), "the scenes there before come after");
            Assert.That(before.All(scenes.Contains), Is.True, "the scenes there before are kept");
            Assert.That(preloaded.Count, Is.EqualTo(Catalogs().Count));
            Assert.That(fileAgain, Is.EqualTo(file), "the restore file keeps the first state");
            Assert.That(File.Exists(SampleBuild.RestorePath), Is.False);
            Assert.That(Snapshot(), Is.EqualTo(snapshot));
        }
    }
}
