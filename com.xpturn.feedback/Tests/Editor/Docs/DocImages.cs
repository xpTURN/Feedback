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
        static Func<double> s_Clock;
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
            s_Clock = PreviewClock.Source;
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
                case "Eases": return EasesShot(shot);
                case "Motions": return MotionsShot(shot);
                case "Graphs": return GraphsShot(shot);
                case "GraphsPlay": return GraphsPlay(shot);
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

        // the ease field's picker grid with every cell in sight, in a window of its own — the picker is a popup that scrolls
        static IEnumerable<int> EasesShot(Shot shot)
        {
            const int columns = 8;
            const float cell = 76f;
            const float row = 58f;
            int cells = EaseField.Thumbed().Count() + 1;
            int rows = (cells + columns - 1) / columns;
            EditorWindow window = Canvas("Eases", new Rect(120, 100, columns * cell + 24, rows * row + 48));
            VisualElement grid = new EasePopup(_ => { }).CreateGUI();
            window.rootVisualElement.Add(grid);
            yield return Settle;
            VisualElement inner = grid.Query(className: InspectorStyles.EaseGrid).First();
            Check(inner != null, "no ease grid");
            List<VisualElement> shown = inner.Query(className: InspectorStyles.EaseCell).ToList();
            Rect bound = window.rootVisualElement.worldBound;
            Check(shown.Count == Enum.GetValues(typeof(EaseType)).Length, $"{shown.Count} ease cells for {Enum.GetValues(typeof(EaseType)).Length} eases");
            Check(shown.All(c => c.worldBound.yMax <= bound.yMax && c.worldBound.xMax <= bound.xMax), "an ease cell is out of sight");
            foreach (int w in Capture(window, Within(window, inner), shot.id))
                yield return w;
        }

        // each motion's shape over one item of 1 s (a Spring over the time it takes to settle) at intensity 1, with the
        // motions' own defaults: an Offset motion's shape, a Tween's or a Spring's value, a Flash's or a Blink's weight, a
        // Gradient's colors. A Set is drawn from before its moment; a Set and a Gradient take values of their own (their
        // defaults are 0 and white)
        static IEnumerable<int> MotionsShot(Shot shot)
        {
            const int columns = 4;
            const float graphWidth = 136f;
            const float graphHeight = 52f;
            var grid = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, paddingLeft = 6, paddingTop = 6 } };
            int count = 0;
            foreach (MotionKind kind in Enum.GetValues(typeof(MotionKind)))
            {
                FeedbackMotion motion = DocSources.MotionData()[kind];
                if (motion is SetMotion set)
                    set.m_Params.m_Value = Vector4.one;
                if (motion is GradientMotion gradient)
                    gradient.m_Params.m_Gradient = new Gradient { colorKeys = new[] { new GradientColorKey(new Color(1f, 0.55f, 0.1f), 0f), new GradientColorKey(new Color(0.85f, 0.2f, 0.6f), 0.5f), new GradientColorKey(new Color(0.2f, 0.45f, 1f), 1f) } };
                var cell = new VisualElement { style = { width = graphWidth + 8, marginRight = 6, marginBottom = 8 } };
                cell.Add(new Label(kind.ToString()));
                cell.Add(Graph(motion, graphWidth, graphHeight));
                grid.Add(cell);
                count++;
            }
            int rows = (count + columns - 1) / columns;
            EditorWindow window = Canvas("Motions", new Rect(120, 100, columns * (graphWidth + 14) + 24, rows * (graphHeight + 30) + 48));
            window.rootVisualElement.Add(grid);
            yield return Settle;
            Rect bound = window.rootVisualElement.worldBound;
            Check(grid.childCount == Enum.GetValues(typeof(MotionKind)).Length, "a graph per motion");
            Check(grid.Children().All(c => c.worldBound.yMax <= bound.yMax && c.worldBound.xMax <= bound.xMax), "a graph is out of sight");
            foreach (int w in Capture(window, Within(window, grid), shot.id))
                yield return w;
        }

        // a floating window of the tests' own type, for an element the driver puts in it
        static EditorWindow Canvas(string title, Rect place)
        {
            var window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            window.titleContent = new GUIContent(title);
            window.Show();
            Float(window, place);
            InspectorStyles.Attach(window.rootVisualElement);
            return window;
        }

        // a motion's shape in a box: the values at 97 even steps of the item, a zero line, the box's height the values' range
        // (an Offset motion's symmetric about zero)
        static VisualElement Graph(FeedbackMotion motion, float boxWidth, float boxHeight)
        {
            // a Gradient's four components are its color
            MotionEvaluator e = MotionEvaluators.Create(motion, motion is GradientMotion ? 4 : 1);
            var state = new MotionState();
            e.Begin(ref state, Vector4.zero, new MotionBegin { Seed = 20251108u, Intensity = 1f });
            const int steps = 97;
            float length = motion.NaturalDuration > 0f ? motion.NaturalDuration : 1f;
            bool instant = e.IsInstant;
            var values = new float[steps];
            var colors = motion is GradientMotion ? new Color[steps] : null;
            for (int i = 0; i < steps; i++)
            {
                float t = i / (float)(steps - 1);
                // a Set from before its moment: its first fifth is the value it found
                if (instant && t < 0.2f)
                {
                    values[i] = 0f;
                    continue;
                }
                var time = new MotionTime(instant ? 0f : t, instant ? 0.0 : t * (double)length);
                e.Evaluate(in state, in time, out MotionSample sample);
                if (colors != null)
                    colors[i] = new Color(sample.Value.x, sample.Value.y, sample.Value.z, 1f);
                values[i] = e.Write == MotionWrite.Offset ? e.EvaluateShape(in state, in time) : e.Write == MotionWrite.Override ? sample.Weight : sample.Value.x;
            }
            float lo, hi;
            if (e.Write == MotionWrite.Offset)
            {
                float m = Mathf.Max(1e-6f, values.Max(v => Mathf.Abs(v)));
                lo = -m;
                hi = m;
            }
            else
            {
                lo = Mathf.Min(0f, values.Min());
                hi = Mathf.Max(1f, values.Max());
            }
            var box = new VisualElement { style = { width = boxWidth, height = boxHeight, marginTop = 2 } };
            box.generateVisualContent += context =>
            {
                Rect r = box.contentRect;
                Painter2D p = context.painter2D;
                Color ink = box.resolvedStyle.color;
                float Y(float v) => r.yMax - 2f - (v - lo) / (hi - lo) * (r.height - 4f);
                if (colors != null)
                {
                    float step = r.width / (steps - 1);
                    for (int i = 0; i < steps - 1; i++)
                    {
                        p.fillColor = colors[i];
                        p.BeginPath();
                        p.MoveTo(new Vector2(r.x + i * step, r.y + 2f));
                        p.LineTo(new Vector2(r.x + (i + 1) * step + 0.5f, r.y + 2f));
                        p.LineTo(new Vector2(r.x + (i + 1) * step + 0.5f, r.yMax - 2f));
                        p.LineTo(new Vector2(r.x + i * step, r.yMax - 2f));
                        p.ClosePath();
                        p.Fill();
                    }
                    return;
                }
                p.strokeColor = new Color(ink.r, ink.g, ink.b, 0.35f);
                p.lineWidth = 1f;
                p.BeginPath();
                p.MoveTo(new Vector2(r.x, Y(0f)));
                p.LineTo(new Vector2(r.xMax, Y(0f)));
                p.Stroke();
                p.strokeColor = ink;
                p.lineWidth = 1.5f;
                p.BeginPath();
                for (int i = 0; i < steps; i++)
                {
                    var at = new Vector2(r.x + i / (float)(steps - 1) * r.width, Y(values[i]));
                    if (i == 0)
                        p.MoveTo(at);
                    else
                        p.LineTo(at);
                }
                p.Stroke();
            };
            return box;
        }

        // the graphs: a cube's player of a Punch on its position's Y and a small Shake, the Graph Preview on and the first
        // item's body and Graph open, Dots on, the preview played on a stepped clock to 0.3 s and paused — the part from the
        // strip to the Graph Preview below the bottom bar
        static IEnumerable<int> GraphsShot(Shot shot)
        {
            NewScene();
            GameObject crate = Cube("Crate");
            var player = crate.AddComponent<FeedbackPlayer>();
            var punch = (TargetItem)ItemFactory.Create(Leaf("Transform/Position/Punch"));
            ((PunchMotion)punch.m_Motion).m_Params.m_Amplitude = new Vector4(0f, 0.5f, 0f, 0f);
            punch.m_Timing.m_Duration = 0.4f;
            var shake = (TargetItem)ItemFactory.Create(Leaf("Transform/Position/Shake"));
            ((ShakeMotion)shake.m_Motion).m_Params.m_Amplitude = new Vector4(0.1f, 0.1f, 0.1f, 0f);
            shake.m_Timing.m_Duration = 0.4f;
            player.m_Items.Add(punch);
            player.m_Items.Add(shake);
            // the items' reference ids fixed before the player is first serialized: an item's random parts are keyed by its
            // id, so the Shake's noise is the same at each run
            Check(UnityEngine.Serialization.ManagedReferenceUtility.SetManagedReferenceIdForObject(player, punch, 1000)
                && UnityEngine.Serialization.ManagedReferenceUtility.SetManagedReferenceIdForObject(player, shake, 1001), "the items' reference ids cannot be set");
            EditorUtility.SetDirty(player);
            EditorSceneManager.SaveScene(crate.scene);
            long rid = new SerializedObject(player).FindProperty("m_Items").GetArrayElementAtIndex(0).managedReferenceId;
            Check(rid == 1000, "the first item's reference id is " + rid);
            string fold = ItemListView.FoldKey(player, rid);
            SessionState.SetBool(SlotGraphField.SwitchPrefix + player.GetEntityId(), true);
            SessionState.SetBool(fold, true);
            SessionState.SetBool(fold + ItemGraphField.FoldSuffix, true);
            EditorPrefs.SetBool(MotionGraphView.DotsKey, true);
            EditorWindow inspector = Inspect(crate);
            yield return Settle;
            double now = 1000.0;
            PreviewClock.Source = () => now;
            Check(PreviewSession.Start(player, Args) == PreviewRefusal.None, "the preview did not start");
            // the clock's first reading is the start, then 18 ticks
            yield return 1;
            for (int i = 0; i < 18; i++)
            {
                now += PreviewClock.TickInterval + 1e-7;
                yield return 1;
            }
            player.Pause();
            yield return Settle;
            Check(PreviewSession.Current != null && PreviewSession.Current.ScrubState(out _, out _, out double place) == SeekRefusal.None && Math.Abs(place - 0.3) < 0.01, "the preview is not paused at 0.30 s");
            var strip = inspector.rootVisualElement.Q<TimelineStrip>();
            var slot = inspector.rootVisualElement.Q<SlotGraphField>();
            var graph = inspector.rootVisualElement.Q<ItemGraphField>();
            Check(strip != null && slot != null && graph != null, "no strip, Graph Preview or item's Graph");
            Check(slot.View.Dots != null && slot.View.Dots.Count > 0 && graph.View.Dots != null && graph.View.Dots.Count > 0, "the graphs recorded no frame");
            Check(slot.Notes.text.Contains("Play 1") && graph.Notes.text.Contains("Play 1"), "the graphs do not follow the preview's play");
            ScrollTo(inspector, strip);
            yield return Settle;
            Vector2 origin = inspector.rootVisualElement.worldBound.position;
            float top = strip.worldBound.yMin - origin.y - 4f;
            float bottom = slot.worldBound.yMax - origin.y + 4f;
            Check(top >= 0f && bottom <= inspector.position.height, $"the strip to the Graph Preview ({bottom - top:F0} pt) does not fit the inspector");
            foreach (int w in Capture(inspector, Rect.MinMaxRect(0f, top, Within(inspector, strip).xMax, bottom), shot.id))
                yield return w;
        }

        // the README's graphs: a cube's player of a Punch on its position's Y and a Shake that starts later, the Graph Preview
        // on and the items folded, Dots on, the preview played to its end on a stepped clock — a frame every two ticks, its
        // last frame held — the part from the strip to the Graph Preview below the bottom bar, the same in every frame.
        // frames-<id>.txt lists the frames and their times
        static IEnumerable<int> GraphsPlay(Shot shot)
        {
            NewScene();
            GameObject crate = Cube("Crate");
            var player = crate.AddComponent<FeedbackPlayer>();
            var punch = (TargetItem)ItemFactory.Create(Leaf("Transform/Position/Punch"));
            ((PunchMotion)punch.m_Motion).m_Params.m_Amplitude = new Vector4(0f, 0.5f, 0f, 0f);
            punch.m_Timing.m_Duration = 0.8f;
            var shake = (TargetItem)ItemFactory.Create(Leaf("Transform/Position/Shake"));
            ((ShakeMotion)shake.m_Motion).m_Params.m_Amplitude = new Vector4(0.15f, 0.15f, 0.15f, 0f);
            shake.m_Timing.m_Delay = 0.3f;
            shake.m_Timing.m_Duration = 0.5f;
            player.m_Items.Add(punch);
            player.m_Items.Add(shake);
            // the items' reference ids fixed before the player is first serialized: an item's random parts are keyed by its
            // id, so the Shake's noise is the same at each run
            Check(UnityEngine.Serialization.ManagedReferenceUtility.SetManagedReferenceIdForObject(player, punch, 1000)
                && UnityEngine.Serialization.ManagedReferenceUtility.SetManagedReferenceIdForObject(player, shake, 1001), "the items' reference ids cannot be set");
            EditorUtility.SetDirty(player);
            EditorSceneManager.SaveScene(crate.scene);
            long rid = new SerializedObject(player).FindProperty("m_Items").GetArrayElementAtIndex(0).managedReferenceId;
            Check(rid == 1000, "the first item's reference id is " + rid);
            SessionState.SetBool(SlotGraphField.SwitchPrefix + player.GetEntityId(), true);
            EditorPrefs.SetBool(MotionGraphView.DotsKey, true);
            EditorWindow inspector = Inspect(crate);
            yield return Settle;
            double now = 1000.0;
            PreviewClock.Source = () => now;
            Check(PreviewSession.Start(player, Args) == PreviewRefusal.None, "the preview did not start");
            // the clock's first reading is the start
            yield return 1;
            Check(PreviewSession.Current.ScrubState(out _, out double high, out _) == SeekRefusal.None, "the preview cannot seek");
            var strip = inspector.rootVisualElement.Q<TimelineStrip>();
            var slot = inspector.rootVisualElement.Q<SlotGraphField>();
            List<ItemHeader> headers = PlayerRoot(inspector, player).Query<ItemHeader>().ToList();
            Check(strip != null && slot != null && headers.Count == 2, $"no strip or Graph Preview, or {headers.Count} item headers");
            Check(inspector.rootVisualElement.Q<ItemGraphField>() == null, "an item is open");
            VisualElement last = slot;
            ScrollTo(inspector, strip);
            yield return Settle;
            Check(slot.Notes.text.Contains("Play 1"), "the Graph Preview does not follow the preview's play");
            Vector2 origin = inspector.rootVisualElement.worldBound.position;
            float top = strip.worldBound.yMin - origin.y - 4f;
            float bottom = last.worldBound.yMax - origin.y + 4f;
            Check(top >= 0f && bottom <= inspector.position.height, $"the strip to the Graph Preview ({bottom - top:F0} pt) does not fit the inspector");
            Rect crop = Rect.MinMaxRect(0f, top, Within(inspector, strip).xMax, bottom);
            const int ticks = 2;
            int steps = (int)Math.Ceiling(high / (ticks * PreviewClock.TickInterval) - 1e-6);
            var frames = new List<string>();
            for (int k = 0; k <= steps; k++)
            {
                if (k > 0)
                {
                    for (int i = 0; i < ticks; i++)
                    {
                        now += PreviewClock.TickInterval + 1e-7;
                        yield return 1;
                    }
                }
                string name = $"{shot.id}-f{frames.Count:000}";
                foreach (int w in Capture(inspector, crop, name))
                    yield return w;
                origin = inspector.rootVisualElement.worldBound.position;
                Check(Math.Abs(strip.worldBound.yMin - origin.y - 4f - top) < 0.5f && Math.Abs(last.worldBound.yMax - origin.y + 4f - bottom) < 0.5f, $"the strip or the Graph Preview moved at frame {k}");
                frames.Add(name + " " + F(k == steps ? HeldFrame : FrameStep));
            }
            Check(slot.View.Dots != null && slot.View.Dots.Count > 0, "the Graph Preview recorded no frame");
            File.WriteAllLines(Path.Combine(s_Out, $"frames-{shot.id}.txt"), frames);
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
            EditorPrefs.DeleteKey(MotionGraphView.DotsKey);
            PreviewClock.Source = s_Clock;
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
            ScrollTo(inspector, whole);
            yield return Settle;
            float bottom = whole.worldBound.yMax - inspector.rootVisualElement.worldBound.y + 4f;
            Check(bottom <= inspector.position.height, $"the component ({whole.worldBound.height:F0} pt) does not fit the inspector");
            foreach (int w in Capture(inspector, Within(inspector, whole), name))
                yield return w;
        }

        // the inspector scrolled so the element's top is near the window's top, by whole points — content that fits the window
        // does not scroll, and a whole point keeps the pixels as they were
        static void ScrollTo(EditorWindow inspector, VisualElement e)
        {
            ScrollView scroll = inspector.rootVisualElement.Q<ScrollView>();
            if (scroll == null)
                return;
            float y = Mathf.Round(e.worldBound.yMin - scroll.contentContainer.worldBound.yMin - 4f);
            scroll.scrollOffset = new Vector2(scroll.scrollOffset.x, Mathf.Max(0f, y));
            scroll.scrollOffset = new Vector2(scroll.scrollOffset.x, Mathf.Floor(scroll.scrollOffset.y));
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

        // the package's keys a run writes by itself — the floating windows' places, written when they close. The canvas is a
        // window of the tests' own type: its keys are kept and put back with the package's, the tests' other keys are not
        static IEnumerable<string> Written()
            => new[] { typeof(FeedbackDebuggerWindow), typeof(FeedbackValidatorWindow), typeof(PreviewBarTestWindow) }.SelectMany(t => "xywhz".Select(c => t.FullName + c));

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
            var written = new HashSet<string>(Written());
            for (XmlNode n = dict?.FirstChild; n != null; n = n.NextSibling)
            {
                if (n.Name != "key")
                    continue;
                XmlNode v = n.NextSibling;
                while (v != null && v.NodeType != XmlNodeType.Element)
                    v = v.NextSibling;
                string key = n.InnerText;
                if (v != null && key.StartsWith(Prefix, StringComparison.Ordinal) && (!key.StartsWith(Prefix + "Tests.", StringComparison.Ordinal) || written.Contains(key)))
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
