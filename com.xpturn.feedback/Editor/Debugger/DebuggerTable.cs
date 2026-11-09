using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // one of the debugger's tables: a MultiColumnListView over the row numbers 0 … n − 1 (a cell gets the source row), its
    // columns' texts kept by name — a test reads a cell without a layout. Sorting compares the reader's numbers; the header
    // keeps its sort, order, widths and hidden columns as view data under one key a table. A line above and below it and a
    // space after (the style sheet's debugger list); its height is up to MaxLines rows plus what the layout gives the header
    // and the lines, and a bottom space. Its own horizontal scroller is hidden: the window's one scroller moves every table
    // (ScrollTo — the header follows the hidden scroller's value), a sideways wheel goes to it
    internal sealed class DebuggerTable
    {
        internal const string ViewKey = "xpTURN.Feedback.Debugger.Table.";
        internal const string HeaderClass = "unity-multi-column-header";
        internal const float BottomSpace = 6f;

        internal readonly MultiColumnListView View;
        internal readonly ScrollView Scroll;
        internal readonly List<int> Rows = new List<int>(64);
        readonly Dictionary<string, Func<int, string>> m_Texts = new Dictionary<string, Func<int, string>>();
        // the header, the lines and the bottom space — the guesses until the first layout
        float m_Chrome = FeedbackDebuggerWindow.HeaderHeight + FeedbackDebuggerWindow.ListBorder + BottomSpace;

        // the table's width or its columns' changed (the window's scroller takes its range again) · a sideways wheel's delta
        internal event Action Reached;
        internal event Action<float> Wheeled;

        internal DebuggerTable(string name, bool sortable)
        {
            View = new MultiColumnListView
            {
                name = name.ToLowerInvariant() + "-table",
                fixedItemHeight = FeedbackDebuggerWindow.LineHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                itemsSource = Rows,
                sortingMode = sortable ? ColumnSortingMode.Default : ColumnSortingMode.None,
                viewDataKey = ViewKey + name,
            };
            View.AddToClassList(InspectorStyles.DebuggerList);
            Scroll = View.Q<ScrollView>();
            Scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            View.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                Fit();
                Reached?.Invoke();
            });
            Scroll.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => Reached?.Invoke());
            View.RegisterCallback<WheelEvent>(e =>
            {
                if (e.delta.x != 0f)
                    Wheeled?.Invoke(e.delta.x);
            });
            Height();
        }

        // how far the columns reach past the table's width
        internal float Overflow => Mathf.Max(0f, Scroll.contentContainer.layout.width - Scroll.contentViewport.layout.width);

        internal float Visible => Scroll.contentViewport.layout.width;

        // the window's scroller's place, as far as this table reaches
        internal void ScrollTo(float x)
        {
            float to = Mathf.Min(x, Overflow);
            if (Mathf.Abs(Scroll.scrollOffset.x - to) > 0.01f)
                Scroll.scrollOffset = new Vector2(to, Scroll.scrollOffset.y);
        }

        void Height() => View.style.height = Mathf.Clamp(Rows.Count, 1, FeedbackDebuggerWindow.MaxLines) * FeedbackDebuggerWindow.LineHeight + m_Chrome;

        // the header and the lines as laid out — a sort mark makes the header taller than its guess
        void Fit()
        {
            VisualElement header = View.Q(className: HeaderClass);
            float chrome = (header != null && header.layout.height > 0f ? header.layout.height : FeedbackDebuggerWindow.HeaderHeight) + BottomSpace
                + View.resolvedStyle.borderTopWidth + View.resolvedStyle.borderBottomWidth;
            if (Mathf.Abs(chrome - m_Chrome) < 0.5f)
                return;
            m_Chrome = chrome;
            Height();
        }

        internal void Add(string name, float width, Func<int, string> text, Comparison<int> compare = null, bool optional = true)
        {
            m_Texts.Add(name, text);
            View.columns.Add(new Column
            {
                name = name,
                title = name,
                width = width,
                optional = optional,
                sortable = compare != null,
                comparison = compare,
                makeCell = () => new Label(),
                bindCell = (e, i) => ((Label)e).text = text(i),
            });
        }

        // the text the column binds for a source row
        internal string Text(string column, int row) => m_Texts[column](row);

        // n rows — then the visible cells bound again
        internal void Refresh(int n)
        {
            if (Rows.Count != n)
            {
                Rows.Clear();
                for (int k = 0; k < n; k++)
                    Rows.Add(k);
                Height();
            }
            View.RefreshItems();
        }

        // ---- the cells' texts

        internal static string Seconds(double s) => double.IsPositiveInfinity(s) ? "∞" : s.ToString("0.00", CultureInfo.InvariantCulture);

        internal static string NameOf(Object o) => o != null ? o.name : "(destroyed)";

        internal static string OwnerText(in PlayRow r)
        {
            switch (r.Kind)
            {
                case RunnerKind.Player: return NameOf(r.Owner);
                case RunnerKind.Driver: return NameOf(r.Owner) + " (driver)";
                default: return r.Owner != null ? r.Owner.GetType().Name : "runner";
            }
        }

        internal static string PositionText(in PlayRow r)
        {
            switch (r.Kind)
            {
                case RunnerKind.Player: return Seconds(r.Position / 1e6) + " / " + Seconds(r.Total) + " s";
                case RunnerKind.Driver: return Seconds(r.DriverSeconds) + " s";
                default: return "";
            }
        }

        internal static string StateText(in PlayRow r) => ((r.Reverse ? "◀ " : "") + (r.Paused ? "‖ " : "") + (r.OffNominal ? "off-nominal" : "")).TrimEnd();

        internal static string WaitsText(in PlayRow r) => r.OpenWaits > 0 ? r.OpenWaits + (r.UntimedWaits > 0 ? " (" + r.UntimedWaits + " untimed)" : "") : "";

        internal static string LinkText(in PlayRow r) => r.Parent != null ? "waited on by " + NameOf(r.Parent) + " #" + r.ParentSerial : r.HasListener ? "listener" : "";

        internal static string MissedText(in GroupRow g) => g.Missed + g.MissedQuiet > 0 ? g.Missed + " · " + g.MissedQuiet : "";

        // a global slot has no target — a destroyed one is a reference to a dead object
        internal static string TargetText(in LayerSlotInfo l) => ReferenceEquals(l.Target, null) ? "(global)" : NameOf(l.Target);

        internal static string SlotText(in LayerSlotInfo l) => l.SlotType != null ? l.SlotType.Name : "?";

        internal static string BaseText(in LayerSlotInfo l) => l.IsAlive ? l.Base.ToString() : "dead";
    }
}
