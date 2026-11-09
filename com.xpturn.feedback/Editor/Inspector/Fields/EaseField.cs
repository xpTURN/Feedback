using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using PopupWindow = UnityEditor.PopupWindow;

namespace xpTURN.Feedback.Editor
{
    // an Ease — the type from a popup of thumbnails (thirty-one + the curve), the parameters only for Back · Elastic, the
    // curve only for Custom. A new type takes its own parameters (Ease.Of: a saved Back P1 0 would be Cubic). The tween's
    // ease, the shake's falloff and the flash's ease use it
    internal sealed class EaseField : VisualElement
    {
        internal const string TypeName = "Ease";
        internal const int ThumbPoints = 24;

        readonly SerializedProperty m_Ease;
        internal readonly Button Choice;
        internal readonly EaseThumb Thumb = new EaseThumb();
        internal readonly FloatField P1;
        internal readonly FloatField P2;
        internal readonly CurveField Curve;

        internal EaseField(SerializedProperty ease, string label)
        {
            m_Ease = ease.Copy();
            var row = new VisualElement();
            row.AddToClassList(InspectorStyles.Row);
            var name = new Label(label);
            name.AddToClassList(InspectorStyles.EaseLabel);
            Choice = new Button(Open);
            Choice.AddToClassList(InspectorStyles.Grow);
            row.Add(name);
            row.Add(Thumb);
            row.Add(Choice);
            Add(row);
            P1 = new FloatField("P1");
            P1.BindProperty(m_Ease.FindPropertyRelative("m_P1"));
            P2 = new FloatField("P2");
            P2.BindProperty(m_Ease.FindPropertyRelative("m_P2"));
            Curve = new CurveField("Curve");
            Curve.BindProperty(m_Ease.FindPropertyRelative("m_Curve"));
            Add(P1);
            Add(P2);
            Add(Curve);
            Show();
            this.TrackPropertyValue(m_Ease.FindPropertyRelative("m_Type"), _ => Show());
        }

        internal EaseType Type => (EaseType)m_Ease.FindPropertyRelative("m_Type").intValue;

        void Show()
        {
            EaseType type = Type;
            Choice.text = type.ToString();
            Thumb.Type = type;
            bool parametric = Parametric(type);
            P1.style.display = parametric ? DisplayStyle.Flex : DisplayStyle.None;
            P1.label = type >= EaseType.ElasticIn && type <= EaseType.ElasticInOut ? "Amplitude" : "Overshoot";
            P2.style.display = parametric && type >= EaseType.ElasticIn && type <= EaseType.ElasticInOut ? DisplayStyle.Flex : DisplayStyle.None;
            P2.label = "Period";
            Curve.style.display = type == EaseType.Custom ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void Open() => PopupWindow.Show(Choice.worldBound, new EasePopup(t => Set(m_Ease, t)));

        // the types that have a thumbnail — all but Custom (its curve is the field)
        internal static IEnumerable<EaseType> Thumbed()
        {
            foreach (EaseType t in Enum.GetValues(typeof(EaseType)))
            {
                if (t != EaseType.Custom)
                    yield return t;
            }
        }

        // a thumbnail's points — (t, Ease.Evaluate(t)) at even steps from 0 to 1
        internal static Vector2[] Points(EaseType type, int count)
        {
            Ease ease = Ease.Of(type);
            var points = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                points[i] = new Vector2(t, ease.Evaluate(t));
            }
            return points;
        }

        // Back (overshoot) and Elastic (amplitude, period) read P1 · P2
        internal static bool Parametric(EaseType type)
            => type >= EaseType.BackIn && type <= EaseType.ElasticInOut;

        // the type with its own parameters — the curve stays
        internal static void Set(SerializedProperty ease, EaseType type)
        {
            Ease of = Ease.Of(type);
            ease.serializedObject.Update();
            ease.FindPropertyRelative("m_Type").intValue = (int)type;
            ease.FindPropertyRelative("m_P1").floatValue = of.m_P1;
            ease.FindPropertyRelative("m_P2").floatValue = of.m_P2;
            ease.serializedObject.ApplyModifiedProperties();
        }
    }

    // a type's curve in a small box — overshoot shown, y from −0.5 to 1.5
    internal sealed class EaseThumb : VisualElement
    {
        EaseType m_Type;
        Vector2[] m_Points;

        internal EaseThumb()
        {
            AddToClassList(InspectorStyles.EaseThumb);
            generateVisualContent += Draw;
        }

        internal EaseType Type
        {
            get => m_Type;
            set
            {
                m_Type = value;
                m_Points = value == EaseType.Custom ? null : EaseField.Points(value, EaseField.ThumbPoints);
                MarkDirtyRepaint();
            }
        }

        void Draw(MeshGenerationContext context)
        {
            if (m_Points == null)
                return;
            Rect r = contentRect;
            if (r.width <= 0f || r.height <= 0f)
                return;
            Painter2D p = context.painter2D;
            p.strokeColor = resolvedStyle.color;
            p.lineWidth = 1.5f;
            p.BeginPath();
            for (int i = 0; i < m_Points.Length; i++)
            {
                var at = new Vector2(r.x + m_Points[i].x * r.width, r.y + (1.5f - m_Points[i].y) / 2f * r.height);
                if (i == 0)
                    p.MoveTo(at);
                else
                    p.LineTo(at);
            }
            p.Stroke();
        }
    }

    // the popup — a thumbnail per type and the curve
    internal sealed class EasePopup : PopupWindowContent
    {
        readonly Action<EaseType> m_Chosen;
        internal static EasePopup Last;

        internal EasePopup(Action<EaseType> chosen)
        {
            m_Chosen = chosen;
            Last = this;
        }

        public override Vector2 GetWindowSize() => new Vector2(332, 300);

        public override void OnGUI(Rect rect)
        {
        }

        public override VisualElement CreateGUI()
        {
            var root = new ScrollView();
            InspectorStyles.Attach(root);
            var grid = new VisualElement();
            grid.AddToClassList(InspectorStyles.EaseGrid);
            foreach (EaseType t in EaseField.Thumbed())
                grid.Add(Cell(t));
            grid.Add(Cell(EaseType.Custom));
            root.Add(grid);
            return root;
        }

        VisualElement Cell(EaseType type)
        {
            var cell = new Button(() => Choose(type)) { tooltip = type.ToString() };
            cell.AddToClassList(InspectorStyles.EaseCell);
            cell.Add(new EaseThumb { Type = type });
            cell.Add(new Label(type == EaseType.Custom ? "Curve" : type.ToString()));
            return cell;
        }

        internal void Choose(EaseType type)
        {
            m_Chosen(type);
            editorWindow?.Close();
        }
    }
}
