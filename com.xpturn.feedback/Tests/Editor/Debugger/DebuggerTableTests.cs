using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using T = xpTURN.Feedback.Tests.Editor.DebuggerTesting;
using Label = UnityEngine.UIElements.Label;

namespace xpTURN.Feedback.Tests.Editor
{
    // the window's tables (the cells · the columns and the first sort · the selection across a sort and a compaction · a
    // choice · Layers and Events · the saved state) and the call sites (the column's frame · the runtime assemblies)
    internal sealed class DebuggerTableTests
    {
        static FeedbackDebuggerWindow s_Window;

        [TearDown]
        public void TearDown() => T.Clean();

        // a window whose tables start from no saved state
        static IEnumerator Fresh()
        {
            FeedbackDebuggerWindow w = T.Window();
            yield return null;
            T.ClearView(w);
            w.Close();
            yield return null;
            s_Window = T.Window();
            for (int i = 0; i < 3; i++)
                yield return null;
        }

        // the bound rows in display order — each row's cells in column order
        static List<List<string>> Rows(MultiColumnListView view) => view.Q<ScrollView>().contentContainer.Children()
            .Where(r => r.resolvedStyle.display != DisplayStyle.None && r.worldBound.height > 0)
            .OrderBy(r => r.worldBound.y)
            .Select(r => r.Query<Label>().ToList().Select(l => l.text).ToList())
            .ToList();

        static int Column(MultiColumnListView view, string name) => view.columns.ToList().FindIndex(c => c.name == name);

        static PlaybackHandle Selected(FeedbackDebuggerWindow w) => w.Plays.View.selectedItem is int row && row < w.Reader.Plays.Count ? w.Reader.Plays[row].Handle : default;

        static void Select(FeedbackDebuggerWindow w, PlaybackHandle h) => w.Plays.View.SetSelection(w.Plays.View.viewController.GetIndexForId(w.Reader.Plays.FindIndex(r => r.Handle == h)));

        static string Sorts(MultiColumnListView view) => string.Join(",", view.sortedColumns.Select(d => d.columnName + ":" + d.direction));

        // ---- the tables

        // 500 plays: only the visible rows have cells (the header's labels are not cells) · a refresh of as many rows binds the
        // same cells again · a folded panel binds nothing · the fold kept for the next window
        [UnityTest]
        public IEnumerator DT12a_TheTablesReuseTheirCells()
        {
            FeedbackScheduler s = T.NewScheduler();
            for (int i = 0; i < 500; i++)
                T.Player("r" + i, s, T.Tween(5f, T.Self)).Play();
            s.Tick(0.016f);
            DebuggerSource.Override = s;
            for (IEnumerator f = Fresh(); f.MoveNext();)
                yield return f.Current;
            FeedbackDebuggerWindow w = s_Window;
            for (int i = 0; i < 5; i++)
                yield return null;
            VisualElement content = w.Plays.View.Q<ScrollView>().contentContainer;
            List<Label> before = content.Query<Label>().ToList();
            int columns = w.Plays.View.columns.Count;
            Assert.That(before.Count, Is.GreaterThan(0).And.LessThan(500), "only the visible rows have cells");
            Assert.That(before.Count % columns, Is.Zero, "whole rows");
            for (int i = 0; i < 10; i++)
                w.Refresh();
            for (int i = 0; i < 3; i++)
                yield return null;
            List<Label> after = content.Query<Label>().ToList();
            Assert.That(after.Count, Is.EqualTo(before.Count));
            Assert.That(after.Zip(before, ReferenceEquals).All(x => x), Is.True, "the same cells bound again");
            w.Folds[0].value = false;
            yield return null;
            string[] texts = after.Select(l => l.text).ToArray();
            s.Tick(0.3f);
            w.Refresh();
            yield return null;
            Assert.That(after.Select(l => l.text), Is.EqualTo(texts), "a folded panel binds nothing");
            Assert.That(EditorPrefs.GetBool(FeedbackDebuggerWindow.FoldKey + "Playbacks", true), Is.False);
            w.Close();
            yield return null;
            FeedbackDebuggerWindow again = T.Window();
            Assert.That(again.Folds[0].value, Is.False, "the fold kept");
        }

