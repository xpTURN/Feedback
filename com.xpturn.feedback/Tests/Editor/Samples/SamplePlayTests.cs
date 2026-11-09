using System.Collections;
using System.Collections.Generic;
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

namespace xpTURN.Feedback.Tests.Editor.Samples
{
    // the rows that enter play from an EditMode test (a runtime test assembly may not reference the editor). Nothing is
    // captured by a lambda: the domain reload of EnterPlayMode leaves captured locals empty; what must cross a reload is a
    // serialized field of the fixture
    public sealed class SamplePlayTests
    {
        [SerializeField] bool[] m_SettingsOn;
        [SerializeField] float[] m_SettingsScale;
        [SerializeField] float m_SettingsBase;
        static readonly List<string> s_Problems = new List<string>();
        static (int errors, int warnings) s_Counts;
        static int s_Rows;

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
                yield return null;
        }

        static IEnumerator PlayFrames(int n)
        {
            int end = Time.frameCount + n;
            while (Time.frameCount < end)
                yield return null;
        }

        // the play's entry clears the Console list: the baseline is zero (a list not cleared — Clear on Play off — starts here)
        static void Begin()
        {
            s_Problems.Clear();
            s_Counts = (0, 0);
            s_Rows = 0;
        }

        static void ConsoleZero()
        {
            var c = Counts();
            if (c.errors != s_Counts.errors || c.warnings != s_Counts.warnings)
                s_Problems.Add($"console {s_Counts} → {c}: " + string.Join(" / ", RowsFrom(s_Rows).Take(6)));
        }

        static void LoadHub() => EditorSceneManager.LoadSceneInPlayMode(SampleKitPaths.HubScene, new LoadSceneParameters(LoadSceneMode.Single));

        static int EventSystems() => Resources.FindObjectsOfTypeAll<EventSystem>().Count(e => e.gameObject.scene.isLoaded && e.isActiveAndEnabled);

        static bool HudShows(SampleCard card)
            => SampleHud.Instance != null && SampleHud.Instance.GetComponentsInChildren<Text>(true).Any(t => t.text == card.Title + "\n" + card.Controls + "\n" + SampleHud.Keys);

