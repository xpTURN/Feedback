using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using PopupWindow = UnityEditor.PopupWindow;

namespace xpTURN.Feedback.Editor
{
    // the item list — a ListView bound to the managed-reference list, rows bound by index. The ListView follows a change of
    // size but not a move, its undo or a redo: the structure's signature — the slots' managed reference ids — is watched and
    // a change of it binds the rows again; a field's change rewrites the headers only. Delete, copy, paste and duplicate come
    // as commands (the keys differ per platform). A missing item type locks delete and move, the drag reorder too. An add
    // (the menu · a drop · a paste) goes after the selected slot, else at the end
    internal sealed class ItemListView : VisualElement
    {
        internal const string FoldPrefix = "xpTURN.Feedback.Inspector.Fold.";

        internal readonly ListView List;
        readonly SerializedObject m_So;
        readonly string m_Path;
        readonly FeedbackPlayer m_Host;
        readonly Action<int> m_PlayItem;
        string m_Signature;
        internal int Refreshes;
        // the validation's results by list slot and its fix (ValidationView)
        internal Func<int, List<RuleResult>> ResultsFor;
        internal Action<RuleResult, RuleFix> ApplyFix;

        internal void RefreshResults() => List.Query<ItemElement>().ForEach(e => e.ShowResults());

        internal ItemListView(SerializedObject so, string path, FeedbackPlayer host, Action<int> playItem, bool readOnly = false)
        {
            ReadOnly = readOnly;
            m_So = so;
            m_Path = path;
            m_Host = host;
            m_PlayItem = playItem;
            AddToClassList(InspectorStyles.List);
            List = new ListView
            {
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                bindingPath = path,
                showBoundCollectionSize = false,
                showAddRemoveFooter = false,
                selectionType = SelectionType.Multiple,
            };
            List.makeItem = () => new ItemElement(this);
            List.bindItem = (e, i) =>
            {
                SerializedProperty list = m_So.FindProperty(m_Path);
                if (list != null && i < list.arraySize)
                    ((ItemElement)e).Bind(list.GetArrayElementAtIndex(i), i, m_Host, Locked);
            };
            Locked = ItemListEdits.Locked(so);
            List.reorderable = !Locked && !ReadOnly;
            Add(List);
            List.Bind(so);
            m_Signature = Signature();
            this.TrackSerializedObjectValue(so, _ => OnChanged());
            RegisterCallback<ValidateCommandEvent>(OnValidate);
            RegisterCallback<ExecuteCommandEvent>(OnExecute);
            RegisterCallback<DragUpdatedEvent>(OnDragUpdated, TrickleDown.TrickleDown);
            RegisterCallback<DragPerformEvent>(OnDragPerform, TrickleDown.TrickleDown);
        }

        internal IReadOnlyList<CatalogLeaf> Leaves => FeedbackCatalog.All;

        internal bool Locked { get; private set; }

        // a linked player's list — Copy only, no drag, the bodies and the switches off
        internal bool ReadOnly { get; }

        // the fold key's object — the host, else the edited object (a preset)
        internal Object FoldHost => m_Host != null ? m_Host : m_So.targetObject;

        internal int Count
        {
            get
            {
                SerializedProperty list = m_So.FindProperty(m_Path);
                return list != null ? list.arraySize : 0;
            }
        }

        // a slot's fold state — the same id is in a duplicated player, so the player is in the key
        internal static string FoldKey(Object host, long id) => FoldPrefix + host.GetEntityId() + "." + id;

        string Signature()
        {
            m_So.Update();
            SerializedProperty list = m_So.FindProperty(m_Path);
            if (list == null)
                return "";
            var sb = new StringBuilder();
            for (int i = 0; i < list.arraySize; i++)
                sb.Append(list.GetArrayElementAtIndex(i).managedReferenceId).Append(';');
            return sb.ToString();
        }

        void OnChanged()
        {
            if (m_So.targetObject == null)
                return;
            string signature = Signature();
            bool locked = ItemListEdits.Locked(m_So);
            if (signature != m_Signature || locked != Locked)
            {
                m_Signature = signature;
                Locked = locked;
                List.reorderable = !locked && !ReadOnly;
                Refreshes++;
                List.RefreshItems();
                return;
            }
            List.Query<ItemElement>().ForEach(e => e.Refresh());
        }

        static bool Handles(string command) => command == "SoftDelete" || command == "Delete" || command == "Copy" || command == "Duplicate" || command == "Paste";

        void OnValidate(ValidateCommandEvent e)
        {
            if (!Handles(e.commandName) || (ReadOnly && e.commandName != "Copy"))
                return;
            if (e.commandName == "Paste" ? ItemClipboard.Count > 0 : Selected().Count > 0)
                e.StopPropagation();
        }

