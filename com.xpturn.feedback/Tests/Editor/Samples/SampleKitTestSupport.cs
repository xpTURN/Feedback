using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using xpTURN.Feedback.Samples;
using xpTURN.Feedback.Samples.Editor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace xpTURN.Feedback.Tests.Editor.Samples
{
    // the samples' tests share: asset path → disk (package paths through their resolved folder), the bundles (each catalog's
    // folder) with the kit, the script GUIDs the scene rules look for, and the Console list's counts and rows (internal
    // UnityEditor.LogEntries — native warnings never reach logMessageReceived)
    static class SampleKitTestSupport
    {
        public const string Kit = "Packages/com.xpturn.feedback/SampleKit";
        public const string ActionsPath = Kit + "/UI/SampleActions.inputactions";
        public const string EventSystemPrefab = Kit + "/UI/SampleEventSystem.prefab";
        const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Type s_LogEntries = Type.GetType("UnityEditor.LogEntries, UnityEditor.CoreModule");
        static readonly Type s_LogEntry = Type.GetType("UnityEditor.LogEntry, UnityEditor.CoreModule");

        public static string Disk(string assetPath)
        {
            PackageInfo p = assetPath.StartsWith("Packages/") ? PackageInfo.FindForAssetPath(assetPath) : null;
            return p == null ? Path.GetFullPath(assetPath) : Path.Combine(p.resolvedPath, assetPath.Length > p.assetPath.Length ? assetPath.Substring(p.assetPath.Length + 1) : "");
        }

        public static List<SampleCatalog> Catalogs() => SampleBuild.Catalogs();

        public static string BundleRoot(SampleCatalog catalog) => Path.GetDirectoryName(AssetDatabase.GetAssetPath(catalog)).Replace('\\', '/');

        public static IEnumerable<string> Roots() => Catalogs().Select(c => Disk(BundleRoot(c))).Append(Disk(Kit)).Distinct();

        public static IEnumerable<string> Files(string pattern) => Roots().Where(Directory.Exists).SelectMany(r => Directory.GetFiles(r, pattern, SearchOption.AllDirectories));

        public static string ScriptGuid(Type type)
            => type == null ? null : AssetDatabase.FindAssets("t:MonoScript " + type.Name).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => AssetDatabase.LoadAssetAtPath<MonoScript>(p)?.GetClass() == type).Select(AssetDatabase.AssetPathToGUID).FirstOrDefault();

        public static int Count(string text, string needle)
        {
            int n = 0;
            for (int i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
                n++;
            return n;
        }

        public static (int errors, int warnings) Counts()
        {
            MethodInfo m = s_LogEntries?.GetMethod("GetCountsByType", All);
            if (m == null)
                return (-1, -1);
            var a = new object[] { 0, 0, 0 };
            m.Invoke(null, a);
            return ((int)a[0], (int)a[1]);
        }

        public static int Rows() => s_LogEntries == null ? 0 : (int)s_LogEntries.GetMethod("GetCount", All).Invoke(null, null);

        // the Console list's rows from 'from' on — what appeared, for a failure's message
        public static List<string> RowsFrom(int from)
        {
            var list = new List<string>();
            if (s_LogEntries == null || s_LogEntry == null)
                return list;
            int count = (int)s_LogEntries.GetMethod("StartGettingEntries", All).Invoke(null, null);
            try
            {
                object e = Activator.CreateInstance(s_LogEntry, true);
                MethodInfo get = s_LogEntries.GetMethod("GetEntryInternal", All);
                FieldInfo msg = s_LogEntry.GetField("message", All);
                for (int i = Math.Max(0, from); i < count; i++)
                {
                    get.Invoke(null, new[] { (object)i, e });
                    list.Add(((string)msg.GetValue(e) ?? "").Split('\n')[0]);
                }
            }
            finally
            {
                s_LogEntries.GetMethod("EndGettingEntries", All).Invoke(null, null);
            }
            return list;
        }
    }
}