        // the hub round trip with the state composed and put back, the Console list unchanged
        [UnityTest]
        public IEnumerator KQ3_HubRoundTrip()
        {
            yield return new EnterPlayMode();
            Begin();
            LoadHub();
            yield return Frames(30);
            Assert.That(SampleHub.Instance, Is.Not.Null, "the hub");
            var enabled0 = new bool[9];
            var scale0 = new float[9];
            for (int i = 1; i < 9; i++)
            {
                enabled0[i] = FeedbackCategories.IsEnabled((FeedbackCategory)i);
                scale0[i] = FeedbackCategories.GetScale((FeedbackCategory)i);
            }
            float base0 = FeedbackTimeScale.BaseScale;
            int flash0 = FeedbackCategories.FlashLimit;
            var cards = SampleHub.Instance.Cards.ToList();
            Assert.That(cards, Is.Not.Empty, "cards — a project that runs these tests holds the fixture bundle");
            int opened = 0;
            foreach (SampleCard card in cards)
            {
                if (!SampleCards.Available(card))
                {
                    Debug.Log($"KQ3 skip {card.Title} requires={card.RequiresText}");
                    continue;
                }
                SampleHub.Open(card);
                yield return Frames(60);
                if (SceneManager.GetActiveScene().name != card.SceneName)
                    s_Problems.Add($"{card.Title}: active {SceneManager.GetActiveScene().name}");
                if (SampleHud.Made != 1 || Resources.FindObjectsOfTypeAll<SampleHud>().Count(h => h.gameObject.scene.isLoaded) != 1)
                    s_Problems.Add($"{card.Title}: HUDs made {SampleHud.Made}");
                if (!HudShows(card))
                    s_Problems.Add($"{card.Title}: the HUD does not show the card's title and controls");
                if (EventSystems() != 1)
                    s_Problems.Add($"{card.Title}: event systems {EventSystems()}");
                SampleState.SetAfter(false);
                for (int i = 1; i < 9; i++)
                    if (FeedbackCategories.IsEnabled((FeedbackCategory)i))
                        s_Problems.Add($"{card.Title}: Before left {(FeedbackCategory)i} on");
                SampleState.SetAfter(true);
                SampleState.SetIntensity(0.5f);
                SampleState.SetCategory(FeedbackCategory.Camera, true, 0.5f);
                if (!Mathf.Approximately(FeedbackCategories.GetScale(FeedbackCategory.Camera), scale0[2] * 0.25f) || !Mathf.Approximately(FeedbackCategories.GetScale(FeedbackCategory.Motion), scale0[1] * 0.5f))
                    s_Problems.Add($"{card.Title}: scales {FeedbackCategories.GetScale(FeedbackCategory.Camera)} {FeedbackCategories.GetScale(FeedbackCategory.Motion)}");
                SampleState.SetTimeScale(0.5f);
                SampleState.SetFlashLimit(1);
                if (!Mathf.Approximately(FeedbackTimeScale.BaseScale, base0 * 0.5f))
                    s_Problems.Add($"{card.Title}: base scale {FeedbackTimeScale.BaseScale}");
                SampleHub.BackToHub();
                yield return Frames(40);
                bool restored = Mathf.Approximately(FeedbackTimeScale.BaseScale, base0) && FeedbackCategories.FlashLimit == flash0;
                for (int i = 1; i < 9; i++)
                    restored &= FeedbackCategories.IsEnabled((FeedbackCategory)i) == enabled0[i] && Mathf.Approximately(FeedbackCategories.GetScale((FeedbackCategory)i), scale0[i]);
                if (!restored)
                    s_Problems.Add($"{card.Title}: not put back");
                if (SceneManager.GetActiveScene().name != SampleKitPaths.HubName)
                    s_Problems.Add($"{card.Title}: back at {SceneManager.GetActiveScene().name}");
                opened++;
                Debug.Log($"KQ3 card {card.Title}");
            }
            ConsoleZero();
            Debug.Log($"KQ3 done cards={cards.Count} opened={opened} problems={s_Problems.Count}");
            Assert.That(opened, Is.GreaterThan(0), "at least one available card");
            Assert.That(s_Problems, Is.Empty);
            yield return new ExitPlayMode();
        }

