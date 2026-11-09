using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the time scale, the user targets and the float event on a test scheduler ticked by hand (0.05 s — edit mode writes no time
    // value: the computed scale is read), the previews on the test's clock (an update is 1/120 s). The time values are read
    // during the preview (its close puts the base back)
    public sealed class TimeCustomTargetsTests
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
            PreviewRecordPaths.Source = new PreviewRecordPathTable();
            s_Now = 0;
            PreviewClock.Source = () => s_Now;
            m_Logs.Clear();
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
        }

        static string B(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");

        static TweenMotion TweenTo(float to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = new Vector4(to, to, to, to);
            return new TweenMotion { m_Params = p };
        }

        static FlashMotion FlashTo(float target)
        {
            var flash = new FlashMotion();
            flash.m_Params.m_Target = new Vector4(target, target, target, target);
            return flash;
        }

        static FlashMotion FlashTo(Vector4 target)
        {
            var flash = new FlashMotion();
            flash.m_Params.m_Target = target;
            return flash;
        }

        // a flash held at its target the whole length
        static FlashMotion Held(float target)
        {
            FlashMotion flash = FlashTo(target);
            flash.m_Params.m_Attack = 0f;
            flash.m_Params.m_Hold = 1f;
            flash.m_Params.m_Release = 0f;
            return flash;
        }

        static SetMotion SetTo(float v) => new SetMotion { m_Params = new SetParams { m_Value = new Vector4(v, 0f, 0f, 0f) } };

        static PunchMotion PunchBy(float a) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(a, a, a, 0f), m_Vibrato = 1f, m_Decay = 0f } };

        static T On<T>(T item, Object target, FeedbackMotion motion, float duration, TargetRefMode mode = TargetRefMode.Direct) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = mode, m_Object = target, m_Path = "", m_Anchor = "", m_ChildLookup = ChildLookup.FirstOfType };
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

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

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items) => PlayerOn(Go("Player"), s, items);

        FeedbackPlayer PlayerOn(GameObject go, FeedbackScheduler s, params FeedbackItem[] items)
        {
            FeedbackPlayer p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            p.Bind(s);
            p.EnsureRuntime();
            m_Players.Add(p);
            return p;
        }

        FeedbackItem Keeper(out Transform t)
        {
            t = Go("Keeper").transform;
            var keeper = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = t, m_Path = "", m_Anchor = "" }, m_Motion = PunchBy(0.5f) };
            keeper.m_Timing.m_Duration = 3f;
            return keeper;
        }

        static void Tick(FeedbackScheduler s, int n)
        {
            for (int i = 0; i < n; i++)
                s.Tick(Step);
        }

        static void Pump(int updates)
        {
            for (int i = 0; i < updates; i++)
            {
                s_Now += UpdateStep;
                PreviewSession.Current?.Update();
            }
        }

        Scene OpenScene(string name)
        {
            string path = "Assets/TimeCustomTargetsTests." + name + ".unity";
            File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Scene s = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            m_Scenes.Add(s);
            m_Assets.Add(path);
            return s;
        }

        // a previewed player in the scene — a tween on itself keeps the session
        static FeedbackPlayer ScenePlayer(Scene scene, params FeedbackItem[] items)
        {
            var player = new GameObject("TimeCustomPlayer").AddComponent<FeedbackPlayer>();
            SceneManager.MoveGameObjectToScene(player.gameObject, scene);
            player.m_Items.AddRange(items);
            var keep = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = TweenTo(3f) };
            keep.m_Timing.m_Duration = 2f;
            player.m_Items.Add(keep);
            return player;
        }

        static ValueSlot<T, TOps> SlotOf<T, TOps>(FeedbackPlayer p, int index = 0) where TOps : struct, IValueOps<T>
            => ((ValueTargetBinding<T, TOps>)p.Runtime.Binding(index)).Slot;

        static Component OwnerOf(FeedbackPlayer p) => SlotOf<float, FloatOps>(p) is CustomFloatSlot c ? c.Owner : null;

        // ---- the service's open request — under a timed one · no ageing · out of the full stack · clamp · one holder · release · close
        [Test]
        public void CQ02a_TheOpenRequest()
        {
            FeedbackScheduler s = Sched();
            TimeScaleService t = s.Context.TimeScale;
            Assert.That(t.IsAlive(default), Is.False, "no handle is alive");
            TimeScaleHandle a = t.Open();
            Assert.That(t.HasOpen && t.Effective == 1f, Is.True, "opened at 1");
            t.SetScale(a, 0.5f);
            Assert.That((t.Effective, t.OpenScaleOf(a)), Is.EqualTo((0.5f, 0.5f)));
            Tick(s, 100);
            Assert.That(t.IsAlive(a) && t.Effective == 0.5f, Is.True, "no ageing over 5 s");
            TimeScaleHandle hit = t.Request(0f, 0.1);
            Assert.That(t.Effective, Is.EqualTo(0f), "a timed request above the open one wins");
            Tick(s, 4);
            Assert.That(!t.IsAlive(hit) && t.Effective == 0.5f, Is.True, "the open one goes on after it");
            LogAssert.Expect(LogType.Warning, TimeScaleService.FullWarning);
            for (int i = 0; i < 12; i++)
                t.Request(0.9f, 10.0);
            Assert.That(t.IsAlive(a), Is.True, "a full stack drops a timed one, never the open one");
            LogAssert.Expect(LogType.Warning, TimeScaleService.RangeWarning);
            t.SetScale(a, 200f);
            Assert.That(t.OpenScaleOf(a), Is.EqualTo(100f), "clamped to 100");
            t.SetScale(a, -1f);
            Assert.That(t.OpenScaleOf(a), Is.EqualTo(0f), "clamped to 0 (warned once)");
            TimeScaleHandle b = t.Open();
            Assert.That((t.IsAlive(a), t.IsAlive(b), t.OpenValue), Is.EqualTo((false, true, 1f)), "a second open takes the first one's place");
            t.SetScale(a, 0.3f);
            Assert.That(t.OpenValue, Is.EqualTo(1f), "a stale handle changes nothing");
            Assert.That(t.Release(b) && !t.HasOpen && !t.Release(b), Is.True, "released once");
            t.Open();
            s.Shutdown();
            Assert.That(t.HasOpen, Is.False, "the close clears it");
        }

        // ---- the Time Scale item — Flash back to 1 (no request left) · Tween stays · Revert · Restore · Set inside Play() · intensity ·
        // the category scale on Flash only · the category off · backward
        [Test]
        public void CQ03a_TheTimeScaleItem()
        {
            {
                FeedbackScheduler s = Sched();
                TimeScaleService t = s.Context.TimeScale;
                Player(s, On(new TimeScaleItem(), null, FlashTo(0.2f), 0.5f)).Play();
                Tick(s, 3);
                float mid = t.Effective;
                Tick(s, 12);
                Assert.That(mid < 1f && t.Effective == 1f, Is.True, "a flash goes and comes back — " + mid);
                Assert.That(t.HasOpen || t.IsOwning, Is.False, "scale 1 holds no request");
            }
            {
                FeedbackScheduler s = Sched();
                TimeScaleService t = s.Context.TimeScale;
                FeedbackPlayer p = Player(s, On(new TimeScaleItem(), null, TweenTo(0.5f), 0.2f), Keeper(out _));
                p.Play();
                Tick(s, 8);
                Assert.That(t.Effective, Is.EqualTo(0.5f), "a tween stays");
                p.Stop(StopMode.Revert);
                s.Tick(Step);
                Assert.That((t.Effective, t.HasOpen), Is.EqualTo((1f, false)), "Revert");
            }
            {
                FeedbackScheduler s = Sched();
                TimeScaleService t = s.Context.TimeScale;
                FeedbackPlayer p = Player(s, On(new TimeScaleItem(), null, TweenTo(0.5f), 0.2f));
                p.Play();
                Tick(s, 8);
                p.Restore();
                Assert.That((t.Effective, t.HasOpen), Is.EqualTo((1f, false)), "Restore");
            }
            {
                FeedbackScheduler s = Sched();
                Player(s, On(new TimeScaleItem(), null, SetTo(0.5f), 0f)).Play();
                Assert.That(s.Context.TimeScale.Effective, Is.EqualTo(0.5f), "a Set inside Play()");
            }
            {
                FeedbackScheduler s = Sched();
                Player(s, On(new TimeScaleItem(), null, Held(0f), 1f)).Play(PlayArgs.Default.WithIntensity(0.5f));
                Tick(s, 3);
                Assert.That(s.Context.TimeScale.Effective, Is.EqualTo(0.5f), "the intensity weighs the flash");
            }
            {
                FeedbackScheduler s = Sched();
                TimeScaleService t = s.Context.TimeScale;
                s.Context.Categories.SetScale(FeedbackCategory.Time, 0.5f);
                FeedbackPlayer p = Player(s, On(new TimeScaleItem(), null, Held(0f), 1f));
                p.Play();
                Tick(s, 3);
                float flashed = t.Effective;
                p.Stop(StopMode.Revert);
                Tick(s, 2);
                Player(s, On(new TimeScaleItem(), null, TweenTo(0.5f), 0.1f)).Play();
                Tick(s, 6);
                Assert.That((flashed, t.Effective), Is.EqualTo((0.5f, 0.5f)), "the category scale weighs a flash, not a tween");
                FeedbackScheduler off = Sched();
                off.Context.Categories.SetEnabled(FeedbackCategory.Time, false);
                Player(off, On(new TimeScaleItem(), null, Held(0f), 1f)).Play();
                Tick(off, 3);
                Assert.That(off.Context.TimeScale.Effective, Is.EqualTo(1f), "the category off skips the play");
            }
            {
                FeedbackScheduler s = Sched();
                TimeScaleService t = s.Context.TimeScale;
                Player(s, On(new TimeScaleItem(), null, TweenTo(0.5f), 0.2f)).Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward));
                s.Tick(Step);
                float mid = t.Effective;
                Tick(s, 8);
                Assert.That(mid < 1f && t.Effective == 1f, Is.True, "backward — from the tween's end to the start · " + mid);
            }
        }

        // ---- several in a context are the later one's (one slot · one request) · the player's release
        // takes the request back · a second tween starts at the scale now · a hit stop above it
        [Test]
        public void CQ03b_TheSlot()
        {
            {
                FeedbackScheduler s = Sched();
                TimeScaleService t = s.Context.TimeScale;
                Player(s, On(new TimeScaleItem(), null, Held(0.5f), 1f), On(new TimeScaleItem(), null, Held(0.4f), 1f)).Play();
                Tick(s, 3);
                Assert.That((t.Effective, t.HasOpen), Is.EqualTo((0.4f, true)), "the later override — not a product");
            }
            {
                FeedbackScheduler s = Sched();
                TimeScaleService t = s.Context.TimeScale;
                FeedbackPlayer p = Player(s, On(new TimeScaleItem(), null, TweenTo(0.5f), 0.1f));
                p.Play();
                Tick(s, 4);
                FeedbackPlayer q = Player(s, On(new TimeScaleItem(), null, TweenTo(0.25f), 0.2f));
                q.Play();
                Tick(s, 2);
                Assert.That(t.Effective < 0.5f && t.Effective > 0.25f, Is.True, "a second tween from the scale now — " + t.Effective);
                q.ReleaseRuntime();
                p.ReleaseRuntime();
                s.Tick(Step);
                Assert.That((t.Effective, t.HasOpen), Is.EqualTo((1f, false)), "the slot's close takes the request back");
            }
            {
                FeedbackScheduler s = Sched();
                TimeScaleService t = s.Context.TimeScale;
                Player(s, On(new TimeScaleItem(), null, SetTo(0.5f), 0f)).Play();
                Player(s, new HitStopItem { m_FreezeDuration = 0.1f, m_MinimumTimeScale = 0.1f, m_TimeScale = 0f }).Play();
                s.Tick(Step);
                float during = t.Effective;
                Tick(s, 4);
                Assert.That((during, t.Effective), Is.EqualTo((0f, 0.5f)), "the freeze wins · the slow motion goes on");
            }
        }

        // ---- Custom Value — tween · punch back to the bits · intensity · another's write is the new base · direct · the three
        // forms · the stage
        [Test]
        public void CQ06a_TheCustomValue()
        {
            FeedbackScheduler s = Sched();
            var target = Go("T").AddComponent<FeedbackTestTarget>();
            target.m_Float = 2f;
            int slots = s.Context.Layers.SlotCount;
            Player(s, On(new CustomFloatItem(), target, TweenTo(5f), 0.2f)).Play();
            Tick(s, 6);
            Assert.That(B(target.m_Float), Is.EqualTo(B(5f)), "a tween's end");
            target.m_Float = 2f;
            FeedbackPlayer punch = Player(s, On(new CustomFloatItem(), target, PunchBy(1f), 0.5f));
            Assert.That(s.Context.Layers.SlotCount - slots, Is.EqualTo(1), "one slot for a component");
            punch.Play();
            Tick(s, 3);
            float full = target.m_Float - 2f;
            Tick(s, 12);
            Assert.That(full != 0f && B(target.m_Float) == B(2f), Is.True, "a punch writes and ends on the base's bits");
            punch.Play(PlayArgs.Default.WithIntensity(0.5f));
            Tick(s, 3);
            float half = target.m_Float - 2f;
            Tick(s, 12);
            Assert.That(half, Is.EqualTo(full * 0.5f).Within(1e-4f), "the intensity");
            punch.Play();
            Tick(s, 3);
            target.m_Float = 7f;
            Tick(s, 12);
            Assert.That(B(target.m_Float), Is.EqualTo(B(7f)), "another's write in the punch is the new base");
            var direct = On(new CustomFloatItem(), target, PunchBy(1f), 0.5f);
            direct.m_DirectWrite = true;
            Assert.That(SlotOf<float, FloatOps>(Player(s, direct)).Direct, Is.True, "a direct write's own slot");
            Player(s, On(new CustomVector3Item(), target, TweenTo(3f), 0.1f)).Play();
            FeedbackPlayer color = Player(s, On(new CustomColorItem(), target, FlashTo(new Vector4(1f, 0f, 0f, 1f)), 0.5f));
            color.Play();
            Tick(s, 3);
            Color mid = target.m_Color;
            Tick(s, 12);
            Assert.That(target.m_Vector, Is.EqualTo(new Vector3(3f, 3f, 3f)), "a vector");
            Assert.That(mid != Color.white && B(target.m_Color.g) == B(1f) && B(target.m_Color.b) == B(1f), Is.True, "a color flash and back");
        }

        [Test]
        public void CQ07a_TheStage()
        {
            FeedbackScheduler s = Sched();
            var plain = Go("Plain").AddComponent<FeedbackTestTarget>();
            var late = Go("Late").AddComponent<FeedbackTestTarget>();
            late.m_Stage = FeedbackTargetStage.LateUpdate;
            Assert.That(SlotOf<float, FloatOps>(Player(s, On(new CustomFloatItem(), plain, PunchBy(1f), 0.5f))).Stage, Is.EqualTo(ConsumptionStage.Update), "no declaration — Update");
            Assert.That(SlotOf<float, FloatOps>(Player(s, On(new CustomFloatItem(), late, PunchBy(1f), 0.5f))).Stage, Is.EqualTo(ConsumptionStage.LateApply), "LateUpdate — LateApply");
            Assert.That(SlotOf<Color, ColorOps>(Player(s, On(new CustomColorItem(), late, TweenTo(1f), 0.5f))).Stage, Is.EqualTo(ConsumptionStage.LateApply), "every form");
        }

        // ---- the walk — first of two · a dragged one · a dragged object's first · Parent up · first of type below every
        // child · a disabled implementer · a refusal with the interface's name · a dragged other component's object
        [Test]
        public void CQ08a_TheWalk()
        {
            FeedbackScheduler s = Sched();
            GameObject two = Go("Two");
            var first = two.AddComponent<FeedbackTestFloatTarget>();
            var second = two.AddComponent<FeedbackTestTarget>();
            Assert.That(OwnerOf(PlayerOn(two, s, On(new CustomFloatItem(), null, PunchBy(1f), 0.5f, TargetRefMode.Self))), Is.SameAs(first), "Self — the first implementer");
            Assert.That(OwnerOf(Player(s, On(new CustomFloatItem(), second, PunchBy(1f), 0.5f))), Is.SameAs(second), "a dragged implementer — that one");
            Assert.That(OwnerOf(Player(s, On(new CustomFloatItem(), two, PunchBy(1f), 0.5f))), Is.SameAs(first), "a dragged object — its first");
            GameObject root = Go("Root");
            var up = root.AddComponent<FeedbackTestTarget>();
            var mid = new GameObject("Mid");
            mid.transform.SetParent(root.transform, false);
            var owner = new GameObject("Owner");
            owner.transform.SetParent(mid.transform, false);
            Assert.That(OwnerOf(PlayerOn(owner, s, On(new CustomFloatItem(), null, PunchBy(1f), 0.5f, TargetRefMode.Parent))), Is.SameAs(up), "Parent — up from the parent");
            GameObject host = Go("Host");
            new GameObject("A").transform.SetParent(host.transform, false);
            var b = new GameObject("B");
            b.transform.SetParent(host.transform, false);
            var below = new GameObject("B1");
            below.transform.SetParent(b.transform, false);
            var deep = below.AddComponent<FeedbackTestTarget>();
            Assert.That(OwnerOf(PlayerOn(host, s, On(new CustomFloatItem(), null, PunchBy(1f), 0.5f, TargetRefMode.Child))), Is.SameAs(deep), "first of type — below every child");
            var off = Go("Off").AddComponent<FeedbackTestTarget>();
            off.enabled = false;
            off.m_Float = 1f;
            Player(s, On(new CustomFloatItem(), off, TweenTo(4f), 0.1f)).Play();
            Tick(s, 4);
            Assert.That(off.m_Float, Is.EqualTo(4f), "a disabled implementer is found and written");
            var lone = Go("Lone").AddComponent<FeedbackTestFloatTarget>();
            LogAssert.Expect(LogType.Warning, new Regex("no IFeedbackTarget<Color> on the target"));
            Assert.That(Player(s, On(new CustomColorItem(), lone, TweenTo(1f), 0.1f)).Runtime.Binding(0), Is.Null, "refused");
            LogAssert.Expect(LogType.Warning, new Regex("no IFeedbackTarget<float> on the target"));
            Assert.That(Player(s, On(new CustomFloatItem(), Go("Empty"), TweenTo(1f), 0.1f)).Runtime.Binding(0), Is.Null, "refused — the readable name");
            var sibling = (CustomColorSlot)SlotOf<Color, ColorOps>(Player(s, On(new CustomColorItem(), first, TweenTo(1f), 0.1f)));
            Assert.That(sibling.Owner, Is.SameAs(second), "a dragged other component — its object's implementer");
        }

        // ---- a throwing setter — an offset: the slot is let go once (the rest runs) · a Persist: the play ends
        [Test]
        public void CQ08b_AThrowingTarget()
        {
            LogAssert.ignoreFailingMessages = true;
            {
                FeedbackScheduler s = Sched();
                var target = Go("T").AddComponent<FeedbackTestTarget>();
                target.m_Throws = true;
                FeedbackPlayer p = Player(s, On(new CustomFloatItem(), target, PunchBy(1f), 0.5f), Keeper(out Transform k));
                m_Logs.Clear();
                p.Play();
                Tick(s, 6);
                int sets = target.Sets;
                Vector3 at = k.localPosition;
                Tick(s, 4);
                Assert.That(SlotOf<float, FloatOps>(p).Detached, Is.True, "the slot is let go");
                Assert.That(p.IsPlaying && k.localPosition != at, Is.True, "the play and its other item go on");
                Assert.That((target.Sets, m_Logs.Count), Is.EqualTo((sets, 1)), "no write after it · one log");
            }
            {
                FeedbackScheduler s = Sched();
                var target = Go("T2").AddComponent<FeedbackTestTarget>();
                target.m_Throws = true;
                FeedbackPlayer p = Player(s, On(new CustomFloatItem(), target, TweenTo(5f), 0.5f), Keeper(out _));
                p.Play();
                Tick(s, 3);
                Assert.That(p.IsPlaying, Is.False, "a Persist write that throws ends the play");
            }
        }

        // ---- Float Event — a call a frame from the base to the end · a punch on the base · intensity ·
        // backward · an instant · Revert's base · one Restore · a throwing listener (the play goes on, one log an occurrence)
        [Test]
        public void CQ09a_TheFloatEvent()
        {
            LogAssert.ignoreFailingMessages = true;
            FeedbackScheduler s = Sched();
            var listener = Go("Listener").AddComponent<FeedbackTestListener>();
            var tween = On(new CustomFloatEventItem { m_Base = 2f }, null, TweenTo(10f), 0.5f);
            tween.m_Event.AddListener(listener.Take);
            Player(s, tween).Play();
            Tick(s, 14);
            Assert.That(listener.Values.Count, Is.EqualTo(11), "the play's pass and a call a frame");
            Assert.That((listener.Values.First(), listener.Values.Last()), Is.EqualTo((2f, 10f)), "from the base to the end value");
            listener.Values.Clear();
            var punch = On(new CustomFloatEventItem { m_Base = 3f }, null, PunchBy(2f), 0.5f);
            punch.m_Event.AddListener(listener.Take);
            FeedbackPlayer pp = Player(s, punch);
            pp.Play();
            Tick(s, 3);
            float full = listener.Values.Last() - 3f;
            Tick(s, 12);
            Assert.That(full != 0f && listener.Values.Last() == 3f, Is.True, "a punch rides on the base and ends on it");
            pp.Play(PlayArgs.Default.WithIntensity(0.5f));
            Tick(s, 3);
            float half = listener.Values.Last() - 3f;
            Tick(s, 12);
            Assert.That(half, Is.EqualTo(full * 0.5f).Within(1e-4f), "the intensity");
            listener.Values.Clear();
            var back = On(new CustomFloatEventItem { m_Base = 2f }, null, TweenTo(10f), 0.5f);
            back.m_Event.AddListener(listener.Take);
            Player(s, back).Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward));
            Tick(s, 14);
            Assert.That(listener.Values[1] > 2f && listener.Values.Last() == 2f, Is.True, "backward — to the base");
            listener.Values.Clear();
            var instant = On(new CustomFloatEventItem { m_Base = 2f }, null, TweenTo(10f), 0f);
            instant.m_Event.AddListener(listener.Take);
            Player(s, instant).Play();
            Tick(s, 2);
            Assert.That(listener.Values, Is.EqualTo(new[] { 10f }), "an instant — the end value once");
            listener.Values.Clear();
            var cut = On(new CustomFloatEventItem { m_Base = 3f }, null, TweenTo(9f), 1f);
            cut.m_Event.AddListener(listener.Take);
            FeedbackPlayer cp = Player(s, cut, Keeper(out _));
            cp.Play();
            Tick(s, 4);
            cp.Stop(StopMode.Revert);
            s.Tick(Step);
            Assert.That(listener.Values.Last(), Is.EqualTo(3f), "Revert calls the base");
            cp.Restore();
            int n = listener.Values.Count;
            cp.Restore();
            Assert.That(listener.Values.Count, Is.EqualTo(n), "a second Restore calls nothing");
            listener.Values.Clear();
            listener.m_Throws = true;
            m_Logs.Clear();
            FeedbackPlayer tp = Player(s, tween);
            tp.Play();
            Tick(s, 5);
            Assert.That(tp.IsPlaying && listener.Values.Count == 6, Is.True, "a throwing listener — called every frame, the play goes on");
            Assert.That(m_Logs.Count, Is.EqualTo(1), "one log an occurrence");
        }

        // ---- Time Scale in the preview — the scene's time scale is not written during it, the clock slows, the notice
        // tells the scale (a hit stop's line instead while it runs)
        [Test]
        public void CQ13a_TheTimeScalePreview()
        {
            Scene scene = OpenScene("cq13");
            SceneManager.SetActiveScene(scene);
            float ts = Time.timeScale;
            FeedbackPlayer player = ScenePlayer(scene, On(new TimeScaleItem(), null, TweenTo(0.5f), 0.2f));
            Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
            Pump(60);
            var notices = new List<PreviewNotice>();
            PreviewNotices.Collect(PreviewSession.Current, notices);
            float during = Time.timeScale;
            float effective = PreviewSession.Current.Scheduler.Context.TimeScale.Effective;
            PreviewSession.Current.Stop();
            Assert.That(during, Is.EqualTo(ts), "edit mode writes no time value");
            Assert.That(effective, Is.EqualTo(0.5f), "the preview clock's scale");
            Assert.That(notices.Single(x => x.Kind == PreviewNoticeKind.TimeScale).Text, Is.EqualTo("Time Scale 0.5 — the preview clock is slowed, the scene is not."));
            Object.DestroyImmediate(player.gameObject);
            FeedbackPlayer both = ScenePlayer(scene, On(new TimeScaleItem(), null, SetTo(0.5f), 0f), new HitStopItem { m_FreezeDuration = 0.5f, m_TimeScale = 0f, m_MinimumTimeScale = 0f });
            Assert.That(PreviewSession.Start(both, default), Is.EqualTo(PreviewRefusal.None));
            Pump(6);
            PreviewNotices.Collect(PreviewSession.Current, notices);
            PreviewSession.Current.Stop();
            Assert.That(notices.Select(x => x.Kind).Where(k => k == PreviewNoticeKind.HitStop || k == PreviewNoticeKind.TimeScale), Is.EqualTo(new[] { PreviewNoticeKind.HitStop }), "the hit stop's line while it runs");
        }

        // ---- Custom Value in the preview — a Tween's and a punch's end is the base without a save · a save in it writes
        // the base to the file, the preview's value back after · the scene stays clean
        [Test]
        public void CQ14a_TheCustomValuePreview()
        {
            Scene scene = OpenScene("cq14");
            SceneManager.SetActiveScene(scene);
            foreach (bool offset in new[] { false, true })
            {
                var go = new GameObject("Target");
                SceneManager.MoveGameObjectToScene(go, scene);
                var target = go.AddComponent<FeedbackTestTarget>();
                target.m_Float = 2f;
                FeedbackPlayer player = ScenePlayer(scene, On(new CustomFloatItem(), target, offset ? (FeedbackMotion)PunchBy(1f) : TweenTo(5f), 2f));
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                Pump(60);
                float during = target.m_Float;
                PreviewSession.Current.Stop();
                Assert.That(during != 2f && target.m_Float == 2f, Is.True, (offset ? "a punch" : "a tween") + " — the end's base without a save");
                Assert.That(scene.isDirty, Is.False, "the scene stays clean");
                Object.DestroyImmediate(player.gameObject);
                Object.DestroyImmediate(go);
            }
            {
                var go = new GameObject("Target");
                SceneManager.MoveGameObjectToScene(go, scene);
                var target = go.AddComponent<FeedbackTestTarget>();
                target.m_Float = 2f;
                FeedbackPlayer player = ScenePlayer(scene, On(new CustomFloatItem(), target, TweenTo(5f), 2f));
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                Pump(60);
                float during = target.m_Float;
                EditorSceneManager.MarkSceneDirty(scene);
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                string file = File.ReadAllText(Path.GetFullPath(scene.path));
                float afterSave = target.m_Float;
                PreviewSession.Current.Stop();
                Assert.That(Regex.Match(file, @"m_Float: ([^\n]+)").Groups[1].Value.Trim(), Is.EqualTo("2"), "the file has the base");
                Assert.That(afterSave, Is.EqualTo(during), "the preview's value back after the save");
                Assert.That(target.m_Float, Is.EqualTo(2f), "the end — the base");
            }
        }

        // ---- Float Event in the preview — the field off: one skip an occurrence, no call · on: an Editor And Runtime
        // listener is called, a Runtime Only one is not
        [Test]
        public void CQ15a_TheFloatEventPreview()
        {
            Scene scene = OpenScene("cq15");
            SceneManager.SetActiveScene(scene);
            var listenerGo = new GameObject("Listener");
            SceneManager.MoveGameObjectToScene(listenerGo, scene);
            var listener = listenerGo.AddComponent<FeedbackTestListener>();
            foreach (bool invoke in new[] { false, true })
            {
                foreach (UnityEventCallState state in new[] { UnityEventCallState.EditorAndRuntime, UnityEventCallState.RuntimeOnly })
                {
                    listener.Values.Clear();
                    var item = On(new CustomFloatEventItem { m_Base = 1f, m_InvokeInPreview = invoke }, null, TweenTo(4f), 0.5f);
                    UnityEventTools.AddPersistentListener(item.m_Event, listener.Take);
                    item.m_Event.SetPersistentListenerState(0, state);
                    FeedbackPlayer player = ScenePlayer(scene, item);
                    Assert.That(PreviewSession.Start(player, default), Is.EqualTo(PreviewRefusal.None));
                    Pump(90);
                    int skipped = PreviewSession.Current.Scheduler.Context.SkippedUnityEvents;
                    var notices = new List<PreviewNotice>();
                    PreviewNotices.Collect(PreviewSession.Current, notices);
                    PreviewSession.Current.Stop();
                    string what = "invoke " + invoke + " · " + state;
                    if (!invoke)
                    {
                        Assert.That((listener.Values.Count, skipped), Is.EqualTo((0, 1)), what + " — no call · one skip");
                        Assert.That(notices.Any(n => n.Kind == PreviewNoticeKind.UnityEvents && n.Text == "1 UnityEvent call(s) not made in the preview."), Is.True, what);
                    }
                    else if (state == UnityEventCallState.EditorAndRuntime)
                        Assert.That(listener.Values.Count > 5 && skipped == 0, Is.True, what + " — called every frame");
                    else
                        Assert.That(listener.Values.Count, Is.Zero, what + " — not called in the editor");
                    Object.DestroyImmediate(player.gameObject);
                }
            }
        }

        // ---- the editor's ways — candidates · a drop's leaves and target · the readable type name · the
        // add menu's defaults · the labels · no direct write · no target field (globals)
        [Test]
        public void CQ17a_TheEditorsWays()
        {
            GameObject host = Go("Host");
            var player = host.AddComponent<FeedbackPlayer>();
            var c1 = new GameObject("C1");
            c1.transform.SetParent(host.transform, false);
            var t1 = c1.AddComponent<FeedbackTestTarget>();
            var c2 = new GameObject("C2");
            c2.transform.SetParent(host.transform, false);
            c2.AddComponent<FeedbackTestFloatTarget>();
            Assert.That(TargetChecks.Candidates(player, typeof(IFeedbackTarget<float>)).Count, Is.EqualTo(2), "float candidates");
            Assert.That(TargetChecks.Candidates(player, typeof(IFeedbackTarget<Color>)), Is.EqualTo(new Object[] { t1 }), "color candidates");
            List<CatalogLeaf> leaves = FeedbackCatalog.Build(FeedbackCatalog.Sources());
            List<CatalogLeaf> suggested = ComponentDrop.Suggest(leaves, c1);
            Assert.That(new[] { typeof(CustomFloatItem), typeof(CustomVector3Item), typeof(CustomColorItem) }.All(t => suggested.Any(l => l.Type == t)), Is.True, "a drop suggests the three");
            Assert.That(suggested.Any(l => l.Type == typeof(CustomFloatEventItem) || l.Type == typeof(TimeScaleItem)), Is.False, "not the globals");
            Assert.That(((TargetItem)ComponentDrop.Create(leaves.First(l => l.Type == typeof(CustomFloatItem)), c1)).m_Target.m_Object, Is.SameAs(t1), "the drop holds the implementer");
            Assert.That((TargetChecks.TypeName(typeof(IFeedbackTarget<float>)), TargetChecks.TypeName(typeof(IFeedbackTarget<Vector3>)), TargetChecks.TypeName(typeof(Light))),
                Is.EqualTo(("IFeedbackTarget<float>", "IFeedbackTarget<Vector3>", "Light")));
            Assert.That((((SetMotion)MotionDefaults.Create(typeof(TimeScaleItem), MotionKind.Set, out _)).m_Params.m_Value.x,
                    ((TweenMotion)MotionDefaults.Create(typeof(TimeScaleItem), MotionKind.Tween, out _)).m_Params.m_To.x,
                    ((FlashMotion)MotionDefaults.Create(typeof(TimeScaleItem), MotionKind.Flash, out _)).m_Params.m_Target.x),
                Is.EqualTo((0.5f, 0.5f, 0.2f)), "the add menu's defaults");
            var e = new CustomFloatEventItem();
            Assert.That((ItemRules.Label(e, "m_InvokeInPreview"), ItemRules.Label(e, "m_Base")), Is.EqualTo(("Invoke In Preview (listeners set to Editor And Runtime)", "Base (offset motions)")));
            Assert.That(ItemRules.Visible(e, "m_DirectWrite") || ItemRules.Visible(new TimeScaleItem(), "m_DirectWrite") || ItemRules.Visible(e, "m_Target"), Is.False, "the globals' direct write and target");
            Assert.That(ItemRules.Visible(new CustomFloatItem(), "m_DirectWrite"), Is.True, "a custom value's direct write");
        }
    }
}
