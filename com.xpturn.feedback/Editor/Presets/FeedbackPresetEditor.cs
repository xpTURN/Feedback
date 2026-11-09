using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the preset asset's inspector — the two ways, the strip and the list without a host, Add · Paste, the results, the
    // hierarchy drop's way, and in play the linked players' Rebuild
    [CustomEditor(typeof(FeedbackPreset))]
    [CanEditMultipleObjects]
    internal sealed class FeedbackPresetEditor : UnityEditor.Editor
    {
        internal const string HelpText = "A Feedback Preset holds an item list — copy it into a player, or link players to it (they play this list). A Unity Preset of a FeedbackPlayer holds the whole component, settings too — a copy only, and its scene references are cleared when it is saved.";
        internal const string DropLinkText = "Link on hierarchy drop (all presets)";
        internal const string PlayText = "Edits reach the linked players at their next Rebuild";
        internal const string RebuildText = "Rebuild Them";

        internal HelpBox Banner;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.AddToClassList(InspectorStyles.Root);
            InspectorStyles.Attach(root);
            root.Add(new HelpBox(HelpText, HelpBoxMessageType.None));
            var drop = new Toggle(DropLinkText) { value = PresetDrop.Links };
            drop.RegisterValueChangedCallback(e => PresetDrop.Links = e.newValue);
            root.Add(drop);
            if (targets.Length > 1)
            {
                root.Add(new HelpBox(FeedbackPlayerEditor.OneAtATimeText, HelpBoxMessageType.Info));
                return root;
            }
            var preset = (FeedbackPreset)target;
            Banner = new HelpBox(PlayText, HelpBoxMessageType.Info);
            Banner.Add(new Button(() => PresetOps.RebuildLinked(preset)) { text = RebuildText });
            Banner.style.display = DisplayStyle.None;
            Banner.schedule.Execute(() =>
            {
                int n = Application.isPlaying ? PresetLinks.Linked(preset).Count(p => p.Runtime != null) : 0;
                Banner.text = PlayText + " (" + n + ")";
                Banner.style.display = n > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }).Every(250);
            root.Add(Banner);
            root.Add(new TimelineStrip(serializedObject, ItemListEdits.ItemsPath, null));
            var list = new ItemListView(serializedObject, ItemListEdits.ItemsPath, null, null);
            root.Add(list);
            var bottom = new VisualElement();
            bottom.AddToClassList(InspectorStyles.Bottom);
            Button add = null;
            add = new Button(() => list.OpenAddMenu(add.worldBound)) { text = FeedbackPlayerEditor.AddText };
            var paste = new Button(() => list.Paste()) { text = FeedbackPlayerEditor.PasteText };
            paste.schedule.Execute(() => paste.SetEnabled(ItemClipboard.Count > 0)).Every(250);
            bottom.Add(paste);
            bottom.Add(add);
            root.Add(new ValidationView(preset, serializedObject, list, bottom));
            root.Add(bottom);
            return root;
        }
    }
}
