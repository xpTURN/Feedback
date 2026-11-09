using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // what a slot keeps by its marker — the editor names the kind, not an optional assembly's slot type (its table keys on
    // Core's slot types)
    internal static class PreviewKeeps
    {
        static readonly Dictionary<Type, PreviewKeepsAttribute> s_Marks = new Dictionary<Type, PreviewKeepsAttribute>();

        internal static bool Of(Type slotType, out PreviewKeepsAttribute mark)
        {
            if (!s_Marks.TryGetValue(slotType, out mark))
            {
                mark = slotType.GetCustomAttribute<PreviewKeepsAttribute>(false);
                s_Marks[slotType] = mark;
            }
            return mark != null;
        }

        internal static void Make(in LayerKey key, PreviewKeepsAttribute mark)
        {
            switch (mark.Kind)
            {
                case PreviewKeep.SerializedPath:
                    if (!(key.Target is Object owner) || owner == null)
                        return;
                    for (int i = 0; i < PreviewSaveState.Count; i++)
                    {
                        if (PreviewSaveState.At(i) is SerializedPathState s && s.Target == owner && s.Path == mark.Path)
                            return;
                    }
                    var state = SerializedPathState.Take(owner, mark.Path);
                    if (state != null)
                        PreviewSaveState.Add(state);
                    return;
                case PreviewKeep.CanvasRendererColor:
                    if (!(key.Target is CanvasRenderer renderer) || renderer == null)
                        return;
                    for (int i = 0; i < PreviewSaveState.Count; i++)
                    {
                        if (PreviewSaveState.At(i) is CanvasRendererColorState s && s.Target == renderer)
                            return;
                    }
                    PreviewSaveState.Add(new CanvasRendererColorState(renderer));
                    return;
                case PreviewKeep.RuntimeProperties:
                    if (!(key.Target is Component component) || component == null || mark.Path == null)
                        return;
                    foreach (string name in mark.Path.Split(' '))
                    {
                        bool kept = false;
                        for (int i = 0; i < PreviewSaveState.Count && !kept; i++)
                            kept = PreviewSaveState.At(i) is RuntimePropertyState s && s.Target == component && s.Name == name;
                        if (!kept && RuntimePropertyState.Take(component, name) is RuntimePropertyState taken)
                            PreviewSaveState.Add(taken);
                    }
                    return;
            }
        }
    }

    // a serialized path's value the animation can't bind (a string · a Text's font data) — taken right before the slot's first
    // write. A save goes back to it and on again; the end goes back; SessionState holds it for a reload whose hook was missed
    internal sealed class SerializedPathState : IPreviewSaveState
    {
        readonly Object m_Target;
        readonly string m_Path;
        readonly string m_Before;
        readonly Scene m_Scene;
        string m_Kept;

        SerializedPathState(Object target, string path, string before, Scene scene)
        {
            m_Target = target;
            m_Path = path;
            m_Before = before;
            m_Scene = scene;
            PreviewKeptValues.Write("path", target, path, before);
        }

        internal static SerializedPathState Take(Object target, string path)
        {
            if (!Get(target, path, out string before))
                return null;
            Scene scene = target is Component c ? c.gameObject.scene : target is GameObject g ? g.scene : default;
            return new SerializedPathState(target, path, before, scene);
        }

        internal Object Target => m_Target;

        internal string Path => m_Path;

        internal string Before => m_Before;

        public Scene Scene => m_Scene;

        internal static bool Get(Object target, string path, out string value)
        {
            value = null;
            SerializedProperty p = new SerializedObject(target).FindProperty(path);
            if (p == null)
                return false;
            switch (p.propertyType)
            {
                case SerializedPropertyType.String: value = p.stringValue; return true;
                case SerializedPropertyType.Integer: value = p.longValue.ToString(CultureInfo.InvariantCulture); return true;
                case SerializedPropertyType.Float: value = p.floatValue.ToString("R", CultureInfo.InvariantCulture); return true;
                case SerializedPropertyType.Boolean: value = p.boolValue ? "1" : "0"; return true;
                default: return false;
            }
        }

        internal static void Set(Object target, string path, string value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(path);
            if (p == null)
                return;
            switch (p.propertyType)
            {
                case SerializedPropertyType.String: p.stringValue = value; break;
                case SerializedPropertyType.Integer: p.longValue = long.Parse(value, CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Float: p.floatValue = float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Boolean: p.boolValue = value == "1"; break;
                default: return;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public void Revert()
        {
            if (m_Target == null || !Get(m_Target, m_Path, out m_Kept))
                return;
            Set(m_Target, m_Path, m_Before);
        }

        public void Reapply()
        {
            if (m_Target != null && m_Kept != null)
                Set(m_Target, m_Path, m_Kept);
        }
    }

    // a CanvasRenderer's color — runtime state (not serialized — a save leaves it; the end and a missed reload put it back)
    internal sealed class CanvasRendererColorState : IPreviewSaveState
    {
        readonly CanvasRenderer m_Target;
        readonly Color m_Before;
        readonly Scene m_Scene;
        Color m_Kept;

        internal CanvasRendererColorState(CanvasRenderer target)
        {
            m_Target = target;
            m_Before = target.GetColor();
            m_Scene = target.gameObject.scene;
            PreviewKeptValues.Write("crcolor", target, "", PreviewKeptValues.Colour(m_Before));
        }

        internal CanvasRenderer Target => m_Target;

        public Scene Scene => m_Scene;

        public void Revert()
        {
            if (m_Target == null)
                return;
            m_Kept = m_Target.GetColor();
            m_Target.SetColor(m_Before);
        }

        public void Reapply()
        {
            if (m_Target != null)
                m_Target.SetColor(m_Kept);
        }
    }

    // a component's public property that is not serialized (TMP's maxVisible*): the end puts it back; a save leaves it
    // (nothing saved) and a reload gives the component's own default — no record for the recovery
    internal sealed class RuntimePropertyState : IPreviewSaveState
    {
        readonly Component m_Target;
        readonly PropertyInfo m_Property;
        readonly object m_Before;
        readonly Scene m_Scene;
        object m_Kept;

        RuntimePropertyState(Component target, PropertyInfo property)
        {
            m_Target = target;
            m_Property = property;
            m_Before = property.GetValue(target);
            m_Scene = target.gameObject.scene;
        }

        internal static RuntimePropertyState Take(Component target, string name)
        {
            PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return p != null && p.CanRead && p.CanWrite ? new RuntimePropertyState(target, p) : null;
        }

        internal Component Target => m_Target;

        internal string Name => m_Property.Name;

        public Scene Scene => m_Scene;

        public void Revert()
        {
            if (m_Target == null)
                return;
            m_Kept = m_Property.GetValue(m_Target);
            m_Property.SetValue(m_Target, m_Before);
        }

        public void Reapply()
        {
            if (m_Target != null && m_Kept != null)
                m_Property.SetValue(m_Target, m_Kept);
        }
    }

    // the kept values' records — one line each: the kind, the object's GlobalObjectId, the path, the value before
    internal static class PreviewKeptValues
    {
        internal const string Key = "xpTURN.Feedback.Preview.Kept";

        static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

        static float P(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        internal static string Colour(Color c) => string.Join(";", F(c.r), F(c.g), F(c.b), F(c.a));

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n");

        static string Unescape(string s)
        {
            var b = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    b.Append(n == 't' ? '\t' : n == 'n' ? '\n' : n);
                }
                else
                    b.Append(s[i]);
            }
            return b.ToString();
        }

        internal static void Write(string kind, Object target, string path, string value)
        {
            string line = kind + "\t" + GlobalObjectId.GetGlobalObjectIdSlow(target).ToString() + "\t" + path + "\t" + Escape(value);
            string all = SessionState.GetString(Key, "");
            SessionState.SetString(Key, all.Length == 0 ? line : all + "\n" + line);
        }

        // the end, after the states went back
        internal static void Ended() => SessionState.EraseString(Key);

        // the recovery: an object found gets its value back; one not found (its scene not loaded) waits for a later load
        internal static void Recover()
        {
            string all = SessionState.GetString(Key, "");
            if (all.Length == 0)
                return;
            var left = new StringBuilder();
            foreach (string line in all.Split('\n'))
            {
                string[] parts = line.Split('\t');
                if (parts.Length != 4 || !GlobalObjectId.TryParse(parts[1], out GlobalObjectId id))
                    continue;
                Object target = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                if (target == null)
                {
                    if (left.Length > 0)
                        left.Append('\n');
                    left.Append(line);
                    continue;
                }
                string value = Unescape(parts[3]);
                if (parts[0] == "path")
                    SerializedPathState.Set(target, parts[2], value);
                else if (parts[0] == "crcolor" && target is CanvasRenderer renderer)
                {
                    string[] c = value.Split(';');
                    if (c.Length == 4)
                        renderer.SetColor(new Color(P(c[0]), P(c[1]), P(c[2]), P(c[3])));
                }
            }
            if (left.Length == 0)
                SessionState.EraseString(Key);
            else
                SessionState.SetString(Key, left.ToString());
        }
    }
}
