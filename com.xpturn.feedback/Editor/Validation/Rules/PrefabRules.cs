using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // B — a prefab instance's or a variant asset's overrides

    // FB-B04: the list's structure is overridden (m_Items.Array.data[i] modifications) — a base change can drop or double an
    // item. The base items missing from the list are named (by managed-reference id). Revert in place; apply to the base in
    // place for an instance (one undo), in a copy of the variant asset (saved — not undoable)
    internal sealed class StructureRule : FeedbackRule
    {
        internal override string Id => "FB-B04";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            // the own list structure — dormant while linked
            if (c.Linked)
                return;
            Component host = c.Host;
            bool instance = !c.InPrefabAsset && PrefabUtility.IsPartOfPrefabInstance(host);
            bool variant = c.InPrefabAsset && PrefabUtility.IsPartOfVariantPrefab(host);
            if (!instance && !variant)
                return;
            PropertyModification[] mods = PrefabUtility.GetPropertyModifications(host.gameObject) ?? Array.Empty<PropertyModification>();
            if (!mods.Any(m => m.target is FeedbackPlayer && m.propertyPath.StartsWith("m_Items.Array.data[", StringComparison.Ordinal)))
                return;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(host) as FeedbackPlayer;
            string missing = "";
            if (source != null && c.Player != null)
            {
                var mine = new HashSet<long>(c.Player.m_Items.Where(x => x != null).Select(x => UnityEngine.Serialization.ManagedReferenceUtility.GetManagedReferenceIdForObject(c.Player, x)));
                List<string> lost = source.m_Items.Where(x => x != null && !mine.Contains(UnityEngine.Serialization.ManagedReferenceUtility.GetManagedReferenceIdForObject(source, x)))
                    .Select(x => string.IsNullOrEmpty(x.m_Label) ? ItemWords.Name(x.GetType()) : x.m_Label).ToList();
                if (lost.Count > 0)
                    missing = " — missing base items: " + string.Join(", ", lost);
            }
            RuleResult r = Result(c, -1, "The list's structure is overridden — a base change can drop or double an item" + missing);
            r.Fixes.Add(new RuleFix("Revert the list structure", () => PrefabUtility.RevertPropertyOverride(new SerializedObject(host).FindProperty("m_Items"), InteractionMode.UserAction)));
            if (source != null)
            {
                string basePath = AssetDatabase.GetAssetPath(source);
                if (instance)
                    r.Fixes.Add(new RuleFix("Apply the list structure to the base", () => PrefabUtility.ApplyPropertyOverride(new SerializedObject(host).FindProperty("m_Items"), basePath, InteractionMode.UserAction)));
                else
                {
                    string variantPath = AssetDatabase.GetAssetPath(host);
                    r.Fixes.Add(new RuleFix("Apply the list structure to the base", () =>
                    {
                        GameObject contents = PrefabUtility.LoadPrefabContents(variantPath);
                        try
                        {
                            PrefabUtility.ApplyPropertyOverride(new SerializedObject(contents.GetComponent<FeedbackPlayer>()).FindProperty("m_Items"), basePath, InteractionMode.AutomatedAction);
                            PrefabUtility.SaveAsPrefabAsset(contents, variantPath);
                        }
                        finally
                        {
                            PrefabUtility.UnloadPrefabContents(contents);
                        }
                    }, undoable: false));
                }
            }
            into.Add(r);
        }
    }

    // FB-B05: overrides on paths the type no longer has — a managed-reference path translated by its id (FindProperty does not
    // resolve managedReferences[id]). Removed with the public RemoveUnusedOverrides: on an instance in place, on a
    // variant asset in its copy (saved — not undoable)
    internal sealed class UnusedOverrideRule : FeedbackRule
    {
        internal override string Id => "FB-B05";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            Component host = c.Host;
            bool instance = !c.InPrefabAsset && PrefabUtility.IsPartOfPrefabInstance(host);
            bool variant = c.InPrefabAsset && PrefabUtility.IsPartOfVariantPrefab(host);
            if (!instance && !variant)
                return;
            PropertyModification[] mods = PrefabUtility.GetPropertyModifications(host.gameObject) ?? Array.Empty<PropertyModification>();
            SerializedObject so = c.HostSerialized;
            List<string> unused = mods.Where(m => m.target is FeedbackPlayer && !Resolves(so, m.propertyPath)).Select(m => m.propertyPath).ToList();
            if (unused.Count == 0)
                return;
            RuleResult r = Result(c, -1, "Unity cannot restore these values — revert the package first if they matter. Unused overrides: " + string.Join(", ", unused));
            if (instance)
            {
                GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(host);
                r.Fixes.Add(new RuleFix("Remove unused overrides", () => PrefabUtility.RemoveUnusedOverrides(new[] { root }, InteractionMode.UserAction)));
            }
            else
            {
                string variantPath = AssetDatabase.GetAssetPath(host);
                r.Fixes.Add(new RuleFix("Remove unused overrides", () =>
                {
                    GameObject contents = PrefabUtility.LoadPrefabContents(variantPath);
                    try
                    {
                        PrefabUtility.RemoveUnusedOverrides(new[] { contents }, InteractionMode.AutomatedAction);
                        PrefabUtility.SaveAsPrefabAsset(contents, variantPath);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(contents);
                    }
                }, undoable: false));
            }
            into.Add(r);
        }

        // managedReferences[rid].rest — the reference's property, then rest under it
        internal static bool Resolves(SerializedObject so, string path)
        {
            const string head = "managedReferences[";
            if (!path.StartsWith(head, StringComparison.Ordinal))
                return so.FindProperty(path) != null;
            int close = path.IndexOf(']');
            if (close < 0 || !long.TryParse(path.Substring(head.Length, close - head.Length), out long rid))
                return false;
            string rest = close + 2 <= path.Length ? path.Substring(close + 2) : "";
            SerializedProperty it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType == SerializedPropertyType.ManagedReference && it.managedReferenceId == rid)
                    return rest.Length == 0 || it.FindPropertyRelative(rest) != null;
            }
            return false;
        }
    }
}
