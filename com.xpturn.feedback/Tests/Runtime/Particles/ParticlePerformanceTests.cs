using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Perf = xpTURN.Feedback.Tests.PlayerPerformanceTests;

namespace xpTURN.Feedback.Tests
{
    // (v) the particle Emission Rate, a flash on its value slot to the module write in the Update stage — the module's path
    // through the core window, within 1.5 µs (judged in an IL2CPP player). The window is judged against this class's own no-play
    // window (measured twice — the smaller), not the core baseline: these rows run after the core assembly, where a development
    // player's console, once an exception is logged, allocates each frame
    public sealed class ParticlePerformanceTests
    {
        static long s_Baseline = -1;
        readonly List<Object> m_Made = new List<Object>();
        FeedbackScheduler m_Saved;
        FeedbackScheduler m_Test;

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = 1f / 60f;
            m_Saved = FeedbackScheduler.Default;
            m_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = m_Test;
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
            FeedbackScheduler.Default = m_Saved;
            if (!m_Test.IsShutdown)
                m_Test.Shutdown();
            Time.captureDeltaTime = 0f;
        }

        static IEnumerator Baseline()
        {
            if (s_Baseline >= 0)
                yield break;
            yield return Perf.Measure("(v) no play", 1, 0);
            long first = Perf.s_Last.Alloc;
            yield return Perf.Measure("(v) no play again", 1, 0);
            s_Baseline = Math.Min(first, Perf.s_Last.Alloc);
        }

        [UnityTest]
        public IEnumerator GP19_ParticleEmissionRate([Values(100, 1000)] int n)
        {
            yield return null;
            yield return Baseline();
            var players = new List<FeedbackPlayer>(n);
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("GP19_ParticleEmissionRate");
                m_Made.Add(go);
                ParticleSystem system = ParticleFixtures.System(go, true, 5f, 1f, 10f);
                var item = Perf.Repeated(new ParticleEmissionRateItem { m_Motion = Perf.Flash(new Vector4(20f, 0f, 0f, 0f)) });
                item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = system, m_Path = "", m_Anchor = "" };
                var p = go.AddComponent<FeedbackPlayer>();
                p.m_Items.Add(item);
                p.EnsureRuntime();
                players.Add(p);
            }
            int prepared = 0, accepted = 0;
            foreach (FeedbackPlayer p in players)
            {
                if (p.Runtime != null && p.Runtime.Binding(0) != null)
                    prepared++;
                if (p.Play() != default)
                    accepted++;
            }
            yield return Perf.Settled("(v) particle emission rate " + n, n, 0, s_Baseline);
            Perf.Window w = Perf.s_Last;
            Assert.That(prepared + " · " + accepted, Is.EqualTo(n + " · " + n), "prepared · accepted");
            Assert.That(w.Frames + " · " + w.Counted, Is.EqualTo(Perf.Frames + " · " + Perf.Frames), "the recorders live — frames · markers");
            Assert.That(Perf.PerPlay(w.Revert, n), Is.GreaterThanOrEqualTo(Perf.LayerFloor), "the value slot puts back — Revert ns a play");
            if (Perf.k_Judge)
            {
                Assert.That(w.PerPlay, Is.GreaterThan(0).And.LessThanOrEqualTo(1500), "per play ns");
                Assert.That(Math.Abs(w.Alloc - s_Baseline), Is.LessThanOrEqualTo(Perf.Equal), "the window " + w.Alloc + " vs " + s_Baseline + " B");
            }
        }
    }
}
