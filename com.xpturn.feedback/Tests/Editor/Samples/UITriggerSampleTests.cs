using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
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
    // S15 UI Trigger: the bundle's second card · the trigger rows, the two UnityEvent calls and the category overrides as the
    // scene holds them · no play call in the sample's code · the hover and the focus turning back · the press and the confirm ·
    // the toggle · the slider's intensity · hold and release · the locked button · Cancel · Before · a pad · no allocation.
    // The handlers are called through ExecuteEvents (the Button on the object takes them too); a play-mode iterator captures
    // no local in a lambda (the reload)
    public sealed class UITriggerSampleTests
    {
        static readonly Dictionary<FeedbackPlayer, int> s_Plays = new Dictionary<FeedbackPlayer, int>();
        static readonly Color Off = new Color(0.25f, 0.28f, 0.35f, 1f);
        static readonly Color On = new Color(0.35f, 0.75f, 0.45f, 1f);
        static readonly string[] s_Cancels = { "Vibration Switch", "Intensity Slider", "Hold and Release" };
        static GameObject s_Hover;
        static Toggle s_Toggle;
        static Slider s_Slider;
        static PointerEventData s_Pointer;
        static float s_Next = 0.3f;

        static string Scene()
        {
            string path = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == "UITrigger");
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

        static void OnPlayed(FeedbackPlayer p, PlaybackHandle h) => s_Plays[p] = Plays(p) + 1;

        static int Plays(FeedbackPlayer p) => p != null && s_Plays.TryGetValue(p, out int n) ? n : 0;

        static void Count(params FeedbackPlayer[] players)
        {
            s_Plays.Clear();
            foreach (FeedbackPlayer p in players)
            {
                p.Played -= OnPlayed;
                p.Played += OnPlayed;
            }
        }

        static GameObject Go(string name) => GameObject.Find(name);

        static FeedbackPlayer Player(GameObject control, string holder) => control.transform.Find(holder).GetComponent<FeedbackPlayer>();

        static PointerEventData Pointer(PointerEventData.InputButton button = PointerEventData.InputButton.Left) => new PointerEventData(EventSystem.current) { button = button };

        static BaseEventData Data() => new BaseEventData(EventSystem.current);

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

        static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f && Mathf.Abs(a.a - b.a) < 0.01f;

        // ---- the bundle and the scene as saved: the rows of each trigger, the two UnityEvent calls (dynamic), the category
        // overrides (the state in no category, the color touches in UI), no play call in the sample's code

        [Test]
        public void TQ01_TheBundleAndTheScene()
        {
            SampleCatalog catalog = Catalogs().FirstOrDefault(c => c.Bundle == "UI");
            if (catalog == null)
                Assert.Ignore("no UI bundle in this project");
            Assert.That(catalog.Cards.Length, Is.EqualTo(2), "the UI bundle's cards");
            SampleCard card = catalog.Cards[1];
            string scene = AssetDatabase.GUIDToAssetPath(card.SceneGuid);
            Assert.That((card.Title, Path.GetFileName(scene), card.RequiredType), Is.EqualTo(("UI Trigger", "UITrigger.unity", "xpTURN.Feedback.Samples.UI.UITriggerLab, xpTURN.Feedback.Samples.UI.UITrigger")));
            string folder = Disk(Path.GetDirectoryName(scene).Replace('\\', '/'));
            PackageInfo package = PackageInfo.FindForAssetPath("Packages/com.xpturn.feedback");
            Assert.That(File.ReadAllText(Path.Combine(package.resolvedPath, "package.json")).Contains("S15 UI Trigger"), Is.True, "the samples entry names S15");
            var asmdef = JsonUtility.FromJson<Asmdef>(File.ReadAllText(Directory.GetFiles(folder, "*.asmdef").Single()));
            Assert.That(asmdef.name, Is.EqualTo("xpTURN.Feedback.Samples.UI.UITrigger"));
            Assert.That(asmdef.defineConstraints, Is.EquivalentTo(new[] { "XPTURN_FEEDBACK_SAMPLES_INPUTSYSTEM", "XPTURN_FEEDBACK_SAMPLES_UGUI", "XPTURN_FEEDBACK_SAMPLES_URP" }));
            string lab = File.ReadAllText(Path.Combine(folder, "UITriggerLab.cs"));
            Assert.That(new[] { ".Play(", "PlayFromEvent", ".Signal(", ".Stop(", ".Skip(", ".Seek(" }.Where(lab.Contains), Is.Empty, "the sample's code plays nothing");

            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            var triggers = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(m => m != null && m.GetType().Name == "FeedbackUITrigger" && m.gameObject.scene.isLoaded).OrderBy(m => m.name).ToList();
            var rows = new List<string>();
            foreach (MonoBehaviour t in triggers)
            {
                var so = new SerializedObject(t);
                SerializedProperty entries = so.FindProperty("m_Entries");
                var line = new List<string>();
                for (int i = 0; i < entries.arraySize; i++)
                {
                    SerializedProperty e = entries.GetArrayElementAtIndex(i);
                    SerializedProperty kind = e.FindPropertyRelative("m_Event"), action = e.FindPropertyRelative("m_Action");
                    string row = kind.enumNames[kind.enumValueIndex] + ">" + action.enumNames[action.enumValueIndex] + ">" + e.FindPropertyRelative("m_Player").objectReferenceValue?.name;
                    if (kind.enumNames[kind.enumValueIndex] == "Select")
                    {
                        SerializedProperty source = e.FindPropertyRelative("m_Source");
                        row += ">" + source.enumNames[source.enumValueIndex];
                    }
                    if (action.enumNames[action.enumValueIndex] == "Stop")
                    {
                        SerializedProperty mode = e.FindPropertyRelative("m_StopMode");
                        row += ">" + mode.enumNames[mode.enumValueIndex];
                    }
                    line.Add(row);
                }
                rows.Add(t.name + (so.FindProperty("m_RequireInteractable").boolValue ? "" : " (any)") + ": " + string.Join(" | ", line));
            }
            const string Menu = ": PointerEnter>PlayForward>Hover | PointerExit>PlayBackward>Hover | Select>PlayForward>Focus>Any | Deselect>PlayBackward>Focus | PointerDown>PlayForward>Press | PointerUp>PlayBackward>Press | PointerClick>Play>Body | Submit>Play>Body";
            Assert.That(rows, Is.EqualTo(new[]
            {
                "Continue" + Menu,
                "Credits" + Menu,
                "Hold and Release: PointerDown>Play>Hold Player | PointerUp>Signal>Hold Player | PointerExit>Stop>Hold Player>Revert | Submit>Play>Hold Player | Submit>Signal>Hold Player | Cancel>Play>Nudge",
                "Intensity Slider: Cancel>Play>Nudge",
                "Locked (any): PointerClick>Play>Locked",
                "Settings" + Menu,
                "Vibration Switch: Cancel>Play>Nudge",
            }));

            Toggle toggle = Resources.FindObjectsOfTypeAll<Toggle>().Single(x => x.gameObject.scene.isLoaded);
            Slider slider = Resources.FindObjectsOfTypeAll<Slider>().Single(x => x.gameObject.scene.isLoaded);
            string Call(UnityEventBase ev, UnityEngine.Object owner, string field)
            {
                var so = new SerializedObject(owner);
                int mode = so.FindProperty(field + ".m_PersistentCalls.m_Calls.Array.data[0].m_Mode").intValue;
                return ev.GetPersistentEventCount() + " " + (ev.GetPersistentTarget(0) as Component)?.name + "." + ev.GetPersistentMethodName(0) + " mode " + mode + " " + ev.GetPersistentListenerState(0);
            }
            Assert.That((Call(toggle.onValueChanged, toggle, "onValueChanged"), Call(slider.onValueChanged, slider, "m_OnValueChanged")),
                Is.EqualTo(("1 Knob Player.PlayFromEvent mode 0 RuntimeOnly", "1 Tick Player.PlayFromEvent mode 0 RuntimeOnly")), "the two dynamic calls");

            var players = Resources.FindObjectsOfTypeAll<FeedbackPlayer>().Where(p => p.gameObject.scene.isLoaded).ToList();
            var overrides = new List<string>();
            foreach (FeedbackPlayer p in players)
            {
                SerializedProperty items = new SerializedObject(p).FindProperty("m_Items");
                for (int i = 0; i < items.arraySize; i++)
                {
                    SerializedProperty over = items.GetArrayElementAtIndex(i).FindPropertyRelative("m_Category.m_Override");
                    if (over == null || !over.boolValue)
                        continue;
                    SerializedProperty c = items.GetArrayElementAtIndex(i).FindPropertyRelative("m_Category.m_Category");
                    overrides.Add($"{p.transform.parent.name}/{p.name}#{i}:{c.enumNames[c.enumValueIndex]}");
                }
            }
            overrides.Sort(StringComparer.Ordinal);
            Assert.That(overrides, Is.EqualTo(new[]
            {
                "Continue/Focus#0:None", "Continue/Focus#1:None", "Continue/Hover#1:UI",
                "Credits/Focus#0:None", "Credits/Focus#1:None", "Credits/Hover#1:UI",
                "Hold and Release/Hold Player#4:UI",
                "Settings/Focus#0:None", "Settings/Focus#1:None", "Settings/Hover#1:UI",
                "Vibration Switch/Knob Player#0:None", "Vibration Switch/Knob Player#1:None",
            }), "the state in no category · the color touches in UI");

            var menu = Resources.FindObjectsOfTypeAll<Button>().Where(b => b.gameObject.scene.isLoaded && new[] { "Continue", "Settings", "Credits" }.Contains(b.name)).ToList();
            Assert.That(menu.Select(b => b.transition + " " + b.navigation.mode).Distinct(), Is.EqualTo(new[] { "None Automatic" }), "the menu buttons");
            Assert.That(Resources.FindObjectsOfTypeAll<Button>().Single(b => b.gameObject.scene.isLoaded && b.name == "Locked").interactable, Is.False, "the locked button");
            Assert.That((triggers.Count, players.Count, menu.Count), Is.EqualTo((7, 17, 3)), "triggers · players · menu buttons");
        }

        [Serializable]
        sealed class Asmdef
        {
            public string name = "";
            public string[] defineConstraints = new string[0];
        }

        // ---- the hover: Pointer Exit turns the running play back where it is — one play, no jump, back to its size

        [UnityTest]
        public IEnumerator TQ02_TheHoverTurnsBack()
        {
            yield return new EnterPlayMode();
            yield return Open();
            (int e0, int w0) = Counts();
            GameObject b = Go("Credits");
            FeedbackPlayer hover = Player(b, "Hover");
            Count(hover);
            ExecuteEvents.Execute(b, Pointer(), ExecuteEvents.pointerEnterHandler);
            yield return Real(0.12f);
            float before = b.transform.localScale.x;
            ExecuteEvents.Execute(b, Pointer(), ExecuteEvents.pointerExitHandler);
            float after = b.transform.localScale.x;
            yield return Real(0.5f);
            float end = b.transform.localScale.x;
            (int plays, bool playing) = (Plays(hover), hover.IsPlaying);
            (int e1, int w1) = Counts();
            yield return new ExitPlayMode();
            Assert.That(before, Is.GreaterThan(1.01f), "the hover grew");
            Assert.That(Mathf.Abs(after - before), Is.LessThan(1e-5f), "the exit turns where it is");
            Assert.That((plays, playing, Mathf.Abs(end - 1f) < 1e-4f), Is.EqualTo((1, false, true)), "one play · back to its size");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        // ---- the focus: a selection by navigation or by a pointer shows the frame (Any), the deselection turns it back

        [UnityTest]
        public IEnumerator TQ03_TheFocus()
        {
            yield return new EnterPlayMode();
            yield return Open();
            EventSystem es = EventSystem.current;
            GameObject settings = Go("Settings"), credits = Go("Credits");
            Image sFrame = settings.transform.Find("Frame").GetComponent<Image>(), cFrame = credits.transform.Find("Frame").GetComponent<Image>();
            es.SetSelectedGameObject(settings, new AxisEventData(es));
            yield return Real(0.4f);
            float sOn = sFrame.color.a;
            es.SetSelectedGameObject(credits, new PointerEventData(es));
            yield return Real(0.4f);
            (float sOff, float cOn) = (sFrame.color.a, cFrame.color.a);
            yield return new ExitPlayMode();
            Assert.That((sOn > 0.99f, sOff < 0.01f, cOn > 0.99f), Is.EqualTo((true, true, true)), $"by navigation {sOn} · deselected {sOff} · by a pointer {cOn}");
        }

        // ---- the press (left button only) and the confirm (a click, a submit)

        [UnityTest]
        public IEnumerator TQ04_ThePressAndTheConfirm()
        {
            yield return new EnterPlayMode();
            yield return Open();
            GameObject b = Go("Continue");
            Transform body = b.transform.Find("Body");
            FeedbackPlayer press = Player(b, "Press"), confirm = body.GetComponent<FeedbackPlayer>();
            Count(press, confirm);
            ExecuteEvents.Execute(b, Pointer(PointerEventData.InputButton.Right), ExecuteEvents.pointerDownHandler);
            yield return Real(0.2f);
            float right = body.localScale.x;
            ExecuteEvents.Execute(b, Pointer(PointerEventData.InputButton.Right), ExecuteEvents.pointerUpHandler);
            int rightPlays = Plays(press);
            ExecuteEvents.Execute(b, Pointer(), ExecuteEvents.pointerDownHandler);
            yield return Real(0.2f);
            float down = body.localScale.x;
            ExecuteEvents.Execute(b, Pointer(), ExecuteEvents.pointerUpHandler);
            yield return Real(0.3f);
            float up = body.localScale.x;
            ExecuteEvents.Execute(b, Pointer(), ExecuteEvents.pointerClickHandler);
            int clicked = Plays(confirm);
            yield return Real(0.4f);
            ExecuteEvents.Execute(b, Data(), ExecuteEvents.submitHandler);
            int submitted = Plays(confirm);
            yield return Real(0.4f);
            yield return new ExitPlayMode();
            Assert.That((Mathf.Abs(right - 1f) < 1e-4f, rightPlays), Is.EqualTo((true, 0)), "the right button presses nothing");
            Assert.That((down < 0.95f, Mathf.Abs(up - 1f) < 1e-3f), Is.EqualTo((true, true)), $"pressed {down} · released {up}");
            Assert.That((clicked, submitted), Is.EqualTo((1, 2)), "a click and a submit confirm");
        }

        // ---- the toggle: on plays forward, off turns the running play back; a switch saved on still moves on its first off

        [UnityTest]
        public IEnumerator TQ05_TheToggle()
        {
            yield return new EnterPlayMode();
            yield return Open();
            GameObject sw = Go("Vibration Switch");
            Toggle toggle = sw.GetComponent<Toggle>();
            var knob = (RectTransform)sw.transform.Find("Knob");
            Image bg = sw.GetComponent<Image>();
            FeedbackPlayer kp = Player(sw, "Knob Player");
            Count(kp);
            float x0 = knob.anchoredPosition.x;
            // a switch saved on — the toggle on (no call) and the knob at its on place before the knob's first play
            toggle.SetIsOnWithoutNotify(true);
            knob.anchoredPosition = new Vector2(22f, 0f);
            bg.color = On;
            toggle.isOn = false;
            yield return Real(0.45f);
            (float fromOn, int plays0) = (knob.anchoredPosition.x, Plays(kp));
            toggle.isOn = true;
            yield return Real(0.1f);
            float mid = knob.anchoredPosition.x;
            toggle.isOn = false;
            float turned = knob.anchoredPosition.x;
            yield return Real(0.45f);
            (float off, bool offColor, int plays1) = (knob.anchoredPosition.x, Near(bg.color, Off), Plays(kp));
            toggle.isOn = true;
            yield return Real(0.45f);
            (float on, bool onColor) = (knob.anchoredPosition.x, Near(bg.color, On));
            yield return new ExitPlayMode();
            Assert.That(x0, Is.EqualTo(-22f), "off at the start");
            Assert.That((Mathf.Abs(fromOn + 22f) < 0.01f, plays0), Is.EqualTo((true, 1)), $"a switch saved on moves on its first off ({fromOn})");
            Assert.That(mid, Is.GreaterThan(-17f), "on moves");
            Assert.That(Mathf.Abs(turned - mid), Is.LessThan(1e-3f), "off turns where it is (the turn's place is a microsecond on)");
            Assert.That((Mathf.Abs(off + 22f) < 0.01f, offColor, plays1), Is.EqualTo((true, true, 2)), "back off · the turn is no new play");
            Assert.That((Mathf.Abs(on - 22f) < 0.01f, onColor), Is.EqualTo((true, true)), "on");
        }

        // ---- the slider: the value is the tick's intensity; a drag's changes are thinned by the cooldown

        [UnityTest]
        public IEnumerator TQ06_TheSlider()
        {
            yield return new EnterPlayMode();
            yield return Open();
            GameObject go = Go("Intensity Slider");
            Slider slider = go.GetComponent<Slider>();
            Transform handle = go.transform.Find("Handle Slide Area/Handle/Grip");
            FeedbackPlayer tick = Player(go, "Tick Player");
            Count(tick);
            float quarter = 0f, full = 0f;
            slider.value = 0.25f;
            float until = Time.realtimeSinceStartup + 0.35f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                quarter = Mathf.Max(quarter, Mathf.Abs(handle.localScale.x - 1f));
            }
            slider.value = 1f;
            until = Time.realtimeSinceStartup + 0.35f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                full = Mathf.Max(full, Mathf.Abs(handle.localScale.x - 1f));
            }
            int plays0 = Plays(tick);
            int changes = 0;
            until = Time.realtimeSinceStartup + 0.3f;
            while (Time.realtimeSinceStartup < until)
            {
                slider.value = changes % 2 == 0 ? 0.4f : 0.6f;
                changes++;
                yield return null;
            }
            int drag = Plays(tick) - plays0;
            yield return new ExitPlayMode();
            Assert.That(plays0, Is.EqualTo(2), "two values, two ticks");
            Assert.That(full / Mathf.Max(quarter, 1e-6f), Is.InRange(3f, 5f), $"the intensity is the value ({quarter} · {full})");
            Assert.That((drag >= 2, drag <= 8, changes > drag), Is.EqualTo((true, true, true)), $"{changes} changes in 0.3 s → {drag} ticks (cooldown 0.05 s)");
        }

        // ---- hold and release: the press half waits for Pointer Up; a quick click does not stick; pulled off it reverts and the
        // Up after sends nothing that lasts; Submit runs Play then Signal — one whole press

        [UnityTest]
        public IEnumerator TQ07_HoldAndRelease()
        {
            yield return new EnterPlayMode();
            yield return Open();
            GameObject h = Go("Hold and Release");
            Transform body = h.transform.Find("Body");
            FeedbackPlayer hp = Player(h, "Hold Player");
            Count(hp);
            ExecuteEvents.Execute(h, Pointer(), ExecuteEvents.pointerDownHandler);
            yield return Real(0.4f);
            (float held, bool waiting) = (body.localScale.x, hp.IsPlaying);
            ExecuteEvents.Execute(h, Pointer(), ExecuteEvents.pointerUpHandler);
            yield return Real(0.6f);
            (float released, bool done1) = (body.localScale.x, !hp.IsPlaying);
            ExecuteEvents.Execute(h, Pointer(), ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(h, Pointer(), ExecuteEvents.pointerUpHandler);
            yield return Real(0.6f);
            (float quick, bool done2) = (body.localScale.x, !hp.IsPlaying);
            ExecuteEvents.Execute(h, Pointer(), ExecuteEvents.pointerDownHandler);
            yield return Real(0.05f);
            ExecuteEvents.Execute(h, Pointer(), ExecuteEvents.pointerExitHandler);
            float reverted = body.localScale.x;
            ExecuteEvents.Execute(h, Pointer(), ExecuteEvents.pointerUpHandler);
            yield return Real(0.3f);
            bool idle = !hp.IsPlaying;
            ExecuteEvents.Execute(h, Pointer(), ExecuteEvents.pointerDownHandler);
            yield return Real(0.3f);
            (float again, bool waitsAgain) = (body.localScale.x, hp.IsPlaying);
            ExecuteEvents.Execute(h, Pointer(), ExecuteEvents.pointerUpHandler);
            yield return Real(0.6f);
            ExecuteEvents.Execute(h, Data(), ExecuteEvents.submitHandler);
            yield return Real(0.6f);
            (float submitted, bool done3, int plays) = (body.localScale.x, !hp.IsPlaying, Plays(hp));
            yield return new ExitPlayMode();
            Assert.That((Mathf.Abs(held - 0.9f) < 0.01f, waiting), Is.EqualTo((true, true)), $"held {held} · waiting");
            Assert.That((Mathf.Abs(released - 1f) < 1e-3f, done1), Is.EqualTo((true, true)), $"released {released}");
            Assert.That((Mathf.Abs(quick - 1f) < 1e-3f, done2), Is.EqualTo((true, true)), $"a quick click {quick}");
            Assert.That((Mathf.Abs(reverted - 1f) < 1e-4f, idle), Is.EqualTo((true, true)), $"pulled off {reverted}");
            Assert.That((Mathf.Abs(again - 0.9f) < 0.01f, waitsAgain), Is.EqualTo((true, true)), $"the next press waits {again}");
            Assert.That((Mathf.Abs(submitted - 1f) < 1e-3f, done3, plays), Is.EqualTo((true, true, 5)), $"a submit {submitted}");
        }

        // ---- the locked button: not interactable, its trigger (Require Interactable off) answers a click

        [UnityTest]
        public IEnumerator TQ08_TheLockedButtonAnswers()
        {
            yield return new EnterPlayMode();
            yield return Open();
            GameObject l = Go("Locked");
            FeedbackPlayer denied = l.GetComponent<FeedbackPlayer>();
            Count(denied);
            bool interactable = l.GetComponent<Button>().interactable;
            ExecuteEvents.Execute(l, Pointer(), ExecuteEvents.pointerClickHandler);
            int plays = Plays(denied);
            yield return Real(0.5f);
            yield return new ExitPlayMode();
            Assert.That((interactable, plays), Is.EqualTo((false, 1)));
        }

        // ---- Cancel on the toggle, the slider and Hold nudges the panel, which comes back

        [UnityTest]
        public IEnumerator TQ09_Cancel()
        {
            yield return new EnterPlayMode();
            yield return Open();
            GameObject panel = Go("Settings Panel");
            FeedbackPlayer nudge = Player(panel, "Nudge");
            Count(nudge);
            float x0 = ((RectTransform)panel.transform).anchoredPosition.x;
            foreach (string name in s_Cancels)
            {
                ExecuteEvents.Execute(Go(name), Data(), ExecuteEvents.cancelHandler);
                yield return Real(0.05f);
            }
            int plays = Plays(nudge);
            yield return Real(0.5f);
            float x1 = ((RectTransform)panel.transform).anchoredPosition.x;
            yield return new ExitPlayMode();
            Assert.That((plays, Mathf.Abs(x1 - x0) < 0.01f), Is.EqualTo((3, true)));
        }

        // ---- Before: the juice stays still (the hover, the text color, the knob's punch, the tick, the press half) while the
        // state shows (the selection frame, the knob and its color); After brings the juice back

        [UnityTest]
        public IEnumerator TQ10_BeforeKeepsTheState()
        {
            yield return new EnterPlayMode();
            yield return Open();
            EventSystem es = EventSystem.current;
            SampleState.SetAfter(false);
            GameObject credits = Go("Credits"), sw = Go("Vibration Switch"), h = Go("Hold and Release");
            Text label = credits.transform.Find("Body/Label").GetComponent<Text>();
            Image frame = credits.transform.Find("Frame").GetComponent<Image>();
            var knob = (RectTransform)sw.transform.Find("Knob");
            Transform handle = Go("Intensity Slider").transform.Find("Handle Slide Area/Handle/Grip"), body = h.transform.Find("Body");
            Color label0 = label.color;
            ExecuteEvents.Execute(credits, Pointer(), ExecuteEvents.pointerEnterHandler);
            sw.GetComponent<Toggle>().isOn = true;
            Go("Intensity Slider").GetComponent<Slider>().value = 0.9f;
            // the press selects Hold — the navigation's selection of Credits comes after it
            ExecuteEvents.Execute(h, Pointer(), ExecuteEvents.pointerDownHandler);
            es.SetSelectedGameObject(credits, new AxisEventData(es));
            float root = 0f, knobScale = 0f, tick = 0f, press = 0f;
            float until = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                root = Mathf.Max(root, Mathf.Abs(credits.transform.localScale.x - 1f));
                knobScale = Mathf.Max(knobScale, Mathf.Abs(knob.localScale.x - 1f));
                tick = Mathf.Max(tick, Mathf.Abs(handle.localScale.x - 1f));
                press = Mathf.Max(press, Mathf.Abs(body.localScale.x - 1f));
            }
            (bool labelStill, float knobX, float frameA, bool bgOn) = (Near(label.color, label0), knob.anchoredPosition.x, frame.color.a, Near(sw.GetComponent<Image>().color, On));
            ExecuteEvents.Execute(h, Pointer(), ExecuteEvents.pointerUpHandler);
            SampleState.SetAfter(true);
            GameObject settings = Go("Settings");
            ExecuteEvents.Execute(settings, Pointer(), ExecuteEvents.pointerEnterHandler);
            yield return Real(0.15f);
            float afterRoot = settings.transform.localScale.x;
            yield return new ExitPlayMode();
            Assert.That((root, knobScale, tick, press), Is.EqualTo((0f, 0f, 0f, 0f)), "the juice stays still in Before");
            Assert.That(labelStill, Is.True, "the text color (UI) stays in Before");
            Assert.That((Mathf.Abs(knobX - 22f) < 0.01f, frameA > 0.99f, bgOn), Is.EqualTo((true, true, true)), $"the state shows — knob {knobX} · frame {frameA}");
            Assert.That(afterRoot, Is.GreaterThan(1.01f), "After brings the hover back");
        }

        // ---- a pad: the first button selected, the D-pad moves, East cancels, South runs Hold's whole press, a lost selection
        // comes back

        [UnityTest]
        public IEnumerator TQ11_APad()
        {
            yield return new EnterPlayMode();
            yield return Open();
            var was = Focus();
            var pad = InputSystem.AddDevice<Gamepad>();
            yield return Frames(10);
            (int e0, int w0) = Counts();
            EventSystem es = EventSystem.current;
            GameObject h = Go("Hold and Release");
            FeedbackPlayer nudge = Player(Go("Settings Panel"), "Nudge"), hp = Player(h, "Hold Player");
            Count(nudge, hp);
            bool first = es.currentSelectedGameObject == Go("Continue");
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadDown));
            yield return Frames(3);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Frames(3);
            bool moved = es.currentSelectedGameObject == Go("Settings");
            es.SetSelectedGameObject(Go("Vibration Switch"));
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.East));
            yield return Frames(3);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Frames(3);
            int cancelled = Plays(nudge);
            es.SetSelectedGameObject(h);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
            yield return Frames(3);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Real(0.6f);
            (int held, bool done) = (Plays(hp), !hp.IsPlaying);
            es.SetSelectedGameObject(null);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadLeft));
            yield return Frames(3);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Frames(3);
            bool restored = es.currentSelectedGameObject == h;
            (int e1, int w1) = Counts();
            InputSystem.RemoveDevice(pad);
            Unfocus(was);
            yield return new ExitPlayMode();
            Assert.That((first, moved), Is.EqualTo((true, true)), "the first button selected · the D-pad moves");
            Assert.That((cancelled, held, done), Is.EqualTo((1, 1, true)), "East cancels · South runs Hold's whole press");
            Assert.That(restored, Is.True, "a lost selection comes back with the D-pad");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        // ---- no allocation once warm: a hover in and out (the turn), the toggle's turn, a slider value (the positive control
        // allocates)

        static void HoverInOut()
        {
            ExecuteEvents.Execute(s_Hover, s_Pointer, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(s_Hover, s_Pointer, ExecuteEvents.pointerExitHandler);
        }

        static void ToggleTurns() => s_Toggle.isOn = !s_Toggle.isOn;

        static void SliderStep()
        {
            s_Next = s_Next > 0.5f ? 0.3f : 0.7f;
            s_Slider.value = s_Next;
        }

        static void Control() => GC.KeepAlive(new object[4]);

        // true only when five runs in a row allocate
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
        public IEnumerator TQ13_NoAllocation()
        {
            yield return new EnterPlayMode();
            yield return Open();
            s_Hover = Go("Credits");
            s_Toggle = Go("Vibration Switch").GetComponent<Toggle>();
            s_Slider = Go("Intensity Slider").GetComponent<Slider>();
            s_Pointer = Pointer();
            for (int i = 0; i < 10; i++)
            {
                HoverInOut();
                ToggleTurns();
                SliderStep();
                yield return null;
            }
            (bool hover, bool toggle, bool slider, bool control) = (Allocates(HoverInOut), Allocates(ToggleTurns), Allocates(SliderStep), Allocates(Control));
            s_Hover = null;
            s_Toggle = null;
            s_Slider = null;
            s_Pointer = null;
            yield return new ExitPlayMode();
            Assert.That((hover, toggle, slider, control), Is.EqualTo((false, false, false, true)));
        }
    }
}
