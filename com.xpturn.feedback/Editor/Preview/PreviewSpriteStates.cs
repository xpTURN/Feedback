using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace xpTURN.Feedback.Editor
{
    // an end's line color — taken right before the slot's first write (a gradient key is not recordable). A save goes back to
    // it and on again; the end goes back. The color before is written down in SessionState until the end: a reload whose
    // hook was missed finds it and the recovery puts it back
    internal sealed class LineColorState : IPreviewSaveState
    {
        readonly LineRenderer m_Target;
        readonly LineEnd m_End;
        readonly Color m_Before;
        readonly Scene m_Scene;
        Color m_Kept;

        internal LineColorState(LineRenderer target, LineEnd end)
        {
            m_Target = target;
            m_End = end;
            m_Before = Get(target, end);
            m_Scene = target.gameObject.scene;
            PreviewLineColors.Write(this);
        }

        internal LineRenderer Target => m_Target;

        internal LineEnd End => m_End;

        internal Color Before => m_Before;

        public Scene Scene => m_Scene;

        internal static Color Get(LineRenderer target, LineEnd end) => end == LineEnd.End ? target.endColor : target.startColor;

        internal static void Set(LineRenderer target, LineEnd end, Color color)
        {
            if (end == LineEnd.End)
                target.endColor = color;
            else
                target.startColor = color;
        }

        // the color now is not the one the session's slot left (read back — the keys are 8 bits) — a user's edit
        internal bool UserChanged()
        {
            if (m_Target == null)
                return false;
            PreviewSession session = PreviewSession.Current;
            if (session == null || !session.Scheduler.Context.Layers.TryFind(m_Target, 0, (int)m_End, out LineColorSlot slot) || !slot.Left(out Color left))
                return true;
            return !default(ColorOps).BitEquals(Get(m_Target, m_End), left);
        }

        public void Revert()
        {
            if (m_Target == null)
                return;
            m_Kept = Get(m_Target, m_End);
            Set(m_Target, m_End, m_Before);
        }

        public void Reapply()
        {
            if (m_Target != null)
                Set(m_Target, m_End, m_Kept);
        }
    }

    // a renderer that never had a sprite — its first sprite sets the hidden m_WasSpriteAssigned (not recordable; a
    // SerializedObject write does not hold). The setter's null clears it: a save goes to null through the setter and back to
    // the sprite after (the first assignment again — m_Size as during the preview). The end needs nothing — the stop's revert
    // clears it; a save's does not (AnimationMode's save revert passes no setter)
    internal sealed class SpriteAssignedState : IPreviewSaveState
    {
        readonly SpriteRenderer m_Target;
        readonly Scene m_Scene;
        Sprite m_Kept;

        internal SpriteAssignedState(SpriteRenderer target)
        {
            m_Target = target;
            m_Scene = target.gameObject.scene;
        }

        internal SpriteRenderer Target => m_Target;

        public Scene Scene => m_Scene;

        internal static bool Assigned(SpriteRenderer target) => new SerializedObject(target).FindProperty("m_WasSpriteAssigned").boolValue;

        public void Revert()
        {
            if (m_Target == null)
                return;
            m_Kept = m_Target.sprite;
            if (m_Kept != null)
                m_Target.sprite = null;
        }

        public void Reapply()
        {
            if (m_Target != null && m_Kept != null)
                m_Target.sprite = m_Kept;
        }
    }

    // the line colors' records — one line each: the renderer's GlobalObjectId, the end, the color before
    internal static class PreviewLineColors
    {
        internal const string Key = "xpTURN.Feedback.Preview.LineColors";

        static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

        static float P(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        internal static void Write(LineColorState state)
        {
            Color c = state.Before;
            string line = GlobalObjectId.GetGlobalObjectIdSlow(state.Target).ToString() + "\t" + (int)state.End + "\t" + string.Join(";", F(c.r), F(c.g), F(c.b), F(c.a));
            string all = SessionState.GetString(Key, "");
            SessionState.SetString(Key, all.Length == 0 ? line : all + "\n" + line);
        }

        // the end, after the states went back
        internal static void Ended() => SessionState.EraseString(Key);

        // the recovery: a renderer found gets its color back; one not found (its scene not loaded) waits for a later load
        internal static void Recover()
        {
            string all = SessionState.GetString(Key, "");
            if (all.Length == 0)
                return;
            var left = new StringBuilder();
            foreach (string line in all.Split('\n'))
            {
                string[] parts = line.Split('\t');
                string[] c = parts.Length == 3 ? parts[2].Split(';') : null;
                if (c == null || c.Length != 4 || !GlobalObjectId.TryParse(parts[0], out GlobalObjectId id) || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int end))
                    continue;
                if (!(GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is LineRenderer target))
                {
                    if (left.Length > 0)
                        left.Append('\n');
                    left.Append(line);
                    continue;
                }
                LineColorState.Set(target, (LineEnd)end, new Color(P(c[0]), P(c[1]), P(c[2]), P(c[3])));
            }
            if (left.Length == 0)
                SessionState.EraseString(Key);
            else
                SessionState.SetString(Key, left.ToString());
        }
    }
}