        // SampleState alone: the composition table, the clamps without a warning, Restore, nothing outside play, and the
        // next session starts from the settings (no write at the play's end)
        [UnityTest]
        public IEnumerator KQ6_StateComposesAndRestores()
        {
            var edit0 = Counts();
            float editBase = FeedbackTimeScale.BaseScale;
            SampleState.SetAfter(false);
            SampleState.SetTimeScale(0.25f);
            Assert.That(Counts(), Is.EqualTo(edit0), "a call outside play warns");
            Assert.That(FeedbackTimeScale.BaseScale, Is.EqualTo(editBase), "a call outside play wrote");
            yield return new EnterPlayMode();
            Begin();
            m_SettingsOn = new bool[9];
            m_SettingsScale = new float[9];
            for (int i = 1; i < 9; i++)
            {
                m_SettingsOn[i] = FeedbackCategories.IsEnabled((FeedbackCategory)i);
                m_SettingsScale[i] = FeedbackCategories.GetScale((FeedbackCategory)i);
            }
            m_SettingsBase = FeedbackTimeScale.BaseScale;
            // the user's own switches before the samples write: Camera off · Motion at 0.5
            FeedbackCategories.SetEnabled(FeedbackCategory.Camera, false);
            FeedbackCategories.SetScale(FeedbackCategory.Motion, 0.5f);
            var on0 = new bool[9];
            var scale0 = new float[9];
            for (int i = 1; i < 9; i++)
            {
                on0[i] = FeedbackCategories.IsEnabled((FeedbackCategory)i);
                scale0[i] = FeedbackCategories.GetScale((FeedbackCategory)i);
            }
            int flash0 = FeedbackCategories.FlashLimit;
            float base0 = FeedbackTimeScale.BaseScale;
            SampleState.SetAfter(false);
            for (int i = 1; i < 9; i++)
                if (FeedbackCategories.IsEnabled((FeedbackCategory)i))
                    s_Problems.Add($"Before: {(FeedbackCategory)i} on");
            SampleState.SetAfter(true);
            for (int i = 1; i < 9; i++)
                if (FeedbackCategories.IsEnabled((FeedbackCategory)i) != on0[i])
                    s_Problems.Add($"After: {(FeedbackCategory)i} {FeedbackCategories.IsEnabled((FeedbackCategory)i)} (before {on0[i]})");
            SampleState.SetIntensity(0.5f);
            SampleState.SetCategory(FeedbackCategory.Flash, false, 1f);
            SampleState.SetCategory(FeedbackCategory.Motion, true, 2f);
            if (!Mathf.Approximately(FeedbackCategories.GetScale(FeedbackCategory.Motion), scale0[1] * 0.5f * 2f))
                s_Problems.Add($"Motion scale {FeedbackCategories.GetScale(FeedbackCategory.Motion)}");
            if (!Mathf.Approximately(FeedbackCategories.GetScale(FeedbackCategory.UI), scale0[7] * 0.5f))
                s_Problems.Add($"UI scale {FeedbackCategories.GetScale(FeedbackCategory.UI)}");
            if (FeedbackCategories.IsEnabled(FeedbackCategory.Flash) || FeedbackCategories.IsEnabled(FeedbackCategory.Camera))
                s_Problems.Add("Flash or Camera on");
            SampleState.SetIntensity(5f);
            SampleState.SetTimeScale(-1f);
            SampleState.SetCategory(FeedbackCategory.None, false, -3f);
            SampleState.SetFlashLimit(-2);
            if (SampleState.Intensity != SampleState.MaxIntensity || SampleState.TimeScale != 0f || FeedbackCategories.FlashLimit != 0)
                s_Problems.Add($"clamps: intensity {SampleState.Intensity} time {SampleState.TimeScale} flash {FeedbackCategories.FlashLimit}");
            SampleState.SetTimeScale(0.5f);
            SampleState.SetFlashLimit(1);
            if (!Mathf.Approximately(FeedbackTimeScale.BaseScale, base0 * 0.5f) || FeedbackCategories.FlashLimit != 1)
                s_Problems.Add($"base {FeedbackTimeScale.BaseScale} flash {FeedbackCategories.FlashLimit}");
            SampleState.SetSampleTimeScale(0f);
            if (FeedbackTimeScale.BaseScale != 0f)
                s_Problems.Add($"a sample's pause: base {FeedbackTimeScale.BaseScale}");
            // a sample's own scale composes with the HUD's (0.5 × 2 = the start's) and clamps
            SampleState.SetSampleTimeScale(2f);
            if (!Mathf.Approximately(FeedbackTimeScale.BaseScale, base0))
                s_Problems.Add($"HUD 0.5 × sample 2: base {FeedbackTimeScale.BaseScale}");
            SampleState.SetSampleTimeScale(-1f);
            float negative = SampleState.SampleTimeScale;
            SampleState.SetSampleTimeScale(float.NaN);
            float nan = SampleState.SampleTimeScale;
            SampleState.SetSampleTimeScale(1000f);
            if (negative != 0f || nan != 1f || SampleState.SampleTimeScale != 100f || FeedbackTimeScale.BaseScale > 100f)
                s_Problems.Add($"sample scale clamps: {negative} {nan} {SampleState.SampleTimeScale} base {FeedbackTimeScale.BaseScale}");
            // the sample's scale left at 100 and the HUD's at 0.5 — Restore puts both back
            SampleState.Restore();
            bool back = Mathf.Approximately(FeedbackTimeScale.BaseScale, base0) && FeedbackCategories.FlashLimit == flash0;
            for (int i = 1; i < 9; i++)
                back &= FeedbackCategories.IsEnabled((FeedbackCategory)i) == on0[i] && Mathf.Approximately(FeedbackCategories.GetScale((FeedbackCategory)i), scale0[i]);
            if (!back)
                s_Problems.Add("Restore did not put the values back");
            if (SampleState.After != true || SampleState.Intensity != 1f || SampleState.TimeScale != 1f || SampleState.SampleTimeScale != 1f || SampleState.Captured)
                s_Problems.Add("Restore did not reset the samples' state");
            ConsoleZero();
            Assert.That(s_Problems, Is.Empty);
            yield return new ExitPlayMode();
            yield return new EnterPlayMode();
            var next = new List<string>();
            for (int i = 1; i < 9; i++)
                if (FeedbackCategories.IsEnabled((FeedbackCategory)i) != m_SettingsOn[i] || !Mathf.Approximately(FeedbackCategories.GetScale((FeedbackCategory)i), m_SettingsScale[i]))
                    next.Add(((FeedbackCategory)i).ToString());
            if (!Mathf.Approximately(FeedbackTimeScale.BaseScale, m_SettingsBase))
                next.Add("BaseScale " + FeedbackTimeScale.BaseScale);
            Assert.That(next, Is.Empty, "the next session starts from the settings");
            yield return new ExitPlayMode();
        }

