using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using xpTURN.Feedback.Samples;
using xpTURN.Feedback.Samples.Editor;
using static xpTURN.Feedback.Tests.Editor.Samples.SampleKitTestSupport;

namespace xpTURN.Feedback.Tests.Editor.Samples
{
    // S1's jump and landing · 'Open in Editor' (the editor's half · the hub's button) · the gallery's TMP gate · the cards'
    // controls. The Basics types are reached by name
    public sealed class BasicsTests
    {
        static int s_Played;
        static readonly string[] s_HudKeys = { "Tab", "R3", "[", "]", "LB", "RB", "LT", "RT", "Esc", "Start", "Select", "H", "L3", "F1", "F2", "F3", "F4", "F5" };

        static string Scene(string name)
        {
            string path = AssetDatabase.FindAssets("t:Scene").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == name);
            if (path == null)
                Assert.Ignore("no Basics bundle in this project");
            return path;
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
                yield return null;
        }

        [UnityTest]
        public IEnumerator BQ07a_TheCompleteJumpsAndLands() => JumpAndLand();

        [UnityTest]
        public IEnumerator BQ07b_TheEmptyJumpsOnly() => JumpAndLand();

        // everything is read after the play's entry — its reload clears an iterator's locals
        static IEnumerator JumpAndLand()
        {
            yield return new EnterPlayMode();
            bool empty = TestContext.CurrentContext.Test.Name.Contains("Empty");
            EditorSceneManager.LoadSceneInPlayMode(Scene(empty ? "GettingStartedEmpty" : "GettingStarted"), new LoadSceneParameters(LoadSceneMode.Single));
            var bg0 = InputSystem.settings.backgroundBehavior;
            var ed0 = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var kb = InputSystem.AddDevice<Keyboard>();
            yield return Frames(30);
            FeedbackPlayer[] players = Resources.FindObjectsOfTypeAll<FeedbackPlayer>().Where(p => p != null && p.gameObject != null && p.gameObject.scene.isLoaded).ToArray();
            s_Played = 0;
            foreach (FeedbackPlayer p in players)
                p.Played += (pl, h) => s_Played++;
            (int e0, int w0) = Counts();
            Transform hero = Resources.FindObjectsOfTypeAll<Rigidbody>().First(r => r.gameObject.scene.isLoaded).transform;
            float y0 = hero.position.y, top = y0;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space));
            yield return Frames(3);
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            float until = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < until)
            {
                top = Mathf.Max(top, hero.position.y);
                yield return null;
            }
            (int e1, int w1) = Counts();
            float landed = hero.position.y - y0;
            InputSystem.RemoveDevice(kb);
            InputSystem.settings.backgroundBehavior = bg0;
            InputSystem.settings.editorInputBehaviorInPlayMode = ed0;
            yield return new ExitPlayMode();
            Assert.That(top - y0, Is.GreaterThan(1f), "the jump");
            Assert.That(Mathf.Abs(landed), Is.LessThan(0.05f), "landed again");
            Assert.That((players.Length, s_Played), Is.EqualTo(empty ? (0, 0) : (2, 2)), "players · plays (Jump on the jump, Land on the landing)");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        [Test]
        public void BQ08a_OpenInEditorTheEditorsHalf()
        {
            Scene("GettingStarted");
            SampleCard[] s1 = Catalogs().SelectMany(c => c.Cards).Where(c => c != null && c.Title.StartsWith("Getting Started", StringComparison.Ordinal)).ToArray();
            Assert.That(s1.Select(c => c.OpenInEditor), Is.EqualTo(new[] { true, true }), "S1's two cards open in the editor");
            SampleCard card = s1[0];
            string cardPath = AssetDatabase.GetAssetPath(card);
            string scenePath = AssetDatabase.GUIDToAssetPath(card.SceneGuid);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetString(SampleKitPaths.OpenInEditorKey, scenePath + "|" + AssetDatabase.AssetPathToGUID(cardPath));
            bool opened = SampleOpenInEditor.OpenPending();
            string active = SceneManager.GetActiveScene().path;
            // opening a scene unloads assets nothing holds — the card is read again (a dropped one compares equal to null)
            UnityEngine.Object selection = Selection.activeObject;
            bool selected = selection != null && selection == AssetDatabase.LoadAssetAtPath<SampleCard>(cardPath);
            if (!Application.isBatchMode)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Assert.That((opened, active, selected), Is.EqualTo((true, scenePath, true)), "opened and selected");
                return;
            }
            // a dirty scene: the save question is answered no (batch — UTF logs the dialog's cancel) and nothing is opened
            new GameObject("BQ08Dirt");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SessionState.SetString(SampleKitPaths.OpenInEditorKey, scenePath + "|x");
            LogAssert.Expect(LogType.Assert, new System.Text.RegularExpressions.Regex("Cancel+ing DisplayDialog"));
            bool openedDirty = SampleOpenInEditor.OpenPending();
            bool keyLeft = SessionState.GetString(SampleKitPaths.OpenInEditorKey, "") != "";
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.That((opened, active, selected), Is.EqualTo((true, scenePath, true)), "opened and selected");
            Assert.That((openedDirty, keyLeft), Is.EqualTo((false, false)), "a refused save opens nothing");
        }

        [UnityTest]
        public IEnumerator BQ08b_TheHubsButton()
        {
            yield return new EnterPlayMode();
            Scene("GettingStarted");
            EditorSceneManager.LoadSceneInPlayMode(SampleKitPaths.HubScene, new LoadSceneParameters(LoadSceneMode.Single));
            yield return Frames(10);
            List<Button> buttons = Resources.FindObjectsOfTypeAll<Button>().Where(b => b.gameObject.scene.isLoaded && b.name == "Open in Editor").ToList();
            int cards = SampleHub.Instance.Cards.Count(c => c.OpenInEditor && SampleCards.Available(c));
            bool none = AllNavigationNone(buttons);
            bool outside = NoneIsACard(buttons);
            yield return new ExitPlayMode();
            Assert.That((buttons.Count, none, outside), Is.EqualTo((cards, true, true)), "a mouse-only button per such card, not a card");
        }

        // static helpers: a lambda in a play-mode iterator must not capture a local (the reload drops the closure)
        static bool AllNavigationNone(List<Button> buttons) => buttons.All(b => b.navigation.mode == Navigation.Mode.None);

        static bool NoneIsACard(List<Button> buttons)
        {
            var cards = (List<Button>)typeof(SampleHub).GetField("m_Buttons", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(SampleHub.Instance);
            return buttons.All(b => !cards.Contains(b));
        }

        static IEnumerator TmpGate(bool forceMissing)
        {
            yield return new EnterPlayMode();
            Func<bool> probe = SampleTmp.s_Probe;
            if (forceMissing)
            {
                SampleTmp.s_Probe = () => false;
                SampleTmp.Reset();
            }
            EditorSceneManager.LoadSceneInPlayMode(Scene("FeedbackGallery"), new LoadSceneParameters(LoadSceneMode.Single));
            yield return Frames(10);
            (int e0, int w0) = Counts();
            Component g = Resources.FindObjectsOfTypeAll<MonoBehaviour>().First(m => m != null && m.GetType().Name == "FeedbackGallery" && m.gameObject.scene.isLoaded);
            var entries = (IList)g.GetType().GetProperty("Entries").GetValue(g);
            MethodInfo available = g.GetType().GetMethod("Available");
            MethodInfo press = g.GetType().GetMethod("Press");
            int tmp = 0, usable = 0;
            object first = null;
            foreach (object e in entries)
                if ((bool)e.GetType().GetField("NeedsTmp").GetValue(e))
                {
                    tmp++;
                    if ((bool)available.Invoke(g, new[] { e }))
                        usable++;
                    first = first ?? e;
                }
            press.Invoke(g, new[] { first });
            yield return Frames(60);
            (int e1, int w1) = Counts();
            SampleTmp.s_Probe = probe;
            SampleTmp.Reset();
            yield return new ExitPlayMode();
            Assert.That(tmp, Is.GreaterThan(0), "entries that need TMP");
            // the expectation from the forced seam or the resources themselves — never from SampleTmp.Ready (the code under test)
            bool present = !forceMissing && Resources.Load("TMP Settings") != null;
            Assert.That(usable, Is.EqualTo(present ? tmp : 0), "the TMP entries follow the resources");
            Assert.That((e1 - e0, w1 - w0), Is.EqualTo((0, 0)), "Console: " + string.Join(" / ", RowsFrom(0).Take(3)));
        }

        [UnityTest]
        public IEnumerator BQ09a_TheTmpGate() => TmpGate(false);

        [UnityTest]
        public IEnumerator BQ09b_TheTmpGateWithoutResources() => TmpGate(true);

        // a card's controls are the sample's only — the HUD shows its keys
        [Test]
        public void BQ11a_CardsHoldNoHudKey()
        {
            var problems = new List<string>();
            foreach (SampleCard card in Catalogs().SelectMany(c => c.Cards).Where(c => c != null))
            {
                string[] tokens = (card.Controls ?? "").Split(new[] { '·', '—', ',', '/', '\n', '(', ')' }, StringSplitOptions.RemoveEmptyEntries).SelectMany(s => s.Trim().Split(' ')).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
                foreach (string key in s_HudKeys.Where(k => tokens.Contains(k)))
                    problems.Add($"{card.Title}: {key}");
            }
            Assert.That(problems, Is.Empty);
        }
    }
}
