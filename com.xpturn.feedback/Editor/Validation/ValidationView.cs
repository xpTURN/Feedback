using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // a player inspector's results — held here. A run when drawn and 0.3 s (the editor clock) after the player's edit or a
    // scene change (ObjectChangeEvents — an undo-recorded change, the frame after). The items' slots and panels are the
    // list's rows; the bottom bar's summary opens the player's own
    internal sealed class ValidationView : VisualElement
    {
        internal const double Delay = 0.3;
        internal const string OnItemsText = " on the items — see their headers";

        internal readonly Button Summary;
        internal readonly VisualElement HostPanel = new VisualElement();
        internal List<RuleResult> Results = new List<RuleResult>();
        internal int Runs;
        readonly Object m_Host;
        readonly ItemListView m_List;
        double m_Due = -1;

        // a host without a list (a MotionDriver): every result in the panel, no summary in a bar
        internal ValidationView(Object host, SerializedObject so, ItemListView list, VisualElement bottom)
        {
            m_Host = host;
            m_List = list;
            Summary = new Button(() => ShowHostPanel(HostPanel.style.display == DisplayStyle.None)) { tooltip = "This player's validation" };
            Summary.AddToClassList(InspectorStyles.ValidationSummary);
            Summary.style.display = DisplayStyle.None;
            bottom?.Insert(0, Summary);
            HostPanel.AddToClassList(InspectorStyles.ValidationHost);
            HostPanel.style.display = DisplayStyle.None;
            Add(HostPanel);
            if (list != null)
            {
                list.ResultsFor = ResultsFor;
                list.ApplyFix = (r, f) =>
                {
                    FeedbackValidator.Apply(r, f);
                    Run();
                };
            }
            this.TrackSerializedObjectValue(so, _ => Schedule());
            RegisterCallback<AttachToPanelEvent>(_ => ObjectChangeEvents.changesPublished += OnChanges);
            RegisterCallback<DetachFromPanelEvent>(_ => ObjectChangeEvents.changesPublished -= OnChanges);
            schedule.Execute(Poll).Every(100);
            Run();
        }

        List<RuleResult> ResultsFor(int item)
        {
            var mine = new List<RuleResult>();
            foreach (RuleResult r in Results)
            {
                if (r.Item == item)
                    mine.Add(r);
            }
            return mine;
        }

        void OnChanges(ref ObjectChangeEventStream stream) => Schedule();

        internal void Schedule() => m_Due = PreviewClock.Source() + Delay;

        internal void Poll()
        {
            if (m_Due >= 0 && PreviewClock.Source() >= m_Due)
            {
                m_Due = -1;
                Run();
            }
        }

        internal void Run()
        {
            if (m_Host == null)
                return;
            Runs++;
            Results = FeedbackValidator.Run(m_Host);
            m_List?.RefreshResults();
            Summary.text = FeedbackValidator.Summary(Results);
            Summary.style.display = Results.Count > 0 && m_List != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_List == null)
                ShowHostPanel(Results.Count > 0);
            else if (HostPanel.style.display == DisplayStyle.Flex)
                ShowHostPanel(true);
        }

        internal void ShowHostPanel(bool open)
        {
            HostPanel.Clear();
            HostPanel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open)
                return;
            foreach (RuleResult r in Results.Where(x => x.Item < 0 || m_List == null))
                HostPanel.Add(ResultRows.Row(r, (x, f) =>
                {
                    FeedbackValidator.Apply(x, f);
                    Run();
                }));
            int onItems = m_List == null ? 0 : Results.Count(x => x.Item >= 0);
            if (onItems > 0)
                HostPanel.Add(new Label(onItems + OnItemsText));
        }
    }

    // a result's row — its severity, id, message and fix buttons (the item panel, the player panel, the window)
    internal static class ResultRows
    {
        internal static string IconName(RuleSeverity s) => s == RuleSeverity.Error ? "console.erroricon.sml" : s == RuleSeverity.Warning ? "console.warnicon.sml" : "console.infoicon.sml";

        internal static string SeverityClass(RuleSeverity s) => InspectorStyles.Severity + "--" + s.ToString().ToLowerInvariant();

        internal static Texture2D Icon(RuleSeverity s) => EditorGUIUtility.IconContent(IconName(s)).image as Texture2D;

        internal static VisualElement Row(RuleResult r, Action<RuleResult, RuleFix> apply)
        {
            var row = new VisualElement();
            row.AddToClassList(InspectorStyles.ValidationRow);
            row.AddToClassList(SeverityClass(r.Severity));
            var icon = new Image { image = Icon(r.Severity) };
            icon.AddToClassList(InspectorStyles.ValidationIcon);
            row.Add(icon);
            var text = new Label(r.ToString());
            text.AddToClassList(InspectorStyles.ValidationText);
            row.Add(text);
            foreach (RuleFix f in r.Fixes)
            {
                RuleFix fix = f;
                var b = new Button(() => apply(r, fix)) { text = fix.Label };
                b.AddToClassList(InspectorStyles.ValidationFix);
                row.Add(b);
            }
            return row;
        }
    }
}