        // two owners' plays met A, B, A: the first sort shows each owner's together, by its plays' numbers · each shown row's
        // number, position and elapsed time are its play's
        [UnityTest]
        public IEnumerator DT12b_ThePlaybacksColumns()
        {
            FeedbackScheduler s = T.NewScheduler();
            s.Tick(0.5f);
            FeedbackPlayer a = T.Player("A", s, T.Tween(5f, T.Self));
            a.m_Policy = PlayPolicy.Overlap;
            FeedbackPlayer b = T.Player("B", s, T.Tween(5f, T.Self));
            PlaybackHandle a1 = a.Play();
            PlaybackHandle b1 = b.Play();
            PlaybackHandle a2 = a.Play();
            s.Tick(0.25f);
            DebuggerSource.Override = s;
            for (IEnumerator f = Fresh(); f.MoveNext();)
                yield return f.Current;
            FeedbackDebuggerWindow w = s_Window;
            w.Refresh();
            for (int i = 0; i < 3; i++)
                yield return null;
            MultiColumnListView view = w.Plays.View;
            Assert.That(view.columns.Select(c => c.name), Is.EqualTo(new[] { "Owner", "#", "Position", "Elapsed", "State", "×", "Seed", "Waits", "Missed", "Link", "Token", "Called from" }));
            Assert.That(Sorts(view), Is.EqualTo("Owner:Ascending"));
            List<List<string>> rows = Rows(view);
            int owner = Column(view, "Owner"), number = Column(view, "#"), position = Column(view, "Position"), elapsed = Column(view, "Elapsed");
            Assert.That(rows.Select(r => r[owner]), Is.EqualTo(new[] { "A", "A", "B" }));
            PlayRow[] shown = new[] { a1, a2, b1 }.Select(h => w.Reader.Plays.Single(r => r.Handle == h)).ToArray();
            for (int k = 0; k < 3; k++)
            {
                Assert.That(rows[k][number], Is.EqualTo(shown[k].Serial.ToString()), "row " + k);
                Assert.That(rows[k][position], Is.EqualTo(DebuggerTable.PositionText(shown[k])), "row " + k);
                Assert.That(rows[k][elapsed], Is.EqualTo("0.25"), "row " + k);
            }
        }

        // three plays sorted by position (X lowest): Y picked, then X stopped — its compaction moves Y's source row and Z takes
        // it, and the sort moves Z, in one refresh — the reader and the table keep Y · the header's sort keeps Y · Y's end: no
        // selection left, the selected line says the end's reason
        [UnityTest]
        public IEnumerator DT12c_TheSelectionFollowsItsPlay()
        {
            FeedbackScheduler s = T.NewScheduler();
            FeedbackPlayer x = T.Player("X", s, T.Tween(5f, T.Self));
            FeedbackPlayer y = T.Player("Y", s, T.Turn(5f, T.Self));
            FeedbackPlayer z = T.Player("Z", s, T.Tween(5f, T.Self));
            x.Play();
            PlaybackHandle hy = y.Play();
            z.Play();
            x.Seek(0.1f);
            y.Seek(0.5f);
            z.Seek(0.9f);
            s.Tick(0.001f);
            DebuggerSource.Override = s;
            for (IEnumerator f = Fresh(); f.MoveNext();)
                yield return f.Current;
            FeedbackDebuggerWindow w = s_Window;
            w.Plays.View.sortColumnDescriptions.Clear();
            w.Plays.View.sortColumnDescriptions.Add(new SortColumnDescription("Position", SortDirection.Descending));
            w.Refresh();
            yield return null;
            Select(w, hy);
            yield return null;
            Assert.That(w.Reader.Selected, Is.EqualTo(hy), "Y picked");
            x.Stop();
            s.Tick(0.001f);
            w.Refresh();
            yield return null;
            Assert.That(w.Reader.Selected, Is.EqualTo(hy), "the reader keeps Y");
            Assert.That(Selected(w), Is.EqualTo(hy), "the table keeps Y");
            Assert.That(Enumerable.Range(0, w.Reader.Items.Count).Select(w.Reader.ItemName), Is.EqualTo(new[] { "RotationItem" }), "Y's items");
            w.Plays.View.sortColumnDescriptions.Clear();
            w.Plays.View.sortColumnDescriptions.Add(new SortColumnDescription("Seed", SortDirection.Ascending));
            yield return null;
            w.Refresh();
            yield return null;
            Assert.That((w.Reader.Selected, Selected(w)), Is.EqualTo((hy, hy)), "Y after the header's sort");
            y.Stop();
            s.Tick(0.001f);
            w.Refresh();
            yield return null;
            Assert.That(w.Plays.View.selectedIds, Is.Empty, "no stale id");
            Assert.That(w.Plays.View.selectedIndex, Is.EqualTo(-1));
            Assert.That(w.SelectedLine.text, Does.Contain("the play ended (Stopped)"));
        }

