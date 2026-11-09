using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests.Editor
{
    // The constraint calls the delegate once and does not warm up, so the delegate measured is the one already run on a
    // warm-up scheduler; the measured scheduler is freshly reserved, or the warm-up would hide a growth. The verdict goes
    // through Assert.That — ApplyTo on Is.Not drops the Not
    public class SchedulerAllocationTests
    {
        const int Count = 8;
        static FeedbackScheduler s_Target;
        static readonly CountingRunner s_Runner = new CountingRunner();
        static readonly PlaybackHandle[] s_Handles = new PlaybackHandle[Count];
        static readonly Dictionary<PlaybackHandle, int> s_Table = new Dictionary<PlaybackHandle, int>(16);
        static int s_Sink;

        static readonly TestDelegate s_Cycle = () =>
        {
            FeedbackScheduler scheduler = s_Target;
            for (int i = 0; i < Count; i++)
                s_Handles[i] = scheduler.Add(s_Runner, i);
            scheduler.Tick(0.016f);
            for (int i = 0; i < Count; i++)
            {
                scheduler.Remove(s_Handles[i]);
                s_Handles[i] = scheduler.Add(s_Runner, i);
            }
            scheduler.Tick(0.016f);
            for (int i = 0; i < Count; i++)
                scheduler.Remove(s_Handles[i]);
            scheduler.Tick(0.016f);
        };

        static readonly TestDelegate s_Lookup = () =>
        {
            if (s_Table.TryGetValue(s_Handles[0], out int value))
                s_Sink += value;
            s_Table[s_Handles[1]] = 2;
            if (s_Handles[0] == s_Handles[1])
                s_Sink++;
        };

        static FeedbackScheduler Reserved()
        {
            var scheduler = new FeedbackScheduler(ContextKind.Test);
            scheduler.Reserve(Count);
            return scheduler;
        }

        [Test]
        public void KT16_AddRemoveTickAndReplacingAllAllocateNothingInsideTheReservation()
        {
            s_Target = Reserved();
            s_Cycle();
            s_Target = Reserved();
            Assert.That(s_Cycle, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void KT16_HandleLookupAllocatesNothing()
        {
            s_Target = Reserved();
            for (int i = 0; i < Count; i++)
                s_Handles[i] = s_Target.Add(s_Runner, i);
            s_Table.Clear();
            s_Table[s_Handles[0]] = 1;
            s_Lookup();
            Assert.That(s_Lookup, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void KT16_AddOutsideTheReservationAllocates()
        {
            var scheduler = new FeedbackScheduler(ContextKind.Test);
            LogAssert.Expect(LogType.Warning, FeedbackScheduler.ShortageWarning);
            Assert.That(() => { scheduler.Add(s_Runner, 0); }, Is.AllocatingGCMemory());
        }
    }
}
