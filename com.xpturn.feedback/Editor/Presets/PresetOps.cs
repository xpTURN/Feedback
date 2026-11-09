using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // the editor's view of a link — the list's owner, a link to a deleted asset, the loaded players that link a preset
    internal static class PresetLinks
    {
        internal static Object Owner(FeedbackPlayer player) => player != null && player.m_Preset != null ? player.m_Preset : (Object)player;

        // the asset was deleted — the field keeps a fake object (a never linked field is a true null)
        internal static bool Missing(FeedbackPlayer player) => player != null && !ReferenceEquals(player.m_Preset, null) && player.m_Preset == null;

        internal static List<FeedbackPlayer> Linked(FeedbackPreset preset)
        {
            var found = new List<FeedbackPlayer>();
            if (preset == null)
                return found;
            foreach (FeedbackPlayer p in Resources.FindObjectsOfTypeAll<FeedbackPlayer>())
            {
                if (p.m_Preset == preset && !EditorUtility.IsPersistent(p) && p.gameObject.scene.IsValid())
                    found.Add(p);
            }
            return found;
        }
    }

    // copy, save, apply, unlink — deep copies. Into a preset the Direct targets on the player, under it or on its parent
    // become relative and the other scene references are cleared (counted): an asset saves a scene reference as nothing,
    // while the loaded asset keeps it until it is unloaded
    internal static class PresetOps
    {
        internal const string CopyUndo = "Copy Feedback Preset";
        internal const string ApplyUndo = "Apply Items to Preset";
        internal const string UnlinkUndo = "Unlink Feedback Preset";
        internal const string MissingText = "The list has missing item types — they would be lost; restore the types first.";
        internal const string ClearedWarning = "Scene references a preset cannot hold were cleared: ";
        internal const string SameText = "The player is linked to this preset — its items are the preset's already.";

        internal static bool CanCopy(Object owner) => owner != null && !MissingTypes.Has(owner);

        static int Begin(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }

        // a player's items made portable: Direct targets on the player, under it or its parent become Self · Child · Parent
        internal static List<FeedbackItem> Portable(FeedbackPlayer player, IReadOnlyList<FeedbackItem> items)
        {
            List<FeedbackItem> copies = ItemCopier.Copies(items);
            Transform root = player.transform;
            foreach (FeedbackItem item in copies)
            {
                if (item is TargetItem t)
                    Relative(ref t.m_Target, root);
                else if (item is CustomActionItem a)
                    Relative(ref a.m_Target, root);
            }
            return copies;
        }

        static void Relative(ref TargetRef target, Transform root)
        {
            if (target.m_Mode != TargetRefMode.Direct || target.m_Object == null)
                return;
            GameObject go = target.m_Object is GameObject g ? g : target.m_Object is Component c ? c.gameObject : null;
            if (go == null || !go.scene.IsValid())
                return;
            if (go.transform == root)
                target.m_Mode = TargetRefMode.Self;
            else if (go.transform.IsChildOf(root))
            {
                target.m_Mode = TargetRefMode.Child;
                target.m_ChildLookup = ChildLookup.Path;
                target.m_Path = AnimationUtility.CalculateTransformPath(go.transform, root);
            }
            else if (root.parent != null && go.transform == root.parent)
                target.m_Mode = TargetRefMode.Parent;
            else
                return;
            target.m_Object = null;
        }

        // a preset's copies into a list (a player's own · another preset's) at `at` — the place, −1 refused
        internal static int CopyInto(SerializedObject so, string listPath, int at, FeedbackPreset from)
        {
            if (from == null || so.targetObject == from || !CanCopy(from))
                return -1;
            if (so.targetObject is FeedbackPlayer p && p.m_Preset != null)
                return -1;
            List<FeedbackItem> copies = ItemCopier.Copies(from.m_Items);
            return ItemListEdits.Insert(so, listPath, at, copies, CopyUndo, so.targetObject is FeedbackPreset, out _);
        }

        internal static int CopyInto(FeedbackPlayer player, FeedbackPreset from, int at = -1)
            => CopyInto(new SerializedObject(player), ItemListEdits.ItemsPath, at, from);

        // the player's items (the linked preset's when linked) into a new preset asset — not undoable (the asset is made)
        internal static FeedbackPreset SaveAs(FeedbackPlayer player, string path, out int cleared)
        {
            cleared = 0;
            if (!CanCopy(PresetLinks.Owner(player)))
            {
                Debug.LogWarning(MissingText, player);
                return null;
            }
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            AssetDatabase.CreateAsset(preset, path);
            Fill(preset, Portable(player, player.Items), out cleared);
            AssetDatabase.SaveAssets();
            // the asset is made, not an undo step: an undo would empty it
            Undo.ClearUndo(preset);
            if (cleared > 0)
                Debug.LogWarning(ClearedWarning + cleared, preset);
            return preset;
        }

        // the preset's list replaced by the player's items — one undo group on the asset
        internal static bool ApplyTo(FeedbackPlayer player, FeedbackPreset preset, out int cleared)
        {
            cleared = 0;
            if (player.m_Preset == preset)
            {
                Debug.LogWarning(SameText, player);
                return false;
            }
            if (!CanCopy(PresetLinks.Owner(player)) || !CanCopy(preset))
            {
                Debug.LogWarning(MissingText, player);
                return false;
            }
            int group = Begin(ApplyUndo);
            var so = new SerializedObject(preset);
            so.FindProperty(ItemListEdits.ItemsPath).arraySize = 0;
            so.ApplyModifiedProperties();
            Fill(preset, Portable(player, player.Items), out cleared);
            Undo.CollapseUndoOperations(group);
            if (cleared > 0)
                Debug.LogWarning(ClearedWarning + cleared, preset);
            return true;
        }

        static void Fill(FeedbackPreset preset, List<FeedbackItem> items, out int cleared)
            => ItemListEdits.Insert(new SerializedObject(preset), ItemListEdits.ItemsPath, -1, items, CopyUndo, true, out cleared);

        // copy: the own list replaced by the preset's copies (the hidden own list is dropped) — one undo group
        internal static bool Unlink(FeedbackPlayer player, bool copy)
        {
            FeedbackPreset preset = player.m_Preset;
            if (preset == null && !PresetLinks.Missing(player))
                return false;
            if (copy && (preset == null || !CanCopy(preset) || !CanCopy(player)))
            {
                Debug.LogWarning(MissingText, player);
                return false;
            }
            int group = Begin(UnlinkUndo);
            var so = new SerializedObject(player);
            if (copy)
            {
                so.FindProperty(ItemListEdits.ItemsPath).arraySize = 0;
                so.ApplyModifiedProperties();
                ItemListEdits.Insert(so, ItemListEdits.ItemsPath, -1, ItemCopier.Copies(preset.m_Items), UnlinkUndo);
                so.Update();
            }
            so.FindProperty("m_Preset").objectReferenceValue = null;
            so.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(group);
            return true;
        }

        internal static void Link(FeedbackPlayer player, FeedbackPreset preset)
        {
            var so = new SerializedObject(player);
            so.FindProperty("m_Preset").objectReferenceValue = preset;
            so.ApplyModifiedProperties();
        }

        internal const string SaveMenu = "CONTEXT/FeedbackPlayer/Save Items as Preset...";
        internal const string ApplyMenu = "CONTEXT/FeedbackPlayer/Apply Items to Preset...";

        [MenuItem(SaveMenu)]
        static void SaveMenuItem(MenuCommand command) => SaveWithDialog((FeedbackPlayer)command.context);

        [MenuItem(ApplyMenu)]
        static void ApplyMenuItem(MenuCommand command) => ApplyWithDialog((FeedbackPlayer)command.context);

        internal static void SaveWithDialog(FeedbackPlayer player)
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Items as Preset", player.name + " Preset", "asset", "A new Feedback Preset with this player's items");
            if (string.IsNullOrEmpty(path))
                return;
            FeedbackPreset preset = SaveAs(player, path, out _);
            if (preset != null)
                EditorGUIUtility.PingObject(preset);
        }

        internal static void ApplyWithDialog(FeedbackPlayer player)
        {
            string chosen = EditorUtility.OpenFilePanel("Apply Items to Preset", Application.dataPath, "asset");
            if (string.IsNullOrEmpty(chosen))
                return;
            var preset = AssetDatabase.LoadAssetAtPath<FeedbackPreset>(FileUtil.GetProjectRelativePath(chosen));
            if (preset == null)
            {
                Debug.LogWarning("Apply Items to Preset: not a Feedback Preset in the project — " + chosen, player);
                return;
            }
            if (ApplyTo(player, preset, out _))
                EditorGUIUtility.PingObject(preset);
        }

        // play mode: the linked prepared players again
        internal static int RebuildLinked(FeedbackPreset preset)
        {
            int n = 0;
            foreach (FeedbackPlayer p in PresetLinks.Linked(preset))
            {
                if (p.Runtime != null && p.Rebuild())
                    n++;
            }
            return n;
        }
    }
}
