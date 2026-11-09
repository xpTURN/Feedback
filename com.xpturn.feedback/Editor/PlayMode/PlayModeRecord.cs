using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // a component's serialized values as path-value entries (JSON — escaped, numbers in the invariant culture). An object
    // reference is its GlobalObjectId only, a managed reference its type name; references, curves, gradients and strings are
    // one entry each — their children (m_FileID · m_PathID, the characters) would write the value again behind its own entry.
    // The apply goes twice: the structure (array sizes · managed reference types) by path, then a walk in the target's order
    // that writes only what differs
    internal static class PlayModeRecord
    {
        static readonly HashSet<string> s_Header = new HashSet<string>
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject", "m_Enabled",
            "m_EditorHideFlags", "m_Script", "m_Name", "m_EditorClassIdentifier", "m_FormatVersion",
        };

        internal const int KInt = 0, KBool = 1, KFloat = 2, KString = 3, KObject = 4, KRef = 5, KCurve = 6, KGradient = 7, KSize = 8, KEnum = 9;

        [Serializable]
        internal sealed class Entry
        {
            public string p;
            public int k;
            public string v;
        }

        [Serializable]
        internal sealed class Doc
        {
            public bool skipItems;
            public List<Entry> e = new List<Entry>();
        }

        [Serializable]
        sealed class Box
        {
            public AnimationCurve Curve;
            public Gradient Gradient;
        }

        internal struct Stats
        {
            public int Written;
            public int Dropped;
            public int NoPath;
            public int FallbackAt;
        }

        static bool Skipped(SerializedProperty it, bool skipItems) => it.depth == 0 && (s_Header.Contains(it.name) || (skipItems && it.name == ItemListEdits.ItemsPath));

        // the kind of a recorded property (−1: none — its children are recorded) and whether its children are entered
        static int Kind(SerializedProperty it, out bool enter)
        {
            enter = true;
            switch (it.propertyType)
            {
                case SerializedPropertyType.ManagedReference: return KRef;
                case SerializedPropertyType.ArraySize: return KSize;
                case SerializedPropertyType.Enum: return KEnum;
                case SerializedPropertyType.Integer: return KInt;
                case SerializedPropertyType.Boolean: return KBool;
                case SerializedPropertyType.Float: return KFloat;
                case SerializedPropertyType.ObjectReference: enter = false; return KObject;
                case SerializedPropertyType.String: enter = false; return KString;
                case SerializedPropertyType.AnimationCurve: enter = false; return KCurve;
                case SerializedPropertyType.Gradient: enter = false; return KGradient;
                default: return -1;
            }
        }

        static string Value(SerializedProperty it, int k)
        {
            switch (k)
            {
                case KRef: return it.managedReferenceFullTypename;
                case KObject: return it.objectReferenceValue == null ? "" : KeepPlayModeChanges.Id(it.objectReferenceValue);
                case KSize:
                case KEnum: return it.intValue.ToString(CultureInfo.InvariantCulture);
                case KInt: return it.longValue.ToString(CultureInfo.InvariantCulture);
                case KBool: return it.boolValue ? "1" : "0";
                // a double keeps its bits (Count To's values)
                case KFloat: return it.numericType == SerializedPropertyNumericType.Double ? it.doubleValue.ToString("R", CultureInfo.InvariantCulture) : it.floatValue.ToString("R", CultureInfo.InvariantCulture);
                case KString: return it.stringValue;
                case KCurve: return JsonUtility.ToJson(new Box { Curve = it.animationCurveValue });
                case KGradient: return JsonUtility.ToJson(new Box { Gradient = it.gradientValue });
            }
            return "";
        }

        // `skipItems`: a linked player's own list is not recorded (its list is the preset's); `unknown`: leaves of a kind the
        // record does not hold
        internal static string Capture(Object target, bool skipItems, out int unknown)
        {
            var doc = new Doc { skipItems = skipItems };
            SerializedProperty it = new SerializedObject(target).GetIterator();
            bool enter = true;
            unknown = 0;
            while (it.Next(enter))
            {
                enter = true;
                if (Skipped(it, skipItems))
                {
                    enter = false;
                    continue;
                }
                int k = Kind(it, out enter);
                if (k < 0)
                {
                    if (!it.hasChildren)
                        unknown++;
                    continue;
                }
                doc.e.Add(new Entry { p = it.propertyPath, k = k, v = Value(it, k) });
            }
            return JsonUtility.ToJson(doc);
        }

        internal static void Apply(Object target, string json, out Stats st)
        {
            st = new Stats { FallbackAt = -1 };
            Doc doc = JsonUtility.FromJson<Doc>(json);
            var so = new SerializedObject(target);
            ByPath(so, doc.e.Where(x => x.k == KRef || x.k == KSize).ToList(), 0, ref st);
            so.Update();
            int from = Walk(so, doc, ref st);
            if (from < doc.e.Count)
                ByPath(so, doc.e, from, ref st);
            if (so.hasModifiedProperties)
                KeepPlayModeChanges.BeginGroup();
            so.ApplyModifiedProperties();
        }

        // the target in the record's order — a different path or structure: the rest by path
        static int Walk(SerializedObject so, Doc doc, ref Stats st)
        {
            SerializedProperty it = so.GetIterator();
            bool enter = true;
            int i = 0;
            while (i < doc.e.Count && it.Next(enter))
            {
                enter = true;
                if (Skipped(it, doc.skipItems))
                {
                    enter = false;
                    continue;
                }
                int k = Kind(it, out enter);
                if (k < 0)
                    continue;
                Entry e = doc.e[i];
                if (it.propertyPath != e.p || k != e.k || ((k == KRef || k == KSize) && Value(it, k) != e.v))
                {
                    st.FallbackAt = i;
                    return i;
                }
                if (k != KRef && k != KSize)
                    Write(it, e, ref st);
                i++;
            }
            return i;
        }

        static void ByPath(SerializedObject so, List<Entry> es, int from, ref Stats st)
        {
            for (int i = from; i < es.Count; i++)
            {
                Entry e = es[i];
                SerializedProperty p = so.FindProperty(e.p);
                if (p == null)
                {
                    st.NoPath++;
                    continue;
                }
                switch (e.k)
                {
                    case KRef:
                        if (p.managedReferenceFullTypename != e.v)
                        {
                            p.managedReferenceValue = string.IsNullOrEmpty(e.v) ? null : Activator.CreateInstance(TypeOf(e.v), true);
                            KeepPlayModeChanges.BeginGroup();
                            so.ApplyModifiedProperties();
                            so.Update();
                            st.Written++;
                        }
                        continue;
                    case KSize:
                        int n = int.Parse(e.v, CultureInfo.InvariantCulture);
                        if (p.intValue != n)
                        {
                            p.intValue = n;
                            KeepPlayModeChanges.BeginGroup();
                            so.ApplyModifiedProperties();
                            so.Update();
                            st.Written++;
                        }
                        continue;
                }
                Write(p, e, ref st);
            }
        }

        static void Write(SerializedProperty p, Entry e, ref Stats st)
        {
            if (e.k == KObject)
            {
                Object o = e.v.Length == 0 ? null : KeepPlayModeChanges.Resolve(e.v) ?? KeepPlayModeChanges.Unfold(e.v);
                if (e.v.Length > 0 && o == null)
                    st.Dropped++;
                if (p.objectReferenceValue == o)
                    return;
                p.objectReferenceValue = o;
                st.Written++;
                return;
            }
            if (Value(p, e.k) == e.v)
                return;
            switch (e.k)
            {
                case KEnum: p.intValue = int.Parse(e.v, CultureInfo.InvariantCulture); break;
                case KInt: p.longValue = long.Parse(e.v, CultureInfo.InvariantCulture); break;
                case KBool: p.boolValue = e.v == "1"; break;
                case KFloat:
                    if (p.numericType == SerializedPropertyNumericType.Double)
                        p.doubleValue = double.Parse(e.v, NumberStyles.Float, CultureInfo.InvariantCulture);
                    else
                        p.floatValue = float.Parse(e.v, NumberStyles.Float, CultureInfo.InvariantCulture);
                    break;
                case KString: p.stringValue = e.v; break;
                case KCurve: p.animationCurveValue = JsonUtility.FromJson<Box>(e.v).Curve; break;
                case KGradient: p.gradientValue = JsonUtility.FromJson<Box>(e.v).Gradient; break;
            }
            st.Written++;
        }

        // `managedReferenceFullTypename` is "<assembly> <full type name>"
        static Type TypeOf(string full)
        {
            int space = full.IndexOf(' ');
            Type t = Type.GetType(full.Substring(space + 1) + ", " + full.Substring(0, space));
            if (t == null)
                throw new InvalidOperationException("The type " + full + " is not loaded");
            return t;
        }
    }
}