        void OnExecute(ExecuteCommandEvent e)
        {
            if (!Handles(e.commandName) || (ReadOnly && e.commandName != "Copy"))
                return;
            List<int> selected = Selected();
            switch (e.commandName)
            {
                case "Paste":
                    Paste();
                    break;
                case "Copy":
                    if (selected.Count > 0)
                        ItemListEdits.CopyToClipboard(m_So, m_Path, selected);
                    break;
                case "Duplicate":
                    if (selected.Count > 0)
                        ItemListEdits.Duplicate(m_So, m_Path, selected);
                    break;
                default:
                    if (selected.Count == 0)
                        return;
                    ItemListEdits.Delete(m_So, m_Path, selected);
                    List.ClearSelection();
                    break;
            }
            e.StopPropagation();
        }

        List<int> Selected() => new List<int>(List.selectedIndices);

        internal void Delete(int index)
        {
            if (!ReadOnly)
                ItemListEdits.Delete(m_So, m_Path, new[] { index });
        }

        internal void Move(int from, int to)
        {
            if (!ReadOnly)
                ItemListEdits.Move(m_So, m_Path, from, to);
        }

        internal void Copy(int index) => ItemListEdits.CopyToClipboard(m_So, m_Path, new[] { index });

        internal void Duplicate(int index)
        {
            if (!ReadOnly)
                ItemListEdits.Duplicate(m_So, m_Path, new[] { index });
        }

        // after the selected slot (else the end) — a slot's menu pastes after that slot
        internal int Paste(int after = -2) => ReadOnly ? -1 : ItemListEdits.Paste(m_So, m_Path, after == -2 ? InsertAt() : after + 1, out _);

        internal void PlayItem(int index) => m_PlayItem?.Invoke(index);

        // after the last selected slot — the end when none is selected
        internal int InsertAt()
        {
            int last = -1;
            foreach (int i in List.selectedIndices)
                last = Math.Max(last, i);
            return last < 0 ? -1 : last + 1;
        }

        internal int Add(in CatalogLeaf leaf)
            => ReadOnly ? -1 : ItemListEdits.Insert(m_So, m_Path, InsertAt(), new[] { ItemFactory.Create(leaf) }, ComponentDrop.UndoName);

        internal void OpenAddMenu(Rect at) => PopupWindow.Show(at, new AddMenuPopup(Leaves, leaf => Add(leaf)));

        List<Object> Dropped() => ComponentDrop.Accepted(m_Host, DragAndDrop.objectReferences);

        static bool Dragging() => DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0;

        List<FeedbackPreset> DroppedPresets() => DragAndDrop.objectReferences.OfType<FeedbackPreset>().ToList();

        bool PresetsFit(List<FeedbackPreset> presets) => !ReadOnly && presets.All(p => p != m_So.targetObject && PresetOps.CanCopy(p));

        void OnDragUpdated(DragUpdatedEvent e)
        {
            if (!Dragging())
                return;
            List<FeedbackPreset> presets = DroppedPresets();
            if (presets.Count > 0)
            {
                DragAndDrop.visualMode = PresetsFit(presets) ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                e.StopPropagation();
                return;
            }
            if (ReadOnly)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                e.StopPropagation();
                return;
            }
            List<Object> dropped = Dropped();
            DragAndDrop.visualMode = dropped.Count > 0 && ComponentDrop.Suggest(Leaves, dropped).Count > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            e.StopPropagation();
        }

        // one leaf: no menu · several: one menu, the chosen leaf for each object it fits
        void OnDragPerform(DragPerformEvent e)
        {
            if (!Dragging())
                return;
            e.StopPropagation();
            List<FeedbackPreset> presets = DroppedPresets();
            if (presets.Count > 0)
            {
                DropPresets(presets);
                return;
            }
            if (ReadOnly)
                return;
            List<Object> dropped = Dropped();
            List<CatalogLeaf> leaves = dropped.Count > 0 ? ComponentDrop.Suggest(Leaves, dropped) : null;
            if (leaves == null || leaves.Count == 0)
                return;
            DragAndDrop.AcceptDrag();
            int at = InsertAt();
            if (leaves.Count == 1)
            {
                ComponentDrop.Add(m_So, m_Path, at, leaves[0], dropped);
                return;
            }
            var menu = new GenericMenu();
            foreach (CatalogLeaf leaf in leaves)
            {
                CatalogLeaf chosen = leaf;
                menu.AddItem(new GUIContent(leaf.Path), false, () => ComponentDrop.Add(m_So, m_Path, at, chosen, dropped));
            }
            menu.ShowAsContext();
        }

        // presets dropped on the list: their copies at the insert place, one undo group each
        internal void DropPresets(List<FeedbackPreset> presets)
        {
            if (!PresetsFit(presets))
                return;
            DragAndDrop.AcceptDrag();
            int at = InsertAt();
            foreach (FeedbackPreset p in presets)
            {
                int placed = PresetOps.CopyInto(m_So, m_Path, at, p);
                if (placed >= 0)
                    at = placed + p.m_Items.Count;
            }
        }

        internal void SetMask(int index, PreviewMask mask)
        {
            if (m_Host != null)
                PreviewMasks.Set(m_Host, index, mask);
        }
    }
}
