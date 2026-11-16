using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the record: the end of LateApply, the followed play's frames against the scene's own values (each frame's property
    // read back), the shadow that keeps up — the play's inputs, its history's skips, pauses, seeks, plays started inside a
    // frame, two clocks — the two starts of a form, other writers, the end frame, the context's settings, a channel's
    // receivers, the edits and the Rebuild wait, and the graphs' fields
    public sealed class RecordGraphTests
    {
        const float Dt = 1f / 120f;

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<View> m_Views = new List<View>();
        PreviewBarTestWindow m_Window;
        Func<double> m_Clock;
        // the editor's Dots choice before — the clone editor shares the user's preferences
        bool m_DotsKept;
        bool m_Dots;

        [SetUp]
        public void SetUp()
        {
            PlayerRuntime.ResetSession();
            PlayModeEdits.Clear();
            m_Clock = PreviewClock.Source;
            m_DotsKept = EditorPrefs.HasKey(MotionGraphView.DotsKey);
            m_Dots = MotionGraphView.DotsOn;
            EditorPrefs.DeleteKey(MotionGraphView.DotsKey);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (View v in m_Views)
                RecordWatch.Remove(v);
            m_Views.Clear();
            PreviewSession.Current?.Stop();
            PreviewClock.Source = m_Clock;
            if (m_DotsKept)
                MotionGraphView.DotsOn = m_Dots;
            else
                EditorPrefs.DeleteKey(MotionGraphView.DotsKey);
            if (m_Window != null)
                m_Window.Close();
            foreach (Object o in m_Made)
            {
                if (o is GameObject go && go != null && go.TryGetComponent(out FeedbackPlayer p))
                    p.ReleaseRuntime();
            }
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Schedulers.Clear();
            PlayModeEdits.Clear();
        }

        // a graph's stand-in: the series the fields keep, fed by the watch
        sealed class View : IRecordView
        {
            internal FeedbackPlayer P;
            internal Object O;
            internal SlotGraphModel Slot;
            internal ItemGraphModel Item;
            internal int ListIndex;
            internal int Channel;
            internal readonly RecordSeries Series = new RecordSeries();
            internal RecordFollow Follow;
            internal int Begins;
            internal int Frames;

            public FeedbackPlayer Player => P;

            public Object Owner => O;

            public void Began(RecordFollow follow)
            {
                Begins++;
                Follow = follow;
                Series.Clear();
            }

            public void Framed(RecordFollow follow)
            {
                Frames++;
                if (Slot != null)
                    Series.Slot(Slot, follow);
                if (Item != null)
                    Series.Item(Item, follow, ListIndex, Channel);
            }

            internal RecordRing Ring => Series.Ring;

            internal Vector4 Last => Ring.Value(Ring.Count - 1);

            internal int ComparedCount => Enumerable.Range(0, Ring.Count).Count(i => Ring.Compared(i, out _));
        }

        static TargetRef Self => new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };

        static TargetRef Child(string path) => new TargetRef { m_Mode = TargetRefMode.Child, m_Path = path, m_Anchor = "" };

        GameObject Go(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null)
                go.transform.SetParent(parent, false);
            else
                m_Made.Add(go);
            return go;
        }

        static FeedbackPlayer Player(GameObject go, params FeedbackItem[] items)
        {
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            return p;
        }

        static PunchMotion Punch(Vector4 a, float vibrato = 4f) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = a, m_Vibrato = vibrato, m_Decay = 1f } };

        static ShakeMotion Shake(float a, float f) => new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = new Vector4(a, a, a, 0f), m_Frequency = f } };

        static TweenMotion Tween(TweenMode mode, Vector4 to) => new TweenMotion { m_Params = new TweenParams { m_Mode = mode, m_To = to, m_Loops = 1 } };

        static T Timed<T>(T item, float duration, float delay = 0f, int repeats = 0, float gap = 0f) where T : EffectItem
        {
            item.m_Timing.m_Duration = duration;
            item.m_Timing.m_Delay = delay;
            item.m_Timing.m_RepeatCount = repeats;
            item.m_Timing.m_RepeatGap = gap;
            return item;
        }

        static PositionItem Pos(FeedbackMotion m, PositionSpace space = PositionSpace.Local) => new PositionItem { m_Target = Self, m_Space = space, m_Motion = m };

        static RotationItem Rot(FeedbackMotion m, TransformSpace space = TransformSpace.Local) => new RotationItem { m_Target = Self, m_Space = space, m_Motion = m };

        static GraphInputs Inputs => new GraphInputs(1f, 1u, false, false, false);

        FeedbackScheduler Live(SettingsValues settings = null)
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            s.Context.UseSettings(settings ?? SettingsValues.Load(ContextKind.Test));
            return s;
        }

        static void Bind(FeedbackPlayer p, FeedbackScheduler s)
        {
            p.Bind(s);
            p.EnsureRuntime();
        }

        // a frame in the loop's order: Revert, the scripts' Update, the Update stage, LateUpdate, LateApply
        static void Frame(FeedbackScheduler s, float dt, Action update = null, Action late = null, float scaled = -1f)
        {
            s.RunRevert();
            update?.Invoke();
            s.RunUpdate(new FrameTime(scaled < 0f ? dt : scaled, dt));
            late?.Invoke();
            s.RunLateApply();
        }

        static float Step(System.Random r) => (float)(1.0 / 144 + r.NextDouble() * (1.0 / 30 - 1.0 / 144));

        View Watch(FeedbackPlayer p, Object owner = null)
        {
            var v = new View { P = p, O = owner != null ? owner : p };
            RecordWatch.Add(v);
            m_Views.Add(v);
            return v;
        }

        // the line of the followed play — the slot model of a group, built from the follow
        static SlotGraphModel Line(FeedbackPlayer p, RecordFollow follow, string group = null, SettingsValues settings = null, double span = 1.2)
            => SlotGraphModel.Build(p, p.m_Items, p, Inputs, span, 150, group, null, settings ?? SettingsValues.Load(ContextKind.Test), follow);

        // the item model of the followed play
        static ItemGraphModel ItemLine(FeedbackPlayer p, RecordFollow follow, int listIndex, int channel = 0)
        {
            Vector4? start = null;
            int k = follow.ProgramItem(listIndex);
            if (k >= 0 && follow.Live.Binding(k) != null && follow.Live.Binding(k).TryGetStartValue(follow.Record, out Vector4 s))
                start = s;
            return ItemGraphModel.Build(p.m_Items, listIndex, p, p, Inputs, channel, 200, start, follow);
        }

        // the first compared frame that differed — its place, value and line
        static string FirstDifference(RecordRing ring, SlotGraphModel model)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                if (!ring.Compared(i, out Vector4 line))
                    continue;
                Vector4 v = ring.Value(i);
                if ((v - line).magnitude > RecordSeries.Tolerance)
                    return "frame " + i + " at " + ring.Seconds(i) + " s: " + v + " against " + line + " · group " + model?.Shown?.Name + " persist " + model?.PersistGroup;
            }
            return "none";
        }

        // ---- T3 the event

        // one event a frame after LateApply wrote (a position slot's too), the frame's time kept; none from a dry scheduler;
        // the preview's ticks and a scrub's zero tick raise it
        [Test]
        public void RG03_TheEndOfLateApply()
        {
            FeedbackScheduler s = Live();
            GameObject go = Go("E");
            FeedbackPlayer p = Player(go, Timed(Pos(Punch(new Vector4(0.5f, 0f, 0f, 0f))), 0.5f));
            Bind(p, s);
            int count = 0;
            Vector3 atEvent = default;
            Action<FeedbackScheduler> h = x =>
            {
                if (x != s)
                    return;
                count++;
                atEvent = go.transform.localPosition;
            };
            FeedbackDiagnostics.LayersApplied += h;
            try
            {
                p.Play();
                for (int n = 0; n < 5; n++)
                {
                    s.RunRevert();
                    s.RunUpdate(new FrameTime(0.01f, 0.02f));
                    s.RunLateApply();
                    Assert.That(atEvent, Is.EqualTo(go.transform.localPosition), "after the position slot wrote");
                }
                Assert.That(atEvent.x, Is.Not.EqualTo(0f), "a frame that moved");
                Assert.That(count, Is.EqualTo(5), "one a frame");
                Assert.That(s.LastTime.ScaledDelta, Is.EqualTo(0.01f));
                Assert.That(s.LastTime.UnscaledDelta, Is.EqualTo(0.02f));
                s.RunLateApply();
                Assert.That(count, Is.EqualTo(5), "a second LateApply in the frame raises none");
            }
            finally
            {
                FeedbackDiagnostics.LayersApplied -= h;
            }

            FeedbackScheduler dry = SlotGraphModel.DryScheduler(SettingsValues.Load(ContextKind.Test));
            m_Schedulers.Add(dry);
            int dryCount = 0;
            Action<FeedbackScheduler> hd = x =>
            {
                if (x == dry)
                    dryCount++;
            };
            FeedbackDiagnostics.LayersApplied += hd;
            dry.Tick(0.01f);
            FeedbackDiagnostics.LayersApplied -= hd;
            Assert.That(dryCount, Is.Zero, "a dry scheduler raises none");

            double now = 0;
            PreviewClock.Source = () => now;
            Assert.That(PreviewSession.Start(p, default), Is.EqualTo(PreviewRefusal.None));
            PreviewSession session = PreviewSession.Current;
            int preview = 0;
            Action<FeedbackScheduler> hp = x =>
            {
                if (x == session.Scheduler)
                    preview++;
            };
            FeedbackDiagnostics.LayersApplied += hp;
            int ticks = session.Ticks;
            for (int i = 0; i < 6; i++)
            {
                now += 1.0 / 60.0;
                session.Update();
            }
            int ticked = session.Ticks - ticks;
            int afterTicks = preview;
            Assert.That(session.Scrub(0.2), Is.EqualTo(SeekRefusal.None));
            FeedbackDiagnostics.LayersApplied -= hp;
            Assert.That(ticked, Is.GreaterThan(0));
            Assert.That(afterTicks, Is.EqualTo(ticked), "a preview tick");
            Assert.That(preview, Is.EqualTo(afterTicks + 1), "a scrub's zero tick");
        }

        // ---- T4 the slot's record

        // positions in two spaces under a turned parent, remapped and scaled intensity, a rotation product, a scale product and a
        // direct write: each frame's dot is the scene's value less its start, the shadow keeps the same — no warning
        [Test]
        public void RG04_TheSlotRecordIsTheScene()
        {
            foreach (bool variable in new[] { false, true })
            {
                var r = new System.Random(variable ? 7 : 8);
                {
                    GameObject parent = Go("P");
                    parent.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
                    parent.transform.localScale = new Vector3(1f, 2f, 1f);
                    GameObject c = Go("C", parent.transform);
                    c.transform.localPosition = new Vector3(1f, 2f, 3f);
                    FeedbackPlayer p = Player(c, Timed(Pos(Punch(new Vector4(0.3f, 0.2f, 0.1f, 0f), 5f)), 0.5f), Timed(Pos(Shake(0.2f, 12f), PositionSpace.World), 0.6f, 0.05f));
                    p.m_RemapIntensity = true;
                    p.m_IntensityRemap = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0.5f));
                    p.m_Intensity = 0.8f;
                    FeedbackScheduler s = Live();
                    Bind(p, s);
                    View v = Watch(p);
                    Vector3 rest = c.transform.localPosition;
                    p.Play(PlayArgs.Default.WithSeed(11u).WithIntensity(0.7f));
                    Assert.That(v.Follow, Is.Not.Null, "followed at Played");
                    v.Slot = Line(p, v.Follow, "C · Position");
                    Assert.That(v.Slot.Follow, Is.SameAs(v.Follow), "the line of that play");
                    float worst = 0f;
                    for (int n = 0; n < 110; n++)
                    {
                        int before = v.Ring.Count;
                        Frame(s, variable ? Step(r) : Dt);
                        if (v.Ring.Count > before)
                            worst = Math.Max(worst, ((Vector3)v.Last - (c.transform.localPosition - rest)).magnitude);
                    }
                    Assert.That(v.Ring.Count, Is.GreaterThan(20));
                    Assert.That(worst, Is.LessThan(1e-5f), "positions");
                    Assert.That(v.ComparedCount, Is.GreaterThan(10), "compared with the shadow");
                    Assert.That(v.Ring.Differs, Is.False, "no other writer");
                }
                {
                    GameObject c = Go("R");
                    c.transform.localRotation = Quaternion.Euler(5f, 10f, 0f);
                    c.transform.localScale = new Vector3(1f, 2f, 0.5f);
                    FeedbackPlayer p = Player(c,
                        Timed(Rot(Punch(new Vector4(20f, 0f, 10f, 0f))), 0.5f),
                        Timed(Rot(new WiggleMotion { m_Params = new WiggleParams { m_Amplitude = new Vector4(10f, 10f, 0f, 0f), m_Frequency = 3f, m_Octaves = 2 } }, TransformSpace.World), 0.3f, 0f, 1),
                        Timed(new ScaleItem { m_Target = Self, m_Motion = Punch(new Vector4(0.3f, 0.3f, 0.3f, 0f), 3f) }, 0.5f),
                        Timed(new SquashAndStretchItem { m_Target = Self, m_Motion = Punch(new Vector4(0.4f, 0f, 0f, 0f), 2f) }, 0.4f, 0.1f));
                    FeedbackScheduler s = Live();
                    Bind(p, s);
                    View turn = Watch(p);
                    View size = Watch(p);
                    Quaternion restTurn = c.transform.localRotation;
                    Vector3 restSize = c.transform.localScale;
                    p.Play(PlayArgs.Default.WithSeed(5u));
                    turn.Slot = Line(p, turn.Follow, "R · Rotation");
                    size.Slot = Line(p, size.Follow, "R · Scale");
                    Assert.That(turn.Slot.Composition, Is.EqualTo(Composition.Rotation));
                    Assert.That(size.Slot.Composition, Is.EqualTo(Composition.Multiplicative));
                    float angle = 0f, factor = 0f;
                    for (int n = 0; n < 90; n++)
                    {
                        int before = turn.Ring.Count;
                        Frame(s, variable ? Step(r) : Dt);
                        if (turn.Ring.Count <= before)
                            continue;
                        Vector4 d = turn.Last;
                        angle = Math.Max(angle, Quaternion.Angle(c.transform.localRotation, restTurn * Quaternion.Euler(d.x, d.y, d.z)));
                        Vector4 f = size.Last;
                        Vector3 composed = new Vector3(restSize.x * (1f + f.x), restSize.y * (1f + f.y), restSize.z * (1f + f.z));
                        factor = Math.Max(factor, (c.transform.localScale - composed).magnitude);
                    }
                    Assert.That(angle, Is.LessThan(2e-3f), "a product of turns (degrees)");
                    Assert.That(factor, Is.LessThan(1e-5f), "a product of scales");
                    Assert.That(turn.Ring.Differs || size.Ring.Differs, Is.False);
                }
            }
            {
                GameObject c = Go("D");
                PositionItem direct = Timed(Pos(Punch(new Vector4(0.4f, 0.1f, 0f, 0f))), 0.5f);
                direct.m_DirectWrite = true;
                FeedbackPlayer p = Player(c, direct);
                FeedbackScheduler s = Live();
                Bind(p, s);
                View v = Watch(p);
                p.Play(PlayArgs.Default.WithSeed(5u));
                v.Slot = Line(p, v.Follow);
                float worst = 0f;
                var r = new System.Random(14);
                for (int n = 0; n < 80; n++)
                {
                    int before = v.Ring.Count;
                    Frame(s, Step(r));
                    if (v.Ring.Count > before)
                        worst = Math.Max(worst, ((Vector3)v.Last - c.transform.localPosition).magnitude);
                }
                Assert.That(s.Context.Layers.TryFind(v.Slot.Shown.Key, out LayerSlot _), Is.False, "a direct write's own slot");
                Assert.That(v.Ring.Count, Is.GreaterThan(10), "found through the binding");
                Assert.That(worst, Is.LessThan(1e-5f));
                Assert.That(v.Ring.Differs, Is.False);
            }
        }

        // ---- T5 the Persist group

        // a Tween + Punch + Tween on one slot: from the play's start; a Set under a Punch on a light; a script that moves the
        // target each Update — absorbed by an offset group, a difference for a Persist group; a toggle's 0 · 1; a backward
        // Tween from its own start
        [Test]
        public void RG05_PersistStartsFromThePlay()
        {
            var r = new System.Random(12);
            {
                GameObject c = Go("M");
                c.transform.localPosition = new Vector3(0.5f, 0f, 0f);
                FeedbackPlayer p = Player(c, Timed(Pos(Tween(TweenMode.To, new Vector4(1f, 2f, 3f, 0f))), 0.4f), Timed(Pos(Punch(new Vector4(0f, 0.5f, 0f, 0f), 3f)), 0.5f, 0.2f),
                    Timed(Pos(Tween(TweenMode.To, new Vector4(-1f, 0f, 0f, 0f))), 0.3f, 0.5f));
                FeedbackScheduler s = Live();
                Bind(p, s);
                View v = Watch(p);
                Vector3 rest = c.transform.localPosition;
                p.Play(PlayArgs.Default.WithSeed(3u));
                v.Slot = Line(p, v.Follow);
                Assert.That(v.Slot.PersistGroup, Is.True);
                float worst = 0f;
                for (int n = 0; n < 120; n++)
                {
                    int before = v.Ring.Count;
                    Frame(s, Step(r));
                    if (v.Ring.Count > before)
                        worst = Math.Max(worst, ((Vector3)v.Last - (c.transform.localPosition - rest)).magnitude);
                }
                Assert.That(worst, Is.LessThan(1e-5f), "from the play's start");
                Assert.That(v.Ring.Differs, Is.False, "the shadow started where the play did");
            }
            {
                GameObject lg = Go("L");
                var light = lg.AddComponent<Light>();
                light.intensity = 1.5f;
                var set = Timed(new LightIntensityItem { m_Target = Self, m_Motion = new SetMotion { m_Params = new SetParams { m_Value = new Vector4(3f, 0f, 0f, 0f) } } }, 0f, 0.1f);
                FeedbackPlayer p = Player(lg, Timed(new LightIntensityItem { m_Target = Self, m_Motion = Punch(new Vector4(1f, 0f, 0f, 0f), 3f) }, 0.6f), set);
                FeedbackScheduler s = Live();
                Bind(p, s);
                View v = Watch(p);
                p.Play(PlayArgs.Default.WithSeed(4u));
                v.Slot = Line(p, v.Follow);
                float worst = 0f;
                for (int n = 0; n < 100; n++)
                {
                    int before = v.Ring.Count;
                    Frame(s, Step(r));
                    if (v.Ring.Count > before)
                        worst = Math.Max(worst, Math.Abs(v.Last.x - (light.intensity - 1.5f)));
                }
                Assert.That(worst, Is.LessThan(1e-5f), "a Set under a Punch");
                Assert.That(v.Ring.Differs, Is.False);
            }
            {
                GameObject lg = Go("L0");
                var light = lg.AddComponent<Light>();
                light.intensity = 1.5f;
                var jump = Timed(new LightIntensityItem { m_Target = Self, m_Motion = new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.FromTo, m_From = new Vector4(3f, 0f, 0f, 0f), m_To = new Vector4(1f, 0f, 0f, 0f), m_Loops = 1 } } }, 0.4f);
                FeedbackPlayer p = Player(lg, Timed(new LightIntensityItem { m_Target = Self, m_Motion = Punch(new Vector4(1f, 0f, 0f, 0f), 3f) }, 0.6f), jump);
                FeedbackScheduler s = Live();
                Bind(p, s);
                View v = Watch(p);
                p.Play(PlayArgs.Default.WithSeed(4u));
                Assert.That(light.intensity, Is.EqualTo(3f), "the Tween wrote its From at t = 0, inside the Play");
                v.Slot = Line(p, v.Follow);
                float worst = 0f;
                for (int n = 0; n < 60; n++)
                {
                    int before = v.Ring.Count;
                    Frame(s, Step(r));
                    if (v.Ring.Count > before)
                        worst = Math.Max(worst, Math.Abs(v.Last.x - (light.intensity - 1.5f)));
                }
                Assert.That(worst, Is.LessThan(1e-5f), "from the start before the t = 0 write");
                Assert.That(v.Ring.Differs, Is.False);
            }
            foreach (bool persist in new[] { false, true })
            {
                GameObject c = Go("F");
                var items = new List<FeedbackItem> { Timed(Pos(Punch(new Vector4(0.3f, 0f, 0f, 0f), 4f)), 0.5f) };
                if (persist)
                    items.Add(Timed(Pos(Tween(TweenMode.By, new Vector4(0f, 1f, 0f, 0f))), 0.4f));
                FeedbackPlayer p = Player(c, items.ToArray());
                FeedbackScheduler s = Live();
                Bind(p, s);
                View v = Watch(p);
                p.Play(PlayArgs.Default.WithSeed(6u));
                v.Slot = Line(p, v.Follow);
                Vector3 moved = c.transform.localPosition;
                float worst = 0f;
                for (int n = 0; n < 80; n++)
                {
                    int before = v.Ring.Count;
                    Frame(s, Step(r), () =>
                    {
                        moved += new Vector3(0.01f, 0f, 0f);
                        c.transform.localPosition = moved;
                    });
                    if (!persist && v.Ring.Count > before)
                        worst = Math.Max(worst, ((Vector3)v.Last - (c.transform.localPosition - moved)).magnitude);
                }
                if (!persist)
                {
                    Assert.That(worst, Is.LessThan(1e-5f), "an offset group takes each frame's base");
                    Assert.That(v.Ring.Differs, Is.False, "the script's move is absorbed");
                }
                else
                    Assert.That(v.Ring.Differs, Is.True, "a Persist group shows the script's move");
            }
            {
                GameObject owner = Go("T");
                GameObject kid = Go("Kid", owner.transform);
                kid.SetActive(false);
                FeedbackPlayer p = Player(owner, Timed(new SetActiveItem { m_Target = Child("Kid"), m_Motion = new BlinkMotion { m_Params = BlinkParams.Default } }, 0.7f));
                FeedbackScheduler s = Live();
                Bind(p, s);
                View v = Watch(p);
                p.Play(PlayArgs.Default.WithSeed(2u));
                v.Slot = Line(p, v.Follow);
                Assert.That(v.Slot.Toggle, Is.True);
                int wrong = 0, seen = 0;
                bool off = false, on = false;
                for (int n = 0; n < 100; n++)
                {
                    int before = v.Ring.Count;
                    Frame(s, 1f / 60f);
                    if (v.Ring.Count <= before)
                        continue;
                    seen++;
                    off |= !kid.activeSelf;
                    on |= kid.activeSelf;
                    if (v.Last.x != (kid.activeSelf ? 1f : 0f))
                        wrong++;
                }
                Assert.That(seen, Is.GreaterThan(20));
                Assert.That(off && on, Is.True, "it blinked");
                Assert.That(wrong, Is.Zero, "a toggle's 0 · 1");
                Assert.That(v.Ring.Differs, Is.False);
            }
            {
                GameObject c = Go("B");
                FeedbackPlayer p = Player(c, Timed(Pos(Tween(TweenMode.To, new Vector4(2f, 0f, 0f, 0f))), 0.3f));
                FeedbackScheduler s = Live();
                Bind(p, s);
                p.Play(PlayArgs.Default.WithSeed(9u));
                for (int n = 0; n < 50; n++)
                    Frame(s, 1f / 60f);
                View v = Watch(p);
                Vector3 before = c.transform.localPosition;
                p.Play(PlayArgs.Default.WithSeed(9u).WithDirection(PlayerDirection.Backward));
                v.Slot = Line(p, v.Follow);
                float worst = 0f;
                for (int n = 0; n < 50; n++)
                {
                    int count = v.Ring.Count;
                    Frame(s, 1f / 60f);
                    if (v.Ring.Count > count)
                        worst = Math.Max(worst, ((Vector3)v.Last - (c.transform.localPosition - before)).magnitude);
                }
                Assert.That(c.transform.localPosition.x, Is.EqualTo(0f).Within(1e-4f), "back to the origin");
                Assert.That(worst, Is.LessThan(1e-5f), "a backward Persist from its own start");
                Assert.That(v.Ring.Differs, Is.False);
            }
        }

        // ---- T6 other writers

        // another player on the same light: the difference from the line is that player's own part; a LateUpdate write is
        // absorbed by a LateApply slot (a light) and kept by an Update one (a toggle); a Motion Driver on the same target
        [Test]
        public void RG06_OtherWriters()
        {
            List<float> Run(bool other, out View view)
            {
                GameObject lg = Go("L");
                var light = lg.AddComponent<Light>();
                light.intensity = 1f;
                FeedbackPlayer p = Player(lg, Timed(new LightIntensityItem { m_Target = Self, m_Motion = Punch(new Vector4(1f, 0f, 0f, 0f), 3f) }, 0.6f));
                FeedbackScheduler s = Live();
                Bind(p, s);
                FeedbackPlayer po = null;
                if (other)
                {
                    po = Player(Go("O"), Timed(new LightIntensityItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = light, m_Path = "", m_Anchor = "" }, m_Motion = Punch(new Vector4(2f, 0f, 0f, 0f), 2f) }, 0.6f));
                    Bind(po, s);
                }
                view = Watch(p);
                View item = Watch(p);
                p.Play(PlayArgs.Default.WithSeed(8u));
                po?.Play(PlayArgs.Default.WithSeed(9u));
                view.Slot = Line(p, view.Follow);
                item.Item = ItemLine(p, item.Follow, 0);
                var values = new List<float>();
                var r = new System.Random(16);
                for (int n = 0; n < 60; n++)
                {
                    Frame(s, Step(r));
                    values.Add(light.intensity);
                }
                Assert.That(item.Ring.Differs, Is.False, "the item's own values are its own");
                Assert.That(item.ComparedCount, Is.GreaterThan(5));
                return values;
            }
            List<float> alone = Run(false, out View a);
            List<float> both = Run(true, out View b);
            Assert.That(a.Ring.Differs, Is.False);
            Assert.That(b.Ring.Differs, Is.True, "another player writes the light");
            float worst = 0f;
            int compared = 0;
            for (int i = 0; i < b.Ring.Count; i++)
            {
                if (!b.Ring.Compared(i, out Vector4 line))
                    continue;
                compared++;
                float theirs = both[i] - alone[i];
                worst = Math.Max(worst, Math.Abs(b.Ring.Value(i).x - line.x - theirs));
            }
            Assert.That(compared, Is.GreaterThan(10));
            Assert.That(worst, Is.LessThan(1e-4f), "the difference is the other's part");

            {
                GameObject lg = Go("L2");
                var light = lg.AddComponent<Light>();
                light.intensity = 1f;
                FeedbackPlayer p = Player(lg, Timed(new LightIntensityItem { m_Target = Self, m_Motion = Punch(new Vector4(1f, 0f, 0f, 0f), 3f) }, 0.6f));
                FeedbackScheduler s = Live();
                Bind(p, s);
                View v = Watch(p);
                p.Play(PlayArgs.Default.WithSeed(7u));
                v.Slot = Line(p, v.Follow);
                for (int n = 0; n < 60; n++)
                    Frame(s, 1f / 60f, null, n == 10 ? (Action)(() => light.intensity = 9f) : null);
                Assert.That(v.Ring.Differs, Is.False, "a LateApply slot takes the write as its base");
            }
            {
                GameObject owner = Go("T");
                GameObject kid = Go("Kid", owner.transform);
                FeedbackPlayer p = Player(owner, Timed(new SetActiveItem { m_Target = Child("Kid"), m_Motion = new BlinkMotion { m_Params = BlinkParams.Default } }, 0.7f));
                FeedbackScheduler s = Live();
                Bind(p, s);
                View v = Watch(p);
                p.Play(PlayArgs.Default.WithSeed(2u));
                v.Slot = Line(p, v.Follow);
                for (int n = 0; n < 40; n++)
                    Frame(s, 1f / 60f, null, n == 6 ? (Action)(() => kid.SetActive(!kid.activeSelf)) : null);
                Assert.That(v.Ring.Differs, Is.True, "an Update slot shows a LateUpdate write");
            }
            {
                GameObject c = Go("MD");
                FeedbackPlayer p = Player(c, Timed(Pos(Punch(new Vector4(0.3f, 0f, 0f, 0f))), 0.5f));
                FeedbackScheduler s = Live();
                Bind(p, s);
                var driver = c.AddComponent<MotionDriver>();
                driver.m_Mode = MotionDriverMode.Loop;
                driver.m_Item = new PositionItem { m_Target = Self, m_Motion = new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(0f, 0.2f, 0f, 0f), m_Frequency = 2f } } };
                driver.Bind(s);
                View v = Watch(p);
                p.Play(PlayArgs.Default.WithSeed(1u));
                v.Slot = Line(p, v.Follow);
                for (int n = 0; n < 50; n++)
                    Frame(s, 1f / 60f);
                driver.Bind(null);
                Assert.That(v.Ring.Differs, Is.True, "a Motion Driver on the same target");
            }
        }

        // ---- T7 the item's record

        // an item's dot is what it gave its target that frame (each property read back) and its line's value there: an offset, an
        // override's weight, a Persist's value, a camera shake's two channels, a toggle, an alpha (a binding that changes units)
        [Test]
        public void RG07_TheItemRecord()
        {
            void Check(FeedbackPlayer p, FeedbackScheduler s, int channel, Func<Vector4, float> wrong, float within, int frames = 70)
            {
                View v = Watch(p);
                v.Channel = channel;
                p.Play(PlayArgs.Default.WithSeed(13u));
                v.Item = ItemLine(p, v.Follow, 0, channel);
                float worst = 0f;
                for (int n = 0; n < frames; n++)
                {
                    int before = v.Ring.Count;
                    Frame(s, 1f / 60f);
                    if (v.Ring.Count > before)
                        worst = Math.Max(worst, wrong(v.Last));
                }
                Assert.That(v.Ring.Count, Is.GreaterThan(10), p.name);
                Assert.That(worst, Is.LessThan(within), p.name + " against the scene");
                Assert.That(v.ComparedCount, Is.GreaterThan(5), p.name);
                Assert.That(v.Ring.Differs, Is.False, p.name + " against its line");
            }
            {
                GameObject c = Go("Offset");
                FeedbackPlayer p = Player(c, Timed(Pos(Shake(0.2f, 9f)), 0.8f));
                FeedbackScheduler s = Live();
                Bind(p, s);
                Check(p, s, 0, d => ((Vector3)d - c.transform.localPosition).magnitude, 1e-5f);
            }
            {
                GameObject lg = Go("Override");
                var light = lg.AddComponent<Light>();
                light.intensity = 1f;
                FeedbackPlayer p = Player(lg, Timed(new LightIntensityItem { m_Target = Self, m_Motion = new FlashMotion { m_Params = new FlashParams { m_Target = new Vector4(5f, 0f, 0f, 0f), m_Attack = 0.1f, m_Hold = 0.2f, m_Release = 0.3f } } }, 0.8f));
                FeedbackScheduler s = Live();
                Bind(p, s);
                Check(p, s, 0, d => Math.Abs(1f + (5f - 1f) * d.x - light.intensity), 1e-4f);
            }
            {
                GameObject c = Go("Persist");
                c.transform.localPosition = new Vector3(0.5f, 0f, 0f);
                FeedbackPlayer p = Player(c, Timed(Pos(Tween(TweenMode.To, new Vector4(1f, 2f, 3f, 0f))), 0.6f));
                FeedbackScheduler s = Live();
                Bind(p, s);
                Check(p, s, 0, d => ((Vector3)d - c.transform.localPosition).magnitude, 1e-5f, 34);
            }
            foreach (int channel in new[] { 0, 1 })
            {
                GameObject cam = Go("Shake" + channel);
                cam.AddComponent<Camera>();
                var shake = new CameraShakeItem { m_Target = Self, m_RotationAmplitude = new Vector3(3f, 3f, 3f), m_Motion = Shake(0.3f, 15f) };
                shake.m_Timing.m_Duration = 0.7f;
                FeedbackPlayer p = Player(cam, shake);
                FeedbackScheduler s = Live();
                Bind(p, s);
                if (channel == 0)
                    Check(p, s, 0, d => ((Vector3)d - cam.transform.localPosition).magnitude, 1e-5f);
                else
                    Check(p, s, 1, d => Quaternion.Angle(Quaternion.Euler(d.x, d.y, d.z), cam.transform.localRotation), 2e-3f);
            }
            {
                GameObject owner = Go("Toggle");
                GameObject kid = Go("Kid", owner.transform);
                FeedbackPlayer p = Player(owner, Timed(new SetActiveItem { m_Target = Child("Kid"), m_Motion = new BlinkMotion { m_Params = BlinkParams.Default } }, 0.7f));
                FeedbackScheduler s = Live();
                Bind(p, s);
                Check(p, s, 0, d => d.x == (kid.activeSelf ? 1f : 0f) ? 0f : 1f, 0.5f, 40);
            }
            {
                GameObject go = Go("Alpha");
                var sprite = go.AddComponent<SpriteRenderer>();
                sprite.color = new Color(1f, 1f, 1f, 0.8f);
                var bump = new SpringMotion { m_Params = new SpringParams { m_Mode = SpringMode.Bump, m_Value = new Vector4(-0.5f, 0f, 0f, 0f), m_Frequency = 4f, m_Damping = 0.3f, m_Settle = 0.001f } };
                FeedbackPlayer p = Player(go, Timed(new SpriteAlphaItem { m_Target = Self, m_Motion = bump }, 0.6f));
                FeedbackScheduler s = Live();
                Bind(p, s);
                Check(p, s, 0, d => Math.Abs(0.8f + d.x - sprite.color.a), 1e-5f);
            }
        }

        // ---- T8 the play's inputs

        // the shadow and the lines are of the followed play: a second play's history skips (Max Plays · cooldown · chance),
        // random lengths and outputs, backward, Opposite, Speed 2, a block's repeats and a background item, a pause and two seeks,
        // a frame whose two clocks differ; overlapping plays — the newest followed, the dots cleared
        [Test]
        public void RG08_ThePlaysInputs()
        {
            var r = new System.Random(9);
            {
                GameObject c = Go("Second");
                var once = Timed(Pos(Punch(new Vector4(0.5f, 0f, 0f, 0f))), 0.3f);
                once.m_Conditions.m_MaxPlays = 1;
                var cool = Timed(Pos(Shake(0.1f, 9f)), 0.4f, 0.05f);
                cool.m_Conditions.m_Cooldown = 100f;
                var chance = Timed(Pos(Punch(new Vector4(0f, 0.4f, 0f, 0f), 3f)), 0.35f);
                chance.m_Conditions.m_Chance = 50f;
                var random = Timed(Pos(Shake(0.15f, 7f), PositionSpace.World), 0.5f);
                random.m_Random.m_RandomizeDuration = true;
                random.m_Random.m_RandomizeOutput = true;
                FeedbackPlayer p = Player(c, once, cool, chance, random);
                p.m_RandomizeDuration = true;
                FeedbackScheduler s = Live();
                Bind(p, s);
                View v = Watch(p);
                View item = Watch(p);
                item.ListIndex = 3;
                Vector3 rest = c.transform.localPosition;
                p.Play(PlayArgs.Default.WithSeed(21u));
                for (int n = 0; n < 150; n++)
                    Frame(s, Step(r));
                p.Play(PlayArgs.Default.WithSeed(22u));
                Assert.That(v.Follow.Copy.Resolved.ItemSkipped(0), Is.True, "Max Plays at the second play");
                Assert.That(v.Follow.Copy.Resolved.ItemSkipped(1), Is.True, "the cooldown");
                v.Slot = Line(p, v.Follow);
                item.Item = ItemLine(p, item.Follow, 3);
                Assert.That(item.Item.Skip, Is.Null);
                float worst = 0f;
                for (int n = 0; n < 150; n++)
                {
                    int before = v.Ring.Count;
                    Frame(s, Step(r));
                    if (v.Ring.Count > before)
                        worst = Math.Max(worst, ((Vector3)v.Last - (c.transform.localPosition - rest)).magnitude);
                }
                Assert.That(worst, Is.LessThan(1e-5f));
                Assert.That(v.ComparedCount, Is.GreaterThan(5));
                Assert.That(v.Ring.Differs, Is.False, "the shadow skips what the play skipped");
                Assert.That(item.Ring.Differs, Is.False, "the item's line is of that play");
                Assert.That(item.ComparedCount, Is.GreaterThan(5));
                Assert.That(v.Begins, Is.EqualTo(2));
            }
            {
                GameObject c = Go("Turns");
                var opposite = Timed(Pos(Punch(new Vector4(0.2f, 0.3f, 0f, 0f))), 0.3f, 0.05f, 2, 0.1f);
                opposite.m_PlayDirection = ItemPlayDirection.Opposite;
                var background = Timed(Pos(Shake(0.05f, 6f)), 0.4f);
                background.m_Timing.m_Background = true;
                FeedbackPlayer p = Player(c, Timed(Pos(Shake(0.2f, 10f)), 0.6f), opposite, new LoopStartItem(), Timed(Pos(Punch(new Vector4(0f, 0f, 0.2f, 0f))), 0.15f), new LoopEndItem { m_LoopCount = 3, m_LoopGap = 0.05f }, background);
                FeedbackScheduler s = Live();
                Bind(p, s);
                View v = Watch(p);
                View item = Watch(p);
                item.ListIndex = 1;
                Vector3 rest = c.transform.localPosition;
                foreach ((PlayerDirection direction, float speed) in new[] { (PlayerDirection.Backward, 1f), (PlayerDirection.Forward, 2f) })
                {
                    p.m_Speed = speed;
                    p.Play(PlayArgs.Default.WithSeed(31u).WithDirection(direction));
                    v.Slot = Line(p, v.Follow, null, null, 1.5);
                    item.Item = ItemLine(p, item.Follow, 1);
                    float worst = 0f;
                    for (int n = 0; n < 160; n++)
                    {
                        int before = v.Ring.Count;
                        Frame(s, Step(r));
                        if (v.Ring.Count > before)
                            worst = Math.Max(worst, ((Vector3)v.Last - (c.transform.localPosition - rest)).magnitude);
                    }
                    Assert.That(worst, Is.LessThan(1e-5f), direction.ToString());
                    Assert.That(v.Ring.Differs, Is.False, direction + " · the shadow");
                    Assert.That(item.Ring.Differs, Is.False, direction + " · the item's line");
                    Assert.That(item.ComparedCount, Is.GreaterThan(3), direction.ToString());
                }
            }
            {
                GameObject c = Go("Seek");
                FeedbackPlayer p = Player(c, Timed(Pos(Shake(0.2f, 10f)), 1f), Timed(Pos(Punch(new Vector4(0.3f, 0f, 0f, 0f))), 0.4f, 0.3f));
                FeedbackScheduler s = Live();
                Bind(p, s);
                View v = Watch(p);
                Vector3 rest = c.transform.localPosition;
                p.Play(PlayArgs.Default.WithSeed(41u));
                v.Slot = Line(p, v.Follow);
                float worst = 0f;
                bool back = false, on = false;
                for (int n = 0; n < 140; n++)
                {
                    if (n == 8)
                        p.Pause();
                    if (n == 14)
                        p.Resume();
                    if (n == 20)
                        back = p.Runtime.SeekAt(TimelineTime.FromSeconds(0.1));
                    if (n == 30)
                        on = p.Runtime.SeekAt(TimelineTime.FromSeconds(0.7));
                    int before = v.Ring.Count;
                    Frame(s, Step(r));
                    if (v.Ring.Count > before)
                        worst = Math.Max(worst, ((Vector3)v.Last - (c.transform.localPosition - rest)).magnitude);
                }
                Assert.That(back && on, Is.True, "the seeks happened");
                Assert.That(v.Follow.Reseeks, Is.GreaterThanOrEqualTo(2), "the shadow sought after them");
                Assert.That(worst, Is.LessThan(1e-5f));
                Assert.That(v.Ring.Differs, Is.False);
            }
            {
                GameObject c = Go("Clocks");
                var unscaled = Timed(Pos(Shake(0.2f, 8f)), 0.6f);
                unscaled.m_Timing.m_Clock = ItemClock.Unscaled;
                FeedbackPlayer p = Player(c, unscaled, Timed(Pos(Punch(new Vector4(0f, 0.3f, 0f, 0f))), 0.5f));
                FeedbackScheduler s = Live();
                Bind(p, s);
                View v = Watch(p);
                View item = Watch(p);
                Vector3 rest = c.transform.localPosition;
                p.Play(PlayArgs.Default.WithSeed(51u));
                v.Slot = Line(p, v.Follow);
                item.Item = ItemLine(p, item.Follow, 0);
                float worst = 0f;
                for (int n = 0; n < 120; n++)
                {
                    int before = v.Ring.Count;
                    Frame(s, 1f / 60f, null, null, 0.5f / 60f);
                    if (v.Ring.Count > before)
                        worst = Math.Max(worst, ((Vector3)v.Last - (c.transform.localPosition - rest)).magnitude);
                }
                Assert.That(worst, Is.LessThan(1e-5f));
                Assert.That(Enumerable.Range(0, v.Ring.Count).Count(i => v.Ring.Off(i)), Is.GreaterThan(10), "two clocks at two rates: off the nominal axis");
                Assert.That(v.Ring.Differs, Is.False, "the shadow moves by the frame's two clocks");
                Assert.That(item.Ring.Count, Is.GreaterThan(10));
                Assert.That(item.Ring.Differs, Is.False, "an item's nominal line is not compared off the axis");
            }
            {
                GameObject c = Go("Overlap");
                FeedbackPlayer p = Player(c, Timed(Pos(Punch(new Vector4(0.3f, 0f, 0f, 0f))), 0.5f));
                p.m_Policy = PlayPolicy.Overlap;
                p.m_MaxOverlap = 2;
                FeedbackScheduler s = Live();
                Bind(p, s);
                View v = Watch(p);
                p.Play(PlayArgs.Default.WithSeed(61u));
                v.Slot = Line(p, v.Follow);
                for (int n = 0; n < 20; n++)
                    Frame(s, 1f / 60f);
                long first = v.Follow.Serial;
                int firstCount = v.Ring.Count;
                p.Play(PlayArgs.Default.WithSeed(62u));
                Assert.That(v.Follow.Serial, Is.GreaterThan(first), "the newest play");
                Assert.That(v.Ring.Count, Is.Zero, "the dots cleared");
                Assert.That(firstCount, Is.GreaterThan(5));
                for (int n = 0; n < 10; n++)
                    Frame(s, 1f / 60f);
                Assert.That(v.Ring.Count, Is.EqualTo(10), "this play's frames only");
            }
        }

        // ---- T9 the context's settings

        // a Test context with Camera ×0.5 and a live gate's ×0.25 on Motion: the shadow and the item's line take the play's
        // context, not the game's — no warning
        [Test]
        public void RG09_ThePlaysContext()
        {
            SettingsValues settings = SettingsValues.Load(ContextKind.Test);
            settings.Scale[(int)FeedbackCategory.Camera] = 0.5f;
            FeedbackScheduler s = Live(settings);
            s.Context.Categories.SetScale(FeedbackCategory.Motion, 0.25f);
            GameObject cam = Go("Cam");
            cam.AddComponent<Camera>();
            var shake = new CameraShakeItem { m_Target = Self, m_Motion = Shake(0.3f, 12f) };
            shake.m_Timing.m_Duration = 0.6f;
            GameObject box = Go("Box");
            FeedbackPlayer p = Player(cam, shake);
            FeedbackPlayer pb = Player(box, Timed(Pos(Punch(new Vector4(0.4f, 0f, 0f, 0f))), 0.5f));
            Bind(p, s);
            Bind(pb, s);
            View v = Watch(p);
            View item = Watch(p);
            View boxItem = Watch(pb);
            p.Play(PlayArgs.Default.WithSeed(3u));
            pb.Play(PlayArgs.Default.WithSeed(4u));
            v.Slot = Line(p, v.Follow);
            item.Item = ItemLine(p, item.Follow, 0);
            boxItem.Item = ItemLine(pb, boxItem.Follow, 0);
            Assert.That(item.Item.Scale, Is.EqualTo(0.5f), "the play's context's Camera");
            Assert.That(boxItem.Item.Scale, Is.EqualTo(0.25f), "the live gate's Motion");
            for (int n = 0; n < 60; n++)
                Frame(s, 1f / 60f);
            Assert.That(v.ComparedCount, Is.GreaterThan(10));
            Assert.That(v.Ring.Differs || item.Ring.Differs || boxItem.Ring.Differs, Is.False);
        }

        // ---- T14 the end frame

        // the frame the play ends is taken once (the base back), none after; the next play of that record is followed again
        [Test]
        public void RG14_TheEndFrame()
        {
            GameObject c = Go("End");
            FeedbackPlayer p = Player(c, Timed(Pos(Punch(new Vector4(0.3f, 0f, 0f, 0f), 3f)), 0.1f));
            FeedbackScheduler s = Live();
            Bind(p, s);
            View v = Watch(p);
            p.Play(PlayArgs.Default.WithSeed(1u));
            v.Slot = Line(p, v.Follow);
            for (int n = 0; n < 12; n++)
                Frame(s, Dt);
            Assert.That(v.Ring.Count, Is.EqualTo(12), "eleven running frames and the end");
            Assert.That(v.Follow.Ended, Is.True);
            Assert.That(v.Last.x, Is.EqualTo(0f), "the end frame: the base back");
            Assert.That(v.Ring.Seconds(v.Ring.Count - 1), Is.EqualTo(0.1).Within(1e-6), "at the play's end");
            for (int n = 0; n < 5; n++)
                Frame(s, Dt);
            Assert.That(v.Ring.Count, Is.EqualTo(12), "none after");
            p.Play(PlayArgs.Default.WithSeed(2u));
            Assert.That(v.Begins, Is.EqualTo(2), "the next play");
            Frame(s, Dt);
            Assert.That(v.Ring.Count, Is.EqualTo(1));
        }

        // ---- T15 plays started inside a frame

        // a Play in Update (after the Revert) and in LateUpdate (after the Update stage) does not move that frame: the shadow
        // does not either — no seek, the same values
        [Test]
        public void RG15_PlaysStartedInsideAFrame()
        {
            GameObject c = Go("Inside");
            FeedbackPlayer p = Player(c, Timed(Pos(Shake(0.2f, 10f)), 0.3f), Timed(Pos(Punch(new Vector4(0.3f, 0f, 0f, 0f))), 0.2f, 0.05f));
            FeedbackScheduler s = Live();
            Bind(p, s);
            View v = Watch(p);
            Vector3 rest = c.transform.localPosition;
            var r = new System.Random(19);
            float worst = 0f;
            int reseeks = 0;
            for (int n = 0; n < 80; n++)
            {
                Action update = n == 2 ? (Action)(() => p.Play(PlayArgs.Default.WithSeed(51u))) : null;
                Action late = n == 40 ? (Action)(() => p.Play(PlayArgs.Default.WithSeed(52u))) : null;
                int before = v.Ring.Count;
                Frame(s, Step(r), update, late);
                if (n == 2 || n == 40)
                {
                    Assert.That(v.Ring.Count, Is.Zero, "the frame it started in: no move");
                    v.Slot = Line(p, v.Follow);
                }
                if (v.Ring.Count > before)
                    worst = Math.Max(worst, ((Vector3)v.Last - (c.transform.localPosition - rest)).magnitude);
                if (v.Follow != null)
                    reseeks = Math.Max(reseeks, v.Follow.Reseeks);
            }
            Assert.That(v.Begins, Is.EqualTo(2));
            Assert.That(reseeks, Is.Zero, "the shadow kept the frames");
            Assert.That(worst, Is.LessThan(1e-5f));
            Assert.That(v.Ring.Differs, Is.False);
        }

        // ---- T17 a channel's shake

        // a channel shake on two receivers: the item's dots are its values (on each receiver the same) and its line's, the
        // receivers counted; the slot graph has no group for it
        [Test]
        public void RG17_AChannelShake()
        {
            FeedbackScheduler s = Live();
            var cams = new List<GameObject>();
            for (int i = 0; i < 2; i++)
            {
                GameObject cam = Go("Cam" + i);
                cam.AddComponent<Camera>();
                var receiver = cam.AddComponent<CameraShakeReceiver>();
                receiver.m_Channel = ChannelRef.Of(2);
                receiver.Bind(s);
                cams.Add(cam);
            }
            var shake = new CameraShakeItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Channel, m_Channel = ChannelRef.Of(2), m_Path = "", m_Anchor = "" }, m_Motion = Shake(0.3f, 12f) };
            shake.m_Timing.m_Duration = 0.6f;
            FeedbackPlayer p = Player(Go("Source"), shake);
            Bind(p, s);
            View item = Watch(p);
            p.Play(PlayArgs.Default.WithSeed(5u));
            item.Item = ItemLine(p, item.Follow, 0);
            var binding = (CameraShakeBinding)p.Runtime.Binding(0);
            Assert.That(binding.Receivers(item.Follow.Record), Is.EqualTo(2));
            float worst = 0f;
            for (int n = 0; n < 50; n++)
            {
                int before = item.Ring.Count;
                Frame(s, 1f / 60f);
                if (item.Ring.Count > before)
                {
                    foreach (GameObject cam in cams)
                        worst = Math.Max(worst, ((Vector3)item.Last - cam.transform.localPosition).magnitude);
                }
            }
            Assert.That(item.Ring.Count, Is.GreaterThan(10));
            Assert.That(worst, Is.LessThan(1e-5f), "each receiver's value");
            Assert.That(item.Ring.Differs, Is.False);
            Assert.That(item.ComparedCount, Is.GreaterThan(5));
            SlotGraphModel slot = Line(p, item.Follow);
            Assert.That(slot.Groups, Is.Empty, "no group for a channel");
            Assert.That(slot.NotDrawn, Is.Not.Empty);
        }

        // ---- U-9 off the nominal axis

        // a play that waits for a signal: its place goes on while it waits, the shadow's with it; the signal the live play takes
        // ends the shadow's wait too — after it still compared, the dots the scene's, no warning
        [Test]
        public void RG18_AfterAWaitOffTheAxis()
        {
            GameObject c = Go("Wait");
            var wait = new WaitForSignalItem();
            wait.m_Timing.m_Rule = StartRule.AfterPrevious;
            var after = Timed(Pos(Shake(0.2f, 9f)), 0.4f);
            after.m_Timing.m_Rule = StartRule.AfterPrevious;
            FeedbackPlayer p = Player(c, Timed(Pos(Punch(new Vector4(0.3f, 0f, 0f, 0f))), 0.2f), wait, after);
            FeedbackScheduler s = Live();
            Bind(p, s);
            View v = Watch(p);
            Vector3 rest = c.transform.localPosition;
            p.Play(PlayArgs.Default.WithSeed(3u));
            v.Slot = Line(p, v.Follow);
            float worst = 0f;
            int beforeSignal = 0;
            for (int n = 0; n < 90; n++)
            {
                if (n == 30)
                {
                    beforeSignal = v.Ring.Count;
                    p.Signal();
                }
                int before = v.Ring.Count;
                Frame(s, 1f / 60f);
                if (v.Ring.Count > before)
                    worst = Math.Max(worst, ((Vector3)v.Last - (c.transform.localPosition - rest)).magnitude);
            }
            Assert.That(beforeSignal, Is.GreaterThan(5), "the frames before the wait");
            Assert.That(v.Ring.Count, Is.GreaterThan(beforeSignal + 10), "the frames after it");
            Assert.That(worst, Is.LessThan(1e-5f), "the dots are the scene's");
            int compared = Enumerable.Range(beforeSignal, v.Ring.Count - beforeSignal).Count(i => v.Ring.Compared(i, out _));
            Assert.That(v.Follow.Lost, Is.False, "the shadow keeps up past the wait");
            Assert.That(compared, Is.GreaterThan(10), "compared after the signal");
            Assert.That(v.Ring.Differs, Is.False, "no warning: " + Trace(v));
        }

        static string Trace(View v)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("reseeks ").Append(v.Follow.Reseeks).Append(" lost ").Append(v.Follow.Lost).Append(" | ");
            for (int i = 0; i < v.Ring.Count; i++)
            {
                bool c = v.Ring.Compared(i, out Vector4 line);
                Vector4 d = v.Ring.Value(i);
                sb.Append(i).Append('@').Append(v.Ring.Seconds(i).ToString("0.0000")).Append(v.Ring.Off(i) ? "off" : "").Append(c ? ((d - line).magnitude > 1e-4f ? " X " : " = ") : " - ").Append(d.x.ToString("0.000")).Append('/').Append(line.x.ToString("0.000")).Append("; ");
            }
            return sb.ToString();
        }

        // ---- T10 · T16 the fields

        // folded: not watched; open: watched — the dots, the play's words, no warning; folded again: let go. An edit of the list
        // drops the followed play; a Rebuild wait follows none
        [UnityTest]
        public IEnumerator RG10_TheFields()
        {
            m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            m_Window.position = new Rect(0, 0, 520, 900);
            m_Window.Show();
            GameObject go = Go("Fields");
            FeedbackPlayer p = Player(go, Timed(Pos(Punch(new Vector4(0.4f, 0f, 0f, 0f), 3f)), 0.6f), Timed(Pos(Shake(0.1f, 8f)), 0.6f, 0.1f));
            SessionState.EraseBool(SlotGraphField.FoldPrefix + p.GetEntityId());
            FeedbackScheduler s = Live();
            Bind(p, s);
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.4);
            SlotGraphField field = inspector.Q<SlotGraphField>();
            int views = RecordWatch.ViewCount;
            Assert.That(field.Fold.value, Is.False);
            p.Play(PlayArgs.Default.WithSeed(7u));
            Assert.That(RecordWatch.FollowOf(p, p), Is.Null, "folded: nothing followed");
            field.Fold.value = true;
            yield return InspectorTesting.Wait(0.4);
            Assert.That(RecordWatch.ViewCount, Is.EqualTo(views + 1), "open: watched");
            p.Play(PlayArgs.Default.WithSeed(8u));
            RecordFollow follow = RecordWatch.FollowOf(p, p);
            Assert.That(follow, Is.Not.Null);
            for (int n = 0; n < 40; n++)
                Frame(s, 1f / 60f);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(field.View.Dots.Count, Is.EqualTo(40), "a dot a frame");
            Assert.That(field.Model.Follow, Is.SameAs(follow), "the line of that play");
            Assert.That(field.Notes.text, Does.Contain("Play " + follow.Copy.Serial + " · seed 8"));
            Assert.That(field.Notice.style.display.value, Is.EqualTo(DisplayStyle.None), "no warning: " + FirstDifference(field.View.Dots, field.Model));
            Assert.That(field.DotsChoice, Is.Not.Null, "the header's Dots");
            Assert.That(field.DotsChoice.value, Is.False, "off at first");
            Assert.That(field.View.TooltipAt(field.View.X(field.View.Dots.Seconds(12))), Does.Not.Contain("recorded"), "hidden dots answer no pointer");
            field.DotsChoice.value = true;
            Assert.That(MotionGraphView.DotsOn, Is.True, "the editor's choice");
            Assert.That(field.View.TooltipAt(field.View.X(field.View.Dots.Seconds(12))), Does.Contain("recorded"), "a dot's values under the pointer");

            var so = new SerializedObject(p);
            so.FindProperty(ItemListEdits.ItemsPath).GetArrayElementAtIndex(0).FindPropertyRelative("m_Timing.m_Duration").floatValue = 0.5f;
            so.ApplyModifiedProperties();
            yield return InspectorTesting.Wait(0.4);
            Assert.That(RecordWatch.FollowOf(p, p), Is.Null, "an edit lets the play go");
            Assert.That(field.View.Dots.Count, Is.Zero, "its dots too");

            PlayModeEdits.Report(p, "m_Items.Array.size");
            Assert.That(PlayModeEdits.Pending(p), Is.True);
            p.Play(PlayArgs.Default.WithSeed(9u));
            Assert.That(RecordWatch.FollowOf(p, p), Is.Null, "a Rebuild wait follows none");
            PlayModeEdits.Clear();
            p.Play(PlayArgs.Default.WithSeed(10u));
            Assert.That(RecordWatch.FollowOf(p, p), Is.Not.Null, "after it, the next play");

            field.Fold.value = false;
            yield return InspectorTesting.Wait(0.2);
            Assert.That(RecordWatch.ViewCount, Is.EqualTo(views), "folded: let go");
            Assert.That(RecordWatch.FollowOf(p, p), Is.Null);
            Assert.That(field.View.Dots.Count, Is.Zero);
        }

        // an inspector's rebuild destroys the editors — their serialized objects — before it clears their elements: two open
        // fields of a followed list let go with no read of a gone serialized object, and the follow with them
        [UnityTest]
        public IEnumerator RG10b_TheEditorsGoBeforeTheirFields()
        {
            m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            m_Window.position = new Rect(0, 0, 520, 900);
            m_Window.Show();
            GameObject go = Go("Rebuilt");
            FeedbackPlayer p = Player(go, Timed(Pos(Punch(new Vector4(0.4f, 0f, 0f, 0f), 3f)), 0.6f));
            SessionState.SetBool(SlotGraphField.FoldPrefix + p.GetEntityId(), true);
            FeedbackScheduler s = Live();
            Bind(p, s);
            int views = RecordWatch.ViewCount;
            UnityEditor.Editor first = UnityEditor.Editor.CreateEditor(p);
            UnityEditor.Editor second = UnityEditor.Editor.CreateEditor(p);
            var a = new InspectorElement(first);
            var b = new InspectorElement(second);
            m_Window.rootVisualElement.Add(a);
            m_Window.rootVisualElement.Add(b);
            yield return InspectorTesting.Wait(0.4);
            Assert.That(RecordWatch.ViewCount, Is.EqualTo(views + 2), "both open: watched");
            p.Play(PlayArgs.Default.WithSeed(7u));
            Assert.That(RecordWatch.FollowOf(p, p), Is.Not.Null);

            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
            a.RemoveFromHierarchy();
            b.RemoveFromHierarchy();
            Assert.That(a.Q<SlotGraphField>().panel, Is.Null);
            Assert.That(RecordWatch.ViewCount, Is.EqualTo(views), "both let go");
            Assert.That(RecordWatch.FollowOf(p, p), Is.Null, "the follow with them");
            SessionState.EraseBool(SlotGraphField.FoldPrefix + p.GetEntityId());
        }

        // the header's Dots is the editor's one choice: the record and its comparison go on while hidden, a shown dot bounds
        // the range, another open graph takes the choice at its poll; a player's item has it, a Motion Driver's not
        [UnityTest]
        public IEnumerator RG10c_TheDotsChoice()
        {
            m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            m_Window.position = new Rect(0, 0, 520, 900);
            m_Window.Show();
            GameObject go = Go("Dots");
            FeedbackPlayer p = Player(go, Timed(Pos(Punch(new Vector4(0.4f, 0f, 0f, 0f), 3f)), 0.6f));
            SessionState.SetBool(SlotGraphField.FoldPrefix + p.GetEntityId(), true);
            FeedbackScheduler s = Live();
            Bind(p, s);
            m_Window.rootVisualElement.Add(new InspectorElement(p));
            m_Window.rootVisualElement.Add(new InspectorElement(p));
            yield return InspectorTesting.Wait(0.4);
            List<SlotGraphField> fields = m_Window.rootVisualElement.Query<SlotGraphField>().ToList();
            Assert.That(fields.Count, Is.EqualTo(2));
            SlotGraphField a = fields[0];
            SlotGraphField b = fields[1];
            Assert.That(a.DotsChoice.value, Is.False);
            Assert.That(b.DotsChoice.value, Is.False);
            p.Play(PlayArgs.Default.WithSeed(7u));
            for (int n = 0; n < 20; n++)
                Frame(s, 1f / 60f);
            Assert.That(a.View.Dots.Count, Is.EqualTo(20), "hidden: recorded all the same");
            yield return InspectorTesting.Wait(0.3);
            int builds = b.Builds;

            a.View.Dots.Add(a.View.Dots.Count > 0 ? TimelineTime.FromSeconds(a.View.Dots.Seconds(0)) : 0L, new Vector4(50f, 0f, 0f, 0f), false, false, default, false);
            a.View.Bounds(out _, out float hidden);
            Assert.That(hidden, Is.LessThan(10f), "a hidden dot bounds nothing");
            a.DotsChoice.value = true;
            a.View.Bounds(out _, out float shown);
            Assert.That(shown, Is.GreaterThanOrEqualTo(50f), "a shown dot bounds the range");
            Assert.That(b.DotsChoice.value, Is.False, "until its poll");
            yield return InspectorTesting.Wait(0.3);
            Assert.That(b.DotsChoice.value, Is.True, "the other graph takes the choice");
            Assert.That(b.Builds, Is.EqualTo(builds), "at its poll — not built again");

            var item = new ItemBody();
            item.Build(new SerializedObject(p).FindProperty(ItemListEdits.ItemsPath).GetArrayElementAtIndex(0), "xpTURN.Feedback.Tests.Dots." + Guid.NewGuid().ToString("N"), p);
            item.Graph.Build();
            Assert.That(item.Graph.DotsChoice, Is.Not.Null, "a player's item");
            var d = Go("Driver").AddComponent<MotionDriver>();
            d.m_Item = Pos(Punch(new Vector4(0.4f, 0f, 0f, 0f), 3f));
            d.m_Mode = MotionDriverMode.Loop;
            var loop = new ItemBody();
            loop.Build(new SerializedObject(d).FindProperty("m_Item"), "xpTURN.Feedback.Tests.Dots." + Guid.NewGuid().ToString("N"), d);
            loop.Graph.Build();
            Assert.That(loop.Graph.DotsChoice, Is.Null, "a Motion Driver has no record");
            SessionState.EraseBool(SlotGraphField.FoldPrefix + p.GetEntityId());
        }
    }
}
