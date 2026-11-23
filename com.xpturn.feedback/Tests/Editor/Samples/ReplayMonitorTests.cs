using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.Video;
using xpTURN.Feedback.Samples;
using static xpTURN.Feedback.Tests.Editor.Samples.SampleKitTestSupport;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace xpTURN.Feedback.Tests.Editor.Samples
{
    // S16 Replay Monitor: the card, the clip and the Video Player as saved · the hit freeze, chapters, replay, power, hold and
    // their states on the real video · Before · the transitions during a running play · the hidden first frames · no allocation.
    // A session whose engine starts no video skips the play tests (a direct start is the positive control). The lab's assembly
    // is optional, so it is reached by name; a play-mode iterator captures no local in a lambda (the reload)
    public sealed class ReplayMonitorTests
    {
        const string LabType = "xpTURN.Feedback.Samples.GameFeelLabs.ReplayMonitorLab, xpTURN.Feedback.Samples.GameFeelLabs.ReplayMonitor";
        const string NoVideo = "the engine started no video in this session";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly string[] s_PlayerNames = { "Power On", "Power Off", "Hit Freeze", "Chapter A", "Chapter B", "Chapter C", "Instant Replay", "Hold" };
        static readonly string[] s_Inputs = { "HitFreeze", "NextChapter", "InstantReplay", "Power", "HoldFrame", "StopHold", "RestoreHold" };
        static VideoPlayer s_Video;
        static Component s_Lab;
        static bool s_Started;
        static bool s_Arrived;
        static int s_Plays;
        static bool s_PreparedStopped;
        static string s_StateAtPrepared;
        static Action s_Tick;

        static string ScenePath()
        {
            string path = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "ReplayMonitor");
            if (path == null)
                Assert.Ignore("no Game Feel Labs bundle in this project");
            if (Type.GetType(LabType) == null)
                Assert.Ignore("no Video module in this project");
            return path;
        }

        static void Find()
        {
            s_Video = UnityEngine.Object.FindAnyObjectByType<VideoPlayer>();
            s_Lab = Resources.FindObjectsOfTypeAll<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "ReplayMonitorLab" && m.gameObject.scene.isLoaded);
        }

        static IEnumerator Open()
        {
            s_Started = false;
            EditorSceneManager.LoadSceneInPlayMode(ScenePath(), new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            Find();
            float until = Time.realtimeSinceStartup + 8f;
            while (!(s_Video.isPlaying && State() == "Running") && Time.realtimeSinceStartup < until)
                yield return null;
            s_Started = s_Video.isPlaying && State() == "Running";
        }

        static string State() => s_Lab.GetType().GetProperty("State").GetValue(s_Lab).ToString();

        static void Call(string method) => s_Lab.GetType().GetMethod(method).Invoke(s_Lab, null);

        static FeedbackPlayer Player(string name) => GameObject.Find("Players/" + name).GetComponent<FeedbackPlayer>();

        static Light BackLight() => GameObject.Find("Back Light").GetComponent<Light>();

        static IEnumerator Real(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
                yield return null;
        }

        // a Seek is read a few frames later: waits until the time is at the target or just past it — for less time than the
        // video would need to play there by itself
        static IEnumerator Arrive(double target)
        {
            s_Arrived = false;
            float until = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < until)
            {
                if (s_Video.time >= target - 0.1 && s_Video.time <= target + 0.4)
                {
                    s_Arrived = true;
                    break;
                }
                yield return null;
            }
            yield return null;
            yield return null;
        }

        static IEnumerator UntilPlaying()
        {
            float until = Time.realtimeSinceStartup + 3f;
            while (!s_Video.isPlaying && Time.realtimeSinceStartup < until)
                yield return null;
        }

        static void OnPlayed(FeedbackPlayer p, PlaybackHandle h) => s_Plays++;

        static void CountPlays()
        {
            s_Plays = 0;
            foreach (string name in s_PlayerNames)
            {
                FeedbackPlayer p = Player(name);
                p.Played -= OnPlayed;
                p.Played += OnPlayed;
            }
        }

        static void Reset()
        {
            SampleState.SetAfter(true);
            SampleState.SetSampleTimeScale(1f);
        }

        // ---- the card, the asmdef and the Video Player as saved

        [Serializable]
        sealed class Asmdef
        {
            public string name = "";
            public string[] defineConstraints = new string[0];
        }

        [Test]
        public void RM01_TheBundleAndTheVideoPlayer()
        {
            SampleCatalog catalog = Catalogs().FirstOrDefault(c => c.Bundle == "Game Feel Labs");
            if (catalog == null)
                Assert.Ignore("no Game Feel Labs bundle in this project");
            Assert.That(catalog.Cards.Length, Is.EqualTo(7), "the bundle's cards");
            SampleCard card = catalog.Cards[6];
            string scene = AssetDatabase.GUIDToAssetPath(card.SceneGuid);
            Assert.That((card.Title, Path.GetFileName(scene), card.RequiredType, card.RequiresText), Is.EqualTo(("Replay Monitor", "ReplayMonitor.unity", LabType, "Video module")));
            string folder = Disk(Path.GetDirectoryName(scene).Replace('\\', '/'));
            PackageInfo package = PackageInfo.FindForAssetPath("Packages/com.xpturn.feedback");
            Assert.That(File.ReadAllText(Path.Combine(package.resolvedPath, "package.json")).Contains("S16 Replay Monitor"), Is.True, "the samples entry names S16");
            string text = File.ReadAllText(Directory.GetFiles(folder, "*.asmdef").Single());
            var asmdef = JsonUtility.FromJson<Asmdef>(text);
            Assert.That(asmdef.name, Is.EqualTo("xpTURN.Feedback.Samples.GameFeelLabs.ReplayMonitor"));
            Assert.That(asmdef.defineConstraints, Is.EquivalentTo(new[] { "XPTURN_FEEDBACK_SAMPLES_INPUTSYSTEM", "XPTURN_FEEDBACK_SAMPLES_UGUI", "XPTURN_FEEDBACK_SAMPLES_URP", "XPTURN_FEEDBACK_SAMPLES_VIDEO" }));
            Assert.That(text.Contains("\"com.unity.modules.video\""), Is.True, "the Video module's version define");
            // without the module the scene stays closed: a later play would enter it and log its deleted Video Player
            if (Type.GetType(LabType) == null)
                return;

            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            VideoPlayer video = Resources.FindObjectsOfTypeAll<VideoPlayer>().Single(v => v.gameObject.scene.isLoaded);
            Assert.That(video.clip != null ? video.clip.name : "", Is.EqualTo("ReplayClip"));
            Assert.That(video.targetTexture != null ? video.targetTexture.name : "", Is.EqualTo("ReplayScreen"));
            Assert.That((video.source, video.renderMode, video.audioOutputMode, video.isLooping, video.playOnAwake, video.waitForFirstFrame, video.timeUpdateMode),
                Is.EqualTo((VideoSource.VideoClip, VideoRenderMode.RenderTexture, VideoAudioOutputMode.None, true, true, true, VideoTimeUpdateMode.GameTime)));
            Material material = video.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That((material.shader.name, material.GetTexture("_BaseMap") == video.targetTexture), Is.EqualTo(("Universal Render Pipeline/Unlit", true)), "the screen shows the render texture");
        }

        [Test]
        public void RM02_TheClip()
        {
            string scene = ScenePath();
            string path = Path.GetDirectoryName(scene).Replace('\\', '/') + "/ReplayClip.webm";
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(path);
            var importer = AssetImporter.GetAtPath(path) as VideoClipImporter;
            Assert.That(clip != null && importer != null, Is.True, "an imported Video Clip");
            Assert.That((clip.width, clip.height, clip.frameCount, Mathf.RoundToInt((float)clip.frameRate), clip.audioTrackCount), Is.EqualTo((480u, 270u, 144ul, 24, (ushort)0)));
            Assert.That(importer.defaultTargetSettings.enableTranscoding, Is.False, "no transcoding");
            Assert.That(new FileInfo(Disk(path)).Length, Is.LessThanOrEqualTo(300 * 1024), "the size budget");
        }

        // ---- the hit freeze: the picture stands for the hit stop and plays on, also after a mash

        [UnityTest]
        public IEnumerator RM05_HitFreeze()
        {
            yield return new EnterPlayMode();
            yield return Open();
            if (!s_Started)
            {
                yield return new ExitPlayMode();
                Assert.Ignore(NoVideo);
            }
            (int e0, int w0) = Counts();
            s_Video.time = 1.0;
            yield return Arrive(1.0);
            Call("HitFreeze");
            yield return null;
            yield return null;
            yield return null;
            bool paused = s_Video.isPaused;
            double stood = s_Video.time, drift = 0;
            float from = Time.time;
            float limit = Time.realtimeSinceStartup + 2f;
            while (!s_Video.isPlaying && Time.realtimeSinceStartup < limit)
            {
                drift = Math.Max(drift, Math.Abs(s_Video.time - stood));
                yield return null;
            }
            float frozen = Time.time - from;
            bool resumed = s_Video.isPlaying;
            yield return Real(0.2f);
            bool moves = s_Video.time > stood + 0.05;
            for (int i = 0; i < 6; i++)
            {
                Call("HitFreeze");
                yield return Real(0.05f);
            }
            yield return Real(0.45f);
            bool afterMash = s_Video.isPlaying;
            (int e1, int w1) = Counts();
            yield return new ExitPlayMode();
            Assert.That((paused, resumed, moves, afterMash), Is.EqualTo((true, true, true, true)), "stands · plays on · moves · after a mash");
            Assert.That(drift, Is.LessThan(0.05), "the picture stands");
            Assert.That(frozen, Is.InRange(0.1f, 0.6f), "the freeze's length on the game clock");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console");
        }

        // ---- the chapters in turn, from the end back to the first

        [UnityTest]
        public IEnumerator RM06_Chapters()
        {
            yield return new EnterPlayMode();
            yield return Open();
            if (!s_Started)
            {
                yield return new ExitPlayMode();
                Assert.Ignore(NoVideo);
            }
            s_Video.time = 0.5;
            yield return Arrive(0.5);
            Call("NextChapter");
            yield return Arrive(2.0);
            bool b = s_Arrived;
            Call("NextChapter");
            yield return Arrive(4.0);
            bool c = s_Arrived;
            Call("NextChapter");
            yield return Arrive(0.0);
            bool a = s_Arrived;
            bool playing = s_Video.isPlaying;
            yield return new ExitPlayMode();
            Assert.That((b, c, a, playing), Is.EqualTo((true, true, true, true)), "B · C · back to A · still playing");
        }

        // ---- the replay: back to 2 s, playing, its title up and gone

        [UnityTest]
        public IEnumerator RM07_InstantReplay()
        {
            yield return new EnterPlayMode();
            yield return Open();
            if (!s_Started)
            {
                yield return new ExitPlayMode();
                Assert.Ignore(NoVideo);
            }
            s_Video.time = 4.5;
            yield return Arrive(4.5);
            Text title = GameObject.Find("Replay Canvas/REPLAY").GetComponent<Text>();
            bool hiddenFirst = title.color.a < 0.01f;
            Call("InstantReplay");
            float peak = 0f, limit = Time.realtimeSinceStartup + 0.7f;
            while (Time.realtimeSinceStartup < limit)
            {
                peak = Mathf.Max(peak, title.color.a);
                yield return null;
            }
            yield return UntilPlaying();
            bool playing = s_Video.isPlaying;
            bool back = s_Video.time < 4.0;
            yield return Real(0.7f);
            float end = title.color.a;

            // its Play also ends a freeze that still holds the picture (the freeze's own Play is kept from coming)
            FeedbackPlayer freeze = Player("Hit Freeze");
            Call("HitFreeze");
            yield return null;
            yield return null;
            freeze.Pause();
            bool heldByFreeze = s_Video.isPaused;
            Call("InstantReplay");
            yield return UntilPlaying();
            bool resumed = s_Video.isPlaying;
            freeze.Stop();
            yield return new ExitPlayMode();
            Assert.That((heldByFreeze, resumed), Is.EqualTo((true, true)), "the replay's Play ends a held freeze");
            Assert.That((hiddenFirst, playing, back), Is.EqualTo((true, true, true)), "hidden first · playing · went back");
            Assert.That(peak, Is.GreaterThan(0.5f), "the title shows");
            Assert.That(end, Is.LessThan(0.02f), "the title goes");
        }

        // ---- power: off holds the place and refuses what would play again; on comes back from there with the light

        [UnityTest]
        public IEnumerator RM08_Power()
        {
            yield return new EnterPlayMode();
            yield return Open();
            if (!s_Started)
            {
                yield return new ExitPlayMode();
                Assert.Ignore(NoVideo);
            }
            float light0 = BackLight().intensity;
            s_Video.time = 1.0;
            yield return Arrive(1.0);
            Call("Power");
            yield return Real(0.45f);
            (string state, bool paused, double place, float dim) = (State(), s_Video.isPaused, s_Video.time, BackLight().intensity);
            CountPlays();
            Call("HitFreeze");
            Call("InstantReplay");
            Call("HoldFrame");
            Call("StopHold");
            Call("RestoreHold");
            int refused = s_Plays;
            yield return Real(0.2f);
            bool stillPaused = s_Video.isPaused && Math.Abs(s_Video.time - place) < 0.05;
            Call("Power");
            yield return Real(0.2f);
            (string on, bool playing, bool fromThere, float light1) = (State(), s_Video.isPlaying, s_Video.time >= place && s_Video.time < place + 0.8, BackLight().intensity);

            // Off in After, Before turned on, then on: nothing stays dimmed
            Call("Power");
            yield return Real(0.4f);
            float dimmedAgain = BackLight().intensity;
            SampleState.SetAfter(false);
            yield return null;
            Call("Power");
            yield return Real(0.2f);
            float light2 = BackLight().intensity;
            bool playing2 = s_Video.isPlaying;
            Reset();
            yield return new ExitPlayMode();
            Assert.That((state, paused, stillPaused, refused), Is.EqualTo(("Off", true, true, 0)), "off · holds · refuses");
            Assert.That(dim, Is.LessThan(light0 * 0.5f), "dimmed");
            Assert.That((on, playing, fromThere), Is.EqualTo(("Running", true, true)), "on from the same place");
            Assert.That(light1, Is.EqualTo(light0).Within(0.01f), "the light comes back");
            Assert.That(dimmedAgain, Is.LessThan(light0 * 0.5f), "dimmed again");
            Assert.That((light2, playing2), Is.EqualTo((light0, true)).Using<(float, bool)>((x, y) => Mathf.Abs(x.Item1 - y.Item1) < 0.01f && x.Item2 == y.Item2 ? 0 : 1), "on in Before leaves nothing dimmed");
        }

        // ---- hold: a frame at 4 s · Revert plays on from there (a Seek is not undone) · Restore · its end plays on

        [UnityTest]
        public IEnumerator RM09_HoldRevertRestore()
        {
            yield return new EnterPlayMode();
            yield return Open();
            if (!s_Started)
            {
                yield return new ExitPlayMode();
                Assert.Ignore(NoVideo);
            }
            s_Video.time = 1.0;
            yield return Arrive(1.0);
            Call("HoldFrame");
            yield return Real(0.4f);
            (string held, bool paused, double at) = (State(), s_Video.isPaused, s_Video.time);
            Call("StopHold");
            yield return UntilPlaying();
            yield return Real(0.2f);
            (string reverted, bool playing, double after) = (State(), s_Video.isPlaying, s_Video.time);

            s_Video.time = 1.0;
            yield return Arrive(1.0);
            Call("HoldFrame");
            yield return Real(0.4f);
            bool pausedAgain = s_Video.isPaused;
            Call("RestoreHold");
            yield return UntilPlaying();
            (string restored, bool playing2) = (State(), s_Video.isPlaying);

            // the play's own end, sped up
            s_Video.time = 1.0;
            yield return Arrive(1.0);
            Call("HoldFrame");
            yield return Real(0.2f);
            SampleState.SetSampleTimeScale(16f);
            float limit = Time.realtimeSinceStartup + 4f;
            while (Player("Hold").IsPlaying && Time.realtimeSinceStartup < limit)
                yield return null;
            Reset();
            yield return null;
            yield return null;
            yield return UntilPlaying();
            (bool ended, string endState, bool endPlaying) = (!Player("Hold").IsPlaying, State(), s_Video.isPlaying);
            yield return new ExitPlayMode();
            Assert.That((held, paused), Is.EqualTo(("Held", true)), "held");
            Assert.That(at, Is.EqualTo(4.0).Within(0.09), "at 4 s");
            Assert.That((reverted, playing), Is.EqualTo(("Running", true)), "Revert plays on");
            Assert.That(after, Is.InRange(4.0, 5.2), "from 4 s, not from 0");
            Assert.That((pausedAgain, restored, playing2), Is.EqualTo((true, "Running", true)), "Restore");
            Assert.That((ended, endState, endPlaying), Is.EqualTo((true, "Running", true)), "the hold's end plays on");
        }

        // ---- Before: the freeze stands down and the juice with it · the commands still run · After brings the freeze back

        [UnityTest]
        public IEnumerator RM10_Before()
        {
            yield return new EnterPlayMode();
            yield return Open();
            if (!s_Started)
            {
                yield return new ExitPlayMode();
                Assert.Ignore(NoVideo);
            }
            Transform monitor = GameObject.Find("Monitor").transform;
            float light0 = BackLight().intensity;
            SampleState.SetAfter(false);
            yield return null;
            s_Video.time = 1.0;
            yield return Arrive(1.0);
            Call("HitFreeze");
            bool never = true, still = true;
            float limit = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < limit)
            {
                never &= s_Video.isPlaying;
                still &= (monitor.localScale - Vector3.one).sqrMagnitude < 1e-6f;
                yield return null;
            }
            Call("NextChapter");
            yield return Arrive(2.0);
            bool chapter = s_Arrived;
            Text title = GameObject.Find("Replay Canvas/REPLAY").GetComponent<Text>();
            Call("InstantReplay");
            float titlePeak = 0f;
            limit = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < limit)
            {
                titlePeak = Mathf.Max(titlePeak, title.color.a);
                yield return null;
            }
            Call("Power");
            yield return Real(0.4f);
            (bool off, float dim) = (s_Video.isPaused, BackLight().intensity);
            Call("Power");
            yield return Real(0.2f);
            SampleState.SetAfter(true);
            yield return null;
            Call("HitFreeze");
            yield return null;
            yield return null;
            yield return null;
            bool freezes = s_Video.isPaused;
            yield return UntilPlaying();
            Reset();
            yield return new ExitPlayMode();
            Assert.That((never, still), Is.EqualTo((true, true)), "Before: no freeze, no punch");
            Assert.That(titlePeak, Is.LessThan(0.01f), "Before: no replay title");
            Assert.That((chapter, off), Is.EqualTo((true, true)), "the commands run in Before");
            Assert.That(dim, Is.LessThan(light0 * 0.5f), "the off state shows in Before");
            Assert.That(freezes, Is.True, "After: the freeze");
        }

        // ---- Loading: nothing plays before the first playing frame, the prepared and still stopped one included

        static void OnPrepared(VideoPlayer v)
        {
            s_PreparedStopped = v.isPrepared && !v.isPlaying;
            s_Lab.GetType().GetMethod("Sync", Private).Invoke(s_Lab, null);
            s_StateAtPrepared = State();
            foreach (string input in s_Inputs)
                Call(input);
        }

        [UnityTest]
        public IEnumerator RM11_Loading()
        {
            yield return new EnterPlayMode();
            string path = ScenePath();
            EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            Find();
            s_PreparedStopped = false;
            s_StateAtPrepared = null;
            CountPlays();
            s_Video.prepareCompleted += OnPrepared;
            bool early = s_Video.isPlaying;
            float limit = Time.realtimeSinceStartup + 8f;
            while (!s_Video.isPlaying && Time.realtimeSinceStartup < limit)
            {
                foreach (string input in s_Inputs)
                    Call(input);
                yield return null;
            }
            s_Video.prepareCompleted -= OnPrepared;
            (bool started, int plays, bool prepared, string state) = (s_Video.isPlaying, s_Plays, s_PreparedStopped, s_StateAtPrepared);
            yield return null;
            yield return null;
            string after = State();
            yield return new ExitPlayMode();
            if (!started)
                Assert.Ignore(NoVideo);
            Assert.That(early, Is.False, "the check starts before the video");
            Assert.That((prepared, state), Is.EqualTo((true, "Loading")), "prepared and stopped is still Loading");
            Assert.That(plays, Is.EqualTo(0), "no input plays anything before the first playing frame");
            Assert.That(after, Is.EqualTo("Running"));
        }

        // ---- no allocation once warm: the lab's whole Update with a running video (the positive control allocates)

        static void Tick() => s_Tick();

        static void Control() => GC.KeepAlive(new object[4]);

        static bool Allocates(TestDelegate run)
        {
            for (int i = 0; i < 5; i++)
            {
                if (!UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory().ApplyTo(run).IsSuccess)
                    return false;
            }
            return true;
        }

        [UnityTest]
        public IEnumerator RM13_NoAllocation()
        {
            yield return new EnterPlayMode();
            yield return Open();
            if (!s_Started)
            {
                yield return new ExitPlayMode();
                Assert.Ignore(NoVideo);
            }
            s_Tick = (Action)Delegate.CreateDelegate(typeof(Action), s_Lab, s_Lab.GetType().GetMethod("Update", Private));
            for (int i = 0; i < 10; i++)
            {
                Tick();
                yield return null;
            }
            (bool update, bool control) = (Allocates(Tick), Allocates(Control));
            s_Tick = null;
            yield return new ExitPlayMode();
            Assert.That((update, control), Is.EqualTo((false, true)));
        }

        // ---- the gallery points here and holds no Video Player

        [Test]
        public void RM14_TheGalleryNote()
        {
            string path = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "FeedbackGallery");
            if (path == null)
                Assert.Ignore("no Basics bundle in this project");
            string text = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(Disk(path)), @"\s+", " ");
            Assert.That(text.Contains("Label: Video (1) Note: \"Video Player \\u2014 see Replay Monitor in Game Feel Labs (needs the Video module)\""), Is.True, "the note");
            Assert.That(text.Contains("--- !u!328 "), Is.False, "no Video Player in the gallery");
        }

        // ---- Off and Hold during a running freeze or replay: its Play comes before the new state, not inside it

        [UnityTest]
        public IEnumerator RM17_TransitionsDuringAPlay()
        {
            yield return new EnterPlayMode();
            yield return Open();
            if (!s_Started)
            {
                yield return new ExitPlayMode();
                Assert.Ignore(NoVideo);
            }
            Call("HitFreeze");
            yield return null;
            yield return null;
            bool freezing = s_Video.isPaused;
            Call("Power");
            yield return Real(0.45f);
            (string off, bool offPaused) = (State(), s_Video.isPaused);
            Call("Power");
            yield return Real(0.3f);

            Call("HitFreeze");
            yield return null;
            yield return null;
            Call("HoldFrame");
            yield return Real(0.45f);
            (string held, bool heldPaused) = (State(), s_Video.isPaused);
            Call("StopHold");
            yield return UntilPlaying();
            yield return Real(0.1f);
            (string reverted, bool playing) = (State(), s_Video.isPlaying);

            Call("InstantReplay");
            yield return null;
            Call("Power");
            yield return Real(0.3f);
            (string off2, bool off2Paused) = (State(), s_Video.isPaused);
            Call("Power");
            yield return Real(0.1f);
            yield return new ExitPlayMode();
            Assert.That(freezing, Is.True, "the freeze runs");
            Assert.That((off, offPaused), Is.EqualTo(("Off", true)), "Z in a freeze");
            Assert.That((held, heldPaused), Is.EqualTo(("Held", true)), "X in a freeze");
            Assert.That((reverted, playing), Is.EqualTo(("Running", true)), "its Revert plays");
            Assert.That((off2, off2Paused), Is.EqualTo(("Off", true)), "Z in a replay's gap");
        }

        // ---- the screen stays hidden until the first playing frame: the render texture still holds the last scene's picture

        [UnityTest]
        public IEnumerator RM18_TheScreenWaitsForThePicture()
        {
            yield return new EnterPlayMode();
            string path = ScenePath();
            EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            Find();
            Renderer screen = s_Video.GetComponent<Renderer>();
            int hidden = 0, shown = 0;
            float limit = Time.realtimeSinceStartup + 8f;
            while (State() == "Loading" && Time.realtimeSinceStartup < limit)
            {
                if (screen.enabled)
                    shown++;
                else
                    hidden++;
                yield return null;
            }
            bool started = State() == "Running";
            bool on = screen.enabled;
            yield return new ExitPlayMode();
            if (!started)
                Assert.Ignore(NoVideo);
            Assert.That((hidden > 0, shown, on), Is.EqualTo((true, 0, true)), "hidden while Loading · shown after");
        }

        // ---- the next chapter counts from the one just sent, and from the picture's own place otherwise

        [UnityTest]
        public IEnumerator RM19_ChapterCount()
        {
            yield return new EnterPlayMode();
            yield return Open();
            if (!s_Started)
            {
                yield return new ExitPlayMode();
                Assert.Ignore(NoVideo);
            }
            s_Video.time = 0.5;
            yield return Arrive(0.5);
            Call("NextChapter");
            Call("NextChapter");
            yield return Arrive(4.0);
            bool two = s_Arrived;
            yield return Real(0.2f);
            s_Video.time = 4.6;
            yield return Arrive(4.6);
            Call("NextChapter");
            yield return Arrive(0.0);
            bool wrapped = s_Arrived;
            yield return new ExitPlayMode();
            Assert.That((two, wrapped), Is.EqualTo((true, true)), "two presses, two chapters · from C to A");
        }
    }
}
