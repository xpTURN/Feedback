using System;
using UnityEditor;

namespace xpTURN.Feedback.Editor
{
    // the wall clock's difference. A tick at most every 1/60 s with the time in between gathered — the batch editor runs its
    // update thousands of times a second and a tick redraws twice. The first tick is 0 and a step is capped at 0.1 s (an
    // editor that stalled). A test swaps the source and runs the updates
    internal sealed class PreviewClock
    {
        internal const double TickInterval = 1.0 / 60.0;
        internal const double MaxStep = 0.1;

        internal static Func<double> Source = () => EditorApplication.timeSinceStartup;

        double m_Last;
        double m_Gathered;
        bool m_Started;

        // true when a tick is due, with its delta
        internal bool Advance(out float dt)
        {
            double now = Source();
            if (!m_Started)
            {
                m_Started = true;
                m_Last = now;
                dt = 0f;
                return true;
            }
            m_Gathered += now - m_Last;
            m_Last = now;
            if (m_Gathered < TickInterval)
            {
                dt = 0f;
                return false;
            }
            dt = (float)Math.Min(m_Gathered, MaxStep);
            m_Gathered = 0;
            return true;
        }
    }
}
