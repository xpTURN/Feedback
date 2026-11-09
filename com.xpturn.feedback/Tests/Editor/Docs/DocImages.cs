using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using PopupWindow = UnityEditor.PopupWindow;

namespace xpTURN.Feedback.Tests.Editor.Docs
{
    // the documents' screenshots, for tools/doc-images.sh. A non-batch editor calls Run(): each shot of the shot list
    // (tools/doc-images/shots.json) makes its state — a scene, a selection, a floating window at a fixed place, a preview or
    // a play paused — and checks it, then writes ready-<name>.txt (the editor's pid, the pixels per point, the window's
    // place and the part of it to keep, in points) and waits for the tool's done-<name>.txt, written once two captures in
    // a row are the same. A shot whose check fails writes fail-<id>.txt and the next shot goes on. The package's editor
    // preferences (xpTURN.Feedback.*) are at their defaults for the run and come back at its end, from a backup in the
    // state folder; a run that was killed leaves its backup, and the next run takes the values from it
    internal static class DocImages
    {
        const string Domain = "com.unity3d.UnityEditor5.x";
        const string Prefix = "xpTURN.Feedback.";
        const string SamplesRoot = "Packages/com.xpturn.feedback.samples-dev";
        const string ScenePath = "Assets/DocImages.unity";
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        // the frames a new state takes to lay out and draw
        const int Settle = 20;
        // a floating window's rounded bottom corners, and the Game view's toolbar, left out of a capture — in points
        const float Corner = 10;
        const float GameToolbar = 21;
        // a GIF frame's step, in seconds (the GIF's frame time), and the time a play's last frame is held
        internal const double FrameStep = 0.04;
        internal const double HeldFrame = 0.6;
        // every play's seed — a shake's noise is the same at each run
        static readonly PlayArgs Args = PlayArgs.Default.WithSeed(20251108);

        [Serializable]
        internal sealed class Shot
        {
            public string id;
            public string file;
            public string state;
        }

        [Serializable]
        sealed class ShotList
        {
            public Shot[] shots;
        }

        sealed class ShotFailed : Exception
        {
            internal ShotFailed(string message) : base(message)
            {
            }
        }

        static string s_Out;
        static string s_State;
        static bool s_Cover;
        static EditorWindow s_Covering;
        static StreamWriter s_Log;
        static IEnumerator<int> s_Steps;
        static int s_Wait;
        static double s_Start;
        static readonly List<EditorWindow> s_Windows = new List<EditorWindow>();