        // a row chosen (a submit in the rows) selects its owner in the editor
        [UnityTest]
        public IEnumerator DT12d_AChoiceSelectsTheOwner()
        {
            FeedbackScheduler s = T.NewScheduler();
            FeedbackPlayer p = T.Player("P", s, T.Tween(5f, T.Self));
            T.Player("Q", s, T.Tween(5f, T.Self)).Play();
            PlaybackHandle hp = p.Play();
            DebuggerSource.Override = s;
            for (IEnumerator f = Fresh(); f.MoveNext();)
                yield return f.Current;
            FeedbackDebuggerWindow w = s_Window;
            Select(w, hp);
            yield return null;
            VisualElement content = w.Plays.View.Q<ScrollView>().contentContainer;
            using (NavigationSubmitEvent e = NavigationSubmitEvent.GetPooled())
            {
                e.target = content;
                content.SendEvent(e);
            }
            yield return null;
            Assert.That(Selection.activeObject == p || Selection.activeGameObject == p.gameObject, Is.True, "selected " + Selection.activeObject);
            Selection.activeObject = null;
        }

        // Layers: a target's row with its slot · Events: a quiet end's row (no kind — its reason), a Stop's row with its
        // reason in End · the selected play's end in the selected line
        [UnityTest]
        public IEnumerator DT12e_TheLayersAndEventsTables()
        {
            FeedbackScheduler s = T.NewScheduler();
            DebuggerSource.Override = s;
            for (IEnumerator f = Fresh(); f.MoveNext();)
                yield return f.Current;
            FeedbackDebuggerWindow w = s_Window;
            FeedbackPlayer p = T.Player("P", s, T.Tween(5f, T.Self));
            p.Play();
            p.Bind(T.NewScheduler());
            p.Play();
            FeedbackPlayer q = T.Player("Q", s, T.Tween(5f, T.Self));
            PlaybackHandle hq = q.Play();
            s.Tick(0.01f);
            w.Refresh();
            yield return null;
            Select(w, hq);
            yield return null;
            MultiColumnListView layers = w.Layers.View;
            List<List<string>> rows = Rows(layers);
            Assert.That(rows.Any(r => r[Column(layers, "Target")] == "Q" && r[Column(layers, "Slot")].Length > 0), Is.True, "Q's layer row");
            q.Stop();
            w.Refresh();
            yield return null;
            MultiColumnListView events = w.EventsTable.View;
            rows = Rows(events);
            int player = Column(events, "Player"), kind = Column(events, "Event"), end = Column(events, "End");
            Assert.That(rows.Count(r => r[player] == "P" && r[kind] == FeedbackDebuggerWindow.QuietText && r[end] == "Stopped"), Is.EqualTo(1), "P's quiet row");
            Assert.That(rows.Count(r => r[player] == "Q" && r[kind] == "Stopped" && r[end] == "Stopped"), Is.EqualTo(1), "Q's Stopped row");
            Assert.That(rows.Count(r => r[kind] == FeedbackDebuggerWindow.QuietText), Is.EqualTo(1), "one quiet row");
            Assert.That(w.SelectedLine.text, Does.Contain("(Stopped)"));
        }

