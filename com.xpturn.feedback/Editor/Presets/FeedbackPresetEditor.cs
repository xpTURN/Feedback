using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the preset asset's inspector — the two ways, the alias, the Graph Player (the strip, the Graph Preview and the items'
    // Graphs drawn on it — the list's fields keep no host), the strip and the list, Add · Paste, the results, the Graph
    // Preview, the hierarchy drop's way, and in play the linked players' Rebuild
    [CustomEditor(typeof(FeedbackPreset))]
    [CanEditMultipleObjects]
    internal sealed class FeedbackPresetEditor : UnityEditor.Editor
    {
        internal const string HelpText = "A Feedback Preset holds an item list — copy it into a player, or link players to it (they play this list). A Unity Preset of a FeedbackPlayer holds the whole component, settings too — a copy only, and its scene references are cleared when it is saved.";
        internal const string DropLinkText = "Link on hierarchy drop (all presets)";
        internal const string PlayText = "Edits reach the players using it at their next Rebuild";
        internal const string RebuildText = "Rebuild Them";
        internal const string AliasTooltip = "The text a Variant key picks this list by — FeedbackPlayer.VariantKey, PlayArgs.WithVariant, a director's FeedbackTimelineOptions — on every player that links this preset or holds it in its Variants";
        internal const string GraphPlayerText = "Graph Player";
        internal const string GraphPlayerTooltip = "The player the strip and the Graphs draw this preset on — its settings, its targets and its scene. Kept for this editor session, not saved. At first a player that links this preset, else one that holds it in its Variants";
        internal const long PollMs = 250;

        internal HelpBox Banner;
        internal ObjectField GraphPlayer;
        internal DropdownField GraphPick;
        internal TimelineStrip Strip;
        internal SlotGraphField SlotGraph;
        internal ItemListView List;
        // the player the strip and the Graphs are built on
        internal FeedbackPlayer Shown { get; private set; }

        readonly PlayerChoices m_Choices = new PlayerChoices();
        VisualElement m_Graphs;
        // the Graph Preview's place, below the bottom bar
        VisualElement m_Preview;
        FeedbackPreset m_Preset;
        bool m_Recheck;

        internal static string AsIfText(FeedbackPlayer player) => "Drawn on " + player.name + " as if it played this preset — it neither links it nor holds it in its Variants";

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.AddToClassList(InspectorStyles.Root);
            InspectorStyles.Attach(root);
            root.Add(new HelpBox(HelpText, HelpBoxMessageType.None));
            var drop = new Toggle(DropLinkText) { value = PresetDrop.Links };
            drop.RegisterValueChangedCallback(e => PresetDrop.Links = e.newValue);
            root.Add(drop);
            root.Add(new PropertyField(serializedObject.FindProperty("m_Alias"), "Alias") { tooltip = AliasTooltip });
            if (targets.Length > 1)
            {
                root.Add(new HelpBox(FeedbackPlayerEditor.OneAtATimeText, HelpBoxMessageType.Info));
                return root;
            }
            var preset = (FeedbackPreset)target;
            m_Preset = preset;
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
            root.Add(GraphRow());
            m_Graphs = new VisualElement();
            root.Add(m_Graphs);
            var list = new ItemListView(serializedObject, ItemListEdits.ItemsPath, null, null);
            List = list;
            m_Preview = new VisualElement();
            Show(PresetGraphPlayers.Resolve(preset));
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
            root.Add(m_Preview);
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                EditorSceneManager.sceneOpened += OnSceneOpened;
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                EditorSceneManager.sceneOpened -= OnSceneOpened;
                EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            });
            return root;
        }

        // the editor goes before its elements (a destroy, an inspector's rebuild): the elements a player drives — the strip,
        // the Graph Preview, the items' Graphs — go with it, before a change of the player reads the disposed serialized object
        void OnDisable()
        {
            m_Graphs?.Clear();
            m_Preview?.Clear();
            if (List != null)
            {
                foreach (ItemGraphField graph in List.Query<ItemGraphField>().ToList())
                    graph.RemoveFromHierarchy();
            }
        }

        // the Graph Player: a field (scene players and prefab players both) and, while its object has two or more, the
        // players' 'n · alias' dropdown
        VisualElement GraphRow()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            GraphPlayer = new ObjectField(GraphPlayerText) { objectType = typeof(FeedbackPlayer), allowSceneObjects = true, tooltip = GraphPlayerTooltip };
            GraphPlayer.AddToClassList(ObjectField.alignedFieldUssClassName);
            GraphPlayer.style.flexGrow = 1f;
            GraphPlayer.style.flexShrink = 1f;
            GraphPlayer.RegisterValueChangedCallback(e =>
            {
                if (e.target == GraphPlayer)
                    Picked(e.newValue as FeedbackPlayer);
            });
            row.Add(GraphPlayer);
            GraphPick = new DropdownField { name = PlayerField.PickName, tooltip = PlayerField.PickTooltip };
            GraphPick.style.width = PlayerField.PickWidth;
            GraphPick.style.flexShrink = 0f;
            GraphPick.RegisterValueChangedCallback(e =>
            {
                e.StopPropagation();
                int line = GraphPick.index;
                if (m_Choices.Shown && line >= 0 && line < m_Choices.Labels.Count)
                    Picked(m_Choices.At(line));
            });
            row.Add(GraphPick);
            row.schedule.Execute(Poll).Every(PollMs);
            return row;
        }

        void Picked(FeedbackPlayer player)
        {
            PresetGraphPlayers.Remember(m_Preset, player);
            if (ReferenceEquals(player, Shown))
                RefreshPick();
            else
                Show(player);
        }

        // the strip and the Graph Preview built on the player, the items' Graphs moved to it — the list itself stays
        void Show(FeedbackPlayer player)
        {
            Shown = player;
            GraphPlayer.SetValueWithoutNotify(player);
            m_Graphs.Clear();
            m_Preview.Clear();
            Strip = new TimelineStrip(serializedObject, ItemListEdits.ItemsPath, player);
            m_Graphs.Add(Strip);
            FeedbackPreset preset = m_Preset;
            SlotGraph = new SlotGraphField(Strip, serializedObject, player, List, preset)
            {
                Note = () => player != null && !PresetGraphPlayers.Lists(player, preset) ? AsIfText(player) : null,
            };
            m_Preview.Add(SlotGraph);
            List.GraphHost = player;
            RefreshPick();
        }

        void RefreshPick()
        {
            if (m_Choices.Update(Shown, false, null, false, false))
                GraphPick.choices = m_Choices.Labels;
            GraphPick.style.display = m_Choices.Shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_Choices.Shown && m_Choices.Index >= 0)
                GraphPick.SetValueWithoutNotify(m_Choices.Labels[m_Choices.Index]);
        }

        // the shown player gone (a scene closed, a delete, play mode's entry or exit): the remembered one, else the default.
        // The remembered one is looked for again only after a scene opened or play mode changed
        void Poll()
        {
            if (!ReferenceEquals(Shown, null) && Shown == null)
                Show(PresetGraphPlayers.Resolve(m_Preset));
            else if (m_Recheck)
            {
                m_Recheck = false;
                FeedbackPlayer player = PresetGraphPlayers.Resolve(m_Preset);
                if (!ReferenceEquals(player, Shown))
                    Show(player);
            }
            RefreshPick();
        }

        void OnSceneOpened(Scene scene, OpenSceneMode mode) => m_Recheck = true;

        void OnPlayModeChanged(PlayModeStateChange change) => m_Recheck = true;
    }

    // a preset inspector's Graph Player, per preset for the editor session: the picked player's GlobalObjectId (an unsaved
    // scene's objects share one — their entity id instead), found again in play mode and back (a prefab instance's ids
    // folded); none picked, the default — a player that links the preset, then one that holds it in its Variants, the loaded
    // scenes in order then the others (a prefab stage), each in hierarchy order then component order. A cleared field is
    // kept cleared
    internal static class PresetGraphPlayers
    {
        internal const string KeyPrefix = "xpTURN.Feedback.Inspector.GraphPlayer.";
        const string EntityPrefix = "entity:";
        const string None = "none";

        static string Key(FeedbackPreset preset) => KeyPrefix + preset.GetEntityId();

        internal static bool Lists(FeedbackPlayer player, FeedbackPreset preset) => player.m_Preset == preset || VariantShow.Uses(player, preset);

        internal static void Remember(FeedbackPreset preset, FeedbackPlayer player)
        {
            string id = None;
            if (player != null)
            {
                id = KeepPlayModeChanges.Id(player);
                if (KeepPlayModeChanges.NoId(id))
                    id = EntityPrefix + player.GetEntityId();
            }
            SessionState.SetString(Key(preset), id);
        }

        internal static void Forget(FeedbackPreset preset) => SessionState.EraseString(Key(preset));

        internal static FeedbackPlayer Resolve(FeedbackPreset preset)
        {
            string id = SessionState.GetString(Key(preset), "");
            if (id == None)
                return null;
            if (id.Length > 0)
            {
                FeedbackPlayer remembered = Find(id);
                if (remembered != null)
                    return remembered;
            }
            return Default(preset);
        }

        static FeedbackPlayer Find(string id)
        {
            if (id.StartsWith(EntityPrefix, StringComparison.Ordinal))
            {
                string entity = id.Substring(EntityPrefix.Length);
                foreach (FeedbackPlayer p in Resources.FindObjectsOfTypeAll<FeedbackPlayer>())
                {
                    if (p.GetEntityId().ToString() == entity)
                        return p;
                }
                return null;
            }
            return (KeepPlayModeChanges.Resolve(id) ?? KeepPlayModeChanges.Resolve(KeepPlayModeChanges.Fold(id)) ?? KeepPlayModeChanges.Unfold(id)) as FeedbackPlayer;
        }

        internal static FeedbackPlayer Default(FeedbackPreset preset)
        {
            List<FeedbackPlayer> found = PresetLinks.Linked(preset);
            if (found.Count == 0)
                return null;
            var keys = new Dictionary<FeedbackPlayer, List<int>>();
            foreach (FeedbackPlayer p in found)
                keys[p] = Order(p, preset);
            found.Sort((a, b) => Compare(keys[a], keys[b]));
            return found[0];
        }

        // the link, the scene, the hierarchy's sibling indices from the root, the player among its object's players
        static List<int> Order(FeedbackPlayer player, FeedbackPreset preset)
        {
            var key = new List<int> { player.m_Preset == preset ? 0 : 1, SceneRank(player.gameObject.scene) };
            int at = key.Count;
            for (Transform t = player.transform; t != null; t = t.parent)
                key.Insert(at, t.GetSiblingIndex());
            key.Add(Array.IndexOf(player.GetComponents<FeedbackPlayer>(), player));
            return key;
        }

        static int SceneRank(Scene scene)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i) == scene)
                    return i;
            }
            return SceneManager.sceneCount;
        }

        static int Compare(List<int> a, List<int> b)
        {
            for (int i = 0; i < a.Count && i < b.Count; i++)
            {
                int c = a[i].CompareTo(b[i]);
                if (c != 0)
                    return c;
            }
            return a.Count.CompareTo(b.Count);
        }
    }
}