        public static void Run()
        {
            s_Out = Environment.GetEnvironmentVariable("DOCIMG_OUT");
            s_State = Environment.GetEnvironmentVariable("DOCIMG_STATE");
            s_Cover = Environment.GetEnvironmentVariable("DOCIMG_COVER") == "1";
            string shots = Environment.GetEnvironmentVariable("DOCIMG_SHOTS");
            if (string.IsNullOrEmpty(s_Out) || string.IsNullOrEmpty(s_State) || string.IsNullOrEmpty(shots))
            {
                UnityEngine.Debug.LogError("DocImages: DOCIMG_OUT, DOCIMG_STATE and DOCIMG_SHOTS are set by tools/doc-images.sh");
                EditorApplication.Exit(1);
                return;
            }
            Directory.CreateDirectory(s_Out);
            Directory.CreateDirectory(s_State);
            s_Log = new StreamWriter(Path.Combine(s_Out, "driver.log")) { AutoFlush = true };
            s_Start = EditorApplication.timeSinceStartup;
            string[] only = (Environment.GetEnvironmentVariable("DOCIMG_ONLY") ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            List<Shot> list = JsonUtility.FromJson<ShotList>(File.ReadAllText(shots)).shots.Where(s => only.Length == 0 || only.Contains(s.id)).ToList();
            s_Steps = Steps(list);
            EditorApplication.update += Tick;
        }

        static void Log(string line) => s_Log.WriteLine($"{EditorApplication.timeSinceStartup - s_Start:F1} {line}");

        static void Tick()
        {
            if (s_Wait-- > 0)
                return;
            bool more;
            try
            {
                more = s_Steps.MoveNext();
            }
            catch (Exception e)
            {
                Log("EXC " + e);
                more = false;
            }
            if (more)
            {
                s_Wait = s_Steps.Current;
                return;
            }
            EditorApplication.update -= Tick;
            Finish();
        }

        static void Finish()
        {
            try
            {
                CloseWindows();
                RestorePrefs();
            }
            catch (Exception e)
            {
                Log("EXC at the end " + e);
            }
            Log("END");
            File.WriteAllText(Path.Combine(s_Out, "end.txt"), "end");
            s_Log.Dispose();
            EditorApplication.Exit(0);
        }

        static IEnumerator<int> Steps(List<Shot> shots)
        {
            Log($"unity {Application.unityVersion} ppp {EditorGUIUtility.pixelsPerPoint} pro {EditorGUIUtility.isProSkin} shots {shots.Count}");
            SavePrefs();
            yield return 30;
            foreach (Shot shot in shots.OrderBy(s => PlayModeShot(s) ? 1 : 0))
            {
                Log($"shot {shot.id} {shot.state}");
                IEnumerator<int> steps = Shoot(shot).GetEnumerator();
                while (true)
                {
                    bool more;
                    try
                    {
                        more = steps.MoveNext();
                    }
                    catch (Exception e)
                    {
                        string why = e is ShotFailed ? e.Message : e.GetType().Name + " " + e.Message + "\n" + e.StackTrace;
                        Log($"FAIL {shot.id} {why}");
                        File.WriteAllText(Path.Combine(s_Out, $"fail-{shot.id}.txt"), why);
                        break;
                    }
                    if (!more)
                        break;
                    yield return steps.Current;
                }
                foreach (int w in Reset())
                    yield return w;
            }
        }

        static bool PlayModeShot(Shot s) => s.state == "FirstKeep" || s.state == "Debugger" || s.state == "Layers";

        static IEnumerable<int> Shoot(Shot shot)
        {
            switch (shot.state)
            {
                case "Hero": return Hero(shot);
                case "Inspector": return Inspector(shot);
                case "FirstEmpty": return FirstEmpty(shot);
                case "FirstAddMenu": return FirstAddMenu(shot);
                case "FirstPreview": return FirstPreview(shot);
                case "FirstJumpField": return FirstJumpField(shot);
                case "FirstKeep": return FirstKeep(shot);
                case "ItemHeader": return ItemHeader(shot);
                case "AddMenu": return AddMenu(shot);
                case "PreviewBar": return PreviewBarShot(shot);
                case "PresetLinked": return PresetLinked(shot);
                case "Debugger": return DebuggerShot(shot);
                case "Validator": return Validator(shot);
                case "Items": return Items(shot);
                case "Strip": return Strip(shot);
                case "Layers": return Layers(shot);
                case "Timeline": return TimelineShot(shot);
            }
            throw new ShotFailed("no state " + shot.state);
        }

        // ---- the shots

        // the README's head: the brick breaker's boss (shown — the game shows it later) hit, exploding and flashing the
        // screen, each play scrubbed frame by frame in the Game view (its toolbar left out) and its last frame held.
        // frames-<id>.txt lists the frames and their times
        static IEnumerable<int> Hero(Shot shot)
        {
            OpenSample("Showcase/BrickBreaker/BrickBreaker.unity");
            GameObject boss = Find<Transform>("Boss Body").gameObject;
            boss.SetActive(true);
            FeedbackPlayer flash = boss.GetComponents<FeedbackPlayer>().FirstOrDefault(p => p.m_Preset == null && p.m_Items.Any(i => i is ScreenFlashItem));
            Check(flash != null, "no screen flash player on 'Boss Body'");
            FeedbackPlayer[] hits = { Linking("Hit Critical"), Linking("Explosion"), flash };
            EditorWindow game = Open("UnityEditor.GameView", new Rect(120, 100, 480, 300));
            yield return Settle * 3;
            var frames = new List<string>();
            foreach (FeedbackPlayer hit in hits)
            {
                Check(PreviewSession.Start(hit, Args) == PreviewRefusal.None, "the preview did not start");
                Check(PreviewSession.Current.ScrubState(out _, out double high, out _) == SeekRefusal.None, "the preview cannot seek");
                int steps = (int)Math.Ceiling(high / FrameStep - 1e-6);
                for (int k = 0; k <= steps; k++)
                {
                    Check(PreviewSession.Current != null && PreviewSession.Current.Scrub(Math.Min(k * FrameStep, high)) == SeekRefusal.None, $"the scrub to {k * FrameStep:F2} s");
                    EditorApplication.QueuePlayerLoopUpdate();
                    string name = $"{shot.id}-f{frames.Count:000}";
                    foreach (int w in Capture(game, Rect.MinMaxRect(0, GameToolbar, game.position.width, game.position.height - Corner), name))
                        yield return w;
                    frames.Add(name + " " + F(k == steps ? HeldFrame : FrameStep));
                }
                PreviewSession.Current?.Stop();
                yield return Settle;
            }
            File.WriteAllLines(Path.Combine(s_Out, $"frames-{shot.id}.txt"), frames);
        }

        // a player of five items with the timeline strip — the inspector of the README and of the editor's page
        static IEnumerable<int> Inspector(Shot shot)
        {
            NewScene();
            GameObject crate = Cube("Crate");
            FeedbackPlayer player = Player(crate, "Transform/Scale/Punch", "Transform/Position/Shake", "Renderer/Material Color/Flash", "Camera/Shake/Shake", "Time/Hit Stop");
            player.m_Items[1].m_Timing.m_Delay = 0.05f;
            player.m_Items[4].m_Timing.m_Rule = StartRule.AfterPrevious;
            EditorWindow inspector = Inspect(crate);
            yield return Settle;
            foreach (int w in CapturePlayer(inspector, player, shot.id))
                yield return w;
        }

        // the first feedback, step 1: 'Body' with a new, empty player
        static IEnumerable<int> FirstEmpty(Shot shot)
        {
            OpenSample("Basics/GettingStarted/Empty/GettingStartedEmpty.unity");
            GameObject body = Find<Transform>("Body").gameObject;
            FeedbackPlayer player = Undo.AddComponent<FeedbackPlayer>(body);
            EditorWindow inspector = Inspect(body);
            yield return Settle;
            Check(player.m_Items.Count == 0, "the new player is not empty");
            foreach (int w in CapturePlayer(inspector, player, shot.id))
                yield return w;
        }

        // the first feedback, step 2: the Add menu with 'punch' typed
        static IEnumerable<int> FirstAddMenu(Shot shot)
        {
            OpenSample("Basics/GettingStarted/Empty/GettingStartedEmpty.unity");
            GameObject body = Find<Transform>("Body").gameObject;
            FeedbackPlayer player = Undo.AddComponent<FeedbackPlayer>(body);
            EditorWindow inspector = Inspect(body);
            yield return Settle;
            foreach (int w in OpenAddMenu(inspector, player))
                yield return w;
            AddMenuPopup.Last.Search.value = "punch";
            yield return Settle;
            AddMenuPopup.Last.Search.Q<TextField>()?.SelectRange(5, 5);
            yield return Settle;
            Check(AddMenuPopup.Last.Shown.Count > 0 && AddMenuPopup.Last.Shown[0].Path.EndsWith("/Punch", StringComparison.Ordinal), "the search for 'punch' lists no Punch first");
            foreach (int w in CapturePopup(shot.id))
                yield return w;
        }

        // the first feedback, step 3: the punch's preview paused at 0.12 s
        static IEnumerable<int> FirstPreview(Shot shot)
        {
            OpenSample("Basics/GettingStarted/Empty/GettingStartedEmpty.unity");
            GameObject body = Find<Transform>("Body").gameObject;
            FeedbackPlayer player = Player(body, "Transform/Scale/Punch");
            EditorWindow inspector = Inspect(body);
            yield return Settle;
            foreach (int w in PausePreview(player, 0.12))
                yield return w;
            foreach (int w in CapturePlayer(inspector, player, shot.id))
                yield return w;
        }

        // the first feedback, step 4: 'Hero' with the player in its Jump field
        static IEnumerable<int> FirstJumpField(Shot shot)
        {
            OpenSample("Basics/GettingStarted/Empty/GettingStartedEmpty.unity");
            GameObject body = Find<Transform>("Body").gameObject;
            FeedbackPlayer player = Player(body, "Transform/Scale/Punch");
            GameObject hero = Find<Transform>("Hero").gameObject;
            MonoBehaviour script = hero.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && new SerializedObject(m).FindProperty("m_Jump") != null);
            Check(script != null, "'Hero' has no script with a Jump field");
            var so = new SerializedObject(script);
            so.FindProperty("m_Jump").objectReferenceValue = player;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorWindow inspector = Inspect(hero);
            yield return Settle;
            foreach (int w in CaptureComponent(inspector, script, shot.id))
                yield return w;
        }

