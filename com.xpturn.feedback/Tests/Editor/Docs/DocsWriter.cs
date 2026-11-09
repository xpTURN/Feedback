using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor.Docs
{
    // rewrites the generated regions of documents from their sources — the text outside a region is left byte for byte
    public static class DocsWriter
    {
        // the files whose text would change — written when write is set
        internal static List<string> Rewrite(IEnumerable<string> files, bool write)
        {
            var changed = new List<string>();
            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                string next = DocRegions.Rewrite(text, DocSources.Produce);
                if (next == text)
                    continue;
                changed.Add(file);
                if (write)
                    File.WriteAllText(file, next);
            }
            return changed;
        }

        // -executeMethod xpTURN.Feedback.Tests.Editor.Docs.DocsWriter.Write
        public static void Write()
        {
            int code = 0;
            try
            {
                List<string> changed = Rewrite(DocRegions.Documents(), true);
                Debug.Log($"DOCS wrote {changed.Count}: {string.Join(", ", changed.ConvertAll(Path.GetFileName))}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }
    }
}
