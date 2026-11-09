using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor.Docs
{
    // the agent skill under Skills~ (a Claude Code plugin) and the marketplace beside the package: the files the Agent Skills
    // format and Claude Code read. The skill's text is also a document — DocsTests read it like the others
    public sealed class SkillTests
    {
        const string SkillName = "xpturn-feedback";
        const string PackageName = "com.xpturn.feedback";

        // the front matter fields of the Agent Skills format — another field fails outside Claude Code
        static readonly string[] Fields = { "name", "description", "license", "compatibility", "metadata", "allowed-tools" };

        // a pointer at a page of the installed package: `Documentation~/<page>.md#<section>` or `README.md#<section>`
        static readonly Regex Pointer = new Regex(@"^(?<file>Documentation~/[A-Za-z]+\.md|README\.md)(?:#(?<section>[a-z0-9-]+))?$");

        static string SkillsRoot => Path.Combine(DocRegions.PackageRoot, "Skills~");
        static string SkillFile => Path.Combine(SkillsRoot, "skills", SkillName, "SKILL.md");
        static string AuthoringFile => Path.Combine(SkillsRoot, "skills", SkillName, "references", "authoring-files.md");

        static string Read(string file) => File.ReadAllText(file).Replace("\r\n", "\n");

        static string PackageVersion => JsonUtility.FromJson<PackageJson>(Read(Path.Combine(DocRegions.PackageRoot, "package.json"))).version;

        [Test]
        public void SK01_DocumentsHoldTheSkill()
        {
            Assert.That(File.Exists(SkillFile), Is.True, "SKILL.md");
            List<string> docs = DocRegions.Documents();
            string[] skillFiles = Directory.GetFiles(SkillsRoot, "*.md", SearchOption.AllDirectories);
            Assert.That(docs, Is.SupersetOf(skillFiles), "every .md under Skills~ is a document the documents' tests read");
            Assert.That(docs, Does.Contain(Path.Combine(DocRegions.PackageRoot, "README.md")), "README.md");
        }

        [Test]
        public void SK02_FrontMatter()
        {
            string[] skills = Directory.GetDirectories(Path.Combine(SkillsRoot, "skills")).Select(Path.GetFileName).ToArray();
            Assert.That(skills, Is.EqualTo(new[] { SkillName }), "one skill");
            var problems = new List<string>();
            Dictionary<string, object> fields = FrontMatter(Read(SkillFile), problems);
            problems.AddRange(fields.Keys.Where(k => !Fields.Contains(k)).Select(k => "not a field of the format: " + k));
            problems.AddRange(fields.Where(f => f.Key != "metadata" && !(f.Value is string)).Select(f => f.Key + " is not a string"));

            string name = fields.TryGetValue("name", out object n) ? n as string : null;
            if (name != SkillName)
                problems.Add($"name '{name}' — the folder is {SkillName}");
            if (name == null || name.Length > 64 || !Regex.IsMatch(name, "^[a-z0-9]+(-[a-z0-9]+)*$"))
                problems.Add($"name '{name}' — lowercase letters, digits and single hyphens, at most 64");
            string description = fields.TryGetValue("description", out object d) ? d as string : null;
            if (string.IsNullOrEmpty(description) || description.Length > 1024)
                problems.Add($"description of {description?.Length ?? 0} characters — 1 to 1024");
            if (fields.TryGetValue("compatibility", out object c) && (!(c is string cs) || cs.Length == 0 || cs.Length > 500))
                problems.Add("compatibility — 1 to 500 characters");

            if (!(fields.TryGetValue("metadata", out object m) && m is Dictionary<string, string> metadata))
            {
                problems.Add("no metadata map");
            }
            else
            {
                if (!metadata.TryGetValue("package", out string package) || package != PackageName)
                    problems.Add($"metadata.package '{package}' — {PackageName}");
                if (!metadata.TryGetValue("version", out string version) || version != PackageVersion)
                    problems.Add($"metadata.version '{version}' — the package is {PackageVersion}");
            }
            Assert.That(problems, Is.Empty);
        }

        [Test]
        public void SK03_PluginManifest()
        {
            Plugin plugin = JsonUtility.FromJson<Plugin>(Read(Path.Combine(SkillsRoot, ".claude-plugin", "plugin.json")));
            Assert.That(plugin.name, Is.EqualTo(SkillName), "name");
            Assert.That(plugin.version, Is.EqualTo(PackageVersion), "the plugin's version is the package's");
            Assert.That(plugin.description, Is.Not.Null.And.Not.Empty, "description");
            Assert.That(Directory.Exists(Path.Combine(SkillsRoot, "skills", SkillName)), Is.True, "the skill in the plugin's skills folder");
        }

        [Test]
        public void SK04_Marketplace()
        {
            string repository = DocRegions.RepositoryRoot;
            if (repository == null)
                Assert.Ignore("an installed package — the marketplace is beside the package only in its repository");
            Marketplace marketplace = JsonUtility.FromJson<Marketplace>(Read(Path.Combine(repository, ".claude-plugin", "marketplace.json")));
            Assert.That(marketplace.name, Is.EqualTo(SkillName), "name");
            Assert.That(marketplace.description, Is.Not.Null.And.Not.Empty, "description — a strict validation asks for one");
            Assert.That(marketplace.owner?.name, Is.Not.Null.And.Not.Empty, "owner");
            Assert.That(marketplace.plugins, Has.Length.EqualTo(1), "one plugin");
            Entry entry = marketplace.plugins[0];
            Assert.That(entry.name, Is.EqualTo(SkillName), "the plugin's name");
            Assert.That(entry.description, Is.Not.Null.And.Not.Empty, "the plugin's description");
            Assert.That(entry.source, Does.StartWith("./"), "a source relative to the marketplace");
            Assert.That(Path.GetFullPath(Path.Combine(repository, entry.source)), Is.EqualTo(Path.GetFullPath(SkillsRoot)), "the source is the plugin under the package");
        }

        // the preset table an agent copies GUIDs from: a row for every preset in the package's Presets folder, the GUID its
        // .meta file holds (read here from the file, not from the table's source) and a Use for cell
        [Test]
        public void SK05_PresetTable()
        {
            List<(string key, string body)> tables = DocRegions.Find(Read(AuthoringFile)).Where(r => r.key.StartsWith("presets:", StringComparison.Ordinal)).ToList();
            Assert.That(tables, Has.Count.EqualTo(1), "one preset table");
            List<string[]> rows = tables[0].body.Split('\n').Where(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("|---", StringComparison.Ordinal))
                .Skip(1).Select(DocSources.Cells).ToList();
            string[] metas = Directory.GetFiles(Path.Combine(DocRegions.PackageRoot, "Presets"), "*.asset.meta");
            Assert.That(metas, Has.Length.GreaterThan(0), "the package's presets");
            var problems = new List<string>();
            foreach (string meta in metas)
            {
                string name = Path.GetFileName(meta).Substring(0, Path.GetFileName(meta).Length - ".asset.meta".Length);
                string guid = Regex.Match(File.ReadAllText(meta), @"(?m)^guid: (?<g>[0-9a-f]{32})$").Groups["g"].Value;
                string[] row = rows.FirstOrDefault(r => r[0] == name);
                if (row == null)
                    problems.Add("no row for " + name);
                else if (row[1] != "`" + guid + "`")
                    problems.Add($"{name}: {row[1]} — its .meta file has {guid}");
            }
            problems.AddRange(rows.Where(r => r[r.Length - 1].Length == 0).Select(r => r[0] + " has no Use for"));
            Assert.That(rows, Has.Count.EqualTo(metas.Length), "a row for each preset");
            Assert.That(problems, Is.Empty);
        }

        // the dev project has every optional package: every preset's items load, so the table is made, not kept
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void SK05d_PresetItemsLoadInDevProject()
        {
            string[] guids = AssetDatabase.FindAssets("t:FeedbackPreset", new[] { "Packages/com.xpturn.feedback/Presets" });
            Assert.That(guids, Has.Length.GreaterThan(0), "the package's presets");
            List<string> missing = guids.Select(g => AssetDatabase.LoadAssetAtPath<FeedbackPreset>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(p => p.m_Items.Count == 0 || p.m_Items.Any(i => i == null)).Select(p => p.name).ToList();
            Assert.That(missing, Is.Empty, "presets with an item that did not load");
        }

        // every pointer at the installed package's pages names a page and a section it has, and the skill points at every
        // page — a span that starts like a pointer and is not one is a problem too
        [Test]
        public void SK06_PointersResolve()
        {
            var problems = new List<string>();
            var pages = new HashSet<string>();
            int pointers = 0;
            foreach (string file in Directory.GetFiles(SkillsRoot, "*.md", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                foreach (Match span in Regex.Matches(Read(file), @"(?<!`)`([^`\n]+)`(?!`)"))
                {
                    string s = span.Groups[1].Value;
                    if (!s.StartsWith("Documentation~/", StringComparison.Ordinal) && !s.StartsWith("README.md", StringComparison.Ordinal))
                        continue;
                    string where = Path.GetFileName(file) + " `" + s + "`";
                    Match m = Pointer.Match(s);
                    if (!m.Success)
                    {
                        problems.Add(where + " — not a page or a section");
                        continue;
                    }
                    pointers++;
                    string page = m.Groups["file"].Value;
                    string target = Path.Combine(DocRegions.PackageRoot, page);
                    if (!File.Exists(target))
                    {
                        problems.Add(where + " — no such page in the package");
                        continue;
                    }
                    pages.Add(page);
                    if (m.Groups["section"].Success && !DocsTests.Headings(Read(target)).Contains(m.Groups["section"].Value))
                        problems.Add(where + " — no such section");
                }
            }
            List<string> all = Directory.GetFiles(Path.Combine(DocRegions.PackageRoot, "Documentation~"), "*.md").Select(f => "Documentation~/" + Path.GetFileName(f)).Append("README.md").ToList();
            TestContext.WriteLine($"pointers {pointers} · pages {pages.Count}");
            Assert.That(problems, Is.Empty);
            Assert.That(pages, Is.SupersetOf(all), "the skill points at every page");
        }

        // the front matter in the form the skill keeps it: top-level `key: value` lines and one map of strings under a key
        // with no value — any other YAML form is a problem, so nothing is read that the test cannot check
        static Dictionary<string, object> FrontMatter(string text, List<string> problems)
        {
            var fields = new Dictionary<string, object>(StringComparer.Ordinal);
            if (!text.StartsWith("---\n", StringComparison.Ordinal))
            {
                problems.Add("no front matter");
                return fields;
            }
            int end = text.IndexOf("\n---\n", 3, StringComparison.Ordinal);
            if (end < 0)
            {
                problems.Add("the front matter is not closed");
                return fields;
            }
            Dictionary<string, string> map = null;
            foreach (string line in text.Substring(4, end - 3).Split('\n'))
            {
                if (line.Length == 0)
                    continue;
                Match top = Regex.Match(line, @"^(?<k>[a-z][a-z-]*):(?: (?<v>.+))?$");
                Match inner = Regex.Match(line, @"^  (?<k>[a-z][a-z-]*): (?<v>.+)$");
                if (top.Success)
                {
                    string key = top.Groups["k"].Value;
                    if (fields.ContainsKey(key))
                    {
                        problems.Add("twice: " + key);
                        continue;
                    }
                    map = top.Groups["v"].Success ? null : new Dictionary<string, string>(StringComparer.Ordinal);
                    fields[key] = map ?? (object)Scalar(top.Groups["v"].Value, problems);
                }
                else if (inner.Success && map != null)
                {
                    map[inner.Groups["k"].Value] = Scalar(inner.Groups["v"].Value, problems);
                }
                else
                {
                    problems.Add("not read: " + line);
                }
            }
            return fields;
        }

        // a quoted or a plain value — a plain one that YAML would read as something else is a problem
        static string Scalar(string value, List<string> problems)
        {
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                string inside = value.Substring(1, value.Length - 2);
                if (inside.IndexOf('"') >= 0 || inside.IndexOf('\\') >= 0)
                    problems.Add("an escape in " + value);
                return inside;
            }
            if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
                return value.Substring(1, value.Length - 2).Replace("''", "'");
            if (value.Contains(": ") || value.Contains(" #") || "&*!|>%@`'\"[]{},#?-".IndexOf(value[0]) >= 0)
                problems.Add("a plain value YAML reads otherwise: " + value);
            return value;
        }

        [Serializable]
        sealed class PackageJson
        {
            public string version;
        }

        [Serializable]
        sealed class Plugin
        {
            public string name;
            public string version;
            public string description;
        }

        [Serializable]
        sealed class Owner
        {
            public string name;
        }

        [Serializable]
        sealed class Entry
        {
            public string name;
            public string source;
            public string description;
        }

        [Serializable]
        sealed class Marketplace
        {
            public string name;
            public string description;
            public Owner owner;
            public Entry[] plugins;
        }
    }
}
