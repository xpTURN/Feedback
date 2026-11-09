using System;
using Unity.Profiling;

namespace xpTURN.Feedback.Editor
{
    // the scheduler's three stage markers (Scripts — the names the scheduler tests pin) recorded while the window is open,
    // the profiler on or off. A frame's value stands after the frame ends (edit mode too); the average is over the samples
    // kept. The markers are every scheduler's of the frame — the loop's, a preview's, a test's
    internal sealed class StageCosts : IDisposable
    {
        internal static readonly string[] Names = { "Feedback.Revert", "Feedback.Update", "Feedback.LateApply" };
        internal const int Samples = 15;

        readonly ProfilerRecorder[] m_Recorders = new ProfilerRecorder[3];

        internal void Start()
        {
            for (int i = 0; i < m_Recorders.Length; i++)
            {
                if (!m_Recorders[i].Valid)
                    m_Recorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, Names[i], Samples);
            }
        }

        internal bool Valid(int stage) => m_Recorders[stage].Valid;

        // nanoseconds
        internal long Last(int stage) => m_Recorders[stage].Valid ? m_Recorders[stage].LastValue : 0;

        internal long Current(int stage) => m_Recorders[stage].Valid ? m_Recorders[stage].CurrentValue : 0;

        internal int Count(int stage) => m_Recorders[stage].Valid ? m_Recorders[stage].Count : 0;

        internal double Average(int stage)
        {
            ProfilerRecorder r = m_Recorders[stage];
            if (!r.Valid || r.Count == 0)
                return 0;
            long sum = 0;
            for (int k = 0; k < r.Count; k++)
                sum += r.GetSample(k).Value;
            return (double)sum / r.Count;
        }

        public void Dispose()
        {
            for (int i = 0; i < m_Recorders.Length; i++)
            {
                if (m_Recorders[i].Valid)
                    m_Recorders[i].Dispose();
                m_Recorders[i] = default;
            }
        }
    }
}
