using System.Collections;
using System.Collections.Generic;
using System.Text;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace xpTURN.Feedback.Tests
{
    // the UniTask adapter on a direct Tick (PlayMode — a runtime assembly) and its frame allocation in AT11_FrameAllocation's
    // window — a discarded UniTask keeps its source (the positive control)
    public sealed class UniTaskAdapterPlayTests
    {
        static readonly StringBuilder s_Log = new StringBuilder();
        static int s_Done;

        [TearDown]
        public void TearDown() => Time.captureDeltaTime = 0f;

        static async UniTaskVoid Watch(UniTask<PlaybackEnd> t, string tag)
        {
            PlaybackEnd e = await t;
            s_Log.Append(tag + ":" + e + " ");
        }

        sealed class L : IPlaybackListener
        {
            internal int Calls;
            public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end) { Calls++; s_Log.Append("L:" + end + " "); }
        }

        [Test]
        public void AT12a_TheAdapter()
        {
            LogAssert.ignoreFailingMessages = true;
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerTestBinding.Logging = false;
            s_Log.Clear();
            var s = new FeedbackScheduler(ContextKind.Test);
            var made = new List<GameObject>();
            FeedbackPlayer Make(float length, float cooldown = 0f)
            {
                var go = new GameObject("u");
                made.Add(go);
                var p = go.AddComponent<FeedbackPlayer>();
                p.m_Policy = PlayPolicy.Overlap;
                p.m_Cooldown = cooldown;
                p.m_Items.Add(new PlayerTestAction { m_Label = "u", m_Length = length });
                p.Bind(s);
                p.EnsureRuntime();
                return p;
            }
            try
            {
                FeedbackPlayer tick = Make(0.1f);
                var l = new L();
                Watch(tick.PlayAsUniTask(PlayArgs.Default.WithListener(l)), "tick").Forget();
                s_Log.Append("|tick ");
                s.Tick(0.2f);
                Watch(Make(0f).PlayAsUniTask(), "instant").Forget();
                FeedbackPlayer cool = Make(1f, 100f);
                Watch(cool.PlayAsUniTask(), "first").Forget();
                Watch(cool.PlayAsUniTask(), "cooldown").Forget();
                int made0 = PlaybackUniTaskSource.Created;
                for (int i = 0; i < 10; i++)
                {
                    Watch(tick.PlayAsUniTask(), "seq").Forget();
                    s.Tick(0.2f);
                }
                Watch(Make(5f).PlayAsUniTask(), "shutdown").Forget();
                string before = s_Log.ToString();
                s.Shutdown();
                string log = s_Log.ToString();
                Assert.That(log, Does.StartWith("|tick L:Completed tick:Completed instant:Completed cooldown:Refused "), log);
                Assert.That(log, Does.Contain("shutdown:ContextShutdown"), log);
                Assert.That(PlaybackUniTaskSource.Created, Is.EqualTo(made0), "reused over ten");
                Assert.That(l.Calls, Is.EqualTo(1));
            }
            finally
            {
                if (!s.IsShutdown) s.Shutdown();
                foreach (GameObject go in made) Object.DestroyImmediate(go);
                PlayerTestBinding.Reset();
            }
        }

        static async UniTaskVoid Loop(FeedbackPlayer p, int n)
        {
            for (int i = 0; i < n; i++)
                await p.PlayAsUniTask();
            s_Done++;
        }

        [UnityTest]
        public IEnumerator AT12b_FrameAllocation()
        {
            LogAssert.ignoreFailingMessages = true;
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerTestBinding.Logging = false;
            var go = new GameObject("tick");
            var tick = go.AddComponent<FeedbackPlayer>();
            tick.m_Policy = PlayPolicy.Restart;
            tick.m_Items.Add(new PlayerTestAction { m_Label = "t", m_Length = 0.001f });
            var go2 = new GameObject("instant");
            var instant = go2.AddComponent<FeedbackPlayer>();
            instant.m_Policy = PlayPolicy.Restart;
            instant.m_Items.Add(new PlayerTestAction { m_Label = "i", m_Length = 0f });
            using ProfilerRecorder rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            Time.captureDeltaTime = 1f / 60f;
            yield return null;
            s_Done = 0;
            Loop(tick, 10).Forget();
            for (int f = 0; f < 300 && s_Done == 0; f++) yield return null;
            var idle = new System.Collections.Generic.List<long>();
            for (int f = 0; f < 31; f++) { yield return null; idle.Add(rec.LastValue); }
            idle.Sort();
            long baseline = idle[15];
            int done = s_Done;
            Loop(tick, 60).Forget();
            int frames = 0, allocating = 0;
            while (s_Done == done && frames < 300)
            {
                yield return null;
                frames++;
                if (frames > 2 && rec.LastValue - baseline >= 64) allocating++;
            }
            int discardedFrames = 0;
            long discardedBytes = 0;
            for (int f = 0; f < 5; f++) { for (int k = 0; k < 50; k++) _ = instant.PlayAsUniTask(); yield return null; discardedBytes += rec.LastValue - baseline; if (rec.LastValue - baseline >= 16 * 50) discardedFrames++; }
            Debug.Log($"PROBE AT12b baseline {baseline} · loop frames {frames} allocating {allocating} · discarded frames {discardedFrames}/5 ({discardedBytes / 250} B a call) · sources {PlaybackUniTaskSource.Created}");
            Time.captureDeltaTime = 0f;
            Assert.That((s_Done, frames), Is.EqualTo((done + 1, frames)), "the loop finished inside the window");
            Assert.That(frames, Is.InRange(55, 130), "a play a frame");
            Assert.That(allocating, Is.LessThanOrEqualTo(3), "nothing once warm (a stray editor chunk or two)");
            Assert.That(discardedFrames, Is.GreaterThanOrEqualTo(4), "the positive control — a discarded UniTask keeps its source");
            Object.Destroy(go);
            Object.Destroy(go2);
            PlayerTestBinding.Reset();
        }
    }
}
