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
using UnityEngine.Serialization;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    internal static class TimelineTesting
    {
        internal static T Item<T>(float duration, StartRule rule = StartRule.WithPrevious) where T : TargetItem, new()
        {
            T item = InspectorTesting.Item<T>(InspectorTesting.Tween(1f), duration);
            item.m_Timing.m_Rule = rule;
            return item;
        }

        // a player made while inactive — its items in before it wakes
        internal static FeedbackPlayer Awake(string name, params FeedbackItem[] items)
        {
            var go = new GameObject(name);
            go.SetActive(false);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            go.SetActive(true);
            return p;
        }

        internal static void Edit(SerializedObject so, string path, float value)
        {
            Undo.IncrementCurrentGroup();
            so.Update();
            so.FindProperty(path).floatValue = value;
            so.ApplyModifiedProperties();
        }

        internal static void Pointer<T>(VisualElement target, Vector2 local, EventType type) where T : EventBase<T>, new()
        {
            var system = new Event { type = type, mousePosition = target.LocalToWorld(local), button = 0, clickCount = 1 };
            EventBase e = typeof(T) == typeof(PointerDownEvent) ? PointerDownEvent.GetPooled(system)
                : typeof(T) == typeof(PointerMoveEvent) ? PointerMoveEvent.GetPooled(system)
                : (EventBase)PointerUpEvent.GetPooled(system);
            using (e)
            {
                e.target = target;
                target.SendEvent(e);
            }
        }
    }

    // the strip, its drag, the heads and the edits' paths, and the wiring: the strip in the inspector, a drag by the pointer,
    // the heads of a preview
    internal sealed class InspectorTimelineTests
    {
        const string ScenePath = "Assets/InspectorTimelineTests.unity";
        const double UpdateStep = 1.0 / 120.0;
        const string Item0 = "m_Items.Array.data[0]";

        static double s_Now;
        PreviewBarTestWindow m_Window;
        Func<double> m_Clock;
        IPreviewRecordSource m_Source;
        Scene m_Scene;
        Scene m_Active;
        readonly List<string> m_Errors = new List<string>();

        [SetUp]
        public void SetUp()
        {
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
            m_Window.position = new Rect(0, 0, 500, 900);
            m_Window.Show();
            m_Errors.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            PreviewSession.Current?.Stop();
            PreviewRecovery.Run();
            m_Window.Close();
            PreviewRecordPaths.Source = m_Source;
            PreviewClock.Source = m_Clock;
            if (m_Active.IsValid())
                SceneManager.SetActiveScene(m_Active);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.path == ScenePath && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(s, true);
            }
            AssetDatabase.DeleteAsset(ScenePath);
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                m_Errors.Add(message.Split('\n')[0]);
        }

        static FeedbackPlayer Player(string name, params FeedbackItem[] items)
        {
            var p = new GameObject(name).AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            return p;
        }

        static void Pump(double seconds)
        {
            for (int i = 0; i < (int)Math.Round(seconds / UpdateStep); i++)
            {
                s_Now += UpdateStep;
                PreviewSession.Current?.Update();
            }
        }

        static TimelineModel Model(FeedbackPlayer p) => TimelineModel.Build(p.m_Items, p.m_DurationMultiplier, p.m_StartDelay);

        // the strip's total is the nominal play — the numbers the timeline gives the prepared runtime, not the function's own
        // output
        [Test]
        public void IX09a_TheStripIsTheNominalPlay()
        {
            var cases = new List<(string name, FeedbackPlayer player, double total)>();
            cases.Add(("sequence", Player("Seq", TimelineTesting.Item<PositionItem>(0.2f), TimelineTesting.Item<ScaleItem>(0.3f, StartRule.AfterPrevious)), 0.5));
            cases.Add(("with previous", Player("With", TimelineTesting.Item<PositionItem>(0.2f), TimelineTesting.Item<ScaleItem>(0.3f)), 0.3));
            var end = new LoopEndItem { m_LoopCount = 3, m_LoopGap = 0.1f };
            end.m_Timing.m_Rule = StartRule.AfterPrevious;
            FeedbackPlayer loop = Player("Loop", new LoopStartItem(), TimelineTesting.Item<PositionItem>(0.2f), end);
            cases.Add(("loop ×3 gap 0.1", loop, 0.8));
            var bg = TimelineTesting.Item<ScaleItem>(1f);
            bg.m_Timing.m_Background = true;
            FeedbackPlayer background = Player("Bg", bg, TimelineTesting.Item<PositionItem>(0.2f, StartRule.AfterPrevious));
            cases.Add(("background", background, 0.2));
            cases.Add(("hold", Player("Hold", TimelineTesting.Item<PositionItem>(0.2f), new HoldItem(), TimelineTesting.Item<ScaleItem>(0.3f, StartRule.AfterPrevious)), 0.5));
            var off = TimelineTesting.Item<ScaleItem>(5f);
            off.m_Active = false;
            FeedbackPlayer inactive = Player("Off", TimelineTesting.Item<PositionItem>(0.2f), off);
            cases.Add(("an inactive effect keeps its time", inactive, 5.0));
            FeedbackPlayer scaled = Player("Scaled", TimelineTesting.Item<PositionItem>(0.2f), TimelineTesting.Item<ScaleItem>(0.3f, StartRule.AfterPrevious));
            scaled.m_DurationMultiplier = 2f;
            scaled.m_StartDelay = 0.5f;
            cases.Add(("×2 + 0.5", scaled, 1.5));
            var rep = TimelineTesting.Item<PositionItem>(0.2f);
            rep.m_Timing.m_RepeatCount = 2;
            rep.m_Timing.m_RepeatGap = 0.1f;
            FeedbackPlayer repeat = Player("Repeat", rep);
            cases.Add(("repeat 2 gap 0.1", repeat, 0.8));
            var inf = TimelineTesting.Item<PositionItem>(0.2f);
            inf.m_Timing.m_Infinite = true;
            FeedbackPlayer endless = Player("Endless", inf);
            cases.Add(("infinite", endless, double.PositiveInfinity));
            cases.Add(("a null slot", Player("Null", TimelineTesting.Item<PositionItem>(0.2f), null, TimelineTesting.Item<ScaleItem>(0.3f, StartRule.AfterPrevious)), 0.3));
            foreach (var (name, player, total) in cases)
            {
                TimelineModel m = Model(player);
                if (double.IsInfinity(total))
                    Assert.That(double.IsInfinity(m.Total), Is.True, name);
                else
                    Assert.That(m.Total, Is.EqualTo(total).Within(1e-6), name);
            }
            TimelineModel lm = Model(loop);
            Assert.That(lm.Bands.Count, Is.EqualTo(1));
            Assert.That(lm.Bands[0].Runs, Is.EqualTo(3));
            Assert.That(lm.Bars.Count, Is.EqualTo(1), "a bar per timed item, once in its loop — the markers are the band");
            Assert.That(lm.Bands[0].End - lm.Bands[0].Start, Is.EqualTo(0.8).Within(1e-6));
            TimelineModel rm = Model(repeat);
            Assert.That(rm.Bars[0].RunEnd, Is.EqualTo(0.2).Within(1e-6), "the first run");
            Assert.That(rm.Bars[0].End, Is.EqualTo(0.8).Within(1e-6), "the repeats");
            Assert.That(Model(inactive).Bars.Select(b => b.Off), Is.EqualTo(new[] { false, true }));
            Assert.That(Model(background).Bars[0].Background, Is.True);
            TimelineModel em = Model(endless);
            Assert.That(double.IsInfinity(em.Bars[0].End) && !em.Seekable, Is.True);
            Assert.That(TimelineStrip.TotalText(em), Is.EqualTo("Total ∞ · no seek"));
            TimelineModel sm = Model(scaled);
            Assert.That(sm.Bars[1].DelayStart, Is.EqualTo(0.9).Within(1e-6), "the second after the first's 0.4 and the 0.5 start delay");
        }

        // a drag moves the right edge (the duration) or the body (the delay) — each drag one undo
        [Test]
        public void IX09b_ADragIsOneUndo()
        {
            FeedbackPlayer p = Player("Drag", TimelineTesting.Item<PositionItem>(0.5f));
            var so = new SerializedObject(p);
            StripDrag edge = StripDrag.Begin(so, ItemListEdits.ItemsPath, 0, true, 1f);
            Assert.That(edge.Property, Is.EqualTo(Item0 + "." + ItemRules.Duration));
            edge.Move(0.1, false);
            edge.Move(0.2, false);
            edge.Move(0.23, false);
            edge.End();
            Assert.That(p.m_Items[0].m_Timing.m_Duration, Is.EqualTo(0.73f).Within(1e-5f));
            StripDrag body = StripDrag.Begin(so, ItemListEdits.ItemsPath, 0, false, 1f);
            body.Move(0.2, false);
            body.Move(0.4, false);
            body.End();
            Assert.That(p.m_Items[0].m_Timing.m_Delay, Is.EqualTo(0.4f).Within(1e-5f));
            Undo.PerformUndo();
            Assert.That(p.m_Items[0].m_Timing.m_Delay, Is.EqualTo(0f), "one undo — the whole drag");
            Assert.That(p.m_Items[0].m_Timing.m_Duration, Is.EqualTo(0.73f).Within(1e-5f), "the other drag stays");
            Undo.PerformUndo();
            Assert.That(p.m_Items[0].m_Timing.m_Duration, Is.EqualTo(0.5f).Within(1e-5f));
        }

        // 0.01 s steps unless free · never below 0 · the axis over the multiplier
        [Test]
        public void IX09s_TheDragSnaps()
        {
            Assert.That(StripDrag.Snap(0.734f, false), Is.EqualTo(0.73f).Within(1e-6f));
            Assert.That(StripDrag.Snap(0.736f, false), Is.EqualTo(0.74f).Within(1e-6f));
            Assert.That(StripDrag.Snap(0.734f, true), Is.EqualTo(0.734f).Within(1e-6f));
            Assert.That(StripDrag.Snap(-0.2f, false), Is.EqualTo(0f));
            FeedbackPlayer p = Player("Snap", TimelineTesting.Item<PositionItem>(0.5f));
            var so = new SerializedObject(p);
            StripDrag d = StripDrag.Begin(so, ItemListEdits.ItemsPath, 0, true, 2f);
            d.Move(0.5, false);
            d.End();
            Assert.That(p.m_Items[0].m_Timing.m_Duration, Is.EqualTo(0.75f).Within(1e-5f), "0.5 s of a ×2 axis is 0.25 s of the item");
        }

        // the inspector's strip — the total, a rebuild after an edit, a drag by the pointer at a bar's right edge
        [UnityTest]
        public IEnumerator IX09c_TheStripInTheInspectorDragsByThePointer()
        {
            FeedbackPlayer p = Player("Strip", TimelineTesting.Item<PositionItem>(0.5f), TimelineTesting.Item<ScaleItem>(0.5f, StartRule.AfterPrevious));
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.3);
            TimelineStrip strip = inspector.Q<TimelineStrip>();
            Assert.That(strip, Is.Not.Null);
            Assert.That(strip.Total.text, Is.EqualTo("Total 1 s"));
            Assert.That(strip.Auto, Is.Null, "no auto Rebuild in edit mode");
            Assert.That(strip.Canvas.contentRect.width, Is.GreaterThan(100f));
            float width = strip.Canvas.contentRect.width - TimelineStrip.Pad * 2f;
            var at = new Vector2(strip.X(0.5), strip.Y(0) + TimelineStrip.RowHeight * 0.5f);
            Assert.That(strip.Hit(at, out int bar, out bool edge) && bar == 0 && edge, Is.True, "the right edge");
            TimelineTesting.Pointer<PointerDownEvent>(strip.Canvas, at, EventType.MouseDown);
            TimelineTesting.Pointer<PointerMoveEvent>(strip.Canvas, at + new Vector2(width * 0.125f, 0f), EventType.MouseDrag);
            TimelineTesting.Pointer<PointerMoveEvent>(strip.Canvas, at + new Vector2(width * 0.25f, 0f), EventType.MouseDrag);
            TimelineTesting.Pointer<PointerUpEvent>(strip.Canvas, at + new Vector2(width * 0.25f, 0f), EventType.MouseUp);
            Assert.That(p.m_Items[0].m_Timing.m_Duration, Is.EqualTo(0.75f).Within(1e-5f), "a quarter of the 1 s axis");
            yield return InspectorTesting.Wait(0.2);
            Assert.That(strip.Total.text, Is.EqualTo("Total 1.25 s"), "rebuilt after the edit");
            Undo.PerformUndo();
            Assert.That(p.m_Items[0].m_Timing.m_Duration, Is.EqualTo(0.5f).Within(1e-5f), "one undo");
            var body = new Vector2(strip.X(0.25), strip.Y(0) + TimelineStrip.RowHeight * 0.5f);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(strip.Hit(body, out _, out bool bodyEdge) && !bodyEdge, Is.True, "the body");
            Assert.That(m_Errors, Is.Empty);
        }

        // a preview's play is a head at its place; none after the stop
        [UnityTest]
        public IEnumerator IX10a_APreviewsPlayIsAHead()
        {
            FeedbackPlayer p = Player("Head", TimelineTesting.Item<PositionItem>(1f));
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.2);
            TimelineStrip strip = inspector.Q<TimelineStrip>();
            Assert.That(PreviewSession.Start(p, PlayArgs.Default), Is.EqualTo(PreviewRefusal.None));
            Pump(0.3);
            strip.RefreshHeads();
            Assert.That(strip.HeadsShown, Is.True);
            Assert.That(strip.Heads.Count, Is.EqualTo(1));
            Assert.That(strip.Heads[0].Seconds, Is.EqualTo(TimelineTime.ToSeconds(p.Position(PreviewSession.Current.Handle))).Within(1e-9));
            Assert.That(strip.Heads[0].Seconds, Is.InRange(0.2, 0.4));
            PreviewSession.Current.Stop();
            strip.RefreshHeads();
            Assert.That(strip.Heads, Is.Empty);
        }

        // the branch of an edit's path — the list, the prepared fields and an item's timing · target · motion wait for a
        // Rebuild; the conditions, the randomness, the intensity and the other player fields reach the next Play
        [Test]
        public void IX12a_TheBranchesOfThePaths()
        {
            var item = TimelineTesting.Item<PositionItem>(1f);
            var curve = TimelineTesting.Item<ScaleItem>(1f);
            curve.m_Motion = new CurveMotion();
            FeedbackPlayer p = Player("Branches", item, curve);
            var so = new SerializedObject(p);
            so.Update();
            long rid = ManagedReferenceUtility.GetManagedReferenceIdForObject(p, p.m_Items[0]);
            long mrid = ManagedReferenceUtility.GetManagedReferenceIdForObject(p, item.m_Motion);
            long crid = ManagedReferenceUtility.GetManagedReferenceIdForObject(p, curve.m_Motion);
            string Item(string rest) => "managedReferences[" + rid + "]." + rest;
            var expected = new (string path, EditBranch branch)[]
            {
                (Item("m_Timing.m_Duration"), EditBranch.Rebuild),
                (Item("m_Timing.m_Rule"), EditBranch.Rebuild),
                (Item("m_Target.m_Object"), EditBranch.Rebuild),
                (Item("m_Active"), EditBranch.Rebuild),
                (Item("m_Conditions.m_Chance"), EditBranch.NextPlay),
                (Item("m_PlayDirection"), EditBranch.NextPlay),
                (Item("m_Random.m_RandomizeOutput"), EditBranch.NextPlay),
                (Item("m_Intensity.m_Min"), EditBranch.NextPlay),
                (Item("m_Label"), EditBranch.None),
                ("managedReferences[" + mrid + "].m_Params.m_To.x", EditBranch.Rebuild),
                ("m_Items.Array.size", EditBranch.Rebuild),
                ("m_Items.Array.data[1]", EditBranch.Rebuild),
                ("m_Policy", EditBranch.Rebuild),
                ("m_MaxOverlap", EditBranch.Rebuild),
                ("m_Anchors.Array.size", EditBranch.Rebuild),
                ("m_StartDelay", EditBranch.NextPlay),
                ("m_DurationMultiplier", EditBranch.NextPlay),
                ("m_Chance", EditBranch.NextPlay),
                ("m_Speed", EditBranch.NextPlay),
                ("m_TimeMode", EditBranch.NextPlay),
            };
            foreach (var (path, branch) in expected)
                Assert.That(PlayModeEdits.Classify(p, path, out bool now), Is.EqualTo(branch), path);
            Assert.That(PlayModeEdits.Classify(p, "managedReferences[" + crid + "].m_Params.m_Curve.m_Curve.Array.data[1].value", out bool curvesNow),
                Is.EqualTo(EditBranch.Rebuild));
            Assert.That(curvesNow, Is.True, "a curve is shared with the running plays");
            PlayModeEdits.Classify(p, "managedReferences[" + mrid + "].m_Params.m_To.x", out bool toNow);
            Assert.That(toNow, Is.False);
        }

        // an unprepared player marks nothing — its first prepare reads the edit
        [Test]
        public void IX12b_AnUnpreparedPlayerMarksNothing()
        {
            FeedbackPlayer p = Player("Unprepared", TimelineTesting.Item<PositionItem>(1f));
            Assert.That(p.Runtime, Is.Null);
            PlayModeEdits.Report(p, "m_Items.Array.size");
            Assert.That(PlayModeEdits.Pending(p), Is.False);
            Assert.That(PlayModeEdits.AutoRebuild, Is.EqualTo(EditorPrefs.GetBool(PlayModeEdits.AutoRebuildKey, false)), "off by default");
        }
    }

    // the play-mode share — the prepared total · two heads · the banner and the auto Rebuild. The bodies capture no local in a
    // lambda: the reload of the play mode does not keep one
    internal sealed class InspectorTimelinePlayModeTests
    {
        static double s_Now;

        static FeedbackPlayer Find(string name) => GameObject.Find(name).GetComponent<FeedbackPlayer>();

        static double Now() => s_Now;

        // the strip's total = the prepared runtime's TotalDuration
        [UnityTest]
        public IEnumerator IX09p_TheStripIsThePreparedTotal()
        {
            yield return new EnterPlayMode();
            FeedbackPlayer a = TimelineTesting.Awake("Prepared9", TimelineTesting.Item<PositionItem>(0.2f), TimelineTesting.Item<ScaleItem>(0.3f, StartRule.AfterPrevious));
            a.m_StartDelay = 0.5f;
            var end = new LoopEndItem { m_LoopCount = 2 };
            end.m_Timing.m_Rule = StartRule.AfterPrevious;
            FeedbackPlayer b = TimelineTesting.Awake("Prepared9b", new LoopStartItem(), TimelineTesting.Item<PositionItem>(0.25f), end);
            b.m_DurationMultiplier = 2f;
            yield return null;
            double ma = TimelineModel.Build(a.m_Items, a.m_DurationMultiplier, a.m_StartDelay).Total;
            double mb = TimelineModel.Build(b.m_Items, b.m_DurationMultiplier, b.m_StartDelay).Total;
            float ta = a.TotalDuration;
            float tb = b.TotalDuration;
            Object.Destroy(a.gameObject);
            Object.Destroy(b.gameObject);
            yield return new ExitPlayMode();
            Assert.That(ta, Is.EqualTo(1.0f).Within(1e-5f));
            Assert.That(ma, Is.EqualTo(ta).Within(1e-5));
            Assert.That(tb, Is.EqualTo(1.0f).Within(1e-5f));
            Assert.That(mb, Is.EqualTo(tb).Within(1e-5));
        }

        // a Chance edit marks nothing; a duration edit marks the player a frame later
        // (the report comes late) — the banner, the heads hidden; a Rebuild from code clears it; the banner's button too
        [UnityTest]
        public IEnumerator IX12p_TheBannerWaitsForARebuild()
        {
            yield return new EnterPlayMode();
            FeedbackPlayer p = TimelineTesting.Awake("Banner12", TimelineTesting.Item<PositionItem>(0.5f));
            yield return null;
            var window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            window.Show();
            InspectorElement inspector = InspectorTesting.Inspector(window, p);
            yield return InspectorTesting.Wait(0.3);
            InspectorStatus status = inspector.Q<InspectorStatus>();
            TimelineStrip strip = inspector.Q<TimelineStrip>();
            bool autoShown = strip.Auto != null;
            var so = new SerializedObject(p);
            TimelineTesting.Edit(so, "m_Items.Array.data[0].m_Conditions.m_Chance", 50f);
            yield return null;
            bool afterChance = PlayModeEdits.Pending(p);
            int count = p.PrepareCount;
            TimelineTesting.Edit(so, "m_Items.Array.data[0].m_Timing.m_Duration", 0.2f);
            bool sameFrame = PlayModeEdits.Pending(p);
            yield return null;
            bool afterDuration = PlayModeEdits.Pending(p);
            status.Refresh();
            strip.RefreshHeads();
            DisplayStyle banner = status.Edited.style.display.value;
            bool headsShown = strip.HeadsShown;
            p.Rebuild();
            bool afterRebuild = PlayModeEdits.Pending(p);
            int rebuilt = p.PrepareCount;
            strip.RefreshHeads();
            bool headsAgain = strip.HeadsShown;
            TimelineTesting.Edit(so, "m_Items.Array.data[0].m_Timing.m_Duration", 0.3f);
            yield return null;
            status.Refresh();
            DisplayStyle again = status.Edited.style.display.value;
            PreviewBarTesting.Submit(status.RebuildButton);
            status.Refresh();
            DisplayStyle afterButton = status.Edited.style.display.value;
            bool pendingAfterButton = PlayModeEdits.Pending(p);
            window.Close();
            Object.Destroy(p.gameObject);
            yield return new ExitPlayMode();
            Assert.That(autoShown, Is.True, "the auto Rebuild switch in play mode");
            Assert.That(afterChance, Is.False, "the next Play reads a Chance");
            Assert.That(sameFrame, Is.False, "the report comes late");
            Assert.That(afterDuration, Is.True);
            Assert.That(banner, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(headsShown, Is.False, "the heads hidden while an edit waits");
            Assert.That(rebuilt, Is.EqualTo(count + 1));
            Assert.That(afterRebuild, Is.False, "a Rebuild from code clears it");
            Assert.That(headsAgain, Is.True);
            Assert.That(again, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(afterButton, Is.EqualTo(DisplayStyle.None));
            Assert.That(pendingAfterButton, Is.False);
        }

        // the auto Rebuild — 150 ms on the editor clock after the edit, again at each edit
        [UnityTest]
        public IEnumerator IX12q_TheAutoRebuildWaits()
        {
            yield return new EnterPlayMode();
            bool saved = PlayModeEdits.AutoRebuild;
            Func<double> clock = PreviewClock.Source;
            PlayModeEdits.AutoRebuild = true;
            s_Now = 100;
            PreviewClock.Source = Now;
            FeedbackPlayer p = TimelineTesting.Awake("Auto12", TimelineTesting.Item<PositionItem>(0.5f));
            yield return null;
            int count = p.PrepareCount;
            var so = new SerializedObject(p);
            TimelineTesting.Edit(so, "m_Items.Array.data[0].m_Timing.m_Duration", 0.2f);
            yield return null;
            s_Now += 0.1;
            PlayModeEdits.Update();
            int at100 = p.PrepareCount;
            TimelineTesting.Edit(so, "m_Items.Array.data[0].m_Timing.m_Duration", 0.3f);
            yield return null;
            s_Now += 0.1;
            PlayModeEdits.Update();
            int atSecond100 = p.PrepareCount;
            s_Now += 0.06;
            PlayModeEdits.Update();
            int at160 = p.PrepareCount;
            bool pending = PlayModeEdits.Pending(p);
            PlayModeEdits.AutoRebuild = saved;
            PreviewClock.Source = clock;
            Object.Destroy(p.gameObject);
            yield return new ExitPlayMode();
            Assert.That(at100, Is.EqualTo(count), "not before 150 ms");
            Assert.That(atSecond100, Is.EqualTo(count), "a second edit waits again");
            Assert.That(at160, Is.EqualTo(count + 1));
            Assert.That(pending, Is.False);
        }

        // two plays alive at once — two heads, each at its play's place (the handle's position in seconds)
        [UnityTest]
        public IEnumerator IX10p_TwoPlaysAreTwoHeads()
        {
            yield return new EnterPlayMode();
            FeedbackPlayer p = TimelineTesting.Awake("Heads10", TimelineTesting.Item<PositionItem>(2f));
            p.m_Policy = PlayPolicy.Overlap;
            p.m_MaxOverlap = 4;
            yield return null;
            PlaybackHandle first = p.Play();
            float until = Time.realtimeSinceStartup + 0.2f;
            while (Time.realtimeSinceStartup < until)
                yield return null;
            PlaybackHandle second = p.Play();
            yield return null;
            var heads = new List<TimelineModel.Head>();
            TimelineModel.Heads(p, heads);
            double h1 = TimelineTime.ToSeconds(p.Position(first));
            double h2 = TimelineTime.ToSeconds(p.Position(second));
            int records = p.Runtime.RecordCount;
            Object.Destroy(p.gameObject);
            yield return new ExitPlayMode();
            Assert.That(records, Is.EqualTo(4));
            Assert.That(heads.Count, Is.EqualTo(2));
            Assert.That(heads.Select(h => h.Seconds).OrderBy(x => x), Is.EqualTo(new[] { h2, h1 }.OrderBy(x => x)).Within(1e-9));
            Assert.That(h1, Is.GreaterThan(h2), "the first started earlier");
            Assert.That(heads.All(h => !h.OffNominal), Is.True);
        }
    }
}
