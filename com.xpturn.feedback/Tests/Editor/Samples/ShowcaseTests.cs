using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using xpTURN.Feedback.Samples;
using static xpTURN.Feedback.Tests.Editor.Samples.SampleKitTestSupport;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace xpTURN.Feedback.Tests.Editor.Samples
{
    // the bundle · the scene · the board · combo and score · the combo's feedback · bricks and numbers · the boss · the same game
    // in Before · again and away · input · no TMP · allocation. The sample's types are reached by name (this assembly references
    // neither the sample nor TMP); the game is driven through the lab's game step with the board's seam (the ball put somewhere) —
    // a hit stop or a slow motion is real time
    public sealed class ShowcaseTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const string LabType = "xpTURN.Feedback.Samples.Showcase.BrickBreakerLab, xpTURN.Feedback.Samples.Showcase.BrickBreaker";
        const string BoardType = "xpTURN.Feedback.Samples.Showcase.BrickBoard, xpTURN.Feedback.Samples.Showcase.BrickBreaker";
        const float Step60 = 1f / 60f;
        static readonly Type s_Volume = Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
        static int s_Played;
        static int s_Warnings;

        [TearDown]
        public void LeaveTheScene()
        {
            if (!EditorApplication.isPlaying && SceneManager.GetActiveScene().path.Contains("/Showcase/"))
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Application.logMessageReceived -= CountWarnings;
            if (!EditorApplication.isPlaying)
                yield break;
            Time.captureDeltaTime = 0f;
            Application.targetFrameRate = -1;
            SampleState.Restore();
            yield return new ExitPlayMode();
        }

        static string Scene()
        {
            string path = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "BrickBreaker" && p.Contains("/Showcase/"));
            if (path == null)
                Assert.Ignore("no Showcase bundle in this project");
            return path;
        }

        // n game frames — an editor tick is not one when the frame rate is capped
        static IEnumerator Frames(int n)
        {
            int until = Time.frameCount + n;
            while (Time.frameCount < until)
                yield return null;
        }

        static IEnumerator Real(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
                yield return null;
        }

        static IEnumerator Open()
        {
            EditorSceneManager.LoadSceneInPlayMode(Scene(), new LoadSceneParameters(LoadSceneMode.Single));
            yield return Frames(20);
        }

        static Component Lab() => Resources.FindObjectsOfTypeAll<MonoBehaviour>().First(m => m != null && m.GetType().Name == "BrickBreakerLab" && m.gameObject.scene.isLoaded);

        static T F<T>(object o, string field) => (T)o.GetType().GetField(field, All).GetValue(o);

        static object P(object o, string property) => o.GetType().GetProperty(property, All).GetValue(o);

        static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, All).Invoke(o, args);

        static object Const(string name) => Type.GetType(BoardType, true).GetField(name, All).GetValue(null);

        static object Board(Component lab) => P(lab, "Board");

        static Vector2 BrickCenter(int i) => (Vector2)Type.GetType(BoardType, true).GetMethod("BrickCenter", All).Invoke(null, new object[] { i });

        static void OnPlayed(FeedbackPlayer p, PlaybackHandle h) => s_Played++;

        static void CountWarnings(string c, string st, LogType t)
        {
            if (t != LogType.Log)
                s_Warnings++;
        }

        // one game step with the ball put under a brick (or the boss) moving up — the event lands in this step
        static void Hit(Component lab, Vector2 target, float halfHeight)
        {
            object board = Board(lab);
            Call(board, "Put", target - new Vector2(0f, halfHeight + 0.2f), new Vector2(0f, 8f));
            Call(lab, "Step", Step60, (float)P(board, "PaddleX"));
            Call(board, "Hold");
        }

        static void BreakBottom(Component lab, int k) => Hit(lab, BrickCenter(16 + k), 0.25f);

        static FeedbackPlayer Field(Component lab, string field) => F<FeedbackPlayer>(lab, field);

        static float Weight(Behaviour v) => (float)s_Volume.GetField("weight").GetValue(v);

        static float Priority(Behaviour v) => (float)s_Volume.GetField("priority").GetValue(v);

        static float OverlayWeight() => Resources.FindObjectsOfTypeAll(s_Volume).Cast<Behaviour>()
            .Where(v => v.gameObject.scene.isLoaded && v.enabled && Priority(v) >= 1000f).Select(Weight).DefaultIfEmpty(0f).Max();

        static List<object> Items(FeedbackPlayer p) => ((IList)F<object>(p, "m_Items")).Cast<object>().ToList();

        static string Label(object item) => (string)item.GetType().GetField("m_Label", All).GetValue(item);

        static object Conditions(object item) => item.GetType().GetField("m_Conditions", All).GetValue(item);

        static Vector2? RangeOf(object item)
        {
            object c = Conditions(item);
            return (bool)c.GetType().GetField("m_UseIntensityRange", All).GetValue(c) ? (Vector2)c.GetType().GetField("m_IntensityRange", All).GetValue(c) : (Vector2?)null;
        }

        static int Clock(object item)
        {
            object timing = item.GetType().GetField("m_Timing", All).GetValue(item);
            return (int)timing.GetType().GetField("m_Clock", All).GetValue(timing);
        }

        // ---- the bundle: catalog · samples entry · asmdef (the particle system condition) · the card's keys bound

        static readonly Dictionary<string, string> s_KeyPaths = new Dictionary<string, string>
        {
            { "Space", "<Keyboard>/space" }, { "←", "<Keyboard>/leftArrow" }, { "→", "<Keyboard>/rightArrow" },
        };

        [Serializable]
        sealed class Asmdef
        {
            public string name = "";
            public string[] defineConstraints = new string[0];
        }

        [Test]
        public void WQ01_TheBundle()
        {
            Scene();
            SampleCatalog catalog = Catalogs().FirstOrDefault(c => c.Bundle == "Showcase");
            Assert.That(catalog, Is.Not.Null, "the Showcase catalog");
            Assert.That(catalog.Order, Is.EqualTo(50));
            Assert.That(catalog.Cards.Select(c => c.SceneName).ToArray(), Is.EqualTo(new[] { "BrickBreaker" }));
            PackageInfo package = PackageInfo.FindForAssetPath("Packages/com.xpturn.feedback");
            string json = File.ReadAllText(Path.Combine(package.resolvedPath, "package.json"));
            Assert.That(json.Contains("\"displayName\": \"Showcase\"") && json.Contains("\"path\": \"Samples~/Showcase\""), Is.True, "the samples entry");
            string[] files = Directory.GetFiles(Disk(BundleRoot(catalog)), "*.asmdef", SearchOption.AllDirectories);
            Assert.That(files.Length, Is.EqualTo(1));
            string text = File.ReadAllText(files[0]);
            var asmdef = JsonUtility.FromJson<Asmdef>(text);
            Assert.That(asmdef.name, Is.EqualTo("xpTURN.Feedback.Samples.Showcase.BrickBreaker"));
            Assert.That(asmdef.defineConstraints, Does.Contain("XPTURN_FEEDBACK_SAMPLES_PARTICLES"));
            Assert.That(text.Contains("com.unity.modules.particlesystem") && !text.Contains("Unity.TextMeshPro"), Is.True, "the module's version define · no TMP reference");
            SampleCard card = catalog.Cards[0];
            Assert.That(card.RequiresText, Is.EqualTo("the Particle System module"));
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            var bound = new HashSet<string>(asset.FindActionMap("Sample", true).bindings.Select(b => b.path));
            var problems = new List<string>();
            foreach (string token in (card.Controls ?? "").Split(new[] { '·', '—', ',', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string k = token.Trim();
                string path = s_KeyPaths.TryGetValue(k, out string known) ? known : k.Length == 1 && char.IsLetterOrDigit(k[0]) ? "<Keyboard>/" + char.ToLowerInvariant(k[0]) : null;
                if (path != null && !bound.Contains(path))
                    problems.Add(k);
            }
            Assert.That(problems, Is.Empty, "card keys not in the Sample map");
        }

        // ---- the scene: bricks (crack · break by range, no flash) · Impact (stops by ranges that don't overlap · rumble) · the
        // players with a time item StopRevert · the boss's lists · the volume · no physics

        [Test]
        public void WQ02_TheScene()
        {
            EditorSceneManager.OpenScene(Scene(), OpenSceneMode.Single);
            Component lab = Resources.FindObjectsOfTypeAll<MonoBehaviour>().First(m => m != null && m.gameObject.scene.isLoaded && m.GetType().Name == "BrickBreakerLab");
            var problems = new List<string>();
            FeedbackPlayer[] bricks = F<FeedbackPlayer[]>(lab, "m_BrickPlayers");
            Assert.That(bricks.Length, Is.EqualTo(32), "a player per brick");
            foreach (FeedbackPlayer b in bricks)
            {
                List<object> items = Items(b);
                foreach (object item in items)
                {
                    object motion = item.GetType().GetField("m_Motion", All)?.GetValue(item);
                    if (motion != null && (motion.GetType().Name == "FlashMotion" || motion.GetType().Name == "BlinkMotion"))
                        problems.Add(b.name + ": a flash");
                }
                Vector2?[] ranges = items.Select(RangeOf).ToArray();
                if (!(ranges.Length == 4 && ranges.Take(2).All(r => r == new Vector2(0f, 0.6f)) && ranges.Skip(2).All(r => r == new Vector2(0.9f, 1f))))
                    problems.Add(b.name + ": crack · break ranges");
            }
            FeedbackPlayer impact = Field(lab, "m_Impact");
            var stops = Items(impact).Where(i => i.GetType().Name == "HitStopItem").Select(i => RangeOf(i) ?? Vector2.zero).OrderBy(r => r.x).ToArray();
            object rumble = Items(impact).FirstOrDefault(i => i.GetType().Name == "GamepadRumbleItem");
            Assert.That((stops.Length, stops.Length == 2 && stops[0].y < stops[1].x, rumble != null ? RangeOf(rumble) : null), Is.EqualTo((2, true, (Vector2?)new Vector2(0.95f, 1f))), "Impact's ranges");
            Assert.That((F<float>(impact, "m_Intensity"), F<bool>(impact, "m_RemapIntensity"), F<bool>(impact, "m_UseDistanceGate")), Is.EqualTo((1f, false, false)), "the range's input");
            foreach (string name in new[] { "m_Impact", "m_BossIntro", "m_BossHit", "m_BossRage", "m_BossDefeat", "m_Boom" })
                if ((int)F<PlayerDisableBehavior>(Field(lab, name), "m_DisableBehavior") != (int)PlayerDisableBehavior.StopRevert)
                    problems.Add(name + ": not StopRevert");
            List<object> defeat = Items(Field(lab, "m_BossDefeat"));
            object slow = defeat.FirstOrDefault(i => i.GetType().Name == "TimeScaleItem");
            Assert.That(slow?.GetType().GetField("m_Motion", All).GetValue(slow)?.GetType().Name, Is.EqualTo("FlashMotion"), "the slow motion comes back");
            foreach (object item in defeat.Concat(Items(Field(lab, "m_BossRage"))))
                if (item.GetType().Name != "TimeScaleItem" && Clock(item) != (int)ItemClock.Unscaled)
                    problems.Add(Label(item) + ": not unscaled");
            object down = Items(Field(lab, "m_BossIntro")).First(i => i.GetType().Name == "PositionItem");
            object downMotion = down.GetType().GetField("m_Motion", All).GetValue(down);
            object tween = downMotion.GetType().GetField("m_Params", All).GetValue(downMotion);
            Assert.That(tween.GetType().GetField("m_Mode", All).GetValue(tween).ToString(), Is.EqualTo("From"), "the boss comes down from above its place");
            foreach (Behaviour v in Resources.FindObjectsOfTypeAll(s_Volume).Cast<Behaviour>().Where(x => x.gameObject.scene.isLoaded))
                if (Priority(v) >= 1000f)
                    problems.Add(v.name + ": priority");
            if (Resources.FindObjectsOfTypeAll<Collider>().Count(c => c.gameObject.scene.isLoaded) + Resources.FindObjectsOfTypeAll<Rigidbody>().Count(r => r.gameObject.scene.isLoaded) > 0)
                problems.Add("physics in the scene");
            Assert.That(problems, Is.Empty);
        }

        // ---- the board (pure): walls · the paddle's angle · a hard brick takes two · no tunnel at top speed in 1/30 s · a lost ball ·
        // the boss's guard · the same inputs give the same state

        [Test]
        public void WQ03_TheBoard()
        {
            Scene();
            Type t = Type.GetType(BoardType, true);
            object b = Activator.CreateInstance(t, true);
            float half = (float)Const("HalfWidth");
            Call(b, "Put", new Vector2(half - 0.25f, 3f), new Vector2(6f, 1f));
            Call(b, "Step", 0.05f, 0f);
            bool wall = ((Vector2)P(b, "Velocity")).x < 0f;
            Call(b, "Put", new Vector2(0.55f, 0.6f + 0.15f + 0.25f), new Vector2(0f, -7f));
            Call(b, "Step", 0.02f, 0f);
            Vector2 v = (Vector2)P(b, "Velocity");
            float angle = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;
            Call(b, "Reset");
            Vector2 hard = BrickCenter(0);
            Call(b, "Put", hard - new Vector2(0f, 0.45f), new Vector2(0f, 8f));
            Call(b, "Step", Step60, 0f);
            int afterOne = (int)Call(b, "HitsLeft", 0);
            Call(b, "Put", hard - new Vector2(0f, 0.45f), new Vector2(0f, 8f));
            Call(b, "Step", Step60, 0f);
            int afterTwo = (int)Call(b, "HitsLeft", 0);
            Vector2 low = BrickCenter(20);
            Call(b, "Put", low - new Vector2(0f, 0.25f + 0.18f + 0.3f), new Vector2(0f, 11f));
            Call(b, "Step", 0.1f, 0f);
            int tunnel = (int)Call(b, "HitsLeft", 20);
            Call(b, "Put", new Vector2(4f, 0.2f), new Vector2(0f, -10f));
            Call(b, "Step", 0.1f, -4f);
            bool lost = (bool)P(b, "Held") && Enumerable.Range(0, (int)P(b, "EventCount")).Any(i => GetKind(Call(b, "EventAt", i)) == "Lost");
            Call(b, "ClearBricks");
            Call(b, "StartBoss");
            Vector2 boss = (Vector2)P(b, "BossCenter");
            Call(b, "Put", boss - new Vector2(0f, 0.8f), new Vector2(0f, 8f));
            Call(b, "Step", Step60, 0f);
            int hp1 = (int)P(b, "BossHp");
            Call(b, "Put", (Vector2)P(b, "BossCenter") - new Vector2(0f, 0.8f), new Vector2(0f, 8f));
            Call(b, "Step", Step60, 0f);
            int hp2 = (int)P(b, "BossHp");
            string RunA() => Run(t);
            Assert.That((wall, Mathf.Abs(angle - 30f) < 1f, afterOne, afterTwo, tunnel, lost), Is.EqualTo((true, true, 1, 0, 0, true)), $"paddle angle {angle:0.0}");
            Assert.That((hp1, hp2, RunA() == Run(t)), Is.EqualTo(((int)Const("BossHealth") - 1, (int)Const("BossHealth") - 1, true)), "the guard · the same state");
        }

        static float BarValue(object bar) => (float)bar.GetType().GetProperty("Value").GetValue(bar);

        static string GetKind(object e) => e.GetType().GetField("Kind").GetValue(e).ToString();

        // a fixed course: launch, steps with a moving paddle — the state as text
        static string Run(Type t)
        {
            object b = Activator.CreateInstance(t, true);
            Call(b, "Launch");
            for (int i = 0; i < 600; i++)
                Call(b, "Step", Step60, Mathf.Sin(i * 0.05f) * 3f);
            Vector2 ball = (Vector2)P(b, "Ball");
            return $"{ball.x:R} {ball.y:R} {P(b, "BricksLeft")}";
        }

        // ---- combo and score: the tiers' edges · ×k · the score · the window (game clock) · a lost ball · the count-up's end ·
        // the table's string

        [UnityTest]
        public IEnumerator WQ04_ComboAndScore()
        {
            yield return new EnterPlayMode();
            yield return Open();
            Component lab = Lab();
            var tiers = new List<int>();
            for (int k = 0; k < 7; k++)
            {
                BreakBottom(lab, k);
                tiers.Add((int)P(lab, "Tier"));
            }
            int score = (int)P(lab, "Score");
            int combo = (int)P(lab, "Combo");
            yield return Real(0.6f);
            int shown = (int)P(lab, "ShownScore");
            Text label = F<Text>(lab, "m_ScoreText");
            string[] table = (string[])lab.GetType().GetField("s_Score", All).GetValue(null);
            bool fromTable = ReferenceEquals(label.text, table[score / 10]);
            Call(Board(lab), "Hold");
            Call(lab, "Step", 2.1f, 0f);
            int afterWindow = (int)P(lab, "Combo");
            BreakBottom(lab, 8);
            BreakBottom(lab, 9);
            Call(Board(lab), "Put", new Vector2(0f, 0.2f), new Vector2(0f, -10f));
            Call(lab, "Step", 0.1f, 4f);
            int afterLost = (int)P(lab, "Combo");
            // the expected score from the rule: 10 × (tier + 1) for each break
            int expected = 10 * (1 + 1 + 2 + 2 + 2 + 3 + 3);
            Assert.That((string.Join(",", tiers), combo, score, shown, fromTable), Is.EqualTo(("0,0,1,1,1,2,2", 7, expected, expected, true)));
            Assert.That((afterWindow, afterLost), Is.EqualTo((0, 0)), "the window · a lost ball");
        }

        // ---- the combo's feedback: Impact's intensity is the tier · the hit stop's length by tier (real time) · two breaks in a
        // frame → one Impact · one ScoreFx · Console 0

        [UnityTest]
        public IEnumerator WQ05_TheCombosFeedback()
        {
            yield return new EnterPlayMode();
            yield return Open();
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            (int e0, int w0) = Counts();
            Component lab = Lab();
            FeedbackPlayer impact = Field(lab, "m_Impact"), scoreFx = Field(lab, "m_ScoreFx");
            var stood = new float[3];
            var intensity = new float[3];
            int[] breaksTo = { 1, 3, 6 };
            int k = 0;
            for (int tier = 0; tier < 3; tier++)
            {
                while (k < breaksTo[tier])
                {
                    yield return Real(0.3f);
                    BreakBottom(lab, k++);
                }
                int last = -1;
                float until = Time.realtimeSinceStartup + 0.4f;
                while (Time.realtimeSinceStartup < until)
                {
                    yield return null;
                    if (Time.timeScale == 0f && Time.frameCount != last)
                    {
                        last = Time.frameCount;
                        stood[tier] += Time.unscaledDeltaTime;
                    }
                }
                intensity[tier] = (float)P(lab, "LastImpact");
            }
            yield return Real(0.4f);
            s_Played = 0;
            impact.Played += OnPlayed;
            int impactBefore = s_Played;
            BreakBottom(lab, k++);
            BreakBottom(lab, k++);
            yield return Frames(3);
            int impactPlays = s_Played - impactBefore;
            impact.Played -= OnPlayed;
            s_Played = 0;
            scoreFx.Played += OnPlayed;
            BreakBottom(lab, k++);
            BreakBottom(lab, k++);
            yield return Frames(3);
            int scorePlays = s_Played;
            scoreFx.Played -= OnPlayed;
            (int e1, int w1) = Counts();
            Assert.That((intensity[0], intensity[1], intensity[2]), Is.EqualTo((0.35f, 0.65f, 1f)), "Impact's intensity by tier");
            Assert.That((stood[0] < 0.01f, stood[1] > 0.02f && stood[1] < 0.07f, stood[2] > 0.06f && stood[2] < 0.12f), Is.EqualTo((true, true, true)), $"stood {stood[0]:0.000} · {stood[1]:0.000} · {stood[2]:0.000} s");
            Assert.That((impactPlays, scorePlays, e1 - e0, w1 - w0), Is.EqualTo((1, 1, 0, 0)), "one Impact · one ScoreFx a frame · Console 0");
        }

        // ---- bricks and numbers: a break plays its brick and hides it 0.12 game s on · six quick breaks all react · the numbers stay
        // at six alive (TMP) with no pool warning

        [UnityTest]
        public IEnumerator WQ06_BricksAndNumbers()
        {
            yield return new EnterPlayMode();
            yield return Open();
            Component lab = Lab();
            FeedbackPlayer[] bricks = F<FeedbackPlayer[]>(lab, "m_BrickPlayers");
            Renderer[] renderers = F<Renderer[]>(lab, "m_Bricks");
            var reacted = new bool[6];
            for (int i = 0; i < 6; i++)
            {
                BreakBottom(lab, i);
                reacted[i] = bricks[16 + i].IsPlaying;
            }
            bool shownAfterBreak = renderers[16].enabled;
            Call(lab, "Step", 0.13f, 0f);
            bool hidden = !renderers[16].enabled;
            Transform numbers = lab.transform.Find("BrickBreakerNumbers(Clone)");
            int numberPlays = -1;
            s_Warnings = 0;
            Application.logMessageReceived += CountWarnings;
            if (numbers != null)
            {
                FeedbackPlayer number = numbers.Find("Number").GetComponent<FeedbackPlayer>();
                yield return Real(1f);
                s_Played = 0;
                number.Played += OnPlayed;
                for (int i = 6; i < 16; i++)
                    BreakBottom(lab, i);
                yield return Real(1f);
                numberPlays = s_Played;
                number.Played -= OnPlayed;
            }
            Application.logMessageReceived -= CountWarnings;
            Assert.That((reacted.All(r => r), shownAfterBreak, hidden), Is.EqualTo((true, true, true)), "the bricks");
            if (numbers != null)
                Assert.That((numberPlays, s_Warnings), Is.EqualTo((6, 0)), "six numbers alive · no pool warning");
        }

        // ---- the boss: Action3 → the entrance (the ball held 1.5 game s · down from above · the bar full) · a hit's stop 0.15 s ·
        // the guard · the rage pulse (a window's peak) · the defeat (slow motion back · the pulse out · Clear · +1,000 · the cheer later)

        [UnityTest]
        public IEnumerator WQ07_TheBoss()
        {
            yield return new EnterPlayMode();
            yield return Open();
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Component lab = Lab();
            object board = Board(lab);
            Call(lab, "BossNow");
            string phase0 = P(lab, "Current").ToString();
            Transform body = F<GameObject>(lab, "m_BossBody").transform;
            yield return Frames(2);
            float startY = body.localPosition.y;
            yield return Real(1.2f);
            float landedY = body.localPosition.y;
            object bar = F<object>(lab, "m_BossBar");
            float barFull = BarValue(bar);
            Call(lab, "Step", 1.6f, 0f);
            string phase1 = P(lab, "Current").ToString();
            Vector2 c = (Vector2)P(board, "BossCenter");
            Hit(lab, c, 0.6f);
            int hp1 = (int)P(board, "BossHp");
            float stood = 0f;
            int last = -1;
            float until = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                if (Time.timeScale == 0f && Time.frameCount != last) { last = Time.frameCount; stood += Time.unscaledDeltaTime; }
            }
            Hit(lab, (Vector2)P(board, "BossCenter"), 0.6f);
            int hpGuard = (int)P(board, "BossHp");
            float barAfter = BarValue(bar);
            FeedbackPlayer rage = Field(lab, "m_BossRage");
            for (int i = 0; i < 7; i++)
            {
                Call(lab, "Step", 0.6f, 0f);
                Hit(lab, (Vector2)P(board, "BossCenter"), 0.6f);
                yield return Real(0.25f);
            }
            int hpRage = (int)P(board, "BossHp");
            float peak = 0f;
            until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until) { yield return null; peak = Mathf.Max(peak, OverlayWeight()); }
            bool raging = rage.IsPlaying;
            for (int i = 0; i < 4; i++)
            {
                Call(lab, "Step", 0.6f, 0f);
                Hit(lab, (Vector2)P(board, "BossCenter"), 0.6f);
                yield return Real(0.25f);
            }
            int score0 = (int)P(lab, "Score");
            string phase2 = P(lab, "Current").ToString();
            float minScale = 1f;
            until = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < until) { yield return null; minScale = Mathf.Min(minScale, Time.timeScale); }
            yield return Real(1.6f);
            float scaleAfter = Time.timeScale;
            bool rageOut = !rage.IsPlaying;
            Assert.That((phase0, startY > 3f, Mathf.Abs(landedY) < 0.05f, barFull, phase1), Is.EqualTo(("BossIntro", true, true, 1f, "Serve")), $"the entrance from {startY:0.00} to {landedY:0.00}");
            Assert.That((hp1, stood > 0.12f && stood < 0.2f, hpGuard, Mathf.Abs(barAfter - 11f / 12f) < 0.01f), Is.EqualTo((11, true, 11, true)), $"a hit stood {stood:0.000} s");
            Assert.That((hpRage <= 4, raging, peak > 0.05f), Is.EqualTo((true, true, true)), $"the rage at hp {hpRage} · pulse peak {peak:0.00}");
            Assert.That((phase2, minScale < 0.5f, Mathf.Abs(scaleAfter - 1f) < 0.001f, rageOut, score0 >= 1000), Is.EqualTo(("Clear", true, true, true, true)), $"the defeat: slow {minScale:0.00} → {scaleAfter:0.00}");
        }

        // ---- the same game in Before: the lab driven by the same game steps in After and in Before — the board's state, the score,
        // the bricks shown; in Before no feedback player plays, the boss stands at its place and the banner shows

        [UnityTest]
        public IEnumerator WQ08_TheSameGameInBefore()
        {
            yield return new EnterPlayMode();
            yield return Open();
            Component lab = Lab();
            var states = new string[2];
            var played = new int[2];
            var bossY = new float[2];
            FeedbackPlayer[] all = Resources.FindObjectsOfTypeAll<FeedbackPlayer>().Where(p => p.gameObject.scene.isLoaded).ToArray();
            for (int pass = 0; pass < 2; pass++)
            {
                SampleState.SetAfter(pass == 0);
                Call(lab, "NewRound");
                yield return Frames(2);
                ((Behaviour)lab).enabled = false;
                s_Played = 0;
                foreach (FeedbackPlayer p in all)
                    p.Played += OnPlayed;
                object board = Board(lab);
                Call(board, "Launch");
                for (int i = 0; i < 900; i++)
                    Call(lab, "Step", Step60, Mathf.Sin(i * 0.04f) * 3.5f);
                Renderer[] r = F<Renderer[]>(lab, "m_Bricks");
                states[pass] = $"{((Vector2)P(board, "Ball")).x:R} {((Vector2)P(board, "Ball")).y:R} {P(board, "BricksLeft")} {P(lab, "Score")} {P(lab, "Lives")} {r.Count(x => x.enabled)}";
                Call(lab, "BossNow");
                yield return Frames(3);
                bossY[pass] = F<GameObject>(lab, "m_BossBody").transform.localPosition.y;
                foreach (FeedbackPlayer p in all)
                    p.Played -= OnPlayed;
                played[pass] = s_Played;
                ((Behaviour)lab).enabled = true;
            }
            Assert.That(states[1], Is.EqualTo(states[0]), "the same game");
            Assert.That((played[0] > 0, played[1], Mathf.Abs(bossY[1]) < 0.001f), Is.EqualTo((true, 0, true)), $"plays After {played[0]} · Before {played[1]} · the boss in Before at {bossY[1]:0.000}");
        }

        // ---- the rage across Before and After: raging in After (a pulse window's peak) · Before stops it (the emission is category
        // None) · After plays it again, once (the ledger · a window over its period)

        [UnityTest]
        public IEnumerator WQ08b_TheRageAcrossBeforeAndAfter()
        {
            yield return new EnterPlayMode();
            yield return Open();
            Component lab = Lab();
            object board = Board(lab);
            FeedbackPlayer rage = Field(lab, "m_BossRage");
            Call(lab, "BossNow");
            yield return Frames(2);
            Call(lab, "Step", 1.6f, 0f);
            for (int i = 0; i < 8; i++)
            {
                Call(lab, "Step", 0.6f, 0f);
                Hit(lab, (Vector2)P(board, "BossCenter"), 0.6f);
                yield return Real(0.25f);
            }
            int hp = (int)P(board, "BossHp");
            yield return Real(0.3f);
            bool on1 = rage.IsPlaying;
            float peak1 = 0f, peakOff = 0f, peak2 = 0f;
            float until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until) { yield return null; peak1 = Mathf.Max(peak1, OverlayWeight()); }
            SampleState.SetAfter(false);
            yield return Frames(3);
            bool offPlaying = rage.IsPlaying;
            until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until) { yield return null; peakOff = Mathf.Max(peakOff, OverlayWeight()); }
            s_Played = 0;
            rage.Played += OnPlayed;
            SampleState.SetAfter(true);
            yield return Frames(3);
            bool on2 = rage.IsPlaying;
            until = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < until) { yield return null; peak2 = Mathf.Max(peak2, OverlayWeight()); }
            rage.Played -= OnPlayed;
            Assert.That((hp, on1, peak1 > 0.05f), Is.EqualTo((4, true, true)), $"raging at {hp} · peak {peak1:0.00}");
            Assert.That((offPlaying, peakOff < 0.01f), Is.EqualTo((false, true)), $"Before: peak {peakOff:0.00}");
            Assert.That((on2, s_Played, peak2 > 0.05f), Is.EqualTo((true, 1, true)), $"After again: peak {peak2:0.00}");
        }

        // ---- again and away: mid slow motion and rage, again → the time scale, the boss's place, the banner, the bricks, the score
        // back; in Before a game over's banner, then After and again → hidden; mid slow motion to the hub → the setting's scale

        [UnityTest]
        public IEnumerator WQ09_AgainAndAway()
        {
            yield return new EnterPlayMode();
            yield return Open();
            Component lab = Lab();
            FeedbackPlayer defeat = Field(lab, "m_BossDefeat");
            F<GameObject>(lab, "m_BossBody").SetActive(true);
            yield return Frames(2);
            defeat.Play();
            yield return Real(0.3f);
            float during = Time.timeScale;
            Call(lab, "NewRound");
            yield return Frames(3);
            float after = Time.timeScale;
            CanvasGroup banner = F<FeedbackPlayer>(lab, "m_Banner").GetComponent<CanvasGroup>();
            SampleState.SetAfter(false);
            for (int i = 0; i < 3; i++)
            {
                Call(Board(lab), "Put", new Vector2(0f, 0.2f), new Vector2(0f, -10f));
                Call(lab, "Step", 0.1f, 4f);
            }
            yield return Frames(2);
            float beforeAlpha = banner.alpha;
            SampleState.SetAfter(true);
            Call(lab, "NewRound");
            yield return Frames(2);
            float againAlpha = banner.alpha;
            int bricksOn = F<Renderer[]>(lab, "m_Bricks").Count(r => r.enabled);
            int score = (int)P(lab, "Score");
            SampleState.SetTimeScale(0.5f);
            F<GameObject>(lab, "m_BossBody").SetActive(true);
            yield return Frames(2);
            defeat.Play();
            yield return Real(0.3f);
            SampleHub.BackToHub();
            yield return Frames(30);
            float hub = Time.timeScale;
            Assert.That((during < 0.5f, after), Is.EqualTo((true, 1f)), "again mid slow motion");
            Assert.That((beforeAlpha, againAlpha, bricksOn, score), Is.EqualTo((1f, 0f, 32, 0)), "the banner shown in Before · hidden again · the bricks · the score");
            Assert.That(hub, Is.EqualTo(1f), "the hub's time scale (the setting's)");
        }

        // ---- input (virtual devices): arrows · Z · X · the stick · the D-pad move the paddle · Space launches · a click on the HUD
        // does not · 2 again · 3 the boss

        [UnityTest]
        public IEnumerator WQ10_Input()
        {
            yield return new EnterPlayMode();
            yield return Open();
            var was = (InputSystem.settings.backgroundBehavior, InputSystem.settings.editorInputBehaviorInPlayMode);
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Component lab = Lab();
            object board = Board(lab);
            // the paddle moves on the game clock — a fixed 1/60 frame (the batch's frames are about 1 ms)
            Time.captureDeltaTime = 1f / 60f;
            var kb = InputSystem.AddDevice<Keyboard>();
            var pad = InputSystem.AddDevice<Gamepad>();
            yield return Frames(2);
            float x0 = (float)P(board, "PaddleX");
            yield return PressKey(kb, new KeyboardState(Key.RightArrow), 10);
            float right = (float)P(board, "PaddleX") - x0;
            x0 = (float)P(board, "PaddleX");
            yield return PressKey(kb, new KeyboardState(Key.Z), 10);
            float z = (float)P(board, "PaddleX") - x0;
            x0 = (float)P(board, "PaddleX");
            yield return PressPad(pad, new GamepadState(GamepadButton.DpadRight), 10);
            float dpad = (float)P(board, "PaddleX") - x0;
            x0 = (float)P(board, "PaddleX");
            yield return PressPad(pad, new GamepadState { leftStick = new Vector2(-1f, 0f) }, 10);
            float stick = (float)P(board, "PaddleX") - x0;
            yield return PressKey(kb, new KeyboardState(Key.Space), 1);
            bool launched = !(bool)P(board, "Held");
            yield return PressKey(kb, new KeyboardState(Key.Digit2), 1);
            bool again = (bool)P(board, "Held") && P(lab, "Current").ToString() == "Serve";
            // a click on the HUD does not launch · the pointer's x is a target the paddle follows at its speed · a click on the field
            // launches
            var mouse = InputSystem.AddDevice<Mouse>();
            yield return Frames(2);
            Vector2 over = ScreenOf(SampleHud.Instance.GetComponentsInChildren<Selectable>(true).First(s => s.gameObject.activeInHierarchy).GetComponent<RectTransform>());
            InputSystem.QueueStateEvent(mouse, new MouseState { position = over });
            yield return Frames(3);
            bool overUi = SampleInput.PointerOverUI;
            yield return PressMouse(mouse, over, 2);
            bool clickOnHud = !(bool)P(board, "Held");
            Vector2 target = Camera.main.WorldToScreenPoint(new Vector3(3f, 1f, 0f));
            float px0 = (float)P(board, "PaddleX");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = target });
            yield return Frames(2);
            float firstSteps = Mathf.Abs((float)P(board, "PaddleX") - px0);
            yield return Frames(40);
            float reached = (float)P(board, "PaddleX");
            yield return PressMouse(mouse, target, 2);
            bool clickOnField = !(bool)P(board, "Held");
            InputSystem.RemoveDevice(mouse);
            yield return PressKey(kb, new KeyboardState(Key.Digit3), 1);
            string boss = P(lab, "Current").ToString();
            InputSystem.RemoveDevice(kb);
            InputSystem.RemoveDevice(pad);
            InputSystem.settings.backgroundBehavior = was.Item1;
            InputSystem.settings.editorInputBehaviorInPlayMode = was.Item2;
            Assert.That((right > 0.5f, z < -0.5f, dpad > 0.5f, stick < -0.5f, launched, again, boss), Is.EqualTo((true, true, true, true, true, true, "BossIntro")),
                $"paddle → {right:0.00} · Z {z:0.00} · D-pad {dpad:0.00} · stick {stick:0.00}");
            Assert.That((overUi, clickOnHud, firstSteps < 0.45f, Mathf.Abs(reached - 3f) < 0.05f, clickOnField), Is.EqualTo((true, false, true, true, true)),
                $"the pointer: two frames {firstSteps:0.00} m · reached {reached:0.00}");
        }

        static Vector2 ScreenOf(RectTransform r) => RectTransformUtility.WorldToScreenPoint(null, r.TransformPoint(r.rect.center));

        static IEnumerator PressMouse(Mouse mouse, Vector2 at, int frames)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = at }.WithButton(MouseButton.Left));
            yield return Frames(frames);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = at });
            yield return Frames(2);
        }

        static IEnumerator PressKey(Keyboard kb, KeyboardState state, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                InputSystem.QueueStateEvent(kb, state);
                yield return null;
            }
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Frames(2);
        }

        static IEnumerator PressPad(Gamepad pad, GamepadState state, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                InputSystem.QueueStateEvent(pad, state);
                yield return null;
            }
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Frames(2);
        }

        // ---- without TMP Essential Resources (check-samples --no-tmp): the notice · no numbers · the game stands · Console 0

        [UnityTest]
        public IEnumerator WQ11_WithoutTmp()
        {
            yield return new EnterPlayMode();
            if (SampleTmp.Ready)
            {
                yield return new ExitPlayMode();
                Assert.Ignore("TMP Essential Resources are in this project");
            }
            (int e0, int w0) = Counts();
            yield return Open();
            Component lab = Lab();
            bool notice = lab.transform.Find("TMP Notice") != null;
            bool numbers = lab.transform.Find("BrickBreakerNumbers(Clone)") != null;
            BreakBottom(lab, 0);
            BreakBottom(lab, 1);
            yield return Frames(30);
            int score = (int)P(lab, "Score");
            (int e1, int w1) = Counts();
            Assert.That((notice, numbers, score, e1 - e0, w1 - w0), Is.EqualTo((true, false, 20, 0, 0)));
        }

        static long Median(long[] values)
        {
            var sorted = (long[])values.Clone();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }

        // the f-th hit of a round of 48 (bottom sixteen, then each top brick twice) — a new round after the 48th; through delegates made
        // once (a reflected call allocates its arguments in the frame it measures)
        sealed class Driver
        {
            internal Action<Vector2, Vector2> Put;
            internal Action<float, float> Step;
            internal Action Hold, NewRound;
            internal readonly Vector2[] Centers = new Vector2[32];
        }

        static Driver Drive(Component lab)
        {
            object board = Board(lab);
            var d = new Driver
            {
                Put = (Action<Vector2, Vector2>)Delegate.CreateDelegate(typeof(Action<Vector2, Vector2>), board, board.GetType().GetMethod("Put", All)),
                Step = (Action<float, float>)Delegate.CreateDelegate(typeof(Action<float, float>), lab, lab.GetType().GetMethod("Step", All)),
                Hold = (Action)Delegate.CreateDelegate(typeof(Action), board, board.GetType().GetMethod("Hold", All)),
                NewRound = (Action)Delegate.CreateDelegate(typeof(Action), lab, lab.GetType().GetMethod("NewRound", All)),
            };
            for (int i = 0; i < 32; i++)
                d.Centers[i] = BrickCenter(i);
            return d;
        }

        static void HitNext(Driver d, int f)
        {
            int n = f % 48;
            if (n == 0 && f > 0)
                d.NewRound();
            int brick = n < 16 ? 16 + n : (n - 16) / 2;
            d.Put(d.Centers[brick] - new Vector2(0f, 0.45f), new Vector2(0f, 8f));
            d.Step(Step60, 0f);
            d.Hold();
        }

        // ---- allocation: a break every frame for 120 frames (the count-up running through) — the frames that allocated are no
        // more than a quiet window's; a positive control (a string a frame) must show (a dead counter is red)

        [UnityTest]
        public IEnumerator WQ12_Allocation()
        {
            yield return new EnterPlayMode();
            yield return Open();
            Component lab = Lab();
            // the HUD's editor-only 'Select Player' label builds a string whenever the last player that played changes — 32 bricks in
            // turn change it every frame (about 106 B, editor only) — off while the sample is measured
            FieldInfo selectLabel = typeof(SampleHud).GetField("m_SelectLabel", All);
            object keepLabel = selectLabel.GetValue(SampleHud.Instance);
            selectLabel.SetValue(SampleHud.Instance, null);
            ProfilerRecorder gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            yield return Real(1f);
            int quiet = 0, busy = 0, control = 0;
            var qb = new long[120];
            var bb = new long[120];
            var cb = new long[120];
            // the quiet window with the lab off — a steady allocation in its Update shows against it (it would be in a quiet window of its own)
            ((Behaviour)lab).enabled = false;
            for (int f = 0; f < 120; f++) { yield return null; qb[f] = gc.LastValue; if (gc.LastValue > 0) quiet++; }
            ((Behaviour)lab).enabled = true;
            // a warm round first (the numbers' pool, the first plays), then a hit every frame: the bottom sixteen once, the top sixteen
            // twice (crack · break), a new round when they are gone
            Driver drive = Drive(lab);
            for (int f = 0; f < 60; f++) { HitNext(drive, f); yield return null; }
            drive.NewRound();
            yield return Real(0.5f);
            for (int f = 0; f < 120; f++)
            {
                HitNext(drive, f);
                yield return null;
                bb[f] = gc.LastValue;
                if (gc.LastValue > 0) busy++;
            }
            string s = "";
            for (int f = 0; f < 120; f++) { s = f.ToString() + s.Length; yield return null; cb[f] = gc.LastValue; if (gc.LastValue > 0) control++; }
            gc.Dispose();
            selectLabel.SetValue(SampleHud.Instance, keepLabel);
            long q50 = Median(qb), b50 = Median(bb), c50 = Median(cb);
            Assert.That(c50 - q50, Is.GreaterThan(40), $"the positive control (a dead counter) — medians quiet {q50} · control {c50}");
#if UNITY_6000_3_OR_NEWER
            Assert.That(b50 - q50, Is.LessThan(40), $"the game's frames against the quiet ones — medians quiet {q50} · busy {b50} · control {c50}");
#else
            Assert.Ignore("6000.0's editor loop allocates in FlushDirty (EditorCompilationInterface.IsCompiling) in each frame the scene changed — the game's frames are measured on 6000.3 and 6000.6");
#endif
        }
    }
}
