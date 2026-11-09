using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the objects' serialized state — every property through SerializedObject (Next(true), the hidden ones too) as (path,
    // value) with a float by its bits and a reference by the object itself. EditorJsonUtility writes a scene reference as 0
    // and cannot see one break
    internal sealed class SerializedSnapshot
    {
        readonly struct Entry
        {
            internal readonly Object Owner;
            internal readonly string Path;
            internal readonly string Value;
            internal readonly Object Reference;

            internal Entry(Object owner, string path, string value, Object reference)
            {
                Owner = owner;
                Path = path;
                Value = value;
                Reference = reference;
            }
        }

        readonly List<Entry> m_Entries = new List<Entry>();

        internal static SerializedSnapshot Take(params Object[] objects)
        {
            var snapshot = new SerializedSnapshot();
            foreach (Object o in objects)
            {
                SerializedProperty it = new SerializedObject(o).GetIterator();
                while (it.Next(true))
                {
                    string value = ValueOf(it, out Object reference);
                    snapshot.m_Entries.Add(new Entry(o, it.propertyPath, value, reference));
                }
            }
            return snapshot;
        }

        // the properties that differ — empty when the state is the same
        internal List<string> Differences(SerializedSnapshot after)
        {
            var lines = new List<string>();
            if (after.m_Entries.Count != m_Entries.Count)
            {
                lines.Add($"{m_Entries.Count} properties before, {after.m_Entries.Count} after");
                return lines;
            }
            for (int i = 0; i < m_Entries.Count; i++)
            {
                Entry a = m_Entries[i];
                Entry b = after.m_Entries[i];
                if (!ReferenceEquals(a.Owner, b.Owner) || a.Path != b.Path)
                {
                    lines.Add($"the layout differs at {a.Path} / {b.Path}");
                    return lines;
                }
                if (a.Value != b.Value || a.Reference != b.Reference)
                    lines.Add($"{a.Owner.GetType().Name} {a.Owner.name}: {a.Path} {a.Value} → {b.Value}");
            }
            return lines;
        }

        // the paths of the properties that differ — a path may hold spaces (a camera's 'field of view'), so it comes from the
        // entries, not from a difference's line. A layout that differs gives its line
        internal List<string> ChangedPaths(SerializedSnapshot after)
        {
            if (after.m_Entries.Count != m_Entries.Count)
                return Differences(after);
            var paths = new List<string>();
            for (int i = 0; i < m_Entries.Count; i++)
            {
                Entry a = m_Entries[i];
                Entry b = after.m_Entries[i];
                if (!ReferenceEquals(a.Owner, b.Owner) || a.Path != b.Path)
                    return Differences(after);
                if (a.Value != b.Value || a.Reference != b.Reference)
                    paths.Add(a.Path);
            }
            return paths;
        }

        static string ValueOf(SerializedProperty p, out Object reference)
        {
            reference = null;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float:
                    return p.type == "double"
                        ? BitConverter.DoubleToInt64Bits(p.doubleValue).ToString("X16", CultureInfo.InvariantCulture)
                        : BitConverter.SingleToInt32Bits(p.floatValue).ToString("X8", CultureInfo.InvariantCulture);
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Character:
                    return p.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:
                    return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Enum:
                    return p.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.String:
                    return p.stringValue;
                case SerializedPropertyType.ObjectReference:
                    reference = p.objectReferenceValue;
                    return reference == null ? "null" : reference.GetType().Name + " " + reference.name;
                case SerializedPropertyType.ManagedReference:
                    return p.managedReferenceFullTypename + " #" + p.managedReferenceId.ToString(CultureInfo.InvariantCulture);
                default:
                    // a composite — its fields come next
                    return p.propertyType.ToString();
            }
        }
    }

    // dirty is a scene's flag — the given scenes' and an open prefab stage's. A setter write in edit mode leaves the scene
    // clean and raises EditorUtility.GetDirtyCount only, not even consistently under a record; an edit applied with Undo
    // marks its scene two updates later
    internal static class DirtyCheck
    {
        internal static List<string> Dirty(params Scene[] scenes)
        {
            var dirty = new List<string>();
            foreach (Scene s in scenes)
            {
                if (s.IsValid() && s.isDirty)
                    dirty.Add(s.path);
            }
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.scene.isDirty)
                dirty.Add(stage.assetPath);
            return dirty;
        }
    }

    // a user's edit as the editor makes it, each in an undo group of its own (the batch editor puts what one update does in
    // one group). The inspector applies a SerializedObject; the Scene view's gizmo records the object, sets it and flushes —
    // the modification event comes at the flush
    internal static class UserEdit
    {
        internal static void Inspector(Object target, string path, Action<SerializedProperty> set)
        {
            Undo.IncrementCurrentGroup();
            var serialized = new SerializedObject(target);
            set(serialized.FindProperty(path));
            serialized.ApplyModifiedProperties();
            Undo.IncrementCurrentGroup();
        }

        internal static void Gizmo(Object target, Action set)
        {
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(target, "Move");
            set();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
        }
    }

    // a file as it was. A scene saved for its baseline and saved again during the preview is the same file. A prefab's
    // baseline is taken after its stage opened and an auto save ran — opening a stage can change the file by itself (URP adds
    // UniversalAdditionalLightData to a light), and the stage saves itself on wall time
    internal sealed class SceneFileCheck
    {
        // a stage's auto save comes about every 0.2 s
        internal const double AutoSaveWait = 0.6;

        readonly string m_Path;
        readonly string m_Baseline;

        SceneFileCheck(string path)
        {
            m_Path = path;
            m_Baseline = File.ReadAllText(path);
        }

        // saved first
        internal static SceneFileCheck ForScene(Scene scene)
        {
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            return new SceneFileCheck(scene.path);
        }

        // the file as it is now — after the stage opened and WaitForAutoSave
        internal static SceneFileCheck ForPrefab(string assetPath) => new SceneFileCheck(assetPath);

        internal string Text => File.ReadAllText(m_Path);

        // the lines that differ from the baseline (a few) — empty when the file is the same
        internal List<string> Differences()
        {
            string[] before = m_Baseline.Split('\n');
            string[] after = Text.Split('\n');
            var lines = new List<string>();
            int count = Math.Max(before.Length, after.Length);
            for (int i = 0; i < count && lines.Count < 8; i++)
            {
                string a = i < before.Length ? before[i].Trim() : "(none)";
                string b = i < after.Length ? after[i].Trim() : "(none)";
                if (a != b)
                    lines.Add($"line {i + 1}: {a} → {b}");
            }
            return lines;
        }

        // the scene saved now, then compared
        internal List<string> SaveAndCompare(Scene scene)
        {
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            return Differences();
        }

        // the stage's own auto-save switch is internal (the project's is EditorSettings.prefabModeAllowAutoSave): set by
        // reflection, the old value returned to put back
        internal static bool AutoSave(PrefabStage stage, bool on)
        {
            PropertyInfo property = typeof(PrefabStage).GetProperty("autoSave", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, "PrefabStage.autoSave");
            bool old = (bool)property.GetValue(stage);
            property.SetValue(stage, on);
            return old;
        }

        // the updates go on until the seconds pass
        internal static IEnumerator WaitForAutoSave(double seconds = AutoSaveWait)
        {
            double until = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < until)
                yield return null;
        }
    }

    // a user's edit before the preview, then the preview and its end — one PerformUndo takes that edit back, and the
    // session's start, ticks and end moved no undo group
    internal readonly struct UndoCheck
    {
        readonly int m_Group;

        UndoCheck(int group) => m_Group = group;

        // the user's edit, as the inspector makes it
        internal static UndoCheck Edit(Object target, string path, Action<SerializedProperty> set)
        {
            UserEdit.Inspector(target, path, set);
            return new UndoCheck(Undo.GetCurrentGroup());
        }

        internal bool GroupKept => Undo.GetCurrentGroup() == m_Group;
    }

    // what a session can leave that no save sees — the AnimationMode lock, our drivers, our hidden temporaries (the
    // recovery's prefix) and the project settings (ProjectSettingsCheck). The registered global states, as their names and
    // values when taken — the test's own baseline, apart from the session's capture. Left() needs nothing taken first: it
    // reads the same after a reload
    internal readonly struct GlobalStateCheck
    {
        readonly ProjectSettingsCheck.State m_Settings;
        readonly List<string> m_Registered;

        GlobalStateCheck(ProjectSettingsCheck.State settings, List<string> registered)
        {
            m_Settings = settings;
            m_Registered = registered;
        }

        internal static GlobalStateCheck Take() => new GlobalStateCheck(ProjectSettingsCheck.Take(), Registered());

        static List<string> Registered()
        {
            var registered = new List<string>();
            foreach (IPreviewGlobalState state in PreviewGlobalState.Registered)
                registered.Add(state.Name + " = " + state.Describe());
            return registered;
        }

        // the project settings first — ProjectSettingsCheck clears a new dirty flag before it fails
        internal void AssertSame()
        {
            ProjectSettingsCheck.FailIfChanged(m_Settings);
            List<string> now = Registered();
            List<string> taken = m_Registered ?? new List<string>();
            var changed = new List<string>();
            int count = Math.Max(now.Count, taken.Count);
            for (int i = 0; i < count; i++)
            {
                string before = i < taken.Count ? taken[i] : "(none)";
                string after = i < now.Count ? now[i] : "(none)";
                if (before != after)
                    changed.Add(before + " → " + after);
            }
            Assert.That(changed, Is.Empty, "registered global states: " + string.Join(" · ", changed));
            List<string> left = Left();
            Assert.That(left, Is.Empty, string.Join(" · ", left));
        }

        internal static List<string> Left()
        {
            var left = new List<string>();
            if (AnimationMode.InAnimationMode())
                left.Add("AnimationMode is held");
            int drivers = Drivers();
            if (drivers != 0)
                left.Add(drivers + " preview driver(s)");
            int temporaries = Temporaries();
            if (temporaries != 0)
                left.Add(temporaries + " hidden temporaries with the preview's prefix");
            return left;
        }

        internal static int Drivers()
        {
            int count = 0;
            foreach (AnimationModeDriver driver in Resources.FindObjectsOfTypeAll<AnimationModeDriver>())
            {
                if (driver != null && driver.name == PreviewDriver.Name)
                    count++;
            }
            return count;
        }

        internal static int Temporaries()
        {
            int count = 0;
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go != null && go.hideFlags == HideFlags.HideAndDontSave && go.name.StartsWith(PreviewRecovery.TemporaryPrefix, StringComparison.Ordinal))
                    count++;
            }
            return count;
        }
    }
}
