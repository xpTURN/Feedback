using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Perf = xpTURN.Feedback.Tests.PlayerPerformanceTests;

namespace xpTURN.Feedback.Tests
{
    // the real loop (the value read where it shows, after LateApply) · the 2D light's write path within its budget in an IL2CPP
    // player
    [PrebuildSetup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    [PostBuildCleanup("xpTURN.Feedback.Tests.Editor.FeedbackTestAssets")]
    public sealed class Light2DPlayTests
    {
        static long s_Baseline = -1;
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<Object> m_Made = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    p.Stop();
            }
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Players.Clear();
            m_Made.Clear();
            yield return null;
            Time.captureDeltaTime = 0f;
        }

        FeedbackPlayer Player(GameObject go, TargetItem item, Light2D light, FeedbackMotion motion, float duration)
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = light, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.Add(item);
            m_Players.Add(p);
            return p;
        }

        [UnityTest]
        public IEnumerator LD09_TheRealLoop()
        {
            Time.captureDeltaTime = 1f / 60f;
            var go = new GameObject("Light2D");
            m_Made.Add(go);
            Light2D light = Light2DFixtures.Make(go);
            light.intensity = 1.25f;
            string Bits() => BitConverter.SingleToInt32Bits(light.intensity).ToString("X8") + "/" + BitConverter.SingleToInt32Bits(light.pointLightOuterRadius).ToString("X8") + "/" + BitConverter.SingleToInt32Bits(light.pointLightInnerRadius).ToString("X8");
            string before = Bits();
            FeedbackPlayer swing = Player(go, new Light2DIntensityItem(), light, new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = Vector4.one, m_Frequency = 2f } }, 0.5f);
            FeedbackPlayer shrink = Player(go, new Light2DRadiusItem(), light, new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(1.75f, 0f, 0f, 0f), m_Frequency = 2f } }, 0.5f);
            float moved = 0f;
            bool innerPast = false;
            void Sample()
            {
                moved = Mathf.Max(moved, Mathf.Abs(light.intensity - 1.25f));
                if (light.pointLightInnerRadius > light.pointLightOuterRadius)
                    innerPast = true;
            }
            Canvas.willRenderCanvases += Sample;
            try
            {
                yield return null;
                swing.Play();
                shrink.Play();
                for (int f = 0; f < 60; f++)
                    yield return null;
            }
            finally
            {
                Canvas.willRenderCanvases -= Sample;
            }
            Assert.That(moved, Is.GreaterThan(0.5f), "the intensity moved where it shows — after LateApply");
            Assert.That(innerPast, Is.False, "the inner never passed the outer");
            Assert.That(Bits(), Is.EqualTo(before), "the end gives the base back");
        }

        static IEnumerator Baseline()
        {
            if (s_Baseline >= 0)
                yield break;
            yield return Perf.Measure("(x) no play", 1, 0);
            long first = Perf.s_Last.Alloc;
            yield return Perf.Measure("(x) no play again", 1, 0);
            s_Baseline = Math.Min(first, Perf.s_Last.Alloc);
        }

        // the per-play ns to the player tool's table, within its budget in an IL2CPP player
        [UnityTest]
        public IEnumerator LD10_Light2DIntensity([Values(100, 1000)] int n)
        {
            FeedbackScheduler saved = FeedbackScheduler.Default;
            var test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = test;
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                yield return null;
                yield return Baseline();
                var players = new List<FeedbackPlayer>(n);
                Light2D first = null;
                for (int i = 0; i < n; i++)
                {
                    var go = new GameObject("LD10_Light2DIntensity");
                    m_Made.Add(go);
                    Light2D light = Light2DFixtures.Make(go);
                    if (first == null)
                        first = light;
                    var item = new Light2DIntensityItem { m_Motion = new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(0.4f, 0f, 0f, 0f), m_Frequency = 2f } } };
                    item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = light, m_Path = "", m_Anchor = "" };
                    item.m_Timing.m_Duration = 1000f;
                    var p = go.AddComponent<FeedbackPlayer>();
                    p.m_Items.Add(item);
                    p.EnsureRuntime();
                    players.Add(p);
                }
                int prepared = 0, accepted = 0;
                float start = first.intensity, moved = 0f;
                void Sample() => moved = Mathf.Max(moved, Mathf.Abs(first.intensity - start));
                Canvas.willRenderCanvases += Sample;
                foreach (FeedbackPlayer p in players)
                {
                    if (p.Runtime != null && p.Runtime.Binding(0) != null)
                        prepared++;
                    if (p.Play() != default)
                        accepted++;
                }
                try
                {
                    yield return Perf.Settled("(x) light 2d intensity " + n, n, 0, s_Baseline, 900);
                }
                finally
                {
                    Canvas.willRenderCanvases -= Sample;
                }
                Perf.Window w = Perf.s_Last;
                Assert.That(moved, Is.GreaterThan(0.1f), "the plays write the light — the positive");
                Assert.That(prepared + " · " + accepted, Is.EqualTo(n + " · " + n), "prepared · accepted");
                Assert.That(w.Frames + " · " + w.Counted, Is.EqualTo(Perf.Frames + " · " + Perf.Frames), "the recorders live — frames · markers");
                Assert.That(w.PerPlay, Is.GreaterThan(0), "per play ns (written down)");
                Perf.RowBudget("(x) light 2d intensity " + n, 900);
                if (Perf.k_Judge)
                    Assert.That(Math.Abs(w.Alloc - s_Baseline), Is.LessThanOrEqualTo(Perf.Equal), "the window " + w.Alloc + " vs " + s_Baseline + " B");
                foreach (FeedbackPlayer p in players)
                    p.Stop();
            }
            finally
            {
                FeedbackScheduler.Default = saved;
                if (!test.IsShutdown)
                    test.Shutdown();
            }
        }
    }
}