        // the first sort and all columns shown with no saved state · a sort and a hidden column kept across a close · cleared,
        // the first sort again
        [UnityTest]
        public IEnumerator DT12f_TheTablesRememberTheirState()
        {
            FeedbackScheduler s = T.NewScheduler();
            T.Player("P", s, T.Tween(5f, T.Self)).Play();
            DebuggerSource.Override = s;
            for (IEnumerator f = Fresh(); f.MoveNext();)
                yield return f.Current;
            FeedbackDebuggerWindow w = s_Window;
            Assert.That(Sorts(w.Plays.View), Is.EqualTo("Owner:Ascending"));
            Assert.That(w.Plays.View.columns.All(c => c.visible), Is.True);
            w.Plays.View.sortColumnDescriptions.Clear();
            w.Plays.View.sortColumnDescriptions.Add(new SortColumnDescription("Seed", SortDirection.Descending));
            w.Plays.View.columns["Seed"].visible = false;
            for (int i = 0; i < 3; i++)
                yield return null;
            w.Close();
            yield return null;
            w = T.Window();
            for (int i = 0; i < 3; i++)
                yield return null;
            Assert.That((Sorts(w.Plays.View), w.Plays.View.columns["Seed"].visible), Is.EqualTo(("Seed:Descending", false)), "kept");
            T.ClearView(w);
            w.Close();
            yield return null;
            w = T.Window();
            for (int i = 0; i < 3; i++)
                yield return null;
            Assert.That((Sorts(w.Plays.View), w.Plays.View.columns["Seed"].visible), Is.EqualTo(("Owner:Ascending", true)), "cleared");
        }

        static float Viewport(DebuggerTable t) => t.View.Q<ScrollView>().contentViewport.layout.height;

        // the rows fit under the header as laid out: the empty table's line, three rows, MaxLines of twenty · every list has a
        // line above and below and a space after, and its lines fit · a wide window shows no horizontal scroller · a narrow one
        // shows the window's one (no table's own) — moved to its end, each table goes as far as its columns reach past it, its
        // header with it — and the rows still fit
        [UnityTest]
        public IEnumerator DT12g_TheRowsFitAndOneScrollerMovesTheColumns()
        {
            FeedbackScheduler s = T.NewScheduler();
            DebuggerSource.Override = s;
            for (IEnumerator f = Fresh(); f.MoveNext();)
                yield return f.Current;
            FeedbackDebuggerWindow w = s_Window;
            w.position = new Rect(0, 0, 2000, 900);
            for (int i = 0; i < 5; i++)
                yield return null;
            Assert.That(Viewport(w.Plays), Is.GreaterThanOrEqualTo(FeedbackDebuggerWindow.LineHeight), "the empty table's line");
            for (int i = 0; i < 3; i++)
                T.Player("p" + i, s, T.Tween(5f, T.Self)).Play();
            w.Refresh();
            for (int i = 0; i < 5; i++)
                yield return null;
            Assert.That(Viewport(w.Plays), Is.GreaterThanOrEqualTo(3 * FeedbackDebuggerWindow.LineHeight), "three rows");
            Assert.That(Viewport(w.Layers), Is.GreaterThanOrEqualTo(3 * FeedbackDebuggerWindow.LineHeight), "three layer rows");
            Assert.That(w.Horizontal.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "a wide window: no scroller");
            foreach (VisualElement list in new VisualElement[] { w.Plays.View, w.ItemsTable.View, w.Layers.View, w.EventsTable.View, w.ServiceList, w.StageList })
                Assert.That(list.ClassListContains(InspectorStyles.DebuggerList), Is.True, list.name);
            IResolvedStyle look = w.Plays.View.resolvedStyle;
            Assert.That((look.borderTopWidth, look.borderBottomWidth, look.marginBottom), Is.EqualTo((1f, 1f, 8f)), "a line above and below, a space after");
            Assert.That(w.ServiceList.Q<ScrollView>().contentViewport.layout.height, Is.GreaterThanOrEqualTo(Mathf.Min(w.ServiceLines.Count, FeedbackDebuggerWindow.MaxLines) * FeedbackDebuggerWindow.LineHeight), "the service lines fit");
            for (int i = 3; i < 20; i++)
                T.Player("p" + i, s, T.Tween(5f, T.Self)).Play();
            w.Refresh();
            for (int i = 0; i < 5; i++)
                yield return null;
            Assert.That(Viewport(w.Plays), Is.GreaterThanOrEqualTo(FeedbackDebuggerWindow.MaxLines * FeedbackDebuggerWindow.LineHeight), "MaxLines rows");
            w.position = new Rect(0, 0, 400, 900);
            for (int i = 0; i < 8; i++)
                yield return null;
            Assert.That(w.Horizontal.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "a narrow window's scroller");
            Assert.That(new[] { w.Plays, w.Layers, w.EventsTable }.Select(t => t.Scroll.horizontalScroller.resolvedStyle.display), Is.All.EqualTo(DisplayStyle.None), "no table's own");
            Assert.That(Viewport(w.Plays), Is.GreaterThanOrEqualTo(FeedbackDebuggerWindow.MaxLines * FeedbackDebuggerWindow.LineHeight), "MaxLines rows in a narrow window");
            VisualElement columns = w.Plays.View.Q(className: DebuggerTable.HeaderClass + "__column-container");
            float header = columns.worldBound.x;
            Assert.That(w.Plays.Overflow, Is.GreaterThan(w.Layers.Overflow), "Playbacks reaches farther than Layers");
            w.Horizontal.value = w.Horizontal.highValue;
            for (int i = 0; i < 3; i++)
                yield return null;
            Assert.That(w.Horizontal.highValue, Is.EqualTo(w.Plays.Overflow).Within(1f), "the range is the farthest reach");
            Assert.That(w.Plays.Scroll.scrollOffset.x, Is.EqualTo(w.Plays.Overflow).Within(1f), "Playbacks at its end");
            Assert.That(w.Layers.Scroll.scrollOffset.x, Is.EqualTo(w.Layers.Overflow).Within(1f), "Layers as far as it reaches");
            Assert.That(header - columns.worldBound.x, Is.EqualTo(w.Plays.Scroll.scrollOffset.x).Within(1f), "the header followed");
        }