        // the first feedback, step 5: Play mode with the player's changes kept and an edit waiting for Rebuild
        static IEnumerable<int> FirstKeep(Shot shot)
        {
            OpenSample("Basics/GettingStarted/Complete/GettingStarted.unity");
            FeedbackPlayer player = Jump();
            Check(KeepPlayModeChanges.Mark(player, true), "Keep Play Mode Changes cannot be turned on: " + KeepPlayModeChanges.State(player, out string why) + " " + why);
            foreach (int w in EnterPlay())
                yield return w;
            player = Jump();
            EditorWindow inspector = Inspect(player.gameObject);
            yield return Settle;
            var so = new SerializedObject(player);
            SerializedProperty amplitude = so.FindProperty("m_Items.Array.data[0].m_Motion.m_Params.m_Amplitude");
            Check(amplitude != null && amplitude.propertyType == SerializedPropertyType.Vector4, "the punch has no amplitude");
            amplitude.vector4Value *= 1.5f;
            so.ApplyModifiedProperties();
            yield return Settle;
            EditorApplication.isPaused = true;
            yield return Settle;
            Check(KeepPlayModeChanges.IsMarked(player), "the player is not marked");
            Check(PlayModeEdits.Pending(player), "no edit waits for Rebuild");
            foreach (int w in CapturePlayer(inspector, player, shot.id))
                yield return w;
        }

