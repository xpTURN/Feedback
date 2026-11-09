using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the Timeline window's preview with Feedback tracks, in a batch editor: the window's state is driven through its internal
    // members (each checked first — a missing one fails, not passes). Values are read in the call that turns the preview off
    // or builds the graph again: at the next update the window may turn the preview on again by itself
    public sealed class TimelinePreviewTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const string ScenePath = "Assets/TimelinePreviewTests.unity";
        const string PrefabPath = "Assets/TimelinePreviewTests.prefab";
        const string TimelinePath = "Assets/TimelinePreviewTests.playable";
        static readonly List<string> s_Logs = new List<string>();

        TimelineEditorWindow m_Window;
        object m_State;
        float m_Fog;
        bool m_FogOn;
        Global m_Global;
        Action m_TrackRebuild;

        static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Log)
                s_Logs.Add(type + ": " + message);
        }

        [SetUp]
        public void SetUp()
        {
            s_Logs.Clear();
            Application.logMessageReceived += OnLog;
            m_Fog = RenderSettings.fogDensity;
            m_FogOn = RenderSettings.fog;
            m_TrackRebuild = PreviewSession.TrackRebuild;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            if (m_State != null && Preview)
                Preview = false;
            if (m_Window != null)
                m_Window.Close();
            PreviewSession.Current?.End(PreviewEndReason.Stopped);
            if (!AnimationMode.InAnimationMode())
                PreviewSession.SettleTrackRebuild();
            PreviewHooks.ReplayPending();
            if (StageUtility.GetCurrentStage() is PrefabStage)
                StageUtility.GoToMainStage();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(PrefabPath);
            AssetDatabase.DeleteAsset(TimelinePath);
            RenderSettings.fogDensity = m_Fog;
            RenderSettings.fog = m_FogOn;
            if (m_Global != null)
                PreviewGlobalState.Unregister(m_Global);
            m_Global = null;
            PreviewSession.TrackRebuild = m_TrackRebuild;
        }

        // ---- the window

        void Open(PlayableDirector director)
        {
            m_Window = TimelineEditor.GetOrCreateWindow();
            m_Window.SetTimeline(director);
            PropertyInfo state = m_Window.GetType().GetProperty("state", All);
            Assert.That(state, Is.Not.Null, "TimelineWindow.state");
            m_State = state.GetValue(m_Window);
            foreach (string member in new[] { "previewMode", "editSequence" })
                Assert.That(m_State.GetType().GetProperty(member), Is.Not.Null, "WindowState." + member);
            foreach (string method in new[] { "EvaluateImmediate", "Evaluate", "Play", "Pause" })
                Assert.That(m_State.GetType().GetMethod(method, All, null, Type.EmptyTypes, null), Is.Not.Null, "WindowState." + method + "()");
            Assert.That(m_Window.GetType().GetMethod("RebuildGraphIfNecessary", All), Is.Not.Null, "TimelineWindow.RebuildGraphIfNecessary");
        }

        bool Preview
        {
            get => (bool)m_State.GetType().GetProperty("previewMode").GetValue(m_State);
            set => m_State.GetType().GetProperty("previewMode").SetValue(m_State, value);
        }

        void Scrub(double t)
        {
            object sequence = m_State.GetType().GetProperty("editSequence").GetValue(m_State);
            sequence.GetType().GetProperty("time").SetValue(sequence, t);
            m_State.GetType().GetMethod("EvaluateImmediate", All, null, Type.EmptyTypes, null).Invoke(m_State, null);
        }

        void Evaluate(double t)
        {
            object sequence = m_State.GetType().GetProperty("editSequence").GetValue(m_State);
            sequence.GetType().GetProperty("time").SetValue(sequence, t);
            m_State.GetType().GetMethod("Evaluate", All, null, Type.EmptyTypes, null).Invoke(m_State, null);
        }

        void Rebuild()
        {
            TimelineEditor.Refresh(RefreshReason.ContentsModified);
            m_Window.GetType().GetMethod("RebuildGraphIfNecessary", All).Invoke(m_Window, new object[] { true });
        }

        // the window's update builds what was asked of it — and nothing when nothing was
        void BuildAsked() => m_Window.GetType().GetMethod("RebuildGraphIfNecessary", All).Invoke(m_Window, new object[] { true });

        // ---- the scene

        static Scene Fresh()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.That(EditorSceneManager.SaveScene(scene, ScenePath), Is.True);
            return scene;
        }

        static TargetRef Self => new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };

        static FeedbackPlayer Player(GameObject go, params FeedbackItem[] items)
        {
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            return p;
        }

        static TweenMotion Tween(Vector4 to)
        {
            TweenParams t = TweenParams.Default;
            t.m_To = to;
            t.m_Ease = Ease.Of(EaseType.Linear);
            return new TweenMotion { m_Params = t };
        }

        static T Timed<T>(T item, float seconds) where T : FeedbackItem
        {
            item.m_Timing.m_Duration = seconds;
            return item;
        }

        static PositionItem Line() => Timed(new PositionItem { m_Target = Self, m_Motion = Tween(new Vector4(1f, 2f, 3f, 0f)) }, 1f);

        static PlayableDirector Director(string name, out TimelineAsset asset, string assetPath = null)
        {
            var d = new GameObject(name).AddComponent<PlayableDirector>();
            d.playOnAwake = false;
            asset = ScriptableObject.CreateInstance<TimelineAsset>();
            if (assetPath != null)
                AssetDatabase.CreateAsset(asset, assetPath);
            d.playableAsset = asset;
            return d;
        }

        static FeedbackClip Track(PlayableDirector d, TimelineAsset asset, FeedbackPlayer player, double start, double duration, FeedbackClipTiming timing = FeedbackClipTiming.Auto)
        {
            FeedbackTrack track = asset.CreateTrack<FeedbackTrack>(null, player.name);
            TimelineClip clip = track.CreateClip<FeedbackClip>();
            clip.start = start;
            clip.duration = duration;
            var feedback = (FeedbackClip)clip.asset;
            feedback.m_Timing = timing;
            d.SetGenericBinding(track, player);
            return feedback;
        }

        static string Block(Renderer r, string property)
        {
            if (!r.HasPropertyBlock())
                return "none";
            var b = new MaterialPropertyBlock();
            r.GetPropertyBlock(b);
            return b.GetColor(property).ToString("F3");
        }

        // a user's edit as the inspector makes it — an undo group of its own (the batch editor puts one update's work in one)
        static void Edit(Object target, string path, Action<SerializedProperty> set)
        {
            Undo.IncrementCurrentGroup();
            var serialized = new SerializedObject(target);
            set(serialized.FindProperty(path));
            serialized.ApplyModifiedProperties();
            Undo.IncrementCurrentGroup();
        }

        static int Sentinels() => Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.name == PreviewRecovery.TemporaryPrefix + "TrackSentinel");

        // a registered state outside serialization — a preview's write of it goes back at the end
        sealed class Global : IPreviewGlobalState
        {
            float m_Captured;
            internal float Value = 1f;

            public string Name => "TimelinePreviewTests";

            public void Capture() => m_Captured = Value;

            public void Restore() => Value = m_Captured;

            public string Describe() => Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ---- PV-3 · PV-4 · PV-18 — a cycle: on, scrub, build again, off

        [UnityTest]
        public IEnumerator TP01_ACycleLeavesTheSceneAsItWas()
        {
            Scene scene = Fresh();
            var boxGo = new GameObject("Box");
            FeedbackPlayer box = Player(boxGo, Line());
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var r = cube.GetComponent<Renderer>();
            var user = new MaterialPropertyBlock();
            user.SetColor("_UserColor", Color.green);
            r.SetPropertyBlock(user);
            RenderSettings.fog = true;
            RenderSettings.fogDensity = 0.01f;
            var hide = new RendererVisibilityItem { m_Target = Self, m_Motion = new SetMotion { m_Params = new SetParams { m_Value = Vector4.zero } } };
            var color = Timed(new MaterialColorItem { m_Target = Self, m_Motion = Tween(new Vector4(1f, 0f, 0f, 1f)) }, 1f);
            var fog = Timed(new FogDensityItem { m_Motion = Tween(new Vector4(0.5f, 0f, 0f, 0f)) }, 1f);
            // a block value through the layer's composition (an override) — the session's tick writes it, not the seek
            var glow = Timed(new MaterialColorItem { m_Target = Self, m_PropertyName = "_EmissionColor", m_Motion = new FlashMotion { m_Params = new FlashParams { m_Target = new Vector4(1f, 1f, 0f, 1f), m_Attack = 0.1f, m_Hold = 0.8f, m_Release = 0.1f } } }, 1f);
            FeedbackPlayer cubePlayer = Player(cube, hide, color, fog, glow);
            PlayableDirector d = Director("Director", out TimelineAsset asset);
            Track(d, asset, box, 0.5, 1.0);
            Track(d, asset, cubePlayer, 0.5, 1.0);
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            m_Global = new Global();
            PreviewGlobalState.Register(m_Global);
            Open(d);
            Preview = true;
            Scrub(1.0);
            m_Global.Value = 2f;
            var got = new List<string>();
            got.Add($"on box={box.transform.localPosition:F2} enabled={r.enabled} fog={RenderSettings.fogDensity:F3} track={(PreviewSession.Current != null && PreviewSession.Current.IsTrack)} overlay={new PreviewOverlay().visible} glow={Block(r, "_EmissionColor")}");
            yield return null;
            Rebuild();
            Scrub(1.2);
            got.Add($"rebuilt box={box.transform.localPosition:F2} enabled={r.enabled} user={Block(r, "_UserColor")} glow={Block(r, "_EmissionColor")}");
            Preview = false;
            got.Add($"off box={box.transform.localPosition:F2} enabled={r.enabled} fog={RenderSettings.fogDensity:F3}");
            yield return null;
            got.Add($"next user={Block(r, "_UserColor")} main={Block(r, "_BaseColor")} session={(PreviewSession.Current != null)} sentinels={Sentinels()} dirty={scene.isDirty} global={m_Global.Value:F1} glow={Block(r, "_EmissionColor")}");
            Assert.That(got, Is.EqualTo(new[]
            {
                "on box=(0.50, 1.00, 1.50) enabled=False fog=0.255 track=True overlay=False glow=RGBA(1.000, 1.000, 0.000, 1.000)",
                "rebuilt box=(0.70, 1.40, 2.10) enabled=False user=RGBA(0.000, 1.000, 0.000, 1.000) glow=RGBA(1.000, 1.000, 0.000, 1.000)",
                "off box=(0.00, 0.00, 0.00) enabled=True fog=0.010",
                "next user=RGBA(0.000, 1.000, 0.000, 1.000) main=RGBA(0.000, 0.000, 0.000, 0.000) session=False sentinels=0 dirty=False global=1.0 glow=RGBA(0.000, 0.000, 0.000, 0.000)",
            }));
            Assert.That(s_Logs, Is.Empty);
        }

        // ---- PV-5 — no session, no write

        [UnityTest]
        public IEnumerator TP02_WithoutASessionNothingIsWritten()
        {
            Fresh();
            FeedbackPlayer box = Player(new GameObject("Box"), Line());
            PlayableDirector d = Director("Director", out TimelineAsset asset);
            Track(d, asset, box, 0.0, 1.0);
            asset.editorSettings.scenePreview = false;
            Open(d);
            Preview = true;
            Evaluate(0.5);
            yield return null;
            Assert.That((PreviewSession.Current == null, box.transform.localPosition, box.IsPlaying), Is.EqualTo((true, Vector3.zero, false)), "Scene Preview off");
            Assert.That(s_Logs, Is.Empty, "no warning");
        }

        // ---- PV-6 — a slot first written after the gather

        [UnityTest]
        public IEnumerator TP03_ALateSlotComesBack()
        {
            Scene scene = Fresh();
            FeedbackPlayer early = Player(new GameObject("Early"), Line());
            FeedbackPlayer late = Player(new GameObject("Late"), Line());
            late.transform.localPosition = new Vector3(5f, 0f, 0f);
            PlayableDirector d = Director("Director", out TimelineAsset asset);
            Track(d, asset, early, 0.0, 1.0);
            Track(d, asset, late, 3.0, 1.0);
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            Open(d);
            Preview = true;
            Scrub(0.5);
            Scrub(3.5);
            Vector3 moved = late.transform.localPosition;
            Preview = false;
            Assert.That((moved != new Vector3(5f, 0f, 0f), late.transform.localPosition, early.transform.localPosition, scene.isDirty), Is.EqualTo((true, new Vector3(5f, 0f, 0f), Vector3.zero, false)));
            yield return null;
        }

        // ---- PV-7 — one lock, two directors (a nested timeline)

        [UnityTest]
        public IEnumerator TP04_ANestedTimelineSharesTheSession()
        {
            Scene scene = Fresh();
            FeedbackPlayer a = Player(new GameObject("A"), Line());
            FeedbackPlayer b = Player(new GameObject("B"), Line());
            b.transform.localPosition = new Vector3(10f, 0f, 0f);
            PlayableDirector child = Director("Child", out TimelineAsset childAsset);
            Track(child, childAsset, b, 0.0, 1.0);
            PlayableDirector master = Director("Master", out TimelineAsset masterAsset);
            Track(master, masterAsset, a, 0.5, 1.0);
            ControlTrack control = masterAsset.CreateTrack<ControlTrack>(null, "Control");
            TimelineClip cc = control.CreateClip<ControlPlayableAsset>();
            cc.start = 0.0;
            cc.duration = 2.0;
            var id = new PropertyName("child");
            ((ControlPlayableAsset)cc.asset).sourceGameObject.exposedName = id;
            master.SetReferenceValue(id, child.gameObject);
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            Open(master);
            Preview = true;
            Scrub(1.0);
            string on = $"{a.transform.localPosition:F2} {b.transform.localPosition:F2} players={PreviewSession.Current?.TrackPlayers.Count}";
            Preview = false;
            string off = $"{a.transform.localPosition:F2} {b.transform.localPosition:F2}";
            Assert.That((on, off), Is.EqualTo(("(0.50, 1.00, 1.50) (1.00, 2.00, 3.00) players=2", "(0.00, 0.00, 0.00) (10.00, 0.00, 0.00)")));
            yield return null;
        }

        // ---- PV-8 — the inspector's preview during the track session

        [UnityTest]
        public IEnumerator TP05_TheInspectorPreviewIsLocked()
        {
            Fresh();
            FeedbackPlayer box = Player(new GameObject("Box"), Line());
            FeedbackPlayer other = Player(new GameObject("Other"), Line());
            PlayableDirector d = Director("Director", out TimelineAsset asset);
            Track(d, asset, box, 0.0, 1.0);
            Open(d);
            Preview = true;
            Scrub(0.5);
            PreviewSession track = PreviewSession.Current;
            PreviewRefusal own = PreviewSession.Start(box, PlayArgs.Default);
            PreviewRefusal another = PreviewSession.Start(other, PlayArgs.Default);
            Scrub(0.7);
            Assert.That((own, another, PreviewSession.Current == track, box.transform.localPosition.x), Is.EqualTo((PreviewRefusal.Locked, PreviewRefusal.Locked, true, 0.7f)).Using(new Within()));
            Preview = false;
            yield return null;
        }

        // ---- PV-9 — an edit during the preview

        [UnityTest]
        public IEnumerator TP06_AnEditOpensThePreviewAgainAndStays()
        {
            Scene scene = Fresh();
            FeedbackPlayer box = Player(new GameObject("Box"), Line());
            PlayableDirector d = Director("Director", out TimelineAsset asset);
            Track(d, asset, box, 0.0, 1.0);
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            Open(d);
            Preview = true;
            Scrub(0.5);
            // the asks for a rebuild — at most one an edit (the window can build first and the session closes on the lost lock), no loop
            int asks = 0;
            PreviewSession.TrackRebuild = () =>
            {
                asks++;
                m_TrackRebuild?.Invoke();
            };
            PreviewSession before = PreviewSession.Current;
            Edit(box.transform, "m_LocalPosition.z", p => p.floatValue = 7f);
            yield return null;
            // ended — waiting for the rebuild, or already open again if the window built first
            bool ended = before != null && PreviewSession.Current != before;
            BuildAsked();
            Scrub(0.5);
            string again = $"{box.transform.localPosition:F2} session={(PreviewSession.Current != null)}";
            // the bound player's list: the next session plays the new one
            Edit(box, "m_Items.Array.data[0].m_Motion.m_Params.m_To.x", p => p.floatValue = 2f);
            yield return null;
            BuildAsked();
            Scrub(0.5);
            string list = $"{box.transform.localPosition:F2}";
            Preview = false;
            Assert.That((ended, again, list, box.transform.localPosition, asks is >= 1 and <= 2), Is.EqualTo((true, "(0.50, 1.00, 5.00) session=True", "(1.00, 1.00, 5.00)", new Vector3(0f, 0f, 7f), true)), "the edit stays after the preview");
        }

        // ---- PV-10 — Undo · a closing scene · a prefab instance's revert

        [UnityTest]
        public IEnumerator TP07_UndoClosingAndPrefabOperations()
        {
            Scene scene = Fresh();
            FeedbackPlayer box = Player(new GameObject("Box"), Line());
            var lamp = new GameObject("Lamp").AddComponent<Light>();
            lamp.intensity = 1f;
            PlayableDirector d = Director("Director", out TimelineAsset asset);
            Track(d, asset, box, 0.0, 1.0);
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            Open(d);
            Preview = true;
            Scrub(0.5);
            // the latest step: the lamp's intensity, changed during the preview
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(lamp, "intensity");
            lamp.intensity = 5f;
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            yield return null;
            Undo.PerformUndo();
            yield return null;
            BuildAsked();
            Scrub(0.5);
            var got = new List<string> { $"undo intensity={lamp.intensity:F1} box={box.transform.localPosition:F2} session={(PreviewSession.Current != null)}" };
            Preview = false;
            got.Add($"off box={box.transform.localPosition:F2}");
            yield return null;
            // a prefab instance whose recorded property is an override, reverted during the preview: the preview goes on from
            // the reverted values and the preview's end leaves them
            var root = new GameObject("Root");
            var child = new GameObject("Box");
            child.transform.SetParent(root.transform, false);
            Player(child, Line());
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            var ip = instance.transform.Find("Box").GetComponent<FeedbackPlayer>();
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(ip.transform);
            ip.transform.localPosition = new Vector3(0.25f, 0f, 0f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(ip.transform);
            int Overrides() => PrefabUtility.GetPropertyModifications(instance).Count(m => m.target == source && m.propertyPath.StartsWith("m_LocalPosition", StringComparison.Ordinal));
            PlayableDirector d2 = Director("Director2", out TimelineAsset asset2);
            Track(d2, asset2, ip, 0.0, 1.0);
            m_Window.SetTimeline(d2);
            m_State = m_Window.GetType().GetProperty("state", All).GetValue(m_Window);
            Preview = true;
            Scrub(0.5);
            PrefabUtility.RevertPrefabInstance(instance, InteractionMode.AutomatedAction);
            got.Add($"revert session={(PreviewSession.Current != null)} overrides={Overrides()} asset={AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).transform.Find("Box").localPosition:F2}");
            yield return null;
            yield return null;
            got.Add($"rebuilt instance={ip.transform.localPosition:F2} session={(PreviewSession.Current != null)}");
            Preview = false;
            yield return null;
            got.Add($"off instance={ip.transform.localPosition:F2} overrides={Overrides()}");
            // the scene closing ends the session before it goes
            Open(d2);
            Preview = true;
            Scrub(0.5);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            got.Add($"closed session={(PreviewSession.Current != null)} sentinels={Sentinels()}");
            Assert.That(got, Is.EqualTo(new[]
            {
                "undo intensity=1.0 box=(0.50, 1.00, 1.50) session=True",
                "off box=(0.00, 0.00, 0.00)",
                "revert session=False overrides=0 asset=(0.00, 0.00, 0.00)",
                "rebuilt instance=(0.50, 1.00, 1.50) session=True",
                "off instance=(0.00, 0.00, 0.00) overrides=0",
                "closed session=False sentinels=0",
            }));
        }

        // ---- PV-11 — a save during the preview

        [UnityTest]
        public IEnumerator TP08_ASaveWritesTheOriginalValues()
        {
            Scene scene = Fresh();
            FeedbackPlayer box = Player(new GameObject("Box"), Line());
            box.transform.localPosition = new Vector3(0.25f, 0f, 0f);
            RenderSettings.fog = true;
            RenderSettings.fogDensity = 0.01f;
            var fog = Timed(new FogDensityItem { m_Motion = Tween(new Vector4(0.5f, 0f, 0f, 0f)) }, 1f);
            box.m_Items.Add(fog);
            PlayableDirector d = Director("Director", out TimelineAsset asset);
            Track(d, asset, box, 0.0, 1.0);
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            Open(d);
            Preview = true;
            Scrub(0.5);
            EditorSceneManager.MarkSceneDirty(scene);
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            string file = File.ReadAllText(ScenePath);
            string after = $"{box.transform.localPosition:F3} fog={RenderSettings.fogDensity:F3} session={(PreviewSession.Current != null)}";
            Preview = false;
            Assert.That((file.Contains("m_LocalPosition: {x: 0.25, y: 0, z: 0}"), file.Contains("m_FogDensity: 0.01"), after), Is.EqualTo((true, true, "(0.625, 1.000, 1.500) fog=0.255 session=True")));
            yield return null;
        }

        // ---- PV-12 · PV-13 — a triggered clip's scrub · a simulated target

        [UnityTest]
        public IEnumerator TP09_TriggeredAndSimulatedClips()
        {
            Fresh();
            FeedbackPlayer box = Player(new GameObject("Box"), Line());
            GameObject particles = new GameObject("Particles");
            particles.AddComponent<ParticleSystem>();
            FeedbackPlayer sim = Player(particles, Timed(new ParticleControlItem { m_Target = Self, m_Action = ParticleAction.Play }, 1f));
            FeedbackPlayer forced = Player(new GameObject("Forced"), Timed(new ParticleControlItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = particles, m_Path = "", m_Anchor = "" }, m_Action = ParticleAction.Play }, 1f));
            PlayableDirector d = Director("Director", out TimelineAsset asset);
            Track(d, asset, box, 0.0, 1.0, FeedbackClipTiming.Triggered);
            Track(d, asset, sim, 0.0, 1.0, FeedbackClipTiming.Auto);
            Track(d, asset, forced, 0.0, 1.0, FeedbackClipTiming.Driven);
            Open(d);
            LogAssert.Expect(LogType.Warning, FeedbackClipBehaviour.NotScrubbedWarning);
            Preview = true;
            Scrub(0.5);
            Scrub(0.8);
            string scrub = $"box={box.transform.localPosition:F2} playing={box.IsPlaying} sim={sim.IsPlaying} forced={forced.IsPlaying}";
            Preview = false;
            Assert.That((scrub, box.transform.localPosition), Is.EqualTo(("box=(0.00, 0.00, 0.00) playing=True sim=True forced=True", Vector3.zero)), "a triggered clip stands at its start while scrubbed · the simulated lists play as Triggered");
            yield return null;
        }

        // ---- PV-19 — a triggered play that ended inside its clip: Revert at the clip's end puts the player back · in the
        //      preview a clip entered again starts from the values before the plays (as an Inspector Preview's Play does) ·
        //      another clip of the player goes on from the values as they are

        [UnityTest]
        public IEnumerator TP11_ATriggeredClipStartsAgainFromTheValuesBefore()
        {
            Fresh();
            PositionItem Rise() => Timed(new PositionItem { m_Target = Self, m_Motion = Tween(new Vector4(1f, 2f, 3f, 0f)) }, 0.2f);
            FeedbackPlayer box = Player(new GameObject("Box"), Rise());
            FeedbackPlayer other = Player(new GameObject("Other"), Rise());
            PlayableDirector d = Director("Director", out TimelineAsset asset);
            void Clips(FeedbackPlayer player, FeedbackClipEnd end, params double[] starts)
            {
                FeedbackTrack track = asset.CreateTrack<FeedbackTrack>(null, player.name);
                foreach (double start in starts)
                {
                    TimelineClip clip = track.CreateClip<FeedbackClip>();
                    clip.start = start;
                    clip.duration = 1.0;
                    ((FeedbackClip)clip.asset).m_Timing = FeedbackClipTiming.Triggered;
                    ((FeedbackClip)clip.asset).m_End = end;
                }
                d.SetGenericBinding(track, player);
            }
            Clips(other, FeedbackClipEnd.Revert, 1.0);
            Clips(box, FeedbackClipEnd.Hold, 5.0, 7.0);
            Open(d);
            Preview = true;
            string At(FeedbackPlayer p) => $"{p.transform.localPosition:F2} playing={p.IsPlaying}";
            // a play ends inside its clip, as its clock would end it, and leaves its end value — Revert at the clip's end
            Scrub(1.5);
            other.Skip();
            Scrub(1.6);
            string ended = At(other);
            Scrub(0.5);
            string left = At(other);
            Scrub(1.5);
            string again = At(other);
            // Hold keeps the end value: the next clip goes on from it · the clip entered again starts from the values before
            Scrub(5.5);
            box.Skip();
            Scrub(7.5);
            string next = At(box);
            box.Skip();
            Scrub(5.5);
            string back = At(box);
            Preview = false;
            Assert.That((ended, left, again, next, back), Is.EqualTo(("(1.00, 2.00, 3.00) playing=False", "(0.00, 0.00, 0.00) playing=False", "(0.00, 0.00, 0.00) playing=True", "(1.00, 2.00, 3.00) playing=True", "(0.00, 0.00, 0.00) playing=True")));
            yield return null;
        }

        // ---- PV-14 — a reload during the preview

        [UnityTest]
        public IEnumerator TP10_AReloadLeavesNothing()
        {
            Scene scene = Fresh();
            FeedbackPlayer box = Player(new GameObject("Box"), Line());
            PlayableDirector d = Director("Director", out TimelineAsset asset);
            Track(d, asset, box, 0.0, 1.0);
            Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
            Open(d);
            Preview = true;
            Scrub(0.5);
            EditorUtility.RequestScriptReload();
            yield return new WaitForDomainReload();
            var after = GameObject.Find("Box");
            Assert.That((after.transform.localPosition, PreviewSession.Current == null, AnimationMode.InAnimationMode(), Sentinels()), Is.EqualTo((Vector3.zero, true, false, 0)));
            m_Window = TimelineEditor.GetWindow();
            m_State = m_Window != null ? m_Window.GetType().GetProperty("state", All).GetValue(m_Window) : null;
        }

        // ---- a simulated list in the preview: an Auto clip plays it as a Triggered one does — the player's chance — and a
        //      plain player's Auto clip entered in the same evaluation still drives, either order of the tracks

        [UnityTest]
        public IEnumerator TP12_ASimulatedListPlaysAsATriggeredClipDoes()
        {
            var got = new List<string>();
            foreach (bool simulatedFirst in new[] { false, true })
            {
                Fresh();
                GameObject particles = new GameObject("Particles");
                particles.AddComponent<ParticleSystem>();
                FeedbackPlayer sim = Player(particles, Timed(new ParticleControlItem { m_Target = Self, m_Action = ParticleAction.Play }, 1f));
                sim.m_Chance = 0f;
                FeedbackPlayer box = Player(new GameObject("Box"), Line());
                PlayableDirector d = Director("Director", out TimelineAsset asset);
                foreach (FeedbackPlayer p in simulatedFirst ? new[] { sim, box } : new[] { box, sim })
                    Track(d, asset, p, 0.0, 1.0);
                Open(d);
                Preview = true;
                Scrub(0.3);
                Scrub(0.5);
                got.Add($"simulated first={simulatedFirst}: simulated playing={sim.IsPlaying} · box x={box.transform.localPosition.x:F2}");
                Preview = false;
                m_Window.Close();
                m_Window = null;
                m_State = null;
                PreviewSession.Current?.End(PreviewEndReason.Stopped);
                yield return null;
            }
            Assert.That(got, Is.EqualTo(new[]
            {
                "simulated first=False: simulated playing=False · box x=0.50",
                "simulated first=True: simulated playing=False · box x=0.50",
            }));
        }

        sealed class Within : IEqualityComparer<(PreviewRefusal, PreviewRefusal, bool, float)>
        {
            public bool Equals((PreviewRefusal, PreviewRefusal, bool, float) a, (PreviewRefusal, PreviewRefusal, bool, float) b)
                => a.Item1 == b.Item1 && a.Item2 == b.Item2 && a.Item3 == b.Item3 && Mathf.Abs(a.Item4 - b.Item4) < 1e-4f;

            public int GetHashCode((PreviewRefusal, PreviewRefusal, bool, float) v) => 0;
        }
    }
}