        // ---- the call sites

        [MethodImpl(MethodImplOptions.NoInlining)]
        static PlaybackHandle PlayHere(FeedbackPlayer p) => p.Play();

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void SendHere(FeedbackScheduler s, int channel) => s.Context.Channels.Send(FeedbackCommand.Play(ChannelRef.Of(channel)), false, default);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void InvokeHere(UnityEvent e) => e.Invoke();

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void PlayParent(FeedbackPlayer p) => p.Play();

        static PlaybackHandle HandleOf(FeedbackScheduler s, FeedbackPlayer p)
        {
            var reader = new DebuggerReader();
            reader.Read(s);
            return reader.Plays.First(r => ReferenceEquals(r.Owner, p)).Handle;
        }

        // the switch on: a direct Play, a channel's, a UnityEvent's name the method that called · its kept frames start at the
        // diagnostics (no debugger frame), sixteen at most · a nested play begun with its parent names the parent's caller, one
        // begun later in a pass names its binding · the switch off keeps nothing
        [Test]
        public void DT13a_CallSites()
        {
            T.Window();
            DebuggerCallSites.Enabled = true;
            DebuggerCallSites.Clear();
            FeedbackScheduler s = T.NewScheduler();
            FeedbackPlayer p = T.Player("p", s, T.Tween(5f, T.Self));
            PlaybackHandle hp = PlayHere(p);
            Assert.That(DebuggerCallSites.TopOf(p, hp), Does.StartWith(nameof(DebuggerTableTests) + "." + nameof(PlayHere) + " ("), "a direct Play");
            Assert.That(DebuggerCallSites.FramesOf(p, hp), Does.StartWith("FeedbackDiagnostics.Raise"), "the first kept frame");
            Assert.That(DebuggerCallSites.FrameCount(p, hp), Is.InRange(1, DebuggerCallSites.Depth));
            FeedbackPlayer c = T.Player("c", s, T.Tween(5f, T.Self));
            var rx = T.Go("rx").AddComponent<FeedbackReceiver>();
            rx.m_Channel = ChannelRef.Of(5);
            rx.m_Player = c;
            rx.Bind(s);
            SendHere(s, 5);
            Assert.That(DebuggerCallSites.TopOf(c, HandleOf(s, c)), Does.StartWith(nameof(DebuggerTableTests) + "." + nameof(SendHere) + " ("), "a channel's");
            FeedbackPlayer u = T.Player("u", s, T.Tween(5f, T.Self));
            var ue = new UnityEvent();
            ue.AddListener(u.PlayFromEvent);
            InvokeHere(ue);
            Assert.That(DebuggerCallSites.TopOf(u, HandleOf(s, u)), Does.StartWith(nameof(DebuggerTableTests) + "." + nameof(InvokeHere) + " ("), "a UnityEvent's");
            FeedbackPlayer early = T.Player("early", s, T.Tween(5f, T.Self));
            PlayParent(T.Player("parent0", s, new PlayPlayerItem { m_Player = early }));
            Assert.That(DebuggerCallSites.TopOf(early, HandleOf(s, early)), Does.StartWith(nameof(DebuggerTableTests) + "." + nameof(PlayParent) + " ("), "nested with its parent");
            FeedbackPlayer late = T.Player("late", s, T.Tween(5f, T.Self));
            var delayed = new PlayPlayerItem { m_Player = late };
            delayed.m_Timing.m_Delay = 0.05f;
            T.Player("parent", s, delayed).Play();
            s.Tick(0.1f);
            Assert.That(DebuggerCallSites.TopOf(late, HandleOf(s, late)), Does.StartWith("NestedPlayBinding."), "nested in a pass");
            DebuggerCallSites.Enabled = false;
            DebuggerCallSites.Clear();
            T.Player("off", s, T.Tween(5f, T.Self)).Play();
            Assert.That(DebuggerCallSites.Count, Is.Zero, "the switch off");
        }