        // an item's header with the validator's warning button
        static IEnumerable<int> ItemHeader(Shot shot)
        {
            NewScene();
            GameObject crate = Cube("Crate");
            FeedbackPlayer player = Player(crate, "Transform/Scale/Punch", "Light/Intensity/Flash");
            EditorWindow inspector = Inspect(crate);
            yield return Settle * 2;
            VisualElement root = PlayerRoot(inspector, player);
            List<ItemHeader> headers = root.Query<ItemHeader>().ToList();
            Check(headers.Count == 2, $"{headers.Count} item headers");
            VisualElement warning = headers[1].Query(className: InspectorStyles.ValidationWarning).Where(v => v.resolvedStyle.display != DisplayStyle.None).First();
            Check(warning != null, "the second item's header has no warning button");
            Rect crop = Within(inspector, headers[0]);
            crop = Rect.MinMaxRect(crop.xMin, crop.yMin, crop.xMax, Within(inspector, headers[1]).yMax);
            foreach (int w in Capture(inspector, crop, shot.id))
                yield return w;
        }

        // the Add menu with no words: Favorites, Recent and the categories
        static IEnumerable<int> AddMenu(Shot shot)
        {
            NewScene();
            GameObject crate = Cube("Crate");
            FeedbackPlayer player = Player(crate);
            foreach (string path in new[] { "Transform/Scale/Punch", "Camera/Shake/Shake" })
                FeedbackCatalog.Favorite(Leaf(path), true);
            foreach (string path in new[] { "Renderer/Material Color/Flash", "Time/Hit Stop", "Transform/Position/Shake" })
                FeedbackCatalog.Used(Leaf(path));
            EditorWindow inspector = Inspect(crate);
            yield return Settle;
            foreach (int w in OpenAddMenu(inspector, player))
                yield return w;
            Check(string.IsNullOrEmpty(AddMenuPopup.Last.Search.value), "the search is not empty");
            foreach (int w in CapturePopup(shot.id))
                yield return w;
        }

        // the preview bar of a paused preview
        static IEnumerable<int> PreviewBarShot(Shot shot)
        {
            NewScene();
            GameObject crate = Cube("Crate");
            FeedbackPlayer player = Player(crate, "Transform/Scale/Punch", "Transform/Position/Shake");
            EditorWindow inspector = Inspect(crate);
            yield return Settle;
            foreach (int w in PausePreview(player, 0.1))
                yield return w;
            PreviewBar bar = PlayerRoot(inspector, player).Query<PreviewBar>().First();
            Check(bar != null, "no preview bar");
            foreach (int w in Capture(inspector, Within(inspector, bar), shot.id))
                yield return w;
        }

        // a player that links a preset: the list read-only, Open Preset and Unlink
        static IEnumerable<int> PresetLinked(Shot shot)
        {
            NewScene();
            GameObject crate = Cube("Crate");
            FeedbackPlayer player = Player(crate);
            player.m_Preset = AssetDatabase.LoadAssetAtPath<FeedbackPreset>("Packages/com.xpturn.feedback/Presets/Hit Heavy.asset");
            Check(player.m_Preset != null, "no preset Hit Heavy");
            EditorWindow inspector = Inspect(crate);
            yield return Settle;
            foreach (int w in CapturePlayer(inspector, player, shot.id))
                yield return w;
        }

        // the debugger's Playbacks: two plays paused in Play mode, the other panels folded
        static IEnumerable<int> DebuggerShot(Shot shot)
        {
            foreach (int w in TwoPlays())
                yield return w;
            foreach (string panel in FeedbackDebuggerWindow.Panels)
                EditorPrefs.SetBool(FeedbackDebuggerWindow.FoldKey + panel, panel == "Playbacks");
            FeedbackDebuggerWindow window = OpenDebugger(new Rect(80, 80, 900, 246));
            yield return Settle * 3;
            foreach (int w in Capture(window, Whole(window), shot.id))
                yield return w;
        }

        // the validator's window with the findings of the open scene — an error, a warning and an info
        static IEnumerable<int> Validator(Shot shot)
        {
            NewScene();
            GameObject crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "Crate";
            FeedbackPlayer player = Player(crate, "Transform/Scale/Punch", "Light/Intensity/Flash", "Transform/Position/Shake");
            var asset = (TargetItem)player.m_Items[2];
            asset.m_Target.m_Mode = TargetRefMode.Direct;
            asset.m_Target.m_Object = AssetDatabase.LoadAssetAtPath<GameObject>(SamplesRoot + "/Basics/Gallery/Art/GallerySpawn.prefab");
            Check(asset.m_Target.m_Object != null, "no prefab for the asset target");
            EditorSceneManager.SaveScene(crate.scene);
            List<RuleResult> found = ValidatorScan.Scan(ValidatorScan.OpenSceneHosts(), "open scenes");
            Check(new[] { RuleSeverity.Error, RuleSeverity.Warning, RuleSeverity.Info }.All(s => found.Any(r => r.Severity == s)), $"the findings are not an error, a warning and an info: {string.Join(", ", found.Select(r => r.Severity))}");
            FeedbackValidatorWindow window = FeedbackValidatorWindow.Open();
            Float(window, new Rect(80, 80, 720, 130));
            yield return Settle * 2;
            foreach (int w in Capture(window, Whole(window), shot.id))
                yield return w;
        }

