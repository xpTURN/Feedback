using UnityEditor;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // the display-only items' marks in the Scene view — the session's context's live marks, drawn while the session runs (an
    // arrow · a torque's axis and ring · an explosion's sphere), fading to their end
    internal static class PreviewMarksView
    {
        static readonly Color s_Force = new Color(1f, 0.55f, 0.1f);
        static readonly Color s_Torque = new Color(0.75f, 0.35f, 1f);
        static readonly Color s_Velocity = new Color(0.2f, 0.85f, 1f);
        static readonly Color s_Explosion = new Color(1f, 0.25f, 0.2f);

        static FeedbackContext s_Context;

        // the marks drawn · the callbacks — the tests count them
        internal static int Draws;
        internal static int Calls;

        internal static void Attach(FeedbackContext context)
        {
            Detach();
            s_Context = context;
            SceneView.duringSceneGui += Draw;
        }

        internal static void Detach()
        {
            if (s_Context == null)
                return;
            SceneView.duringSceneGui -= Draw;
            s_Context = null;
        }

        static void Draw(SceneView view)
        {
            Calls++;
            PreviewMarks marks = s_Context?.Marks;
            if (marks == null || Event.current.type != EventType.Repaint)
                return;
            double now = s_Context.Now;
            Color saved = Handles.color;
            for (int i = 0; i < marks.Count; i++)
            {
                PreviewMark mark = marks[i];
                if (mark.Until <= now)
                    continue;
                float alpha = Mathf.Clamp01((float)((mark.Until - now) / PreviewMarks.Seconds));
                float size = HandleUtility.GetHandleSize(mark.Origin);
                switch (mark.Kind)
                {
                    case PreviewMarkKind.Explosion:
                        Handles.color = Fade(s_Explosion, alpha);
                        Handles.DrawWireDisc(mark.Origin, Vector3.up, mark.Radius);
                        Handles.DrawWireDisc(mark.Origin, Vector3.right, mark.Radius);
                        Handles.DrawWireDisc(mark.Origin, Vector3.forward, mark.Radius);
                        break;
                    case PreviewMarkKind.Torque:
                        Handles.color = Fade(s_Torque, alpha);
                        Arrow(mark.Origin, mark.Vector, size);
                        if (mark.Vector.sqrMagnitude > 0f)
                            Handles.DrawWireDisc(mark.Origin, mark.Vector.normalized, size * 0.4f);
                        break;
                    default:
                        Handles.color = Fade(mark.Kind == PreviewMarkKind.Velocity ? s_Velocity : s_Force, alpha);
                        Arrow(mark.Origin, mark.Vector, size);
                        break;
                }
                Draws++;
            }
            Handles.color = saved;
        }

        static Color Fade(Color color, float alpha) => new Color(color.r, color.g, color.b, color.a * alpha);

        // the length on a log scale of the magnitude, sized to the screen
        static void Arrow(Vector3 origin, Vector3 vector, float size)
        {
            float magnitude = vector.magnitude;
            if (magnitude <= 0f)
                return;
            Vector3 direction = vector / magnitude;
            float length = size * (0.5f + Mathf.Log(1f + magnitude, 2f) * 0.25f);
            Vector3 tip = origin + direction * length;
            Handles.DrawLine(origin, tip, 2f);
            Handles.ConeHandleCap(0, tip, Quaternion.LookRotation(direction), size * 0.15f, EventType.Repaint);
        }
    }
}
