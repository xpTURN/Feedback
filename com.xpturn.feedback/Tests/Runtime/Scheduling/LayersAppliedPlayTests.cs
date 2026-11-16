using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // the end of LateApply in the player loop: the default scheduler raises one event a frame, after its late layers wrote —
    // the position read there is the one at the end of PostLateUpdate (a loop system — WaitForEndOfFrame never resumes in
    // batch mode). TearDown puts the loop and the capture delta back after a red test too. No UnityEditor here
    public sealed class LayersAppliedPlayTests
    {
        struct EndReader { }

        static Transform s_Target;
        static Vector3 s_AtEnd;
        static int s_EndFrame = -1;
        readonly List<Object> m_Made = new List<Object>();

        static void OnEnd()
        {
            if (s_Target == null)
                return;
            s_AtEnd = s_Target.localPosition;
            s_EndFrame = Time.frameCount;
        }

        static void SetReader(bool on)
        {
            PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.PostLateUpdate))
                    continue;
                var list = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList);
                list.RemoveAll(s => s.type == typeof(EndReader));
                if (on)
                    list.Add(new PlayerLoopSystem { type = typeof(EndReader), updateDelegate = OnEnd });
                loop.subSystemList[i].subSystemList = list.ToArray();
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SetReader(false);
            s_Target = null;
            s_EndFrame = -1;
            Time.captureDeltaTime = 0f;
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Made.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator LA01_OneAFrameAfterTheLateLayers()
        {
            Time.captureDeltaTime = 1f / 60f;
            var go = new GameObject("LA01");
            m_Made.Add(go);
            var player = go.AddComponent<FeedbackPlayer>();
            var item = new PositionItem
            {
                m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" },
                m_Motion = new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = new Vector4(0.3f, 0.3f, 0.3f, 0f), m_Frequency = 10f } },
            };
            item.m_Timing.m_Duration = 2f;
            player.m_Items.Add(item);
            yield return null;
            s_Target = go.transform;
            SetReader(true);
            var frames = new List<int>();
            var atEvent = new Dictionary<int, Vector3>();
            Action<FeedbackScheduler> handler = s =>
            {
                if (s != FeedbackScheduler.Default)
                    return;
                frames.Add(Time.frameCount);
                atEvent[Time.frameCount] = go.transform.localPosition;
            };
            FeedbackDiagnostics.LayersApplied += handler;
            int compared = 0, differ = 0, moved = 0;
            try
            {
                player.Play();
                for (int i = 0; i < 14; i++)
                {
                    yield return null;
                    // the end of the last frame, read in this one's Update
                    if (s_EndFrame >= 0 && atEvent.TryGetValue(s_EndFrame, out Vector3 v))
                    {
                        compared++;
                        if (v != s_AtEnd)
                            differ++;
                        if (v != Vector3.zero)
                            moved++;
                    }
                }
            }
            finally
            {
                FeedbackDiagnostics.LayersApplied -= handler;
            }
            Assert.That(frames.Count, Is.GreaterThanOrEqualTo(12));
            Assert.That(frames.Distinct().Count(), Is.EqualTo(frames.Count), "one a frame");
            Assert.That(compared, Is.GreaterThan(10));
            Assert.That(moved, Is.GreaterThan(5), "the shake moved the target");
            Assert.That(differ, Is.Zero, "the value at the end of the frame");
        }
    }
}