        // target items, an action and flow items in one list
        static IEnumerable<int> Items(Shot shot)
        {
            NewScene();
            GameObject crate = Cube("Crate");
            FeedbackPlayer player = Player(crate, "Transform/Scale/Punch", "Renderer/Material Color/Flash", "Flow/Wait", "Flow/Unity Event", "Transform/Position/Tween");
            EditorWindow inspector = Inspect(crate);
            yield return Settle;
            ItemListView list = PlayerRoot(inspector, player).Query<ItemListView>().First();
            Check(list != null, "no item list");
            foreach (int w in Capture(inspector, Within(inspector, list), shot.id))
                yield return w;
        }

        // the timeline strip of items with a rule, a delay and extra runs
        static IEnumerable<int> Strip(Shot shot)
        {
            NewScene();
            GameObject crate = Cube("Crate");
            FeedbackPlayer player = Player(crate, "Transform/Scale/Punch", "Renderer/Material Color/Flash", "Transform/Position/Shake", "Light/Intensity/Flash");
            player.m_Items[1].m_Timing.m_Delay = 0.1f;
            player.m_Items[2].m_Timing.m_Rule = StartRule.AfterPrevious;
            player.m_Items[2].m_Timing.m_RepeatCount = 2;
            player.m_Items[2].m_Timing.m_RepeatGap = 0.05f;
            player.m_Items[3].m_Timing.m_Rule = StartRule.AfterAll;
            EditorWindow inspector = Inspect(crate);
            yield return Settle;
            VisualElement strip = PlayerRoot(inspector, player).Query(className: InspectorStyles.Strip).First();
            Check(strip != null, "no timeline strip");
            foreach (int w in Capture(inspector, Within(inspector, strip), shot.id))
                yield return w;
        }

        // the debugger's Layers: two plays writing one value, paused in Play mode
        static IEnumerable<int> Layers(Shot shot)
        {
            foreach (int w in TwoPlays())
                yield return w;
            foreach (string panel in FeedbackDebuggerWindow.Panels)
                EditorPrefs.SetBool(FeedbackDebuggerWindow.FoldKey + panel, panel == "Layers");
            FeedbackDebuggerWindow window = OpenDebugger(new Rect(80, 80, 900, 180));
            yield return Settle * 3;
            foreach (int w in Capture(window, Whole(window), shot.id))
                yield return w;
        }

        // the Timeline window on the cutscene's director
        static IEnumerable<int> TimelineShot(Shot shot)
        {
            OpenSample("Advanced/TimelineCutscene/TimelineCutscene.unity");
            GameObject director = Find<Transform>("Cutscene").gameObject;
            Selection.activeGameObject = director;
            Type type = TypeCache.GetTypesDerivedFrom<EditorWindow>().FirstOrDefault(t => t.FullName == "UnityEditor.Timeline.TimelineWindow");
            Check(type != null, "no Timeline window type");
            var window = (EditorWindow)ScriptableObject.CreateInstance(type);
            window.Show();
            Float(window, new Rect(80, 80, 960, 360));
            yield return Settle;
            Selection.activeGameObject = null;
            yield return Settle;
            Selection.activeGameObject = director;
            yield return Settle * 3;
            foreach (int w in Capture(window, Whole(window), shot.id))
                yield return w;
        }

        // ---- the states

        static void NewScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static void OpenSample(string path)
        {
            Scene scene = EditorSceneManager.OpenScene(SamplesRoot + "/" + path, OpenSceneMode.Single);
            Check(scene.IsValid(), "no scene " + path);
        }

        // a cube without its collider — nothing for the validator to note about moving it
        static GameObject Cube(string name)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static CatalogLeaf Leaf(string path)
        {
            foreach (CatalogLeaf leaf in FeedbackCatalog.All)
                if (leaf.Path == path)
                    return leaf;
            throw new ShotFailed("no Add menu entry " + path);
        }

        static FeedbackPlayer Player(GameObject go, params string[] paths)
        {
            var player = go.AddComponent<FeedbackPlayer>();
            foreach (string path in paths)
                player.m_Items.Add(ItemFactory.Create(Leaf(path)));
            EditorUtility.SetDirty(player);
            if (go.scene.path == ScenePath)
                EditorSceneManager.SaveScene(go.scene);
            return player;
        }

