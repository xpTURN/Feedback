using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // after each domain reload: a user's feedback type of the last reload that is still there but can no longer be read —
    // abstract, or no longer a feedback. Unity reads such a slot as empty (no missing type, no lock) and the next save drops
    // it. The names live in the project's Library
    [InitializeOnLoad]
    internal static class CustomTypeWatch
    {
        internal const string NamesPath = "Library/xpTURN.Feedback/CustomFeedbackTypes.txt";
        internal const string LostText = "A custom feedback can no longer be read: {0}. The scenes, prefabs and presets that hold it lose it on their next save — make it a concrete feedback again first (rename one with [MovedFrom]).";

        static CustomTypeWatch()
        {
            string[] previous = File.Exists(NamesPath) ? File.ReadAllLines(NamesPath) : Array.Empty<string>();
            List<string> lost = Unreadable(previous, Resolve);
            if (lost.Count > 0)
                Debug.LogError(string.Format(LostText, string.Join("; ", lost)));
            Directory.CreateDirectory(Path.GetDirectoryName(NamesPath));
            File.WriteAllLines(NamesPath, Current());
        }

        // the user's feedback types a list can hold, by assembly-qualified name
        internal static List<string> Current()
            => TypeCache.GetTypesDerivedFrom<FeedbackItem>().Where(t => !t.IsAbstract && !t.ContainsGenericParameters && FeedbackCatalog.IsCustom(t))
                .Select(t => t.FullName + ", " + t.Assembly.GetName().Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

        internal static Type Resolve(string name) => Type.GetType(name, false);

        // the names still there but abstract or no longer a feedback, with why — a name gone is a missing type (FB-B01)
        internal static List<string> Unreadable(IEnumerable<string> previous, Func<string, Type> resolve)
        {
            var lost = new List<string>();
            foreach (string name in previous)
            {
                Type t = resolve(name);
                if (t == null)
                    continue;
                if (!typeof(FeedbackItem).IsAssignableFrom(t))
                    lost.Add(t.FullName + " is no longer a feedback");
                else if (t.IsAbstract)
                    lost.Add(t.FullName + " is abstract");
            }
            return lost;
        }
    }
}
