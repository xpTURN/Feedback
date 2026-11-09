using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Editor.Docs
{
    // the repository root's README (tools/root-readme.py writes it from the package's README) and its translations — in
    // the repository only; an installed package has no root and these are skipped
    public sealed class RootReadmeTests
    {
        const string Package = "com.xpturn.feedback";
        static readonly Regex Link = new Regex(@"(\[[^\]]*\])\((?<to>[^)\s]+)\)");
        static readonly Regex Region = new Regex(@"<!-- gen:(?<key>[^\n]+?) -->(?<nl>\n?)(?<body>.*?)<!-- /gen:\k<key> -->", RegexOptions.Singleline);
        static readonly (string code, string name)[] Languages = { ("ko-KR", "한국어"), ("ja-JP", "日本語"), ("zh-CN", "简体中文") };

        static string Read(string file) => File.ReadAllText(file).Replace("\r\n", "\n");

        static string Root()
        {
            string root = DocRegions.RepositoryRoot;
            if (root == null)
                Assert.Ignore("an installed package — the repository root's README is in the repository only");
            return root;
        }

        static string LanguageLine(string current)
            => string.Join(" | ", new[] { ("README.md", "English", (string)null) }.Concat(Languages.Select(l => ($"README.{l.code}.md", l.name, l.code)))
                .Select(l => l.Item3 == current ? $"**{l.Item2}**" : $"[{l.Item2}]({l.Item1})"));

        // the package README as the root's: the note, the language line after the title (and badges), the links into the
        // package with its folder in front, the generated regions' markers out
        internal static string Transform(string text, string packageRoot)
        {
            var lines = text.Split('\n').ToList();
            int at = 1;
            while (at < lines.Count && lines[at].StartsWith("[![", StringComparison.Ordinal))
                at++;
            lines.InsertRange(at, new[] { "", LanguageLine(null) });
            string joined = Link.Replace(string.Join("\n", lines), m =>
            {
                string to = m.Groups["to"].Value;
                string path = to.Split('#')[0];
                if (Regex.IsMatch(to, "^(https?:|mailto:|#)") || path.Length == 0 || !(File.Exists(Path.Combine(packageRoot, path)) || Directory.Exists(Path.Combine(packageRoot, path))))
                    return m.Value;
                return m.Groups[1].Value + "(" + Package + "/" + to + ")";
            });
            joined = Regex.Replace(joined, @"^<!-- /?gen:[^>]*-->\n", "", RegexOptions.Multiline);
            joined = Regex.Replace(joined, @"<!-- /?gen:[^>]*-->", "");
            return $"<!-- Generated from {Package}/README.md by tools/root-readme.py — edit that file, not this one -->\n" + joined;
        }

        static IEnumerable<string> Files(string root)
            => new[] { Path.Combine(root, "README.md") }.Concat(Languages.Select(l => Path.Combine(root, $"README.{l.code}.md")));

        [Test]
        public void RR01_RootIsThePackageReadme()
        {
            string root = Root();
            string expected = Transform(Read(Path.Combine(DocRegions.PackageRoot, "README.md")), DocRegions.PackageRoot);
            string file = Path.Combine(root, "README.md");
            Assert.That(File.Exists(file), "the root README — tools/root-readme.py writes it");
            string actual = Read(file);
            if (actual == expected)
                return;
            string[] a = actual.Split('\n'), e = expected.Split('\n');
            int i = 0;
            while (i < Math.Min(a.Length, e.Length) && a[i] == e[i])
                i++;
            Assert.Fail($"the root README differs from the package README's at line {i + 1} — run tools/root-readme.py\n  root:    {(i < a.Length ? a[i] : "(end)")}\n  package: {(i < e.Length ? e[i] : "(end)")}");
        }

        [Test]
        public void RR02_LinksResolve()
        {
            string root = Root();
            var bad = new List<string>();
            int links = 0;
            foreach (string file in Files(root).Where(File.Exists))
            {
                foreach (Match m in Link.Matches(Read(file)))
                {
                    string to = m.Groups["to"].Value;
                    if (Regex.IsMatch(to, "^(https?:|mailto:)"))
                        continue;
                    links++;
                    string[] parts = to.Split('#');
                    string target = parts[0].Length == 0 ? file : Path.GetFullPath(Path.Combine(root, parts[0]));
                    if (!File.Exists(target) && !Directory.Exists(target))
                    {
                        bad.Add($"{Path.GetFileName(file)} ({to}) — no such file from the root");
                        continue;
                    }
                    if (parts.Length > 1 && target.EndsWith(".md", StringComparison.Ordinal) && !DocsTests.Headings(Read(target)).Contains(parts[1]))
                        bad.Add($"{Path.GetFileName(file)} ({to}) — no such heading");
                }
            }
            TestContext.WriteLine($"links {links}");
            Assert.That(links, Is.GreaterThan(10), "links read");
            Assert.That(bad, Is.Empty);
        }

        [Test]
        public void RR03_LanguageLines()
        {
            string root = Root();
            var bad = new List<string>();
            string englishFile = Path.Combine(root, "README.md");
            if (!File.Exists(englishFile))
                bad.Add("README.md — missing (tools/root-readme.py writes it)");
            else if (!Read(englishFile).Split('\n').Contains(LanguageLine(null)))
                bad.Add("README.md — no language line");
            foreach ((string code, string name) in Languages)
            {
                string file = Path.Combine(root, $"README.{code}.md");
                if (!File.Exists(file))
                {
                    bad.Add($"README.{code}.md — missing");
                    continue;
                }
                string[] lines = Read(file).Split('\n');
                if (!lines.Contains(LanguageLine(code)))
                    bad.Add($"README.{code}.md — no language line with {name} in bold");
                if (!lines.Any(l => l.StartsWith("> ", StringComparison.Ordinal) && l.Contains("(README.md)")))
                    bad.Add($"README.{code}.md — no line saying the English README is the reference");
                if (!lines.Any(l => Regex.IsMatch(l, "^<!-- english: [0-9a-f]{12} -->$")))
                    bad.Add($"README.{code}.md — no English fingerprint line");
            }
            Assert.That(bad, Is.Empty);
        }

        [Test]
        public void RR05_TranslationRegionsAreTheEnglishOnes()
        {
            string root = Root();
            Dictionary<string, string> english = Region.Matches(Read(Path.Combine(DocRegions.PackageRoot, "README.md"))).Cast<Match>().ToDictionary(m => m.Groups["key"].Value, m => m.Groups["body"].Value);
            var bad = new List<string>();
            int regions = 0;
            foreach ((string code, _) in Languages)
            {
                string file = Path.Combine(root, $"README.{code}.md");
                if (!File.Exists(file))
                    continue;
                foreach (Match m in Region.Matches(Read(file)))
                {
                    regions++;
                    string key = m.Groups["key"].Value;
                    if (!english.TryGetValue(key, out string body))
                        bad.Add($"README.{code}.md — {key}: not in the package README");
                    else if (body != m.Groups["body"].Value)
                        bad.Add($"README.{code}.md — {key}: not the package README's — run tools/root-readme.py");
                }
            }
            TestContext.WriteLine($"regions {regions}");
            Assert.That(regions, Is.GreaterThan(0), "the translations' regions read");
            Assert.That(bad, Is.Empty);
        }

        // the documents' forbidden words (DX01) — Hangul only in the Korean translation and the language lines
        [Test]
        public void RR09_NoForbiddenWords()
        {
            string root = Root();
            var bad = new List<string>();
            foreach (string file in Files(root).Where(File.Exists))
            {
                string[] lines = Read(file).Split('\n');
                bool korean = file.EndsWith(".ko-KR.md", StringComparison.Ordinal);
                for (int i = 0; i < lines.Length; i++)
                    foreach (string word in DocsTests.Forbidden(lines[i], korean || lines[i].Contains("(README.ko-KR.md)")))
                        bad.Add($"{Path.GetFileName(file)}:{i + 1} '{word}'");
            }
            Assert.That(bad, Is.Empty);
        }

        // the translations show the English README's images, each with its own alt text
        [Test]
        public void RR11_TranslationsShowTheEnglishImages()
        {
            string root = Root();
            List<string> Images(string file) => ImageTests.Image.Matches(Read(file)).Cast<Match>().Select(m => m.Groups["to"].Value).ToList();
            List<string> english = Images(Path.Combine(root, "README.md"));
            var bad = new List<string>();
            foreach ((string code, _) in Languages)
            {
                string file = Path.Combine(root, $"README.{code}.md");
                if (!File.Exists(file))
                    continue;
                List<string> theirs = Images(file);
                if (!theirs.SequenceEqual(english))
                    bad.Add($"README.{code}.md — [{string.Join(", ", theirs)}], the English README [{string.Join(", ", english)}]");
            }
            Assert.That(english, Is.Not.Empty, "the English README's images");
            Assert.That(bad, Is.Empty);
        }

        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void RR10_TheDevProjectFindsTheRepositoryRoot()
            => Assert.That(DocRegions.RepositoryRoot, Is.Not.Null, "the dev project reads the package from the repository — its root must be found");
    }
}
