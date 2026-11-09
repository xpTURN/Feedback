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
    // copy has its own. The player's unsaved table by list index is filled from it at each replay — a reorder follows the id
    internal static class PreviewMasks
    {
        const string Prefix = "xpTURN.Feedback.Preview.Mask.";

        // −1 before the first serialization — Set serializes first
        internal static long ReferenceId(FeedbackPlayer player, int listIndex)
        {
            FeedbackItem item = player.Items[listIndex];
            return item == null ? -1 : ManagedReferenceUtility.GetManagedReferenceIdForObject(PresetLinks.Owner(player), item);
        }

        static string Key(FeedbackPlayer player, long id) => Prefix + player.GetEntityId() + "." + id;

        internal static string Key(FeedbackPlayer player, int listIndex) => Key(player, ReferenceId(player, listIndex));

        internal static void Set(FeedbackPlayer player, int listIndex, PreviewMask mask)
        {
            new SerializedObject(PresetLinks.Owner(player)).Update();
            long id = ReferenceId(player, listIndex);
            if (id < 0)
                return;
            if (mask == PreviewMask.None)
                SessionState.EraseInt(Key(player, id));
            else
                SessionState.SetInt(Key(player, id), (int)mask);
            Apply(player);
        }

        internal static PreviewMask Get(FeedbackPlayer player, int listIndex)
        {
            long id = ReferenceId(player, listIndex);
            return id < 0 ? PreviewMask.None : (PreviewMask)SessionState.GetInt(Key(player, id), 0);
        }

        internal static void Apply(FeedbackPlayer player)
        {
            if (player == null)
                return;
            var items = player.Items;
            byte[] table = null;
            for (int i = 0; i < items.Count; i++)
            {
                long id = ReferenceId(player, i);
                int mask = id < 0 ? 0 : SessionState.GetInt(Key(player, id), 0);
                if (mask != (int)PreviewMask.Mute && PrefabChild(player, items[i]))
                    mask = (int)PreviewMask.Mute;
                if (mask == 0)
                    continue;
                table ??= new byte[items.Count];
                table[i] = (byte)mask;
            }
            player.m_PreviewMasks = table;
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

        internal static void Clear(FeedbackPlayer player)
        {
            for (int i = 0; i < player.Items.Count; i++)
            {
                long id = ReferenceId(player, i);
                if (id >= 0)
                    SessionState.EraseInt(Key(player, id));
            }
            player.m_PreviewMasks = null;
        }
    }
}
