using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // the total and the strip — a row per program item: the delay (thin), the first run (the category's colour — dimmed when
    // the item is off), the repeats (faint), a loop block behind its members (×runs), an endless end (an arrow). Dragging a
    // bar's body moves the delay, its right edge the duration (0.01 s steps — Shift for none), one undo per drag. The heads
    // are the live plays (every 0.1 s); hidden in play mode while an edit waits for a Rebuild. A list without a host (a
    // preset) has no heads, multiplier 1 and no start delay. In play mode the header has the auto Rebuild switch
    internal sealed class TimelineStrip : VisualElement
    {
        internal const float RowHeight = 6f;
        internal const float Pad = 4f;
        internal const float EdgeGrab = 4f;
        internal const string AutoRebuildText = "Auto Rebuild";

        readonly SerializedObject m_So;
        readonly string m_Path;
        readonly FeedbackPlayer m_Host;
        readonly List<TimelineModel.Head> m_Heads = new List<TimelineModel.Head>();
        readonly List<Label> m_BandLabels = new List<Label>();
        internal readonly Label Total = new Label();
        internal readonly VisualElement Canvas = new VisualElement();
        internal readonly Toggle Auto;
        internal TimelineModel Model { get; private set; }
        internal bool HeadsShown { get; private set; }
        internal IReadOnlyList<TimelineModel.Head> Heads => m_Heads;
        StripDrag m_Drag;
        float m_DragX;

        readonly bool m_ReadOnly;
        readonly SerializedObject m_HostSo;

        internal TimelineStrip(SerializedObject so, string listPath, FeedbackPlayer host, bool readOnly = false, SerializedObject settings = null)
        {
            m_ReadOnly = readOnly;
            // a linked player's strip: the list is the preset's, the multiplier and the delay the player's — the player's SerializedObject
            m_HostSo = host != null && so.targetObject != host ? (settings ?? new SerializedObject(host)) : null;
            m_So = so;
            m_Path = listPath;
            m_Host = host;
            AddToClassList(InspectorStyles.Strip);
            var header = new VisualElement();
            header.AddToClassList(InspectorStyles.Row);
            Total.AddToClassList(InspectorStyles.Grow);
            header.Add(Total);
            if (host != null && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Auto = new Toggle(AutoRebuildText) { value = PlayModeEdits.AutoRebuild, tooltip = "Rebuild 0.15 s after an edit that waits for one — a running play is cut" };
                Auto.RegisterValueChangedCallback(e => PlayModeEdits.AutoRebuild = e.newValue);
                header.Add(Auto);
            }
            Add(header);
            Canvas.AddToClassList(InspectorStyles.StripCanvas);
            Canvas.generateVisualContent += Draw;
            Canvas.RegisterCallback<PointerDownEvent>(OnPointerDown);
            Canvas.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            Canvas.RegisterCallback<PointerUpEvent>(OnPointerUp);
            Canvas.RegisterCallback<GeometryChangedEvent>(_ => PlaceBandLabels());
            Add(Canvas);
            Rebuild();
            this.TrackSerializedObjectValue(so, _ => Rebuild());
            // one element tracks one SerializedObject (a second throws and is dropped): the settings' watcher is a child
            if (m_HostSo != null)
            {
                var watch = new VisualElement { pickingMode = PickingMode.Ignore };
                watch.TrackSerializedObjectValue(m_HostSo, _ => Rebuild());
                Add(watch);
            }
            if (host != null)
                schedule.Execute(RefreshHeads).Every(100);
        }

        SerializedObject Settings => m_HostSo ?? m_So;
        float Multiplier => Settings.FindProperty("m_DurationMultiplier")?.floatValue ?? 1f;
        float StartDelay => Settings.FindProperty("m_StartDelay")?.floatValue ?? 0f;

        internal void Rebuild()
        {
            if (m_So.targetObject == null)
                return;
            m_So.Update();
            m_HostSo?.Update();
            Model = TimelineModel.Build(TimelineModel.Items(m_So, m_Path), Multiplier, StartDelay);
            Span = SpanOf(Model);
            Total.text = TotalText(Model);
            Canvas.style.height = Pad * 2f + Math.Max(1, Model.Bars.Count) * RowHeight;
            foreach (Label l in m_BandLabels)
                l.RemoveFromHierarchy();
            m_BandLabels.Clear();
            foreach (TimelineModel.Band band in Model.Bands)
            {
                var label = new Label(band.Runs > 0 ? "×" + band.Runs : "×∞") { pickingMode = PickingMode.Ignore };
                label.AddToClassList(InspectorStyles.StripBand);
                m_BandLabels.Add(label);
                Canvas.Add(label);
            }
            PlaceBandLabels();
            RefreshHeads();
            Canvas.MarkDirtyRepaint();
        }

        internal static string TotalText(TimelineModel model)
        {
            string total = double.IsInfinity(model.Total) ? "Total ∞" : "Total " + model.Total.ToString("0.###") + " s";
            return model.Seekable ? total : total + " · no seek";
        }

        internal void RefreshHeads()
        {
            HeadsShown = TimelineModel.HeadsShown(m_Host);
            int before = m_Heads.Count;
            TimelineModel.Heads(HeadsShown ? m_Host : null, m_Heads);
            if (before > 0 || m_Heads.Count > 0)
                Canvas.MarkDirtyRepaint();
        }

        // the axis — the finite ends, an endless bar's arrow room after them
        internal double Span { get; private set; } = 1;

        static double SpanOf(TimelineModel model)
        {
            double span = 0;
            bool endless = false;
            foreach (TimelineModel.Bar b in model.Bars)
            {
                endless |= double.IsInfinity(b.End);
                span = Math.Max(span, Finite(b.Start));
                span = Math.Max(span, Finite(b.RunEnd));
                span = Math.Max(span, Finite(b.End));
            }
            if (span <= 0)
                span = 1;
            return endless ? span * 1.15 : span;
        }

        static double Finite(double t) => double.IsInfinity(t) ? 0 : t;

        internal float X(double seconds)
        {
            float width = Math.Max(1f, Canvas.contentRect.width - Pad * 2f);
            if (double.IsInfinity(seconds))
                return Pad + width;
            return Pad + (float)(seconds / Span) * width;
        }

        internal float Y(int row) => Pad + row * RowHeight;

        void PlaceBandLabels()
        {
            if (Model == null)
                return;
            for (int i = 0; i < m_BandLabels.Count && i < Model.Bands.Count; i++)
            {
                m_BandLabels[i].style.left = X(Model.Bands[i].Start) + 1f;
                m_BandLabels[i].style.top = 0f;
            }
        }

        void Draw(MeshGenerationContext context)
        {
            if (Model == null)
                return;
            Painter2D p = context.painter2D;
            float height = Canvas.contentRect.height;
            foreach (TimelineModel.Band band in Model.Bands)
                Box(p, X(band.Start), 0f, X(band.End), height, new Color(1f, 1f, 1f, 0.06f));
            for (int i = 0; i < Model.Bars.Count; i++)
            {
                TimelineModel.Bar b = Model.Bars[i];
                Color c = b.Flow ? new Color(0.6f, 0.6f, 0.6f) : InspectorStyles.CategoryColor(b.Category);
                if (b.Off)
                    c.a = 0.35f;
                float y = Y(i);
                float top = b.Background ? y + RowHeight * 0.5f : y + 1f;
                float bottom = y + RowHeight - 1f;
                if (b.DelayStart < b.Start)
                    Box(p, X(b.DelayStart), y + RowHeight * 0.5f - 0.5f, X(b.Start), y + RowHeight * 0.5f + 0.5f, new Color(c.r, c.g, c.b, c.a * 0.5f));
                float x0 = X(b.Start);
                Box(p, x0, top, Math.Max(x0 + 1.5f, X(b.RunEnd)), bottom, c);
                if (b.End > b.RunEnd)
                    Box(p, X(b.RunEnd), top, X(b.End), bottom, new Color(c.r, c.g, c.b, c.a * 0.4f));
                if (double.IsInfinity(b.End))
                    Arrow(p, X(b.End), top, bottom, c);
            }
            if (!HeadsShown)
                return;
            foreach (TimelineModel.Head h in m_Heads)
            {
                float x = X(h.Seconds);
                Box(p, x - 0.5f, 0f, x + 0.5f, height, h.OffNominal ? new Color(1f, 1f, 1f, 0.35f) : Color.white);
            }
        }

        static void Box(Painter2D p, float x0, float y0, float x1, float y1, Color c)
        {
            if (x1 <= x0 || y1 <= y0)
                return;
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, y0));
            p.LineTo(new Vector2(x1, y1));
            p.LineTo(new Vector2(x0, y1));
            p.ClosePath();
            p.Fill();
        }

        static void Arrow(Painter2D p, float x, float y0, float y1, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x - 4f, y0 - 1f));
            p.LineTo(new Vector2(x, (y0 + y1) * 0.5f));
            p.LineTo(new Vector2(x - 4f, y1 + 1f));
            p.ClosePath();
            p.Fill();
        }

        // the bar and the part under a point — the right edge (the duration) first, then the body (the delay)
        internal bool Hit(Vector2 local, out int bar, out bool edge)
        {
            bar = -1;
            edge = false;
            if (Model == null)
                return false;
            int row = (int)Math.Floor((local.y - Pad) / RowHeight);
            if (row < 0 || row >= Model.Bars.Count)
                return false;
            TimelineModel.Bar b = Model.Bars[row];
            bar = row;
            if (b.DurationEditable && !double.IsInfinity(b.RunEnd) && Math.Abs(local.x - X(b.RunEnd)) <= EdgeGrab)
            {
                edge = true;
                return true;
            }
            return b.DelayEditable && local.x >= X(b.DelayStart) - EdgeGrab && local.x <= X(b.RunEnd);
        }

        void OnPointerDown(PointerDownEvent e)
        {
            if (m_ReadOnly || e.button != 0 || !Hit(e.localPosition, out int bar, out bool edge))
                return;
            m_Drag = StripDrag.Begin(m_So, m_Path, Model.Bars[bar].ListIndex, edge, Model.Factor);
            m_DragX = e.localPosition.x;
            Canvas.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (m_Drag == null)
                return;
            float width = Math.Max(1f, Canvas.contentRect.width - Pad * 2f);
            m_Drag.Move((e.localPosition.x - m_DragX) / width * Span, e.shiftKey);
            e.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (m_Drag == null)
                return;
            m_Drag.End();
            m_Drag = null;
            Canvas.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }
    }

    // a drag of a bar: the body is the delay, the right edge the duration — the item's value moves by the axis's seconds over
    // the multiplier, in 0.01 s steps (free: none), never below 0. Each move writes; the drag is one undo group
    internal sealed class StripDrag
    {
        internal const string UndoName = "Drag Feedback Timing";

        readonly SerializedObject m_So;
        readonly string m_Property;
        readonly float m_From;
        readonly float m_Factor;
        readonly int m_Group;

        StripDrag(SerializedObject so, string property, float from, float factor, int group)
        {
            m_So = so;
            m_Property = property;
            m_From = from;
            m_Factor = factor;
            m_Group = group;
        }

        internal string Property => m_Property;

        internal static StripDrag Begin(SerializedObject so, string listPath, int listIndex, bool edge, float factor)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            int group = Undo.GetCurrentGroup();
            so.Update();
            string property = listPath + ".Array.data[" + listIndex + "]." + (edge ? ItemRules.Duration : ItemRules.Delay);
            return new StripDrag(so, property, so.FindProperty(property).floatValue, factor > 0f ? factor : 1f, group);
        }

        internal static float Snap(float seconds, bool free) => Mathf.Max(0f, free ? seconds : Mathf.Round(seconds * 100f) / 100f);

        // the axis's seconds since the drag began
        internal void Move(double axisSeconds, bool free)
        {
            m_So.Update();
            SerializedProperty p = m_So.FindProperty(m_Property);
            if (p == null)
                return;
            p.floatValue = Snap(m_From + (float)(axisSeconds / m_Factor), free);
            m_So.ApplyModifiedProperties();
        }

        internal void End() => Undo.CollapseUndoOperations(m_Group);
    }
}
