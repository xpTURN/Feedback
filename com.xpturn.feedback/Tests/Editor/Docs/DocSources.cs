using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using xpTURN.Feedback.Editor;

namespace xpTURN.Feedback.Tests.Editor.Docs
{
    // what each region kind is made from — snippet: (compiled test sources) · count: · card: (a sample card's README) ·
    // reference: (the [FeedbackTarget] types — static metadata only) · api: (a public type's members, described by its ///
    // comments) · rules: (the validator's rules) · motions: (the motions — their evaluators and the targets taking them) ·
    // samples: (the sample catalogs, the cards' READMEs and the samples' assemblies) · presets: (the package's presets and
    // their items). A kind not in the table throws; every kind in the table is used by some document. A table's
    // hand-written last cell is kept by the row's first cell
    internal static class DocSources
    {
        internal static readonly Dictionary<string, Func<string, string, string>> Kinds = new Dictionary<string, Func<string, string, string>>
        {
            { "snippet", (arg, body) => Snippet(arg) },
            { "count", (arg, body) => Count(arg) },
            { "card", (arg, body) => Card(arg) },
            { "reference", (arg, body) => Reference(arg, body) },
            { "api", (arg, body) => Api(arg) },
            { "rules", (arg, body) => Rules(body) },
            { "motions", (arg, body) => Motions(body) },
            { "samples", (arg, body) => Samples(body) },
            { "presets", (arg, body) => Presets(body) },
        };

        internal static string Produce(string key, string body)
        {
            int colon = key.IndexOf(':');
            string kind = colon < 0 ? key : key.Substring(0, colon);
            string arg = colon < 0 ? "" : key.Substring(colon + 1);
            return Kinds.TryGetValue(kind, out Func<string, string, string> make) ? make(arg, body) : throw new InvalidOperationException("unknown region kind '" + kind + "' (" + key + ")");
        }

        // ---- snippets: // doc: <name> … // end in any .cs under Tests/
        static readonly Regex SnippetRegion = new Regex(@"^[ \t]*// doc: (?<name>\S+)[ \t]*\n(?<code>.*?)^[ \t]*// end[ \t]*$", RegexOptions.Singleline | RegexOptions.Multiline);

        // name → (file, code) · the names found twice
        internal static (Dictionary<string, (string file, string code)> all, List<string> twice) Snippets()
        {
            var all = new Dictionary<string, (string, string)>();
            var twice = new List<string>();
            foreach (string file in Directory.GetFiles(Path.Combine(DocRegions.PackageRoot, "Tests"), "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                foreach (Match m in SnippetRegion.Matches(File.ReadAllText(file).Replace("\r\n", "\n")))
                {
                    string name = m.Groups["name"].Value;
                    if (all.ContainsKey(name))
                        twice.Add($"{name} ({Path.GetFileName(all[name].Item1)} · {Path.GetFileName(file)})");
                    else
                        all[name] = (file, Dedent(m.Groups["code"].Value));
                }
            }
            return (all, twice);
        }

        static string Dedent(string code)
        {
            string[] lines = code.TrimEnd('\n').Split('\n');
            int indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart(' ').Length).DefaultIfEmpty(0).Min();
            return string.Join("\n", lines.Select(l => l.Length >= indent ? l.Substring(indent) : l.TrimStart(' '))) + "\n";
        }

        static string Snippet(string name)
            => Snippets().all.TryGetValue(name, out var s) ? "```csharp\n" + s.code + "```\n" : throw new InvalidOperationException("no snippet '" + name + "'");

        // ---- counts
        internal static List<Type> FeedbackTypes()
            => TypeCache.GetTypesWithAttribute<FeedbackTargetAttribute>().Where(t => !t.IsAbstract && typeof(FeedbackItem).IsAssignableFrom(t)).ToList();

