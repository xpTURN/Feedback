using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the Video Player's play state (Particle Control's form) · a seek · the preview skips. A module type the main editor
    // tests name; the Video assembly is the dev project's (DevProject). The clip is made here (MediaEncoder); the player's
    // state changes on its own clock, waited for on the wall clock
    [Category(OptionalAssemblyTests.DevProjectCategory)]
    public sealed class VideoTargetsTests
    {
        const string ClipPath = "Assets/VideoTargetsTests.webm";

        static VideoClip MakeClip()
        {
            var attrs = new VideoTrackAttributes { frameRate = new MediaRational(10), width = 32, height = 32, includeAlpha = false, bitRateMode = VideoBitrateMode.Low };
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            using (var enc = new MediaEncoder(Path.GetFullPath(ClipPath), attrs))
            {
                for (int f = 0; f < 20; f++)
                {
                    var px = new Color32[32 * 32];
                    for (int i = 0; i < px.Length; i++)
                        px[i] = new Color32((byte)(f * 12), 0, 0, 255);
                    tex.SetPixels32(px);
                    tex.Apply();
                    enc.AddFrame(tex);
                }
            }
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(ClipPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<VideoClip>(ClipPath);
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }
        }

        // the player's state changes on its own clock — a batch EditMode frame is far shorter (a 120-frame wait is about 0.3 s)
        static IEnumerator Until(System.Func<bool> done, double seconds)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!done() && clock.Elapsed.TotalSeconds < seconds)
                yield return Frames(1);
        }

        [UnityTest]
        public IEnumerator VD18_PlayStateAndSeek()
        {
            var go = new GameObject("video");
            var vp = go.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.renderMode = VideoRenderMode.APIOnly;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            var schedulers = new List<FeedbackScheduler>();
            var players = new List<FeedbackPlayer>();
            try
            {
                vp.clip = MakeClip();
                vp.Prepare();
                yield return Until(() => vp.isPrepared, 10.0);
                Assert.That(vp.isPrepared, Is.True, "the clip prepares");
                // the engine's positive: a direct play starts in this session — a long batch session sometimes starts none (a fresh
                // player neither), our code apart: no judgment then
                vp.Play();
                yield return Until(() => vp.isPlaying, 5.0);
                bool engine = vp.isPlaying;
                vp.Stop();
                yield return Until(() => !vp.isPlaying, 5.0);
                if (!engine)
                    Assert.Ignore("the engine started no video in this session (a direct play — the positive)");
                vp.Prepare();
                yield return Until(() => vp.isPrepared, 10.0);
                FeedbackPlayer Make(FeedbackScheduler s, VideoAction action, float time = 0f)
                {
                    var host = new GameObject("p").AddComponent<FeedbackPlayer>();
                    var item = new VideoPlayerControlItem { m_Action = action, m_Time = time, m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = vp, m_Path = "", m_Anchor = "" }, m_Motion = new SetMotion() };
                    item.m_Timing.m_Duration = 0f;
                    host.m_Items.Add(item);
                    // a punch on the player's own position keeps the play running (a Revert stop has a play to stop)
                    var keeper = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = new PunchMotion() };
                    keeper.m_Timing.m_Duration = 30f;
                    host.m_Items.Add(keeper);
                    host.Bind(s);
                    host.EnsureRuntime();
                    players.Add(host);
                    return host;
                }
                var sched = new FeedbackScheduler(ContextKind.Test);
                schedulers.Add(sched);
                FeedbackPlayer play = Make(sched, VideoAction.Play);
                play.Play();
                sched.Tick(0.05f);
                yield return Until(() => vp.isPlaying, 5.0);
                bool playing = vp.isPlaying;
                play.Stop(StopMode.Revert);
                sched.Tick(0f);
                yield return Until(() => !vp.isPlaying, 5.0);
                bool back = !vp.isPlaying;
                vp.Play();
                yield return Until(() => vp.isPlaying, 5.0);
                bool direct = vp.isPlaying;
                FeedbackPlayer pause = Make(sched, VideoAction.Pause);
                pause.Play();
                sched.Tick(0.05f);
                yield return Until(() => vp.isPaused, 5.0);
                bool paused = vp.isPaused;
                pause.Stop(StopMode.Revert);
                sched.Tick(0f);
                yield return Until(() => vp.isPlaying, 5.0);
                bool resumed = vp.isPlaying;
                bool seeked = false;
                vp.seekCompleted += _ => seeked = true;
                FeedbackPlayer seek = Make(sched, VideoAction.Seek, 1f);
                seek.Play();
                sched.Tick(0.05f);
                yield return Until(() => seeked, 5.0);
                var preview = new FeedbackScheduler(ContextKind.Preview);
                schedulers.Add(preview);
                vp.Stop();
                yield return Until(() => !vp.isPlaying, 5.0);
                FeedbackPlayer skipped = Make(preview, VideoAction.Play);
                skipped.Play();
                preview.Tick(0.05f);
                yield return Until(() => vp.isPlaying, 2.0);
                bool skippedStill = !vp.isPlaying;
                // a pause judged over a play (a player that never started reads as paused too)
                Assert.That((playing, back, direct, paused, resumed, seeked, skippedStill), Is.EqualTo((true, true, true, true, true, true, true)),
                    "playing · back · playing again · paused · resumed · seeked · the preview skipped");
            }
            finally
            {
                foreach (FeedbackPlayer p in players)
                {
                    if (p != null)
                    {
                        p.ReleaseRuntime();
                        Object.DestroyImmediate(p.gameObject);
                    }
                }
                foreach (FeedbackScheduler s in schedulers)
                {
                    if (!s.IsShutdown)
                        s.Shutdown();
                }
                Object.DestroyImmediate(go);
                AssetDatabase.DeleteAsset(ClipPath);
            }
        }
    }
}
