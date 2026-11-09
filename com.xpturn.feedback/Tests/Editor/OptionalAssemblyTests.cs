using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Scripting;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace xpTURN.Feedback.Tests.Editor
{
    // a define constraint or versionDefine typo drops an assembly without a warning.
    public sealed class OptionalAssemblyTests
    {
        public const string DevProjectCategory = "DevProject";

        const string PackageName = "com.xpturn.feedback";

        static readonly string[] AlwaysCompiled =
        {
            "xpTURN.Feedback",
            "xpTURN.Feedback.Math",
            "xpTURN.Feedback.Editor",
        };

        [Serializable]
        sealed class AsmdefData
        {
            public string name;
            public string[] defineConstraints;
            public VersionDefine[] versionDefines;
            public string[] includePlatforms;
        }

        [Serializable]
        sealed class VersionDefine
        {
            public string name;
            public string expression;
            public string define;
        }

        [Test]
        public void AlwaysCompiledAssembliesExist()
        {
            var compiled = CompiledAssemblyNames();
            Assert.That(AlwaysCompiled.Where(n => !compiled.Contains(n)), Is.Empty);
        }

        // Holds in any project: the minimal manifest job runs it with the optional dependencies removed.
        [Test]
        public void OptionalAssemblyExistsExactlyWhenItsDependencyIsInstalled()
        {
            var installed = PackageInfo.GetAllRegisteredPackages().ToDictionary(p => p.name, p => p.version);
            var compiled = CompiledAssemblyNames();
            var mismatches = new List<string>();

            foreach (var asmdef in OptionalAsmdefs().Concat(EditorOptionalAsmdefs()))
            {
                var expected = true;
                var reasons = new List<string>();
                foreach (var constraint in asmdef.defineConstraints)
                {
                    var source = asmdef.versionDefines?.FirstOrDefault(v => v.define == constraint);
                    if (source == null)
                    {
                        mismatches.Add($"{asmdef.name}: constraint '{constraint}' has no versionDefine");
                        expected = false;
                        continue;
                    }

                    if (!TryParseVersion(source.expression, out var minimum))
                    {
                        mismatches.Add($"{asmdef.name}: expression '{source.expression}' is not a plain minimum version");
                        expected = false;
                        continue;
                    }

                    var satisfied = installed.TryGetValue(source.name, out var version)
                                    && TryParseVersion(version, out var actual)
                                    && actual >= minimum;
                    reasons.Add($"{source.name} {(version ?? "missing")} (needs {source.expression})");
                    expected &= satisfied;
                }

                var present = compiled.Contains(asmdef.name);
                if (present != expected)
                    mismatches.Add($"{asmdef.name}: compiled={present}, expected={expected} — {string.Join(", ", reasons)}");
            }

            Assert.That(mismatches, Is.Empty);
        }

        // The dev project installs every optional dependency, so a misspelled versionDefine package name shows up here.
        [Test, Category(DevProjectCategory)]
        public void Reg12_EveryOptionalAssemblyExistsInDevProject()
        {
            var compiled = CompiledAssemblyNames();
            Assert.That(OptionalAsmdefs().Concat(EditorOptionalAsmdefs()).Select(a => a.name).Where(n => !compiled.Contains(n)), Is.Empty);
        }

        // an optional editor assembly — Editor only, a friend of Core and of the editor assembly (it drives their internals);
        // no AlwaysLink: it never reaches a player
        [Test]
        public void OptionalEditorAssembliesAreEditorOnlyFriends()
        {
            var core = new HashSet<string>(typeof(FeedbackItem).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName));
            var editor = new HashSet<string>(typeof(xpTURN.Feedback.Editor.PreviewSession).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName));
            var mismatches = new List<string>();
            foreach (var asmdef in EditorOptionalAsmdefs())
            {
                if (asmdef.includePlatforms == null || !asmdef.includePlatforms.SequenceEqual(new[] { "Editor" }))
                    mismatches.Add($"{asmdef.name}: not Editor only");
                if (!core.Contains(asmdef.name))
                    mismatches.Add($"{asmdef.name}: not in Core's InternalsVisibleTo");
                if (!editor.Contains(asmdef.name))
                    mismatches.Add($"{asmdef.name}: not in the editor assembly's InternalsVisibleTo");
            }
            Assert.That(mismatches, Is.Empty);
        }

        // the conditional test assemblies — a define constraint besides UNITY_INCLUDE_TESTS — compile in the dev project. A
        // versionDefines criterion would miss one whose versionDefines were deleted
        [Test, Category(DevProjectCategory)]
        public void EveryConditionalTestAssemblyExistsInDevProject()
        {
            var package = PackageInfo.FindForAssetPath("Packages/" + PackageName);
            Assert.That(package, Is.Not.Null, PackageName + " is not registered");
            var testsDir = Path.Combine(package.resolvedPath, "Tests");
            var conditional = Directory.GetFiles(testsDir, "*.asmdef", SearchOption.AllDirectories)
                .Select(path => JsonUtility.FromJson<AsmdefData>(File.ReadAllText(path)))
                .Where(a => a.defineConstraints != null && a.defineConstraints.Any(c => c != "UNITY_INCLUDE_TESTS"))
                .Select(a => a.name)
                .ToList();
            Assert.That(conditional, Is.Not.Empty, "no conditional test assembly found under " + testsDir);
            var compiled = CompiledAssemblyNames();
            Assert.That(conditional.Where(n => !compiled.Contains(n)), Is.Empty);
        }

        // the runtime test assemblies compile for players, so they reference no UnityEditor assembly. The player script
        // compile leaves test assemblies out — this is the check short of a player build. A use inside #if UNITY_EDITOR counts
        // too: the editor compile defines it. Every asmdef under Tests/Runtime that compiles here — a conditional one with its
        // package (a hand list stays green on one it does not name)
        [Test]
        public void RuntimeTestAssemblyReferencesNoEditorAssembly()
        {
            var package = PackageInfo.FindForAssetPath("Packages/" + PackageName);
            Assert.That(package, Is.Not.Null, PackageName + " is not registered");
            var compiled = CompiledAssemblyNames();
            var names = Directory.GetFiles(Path.Combine(package.resolvedPath, "Tests", "Runtime"), "*.asmdef", SearchOption.AllDirectories)
                .Select(path => JsonUtility.FromJson<AsmdefData>(File.ReadAllText(path)).name)
                .Where(n => compiled.Contains(n))
                .ToList();
            Assert.That(names, Does.Contain(typeof(FrameRecorder).Assembly.GetName().Name), "the main runtime test assembly is among them");
            var assemblies = names.Select(n => System.Reflection.Assembly.Load(n));
            var editor = assemblies.SelectMany(a => a.GetReferencedAssemblies())
                .Select(a => a.Name)
                .Where(n => n.StartsWith("UnityEditor", StringComparison.Ordinal));
            Assert.That(editor, Is.Empty);
        }

        // a runtime test assembly with no test in it (fixtures it registers at SubsystemRegistration) is referenced by nothing —
        // a Minimal player strips it with its registration — so it is always linked. The editor strips nothing: only a player
        // run sees it. The video fixtures' assembly is the positive (where it compiles)
        [Test]
        public void RuntimeTestAssemblyWithoutTestsIsAlwaysLinked()
        {
            var package = PackageInfo.FindForAssetPath("Packages/" + PackageName);
            Assert.That(package, Is.Not.Null, PackageName + " is not registered");
            var compiled = CompiledAssemblyNames();
            var names = Directory.GetFiles(Path.Combine(package.resolvedPath, "Tests", "Runtime"), "*.asmdef", SearchOption.AllDirectories)
                .Select(path => JsonUtility.FromJson<AsmdefData>(File.ReadAllText(path)).name)
                .Where(n => compiled.Contains(n))
                .ToList();
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var testless = names.Where(n => !System.Reflection.Assembly.Load(n).GetTypes().Any(t => t.GetMethods(all).Any(m =>
                m.IsDefined(typeof(TestAttribute), true) || m.IsDefined(typeof(UnityEngine.TestTools.UnityTestAttribute), true) || m.IsDefined(typeof(TestCaseAttribute), true)))).ToList();
            const string video = "xpTURN.Feedback.Tests.Video";
            if (compiled.Contains(video))
                Assert.That(testless, Does.Contain(video), "the video fixtures' assembly has no test — the positive");
            Assert.That(testless.Where(n => !System.Reflection.Assembly.Load(n).IsDefined(typeof(AlwaysLinkAssemblyAttribute), false)), Is.Empty);
        }

        // no saved type outside Core, and Core's friends. A friend can derive from the closed item layer, so this keeps saved
        // types in Core. The minimal manifest job compiles no optional assembly — there only the friend names and the root
        // guard mean anything
        [Test]
        public void OptionalAssembliesAreLinkedCoreFriendsWithoutSavedTypes()
        {
            var roots = new[] { typeof(FeedbackItem), typeof(FeedbackMotion) };
            var core = typeof(FeedbackItem).Assembly;
            var friends = new HashSet<string>(core.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName));
            var compiled = CompiledAssemblyNames();
            var mismatches = new List<string>();

            foreach (var asmdef in OptionalAsmdefs())
            {
                if (!friends.Contains(asmdef.name))
                    mismatches.Add($"{asmdef.name}: not in Core's InternalsVisibleTo");
                if (!compiled.Contains(asmdef.name))
                    continue;

                var assembly = System.Reflection.Assembly.Load(asmdef.name);
                if (!assembly.IsDefined(typeof(AlwaysLinkAssemblyAttribute), false))
                    mismatches.Add($"{asmdef.name}: no [assembly: AlwaysLinkAssembly]");
                foreach (var type in assembly.GetTypes().Where(t => roots.Any(r => r.IsAssignableFrom(t))))
                    mismatches.Add($"{asmdef.name}: {type.FullName} is a saved type — saved types live in Core");
            }

            // the roots stay complete: a [SerializeReference] field of another base would let its derived types past this check
            var slots = core.GetTypes()
                .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(f => f.IsDefined(typeof(SerializeReference), false))
                .ToList();
            if (slots.Count == 0)
                mismatches.Add("no [SerializeReference] field in Core");
            foreach (var slot in slots.Where(f => !roots.Any(r => r.IsAssignableFrom(ElementType(f.FieldType)))))
                mismatches.Add($"{slot.DeclaringType.Name}.{slot.Name}: a [SerializeReference] field outside the roots");

            Assert.That(mismatches, Is.Empty);
        }

        // the Unity object types of the optional assemblies — the components and assets that need their package, saved where
        // that package is (the item layer stays in Core), and the inspectors of the editor ones. A new one is a decision; a
        // golden entry with no assembly is stale
        static readonly Dictionary<string, string[]> ObjectTypes = new Dictionary<string, string[]>
        {
            { "xpTURN.Feedback.Cinemachine", new[] { "xpTURN.Feedback.CinemachineShakeExtension" } },
            { "xpTURN.Feedback.Timeline", new[] { "xpTURN.Feedback.FeedbackClip", "xpTURN.Feedback.FeedbackTrack" } },
            { "xpTURN.Feedback.UGUI", new[] { "xpTURN.Feedback.FeedbackUITrigger", "xpTURN.Feedback.FloatingTextInstance", "xpTURN.Feedback.FloatingTextStyle", "xpTURN.Feedback.ProgressBar" } },
            { "xpTURN.Feedback.Editor.UGUI", new[] { "xpTURN.Feedback.Editor.FeedbackUITriggerEditor" } },
        };

        [Test]
        public void OptionalAssembliesHoldOnlyTheirUnityObjectTypes()
        {
            List<AsmdefData> asmdefs = OptionalAsmdefs();
            var compiled = CompiledAssemblyNames();
            var mismatches = new List<string>();
            List<AsmdefData> editors = EditorOptionalAsmdefs();
            foreach (string name in ObjectTypes.Keys.Where(n => asmdefs.Concat(editors).All(a => a.name != n)))
                mismatches.Add($"{name}: in the golden, no such optional assembly");
            foreach (AsmdefData asmdef in asmdefs.Concat(editors).Where(a => compiled.Contains(a.name)))
            {
                string[] found = System.Reflection.Assembly.Load(asmdef.name).GetTypes()
                    .Where(t => typeof(UnityEngine.Object).IsAssignableFrom(t))
                    .Select(t => t.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                string[] expected = ObjectTypes.TryGetValue(asmdef.name, out string[] golden) ? golden : Array.Empty<string>();
                if (!found.SequenceEqual(expected))
                    mismatches.Add($"{asmdef.name}: {string.Join(", ", found)} — the golden {string.Join(", ", expected)}");
            }
            Assert.That(mismatches, Is.Empty);
        }

        [Serializable]
        sealed class KitAsmdef
        {
            public string name;
            public string[] references;
            public string[] defineConstraints;
            public VersionDefine[] versionDefines;
        }

        // the sample kit's assemblies live outside Runtime/ (no Core friend, no AlwaysLink): the unconditional ones always
        // compile, Samples references Core only, the UI one compiles exactly when Input System and uGUI are installed — plain
        // minimums, no upper bound
        [Test]
        public void SampleKitAssembliesFollowTheirRules()
        {
            var package = PackageInfo.FindForAssetPath("Packages/" + PackageName);
            var kit = Path.Combine(package.resolvedPath, "SampleKit");
            var installed = PackageInfo.GetAllRegisteredPackages().ToDictionary(p => p.name, p => p.version);
            var compiled = CompiledAssemblyNames();
            var friends = new HashSet<string>(typeof(FeedbackItem).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName));
            var asmdefs = Directory.GetFiles(kit, "*.asmdef", SearchOption.AllDirectories).Select(p => JsonUtility.FromJson<KitAsmdef>(File.ReadAllText(p))).ToList();
            Assert.That(asmdefs.Select(a => a.name), Is.EquivalentTo(new[] { "xpTURN.Feedback.Samples", "xpTURN.Feedback.Samples.UI", "xpTURN.Feedback.Samples.Editor" }));
            var mismatches = new List<string>();
            foreach (var asmdef in asmdefs)
            {
                if (friends.Contains(asmdef.name))
                    mismatches.Add($"{asmdef.name}: a Core friend");
                var expected = true;
                foreach (var constraint in asmdef.defineConstraints ?? new string[0])
                {
                    var source = asmdef.versionDefines?.FirstOrDefault(v => v.define == constraint);
                    if (source == null || !TryParseVersion(source.expression, out var minimum))
                    {
                        mismatches.Add($"{asmdef.name}: constraint '{constraint}' has no plain-minimum versionDefine");
                        expected = false;
                        continue;
                    }
                    expected &= installed.TryGetValue(source.name, out var version) && TryParseVersion(version, out var actual) && actual >= minimum;
                }
                if (compiled.Contains(asmdef.name) != expected)
                    mismatches.Add($"{asmdef.name}: compiled={compiled.Contains(asmdef.name)}, expected={expected}");
            }
            var samples = asmdefs.First(a => a.name == "xpTURN.Feedback.Samples");
            if (!(samples.references ?? new string[0]).SequenceEqual(new[] { "xpTURN.Feedback" }) || (samples.defineConstraints?.Length ?? 0) != 0)
                mismatches.Add("xpTURN.Feedback.Samples: unconditional, references Core only");
            if ((asmdefs.First(a => a.name == "xpTURN.Feedback.Samples.Editor").defineConstraints?.Length ?? 0) != 0)
                mismatches.Add("xpTURN.Feedback.Samples.Editor: unconditional");
            if (Type.GetType("xpTURN.Feedback.Samples.SampleCard, xpTURN.Feedback.Samples") == null || Type.GetType("xpTURN.Feedback.Samples.SampleCatalog, xpTURN.Feedback.Samples") == null)
                mismatches.Add("the card and the catalog live in the unconditional assembly (a catalog never turns into a missing script)");
            var ui = asmdefs.First(a => a.name == "xpTURN.Feedback.Samples.UI");
            if (!new[] { "com.unity.inputsystem", "com.unity.ugui" }.All(n => ui.versionDefines?.Any(v => v.name == n) == true))
                mismatches.Add("xpTURN.Feedback.Samples.UI: conditional on Input System and uGUI");
            Assert.That(mismatches, Is.Empty);
        }

        // the dev project installs Input System and uGUI: a misspelled versionDefines package name drops the UI assembly here
        [Test, Category(DevProjectCategory)]
        public void Reg12_SampleKitAssembliesExistInDevProject()
        {
            var compiled = CompiledAssemblyNames();
            Assert.That(new[] { "xpTURN.Feedback.Samples", "xpTURN.Feedback.Samples.UI", "xpTURN.Feedback.Samples.Editor" }.Where(n => !compiled.Contains(n)), Is.Empty);
        }

        // the optional assemblies under Editor/, at any depth
        static List<AsmdefData> EditorOptionalAsmdefs()
        {
            var package = PackageInfo.FindForAssetPath("Packages/" + PackageName);
            Assert.That(package, Is.Not.Null, PackageName + " is not registered");
            var editorDir = Path.Combine(package.resolvedPath, "Editor");
            var result = Directory.GetFiles(editorDir, "*.asmdef", SearchOption.AllDirectories)
                .Select(path => JsonUtility.FromJson<AsmdefData>(File.ReadAllText(path)))
                .Where(a => a.defineConstraints != null && a.defineConstraints.Length > 0)
                .ToList();
            Assert.That(result, Is.Not.Empty, "no optional editor assembly found under " + editorDir);
            return result;
        }

        static List<AsmdefData> OptionalAsmdefs()
        {
            var package = PackageInfo.FindForAssetPath("Packages/" + PackageName);
            Assert.That(package, Is.Not.Null, PackageName + " is not registered");

            var runtimeDir = Path.Combine(package.resolvedPath, "Runtime");
            var result = Directory.GetFiles(runtimeDir, "*.asmdef", SearchOption.AllDirectories)
                .Select(path => JsonUtility.FromJson<AsmdefData>(File.ReadAllText(path)))
                .Where(a => a.defineConstraints != null && a.defineConstraints.Length > 0)
                .ToList();
            Assert.That(result, Is.Not.Empty, "no optional assembly found under " + runtimeDir);
            return result;
        }

        // an item's implementing assembly — the [FeedbackTarget] Implementation (empty is Core) — and whether the player compiles
        // it. The registry, catalog and preview tests judge by it: the minimal manifest drops the optional assemblies, the data
        // stays in Core
        internal static string ImplementationOf(Type item)
            => ((FeedbackTargetAttribute)Attribute.GetCustomAttribute(item, typeof(FeedbackTargetAttribute), false))?.Implementation ?? "";

        internal static bool Implemented(Type item)
        {
            string name = ImplementationOf(item);
            return name.Length == 0 || new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.Player).Select(a => a.name)).Contains(name);
        }

        static HashSet<string> CompiledAssemblyNames()
        {
            return new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.Editor).Select(a => a.name));
        }

        internal static Type ElementType(Type type)
        {
            if (type.IsArray)
                return type.GetElementType();
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) ? type.GetGenericArguments()[0] : type;
        }

        static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(text))
                return false;
            var end = text.IndexOfAny(new[] { '-', '+' });
            return Version.TryParse(end < 0 ? text : text.Substring(0, end), out version);
        }
    }
}
