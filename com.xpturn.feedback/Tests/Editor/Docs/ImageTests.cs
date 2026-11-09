using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor.Docs
{
    // the documents' images (Documentation~/images — tools/doc-images.sh takes them): each used, described, small enough,
    // outside the generated regions and on the shot list. That an image still shows the editor as it is now is the tool's
    // --check, not these tests
    public sealed class ImageTests
    {
        const int Floor = 15;
        const long PngLimit = 150 * 1024;
        const long GifLimit = 1024 * 1024;
        const long TotalLimit = 3 * 1024 * 1024;
        internal static readonly Regex Image = new Regex(@"!\[(?<alt>[^\]]*)\]\((?<to>[^)\s]+)\)");
        static readonly Regex FileName = new Regex(@"^[a-z0-9]+(-[a-z0-9]+)*\.(png|gif)$");

        static string Read(string file) => File.ReadAllText(file).Replace("\r\n", "\n");

        static string Folder => Path.Combine(DocRegions.PackageRoot, "Documentation~", "images");

        static string Short(string file) => file.Substring(DocRegions.PackageRoot.Length + 1);

        static List<string> Files() => Directory.Exists(Folder) ? Directory.GetFiles(Folder).Where(f => !Path.GetFileName(f).StartsWith(".", StringComparison.Ordinal)).OrderBy(f => f, StringComparer.Ordinal).ToList() : new List<string>();

        // every image line of the documents: the document, its line, the alt text and the file it points at
        static List<(string doc, int line, string alt, string file)> Uses()
        {
            var uses = new List<(string, int, string, string)>();
            foreach (string doc in DocRegions.Documents())
            {
                string[] lines = Read(doc).Split('\n');
                for (int i = 0; i < lines.Length; i++)
                    foreach (Match m in Image.Matches(lines[i]))
                        uses.Add((doc, i + 1, m.Groups["alt"].Value, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(doc), m.Groups["to"].Value))));
            }
            return uses;
        }

        [Test]
        public void IM01_EveryImageIsUsed()
        {
            List<string> files = Files();
            var used = new HashSet<string>(Uses().Select(u => u.file));
            TestContext.WriteLine($"images {files.Count} · used {used.Count}");
            Assert.That(files.Count, Is.GreaterThanOrEqualTo(Floor), "images in Documentation~/images (tools/doc-images.sh)");
            Assert.That(files.Where(f => !used.Contains(f)).Select(Path.GetFileName), Is.Empty, "images no document shows");
            Assert.That(used.Where(f => Path.GetDirectoryName(f) != Folder).Select(f => f.Substring(DocRegions.PackageRoot.Length + 1)), Is.Empty, "images outside Documentation~/images");
        }

        [Test]
        public void IM02_EveryImageIsDescribed()
        {
            var bad = new List<string>();
            List<(string doc, int line, string alt, string file)> uses = Uses();
            foreach ((string doc, int line, string alt, _) in uses)
            {
                if (alt.Trim().Length == 0)
                    bad.Add($"{Short(doc)}:{line} — no alt text");
                foreach (string word in DocsTests.Forbidden(alt, false))
                    bad.Add($"{Short(doc)}:{line} '{word}'");
            }
            Assert.That(uses.Count, Is.GreaterThanOrEqualTo(Floor), "image lines read");
            Assert.That(bad, Is.Empty);
        }

        [Test]
        public void IM03_ImagesAreSmall()
        {
            List<string> files = Files();
            var bad = new List<string>();
            foreach (string f in files)
            {
                long size = new FileInfo(f).Length;
                string name = Path.GetFileName(f);
                if (!FileName.IsMatch(name))
                    bad.Add($"{name} — a name of lowercase words and hyphens, .png or .gif");
                else if (size > (name.EndsWith(".gif", StringComparison.Ordinal) ? GifLimit : PngLimit))
                    bad.Add($"{name} — {size / 1024} KB");
            }
            long total = files.Sum(f => new FileInfo(f).Length);
            TestContext.WriteLine($"images {files.Count} · {total / 1024} KB");
            Assert.That(files.Count, Is.GreaterThanOrEqualTo(Floor), "images read");
            Assert.That(bad, Is.Empty, $"PNG up to {PngLimit / 1024} KB, GIF up to {GifLimit / 1024} KB");
            Assert.That(total, Is.LessThanOrEqualTo(TotalLimit), "all the images together");
        }

        [Test]
        public void IM04_NoImageInARegion()
        {
            var bad = new List<string>();
            foreach (string doc in DocRegions.Documents())
                foreach ((string key, string body) in DocRegions.Find(Read(doc)))
                    if (Image.IsMatch(body))
                        bad.Add($"{Short(doc)} {key} — a region is written again from its source and loses the image");
            Assert.That(bad, Is.Empty);
        }

        [Serializable]
        sealed class ShotList
        {
            public DocImages.Shot[] shots;
        }

        [Test]
        public void IM05_EveryImageIsOnTheShotList()
        {
            string root = DocRegions.RepositoryRoot;
            if (root == null)
                Assert.Ignore("an installed package — the shot list is in the repository only");
            string path = Path.Combine(root, "tools", "doc-images", "shots.json");
            Assert.That(File.Exists(path), "tools/doc-images/shots.json");
            DocImages.Shot[] shots = JsonUtility.FromJson<ShotList>(File.ReadAllText(path)).shots;
            var bad = new List<string>();
            foreach (DocImages.Shot s in shots)
            {
                if (!FileName.IsMatch(s.file ?? ""))
                    bad.Add($"shot {s.id}: '{s.file}' — a name of lowercase words and hyphens, .png or .gif");
                foreach (string word in DocsTests.Forbidden($"{s.id} {s.file} {s.state}", false))
                    bad.Add($"shot {s.id} '{word}'");
            }
            foreach (string twice in shots.GroupBy(s => s.file).Where(g => g.Count() > 1).Select(g => g.Key))
                bad.Add($"'{twice}' twice on the shot list");
            var listed = new HashSet<string>(shots.Select(s => s.file));
            bad.AddRange(Uses().Select(u => Path.GetFileName(u.file)).Distinct().Where(f => !listed.Contains(f)).Select(f => f + " — not on the shot list: tools/doc-images.sh does not take it"));
            Assert.That(shots.Length, Is.GreaterThanOrEqualTo(Floor), "shots read");
            Assert.That(bad, Is.Empty);
        }
    }
}
