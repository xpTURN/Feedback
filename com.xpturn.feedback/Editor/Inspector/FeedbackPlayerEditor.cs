using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // ① the status line ② the preview bar ③ the settings and the intensity ④ the total and the strip ⑤ the item list ⑥ the
    // bottom bar — Add · Paste. Several players: the settings and a note — a managed-reference list differs per player. The
    // keep row after the status line (first for several players)
    [CustomEditor(typeof(FeedbackPlayer))]
    [CanEditMultipleObjects]
    internal sealed class FeedbackPlayerEditor : UnityEditor.Editor
    {
        internal const string OneAtATimeText = "The item list is edited one player at a time.";
        internal const string AddText = "Add Feedback";
        internal const string PasteText = "Paste";

        internal VisualElement Section;
        internal ItemListView List;
        internal ValidationView Validation;
        internal int Builds;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.AddToClassList(InspectorStyles.Root);
            InspectorStyles.Attach(root);
            var player = (FeedbackPlayer)target;
            bool one = targets.Length == 1;
            PreviewBar bar = null;
            if (one)
            {
                root.Add(new InspectorStatus(player));
                root.Add(KeepPlayModeChanges.Row(targets));
                bar = new PreviewBar(player);
                root.Add(bar);
            }
            else
                root.Add(KeepPlayModeChanges.Row(targets));
            root.Add(new PlayerSettingsView(serializedObject));
            if (one)
                root.Add(new IntensityView(player));
            if (!one)
            {
                // several players: the link field for all of them, the list one player at a time
                root.Add(new PropertyField(serializedObject.FindProperty("m_Preset"), "Preset"));
                var note = new HelpBox(OneAtATimeText, HelpBoxMessageType.Info);
                note.AddToClassList(InspectorStyles.OneAtATime);
                root.Add(note);
                return root;
            }
            // the link field, then the list's section — built again when the link changes
            var section = new VisualElement();
            root.Add(section);
            SerializedProperty link = serializedObject.FindProperty("m_Preset");
            void Build()
            {
                section.Clear();
                Section = section;
                var field = new ObjectField("Preset") { objectType = typeof(FeedbackPreset), allowSceneObjects = false, tooltip = "Link a Feedback Preset — this player plays its list. A Unity Preset holds the whole component instead (a copy)." };
                field.BindProperty(link);
                var row = new VisualElement();
                row.AddToClassList(InspectorStyles.Row);
                field.AddToClassList(InspectorStyles.Grow);
                row.Add(field);
                Button more = null;
                more = new Button(() =>
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Save Items as Preset..."), false, () => PresetOps.SaveWithDialog(player));
                    menu.AddItem(new GUIContent("Apply Items to Preset..."), false, () => PresetOps.ApplyWithDialog(player));
                    menu.DropDown(more.worldBound);
                }) { iconImage = Background.FromTexture2D(EditorGUIUtility.IconContent(InspectorStyles.MenuArrowIcon).image as Texture2D), tooltip = "Save this player's items as a preset, or write them over a preset" };
                row.Add(more);
                section.Add(row);
                serializedObject.Update();
                FeedbackPreset preset = player.m_Preset;
                SerializedObject listSo = preset != null ? new SerializedObject(preset) : serializedObject;
                bool linked = preset != null;
                section.Add(new TimelineStrip(listSo, ItemListEdits.ItemsPath, player, linked, serializedObject));
                var list = new ItemListView(listSo, ItemListEdits.ItemsPath, player, bar.PlayItem, linked);
                List = list;
                section.Add(list);
                var bottom = new VisualElement();
                bottom.AddToClassList(InspectorStyles.Bottom);
                if (linked)
                {
                    bottom.Add(new Button(() => { Selection.activeObject = preset; EditorGUIUtility.PingObject(preset); }) { text = "Open Preset" });
                    bottom.Add(new Button(() => PresetOps.Unlink(player, false)) { text = "Unlink" });
                    bottom.Add(new Button(() => PresetOps.Unlink(player, true)) { text = "Unlink and Copy" });
                }
                else
                {
                    Button add = null;
                    add = new Button(() => list.OpenAddMenu(add.worldBound)) { text = AddText };
                    add.AddToClassList(InspectorStyles.AddButton);
                    var paste = new Button(() => list.Paste()) { text = PasteText, tooltip = "The copied items after the selected one — references into another scene are cleared" };
                    paste.AddToClassList(InspectorStyles.PasteButton);
                    paste.schedule.Execute(() => paste.SetEnabled(ItemClipboard.Count > 0)).Every(250);
                    bottom.Add(paste);
                    bottom.Add(add);
                }
                Validation = new ValidationView(player, serializedObject, list, bottom);
                section.Add(Validation);
                section.Add(bottom);
                Builds++;
            }
            Build();
            section.TrackPropertyValue(link, _ => Build());
            return root;
        }
    }
}
