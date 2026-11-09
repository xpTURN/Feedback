using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // the fixtures of an optional module's items — the test assembly can't name the module's types (the minimal manifest), so
    // a conditional test assembly adds them at its load (the editor's and the runtime's registration — the same keys again on
    // each play entry). The module leaf gets its item's module's components, the item its fields
    internal static class ModuleFixtures
    {
        static readonly Dictionary<string, Action<GameObject>> s_Leaves = new Dictionary<string, Action<GameObject>>();
        static readonly Dictionary<Type, Action<TargetItem>> s_Items = new Dictionary<Type, Action<TargetItem>>();
        static readonly Dictionary<Type, string> s_Modules = new Dictionary<Type, string>();
        // an item whose first runs allocate in the editor only (the reason named) — the editor's rows leave it out, the player's
        // measure it
        static readonly Dictionary<Type, string> s_EditorAllocates = new Dictionary<Type, string>();

        internal static void Add(string module, Action<GameObject> leaf, Type[] items, Action<TargetItem> fields)
        {
            s_Leaves[module] = leaf;
            foreach (Type t in items)
            {
                s_Items[t] = fields;
                s_Modules[t] = module;
            }
        }

        internal static bool Has(Type item) => s_Items.ContainsKey(item);

        internal static void EditorAllocates(Type item, string reason) => s_EditorAllocates[item] = reason;

        internal static bool SkipInEditor(Type item) => Application.isEditor && s_EditorAllocates.ContainsKey(item);

        // the item's module's components only — a Rigidbody and a Rigidbody2D can't share a leaf
        internal static void Leaf(GameObject leaf, Type item)
        {
            if (s_Modules.TryGetValue(item, out string module))
                s_Leaves[module](leaf);
        }

        internal static void Fields(TargetItem item)
        {
            if (s_Items.TryGetValue(item.GetType(), out Action<TargetItem> fields))
                fields?.Invoke(item);
        }
    }
}