        static IEnumerable<T> InScene<T>() where T : Component
            => Resources.FindObjectsOfTypeAll<T>().Where(c => c.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(c));

        static T Find<T>(string name) where T : Component
        {
            T found = InScene<T>().FirstOrDefault(c => c.name == name);
            Check(found != null, $"no {typeof(T).Name} named '{name}'");
            return found;
        }

        // Getting Started's Jump: the player on 'Body' with its own list
        static FeedbackPlayer Jump()
        {
            FeedbackPlayer found = InScene<FeedbackPlayer>().FirstOrDefault(p => p.name == "Body" && p.m_Preset == null);
            Check(found != null, "no Jump player on 'Body'");
            return found;
        }

        static FeedbackPlayer Linking(string preset)
        {
            FeedbackPlayer found = InScene<FeedbackPlayer>().FirstOrDefault(p => p.m_Preset != null && p.m_Preset.name == preset);
            Check(found != null, $"no player linking '{preset}'");
            return found;
        }

        static IEnumerable<int> PausePreview(FeedbackPlayer player, double at)
        {
            Check(PreviewSession.Start(player, Args) == PreviewRefusal.None, "the preview did not start");
            Check(PreviewSession.Current.Scrub(at) == SeekRefusal.None, "the preview cannot seek");
            yield return Settle;
            Check(PreviewSession.Current != null, "the preview ended");
            Check(PreviewSession.Current.ScrubState(out _, out _, out double place) == SeekRefusal.None && Math.Abs(place - at) < 1e-3, $"the preview is not paused at {at:F2} s");
        }

        // in Play mode without a domain reload (this run's project), Jump and Land played on 'Body', each paused at 0.1 s
        // (the players' own pause and seek, as the preview's scrub) and the editor paused
        static IEnumerable<int> TwoPlays()
        {
            OpenSample("Basics/GettingStarted/Complete/GettingStarted.unity");
            foreach (int w in EnterPlay())
                yield return w;
            EditorApplication.isPaused = true;
            yield return Settle;
            FeedbackPlayer jump = Jump();
            FeedbackPlayer land = Linking("Land");
            Selection.activeGameObject = jump.gameObject;
            foreach (FeedbackPlayer player in new[] { jump, land })
            {
                PlaybackHandle handle = player.Play(Args);
                Check(player.SeekRange(handle, out _, out _) == SeekRefusal.None, "a play cannot seek");
                player.Pause();
                Check(player.SeekAt(handle, TimelineTime.FromSeconds(0.1)), "a play did not seek");
            }
            EditorApplication.Step();
            yield return Settle;
            Check(jump.IsPlaying && land.IsPlaying && jump.HasPaused && land.HasPaused, "the two plays are not paused");
        }

        static IEnumerable<int> EnterPlay()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.isPlaying = true;
            for (int i = 0; i < 600 && !EditorApplication.isPlaying; i++)
                yield return 1;
            Check(EditorApplication.isPlaying, "Play mode did not start");
            Log("play mode");
            yield return Settle * 3;
        }

