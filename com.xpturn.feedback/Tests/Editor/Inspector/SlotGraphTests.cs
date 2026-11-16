using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the slot graph: its sum against what the runtime writes frame by frame (offsets in three spaces under a turned and
    // stretched parent, a camera shake's capped group, a product of turns, a scale's product, an override over an offset, a
    // Persist under an offset, start orders), the items alone, the forms, the cap, the groups and their names, lists with
    // other items, the cost, the Unconditional, the player's Graph under the strip, the scrub, the live player untouched and
    // none in a preset's inspector
    public sealed class SlotGraphTests
    {
        const float Dt = 1f / 120f;

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        PreviewBarTestWindow m_Window;
        FeedbackScheduler m_Default;
        Func<double> m_Clock;

        [SetUp]
        public void SetUp()
        {
            PlayerRuntime.ResetSession();
            m_Default = FeedbackScheduler.Default;
            m_Clock = PreviewClock.Source;
        }

        [TearDown]
        public void TearDown()
        {
            FeedbackScheduler.Default = m_Default;
            PreviewSession.Current?.Stop();
            PreviewClock.Source = m_Clock;
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
        }

        static TargetRef Self => new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };

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

        static PositionItem Pos(FeedbackMotion m, float duration, PositionSpace space = PositionSpace.Local, float delay = 0f)
        {
            var item = new PositionItem { m_Target = Self, m_Space = space, m_Motion = m };
            item.m_Timing.m_Duration = duration;
            item.m_Timing.m_Delay = delay;
            return item;
        }

        static RotationItem Rot(FeedbackMotion m, float duration, TransformSpace space = TransformSpace.Local, float delay = 0f, int repeats = 0, float gap = 0f)
        {
            var item = new RotationItem { m_Target = Self, m_Space = space, m_Motion = m };
            item.m_Timing.m_Duration = duration;
            item.m_Timing.m_Delay = delay;
            item.m_Timing.m_RepeatCount = repeats;
            item.m_Timing.m_RepeatGap = gap;
            return item;
        }

        static SettingsValues Caps(float offset, float angle)
        {
            SettingsValues v = SettingsValues.Load(ContextKind.Test);
            v.MaximumShakeOffset = offset;
            v.MaximumShakeAngle = angle;
            return v;
        }

        // the player's scheduler cell
        static object Bound(FeedbackPlayer p) => typeof(FeedbackPlayer).GetField("m_Bound", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(p);

        static GraphInputs Inputs(uint seed, bool free = false) => new GraphInputs(1f, seed, free, false, false);

        // frames ticks at 1/120 on 1/8 as many columns
        static SlotGraphModel Model(FeedbackPlayer p, uint seed, int frames, string group = null, SettingsValues settings = null, bool free = false)
            => SlotGraphModel.Build(p, p.m_Items, p, Inputs(seed, free), frames * (double)Dt, frames / SlotGraphModel.PerColumn, group, null, settings ?? SettingsValues.Load(ContextKind.Test));

        static Vector4 Tick(SlotGraphModel m, int n) => new Vector4(m.Ticks[n * 4], m.Ticks[n * 4 + 1], m.Ticks[n * 4 + 2], m.Ticks[n * 4 + 3]);

        static Quaternion Q(Vector4 v) => new Quaternion(v.x, v.y, v.z, v.w);

        static Vector4 V(Quaternion q) => new Vector4(q.x, q.y, q.z, q.w);

        // the sum's ticks against a live Test scheduler's frames: the start composed with each tick's displacement back into
        // the property's value. rotation: the property read as a quaternion
        float Worst(FeedbackPlayer p, uint seed, int frames, string group, SettingsValues settings, Func<Vector4> read, out SlotGraphModel model)
        {
            Vector4 rest = read();
            model = Model(p, seed, frames, group, settings);
            Assert.That(read(), Is.EqualTo(rest), "the model leaves the scene");
            Assert.That(model.Shown, Is.Not.Null, "a group");
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            s.Context.UseSettings(settings ?? SettingsValues.Load(ContextKind.Test));
            p.Bind(s);
            p.EnsureRuntime();
            p.Play(PlayArgs.Default.WithSeed(seed));
            float worst = 0f;
            for (int n = 1; n <= frames; n++)
            {
                s.Tick(Dt);
                s.RunLateApply();
                Vector4 live = read();
                Vector4 d = Tick(model, n - 1);
                float diff;
                if (model.Composition == Composition.Rotation)
                    diff = Quaternion.Angle(Q(live), Q(rest) * Quaternion.Euler(d.x, d.y, d.z));
                else if (model.Composition == Composition.Multiplicative)
                    diff = (live - new Vector4(rest.x * (1f + d.x), rest.y * (1f + d.y), rest.z * (1f + d.z), 0f)).magnitude;
                else
                {
                    Vector4 composed = rest + d;
                    for (int c = model.Dimension; c < 4; c++)
                        composed[c] = live[c];
                    diff = (live - composed).magnitude;
                }
                worst = Math.Max(worst, diff);
            }
            p.Stop();
            p.ReleaseRuntime();
            return worst;
        }

        FeedbackItem[] CameraItems()
        {
            var a = Pos(Punch(new Vector4(0.3f, 0.2f, 0.1f, 0f), 5f), 0.5f);
            var b = Pos(new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = new Vector4(0.2f, 0.2f, 0.2f, 0f), m_Frequency = 10f } }, 0.6f, PositionSpace.World, 0.05f);
            var r1 = Rot(Punch(new Vector4(20f, 0f, 10f, 0f)), 0.5f);
            var r2 = Rot(new WiggleMotion { m_Params = new WiggleParams { m_Amplitude = new Vector4(10f, 10f, 0f, 0f), m_Frequency = 3f, m_Octaves = 2 } }, 0.3f, TransformSpace.World, 0f, 1);
            var sh = new CameraShakeItem { m_Target = Self, m_RotationAmplitude = new Vector3(3f, 3f, 3f), m_Motion = new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = new Vector4(0.3f, 0.3f, 0.3f, 0f), m_Frequency = 15f } } };
            sh.m_Timing.m_Duration = 0.7f;
            return new FeedbackItem[] { a, b, r1, r2, sh };
        }

        GameObject CameraUnderParent(out GameObject parent)
        {
            parent = Go("P");
            parent.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
            parent.transform.localScale = new Vector3(1f, 2f, 1f);
            GameObject cam = Go("C", parent.transform);
            cam.transform.localPosition = new Vector3(1f, 2f, 3f);
            cam.transform.localRotation = Quaternion.Euler(5f, 10f, 0f);
            cam.AddComponent<Camera>();
            return cam;
        }

        // the sum is what the runtime writes: three spaces under a turned, stretched parent with a capped camera shake, a
        // product of turns, a scale's product, an override over an offset, a Persist under a later offset, start orders
        [Test]
        public void SG01_TheSumIsWhatTheRuntimeWrites()
        {
            GameObject cam = CameraUnderParent(out _);
            FeedbackPlayer p = Player(cam, CameraItems());
            float position = Worst(p, 11u, 96, "C · Position", Caps(0.1f, 1.5f), () => cam.transform.localPosition, out SlotGraphModel m1);
            Assert.That(m1.Shown.Items, Is.EquivalentTo(new[] { 0, 1, 4 }), "Position · Position · the shake's position");
            Assert.That(position, Is.LessThan(1e-5f), "position");
            float turn = Worst(p, 11u, 96, "C · Rotation", Caps(0.1f, 1.5f), () => V(cam.transform.localRotation), out SlotGraphModel m2);
            Assert.That(m2.Composition, Is.EqualTo(Composition.Rotation));
            Assert.That(turn, Is.LessThan(2e-3f), "rotation (degrees)");
            Assert.That(Worst(p, 11u, 96, "C · Position", null, () => cam.transform.localPosition, out _), Is.LessThan(1e-5f), "no caps");

            GameObject cube = Go("Cube");
            cube.transform.localScale = new Vector3(1f, 2f, 0.5f);
            var sc = new ScaleItem { m_Target = Self, m_Motion = Punch(new Vector4(0.3f, 0.3f, 0.3f, 0f), 3f) };
            sc.m_Timing.m_Duration = 0.5f;
            var sq = new SquashAndStretchItem { m_Target = Self, m_Motion = Punch(new Vector4(0.4f, 0f, 0f, 0f), 2f) };
            sq.m_Timing.m_Duration = 0.4f;
            sq.m_Timing.m_Delay = 0.1f;
            FeedbackPlayer ps = Player(cube, sc, sq);
            Assert.That(Worst(ps, 5u, 80, null, null, () => cube.transform.localScale, out SlotGraphModel m3), Is.LessThan(1e-5f), "scale");
            Assert.That(m3.Composition, Is.EqualTo(Composition.Multiplicative));

            GameObject lightGo = Go("L");
            var light = lightGo.AddComponent<Light>();
            light.intensity = 1.5f;
            var fl = new LightIntensityItem { m_Target = Self, m_Motion = new FlashMotion { m_Params = new FlashParams { m_Target = new Vector4(5f, 0f, 0f, 0f), m_Attack = 0.05f, m_Hold = 0.1f, m_Release = 0.2f } } };
            fl.m_Timing.m_Duration = 0.4f;
            fl.m_Timing.m_Delay = 0.1f;
            var pu = new LightIntensityItem { m_Target = Self, m_Motion = Punch(new Vector4(1f, 0f, 0f, 0f), 3f) };
            pu.m_Timing.m_Duration = 0.6f;
            FeedbackPlayer pl = Player(lightGo, pu, fl);
            Assert.That(Worst(pl, 9u, 88, null, null, () => new Vector4(light.intensity, 0f, 0f, 0f), out _), Is.LessThan(1e-5f), "an override over an offset");

            GameObject mover = Go("M");
            mover.transform.localPosition = new Vector3(0.5f, 0f, 0f);
            var tw = Pos(new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.To, m_To = new Vector4(1f, 2f, 3f, 0f), m_Loops = 1 } }, 0.4f);
            var pp = Pos(Punch(new Vector4(0f, 0.5f, 0f, 0f), 3f), 0.5f, PositionSpace.Local, 0.2f);
            FeedbackPlayer pm = Player(mover, tw, pp);
            Assert.That(Worst(pm, 3u, 88, null, null, () => mover.transform.localPosition, out _), Is.LessThan(1e-5f), "a Persist under an offset");

            GameObject spin = Go("S");
            FeedbackPlayer pr = Player(spin, Rot(Punch(new Vector4(30f, 0f, 0f, 0f), 2f), 0.2f, TransformSpace.Local, 0f, 2), Rot(Punch(new Vector4(0f, 30f, 0f, 0f), 2f), 0.2f, TransformSpace.Local, 0.05f, 1, 0.1f));
            Assert.That(Worst(pr, 2u, 88, null, null, () => V(spin.transform.localRotation), out _), Is.LessThan(2e-3f), "start orders of a product");
        }

        // an item's line alone is its list's sum with the others turned off — the same seeds, the same places
        [Test]
        public void SG02_ItemsAloneAreTheirListsAlone()
        {
            GameObject cam = CameraUnderParent(out _);
            FeedbackPlayer p = Player(cam, CameraItems());
            SlotGraphModel m = Model(p, 11u, 96, "C · Position", Caps(0.1f, 1.5f));
            foreach (int index in m.Shown.Items)
            {
                ItemGraphModel alone = m.Alone(p, p.m_Items, p, index, null, out float[] ticks);
                Assert.That(alone, Is.Not.Null);
                var active = p.m_Items.Select(i => i.m_Active).ToArray();
                for (int i = 0; i < p.m_Items.Count; i++)
                    p.m_Items[i].m_Active = i == index;
                SlotGraphModel only = Model(p, 11u, 96, "C · Position", Caps(0.1f, 1.5f));
                for (int i = 0; i < p.m_Items.Count; i++)
                    p.m_Items[i].m_Active = active[i];
                Assert.That(ticks, Is.EqualTo(only.Ticks), "item " + index);
            }
        }

        // the forms: a scale's factor less one, a turn past a full circle unwrapped, the axes' note for two turns and none
        // for one
        [Test]
        public void SG03_Forms()
        {
            GameObject cube = Go("Cube");
            cube.transform.localScale = new Vector3(2f, 2f, 2f);
            FeedbackPlayer ps = Player(cube, new ScaleItem { m_Target = Self, m_Motion = Punch(new Vector4(0.5f, 0f, 0f, 0f)) });
            SlotGraphModel scale = Model(ps, 1u, 64);
            Assert.That(scale.Ticks.Where((v, i) => i % 4 == 0).Max(), Is.GreaterThan(0.2f).And.LessThan(0.6f), "the punch as the factor less one, not × 2");

            GameObject spin = Go("S");
            var turn = Rot(new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.By, m_To = new Vector4(0f, 400f, 0f, 0f), m_Loops = 1 } }, 0.5f);
            FeedbackPlayer pt = Player(spin, turn);
            SlotGraphModel unwrapped = Model(pt, 1u, 80);
            float lastY = 0f, jump = 0f;
            for (int n = 0; n < unwrapped.TickCount; n++)
            {
                float y = unwrapped.Ticks[n * 4 + 1];
                jump = Math.Max(jump, Math.Abs(y - lastY));
                lastY = y;
            }
            Assert.That(lastY, Is.EqualTo(400f).Within(0.05f), "past the full circle");
            Assert.That(jump, Is.LessThan(40f), "no wrap");

            GameObject two = Go("T");
            FeedbackPlayer p2 = Player(two, Rot(Punch(new Vector4(10f, 0f, 0f, 0f)), 0.5f), Rot(Punch(new Vector4(0f, 10f, 0f, 0f)), 0.5f, TransformSpace.Local, 0.05f));
            SlotGraphModel m2 = Model(p2, 1u, 80);
            var alone = m2.Shown.Items.Select(i => { m2.Alone(p2, p2.m_Items, p2, i, null, out float[] t); return t; }).ToList();
            Assert.That(m2.AxesInteract(alone), Is.True, "two turns ±10°");
            GameObject one = Go("O");
            FeedbackPlayer p1 = Player(one, Rot(Punch(new Vector4(10f, 0f, 0f, 0f)), 0.5f), Rot(Punch(new Vector4(5f, 0f, 0f, 0f)), 0.5f, TransformSpace.Local, 0.05f));
            SlotGraphModel m1 = Model(p1, 1u, 80);
            var alone1 = m1.Shown.Items.Select(i => { m1.Alone(p1, p1.m_Items, p1, i, null, out float[] t); return t; }).ToList();
            Assert.That(m1.AxesInteract(alone1), Is.False, "two turns on one axis add");
        }

        // the cap: the capped slot's size component under the limit · the limit × the Camera scale · none without a limit
        [Test]
        public void SG04_TheCap()
        {
            GameObject cam = CameraUnderParent(out _);
            var a = new CameraShakeItem { m_Target = Self, m_Motion = new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = new Vector4(0.4f, 0.4f, 0.4f, 0f), m_Frequency = 12f } } };
            a.m_Timing.m_Duration = 0.6f;
            var b = new CameraShakeItem { m_Target = Self, m_Motion = new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = new Vector4(0.4f, 0.4f, 0.4f, 0f), m_Frequency = 7f } } };
            b.m_Timing.m_Duration = 0.6f;
            FeedbackPlayer p = Player(cam, a, b);
            SettingsValues caps = Caps(0.1f, 0f);
            caps.Scale[(int)FeedbackCategory.Camera] = 0.5f;
            SlotGraphModel m = Model(p, 4u, 80, null, caps);
            Assert.That(m.Capped, Is.True);
            Assert.That(m.Cap, Is.EqualTo(0.05f).Within(1e-6f), "the limit × the Camera scale");
            Assert.That(m.Sum.Dimension, Is.EqualTo(4), "x · y · z · size");
            float size = Enumerable.Range(0, m.TickCount).Max(n => m.Ticks[n * 4 + 3]);
            Assert.That(size, Is.LessThanOrEqualTo(0.05f + 1e-5f).And.GreaterThan(0.02f), "the size under the limit");
            Assert.That(Model(p, 4u, 80).Capped, Is.False, "no limit");
        }

        // groups by the slot: a Position, a Rotate Around and a camera shake on one position · the shake's turn its own ·
        // names · a channel's shake not drawn
        [Test]
        public void SG05_GroupsAndNames()
        {
            GameObject cam = CameraUnderParent(out _);
            GameObject pivot = Go("Pivot");
            OscillateParams orbit = OscillateParams.Default;
            orbit.m_Amplitude = new Vector4(30f, 0f, 0f, 0f);
            var around = new RotateAroundItem { m_Target = Self, m_Motion = new OscillateMotion { m_Params = orbit } };
            around.m_Pivot = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = pivot.transform, m_Path = "", m_Anchor = "" };
            around.m_Timing.m_Duration = 0.4f;
            var sh = new CameraShakeItem { m_Target = Self, m_RotationAmplitude = new Vector3(2f, 2f, 2f), m_Motion = new ShakeMotion { m_Params = ShakeParams.Default } };
            sh.m_Timing.m_Duration = 0.4f;
            var channel = new CameraShakeItem { m_Label = "Impact", m_Motion = new ShakeMotion { m_Params = ShakeParams.Default } };
            channel.m_Target = new TargetRef { m_Mode = TargetRefMode.Channel, m_Path = "", m_Anchor = "" };
            channel.m_Timing.m_Duration = 0.4f;
            FeedbackPlayer p = Player(cam, Pos(Punch(Vector4.one * 0.1f), 0.4f), around, sh, channel);
            SlotGraphModel m = Model(p, 1u, 64);
            Assert.That(m.Groups.Select(g => g.Name), Is.EquivalentTo(new[] { "C · Position", "C · Rotation" }));
            Assert.That(m.Groups.First(g => g.Name == "C · Position").Items, Is.EquivalentTo(new[] { 0, 1, 2 }), "three kinds, one slot");
            Assert.That(m.Groups.First(g => g.Name == "C · Rotation").Items, Is.EquivalentTo(new[] { 2 }));
            Assert.That(m.Shown.Name, Is.EqualTo("C · Position"), "the most items");
            Assert.That(m.NotDrawn, Is.EquivalentTo(new[] { "Impact" }), "a channel's shake");
        }

        // a list with other items: the flow, an event and a chain keep the places — the sum is the runtime's · the event is
        // not called by the graph
        [Test]
        public void SG06_ListsWithOtherItems()
        {
            GameObject go = Go("Mixed");
            int called = 0;
            var events = new UnityEventItem();
            events.m_Event.AddListener(() => called++);
            var hold = new HoldItem();
            hold.m_Timing.m_Duration = 0.1f;
            var after = Pos(Punch(new Vector4(0.5f, 0f, 0f, 0f)), 0.3f);
            after.m_Timing.m_Rule = StartRule.AfterPrevious;
            FeedbackPlayer p = Player(go, Pos(Punch(new Vector4(0f, 0.3f, 0f, 0f)), 0.2f), events, hold, after);
            float worst = Worst(p, 6u, 80, null, null, () => go.transform.localPosition, out SlotGraphModel m);
            Assert.That(worst, Is.LessThan(1e-5f));
            Assert.That(called, Is.EqualTo(1), "called once — by the live play, not by the graph");
        }

        // the cost: a 100-item list's busiest slot at 400 columns
        [Test]
        public void SG07_TheCost()
        {
            GameObject go = Go("Cost");
            var items = new List<FeedbackItem>();
            for (int i = 0; i < 100; i++)
            {
                FeedbackMotion motion = i % 2 == 0 ? Punch(Vector4.one * 0.1f) : (FeedbackMotion)new ShakeMotion { m_Params = ShakeParams.Default };
                var it = Pos(motion, 0.5f, PositionSpace.Local, (i % 10) * 0.2f);
                it.m_Timing.m_RepeatCount = 1;
                items.Add(it);
            }
            FeedbackPlayer p = Player(go, items.ToArray());
            var times = new List<double>();
            for (int round = 0; round < 3; round++)
            {
                var sw = Stopwatch.StartNew();
                SlotGraphModel.Build(p, p.m_Items, p, Inputs(1u), 3.0, 400, null, null, SettingsValues.Load(ContextKind.Test));
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            UnityEngine.Debug.Log("SG07 the sum of 100 items at 400 columns: " + string.Join(" · ", times.Select(t => t.ToString("F1"))) + " ms");
            Assert.That(times[1], Is.LessThan(25.0), "median ms");
        }

        // Unconditional: a Chance 0 item is drawn on a free play and left out otherwise
        [Test]
        public void SG08_Unconditional()
        {
            GameObject go = Go("Chance");
            PositionItem item = Pos(Punch(new Vector4(0.5f, 0f, 0f, 0f)), 0.4f);
            item.m_Conditions.m_Chance = 0f;
            FeedbackPlayer p = Player(go, item, Pos(Punch(new Vector4(0f, 0.1f, 0f, 0f)), 0.4f));
            SlotGraphModel free = Model(p, 1u, 64, null, null, true);
            SlotGraphModel ruled = Model(p, 1u, 64, null, null, false);
            Assert.That(free.Ticks.Where((v, i) => i % 4 == 0).Max(Math.Abs), Is.GreaterThan(0.1f), "free: the Chance item plays");
            Assert.That(ruled.Ticks.Where((v, i) => i % 4 == 0).Max(Math.Abs), Is.LessThan(1e-6f), "ruled: left out");
        }

        // the player's Graph: under the strip, folded and idle at first; open, built once; the groups' popup; the drawing
        // spans the strip's axis; the alone lines arrive; the list's selection makes its item's line strong
        [UnityTest]
        public IEnumerator SG09_ThePlayersGraph()
        {
            m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            m_Window.position = new Rect(0, 0, 520, 900);
            m_Window.Show();
            GameObject cam = CameraUnderParent(out _);
            FeedbackPlayer p = Player(cam, CameraItems());
            SessionState.EraseBool(SlotGraphField.FoldPrefix + p.GetEntityId());
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.4);
            var editorRoot = inspector.Q<SlotGraphField>();
            Assert.That(editorRoot, Is.Not.Null, "a Graph");
            TimelineStrip strip = inspector.Q<TimelineStrip>();
            Assert.That(editorRoot.parent.IndexOf(editorRoot), Is.EqualTo(editorRoot.parent.IndexOf(strip) + 1), "right under the strip");
            Assert.That(editorRoot.Fold.value, Is.False, "folded at first");
            Assert.That(editorRoot.Builds, Is.Zero, "idle while folded");
            var so = new SerializedObject(p);
            so.FindProperty(ItemListEdits.ItemsPath).GetArrayElementAtIndex(0).FindPropertyRelative("m_Timing.m_Duration").floatValue = 0.45f;
            so.ApplyModifiedProperties();
            yield return InspectorTesting.Wait(0.4);
            Assert.That(editorRoot.Builds, Is.Zero, "an edit while folded builds nothing");
            editorRoot.Fold.value = true;
            yield return InspectorTesting.Wait(0.6);
            Assert.That(editorRoot.Builds, Is.EqualTo(1), "open: built once");
            Assert.That(editorRoot.GroupChoice.choices.Count, Is.EqualTo(2), "position · rotation");
            Assert.That(editorRoot.AloneItems.Count, Is.EqualTo(3), "the items alone arrived");
            double t = editorRoot.Model.Span * 0.37;
            float graphX = editorRoot.View.worldBound.x + editorRoot.View.X(t);
            float stripX = strip.Canvas.worldBound.x + strip.X(t);
            Assert.That(graphX, Is.EqualTo(stripX).Within(1.5f), "the strip's axis");
            ItemListView list = inspector.Q<ItemListView>();
            list.List.SetSelection(1);
            yield return InspectorTesting.Wait(0.2);
            int at = editorRoot.AloneItems.IndexOf(1);
            Assert.That(editorRoot.View.Strong[at], Is.True, "the selected item strong");
            Assert.That(editorRoot.View.Strong.Count(s => s), Is.EqualTo(1));
            Assert.That(editorRoot.Notes.text, Does.Contain("Sample seed"));
        }

        // a press scrubs: no session — the bar's play starts and stops at that place; an endless list refuses with words
        [UnityTest]
        public IEnumerator SG10_TheScrub()
        {
            m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            m_Window.position = new Rect(0, 0, 520, 900);
            m_Window.Show();
            GameObject go = Go("Scrub");
            FeedbackPlayer p = Player(go, Pos(Punch(new Vector4(0.5f, 0f, 0f, 0f)), 0.8f), Pos(Punch(new Vector4(0f, 0.2f, 0f, 0f)), 0.5f, PositionSpace.Local, 0.2f));
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, p);
            yield return InspectorTesting.Wait(0.4);
            SlotGraphField field = inspector.Q<SlotGraphField>();
            field.Fold.value = true;
            yield return InspectorTesting.Wait(0.5);
            field.View.Scrubbed(0.3);
            yield return InspectorTesting.Wait(0.2);
            PreviewSession session = PreviewSession.Current;
            Assert.That(session, Is.Not.Null, "a session");
            Assert.That(session.Subject, Is.EqualTo(p));
            Assert.That(session.ScrubState(out _, out _, out double place), Is.EqualTo(SeekRefusal.None));
            Assert.That(place, Is.EqualTo(0.3).Within(1e-3), "at the press");
            field.View.Scrubbed(0.6);
            session.ScrubState(out _, out _, out place);
            Assert.That(place, Is.EqualTo(0.6).Within(1e-3), "the drag follows");
            session.Stop();

            PositionItem endless = Pos(new WiggleMotion { m_Params = WiggleParams.Default }, 0.5f);
            endless.m_Timing.m_Infinite = true;
            p.m_Items.Add(endless);
            EditorUtility.SetDirty(p);
            yield return InspectorTesting.Wait(0.6);
            field = inspector.Q<SlotGraphField>();
            field.View.Scrubbed(0.3);
            yield return InspectorTesting.Wait(0.2);
            Assert.That(field.Notes.text, Does.Contain(PreviewTexts.Of(SeekRefusal.Endless)), "an endless list's words");
        }

        // a graph built while the player plays in its preview leaves the play, the player's cell and its events alone
        [UnityTest]
        public IEnumerator SG11_TheLivePlayerUntouched()
        {
            m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            m_Window.Show();
            GameObject go = Go("Live");
            var light = go.AddComponent<Light>();
            light.intensity = 1.5f;
            var flash = new LightIntensityItem { m_Target = Self, m_Motion = new FlashMotion { m_Params = new FlashParams { m_Target = new Vector4(5f, 0f, 0f, 0f), m_Attack = 0.05f, m_Hold = 1.5f, m_Release = 0.2f } } };
            flash.m_Timing.m_Duration = 2f;
            FeedbackPlayer p = Player(go, Pos(Punch(new Vector4(0.5f, 0f, 0f, 0f)), 2f), Pos(Punch(new Vector4(0f, 0.2f, 0f, 0f)), 2f), flash);
            p.m_Policy = PlayPolicy.Restart;
            // a Variant: a Restart's play stops the player's other lists
            var variant = ScriptableObject.CreateInstance<FeedbackPreset>();
            m_Made.Add(variant);
            variant.m_Items.Add(Pos(Punch(new Vector4(0f, 0f, 0.1f, 0f)), 0.5f));
            p.m_Variants.Add(variant);
            int played = 0;
            p.Played += (_, __) => played++;
            double now = 0;
            PreviewClock.Source = () => now;
            Assert.That(PreviewSession.Start(p, default), Is.EqualTo(PreviewRefusal.None));
            PreviewSession session = PreviewSession.Current;
            for (int i = 0; i < 36; i++)
            {
                now += 1.0 / 120.0;
                session.Update();
            }
            yield return null;
            double before = TimelineTime.ToSeconds(p.Runtime.Position(0));
            p.m_LastPlay = -7.0;
            double lastPlay = p.m_LastPlay;
            long stamp = p.m_Stamp;
            int count = played;
            object bound = Bound(p);
            Assert.That(light.intensity, Is.GreaterThan(4f), "the preview's flash holds");
            SlotGraphModel m = SlotGraphModel.Build(p, p.m_Items, p, GraphInputs.Of(p), 2.0, 100, null, SlotGraphModel.LiveLayers(p, false));
            Assert.That(m.Seeded, Is.True, "the dry slots start from the session's");
            SlotGroup intensity = m.Groups.First(g => g.Items.Contains(2));
            SlotGraphModel lit = SlotGraphModel.Build(p, p.m_Items, p, GraphInputs.Of(p), 2.0, 100, intensity.Name, SlotGraphModel.LiveLayers(p, false));
            float peak = Enumerable.Range(0, lit.TickCount).Max(n => lit.Ticks[n * 4]);
            Assert.That(peak, Is.EqualTo(3.5f).Within(1e-3f), "from the session's start (1.5), not the scene's held flash");
            Assert.That(Bound(p), Is.SameAs(bound), "the player's scheduler");
            Assert.That(PreviewSession.Current, Is.SameAs(session), "the session goes on");
            Assert.That(p.Runtime.Record(0).Active, Is.True, "the play goes on — no Restart from the graph");
            Assert.That(p.m_LastPlay, Is.EqualTo(lastPlay), "the cooldown's cell");
            Assert.That(p.m_Stamp, Is.EqualTo(stamp), "the plays' stamp");
            Assert.That(played, Is.EqualTo(count), "no Played from the graph");
            Assert.That(TimelineTime.ToSeconds(p.Runtime.Position(0)), Is.GreaterThanOrEqualTo(before));
        }

        // play mode: the live categories (a Camera scale the game set) on the cap, the dry slots start from the playing
        // ones, and the play, the player's cell and its events are left alone
        [Test]
        public void SG14_PlayMode()
        {
            var live = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(live);
            SettingsValues settings = Caps(0.2f, 0f);
            live.Context.UseSettings(settings);
            FeedbackScheduler.Default = live;
            FeedbackCategories.SetScale(FeedbackCategory.Camera, 0.5f);
            Assert.That(SlotGraphModel.GameSettings(true).Scale[(int)FeedbackCategory.Camera], Is.EqualTo(0.5f), "the live scale");
            GameObject cam = CameraUnderParent(out _);
            var shake = new CameraShakeItem { m_Target = Self, m_Motion = new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = new Vector4(0.4f, 0.4f, 0.4f, 0f), m_Frequency = 12f } } };
            shake.m_Timing.m_Duration = 1f;
            FeedbackPlayer p = Player(cam, shake, Pos(Punch(new Vector4(0.3f, 0f, 0f, 0f)), 1f));
            int played = 0;
            p.Played += (_, __) => played++;
            p.Bind(live);
            p.EnsureRuntime();
            p.Play(PlayArgs.Default.WithSeed(2u));
            for (int n = 0; n < 20; n++)
            {
                live.Tick(Dt);
                live.RunLateApply();
            }
            double lastPlay = p.m_LastPlay;
            int count = played;
            object bound = Bound(p);
            Vector3 now = cam.transform.localPosition;
            GraphInputs inputs = GraphInputs.Of(p, true);
            settings.Scale[(int)FeedbackCategory.Camera] = 0.5f;
            SlotGraphModel m = SlotGraphModel.Build(p, p.m_Items, p, in inputs, 0.8, 12, null, SlotGraphModel.LiveLayers(p, true), settings);
            Assert.That(inputs.Live, Is.True);
            Assert.That(m.Seeded, Is.True, "from the playing slots");
            Assert.That(m.Cap, Is.EqualTo(0.1f).Within(1e-6f), "the limit × the live Camera scale");
            Assert.That(p.Runtime.Record(0).Active, Is.True, "the play goes on");
            Assert.That(p.m_LastPlay, Is.EqualTo(lastPlay));
            Assert.That(played, Is.EqualTo(count), "no Played from the graph");
            Assert.That(cam.transform.localPosition, Is.EqualTo(now), "the scene as the play left it");
            Assert.That(Bound(p), Is.SameAs(bound), "the player's scheduler");
            p.Stop();
            p.ReleaseRuntime();
        }

        // the list's other items are not prepared in a dry play: a Set Parent leaves the hierarchy as it was
        [Test]
        public void SG15_OtherItemsDoNothing()
        {
            GameObject go = Go("Others");
            GameObject kid = Go("Kid", go.transform);
            GameObject newParent = Go("NewParent");
            var parent = new SetParentItem { m_Motion = new SetMotion() };
            parent.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = kid.transform, m_Path = "", m_Anchor = "" };
            parent.m_NewParent = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = newParent.transform, m_Path = "", m_Anchor = "" };
            parent.m_Timing.m_Duration = 0.2f;
            FeedbackPlayer p = Player(go, parent, Pos(Punch(new Vector4(0.3f, 0f, 0f, 0f)), 0.4f));
            SlotGraphModel m = SlotGraphModel.Build(p, p.m_Items, p, Inputs(1u), 0.6, 20, null, null, SettingsValues.Load(ContextKind.Test));
            Assert.That(m.Shown, Is.Not.Null);
            Assert.That(kid.transform.parent, Is.SameAs(go.transform), "not moved");
            // the positive: a live runtime prepares it
            var liveScheduler = new FeedbackScheduler(ContextKind.Preview);
            m_Schedulers.Add(liveScheduler);
            var liveRuntime = new PlayerRuntime(p, p.m_Items, p, liveScheduler, null, false);
            Assert.That(liveRuntime.Binding(0), Is.Not.Null, "a live runtime prepares the Set Parent — " + string.Join(",", Enumerable.Range(0, liveRuntime.ItemCount).Select(k => liveRuntime.Binding(k)?.GetType().Name ?? "null")));
            liveRuntime.Release(PlaybackEnd.Stopped);
            // while it plays too — a Set Parent puts the parent back at its end
            FeedbackScheduler dry = SlotGraphModel.DryScheduler(SettingsValues.Load(ContextKind.Test));
            m_Schedulers.Add(dry);
            var runtime = new PlayerRuntime(p, p.m_Items, p, dry, null, false);
            Assert.That(runtime.Binding(0), Is.Null, "not prepared");
            runtime.Play(PlayArgs.Default.WithSeed(1u), default);
            for (int n = 0; n < 12; n++)
            {
                dry.Tick(Dt);
                dry.RunLateApply();
                Assert.That(kid.transform.parent, Is.SameAs(go.transform), "not moved while it plays");
            }
            runtime.Release(PlaybackEnd.Stopped);
            Assert.That(newParent.transform.childCount, Is.Zero);
        }

        // an instance write in a dry play keeps the renderer's materials — no copy while it plays
        [Test]
        public void SG16_NoCopiesInADryPlay()
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Made.Add(cube);
            Renderer r = cube.GetComponent<Renderer>();
            Material original = r.sharedMaterial;
            OscillateParams glow = OscillateParams.Default;
            glow.m_Amplitude = new Vector4(1f, 1f, 1f, 0f);
            var emission = new EmissionItem { m_Target = Self, m_WriteMode = MaterialWriteMode.Instance, m_Motion = new OscillateMotion { m_Params = glow } };
            emission.m_Timing.m_Duration = 0.5f;
            FeedbackPlayer p = Player(cube, emission);
            FeedbackScheduler dry = SlotGraphModel.DryScheduler(SettingsValues.Load(ContextKind.Test));
            m_Schedulers.Add(dry);
            var runtime = new PlayerRuntime(p, p.m_Items, p, dry, null, false);
            Assert.That(runtime.Binding(0), Is.Not.Null, "prepared");
            Assert.That(r.sharedMaterial, Is.SameAs(original), "prepared without a copy");
            runtime.Play(PlayArgs.Default.WithSeed(1u), default);
            for (int n = 0; n < 12; n++)
            {
                dry.Tick(Dt);
                dry.RunLateApply();
            }
            Assert.That(r.sharedMaterial, Is.SameAs(original), "playing without a copy");
            runtime.Release(PlaybackEnd.Stopped);
        }

        // an open item (another player's play waited for, up to a time limit) is not prepared in a dry play: its node keeps the
        // nominal place the strip draws — the time limit — where the live play waits for the other player's end
        [Test]
        public void SG17_AnOpenItem()
        {
            GameObject otherGo = Go("Other");
            FeedbackPlayer other = Player(otherGo, Pos(Punch(new Vector4(0f, 0f, 0.1f, 0f)), 0.6f));
            GameObject go = Go("Waits");
            var wait = new PlayPlayerItem { m_Player = other };
            wait.m_Play.m_Wait = true;
            wait.m_Play.m_Timeout = 0.2f;
            var after = Pos(Punch(new Vector4(0.4f, 0f, 0f, 0f)), 0.3f);
            after.m_Timing.m_Rule = StartRule.AfterPrevious;
            FeedbackPlayer p = Player(go, wait, after);
            SlotGraphModel m = Model(p, 1u, 96);
            int first = Enumerable.Range(0, m.TickCount).First(n => Math.Abs(m.Ticks[n * 4]) > 1e-6f);
            UnityEngine.Debug.Log("SG17 the punch after the open item starts at tick " + first + " (" + (first + 1) * Dt + " s)");
            Assert.That((first + 1) * Dt, Is.EqualTo(0.2f).Within(2f * Dt), "after the time limit");
        }

        // a preset's own inspector has no Graph (no scene to find the targets in)
        [UnityTest]
        public IEnumerator SG12_NoneInAPresetsInspector()
        {
            m_Window = ScriptableObject.CreateInstance<PreviewBarTestWindow>();
            m_Window.Show();
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            m_Made.Add(preset);
            preset.m_Items.Add(Pos(Punch(Vector4.one * 0.1f), 0.4f));
            InspectorElement inspector = InspectorTesting.Inspector(m_Window, preset);
            yield return InspectorTesting.Wait(0.3);
            Assert.That(inspector.Q<TimelineStrip>(), Is.Not.Null);
            Assert.That(inspector.Q<SlotGraphField>(), Is.Null);
        }
        // ---- every graph item's combo (the bindings play test's table): the graph's dry play leaves the scene as it was —
        // the objects' serialized state, their materials and blocks, the render settings, the time scale, the roots and the
        // hidden objects — and its slots are the ones the runtime prepares

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static GameObject ComboFixture(MethodInfo makeLeaf, MethodInfo materials, object kind, Type itemType)
        {
            var template = new GameObject("template");
            var mid = new GameObject("c");
            mid.transform.SetParent(template.transform, false);
            mid.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
            var leaf = (GameObject)makeLeaf.Invoke(null, new[] { kind, itemType });
            string k = kind.ToString();
            if (k == "Cube")
            {
                var prepared = Resources.Load<Material>("FeedbackTestMaterial");
                if (prepared != null)
                    leaf.GetComponent<Renderer>().sharedMaterial = prepared;
                materials.Invoke(null, new object[] { leaf });
            }
            leaf.name = "g";
            leaf.transform.SetParent(mid.transform, false);
            if (k == "LightCamera")
            {
                leaf.AddComponent<Light>();
                leaf.AddComponent<Camera>();
            }
            // 6000.6 fills a URP material's missing shader properties at its first property query — asked here, before the
            // snapshot, so the graph's (and the runtime's) query is not the first
            foreach (Renderer r in template.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material m in r.sharedMaterials)
                    m?.HasProperty(Shader.PropertyToID("_BaseColor"));
            }
            return template;
        }

        static void Snap(StringBuilder sb, Transform t)
        {
            sb.Append(t.name).Append(t.gameObject.activeSelf ? '+' : '-').Append(t.gameObject.layer).Append('|');
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null)
                    continue;
                sb.Append(c.GetType().Name).Append(':').Append(EditorJsonUtility.ToJson(c)).Append('|');
                if (c is Renderer r)
                {
                    sb.Append("block=").Append(r.HasPropertyBlock());
                    if (r.sharedMaterial != null)
                        sb.Append(" material=").Append(EditorJsonUtility.ToJson(r.sharedMaterial));
                    sb.Append('|');
                }
            }
        }

        static string Snap(GameObject root)
        {
            var sb = new StringBuilder();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                Snap(sb, t);
            Camera main = Camera.main;
            if (main != null && !main.transform.IsChildOf(root.transform))
                Snap(sb, main.transform);
            sb.Append("roots=").Append(SceneManager.GetActiveScene().rootCount)
                .Append(" all=").Append(Resources.FindObjectsOfTypeAll<GameObject>().Length)
                .Append(" fog=").Append(RenderSettings.fogColor).Append(RenderSettings.fogDensity).Append(RenderSettings.fogStartDistance).Append(RenderSettings.fogEndDistance)
                .Append(" ambient=").Append(RenderSettings.ambientLight).Append(RenderSettings.ambientIntensity)
                .Append(" sky=").Append(RenderSettings.skybox != null ? EditorJsonUtility.ToJson(RenderSettings.skybox) : "")
                .Append(" time=").Append(Time.timeScale).Append(' ').Append(Time.fixedDeltaTime);
            return sb.ToString();
        }

        static string Differ(string a, string b)
        {
            int i = 0;
            while (i < a.Length && i < b.Length && a[i] == b[i])
                i++;
            int from = Math.Max(0, i - 60);
            return a.Substring(from, Math.Min(140, a.Length - from)) + " ==> " + b.Substring(from, Math.Min(140, b.Length - from));
        }

        void DryOverCombos(bool materialsOnly, int expected)
        {
            Type host = typeof(PlayerBindingsPlayTests);
            object tests = Activator.CreateInstance(host);
            var combos = ((IEnumerable)host.GetMethod("Combos", Any).Invoke(tests, new object[] { materialsOnly })).Cast<object>().ToList();
            MethodInfo makeLeaf = host.GetMethod("MakeLeaf", Any);
            MethodInfo materials = host.GetMethod("Materials", Any);
            var fails = new List<string>();
            int graphs = 0;
            foreach (object combo in combos)
            {
                var tuple = (ITuple)combo;
                var name = (string)tuple[0];
                var make = (Func<GameObject, FeedbackItem>)tuple[1];
                var itemType = (Type)tuple[3];
                GameObject root = ComboFixture(makeLeaf, materials, tuple[2], itemType);
                m_Made.Add(root);
                FeedbackItem item = make(root);
                var channels = new List<GraphChannel>();
                (item is TargetItem t ? FeedbackImplementations.Create(t) : null)?.Describe(channels);
                if (channels.Count == 0)
                    continue;
                graphs++;
                FeedbackPlayer p = Player(root, item);
                // the graph first — the live runtime after it (an instance write's copies touch the originals)
                string before = Snap(root);
                SlotGraphModel m = SlotGraphModel.Build(p, p.m_Items, p, Inputs(7u), 0.5, 16, null, null, SettingsValues.Load(ContextKind.Test));
                string after = Snap(root);
                if (after != before)
                    fails.Add(name + " changed the scene: " + Differ(before, after));
                // the runtime's own slots
                var live = new FeedbackScheduler(ContextKind.Test);
                m_Schedulers.Add(live);
                p.Bind(live);
                p.EnsureRuntime();
                var keys = new List<LayerKey>();
                var slots = new List<LayerSlot>();
                p.Runtime.Binding(0)?.GraphSlots(slots);
                keys.AddRange(slots.Select(s => s.Key));
                p.ReleaseRuntime();
                var graphKeys = m.Groups.Select(g => g.Key).ToList();
                if (graphKeys.Count != keys.Count || keys.Any(k => !graphKeys.Contains(k)))
                    fails.Add(name + " slots " + string.Join(",", graphKeys.Select(k => k.SlotType.Name)) + " / runtime " + string.Join(",", keys.Select(k => k.SlotType.Name)));
                Object.DestroyImmediate(root);
            }
            if (expected > 0)
                Assert.That(graphs, Is.EqualTo(expected), "graph combos");
            Assert.That(graphs, Is.GreaterThan(0));
            Assert.That(fails, Is.Empty);
        }

        [Test]
        public void SG13_DryOverEveryCombo() => DryOverCombos(false, -1);

        [Test, Category("DevProject")]
        public void SG13_DryOverEveryComboMaterials() => DryOverCombos(true, 64);
    }
}