        static string Count(string name)
        {
            switch (name)
            {
                case "feedbacks": return FeedbackTypes().Count.ToString();
                case "targets": return FeedbackTypes().Count(t => typeof(TargetItem).IsAssignableFrom(t)).ToString();
                case "motions": return Enum.GetValues(typeof(MotionKind)).Length.ToString();
                case "combinations":
                    return FeedbackTypes().Where(t => typeof(TargetItem).IsAssignableFrom(t))
                        .Sum(t => Enum.GetValues(typeof(MotionKind)).Cast<MotionKind>().Count(((TargetItem)Activator.CreateInstance(t)).Traits.Supports)).ToString();
                case "leaves": return FeedbackCatalog.All.Count(l => !l.Custom).ToString();
                case "presets": return AssetDatabase.FindAssets("t:FeedbackPreset", new[] { "Packages/com.xpturn.feedback/Presets" }).Length.ToString();
                case "bundles": return Regex.Matches(File.ReadAllText(Path.Combine(DocRegions.PackageRoot, "package.json")), "\"path\": \"Samples~/").Count.ToString();
                default: throw new InvalidOperationException("no count '" + name + "'");
            }
        }

        // ---- a sample card's README section: card:<path from the package root>#<section>
        static string Card(string arg)
        {
            int hash = arg.IndexOf('#');
            if (hash < 0)
                throw new InvalidOperationException("card region without a section: " + arg);
            string file = Path.Combine(DocRegions.PackageRoot, arg.Substring(0, hash));
            if (!File.Exists(file))
                throw new InvalidOperationException("no card README " + arg.Substring(0, hash));
            Match m = Regex.Match(File.ReadAllText(file).Replace("\r\n", "\n"), "^## " + Regex.Escape(arg.Substring(hash + 1)) + "\n\n(?<s>.*?)\n\n## ", RegexOptions.Singleline | RegexOptions.Multiline);
            return m.Success ? m.Groups["s"].Value + "\n" : throw new InvalidOperationException("no section " + arg);
        }

        // ---- the package's player assemblies (not the tests', not the samples') — CompilationPipeline + Assembly.Load
        internal static IEnumerable<Assembly> RuntimeAssemblies()
            => UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.PlayerWithoutTestAssemblies)
                .Select(a => a.name)
                .Where(n => n.StartsWith("xpTURN.Feedback", StringComparison.Ordinal) && !n.Contains(".Samples"))
                .OrderBy(n => n, StringComparer.Ordinal)
                .Select(Assembly.Load);

        // ---- tables whose last cell is written by hand: the cell is kept by the row's first cell
        internal static Dictionary<string, string> Descriptions(string body)
        {
            var kept = new Dictionary<string, string>();
            foreach (string line in body.Split('\n'))
            {
                if (!line.StartsWith("| ", StringComparison.Ordinal) || line.StartsWith("|---", StringComparison.Ordinal))
                    continue;
                string[] cells = Cells(line);
                if (cells.Length >= 2 && !kept.ContainsKey(cells[0]))
                    kept[cells[0]] = cells[cells.Length - 1];
            }
            return kept;
        }

        internal static string[] Cells(string line)
            => Regex.Split(line.Trim().Trim('|'), @"(?<!\\)\|").Select(c => c.Trim()).ToArray();

        static string Table(string[] header, IEnumerable<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.Append("| ").Append(string.Join(" | ", header)).Append(" |\n|").Append(string.Concat(header.Select(_ => "---|"))).Append('\n');
            foreach (string[] row in rows)
                sb.Append("| ").Append(string.Join(" | ", row)).Append(" |\n");
            return sb.ToString();
        }

        // ---- the feedback reference: reference:<the path's first segment>
        static FeedbackTargetAttribute Attr(Type t) => (FeedbackTargetAttribute)Attribute.GetCustomAttribute(t, typeof(FeedbackTargetAttribute), false);

        internal static List<string> Groups() => FeedbackTypes().Select(t => Attr(t).Path.Split('/')[0]).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToList();

