using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // the slot types' rows from their attributes — the source of PreviewRecordPaths. Knows and Get read the same table. The
    // owner type's paths go on the key's target as it is (a RectTransform takes Transform's rows); a GameObject owner takes
    // the target's GameObject. {index} opens within the path's array: the key's second aux, −1 for every index
    internal sealed class PreviewRecordPathTable : IPreviewRecordSource
    {
        const string Index = "{index}";
        const string ArrayData = ".Array.data[";

        readonly Dictionary<Type, RecordPathsAttribute> m_Rows = new Dictionary<Type, RecordPathsAttribute>();
        readonly HashSet<Type> m_Skips = new HashSet<Type>();

        internal PreviewRecordPathTable()
        {
            foreach (Type type in TypeCache.GetTypesWithAttribute<RecordPathsAttribute>())
                m_Rows[type] = type.GetCustomAttribute<RecordPathsAttribute>(false);
            foreach (Type type in TypeCache.GetTypesWithAttribute<NotRecordedAttribute>())
                m_Skips.Add(type);
        }

        internal IEnumerable<Type> RowTypes => m_Rows.Keys;

        internal IEnumerable<Type> SkipTypes => m_Skips;

        internal RecordPathsAttribute RowsOf(Type slotType) => m_Rows.TryGetValue(slotType, out RecordPathsAttribute rows) ? rows : null;

        public bool Knows(Type slotType) => m_Rows.ContainsKey(slotType) || m_Skips.Contains(slotType);

        public PreviewRecordResult Get(in LayerKey key, List<PreviewRecordPath> paths)
        {
            if (m_Skips.Contains(key.SlotType))
                return PreviewRecordResult.Skip;
            if (!m_Rows.TryGetValue(key.SlotType, out RecordPathsAttribute rows))
                return PreviewRecordResult.Unknown;
            Object owner = key.Target;
            if (rows.Owner == typeof(GameObject) && owner is Component component)
                owner = component.gameObject;
            Expand(owner, rows.Paths, key.Aux2, paths);
            return PreviewRecordResult.Record;
        }

        // the rows on an owner: a path with {index} once per index of its array that the aux takes
        internal static void Expand(Object owner, string[] rows, int index, List<PreviewRecordPath> paths)
        {
            foreach (string path in rows)
            {
                int at = path.IndexOf(Index, StringComparison.Ordinal);
                if (at < 0)
                {
                    paths.Add(new PreviewRecordPath(owner, path));
                    continue;
                }
                int data = path.LastIndexOf(ArrayData, at, StringComparison.Ordinal);
                SerializedProperty array = data > 0 ? new SerializedObject(owner).FindProperty(path.Substring(0, data)) : null;
                int count = array != null && array.isArray ? array.arraySize : 0;
                for (int i = 0; i < count; i++)
                {
                    if (index < 0 || index == i)
                        paths.Add(new PreviewRecordPath(owner, path.Replace(Index, i.ToString(CultureInfo.InvariantCulture))));
                }
            }
        }
    }

    // the table is the source from the load — a test swaps the source and puts it back
    [InitializeOnLoad]
    internal static class PreviewRecordPathInstall
    {
        static PreviewRecordPathInstall()
        {
            if (PreviewRecordPaths.Source == null)
                PreviewRecordPaths.Source = new PreviewRecordPathTable();
        }
    }
}
