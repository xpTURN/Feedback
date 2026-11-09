using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback
{
    // what a display-only item shows in the preview instead of writing — an arrow from a point, a radius. Made by a preview
    // context only; the editor draws the live ones in the Scene view
    internal enum PreviewMarkKind
    {
        Force,
        Torque,
        Velocity,
        Explosion,
    }

    internal struct PreviewMark
    {
        internal PreviewMarkKind Kind;
        internal Vector3 Origin;
        internal Vector3 Vector;
        internal float Radius;
        internal double Until;
    }

    internal sealed class PreviewMarks
    {
        // how long a mark shows — the session's clock
        internal const double Seconds = 1.0;

        readonly List<PreviewMark> m_Marks = new List<PreviewMark>();

        internal int Count => m_Marks.Count;

        // every mark made — the notice counts them
        internal int Total { get; private set; }

        internal PreviewMark this[int i] => m_Marks[i];

        // the expired ones go first — a repeating session adds marks as long as it runs
        internal void Add(in PreviewMark mark)
        {
            double now = mark.Until - Seconds;
            int k = 0;
            for (int i = 0; i < m_Marks.Count; i++)
            {
                if (m_Marks[i].Until > now)
                    m_Marks[k++] = m_Marks[i];
            }
            m_Marks.RemoveRange(k, m_Marks.Count - k);
            m_Marks.Add(mark);
            Total++;
        }

        internal bool AliveAt(double now)
        {
            for (int i = 0; i < m_Marks.Count; i++)
            {
                if (m_Marks[i].Until > now)
                    return true;
            }
            return false;
        }
    }
}
