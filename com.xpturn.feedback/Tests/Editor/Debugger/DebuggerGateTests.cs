using System.Linq;
using NUnit.Framework;
using UnityEngine;
using xpTURN.Feedback.Editor;
using T = xpTURN.Feedback.Tests.Editor.DebuggerTesting;

namespace xpTURN.Feedback.Tests.Editor
{
    // the allocation gate with the window open (edit mode — the same verdicts as a play entry: the fixtures are prepared
    // before the run). The first run of five fresh fixtures, red only when all five allocate
    internal sealed class DebuggerGateTests
    {
        static int s_Warnings;

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Warning)
                s_Warnings++;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            T.Clean();
        }

        // the seek and listener cycles with a second missed Signal of each way · the window's rings hear them (the events and
        // the ends) · the call sites off · a fixture's first missed Signal warns once, outside the run
        [Test]
        public void DT10a_TheGateWithTheWindowOpen()
        {
            DebuggerCallSites.Enabled = false;
            T.Window();
            FeedbackDebuggerWindow w = Resources.FindObjectsOfTypeAll<FeedbackDebuggerWindow>().Single();
            Assert.That(T.Positive(), Is.EqualTo(5), "the positive control");
            long heard = w.Events.Total;
            long ends = w.Events.EndTotal;
            s_Warnings = 0;
            Application.logMessageReceived += OnLog;
            int hits = T.FirstRuns(T.Cycle);
            Application.logMessageReceived -= OnLog;
            Assert.That(hits, Is.LessThan(5), "allocating first runs " + hits + "/5");
            Assert.That(w.Events.Total, Is.GreaterThan(heard), "the window heard the cycles");
            Assert.That(w.Events.EndTotal, Is.GreaterThan(ends), "the window heard the ends");
            Assert.That(s_Warnings, Is.EqualTo(6), "a warning per fixture — its first missed Signal, made outside the run");
        }
    }
}
