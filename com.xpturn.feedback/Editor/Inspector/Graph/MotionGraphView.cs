using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace xpTURN.Feedback.Editor
{
    // a model's columns drawn: the delay shaded, the zero line, the occurrences' starts, the random output's band, each shown
    // component as its column's least-to-most stroke joined to the next column, and the live plays' heads. An offset's axis is
    // symmetric about 0, a weight's and a toggle's from 0 to at least 1
    internal sealed class MotionGraphView : VisualElement
    {
        // x · y · z · w — the curve editor's colors
        internal static readonly Color[] ComponentColors =
        {
            new Color(0.93f, 0.35f, 0.33f), new Color(0.47f, 0.8f, 0.33f), new Color(0.36f, 0.6f, 1f), new Color(0.7f, 0.7f, 0.7f),
        };
        internal static readonly string[] ComponentNames = { "X", "Y", "Z", "W" };

        internal ItemGraphModel Model;
        internal readonly bool[] Shown = { true, true, true, true };
        internal readonly List<TimelineModel.Head> Heads = new List<TimelineModel.Head>();
        // the vertical range in the values' unit, at the top left
        internal readonly Label Scale;
        // other models' lines under the main one (the slot graph's items alone) — a strong one nearly as dark
        internal readonly List<ItemGraphModel> Faint = new List<ItemGraphModel>();
        internal readonly List<bool> Strong = new List<bool>();
        // the components' names — the slot graph's size is |v|
        internal string[] Names = ComponentNames;
        // a dashed line at this value of the fourth component (the capped group's limit) — NaN: none
        internal float Cap = float.NaN;
        // a press and its drag: the seconds under the pointer
        internal Action<double> Scrubbed;
        // the record's frames over the lines — one off the nominal axis faint
        internal RecordRing Dots;
        // the editor's choice for every graph, off at first — the record and its comparison go on without the dots
        internal const string DotsKey = "xpTURN.Feedback.Inspector.Graph.Dots";
        float m_Lo;
        float m_Hi;

        internal MotionGraphView()
        {
            AddToClassList(InspectorStyles.GraphCanvas);
            generateVisualContent += Draw;
            RegisterCallback<PointerMoveEvent>(e => tooltip = TooltipAt(e.localPosition.x));
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnDrag);
            RegisterCallback<PointerUpEvent>(OnUp);
            Scale = new Label { pickingMode = PickingMode.Ignore };
            Scale.AddToClassList(InspectorStyles.GraphScale);
            Add(Scale);
        }

        internal double SecondsAt(float localX)
        {
            Rect r = contentRect;
            double u = Mathf.Clamp01((localX - r.x) / Math.Max(1f, r.width));
            return Model.From + (Model.To - Model.From) * u;
        }

        void OnDown(PointerDownEvent e)
        {
            if (e.button != 0 || Scrubbed == null || !Drawable)
                return;
            this.CapturePointer(e.pointerId);
            Scrubbed(SecondsAt(e.localPosition.x));
            e.StopPropagation();
        }

        void OnDrag(PointerMoveEvent e)
        {
            if (Scrubbed == null || !Drawable || !this.HasPointerCapture(e.pointerId))
                return;
            Scrubbed(SecondsAt(e.localPosition.x));
        }

        void OnUp(PointerUpEvent e)
        {
            if (this.HasPointerCapture(e.pointerId))
                this.ReleasePointer(e.pointerId);
        }

        // a new model or other shown components: the range's words and the drawing
        internal void Refresh()
        {
            Scale.text = ScaleText();
            MarkDirtyRepaint();
        }

        // an offset's ± its largest, a weight's or a toggle's top, else least ~ most — none without values
        internal string ScaleText()
        {
            if (!Drawable)
                return "";
            Model.Range(Shown, out float lo, out float hi);
            if (float.IsInfinity(lo) || float.IsInfinity(hi))
                return "";
            if (Model.Toggle || Model.Weight)
                return Number(Math.Max(1f, hi));
            if (Model.Write == MotionWrite.Offset)
                return "±" + Number(Math.Max(Math.Abs(lo), Math.Abs(hi)));
            return Number(lo) + " ~ " + Number(hi);
        }

        static string Number(float v) => v != 0f && Math.Abs(v) < 0.001f ? v.ToString("G2", CultureInfo.InvariantCulture) : N(v);

        bool Drawable => Model != null && Model.Columns > 0 && Model.Min != null && Model.To > Model.From;

        internal static bool DotsOn
        {
            get => EditorPrefs.GetBool(DotsKey, false);
            set => EditorPrefs.SetBool(DotsKey, value);
        }

        // the dots are drawn, bound the range and answer the pointer
        internal bool DotsShown => Dots != null && DotsOn;

        // a header's Dots: the choice for every graph — this view drawn again at once, the other open ones at their poll
        internal Toggle NewDotsToggle()
        {
            var toggle = new Toggle { text = "Dots", value = DotsOn, tooltip = "The followed play's frames over the line — the comparison and its warning go on without them" };
            toggle.AddToClassList(InspectorStyles.GraphComponent);
            toggle.RegisterValueChangedCallback(e =>
            {
                DotsOn = e.newValue;
                MarkDirtyRepaint();
            });
            return toggle;
        }

        internal float X(double seconds)
        {
            Rect r = contentRect;
            return r.x + (float)((seconds - Model.From) / (Model.To - Model.From)) * r.width;
        }

        float Y(float v)
        {
            Rect r = contentRect;
            return r.yMax - 2f - (v - m_Lo) / (m_Hi - m_Lo) * (r.height - 4f);
        }

        // the vertical range of the shown components
        internal void Bounds(out float lo, out float hi)
        {
            Model.Range(Shown, out lo, out hi);
            foreach (ItemGraphModel f in Faint)
            {
                if (f == null || f.Min == null)
                    continue;
                f.Range(Shown, out float flo, out float fhi);
                if (!float.IsInfinity(flo))
                {
                    lo = Math.Min(lo, flo);
                    hi = Math.Max(hi, fhi);
                }
            }
            if (DotsShown)
            {
                for (int i = 0; i < Dots.Count; i++)
                {
                    Vector4 v = Dots.Value(i);
                    for (int d = 0; d < Model.Dimension; d++)
                    {
                        if (!Shown[d])
                            continue;
                        lo = Math.Min(lo, v[d]);
                        hi = Math.Max(hi, v[d]);
                    }
                }
            }
            if (float.IsInfinity(lo) || float.IsInfinity(hi))
            {
                lo = -1f;
                hi = 1f;
            }
            if (Model.Toggle || Model.Weight)
            {
                lo = Math.Min(0f, lo);
                hi = Math.Max(1f, hi);
            }
            else if (Model.Write == MotionWrite.Offset)
            {
                float m = Math.Max(1e-6f, Math.Max(Math.Abs(lo), Math.Abs(hi)));
                if (!float.IsNaN(Cap) && Model.Dimension == 4 && Shown[3])
                    m = Math.Max(m, Cap);
                lo = -m;
                hi = m;
            }
            else
            {
                float pad = Math.Max(1e-4f, (hi - lo) * 0.08f);
                lo -= pad;
                hi += pad;
            }
        }

        void Draw(MeshGenerationContext context)
        {
            if (!Drawable)
                return;
            Rect r = contentRect;
            if (r.width <= 0f || r.height <= 0f)
                return;
            Bounds(out m_Lo, out m_Hi);
            Painter2D p = context.painter2D;
            Color ink = resolvedStyle.color;
            int columns = Model.Columns;
            float step = r.width / columns;
            // the delay
            if (Model.Start > Model.From)
                Box(p, X(Model.From), r.y, X(Model.Start), r.yMax, new Color(ink.r, ink.g, ink.b, 0.06f));
            // the zero line · the occurrences' starts
            p.lineWidth = 1f;
            p.strokeColor = new Color(ink.r, ink.g, ink.b, 0.3f);
            p.BeginPath();
            float zero = Y(0f);
            if (zero >= r.y && zero <= r.yMax)
            {
                p.MoveTo(new Vector2(r.x, zero));
                p.LineTo(new Vector2(r.xMax, zero));
            }
            foreach (double s in Model.Starts)
            {
                float x = X(s);
                if (x <= r.x + 0.5f || x > r.xMax)
                    continue;
                p.MoveTo(new Vector2(x, r.y));
                p.LineTo(new Vector2(x, r.yMax));
            }
            p.Stroke();
            for (int i = 0; i < Faint.Count; i++)
            {
                ItemGraphModel f = Faint[i];
                if (f == null || f.Min == null || f.Columns != columns)
                    continue;
                bool strong = i < Strong.Count && Strong[i];
                for (int d = 0; d < f.Dimension; d++)
                {
                    if (!Shown[d])
                        continue;
                    Color color = f.Dimension == 1 ? ink : ComponentColors[d];
                    Lines(p, f, d, new Color(color.r, color.g, color.b, strong ? 0.85f : 0.28f), strong ? 1.5f : 1f, r, step);
                }
            }
            for (int d = 0; d < Model.Dimension; d++)
            {
                if (!Shown[d])
                    continue;
                Color color = Model.Dimension == 1 ? ink : ComponentColors[d];
                if (Model.Band)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        if (!Model.HasBand[c])
                            continue;
                        int at = c * 4 + d;
                        Box(p, r.x + c * step, Y(Model.BandMax[at]), r.x + (c + 1) * step, Y(Model.BandMin[at]), new Color(color.r, color.g, color.b, 0.15f));
                    }
                }
                Lines(p, Model, d, color, Faint.Count > 0 ? 2f : 1.5f, r, step);
            }
            if (!float.IsNaN(Cap) && Model.Dimension == 4 && Shown[3])
            {
                float y = Y(Cap);
                p.strokeColor = ComponentColors[3];
                p.lineWidth = 1f;
                p.BeginPath();
                for (float x = r.x; x < r.xMax; x += 6f)
                {
                    p.MoveTo(new Vector2(x, y));
                    p.LineTo(new Vector2(Math.Min(r.xMax, x + 3f), y));
                }
                p.Stroke();
            }
            if (DotsShown)
            {
                for (int i = 0; i < Dots.Count; i++)
                {
                    double seconds = Dots.Seconds(i);
                    if (seconds < Model.From || seconds > Model.To)
                        continue;
                    float x = X(seconds);
                    Vector4 v = Dots.Value(i);
                    float alpha = Dots.Off(i) ? 0.35f : 1f;
                    for (int d = 0; d < Model.Dimension; d++)
                    {
                        if (!Shown[d])
                            continue;
                        float y = Y(v[d]);
                        if (y < r.y - 1f || y > r.yMax + 1f)
                            continue;
                        Color color = Model.Dimension == 1 ? ink : ComponentColors[d];
                        Box(p, x - 1.5f, y - 1.5f, x + 1.5f, y + 1.5f, new Color(color.r, color.g, color.b, alpha));
                    }
                }
            }
            foreach (TimelineModel.Head h in Heads)
            {
                if (h.Seconds < Model.From || h.Seconds > Model.To)
                    continue;
                float x = X(h.Seconds);
                Box(p, x - 0.5f, r.y, x + 0.5f, r.yMax, h.OffNominal ? new Color(ink.r, ink.g, ink.b, 0.35f) : ink);
            }
        }

        // a component's columns: the least-to-most stroke joined to the next column by its first and last values
        void Lines(Painter2D p, ItemGraphModel m, int d, Color color, float width, Rect r, float step)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            p.BeginPath();
            bool open = false;
            for (int c = 0; c < m.Columns; c++)
            {
                if (!m.Has[c])
                {
                    open = false;
                    continue;
                }
                int at = c * 4 + d;
                float x = r.x + (c + 0.5f) * step;
                if (open)
                    p.LineTo(new Vector2(x, Y(m.First[at])));
                else
                    p.MoveTo(new Vector2(x, Y(m.First[at])));
                if (m.Max[at] != m.Min[at])
                {
                    p.MoveTo(new Vector2(x, Y(m.Min[at])));
                    p.LineTo(new Vector2(x, Y(m.Max[at])));
                    p.MoveTo(new Vector2(x, Y(m.Last[at])));
                }
                open = true;
            }
            p.Stroke();
        }

        static void Box(Painter2D p, float x0, float y0, float x1, float y1, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, y0));
            p.LineTo(new Vector2(x1, y1));
            p.LineTo(new Vector2(x0, y1));
            p.ClosePath();
            p.Fill();
        }

        static string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // the recorded frame nearest the pointer, within a few pixels: its values and the line's there
        void AppendRecorded(StringBuilder sb, float localX)
        {
            if (!DotsShown)
                return;
            int best = -1;
            float gap = 4f;
            for (int i = 0; i < Dots.Count; i++)
            {
                float d = Math.Abs(X(Dots.Seconds(i)) - localX);
                if (d <= gap)
                {
                    gap = d;
                    best = i;
                }
            }
            if (best < 0)
                return;
            Vector4 v = Dots.Value(best);
            bool compared = Dots.Compared(best, out Vector4 line);
            sb.Append(" · recorded");
            for (int d = 0; d < Model.Dimension; d++)
            {
                if (!Shown[d])
                    continue;
                sb.Append(' ');
                if (Model.Dimension > 1)
                    sb.Append(Names[d]).Append(' ');
                sb.Append(N(v[d]));
                if (compared)
                    sb.Append(" (line ").Append(N(line[d])).Append(')');
            }
        }

        // the place under the pointer and each shown component's least and most there
        internal string TooltipAt(float localX)
        {
            if (!Drawable)
                return "";
            Rect r = contentRect;
            int c = Mathf.Clamp((int)((localX - r.x) / Math.Max(1f, r.width) * Model.Columns), 0, Model.Columns - 1);
            double seconds = Model.From + (Model.To - Model.From) * (c + 0.5) / Model.Columns;
            var sb = new StringBuilder();
            sb.Append(seconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(" s");
            AppendRecorded(sb, localX);
            if (!Model.Has[c])
                return sb.Append(" · not playing").ToString();
            for (int d = 0; d < Model.Dimension; d++)
            {
                if (!Shown[d])
                    continue;
                int at = c * 4 + d;
                sb.Append(" · ");
                if (Model.Dimension > 1)
                    sb.Append(Names[d]).Append(' ');
                sb.Append(N(Model.Min[at]));
                if (Model.Max[at] != Model.Min[at])
                    sb.Append(" ~ ").Append(N(Model.Max[at]));
            }
            return sb.ToString();
        }
    }
}
