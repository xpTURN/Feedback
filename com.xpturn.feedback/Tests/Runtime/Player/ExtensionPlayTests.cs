using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using xpTURN.Feedback.Tests.Extension;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // PlayMode — a user's timed action as its player is turned off or destroyed: the end each disable behavior gives, then
    // Release. Default is a test scheduler the real loop runs, the frames fixed with captureDeltaTime 1/90; TearDown puts both
    // back. A row checks everything and judges at the end
    public sealed class ExtensionPlayTests
    {
        static FeedbackScheduler s_Saved;
        static FeedbackScheduler s_Test;

        readonly List<string> m_Fail = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();

        [SetUp]
        public void SetUp()
        {
            m_Fail.Clear();
            ProbeLog.Clear();
            Time.captureDeltaTime = 1f / 90f;
            s_Saved = FeedbackScheduler.Default;
            s_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = s_Test;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Made.Clear();
            yield return null;
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    p.ReleaseRuntime();
            }
            m_Players.Clear();
            FeedbackScheduler.Default = s_Saved;
            s_Test.Shutdown();
            s_Test = null;
            s_Saved = null;
            Time.captureDeltaTime = 0f;
            ProbeLog.Clear();
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        FeedbackPlayer Player(string name, PlayerDisableBehavior behavior)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            var player = go.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_DisableBehavior = behavior;
            player.m_Items.Add(new ProbeTimed { m_Length = 1f });
            player.EnsureRuntime();
            return player;
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
                yield return null;
        }

        static string Ends() => string.Join(" | ", ProbeLog.Calls.Where(c => c.StartsWith("End") || c == "Release"));

        // turned off: Stop Hold → Hold · Stop Revert → Revert · Continue → nothing (it goes on to Completed). Destroyed:
        // Continue lets a finite play finish on its own, then Release · a paused one ends with Hold · Stop Hold ends with Hold
        [UnityTest]
        public IEnumerator EX11_DisableAndDestroyEnds()
        {
            foreach ((PlayerDisableBehavior behavior, string off) in new[] { (PlayerDisableBehavior.StopHold, "End s0 Hold"), (PlayerDisableBehavior.StopRevert, "End s0 Revert"), (PlayerDisableBehavior.Continue, "") })
            {
                FeedbackPlayer p = Player("off-" + behavior, behavior);
                ProbeLog.Clear();
                p.Play();
                yield return Frames(10);
                p.gameObject.SetActive(false);
                string got = Ends();
                Check(got == off, behavior + " off: " + got);
                p.gameObject.SetActive(true);
                yield return Frames(100);
                if (behavior == PlayerDisableBehavior.Continue)
                    Check(Ends() == "End s0 Completed", "Continue runs on: " + Ends());
            }
            FeedbackPlayer d = Player("destroyed", PlayerDisableBehavior.Continue);
            ProbeLog.Clear();
            d.Play();
            yield return Frames(10);
            Object.Destroy(d.gameObject);
            yield return Frames(100);
            Check(Ends() == "End s0 Completed | Release", "destroyed with Continue — finishes, then Release: " + Ends());
            FeedbackPlayer paused = Player("paused", PlayerDisableBehavior.Continue);
            ProbeLog.Clear();
            paused.Play();
            yield return Frames(10);
            paused.Pause();
            Object.Destroy(paused.gameObject);
            yield return Frames(2);
            Check(Ends() == "End s0 Hold | Release", "destroyed while paused — Hold, then Release: " + Ends());
            FeedbackPlayer held = Player("held", PlayerDisableBehavior.StopHold);
            ProbeLog.Clear();
            held.Play();
            yield return Frames(10);
            Object.Destroy(held.gameObject);
            yield return Frames(2);
            Check(Ends() == "End s0 Hold | Release", "destroyed with Stop Hold — Hold, then Release: " + Ends());
            Assert.That(m_Fail, Is.Empty);
        }
    }
}
