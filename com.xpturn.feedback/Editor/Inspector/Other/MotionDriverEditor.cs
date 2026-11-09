using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using PopupWindow = UnityEditor.PopupWindow;

namespace xpTURN.Feedback.Editor
{
    // the driver — the preview bar (one target, edit mode), the item: its kind from the add menu's target items only (one
    // managed-reference slot) and its body, then the mode, the time mode, the intensity and the Follow transform. In play
    // mode an edit prepares the driver again at the next tick, from 0 s
    [CustomEditor(typeof(MotionDriver))]
    [CanEditMultipleObjects]
    internal sealed class MotionDriverEditor : UnityEditor.Editor
    {
        internal const string ItemPath = "m_Item";
        internal const string SetItemUndo = "Set Motion Driver Item";
        internal const string PlayModeText = "A play-mode edit prepares the driver again at the next tick — from 0 s.";
        internal const string OneAtATimeText = "The item is edited one driver at a time.";
        static readonly string[] s_Fields = { "m_Mode", "m_TimeMode", "m_Intensity", "m_FollowTarget" };

        internal Button ItemButton;
        internal ItemBody Body;
        internal ValidationView Validation;
        long m_ItemId = long.MinValue;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.AddToClassList(InspectorStyles.Root);
            InspectorStyles.Attach(root);
            var driver = (MotionDriver)target;
            bool one = targets.Length == 1;
            if (one)
                root.Add(new PreviewBar(driver));
            if (one)
            {
                var row = new VisualElement();
                row.AddToClassList(InspectorStyles.Row);
                var label = new Label("Item");
                label.AddToClassList(InspectorStyles.EaseLabel);
                ItemButton = new Button(() => OpenItemMenu(ItemButton.worldBound));
                ItemButton.AddToClassList(InspectorStyles.Grow);
                row.Add(label);
                row.Add(ItemButton);
                root.Add(row);
                Body = new ItemBody();
                root.Add(Body);
                RebuildItem();
                root.TrackSerializedObjectValue(serializedObject, _ => RebuildItem());
                Validation = new ValidationView(driver, serializedObject, null, null);
                root.Add(Validation);
            }
            foreach (string name in s_Fields)
            {
                SerializedProperty p = serializedObject.FindProperty(name);
                if (p == null)
                    continue;
                var field = new PropertyField(p);
                field.BindProperty(p);
                root.Add(field);
            }
            if (!one)
                root.Add(new HelpBox(OneAtATimeText, HelpBoxMessageType.Info));
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                root.Add(new HelpBox(PlayModeText, HelpBoxMessageType.Info));
            return root;
        }

        // the body follows the slot's instance — built again when another item is set (an undo too)
        void RebuildItem()
        {
            if (target == null || Body == null)
                return;
            serializedObject.Update();
            SerializedProperty item = serializedObject.FindProperty(ItemPath);
            var value = item.managedReferenceValue as TargetItem;
            ItemButton.text = value != null ? ItemWords.Name(value.GetType()) + (value.m_Motion != null ? " / " + value.m_Motion.Kind : "") : "(none)";
            if (item.managedReferenceId == m_ItemId && value != null)
                return;
            m_ItemId = item.managedReferenceId;
            if (value == null)
                Body.Clear();
            else
                Body.Build(item, ItemListView.FoldPrefix + target.GetEntityId() + ".Driver", (Component)target);
        }

        void OpenItemMenu(Rect at) => PopupWindow.Show(at, new AddMenuPopup(Leaves(FeedbackCatalog.All), leaf => SetItem(serializedObject, leaf)));

        // the add menu's target items — a driver drives one target item
        internal static List<CatalogLeaf> Leaves(IReadOnlyList<CatalogLeaf> all)
        {
            var leaves = new List<CatalogLeaf>();
            foreach (CatalogLeaf leaf in all)
            {
                if (typeof(TargetItem).IsAssignableFrom(leaf.Type))
                    leaves.Add(leaf);
            }
            return leaves;
        }

        // the leaf's new item in the slot — one undo
        internal static void SetItem(SerializedObject so, in CatalogLeaf leaf)
        {
            if (!(ItemFactory.Create(leaf) is TargetItem item))
                return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(SetItemUndo);
            so.Update();
            so.FindProperty(ItemPath).managedReferenceValue = item;
            so.ApplyModifiedProperties();
        }
    }
}