        // the implementing assembly → the name the README's Requirements table gives its package
        internal static readonly Dictionary<string, string> Packages = new Dictionary<string, string>
        {
            { "xpTURN.Feedback.URP", "Universal RP" },
            { "xpTURN.Feedback.UGUI", "uGUI" },
            { "xpTURN.Feedback.Cinemachine", "Cinemachine" },
            { "xpTURN.Feedback.InputSystem", "Input System" },
            { "xpTURN.Feedback.Animation", "Animation module" },
            { "xpTURN.Feedback.Particles", "Particle System module" },
            { "xpTURN.Feedback.Physics", "Physics module" },
            { "xpTURN.Feedback.Physics2D", "Physics 2D module" },
            { "xpTURN.Feedback.Video", "Video module" },
            { "xpTURN.Feedback.Timeline", "Timeline" },
        };

        static string Reference(string group, string body)
        {
            Dictionary<string, string> kept = Descriptions(body);
            var rows = new List<string[]>();
            foreach (Type t in FeedbackTypes().Where(t => Attr(t).Path.Split('/')[0] == group).OrderBy(t => Attr(t).Path, StringComparer.Ordinal))
            {
                FeedbackTargetAttribute a = Attr(t);
                bool target = typeof(TargetItem).IsAssignableFrom(t);
                TargetTraits traits = target ? ((TargetItem)Activator.CreateInstance(t)).Traits : null;
                string motions = target ? string.Join(" · ", Enum.GetValues(typeof(MotionKind)).Cast<MotionKind>().Where(traits.Supports)) : "—";
                string category = "—";
                if (typeof(EffectItem).IsAssignableFrom(t))
                {
                    FeedbackCategory c = ((EffectItem)Activator.CreateInstance(t)).DeclaredCategory;
                    category = c == FeedbackCategory.None ? "—" : c.ToString();
                }
                string needs = a.Implementation.Length == 0 ? "—" : Packages.TryGetValue(a.Implementation, out string n) ? n : throw new InvalidOperationException("no package name for " + a.Implementation);
                string key = "`" + a.Path + "`";
                kept.TryGetValue(key, out string description);
                rows.Add(new[] { key, motions, target ? traits.ValueKind.ToString() : "—", category, a.Preview == PreviewGrade.DisplayOnly ? "Display only" : a.Preview.ToString(), needs, description ?? "" });
            }
            if (rows.Count == 0)
                throw new InvalidOperationException("no feedback in group " + group);
            return Table(new[] { "Feedback", "Motions", "Value", "Category", "Preview", "Needs", "Description" }, rows);
        }

        // ---- the validation rules: rules:<anything> — one row per id letter
        static string Rules(string body)
        {
            Dictionary<string, string> kept = Descriptions(body);
            var rows = new List<string[]>();
            foreach (IGrouping<char, FeedbackRule> g in FeedbackValidator.Rules.Where(r => r.GetType().Assembly == typeof(FeedbackRule).Assembly).GroupBy(r => r.Id[3]).OrderBy(g => g.Key))
            {
                List<int> numbers = g.Select(r => int.Parse(r.Id.Substring(4))).OrderBy(n => n).ToList();
                List<int> gaps = Enumerable.Range(numbers[0], numbers[numbers.Count - 1] - numbers[0] + 1).Except(numbers).ToList();
                string key = $"`FB-{g.Key}{numbers[0]:00}`" + (numbers.Count > 1 ? $" – `FB-{g.Key}{numbers[numbers.Count - 1]:00}`" : "") + (gaps.Count > 0 ? " (no " + string.Join(", ", gaps.Select(n => $"{g.Key}{n:00}")) + ")" : "");
                string severity = string.Join(" · ", new[] { RuleSeverity.Error, RuleSeverity.Warning, RuleSeverity.Info }.Select(s => (s, n: g.Count(r => r.Severity == s))).Where(x => x.n > 0).Select(x => x.s + " " + x.n));
                string scope = string.Join(" · ", g.Select(r => r.Scope == RuleScope.Project ? "the project" : "objects").Distinct().OrderBy(x => x, StringComparer.Ordinal));
                kept.TryGetValue(key, out string description);
                rows.Add(new[] { key, severity, scope, description ?? "" });
            }
            return Table(new[] { "Rules", "Severity", "Checks", "Description" }, rows);
        }