        // the selected play's kept frames in an area StackLines lines high (at least StackLines of its text, fewer than one more)
        // that scrolls the rest
        [UnityTest]
        public IEnumerator DT13c_TheCallStackArea()
        {
            DebuggerCallSites.Enabled = true;
            FeedbackScheduler s = T.NewScheduler();
            DebuggerSource.Override = s;
            for (IEnumerator f = Fresh(); f.MoveNext();)
                yield return f.Current;
            FeedbackDebuggerWindow w = s_Window;
            PlaybackHandle h = PlayHere(T.Player("p", s, T.Tween(5f, T.Self)));
            w.Refresh();
            yield return null;
            Select(w, h);
            for (int i = 0; i < 5; i++)
                yield return null;
            TextField stack = w.Stack;
            Assert.That(stack.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "shown");
            Assert.That(stack.value.Split('\n').Length, Is.GreaterThan(FeedbackDebuggerWindow.StackLines), "more frames than the area's lines");
            TextElement text = stack.Q<TextElement>();
            float Measure(int n) => text.MeasureTextSize(string.Join("\n", Enumerable.Repeat("0", n)), 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).y;
            ScrollView area = stack.Q<ScrollView>();
            float shown = area.contentViewport.layout.height;
            Assert.That(shown, Is.GreaterThanOrEqualTo(Measure(FeedbackDebuggerWindow.StackLines) - 1f), "StackLines lines");
            Assert.That(shown, Is.LessThan(Measure(FeedbackDebuggerWindow.StackLines + 1)), "fewer than one more");
            Assert.That(area.verticalScroller.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "the rest scrolled");
        }

        [Serializable]
        sealed class AsmdefName
        {
            public string name;
        }

        // the runtime assemblies the rule skips: those of the package's Runtime folder — the asmdef files there, all compiled in
        // the development project
        [Test, Category(OptionalAssemblyTests.DevProjectCategory)]
        public void DT13b_TheRuntimeAssemblies()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(FeedbackPlayer).Assembly);
            string[] files = Directory.GetFiles(Path.Combine(info.resolvedPath, "Runtime"), "*.asmdef", SearchOption.AllDirectories);
            IEnumerable<string> names = files.Select(f => JsonUtility.FromJson<AsmdefName>(File.ReadAllText(f)).name);
            Assert.That(DebuggerCallSites.RuntimeNames(), Is.EquivalentTo(names));
        }
    }
}
