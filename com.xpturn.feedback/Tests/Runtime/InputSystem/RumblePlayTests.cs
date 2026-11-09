using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Perf = xpTURN.Feedback.Tests.PlayerPerformanceTests;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests
{
    // the real loop on a fresh test scheduler set as the default, judged by the recording gamepad's calls. The fixed frame time
    // drives the scaled clocks only — the items run on the Player clock here (the default Unscaled one would run at the batch's
    // real time) and the resend on manual ticks
    public sealed class RumblePlayTests
    {
        static long s_Baseline = -1;
        static int[] s_Junk;
#if UNITY_EDITOR
        // by name — the runtime test assemblies reference no editor assembly
        static readonly Type s_EditorApp = Type.GetType("UnityEditor.EditorApplication, UnityEditor.CoreModule");
#endif
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<InputDevice> m_Devices = new List<InputDevice>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        FeedbackScheduler m_Saved;
        FeedbackScheduler m_Test;

        [SetUp]
        public void SetUp()
        {
            m_Saved = FeedbackScheduler.Default;
            m_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = m_Test;
            Time.captureDeltaTime = 1f / 60f;
            RecordingGamepad.Count = 0;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    p.Stop();
            }
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Players.Clear();
            m_Schedulers.Clear();
            m_Made.Clear();
            yield return null;
            FeedbackScheduler.Default = m_Saved;
            if (!m_Test.IsShutdown)
                m_Test.Shutdown();
            foreach (InputDevice d in m_Devices)
            {
                if (d.added)
                    InputSystem.RemoveDevice(d);
            }
            m_Devices.Clear();
            RumbleCommands.Listen(false);
            Time.captureDeltaTime = 0f;
            FeedbackCategories.SetEnabled(FeedbackCategory.Haptics, true);
        }

        RecordingGamepad Pad(string name)
        {
            var d = InputSystem.AddDevice<RecordingGamepad>(name);
            m_Devices.Add(d);
            return d;
        }

        FeedbackPlayer Player(GameObject parent, params FeedbackItem[] items)
        {
            var go = new GameObject("HT player");
            m_Made.Add(go);
            go.SetActive(false);
            if (parent != null)
                go.transform.SetParent(parent.transform, false);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            go.SetActive(true);
            p.EnsureRuntime();
            m_Players.Add(p);
            return p;
        }

        static GamepadRumbleItem Rumble(FeedbackMotion motion, float duration, GamepadRumbleDevice device = GamepadRumbleDevice.Current)
        {
            var item = new GamepadRumbleItem { m_Device = device };
            item.m_Timing.m_Clock = ItemClock.Player;
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static TweenMotion Tween(TweenMode mode, float x, float y)
        {
            TweenParams p = TweenParams.Default;
            p.m_Mode = mode;
            p.m_To = new Vector4(x, y, 0f, 0f);
            return new TweenMotion { m_Params = p };
        }

        static FlashMotion Hold(float x, float y) => new FlashMotion { m_Params = new FlashParams { m_Target = new Vector4(x, y, 0f, 0f), m_Attack = 0.01f, m_Hold = 0.98f, m_Release = 0.01f } };

        static CurveMotion Constant(float x, float y)
            => new CurveMotion { m_Params = new CurveParams { m_Curve = AnimationCurve.Constant(0f, 1f, 1f), m_RemapZero = Vector4.zero, m_RemapOne = new Vector4(x, y, 0f, 0f) } };

        static string V(Vector2 v) => $"({v.x:0.000},{v.y:0.000})";

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
                yield return null;
        }

        static bool Prepared(FeedbackPlayer p) => p.Runtime != null && p.Runtime.Binding(0) != null;

        // ---- the motions × the play's intensity — a Tween To takes none, a By · a Flash · a Curve take it; each end (0, 0)

        [UnityTest]
        public IEnumerator HT04a_TheMotions()
        {
            RecordingGamepad pad = Pad("HT pad");
            pad.MakeCurrent();
            var peaks = new List<string>();
            int zeros = 0;
            foreach ((FeedbackMotion motion, float duration) in new (FeedbackMotion, float)[]
            {
                (Tween(TweenMode.To, 0.5f, 0.25f), 0.2f), (Tween(TweenMode.By, 0.5f, 0.25f), 0.2f),
                (new FlashMotion { m_Params = FlashParams.Default }, 0.2f), (Constant(0.6f, 0.3f), 0.2f),
            })
            {
                foreach (float intensity in new[] { 1f, 0.5f })
                {
                    FeedbackPlayer p = Player(null, Rumble(motion, duration));
                    int from = RecordingGamepad.Count;
                    p.Play(PlayArgs.Default.WithIntensity(intensity));
                    int frames = 0, last = from;
                    for (; frames < 40; frames++)
                    {
                        yield return null;
                        if (RecordingGamepad.Count > last && RecordingGamepad.Values[RecordingGamepad.Count - 1] == Vector2.zero && frames > 2)
                            break;
                        last = RecordingGamepad.Count;
                    }
                    peaks.Add(V(RecordingGamepad.Peak(pad, from)));
                    if (frames < 40 && RecordingGamepad.LastFor(pad) == Vector2.zero)
                        zeros++;
                    p.Stop(StopMode.Revert);
                    yield return Frames(2);
                }
            }
            Assert.That(peaks, Is.EqualTo(new[] { "(0.500,0.250)", "(0.500,0.250)", "(0.500,0.250)", "(0.250,0.125)", "(1.000,1.000)", "(0.500,0.500)", "(0.600,0.300)", "(0.300,0.150)" }),
                "To takes no intensity · By · Flash · Curve take it");
            Assert.That(zeros, Is.EqualTo(8), "every end — (0, 0) within the frames");
        }

        // ---- nothing stays — Completed and a Hold stop send (0, 0) the next frame; a player's Pause lets a Tween go (it goes on after
        // the Resume) and keeps a Curve's value

        [UnityTest]
        public IEnumerator HT05a_TheEnds()
        {
            RecordingGamepad pad = Pad("HT pad");
            pad.MakeCurrent();
            FeedbackPlayer a = Player(null, Rumble(Tween(TweenMode.To, 0.5f, 0.25f), 0.2f));
            a.Play();
            yield return Frames(20);
            string done = RecordingGamepad.Tail(pad, Math.Max(0, RecordingGamepad.Count - 3));
            int doneAt = RecordingGamepad.Count >= 2 ? RecordingGamepad.Frames[RecordingGamepad.Count - 2] : -1;
            FeedbackPlayer b = Player(null, Rumble(Tween(TweenMode.To, 0.5f, 0.25f), 1f));
            b.Play();
            yield return Frames(10);
            int from = RecordingGamepad.Count;
            b.Stop(StopMode.Hold);
            yield return Frames(3);
            string hold = RecordingGamepad.Tail(pad, from);
            Assert.That(done, Does.EndWith("S(0.50,0.25)@" + doneAt + " S(0.00,0.00)@" + (doneAt + 1) + " "), "the end's value, then (0, 0) the next frame");
            Assert.That(hold, Does.StartWith("S(0.00,0.00)@"), "a Hold stop — (0, 0) the next frame");

            FeedbackPlayer t = Player(null, Rumble(Tween(TweenMode.To, 0.5f, 0.25f), 1f));
            t.Play();
            yield return Frames(10);
            Vector2 before = RecordingGamepad.LastFor(pad);
            t.Pause();
            yield return Frames(5);
            Vector2 paused = RecordingGamepad.LastFor(pad);
            from = RecordingGamepad.Count;
            t.Resume();
            yield return Frames(3);
            Vector2 resumed = RecordingGamepad.Peak(pad, from);
            t.Stop(StopMode.Revert);
            yield return Frames(2);
            FeedbackPlayer c = Player(null, Rumble(Constant(0.6f, 0.3f), 1f));
            c.Play();
            yield return Frames(5);
            c.Pause();
            from = RecordingGamepad.Count;
            yield return Frames(5);
            Assert.That(paused, Is.EqualTo(Vector2.zero), "a paused Tween asks for nothing");
            Assert.That(resumed.x, Is.GreaterThanOrEqualTo(before.x).And.LessThan(0.5f), "the Tween goes on from where it was");
            Assert.That((RecordingGamepad.CountFor(pad, from), RecordingGamepad.LastFor(pad)), Is.EqualTo((0, new Vector2(0.6f, 0.3f))), "a paused Curve keeps its value");
        }

        // ---- Current and All on one pad sum and clamp · NaN is 0 · two Tweens on one path — the later written only

        [UnityTest]
        public IEnumerator HT06a_SumClampNaN()
        {
            RecordingGamepad pad = Pad("HT pad");
            pad.MakeCurrent();
            FeedbackPlayer a = Player(null, Rumble(Constant(0.7f, 0.2f), 2f));
            FeedbackPlayer b = Player(null, Rumble(Constant(0.7f, 0.2f), 2f, GamepadRumbleDevice.All));
            a.Play();
            b.Play();
            yield return Frames(3);
            Vector2 sum = RecordingGamepad.LastFor(pad);
            a.Stop();
            b.Stop();
            yield return Frames(2);
            GamepadRumbleService svc = GamepadRumbleService.Of(m_Test.Context);
            svc.SetRequest(GamepadRumbleService.CurrentPath, new Vector2(float.NaN, 0.5f));
            yield return null;
            Vector2 nan = RecordingGamepad.LastFor(pad);
            yield return Frames(2);
            FeedbackPlayer x = Player(null, Rumble(Tween(TweenMode.To, 0.5f, 0f), 0.2f));
            FeedbackPlayer y = Player(null, Rumble(Tween(TweenMode.To, 0f, 0.5f), 0.3f));
            int from = RecordingGamepad.Count;
            x.Play();
            y.Play();
            yield return Frames(25);
            Assert.That(sum, Is.EqualTo(new Vector2(1f, 0.4f)), "Current + All on one pad — summed, clamped");
            Assert.That(nan, Is.EqualTo(new Vector2(0f, 0.5f)), "NaN → 0");
            Assert.That((RecordingGamepad.Peak(pad, from).x, RecordingGamepad.Peak(pad, from).y, RecordingGamepad.LastFor(pad)), Is.EqualTo((0f, 0.5f, Vector2.zero)),
                "two Tweens on one path — the later written; nothing after both end");
        }

        // ---- Current follows the current pad (the one before to (0, 0)) · All drives every pad · Player the PlayerInput's paired
        // pad (none in the parents — refused) · a removed pad leaves the ledger, re-added it is driven again · pads added after the
        // preparation

        [UnityTest]
        public IEnumerator HT07a_CurrentAndAll()
        {
            RecordingGamepad a = Pad("HT a");
            FeedbackPlayer cur = Player(null, Rumble(Hold(0.5f, 0.5f), 2f));
            cur.Play();
            yield return Frames(5);
            RecordingGamepad b = Pad("HT b");
            yield return Frames(2);
            Vector2 swapA = RecordingGamepad.LastFor(a), swapB = RecordingGamepad.LastFor(b);
            cur.Stop();
            yield return Frames(2);
            FeedbackPlayer all = Player(null, Rumble(Hold(0.4f, 0.2f), 2f, GamepadRumbleDevice.All));
            all.Play();
            yield return Frames(3);
            Assert.That((swapA, swapB), Is.EqualTo((Vector2.zero, new Vector2(0.5f, 0.5f))), "the new current pad · the one before (0, 0)");
            Assert.That((RecordingGamepad.LastFor(a), RecordingGamepad.LastFor(b)), Is.EqualTo((new Vector2(0.4f, 0.2f), new Vector2(0.4f, 0.2f))), "All — every pad");
        }

        [UnityTest]
        public IEnumerator HT07b_ThePlayer()
        {
            RecordingGamepad a = Pad("HT a");
            RecordingGamepad b = Pad("HT b");
            var template = new GameObject("HT template");
            m_Made.Add(template);
            template.SetActive(false);
            template.AddComponent<PlayerInput>();
            PlayerInput pi = PlayerInput.Instantiate(template, pairWithDevice: a);
            m_Made.Add(pi.gameObject);
            FeedbackPlayer p = Player(pi.gameObject, Rumble(Hold(0.6f, 0.3f), 2f, GamepadRumbleDevice.Player));
            FeedbackPlayer lone = Player(null, Rumble(Hold(0.6f, 0.3f), 2f, GamepadRumbleDevice.Player));
            p.Play();
            lone.Play();
            yield return Frames(3);
            Assert.That((RecordingGamepad.LastFor(a), RecordingGamepad.CountFor(b)), Is.EqualTo((new Vector2(0.6f, 0.3f), 0)), "the player's pad only");
            Assert.That((Prepared(p), Prepared(lone)), Is.EqualTo((true, false)), "no PlayerInput in the parents — refused");
        }

        [UnityTest]
        public IEnumerator HT07c_DeviceEvents()
        {
            RecordingGamepad a = Pad("HT a");
            RecordingGamepad b = Pad("HT b");
            FeedbackPlayer p = Player(null, Rumble(Hold(0.5f, 0.5f), 3f, GamepadRumbleDevice.All));
            GamepadRumbleService svc = GamepadRumbleService.Of(m_Test.Context);
            p.Play();
            yield return Frames(3);
            InputSystem.RemoveDevice(b);
            yield return Frames(2);
            int removed = svc.DeviceCount;
            InputSystem.AddDevice(b);
            yield return Frames(2);
            Assert.That((removed, svc.DeviceCount, RecordingGamepad.LastFor(b)), Is.EqualTo((1, 2, new Vector2(0.5f, 0.5f))), "a removed pad leaves the ledger · re-added it is driven again");
        }

        [UnityTest]
        public IEnumerator HT07d_PadsAfterThePreparation()
        {
            foreach (Gamepad g in Gamepad.all.ToArray())
                InputSystem.RemoveDevice(g);
            FeedbackPlayer p = Player(null, Rumble(Hold(0.3f, 0.3f), 2f, GamepadRumbleDevice.All));
            GamepadRumbleService svc = GamepadRumbleService.Of(m_Test.Context);
            RecordingGamepad a = Pad("HT a");
            RecordingGamepad b = Pad("HT b");
            RecordingGamepad c = Pad("HT c");
            p.Play();
            yield return Frames(3);
            Assert.That((RecordingGamepad.LastFor(a), RecordingGamepad.LastFor(b), RecordingGamepad.LastFor(c), svc.DeviceCount),
                Is.EqualTo((new Vector2(0.3f, 0.3f), new Vector2(0.3f, 0.3f), new Vector2(0.3f, 0.3f), 3)), "the ledger grows at the device events");
        }

        // ---- a held value is sent once (the attack's steps, not every frame) · a change under 1/255 is not sent · again every 5 s
        // while not zero (the service's unscaled clock — manual ticks)

        [UnityTest]
        public IEnumerator HT08a_ChangedOnly()
        {
            RecordingGamepad pad = Pad("HT pad");
            pad.MakeCurrent();
            FeedbackPlayer p = Player(null, Rumble(Hold(0.5f, 0.5f), 7f));
            p.Play();
            yield return Frames(30);
            int held = RecordingGamepad.CountFor(pad);
            p.Stop();
            yield return Frames(2);
            GamepadRumbleService svc = GamepadRumbleService.Of(m_Test.Context);
            int from = RecordingGamepad.CountFor(pad);
            svc.SetRequest(GamepadRumbleService.CurrentPath, new Vector2(0.3f, 0.3f));
            yield return null;
            svc.SetRequest(GamepadRumbleService.CurrentPath, new Vector2(0.303f, 0.3f));
            yield return null;
            svc.SetRequest(GamepadRumbleService.CurrentPath, new Vector2(0.306f, 0.3f));
            yield return null;
            Assert.That(held, Is.InRange(2, 8), "the attack's steps and the hold — not every frame");
            Assert.That(RecordingGamepad.CountFor(pad) - from, Is.EqualTo(2), "0.3 · then +0.006 (+0.003 is under the step)");
        }

        [UnityTest]
        public IEnumerator HT08b_AgainEveryFiveSeconds()
        {
            RecordingGamepad pad = Pad("HT pad");
            pad.MakeCurrent();
            yield return null;
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            GamepadRumbleService svc = GamepadRumbleService.Of(s.Context);
            var at = new List<int>();
            for (int i = 0; i < 660; i++)
            {
                int before = RecordingGamepad.CountFor(pad);
                svc.SetRequest(GamepadRumbleService.CurrentPath, new Vector2(0.4f, 0.4f));
                s.Tick(1f / 60f);
                if (RecordingGamepad.CountFor(pad) > before)
                    at.Add(i);
            }
            s.Tick(1f / 60f);
            Assert.That(at, Is.EqualTo(new List<int> { 0, 300, 600 }), "again every 5 s while not zero");
            Assert.That(RecordingGamepad.LastFor(pad), Is.EqualTo(Vector2.zero), "the request gone — (0, 0)");
        }

        // ---- a focus loss through its own raise — the editor app's in the editor, Application's in a player (the subscription,
        // not only the handler) — one (0, 0); an editor pause and its end keep the focus's reason; the focus back plays again · the
        // Haptics category off stops a Tween too

        [UnityTest]
        public IEnumerator HT09a_TheMuteReasons()
        {
            RecordingGamepad pad = Pad("HT pad");
            pad.MakeCurrent();
            FeedbackPlayer p = Player(null, Rumble(Hold(0.5f, 0.5f), 3f));
            GamepadRumbleService svc = GamepadRumbleService.Of(m_Test.Context);
            p.Play();
            yield return Frames(5);
#if UNITY_EDITOR
            MethodInfo focus = s_EditorApp?.GetMethod("Internal_FocusChanged", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(focus, Is.Not.Null, "the editor app's own raise of focusChanged");
#else
            MethodInfo focus = typeof(Application).GetMethod("InvokeFocusChanged", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(focus, Is.Not.Null, "Application's own raise of focusChanged");
#endif
            int from = RecordingGamepad.Count;
            focus.Invoke(null, new object[] { false });
            yield return Frames(2);
            svc.OnPaused(true);
            svc.OnPaused(false);
            yield return Frames(3);
            (int calls, Vector2 last, int muted) lost = (RecordingGamepad.CountFor(pad, from), RecordingGamepad.LastFor(pad), svc.Muted);
            from = RecordingGamepad.Count;
            focus.Invoke(null, new object[] { true });
            yield return Frames(2);
            int back = RecordingGamepad.CountFor(pad, from);
            p.Stop();
            FeedbackPlayer t = Player(null, Rumble(Tween(TweenMode.To, 0.5f, 0.25f), 1f));
            t.Play();
            yield return Frames(10);
            FeedbackCategories.SetEnabled(FeedbackCategory.Haptics, false);
            yield return Frames(5);
            (Vector2 last, int muted) off = (RecordingGamepad.LastFor(pad), svc.Muted);
            from = RecordingGamepad.Count;
            FeedbackCategories.SetEnabled(FeedbackCategory.Haptics, true);
            yield return Frames(2);
            Assert.That(lost, Is.EqualTo((1, Vector2.zero, GamepadRumbleService.FocusLost)), "a focus loss — one (0, 0); a pause and its end keep the focus's reason");
            Assert.That(back, Is.GreaterThan(0), "the focus back — the request again");
            Assert.That(off, Is.EqualTo((Vector2.zero, GamepadRumbleService.CategoryOff)), "the category off — (0, 0) for a Tween too");
            Assert.That(RecordingGamepad.CountFor(pad, from), Is.GreaterThan(0), "the category on — again");
        }

#if UNITY_EDITOR
        // ---- in the editor: the Game view's focus is not the app's — Application's raise (a click in another editor window)
        // keeps the pad playing · the close lets go of the editor app's event

        [UnityTest]
        public IEnumerator HT09b_TheGameViewsFocus()
        {
            RecordingGamepad pad = Pad("HT pad");
            pad.MakeCurrent();
            FeedbackPlayer p = Player(null, Rumble(Hold(0.5f, 0.5f), 3f));
            GamepadRumbleService svc = GamepadRumbleService.Of(m_Test.Context);
            p.Play();
            yield return Frames(5);
            MethodInfo focus = typeof(Application).GetMethod("InvokeFocusChanged", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(focus, Is.Not.Null, "Application's own raise of focusChanged");
            int from = RecordingGamepad.Count;
            focus.Invoke(null, new object[] { false });
            yield return Frames(3);
            (int calls, Vector2 last, int muted) kept = (RecordingGamepad.CountFor(pad, from), RecordingGamepad.LastFor(pad), svc.Muted);
            focus.Invoke(null, new object[] { true });
            FieldInfo apps = s_EditorApp?.GetField("focusChanged", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(apps, Is.Not.Null, "the editor app's focusChanged");
            int open = (apps.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
            m_Test.Shutdown();
            int closed = (apps.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
            Assert.That(kept, Is.EqualTo((0, new Vector2(0.5f, 0.5f), 0)), "the Game view's focus loss — no (0, 0), still playing");
            Assert.That(open - closed, Is.EqualTo(1), "the close lets go of the editor app's focusChanged");
        }
#endif

        // ---- the context's close sends (0, 0) to every pad of the ledger

        [UnityTest]
        public IEnumerator HT03b_TheCloseStopsEveryPad()
        {
            RecordingGamepad a = Pad("HT a");
            RecordingGamepad b = Pad("HT b");
            FeedbackPlayer p = Player(null, Rumble(Hold(0.5f, 0.5f), 3f, GamepadRumbleDevice.All));
            GamepadRumbleService svc = GamepadRumbleService.Of(m_Test.Context);
            p.Play();
            yield return Frames(3);
            int from = RecordingGamepad.Count;
            m_Test.Shutdown();
            Assert.That(RecordingGamepad.Tail(null, from), Is.EqualTo($"S(0.00,0.00)@{Time.frameCount} S(0.00,0.00)@{Time.frameCount} "), "the close — (0, 0) to both");
            Assert.That((svc.IsClosed, svc.DeviceCount), Is.EqualTo((true, 0)));
        }

        // ---- the test context drives managed pads only (a pad marked native — the device's flag set by reflection — is left
        // alone); the runtime context drives it

        [UnityTest]
        public IEnumerator HT12a_ManagedPadsOnly()
        {
            RecordingGamepad pad = Pad("HT native");
            FieldInfo flags = typeof(InputDevice).GetField("m_DeviceFlags", BindingFlags.NonPublic | BindingFlags.Instance);
            object native = Enum.Parse(flags.FieldType, "Native");
            flags.SetValue(pad, Enum.ToObject(flags.FieldType, Convert.ToInt32(flags.GetValue(pad)) | Convert.ToInt32(native)));
            RecordingGamepad managed = Pad("HT managed");
            FeedbackPlayer p = Player(null, Rumble(Hold(0.5f, 0.5f), 1f, GamepadRumbleDevice.All));
            p.Play();
            yield return Frames(3);
            int testCalls = RecordingGamepad.CountFor(pad);
            Vector2 managedLast = RecordingGamepad.LastFor(managed);
            var rt = new FeedbackScheduler(ContextKind.Runtime);
            m_Schedulers.Add(rt);
            GamepadRumbleService runtime = GamepadRumbleService.Of(rt.Context);
            runtime.SetRequest(GamepadRumbleService.AllPath, new Vector2(0.2f, 0.2f));
            rt.Tick(1f / 60f);
            Assert.That(pad.native, Is.True, "the pad is marked native");
            Assert.That((testCalls, managedLast), Is.EqualTo((0, new Vector2(0.5f, 0.5f))), "the test context leaves the native pad alone");
            Assert.That(RecordingGamepad.LastFor(pad), Is.EqualTo(new Vector2(0.2f, 0.2f)), "the runtime context drives it");
        }

        // ---- five fresh fixtures' first cycle (a warm-up on another first) with pads · the real loop from rest against the run's
        // idle frames

        sealed class Fixture
        {
            public FeedbackScheduler Scheduler;
            public FeedbackPlayer Player;
        }

        static Fixture s_Fixture;

        Fixture Fresh(FeedbackMotion motion, GamepadRumbleDevice device)
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            var go = new GameObject("HT fresh");
            m_Made.Add(go);
            go.SetActive(false);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.Add(Rumble(motion, 0.1f, device));
            p.Bind(s);
            go.SetActive(true);
            p.EnsureRuntime();
            return new Fixture { Scheduler = s, Player = p };
        }

        static void Cycle()
        {
            s_Fixture.Player.Play();
            for (int i = 0; i < 8; i++)
                s_Fixture.Scheduler.Tick(1f / 60f);
            s_Fixture.Player.Stop();
            s_Fixture.Scheduler.Tick(1f / 60f);
        }

        [UnityTest]
        public IEnumerator HT15a_FirstCycles()
        {
            RecordingGamepad pad = Pad("HT pad");
            pad.MakeCurrent();
            Pad("HT other");
            yield return null;
            var red = new List<string>();
            foreach ((string label, Func<FeedbackMotion> motion, GamepadRumbleDevice device) in new (string, Func<FeedbackMotion>, GamepadRumbleDevice)[]
            {
                ("tween current", () => Tween(TweenMode.By, 0.5f, 0.5f), GamepadRumbleDevice.Current),
                ("flash all", () => new FlashMotion { m_Params = FlashParams.Default }, GamepadRumbleDevice.All),
                ("curve current", () => Constant(0.5f, 0.5f), GamepadRumbleDevice.Current),
            })
            {
                s_Fixture = Fresh(motion(), device);
                Cycle();
                int hits = 0, c0 = RecordingGamepad.Count;
                for (int i = 0; i < 5; i++)
                {
                    s_Fixture = Fresh(motion(), device);
                    if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(Cycle)).IsSuccess)
                        hits++;
                }
                Assert.That(RecordingGamepad.Count - c0, Is.GreaterThan(0), label + " sent");
                if (hits == 5)
                    red.Add(label);
            }
            s_Fixture = null;
            int positive = 0;
            for (int i = 0; i < 5; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => s_Junk = new int[8])).IsSuccess)
                    positive++;
            }
            Assert.That(red, Is.Empty, "the first cycles allocate nothing");
            Assert.That(positive, Is.EqualTo(5), "the positive control");
        }

        [UnityTest]
        public IEnumerator HT15b_TheRealLoopFromRest()
        {
            LogAssert.ignoreFailingMessages = true;
            RecordingGamepad pad = Pad("HT pad");
            pad.MakeCurrent();
            RecordingGamepad other = Pad("HT other");
            var template = new GameObject("HT template");
            m_Made.Add(template);
            template.SetActive(false);
            template.AddComponent<PlayerInput>();
            PlayerInput pi = PlayerInput.Instantiate(template, pairWithDevice: other);
            m_Made.Add(pi.gameObject);
            FeedbackPlayer a = Player(null, Rumble(Tween(TweenMode.By, 0.5f, 0.25f), 0.5f));
            FeedbackPlayer b = Player(null, Rumble(Constant(0.3f, 0.3f), 0.4f, GamepadRumbleDevice.All));
            FeedbackPlayer c = Player(pi.gameObject, Rumble(new FlashMotion { m_Params = FlashParams.Default }, 0.3f, GamepadRumbleDevice.Player));
            GamepadRumbleService svc = GamepadRumbleService.Of(m_Test.Context);
            using ProfilerRecorder rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            for (int f = 0; f < 90; f++)
            {
                if (f % 30 == 0) a.Play();
                if (f % 25 == 0) b.Play();
                if (f % 20 == 0) c.Play();
                yield return null;
            }
            yield return Frames(40);
            var idle = new List<long>();
            for (int f = 0; f < 31; f++)
            {
                yield return null;
                idle.Add(rec.LastValue);
            }
            idle.Sort();
            long baseline = idle[15];
            int idleAllocating = idle.Count(v => v - baseline >= 64);
            int sends = svc.Sends, allocating = 0;
            for (int f = 0; f < 120; f++)
            {
                if (f % 30 == 0) a.Play();
                if (f % 25 == 0) b.Play();
                if (f % 20 == 0) c.Play();
                yield return null;
                if (rec.LastValue - baseline >= 64)
                    allocating++;
            }
            sends = svc.Sends - sends;
            int positive = 0;
            for (int f = 0; f < 5; f++)
            {
                s_Junk = new int[2048];
                yield return null;
                if (rec.LastValue - baseline >= 4000)
                    positive++;
            }
            Debug.Log($"HT15b rumble real loop · idle allocating {idleAllocating}/31 · allocating {allocating}/120 · sends {sends}");
            Assert.That(sends, Is.GreaterThan(30), "the pads were driven in the window");
            Assert.That(positive, Is.GreaterThanOrEqualTo(4), "the recorder sees an allocation — the positive");
            Assert.That(allocating, Is.LessThanOrEqualTo(idleAllocating + 3), "the plays from rest allocate nothing");
        }

        // ---- the per-play ns within its budget in an IL2CPP player · the plays send in the window

        static IEnumerator Baseline()
        {
            if (s_Baseline >= 0)
                yield break;
            yield return Perf.Measure("(x) no play", 1, 0);
            long first = Perf.s_Last.Alloc;
            yield return Perf.Measure("(x) no play again", 1, 0);
            s_Baseline = Math.Min(first, Perf.s_Last.Alloc);
        }

        [UnityTest]
        public IEnumerator HT16a_Recorded([Values(100, 1000)] int n)
        {
            yield return null;
            yield return Baseline();
            RecordingGamepad pad = Pad("HT pad");
            pad.MakeCurrent();
            var players = new List<FeedbackPlayer>(n);
            for (int i = 0; i < n; i++)
            {
                FeedbackPlayer p = Player(null, Rumble(new CurveMotion { m_Params = CurveParams.Default }, 1000f));
                players.Add(p);
            }
            int prepared = players.Count(Prepared);
            int accepted = players.Count(p => p.Play() != default);
            int from = RecordingGamepad.Count;
            yield return Perf.Settled("(x) gamepad rumble " + n, n, 0, s_Baseline, 600);
            Perf.Window w = Perf.s_Last;
            Assert.That(RecordingGamepad.CountFor(pad, from), Is.GreaterThan(0), "the plays send in the window — the positive");
            Assert.That(prepared + " · " + accepted, Is.EqualTo(n + " · " + n), "prepared · accepted");
            Assert.That(w.Frames + " · " + w.Counted, Is.EqualTo(Perf.Frames + " · " + Perf.Frames), "the recorders live — frames · markers");
            Assert.That(w.PerPlay, Is.GreaterThan(0), "per play ns (written down)");
            Perf.RowBudget("(x) gamepad rumble " + n, 600);
            if (Perf.k_Judge)
                Assert.That(Math.Abs(w.Alloc - s_Baseline), Is.LessThanOrEqualTo(Perf.Equal), "the window " + w.Alloc + " vs " + s_Baseline + " B");
        }

        // ---- the command path — the base Gamepad's rumble is 'RMBL' (16 bytes · low @8 · high @12) · no dedup · Pause sends
        // (0, 0), Resume the last · Reset · Pause send nothing once the managed cache is off · (0, 0) is always sent

        [UnityTest]
        public IEnumerator HT17a_TheCommandPath()
        {
            var g = InputSystem.AddDevice<Gamepad>("HT plain");
            m_Devices.Add(g);
            yield return null;
            RumbleCommands.Clear();
            RumbleCommands.Listen(true);
            g.SetMotorSpeeds(0.5f, 0.25f);
            g.SetMotorSpeeds(0.5f, 0.25f);
            string twice = RumbleCommands.Log;
            RumbleCommands.Clear();
            g.PauseHaptics();
            g.ResumeHaptics();
            g.ResetHaptics();
            string pause = RumbleCommands.Log;
            RumbleCommands.Clear();
            g.ResetHaptics();
            g.PauseHaptics();
            string off = RumbleCommands.Log;
            RumbleCommands.Clear();
            g.SetMotorSpeeds(0f, 0f);
            string zero = RumbleCommands.Log;
            RumbleCommands.Listen(false);
            Assert.That(twice, Is.EqualTo("HT plain:RMBL[16](0.50,0.25) HT plain:RMBL[16](0.50,0.25) "), "a call is a command — no dedup");
            Assert.That(pause, Is.EqualTo("HT plain:RMBL[16](0.00,0.00) HT plain:RMBL[16](0.50,0.25) HT plain:RMBL[16](0.00,0.00) "), "Pause · Resume · Reset");
            Assert.That(off, Is.Empty, "the managed cache off — Reset · Pause send nothing");
            Assert.That(zero, Is.EqualTo("HT plain:RMBL[16](0.00,0.00) "), "(0, 0) is always sent");
        }
    }
}