        static IEnumerable<int> Reset()
        {
            PreviewSession.Current?.Stop();
            foreach (PopupWindow w in Resources.FindObjectsOfTypeAll<PopupWindow>())
                w.Close();
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPaused = false;
                EditorApplication.isPlaying = false;
                for (int i = 0; i < 600 && EditorApplication.isPlaying; i++)
                    yield return 1;
            }
            CloseWindows();
            foreach (string panel in FeedbackDebuggerWindow.Panels)
                EditorPrefs.DeleteKey(FeedbackDebuggerWindow.FoldKey + panel);
            EditorPrefs.DeleteKey(FeedbackCatalog.FavoritesKey);
            EditorPrefs.DeleteKey(FeedbackCatalog.RecentKey);
            yield return Settle;
        }

        // ---- the windows

        static EditorWindow Open(string typeName, Rect place)
        {
            Type type = typeof(EditorWindow).Assembly.GetType(typeName);
            var window = (EditorWindow)ScriptableObject.CreateInstance(type);
            window.Show();
            window.position = place;
            s_Windows.Add(window);
            return window;
        }

        static void Float(EditorWindow window, Rect place)
        {
            window.position = place;
            s_Windows.Add(window);
            EditorApplication.delayCall += () =>
            {
                if (window != null)
                    window.position = place;
            };
        }

        // a floating inspector on the object, its other components folded
        static EditorWindow Inspect(GameObject go)
        {
            foreach (Component c in go.GetComponents<Component>())
                if (c != null && !(c is FeedbackPlayer) && !(c is MonoBehaviour))
                    UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded(c, false);
            Selection.activeGameObject = go;
            return Open("UnityEditor.InspectorWindow", new Rect(120, 60, 420, 860));
        }

        static FeedbackDebuggerWindow OpenDebugger(Rect place)
        {
            FeedbackDebuggerWindow window = FeedbackDebuggerWindow.Open();
            Float(window, place);
            return window;
        }

        static void CloseWindows()
        {
            foreach (EditorWindow w in s_Windows)
                if (w != null)
                    w.Close();
            s_Windows.Clear();
            s_Covering = null;
        }

        static IEnumerable<int> OpenAddMenu(EditorWindow inspector, FeedbackPlayer player)
        {
            VisualElement root = PlayerRoot(inspector, player);
            ItemListView list = root.Query<ItemListView>().First();
            VisualElement add = root.Query(className: InspectorStyles.AddButton).First();
            Check(list != null && add != null, "no item list or Add Feedback button");
            inspector.Focus();
            list.OpenAddMenu(add.worldBound);
            yield return Settle;
            Check(AddMenuPopup.Last != null && Popup() != null, "the Add menu did not open");
        }

        static EditorWindow Popup() => Resources.FindObjectsOfTypeAll<PopupWindow>().FirstOrDefault();

        // the player's editor: its root in the inspector (the package's inspector class)
        static VisualElement PlayerRoot(EditorWindow inspector, FeedbackPlayer player)
        {
            VisualElement whole = ComponentElement(inspector, player);
            VisualElement root = whole?.Query(className: InspectorStyles.Root).First();
            Check(root != null, "the inspector shows no player editor");
            return root;
        }

        // a component's whole editor in the inspector — its title bar and its fields
        static VisualElement ComponentElement(EditorWindow inspector, Object target)
        {
            foreach (VisualElement v in inspector.rootVisualElement.Query<VisualElement>().ToList())
            {
                if (v.GetType().Name != "EditorElement")
                    continue;
                PropertyInfo editor = v.GetType().GetProperty("editor", Any);
                if (editor?.GetValue(v) is UnityEditor.Editor e && e.target == target)
                    return v;
            }
            return null;
        }

        static IEnumerable<int> CapturePlayer(EditorWindow inspector, FeedbackPlayer player, string name)
            => CaptureComponent(inspector, player, name);

        static IEnumerable<int> CaptureComponent(EditorWindow inspector, Object target, string name)
        {
            VisualElement whole = ComponentElement(inspector, target);
            Check(whole != null, "the inspector does not show " + target.GetType().Name);
            Rect crop = Within(inspector, whole);
            Check(crop.yMax <= inspector.position.height, $"the component ({crop.height:F0} pt) does not fit the inspector");
            return Capture(inspector, crop, name);
        }

        static IEnumerable<int> CapturePopup(string name)
        {
            EditorWindow popup = Popup();
            Check(popup != null, "no popup");
            return Capture(popup, new Rect(0, 0, popup.position.width, popup.position.height), name);
        }

        // a window's content without its rounded bottom corners
        static Rect Whole(EditorWindow w) => new Rect(0, 0, w.position.width, w.position.height - Corner);

        // an element's rectangle in the window's content, in points, with a margin inside the window
        static Rect Within(EditorWindow w, VisualElement e)
        {
            const float margin = 4;
            Rect r = e.worldBound;
            Vector2 origin = w.rootVisualElement.worldBound.position;
            return Rect.MinMaxRect(Mathf.Max(0, r.xMin - origin.x - margin), Mathf.Max(0, r.yMin - origin.y - margin),
                Mathf.Min(w.position.width, r.xMax - origin.x + margin), Mathf.Min(w.position.height, r.yMax - origin.y + margin));
        }

        static void Check(bool ok, string why)
        {
            if (!ok)
                throw new ShotFailed(why);
        }

        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // the tool captures the window and keeps the crop once two captures in a row are the same
        static IEnumerable<int> Capture(EditorWindow w, Rect crop, string name)
        {
            Check(crop.width >= 1 && crop.height >= 1, $"{name}: an empty crop");
            w.Repaint();
            yield return Settle;
            if (s_Cover && !(w is PopupWindow))
            {
                if (s_Covering == null)
                {
                    s_Covering = (EditorWindow)ScriptableObject.CreateInstance(typeof(EditorWindow).Assembly.GetType("UnityEditor.ConsoleWindow"));
                    s_Covering.Show();
                    s_Windows.Add(s_Covering);
                }
                s_Covering.position = new Rect(w.position.x + 40, w.position.y + 40, 320, 240);
                s_Covering.Focus();
                yield return Settle;
            }
            Rect r = w.position;
            string done = Path.Combine(s_Out, $"done-{name}.txt");
            File.Delete(done);
            string ready = Path.Combine(s_Out, $"ready-{name}.txt");
            string pid = Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
            File.WriteAllText(ready + ".tmp", string.Join(" ", new[] { pid, F(EditorGUIUtility.pixelsPerPoint), F(r.x), F(r.y), F(r.width), F(r.height), F(crop.x), F(crop.y), F(crop.width), F(crop.height) }));
            File.Move(ready + ".tmp", ready);
            double until = EditorApplication.timeSinceStartup + 60;
            while (!File.Exists(done))
            {
                Check(EditorApplication.timeSinceStartup < until, $"{name}: no answer from the tool");
                w.Repaint();
                yield return 3;
            }
            string answer = File.ReadAllText(done).Trim();
            Check(answer.StartsWith("ok", StringComparison.Ordinal), $"{name}: {answer}");
        }

        // ---- the package's editor preferences

        static string Backup => Path.Combine(s_State, "prefs-backup.txt");
        static Dictionary<string, (string kind, string value)> s_Saved;

        // the package's keys a run writes by itself — the floating windows' places, written when they close
        static IEnumerable<string> Written()
            => new[] { typeof(FeedbackDebuggerWindow), typeof(FeedbackValidatorWindow) }.SelectMany(t => "xywhz".Select(c => t.FullName + c));

        // the package's keys in the editor preferences' file, through defaults(1). The editor writes its own changes to the
        // file later (at the latest when it quits) — this is the file, not the values the editor holds now
        static Dictionary<string, (string kind, string value)> PackageKeys()
        {
            var keys = new Dictionary<string, (string, string)>();
            var start = new ProcessStartInfo("/usr/bin/defaults", "export " + Domain + " -") { RedirectStandardOutput = true, UseShellExecute = false };
            string xml;
            using (Process p = Process.Start(start))
            {
                xml = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
            }
            var doc = new XmlDocument { XmlResolver = null };
            doc.LoadXml(xml.Substring(xml.IndexOf("<plist", StringComparison.Ordinal)));
            XmlNode dict = doc.SelectSingleNode("/plist/dict");
            for (XmlNode n = dict?.FirstChild; n != null; n = n.NextSibling)
            {
                if (n.Name != "key")
                    continue;
                XmlNode v = n.NextSibling;
                while (v != null && v.NodeType != XmlNodeType.Element)
                    v = v.NextSibling;
                string key = n.InnerText;
                if (v != null && key.StartsWith(Prefix, StringComparison.Ordinal) && !key.StartsWith(Prefix + "Tests.", StringComparison.Ordinal))
                    keys[key] = (v.Name, v.InnerText);
            }
            return keys;
        }

        // the values to put back: a killed run's backup (its values from before that run) or the file's now, written to the
        // backup; then every package key is deleted — the run sees the defaults
        static void SavePrefs()
        {
            Dictionary<string, (string kind, string value)> file = PackageKeys();
            if (File.Exists(Backup))
            {
                s_Saved = new Dictionary<string, (string kind, string value)>();
                foreach (string line in File.ReadAllLines(Backup))
                {
                    string[] f = line.Split('\t');
                    if (f.Length == 3)
                        s_Saved[f[0]] = (f[1], System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(f[2])));
                }
                Log($"prefs from the backup of a run that did not finish {s_Saved.Count}");
            }
            else
            {
                s_Saved = file;
                File.WriteAllLines(Backup + ".tmp", s_Saved.Select(k => k.Key + "\t" + k.Value.kind + "\t" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(k.Value.value))));
                File.Move(Backup + ".tmp", Backup);
                Log($"prefs saved {s_Saved.Count}");
            }
            foreach (string key in s_Saved.Keys.Union(file.Keys))
                EditorPrefs.DeleteKey(key);
        }

        // the saved values back, and the package's other keys deleted
        static void RestorePrefs()
        {
            if (s_Saved == null)
                return;
            foreach (string key in PackageKeys().Keys.Union(Written()).Where(k => !s_Saved.ContainsKey(k)))
                EditorPrefs.DeleteKey(key);
            foreach (KeyValuePair<string, (string kind, string value)> k in s_Saved)
            {
                switch (k.Value.kind)
                {
                    case "integer":
                        EditorPrefs.SetInt(k.Key, int.Parse(k.Value.value, CultureInfo.InvariantCulture));
                        break;
                    case "real":
                        EditorPrefs.SetFloat(k.Key, float.Parse(k.Value.value, CultureInfo.InvariantCulture));
                        break;
                    case "string":
                        EditorPrefs.SetString(k.Key, k.Value.value);
                        break;
                    case "true":
                    case "false":
                        EditorPrefs.SetBool(k.Key, k.Value.kind == "true");
                        break;
                    default:
                        Log($"prefs kept as they are: {k.Key} ({k.Value.kind})");
                        break;
                }
            }
            File.Delete(Backup);
            Log($"prefs restored {s_Saved.Count}");
        }
    }
}
