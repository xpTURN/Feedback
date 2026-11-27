using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // ① the status line ② the preview bar ③ the settings and the intensity ④ the link, the Variants and Show ⑤ the shown list's alias, the total and
    // the strip ⑥ the item list ⑦ the bottom bar — Add · Paste ⑧ the Graph Preview. ⑤ ~ ⑧ are the shown list's: a Variant's read-only. Several
    // players: the settings and a note — a managed-reference list differs per player. The keep row after the status line (first
    // for several players)
    [CustomEditor(typeof(FeedbackPlayer))]
    [CanEditMultipleObjects]
    internal sealed class FeedbackPlayerEditor : UnityEditor.Editor
    {
        internal const string OneAtATimeText = "The item list is edited one player at a time.";
        internal const string AddText = "Add Feedback";
        internal const string PasteText = "Paste";
        internal const string BaseText = "Base";
        internal const string VariantsOpenKey = "xpTURN.Feedback.Inspector.VariantsOpen.";
        internal const string VariantsTooltip = "Presets this player plays besides its list — a play picks one (PlayArgs.WithPreset, a Timeline clip's Preset). They play on this player's anchors and settings.";
        internal const string ShowTooltip = "The list the inspector shows and the preview plays — Base is the player's own list (or its linked preset's)";
        internal const string AliasTooltip = "The alias a Variant key picks this list by — FeedbackPlayer.VariantKey, PlayArgs.WithVariant, a director's FeedbackTimelineOptions";
        internal const string PresetAliasTooltip = "This preset's Alias — edit it in the preset's inspector";

        internal VisualElement Section;
        internal ItemListView List;
        internal SlotGraphField SlotGraph;
        internal ValidationView Validation;
        internal int Builds;
        internal ListView Variants;
        internal DropdownField Show;
        internal HelpBox VariantNotice;
        internal TextField ListAlias;

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
            // the head — the link field, the Variants and Show — built once; then the shown list's section, built again when the
            // link, Show or the shown slot changes
            SerializedProperty link = serializedObject.FindProperty("m_Preset");
            SerializedProperty variants = serializedObject.FindProperty("m_Variants");
            var head = new VisualElement();
            root.Add(head);
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
            head.Add(row);
            // the slots' own rows; + adds an empty slot (the list's own + copies the last one)
            Variants = new ListView
            {
                bindingPath = "m_Variants",
                showFoldoutHeader = true,
                showAddRemoveFooter = true,
                showBoundCollectionSize = false,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                tooltip = VariantsTooltip,
            };
            Variants.onAdd = _ =>
            {
                serializedObject.Update();
                variants.arraySize++;
                variants.GetArrayElementAtIndex(variants.arraySize - 1).objectReferenceValue = null;
                serializedObject.ApplyModifiedProperties();
            };
            // closed at first, then as left — per player for the session. The list's foldout follows the property's isExpanded
            // (true on a new player)
            string openKey = VariantsOpenKey + player.GetEntityId();
            variants.isExpanded = SessionState.GetBool(openKey, false);
            Variants.BindProperty(variants);
            Foldout fold = Variants.Q<Foldout>();
            fold?.RegisterValueChangedCallback(e =>
            {
                if (e.target == fold)
                    SessionState.SetBool(openKey, e.newValue);
            });
            head.Add(Variants);
            VariantNotice = new HelpBox("", HelpBoxMessageType.Warning);
            head.Add(VariantNotice);
            Show = new DropdownField("Show") { tooltip = ShowTooltip };
            head.Add(Show);
            var section = new VisualElement();
            root.Add(section);
            var usables = new List<FeedbackPreset>();
            FeedbackPreset built = null;
            FeedbackPreset aliasOf = null;
            void Build()
            {
                section.Clear();
                Section = section;
                serializedObject.Update();
                FeedbackPreset shown = VariantShow.Get(player);
                built = shown;
                FeedbackPreset preset = player.m_Preset;
                Object owner = shown != null ? shown : preset != null ? preset : (Object)player;
                bool own = ReferenceEquals(owner, player);
                SerializedObject listSo = own ? serializedObject : new SerializedObject(owner);
                // the shown list's alias: the own list's edited here, a preset's — linked or a Variant — read-only
                ListAlias = new TextField("Alias") { tooltip = own ? AliasTooltip : PresetAliasTooltip };
                ListAlias.AddToClassList(InspectorStyles.Alias);
                aliasOf = own ? null : (FeedbackPreset)owner;
                if (own)
                    ListAlias.BindProperty(serializedObject.FindProperty("m_ListAlias"));
                else
                {
                    ListAlias.SetValueWithoutNotify(aliasOf.m_Alias ?? "");
                    ListAlias.SetEnabled(false);
                }
                section.Add(ListAlias);
                var strip = new TimelineStrip(listSo, ItemListEdits.ItemsPath, player, !own, serializedObject);
                section.Add(strip);
                var list = new ItemListView(listSo, ItemListEdits.ItemsPath, player, bar.PlayItem, !own);
                List = list;
                SlotGraph = new SlotGraphField(strip, listSo, player, list);
                section.Add(list);
                var bottom = new VisualElement();
                bottom.AddToClassList(InspectorStyles.Bottom);
                if (shown != null)
                    bottom.Add(new Button(() => { Selection.activeObject = shown; EditorGUIUtility.PingObject(shown); }) { text = "Open Preset" });
                else if (preset != null)
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
                section.Add(SlotGraph);
                Builds++;
            }
            // the head's texts — the slots' count, the slots that play nothing and the lists of one alias, Show's choices and pick —
            // and a shown preset's alias
            void Head()
            {
                serializedObject.Update();
                if (aliasOf != null)
                    ListAlias.SetValueWithoutNotify(aliasOf.m_Alias ?? "");
                Variants.headerTitle = "Variants (" + (player.m_Variants != null ? player.m_Variants.Count : 0) + ")";
                List<string> problems = VariantSlotRule.Problems(player).Select(x => x.Message).Concat(VariantAliasRule.Problems(player)).ToList();
                VariantNotice.text = string.Join("\n", problems);
                VariantNotice.style.display = problems.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                usables.Clear();
                usables.AddRange(VariantShow.Usables(player));
                var choices = new List<string> { BaseText };
                foreach (FeedbackPreset v in usables)
                    choices.Add(usables.Count(x => x.name == v.name) > 1 ? v.name + " (slot " + player.m_Variants.IndexOf(v) + ")" : v.name);
                Show.choices = choices;
                Show.SetValueWithoutNotify(choices[usables.IndexOf(VariantShow.Get(player)) + 1]);
                Show.style.display = usables.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            Show.RegisterValueChangedCallback(_ => VariantShow.Set(player, Show.index > 0 ? usables[Show.index - 1] : null));
            Build();
            Head();
            section.TrackPropertyValue(link, _ => Build());
            // the slots edited or an asset deleted: the head again, and the section when its list is no longer the one shown
            void Refresh()
            {
                Head();
                if (!ReferenceEquals(built, VariantShow.Get(player)))
                    Build();
            }
            head.TrackSerializedObjectValue(serializedObject, _ => Refresh());
            void OnShow(FeedbackPlayer p)
            {
                if (p != player)
                    return;
                Head();
                Build();
            }
            // a preset's edit — its alias — reaches the head's notice too
            void OnChanges(ref ObjectChangeEventStream stream)
            {
                if (player != null)
                    Head();
            }
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                VariantShow.Changed += OnShow;
                EditorApplication.projectChanged += Refresh;
                ObjectChangeEvents.changesPublished += OnChanges;
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                VariantShow.Changed -= OnShow;
                EditorApplication.projectChanged -= Refresh;
                ObjectChangeEvents.changesPublished -= OnChanges;
            });
            return root;
        }
    }
}
