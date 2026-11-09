using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // the real loop (a test leaves no play running — a detached one included) · the frame allocation on a fixed frame by the
    // frames that allocate against the median idle frame of the same run (the editor drops a few-kB chunk into any one
    // frame) — a discarded PlayAsync is the positive control · a record of the cost
    public sealed class PlayerAsyncPlayTests
    {
        static readonly StringBuilder s_Log = new StringBuilder();
        static int s_Done;

        readonly List<FeedbackPlayer> m_Made = new List<FeedbackPlayer>();

        // a failed test leaves nothing behind either: the fixed frame goes back and every player it made stops first (a
        // destroyed player's running play goes on detached)
        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (FeedbackPlayer p in m_Made)
            {
                if (p == null)
                    continue;
                p.Stop();
                Object.Destroy(p.gameObject);
            }
            m_Made.Clear();
        }

        static async Awaitable Watch(Awaitable<PlaybackEnd> a, string tag)
        {
            PlaybackEnd e = await a;
            s_Log.Append(tag + ":" + e + " ");
        }

        FeedbackPlayer Make(string name, float length, PlayPolicy policy = PlayPolicy.Overlap)
        {
            var go = new GameObject(name);
            var p = go.AddComponent<FeedbackPlayer>();
            m_Made.Add(p);
            p.m_Policy = policy;
            p.m_Items.Add(new PlayerTestAction { m_Label = name, m_Length = length });
            return p;
        }

        [UnityTest]
        public IEnumerator AT10_TheRealLoop()
        {
            LogAssert.ignoreFailingMessages = true;
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerTestBinding.Logging = false;
            s_Log.Clear();
            FeedbackPlayer c = Make("c", 0.05f);
            FeedbackPlayer d = Make("d", 5f);
            FeedbackPlayer k = Make("k", 5f);
            var caller = new GameObject("caller").AddComponent<FeedbackPlayer>();
            m_Made.Add(caller);
            yield return null;
            _ = Watch(c.PlayAsync(), "completed");
            _ = Watch(d.PlayAsync(), "destroyed");
            d.Pause();
            FeedbackPlayer e = Make("e", 0.5f);
            _ = Watch(e.PlayAsync(), "continues");
            _ = Watch(k.PlayAsync(caller.destroyCancellationToken), "caller");
            float t = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t < 0.2f)
                yield return null;
            Object.Destroy(d.gameObject);
            Object.Destroy(e.gameObject);
            Object.Destroy(caller.gameObject);
            yield return null;
            yield return null;
            string log = s_Log.ToString();
            foreach (string want in new[] { "completed:Completed", "destroyed:Destroyed", "caller:Stopped" })
                Assert.That(log, Does.Contain(want), log);
            Assert.That(log, Does.Not.Contain("continues:"), "a running play of a destroyed Continue player goes on detached: " + log);
            t = Time.realtimeSinceStartup;
            while (!s_Log.ToString().Contains("continues:") && Time.realtimeSinceStartup - t < 3f)
                yield return null;
            Assert.That(s_Log.ToString(), Does.Contain("continues:Completed"), "and ends Completed — nothing runs into the next test");
            PlayerTestBinding.Reset();
        }

        static async Awaitable Loop(FeedbackPlayer p, int n)
        {
            for (int i = 0; i < n; i++)
                await p.PlayAsync();
            s_Done++;
        }

        static async Awaitable Burst(FeedbackPlayer p, int n)
        {
            for (int i = 0; i < n; i++)
                await p.PlayAsync();
        }

        [UnityTest]
        public IEnumerator AT11_FrameAllocation()
        {
            LogAssert.ignoreFailingMessages = true;
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerTestBinding.Logging = false;
            FeedbackPlayer tick = Make("tick", 0.001f, PlayPolicy.Restart);
            FeedbackPlayer instant = Make("instant", 0f, PlayPolicy.Restart);
            using ProfilerRecorder rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            // a fixed frame: a batch frame is far shorter than the play, and a play a frame makes the window
            Time.captureDeltaTime = 1f / 60f;
            yield return null;
            tick.EnsureRuntime();
            instant.EnsureRuntime();
            s_Done = 0;
            _ = Loop(tick, 10);
            for (int f = 0; f < 300 && s_Done == 0; f++) yield return null;
            _ = Burst(instant, 300);
            yield return null;
            // the median idle frame — the editor drops an occasional few-KB chunk into any one frame
            var idle = new System.Collections.Generic.List<long>();
            for (int f = 0; f < 31; f++) { yield return null; idle.Add(rec.LastValue); }
            idle.Sort();
            long baseline = idle[15];
            int done = s_Done;
            _ = Loop(tick, 60);
            int frames = 0, allocating = 0;
            while (s_Done == done && frames < 300)
            {
                yield return null;
                frames++;
                if (frames > 2 && rec.LastValue - baseline >= 64) allocating++;
            }
            int burstFrames = 0;
            for (int f = 0; f < 5; f++) { _ = Burst(instant, 300); yield return null; if (rec.LastValue - baseline >= 16 * 300) burstFrames++; }
            int discardedFrames = 0;
            long discardedBytes = 0;
            for (int f = 0; f < 5; f++) { for (int k = 0; k < 50; k++) _ = instant.PlayAsync(); yield return null; discardedBytes += rec.LastValue - baseline; if (rec.LastValue - baseline >= 16 * 50) discardedFrames++; }
            Debug.Log($"PROBE AT11 baseline {baseline} · loop frames {frames} allocating {allocating} · burst frames {burstFrames}/5 · discarded frames {discardedFrames}/5 ({discardedBytes / 250} B a call) · sources {PlaybackAwaiter.Created}");
            Time.captureDeltaTime = 0f;
            Assert.That((s_Done, frames), Is.EqualTo((done + 1, frames)), "the loop finished inside the window");
            Assert.That(frames, Is.InRange(55, 130), "a play a frame");
            Assert.That(allocating, Is.LessThanOrEqualTo(3), "PlayAsync allocates nothing once warm (a stray editor chunk or two)");
            Assert.That(burstFrames, Is.LessThanOrEqualTo(1), "an instant burst of the warmed size");
            Assert.That(discardedFrames, Is.GreaterThanOrEqualTo(4), "the positive control — a discarded Awaitable");
            PlayerTestBinding.Reset();
        }

        static async Awaitable Chain(List<FeedbackPlayer> players)
        {
            for (int i = 0; i < players.Count; i++)
                await players[i].PlayAsync();
        }

        static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        // Play and PlayAsync on instant players (the end inside Play — the awaiting code goes on at once), a
        // stopwatch, the smaller of five rounds — written down, no budget
        [UnityTest]
        public IEnumerator AT16_TheCost()
        {
            LogAssert.ignoreFailingMessages = true;
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerTestBinding.Logging = false;
            const int n = 1000;
            var players = new List<FeedbackPlayer>(n);
            for (int i = 0; i < n; i++)
                players.Add(Make("cost", 0f));
            yield return null;
            foreach (FeedbackPlayer p in players)
                p.EnsureRuntime();
            foreach (FeedbackPlayer p in players)
                p.Play();
            _ = Chain(players);
            yield return null;
            double play = double.MaxValue, playAsync = double.MaxValue;
            var sw = new System.Diagnostics.Stopwatch();
            for (int r = 0; r < 5; r++)
            {
                sw.Restart();
                for (int i = 0; i < n; i++)
                    players[i].Play();
                play = Math.Min(play, sw.Elapsed.TotalMilliseconds * 1e6 / n);
                sw.Restart();
                _ = Chain(players);
                playAsync = Math.Min(playAsync, sw.Elapsed.TotalMilliseconds * 1e6 / n);
                yield return null;
            }
            Debug.Log("PERF " + (Application.isEditor ? "editor " : "player ") + "(a) playasync instant n=" + n + " playNs=" + F(play) + " playAsyncNs=" + F(playAsync) + " overheadNs=" + F(playAsync - play) + " sources=" + PlaybackAwaiter.Created);
            Assert.That(playAsync, Is.GreaterThan(0));
            PlayerTestBinding.Reset();
        }
    }
}
