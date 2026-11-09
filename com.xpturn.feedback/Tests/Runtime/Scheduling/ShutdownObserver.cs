using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // what a scene object's cleanup sees at play exit — OnApplicationQuit before quitting, OnDestroy after it. The results
    // are public primitives: the editor tests do not see internals
    public sealed class ShutdownObserver : MonoBehaviour
    {
        public static string SawAtQuit = "-";
        public static string SawAtDestroy = "-";
        public static bool AddRefused;

        public static void ResetRecord()
        {
            SawAtQuit = "-";
            SawAtDestroy = "-";
            AddRefused = false;
        }

        static string State()
        {
            FeedbackScheduler scheduler = FeedbackScheduler.Default;
            return scheduler == null ? "null" : scheduler.IsShutdown ? "closed" : "open";
        }

        void OnApplicationQuit() => SawAtQuit = State();

        // a playback started by the cleanup is refused
        void OnDestroy()
        {
            SawAtDestroy = State();
            FeedbackScheduler scheduler = FeedbackScheduler.Default;
            AddRefused = scheduler == null || scheduler.Add(new NullRunner(), 0) == default;
        }

        sealed class NullRunner : IPlaybackRunner
        {
            public bool Advance(int state, in FrameTime time) => false;

            public void Abort(int state, AbortReason reason)
            {
            }

            public Object DebugOwner => null;
        }
    }
}
