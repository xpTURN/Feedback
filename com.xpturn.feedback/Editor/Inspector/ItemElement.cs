using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // one row — the header and the body (built when open). The row is bound by index: the type in a slot can change under it
    // (a move, an undo), so a bind reads the slot again and a null or missing slot binds nothing
    internal sealed class ItemElement : VisualElement
    {
        internal readonly ItemHeader Header;
        internal readonly ItemBody Body = new ItemBody();
        internal readonly VisualElement Results = new VisualElement();
        internal Type BoundType;
        internal int Index = -1;
        readonly ItemListView m_Owner;
        SerializedProperty m_Item;
        FeedbackPlayer m_Host;
        string m_FoldKey;
        bool m_Missing;
        string m_SeverityClass;

        internal ItemElement(ItemListView owner)
        {
            m_Owner = owner;
            AddToClassList(InspectorStyles.Item);
            Header = new ItemHeader(() => m_Owner.PlayItem(Index));
            Add(Header);
            Results.AddToClassList(InspectorStyles.ValidationResults);
            Results.style.display = DisplayStyle.None;
            Add(Results);
            Add(Body);
            Header.Warning.clicked += () =>
            {
                Results.style.display = Results.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
                ShowResults();
            };
            Header.Fold.RegisterValueChangedCallback(e =>
            {
                if (e.target != Header.Fold)
                    return;
                if (m_FoldKey != null)
                    SessionState.SetBool(m_FoldKey, e.newValue);
                ShowBody(e.newValue);
            });
            Header.Solo.RegisterValueChangedCallback(e =>
            {
                Header.Mute.SetValueWithoutNotify(false);
                m_Owner.SetMask(Index, e.newValue ? PreviewMask.Solo : PreviewMask.None);
            });
            Header.Mute.RegisterValueChangedCallback(e =>
            {
                Header.Solo.SetValueWithoutNotify(false);
                m_Owner.SetMask(Index, e.newValue ? PreviewMask.Mute : PreviewMask.None);
            });
            Header.AddManipulator(new ContextualMenuManipulator(e =>
            {
                DropdownMenuAction.Status locked = m_Owner.Locked || m_Owner.ReadOnly ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
                bool ro = m_Owner.ReadOnly;
                e.menu.AppendAction("Copy", _ => m_Owner.Copy(Index), BoundType != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                e.menu.AppendAction("Paste After", _ => m_Owner.Paste(Index), ItemClipboard.Count > 0 && !ro ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                e.menu.AppendAction("Duplicate", _ => m_Owner.Duplicate(Index), BoundType != null && !ro ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                e.menu.AppendSeparator();
                e.menu.AppendAction("Delete", _ => m_Owner.Delete(Index), locked);
                e.menu.AppendAction("Move Up", _ => m_Owner.Move(Index, Index - 1), Index > 0 && !m_Owner.Locked && !ro ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                e.menu.AppendAction("Move Down", _ => m_Owner.Move(Index, Index + 1), Index < m_Owner.Count - 1 && !m_Owner.Locked && !ro ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }));
        }

        internal void Bind(SerializedProperty item, int index, FeedbackPlayer host, bool missing)
        {
            m_Item = item.Copy();
            m_Host = host;
            Index = index;
            m_Missing = missing;
            var value = item.managedReferenceValue as FeedbackItem;
            BoundType = value?.GetType();
            SerializedProperty active = value == null ? null : item.FindPropertyRelative("m_Active");
            if (active != null)
                Header.Active.BindProperty(active);
            else
                Header.Active.Unbind();
            Header.Active.SetEnabled(active != null && !m_Owner.ReadOnly);
            Body.SetEnabled(!m_Owner.ReadOnly);
            if (host != null && value is EffectItem)
            {
                PreviewMask mask = PreviewMasks.Get(host, index);
                Header.Solo.SetValueWithoutNotify(mask == PreviewMask.Solo);
                Header.Mute.SetValueWithoutNotify(mask == PreviewMask.Mute);
            }
            Object foldHost = m_Owner.FoldHost;
            m_FoldKey = value == null || foldHost == null ? null : ItemListView.FoldKey(foldHost, item.managedReferenceId);
            bool open = m_FoldKey != null && SessionState.GetBool(m_FoldKey, false);
            Header.Fold.SetValueWithoutNotify(open);
            Header.Fold.SetEnabled(value != null);
            Refresh();
            ShowBody(open);
            ShowResults();
        }

        // the slot — the highest severity, the messages as its tooltip — and the panel of messages and fix buttons it opens
        internal void ShowResults()
        {
            List<RuleResult> results = m_Owner.ResultsFor?.Invoke(Index) ?? new List<RuleResult>();
            RuleSeverity? highest = FeedbackValidator.Highest(results, Index);
            if (m_SeverityClass != null)
                Header.Warning.RemoveFromClassList(m_SeverityClass);
            m_SeverityClass = highest != null ? ResultRows.SeverityClass(highest.Value) : null;
            if (m_SeverityClass != null)
                Header.Warning.AddToClassList(m_SeverityClass);
            Header.Warning.style.display = highest != null ? DisplayStyle.Flex : DisplayStyle.None;
            Header.Warning.iconImage = highest != null ? Background.FromTexture2D(ResultRows.Icon(highest.Value)) : default;
            Header.Warning.tooltip = string.Join("\n", results);
            if (highest == null)
                Results.style.display = DisplayStyle.None;
            Results.Clear();
            if (Results.style.display == DisplayStyle.Flex)
            {
                foreach (RuleResult r in results)
                    Results.Add(ResultRows.Row(r, (x, f) => m_Owner.ApplyFix?.Invoke(x, f)));
            }
        }

        // the header's words again — after a field changed (the body stays)
        internal void Refresh()
        {
            var value = m_Item?.managedReferenceValue as FeedbackItem;
            Header.Show(value, m_Missing);
            EnableInClassList(InspectorStyles.ItemOff, value != null && !value.m_Active);
        }

        void ShowBody(bool open)
        {
            if (open && m_Item != null)
                Body.Build(m_Item, m_FoldKey, m_Host);
            else
                Body.Clear();
            Body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
