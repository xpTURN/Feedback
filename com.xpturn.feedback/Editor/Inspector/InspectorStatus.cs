using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the player's status line — a newer format, a missing item type and, in play mode, an edit that waits for a Rebuild
    // (with the button) and that the play's changes are kept. Seen again every 0.25 s while in a panel — the edit's report
    // comes late
    internal sealed class InspectorStatus : VisualElement
    {
        internal const string NewerText = "Saved by a newer version of the package — the fields and item types this version does not know are lost.";
        internal const string MissingText = "An item's type is missing — delete and move are locked until the type returns or the data is cleared. A save before the type returns can drop the item.";
        internal const string MissingPresetText = "The linked preset is missing — the player plays its own hidden items.";
        internal const string LinkedText = "Linked to a preset — edit it in the preset's inspector.";
        internal const string EditedText = "Changed after the player prepared — the list and an item's timing, target or motion reach the plays after a Rebuild (curves and gradients show at once).";

        readonly FeedbackPlayer m_Player;
        internal readonly HelpBox Newer = new HelpBox(NewerText, HelpBoxMessageType.Warning);
        internal readonly HelpBox Missing = new HelpBox(MissingText, HelpBoxMessageType.Error);
        internal readonly HelpBox Edited = new HelpBox(EditedText, HelpBoxMessageType.Info);
        internal readonly Button RebuildButton;
        internal readonly HelpBox MissingPreset = new HelpBox(MissingPresetText, HelpBoxMessageType.Warning);
        internal readonly HelpBox Linked = new HelpBox(LinkedText, HelpBoxMessageType.None);
        internal readonly HelpBox Kept = new HelpBox(KeepPlayModeChanges.KeptText, HelpBoxMessageType.Info);

        internal InspectorStatus(FeedbackPlayer player)
        {
            m_Player = player;
            AddToClassList(InspectorStyles.Status);
            Add(Linked);
            Add(MissingPreset);
            Add(Newer);
            Add(Missing);
            RebuildButton = new Button(() =>
            {
                if (m_Player != null)
                    m_Player.Rebuild();
                Refresh();
            }) { text = "Rebuild" };
            Edited.Add(RebuildButton);
            Add(Edited);
            Add(Kept);
            Refresh();
            schedule.Execute(Refresh).Every(250);
        }

        internal void Refresh()
        {
            if (m_Player == null)
                return;
            Show(Linked, m_Player.m_Preset != null);
            Show(MissingPreset, PresetLinks.Missing(m_Player));
            Show(Newer, m_Player.m_FormatVersion > FeedbackFormat.Current || (m_Player.m_Preset != null && m_Player.m_Preset.m_FormatVersion > FeedbackFormat.Current));
            Show(Missing, ItemListEdits.Locked(PresetLinks.Owner(m_Player)));
            Show(Edited, Application.isPlaying && PlayModeEdits.Pending(m_Player));
            Show(Kept, Application.isPlaying && KeepPlayModeChanges.IsMarked(m_Player));
        }

        static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
