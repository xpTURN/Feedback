using System;
using System.Collections.Generic;
using UnityEditor;

namespace xpTURN.Feedback.Editor
{
    // the list a player's inspector shows — Base (the player's own, or its linked preset's) or one of its Variants. SessionState
    // per player: not saved, a copy has its own. A slot that plays nothing (empty, gone, repeated, the linked preset) is never
    // shown — Base instead
    internal static class VariantShow
    {
        const string Prefix = "xpTURN.Feedback.Inspector.Show.";

        // every Set — an open inspector of that player builds its list again
        internal static event Action<FeedbackPlayer> Changed;

        static string Key(FeedbackPlayer player) => Prefix + player.GetEntityId();

        // a slot that prepares a runtime — the player's rule (FeedbackPlayer.PrepareVariants)
        internal static bool Usable(FeedbackPlayer player, int slot)
        {
            List<FeedbackPreset> variants = player.m_Variants;
            if (variants == null || slot < 0 || slot >= variants.Count)
                return false;
            FeedbackPreset preset = variants[slot];
            return preset != null && !ReferenceEquals(preset, player.m_Preset) && variants.IndexOf(preset) == slot;
        }

        internal static List<FeedbackPreset> Usables(FeedbackPlayer player)
        {
            var found = new List<FeedbackPreset>();
            for (int k = 0; player != null && player.m_Variants != null && k < player.m_Variants.Count; k++)
            {
                if (Usable(player, k))
                    found.Add(player.m_Variants[k]);
            }
            return found;
        }

        internal static bool Uses(FeedbackPlayer player, FeedbackPreset preset) => preset != null && Usables(player).Contains(preset);

        static string Id(FeedbackPreset preset) => preset.GetEntityId().ToString();

        // the shown Variant — null for Base
        internal static FeedbackPreset Get(FeedbackPlayer player)
        {
            if (player == null)
                return null;
            string id = SessionState.GetString(Key(player), "");
            if (id.Length == 0)
                return null;
            foreach (FeedbackPreset preset in Usables(player))
            {
                if (Id(preset) == id)
                    return preset;
            }
            return null;
        }

        // another list ends the player's own preview (its values back) — the next ▶ plays the new one. Every Set is announced:
        // a validation row's pick of the list shown already still opens its item
        internal static void Set(FeedbackPlayer player, FeedbackPreset preset)
        {
            if (player == null)
                return;
            FeedbackPreset before = Get(player);
            if (preset != null && Uses(player, preset))
                SessionState.SetString(Key(player), Id(preset));
            else
                SessionState.EraseString(Key(player));
            PreviewSession session = PreviewSession.Current;
            if (!ReferenceEquals(before, Get(player)) && session != null && !session.IsTrack && session.Player == player)
                session.Stop();
            Changed?.Invoke(player);
        }
    }
}
