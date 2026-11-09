using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace xpTURN.Feedback.Editor
{
    // one entry of the add menu — a target item × a motion it supports, or an action · flow item
    internal readonly struct CatalogLeaf
    {
        internal const int NoMotion = -1;

        internal readonly Type Type;
        internal readonly int Motion;
        internal readonly string Path;
        internal readonly string Aliases;
        internal readonly bool Registered;

        internal CatalogLeaf(Type type, int motion, string path, string aliases, bool registered)
        {
            Type = type;
            Motion = motion;
            Path = path;
            Aliases = aliases ?? "";
            Registered = registered;
        }

        // a user's feedback — the package's counts leave it out
        internal bool Custom => FeedbackCatalog.IsCustom(Type);

        // the saved id of a favorite · a recent leaf
        internal string Id => Type.FullName + (Motion != NoMotion ? "/" + (MotionKind)Motion : "");

        internal string Name
        {
            get
            {
                int slash = Path.LastIndexOf('/');
                return slash < 0 ? Path : Path.Substring(slash + 1);
            }
        }
    }

    // the add menu's leaves from the [FeedbackTarget] types, the search's score, the favorites and the recent ones (per user).
    // An effect type without a registered implementation is dimmed — a flow item never
    internal static class FeedbackCatalog
    {
        internal const string FavoritesKey = "xpTURN.Feedback.Inspector.Favorites";
        internal const string RecentKey = "xpTURN.Feedback.Inspector.Recent";
        internal const int RecentMax = 10;
        const char Separator = ';';

        static List<CatalogLeaf> s_All;

        internal static IReadOnlyList<CatalogLeaf> All => s_All ?? (s_All = Build(Sources()));

        internal static List<(Type type, bool registered)> Sources()
        {
            var registered = new HashSet<Type>(FeedbackImplementations.RegisteredTypes);
            var sources = new List<(Type, bool)>();
            foreach (Type t in TypeCache.GetTypesWithAttribute<FeedbackTargetAttribute>())
            {
                if (t.IsAbstract || !typeof(FeedbackItem).IsAssignableFrom(t))
                    continue;
                sources.Add((t, !typeof(EffectItem).IsAssignableFrom(t) || registered.Contains(t)));
            }
            // a user's feedbacks: the package's adapters are their bindings
            foreach (Type t in TypeCache.GetTypesDerivedFrom<CustomActionItem>().Concat(TypeCache.GetTypesDerivedFrom<CustomEffectItem>()))
            {
                if (!t.IsAbstract && !t.ContainsGenericParameters)
                    sources.Add((t, FeedbackImplementations.IsImplemented(t)));
            }
            return sources;
        }

        // a user's feedback (the two extension bases)
        internal static bool IsCustom(Type t) => t != null && (typeof(CustomActionItem).IsAssignableFrom(t) || typeof(CustomEffectItem).IsAssignableFrom(t));

        // the group of a user's feedback without [CustomFeedback] — Custom is the package's
        internal const string ProjectGroup = "Project";

        // after the leaf of a user's feedback whose assembly builds do not have (FB-B09)
        internal const string NotInBuilds = " (not in builds)";

        // the menu path and the search words: [FeedbackTarget], else a user's [CustomFeedback] or Project/<its name>
        internal static bool Meta(Type t, out string path, out string aliases)
        {
            var a = (FeedbackTargetAttribute)Attribute.GetCustomAttribute(t, typeof(FeedbackTargetAttribute), false);
            if (a != null)
            {
                path = a.Path;
                aliases = a.Aliases;
                return true;
            }
            if (IsCustom(t))
            {
                var c = (CustomFeedbackAttribute)Attribute.GetCustomAttribute(t, typeof(CustomFeedbackAttribute), false);
                path = c != null && !string.IsNullOrEmpty(c.Path) ? c.Path : ProjectGroup + "/" + ObjectNames.NicifyVariableName(t.Name);
                aliases = c?.Aliases ?? "";
                return true;
            }
            path = null;
            aliases = null;
            return false;
        }

        // a target item's leaves are its path + each motion it supports; the others' leaf is the path. A user's leaf on a path
        // another type's leaf holds (the package's or another user's) gets its type's name after it — the package's keep theirs;
        // one whose assembly builds do not have gets NotInBuilds. In path order
        internal static List<CatalogLeaf> Build(IEnumerable<(Type type, bool registered)> sources)
        {
            var leaves = new List<CatalogLeaf>();
            var custom = new List<CatalogLeaf>();
            foreach ((Type t, bool registered) in sources)
            {
                if (!Meta(t, out string path, out string aliases))
                    continue;
                List<CatalogLeaf> into = IsCustom(t) ? custom : leaves;
                if (typeof(TargetItem).IsAssignableFrom(t))
                {
                    TargetTraits traits = ((TargetItem)Activator.CreateInstance(t, true)).Traits;
                    foreach (MotionKind k in Enum.GetValues(typeof(MotionKind)))
                    {
                        if (traits.Supports(k))
                            into.Add(new CatalogLeaf(t, (int)k, path + "/" + k, aliases, registered));
                    }
                }
                else
                {
                    into.Add(new CatalogLeaf(t, CatalogLeaf.NoMotion, path, aliases, registered));
                }
            }
            var taken = new HashSet<string>(leaves.Select(l => l.Path));
            Dictionary<string, int> owners = custom.GroupBy(l => l.Path).ToDictionary(g => g.Key, g => g.Select(l => l.Type).Distinct().Count());
            foreach (CatalogLeaf leaf in custom)
            {
                bool shared = taken.Contains(leaf.Path) || owners[leaf.Path] > 1;
                string path = (shared ? leaf.Path + " (" + leaf.Type.Name + ")" : leaf.Path) + (PlayerAssemblies.Lacks(leaf.Type) ? NotInBuilds : "");
                leaves.Add(path != leaf.Path ? new CatalogLeaf(leaf.Type, leaf.Motion, path, leaf.Aliases, leaf.Registered) : leaf);
            }
            leaves.Sort((x, y) => string.CompareOrdinal(x.Path, y.Path));
            return leaves;
        }

        // 4 = the leaf's name starts with the words · 3 = another segment does · 2 = the path holds them · 1 = an alias starts
        // with them · −1 = no match. Lower case, spaces ignored; an empty query matches all
        internal static int Score(in CatalogLeaf leaf, string query)
        {
            string q = Squeeze(query);
            if (q.Length == 0)
                return 3;
            string[] segments = leaf.Path.Split('/');
            if (Squeeze(segments[segments.Length - 1]).StartsWith(q, StringComparison.Ordinal))
                return 4;
            foreach (string segment in segments)
            {
                if (Squeeze(segment).StartsWith(q, StringComparison.Ordinal))
                    return 3;
            }
            if (Squeeze(leaf.Path).Contains(q))
                return 2;
            foreach (string alias in leaf.Aliases.Split(' '))
            {
                if (alias.Length > 0 && alias.ToLowerInvariant().StartsWith(q, StringComparison.Ordinal))
                    return 1;
            }
            return -1;
        }

        static string Squeeze(string s) => (s ?? "").Replace(" ", "").ToLowerInvariant();

        // the matches, the higher score first — a tie keeps the path order
        internal static List<CatalogLeaf> Search(IReadOnlyList<CatalogLeaf> leaves, string query)
        {
            var hits = new List<(CatalogLeaf leaf, int score, int order)>();
            for (int i = 0; i < leaves.Count; i++)
            {
                int score = Score(leaves[i], query);
                if (score >= 0)
                    hits.Add((leaves[i], score, i));
            }
            hits.Sort((x, y) => x.score != y.score ? y.score.CompareTo(x.score) : x.order.CompareTo(y.order));
            var result = new List<CatalogLeaf>(hits.Count);
            foreach (var hit in hits)
                result.Add(hit.leaf);
            return result;
        }

        // the leaves a key lists, in its order — an id whose type is gone is skipped
        internal static List<CatalogLeaf> Listed(IReadOnlyList<CatalogLeaf> leaves, string key)
        {
            var result = new List<CatalogLeaf>();
            foreach (string id in EditorPrefs.GetString(key, "").Split(Separator))
            {
                if (id.Length == 0)
                    continue;
                for (int i = 0; i < leaves.Count; i++)
                {
                    if (leaves[i].Id == id)
                    {
                        result.Add(leaves[i]);
                        break;
                    }
                }
            }
            return result;
        }

        // the chosen leaf goes first in the recent ones — ten at most
        internal static void Used(in CatalogLeaf leaf)
        {
            List<string> ids = Ids(RecentKey);
            string id = leaf.Id;
            ids.Remove(id);
            ids.Insert(0, id);
            if (ids.Count > RecentMax)
                ids.RemoveRange(RecentMax, ids.Count - RecentMax);
            EditorPrefs.SetString(RecentKey, string.Join(Separator.ToString(), ids));
        }

        internal static bool IsFavorite(in CatalogLeaf leaf) => Ids(FavoritesKey).Contains(leaf.Id);

        // a new favorite goes last
        internal static void Favorite(in CatalogLeaf leaf, bool on)
        {
            List<string> ids = Ids(FavoritesKey);
            string id = leaf.Id;
            ids.Remove(id);
            if (on)
                ids.Add(id);
            EditorPrefs.SetString(FavoritesKey, string.Join(Separator.ToString(), ids));
        }

        static List<string> Ids(string key)
        {
            var ids = new List<string>();
            foreach (string id in EditorPrefs.GetString(key, "").Split(Separator))
            {
                if (id.Length > 0 && !ids.Contains(id))
                    ids.Add(id);
            }
            return ids;
        }
    }
}
