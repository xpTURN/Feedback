using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // the value slot's consumption stages in real frames: Default is a test scheduler, the capture delta mixes frames with and
    // without FixedUpdate, and TearDown puts Default, the capture delta and the player loop back after a red test too. A runner
    // in Default (a binding's place) feeds both slots each frame; the targets record what FixedUpdate · Update · LateUpdate saw
    // and a loop system at the end of PostLateUpdate (the window) what the render gets. No UnityEditor here
    public class LayerStagePlayTests
    {
        struct StageWindow { }

        // o(f) = 0.25 × (f mod 7 + 1) — 1 + o is exact, and the offsets of neighbouring frames differ
        sealed class OffsetRunner : IPlaybackRunner
        {
            readonly TestFloatSlot m_Update;
            readonly ContributionHandle m_UpdateHandle;
            readonly TestFloatSlot m_LateApply;
            readonly ContributionHandle m_LateApplyHandle;
            internal readonly Dictionary<int, float> Offsets = new Dictionary<int, float>();

            internal OffsetRunner(TestFloatSlot update, ContributionHandle updateHandle, TestFloatSlot lateApply, ContributionHandle lateApplyHandle)
            {
                m_Update = update;
                m_UpdateHandle = updateHandle;
                m_LateApply = lateApply;
                m_LateApplyHandle = lateApplyHandle;
            }

            public bool Advance(int state, in FrameTime time)
            {
                int f = Time.frameCount;
                float o = 0.25f * (f % 7 + 1);
                m_Update.SetOffset(m_UpdateHandle, o);
                m_LateApply.SetOffset(m_LateApplyHandle, o);
                Offsets[f] = o;
                return true;
            }

            public void Abort(int state, AbortReason reason)
            {
            }

            public Object DebugOwner => null;
        }

        static FeedbackScheduler s_Saved;
        static FeedbackScheduler s_Test;
        static Action s_Window;
        readonly List<Object> m_Made = new List<Object>();

        static void OnWindow() => s_Window?.Invoke();

        static void SetWindow(bool on)
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate))
                    continue;
                var list = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList);
                list.RemoveAll(s => s.type == typeof(StageWindow));
                if (on)
                    list.Add(new PlayerLoopSystem { type = typeof(StageWindow), updateDelegate = OnWindow });
                loop.subSystemList[i].subSystemList = list.ToArray();
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SetWindow(false);
            s_Window = null;
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
            yield return null;
        }

        static bool B(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        LayerTestTarget Target(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            var target = go.AddComponent<LayerTestTarget>();
            target.Value = 1f;
            return target;
        }

        // an Update-stage and a LateApply test float slot (base 1) fed by the runner for 40 frames with and without
        // FixedUpdate — the Update-stage slot's LateUpdate sees 1 + o(f) of that frame, FixedUpdate · Update see 1 for both, the LateApply
        // slot's LateUpdate sees 1, the window sees both composites; the frame the contributions end shows 1 in LateUpdate and
        // the window, and the end is 1 bit for bit
        [UnityTest]
        public IEnumerator UP01_ConsumptionStagesInRealFrames()
        {
            Time.captureDeltaTime = 1f / 90f;
            s_Saved = FeedbackScheduler.Default;
            s_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = s_Test;
            var layers = s_Test.Context.Layers;
            LayerTestTarget u = Target("update"), l = Target("lateApply");
            var su = layers.GetOrCreate(u, 0, 0, () => new TestFloatSlot(u, ConsumptionStage.Update, Composition.Additive, false));
            var sl = layers.GetOrCreate(l, 0, 0, () => new TestFloatSlot(l, ConsumptionStage.LateApply, Composition.Additive, false));
            su.Reserve(1);
            sl.Reserve(1);
            var hu = su.Begin(ContributionKind.Offset);
            var hl = sl.Begin(ContributionKind.Offset);
            var runner = new OffsetRunner(su, hu, sl, hl);
            s_Test.Reserve(1);
            PlaybackHandle handle = s_Test.Add(runner, 0);
            var window = new List<(int Frame, float Update, float LateApply)>();
            s_Window = () => window.Add((Time.frameCount, u.Value, l.Value));
            SetWindow(true);
            yield return null;
            u.Seen.Clear();
            l.Seen.Clear();
            window.Clear();
            int first = Time.frameCount;
            for (int f = 0; f < 40; f++)
                yield return null;
            int last = Time.frameCount;
            s_Test.Remove(handle);
            su.End(hu);
            sl.End(hl);
            for (int f = 0; f < 3; f++)
                yield return null;
            SetWindow(false);

            // the frames between — the runner joined the frame before the first, and the contributions end in the last
            int span = last - first - 1;
            var fixedFrames = new HashSet<int>();
            int lateComposite = 0;
            var notBase = new List<string>();
            foreach (var (frame, where, value) in u.Seen)
            {
                if (frame <= first || frame >= last)
                    continue;
                if (where == "LateUpdate")
                {
                    if (runner.Offsets.TryGetValue(frame, out float o) && B(value, 1f + o))
                        lateComposite++;
                    continue;
                }
                if (where == "FixedUpdate")
                    fixedFrames.Add(frame);
                if (!B(value, 1f))
                    notBase.Add($"update {where} {frame}: {value}");
            }
            foreach (var (frame, where, value) in l.Seen)
            {
                if (frame > first && frame < last && !B(value, 1f))
                    notBase.Add($"lateApply {where} {frame}: {value}");
            }
            int windowComposite = window.FindAll(e => e.Frame > first && e.Frame < last && runner.Offsets.TryGetValue(e.Frame, out float o)
                && B(e.Update, 1f + o) && B(e.LateApply, 1f + o)).Count;
            Assert.That(lateComposite, Is.EqualTo(span), "the Update slot's LateUpdate sees 1 + o(f) of that frame");
            Assert.That(notBase, Is.Empty, "FixedUpdate · Update and the LateApply slot's LateUpdate see the base");
            Assert.That(fixedFrames.Count > 0 && fixedFrames.Count < span, $"frames with FixedUpdate {fixedFrames.Count} of {span}");
            Assert.That(windowComposite, Is.EqualTo(span), "the window sees both composites");
            Assert.That(u.Seen.Exists(e => e.Frame == last && e.Where == "LateUpdate" && B(e.Value, 1f)), "the end frame's LateUpdate sees 1");
            Assert.That(window.Exists(e => e.Frame == last && B(e.Update, 1f) && B(e.LateApply, 1f)), "the end frame's window sees 1");
            Assert.That(B(u.Value, 1f) && B(l.Value, 1f), $"end — {u.Value} · {l.Value}");
        }
    }
}
