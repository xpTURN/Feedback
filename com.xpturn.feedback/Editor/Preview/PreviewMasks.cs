using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace xpTURN.Feedback.Editor
{
    internal enum PreviewMask
    {
        None = 0,
        Mute = 1,
        Solo = 2,
    }

    // the preview's masks — SessionState, keyed by (the player's entity id, the item's managed reference id): not saved, a
    // copy has its own. A Variant's list keys by its preset too — a copied preset has the same ids. The player's unsaved table
    // by list index is the shown list's, filled at each replay — a reorder follows the id
    internal static class PreviewMasks
    {
        const string Prefix = "xpTURN.Feedback.Preview.Mask.";

        // the list a preset picks of a player and that list's owner — the player's (its own or the linked preset's) for none or
        // the linked preset, else the preset's: a Variant
        internal static Object ListOwner(FeedbackPlayer player, FeedbackPreset preset)
            => preset == null || ReferenceEquals(preset, player.m_Preset) ? PresetLinks.Owner(player) : preset;

        internal static bool IsVariant(FeedbackPlayer player, Object owner) => owner is FeedbackPreset && !ReferenceEquals(owner, PresetLinks.Owner(player));

        internal static List<FeedbackItem> ItemsOf(FeedbackPlayer player, Object owner) => IsVariant(player, owner) ? ((FeedbackPreset)owner).m_Items : player.Items;

        // −1 before the first serialization — Set serializes first
        internal static long ReferenceId(FeedbackPlayer player, int listIndex) => ReferenceId(player, PresetLinks.Owner(player), listIndex);

        internal static long ReferenceId(FeedbackPlayer player, Object owner, int listIndex)
        {
            FeedbackItem item = ItemsOf(player, owner)[listIndex];
            return item == null ? -1 : ManagedReferenceUtility.GetManagedReferenceIdForObject(IsVariant(player, owner) ? owner : PresetLinks.Owner(player), item);
        }

        static string Key(FeedbackPlayer player, Object owner, long id)
            => IsVariant(player, owner) ? Prefix + player.GetEntityId() + "." + owner.GetEntityId() + "." + id : Prefix + player.GetEntityId() + "." + id;

        internal static string Key(FeedbackPlayer player, int listIndex) => Key(player, PresetLinks.Owner(player), ReferenceId(player, listIndex));

        internal static void Set(FeedbackPlayer player, int listIndex, PreviewMask mask) => Set(player, PresetLinks.Owner(player), listIndex, mask);

        internal static void Set(FeedbackPlayer player, Object owner, int listIndex, PreviewMask mask)
        {
            new SerializedObject(IsVariant(player, owner) ? owner : PresetLinks.Owner(player)).Update();
            long id = ReferenceId(player, owner, listIndex);
            if (id < 0)
                return;
            if (mask == PreviewMask.None)
                SessionState.EraseInt(Key(player, owner, id));
            else
                SessionState.SetInt(Key(player, owner, id), (int)mask);
            Apply(player, owner);
        }

        internal static PreviewMask Get(FeedbackPlayer player, int listIndex) => Get(player, PresetLinks.Owner(player), listIndex);

        internal static PreviewMask Get(FeedbackPlayer player, Object owner, int listIndex)
        {
            long id = ReferenceId(player, owner, listIndex);
            return id < 0 ? PreviewMask.None : (PreviewMask)SessionState.GetInt(Key(player, owner, id), 0);
        }

        internal static void Apply(FeedbackPlayer player) => Apply(player, PresetLinks.Owner(player));

        // the table of the list the next play shows, and that list's owner — the runtimes of the other lists read no mask
        internal static void Apply(FeedbackPlayer player, Object owner)
        {
            if (player == null)
                return;
            List<FeedbackItem> items = ItemsOf(player, owner);
            byte[] table = null;
            for (int i = 0; i < items.Count; i++)
            {
                long id = ReferenceId(player, owner, i);
                int mask = id < 0 ? 0 : SessionState.GetInt(Key(player, owner, id), 0);
                if (mask != (int)PreviewMask.Mute && PrefabChild(player, items[i]))
                    mask = (int)PreviewMask.Mute;
                if (mask == 0)
                    continue;
                table ??= new byte[items.Count];
                table[i] = (byte)mask;
            }
            player.m_PreviewMasks = table;
            player.m_PreviewMaskOwner = IsVariant(player, owner) ? owner : null;
        }

        // edit mode cannot move a prefab instance's own child — the engine logs an error and leaves it, play moves it. Each
        // replay leaves such a Set Parent out — not saved, the user's masks untouched (PreviewNotices tells)
        internal static bool PrefabChild(FeedbackPlayer player, FeedbackItem item)
        {
            if (!(item is SetParentItem set) || !set.m_Active)
                return false;
            var context = new PrepareContext(player.gameObject, player, null, 1);
            return IsPrefabChild(TargetResolver.Find<Transform>(in set.m_Target, in context, out _));
        }

        // part of an instance, not its outermost root, not an object added to it — FB-G13's too
        internal static bool IsPrefabChild(Transform target) => target != null && PrefabUtility.IsPartOfPrefabInstance(target)
            && !PrefabUtility.IsOutermostPrefabInstanceRoot(target.gameObject) && !PrefabUtility.IsAddedGameObjectOverride(target.gameObject);

        // every list's — the player's and its Variants'
        internal static void Clear(FeedbackPlayer player)
        {
            Clear(player, PresetLinks.Owner(player));
            for (int k = 0; player.m_Variants != null && k < player.m_Variants.Count; k++)
            {
                if (VariantShow.Usable(player, k))
                    Clear(player, player.m_Variants[k]);
            }
            player.m_PreviewMasks = null;
            player.m_PreviewMaskOwner = null;
        }

        static void Clear(FeedbackPlayer player, Object owner)
        {
            List<FeedbackItem> items = ItemsOf(player, owner);
            for (int i = 0; i < items.Count; i++)
            {
                long id = ReferenceId(player, owner, i);
                if (id >= 0)
                    SessionState.EraseInt(Key(player, owner, id));
            }
        }
    }
}
