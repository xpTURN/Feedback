using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEditor;
using UnityEngine;
using xpTURN.Feedback.Editor;

namespace xpTURN.Feedback.Tests.Editor.Docs
{
    public sealed class DocsTests
    {
        // the least the documents hold — a check that reads less than this is empty, not green
        static class Floor
        {
            public const int Documents = 10;
            public const int Lines = 2100;
            public const int Regions = 80;
            public const int Names = 340;
            public const int Links = 60;
            public const int ApiTypes = 25;
            public const int Groups = 20;
            public const int KeptRows = 190;
            public const int Comments = 180;
        }

        static string Read(string file) => File.ReadAllText(file).Replace("\r\n", "\n");

        static string Short(string file) => file.Substring(DocRegions.PackageRoot.Length + 1);

        // the text outside code fences, by line
        static IEnumerable<(int line, string text)> Prose(string text)
        {
            bool fence = false;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("```", StringComparison.Ordinal))
                {
                    fence = !fence;
                    continue;
                }
                if (!fence)
                    yield return (i + 1, lines[i]);
            }
        }

        [Test]
        public void DX00_DocumentsAboveTheFloor()
        {
            List<string> docs = DocRegions.Documents();
            int lines = docs.Sum(f => Read(f).Split('\n').Length);
            int regions = docs.Sum(f => DocRegions.Find(Read(f)).Count);
            TestContext.WriteLine($"documents {docs.Count} · lines {lines} · regions {regions}");
            Assert.That(docs.Count, Is.GreaterThanOrEqualTo(Floor.Documents), "documents");
            Assert.That(lines, Is.GreaterThanOrEqualTo(Floor.Lines), "lines");
            Assert.That(regions, Is.GreaterThanOrEqualTo(Floor.Regions), "regions");
        }

        static readonly Regex[] s_Forbidden =
        {
            new Regex(@"\bFeel\b"), new Regex(@"\bMM[A-Z][A-Za-z]+"), new Regex(@"\bPlan-"), new Regex("§"),
            new Regex(@"\b(?!FB-)[A-Z]{1,3}-\d+[a-z]?\b"), new Regex(@"/Users/|/private/|(?<![\w~])~/"),
        };
        static readonly Regex s_Hangul = new Regex(@"[가-힣ㄱ-ㆎ]");

        // the words a document's line may not hold — "Game Feel", a sample bundle's name, aside; Hangul unless allowed
        internal static IEnumerable<string> Forbidden(string line, bool hangul)
        {
            line = Regex.Replace(line, "(?i)game feel", "");
            foreach (Regex r in s_Forbidden.Where(r => r.IsMatch(line)))
                yield return r.Match(line).Value;
            if (!hangul && s_Hangul.IsMatch(line))
                yield return s_Hangul.Match(line).Value;
        }

        [Test]
        public void DX01_NoForbiddenWords()
        {
            var bad = new List<string>();
            foreach (string file in DocRegions.Documents())
            {
                string[] all = Read(file).Split('\n');
                for (int i = 0; i < all.Length; i++)
                    foreach (string word in Forbidden(all[i], false))
                        bad.Add($"{Short(file)}:{i + 1} '{word}'");
            }
            Assert.That(bad, Is.Empty);
        }

        [Test]
        public void DX02_RegionsAreCurrent()
        {
            var stale = new List<string>();
            var used = new HashSet<string>();
            var snippetsUsed = new HashSet<string>();
            foreach (string file in DocRegions.Documents())
            {
                string text = Read(file);
                string next = DocRegions.Rewrite(text, DocSources.Produce);
                Dictionary<string, string> after = DocRegions.Find(next).GroupBy(r => r.key).ToDictionary(g => g.Key, g => g.First().body);
                foreach ((string key, string body) in DocRegions.Find(text))
                {
                    used.Add(key.Split(':')[0]);
                    if (key.StartsWith("snippet:", StringComparison.Ordinal))
                        snippetsUsed.Add(key.Substring(8));
                    if (after[key] != body)
                        stale.Add(Short(file) + " " + key);
                }
            }
            var (all, twice) = DocSources.Snippets();
            List<string> unused = all.Keys.Where(n => !snippetsUsed.Contains(n)).ToList();
            Assert.That(DocSources.Kinds.Keys, Is.SubsetOf(used), "every region kind is used");
            Assert.That(twice, Is.Empty, "snippet names twice");
            Assert.That(unused, Is.Empty, "snippets no document shows");
            Assert.That(stale, Is.Empty, "regions to rewrite (tools/write-docs.sh)");
        }

        [Test]
        public void DX02w_WriterTouchesOnlyRegions()
        {
            string dir = Path.Combine(Path.GetFullPath("Temp"), "DocsWriterProbe");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "probe.md");
            string before = "# Probe\r\n\nText — keep  it.  \n<!-- gen:count:motions -->0<!-- /gen:count:motions --> motions.\n\n<!-- gen:snippet:readme-hit -->\nold\n<!-- /gen:snippet:readme-hit -->\ntail without a newline";
            try
            {
                File.WriteAllText(file, before);
                List<string> first = DocsWriter.Rewrite(new[] { file }, true);
                string after = File.ReadAllText(file);
                List<string> second = DocsWriter.Rewrite(new[] { file }, true);
                string Outside(string s) => Regex.Replace(s, @"<!-- gen:(?<k>[^\n]+?) -->(\n?).*?<!-- /gen:\k<k> -->", "<$1>", RegexOptions.Singleline);
                Assert.That(first, Has.Count.EqualTo(1), "the first write changes the file");
                Assert.That(Outside(after), Is.EqualTo(Outside(before)), "the text outside the regions byte for byte");
                Assert.That(after, Does.Contain("<!-- gen:count:motions -->" + Enum.GetValues(typeof(MotionKind)).Length + "<!-- /gen:count:motions -->"), "an inline region stays inline");
                Assert.That(second, Is.Empty, "a second write changes nothing");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // the package's own menus — another package's menu is not checked (it is absent where that package is)
        static HashSet<string> Menus()
        {
            var menus = new HashSet<string>();
            foreach (MethodInfo m in TypeCache.GetMethodsWithAttribute<MenuItem>())
            {
                foreach (MenuItem a in m.GetCustomAttributes<MenuItem>())
                    menus.Add(a.menuItem);
            }
            foreach (Type t in TypeCache.GetTypesWithAttribute<AddComponentMenu>())
                menus.Add("Component/" + t.GetCustomAttribute<AddComponentMenu>().componentMenu);
            foreach (Type t in TypeCache.GetTypesWithAttribute<CreateAssetMenuAttribute>())
                menus.Add("Assets/Create/" + t.GetCustomAttribute<CreateAssetMenuAttribute>().menuName);
            return menus;
        }

        [Test]
        public void DX03_NamesExist()
        {
            Dictionary<string, Type> types = DocSources.RuntimeAssemblies().SelectMany(a => a.GetExportedTypes()).GroupBy(t => t.Name.Split('`')[0]).ToDictionary(g => g.Key, g => g.First());
            HashSet<string> menus = Menus();
            var paths = new HashSet<string>(FeedbackCatalog.All.Select(l => l.Path).Concat(DocSources.FeedbackTypes().Select(t => ((FeedbackTargetAttribute)Attribute.GetCustomAttribute(t, typeof(FeedbackTargetAttribute))).Path)));
            var groups = new HashSet<string>(paths.Select(p => p.Split('/')[0]));
            var code = new Regex(@"^(?<type>[A-Z][A-Za-z0-9]*)(?:<[^>]*>)?(?:\.(?<member>[A-Za-z_][A-Za-z0-9_]*))?(?:\(.*\))?$");
            var bad = new List<string>();
            int checkedNames = 0, other = 0;
            foreach (string file in DocRegions.Documents())
            {
                foreach ((int line, string text) in Prose(Read(file)))
                {
                    foreach (Match span in Regex.Matches(text, @"(?<!`)`([^`]+)`(?!`)"))
                    {
                        string s = span.Groups[1].Value;
                        string where = $"{Short(file)}:{line} `{s}`";
                        if (Regex.IsMatch(s, @"^(Tools|Window|Component|Assets > Create) > xpTURN > "))
                        {
                            checkedNames++;
                            if (!menus.Contains(s.Replace(" > ", "/")) && AbsentMenu(s) is string why)
                                bad.Add(where + " — no such menu (" + why + ")");
                        }
                        else if (s.Contains("/") && groups.Contains(s.Split('/')[0]))
                        {
                            checkedNames++;
                            if (!paths.Contains(s))
                                bad.Add(where + " — no such feedback");
                        }
                        else if (code.Match(s) is Match m && m.Success && types.TryGetValue(m.Groups["type"].Value, out Type t))
                        {
                            checkedNames++;
                            string member = m.Groups["member"].Value;
                            if (member.Length > 0 && t.GetMember(member, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length == 0)
                                bad.Add(where + " — " + t.Name + " has no public " + member);
                        }
                        else
                        {
                            other++;
                        }
                    }
                }
            }
            TestContext.WriteLine($"checked {checkedNames} · other {other}");
            Assert.That(checkedNames, Is.GreaterThanOrEqualTo(Floor.Names), "names checked");
            Assert.That(bad, Is.Empty);
        }

        [Test]
        public void DX06_EveryCodeBlockIsASnippet()
        {
            var bad = new List<string>();
            foreach (string file in DocRegions.Documents())
            {
                string text = Read(file);
                int blocks = Regex.Matches(text, "^```csharp$", RegexOptions.Multiline).Count;
                int snippets = DocRegions.Find(text).Count(r => r.key.StartsWith("snippet:", StringComparison.Ordinal));
                if (blocks != snippets)
                    bad.Add($"{Short(file)}: {blocks} csharp blocks · {snippets} snippet regions");
            }
            Assert.That(bad, Is.Empty);
        }

        static string Slug(string heading)
            => Regex.Replace(heading.Trim().ToLowerInvariant(), @"[^\p{L}\p{Nd} \-_]", "").Replace(' ', '-');

        internal static List<string> Headings(string text)
            => Prose(text).Where(l => Regex.IsMatch(l.text, "^#{1,6} ")).Select(l => Slug(l.text.TrimStart('#'))).ToList();

        [Test]
        public void DX07_LinksResolve()
        {
            string root = DocRegions.PackageRoot;
            string docs = Path.Combine(root, "Documentation~");
            string readme = Path.Combine(root, "README.md");
            string index = Path.Combine(docs, "index.md");
            var bad = new List<string>();
            int links = 0;
            var fromReadme = new HashSet<string>();
            var fromIndex = new HashSet<string>();
            foreach (string file in DocRegions.Documents())
            {
                string text = Read(file);
                foreach (string twice in Headings(text).GroupBy(h => h).Where(g => g.Count() > 1).Select(g => g.Key))
                    bad.Add($"{Short(file)} — two headings make the anchor #{twice}");
                foreach ((int line, string prose) in Prose(text))
                {
                    foreach (Match m in Regex.Matches(prose, @"\[[^\]]*\]\((?<to>[^)\s]+)\)"))
                    {
                        links++;
                        string to = m.Groups["to"].Value;
                        string where = $"{Short(file)}:{line} ({to})";
                        if (to.StartsWith("http", StringComparison.Ordinal))
                        {
                            if (!to.StartsWith("https://", StringComparison.Ordinal))
                                bad.Add(where + " — not https");
                            continue;
                        }
                        string[] parts = to.Split('#');
                        string target = parts[0].Length == 0 ? file : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file), parts[0]));
                        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(target))
                        {
                            bad.Add(where + " — no such file in the package");
                            continue;
                        }
                        if (parts.Length > 1 && !Headings(Read(target)).Contains(parts[1]))
                            bad.Add(where + " — no such heading");
                        if (Path.GetDirectoryName(target) == docs && target != index)
                        {
                            if (file == readme)
                                fromReadme.Add(Path.GetFileName(target));
                            if (file == index)
                                fromIndex.Add(Path.GetFileName(target));
                        }
                    }
                }
            }
            var pages = new HashSet<string>(Directory.GetFiles(docs, "*.md").Select(Path.GetFileName).Where(n => n != "index.md"));
            TestContext.WriteLine($"links {links} · pages {pages.Count}");
            Assert.That(links, Is.GreaterThanOrEqualTo(Floor.Links), "links read");
            Assert.That(bad, Is.Empty);
            Assert.That(fromReadme, Is.EquivalentTo(pages), "the README's map lists every page");
            Assert.That(fromIndex, Is.EquivalentTo(pages), "index.md lists every page");
        }

        static string Doc(string name) => Path.Combine(DocRegions.PackageRoot, "Documentation~", name);

        static List<string> Keys(string file, string kind)
            => DocRegions.Find(Read(file)).Select(r => r.key).Where(k => k.StartsWith(kind + ":", StringComparison.Ordinal)).Select(k => k.Substring(kind.Length + 1)).ToList();

        static string Heading(Type t)
            => t.IsGenericTypeDefinition ? DocComments.ShortName(t) + "<" + string.Join(", ", t.GetGenericArguments().Select(a => a.Name)) + ">" : t.Name;

        [Test]
        public void DX04_EveryTypeAndGroupHasItsPlace()
        {
            List<string> keys = Keys(Doc("ScriptingAPI.md"), "api");
            List<Type> api = DocSources.ApiTypes();
            var exported = new HashSet<string>(DocSources.Exported().Select(t => t.FullName));
            List<string> missing = api.Select(t => t.FullName).Where(n => !keys.Contains(n)).ToList();
            List<string> notApi = keys.Where(k => exported.Contains(k) && api.All(t => t.FullName != k)).ToList();
            List<string> twice = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            List<string> groups = DocSources.Groups();
            List<string> groupKeys = Keys(Doc("Feedbacks.md"), "reference");
            var headings = new HashSet<string>(Prose(Read(Doc("ScriptingAPI.md"))).Where(l => l.text.StartsWith("### ", StringComparison.Ordinal)).Select(l => l.text.Substring(4).Trim()));
            var names = new HashSet<string>(DocSources.Exported().Select(Heading));
            List<string> userWithout = DocSources.UserTypes().Select(Heading).Where(h => !headings.Contains(h)).ToList();
            List<string> notTypes = headings.Where(h => !names.Contains(h) && Absent(h.Split('<')[0]) != null).ToList();
            TestContext.WriteLine($"api types {api.Count} · regions {keys.Count} · user types {DocSources.UserTypes().Count} · groups {groups.Count} · group regions {groupKeys.Count}");
            Assert.That(api.Count, Is.GreaterThanOrEqualTo(Floor.ApiTypes), "the api types read");
            Assert.That(groups.Count, Is.GreaterThanOrEqualTo(Floor.Groups), "the groups read");
            Assert.That(missing, Is.Empty, "api types without a region");
            Assert.That(notApi, Is.Empty, "regions for types without members");
            Assert.That(twice, Is.Empty, "a type twice");
            Assert.That(groups.Where(g => !groupKeys.Contains(g)), Is.Empty, "groups without a reference region");
            Assert.That(groupKeys.Where(g => !groups.Contains(g)), Is.Empty, "reference regions of no group");
            Assert.That(groupKeys.GroupBy(g => g).Where(g => g.Count() > 1).Select(g => g.Key), Is.Empty, "a group twice");
            Assert.That(userWithout, Is.Empty, "user types without a section");
            Assert.That(notTypes, Is.Empty, "### headings that are not public types");
        }

        [Test]
        public void DX05_KeptCellsWritten()
        {
            var empty = new List<string>();
            int rows = 0;
            foreach ((string key, string body) in new[] { "Feedbacks.md", "Concepts.md", "GettingStarted.md" }.SelectMany(d => DocRegions.Find(Read(Doc(d)))).Where(r => r.key.StartsWith("reference:", StringComparison.Ordinal) || r.key.StartsWith("rules:", StringComparison.Ordinal) || r.key.StartsWith("motions:", StringComparison.Ordinal) || r.key.StartsWith("samples:", StringComparison.Ordinal)))
            {
                foreach (string line in body.Split('\n').Where(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("|---", StringComparison.Ordinal)).Skip(1))
                {
                    rows++;
                    string[] cells = DocSources.Cells(line);
                    if (cells[cells.Length - 1].Length == 0)
                        empty.Add(key + " " + cells[0]);
                }
            }
            Assert.That(rows, Is.GreaterThanOrEqualTo(Floor.KeptRows), "the rows read");
            Assert.That(empty, Is.Empty, "rows without a description");
        }

        // why a type this version did not export may stay in the documents — null when its source is in an assembly that did
        // not compile here. Found by its declaration and the asmdef above it, not by the reader the regions use
        static string Absent(string name)
        {
            var compiled = new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies).Select(a => a.name));
            string runtime = Path.Combine(DocRegions.PackageRoot, "Runtime");
            var declared = new Regex(@"\bpublic\b[^\n]*\b(?:class|struct|interface|enum)\s+" + Regex.Escape(name) + @"\b");
            List<string> files = Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories).Where(f => declared.IsMatch(File.ReadAllText(f))).ToList();
            if (files.Count == 0)
                return "declared nowhere";
            foreach (string f in files)
            {
                string dir = Path.GetDirectoryName(f);
                while (dir != null && dir.Length >= runtime.Length && Directory.GetFiles(dir, "*.asmdef").Length == 0)
                    dir = Path.GetDirectoryName(dir);
                string asmdef = dir == null ? null : Directory.GetFiles(dir, "*.asmdef").FirstOrDefault();
                string assembly = asmdef == null ? null : JsonUtility.FromJson<AsmdefName>(File.ReadAllText(asmdef)).name;
                if (assembly == null || compiled.Contains(assembly))
                    return "its assembly " + (assembly ?? "(none)") + " compiled here";
            }
            return null;
        }

        // why a menu this version does not have may stay in the documents — null when the component or asset menu attribute
        // that names it is in an assembly that did not compile here
        static string AbsentMenu(string menu)
        {
            Match m = Regex.Match(menu, "^(?:Component|Assets > Create) > (?<path>.+)$");
            if (!m.Success)
                return "declared nowhere";
            var declared = new Regex(@"\[(?:AddComponentMenu\(|CreateAssetMenu\([^\]]*menuName = )""" + Regex.Escape(m.Groups["path"].Value.Replace(" > ", "/")) + @"""");
            string runtime = Path.Combine(DocRegions.PackageRoot, "Runtime");
            List<string> names = Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories).Where(f => declared.IsMatch(File.ReadAllText(f)))
                .Select(f => Regex.Match(File.ReadAllText(f), @"\bpublic\b[^\n]*\b(?:class|struct)\s+(?<n>\w+)").Groups["n"].Value).ToList();
            return names.Count == 0 ? "declared nowhere" : names.Select(Absent).FirstOrDefault(why => why != null);
        }

        // what a version without some optional package leaves as written: the api regions and the ### sections of the types it
        // did not compile — each in an assembly that is not here
        [Test]
        public void DX08_SkippedRegionsAreAbsentAssemblies()
        {
            var exported = new HashSet<string>(DocSources.Exported().Select(t => t.FullName));
            var names = new HashSet<string>(DocSources.Exported().Select(Heading));
            var skipped = Keys(Doc("ScriptingAPI.md"), "api").Where(k => !exported.Contains(k)).Select(k => k.Substring(k.LastIndexOf('.') + 1).Split('`')[0]).ToList();
            skipped.AddRange(Prose(Read(Doc("ScriptingAPI.md"))).Where(l => l.text.StartsWith("### ", StringComparison.Ordinal)).Select(l => l.text.Substring(4).Trim()).Where(h => !names.Contains(h)).Select(h => h.Split('<')[0]));
            List<string> bad = skipped.Distinct().Select(n => (n, why: Absent(n))).Where(x => x.why != null).Select(x => x.n + ": " + x.why).ToList();
            TestContext.WriteLine("skipped " + skipped.Count + ": " + string.Join(", ", skipped));
            Assert.That(bad, Is.Empty);
        }

        [Serializable]
        sealed class AsmdefName
        {
            public string name;
        }

        // the dev project has every optional package: no api region is skipped
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void DX08d_NothingSkippedInDevProject()
        {
            var exported = new HashSet<string>(DocSources.Exported().Select(t => t.FullName));
            var names = new HashSet<string>(DocSources.Exported().Select(Heading));
            Assert.That(Keys(Doc("ScriptingAPI.md"), "api").Where(k => !exported.Contains(k)), Is.Empty, "api regions skipped");
            Assert.That(Prose(Read(Doc("ScriptingAPI.md"))).Where(l => l.text.StartsWith("### ", StringComparison.Ordinal)).Select(l => l.text.Substring(4).Trim()).Where(h => !names.Contains(h)), Is.Empty, "sections of types not here");
        }

        [Test]
        public void DX10_XmlComments()
        {
            var problems = new List<string>();
            int reflection = 0, paired = 0;
            List<Type> api = DocSources.ApiTypes();
            var scope = new HashSet<string>();
            foreach (Type t in api.Concat(DocSources.UserTypes()))
            {
                List<DocComments.Entry> of = DocComments.Of(t);
                scope.Add(DocComments.ShortName(t));
                int typeComments = of.Count(e => e.Kind == "type" && e.Xml != null);
                if (typeComments != 1)
                    problems.Add($"{t.Name}: {typeComments} type comments");
                if (!api.Contains(t))
                    continue;
                var matched = new HashSet<DocComments.Entry>();
                IEnumerable<MemberInfo> members = t.IsEnum ? t.GetFields(BindingFlags.Public | BindingFlags.Static) : DocSources.Members(t).Select(m => m.member);
                foreach (MemberInfo m in members)
                {
                    reflection++;
                    DocComments.Entry e = DocComments.Find(t, m, out string why);
                    if (e == null)
                    {
                        problems.Add($"{t.Name}.{m.Name}: {why}");
                        continue;
                    }
                    paired++;
                    matched.Add(e);
                    if (e.Xml == null)
                        problems.Add($"{e}: no /// comment");
                }
                // a parameterless constructor is not a row (DocSources.Members)
                foreach (DocComments.Entry e in of.Where(e => e.Kind != "type" && !matched.Contains(e) && !(e.Kind == "ctor" && e.ParamNames.Length == 0)))
                    problems.Add($"{e}: a public declaration reflection does not list");
            }
            var types = DocSources.Exported().ToDictionary(t => DocComments.ShortName(t), t => t);
            foreach (DocComments.Entry e in DocComments.All.Where(e => e.Xml != null || e.Kind == "orphan"))
            {
                if (e.Kind != "orphan" && !scope.Contains(e.Type))
                    continue;
                problems.AddRange(DocComments.Form(e).Select(p => $"{e}: {p}"));
                foreach (string cref in DocComments.Crefs(e))
                {
                    string[] parts = cref.Split('.');
                    if (!types.TryGetValue(parts[0], out Type ct) || (parts.Length > 1 && ct.GetMember(parts[1], BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Length == 0) || parts.Length > 2)
                        problems.Add($"{e}: cref \"{cref}\" names no public type or member of the package");
                }
            }
            int read = DocComments.All.Count(e => e.Xml != null);
            TestContext.WriteLine($"comments read {read} · members by reflection {reflection} · paired {paired} · problems {problems.Count}");
            Assert.That(read, Is.GreaterThanOrEqualTo(Floor.Comments), "the /// comments read");
            Assert.That(problems, Is.Empty);
            Assert.That(paired, Is.EqualTo(reflection), "every member paired with its declaration");
        }

        // the reference names a package as the README's Requirements table does
        [Test]
        public void DX03n_NeedsAreReadmePackages()
        {
            string readme = Read(Path.Combine(DocRegions.PackageRoot, "README.md"));
            Match table = Regex.Match(readme, @"## Requirements\n(?<s>.*?)\n## ", RegexOptions.Singleline);
            var packages = new HashSet<string>(table.Groups["s"].Value.Split('\n').Where(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("|---", StringComparison.Ordinal)).Skip(1).Select(l => DocSources.Cells(l)[0]));
            Assert.That(packages.Count, Is.GreaterThanOrEqualTo(5), "the README's packages read");
            Assert.That(DocSources.Packages.Values.Where(n => !packages.Contains(n)), Is.Empty, "names the README does not give");
        }

        // the performance page is measured on the README's tested editor lines (major.minor) — a patch release of the same
        // line keeps the page, a new line means measuring again
        [Test]
        public void DX03v_PerformanceVersionsAreTested()
        {
            string readme = Read(Path.Combine(DocRegions.PackageRoot, "README.md"));
            string page = Read(Doc("Performance.md"));
            Match tested = Regex.Match(readme, @"Tested with Unity\s+(?<a>\d+\.\d+\.\d+\w+)\s+and\s+(?<b>\d+\.\d+\.\d+\w+)");
            Match measured = Regex.Match(page, @"Unity\s+(?<a>\d+\.\d+\.\d+\w+)\s+and\s+(?<b>\d+\.\d+\.\d+\w+)");
            Assert.That(tested.Success && measured.Success, "the versions read");
            static string Line(Group version) => Regex.Match(version.Value, @"^\d+\.\d+").Value;
            Assert.That(Line(measured.Groups["a"]) + " · " + Line(measured.Groups["b"]), Is.EqualTo(Line(tested.Groups["a"]) + " · " + Line(tested.Groups["b"])),
                "the performance page's editor lines are the tested ones");
        }

        // the steps from a class to a played feedback name the validator's codes and the Add menu's tail as the editor has them
        [Test]
        public void DX11_CustomStepsMatchTheEditor()
        {
            Match steps = Regex.Match(Read(Doc("ScriptingAPI.md")), @"\*\*From a class to a played feedback\.\*\*\n(?<s>.*?)\n### ", RegexOptions.Singleline);
            Assert.That(steps.Success, "the steps read");
            string text = steps.Groups["s"].Value;
            List<string> codes = Regex.Matches(text, @"\bFB-[A-Z]\d{2}\b").Cast<Match>().Select(m => m.Value).Distinct().ToList();
            var ids = new HashSet<string>(FeedbackValidator.Rules.Select(r => r.Id));
            Assert.That(codes.Count, Is.GreaterThanOrEqualTo(3), "the codes read");
            Assert.That(codes.Where(c => !ids.Contains(c)), Is.Empty, "codes the validator does not have");
            Assert.That(text, Does.Contain("**" + FeedbackCatalog.NotInBuilds.Trim() + "**"), "the Add menu's tail");
        }
    }
}
