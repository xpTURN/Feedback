using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // the preset's rules

    // FB-A09: a Direct target in a preset — a preset cannot hold an object for each player
    internal sealed class PresetDirectRule : FeedbackRule
    {
        internal const string Message = "A preset cannot hold an object — use Self, Child, Anchor or Main Camera";

        internal override string Id => "FB-A09";
        internal override RuleSeverity Severity => RuleSeverity.Error;
        internal override bool Applies(Component host) => false;
        internal override bool ListOnly => true;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (!(c.Owner is FeedbackPreset))
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if (!RuleFixes.TargetOf(item, out TargetRef target) || target.m_Mode != TargetRefMode.Direct || TargetChecks.Global(item.GetType()))
                    continue;
                RuleResult r = Result(c, i, Message);
                bool camera = item is CameraShakeItem;
                r.Fixes.Add(c.ItemFix(camera ? "Target the main camera" : "Target Self", c.ItemPath(i, "m_Target"), p =>
                {
                    p.FindPropertyRelative("m_Mode").enumValueIndex = (int)(camera ? TargetRefMode.MainCamera : TargetRefMode.Self);
                    p.FindPropertyRelative("m_Object").objectReferenceValue = null;
                }));
                into.Add(r);
            }
        }
    }

    // FB-B08: the linked preset was deleted — the player plays its own hidden items
    internal sealed class MissingPresetRule : FeedbackRule
    {
        internal const string Message = "The linked preset is missing — the player plays its own hidden items";

        internal override string Id => "FB-B08";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (!PresetLinks.Missing(c.Player))
                return;
            RuleResult r = Result(c, -1, Message);
            Component host = c.Host;
            r.Fixes.Add(new RuleFix("Clear the link", () => RuleFixes.Write(host, "m_Preset", p => p.objectReferenceValue = null)));
            into.Add(r);
        }
    }

    // FB-B11: two of a player's lists with one alias — a key plays the first; two that differ in case alone — a key matches
    // the exact text
    internal sealed class VariantAliasRule : FeedbackRule
    {
        internal override string Id => "FB-B11";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool OnVariants => false;

        // each later list whose alias meets an earlier one's and why — the rule's and the inspector's notice
        internal static List<string> Problems(FeedbackPlayer p)
        {
            var found = new List<string>();
            if (p == null)
                return found;
            // the lists in the order a key looks: the player's own (-1), then the Variants' live slots
            var aliases = new List<(int Slot, string Alias)>();
            string own = p.m_Preset != null ? p.m_Preset.m_Alias : p.m_ListAlias;
            if (!string.IsNullOrEmpty(own))
                aliases.Add((-1, own));
            for (int k = 0; p.m_Variants != null && k < p.m_Variants.Count; k++)
            {
                FeedbackPreset v = p.m_Variants[k];
                if (v != null && !ReferenceEquals(v, p.m_Preset) && p.m_Variants.IndexOf(v) == k && !string.IsNullOrEmpty(v.m_Alias))
                    aliases.Add((k, v.m_Alias));
            }
            for (int i = 1; i < aliases.Count; i++)
            {
                for (int j = 0; j < i; j++)
                {
                    bool same = string.Equals(aliases[i].Alias, aliases[j].Alias, System.StringComparison.Ordinal);
                    if (!same && !string.Equals(aliases[i].Alias, aliases[j].Alias, System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    string first = aliases[j].Slot < 0 ? "the player's list" : "Variants slot " + aliases[j].Slot;
                    found.Add(same
                        ? "Variants slot " + aliases[i].Slot + " has the alias \"" + aliases[i].Alias + "\" of " + first + " — that key plays " + first
                        : "Variants slot " + aliases[i].Slot + "'s alias \"" + aliases[i].Alias + "\" differs from " + first + "'s only in case — a key matches the exact text");
                    break;
                }
            }
            return found;
        }

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            foreach (string message in Problems(c.Player))
                into.Add(Result(c, -1, message));
        }
    }

    // FB-B10: a Variants slot that plays nothing — empty, its preset gone, a repeat of an earlier slot, the linked preset
    internal sealed class VariantSlotRule : FeedbackRule
    {
        internal override string Id => "FB-B10";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool OnVariants => false;

        // each slot that plays nothing and why — the rule's and the inspector's notice
        internal static List<(int Slot, string Message)> Problems(FeedbackPlayer player)
        {
            var found = new List<(int Slot, string Message)>();
            List<FeedbackPreset> variants = player != null ? player.m_Variants : null;
            for (int k = 0; variants != null && k < variants.Count; k++)
            {
                FeedbackPreset preset = variants[k];
                if (ReferenceEquals(preset, null))
                    found.Add((k, "Variants slot " + k + " is empty — it plays nothing"));
                else if (preset == null)
                    found.Add((k, "Variants slot " + k + "'s preset is missing — it plays nothing"));
                else if (ReferenceEquals(preset, player.m_Preset))
                    found.Add((k, "Variants slot " + k + " is the linked preset — it plays as the player's list"));
                else if (variants.IndexOf(preset) != k)
                    found.Add((k, "Variants slot " + k + " repeats slot " + variants.IndexOf(preset) + " — the first one plays"));
            }
            return found;
        }

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (c.Player == null)
                return;
            Component host = c.Host;
            foreach ((int slot, string message) in Problems(c.Player))
            {
                RuleResult r = Result(c, -1, message);
                int at = slot;
                r.Fixes.Add(new RuleFix("Remove slot " + slot, () => RuleFixes.Write(host, "m_Variants", p =>
                {
                    p.GetArrayElementAtIndex(at).objectReferenceValue = null;
                    p.DeleteArrayElementAtIndex(at);
                })));
                into.Add(r);
            }
        }
    }
}
