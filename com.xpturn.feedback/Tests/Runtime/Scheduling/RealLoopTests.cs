using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace xpTURN.Feedback.Tests
{
    // the frame positions and the boundary (one test — Default swapped once) · the isolation on the real Default. Frames are
    // split by frame number: split by the Revert entry, a Revert moved to the end of the frame keeps the same cycle. TearDown
    // puts Default, the capture delta and the real Default's runners back after a red test too. No UnityEditor here
    public class RealLoopTests
    {
        static readonly string[] Positions = { "FB.Revert", "MB.FixedUpdate", "MB.Update", "FB.Update", "MB.LateUpdate", "FB.LateApply" };

        const string ThrowMessage = "RealLoopTests thrower";

        sealed class RecordingLayers : ILayerStages
        {
            public void Revert() => FrameRecorder.Add("FB.Revert");

            public void Apply(ConsumptionStage stage) => FrameRecorder.Add(stage == ConsumptionStage.Update ? "FB.Update" : "FB.LateApply");
        }

        sealed class Runner : IPlaybackRunner
        {
            readonly string m_Name;
            readonly bool m_Throws;

            internal Runner(string name, bool throws = false)
            {
                m_Name = name;
                m_Throws = throws;
            }

            internal int Advances { get; private set; }

            internal AbortReason? Aborted { get; private set; }

            public bool Advance(int state, in FrameTime time)
            {
                Advances++;
                FrameRecorder.Add(m_Name);
                if (m_Throws)
                    throw new InvalidOperationException(ThrowMessage);
                return true;
            }

            public void Abort(int state, AbortReason reason) => Aborted = reason;

            public UnityEngine.Object DebugOwner => null;
        }

        static FeedbackScheduler s_SavedDefault;
        static FeedbackScheduler s_Test;
        static GameObject s_Recorder;
        static FeedbackScheduler s_Real;
        static readonly List<PlaybackHandle> s_Handles = new List<PlaybackHandle>();
        static int s_Reserved;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            FrameRecorder.UpdateHook = null;
            if (s_Test != null)
            {
                FeedbackScheduler.Default = s_SavedDefault;
                s_Test.Shutdown();
                s_Test = null;
                s_SavedDefault = null;
            }
            Time.captureDeltaTime = 0f;
            if (s_Recorder != null)
                UnityEngine.Object.Destroy(s_Recorder);
            s_Recorder = null;
            if (s_Real != null)
            {
                foreach (var handle in s_Handles)
                    s_Real.Remove(handle);
                s_Real.Unreserve(s_Reserved);
                s_Real = null;
            }
            s_Handles.Clear();
            s_Reserved = 0;
            FrameRecorder.Clear();
            yield break;
        }

        // the record by frame number, in order
        static List<(int Frame, List<string> Entries)> ByFrame()
        {
            var frames = new List<(int Frame, List<string> Entries)>();
            for (int i = 0; i < FrameRecorder.Record.Count; i++)
            {
                int frame = FrameRecorder.Frames[i];
                if (frames.Count == 0 || frames[frames.Count - 1].Frame != frame)
                    frames.Add((frame, new List<string>()));
                frames[frames.Count - 1].Entries.Add(FrameRecorder.Record[i]);
            }
            return frames;
        }

        static string Describe((int Frame, List<string> Entries) frame) => frame.Frame + ": " + string.Join(" > ", frame.Entries);

        // Default is a test scheduler with a recording layer; the capture delta makes one FixedUpdate a frame. Each whole
        // frame (the first and the last are cut) is Revert → FixedUpdate → Update → our Update → LateUpdate → our LateApply.
        // A runner added in MonoBehaviour.Update does not advance in that frame, and from the next frame advances once between
        // MonoBehaviour.Update and our Update — the pass limit is Revert's
        [UnityTest]
        public IEnumerator RT04_FramePositionsAndBoundary()
        {
            Time.captureDeltaTime = Time.fixedDeltaTime;
            s_SavedDefault = FeedbackScheduler.Default;
            s_Test = new FeedbackScheduler(ContextKind.Test, new RecordingLayers());
            s_Test.Reserve(1);
            FeedbackScheduler.Default = s_Test;
            var runner = new Runner("R");
            int start = Time.frameCount;
            int addFrame = -1;
            FrameRecorder.UpdateHook = () =>
            {
                if (addFrame >= 0 || Time.frameCount < start + 3)
                    return;
                addFrame = Time.frameCount;
                s_Test.Add(runner, 0);
            };
            s_Recorder = new GameObject(nameof(FrameRecorder), typeof(FrameRecorder));
            FrameRecorder.Clear();
            for (int i = 0; i < 8; i++)
                yield return null;

            var frames = ByFrame();
            var whole = frames.Skip(1).Take(Math.Max(0, frames.Count - 2)).ToList();
            Assert.That(whole.Count, Is.GreaterThanOrEqualTo(3), "whole frames");
            Assert.That(whole.Where(f => !f.Entries.Where(e => e != "R").SequenceEqual(Positions)).Select(Describe), Is.Empty, "positions");

            Assert.That(addFrame, Is.GreaterThanOrEqualTo(0), "the runner was added");
            Assert.That(frames.Where(f => f.Frame == addFrame && f.Entries.Contains("R")).Select(Describe), Is.Empty, "no advance in the add frame");
            var after = whole.Where(f => f.Frame > addFrame).ToList();
            Assert.That(after.Count, Is.GreaterThanOrEqualTo(2), "whole frames after the add");
            Assert.That(after.Where(f => !(f.Entries.Count(e => e == "R") == 1
                    && f.Entries.IndexOf("R") > f.Entries.IndexOf("MB.Update")
                    && f.Entries.IndexOf("R") < f.Entries.IndexOf("FB.Update"))).Select(Describe),
                Is.Empty, "one advance between MonoBehaviour.Update and our Update");
        }

        // three runners on the real Default — front, thrower, back. The thrower advances once and ends Faulted with one log;
        // front and back advance once a frame, back in the throw frame too (the pass goes on from the next slot). One
        // frame first: after a swapped Default the real one's Revert runs before the Add (the stop diagnostic)
        [UnityTest]
        public IEnumerator RT06_IsolationOnTheRealDefault()
        {
            yield return null;
            s_Real = FeedbackScheduler.Default;
            s_Real.Reserve(3);
            s_Reserved = 3;
            var front = new Runner("A");
            var thrower = new Runner("T", true);
            var back = new Runner("B");
            s_Handles.Add(s_Real.Add(front, 0));
            s_Handles.Add(s_Real.Add(thrower, 0));
            s_Handles.Add(s_Real.Add(back, 0));
            LogAssert.Expect(LogType.Exception, new Regex(ThrowMessage));
            FrameRecorder.Clear();
            for (int i = 0; i < 5; i++)
                yield return null;

            var frames = ByFrame();
            Assert.That(frames.Count, Is.GreaterThanOrEqualTo(3), "frames");
            Assert.That(frames[0].Entries, Is.EqualTo(new[] { "A", "T", "B" }), "the throw frame");
            Assert.That(frames.Skip(1).Where(f => !f.Entries.SequenceEqual(new[] { "A", "B" })).Select(Describe), Is.Empty, "the frames after");
            Assert.That(thrower.Advances, Is.EqualTo(1), "the thrower advanced once");
            Assert.That(thrower.Aborted, Is.EqualTo(AbortReason.Faulted), "the thrower ended Faulted");
        }
    }
}
