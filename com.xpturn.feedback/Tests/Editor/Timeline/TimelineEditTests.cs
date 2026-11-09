using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // a Feedback track in edit mode — it writes nothing outside a preview; a clip's seed and capabilities
    public sealed class TimelineEditTests
    {
        static readonly List<string> s_Logs = new List<string>();
        readonly List<Object> m_Made = new List<Object>();

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type != LogType.Log)
                s_Logs.Add(type + ": " + condition);
        }

        [SetUp]
        public void SetUp()
        {
            s_Logs.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
        }

        TimelineAsset Asset(out FeedbackTrack track)
        {
            var asset = ScriptableObject.CreateInstance<TimelineAsset>();
            m_Made.Add(asset);
            track = asset.CreateTrack<FeedbackTrack>(null, "Feedback");
            return asset;
        }

        static ClipCaps Caps(TimelineClip clip) => ((ITimelineClipAsset)clip.asset).clipCaps;

        [Test]
        public void TE01_EditModeWritesNothing()
        {
            var go = new GameObject("Edit");
            m_Made.Add(go);
            var player = go.AddComponent<FeedbackPlayer>();
            TweenParams tween = TweenParams.Default;
            tween.m_To = new Vector4(1f, 0f, 0f, 0f);
            var item = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = new TweenMotion { m_Params = tween } };
            item.m_Timing.m_Duration = 1f;
            player.m_Items.Add(item);
            var d = go.AddComponent<PlayableDirector>();
            d.playOnAwake = false;
            d.timeUpdateMode = DirectorUpdateMode.Manual;
            TimelineAsset asset = Asset(out FeedbackTrack track);
            TimelineClip driven = track.CreateClip<FeedbackClip>();
            driven.start = 0.0;
            driven.duration = 1.0;
            ((FeedbackClip)driven.asset).m_Timing = FeedbackClipTiming.Driven;
            TimelineClip triggered = track.CreateClip<FeedbackClip>();
            triggered.start = 2.0;
            triggered.duration = 1.0;
            ((FeedbackClip)triggered.asset).m_Timing = FeedbackClipTiming.Triggered;
            d.playableAsset = asset;
            d.SetGenericBinding(track, player);
            d.RebuildGraph();
            foreach (double t in new[] { 0.5, 2.5, 0.2, 4.0 })
            {
                d.time = t;
                d.Evaluate();
            }
            Assert.That((player.IsPlaying, go.transform.localPosition), Is.EqualTo((false, Vector3.zero)));
            Assert.That(s_Logs, Is.Empty, "no warning — not the edit mode Play's");
        }

        [Test]
        public void TE02_AClipMadeOnTheTrackDrawsASeed()
        {
            Asset(out FeedbackTrack track);
            uint a = ((FeedbackClip)track.CreateClip<FeedbackClip>().asset).m_Seed;
            uint b = ((FeedbackClip)track.CreateClip<FeedbackClip>().asset).m_Seed;
            Assert.That((a != 0u, b != 0u, a != b), Is.EqualTo((true, true, true)), a + " · " + b);
        }

        [Test]
        public void TE03_ExtrapolationIsADrivenClipsOnly()
        {
            Asset(out FeedbackTrack track);
            var got = new List<string>();
            foreach (FeedbackClipTiming timing in new[] { FeedbackClipTiming.Auto, FeedbackClipTiming.Driven, FeedbackClipTiming.Triggered })
            {
                foreach (bool loop in new[] { false, true })
                {
                    TimelineClip clip = track.CreateClip<FeedbackClip>();
                    var asset = (FeedbackClip)clip.asset;
                    asset.m_Timing = timing;
                    asset.m_Loop = loop;
                    typeof(TimelineClip).GetProperty("postExtrapolationMode").GetSetMethod(true).Invoke(clip, new object[] { TimelineClip.ClipExtrapolation.Hold });
                    got.Add($"{timing} {(loop ? "loop" : "-")} {Caps(clip)} {clip.postExtrapolationMode}");
                }
            }
            Assert.That(got, Is.EqualTo(new[]
            {
                "Auto - ClipIn, SpeedMultiplier None",
                "Auto loop Looping, ClipIn, SpeedMultiplier None",
                "Driven - Extrapolation, ClipIn, SpeedMultiplier Hold",
                "Driven loop Looping, Extrapolation, ClipIn, SpeedMultiplier Hold",
                "Triggered - ClipIn, SpeedMultiplier None",
                "Triggered loop Looping, ClipIn, SpeedMultiplier None",
            }));
        }
    }
}
