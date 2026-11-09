using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // the runtime debugger — the playing loop's scheduler or the preview session's, picked at each refresh. Five panels in
    // Foldouts: Playbacks (a table of the plays, the selected play's items under it), Layers and Events as tables, Services
    // and Stages as lines; a folded panel is not laid out and binds nothing; 10 Hz while visible. One horizontal scroller at
    // the window's bottom moves every table's columns. Read only: it changes no
    // runtime state — a play's row double-clicked or submitted selects its owner in the editor, Clear empties the window's
    // ring. The selection is the reader's handle, set back on its row after each refresh (the rows are rebuilt and the sort
    // moves them). The diagnostics are subscribed while the window is enabled (OnDisable comes before a domain reload too;
    // with the reload off it stays across a play entry)
    internal sealed class FeedbackDebuggerWindow : EditorWindow
    {
        internal const string Title = "Feedback Debugger";
        internal const string MenuPath = "Window/xpTURN/Feedback Debugger";
        internal const string FoldKey = "xpTURN.Feedback.Debugger.";
        internal static readonly string[] Panels = { "Playbacks", "Layers", "Services", "Stages", "Events" };
        internal const int RefreshMs = 100;
        internal const float LineHeight = 18f;
        internal const float HeaderHeight = 24f;
        // the lines above and below a list (the style sheet's debugger list)
        internal const float ListBorder = 2f;
        internal const int MaxLines = 13;
        // the call stack's area: this many of its lines, the rest scrolled
        internal const int StackLines = 4;
        internal const string NoSourceText = "No source — enter play mode or start a preview.";
        internal const string NotMadeText = " — not made yet";
        internal const string NoSelectionText = "Pick a playback to see its items and its player's tokens.";
        internal const string StagesNote = "The stages of every scheduler that ran this frame (the loop, a preview, a test's direct tick).";
        internal const string StoppedText = "The stages did not run while playbacks are alive — the PlayerLoop may have been replaced (FeedbackPlayerLoop.Reinstall()).";
        internal const string ReloadNote = "Events before a domain reload are dropped.";
        internal const string QuietText = "ended without an event";
        internal const string CallSitesTip = "Keep where each play starts — about 0.2 ms a play, and it allocates, while on.";
        internal const int StoppedRefreshes = 5;

        internal readonly DebuggerReader Reader = new DebuggerReader();
        internal readonly List<string> ServiceLines = new List<string>(32);
        internal readonly Foldout[] Folds = new Foldout[5];
        internal DebuggerTable Plays;
        internal DebuggerTable ItemsTable;
        internal DebuggerTable Layers;
        internal DebuggerTable EventsTable;
        internal ListView ServiceList;
        internal ListView StageList;
        internal TextField Stack;
        internal DebuggerEvents Events;
        internal StageCosts Costs;
        internal DebuggerSourceKind Kind;
        internal Label Source;
        internal ToolbarToggle CallSites;
        internal Scroller Horizontal;
        internal Label SelectedLine;
        internal Label EventsLine;
        internal Label MissedLine;
        internal Label StoppedLine;
        readonly List<int> m_ServiceRows = new List<int>(32);
        readonly List<int> m_StageRows = new List<int>(4);
        readonly int[] m_One = new int[1];
        DebuggerTable[] m_Tables;
        IVisualElementScheduledItem m_Tick;
        int m_StoppedRuns;
        bool m_Refreshing;

        [MenuItem(MenuPath)]
        internal static FeedbackDebuggerWindow Open() => GetWindow<FeedbackDebuggerWindow>(Title);

        internal static bool IsOpen(int panel) => EditorPrefs.GetBool(FoldKey + Panels[panel], true);

        void OnEnable()
        {
            Events ??= new DebuggerEvents();
            Events.Subscribe();
            Costs ??= new StageCosts();
            Costs.Start();
            DebuggerCallSites.Load();
        }

        void OnDisable()
        {
            Events?.Unsubscribe();
            Costs?.Dispose();
        }

        void OnBecameVisible() => m_Tick?.Resume();

        void OnBecameInvisible() => m_Tick?.Pause();

        void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            InspectorStyles.Attach(root);
            var bar = new Toolbar();
            bar.Add(Source = new Label { name = "source" });
            Source.style.flexGrow = 1;
            bar.Add(CallSites = new ToolbarToggle { text = "Call sites", value = DebuggerCallSites.Enabled, tooltip = CallSitesTip });
            CallSites.RegisterValueChangedCallback(e => DebuggerCallSites.Enabled = e.newValue);
            root.Add(bar);
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1f;
            root.Add(scroll);
            for (int i = 0; i < Panels.Length; i++)
            {
                int panel = i;
                var fold = new Foldout { text = Panels[i], value = IsOpen(i), name = Panels[i].ToLowerInvariant() };
                fold.RegisterValueChangedCallback(e =>
                {
                    if (e.target != fold)
                        return;
                    EditorPrefs.SetBool(FoldKey + Panels[panel], e.newValue);
                    Refresh();
                });
                Folds[i] = fold;
                scroll.Add(fold);
            }
            BuildPlays();
            BuildLayers();
            Folds[2].Add(ServiceList = Lines(m_ServiceRows, k => ServiceLines[k]));
            Folds[3].Add(StageList = Lines(m_StageRows, StageLine));
            ServiceList.AddToClassList(InspectorStyles.DebuggerList);
            StageList.AddToClassList(InspectorStyles.DebuggerList);
            Folds[3].Add(new Label(StagesNote));
            Folds[3].Add(StoppedLine = new Label(StoppedText) { name = "stopped" });
            BuildEvents();
            root.Add(Horizontal = new Scroller(0f, 0f, Slide, SliderDirection.Horizontal) { name = "horizontal" });
            Horizontal.style.display = DisplayStyle.None;
            m_Tables = new[] { Plays, ItemsTable, Layers, EventsTable };
            foreach (DebuggerTable t in m_Tables)
            {
                t.Reached += Reach;
                t.Wheeled += d => Horizontal.value += d * LineHeight;
            }
            m_Tick = root.schedule.Execute(Refresh).Every(RefreshMs);
            Refresh();
        }

        // the scroller's range: the farthest a table's columns reach past it (a folded one reaches nothing) — hidden when every
        // table fits
        void Reach()
        {
            float reach = 0f, visible = 0f;
            foreach (DebuggerTable t in m_Tables)
            {
                float over = t.Overflow;
                if (over > reach)
                {
                    reach = over;
                    visible = t.Visible;
                }
            }
            Horizontal.highValue = reach;
            Horizontal.style.display = reach > 0.5f ? DisplayStyle.Flex : DisplayStyle.None;
            if (reach > 0.5f)
                Horizontal.Adjust(visible / (visible + reach));
            Slide(Horizontal.value);
        }

        void Slide(float x)
        {
            foreach (DebuggerTable t in m_Tables)
                t.ScrollTo(x);
        }

        static ListView Lines(List<int> rows, Func<int, string> text) => new ListView
        {
            fixedItemHeight = LineHeight,
            virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
            itemsSource = rows,
            makeItem = () => new Label(),
            bindItem = (e, k) => ((Label)e).text = text(k),
        };

        // ---- Playbacks: a play a row. Every comparison ends on the owner's group and the play's number — the engine's own tie
        // is the source row, and a compaction moves the source rows

        PlayRow P(int i) => Reader.Plays[i];

        int Tie(int a, int b)
        {
            PlayRow x = P(a), y = P(b);
            int c = x.Group.CompareTo(y.Group);
            return c != 0 ? c : x.Serial.CompareTo(y.Serial);
        }

        Comparison<int> By(Func<PlayRow, double> key) => (a, b) =>
        {
            int c = key(P(a)).CompareTo(key(P(b)));
            return c != 0 ? c : Tie(a, b);
        };

        double Elapsed(in PlayRow r) => double.IsNaN(r.StartedAt) ? double.NaN : Reader.Now - r.StartedAt;

        int MissedSum(in PlayRow r) => r.Group >= 0 ? Reader.Groups[r.Group].Missed + Reader.Groups[r.Group].MissedQuiet : 0;

        static string CalledFrom(in PlayRow r) => r.Owner is FeedbackPlayer p ? DebuggerCallSites.TopOf(p, r.Handle) : "";

        void BuildPlays()
        {
            Plays = new DebuggerTable("Playbacks", true);
            Plays.Add("Owner", 120, i => DebuggerTable.OwnerText(P(i)), Tie, false);
            Plays.Add("#", 40, i => P(i).Kind == RunnerKind.Player ? P(i).Serial.ToString() : "", By(r => r.Serial));
            Plays.Add("Position", 100, i => DebuggerTable.PositionText(P(i)), By(r => r.Position));
            Plays.Add("Elapsed", 60, i => double.IsNaN(P(i).StartedAt) ? "—" : DebuggerTable.Seconds(Elapsed(P(i))), By(r => double.IsNaN(r.StartedAt) ? double.MinValue : Elapsed(r)));
            Plays.Add("State", 80, i => DebuggerTable.StateText(P(i)), By(r => (r.Reverse ? 1 : 0) + (r.Paused ? 2 : 0) + (r.OffNominal ? 4 : 0)));
            Plays.Add("×", 40, i => P(i).Kind == RunnerKind.Player ? P(i).Intensity.ToString("0.##", CultureInfo.InvariantCulture) : "", By(r => r.Intensity));
            Plays.Add("Seed", 80, i => P(i).Seed.ToString(), By(r => r.Seed));
            Plays.Add("Waits", 60, i => DebuggerTable.WaitsText(P(i)), By(r => r.OpenWaits));
            Plays.Add("Missed", 70, i => P(i).Group >= 0 ? DebuggerTable.MissedText(Reader.Groups[P(i).Group]) : "", By(r => MissedSum(r)));
            Plays.Add("Link", 140, i => DebuggerTable.LinkText(P(i)));
            Plays.Add("Token", 50, i => P(i).Token >= 0 ? P(i).Token.ToString() : "", By(r => r.Token));
            Plays.Add("Called from", 220, i => CalledFrom(P(i)), (a, b) =>
            {
                int c = string.CompareOrdinal(CalledFrom(P(a)), CalledFrom(P(b)));
                return c != 0 ? c : Tie(a, b);
            });
            // the first sort — a saved one replaces it
            Plays.View.sortColumnDescriptions.Add(new SortColumnDescription("Owner", SortDirection.Ascending));
            Plays.View.selectionType = SelectionType.Single;
            // a refresh's own selection events (the sort and the rows move the selected id) are not picks
            Plays.View.selectionChanged += _ =>
            {
                if (!m_Refreshing)
                    Pick(Plays.View.selectedItem);
            };
            Plays.View.itemsChosen += items =>
            {
                foreach (object item in items)
                {
                    Choose(item);
                    break;
                }
            };
            Folds[0].Add(Plays.View);
            Folds[0].Add(SelectedLine = new Label { name = "selected" });
            ItemsTable = new DebuggerTable("Items", false);
            ItemsTable.Add("[#]", 40, k => Reader.Items[k].ListIndex.ToString(), null, false);
            ItemsTable.Add("Item", 140, k => Reader.ItemName(k));
            ItemsTable.Add("Running", 60, k => Reader.Items[k].Running ? "running" : "");
            ItemsTable.Add("Touched", 60, k => Reader.Items[k].Touched ? "touched" : "");
            ItemsTable.Add("Turned", 60, k => Reader.Items[k].Turned ? "turned" : "");
            ItemsTable.Add("Output", 50, k => "×" + Reader.Items[k].Output.ToString("0.##", CultureInfo.InvariantCulture));
            ItemsTable.Add("Note", 160, k => Reader.Items[k].Note ?? "");
            Folds[0].Add(ItemsTable.View);
            Folds[0].Add(Stack = new TextField { multiline = true, isReadOnly = true, name = "stack", verticalScrollerVisibility = ScrollerVisibility.Auto });
            Stack.style.height = StackLines * LineHeight;
            Stack.RegisterCallback<GeometryChangedEvent>(_ => FitStack());
        }

        // the call stack's height: StackLines lines of its own text, measured, and its input's padding and border
        void FitStack()
        {
            TextElement text = Stack.Q<TextElement>();
            VisualElement input = Stack.Q(className: TextField.inputUssClassName);
            if (text == null || input == null)
                return;
            float lines = text.MeasureTextSize(StackMeasure, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).y;
            if (float.IsNaN(lines) || lines <= 0f)
                return;
            IResolvedStyle box = input.resolvedStyle;
            float height = lines + box.paddingTop + box.paddingBottom + box.borderTopWidth + box.borderBottomWidth;
            if (Mathf.Abs(Stack.resolvedStyle.height - height) > 0.5f)
                Stack.style.height = height;
        }

        // StackLines lines of one character — what the area's height is measured on
        internal static readonly string StackMeasure = OneCharacterLines(StackLines);

        static string OneCharacterLines(int n)
        {
            var b = new StringBuilder("0");
            for (int i = 1; i < n; i++)
                b.Append("\n0");
            return b.ToString();
        }

        // a row's play: its handle and owner kept (the same play again changes nothing)
        internal void Pick(object item)
        {
            if (!(item is int row) || row < 0 || row >= Reader.Plays.Count)
                return;
            if (Reader.Plays[row].Handle == Reader.Selected)
                return;
            Reader.Select(row);
            Refresh();
        }

        // a row chosen: its owner selected in the editor
        internal void Choose(object item)
        {
            if (!(item is int row) || row < 0 || row >= Reader.Plays.Count)
                return;
            Object owner = Reader.Plays[row].Owner;
            if (owner != null)
            {
                Selection.activeObject = owner;
                EditorGUIUtility.PingObject(owner);
            }
        }

        // ---- Layers · Events

        void BuildLayers()
        {
            Layers = new DebuggerTable("Layers", true);
            Layers.Add("Target", 120, k => DebuggerTable.TargetText(Reader.Slots[k]), (a, b) => Reader.TargetOrder[a].CompareTo(Reader.TargetOrder[b]), false);
            Layers.Add("Slot", 140, k => DebuggerTable.SlotText(Reader.Slots[k]), (a, b) => Reader.SlotOrder[a].CompareTo(Reader.SlotOrder[b]));
            Layers.Add("Property", 100, k => Reader.Slots[k].Aux != 0 ? DebuggerReader.AuxName(Reader.Slots[k].Aux) : "");
            Layers.Add("Index", 40, k => Reader.Slots[k].Aux2 != 0 ? Reader.Slots[k].Aux2.ToString() : "");
            Layers.Add("Stage", 70, k => Reader.Slots[k].Stage.ToString(), (a, b) => Reader.Slots[a].Stage.CompareTo(Reader.Slots[b].Stage));
            Layers.Add("Writers", 50, k => Reader.Slots[k].LiveContributions.ToString(), (a, b) => Reader.Slots[a].LiveContributions.CompareTo(Reader.Slots[b].LiveContributions));
            Layers.Add("Pending", 50, k => Reader.Slots[k].Pending ? "pending" : "");
            Layers.Add("Direct", 50, k => Reader.Slots[k].Direct ? "direct" : "");
            Layers.Add("Base", 160, k => DebuggerTable.BaseText(Reader.Slots[k]));
            Folds[1].Add(Layers.View);
        }

        void BuildEvents()
        {
            EventsTable = new DebuggerTable("Events", false);
            EventsTable.Add("Frame", 60, k => Events.At(k).Frame.ToString(), null, false);
            EventsTable.Add("Time", 70, k => Events.At(k).Time.ToString("0.000", CultureInfo.InvariantCulture));
            EventsTable.Add("Player", 120, k => DebuggerTable.NameOf(Events.At(k).Player));
            EventsTable.Add("Event", 140, k => Events.At(k).Quiet ? QuietText : Events.At(k).Kind.ToString());
            EventsTable.Add("Handle", 80, k => Events.At(k).Handle.ToString());
            EventsTable.Add("End", 100, k => EndText(Events.At(k)));
            Folds[4].Add(EventsTable.View);
            Folds[4].Add(EventsLine = new Label { name = "events" });
            Folds[4].Add(MissedLine = new Label { name = "missed" });
            Folds[4].Add(new Label(ReloadNote));
            Folds[4].Add(new Button(() =>
            {
                Events.Clear();
                Refresh();
            }) { text = "Clear" });
        }

        // a quiet row's own reason · a Completed or Stopped row's from the end ring
        internal string EndText(in EventEntry e)
        {
            if (e.Quiet)
                return e.End.ToString();
            if (e.Kind != PlayerEventKind.Completed && e.Kind != PlayerEventKind.Stopped)
                return "";
            return Events.EndOf(e.Player, e.Handle, out PlaybackEnd end) ? end.ToString() : "";
        }

        // ---- the refresh

        internal void Refresh()
        {
            m_Refreshing = true;
            try
            {
                FeedbackScheduler scheduler = DebuggerSource.Pick(out Kind);
                Reader.Read(scheduler);
                ServicesOf();
                if (Source == null)
                    return;
                Source.text = scheduler == null ? NoSourceText
                    : Kind + " — " + scheduler.ActiveCount + " plays · " + scheduler.SlotCount + " slots · " + scheduler.ReservedCount + " reserved · " + Reader.Slots.Count + " layer slots";
                Plays.Refresh(Reader.Plays.Count);
                ItemsTable.View.style.display = Reader.Items.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                ItemsTable.Refresh(Reader.Items.Count);
                Layers.Refresh(Reader.Slots.Count);
                Lines(ServiceList, m_ServiceRows, ServiceLines.Count);
                Lines(StageList, m_StageRows, StageCosts.Names.Length);
                EventsTable.Refresh(Events.Count);
                Reselect();
                SelectedLine.text = SelectionText();
                string frames = Reader.SelectedOwner is FeedbackPlayer selected ? DebuggerCallSites.FramesOf(selected, Reader.Selected) : "";
                Stack.SetValueWithoutNotify(frames);
                Stack.style.display = frames.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                EventsLine.text = Events.Count + " events · " + Events.Total + " taken · " + Events.Overwritten + " overwritten";
                MissedLine.text = MissedText();
                bool stopped = Kind == DebuggerSourceKind.Play && Reader.Plays.Count > 0 && Costs.Count(0) > 0 && Costs.Last(0) == 0 && Costs.Last(1) == 0 && Costs.Last(2) == 0;
                m_StoppedRuns = stopped ? m_StoppedRuns + 1 : 0;
                StoppedLine.style.display = m_StoppedRuns >= StoppedRefreshes ? DisplayStyle.Flex : DisplayStyle.None;
            }
            finally
            {
                m_Refreshing = false;
            }
        }

        static void Lines(ListView list, List<int> rows, int n)
        {
            if (rows.Count != n)
            {
                rows.Clear();
                for (int k = 0; k < n; k++)
                    rows.Add(k);
                list.style.height = Mathf.Clamp(n, 1, MaxLines) * LineHeight + ListBorder;
            }
            list.RefreshItems();
        }

        // the table's selection on the held handle's row — none when that play ended (no stale id left)
        void Reselect()
        {
            MultiColumnListView view = Plays.View;
            int row = -1;
            if (Reader.Selected != default)
            {
                for (int i = 0; i < Reader.Plays.Count; i++)
                {
                    if (Reader.Plays[i].Handle == Reader.Selected)
                    {
                        row = i;
                        break;
                    }
                }
            }
            if (row < 0)
            {
                if (view.selectedIndex >= 0 || HasIds(view))
                    view.ClearSelection();
                return;
            }
            int sorted = view.viewController.GetIndexForId(row);
            if (view.selectedIndex != sorted)
            {
                m_One[0] = sorted;
                view.SetSelectionWithoutNotify(m_One);
            }
        }

        static bool HasIds(BaseListView list)
        {
            foreach (int _ in list.selectedIds)
                return true;
            return false;
        }

        // the Services and Stages panels' lines
        internal string Line(int panel, int k) => panel == 2 ? ServiceLines[k] : panel == 3 ? StageLine(k) : "";

        string StageLine(int k)
        {
            int n = Costs.Count(k);
            if (n == 0)
                return StageCosts.Names[k] + " —";
            return StageCosts.Names[k] + " " + (Costs.Last(k) / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " µs · avg " + (Costs.Average(k) / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " µs (" + n + ")";
        }

        string SelectionText()
        {
            if (Reader.SelectedOwner == null)
                return NoSelectionText;
            var b = new StringBuilder("Selected: ").Append(Reader.SelectedOwner.name);
            if (Reader.SelectedEnded)
            {
                b.Append(" — the play ended");
                if (Reader.SelectedOwner is FeedbackPlayer ended && Events.EndOf(ended, Reader.Selected, out PlaybackEnd end))
                    b.Append(" (").Append(end).Append(')');
            }
            if (Reader.SelectedOwner is FeedbackPlayer p)
                b.Append(" · missed signals ").Append(p.MissedSignals).Append(" direct · ").Append(p.MissedQuietSignals).Append(" quiet");
            if (Reader.Tokens.Count == 0)
                b.Append(" · no tokens");
            foreach (TokenRow t in Reader.Tokens)
            {
                b.Append(" · token ").Append(t.Index).Append(t.Used ? " used" : " free");
                if (t.Canceled)
                    b.Append(" canceled");
                if (t.Waited)
                    b.Append(" waited on");
            }
            return b.ToString();
        }

        // the players the ring met and the owners of the plays, once each — those with a missed signal
        string MissedText()
        {
            var b = new StringBuilder("Missed signals (direct / quiet): ");
            int shown = 0;
            foreach (FeedbackPlayer p in Events.Seen)
                shown = Missed(b, p, shown);
            foreach (GroupRow g in Reader.Groups)
            {
                if (g.Owner is FeedbackPlayer p && !Events.Seen.Contains(p))
                    shown = Missed(b, p, shown);
            }
            if (shown == 0)
                b.Append("none");
            if (Events.SeenDropped > 0)
                b.Append(" · +").Append(Events.SeenDropped).Append(" more");
            return b.ToString();
        }

        static int Missed(StringBuilder b, FeedbackPlayer p, int shown)
        {
            if (p == null || p.MissedSignals + p.MissedQuietSignals == 0)
                return shown;
            b.Append(shown > 0 ? " · " : "").Append(p.name).Append(' ').Append(p.MissedSignals).Append(" / ").Append(p.MissedQuietSignals);
            return shown + 1;
        }

        void ServicesOf()
        {
            ServiceLines.Clear();
            CategoryGate gate = Reader.Gate;
            if (gate == null)
                ServiceLines.Add("Categories" + NotMadeText);
            else
            {
                for (int c = 1; c < SettingsValues.CategoryCount; c++)
                {
                    var category = (FeedbackCategory)c;
                    ServiceLines.Add(category + (gate.IsEnabled(category) ? " on" : " off") + " ×" + gate.RawScale(category).ToString("0.##", CultureInfo.InvariantCulture) + " (effective ×" + gate.Scale(category).ToString("0.##", CultureInfo.InvariantCulture) + ")");
                }
                ServiceLines.Add("Flash limit " + gate.FlashLimit + " a second · " + gate.FlashesInLastSecond(Reader.Now) + " in the last second · " + gate.FlashHeld + " held");
            }
            TimeScaleService time = Reader.TimeScale;
            if (time == null)
                ServiceLines.Add("Time scale" + NotMadeText);
            else
            {
                ServiceLines.Add("Time scale ×" + time.Effective.ToString("0.###", CultureInfo.InvariantCulture) + " · base ×" + time.BaseScale.ToString("0.###", CultureInfo.InvariantCulture) + (time.IsOwning ? " · owning Time.timeScale" : ""));
                for (int i = time.Count - 1; i >= 0; i--)
                    ServiceLines.Add("  ×" + time.ScaleAt(i).ToString("0.###", CultureInfo.InvariantCulture) + " for " + time.RemainingAt(i).ToString("0.000", CultureInfo.InvariantCulture) + " s" + (time.FreshAt(i) ? " (new)" : ""));
                // the open request (a Time Scale item) — under the timed ones
                if (time.HasOpen)
                    ServiceLines.Add("  ×" + time.OpenValue.ToString("0.###", CultureInfo.InvariantCulture) + " open" + (time.Count > 0 ? " (under the timed)" : ""));
            }
            if (Reader.ChannelService == null)
                ServiceLines.Add("Channels" + NotMadeText);
            else
            {
                if (Reader.Channels.Count == 0)
                    ServiceLines.Add("Channels — none registered");
                foreach (ChannelRow r in Reader.Channels)
                    ServiceLines.Add("Channel " + (r.Key.Mode == ChannelRefMode.Int ? r.Key.Id.ToString() : DebuggerTable.NameOf(r.Key.Asset)) + " — " + r.Commands + " receivers · " + r.Shakes + " shake receivers · " + r.Signals + " signal receivers");
            }
            // the pools — read without making the service
            if (Reader.Pools == null)
                ServiceLines.Add("Pools" + NotMadeText);
            else
            {
                if (Reader.PoolRows.Count == 0)
                    ServiceLines.Add("Pools — none");
                foreach (PoolRow r in Reader.PoolRows)
                    ServiceLines.Add(r.Pooled ? "Pool " + DebuggerTable.NameOf(r.Prefab) + " — " + r.Live + " live · " + r.Resting + " resting · " + r.Grown + " grown" : "Not pooled — " + r.Live + " live");
            }
            // the extensions made — read without making one
            for (int e = 0; e < Reader.ExtensionNames.Count; e++)
            {
                var line = new StringBuilder(Reader.ExtensionNames[e]);
                List<ServiceRow> rows = Reader.ExtensionRows[e];
                for (int i = 0; i < rows.Count; i++)
                    line.Append(i == 0 ? " — " : " · ").Append(rows[i].Label).Append(' ').Append(rows[i].Value.ToString("0.##", CultureInfo.InvariantCulture));
                ServiceLines.Add(line.ToString());
            }
        }
    }
}