        const string SavedKey = "xpTURN.Feedback.Tests.Samples.KQ6b.Saved";
        const string OptionsKey = "xpTURN.Feedback.Tests.Samples.KQ6b.Options";
        const string EnabledKey = "xpTURN.Feedback.Tests.Samples.KQ6b.Enabled";
        static bool s_After2;
        static float s_Time2;
        static bool s_Captured2;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();
            if (SessionState.GetBool(SavedKey, false))
            {
                EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(OptionsKey, 0);
                EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(EnabledKey, false);
                SessionState.EraseBool(SavedKey);
                SessionState.EraseInt(OptionsKey);
                SessionState.EraseBool(EnabledKey);
            }
        }

        // with the domain reload off a static survives the play: the samples' state still starts over (SubsystemRegistration)
        [UnityTest]
        public IEnumerator KQ6b_StateStartsOverWithoutDomainReload()
        {
            SessionState.SetBool(SavedKey, true);
            SessionState.SetInt(OptionsKey, (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetBool(EnabledKey, EditorSettings.enterPlayModeOptionsEnabled);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            yield return new EnterPlayMode(false);
            yield return Frames(5);
            SampleState.SetAfter(false);
            SampleState.SetTimeScale(0.5f);
            yield return new ExitPlayMode();
            yield return new EnterPlayMode(false);
            yield return Frames(5);
            s_After2 = SampleState.After;
            s_Time2 = SampleState.TimeScale;
            s_Captured2 = SampleState.Captured;
            yield return new ExitPlayMode();
            Assert.That((s_After2, s_Time2, s_Captured2), Is.EqualTo((true, 1f, false)), "the samples' state of the play before");
        }

        static void Press(Keyboard kb, Key key) => InputSystem.QueueStateEvent(kb, new KeyboardState(key));
        static void Release(Keyboard kb) => InputSystem.QueueStateEvent(kb, new KeyboardState());
        static void Pad(Gamepad pad, GamepadButton b) => InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(b));
        static void PadRelease(Gamepad pad) => InputSystem.QueueStateEvent(pad, new GamepadState());

        static string Selected() => EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null ? EventSystem.current.currentSelectedGameObject.name : "none";

        // input through the Common module and actions (virtual devices; batch drops them unless the focus settings are set):
        // the HUD shortcut (the positive control) · the hub's first selection, the pad's down and submit · the pointer over
        // the HUD
        [UnityTest]
        public IEnumerator KQ13_InputThroughTheCommonModule()
        {
            yield return new EnterPlayMode();
            Begin();
            var bg0 = InputSystem.settings.backgroundBehavior;
            var ed0 = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var kb = InputSystem.AddDevice<Keyboard>();
            var pad = InputSystem.AddDevice<Gamepad>();
            var mouse = InputSystem.AddDevice<Mouse>();
            LoadHub();
            yield return Frames(30);
            Press(kb, Key.Tab);
            yield return Frames(3);
            bool flipped = !SampleState.After;
            Release(kb);
            yield return Frames(3);
            Press(kb, Key.Tab);
            yield return Frames(3);
            Release(kb);
            yield return Frames(3);
            if (!flipped || !SampleState.After)
                s_Problems.Add($"Tab: flipped {flipped} back {SampleState.After}");
            List<SampleCard> available = SampleHub.Instance.Cards.Where(SampleCards.Available).ToList();
            if (Selected() != available[0].Title)
                s_Problems.Add($"first selection {Selected()} (expected {available[0].Title})");
            SampleCard target = available[0];
            if (available.Count > 1)
            {
                Pad(pad, GamepadButton.DpadDown);
                yield return Frames(3);
                PadRelease(pad);
                yield return Frames(3);
                if (Selected() != available[1].Title)
                    s_Problems.Add($"after down {Selected()} (expected {available[1].Title})");
                else
                    target = available[1];
            }
            Pad(pad, GamepadButton.South);
            yield return Frames(3);
            PadRelease(pad);
            yield return Frames(60);
            if (SceneManager.GetActiveScene().name != target.SceneName)
                s_Problems.Add($"submit opened {SceneManager.GetActiveScene().name} (expected {target.SceneName})");
            var panel = (RectTransform)SampleHud.Instance.transform.Find("Panel");
            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            Vector2 over = (corners[0] + corners[2]) * 0.5f;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = over });
            yield return Frames(4);
            bool overUi = SampleInput.PointerOverUI;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(Screen.width - 4f, 4f) });
            yield return Frames(4);
            bool outside = SampleInput.PointerOverUI;
            if (!overUi || outside)
                s_Problems.Add($"pointer over the HUD {overUi} · outside {outside} (screen {Screen.width}x{Screen.height} · panel centre {over})");
            if (EventSystems() != 1)
                s_Problems.Add($"event systems {EventSystems()}");
            ConsoleZero();
            InputSystem.RemoveDevice(kb);
            InputSystem.RemoveDevice(pad);
            InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior = bg0;
            InputSystem.settings.editorInputBehaviorInPlayMode = ed0;
            Debug.Log($"KQ13 done problems={s_Problems.Count}");
            Assert.That(s_Problems, Is.Empty);
            yield return new ExitPlayMode();
        }

        // in the old-input clone — the HUD says why there is no input, and nothing else is said
        [UnityTest]
        public IEnumerator KQ15_OldInputNotice()
        {
            yield return new EnterPlayMode();
            Begin();
            LoadHub();
            yield return Frames(30);
            bool notice = SampleHud.Instance != null && SampleHud.Instance.GetComponentsInChildren<Text>(true).Any(t => t.text.Contains("Active Input Handling"));
#if ENABLE_INPUT_SYSTEM
            if (notice)
                s_Problems.Add("the old-input notice shows with the Input System enabled");
#else
            if (!notice)
                s_Problems.Add("no old-input notice");
#endif
            ConsoleZero();
            Assert.That(s_Problems, Is.Empty);
            yield return new ExitPlayMode();
        }

        // the editor's 'Select Player' label: a player's text is built once — back to a player that played before, the label holds
        // the same string
        [UnityTest]
        public IEnumerator EQ4_SelectPlayerLabelBuiltOnce()
        {
            yield return new EnterPlayMode();
            LoadHub();
            yield return PlayFrames(10);
            FeedbackPlayer a = new GameObject("A").AddComponent<FeedbackPlayer>();
            FeedbackPlayer b = new GameObject("B").AddComponent<FeedbackPlayer>();
            SampleHud hud = SampleHud.Instance;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo last = typeof(SampleHud).GetField("m_LastPlayed", flags);
            Text label = (Text)typeof(SampleHud).GetField("m_SelectLabel", flags).GetValue(hud);
            last.SetValue(hud, a);
            yield return PlayFrames(2);
            string first = label.text;
            last.SetValue(hud, b);
            yield return PlayFrames(2);
            string second = label.text;
            last.SetValue(hud, a);
            yield return PlayFrames(2);
            Assert.That((first, second, ReferenceEquals(label.text, first)), Is.EqualTo(("Select Player: A", "Select Player: B", true)));
            yield return new ExitPlayMode();
        }
    }
}
