using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;
using xpTURN.Feedback.Tests.Extension;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // a user's feedback in an assembly builds do not have — this editor one
    [CustomFeedback("Probe/Editor Only")]
    [Serializable]
    public sealed class EditorOnlyProbe : CustomActionItem
    {
        protected internal override CustomAction CreateAction() => new CustomActionNone();
    }

    // the extension bases through a user's items — an assembly without InternalsVisibleTo (Tests.Extension) — on a test
    // scheduler ticked by hand (0.05 s), the previews on the test's clock
    public sealed class ExtensionTests
    {
        const float Step = 0.05f;
        const double UpdateStep = 1.0 / 120.0;

        static double s_Now;

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<Scene> m_Scenes = new List<Scene>();
        readonly List<string> m_Assets = new List<string>();
        readonly List<string> m_Logs = new List<string>();
        IPreviewRecordSource m_Source;
        Func<double> m_Clock;
        Func<IReadOnlyList<string>, bool> m_Ask;

        void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
                m_Logs.Add(condition);
        }

        [SetUp]
        public void SetUp()
        {
            m_Source = PreviewRecordPaths.Source;
            m_Clock = PreviewClock.Source;
            m_Ask = MissingTypeSaveGuard.Ask;
            PreviewRecordPaths.Source = new PreviewRecordPathTable();
            s_Now = 0;
            PreviewClock.Source = () => s_Now;
            m_Logs.Clear();
            ProbeLog.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            PreviewSession.Current?.Stop();
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    p.ReleaseRuntime();
            }
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            PreviewRecovery.Run();
            PreviewRecordPaths.Source = m_Source;
            PreviewClock.Source = m_Clock;
            MissingTypeSaveGuard.Ask = m_Ask;
            MissingTypeSaveGuard.Forget();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            foreach (Scene s in m_Scenes)
            {
                if (s.IsValid() && s.isLoaded)
                    EditorSceneManager.CloseScene(s, true);
            }
            foreach (string a in m_Assets)
                AssetDatabase.DeleteAsset(a);
            m_Made.Clear();
            m_Schedulers.Clear();
            m_Players.Clear();
            m_Scenes.Clear();
            m_Assets.Clear();
            ProbeLog.Clear();
        }

        static string B(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");

        GameObject Go(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items) => Player(s, 1, items);

        FeedbackPlayer Player(FeedbackScheduler s, int overlap, params FeedbackItem[] items)
        {
            FeedbackPlayer p = Go("Player").AddComponent<FeedbackPlayer>();
            if (overlap > 1)
            {
                p.m_Policy = PlayPolicy.Overlap;
                p.m_MaxOverlap = overlap;
            }
            p.m_Items.AddRange(items);
            p.Bind(s);
            p.EnsureRuntime();
            m_Players.Add(p);
            return p;
        }

        static void Tick(FeedbackScheduler s, int n)
        {
            for (int i = 0; i < n; i++)
                s.Tick(Step);
        }

        static T At<T>(T item, float delay) where T : FeedbackItem
        {
            item.m_Timing.m_Delay = delay;
            return item;
        }

        static PositionItem Keeper(Transform t, float length)
        {
            var keeper = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = t, m_Path = "", m_Anchor = "" }, m_Motion = TweenTo(1f) };
            keeper.m_Timing.m_Duration = length;
            return keeper;
        }

        static TweenMotion TweenTo(float to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = new Vector4(to, to, to, to);
            return new TweenMotion { m_Params = p };
        }

        static string Calls() => string.Join(" | ", ProbeLog.Calls);

        // ---- the user's assembly is no friend of Core — the probes compile against the public surface alone
        [Test]
        public void EX00_TheUsersAssemblyIsNoFriend()
        {
            string outside = typeof(ProbeOneShot).Assembly.GetName().Name;
            Assert.That(outside, Is.EqualTo("xpTURN.Feedback.Tests.Extension"));
            var friends = typeof(FeedbackPlayer).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName).ToList();
            Assert.That(friends, Does.Not.Contain(outside));
        }

        // ---- the registry: the exact registration, else the two bases' adapters — nothing registered for them
        sealed class OverrideBinding : ItemBinding
        {
            internal override bool Prepare(in PrepareContext context) => true;

            internal override void Begin(int record, in BeginContext context)
            {
            }

            internal override void Evaluate(int record, in MotionTime time)
            {
            }

            internal override void End(int record, EndReason reason)
            {
            }
        }

        [Test]
        public void EX01_TheRegistryFallsBackForTheTwoBases()
        {
            int before = FeedbackImplementations.RegisteredTypes.Count;
            Assert.That(FeedbackImplementations.Create(new ProbeOneShot()), Is.InstanceOf<CustomActionBinding>());
            Assert.That(FeedbackImplementations.Create(new ProbeValue()), Is.InstanceOf<CustomEffectBinding>());
            Assert.That(FeedbackImplementations.IsImplemented(typeof(ProbeOneShot)) && FeedbackImplementations.IsImplemented(typeof(ProbeValue)), Is.True);
            Assert.That(FeedbackImplementations.IsImplemented(typeof(UnityEventItem)), Is.True, "an exact registration");
            Assert.That(FeedbackImplementations.IsImplemented(typeof(FeedbackPlayer)), Is.False);
            Assert.That(FeedbackImplementations.RegisteredTypes.Contains(typeof(ProbeOneShot)), Is.False, "the fallback registers nothing");
            Assert.That(FeedbackImplementations.RegisteredTypes.Count, Is.EqualTo(before));
            FeedbackImplementations.Register<ProbeOverridden>(i => new OverrideBinding());
            Assert.That(FeedbackImplementations.Create(new ProbeOverridden()), Is.InstanceOf<OverrideBinding>(), "the exact registration wins");
        }

        // ---- an instant: forward Fire, backward Undo, the executed instant hears Revert (not Hold · Complete)
        [Test]
        public void EX02_AnInstantFiresForwardAndUndoesBackward()
        {
            FeedbackScheduler s = Sched();
            Transform t = Go("Keeper").transform;
            FeedbackPlayer p = Player(s, At(new ProbeOneShot(), 0.2f), Keeper(t, 0.6f));
            p.Play(PlayArgs.Default.WithIntensity(0.5f));
            Tick(s, 6);
            Assert.That(Calls(), Is.EqualTo("Fire s0 i0.5 c1 r0 w0"), "forward, at its place");
            ProbeLog.Clear();
            Tick(s, 20);
            p.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward));
            Tick(s, 20);
            Assert.That(Calls(), Is.EqualTo("Undo s0 r1 w0"), "backward — Undo, not Fire");
            ProbeLog.Clear();
            p.Play();
            Tick(s, 6);
            p.Stop(StopMode.Revert);
            Assert.That(ProbeLog.Calls, Is.EqualTo(new[] { "Fire s0 i1 c1 r0 w0", "End s0 Revert" }), "the executed instant hears the Revert");
            ProbeLog.Clear();
            p.Play();
            Tick(s, 6);
            p.Stop(StopMode.Hold);
            p.Play();
            Tick(s, 6);
            p.Stop(StopMode.Complete);
            Assert.That(ProbeLog.Calls, Is.EqualTo(new[] { "Fire s0 i1 c1 r0 w0", "Fire s0 i1 c1 r0 w0" }), "Hold · Complete come to running items only");
        }

        // ---- a timed act: the length · the order · T both ways · slots · the end reasons · Restore · Release
        [Test]
        public void EX03_ATimedActBeginsUpdatesEnds()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, 2, new ProbeTimed { m_Length = 0.5f });
            Assert.That(p.TotalDuration, Is.EqualTo(0.5f).Within(1e-6f), "Duration is the timeline's length");
            Assert.That(new ProbeTimed().ActsBackward && !new ProbeOneShot().ActsBackward, Is.True, "a timed act runs backward on its own");
            p.Play();
            Tick(s, 14);
            Assert.That(ProbeLog.Calls, Is.EqualTo(new[] { "Prepare n2 p0", "Begin s0 r0", "End s0 Completed" }));
            Assert.That(ProbeLog.Values.Zip(ProbeLog.Values.Skip(1), (a, b) => b >= a).All(x => x), Is.True, "T goes up");
            Assert.That((ProbeLog.Values.First(), ProbeLog.Values.Last()), Is.EqualTo((0f, 1f)));
            Assert.That(ProbeLog.Seconds.Last(), Is.EqualTo(0.5).Within(1e-6), "Elapsed is the occurrence's timeline seconds");
            ProbeLog.Clear();
            p.Play();
            Tick(s, 2);
            p.Play();
            Tick(s, 2);
            Assert.That(ProbeLog.Calls, Is.EqualTo(new[] { "Begin s0 r0", "Begin s1 r0" }), "two overlapping plays — two slots");
            Assert.That(ProbeLog.Slots.Distinct().OrderBy(x => x), Is.EqualTo(new[] { 0, 1 }));
            p.Stop(StopMode.Hold);
            ProbeLog.Clear();
            foreach (StopMode mode in new[] { StopMode.Hold, StopMode.Revert, StopMode.Complete })
            {
                p.Play();
                Tick(s, 3);
                p.Stop(mode);
            }
            Assert.That(ProbeLog.Calls.Where(c => c.StartsWith("End", StringComparison.Ordinal)), Is.EqualTo(new[] { "End s0 Hold", "End s0 Revert", "End s0 Complete" }));
            ProbeLog.Clear();
            p.Play();
            Tick(s, 3);
            p.Restore();
            Assert.That(ProbeLog.Calls, Is.EqualTo(new[] { "Begin s0 r0", "End s0 Hold", "Restore" }), "a Restore stops with Hold first");
            ProbeLog.Clear();
            p.Play();
            Tick(s, 3);
            p.ReleaseRuntime();
            Assert.That(ProbeLog.Calls, Is.EqualTo(new[] { "Begin s0 r0", "End s0 Hold", "Release" }), "a release stops with Hold, then Release");
            ProbeLog.Clear();
            FeedbackPlayer back = Player(s, new ProbeTimed { m_Length = 0.5f });
            ProbeLog.Clear();
            back.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward));
            Tick(s, 14);
            Assert.That(ProbeLog.Calls.First(), Is.EqualTo("Begin s0 r1"));
            Assert.That((ProbeLog.Values.First(), ProbeLog.Values.Last()), Is.EqualTo((1f, 0f)), "backward — T from 1 to 0");
        }

        // ---- Swept: a Seek's and a Skip's sweeps both ways, and a Seek deferred inside a play's events — not the frames
        [Test]
        public void EX04_SweepsAreMarked()
        {
            FeedbackScheduler s = Sched();
            Transform t = Go("Keeper").transform;
            FeedbackPlayer p = Player(s, At(new ProbeOneShot(), 0.2f), Keeper(t, 0.8f));
            p.Play();
            Assert.That(p.Seek(0.5f), Is.True);
            Assert.That(p.Seek(0.1f), Is.True);
            Tick(s, 4);
            p.Stop(StopMode.Hold);
            p.Play();
            Tick(s, 1);
            p.Skip();
            p.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward));
            Tick(s, 1);
            p.Skip();
            Assert.That(ProbeLog.Calls, Is.EqualTo(new[] { "Fire s0 i1 c1 r0 w1", "Undo s0 r1 w1", "Fire s0 i1 c1 r0 w0", "Fire s0 i1 c1 r0 w1", "Undo s0 r1 w1" }));
            ProbeLog.Clear();
            var seeker = At(new UnityEventItem(), 0.1f);
            FeedbackPlayer q = Player(s, seeker, At(new ProbeOneShot(), 0.4f), Keeper(Go("Keeper2").transform, 0.8f));
            seeker.m_Event.AddListener(() => q.Seek(0.6f));
            q.Play();
            Tick(s, 4);
            Assert.That(ProbeLog.Calls, Is.EqualTo(new[] { "Fire s0 i1 c1 r0 w1" }), "a Seek inside the events runs at the pass's end — a sweep");
        }

        // ---- the effect is a Custom Float Event's value: five motions both ways, bit for bit · the base back once on Revert
        static FeedbackMotion MotionOf(int k)
        {
            switch (k)
            {
                case 0:
                    return TweenTo(10f);
                case 1:
                    return new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(2f, 2f, 2f, 0f), m_Vibrato = 2f, m_Decay = 0.5f } };
                case 2:
                    return new ShakeMotion();
                case 3:
                    return new CurveMotion { m_Params = new CurveParams { m_Curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f), m_RemapZero = Vector4.zero, m_RemapOne = new Vector4(3f, 0f, 0f, 0f) } };
                default:
                    return new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(1.5f, 0f, 0f, 0f), m_Frequency = 2f } };
            }
        }

        [Test]
        public void EX05_TheEffectIsTheFloatEventsValue()
        {
            var bad = new List<string>();
            foreach (bool backward in new[] { false, true })
            {
                for (int k = 0; k < 5; k++)
                {
                    FeedbackScheduler s = Sched();
                    var listener = Go("Listener").AddComponent<FeedbackTestListener>();
                    var ev = new CustomFloatEventItem { m_Base = 2f, m_Motion = MotionOf(k) };
                    ev.m_Timing.m_Duration = 0.5f;
                    ev.m_Event.AddListener(listener.Take);
                    var mine = new ProbeValue { m_Base = 2f, m_Motion = MotionOf(k) };
                    mine.m_Timing.m_Duration = 0.5f;
                    PlayArgs args = PlayArgs.Default.WithSeed(7u).WithDirection(backward ? PlayerDirection.Backward : PlayerDirection.Forward);
                    Player(s, ev).Play(in args);
                    Player(s, mine).Play(in args);
                    Tick(s, 14);
                    string a = string.Join(",", listener.Values.Select(B));
                    string b = string.Join(",", ProbeLog.Values.Select(B));
                    if (a != b || listener.Values.Count < 5)
                        bad.Add($"{MotionOf(k).Kind} {(backward ? "back" : "forward")}: {listener.Values.Count} vs {ProbeLog.Values.Count}");
                    ProbeLog.Clear();
                }
            }
            Assert.That(bad, Is.Empty);
            FeedbackScheduler r = Sched();
            var revert = new ProbeValue { m_Base = 3f, m_Motion = TweenTo(9f) };
            revert.m_Timing.m_Duration = 1f;
            FeedbackPlayer rp = Player(r, revert);
            rp.Play();
            Tick(r, 4);
            rp.Stop(StopMode.Revert);
            Assert.That((ProbeLog.Values.Last(), ProbeLog.Slots.Last()), Is.EqualTo((3f, -1)), "Revert — the base once, slot −1");
            FeedbackPlayer sp = Player(Sched(), new ProbeValue { m_Motion = new SpringMotion() });
            Assert.That(sp.Runtime.Binding(0), Is.Null.Or.Property("SkipReason").Not.Null, "a motion outside the five is refused");
        }

        // ---- categories: declared once per type · the override wins · Flash reads as None · off leaves new plays out · the
        // scale while running · a Haptics effect stops when its category is turned off
        [Test]
        public void EX06_TheCategoryGates()
        {
            Assert.That(new ProbeHaptic().EffectiveCategory, Is.EqualTo(FeedbackCategory.Haptics));
            Assert.That(new ProbeFlash().EffectiveCategory, Is.EqualTo(FeedbackCategory.None), "a declared Flash reads as None");
            var over = new ProbeHaptic();
            over.m_Category = new CategoryOverride { m_Override = true, m_Category = FeedbackCategory.UI };
            Assert.That(over.EffectiveCategory, Is.EqualTo(FeedbackCategory.UI), "the override wins");
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, new ProbeHaptic());
            s.Context.Categories.SetEnabled(FeedbackCategory.Haptics, false);
            p.Play();
            Tick(s, 2);
            Assert.That(ProbeLog.Calls, Is.Empty, "off — a new play leaves it out");
            s.Context.Categories.SetEnabled(FeedbackCategory.Haptics, true);
            s.Context.Categories.SetScale(FeedbackCategory.Haptics, 0.5f);
            p.Play();
            Tick(s, 2);
            Assert.That(ProbeLog.Calls, Is.EqualTo(new[] { "Haptic c0.5" }));
            ProbeLog.Clear();
            FeedbackPlayer timed = Player(s, new ProbeHapticTimed());
            timed.Play();
            Tick(s, 3);
            s.Context.Categories.SetEnabled(FeedbackCategory.Haptics, false);
            Tick(s, 3);
            Assert.That((ProbeLog.Values.First(), ProbeLog.Values.Last()), Is.EqualTo((0.5f, 0f)), "the scale while running — 0 once off");
            timed.Stop(StopMode.Hold);
            s.Context.Categories.SetEnabled(FeedbackCategory.Haptics, true);
            ProbeLog.Clear();
            var tween = new ProbeHapticValue { m_Base = 1f, m_Motion = TweenTo(5f) };
            tween.m_Timing.m_Duration = 1f;
            FeedbackPlayer hp = Player(s, tween);
            hp.Play();
            Tick(s, 3);
            int running = ProbeLog.Values.Count;
            s.Context.Categories.SetEnabled(FeedbackCategory.Haptics, false);
            Tick(s, 4);
            Assert.That(ProbeLog.Values.Count, Is.EqualTo(running + 1), "off — one call more");
            Assert.That((ProbeLog.Values.Last(), ProbeLog.Slots.Last()), Is.EqualTo((1f, 0)), "the base, on the slot");
            s.Context.Categories.SetEnabled(FeedbackCategory.Haptics, true);
            s.Context.Categories.SetScale(FeedbackCategory.Haptics, 1f);
        }

        // ---- an offset effect's value takes the category's scale
        [Test]
        public void EX06b_TheEffectTakesTheCategoryScale()
        {
            FeedbackScheduler s = Sched();
            s.Context.Categories.SetScale(FeedbackCategory.Haptics, 0.5f);
            PunchMotion Punch() => new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(2f, 0f, 0f, 0f), m_Vibrato = 1f, m_Decay = 0f } };
            var half = new ProbeHapticValue { m_Base = 1f, m_Motion = Punch() };
            half.m_Timing.m_Duration = 0.5f;
            var full = new ProbeValue { m_Base = 1f, m_Motion = Punch() };
            full.m_Timing.m_Duration = 0.5f;
            Player(s, half).Play(PlayArgs.Default.WithSeed(3u));
            Tick(s, 3);
            float h = ProbeLog.Values.Last() - 1f;
            ProbeLog.Clear();
            Player(s, full).Play(PlayArgs.Default.WithSeed(3u));
            Tick(s, 3);
            float f = ProbeLog.Values.Last() - 1f;
            s.Context.Categories.SetScale(FeedbackCategory.Haptics, 1f);
            Assert.That(f != 0f && Mathf.Abs(h - f * 0.5f) < 1e-5f, Is.True, $"half {h} · full {f}");
        }

        // ---- the user's exceptions: logged once a slot, the play goes on and completes, the slot's End still comes · a
        // Prepare that throws goes the runtime's way (logged, then Release) · one that refuses leaves the item out
        sealed class EndListener : IPlaybackListener
        {
            internal readonly List<PlaybackEnd> Ends = new List<PlaybackEnd>();

            public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end) => Ends.Add(end);
        }

        [Test]
        public void EX07_UserExceptionsAreContained()
        {
            LogAssert.ignoreFailingMessages = true;
            FeedbackScheduler s = Sched();
            Transform t = Go("Keeper").transform;
            FeedbackPlayer p = Player(s, new ProbeTimed { m_Length = 0.5f }, At(new ProbeOneShot(), 0.1f), Keeper(t, 0.4f));
            var ends = new EndListener();
            ProbeLog.Clear();
            ProbeLog.Throw = true;
            m_Logs.Clear();
            p.Play(PlayArgs.Default.WithListener(ends));
            Tick(s, 14);
            Assert.That(ProbeLog.Values, Is.Empty, "no Update for the slot whose Begin threw");
            Assert.That(ProbeLog.Calls.Last(), Is.EqualTo("End s0 Completed"), "its End comes");
            Assert.That(ProbeLog.Calls.Any(c => c.StartsWith("Fire", StringComparison.Ordinal)), Is.True, "the other item ran");
            Assert.That(t.localPosition.x, Is.EqualTo(1f), "the keeper reached its end");
            Assert.That(ends.Ends, Is.EqualTo(new[] { PlaybackEnd.Completed }));
            Assert.That(m_Logs.Count, Is.EqualTo(2), "one log for the begin, one for the fire");
            ProbeLog.Clear();
            ProbeLog.Throw = true;
            ProbeLog.Refuse = true;
            m_Logs.Clear();
            FeedbackPlayer thrown = Player(s, new ProbeTimed());
            Assert.That(ProbeLog.Calls, Is.EqualTo(new[] { "Prepare n1 p0", "Release" }), "a throwing Prepare — Release follows");
            Assert.That(m_Logs.Count(l => l.Contains("probe prepare")), Is.EqualTo(1), "logged with its exception");
            Assert.That(thrown.Runtime.Binding(0), Is.Null, "the item is left out");
            ProbeLog.Clear();
            ProbeLog.Refuse = true;
            FeedbackPlayer refused = Player(s, new ProbeTimed());
            Assert.That(ProbeLog.Calls, Is.EqualTo(new[] { "Prepare n1 p0", "Release" }), "a refusing Prepare — Release follows");
            Assert.That(refused.Runtime.Binding(0), Is.Null);
        }

        // ---- an Undo with no Fire before it: an instant turned by its Play Direction in a forward play
        [Test]
        public void EX07b_AnUndoWithoutAFire()
        {
            FeedbackScheduler s = Sched();
            var turned = At(new ProbeOneShot(), 0.1f);
            turned.m_PlayDirection = ItemPlayDirection.Opposite;
            FeedbackPlayer p = Player(s, turned, Keeper(Go("Keeper").transform, 0.4f));
            p.Play();
            Tick(s, 10);
            Assert.That(ProbeLog.Calls, Is.EqualTo(new[] { "Undo s0 r1 w0" }));
        }

        // ---- the preview: Skip calls nothing (not even CreateAction) and counts · Run runs with InPreview
        Scene OpenScene(string name)
        {
            string path = "Assets/ExtensionTests." + name + ".unity";
            File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Scene s = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            m_Scenes.Add(s);
            m_Assets.Add(path);
            return s;
        }

        static void Pump(int updates)
        {
            for (int i = 0; i < updates; i++)
            {
                s_Now += UpdateStep;
                PreviewSession.Current?.Update();
            }
        }

        [Test]
        public void EX08_ThePreviewSkipsUnlessTheTypeRuns()
        {
            Scene scene = OpenScene("ex08");
            SceneManager.SetActiveScene(scene);
            var player = new GameObject("ExtensionPlayer").AddComponent<FeedbackPlayer>();
            SceneManager.MoveGameObjectToScene(player.gameObject, scene);
            player.m_Items.Add(new ProbeTimed { m_Length = 0.5f });
            player.m_Items.Add(new ProbeRun());
            var keep = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = TweenTo(3f) };
            keep.m_Timing.m_Duration = 2f;
            player.m_Items.Add(keep);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(30);
            FeedbackContext context = PreviewSession.Current.Scheduler.Context;
            (int skipped, int ran) = (context.SkippedCustom, context.RanCustom);
            PreviewSession.Current.Stop();
            Assert.That(ProbeLog.Calls, Is.EqualTo(new[] { "Run prepare p1", "Run begin" }), "Skip calls nothing · Run runs with InPreview");
            Assert.That((skipped, ran), Is.EqualTo((1, 1)), "one skipped occurrence · one prepared Run type");
        }

        // ---- a found target that is destroyed ends the item (Aborted), nothing after
        [Test]
        public void EX09_ADestroyedTargetEndsTheItem()
        {
            FeedbackScheduler s = Sched();
            GameObject target = Go("Target");
            var item = new ProbeTargetValue { m_Motion = TweenTo(4f), m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target.transform, m_Path = "", m_Anchor = "" } };
            item.m_Timing.m_Duration = 1f;
            FeedbackPlayer p = Player(s, item, Keeper(Go("Keeper").transform, 1f));
            p.Play();
            Tick(s, 3);
            Assert.That(target.transform.localPosition.x, Is.GreaterThan(0f));
            Object.DestroyImmediate(target);
            Tick(s, 4);
            Assert.That(ProbeLog.Calls, Is.EqualTo(new[] { "End s0 Aborted" }));
        }

        // ---- a user's effect in a motion driver's loop: no player, one slot, no end
        [Test]
        public void EX10_AnEffectInAMotionDriver()
        {
            FeedbackScheduler s = Sched();
            GameObject go = Go("Driver");
            go.SetActive(false);
            var driver = go.AddComponent<MotionDriver>();
            driver.m_Item = new ProbeValue { m_Base = 1f, m_Motion = MotionOf(4) };
            driver.m_Mode = MotionDriverMode.Loop;
            driver.Bind(s);
            go.SetActive(true);
            Tick(s, 10);
            Assert.That(ProbeLog.Values.Count, Is.GreaterThanOrEqualTo(8), "the loop calls Apply every frame");
            Assert.That(ProbeLog.Slots.Distinct(), Is.EqualTo(new[] { 0 }));
            Assert.That(ProbeLog.Values.Distinct().Count(), Is.GreaterThan(2), "an oscillation");
            driver.m_Mode = MotionDriverMode.Follow;
            driver.m_Item = new ProbeValue { m_Motion = new SpringMotion() };
            LogAssert.ignoreFailingMessages = true;
            Assert.That(FeedbackImplementations.Create(driver.m_Item), Is.InstanceOf<CustomEffectBinding>(), "the driver's binding is the adapter too");
        }

        // ---- the Add menu: the attribute's path · Project/<name> without one · a shared path gets the type's name (the
        // package's leaf keeps its own) · registered · an effect's five leaves · a new action's empty target is Self
        [Test]
        public void EX12_TheCatalogListsUserFeedbacks()
        {
            List<CatalogLeaf> leaves = FeedbackCatalog.Build(FeedbackCatalog.Sources());
            CatalogLeaf one = leaves.Single(l => l.Type == typeof(ProbeOneShot));
            Assert.That((one.Path, one.Registered, one.Custom), Is.EqualTo(("Probe/One Shot", true, true)));
            Assert.That(leaves.Where(l => l.Type == typeof(ProbeValue)).Select(l => l.Path), Is.EqualTo(new[] { "Probe/Value/Curve", "Probe/Value/Oscillate", "Probe/Value/Punch", "Probe/Value/Shake", "Probe/Value/Tween" }));
            Assert.That(leaves.Single(l => l.Type == typeof(ProbeNoAttribute)).Path, Is.EqualTo("Project/Probe No Attribute"));
            Assert.That(leaves.Single(l => l.Type == typeof(ProbeClash)).Path, Is.EqualTo("Flow/Unity Event (ProbeClash)"));
            Assert.That(leaves.Count(l => l.Path == "Flow/Unity Event"), Is.EqualTo(1), "the package's leaf keeps its path");
            Assert.That(leaves.Where(l => l.Type == typeof(ProbeTwinA) || l.Type == typeof(ProbeTwinB)).Select(l => l.Path).OrderBy(x => x, StringComparer.Ordinal),
                Is.EqualTo(new[] { "Probe/Twin (ProbeTwinA)", "Probe/Twin (ProbeTwinB)" }), "two user types on one path");
            Assert.That(leaves.Where(l => l.Custom).All(l => l.Registered), Is.True);
            Assert.That(leaves.Where(l => !l.Custom).All(l => !FeedbackCatalog.IsCustom(l.Type)), Is.True);
            Assert.That(ItemWords.Name(typeof(ProbeOneShot)), Is.EqualTo("One Shot"));
            Assert.That(ItemWords.Name(typeof(ProbeNoAttribute)), Is.EqualTo("Probe No Attribute"));
            var made = (ProbeOneShot)ItemFactory.Create(one);
            Assert.That(made.m_Target.m_Mode, Is.EqualTo(TargetRefMode.Self), "an empty Direct target made Self");
            var global = (ProbeNamedFields)ItemFactory.Create(leaves.Single(l => l.Type == typeof(ProbeNamedFields)));
            Assert.That(global.m_Target.m_Mode, Is.EqualTo(TargetRefMode.Direct), "a global one keeps it");
            Assert.That(MotionDriverEditor.Leaves(leaves).Where(l => l.Custom).Select(l => l.Type).Distinct(), Does.Contain(typeof(ProbeValue)), "a user effect in the driver's menu");
        }

        // ---- the body's fields: a user's own fields whatever their names · a user action's intensity fields · a user effect
        // without the direct-write field and with the base's label · a global hides its target
        [Test]
        public void EX13_TheInspectorShowsUserFields()
        {
            FeedbackPlayer p = Go("Inspector").AddComponent<FeedbackPlayer>();
            p.m_Items.Add(new ProbeOneShot());
            p.m_Items.Add(new ProbeTargetValue { m_Motion = TweenTo(1f) });
            p.m_Items.Add(new ProbeNamedFields());
            p.m_Items.Add(new ProbeValue { m_Motion = TweenTo(1f) });
            var so = new SerializedObject(p);
            SerializedProperty items = so.FindProperty("m_Items");
            List<string> action = ItemBody.FieldsOf(items.GetArrayElementAtIndex(0), p.m_Items[0]);
            List<string> effect = ItemBody.FieldsOf(items.GetArrayElementAtIndex(1), p.m_Items[1]);
            List<string> named = ItemBody.FieldsOf(items.GetArrayElementAtIndex(2), p.m_Items[2]);
            List<string> global = ItemBody.FieldsOf(items.GetArrayElementAtIndex(3), p.m_Items[3]);
            Assert.That(action, Does.Contain("m_Volume").And.Contain("m_Target").And.Contain("m_Intensity").And.Contain("m_Random.m_RandomizeOutput"));
            Assert.That(effect, Does.Contain("m_Base").And.Contain("m_Target").And.Not.Contain("m_DirectWrite"));
            Assert.That(ItemRules.Label(p.m_Items[1], "m_Base"), Is.EqualTo("Base (offset motions)"));
            Assert.That(named, Does.Contain("m_Interpolation").And.Contain("m_Min").And.Contain("m_DirectWrite").And.Not.Contain("m_Target"), "the user's names · Global");
            Assert.That(global, Does.Not.Contain("m_Target"));
        }

        // ---- the rules: a user's feedback is installed (B07) · its Requires reaches A01 · a declared Flash (N05)
        [Test]
        public void EX15_TheRulesSeeUserFeedbacks()
        {
            GameObject host = Go("Rules");
            GameObject plain = Go("Plain");
            FeedbackPlayer p = host.AddComponent<FeedbackPlayer>();
            p.m_Items.Add(new ProbeOneShot { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" } });
            p.m_Items.Add(new ProbeValue { m_Motion = TweenTo(1f) });
            p.m_Items.Add(new ProbeLightAction { m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = plain, m_Path = "", m_Anchor = "" } });
            p.m_Items.Add(new ProbeFlash { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" } });
            List<RuleResult> results = FeedbackValidator.Run(p);
            Assert.That(results.Where(r => r.Id == "FB-B07"), Is.Empty, "a user's feedback is not 'not installed'");
            Assert.That(results.Where(r => r.Id == "FB-A01").Select(r => r.Item), Is.EqualTo(new[] { 2 }), "the Light the action requires");
            Assert.That(results.Where(r => r.Id == "FB-N05").Select(r => r.Item), Is.EqualTo(new[] { 3 }));
            Assert.That(TargetChecks.Requires(typeof(ProbeTargetValue)), Is.EqualTo(typeof(Transform)));
            Assert.That(TargetChecks.Global(typeof(ProbeValue)) && !TargetChecks.Global(typeof(ProbeTargetValue)), Is.True);
            Light light = Go("Light").AddComponent<Light>();
            List<CatalogLeaf> leaves = FeedbackCatalog.Build(FeedbackCatalog.Sources());
            Assert.That(ComponentDrop.Suggest(leaves, light).Select(l => l.Type), Does.Contain(typeof(ProbeLightAction)), "a dropped Light suggests the action that requires one");
            var dropped = (ProbeLightAction)ComponentDrop.Create(leaves.Single(l => l.Type == typeof(ProbeLightAction)), light);
            Assert.That((dropped.m_Target.m_Mode, dropped.m_Target.m_Object), Is.EqualTo((TargetRefMode.Direct, (Object)light)), "the drop's target");
        }

        // ---- the preview's notices: the skipped calls · the types it ran
        [Test]
        public void EX16_ThePreviewTellsWhatItSkippedAndRan()
        {
            Scene scene = OpenScene("ex16");
            SceneManager.SetActiveScene(scene);
            var player = new GameObject("ExtensionPlayer").AddComponent<FeedbackPlayer>();
            SceneManager.MoveGameObjectToScene(player.gameObject, scene);
            player.m_Items.Add(new ProbeTimed { m_Length = 0.5f });
            player.m_Items.Add(new ProbeRun());
            var keep = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = TweenTo(3f) };
            keep.m_Timing.m_Duration = 2f;
            player.m_Items.Add(keep);
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(30);
            var notices = new List<PreviewNotice>();
            PreviewNotices.Collect(PreviewSession.Current, notices);
            PreviewSession.Current.Stop();
            Assert.That(notices.Single(n => n.Kind == PreviewNoticeKind.CustomSkipped).Text, Is.EqualTo("1 custom feedback call(s) not made in the preview."));
            Assert.That(notices.Single(n => n.Kind == PreviewNoticeKind.CustomUnrecorded).Text, Is.EqualTo("1 custom feedback(s) run in the preview — what their code changes does not come back."));
        }

        // ---- Keep Play Mode Changes carries a user's fields — numbers, text, vectors, a color, a struct, an asset reference, a
        // layer mask, a curve, an enum — nothing unknown, nothing dropped
        [Test]
        public void EX17_KeepChangesCarriesUserFields()
        {
            string presetPath = AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:FeedbackPreset", new[] { "Packages/com.xpturn.feedback/Presets" }).First());
            Object asset = AssetDatabase.LoadAssetAtPath<FeedbackPreset>(presetPath);
            Assert.That(asset, Is.Not.Null, "a package preset to refer to");
            FeedbackPlayer k = Go("EX17Keep").AddComponent<FeedbackPlayer>();
            var item = new ProbeFieldKinds
            {
                m_Gain = 0.25f, m_Count = 3, m_Event = "event:/hit", m_Offset = new Vector3(1f, 2f, 3f), m_Tint = new Color(0.1f, 0.2f, 0.3f, 0.4f),
                m_Ref = new ProbeEventRef { m_A = 7, m_B = -9, m_Path = "bank:/sfx" }, m_Asset = asset, m_Mask = 1 << 5,
                m_Curve = AnimationCurve.Constant(0f, 1f, 0.5f), m_Kind = FeedbackCategory.Haptics,
            };
            k.m_Items.Add(item);
            FeedbackPlayer kept = Go("EX17Into").AddComponent<FeedbackPlayer>();
            PlayModeRecord.Apply(kept, PlayModeRecord.Capture(k, false, out int unknown), out PlayModeRecord.Stats stats);
            Assert.That((unknown, stats.Dropped), Is.EqualTo((0, 0)));
            var back = (ProbeFieldKinds)kept.m_Items.Single();
            Assert.That((back.m_Gain, back.m_Count, back.m_Event, back.m_Offset, back.m_Tint), Is.EqualTo((0.25f, 3, "event:/hit", new Vector3(1f, 2f, 3f), new Color(0.1f, 0.2f, 0.3f, 0.4f))));
            Assert.That((back.m_Ref.m_A, back.m_Ref.m_B, back.m_Ref.m_Path, back.m_Asset, back.m_Mask.value, back.m_Curve.Evaluate(0.5f), back.m_Kind),
                Is.EqualTo((7, -9, "bank:/sfx", asset, 1 << 5, 0.5f, FeedbackCategory.Haptics)));
        }

        // ---- a preset: Save As makes a user action's Direct targets on the player · under it · on its parent Self · Child ·
        // Parent (an outside one cleared, counted) · FB-A09 and its fix see a user action's Direct target
        [Test]
        public void EX18_APresetMakesAUserActionsTargetsRelative()
        {
            GameObject parent = Go("EX18Parent");
            var host = new GameObject("EX18Player");
            host.transform.SetParent(parent.transform, false);
            var kid = new GameObject("Kid");
            kid.transform.SetParent(host.transform, false);
            GameObject outside = Go("EX18Outside");
            FeedbackPlayer p = host.AddComponent<FeedbackPlayer>();
            foreach (Object o in new Object[] { host.transform, kid.transform, parent.transform, outside.transform })
                p.m_Items.Add(new ProbeTimed { m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" } });
            string path = "Assets/ExtensionTests.ex18.asset";
            m_Assets.Add(path);
            FeedbackPreset preset = PresetOps.SaveAs(p, path, out int cleared);
            Assert.That(preset.m_Items.Select(i => ((ProbeTimed)i).m_Target).Select(t => t.m_Mode + (t.m_Mode == TargetRefMode.Child ? "(" + t.m_Path + ")" : "")),
                Is.EqualTo(new[] { "Self", "Child(Kid)", "Parent", "Direct" }));
            Assert.That(cleared, Is.EqualTo(1), "the outside one");
            RuleResult a09 = FeedbackValidator.Run(preset).Single(r => r.Id == "FB-A09");
            Assert.That(a09.Item, Is.EqualTo(3), "the Direct one left");
            FeedbackValidator.Apply(a09, a09.Fixes[0]);
            Assert.That(((ProbeTimed)preset.m_Items[3]).m_Target.m_Mode, Is.EqualTo(TargetRefMode.Self), "the fix");
        }

        // ---- a user's type whose parameterless constructor is not public: the Add menu builds and makes it, a copy is its own
        [Test]
        public void EX19_AHiddenConstructorStillBuildsAndCopies()
        {
            List<CatalogLeaf> leaves = FeedbackCatalog.Build(FeedbackCatalog.Sources());
            Assert.That(leaves.Where(l => l.Type == typeof(ProbeHiddenEffect)).Select(l => l.Path), Does.Contain("Probe/Hidden Effect/Tween"), "an effect's traits read");
            Assert.That(ItemFactory.Create(leaves.Single(l => l.Type == typeof(ProbeHiddenAction))), Is.InstanceOf<ProbeHiddenAction>());
            Assert.That(ItemFactory.Create(leaves.First(l => l.Type == typeof(ProbeHiddenEffect))), Is.InstanceOf<ProbeHiddenEffect>());
            ProbeHiddenAction source = ProbeHiddenAction.Make();
            source.m_Gain = 6f;
            var copy = (ProbeHiddenAction)ItemCopier.Copy(source);
            Assert.That(copy, Is.Not.SameAs(source));
            Assert.That(copy.m_Gain, Is.EqualTo(6f));
        }

        // ---- a user's type gone or unreadable: a preset of [Wait a, a user's item, Wait c] read again from its file with the
        // user item's type line rewritten
        const string UserAsm = ", asm: xpTURN.Feedback.Tests.Extension}";
        const string GoneAsm = ", asm: NoSuch.UserAssembly}";

        FeedbackPreset UserPreset(string name, Func<string, string> rewrite)
        {
            string path = "Assets/ExtensionTests." + name + ".asset";
            m_Assets.Add(path);
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            preset.m_Items.Add(new WaitItem { m_Label = "a" });
            preset.m_Items.Add(new ProbeFieldKinds { m_Gain = 2.5f, m_Label = "user" });
            preset.m_Items.Add(new WaitItem { m_Label = "c" });
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssets();
            return Reread(path, rewrite);
        }

        static FeedbackPreset Reread(string path, Func<string, string> rewrite)
        {
            Resources.UnloadAsset(AssetDatabase.LoadAssetAtPath<FeedbackPreset>(path));
            string full = Path.GetFullPath(path);
            File.WriteAllText(full, rewrite(File.ReadAllText(full)));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<FeedbackPreset>(path);
        }

        static void Relabel(Object owner, string label)
        {
            var so = new SerializedObject(owner);
            so.FindProperty("m_Items.Array.data[0].m_Label").stringValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---- the user's assembly gone (a file read with the type already missing): the list is locked · FB-B01 says to
        // restore the type before a save · a save keeps the item's data · the type back brings it back
        [Test]
        public void EX20_AMissingUserAssemblyLocksAndKeeps()
        {
            MissingTypeSaveGuard.Ask = _ => true;
            FeedbackPreset preset = UserPreset("ex20", t => t.Replace(UserAsm, GoneAsm));
            Assert.That(preset.m_Items.Select(i => i?.m_Label), Is.EqualTo(new[] { "a", null, "c" }));
            Assert.That((MissingTypes.Has(preset), ItemListEdits.Locked(preset)), Is.EqualTo((true, true)));
            RuleResult b01 = FeedbackValidator.Run(preset).Single(r => r.Id == "FB-B01");
            Assert.That((b01.Item, b01.Message.Contains("before you save")), Is.EqualTo((1, true)));
            Relabel(preset, "a2");
            AssetDatabase.SaveAssets();
            string path = AssetDatabase.GetAssetPath(preset);
            Assert.That(File.ReadAllText(path), Does.Contain("m_Label: a2").And.Contain("m_Gain: 2.5"), "a save keeps the data");
            preset = Reread(path, t => t.Replace(GoneAsm, UserAsm));
            Assert.That((preset.m_Items[0].m_Label, (preset.m_Items[1] as ProbeFieldKinds)?.m_Gain), Is.EqualTo(("a2", (float?)2.5f)), "the type back");
        }

        // ---- a user's type made abstract or moved off its base: the reload's check names it · a slot of such a type reads empty
        // without a missing type — FB-B02 names the case
        [Test]
        public void EX21_AnUnreadableUserTypeIsNamed()
        {
            static string Name(Type t) => t.FullName + ", " + t.Assembly.GetName().Name;
            Assert.That(CustomTypeWatch.Unreadable(new[] { Name(typeof(ProbeOneShot)), Name(typeof(CustomActionItem)), Name(typeof(ProbeLog)), "Gone.Type, Gone.Assembly" }, CustomTypeWatch.Resolve),
                Is.EqualTo(new[] { "xpTURN.Feedback.CustomActionItem is abstract", "xpTURN.Feedback.Tests.Extension.ProbeLog is no longer a feedback" }));
            List<string> current = CustomTypeWatch.Current();
            Assert.That(current, Does.Contain(Name(typeof(ProbeOneShot))).And.Contain(Name(typeof(ProbeValue))).And.Contain(Name(typeof(EditorOnlyProbe))));
            Assert.That(current, Does.Not.Contain(Name(typeof(CustomActionItem))).And.Not.Contain(Name(typeof(WaitItem))), "a list's user types only");
            Assert.That(File.ReadAllLines(CustomTypeWatch.NamesPath), Does.Contain(Name(typeof(ProbeOneShot))), "the last reload's names");
            LogAssert.ignoreFailingMessages = true;
            FeedbackPreset preset = UserPreset("ex21", t => t.Replace("class: ProbeFieldKinds, ns: xpTURN.Feedback.Tests.Extension" + UserAsm, "class: CustomActionItem, ns: xpTURN.Feedback, asm: xpTURN.Feedback}"));
            Assert.That((preset.m_Items[1], MissingTypes.Has(preset)), Is.EqualTo(((FeedbackItem)null, false)), "an empty slot, no missing type");
            Assert.That(FeedbackValidator.Run(preset).Single(r => r.Id == "FB-B02").Message, Does.Contain("made abstract or left its base"));
        }

        // ---- a save that can drop items of a missing type asks first: Don't Save keeps the file and the object dirty, and the
        // answer holds for the path until it is forgotten · Save writes · nothing asked without a missing type · a loaded scene
        // and a prefab asset are seen · a batch run's own answer saves and says so
        [Test]
        public void EX22_ASaveThatCanDropAsksFirst()
        {
            var asked = new List<string>();
            bool answer = false;
            MissingTypeSaveGuard.Ask = paths =>
            {
                asked.AddRange(paths);
                return answer;
            };
            FeedbackPreset preset = UserPreset("ex22", t => t.Replace(UserAsm, GoneAsm));
            string path = AssetDatabase.GetAssetPath(preset);
            Relabel(preset, "a2");
            AssetDatabase.SaveAssets();
            Assert.That(asked, Is.EqualTo(new[] { path }));
            Assert.That((File.ReadAllText(path).Contains("m_Label: a2"), EditorUtility.IsDirty(preset)), Is.EqualTo((false, true)), "Don't Save");
            answer = true;
            AssetDatabase.SaveAssets();
            Assert.That((asked.Count, File.ReadAllText(path).Contains("m_Label: a2")), Is.EqualTo((1, false)), "the answer holds");
            MissingTypeSaveGuard.Forget();
            AssetDatabase.SaveAssets();
            Assert.That((asked.Count, File.ReadAllText(path).Contains("m_Label: a2")), Is.EqualTo((2, true)), "Save");
            FeedbackPreset plain = UserPreset("ex22b", t => t);
            Relabel(plain, "b2");
            AssetDatabase.SaveAssets();
            Assert.That((asked.Count, MissingTypeSaveGuard.Holds(AssetDatabase.GetAssetPath(plain))), Is.EqualTo((2, false)), "nothing to ask");

            string scenePath = "Assets/ExtensionTests.ex22.unity";
            m_Assets.Add(scenePath);
            InspectorTesting.MissingScene(scenePath);
            m_Scenes.Add(EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive));
            Scene clean = OpenScene("ex22c");
            Assert.That((MissingTypeSaveGuard.Holds(scenePath), MissingTypeSaveGuard.Holds(clean.path)), Is.EqualTo((true, false)), "a loaded scene");
            string prefabPath = "Assets/ExtensionTests.ex22.prefab";
            m_Assets.Add(prefabPath);
            GameObject src = Go("EX22Prefab");
            src.AddComponent<FeedbackPlayer>().m_Items.Add(new ProbeFieldKinds { m_Gain = 2.5f });
            PrefabUtility.SaveAsPrefabAsset(src, prefabPath);
            File.WriteAllText(Path.GetFullPath(prefabPath), File.ReadAllText(Path.GetFullPath(prefabPath)).Replace(UserAsm, GoneAsm));
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            Assert.That(MissingTypeSaveGuard.Holds(prefabPath), Is.True, "a prefab asset");

            if (!Application.isBatchMode)
                return;
            MissingTypeSaveGuard.Ask = m_Ask;
            MissingTypeSaveGuard.Forget();
            Relabel(preset, "a3");
            LogAssert.Expect(LogType.Warning, MissingTypeSaveGuard.BatchWarning + path);
            AssetDatabase.SaveAssets();
            Assert.That(File.ReadAllText(path), Does.Contain("m_Label: a3"), "a batch run saves");
        }

        // ---- a user's feedback in an assembly builds do not have (this editor one): FB-B09 · the Add menu's leaf says so; a test
        // assembly that players have and a precompiled one are not flagged
        [Test]
        public void EX23_AnAssemblyBuildsLackIsFlagged()
        {
            Assert.That((PlayerAssemblies.Lacks(typeof(EditorOnlyProbe)), PlayerAssemblies.Lacks(typeof(ProbeOneShot)), PlayerAssemblies.Lacks(typeof(WaitItem))), Is.EqualTo((true, false, false)));
            Assert.That(PlayerAssemblies.Lacks(typeof(Assert)), Is.False, "a precompiled assembly is not judged");
            FeedbackPlayer p = Go("EX23").AddComponent<FeedbackPlayer>();
            p.m_Items.Add(new EditorOnlyProbe());
            p.m_Items.Add(new ProbeOneShot());
            RuleResult b09 = FeedbackValidator.Run(p).Single(r => r.Id == "FB-B09");
            Assert.That((b09.Item, b09.Severity, b09.Message), Is.EqualTo((0, RuleSeverity.Error, PlayerlessTypeRule.Message + typeof(EditorOnlyProbe).Assembly.GetName().Name)));
            List<CatalogLeaf> leaves = FeedbackCatalog.Build(FeedbackCatalog.Sources());
            Assert.That(leaves.Single(l => l.Type == typeof(EditorOnlyProbe)).Path, Is.EqualTo("Probe/Editor Only" + FeedbackCatalog.NotInBuilds));
            Assert.That(leaves.Single(l => l.Type == typeof(ProbeOneShot)).Path, Is.EqualTo("Probe/One Shot"));
        }

        // ---- the debugger's item line: a slot a throw stopped says faulted until its next Begin · a type the preview skips
        // says so · a user item that ran and a package item say nothing
        static ProbeValue Valued()
        {
            var v = new ProbeValue { m_Motion = TweenTo(1f) };
            v.m_Timing.m_Duration = 1f;
            return v;
        }

        static string Notes(DebuggerReader reader, FeedbackScheduler s, Func<PlayRow, bool> play)
        {
            reader.Read(s);
            reader.Select(reader.Plays.FindIndex(r => play(r)));
            reader.Read(s);
            return string.Join(" | ", reader.Items.Select(i => i.ListIndex + ":" + (i.Note ?? "-")));
        }

        [Test]
        public void EX24_TheDebuggerNotesWhatUserCodeMissed()
        {
            LogAssert.ignoreFailingMessages = true;
            FeedbackScheduler s = Sched();
            FeedbackPlayer thrown = Player(s, new ProbeTimed { m_Length = 1f }, Keeper(Go("Keeper").transform, 1f), Valued());
            FeedbackPlayer clean = Player(s, new ProbeTimed { m_Length = 1f }, Valued());
            FeedbackPlayer overlap = Player(s, 2, new ProbeTimed { m_Length = 1f });
            ProbeLog.Throw = true;
            thrown.Play();
            overlap.Play();
            Tick(s, 2);
            ProbeLog.Throw = false;
            overlap.Play();
            clean.Play();
            Tick(s, 2);
            var reader = new DebuggerReader();
            Assert.That(Notes(reader, s, r => ReferenceEquals(r.Owner, thrown)), Is.EqualTo("0:faulted | 1:- | 2:faulted"));
            Assert.That(Notes(reader, s, r => ReferenceEquals(r.Owner, clean)), Is.EqualTo("0:- | 1:-"));
            reader.Read(s);
            long[] serials = reader.Plays.Where(r => ReferenceEquals(r.Owner, overlap)).Select(r => r.Serial).OrderBy(x => x).ToArray();
            Assert.That(serials.Select(n => Notes(reader, s, r => ReferenceEquals(r.Owner, overlap) && r.Serial == n)), Is.EqualTo(new[] { "0:faulted", "0:-" }), "each play record's own");
            DebuggerSource.Override = s;
            try
            {
                FeedbackDebuggerWindow w = DebuggerTesting.Window();
                w.Refresh();
                w.Reader.Select(w.Reader.Plays.FindIndex(r => ReferenceEquals(r.Owner, thrown)));
                w.Refresh();
                int timed = Enumerable.Range(0, w.Reader.Items.Count).Single(k => w.ItemsTable.Text("Item", k) == "ProbeTimed");
                int position = Enumerable.Range(0, w.Reader.Items.Count).Single(k => w.ItemsTable.Text("Item", k) == "PositionItem");
                Assert.That(w.ItemsTable.Text("Note", timed), Is.EqualTo("faulted"));
                Assert.That(w.ItemsTable.Text("Note", position), Is.Empty);
            }
            finally
            {
                DebuggerTesting.Clean();
            }
            thrown.Play();
            Tick(s, 1);
            Assert.That(Notes(reader, s, r => ReferenceEquals(r.Owner, thrown)), Is.EqualTo("0:- | 1:- | 2:-"), "the next play's Begin clears it");

            Scene scene = OpenScene("ex24");
            SceneManager.SetActiveScene(scene);
            var player = new GameObject("ExtensionPlayer").AddComponent<FeedbackPlayer>();
            SceneManager.MoveGameObjectToScene(player.gameObject, scene);
            player.m_Items.Add(new ProbeTimed { m_Length = 0.5f });
            player.m_Items.Add(new ProbeRun());
            var keep = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = TweenTo(3f) };
            keep.m_Timing.m_Duration = 2f;
            player.m_Items.Add(keep);
            player.m_Items.Add(Valued());
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(30);
            string preview = Notes(reader, PreviewSession.Current.Scheduler, r => r.Kind == RunnerKind.Player);
            PreviewSession.Current.Stop();
            Assert.That(preview, Is.EqualTo("0:not called in the preview | 1:- | 2:- | 3:not called in the preview"));
        }
    }
}
