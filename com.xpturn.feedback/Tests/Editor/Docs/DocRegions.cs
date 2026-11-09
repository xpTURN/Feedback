using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.PackageManager;

namespace xpTURN.Feedback.Tests.Editor.Docs
{
    // a generated span in a document: <!-- gen:key --> … <!-- /gen:key --> — the body is rewritten from its source.
    // A block region starts its body on the next line; an inline one (a count in a sentence) does not
    internal static class DocRegions
    {
        static readonly Regex Region = new Regex(@"<!-- gen:(?<key>[^\n]+?) -->(?<nl>\n?)(?<body>.*?)<!-- /gen:\k<key> -->", RegexOptions.Singleline);

        internal static string PackageRoot => PackageInfo.FindForAssetPath("Packages/com.xpturn.feedback").resolvedPath;

        // the repository's root — the package's parent when the repository's docs writer is beside the package; none for
        // an installed package (a project's Packages folder, the package cache of a git URL or a tarball)
        internal static string RepositoryRoot
        {
            get
            {
                string parent = Path.GetDirectoryName(PackageRoot);
                return File.Exists(Path.Combine(parent, "tools", "write-docs.sh")) ? parent : null;
            }
        }

        // the repository root's README and its translations — tools/root-readme.py writes them
        internal static readonly string[] Translations = { "ko-KR", "ja-JP", "zh-CN" };

        // README.md, every .md under Documentation~ and every .md of the agent skill under Skills~
        internal static List<string> Documents()
        {
            string root = PackageRoot;
            var files = new List<string> { Path.Combine(root, "README.md") };
            files.AddRange(Directory.GetFiles(Path.Combine(root, "Documentation~"), "*.md", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal));
            string skills = Path.Combine(root, "Skills~");
            if (Directory.Exists(skills))
                files.AddRange(Directory.GetFiles(skills, "*.md", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal));
            return files;
        }

        internal static List<(string key, string body)> Find(string text)
            => Region.Matches(text).Cast<Match>().Select(m => (m.Groups["key"].Value, m.Groups["body"].Value)).ToList();

        // every region's body from produce(key, body); a null answer keeps the body
        internal static string Rewrite(string text, Func<string, string, string> produce)
            => Region.Replace(text, m =>
            {
                string key = m.Groups["key"].Value;
                string body = produce(key, m.Groups["body"].Value) ?? m.Groups["body"].Value;
                return "<!-- gen:" + key + " -->" + m.Groups["nl"].Value + body + "<!-- /gen:" + key + " -->";
            });
    }
}
