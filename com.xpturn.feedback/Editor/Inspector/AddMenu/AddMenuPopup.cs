using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the add menu — a search field over a tree. No words: the favorites, the recent ones, then the categories (Transform ›
    // Position › Punch); words: the matches by score. A click or Enter on a leaf chooses it (recorded as recent); ☆ marks a
    // favorite. A leaf without a registered implementation is dimmed
    internal sealed class AddMenuPopup : PopupWindowContent
    {
        internal const string FavoritesText = "Favorites";
        internal const string RecentText = "Recent";
        internal const string DimmedTip = "No implementation is registered for this type — its optional package is missing.";

        internal sealed class Row
        {
            internal readonly string Label;
            internal readonly bool IsLeaf;
            internal readonly CatalogLeaf Leaf;

            internal Row(string label)
            {
                Label = label;
            }

            internal Row(string label, in CatalogLeaf leaf)
            {
                Label = label;
                IsLeaf = true;
                Leaf = leaf;
            }
        }

        readonly IReadOnlyList<CatalogLeaf> m_Leaves;
        readonly Action<CatalogLeaf> m_Chosen;
        internal ToolbarSearchField Search;
        internal UnityEngine.UIElements.TreeView Tree;
        // the leaves shown, in the tree's order — with words, the first is Enter's
        internal readonly List<CatalogLeaf> Shown = new List<CatalogLeaf>();
        int m_NextId;
        bool m_SearchFocused;
        internal static AddMenuPopup Last;

        internal AddMenuPopup(IReadOnlyList<CatalogLeaf> leaves, Action<CatalogLeaf> chosen)
        {
            m_Leaves = leaves;
            m_Chosen = chosen;
            Last = this;
        }

        public override Vector2 GetWindowSize() => new Vector2(300, 380);

        public override void OnGUI(Rect rect)
        {
        }

        public override void OnClose()
        {
            Input.imeCompositionMode = IMECompositionMode.Auto;
        }

        public override VisualElement CreateGUI()
        {
            var root = new VisualElement();
            Vector2 size = GetWindowSize();
            root.style.width = size.x;
            root.style.height = size.y;
            root.AddToClassList(InspectorStyles.AddMenu);
            InspectorStyles.Attach(root);
            Search = new ToolbarSearchField();
            Search.AddToClassList(InspectorStyles.AddMenuSearch);
            Search.RegisterValueChangedCallback(e => Filter(e.newValue));
            Search.RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && !string.IsNullOrWhiteSpace(Search.value) && Shown.Count > 0)
                {
                    Choose(Shown[0]);
                    e.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            Search.RegisterCallback<FocusInEvent>(_ =>
            {
                m_SearchFocused = true;
                Search.schedule.Execute(() =>
                {
                    if (m_SearchFocused)
                        Input.imeCompositionMode = IMECompositionMode.Off;
                });
            });
            Search.RegisterCallback<FocusOutEvent>(_ =>
            {
                m_SearchFocused = false;
                Input.imeCompositionMode = IMECompositionMode.Auto;
            });
            Tree = new UnityEngine.UIElements.TreeView { selectionType = SelectionType.Single, fixedItemHeight = 20 };
            Tree.AddToClassList(InspectorStyles.Grow);
            Tree.makeItem = MakeRow;
            Tree.bindItem = (e, i) => BindRow(e, Tree.GetItemDataForIndex<Row>(i));
            Tree.itemsChosen += chosen =>
            {
                foreach (object o in chosen)
                {
                    if (o is Row r && r.IsLeaf)
                    {
                        Choose(r.Leaf);
                        return;
                    }
                }
            };
            root.Add(Search);
            root.Add(Tree);
            Filter("");
            root.schedule.Execute(() => Search.Focus());
            return root;
        }

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList(InspectorStyles.Row);
            var label = new Label();
            label.AddToClassList(InspectorStyles.Grow);
            var star = new Button { tooltip = "Favorite" };
            star.AddToClassList(InspectorStyles.AddMenuStar);
            star.clicked += () =>
            {
                if (row.userData is Row r && r.IsLeaf)
                {
                    FeedbackCatalog.Favorite(r.Leaf, !FeedbackCatalog.IsFavorite(r.Leaf));
                    Filter(Search.value);
                }
            };
            row.Add(label);
            row.Add(star);
            row.RegisterCallback<ClickEvent>(e =>
            {
                if (!(e.target is Button) && row.userData is Row r && r.IsLeaf)
                    Choose(r.Leaf);
            });
            return row;
        }

        static void BindRow(VisualElement e, Row r)
        {
            e.userData = r;
            var label = e.Q<Label>();
            var star = e.Q<Button>();
            label.text = r.Label;
            bool dimmed = r.IsLeaf && !r.Leaf.Registered;
            label.EnableInClassList(InspectorStyles.Dimmed, dimmed);
            label.tooltip = dimmed ? DimmedTip : r.IsLeaf ? r.Leaf.Path : "";
            star.style.display = r.IsLeaf ? DisplayStyle.Flex : DisplayStyle.None;
            if (r.IsLeaf)
                star.text = FeedbackCatalog.IsFavorite(r.Leaf) ? "★" : "☆";
        }

        // the tree for the words — none: favorites, recent, the categories; some: the matches, best first
        internal void Filter(string query)
        {
            Shown.Clear();
            m_NextId = 0;
            var roots = new List<TreeViewItemData<Row>>();
            var expanded = new List<int>();
            if (string.IsNullOrWhiteSpace(query))
            {
                Group(roots, expanded, FavoritesText, FeedbackCatalog.Listed(m_Leaves, FeedbackCatalog.FavoritesKey));
                Group(roots, expanded, RecentText, FeedbackCatalog.Listed(m_Leaves, FeedbackCatalog.RecentKey));
                Categories(roots);
            }
            else
            {
                foreach (CatalogLeaf leaf in FeedbackCatalog.Search(m_Leaves, query))
                {
                    roots.Add(new TreeViewItemData<Row>(m_NextId++, new Row(leaf.Path, leaf)));
                    Shown.Add(leaf);
                }
            }
            if (Tree == null)
                return;
            Tree.SetRootItems(roots);
            Tree.Rebuild();
            foreach (int id in expanded)
                Tree.ExpandItem(id);
        }

        void Group(List<TreeViewItemData<Row>> roots, List<int> expanded, string name, List<CatalogLeaf> leaves)
        {
            if (leaves.Count == 0)
                return;
            int id = m_NextId++;
            var children = new List<TreeViewItemData<Row>>(leaves.Count);
            foreach (CatalogLeaf leaf in leaves)
            {
                children.Add(new TreeViewItemData<Row>(m_NextId++, new Row(leaf.Path, leaf)));
                Shown.Add(leaf);
            }
            roots.Add(new TreeViewItemData<Row>(id, new Row(name), children));
            expanded.Add(id);
        }

        // the paths' segments as groups — the last segment is the leaf
        void Categories(List<TreeViewItemData<Row>> roots)
        {
            var top = new Node(null);
            foreach (CatalogLeaf leaf in m_Leaves)
            {
                string[] segments = leaf.Path.Split('/');
                Node at = top;
                for (int i = 0; i < segments.Length - 1; i++)
                    at = at.Child(segments[i]);
                at.Leaves.Add(leaf);
            }
            foreach (Node n in top.Children)
                roots.Add(Item(n));
        }

        TreeViewItemData<Row> Item(Node node)
        {
            int id = m_NextId++;
            var children = new List<TreeViewItemData<Row>>();
            foreach (Node n in node.Children)
                children.Add(Item(n));
            foreach (CatalogLeaf leaf in node.Leaves)
            {
                children.Add(new TreeViewItemData<Row>(m_NextId++, new Row(leaf.Name, leaf)));
                Shown.Add(leaf);
            }
            return new TreeViewItemData<Row>(id, new Row(node.Name), children);
        }

        sealed class Node
        {
            internal readonly string Name;
            internal readonly List<Node> Children = new List<Node>();
            internal readonly List<CatalogLeaf> Leaves = new List<CatalogLeaf>();

            internal Node(string name)
            {
                Name = name;
            }

            internal Node Child(string name)
            {
                foreach (Node n in Children)
                {
                    if (n.Name == name)
                        return n;
                }
                var child = new Node(name);
                Children.Add(child);
                return child;
            }
        }

        internal void Choose(in CatalogLeaf leaf)
        {
            FeedbackCatalog.Used(leaf);
            m_Chosen(leaf);
            editorWindow?.Close();
        }
    }
}