        // ---- the motions: motions:<anything> — one row per motion: timed or instant, its writes, the targets taking it
        static string Motions(string body)
        {
            Dictionary<string, string> kept = Descriptions(body);
            Dictionary<MotionKind, FeedbackMotion> data = typeof(FeedbackMotion).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(FeedbackMotion).IsAssignableFrom(t))
                .Select(t => (FeedbackMotion)Activator.CreateInstance(t)).ToDictionary(m => m.Kind);
            List<TargetTraits> targets = FeedbackTypes().Where(t => typeof(TargetItem).IsAssignableFrom(t)).Select(t => ((TargetItem)Activator.CreateInstance(t)).Traits).ToList();
            var rows = new List<string[]>();
            foreach (MotionKind kind in Enum.GetValues(typeof(MotionKind)))
            {
                MotionEvaluator e = data.TryGetValue(kind, out FeedbackMotion m) ? MotionEvaluators.Create(m, 1) : throw new InvalidOperationException("no motion data for " + kind);
                string key = "`" + kind + "`";
                kept.TryGetValue(key, out string description);
                rows.Add(new[] { key, e.IsInstant ? "Instant" : "Timed", Writes(m, e.Write), targets.Count(t => t.Supports(kind)).ToString(), description ?? "" });
            }
            return Table(new[] { "Motion", "Kind", "Writes", "Targets", "Description" }, rows);
        }

        // the default write, then each other write a value of one of the parameters' enum fields gives: "Offset · Persist (Mode: Move To)"
        static string Writes(FeedbackMotion m, MotionWrite first)
        {
            var writes = new List<string> { first.ToString() };
            FieldInfo pf = m.GetType().GetField("m_Params", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (pf == null)
                return first.ToString();
            foreach (FieldInfo ef in pf.FieldType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Where(f => f.FieldType.IsEnum))
            {
                foreach (object v in Enum.GetValues(ef.FieldType))
                {
                    object p = pf.GetValue(m);
                    ef.SetValue(p, v);
                    var copy = (FeedbackMotion)Activator.CreateInstance(m.GetType());
                    pf.SetValue(copy, p);
                    MotionWrite w = MotionEvaluators.Create(copy, 1).Write;
                    if (w != first)
                        writes.Add(w + " (" + ObjectNames.NicifyVariableName(ef.Name) + ": " + ObjectNames.NicifyVariableName(v.ToString()) + ")");
                }
            }
            return string.Join(" · ", writes.Distinct());
        }

        // ---- the samples: samples:<anything> — one row per card in the catalogs' order (as the hub lists them): its README's
        // title, its bundle, and the packages its assembly needs beyond the ones every sample needs (named as the reference
        // names them)
        static string Samples(string body)
        {
            Dictionary<string, string> kept = Descriptions(body);
            string samples = Path.Combine(DocRegions.PackageRoot, "Samples~");
            var assetOf = new Dictionary<string, string>();
            foreach (string meta in Directory.GetFiles(samples, "*.asset.meta", SearchOption.AllDirectories))
                assetOf[Regex.Match(File.ReadAllText(meta), @"guid: (?<g>[0-9a-f]{32})").Groups["g"].Value] = meta.Substring(0, meta.Length - 5);
            var named = new Dictionary<string, string>();
            foreach (string a in Directory.GetFiles(Path.Combine(DocRegions.PackageRoot, "Runtime"), "*.asmdef", SearchOption.AllDirectories))
            {
                Asmdef d = JsonUtility.FromJson<Asmdef>(File.ReadAllText(a));
                foreach (VersionDefine v in d.versionDefines ?? new VersionDefine[0])
                    if (Packages.TryGetValue(d.name, out string n))
                        named[v.name] = n;
            }
            var needs = new Dictionary<string, List<string>>();
            foreach (string a in Directory.GetFiles(samples, "*.asmdef", SearchOption.AllDirectories))
            {
                Asmdef d = JsonUtility.FromJson<Asmdef>(File.ReadAllText(a));
                needs[Path.GetDirectoryName(a)] = (d.versionDefines ?? new VersionDefine[0]).Where(v => (d.defineConstraints ?? new string[0]).Contains(v.define)).Select(v => v.name).ToList();
            }
            List<string> common = needs.Values.Aggregate((x, y) => x.Intersect(y).ToList());
            var rows = new List<string[]>();
            var titles = new HashSet<string>();
            foreach (string catalog in Directory.GetFiles(samples, "*Catalog.asset", SearchOption.AllDirectories).OrderBy(c => int.Parse(Regex.Match(File.ReadAllText(c), @"\n  Order: (?<o>-?\d+)").Groups["o"].Value)))
            {
                string text = File.ReadAllText(catalog);
                string bundle = Regex.Match(text, @"\n  Bundle: (?<b>[^\n]+)").Groups["b"].Value;
                foreach (Match card in Regex.Matches(text.Substring(text.IndexOf("\n  Cards:", StringComparison.Ordinal)), @"guid: (?<g>[0-9a-f]{32}), type: 2"))
                {
                    string asset = assetOf.TryGetValue(card.Groups["g"].Value, out string f) ? f : throw new InvalidOperationException("a card the catalog " + bundle + " names is not in Samples~");
                    string dir = Path.GetDirectoryName(asset);
                    string readme = Path.Combine(dir, "README.md");
                    string title = File.Exists(readme) ? Regex.Match(File.ReadAllText(readme), @"\A# (?<t>[^\r\n]+)").Groups["t"].Value : throw new InvalidOperationException("no README next to " + asset);
                    if (!titles.Add(title))
                        throw new InvalidOperationException("two samples named " + title);
                    string owner = dir;
                    while (!needs.ContainsKey(owner) && owner.Length > samples.Length)
                        owner = Path.GetDirectoryName(owner);
                    List<string> own = needs.TryGetValue(owner, out List<string> l) ? l.Except(common).ToList() : throw new InvalidOperationException("no assembly above " + asset);
                    string also = own.Count == 0 ? "—" : string.Join(" · ", own.Select(p => named.TryGetValue(p, out string n) ? n : throw new InvalidOperationException("no package name for " + p)));
                    kept.TryGetValue(title, out string description);
                    rows.Add(new[] { title, bundle, also, description ?? "" });
                }
            }
            if (rows.Count == 0)
                throw new InvalidOperationException("no sample card");
            return Table(new[] { "Sample", "Bundle", "Also needs", "Description" }, rows);
        }

        // ---- the presets: presets:<anything> — one row per preset in the package's Presets folder, by name: its GUID (what
        // a scene or a prefab file writes to link it), its items as the Add menu names them (a target other than the object
        // itself in brackets), the components the object must have for the items that target it, and the packages the
        // items need (named as the reference names them)
        static string Presets(string body)
        {
            Dictionary<string, string> kept = Descriptions(body);
            var rows = new List<string[]>();
            IEnumerable<(string guid, FeedbackPreset preset)> presets = AssetDatabase.FindAssets("t:FeedbackPreset", new[] { "Packages/com.xpturn.feedback/Presets" })
                .Select(g => (g, AssetDatabase.LoadAssetAtPath<FeedbackPreset>(AssetDatabase.GUIDToAssetPath(g))))
                .OrderBy(p => p.Item2.name, StringComparer.Ordinal);
            foreach ((string guid, FeedbackPreset preset) in presets)
            {
                // an item whose type this project does not have (its optional package is not installed): the table is
                // kept as written
                if (preset.m_Items.Any(i => i == null))
                    return null;
                var items = new List<string>();
                var needs = new List<string>();
                var packages = new List<string>();
                foreach (FeedbackItem item in preset.m_Items)
                {
                    Type t = item.GetType();
                    FeedbackTargetAttribute a = Attr(t) ?? throw new InvalidOperationException("an item without an Add menu path in the preset " + preset.name);
                    var target = item as TargetItem;
                    int motion = target != null && target.m_Motion != null ? (int)target.m_Motion.Kind : CatalogLeaf.NoMotion;
                    CatalogLeaf leaf = FeedbackCatalog.All.FirstOrDefault(l => l.Type == t && l.Motion == motion);
                    if (leaf.Type == null)
                        throw new InvalidOperationException("no Add menu entry for " + t.Name + " in the preset " + preset.name);
                    bool onObject = target != null && !a.Global && target.m_Target.m_Mode == TargetRefMode.Self;
                    string where = target == null || a.Global || onObject ? "" : " (" + ObjectNames.NicifyVariableName(target.m_Target.m_Mode.ToString()).ToLowerInvariant() + ")";
                    items.Add("`" + leaf.Path + "`" + where);
                    string required = a.Requires != null ? a.Requires.Name : string.IsNullOrEmpty(a.RequiresName) ? null : a.RequiresName.Split(',')[0].Split('.').Last();
                    if (onObject && required != null && a.Requires != typeof(Transform))
                        needs.Add(required);
                    if (a.Implementation.Length > 0)
                        packages.Add(Packages.TryGetValue(a.Implementation, out string n) ? n : throw new InvalidOperationException("no package name for " + a.Implementation));
                }
                kept.TryGetValue(preset.name, out string description);
                rows.Add(new[] { preset.name, "`" + guid + "`", string.Join(" · ", items), Joined(needs), Joined(packages), description ?? "" });
            }
            if (rows.Count == 0)
                throw new InvalidOperationException("no preset");
            return Table(new[] { "Preset", "GUID", "Items", "The object needs", "Packages", "Use for" }, rows);
        }

        static string Joined(List<string> list) => list.Count == 0 ? "—" : string.Join(" · ", list.Distinct());

        [Serializable]
        sealed class Asmdef
        {
            public string name;
            public string[] defineConstraints;
            public VersionDefine[] versionDefines;
        }

        [Serializable]
        sealed class VersionDefine
        {
            public string name;
            public string define;
        }

        // ---- the scripting API: api:<full type name>
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        const BindingFlags Inherited = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // protected, or protected internal — what a derived type in another assembly sees
        static bool Family(MethodBase m) => m != null && (m.IsFamily || m.IsFamilyOrAssembly);

        // a generic method's <T, …>
        static string TypeParameters(MethodInfo m) => m.IsGenericMethodDefinition ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">" : "";

        internal static List<Type> Exported() => RuntimeAssemblies().SelectMany(a => a.GetExportedTypes()).ToList();

        // a public member other than a parameterless constructor, in table order: kind, then name, then parameters
        internal static List<(MemberInfo member, string signature)> Members(Type t)
        {
            var list = new List<(int order, MemberInfo m, string sig, int count)>();
            if (t.IsEnum)
                return new List<(MemberInfo, string)>();
            foreach (ConstructorInfo c in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Where(c => c.GetParameters().Length > 0))
                list.Add((0, c, DocComments.ShortName(t) + "(" + Params(c.GetParameters()) + ")", c.GetParameters().Length));
            foreach (EventInfo e in t.GetEvents(Declared))
                list.Add((1, e, "event " + Name(e.EventHandlerType) + " " + e.Name, 0));
            foreach (FieldInfo f in t.GetFields(Declared))
                list.Add((2, f, (f.IsLiteral ? "const " : f.IsStatic ? "static " : "") + (f.IsInitOnly ? "readonly " : "") + Name(f.FieldType) + " " + f.Name, 0));
            foreach (PropertyInfo p in t.GetProperties(Declared))
            {
                MethodInfo get = p.GetGetMethod(), set = p.GetSetMethod();
                list.Add((3, p, ((get ?? set).IsStatic ? "static " : "") + Name(p.PropertyType) + " " + p.Name + " { " + (get != null ? "get; " : "") + (set != null ? "set; " : "") + "}", 0));
            }
            foreach (MethodInfo m in t.GetMethods(Declared).Where(m => !m.IsSpecialName || m.Name.StartsWith("op_", StringComparison.Ordinal)))
            {
                bool extension = m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false);
                string name = m.Name.StartsWith("op_", StringComparison.Ordinal) ? "operator " + Operator(m.Name) : m.Name + TypeParameters(m);
                list.Add((4, m, (m.IsStatic && !extension ? "static " : "") + Name(m.ReturnType) + " " + name + "(" + (extension ? "this " : "") + Params(m.GetParameters()) + ")", m.GetParameters().Length));
            }
            // a class one can derive from: its protected members (the ones a derived type overrides or calls) — a sealed one's
            // are no one's
            if (t.IsClass && !t.IsSealed)
            {
                foreach (ConstructorInfo c in t.GetConstructors(Inherited).Where(c => Family(c) && c.GetParameters().Length > 0))
                    list.Add((0, c, "protected " + DocComments.ShortName(t) + "(" + Params(c.GetParameters()) + ")", c.GetParameters().Length));
                foreach (PropertyInfo p in t.GetProperties(Inherited).Where(p => Family(p.GetGetMethod(true) ?? p.GetSetMethod(true))))
                {
                    MethodInfo get = p.GetGetMethod(true), set = p.GetSetMethod(true);
                    list.Add((3, p, "protected " + Name(p.PropertyType) + " " + p.Name + " { " + (Family(get) ? "get; " : "") + (Family(set) ? "set; " : "") + "}", 0));
                }
                foreach (MethodInfo m in t.GetMethods(Inherited).Where(m => Family(m) && !m.IsSpecialName))
                    list.Add((4, m, "protected " + (m.IsStatic ? "static " : "") + Name(m.ReturnType) + " " + m.Name + TypeParameters(m) + "(" + Params(m.GetParameters()) + ")", m.GetParameters().Length));
            }
            return list.OrderBy(x => x.order).ThenBy(x => x.m is ConstructorInfo ? "" : x.m.Name, StringComparer.Ordinal).ThenBy(x => x.count).ThenBy(x => x.sig, StringComparer.Ordinal)
                .Select(x => (x.m, x.sig)).ToList();
        }

        // the types with members, then the package's enums their signatures name
        internal static List<Type> ApiTypes()
        {
            List<Type> exported = Exported();
            var set = new HashSet<Type>(exported);
            List<Type> api = exported.Where(t => !t.IsEnum && Members(t).Count > 0).ToList();
            var enums = new HashSet<Type>();
            void Add(Type x)
            {
                if (x == null)
                    return;
                if (x.IsByRef || x.IsArray)
                {
                    Add(x.GetElementType());
                    return;
                }
                if (x.IsGenericType)
                {
                    foreach (Type a in x.GetGenericArguments())
                        Add(a);
                }
                if (x.IsEnum && set.Contains(x))
                    enums.Add(x);
            }
            foreach ((MemberInfo m, string _) in api.SelectMany(Members))
            {
                switch (m)
                {
                    case MethodBase mb:
                        foreach (ParameterInfo p in mb.GetParameters())
                            Add(p.ParameterType);
                        if (mb is MethodInfo mi)
                            Add(mi.ReturnType);
                        break;
                    case FieldInfo f: Add(f.FieldType); break;
                    case PropertyInfo p: Add(p.PropertyType); break;
                    case EventInfo e: Add(e.EventHandlerType); break;
                }
            }
            return api.Concat(enums).OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
        }

        // the types a user adds or creates in the editor and that have no public member: a component on the Add Component
        // menu, a ScriptableObject
        internal static List<Type> UserTypes()
        {
            List<Type> api = ApiTypes();
            return Exported().Where(t => !t.IsAbstract && !api.Contains(t))
                .Where(t => (typeof(Component).IsAssignableFrom(t) && t.GetCustomAttribute<AddComponentMenu>() is AddComponentMenu a && a.componentMenu.Length > 0)
                            || typeof(ScriptableObject).IsAssignableFrom(t))
                .OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
        }

        static string Api(string fullName)
        {
            Type t = ApiTypes().FirstOrDefault(x => x.FullName == fullName);
            if (t == null)
                return null;
            DocComments.Entry type = DocComments.Of(t).FirstOrDefault(e => e.Kind == "type" && e.Xml != null);
            string head = DocComments.Render(type) + "\n\n";
            if (t.IsEnum)
            {
                IEnumerable<string[]> values = t.GetFields(BindingFlags.Public | BindingFlags.Static).OrderBy(f => f.MetadataToken)
                    .Select(f => new[] { "`" + f.Name + " = " + Convert.ToInt64(f.GetRawConstantValue()) + "`", DocComments.Render(DocComments.Find(t, f, out _)) });
                return head + Table(new[] { "Value", "Description" }, values);
            }
            return head + Table(new[] { "Member", "Description" }, Members(t).Select(m => new[] { "`" + m.signature + "`", DocComments.Render(DocComments.Find(t, m.member, out _)) }));
        }

        static string Operator(string op)
        {
            switch (op)
            {
                case "op_Equality": return "==";
                case "op_Inequality": return "!=";
                case "op_Implicit": return "implicit";
                case "op_Explicit": return "explicit";
                default: return op.Substring(3);
            }
        }

        static string Params(ParameterInfo[] ps)
            => string.Join(", ", ps.Select(p =>
            {
                Type type = p.ParameterType;
                string modifier = "";
                if (type.IsByRef)
                {
                    type = type.GetElementType();
                    modifier = p.IsIn ? "in " : p.IsOut ? "out " : "ref ";
                }
                string value = p.HasDefaultValue ? " = " + (p.DefaultValue == null ? "default" : p.DefaultValue is bool b ? (b ? "true" : "false") : p.DefaultValue is string s ? "\"" + s + "\"" : p.DefaultValue is Enum ? Name(type) + "." + p.DefaultValue : Convert.ToString(p.DefaultValue, System.Globalization.CultureInfo.InvariantCulture)) : "";
                return modifier + Name(type) + " " + p.Name + value;
            }));

        static readonly Dictionary<Type, string> Keywords = new Dictionary<Type, string>
        {
            { typeof(void), "void" }, { typeof(bool), "bool" }, { typeof(int), "int" }, { typeof(uint), "uint" }, { typeof(long), "long" },
            { typeof(float), "float" }, { typeof(double), "double" }, { typeof(string), "string" }, { typeof(object), "object" }, { typeof(byte), "byte" },
        };

        internal static string Name(Type t)
        {
            if (Keywords.TryGetValue(t, out string k))
                return k;
            if (t.IsArray)
                return Name(t.GetElementType()) + "[]";
            if (Nullable.GetUnderlyingType(t) is Type u)
                return Name(u) + "?";
            if (t.IsGenericParameter)
                return t.Name;
            if (t.IsGenericType)
                return DocComments.ShortName(t) + "<" + string.Join(", ", t.GetGenericArguments().Select(Name)) + ">";
            return t.Name;
        }
    }
}
