using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // the light row · the toggle's row in real frames: Default is a test scheduler, the capture delta mixes frames with and
    // without FixedUpdate, and TearDown puts Default and the capture delta back after a red test too. Both rows look through
    // LateUpdate and OnEnable — no loop window. No UnityEditor here
    public class LayerLightTogglePlayTests
    {
        static FeedbackScheduler s_Saved;
        static FeedbackScheduler s_Test;
        readonly List<Object> m_Made = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (s_Test != null)
            {
                FeedbackScheduler.Default = s_Saved;
                s_Test.Shutdown();
                s_Test = null;
                s_Saved = null;
            }
            Time.captureDeltaTime = 0f;
            foreach (Object o in m_Made)
                if (o != null)
                    Object.Destroy(o);
            m_Made.Clear();
            FrameRecorder.Clear();
            yield return null;
        }

        PropertyLayerRegistry Swap()
        {
            Time.captureDeltaTime = 1f / 90f;
            s_Saved = FeedbackScheduler.Default;
            s_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = s_Test;
            return s_Test.Context.Layers;
        }

        GameObject G(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        static bool Bits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        // two lights — 0.2 + offset −0.5, and 1.3 with a sine offset and a blink (override) — over 30 frames with and
        // without FixedUpdate: LateUpdate sees the base during the effect (LateApply is after it) and the end is the base bit
        // for bit
        [UnityTest]
        public IEnumerator LP01_LightInRealFrames()
        {
            var layers = Swap();
            m_Made.Add(new GameObject(nameof(FrameRecorder), typeof(FrameRecorder)));
            var g1 = G("light1");
            var g2 = G("light2");
            var l1 = g1.AddComponent<Light>();
            var l2 = g2.AddComponent<Light>();
            l1.intensity = 0.2f;
            l2.intensity = 1.3f;
            var w1 = g1.AddComponent<LayerToggleTarget>();
            var w2 = g2.AddComponent<LayerToggleTarget>();
            w1.Light = l1;
            w2.Light = l2;
            var s1 = layers.GetOrCreate(l1, 0, 0, () => new LightIntensitySlot(l1));
            var s2 = layers.GetOrCreate(l2, 0, 0, () => new LightIntensitySlot(l2));
            s1.Reserve(1);
            s2.Reserve(2);
            var h1 = s1.Begin(ContributionKind.Offset);
            var h2 = s2.Begin(ContributionKind.Offset);
            var h2b = s2.Begin(ContributionKind.Override);
            FrameRecorder.Clear();
            w1.Seen.Clear();
            w2.Seen.Clear();
            int first = Time.frameCount;
            for (int f = 0; f < 30; f++)
            {
                s1.SetOffset(h1, -0.5f);
                s2.SetOffset(h2, 0.4f * Mathf.Sin(f * 0.7f));
                s2.SetOverride(h2b, 5f, f % 4 < 2 ? 1f : 0f);
                yield return null;
            }
            int last = Time.frameCount;
            s1.End(h1);
            s2.End(h2);
            s2.End(h2b);
            yield return null;
            yield return null;
            yield return null;

            var fixedFrames = new HashSet<int>();
            for (int i = 0; i < FrameRecorder.Record.Count; i++)
                if (FrameRecorder.Record[i] == "MB.FixedUpdate" && FrameRecorder.Frames[i] > first && FrameRecorder.Frames[i] < last)
                    fixedFrames.Add(FrameRecorder.Frames[i]);
            int span = last - first - 1;
            Assert.That(fixedFrames.Count > 0 && fixedFrames.Count < span, $"frames with FixedUpdate {fixedFrames.Count} of {span}");
            int seen = 0, composed = 0;
            foreach (var (frame, intensity) in w1.Seen)
            {
                if (frame < first || frame > last)
                    continue;
                seen++;
                if (!Bits(intensity, 0.2f))
                    composed++;
            }
            foreach (var (frame, intensity) in w2.Seen)
            {
                if (frame < first || frame > last)
                    continue;
                seen++;
                if (!Bits(intensity, 1.3f))
                    composed++;
            }
            Assert.That(seen, Is.GreaterThan(50), "LateUpdates seen");
            Assert.That(composed, Is.EqualTo(0), "LateUpdates that saw the composed value");
            Assert.That(Bits(l1.intensity, 0.2f), $"light 1 end — {l1.intensity}");
            Assert.That(Bits(l2.intensity, 1.3f), $"light 2 end — {l2.intensity}");
        }

        // the toggle's row — an object turned on in the Update stage gets that frame's LateUpdate, one turned off does not;
        // OnEnable once while the blink holds
        [UnityTest]
        public IEnumerator LP02_ToggleInTheUpdateStage()
        {
            var layers = Swap();
            var x = G("x");
            var tx = x.AddComponent<LayerToggleTarget>();
            x.SetActive(false);
            var y = G("y");
            var ty = y.AddComponent<LayerToggleTarget>();
            yield return null;
            yield return null;
            var sx = layers.GetOrCreate(x, 0, 0, () => new GameObjectActiveSlot(x));
            var sy = layers.GetOrCreate(y, 0, 0, () => new GameObjectActiveSlot(y));
            sx.Reserve(1);
            sy.Reserve(1);
            int enables = tx.Enables;
            var hx = sx.Begin();
            var hy = sy.Begin();
            sx.SetOverride(hx, true, 1f);
            sy.SetOverride(hy, false, 1f);
            for (int f = 0; f < 6; f++)
                yield return null;
            Assert.That(x.activeSelf, "x on");
            Assert.That(!y.activeSelf, "y off");
            Assert.That(tx.Enables - enables, Is.EqualTo(1), "x OnEnable");
            Assert.That(tx.FirstLateAfterEnable, Is.EqualTo(tx.EnabledFrame), "x's first LateUpdate is in the frame it was turned on");
            Assert.That(ty.LateFrames.Contains(ty.DisabledFrame), Is.False, $"y got a LateUpdate in the frame it was turned off ({ty.DisabledFrame})");
            sx.End(hx);
            sy.End(hy);
            yield return null;
            yield return null;
            Assert.That(!x.activeSelf && y.activeSelf, "the befores");
        }
    }
}
