using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // a window for the tests' elements — a panel: a button's clicked runs on a NavigationSubmitEvent
    internal sealed class PreviewBarTestWindow : EditorWindow
    {
    }

    internal static class PreviewBarTesting
    {
        internal static void Submit(Button button)
        {
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = button;
                button.SendEvent(e);
            }
        }

        internal static FeedbackPlayer Mover(string name, float length = 1f)
        {
            var go = new GameObject(name);
            go.transform.position = new Vector3(1f, 0f, 0f);
            var player = go.AddComponent<FeedbackPlayer>();
            var item = new PositionItem { m_Motion = new TweenMotion { m_Params = new TweenParams { m_To = new Vector4(5f, 5f, 5f, 5f), m_Loops = 1 } } };
            item.m_Target.m_Mode = TargetRefMode.Self;
            item.m_Timing.m_Duration = length;
            player.m_Items.Add(item);
            return player;
        }

        internal static ShakeMotion Shake() => new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = new Vector4(1f, 1f, 0f, 0f), m_Frequency = 7f } };

        internal static MotionDriver Driver(string name)
        {
            var driver = new GameObject(name).AddComponent<MotionDriver>();
            var item = new PositionItem { m_Motion = Shake() };
            item.m_Target.m_Mode = TargetRefMode.Self;
            item.m_Timing.m_Duration = 1f;
            driver.m_Item = item;
            return driver;
        }

        internal static PreviewBar Bar(EditorWindow window, Object target)
        {
            var inspector = new InspectorElement(target);
            window.rootVisualElement.Add(inspector);
            return inspector.Q<PreviewBar>();
        }

        internal static IEnumerator Wait(double seconds)
        {
            double end = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < end)
                yield return null;
        }

        internal static void Destroy(params string[] names)
        {
            foreach (string name in names)
            {
                GameObject go = GameObject.Find(name);
                if (go != null)
                    Object.DestroyImmediate(go);
            }
        }
    }

    // the end's memory · the bar, the overlay, the words. The session runs on the test's clock — an update is 1/120 s; the bar
    // is refreshed by the test (its scheduler is the wall clock). The bar's settings are the user's, kept and put back
    public sealed class PreviewBarTests
    {
        const string ScenePath = "Assets/PreviewBarTests.unity";
        const string PrefabPath = "Assets/PreviewBarTests.prefab";
        const double UpdateStep = 1.0 / 120.0;

        static readonly string[] s_Bools = { PreviewBar.UnconditionalKey, PreviewBar.RepeatKey, PreviewSession.LiveTuningKey, PreviewSession.EndOnDeselectKey };
        static double s_Now;

        bool[] m_Had;
        bool[] m_Values;
        bool m_HadSpeed;
        float m_Speed;
        IPreviewRecordSource m_Source;
        Func<double> m_Clock;
        Scene m_Scene;
        Scene m_Active;
        PreviewBarTestWindow m_Window;

        [SetUp]
        public void SetUp()
        {
            m_Had = s_Bools.Select(EditorPrefs.HasKey).ToArray();
            m_Values = s_Bools.Select(k => EditorPrefs.GetBool(k, false)).ToArray();
            m_HadSpeed = EditorPrefs.HasKey(PreviewBar.SpeedKey);
            m_Speed = EditorPrefs.GetFloat(PreviewBar.SpeedKey, 1f);
            foreach (string key in s_Bools)
                EditorPrefs.DeleteKey(key);
            EditorPrefs.DeleteKey(PreviewBar.SpeedKey);
            m_Source = PreviewRecordPaths.Source;
            m_Clock = PreviewClock.Source;
            PreviewRecordPaths.Source = new PreviewRecordPathTable();
            s_Now = 0;
            PreviewClock.Source = () => s_Now;
            m_Active = SceneManager.GetActiveScene();
            File.WriteAllText(Path.GetFullPath(ScenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            m_Scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(m_Scene);
            m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            m_Window.Show();
        }

        [TearDown]
        public void TearDown()
        {
            PreviewSession.Current?.Stop();
            Selection.activeObject = null;
            if (m_Window != null)
                m_Window.Close();
            for (int i = 0; i < s_Bools.Length; i++)
            {
                if (m_Had[i])
                    EditorPrefs.SetBool(s_Bools[i], m_Values[i]);
                else
                    EditorPrefs.DeleteKey(s_Bools[i]);
            }
            if (m_HadSpeed)
                EditorPrefs.SetFloat(PreviewBar.SpeedKey, m_Speed);
            else
                EditorPrefs.DeleteKey(PreviewBar.SpeedKey);
            PreviewRecordPaths.Source = m_Source;
            PreviewClock.Source = m_Clock;
            if (m_Active.IsValid())
                SceneManager.SetActiveScene(m_Active);
            if (m_Scene.IsValid())
                EditorSceneManager.CloseScene(m_Scene, true);
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(PrefabPath);
        }

        static void Pump(int updates)
        {
            for (int i = 0; i < updates; i++)
            {
                s_Now += UpdateStep;
                PreviewSession.Current?.Update();
            }
        }

        static void Seconds(double seconds) => Pump((int)Math.Round(seconds / UpdateStep));

        static PreviewSession S => PreviewSession.Current;

        static void Submit(Button button) => PreviewBarTesting.Submit(button);

        static FeedbackPlayer Mover(string name, float length = 1f) => PreviewBarTesting.Mover(name, length);

        static ShakeMotion Shake() => PreviewBarTesting.Shake();

        static bool In(VisualElement e) => e.parent != null;

        static float X(Component c) => c.transform.position.x;

        PreviewBar Bar(Object target) => PreviewBarTesting.Bar(m_Window, target);

        PreviewOverlay Overlay()
        {
            var overlay = new PreviewOverlay();
            m_Window.rootVisualElement.Add(overlay.CreatePanelContent());
            return overlay;
        }

        // the Game view's notice of a camera shake — what the session's notices hold
        static string CameraNotice()
        {
            var notices = new List<PreviewNotice>();
            PreviewNotices.Collect(S, notices);
            foreach (PreviewNotice n in notices)
            {
                if (n.Kind == PreviewNoticeKind.CameraGameView || n.Kind == PreviewNoticeKind.CameraMainScene || n.Kind == PreviewNoticeKind.NoMainCamera)
                    return n.Text;
            }
            return null;
        }

        static FeedbackPlayer Endless(string name)
        {
            FeedbackPlayer p = Mover(name, 0.1f);
            p.m_Items.Insert(0, new LoopStartItem());
            p.m_Items.Add(new LoopEndItem { m_LoopCount = 0 });
            return p;
        }

        // ---- the end's memory (the reloads are in PreviewBarReloadTests)

        [Test]
        public void PX22c_AnEndWithoutAReloadIsTold()
        {
            FeedbackPlayer p = Mover("A", 0.5f);
            PreviewBar b = Bar(p);
            Submit(b.PlayButton);
            Submit(b.StopButton);
            b.Refresh();
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.Stopped));
            Assert.That(b.Status.text, Is.EqualTo(PreviewTexts.Of(PreviewEndReason.Stopped)));
            Submit(b.PlayButton);
            Seconds(0.8);
            b.Refresh();
            Assert.That(S, Is.Null);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.AutoEnd));
            Assert.That(b.Status.text, Is.EqualTo(PreviewTexts.Of(PreviewEndReason.AutoEnd)));
        }

        // a refused replay ends the session at the next update — the bar tells the refusal, not the auto end
        [Test]
        public void PX22d_AnEndAfterARefusedReplayTellsTheRefusal()
        {
            FeedbackPlayer p = Mover("A", 2f);
            p.m_Cooldown = 10f;
            EditorPrefs.SetBool(PreviewBar.UnconditionalKey, false);
            PreviewBar b = Bar(p);
            Submit(b.PlayButton);
            Seconds(0.1);
            Submit(b.PlayButton);
            Seconds(0.3);
            b.Refresh();
            Assert.That(S, Is.Null);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.AutoEnd));
            Assert.That(b.Status.text, Is.EqualTo(PreviewTexts.ReplayRefused));
        }

        // ---- the overlay

        [Test]
        public void PX16a1_TheOverlayShowsDuringASession()
        {
            FeedbackPlayer p = Mover("A");
            PreviewOverlay o = Overlay();
            Assert.That(o.visible, Is.False);
            PreviewSession.Start(p, PlayArgs.Default);
            Assert.That(o.visible, Is.True);
            S.Stop();
            Assert.That(o.visible, Is.False);
        }

        [Test]
        public void PX16a2_TheOverlaysSubjectSelectsIt()
        {
            FeedbackPlayer p = Mover("A");
            PreviewSession.Start(p, PlayArgs.Default);
            PreviewOverlay o = Overlay();
            Selection.activeObject = null;
            Submit(o.Subject);
            Assert.That(Selection.activeObject, Is.SameAs(p));
        }

        [Test]
        public void PX16a3_TheOverlaysStopEnds()
        {
            FeedbackPlayer p = Mover("A");
            PreviewSession.Start(p, PlayArgs.Default);
            PreviewOverlay o = Overlay();
            Submit(o.Stop);
            Assert.That(S, Is.Null);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.Stopped));
        }

        [Test]
        public void PX16a4_SignalShowsForAnOpenWaitAndClosesIt()
        {
            FeedbackPlayer p = Mover("A", 0.2f);
            var wait = new WaitForSignalItem();
            wait.m_Timing.m_Rule = StartRule.AfterPrevious;
            p.m_Items.Add(wait);
            PreviewSession.Start(p, PlayArgs.Default.WithUnconditional());
            PreviewOverlay o = Overlay();
            Seconds(0.1);
            o.Refresh();
            Assert.That(o.Signal.style.display.value, Is.EqualTo(DisplayStyle.None), "no wait yet");
            Seconds(0.3);
            o.Refresh();
            Assert.That(o.Signal.style.display.value, Is.EqualTo(DisplayStyle.Flex), "a wait");
            Submit(o.Signal);
            Assert.That(p.OpenSignalWaits(), Is.Zero);
        }

        // the notices in their order — the two always there last
        [Test]
        public void PX16a5_TheOverlaysLinesAreTheNotices()
        {
            FeedbackPlayer p = Mover("A");
            p.m_Items.Add(new CameraShakeItem { m_Motion = Shake() });
            PreviewSession.Start(p, PlayArgs.Default.WithUnconditional());
            PreviewOverlay o = Overlay();
            Seconds(0.05);
            o.Refresh();
            var notices = new List<PreviewNotice>();
            PreviewNotices.Collect(S, notices);
            int lines = o.ShownLines;
            Assert.That(lines, Is.EqualTo(notices.Count));
            Assert.That(o.LineText(lines - 2), Is.EqualTo(notices.First(n => n.Kind == PreviewNoticeKind.Locked).Text));
            Assert.That(o.LineText(lines - 1), Is.EqualTo(notices.First(n => n.Kind == PreviewNoticeKind.ExecuteAlways).Text));
        }

        // ---- the bar's buttons

        // the start takes the bar's fields — the mark (on by default), then the repeat and the speed
        [Test]
        public void PX16b1_PlayStartsWithTheBarsFields()
        {
            FeedbackPlayer p = Mover("A");
            Submit(Bar(p).PlayButton);
            Assert.That(S.Player, Is.SameAs(p));
            Assert.That(S.Args.Unconditional, Is.True);
            Assert.That(S.Repeat, Is.False);
            Assert.That(S.Speed, Is.EqualTo(1f));
            S.Stop();
            EditorPrefs.SetBool(PreviewBar.RepeatKey, true);
            EditorPrefs.SetFloat(PreviewBar.SpeedKey, 1.5f);
            Submit(Bar(Mover("B")).PlayButton);
            Assert.That(S.Repeat, Is.True);
            Assert.That(S.Speed, Is.EqualTo(1.5f));
        }

        [Test]
        public void PX16b2_PlayGoesOnAPausedPlay()
        {
            FeedbackPlayer p = Mover("A");
            PreviewBar b = Bar(p);
            Submit(b.PlayButton);
            PreviewSession s = S;
            Seconds(0.3);
            p.Pause();
            float x = X(p);
            Submit(b.PlayButton);
            Assert.That(S, Is.SameAs(s));
            Assert.That(p.HasPaused, Is.False);
            Assert.That(X(p), Is.EqualTo(x).Within(0.01f), "not started over");
        }

        [Test]
        public void PX16b3_PauseAndResume()
        {
            FeedbackPlayer p = Mover("A");
            PreviewBar b = Bar(p);
            Submit(b.PlayButton);
            Seconds(0.1);
            Submit(b.PauseButton);
            Assert.That(p.HasPaused, Is.True);
            Submit(b.PauseButton);
            Assert.That(p.HasPaused, Is.False);
        }

        [Test]
        public void PX16b4_StopEnds()
        {
            PreviewBar b = Bar(Mover("A"));
            Submit(b.PlayButton);
            Submit(b.StopButton);
            Assert.That(S, Is.Null);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.Stopped));
        }

        [Test]
        public void PX16b5_NewSeedPlaysAgain()
        {
            PreviewBar b = Bar(Mover("A"));
            Submit(b.PlayButton);
            Seconds(0.1);
            PlaybackHandle h = S.Handle;
            Submit(b.SeedButton);
            Assert.That(S.Handle, Is.Not.EqualTo(h));
        }

        [Test]
        public void PX16b6_TheSliderScrubs()
        {
            FeedbackPlayer p = Mover("A");
            PreviewBar b = Bar(p);
            Submit(b.PlayButton);
            Seconds(0.1);
            b.Refresh();
            b.Scrub.value = 0.5f;
            Assert.That(p.HasPaused, Is.True);
            Assert.That(X(p), Is.EqualTo(3f).Within(0.05f));
        }

        // the refresh moves the slider without a seek
        [Test]
        public void PX16b7_ARefreshDoesNotScrub()
        {
            FeedbackPlayer p = Mover("A");
            PreviewBar b = Bar(p);
            Submit(b.PlayButton);
            Seconds(0.3);
            float x = X(p);
            b.Refresh();
            Seconds(0.1);
            b.Refresh();
            Assert.That(p.HasPaused, Is.False);
            Assert.That(X(p), Is.GreaterThan(x));
        }

        // ---- the status line

        [Test]
        public void PX16c1_AStartRefusalIsTold()
        {
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(Mover("A").gameObject, PrefabPath);
            PreviewBar b = Bar(asset.GetComponent<FeedbackPlayer>());
            Submit(b.PlayButton);
            Assert.That(S, Is.Null);
            Assert.That(b.Status.text, Is.EqualTo(PreviewTexts.Of(PreviewRefusal.Asset)));
        }

        [Test]
        public void PX16c2_ARefusedReplayIsTold()
        {
            FeedbackPlayer p = Mover("A", 2f);
            p.m_Cooldown = 10f;
            EditorPrefs.SetBool(PreviewBar.UnconditionalKey, false);
            PreviewBar b = Bar(p);
            Submit(b.PlayButton);
            Seconds(0.1);
            Submit(b.PlayButton);
            b.Refresh();
            Assert.That(S, Is.Not.Null);
            Assert.That(b.Status.text, Is.EqualTo(PreviewTexts.ReplayRefused));
        }

        [Test]
        public void PX16c3_AScrubRefusalIsTold()
        {
            PreviewBar b = Bar(Endless("A"));
            Submit(b.PlayButton);
            Seconds(0.1);
            b.Refresh();
            Assert.That(b.Status.text, Is.EqualTo(PreviewTexts.Of(SeekRefusal.Endless)));
            Assert.That(b.Scrub.enabledSelf, Is.False);
        }

        [Test]
        public void PX16c4_TheGameViewsNoticeIsTold()
        {
            FeedbackPlayer p = Mover("A");
            p.m_Items.Add(new CameraShakeItem { m_Motion = Shake() });
            PreviewBar b = Bar(p);
            Submit(b.PlayButton);
            Seconds(0.1);
            b.Refresh();
            string notice = CameraNotice();
            (string text, HelpBoxMessageType type, DisplayStyle display, string status) told = (b.Notice.text, b.Notice.messageType, b.Notice.style.display.value, b.Status.text);
            Submit(b.StopButton);
            b.Refresh();
            Assert.That(notice, Is.Not.Null);
            Assert.That(told, Is.EqualTo((notice, HelpBoxMessageType.Warning, DisplayStyle.Flex, "")), "the notice in a warning box");
            Assert.That((b.Notice.style.display.value, b.Status.text), Is.EqualTo((DisplayStyle.None, PreviewTexts.Of(PreviewEndReason.Stopped))), "the end — plain, the box gone");
        }

        // the end of its own subject only — and the last one
        [Test]
        public void PX16c5_OnlyTheSubjectsEndIsTold()
        {
            PreviewBar a = Bar(Mover("A"));
            PreviewBar c = Bar(Mover("B"));
            Submit(a.PlayButton);
            Submit(a.StopButton);
            a.Refresh();
            c.Refresh();
            Assert.That(a.Status.text, Is.EqualTo(PreviewTexts.Of(PreviewEndReason.Stopped)));
            Assert.That(c.Status.text, Is.Empty);
            Submit(a.PlayButton);
            Submit(c.PlayButton);
            a.Refresh();
            Assert.That(a.Status.text, Is.EqualTo(PreviewTexts.Of(PreviewEndReason.Replaced)));
        }

        [Test]
        public void PX16c6_AScrubRefusalComesBeforeTheNotice()
        {
            FeedbackPlayer p = Endless("A");
            p.m_Items.Add(new CameraShakeItem { m_Motion = Shake() });
            PreviewBar b = Bar(p);
            Submit(b.PlayButton);
            Seconds(0.1);
            b.Refresh();
            Assert.That(CameraNotice(), Is.Not.Null);
            Assert.That(b.Status.text, Is.EqualTo(PreviewTexts.Of(SeekRefusal.Endless)));
        }

        [Test]
        public void PX16d_TheTextsCoverTheEnums()
        {
            foreach (PreviewEndReason r in Enum.GetValues(typeof(PreviewEndReason)))
                Assert.That(r == PreviewEndReason.None || !string.IsNullOrEmpty(PreviewTexts.Of(r)), r.ToString());
            foreach (PreviewRefusal r in Enum.GetValues(typeof(PreviewRefusal)))
                Assert.That(r == PreviewRefusal.None || !string.IsNullOrEmpty(PreviewTexts.Of(r)), r.ToString());
            foreach (SeekRefusal r in Enum.GetValues(typeof(SeekRefusal)))
                Assert.That(r == SeekRefusal.None || !string.IsNullOrEmpty(PreviewTexts.Of(r)), r.ToString());
        }

        // ---- the driver's bar · many targets · the refresh · the settings

        [Test]
        public void PX16f_TheDriversBar()
        {
            MotionDriver d = PreviewBarTesting.Driver("D");
            PreviewBar b = Bar(d);
            Assert.That(In(b.PauseButton) || In(b.SeedButton) || In(b.Scrub) || In(b.IntensityField) || In(b.Unconditional) || In(b.RepeatToggle), Is.False, "the player's fields");
            Assert.That(In(b.SpeedField) && In(b.DeselectToggle), Is.True);
            Submit(b.PlayButton);
            Assert.That(S.MotionDriver, Is.SameAs(d));
            Submit(b.StopButton);
            Assert.That(S, Is.Null);
        }

        [Test]
        public void PX16g_ManyTargetsHaveNoBar()
        {
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(new Object[] { Mover("A"), Mover("B") });
            try
            {
                var inspector = new InspectorElement(editor);
                m_Window.rootVisualElement.Add(inspector);
                Assert.That(inspector.Q<PreviewBar>(), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        // the bar refreshes while it is in a panel only — the engine stops a detached element's schedule as well
        [UnityTest]
        public IEnumerator PX16h_TheRefreshRunsWhileAttached()
        {
            PreviewClock.Source = m_Clock;
            var inspector = new InspectorElement(Mover("A"));
            m_Window.rootVisualElement.Add(inspector);
            int r0 = PreviewBar.Refreshes;
            yield return PreviewBarTesting.Wait(0.35);
            Assert.That(PreviewBar.Refreshes - r0, Is.GreaterThan(0), "attached");
            m_Window.rootVisualElement.Remove(inspector);
            int r1 = PreviewBar.Refreshes;
            yield return PreviewBarTesting.Wait(0.35);
            Assert.That(PreviewBar.Refreshes - r1, Is.Zero, "detached");
        }

        // the settings' defaults and keys — a start without the bar keeps the session's own defaults
        [Test]
        public void PX16i_TheSettings()
        {
            FeedbackPlayer p = Mover("A");
            PreviewBar b = Bar(p);
            Assert.That(b.Unconditional.value, Is.True);
            Assert.That(b.RepeatToggle.value, Is.False);
            Assert.That(b.SpeedField.value, Is.EqualTo(1f));
            bool tuning = PreviewSession.LiveTuning;
            b.TuningToggle.value = !tuning;
            b.DeselectToggle.value = true;
            Assert.That(PreviewSession.LiveTuning, Is.EqualTo(!tuning));
            Assert.That(PreviewSession.EndOnDeselect, Is.True);
            PreviewSession.EndOnDeselect = false;
            PreviewSession.Start(p, PlayArgs.Default);
            Assert.That(S.Args.Unconditional, Is.False);
            Assert.That(S.Repeat, Is.False);
            Assert.That(S.Speed, Is.EqualTo(1f));
        }

        // a bar built again reads the session's arguments
        [Test]
        public void PX16s1_ABarBuiltAgainReadsTheArguments()
        {
            FeedbackPlayer p = Mover("A");
            PreviewBar b = Bar(p);
            b.IntensityField.value = 0.5f;
            b.DirectionField.index = 2;
            Submit(b.PlayButton);
            m_Window.rootVisualElement.Clear();
            PreviewBar again = Bar(p);
            Assert.That(again.IntensityField.value, Is.EqualTo(0.5f));
            Assert.That(again.DirectionField.index, Is.EqualTo(2));
        }

        // no Toggle — it would turn each replay around
        [Test]
        public void PX16s2_TheDirectionsHaveNoToggle()
        {
            PreviewBar b = Bar(Mover("A"));
            Assert.That(b.DirectionField.choices, Has.Count.EqualTo(3));
            Assert.That(b.DirectionField.choices, Has.No.Member("Toggle"));
        }

        // the overlay keeps its lines
        [Test]
        public void PX16s3_TheOverlayKeepsItsLines()
        {
            PreviewSession.Start(Mover("A"), PlayArgs.Default);
            PreviewOverlay o = Overlay();
            o.Refresh();
            VisualElement first = o.Lines[0];
            o.Refresh();
            Assert.That(o.Lines[0], Is.SameAs(first));
        }

        // a subject destroyed during the session — the bar does not throw
        [Test]
        public void PX16s4_ADestroyedSubject()
        {
            FeedbackPlayer p = Mover("A");
            PreviewBar b = Bar(p);
            Submit(b.PlayButton);
            Seconds(0.05);
            Object.DestroyImmediate(p.gameObject);
            Pump(2);
            Assert.DoesNotThrow(() =>
            {
                b.Refresh();
                Submit(b.PlayButton);
                Submit(b.StopButton);
                b.Refresh();
            });
            Assert.That(S, Is.Null);
            Assert.That(b.Status.text, Is.Empty);
        }

        // the rows by the mode, chosen at the build
        [Test]
        public void PX16s5_TheRowsOfTheEditMode()
        {
            PreviewBar b = Bar(Mover("A"));
            Assert.That(b.PlayRows.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(b.EditRows.style.display.value, Is.Not.EqualTo(DisplayStyle.None));
        }

        // a negative intensity is cut to 0
        [Test]
        public void PX16s6_ANegativeIntensity()
        {
            PreviewBar b = Bar(Mover("A"));
            Submit(b.PlayButton);
            b.IntensityField.value = -3f;
            Assert.That(b.IntensityField.value, Is.EqualTo(0f));
            Assert.That(S.Args.Intensity, Is.EqualTo(0f));
        }
    }

    // a script reload during the session: the fields of a test are gone after it, so the objects are found by name and the
    // bar's settings are not touched
    public sealed class PreviewBarReloadTests
    {
        [TearDown]
        public void TearDown()
        {
            PreviewSession.Current?.Stop();
            PreviewBarTesting.Destroy("BarReloadA", "BarReloadB");
        }

        // shipped API only — the last end survives the reload
        [UnityTest]
        public IEnumerator PX22a_TheLastEndCrossesAReload()
        {
            FeedbackPlayer p = PreviewBarTesting.Mover("BarReloadA", 5f);
            Assert.That(PreviewSession.Start(p, PlayArgs.Default), Is.EqualTo(PreviewRefusal.None));
            yield return null;
            EditorUtility.RequestScriptReload();
            yield return new WaitForDomainReload();
            Assert.That(PreviewSession.Current, Is.Null);
            Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.Reload));
        }

        [UnityTest]
        public IEnumerator PX22b_TheSubjectsBarTellsTheReload()
        {
            FeedbackPlayer p = PreviewBarTesting.Mover("BarReloadA", 5f);
            PreviewBarTesting.Mover("BarReloadB", 5f);
            PreviewSession.Start(p, PlayArgs.Default);
            yield return null;
            EditorUtility.RequestScriptReload();
            yield return new WaitForDomainReload();
            var window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            window.Show();
            try
            {
                PreviewBar a = PreviewBarTesting.Bar(window, GameObject.Find("BarReloadA").GetComponent<FeedbackPlayer>());
                PreviewBar b = PreviewBarTesting.Bar(window, GameObject.Find("BarReloadB").GetComponent<FeedbackPlayer>());
                // not empty first — an empty word would look like the bar of another subject
                Assert.That(a.Status.text, Is.Not.Empty);
                Assert.That(a.Status.text, Is.EqualTo(PreviewTexts.Of(PreviewEndReason.Reload)));
                Assert.That(b.Status.text, Is.Empty);
            }
            finally
            {
                window.Close();
            }
        }
    }

    // the bar in play mode: entering play reloads the domain, so the objects are found by name
    public sealed class PreviewBarPlayModeTests
    {
        [TearDown]
        public void TearDown()
        {
            PreviewSession.Current?.Stop();
            PreviewBarTesting.Destroy("BarPlayA", "BarPlayD");
        }

        // ▶ is the player's Play with the bar's arguments, ■ its Stop — no session
        [UnityTest]
        public IEnumerator PX16e_ThePlayModesBarPlaysAndStops()
        {
            PreviewBarTesting.Mover("BarPlayA", 1f);
            yield return new EnterPlayMode();
            var p = GameObject.Find("BarPlayA").GetComponent<FeedbackPlayer>();
            var window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            window.Show();
            PreviewBar b = PreviewBarTesting.Bar(window, p);
            b.DirectionField.index = 2;
            PreviewBarTesting.Submit(b.PlayModePlay);
            yield return PreviewBarTesting.Wait(0.1);
            float x0 = p.transform.position.x;
            yield return PreviewBarTesting.Wait(0.1);
            bool playing = p.IsPlaying;
            float x1 = p.transform.position.x;
            bool session = PreviewSession.Current != null;
            PreviewBarTesting.Submit(b.PlayModeStop);
            bool stopped = !p.IsPlaying;
            window.Close();
            yield return new ExitPlayMode();
            Assert.That(playing, Is.True);
            Assert.That(x1, Is.LessThan(x0), "backward — from 5 down");
            Assert.That(session, Is.False);
            Assert.That(stopped, Is.True);
        }

        [UnityTest]
        public IEnumerator PX16f2_ThePlayModeHidesTheDriversBar()
        {
            PreviewBarTesting.Driver("BarPlayD");
            yield return new EnterPlayMode();
            var window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            window.Show();
            PreviewBar b = PreviewBarTesting.Bar(window, GameObject.Find("BarPlayD").GetComponent<MotionDriver>());
            DisplayStyle display = b.style.display.value;
            window.Close();
            yield return new ExitPlayMode();
            Assert.That(display, Is.EqualTo(DisplayStyle.None));
        }

        // a session ended by play mode — the play mode's rows and the end
        [UnityTest]
        public IEnumerator PX16s7_ThePlayModesRowsAndEnd()
        {
            FeedbackPlayer p0 = PreviewBarTesting.Mover("BarPlayA", 1f);
            PreviewSession.Start(p0, PlayArgs.Default);
            yield return null;
            yield return new EnterPlayMode();
            var window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            window.Show();
            PreviewBar b = PreviewBarTesting.Bar(window, GameObject.Find("BarPlayA").GetComponent<FeedbackPlayer>());
            DisplayStyle edit = b.EditRows.style.display.value;
            DisplayStyle play = b.PlayRows.style.display.value;
            string status = b.Status.text;
            window.Close();
            yield return new ExitPlayMode();
            Assert.That(edit, Is.EqualTo(DisplayStyle.None));
            Assert.That(play, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(status, Is.EqualTo(PreviewTexts.Of(PreviewEndReason.PlayMode)));
        }
    }
}
