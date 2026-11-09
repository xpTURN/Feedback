using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using xpTURN.Feedback.Samples;
using static xpTURN.Feedback.Tests.Editor.Samples.SampleKitTestSupport;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace xpTURN.Feedback.Tests.Editor.Samples
{
    // the UI bundle (catalog · samples entry · asmdef · players on the unscaled clock · the bars' order) · the pause · the HP
    // bar · the reward popup · the shop · Before · a pad · without TMP. The sample's types and ProgressBar are reached by name; a
    // play-mode iterator captures no local in a lambda (the reload); a Unity object is read as a bool or a number inside play
    public sealed class UIJuiceTests
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Dictionary<FeedbackPlayer, int> s_Plays = new Dictionary<FeedbackPlayer, int>();

        static string Scene()
        {
            string path = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "UIJuice");
            if (path == null)
                Assert.Ignore("no UI bundle in this project");
            return path;
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
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

        static Component Lab() => Resources.FindObjectsOfTypeAll<MonoBehaviour>().First(m => m != null && m.GetType().Name == "UIJuiceLab" && m.gameObject.scene.isLoaded);

        static T F<T>(object o, string field) => (T)o.GetType().GetField(field, All).GetValue(o);

        static object Call(Component lab, string method, params object[] args) => lab.GetType().GetMethod(method).Invoke(lab, args);

        static int I(Component lab, string property) => (int)lab.GetType().GetProperty(property).GetValue(lab);

        static bool B(Component lab, string property) => (bool)lab.GetType().GetProperty(property).GetValue(lab);

        static float Shown(Component bar, int i) => (float)bar.GetType().GetMethod("ShownOf", All).Invoke(bar, new object[] { i });

        static RectTransform BarRect(Component bar, string field) => F<RectTransform>(bar, field);

        static void OnPlayed(FeedbackPlayer p, PlaybackHandle h) => s_Plays[p] = Plays(p) + 1;

        static int Plays(FeedbackPlayer p) => p != null && s_Plays.TryGetValue(p, out int n) ? n : 0;

        static void Count(params FeedbackPlayer[] players)
        {
            s_Plays.Clear();
            foreach (FeedbackPlayer p in players)
                if (p != null)
                {
                    p.Played -= OnPlayed;
                    p.Played += OnPlayed;
                }
        }

        static FeedbackPlayer Hover(Component lab, int item) => F<FeedbackPlayer>(Hovers(lab)[item], "m_Player");

        static Component[] Hovers(Component lab) => F<Button[]>(lab, "m_Items").Select(b => b.GetComponents<MonoBehaviour>().First(m => m.GetType().Name == "UIJuiceHover")).ToArray<Component>();

        // the topmost bar child over the bar's point at frac (sibling order = draw order)
        static string Top(Component bar, float frac)
        {
            var root = (RectTransform)bar.transform;
            var c = new Vector3[4];
            root.GetWorldCorners(c);
            float x = Mathf.Lerp(c[0].x, c[3].x, frac);
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var r = (RectTransform)root.GetChild(i);
                r.GetWorldCorners(c);
                if (x >= c[0].x && x <= c[2].x && r.localScale.x > 0f)
                    return r.name;
            }
            return "bg";
        }

        static (InputSettings.BackgroundBehavior, InputSettings.EditorInputBehaviorInPlayMode) Focus()
        {
            var was = (InputSystem.settings.backgroundBehavior, InputSystem.settings.editorInputBehaviorInPlayMode);
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            return was;
        }

        static void Unfocus((InputSettings.BackgroundBehavior, InputSettings.EditorInputBehaviorInPlayMode) was)
        {
            InputSystem.settings.backgroundBehavior = was.Item1;
            InputSystem.settings.editorInputBehaviorInPlayMode = was.Item2;
        }

        static Vector2 ScreenOf(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            return RectTransformUtility.WorldToScreenPoint(null, (corners[0] + corners[2]) * 0.5f);
        }

        // ---- the bundle: catalog · samples entry · asmdef · every player on the unscaled clock (the Scaled ornament aside) ·
        // the bars' order and pivots · the items' transition · the card's one-letter keys bound in the Sample map

        [Test]
        public void JQ01_TheBundle()
        {
            string scene = Scene();
            SampleCatalog catalog = Catalogs().FirstOrDefault(c => c.Bundle == "UI");
            Assert.That(catalog, Is.Not.Null, "the UI catalog");
            Assert.That((catalog.Order, catalog.Cards.Length, AssetDatabase.GUIDToAssetPath(catalog.Cards[0].SceneGuid)), Is.EqualTo((30, 2, scene)));
            PackageInfo package = PackageInfo.FindForAssetPath("Packages/com.xpturn.feedback");
            string json = File.ReadAllText(Path.Combine(package.resolvedPath, "package.json"));
            Assert.That(json.Contains("\"displayName\": \"UI\"") && json.Contains("\"path\": \"Samples~/UI\""), Is.True, "the samples entry");
            var asmdef = JsonUtility.FromJson<Asmdef>(File.ReadAllText(Directory.GetFiles(Disk(Path.GetDirectoryName(scene).Replace('\\', '/')), "*.asmdef", SearchOption.AllDirectories).Single()));
            Assert.That((asmdef.name, asmdef.references.Contains("xpTURN.Feedback.UGUI")), Is.EqualTo(("xpTURN.Feedback.Samples.UI.UIJuice", true)), "the asmdef's name · its UGUI reference");
            Assert.That(asmdef.defineConstraints, Is.EquivalentTo(new[] { "XPTURN_FEEDBACK_SAMPLES_INPUTSYSTEM", "XPTURN_FEEDBACK_SAMPLES_UGUI", "XPTURN_FEEDBACK_SAMPLES_URP" }), "the asmdef's constraints");

            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            var problems = new List<string>();
            var players = Resources.FindObjectsOfTypeAll<FeedbackPlayer>().Where(p => p.gameObject.scene.isLoaded).ToList();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path.GetDirectoryName(scene).Replace('\\', '/') + "/UIJuicePopupTmp.prefab");
            Assert.That(prefab, Is.Not.Null, "the popup's TMP prefab");
            players.AddRange(prefab.GetComponentsInChildren<FeedbackPlayer>(true));
            FieldInfo mode = typeof(FeedbackPlayer).GetField("m_TimeMode", All);
            foreach (FeedbackPlayer p in players)
            {
                bool scaled = (int)mode.GetValue(p) == 0;
                if (scaled != (p.name == "Ornament (Scaled)"))
                    problems.Add($"{p.name}: {(scaled ? "Scaled" : "Unscaled")}");
            }
            foreach (Component bar in Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(m => m.GetType().Name == "ProgressBar" && m.gameObject.scene.isLoaded))
            {
                RectTransform dec = BarRect(bar, "m_DelayedDecrease"), inc = BarRect(bar, "m_DelayedIncrease"), fg = BarRect(bar, "m_Foreground");
                if (!(dec.GetSiblingIndex() < inc.GetSiblingIndex() && inc.GetSiblingIndex() < fg.GetSiblingIndex()))
                    problems.Add($"{bar.name}: order dec {dec.GetSiblingIndex()} inc {inc.GetSiblingIndex()} fg {fg.GetSiblingIndex()}");
                if (new[] { dec, inc, fg }.Any(r => r.pivot.x != 0f))
                    problems.Add($"{bar.name}: a fill's pivot is not on its left");
                if (bar.GetComponent<Graphic>() == null)
                    problems.Add($"{bar.name}: no graphic on the root (Denied (UI)'s color)");
            }
            foreach (Button b in Resources.FindObjectsOfTypeAll<Button>().Where(b => b.gameObject.scene.isLoaded && b.name.StartsWith("Item ", StringComparison.Ordinal)))
                if (b.transition != Selectable.Transition.None || b.navigation.mode != Navigation.Mode.Automatic)
                    problems.Add($"{b.name}: transition {b.transition} navigation {b.navigation.mode}");
            CanvasGroup popup = Resources.FindObjectsOfTypeAll<CanvasGroup>().First(g => g.gameObject.scene.isLoaded && g.name == "Reward Popup");
            if (popup.alpha != 0f || popup.interactable || popup.blocksRaycasts)
                problems.Add("the popup is not hidden (alpha · interactable · blocksRaycasts)");
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            var bound = new HashSet<string>(asset.FindActionMap("Sample", true).bindings.Select(x => x.path));
            foreach (string token in (catalog.Cards[0].Controls ?? "").Split(new[] { '·', '—', ',', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (token.Trim().Length == 1 && char.IsLetterOrDigit(token.Trim()[0]) && !bound.Contains("<Keyboard>/" + char.ToLowerInvariant(token.Trim()[0])))
                    problems.Add("card key " + token);
            Assert.That(players.Count, Is.GreaterThan(10), "players");
            Assert.That(problems, Is.Empty);
        }

        [Serializable]
        sealed class Asmdef
        {
            public string name = "";
            public string[] references = new string[0];
            public string[] defineConstraints = new string[0];
        }

        // ---- the pause: the Unscaled ornament and the UI move, the Scaled ornament and the cube stop; a reload puts it back

        [UnityTest]
        public IEnumerator JQ02_PauseKeepsTheUiMoving()
        {
            yield return new EnterPlayMode();
            yield return Open();
            (int e0, int w0) = Counts();
            float base0 = FeedbackTimeScale.BaseScale;
            Component lab = Lab();
            Transform un = GameObject.Find("Ornament (Unscaled)").transform, sc = GameObject.Find("Ornament (Scaled)").transform, cube = F<Transform>(lab, "m_Cube");
            Call(lab, "TogglePause");
            yield return Frames(2);
            bool paused = B(lab, "Paused") && SampleState.SampleTimeScale == 0f && SampleState.TimeScale == 1f;
            float scale = Time.timeScale, baseScale = FeedbackTimeScale.BaseScale;
            float u0 = un.localEulerAngles.z, s0 = sc.localEulerAngles.z;
            Quaternion c0 = cube.rotation;
            FeedbackPlayer hover = Hover(lab, 0);
            Component bar = F<Component>(lab, "m_Hp");
            Count(hover);
            Hovers(lab)[0].GetType().GetMethod("OnPointerEnter").Invoke(Hovers(lab)[0], new object[] { new PointerEventData(EventSystem.current) });
            Call(lab, "Hit", 15);
            Transform item = F<Button[]>(lab, "m_Items")[0].transform;
            float uMove = 0f, sMove = 0f, itemDev = 0f;
            float until = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                uMove = Mathf.Max(uMove, Mathf.Abs(Mathf.DeltaAngle(u0, un.localEulerAngles.z)));
                sMove = Mathf.Max(sMove, Mathf.Abs(Mathf.DeltaAngle(s0, sc.localEulerAngles.z)));
                itemDev = Mathf.Max(itemDev, Mathf.Abs(item.localScale.x - 1f));
            }
            float cubeTurn = Quaternion.Angle(c0, cube.rotation);
            float fg = Shown(bar, 0);
            int hovers = Plays(hover);
            Call(lab, "TogglePause");
            yield return Frames(2);
            bool resumed = !B(lab, "Paused") && Mathf.Approximately(FeedbackTimeScale.BaseScale, base0);
            Call(lab, "TogglePause");
            yield return Open();
            bool reloaded = !B(Lab(), "Paused") && Mathf.Approximately(FeedbackTimeScale.BaseScale, base0);
            (int e1, int w1) = Counts();
            yield return new ExitPlayMode();
            Assert.That((paused, scale, baseScale), Is.EqualTo((true, 0f, 0f)), "the pause is the sample's time scale 0 (not the HUD's)");
            Assert.That(uMove, Is.GreaterThan(5f), "the Unscaled ornament swings while paused");
            Assert.That((sMove < 0.01f, cubeTurn < 0.01f), Is.EqualTo((true, true)), $"the Scaled ornament and the cube stop ({sMove} · {cubeTurn})");
            Assert.That((hovers, itemDev > 0.01f, fg < 0.86f), Is.EqualTo((1, true, true)), "a hover plays and the HP bar moves while paused");
            Assert.That((resumed, reloaded), Is.EqualTo((true, true)), "the pause off · a reload starts unpaused");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        // ---- the HP bar: a loss stays red for the delay, a gain shows green before the foreground follows; full and empty refuse

        [UnityTest]
        public IEnumerator JQ03_TheHpBar()
        {
            yield return new EnterPlayMode();
            yield return Open();
            (int e0, int w0) = Counts();
            Component lab = Lab();
            Component bar = F<Component>(lab, "m_Hp");
            FeedbackPlayer denied = F<FeedbackPlayer>(lab, "m_HpDenied"), hurt = F<FeedbackPlayer>(lab, "m_Hurt");
            Count(denied, hurt);
            Call(lab, "Heal", 20);
            int fullDenied = Plays(denied);
            float t0 = Time.realtimeSinceStartup;
            Call(lab, "Hit", 15);
            string lossTop = "";
            float decHeld = 1f;
            while (Time.realtimeSinceStartup - t0 < 0.4f)
            {
                yield return null;
                decHeld = Mathf.Min(decHeld, Shown(bar, 1));
                if (Time.realtimeSinceStartup - t0 > 0.3f && lossTop.Length == 0)
                    lossTop = Top(bar, 0.92f);
            }
            yield return Real(0.8f);
            float decAfter = Shown(bar, 1);
            t0 = Time.realtimeSinceStartup;
            Call(lab, "Heal", 10);
            string gainTop = "";
            float fgHeld = 1f;
            while (Time.realtimeSinceStartup - t0 < 0.4f)
            {
                yield return null;
                fgHeld = Mathf.Min(fgHeld, Shown(bar, 0));
                if (Time.realtimeSinceStartup - t0 > 0.3f && gainTop.Length == 0)
                    gainTop = Top(bar, 0.9f);
            }
            yield return Real(0.8f);
            float fgAfter = Shown(bar, 0);
            // one hit a frame — a Restart player played again in the same frame grows its slot's ledger (a Console warning)
            for (int i = 0; i < 7; i++)
            {
                Call(lab, "Hit", 15);
                yield return null;
            }
            yield return Real(1.2f);
            int d0 = Plays(denied);
            Call(lab, "Hit", 15);
            int emptyDenied = Plays(denied) - d0;
            bool shook = Plays(hurt) >= 1;
            (int e1, int w1) = Counts();
            yield return new ExitPlayMode();
            Assert.That((fullDenied, shook), Is.EqualTo((1, true)), "a heal at full refuses · a hit shakes");
            Assert.That((decHeld > 0.99f, lossTop, decAfter < 0.86f), Is.EqualTo((true, "Delayed Decrease", true)), "a loss: the red stays for the delay, then follows");
            Assert.That((fgHeld < 0.86f, gainTop, fgAfter > 0.94f), Is.EqualTo((true, "Delayed Increase", true)), "a gain: green first, then the foreground");
            Assert.That(emptyDenied, Is.EqualTo(1), "a hit at 0 refuses");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        // ---- the reward popup: XP full opens it (alpha 1 · interactable · the count reaches 250 with TMP), XP refuses meanwhile,
        // it closes by itself (alpha 0 · no raycast · XP 0 · level 2)

        static string TmpText(GameObject root, string child)
        {
            if (root == null)
                return null;
            Transform t = root.transform.Find(child);
            Component c = t != null ? t.GetComponents<Component>().FirstOrDefault(x => x.GetType().Name == "TextMeshProUGUI") : null;
            return c != null ? (string)c.GetType().GetProperty("text").GetValue(c) : null;
        }

        [UnityTest]
        public IEnumerator JQ04_TheRewardPopup()
        {
            yield return new EnterPlayMode();
            yield return Open();
            (int e0, int w0) = Counts();
            Component lab = Lab();
            CanvasGroup popup = F<CanvasGroup>(lab, "m_Popup");
            FeedbackPlayer xpDenied = F<FeedbackPlayer>(lab, "m_XpDenied");
            Count(xpDenied);
            for (int i = 0; i < 4; i++)
                Call(lab, "AddXp", 25);
            yield return Real(0.4f);
            float alpha = popup.alpha;
            bool open = popup.interactable && popup.blocksRaycasts && B(lab, "PopupOpen");
            Call(lab, "AddXp", 25);
            int refused = Plays(xpDenied);
            yield return Real(0.9f);
            string count = TmpText(F<GameObject>(lab, "m_PopupTmp"), "Gold");
            yield return Real(1.8f);
            bool closed = popup.alpha == 0f && !popup.interactable && !popup.blocksRaycasts && !B(lab, "PopupOpen");
            (int xp, int level, int gold) = (I(lab, "Xp"), I(lab, "Level"), I(lab, "Gold"));
            // opened and closed at once: the show's tween stops where it is, the hide's reaches 0 (both write the alpha)
            for (int i = 0; i < 4; i++)
                Call(lab, "AddXp", 25);
            yield return Real(0.05f);
            Call(lab, "CloseReward");
            yield return Real(0.6f);
            float quick = popup.alpha;
            // closed and opened again at once: the hide's tween stops where it is, the show's reaches 1
            for (int i = 0; i < 4; i++)
                Call(lab, "AddXp", 25);
            yield return Real(0.3f);
            Call(lab, "CloseReward");
            yield return null;
            for (int i = 0; i < 4; i++)
                Call(lab, "AddXp", 25);
            yield return Real(0.6f);
            float reopened = popup.alpha;
            (int e1, int w1) = Counts();
            yield return new ExitPlayMode();
            Assert.That((alpha, open), Is.EqualTo((1f, true)), "XP full opens the popup");
            Assert.That(refused, Is.EqualTo(1), "XP refuses while it is open");
            if (SampleTmp.Ready)
                Assert.That(count, Is.EqualTo("Gold +250"), "the count-up");
            Assert.That((closed, xp, level, gold), Is.EqualTo((true, 0, 2, 370)), "it closes by itself · XP 0 · level 2 · the reward");
            Assert.That((quick, reopened), Is.EqualTo((0f, 1f)), "opened and closed at once ends hidden · closed and opened at once ends shown");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        // ---- the shop: a hover from the pointer and from navigation (a click's and code's selection are quiet), buy · denied ·
        // a fast second buy not dropped, the hidden popup lets the pointer through

        [UnityTest]
        public IEnumerator JQ05_TheShop()
        {
            yield return new EnterPlayMode();
            yield return Open();
            var was = Focus();
            var mouse = InputSystem.AddDevice<Mouse>();
            yield return Frames(10);
            (int e0, int w0) = Counts();
            Component lab = Lab();
            Component[] hovers = Hovers(lab);
            FeedbackPlayer h1 = Hover(lab, 1), h2 = Hover(lab, 2);
            FeedbackPlayer[] press = F<FeedbackPlayer[]>(lab, "m_Press"), denied = F<FeedbackPlayer[]>(lab, "m_Denied");
            FeedbackPlayer goldPop = F<FeedbackPlayer>(lab, "m_GoldPop");
            Count(h1, h2, press[0], press[1], denied[2], goldPop);
            MethodInfo select = hovers[2].GetType().GetMethod("OnSelect");
            select.Invoke(hovers[2], new object[] { new PointerEventData(EventSystem.current) });
            select.Invoke(hovers[2], new object[] { new BaseEventData(EventSystem.current) });
            int quiet = Plays(h2);
            select.Invoke(hovers[2], new object[] { new AxisEventData(EventSystem.current) });
            int byAxis = Plays(h2);
            // the pointer over item 1 (inside the hidden popup's rect) — a hover, then a click: a buy, no second hover
            var item1 = (RectTransform)F<Button[]>(lab, "m_Items")[1].transform;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = ScreenOf(item1) });
            yield return Frames(5);
            int hovered = Plays(h1);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = ScreenOf(item1) }.WithButton(MouseButton.Left));
            yield return Frames(3);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = ScreenOf(item1) });
            yield return Frames(3);
            (int afterClick, int gold1, int bought) = (Plays(h1), I(lab, "Gold"), Plays(press[1]));
            Call(lab, "Buy", 2);
            int refused = Plays(denied[2]);
            lab.GetType().GetField("m_GoldValue", All).SetValue(lab, 200);
            Call(lab, "Buy", 0);
            yield return null;
            Call(lab, "Buy", 0);
            yield return Frames(2);
            (int gold2, int p0, int pops) = (I(lab, "Gold"), Plays(press[0]), Plays(goldPop));
            (int e1, int w1) = Counts();
            InputSystem.RemoveDevice(mouse);
            Unfocus(was);
            yield return new ExitPlayMode();
            Assert.That((quiet, byAxis), Is.EqualTo((0, 1)), "a selection by pointer or code is quiet · by navigation hovers");
            Assert.That((hovered, afterClick, gold1, bought), Is.EqualTo((1, 1, 30, 1)), "the pointer hovers through the hidden popup · a click buys without a second hover");
            Assert.That(refused, Is.EqualTo(1), "a price above the gold is denied");
            Assert.That((gold2, p0, pops), Is.EqualTo((120, 2, 3)), "a fast second buy is not dropped");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        // ---- Before: no plays — the bars jump, the popup shows its end at once and hides at once; After plays again; the
        // paused banner's pulse starts in After after a pause in Before

        [UnityTest]
        public IEnumerator JQ06_BeforePlaysNothing()
        {
            yield return new EnterPlayMode();
            yield return Open();
            (int e0, int w0) = Counts();
            Component lab = Lab();
            Component bar = F<Component>(lab, "m_Hp");
            FeedbackPlayer hurt = F<FeedbackPlayer>(lab, "m_Hurt"), press = F<FeedbackPlayer[]>(lab, "m_Press")[0], h0 = Hover(lab, 0), pulse = F<FeedbackPlayer>(lab, "m_PausedPulse");
            CanvasGroup popup = F<CanvasGroup>(lab, "m_Popup");
            Count(hurt, press, h0, pulse);
            SampleState.SetAfter(false);
            Call(lab, "Hit", 15);
            float dec = Shown(bar, 1), fg = Shown(bar, 0);
            Call(lab, "Buy", 0);
            Hovers(lab)[0].GetType().GetMethod("OnPointerEnter").Invoke(Hovers(lab)[0], new object[] { new PointerEventData(EventSystem.current) });
            for (int i = 0; i < 4; i++)
                Call(lab, "AddXp", 25);
            float shown = popup.alpha;
            string count = TmpText(F<GameObject>(lab, "m_PopupTmp"), "Gold");
            Call(lab, "CloseReward");
            float hidden = popup.alpha;
            Call(lab, "TogglePause");
            yield return Frames(3);
            int beforePlays = Plays(hurt) + Plays(press) + Plays(h0) + Plays(pulse);
            SampleState.SetAfter(true);
            yield return Frames(3);
            int pulses = Plays(pulse);
            Call(lab, "TogglePause");
            Call(lab, "Hit", 15);
            int afterHurt = Plays(hurt);
            (int e1, int w1) = Counts();
            SampleState.SetAfter(true);
            yield return new ExitPlayMode();
            Assert.That((dec, fg), Is.EqualTo((0.85f, 0.85f)).Within(0.001f), "the bar jumps (no delay)");
            Assert.That(beforePlays, Is.EqualTo(0), "no hurt · press · hover in Before");
            Assert.That((shown, hidden), Is.EqualTo((1f, 0f)), "the popup shows its end and hides at once");
            if (SampleTmp.Ready)
                Assert.That(count, Is.EqualTo("Gold +250"), "the count's end at once");
            Assert.That((pulses >= 1, afterHurt), Is.EqualTo((true, 1)), "After: the banner pulses · a hit shakes");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        // ---- a pad alone: the first item selected, the D-pad moves and hovers, South buys, North pauses; a lost selection comes
        // back with the D-pad; 3 pauses too

        [UnityTest]
        public IEnumerator JQ07_APad()
        {
            yield return new EnterPlayMode();
            yield return Open();
            var was = Focus();
            var pad = InputSystem.AddDevice<Gamepad>();
            var kb = InputSystem.AddDevice<Keyboard>();
            yield return Frames(10);
            (int e0, int w0) = Counts();
            Component lab = Lab();
            Button[] items = F<Button[]>(lab, "m_Items");
            FeedbackPlayer h1 = Hover(lab, 1);
            FeedbackPlayer press1 = F<FeedbackPlayer[]>(lab, "m_Press")[1];
            Count(h1, press1);
            bool first = EventSystem.current.currentSelectedGameObject == items[0].gameObject;
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadRight));
            yield return Frames(3);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Frames(3);
            bool moved = EventSystem.current.currentSelectedGameObject == items[1].gameObject;
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
            yield return Frames(3);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Frames(3);
            (int hovered, int bought, int gold) = (Plays(h1), Plays(press1), I(lab, "Gold"));
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.North));
            yield return Frames(3);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Frames(3);
            bool padPause = B(lab, "Paused");
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Digit3));
            yield return Frames(3);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            yield return Frames(3);
            bool keyResume = !B(lab, "Paused");
            EventSystem.current.SetSelectedGameObject(null);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadLeft));
            yield return Frames(3);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Frames(3);
            GameObject back = EventSystem.current.currentSelectedGameObject;
            bool restored = back != null && back.transform.IsChildOf(F<Transform>(lab, "m_Screen"));
            (int e1, int w1) = Counts();
            InputSystem.RemoveDevice(pad);
            InputSystem.RemoveDevice(kb);
            Unfocus(was);
            yield return new ExitPlayMode();
            Assert.That((first, moved), Is.EqualTo((true, true)), "the first item selected · the D-pad moves");
            Assert.That((hovered, bought, gold), Is.EqualTo((1, 1, 30)), "the move hovers · South buys");
            Assert.That((padPause, keyResume), Is.EqualTo((true, true)), "North pauses · 3 resumes");
            Assert.That(restored, Is.True, "a lost selection comes back with the D-pad");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        // ---- without TMP's resources (forced): no prefab, the notice line stays, the popup still opens with its legacy title

        [UnityTest]
        public IEnumerator JQ08_TheRewardWithoutTmp()
        {
            yield return new EnterPlayMode();
            Func<bool> probe = SampleTmp.s_Probe;
            SampleTmp.s_Probe = () => false;
            SampleTmp.Reset();
            yield return Open();
            (int e0, int w0) = Counts();
            Component lab = Lab();
            bool prefab = F<GameObject>(lab, "m_PopupTmp") != null;
            bool notice = F<GameObject>(lab, "m_TmpNotice").activeInHierarchy;
            bool title = F<GameObject>(lab, "m_PopupTitle").activeInHierarchy;
            for (int i = 0; i < 4; i++)
                Call(lab, "AddXp", 25);
            yield return Real(0.5f);
            float alpha = F<CanvasGroup>(lab, "m_Popup").alpha;
            (int e1, int w1) = Counts();
            SampleTmp.s_Probe = probe;
            SampleTmp.Reset();
            yield return new ExitPlayMode();
            Assert.That((prefab, notice, title, alpha), Is.EqualTo((false, true, true, 1f)), "no TMP part · the notice stays · the legacy title · the popup opens");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }
    }
}
