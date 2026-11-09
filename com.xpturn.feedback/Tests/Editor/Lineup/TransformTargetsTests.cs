using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the transform targets on a test scheduler ticked by hand (0.05 s). A Tween and a Set are Persist — a Revert stop in the
    // middle and a Restore after the end give the start back, bit for bit; an offset motion ends at the base
    public sealed class TransformTargetsTests
    {
        const float Step = 0.05f;
        const string PathA = "Assets/TransformTargetsA.unity";
        const string PathB = "Assets/TransformTargetsB.unity";

        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();

        [TearDown]
        public void TearDown()
        {
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
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            m_Schedulers.Clear();
            m_Players.Clear();
        }

        static string B(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");

        static string B3(Vector3 v) => B(v.x) + B(v.y) + B(v.z);

        static string BQ(Quaternion q) => B(q.x) + B(q.y) + B(q.z) + B(q.w);

        static string Trs(Transform t) => B3(t.localPosition) + "|" + BQ(t.localRotation) + "|" + B3(t.localScale);

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };

        static TweenMotion TweenTo(float to, TweenMode mode = TweenMode.To)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = new Vector4(to, 0f, 0f, 0f);
            p.m_Mode = mode;
            return new TweenMotion { m_Params = p };
        }

        static PunchMotion PunchBy(float a) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = new Vector4(a, a, a, 0f), m_Vibrato = 1f, m_Decay = 0f } };

        static OscillateMotion Oscillate(float a) => new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = new Vector4(a, 0f, 0f, 0f), m_Frequency = 1f, m_Phase = 0f } };

        static PlayArgs Backward => PlayArgs.Default.WithDirection(PlayerDirection.Backward);

        static T On<T>(T item, Object target, FeedbackMotion motion, float duration) where T : TargetItem
        {
            item.m_Target = Direct(target);
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        GameObject Go(string name, Vector3 position = default)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            m_Made.Add(go);
            return go;
        }

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            FeedbackPlayer p = Go("Player").AddComponent<FeedbackPlayer>();
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

        // a Persist play: a Revert stop in the middle and a Restore after the end both give the start back, bit for bit
        // an instant's play ends at once — a keeper runs on, so its Revert stop comes while the play lives
        IEnumerable<string> PersistBack(string name, Func<GameObject> make, Func<GameObject, string> read, Func<GameObject, FeedbackItem> item, bool keeper = false)
        {
            foreach (bool midway in new[] { true, false })
            {
                FeedbackScheduler s = Sched();
                GameObject t = make();
                string before = read(t);
                FeedbackPlayer p = keeper ? Player(s, item(t), On(new PositionItem(), Go("Keeper"), PunchBy(0.5f), 1f)) : Player(s, item(t));
                p.Play();
                if (midway)
                {
                    Tick(s, 8);
                    p.Stop(StopMode.Revert);
                }
                else
                {
                    Tick(s, 30);
                    p.Restore();
                }
                s.Tick(0f);
                string after = read(t);
                if (after != before)
                    yield return name + (midway ? " — a Revert stop " : " — a Restore ") + before + " → " + after;
            }
        }

        Vector3 Squash(SquashAxis axis, FeedbackMotion motion, float duration, int ticks)
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("Squash");
            SquashAndStretchItem item = On(new SquashAndStretchItem(), t, motion, duration);
            item.m_Axis = axis;
            Player(s, item).Play();
            Tick(s, ticks);
            return t.transform.localScale;
        }

        static readonly (SquashAxis Axis, string Pattern)[] s_Axes =
        {
            (SquashAxis.XtoYZ, "+--"), (SquashAxis.XtoY, "+-="), (SquashAxis.XtoZ, "+=-"),
            (SquashAxis.YtoXZ, "-+-"), (SquashAxis.YtoX, "-+="), (SquashAxis.YtoZ, "=+-"),
            (SquashAxis.ZtoXY, "--+"), (SquashAxis.ZtoX, "-=+"), (SquashAxis.ZtoY, "=-+"),
        };

        // ---- each axis mode stretches its main axis, presses its own, leaves the third — YtoZ writes y and z

        [Test]
        public void TQ01_Reg15_EachAxisModeWritesItsOwnAxes()
        {
            var fails = new List<string>();
            foreach ((SquashAxis axis, string want) in s_Axes)
            {
                Vector3 scale = Squash(axis, TweenTo(1f), 0.5f, 15);
                string got = "";
                for (int i = 0; i < 3; i++)
                    got += scale[i] > 1.0001f ? "+" : scale[i] < 0.9999f ? "-" : "=";
                if (got != want)
                    fails.Add(axis + " " + got + " (" + want + ")");
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- the up is used — a direction off every axis (a level direction with the Forward up is
        // degenerate): the end rotation's up leans to the axis as far as the direction lets it, three different rotations. Set

        Quaternion LookUp(LookAtUp up, Vector3 direction, out float lean)
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("Up");
            LookAtItem item = On(new LookAtItem(), t, new SetMotion(), 0f);
            item.m_Mode = LookAtMode.Direction;
            item.m_Direction = direction;
            item.m_Up = up;
            Player(s, item).Play();
            s.Tick(Step);
            lean = Vector3.Dot(t.transform.up, Axis(up));
            return t.transform.rotation;
        }

        static Vector3 Axis(LookAtUp up) => up == LookAtUp.Up ? Vector3.up : up == LookAtUp.Forward ? Vector3.forward : Vector3.right;

        [Test]
        public void TQ02_Reg20_TheUpIsUsed()
        {
            var direction = new Vector3(0f, 1f, 1f);
            var rotations = new List<Quaternion>();
            foreach (LookAtUp up in new[] { LookAtUp.Up, LookAtUp.Forward, LookAtUp.Right })
            {
                rotations.Add(LookUp(up, direction, out float lean));
                float want = Mathf.Sqrt(1f - Mathf.Pow(Vector3.Dot(direction.normalized, Axis(up)), 2f));
                Assert.That(lean, Is.EqualTo(want).Within(2e-3f), up + "'s lean");
            }
            Assert.That(Quaternion.Angle(rotations[0], rotations[1]), Is.GreaterThan(1f), "Up · Forward");
            Assert.That(Quaternion.Angle(rotations[0], rotations[2]), Is.GreaterThan(1f), "Up · Right");
            Assert.That(Quaternion.Angle(rotations[1], rotations[2]), Is.GreaterThan(1f), "Forward · Right");
        }

        // ---- each item makes its own binding (the registry's completeness by name is DataModelReadersTests')

        [Test]
        public void TQ04_EachMakesItsBinding()
        {
            var want = new Dictionary<Type, Type>
            {
                { typeof(SquashAndStretchItem), typeof(SquashAndStretchBinding) }, { typeof(LookAtItem), typeof(LookAtBinding) },
                { typeof(RotateAroundItem), typeof(RotateAroundBinding) }, { typeof(MatchTransformItem), typeof(MatchTransformBinding) },
                { typeof(SetParentItem), typeof(SetParentBinding) },
            };
            foreach (KeyValuePair<Type, Type> w in want)
                Assert.That(FeedbackImplementations.Create((EffectItem)Activator.CreateInstance(w.Key))?.GetType(), Is.EqualTo(w.Value), w.Key.Name);
        }

        // ---- stops — +1 doubles the main axis, two axes 1/√2, one axis 1/2; the volume stays in
        // all nine. A punch of one stop reaches −1 and +1. The end of a contribution is the base; Persist comes back

        [Test]
        public void TQ05a_SquashValuesAndVolume()
        {
            var want = new Dictionary<SquashAxis, Vector3>
            {
                { SquashAxis.XtoYZ, new Vector3(2f, 0.70710678f, 0.70710678f) }, { SquashAxis.XtoY, new Vector3(2f, 0.5f, 1f) },
                { SquashAxis.YtoXZ, new Vector3(0.70710678f, 2f, 0.70710678f) }, { SquashAxis.ZtoY, new Vector3(1f, 0.5f, 2f) },
            };
            foreach (KeyValuePair<SquashAxis, Vector3> w in want)
                Assert.That((Squash(w.Key, TweenTo(1f), 0.5f, 15) - w.Value).sqrMagnitude, Is.LessThan(1e-10f), w.Key.ToString());
            foreach ((SquashAxis axis, string _) in s_Axes)
            {
                Vector3 scale = Squash(axis, TweenTo(1f), 0.5f, 15);
                Assert.That(scale.x * scale.y * scale.z, Is.EqualTo(1f).Within(1e-4f), axis + "'s volume");
            }
        }

        [Test]
        public void TQ05b_SquashPunchIsSymmetricAndEndsAtTheBase()
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("Punch");
            Player(s, On(new SquashAndStretchItem(), t, PunchBy(1f), 1f)).Play();
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 22; i++)
            {
                s.Tick(Step);
                min = Mathf.Min(min, t.transform.localScale.y);
                max = Mathf.Max(max, t.transform.localScale.y);
            }
            s.Tick(0f);
            Assert.That(-Mathf.Log(min, 2f), Is.InRange(0.9f, 1.05f), "down to −1 stop");
            Assert.That(Mathf.Log(max, 2f), Is.InRange(0.9f, 1.05f), "up to +1 stop");
            Assert.That(B3(t.transform.localScale), Is.EqualTo(B3(Vector3.one)), "the end");
        }

        [Test]
        public void TQ05c_SquashPersistComesBack()
        {
            var fails = PersistBack("squash tween", () => Go("Persist"), t => B3(t.transform.localScale),
                t => On(new SquashAndStretchItem(), t, TweenTo(1f), 1f)).ToList();
            fails.AddRange(PersistBack("squash from a scaled start", () => { GameObject t = Go("Scaled"); t.transform.localScale = new Vector3(1.5f, 0.75f, 2f); return t; },
                t => B3(t.transform.localScale), t => On(new SquashAndStretchItem(), t, TweenTo(-1f), 1f)));
            Assert.That(fails, Is.Empty);
        }

        // the Persist from the play's start, not the slot's base: a scaled start doubles its own main axis
        [Test]
        public void TQ05d_SquashFromTheStartAndWithAScaleItem()
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("FromStart");
            t.transform.localScale = new Vector3(3f, 0.5f, 3f);
            Player(s, On(new SquashAndStretchItem(), t, TweenTo(1f), 0.5f)).Play();
            Tick(s, 15);
            Assert.That((t.transform.localScale - new Vector3(3f / Mathf.Sqrt(2f), 1f, 3f / Mathf.Sqrt(2f))).sqrMagnitude, Is.LessThan(1e-9f), "the start's own axes");

            FeedbackScheduler alone = Sched();
            GameObject a = Go("Alone");
            Player(alone, On(new SquashAndStretchItem(), a, TweenTo(1f), 0.5f)).Play();
            Tick(alone, 30);
            FeedbackScheduler both = Sched();
            GameObject b = Go("WithScale");
            Player(both, On(new SquashAndStretchItem(), b, TweenTo(1f), 0.5f), On(new ScaleItem(), b, PunchBy(0.5f), 1f)).Play();
            Tick(both, 30);
            both.Tick(0f);
            Assert.That(B3(b.transform.localScale), Is.EqualTo(B3(a.transform.localScale)), "a scale punch on the same slot ends at the squash's value");
        }

        // ---- the three modes by Set and by Tween · following while the tween runs, not after · a degenerate
        // direction keeps the rotation · Persist comes back · From arrives at the start

        [Test]
        public void TQ06a_LookAtModesBySetAndTween()
        {
            var fails = new List<string>();
            foreach (bool set in new[] { true, false })
            {
                foreach (LookAtMode mode in new[] { LookAtMode.Transform, LookAtMode.Position, LookAtMode.Direction })
                {
                    FeedbackScheduler s = Sched();
                    GameObject t = Go("Look");
                    GameObject at = Go("At", new Vector3(5f, 0f, 5f));
                    LookAtItem item = On(new LookAtItem(), t, set ? (FeedbackMotion)new SetMotion() : TweenTo(1f), set ? 0f : 0.5f);
                    item.m_Mode = mode;
                    item.m_LookTarget = Direct(at.transform);
                    item.m_Position = new Vector3(5f, 0f, 5f);
                    item.m_Direction = new Vector3(1f, 0f, 1f);
                    Player(s, item).Play();
                    Tick(s, set ? 1 : 15);
                    float angle = Vector3.Angle(t.transform.forward, new Vector3(1f, 0f, 1f));
                    if (angle > 0.01f)
                        fails.Add((set ? "Set " : "Tween ") + mode + " " + angle);
                }
            }
            Assert.That(fails, Is.Empty);
        }

        [Test]
        public void TQ06b_LookAtFollowsWhileItTweens()
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("Follow");
            GameObject at = Go("Moving", new Vector3(5f, 0f, 0f));
            LookAtItem item = On(new LookAtItem(), t, TweenTo(1f), 1f);
            item.m_LookTarget = Direct(at.transform);
            Player(s, item).Play();
            Tick(s, 10);
            at.transform.position = new Vector3(0f, 0f, 5f);
            Tick(s, 12);
            Assert.That(Vector3.Angle(t.transform.forward, at.transform.position), Is.LessThan(0.01f), "the end looks at the moved target");
            at.transform.position = new Vector3(-5f, 0f, 0f);
            Quaternion end = t.transform.rotation;
            Tick(s, 5);
            Assert.That(BQ(t.transform.rotation), Is.EqualTo(BQ(end)), "no following after the end");
        }

        [Test]
        public void TQ06c_LookAtDegenerateKeepsTheRotation()
        {
            var fails = new List<string>();
            foreach (bool along in new[] { false, true })
            {
                FeedbackScheduler s = Sched();
                GameObject t = Go("Degenerate", new Vector3(1f, 2f, 3f));
                t.transform.rotation = Quaternion.Euler(10f, 20f, 30f);
                Quaternion before = t.transform.rotation;
                LookAtItem item = On(new LookAtItem(), t, TweenTo(1f), 0.3f);
                // its own position, or a direction along the up
                item.m_Mode = along ? LookAtMode.Direction : LookAtMode.Position;
                item.m_Position = new Vector3(1f, 2f, 3f);
                item.m_Direction = Vector3.up;
                Player(s, item).Play();
                Tick(s, 10);
                if (Quaternion.Angle(t.transform.rotation, before) > 1e-3f)
                    fails.Add((along ? "along the up " : "its own position ") + t.transform.rotation.eulerAngles);
            }
            Assert.That(fails, Is.Empty);
        }

        [Test]
        public void TQ06d_LookAtPersistAndFrom()
        {
            var fails = PersistBack("look at", () => { GameObject t = Go("Turned"); t.transform.rotation = Quaternion.Euler(0f, 30f, 0f); return t; }, t => BQ(t.transform.localRotation), t =>
            {
                LookAtItem item = On(new LookAtItem(), t, TweenTo(1f), 1f);
                item.m_LookTarget = Direct(Go("PersistAt", new Vector3(3f, 1f, 4f)).transform);
                return item;
            }).ToList();
            fails.AddRange(PersistBack("look at by Set", () => { GameObject t = Go("TurnedSet"); t.transform.rotation = Quaternion.Euler(0f, 30f, 0f); return t; }, t => BQ(t.transform.localRotation), t =>
            {
                LookAtItem item = On(new LookAtItem(), t, new SetMotion(), 0f);
                item.m_LookTarget = Direct(Go("SetAt", new Vector3(3f, 1f, 4f)).transform);
                return item;
            }, true));
            Assert.That(fails, Is.Empty);

            FeedbackScheduler s = Sched();
            GameObject from = Go("From");
            from.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            Quaternion start = from.transform.rotation;
            GameObject target = Go("FromAt", new Vector3(-3f, 0f, 0f));
            LookAtItem arrive = On(new LookAtItem(), from, TweenTo(1f, TweenMode.From), 1f);
            arrive.m_LookTarget = Direct(target.transform);
            Player(s, arrive).Play();
            s.Tick(Step);
            // a step in (0.05 of 1 s) the 120° turn has gone about 6° from the look rotation
            Assert.That(Vector3.Angle(from.transform.forward, target.transform.position), Is.LessThan(10f), "From starts at the look rotation");
            Tick(s, 25);
            Assert.That(Quaternion.Angle(from.transform.rotation, start), Is.LessThan(1e-3f), "and arrives at the start");
        }

        // a main camera reference with none at the prepare — a use finds it
        [Test]
        public void TQ06e_LookAtTheMainCameraThatComesLater()
        {
            var untagged = new List<GameObject>();
            foreach (Camera other in Camera.allCameras)
            {
                if (other.CompareTag("MainCamera"))
                {
                    other.gameObject.tag = "Untagged";
                    untagged.Add(other.gameObject);
                }
            }
            try
            {
                Assert.That(Camera.main, Is.Null, "no main camera at the prepare");
                FeedbackScheduler s = Sched();
                GameObject t = Go("Billboard");
                LookAtItem item = On(new LookAtItem(), t, TweenTo(1f), 0.5f);
                item.m_LookTarget = new TargetRef { m_Mode = TargetRefMode.MainCamera, m_Path = "", m_Anchor = "" };
                FeedbackPlayer p = Player(s, item);
                Assert.That(p.Runtime.Binding(0), Is.InstanceOf<LookAtBinding>(), "prepared without a camera");
                GameObject camera = Go("Camera", new Vector3(0f, 0f, -5f));
                camera.AddComponent<Camera>();
                camera.tag = "MainCamera";
                p.Play();
                Tick(s, 15);
                Assert.That(Vector3.Angle(t.transform.forward, Vector3.back), Is.LessThan(0.01f), "looks at the camera that came");
                camera.tag = "Untagged";
            }
            finally
            {
                foreach (GameObject o in untagged)
                {
                    if (o != null)
                        o.tag = "MainCamera";
                }
            }
        }

        // ---- the orbit about the world axis through a pivot read each frame · the rotation stays · an
        // oscillation's farthest point is its angle's chord, on the circle · with a position item · Persist comes back

        [Test]
        public void TQ07a_RotateAroundTheMovingPivot()
        {
            FeedbackScheduler s = Sched();
            GameObject pivot = Go("Pivot");
            GameObject t = Go("Orbit", new Vector3(2f, 0f, 0f));
            Quaternion rotation = t.transform.rotation;
            RotateAroundItem item = On(new RotateAroundItem(), t, TweenTo(90f), 0.5f);
            item.m_Pivot = Direct(pivot.transform);
            Player(s, item).Play();
            Tick(s, 15);
            Assert.That((t.transform.position - new Vector3(0f, 0f, -2f)).magnitude, Is.LessThan(1e-4f), "a quarter turn about up");
            Assert.That(BQ(t.transform.rotation), Is.EqualTo(BQ(rotation)), "the rotation stays");

            FeedbackScheduler m = Sched();
            GameObject moving = Go("MovingPivot");
            GameObject o = Go("Orbit2", new Vector3(2f, 0f, 0f));
            RotateAroundItem around = On(new RotateAroundItem(), o, TweenTo(90f), 1f);
            around.m_Pivot = Direct(moving.transform);
            Player(m, around).Play();
            Tick(m, 10);
            moving.transform.position = new Vector3(1f, 0f, 0f);
            Tick(m, 15);
            Assert.That((o.transform.position - new Vector3(1f, 0f, -1f)).magnitude, Is.LessThan(1e-4f), "about the moved pivot");
        }

        [Test]
        public void TQ07b_RotateAroundOscillationIsTheChord()
        {
            FeedbackScheduler s = Sched();
            GameObject pivot = Go("ChordPivot");
            GameObject t = Go("Swing", new Vector3(2f, 0f, 0f));
            RotateAroundItem item = On(new RotateAroundItem(), t, Oscillate(30f), 1f);
            item.m_Pivot = Direct(pivot.transform);
            Player(s, item).Play();
            float far = 0f, off = 0f;
            for (int i = 0; i < 22; i++)
            {
                s.Tick(Step);
                far = Mathf.Max(far, (t.transform.position - new Vector3(2f, 0f, 0f)).magnitude);
                off = Mathf.Max(off, Mathf.Abs(t.transform.position.magnitude - 2f));
            }
            Assert.That(far, Is.EqualTo(4f * Mathf.Sin(15f * Mathf.Deg2Rad)).Within(1e-3f), "30°'s chord");
            Assert.That(off, Is.LessThan(1e-4f), "on the circle");

            FeedbackScheduler w = Sched();
            GameObject withPunch = Go("SwingAndPunch", new Vector3(2f, 0f, 0f));
            RotateAroundItem swing = On(new RotateAroundItem(), withPunch, Oscillate(30f), 1f);
            swing.m_Pivot = Direct(Go("PunchPivot").transform);
            Player(w, swing, On(new PositionItem(), withPunch, PunchBy(0.5f), 1f)).Play();
            Tick(w, 30);
            w.Tick(0f);
            Assert.That(B3(withPunch.transform.position), Is.EqualTo(B3(new Vector3(2f, 0f, 0f))), "with a position punch, the end is the base");
        }

        [Test]
        public void TQ07c_RotateAroundPersistComesBack()
        {
            var fails = PersistBack("orbit", () => Go("OrbitPersist", new Vector3(2f, 1f, 0f)), t => B3(t.transform.localPosition), t =>
            {
                RotateAroundItem item = On(new RotateAroundItem(), t, TweenTo(90f), 1f);
                item.m_Pivot = Direct(Go("PersistPivot").transform);
                return item;
            }).ToList();
            Assert.That(fails, Is.Empty);
        }

        // ---- the channels · Track · the local scale · Persist over three slots · with the transform items ·
        // backward to the origin · a Rebuild keeps it

        GameObject Destination(string name)
        {
            GameObject d = Go(name, new Vector3(3f, 1f, 2f));
            d.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            d.transform.localScale = new Vector3(2f, 2f, 2f);
            return d;
        }

        [Test]
        public void TQ08a_MatchChannelsAndTrack()
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("Match");
            GameObject d = Destination("Dest");
            MatchTransformItem item = On(new MatchTransformItem(), t, TweenTo(1f), 0.5f);
            item.m_Destination = Direct(d.transform);
            item.m_Scale = false;
            Player(s, item).Play();
            Tick(s, 15);
            Assert.That((t.transform.position - d.transform.position).magnitude, Is.LessThan(1e-4f), "position");
            Assert.That(Quaternion.Angle(t.transform.rotation, d.transform.rotation), Is.LessThan(1e-3f), "rotation");
            Assert.That(t.transform.localScale, Is.EqualTo(Vector3.one), "the scale channel off");

            FeedbackScheduler sc = Sched();
            GameObject parent = Go("Parent");
            parent.transform.localScale = new Vector3(3f, 3f, 3f);
            GameObject child = Go("Child");
            child.transform.SetParent(parent.transform, false);
            MatchTransformItem scale = On(new MatchTransformItem(), child, TweenTo(1f), 0.5f);
            scale.m_Destination = Direct(Destination("ScaleDest").transform);
            Player(sc, scale).Play();
            Tick(sc, 15);
            Assert.That(child.transform.localScale, Is.EqualTo(new Vector3(2f, 2f, 2f)), "the destination's local scale");

            foreach (bool track in new[] { false, true })
            {
                FeedbackScheduler k = Sched();
                GameObject m = Go("Tracking");
                GameObject moving = Destination("MovingDest");
                MatchTransformItem follow = On(new MatchTransformItem(), m, TweenTo(1f), 1f);
                follow.m_Destination = Direct(moving.transform);
                follow.m_Track = track;
                Player(k, follow).Play();
                Tick(k, 10);
                moving.transform.position = new Vector3(-3f, 0f, 0f);
                Tick(k, 15);
                Vector3 want = track ? new Vector3(-3f, 0f, 0f) : new Vector3(3f, 1f, 2f);
                Assert.That((m.transform.position - want).magnitude, Is.LessThan(1e-3f), "Track " + track);
            }
        }

        [Test]
        public void TQ08b_MatchPersistAndTheTransformItems()
        {
            var fails = PersistBack("match", () => Go("MatchPersist", new Vector3(1f, 0f, 0f)), t => Trs(t.transform), t =>
            {
                MatchTransformItem item = On(new MatchTransformItem(), t, TweenTo(1f), 1f);
                item.m_Destination = Direct(Destination("PersistDest").transform);
                return item;
            }).ToList();
            Assert.That(fails, Is.Empty);

            FeedbackScheduler s = Sched();
            GameObject t2 = Go("MatchAndPunch");
            GameObject d = Destination("PunchDest");
            MatchTransformItem match = On(new MatchTransformItem(), t2, TweenTo(1f), 0.5f);
            match.m_Destination = Direct(d.transform);
            Player(s, match, On(new PositionItem(), t2, PunchBy(0.5f), 1f), On(new RotationItem(), t2, PunchBy(10f), 1f), On(new ScaleItem(), t2, PunchBy(0.5f), 1f)).Play();
            Tick(s, 30);
            s.Tick(0f);
            Assert.That((t2.transform.position - d.transform.position).magnitude, Is.LessThan(1e-4f), "position with a punch");
            Assert.That(Quaternion.Angle(t2.transform.rotation, d.transform.rotation), Is.LessThan(1e-3f), "rotation with a punch");
            Assert.That((t2.transform.localScale - d.transform.localScale).magnitude, Is.LessThan(1e-4f), "scale with a punch");
        }

        [Test]
        public void TQ08c_MatchBackwardAndRebuild()
        {
            foreach (bool rebuild in new[] { false, true })
            {
                FeedbackScheduler s = Sched();
                GameObject t = Go("Back", new Vector3(1f, 2f, 3f));
                t.transform.rotation = Quaternion.Euler(0f, 10f, 0f);
                string start = Trs(t.transform);
                GameObject d = Destination("BackDest");
                MatchTransformItem item = On(new MatchTransformItem(), t, TweenTo(1f), 0.5f);
                item.m_Destination = Direct(d.transform);
                FeedbackPlayer p = Player(s, item);
                p.Play();
                Tick(s, 15);
                Assert.That((t.transform.position - d.transform.position).magnitude, Is.LessThan(1e-4f), "forward");
                if (rebuild)
                    p.Rebuild();
                p.Play(Backward);
                Tick(s, 15);
                Assert.That(Trs(t.transform), Is.EqualTo(start), (rebuild ? "after a Rebuild " : "") + "backward ends at the origin");
            }
        }

        // ---- the place — stays · the local values · a Revert stop while the play runs and a Restore put the
        // parent, the sibling index and the local values back bit for bit · a RectTransform · another scene · the root ·
        // backward · a cycle refused

        [Test]
        public void TQ09a_SetParentAndItsPlaceBack()
        {
            foreach (bool stays in new[] { true, false })
            {
                FeedbackScheduler s = Sched();
                GameObject p1 = Go("P1", new Vector3(5f, 0f, 0f));
                p1.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
                GameObject p2 = Go("P2", new Vector3(-3f, 1f, 0f));
                p2.transform.localScale = new Vector3(2f, 2f, 2f);
                GameObject a = Go("A"), t = Go("T"), c = Go("C");
                foreach (GameObject g in new[] { a, t, c })
                    g.transform.SetParent(p1.transform, false);
                t.transform.localPosition = new Vector3(1f, 2f, 3f);
                string trs = Trs(t.transform);
                Vector3 world = t.transform.position;
                SetParentItem item = On(new SetParentItem(), t, new SetMotion(), 0f);
                item.m_NewParent = Direct(p2.transform);
                item.m_WorldPositionStays = stays;
                // a keeper runs on, so the Set's Revert comes while the play lives
                FeedbackPlayer p = Player(s, item, On(new PositionItem(), Go("Keeper"), PunchBy(0.5f), 1f));
                p.Play();
                Tick(s, 3);
                Assert.That(t.transform.parent, Is.SameAs(p2.transform), "set");
                if (stays)
                    Assert.That((t.transform.position - world).magnitude, Is.LessThan(1e-4f), "the world place stays");
                else
                    Assert.That(B3(t.transform.localPosition), Is.EqualTo(B3(new Vector3(1f, 2f, 3f))), "the local values stay");
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                Assert.That((t.transform.parent, t.transform.GetSiblingIndex(), Trs(t.transform)), Is.EqualTo((p1.transform, 1, trs)), "a Revert stop");
                p.Play();
                Tick(s, 30);
                Assert.That(t.transform.parent, Is.SameAs(p2.transform), "Persist");
                p.Restore();
                s.Tick(0f);
                Assert.That((t.transform.parent, t.transform.GetSiblingIndex(), Trs(t.transform)), Is.EqualTo((p1.transform, 1, trs)), "a Restore");
            }
        }

        [Test]
        public void TQ09b_SetParentBackwardRootAndCycle()
        {
            FeedbackScheduler s = Sched();
            GameObject p1 = Go("Back1"), p2 = Go("Back2"), t = Go("BackT");
            t.transform.SetParent(p1.transform, false);
            SetParentItem item = On(new SetParentItem(), t, new SetMotion(), 0f);
            item.m_NewParent = Direct(p2.transform);
            FeedbackPlayer p = Player(s, item);
            p.Play();
            Tick(s, 2);
            Assert.That(t.transform.parent, Is.SameAs(p2.transform), "forward");
            p.Play(Backward);
            Tick(s, 2);
            Assert.That(t.transform.parent, Is.SameAs(p1.transform), "backward writes the origin");

            FeedbackScheduler r = Sched();
            GameObject holder = Go("Holder"), rooted = Go("Rooted");
            rooted.transform.SetParent(holder.transform, false);
            SetParentItem toRoot = On(new SetParentItem(), rooted, new SetMotion(), 0f);
            toRoot.m_NewParent = TargetRef.Default;
            FeedbackPlayer pr = Player(r, toRoot);
            pr.Play();
            Tick(r, 2);
            Assert.That(rooted.transform.parent, Is.Null, "an empty new parent is the root");
            pr.Restore();
            r.Tick(0f);
            Assert.That(rooted.transform.parent, Is.SameAs(holder.transform), "and back");

            GameObject cycle = Go("Cycle"), below = Go("Below");
            below.transform.SetParent(cycle.transform, false);
            SetParentItem refused = On(new SetParentItem(), cycle, new SetMotion(), 0f);
            refused.m_NewParent = Direct(below.transform);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("the new parent is the target or under it"));
            FeedbackPlayer pc = Player(Sched(), refused);
            Assert.That(pc.Runtime.Binding(0), Is.Null, "a cycle is refused at the prepare");
        }

        [Test]
        public void TQ09c_SetParentRectTransformRestores()
        {
            FeedbackScheduler s = Sched();
            GameObject canvas = Go("Canvas");
            canvas.AddComponent<RectTransform>();
            canvas.AddComponent<Canvas>();
            var holder = new GameObject("Holder", typeof(RectTransform));
            m_Made.Add(holder);
            holder.transform.SetParent(canvas.transform, false);
            var box = (RectTransform)holder.transform;
            box.anchorMin = new Vector2(0.2f, 0.2f);
            box.anchorMax = new Vector2(0.8f, 0.6f);
            var ui = new GameObject("UI", typeof(RectTransform));
            m_Made.Add(ui);
            ui.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)ui.transform;
            rt.anchorMin = new Vector2(0.1f, 0.2f);
            rt.anchorMax = new Vector2(0.7f, 0.9f);
            rt.anchoredPosition = new Vector2(10f, 20f);
            rt.sizeDelta = new Vector2(-40f, 25f);
            SerializedSnapshot before = SerializedSnapshot.Take(rt);
            SetParentItem item = On(new SetParentItem(), rt, new SetMotion(), 0f);
            item.m_NewParent = Direct(box);
            FeedbackPlayer p = Player(s, item);
            p.Play();
            s.Tick(Step);
            Assert.That(rt.parent, Is.SameAs(box), "set");
            p.Restore();
            s.Tick(0f);
            Assert.That(before.Differences(SerializedSnapshot.Take(rt)), Is.Empty, "the anchors, pivot, anchored position and size come back");
        }

        static Scene OpenScene(string path)
        {
            File.WriteAllText(Path.GetFullPath(path), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        [Test]
        public void TQ09d_SetParentIntoAnotherSceneAndBack()
        {
            Scene active = SceneManager.GetActiveScene();
            Scene a = OpenScene(PathA), b = OpenScene(PathB);
            try
            {
                SceneManager.SetActiveScene(a);
                var first = new GameObject("First");
                var t = new GameObject("Root");
                var last = new GameObject("Last");
                SceneManager.SetActiveScene(b);
                var other = new GameObject("InB");
                FeedbackScheduler s = Sched();
                SetParentItem item = On(new SetParentItem(), t, new SetMotion(), 0f);
                item.m_NewParent = Direct(other.transform);
                FeedbackPlayer p = Player(s, item);
                SceneManager.MoveGameObjectToScene(p.gameObject, a);
                p.Play();
                s.Tick(Step);
                Assert.That(t.scene, Is.EqualTo(b), "the parent's scene");
                p.Restore();
                s.Tick(0f);
                Assert.That(t.scene, Is.EqualTo(a), "back in its scene");
                Assert.That(a.GetRootGameObjects().Select(g => g.name).Take(3), Is.EqualTo(new[] { "First", "Root", "Last" }), "at its root index");
            }
            finally
            {
                if (active.IsValid())
                    SceneManager.SetActiveScene(active);
                EditorSceneManager.CloseScene(a, true);
                EditorSceneManager.CloseScene(b, true);
                AssetDatabase.DeleteAsset(PathA);
                AssetDatabase.DeleteAsset(PathB);
            }
        }
    }
}
