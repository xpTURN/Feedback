using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // EditMode — the direction, the backward refusal, the backward play, the snapshots, Seek · Skip · Restore · Signal, the
    // first frame, the backward instants, Looped, the surface. A player is bound to a test scheduler that the row ticks
    // directly. A row checks everything and judges at the end. Warnings and errors are counted by a log listener; a logged
    // exception is expected with LogAssert. The test binding's log marks a backward Begin · Execute with "~" (a hook reads the
    // context). The rows that read Camera.main turn the open scene's main camera off and use their own
    public sealed class PlayerControlTests
    {
        static readonly List<string> s_Warnings = new List<string>();
        static readonly List<string> s_Errors = new List<string>();
        readonly List<string> m_Fail = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<Camera> m_Silenced = new List<Camera>();

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
                s_Warnings.Add(condition);
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                s_Errors.Add(type + ": " + condition);
        }

        [SetUp]
        public void SetUp()
        {
            m_Fail.Clear();
            s_Warnings.Clear();
            s_Errors.Clear();
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerRuntime.ResetSession();
            Application.logMessageReceived += OnLog;
            Hooks();
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            // edit mode's destruction brings no OnDestroy — the one that bound releases first
            foreach (FeedbackPlayer player in m_Players)
            {
                if (player != null)
                    player.ReleaseRuntime();
            }
            m_Players.Clear();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            foreach (Camera camera in m_Silenced)
            {
                if (camera != null)
                    camera.enabled = true;
            }
            m_Silenced.Clear();
            foreach (FeedbackScheduler scheduler in m_Schedulers)
            {
                if (!scheduler.IsShutdown)
                    scheduler.Shutdown();
            }
            m_Schedulers.Clear();
            PlayerTestBinding.Reset();
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        // ---- fixtures

        FeedbackScheduler Sched()
        {
            var scheduler = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(scheduler);
            return scheduler;
        }

        GameObject Go(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        GameObject At(string name, Vector3 at)
        {
            GameObject go = Go(name);
            go.transform.localPosition = at;
            return go;
        }

        FeedbackPlayer PlayerOn(FeedbackScheduler s, params FeedbackItem[] items) => PlayerOn(Go("player"), s, items);

        FeedbackPlayer PlayerOn(GameObject owner, FeedbackScheduler s, params FeedbackItem[] items)
        {
            var player = owner.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Items.AddRange(items);
            if (s != null)
                player.Bind(s);
            return player;
        }

        void SilenceSceneCameras()
        {
            foreach (Camera other in Camera.allCameras)
            {
                if (!other.CompareTag("MainCamera"))
                    continue;
                other.enabled = false;
                m_Silenced.Add(other);
            }
        }

        static PlayerTestAction Act(string label, float length, float delay = 0f, StartRule rule = StartRule.WithPrevious)
        {
            var action = new PlayerTestAction { m_Label = label, m_Length = length };
            action.m_Timing.m_Delay = delay;
            action.m_Timing.m_Rule = rule;
            return action;
        }

        static T Repeating<T>(T item) where T : FeedbackItem
        {
            item.m_Timing.m_RepeatForever = true;
            return item;
        }

        static T Only<T>(T item, DirectionCondition condition) where T : EffectItem
        {
            item.m_Conditions.m_Direction = condition;
            return item;
        }

        static T Timed<T>(T item, float delay, StartRule rule = StartRule.WithPrevious) where T : FeedbackItem
        {
            item.m_Timing.m_Delay = delay;
            item.m_Timing.m_Rule = rule;
            return item;
        }

        static T Clock<T>(T item, ItemClock clock) where T : FeedbackItem
        {
            item.m_Timing.m_Clock = clock;
            return item;
        }

        static LoopEndItem LoopEnd(int count)
        {
            var end = new LoopEndItem { m_LoopCount = count };
            end.m_Timing.m_Rule = StartRule.AfterPrevious;
            return end;
        }

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };
        static TargetRef Ref(TargetRefMode mode) => new TargetRef { m_Mode = mode, m_Path = "", m_Anchor = "" };

        static T Item<T>(FeedbackMotion motion, TargetRef target, float duration) where T : TargetItem, new()
        {
            var item = new T { m_Motion = motion, m_Target = target };
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static TweenMotion Tween(TweenMode mode, Vector3 value) => Tween(mode, value, value, Ease.Of(EaseType.Linear));

        static TweenMotion Tween(TweenMode mode, Vector3 from, Vector3 to, Ease ease)
            => new TweenMotion { m_Params = new TweenParams { m_Mode = mode, m_From = from, m_To = to, m_Ease = ease, m_Loops = 1 } };

        static ShakeMotion Shake(Vector4 a) => new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = a, m_Frequency = 10f } };
        static OscillateMotion Oscillate(Vector4 a) => new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = a, m_Frequency = 1f } };
        static SetMotion Set(Vector4 v, bool relative = false) => new SetMotion { m_Params = new SetParams { m_Value = v, m_Relative = relative } };

        static PositionItem Pos(GameObject target, TweenMode mode, Vector3 value, float duration)
            => Item<PositionItem>(Tween(mode, value), Direct(target.transform), duration);

        static SetActiveItem SetActive(GameObject target, bool value, bool relative = false)
            => Item<SetActiveItem>(Set(new Vector4(value ? 1f : 0f, 0f, 0f, 0f), relative), Direct(target), 0f);

        static UnityEventItem Event(UnityAction action)
        {
            var item = new UnityEventItem();
            item.m_Event.AddListener(action);
            return item;
        }

        static PlayArgs Backward => PlayArgs.Default.WithDirection(PlayerDirection.Backward);

        static void Run(FeedbackScheduler s, int n, float dt)
        {
            for (int i = 0; i < n; i++)
                s.Tick(dt);
        }

        // the reverse mark after a Begin · Execute's log line, then the row's own user code
        static void Hooks(Action<PlayerTestBinding, string, int> user = null)
        {
            PlayerTestBinding.Hook = (b, what, record) =>
            {
                if ((what == "Begin" || what == "Execute") && b.LastBegin.Reverse && PlayerTestBinding.Log.Count > 0)
                    PlayerTestBinding.Log[PlayerTestBinding.Log.Count - 1] += "~";
                user?.Invoke(b, what, record);
            };
        }

        static void Watch(FeedbackPlayer p)
        {
            p.Played += (pl, h) => PlayerTestBinding.Log.Add("|Played");
            p.Completed += (pl, h) => PlayerTestBinding.Log.Add("|Completed");
            p.Stopped += (pl, h) => PlayerTestBinding.Log.Add("|Stopped");
            p.Looped += (pl, h) => PlayerTestBinding.Log.Add("|Looped");
        }

        static string TakeLog()
        {
            string text = string.Join(" ", PlayerTestBinding.Log);
            PlayerTestBinding.Log.Clear();
            return text;
        }

        static string Events() => string.Join(" ", PlayerTestBinding.Log.Where(l => l.Length > 0 && l[0] == '|'));

        static PlaybackRecord Rec(FeedbackPlayer p, PlaybackHandle h)
        {
            if (h == default || p.Runtime == null)
                return null;
            for (int r = 0; r < p.Runtime.RecordCount; r++)
            {
                PlaybackRecord record = p.Runtime.Record(r);
                if (record.Active && record.Handle == h)
                    return record;
            }
            return null;
        }

        static string Dir(FeedbackPlayer p, PlaybackHandle h)
        {
            PlaybackRecord record = Rec(p, h);
            return record == null ? "x" : record.Reverse ? "B" : "F";
        }

        static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        static string V(Vector3 v) => "(" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + ")";
        static string X(GameObject go) => F(go.transform.localPosition.x);
        static string P(GameObject go) => V(go.transform.localPosition);
        static bool Near(Vector3 a, Vector3 b, float e = 1e-4f) => (a - b).magnitude <= e;
        static bool Bits(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        static bool Bits(Vector3 a, Vector3 b) => Bits(a.x, b.x) && Bits(a.y, b.y) && Bits(a.z, b.z);

        static int Count(string warning, int from = 0) => s_Warnings.Skip(from).Count(w => w.StartsWith(warning, StringComparison.Ordinal));

        static long Position(FeedbackPlayer p) => p.Runtime.Position(0);

        static PositionBinding PosBinding(FeedbackPlayer p, int item = 0) => (PositionBinding)p.Runtime.Binding(item);

        // ---- the direction

        [Test]
        public void DE01_Reg04_Direction()
        {
            FeedbackScheduler s = Sched();
            // Toggle turns at each accepted play — the cooldown's refusal comes before the direction
            FeedbackPlayer t = PlayerOn(s, Act("a", 0.1f));
            t.m_Direction = PlayerDirection.Toggle;
            t.m_Cooldown = 0.5f;
            var sb = new StringBuilder();
            for (int k = 0; k < 4; k++)
            {
                sb.Append(Dir(t, t.Play()));
                sb.Append(Dir(t, t.Play()));
                s.Tick(0.6f);
            }
            Check(sb.ToString() == "FxBxFxBx", "Toggle and the cooldown: " + sb);
            // a list that cannot play backward: the backward check's refusal leaves the cell — every Toggle after the first is
            // refused, one warning
            FeedbackPlayer e = PlayerOn(s, Repeating(Act("e", 0.1f)));
            e.m_Direction = PlayerDirection.Toggle;
            int warned = s_Warnings.Count;
            sb.Clear();
            for (int k = 0; k < 5; k++)
            {
                sb.Append(Dir(e, e.Play()));
                s.Tick(0.1f);
            }
            Check(sb.ToString() == "Fxxxx" && Count(FeedbackPlayer.ReverseWarning, warned) == 1, "Toggle of an endless list: " + sb + " · warnings " + Count(FeedbackPlayer.ReverseWarning, warned));
            // the argument wins over the setting; its Toggle is the player's cell
            FeedbackPlayer f = PlayerOn(s, Act("f", 0.1f));
            sb.Clear();
            sb.Append(Dir(f, f.Play(Backward)));
            sb.Append(Dir(f, f.Play()));
            for (int k = 0; k < 3; k++)
                sb.Append(Dir(f, f.Play(PlayArgs.Default.WithDirection(PlayerDirection.Toggle))));
            Check(sb.ToString() == "BFFBF", "the argument: " + sb);
            // an undefined argument: no handle, a warning that names it
            warned = s_Warnings.Count;
            PlaybackHandle bad = f.Play(PlayArgs.Default.WithDirection((PlayerDirection)7));
            Check(bad == default && s_Warnings.Skip(warned).Count(w => w.StartsWith(FeedbackPlayer.ArgumentWarning, StringComparison.Ordinal) && w.Contains("direction 7")) == 1,
                "an undefined direction: " + bad + " · " + string.Join(" | ", s_Warnings.Skip(warned)));
            // an unknown saved value is Forward
            FeedbackPlayer u = PlayerOn(s, Act("u", 0.1f));
            u.m_Direction = (PlayerDirection)9;
            Check(Dir(u, u.Play()) == "F", "an unknown saved value");
            // the listener cell makes PlayArgs 32 B
            Check(UnsafeUtility.SizeOf<PlayArgs>() == 32, "PlayArgs is " + UnsafeUtility.SizeOf<PlayArgs>() + " B");
            // a play that faulted at t = 0 was accepted: it turned the cell and set the cooldown
            FeedbackPlayer z = PlayerOn(s, Act("z", 0.1f));
            z.m_Direction = PlayerDirection.Toggle;
            z.m_Cooldown = 0.5f;
            PlayerTestBinding.ThrowsIn.Add("z.Begin");
            LogAssert.Expect(LogType.Exception, new Regex("test z.Begin"));
            PlaybackHandle faulted = z.Play();
            PlaybackHandle cooled = z.Play();
            PlayerTestBinding.ThrowsIn.Clear();
            bool turned = z.Runtime.ToggleBackward;
            s.Tick(0.6f);
            string next = Dir(z, z.Play());
            Check(faulted == default && cooled == default && turned && next == "B", "a faulted play: " + faulted + " · " + cooled + " · turned " + turned + " · next " + next);
            // Rebuild hands the cell over
            FeedbackPlayer r = PlayerOn(s, Act("r", 0.1f));
            r.m_Direction = PlayerDirection.Toggle;
            string before = Dir(r, r.Play());
            r.Rebuild();
            string after = Dir(r, r.Play());
            Check(before == "F" && after == "B", "Toggle after Rebuild: " + before + " " + after);
            End();
        }

        // ---- the backward refusal

        static FeedbackItem[] Endless() => new FeedbackItem[] { Repeating(Act("e", 0.1f)) };

        static FeedbackItem[] OpenWait() => new FeedbackItem[] { new WaitForSignalItem(), Act("after", 0.1f, 0f, StartRule.AfterPrevious) };

        static FeedbackItem[] ZeroPeriodBackground()
        {
            PlayerTestAction background = Repeating(Act("bg", 0f));
            background.m_Timing.m_Background = true;
            return new FeedbackItem[] { Act("fg", 0.5f), background };
        }

        static FeedbackItem[] Saturated() => new FeedbackItem[] { Act("s1", 6e12f), Act("s2", 6e12f, 0f, StartRule.AfterPrevious) };

        string Seeds(FeedbackScheduler s, bool refusedFirst, out bool refused)
        {
            FeedbackPlayer p = PlayerOn(s, Endless());
            p.m_Chance = 50f;
            p.EnsureRuntime();
            p.Runtime.Reseed(42u);
            refused = false;
            if (refusedFirst)
            {
                int warned = s_Warnings.Count;
                refused = p.Play(Backward) == default && Count(FeedbackPlayer.ReverseWarning, warned) == 1;
            }
            var sb = new StringBuilder();
            for (int k = 0; k < 4; k++)
            {
                PlaybackHandle h = p.Play();
                sb.Append(h == default ? "x" : Rec(p, h).Seed.ToString("X8", CultureInfo.InvariantCulture)).Append(' ');
                p.Stop();
            }
            return sb.ToString();
        }

        [Test]
        public void DE02_BackwardRefusal()
        {
            FeedbackScheduler s = Sched();
            var lists = new (string Name, Func<FeedbackItem[]> Make, bool StaticPasses)[]
            {
                ("an endless repeat", Endless, false),
                ("an open wait", OpenWait, false),
                ("a zero-period endless background", ZeroPeriodBackground, true),
                ("a saturated total", Saturated, true),
            };
            foreach ((string name, Func<FeedbackItem[]> make, bool staticPasses) in lists)
            {
                FeedbackPlayer p = PlayerOn(s, make());
                Watch(p);
                PlaybackHandle forward = p.Play();
                PlayerTestBinding.Log.Clear();
                int warned = s_Warnings.Count;
                PlaybackHandle back = p.Play(Backward);
                PlaybackHandle again = p.Play(Backward);
                Check(back == default && again == default, name + ": refused");
                Check(forward != default && Rec(p, forward) != null && Events() == "", name + ": the forward play goes on, no event (" + Events() + ")");
                Check(Count(FeedbackPlayer.ReverseWarning, warned) == 1, name + ": warnings " + Count(FeedbackPlayer.ReverseWarning, warned));
                // the static check lets the last two through — the confirmed result is what the backward check reads
                Check(p.Runtime.Program.IsSeekable == staticPasses, name + ": the static check " + p.Runtime.Program.IsSeekable);
            }
            // the random is put back: the next four seeds are the ones of a player that was never asked
            string asked = Seeds(s, true, out bool refusedBy7a);
            string control = Seeds(s, false, out _);
            Check(refusedBy7a && asked == control, "the seeds after a refusal: " + asked + " · control " + control + " · backward check " + refusedBy7a);
            // a wait with a timeout plays backward
            FeedbackPlayer w = PlayerOn(s, new WaitForSignalItem { m_Timeout = 0.3f }, Act("after", 0.1f, 0f, StartRule.AfterPrevious));
            Check(w.Play(Backward) != default, "a wait with a timeout");
            // the refusal sets no cooldown
            FeedbackPlayer c = PlayerOn(s, Endless());
            c.m_Cooldown = 0.5f;
            bool refusedC = c.Play(Backward) == default;
            bool acceptedC = c.Play() != default;
            Check(refusedC && acceptedC, "the cooldown after a refusal: " + refusedC + " · " + acceptedC);
            End();
        }

        // ---- the direction's conditions

        [Test]
        public void DE03_DirectionConditions()
        {
            FeedbackScheduler s = Sched();
            // a 0 ~ 0.3, then f (ForwardOnly) and r (ReverseOnly) at its end — a backward play begins there
            FeedbackPlayer p = PlayerOn(s, Act("a", 0.3f), Only(Act("f", 0f, 0f, StartRule.AfterPrevious), DirectionCondition.ForwardOnly), Only(Act("r", 0f), DirectionCondition.ReverseOnly));
            p.Play();
            Run(s, 3, 0.1f);
            string forward = Called(TakeLog());
            p.Play(Backward);
            string begun = TakeLog();
            Run(s, 3, 0.1f);
            string backward = Called(begun + " " + TakeLog());
            Check(forward == "a f" && backward == "r a", "the items called: forward " + forward + " · backward " + backward);
            // a one-direction instant does its forward act in its own direction — a begins on the T = 1 side
            Check(begun == "r.Execute#0 a.Begin#0~ a.Evaluate(1)", "the backward play's start: " + begun);
            GameObject t = At("t3", new Vector3(1f, 0f, 0f));
            GameObject g = Go("g3");
            int reverseOnly = 0, always = 0;
            FeedbackPlayer q = PlayerOn(s,
                Only(Pos(t, TweenMode.To, new Vector3(0f, 5f, 0f), 0.2f), DirectionCondition.ReverseOnly),
                Only(Timed(SetActive(g, false), 0.1f), DirectionCondition.ReverseOnly),
                Only(Timed(Event(() => reverseOnly++), 0.1f), DirectionCondition.ReverseOnly),
                Timed(Event(() => always++), 0.1f));
            q.Play();
            Run(s, 4, 0.1f);
            Check(reverseOnly == 0 && always == 1 && g.activeSelf && P(t) == "(1, 0, 0)", "forward: " + reverseOnly + " · " + always + " · " + g.activeSelf + " · " + P(t));
            q.Play(Backward);
            Run(s, 4, 0.1f);
            Check(!g.activeSelf && reverseOnly == 1 && always == 1 && P(t) == "(1, 0, 0)", "backward: " + g.activeSelf + " · " + reverseOnly + " · " + always + " · " + P(t));
            End();
        }

        // the labels of the Begin · Execute lines, in order
        static string Called(string log) => string.Join(" ", log.Split(' ').Where(l => l.Contains(".Begin") || l.Contains(".Execute")).Select(l => l.Substring(0, l.IndexOf('.'))));

        // ---- the backward play's events

        // i0 · a 0 ~ 0.3 · i1 at 0.3 · b 0.3 ~ 0.5 · i2 at 0.5
        static FeedbackItem[] Mirror() => new FeedbackItem[] { Act("i0", 0f), Act("a", 0.3f), Act("i1", 0f, 0f, StartRule.AfterPrevious), Act("b", 0.2f), Act("i2", 0f, 0f, StartRule.AfterPrevious) };

        [Test]
        public void DE04_BackwardEvents()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = PlayerOn(s, Mirror());
            Watch(p);
            p.Play(Backward);
            var ticks = new List<string> { TakeLog() };
            for (int k = 0; k < 5; k++)
            {
                s.Tick(0.1f);
                ticks.Add(TakeLog());
            }
            string[] expected =
            {
                "i2.Execute#0~ b.Begin#0~ b.Evaluate(1) |Played",
                "b.Evaluate(0.5)",
                "b.Evaluate(0) b.End:Completed i1.Execute#0~ a.Begin#0~ b.Settle a.Evaluate(1)",
                "a.Evaluate(0.667)",
                "a.Evaluate(0.333)",
                "a.Evaluate(0) a.End:Completed i0.Execute#0~ a.Settle |Completed",
            };
            for (int k = 0; k < expected.Length; k++)
                Check(ticks[k] == expected[k], "tick " + k + ": " + ticks[k]);
            // the start delay first: no event in its first tick
            FeedbackPlayer d = PlayerOn(s, Act("a", 0.2f));
            d.m_StartDelay = 0.2f;
            Watch(d);
            d.Play(Backward);
            ticks.Clear();
            ticks.Add(TakeLog());
            for (int k = 0; k < 4; k++)
            {
                s.Tick(0.1f);
                ticks.Add(TakeLog());
            }
            Check(ticks[0] == "|Played" && ticks[1] == "" && ticks[2] == "a.Begin#0~ a.Evaluate(1)" && ticks[3] == "a.Evaluate(0.5)"
                  && ticks[4] == "a.Evaluate(0) a.End:Completed a.Settle |Completed", "the start delay: " + string.Join(" / ", ticks));
            // the speed: 2 takes half the ticks
            FeedbackPlayer v = PlayerOn(s, Act("a", 0.4f));
            v.m_Speed = 2f;
            Watch(v);
            v.Play(Backward);
            s.Tick(0.1f);
            s.Tick(0.1f);
            string fast = TakeLog();
            Check(fast.EndsWith("|Completed", StringComparison.Ordinal) && !v.IsPlaying, "the speed 2: " + fast);
            // the pause stands the head
            FeedbackPlayer w = PlayerOn(s, Act("a", 0.4f));
            w.Play(Backward);
            s.Tick(0.1f);
            w.Pause();
            Run(s, 3, 0.1f);
            long paused = Position(w);
            w.Resume();
            s.Tick(0.1f);
            Check(paused == 300000 && Position(w) == 200000, "the pause: " + paused + " → " + Position(w));
            End();
        }

        // ---- ν's clock

        [Test]
        public void DE05_PositionClock()
        {
            FeedbackScheduler s = Sched();
            var unscaledOnly = new FrameTime(0f, 0.1f);
            // every item Unscaled: the play ends on the unscaled time — forward and backward, with the scaled dt 0
            foreach (bool backward in new[] { false, true })
            {
                FeedbackPlayer p = PlayerOn(s, Clock(Act("u", 0.3f), ItemClock.Unscaled));
                PlayerTestBinding.Log.Clear();
                p.Play(backward ? Backward : PlayArgs.Default);
                string first = TakeLog();
                Check(first == (backward ? "u.Begin#0~ u.Evaluate(1)" : "u.Begin#0 u.Evaluate(0)"), (backward ? "backward" : "forward") + " on the unscaled clock begins: " + first);
                int ticks = 0;
                while (p.IsPlaying && ticks < 8)
                {
                    s.Tick(unscaledOnly);
                    ticks++;
                }
                Check(ticks == 3 && !p.IsPlaying, (backward ? "backward" : "forward") + " on the unscaled clock: " + ticks + " ticks");
            }
            // mixed clocks: the time mode's (Scaled) — the head stands
            FeedbackPlayer m = PlayerOn(s, Clock(Act("u", 0.3f), ItemClock.Unscaled), Clock(Act("s", 0.3f), ItemClock.Scaled));
            m.Play(Backward);
            for (int k = 0; k < 3; k++)
                s.Tick(unscaledOnly);
            Check(m.IsPlaying && Position(m) == 300000, "mixed clocks: " + Position(m));
            // the program's function is the cursor's, for every mask
            ItemClock[][] masks =
            {
                new[] { ItemClock.Scaled }, new[] { ItemClock.Unscaled }, new[] { ItemClock.Player },
                new[] { ItemClock.Scaled, ItemClock.Unscaled }, new[] { ItemClock.Scaled, ItemClock.Player },
                new[] { ItemClock.Unscaled, ItemClock.Player }, new[] { ItemClock.Scaled, ItemClock.Unscaled, ItemClock.Player },
            };
            foreach (ItemClock[] mask in masks)
            {
                FeedbackPlayer p = PlayerOn(s, mask.Select((c, i) => (FeedbackItem)Clock(Act("c" + i, 0.1f), c)).ToArray());
                p.EnsureRuntime();
                foreach (TimelineClock baseClock in new[] { TimelineClock.Scaled, TimelineClock.Unscaled })
                {
                    TimelineCursor cursor = p.Runtime.Record(0).Cursor;
                    cursor.Begin(baseClock);
                    TimelineClock program = p.Runtime.Program.PositionClock(baseClock);
                    Check(cursor.PositionClock == program, string.Join("+", mask) + " on " + baseClock + ": cursor " + cursor.PositionClock + " · program " + program);
                }
            }
            End();
        }

        // ---- the origin

        [Test]
        public void DE06_Origin()
        {
            FeedbackScheduler s = Sched();
            // a forward To, then backward: back to the value before it
            GameObject t = At("t6", Vector3.zero);
            FeedbackPlayer p = PlayerOn(s, Pos(t, TweenMode.To, new Vector3(10f, 0f, 0f), 0.4f));
            p.Play();
            Run(s, 4, 0.1f);
            var seq = new List<string>();
            p.Play(Backward);
            seq.Add(X(t));
            for (int k = 0; k < 4; k++)
            {
                s.Tick(0.1f);
                seq.Add(X(t));
            }
            Check(string.Join(" ", seq) == "10 7.5 5 2.5 0", "backward after forward: " + string.Join(" ", seq));
            // backward first: from the value it finds — the first evaluation is the end state
            GameObject first = At("first", new Vector3(3f, 0f, 0f));
            FeedbackPlayer q = PlayerOn(s, Pos(first, TweenMode.To, new Vector3(10f, 0f, 0f), 0.4f));
            seq.Clear();
            q.Play(Backward);
            seq.Add(X(first));
            for (int k = 0; k < 4; k++)
            {
                s.Tick(0.1f);
                seq.Add(X(first));
            }
            Check(string.Join(" ", seq) == "10 8.25 6.5 4.75 3", "backward first: " + string.Join(" ", seq));
            // three occurrences: each from the origin backward
            GameObject rep = At("rep", Vector3.zero);
            PositionItem repeated = Pos(rep, TweenMode.To, new Vector3(10f, 0f, 0f), 0.2f);
            repeated.m_Timing.m_RepeatCount = 2;
            FeedbackPlayer r = PlayerOn(s, repeated);
            r.Play();
            Run(s, 12, 0.05f);
            seq.Clear();
            r.Play(Backward);
            seq.Add(X(rep));
            for (int k = 0; k < 12; k++)
            {
                s.Tick(0.05f);
                seq.Add(X(rep));
            }
            Check(string.Join(" ", seq) == "10 7.5 5 2.5 10 7.5 5 2.5 10 7.5 5 2.5 0", "three occurrences backward: " + string.Join(" ", seq));
            // the ends of a backward play: Revert is the value before it, Complete the T = 0 side, Hold where it is
            foreach ((StopMode mode, string want) in new[] { (StopMode.Revert, "10"), (StopMode.Complete, "0"), (StopMode.Hold, "5") })
            {
                GameObject e = At("end", Vector3.zero);
                FeedbackPlayer pe = PlayerOn(s, Pos(e, TweenMode.To, new Vector3(10f, 0f, 0f), 0.4f));
                pe.Play();
                Run(s, 4, 0.1f);
                pe.Play(Backward);
                Run(s, 2, 0.1f);
                string middle = X(e);
                pe.Stop(mode);
                Check(middle == "5" && X(e) == want, "Stop(" + mode + ") backward: " + middle + " → " + X(e));
            }
            // backward first then forward: the backward play's value is the origin, a From moves
            GameObject from = At("from", new Vector3(1f, 0f, 0f));
            FeedbackPlayer f = PlayerOn(s, Pos(from, TweenMode.From, new Vector3(0f, 5f, 0f), 0.2f));
            f.Play(Backward);
            Run(s, 2, 0.1f);
            string afterBack = P(from);
            f.Play();
            string start = P(from);
            Run(s, 2, 0.1f);
            Check(afterBack == "(0, 5, 0)" && start == "(0, 5, 0)" && P(from) == "(1, 0, 0)", "a From after a backward first play: " + afterBack + " → " + start + " → " + P(from));
            // Rebuild after the first frame and after a backward play keeps the origin
            GameObject a = At("applied", new Vector3(1f, 0f, 0f));
            FeedbackPlayer pa = PlayerOn(s, Pos(a, TweenMode.From, new Vector3(0f, 5f, 0f), 0.2f));
            pa.m_ApplyInitialState = true;
            pa.EnsureRuntime();
            pa.Rebuild();
            pa.Play();
            seq.Clear();
            seq.Add(P(a));
            for (int k = 0; k < 2; k++)
            {
                s.Tick(0.1f);
                seq.Add(P(a));
            }
            Check(string.Join(" ", seq) == "(0, 5, 0) (0.5, 2.5, 0) (1, 0, 0)", "Rebuild after the first frame: " + string.Join(" ", seq));
            GameObject b = At("backed", new Vector3(1f, 0f, 0f));
            FeedbackPlayer pb = PlayerOn(s, Pos(b, TweenMode.From, new Vector3(0f, 5f, 0f), 0.2f));
            pb.Play();
            Run(s, 2, 0.1f);
            pb.Play(Backward);
            Run(s, 2, 0.1f);
            pb.Rebuild();
            pb.Play();
            seq.Clear();
            seq.Add(P(b));
            Run(s, 2, 0.1f);
            seq.Add(P(b));
            Check(string.Join(" ", seq) == "(0, 5, 0) (1, 0, 0)", "Rebuild after a backward play: " + string.Join(" ", seq));
            End();
        }

        // ---- the forward play after a backward one

        // the jumps at the plays' t = 0: count and distance
        static (int Count, float Distance) Jumps(FeedbackScheduler s, FeedbackPlayer p, GameObject t, int plays)
        {
            int count = 0;
            float distance = 0f;
            for (int k = 0; k < plays; k++)
            {
                Vector3 before = t.transform.localPosition;
                p.Play();
                float jump = (t.transform.localPosition - before).magnitude;
                if (jump > 1e-4f)
                {
                    count++;
                    distance += jump;
                }
                s.Tick(0.1f);
            }
            return (count, distance);
        }

        [Test]
        public void DE07_ForwardAfterBackward()
        {
            FeedbackScheduler s = Sched();
            var to = new Vector3(0f, 5f, 0f);
            // From and To toggled four rounds, each run through: every round moves
            foreach (TweenMode mode in new[] { TweenMode.From, TweenMode.To })
            {
                GameObject t = At("t7", new Vector3(1f, 0f, 0f));
                FeedbackPlayer p = PlayerOn(s, Pos(t, mode, to, 0.2f));
                p.m_Direction = PlayerDirection.Toggle;
                var rounds = new List<string>();
                for (int k = 0; k < 4; k++)
                {
                    p.Play();
                    string a = P(t);
                    Run(s, 2, 0.1f);
                    rounds.Add(a + "→" + P(t));
                }
                string want = mode == TweenMode.From
                    ? "(0, 5, 0)→(1, 0, 0) (1, 0, 0)→(0, 5, 0) (0, 5, 0)→(1, 0, 0) (1, 0, 0)→(0, 5, 0)"
                    : "(1, 0, 0)→(0, 5, 0) (0, 5, 0)→(1, 0, 0) (1, 0, 0)→(0, 5, 0) (0, 5, 0)→(1, 0, 0)";
                Check(string.Join(" ", rounds) == want, mode + " toggled: " + string.Join(" ", rounds));
            }
            // a cut Toggle: the origin does not drift — a full backward play after it ends at the first value
            GameObject c = At("cut", new Vector3(1f, 0f, 0f));
            FeedbackPlayer pc = PlayerOn(s, Pos(c, TweenMode.To, to, 0.4f));
            pc.m_Direction = PlayerDirection.Toggle;
            (int jumps, float distance) = Jumps(s, pc, c, 6);
            Run(s, 3, 0.1f);
            Check(Near(PosBinding(pc).Origin, new Vector3(1f, 0f, 0f)) && P(c) == "(1, 0, 0)", "a cut Toggle's origin: " + V(PosBinding(pc).Origin) + " · the full backward play ends " + P(c));
            // the cut Toggle's jumps: three
            Check(jumps == 3 && Mathf.Abs(distance - 5.736f) < 0.01f, "a cut Toggle's jumps: " + jumps + " · " + F(distance));
            // moved after a full backward play: a To goes on from the moved value, a From goes to the origin
            GameObject mt = At("movedTo", new Vector3(1f, 0f, 0f));
            FeedbackPlayer pt = PlayerOn(s, Pos(mt, TweenMode.To, to, 0.2f));
            pt.Play();
            Run(s, 2, 0.1f);
            pt.Play(Backward);
            Run(s, 2, 0.1f);
            mt.transform.localPosition = new Vector3(7f, 7f, 7f);
            pt.Play();
            string toStart = P(mt);
            Run(s, 2, 0.1f);
            pt.Play(Backward);
            Run(s, 2, 0.1f);
            Check(toStart == "(7, 7, 7)" && P(mt) == "(7, 7, 7)", "a To moved after a backward play: starts " + toStart + " · the next backward play ends " + P(mt));
            GameObject mf = At("movedFrom", new Vector3(1f, 0f, 0f));
            FeedbackPlayer pf = PlayerOn(s, Pos(mf, TweenMode.From, to, 0.2f));
            pf.Play();
            Run(s, 2, 0.1f);
            pf.Play(Backward);
            Run(s, 2, 0.1f);
            mf.transform.localPosition = new Vector3(7f, 7f, 7f);
            pf.Play();
            Run(s, 2, 0.1f);
            Check(P(mf) == "(1, 0, 0)", "a From moved after a backward play goes to the origin: " + P(mf));
            // Rebuild after a cut backward play hands the cut over
            GameObject rb = At("rebuilt", new Vector3(1f, 0f, 0f));
            FeedbackPlayer pr = PlayerOn(s, Pos(rb, TweenMode.To, to, 0.4f));
            pr.Play();
            Run(s, 4, 0.1f);
            pr.Play(Backward);
            s.Tick(0.1f);
            string cutAt = P(rb);
            pr.Rebuild();
            pr.Play();
            Run(s, 4, 0.1f);
            pr.Play(Backward);
            Run(s, 4, 0.1f);
            Check(cutAt == "(0.25, 3.75, 0)" && P(rb) == "(1, 0, 0)", "Rebuild after a cut backward play: cut at " + cutAt + " · the full backward play ends " + P(rb));
            End();
        }

        // ---- Seek

        // t 0 ~ 0.6 · instants i1 0.1 · i2 0.3 · i3 0.5 — WithPrevious chains the starts
        static FeedbackItem[] Seekable() => new FeedbackItem[] { Act("t", 0.6f), Act("i1", 0f, 0.1f), Act("i2", 0f, 0.2f), Act("i3", 0f, 0.2f) };

        [Test]
        public void DE08_Reg14_Seek()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = PlayerOn(s, Seekable());
            Watch(p);
            Check(!p.Seek(1f), "no play: false");
            p.Play();
            PlayerTestBinding.Log.Clear();
            bool ahead = p.Seek(0.4f);
            string aheadLog = TakeLog();
            bool back = p.Seek(0.2f);
            string backLog = TakeLog();
            bool same = p.Seek(0.2f);
            string sameLog = TakeLog();
            Check(ahead && aheadLog == "i1.Execute#0 i2.Execute#0 t.Evaluate(0.667)", "Seek(0.4): " + ahead + " " + aheadLog);
            Check(back && backLog == "i2.Execute#0~ t.Evaluate(0.333)", "Seek(0.2) back: " + back + " " + backLog);
            Check(same && sameLog == "t.Evaluate(0.333)", "the same target: " + same + " " + sameLog);
            int warned = s_Warnings.Count;
            bool negative = p.Seek(-1f);
            bool nan = p.Seek(float.NaN);
            Check(!negative && !nan && Position(p) == 200000 && Count(FeedbackPlayer.ArgumentWarning, warned) == 1 && TakeLog() == "",
                "−1 · NaN: " + negative + " " + nan + " · " + Position(p) + " · warnings " + Count(FeedbackPlayer.ArgumentWarning, warned));
            s.Tick(0.1f);
            Check(TakeLog() == "i2.Execute#0 t.Evaluate(0.5)", "the tick after the seek runs i2 again");
            // the end: Completed at once — 10 and +∞ are the forward end
            bool end = p.Seek(10f);
            Check(end && TakeLog() == "i3.Execute#0 t.Evaluate(1) t.End:Completed t.Settle |Completed" && !p.IsPlaying, "Seek(10)");
            p.Play();
            PlayerTestBinding.Log.Clear();
            Check(p.Seek(float.PositiveInfinity) && Events() == "|Completed" && !p.IsPlaying, "Seek(+∞): " + TakeLog());
            // a paused play moves and stays paused
            p.Play();
            p.Pause();
            bool pausedMoved = p.Seek(0.35f);
            Run(s, 2, 0.1f);
            Check(pausedMoved && Position(p) == 350000 && p.Runtime.Record(0).Paused, "a paused play: " + pausedMoved + " · " + Position(p));
            p.Stop();
            // backward: down and up — up is forward events — and Seek(0) ends it at once
            p.Play(Backward);
            p.Seek(0.45f);
            PlayerTestBinding.Log.Clear();
            bool down = p.Seek(0.15f);
            string downLog = string.Join(" ", PlayerTestBinding.Log.Where(l => l.Contains("Execute")));
            PlayerTestBinding.Log.Clear();
            bool up = p.Seek(0.55f);
            string upLog = string.Join(" ", PlayerTestBinding.Log.Where(l => l.Contains("Execute")));
            PlayerTestBinding.Log.Clear();
            s.Tick(0.1f);
            string tickLog = string.Join(" ", PlayerTestBinding.Log.Where(l => l.Contains("Execute")));
            PlayerTestBinding.Log.Clear();
            bool zero = p.Seek(0f);
            string zeroLog = TakeLog();
            Check(down && downLog == "i2.Execute#0~", "backward Seek(0.15): " + downLog);
            Check(up && upLog == "i2.Execute#0 i3.Execute#0", "backward Seek(0.55): " + upLog);
            Check(tickLog == "i3.Execute#0~", "backward tick: " + tickLog);
            Check(zero && zeroLog == "i2.Execute#0~ i1.Execute#0~ t.Evaluate(0) t.End:Completed t.Settle |Completed" && !p.IsPlaying, "backward Seek(0): " + zeroLog);
            // the axis does not turn: a backward play's +∞ is its start — it goes on
            p.Play(Backward);
            s.Tick(0.15f);
            PlayerTestBinding.Log.Clear();
            bool infinite = p.Seek(float.PositiveInfinity);
            string infLog = string.Join(" ", PlayerTestBinding.Log.Where(l => l.Contains("Execute") || l[0] == '|'));
            long atStart = Position(p);
            PlayerTestBinding.Log.Clear();
            s.Tick(0.1f);
            string nextLog = string.Join(" ", PlayerTestBinding.Log.Where(l => l.Contains("Execute") || l[0] == '|'));
            Check(infinite && infLog == "i3.Execute#0" && atStart == 600000 && nextLog == "i3.Execute#0~" && p.IsPlaying && Position(p) == 500000,
                "backward Seek(+∞): " + infLog + " · " + atStart + " · then " + nextLog + " · " + Position(p));
            End();
        }

        // ---- off the nominal axis

        [Test]
        public void DE09_OffNominal()
        {
            FeedbackScheduler s = Sched();
            // two clocks: the fast one passed an instant — Seek is false, Skip runs the rest once each
            FeedbackPlayer p = PlayerOn(s, Clock(Act("s", 1f), ItemClock.Scaled), Clock(Act("u", 0.4f), ItemClock.Unscaled),
                Clock(Act("i", 0f, 0f, StartRule.AfterPrevious), ItemClock.Unscaled), Clock(Act("k", 0f, 0.5f), ItemClock.Scaled));
            Watch(p);
            p.Play();
            for (int k = 0; k < 5; k++)
                s.Tick(new FrameTime(0.05f, 0.1f));
            string before = TakeLog();
            bool off = p.Runtime.Record(0).Cursor.IsOffNominal;
            long place = Position(p);
            bool seek = p.Seek(0.45f);
            string seekLog = TakeLog();
            bool skip = p.Skip();
            string skipLog = string.Join(" ", PlayerTestBinding.Log.Where(l => l.Contains("Execute") || l.Contains("End") || l[0] == '|'));
            PlayerTestBinding.Log.Clear();
            Check(before.Contains("i.Execute#0") && off && place == 250000, "two clocks: " + off + " · " + place);
            Check(!seek && seekLog == "", "two clocks: Seek " + seek + " " + seekLog);
            Check(skip && skipLog == "k.Execute#0 s.End:Completed |Completed", "two clocks: Skip " + skipLog);
            // a signal before the wait's timeout: Seek false, Skip runs what is left once
            FeedbackPlayer w = PlayerOn(s, new WaitForSignalItem { m_Timeout = 0.5f }, Act("after", 0.2f, 0f, StartRule.AfterPrevious), Act("j", 0f, 0f, StartRule.AfterPrevious));
            Watch(w);
            w.Play();
            s.Tick(0.1f);
            w.Signal();
            s.Tick(0.1f);
            PlayerTestBinding.Log.Clear();
            bool early = w.Runtime.Record(0).Cursor.IsOffNominal;
            bool seekW = w.Seek(0.9f);
            bool skipW = w.Skip();
            string skipWLog = string.Join(" ", PlayerTestBinding.Log.Where(l => l.Contains("Begin") || l.Contains("Execute") || l.Contains("End") || l[0] == '|'));
            Check(early && !seekW && skipW && skipWLog == "after.End:Completed j.Execute#0 |Completed", "an early signal: " + early + " · Seek " + seekW + " · Skip " + skipWLog);
            End();
        }

        // ---- Seek · Skip inside the play's events

        [Test]
        public void DE10_InsideEvents()
        {
            FeedbackScheduler s = Sched();
            // i1's Execute asks for Seek(0.45): at the pass's end — i1 once, the place is the target
            FeedbackPlayer p = PlayerOn(s, Seekable());
            Watch(p);
            bool asked = false;
            bool taken = false;
            Hooks((b, what, r) =>
            {
                if (what == "Execute" && b.Item.m_Label == "i1" && !asked)
                {
                    asked = true;
                    taken = p.Seek(0.45f);
                }
            });
            p.Play();
            PlayerTestBinding.Log.Clear();
            s.Tick(0.15f);
            string log = TakeLog();
            Check(taken && log.Split(' ').Count(l => l.StartsWith("i1.Execute", StringComparison.Ordinal)) == 1 && log.Contains("i2.Execute#0") && Position(p) == 450000,
                "Seek inside: " + log + " · " + Position(p));
            Run(s, 2, 0.1f);
            Check(TakeLog().Contains("|Completed"), "it goes on to its end");
            // Skip inside: at the pass's end
            FeedbackPlayer q = PlayerOn(s, Seekable());
            Watch(q);
            bool skipped = false;
            Hooks((b, what, r) =>
            {
                if (what == "Execute" && b.Item.m_Label == "i1" && !skipped)
                {
                    skipped = true;
                    q.Skip();
                }
            });
            q.Play();
            PlayerTestBinding.Log.Clear();
            s.Tick(0.15f);
            string skipLog = TakeLog();
            Check(skipLog.EndsWith("i2.Execute#0 i3.Execute#0 t.Evaluate(1) t.End:Completed t.Settle |Completed", StringComparison.Ordinal) && !q.IsPlaying, "Skip inside: " + skipLog);
            // Skip first; what the deferred seek's own events ask waits for the next pass
            FeedbackPlayer r2 = PlayerOn(s, Seekable());
            Watch(r2);
            bool nested = false;
            Hooks((b, what, r) =>
            {
                if (what == "Execute" && b.Item.m_Label == "i1" && !nested)
                {
                    nested = true;
                    r2.Seek(0.45f);
                    r2.Skip();
                }
            });
            r2.Play();
            PlayerTestBinding.Log.Clear();
            s.Tick(0.15f);
            Check(TakeLog().EndsWith("|Completed", StringComparison.Ordinal) && !r2.IsPlaying, "Skip before Seek");
            FeedbackPlayer n = PlayerOn(s, Seekable());
            bool inner = false;
            Hooks((b, what, r) =>
            {
                if (what == "Execute" && b.Item.m_Label == "i1" && !nested)
                {
                    nested = true;
                    n.Seek(0.45f);
                }
                else if (what == "Execute" && b.Item.m_Label == "i2" && !inner)
                {
                    inner = true;
                    n.Seek(0.2f);
                }
            });
            nested = false;
            n.Play();
            s.Tick(0.15f);
            long afterPass = Position(n);
            PlayerTestBinding.Log.Clear();
            s.Tick(0.1f);
            // the next pass: the tick runs i3, then the waiting Seek(0.2) undoes i3 and i2 in reverse
            string undone = string.Join(" ", PlayerTestBinding.Log.Where(l => l.Contains("Execute")));
            Check(inner && afterPass == 450000 && Position(n) == 200000 && undone == "i3.Execute#0 i3.Execute#0~ i2.Execute#0~",
                "the seek inside the deferred seek: " + afterPass + " → " + Position(n) + " · " + undone);
            // a direct Seek's sweep asks for a Seek: true, after the next tick's pass — over that tick's progress
            FeedbackPlayer d = PlayerOn(s, Seekable());
            bool askedD = false;
            bool innerD = false;
            Hooks((b, what, r) =>
            {
                if (what == "Execute" && b.Item.m_Label == "i1" && !askedD)
                {
                    askedD = true;
                    innerD = d.Seek(0.05f);
                }
            });
            d.Play();
            bool outer = d.Seek(0.4f);
            long afterOuter = Position(d);
            s.Tick(0.1f);
            Check(outer && innerD && afterOuter == 400000 && Position(d) == 50000, "a seek inside a seek's sweep: " + afterOuter + " → " + Position(d));
            // a paused play's deferred Skip waits for Resume's first tick
            FeedbackPlayer w = PlayerOn(s, Seekable());
            Watch(w);
            bool skipAsked = false;
            bool skipTaken = false;
            Hooks((b, what, r) =>
            {
                if (what == "Execute" && b.Item.m_Label == "i1" && !skipAsked)
                {
                    skipAsked = true;
                    skipTaken = w.Skip();
                }
            });
            w.Play();
            w.Pause();
            w.Seek(0.3f);
            PlayerTestBinding.Log.Clear();
            Run(s, 3, 0.1f);
            long held = Position(w);
            string pausedLog = Events();
            w.Resume();
            s.Tick(0.1f);
            Check(skipTaken && held == 300000 && pausedLog == "" && Events().Contains("|Completed") && !w.IsPlaying, "a paused play's deferred Skip: " + held + " · " + Events());
            End();
        }

        // ---- the same ν

        [Test]
        public void DE11_SameNu()
        {
            FeedbackScheduler s = Sched();
            var results = new List<(Vector3 Value, uint Seed)>();
            for (int way = 0; way < 4; way++)
            {
                GameObject t = At("nu" + way, Vector3.zero);
                PositionItem shake = Item<PositionItem>(Shake(Vector4.one), Direct(t.transform), 0.3f);
                shake.m_Timing.m_RepeatCount = 2;
                FeedbackPlayer p = PlayerOn(s, shake);
                PlayArgs args = PlayArgs.Default.WithSeed(7u);
                switch (way)
                {
                    case 0:
                        p.Play(args);
                        Run(s, 4, 0.1f);
                        break;
                    case 1:
                        p.Play(args);
                        s.RunRevert();
                        p.Seek(0.4f);
                        s.RunLateApply();
                        break;
                    case 2:
                        p.Play(args.WithDirection(PlayerDirection.Backward));
                        s.Tick(0.5f);
                        break;
                    default:
                        p.Play(args);
                        s.Tick(0.75f);
                        s.RunRevert();
                        p.Seek(0.4f);
                        s.RunLateApply();
                        break;
                }
                results.Add((t.transform.localPosition, PosBinding(p).EvaluationSeed(0)));
            }
            bool same = results.All(r => Bits(r.Value, results[0].Value) && r.Seed == results[0].Seed);
            Check(same && results[0].Value != Vector3.zero, "ν 0.4 four ways: " + string.Join(" · ", results.Select(r => V(r.Value) + " " + r.Seed.ToString("X8", CultureInfo.InvariantCulture))));
            End();
        }

        // ---- Skip

        [Test]
        public void DE12_Skip()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = PlayerOn(s, Seekable());
            Watch(p);
            Check(!p.Skip(), "no play: false");
            p.Play();
            s.Tick(0.15f);
            PlayerTestBinding.Log.Clear();
            Check(p.Skip() && TakeLog() == "i2.Execute#0 i3.Execute#0 t.Evaluate(1) t.End:Completed t.Settle |Completed", "forward Skip");
            p.Play(Backward);
            s.Tick(0.15f);
            PlayerTestBinding.Log.Clear();
            Check(p.Skip() && TakeLog() == "i2.Execute#0~ i1.Execute#0~ t.Evaluate(0) t.End:Completed t.Settle |Completed", "backward Skip");
            // an endless repeat: Stop(Complete) — Stopped
            FeedbackPlayer e = PlayerOn(s, Repeating(Act("a", 0.2f)));
            Watch(e);
            e.Play();
            s.Tick(0.1f);
            PlayerTestBinding.Log.Clear();
            Check(e.Skip() && TakeLog() == "a.Evaluate(1) a.End:Complete a.Settle |Stopped", "an endless repeat");
            // a paused play too
            p.Play();
            p.Pause();
            PlayerTestBinding.Log.Clear();
            Check(p.Skip() && TakeLog() == "i1.Execute#0 i2.Execute#0 i3.Execute#0 t.Evaluate(1) t.End:Completed t.Settle |Completed", "a paused play");
            End();
        }

        // ---- Restore

        [Test]
        public void DE13_Restore()
        {
            FeedbackScheduler s = Sched();
            var ten = new Vector3(10f, 0f, 0f);
            // a To played twice goes back to the value before the first
            GameObject t = At("t13", Vector3.zero);
            FeedbackPlayer p = PlayerOn(s, Pos(t, TweenMode.To, ten, 0.2f));
            Watch(p);
            p.Play();
            Run(s, 2, 0.1f);
            p.Play();
            Run(s, 2, 0.1f);
            p.Restore();
            Check(X(t) == "0", "twice then Restore: " + X(t));
            // two items of one target: the earlier wins
            GameObject two = At("two", Vector3.zero);
            FeedbackPlayer q = PlayerOn(s, Pos(two, TweenMode.To, new Vector3(5f, 0f, 0f), 0.2f), Timed(Pos(two, TweenMode.To, ten, 0.2f), 0f, StartRule.AfterPrevious));
            q.Play();
            Run(s, 4, 0.1f);
            q.Restore();
            Check(X(two) == "0", "two items: " + X(two));
            // inside a play: Stopped and the first value
            p.Play();
            s.Tick(0.1f);
            PlayerTestBinding.Log.Clear();
            p.Restore();
            Check(Events() == "|Stopped" && X(t) == "0" && !p.IsPlaying, "inside a play: " + Events() + " · " + X(t));
            // a second Restore does nothing; the next play takes a new first value
            t.transform.localPosition = new Vector3(4f, 0f, 0f);
            p.Restore();
            string second = X(t);
            p.Play();
            Run(s, 2, 0.1f);
            p.Restore();
            Check(second == "4" && X(t) == "4", "the second Restore · the next first value: " + second + " · " + X(t));
            // a backward play first: the value before it
            GameObject from = At("from13", new Vector3(1f, 0f, 0f));
            FeedbackPlayer f = PlayerOn(s, Pos(from, TweenMode.From, new Vector3(0f, 5f, 0f), 0.2f));
            f.Play(Backward);
            Run(s, 2, 0.1f);
            f.Restore();
            Check(P(from) == "(1, 0, 0)", "backward first: " + P(from));
            // a SetActive Set
            GameObject g = Go("g13");
            FeedbackPlayer a = PlayerOn(s, SetActive(g, false));
            a.Play();
            bool off = !g.activeSelf;
            a.Restore();
            Check(off && g.activeSelf, "SetActive: " + off + " · " + g.activeSelf);
            // a player that is not prepared stays so
            FeedbackPlayer lazy = PlayerOn(Go("lazy"), null, Act("l", 0.1f));
            lazy.Restore();
            Check(lazy.Runtime == null, "not prepared");
            // a lost runtime (a recompile's shape) has nothing to put back
            GameObject lost = At("lost", Vector3.zero);
            FeedbackPlayer l2 = PlayerOn(s, Pos(lost, TweenMode.To, ten, 0.2f));
            l2.Play();
            Run(s, 2, 0.1f);
            l2.ReleaseRuntime();
            l2.EnsureRuntime();
            l2.Restore();
            Check(X(lost) == "10", "after a lost runtime: " + X(lost));
            // Rebuild hands the first value over
            GameObject rb = At("rb13", Vector3.zero);
            FeedbackPlayer r = PlayerOn(s, Pos(rb, TweenMode.To, ten, 0.2f));
            r.Play();
            Run(s, 2, 0.1f);
            r.Rebuild();
            r.Restore();
            Check(X(rb) == "0", "Restore after Rebuild: " + X(rb));
            // a play a Stopped subscriber starts goes on and writes over the first value at its next tick
            GameObject m7 = At("m7", Vector3.zero);
            FeedbackPlayer m = PlayerOn(s, Pos(m7, TweenMode.To, ten, 0.4f));
            Watch(m);
            bool replay = true;
            m.Stopped += (pl, h) =>
            {
                if (replay)
                {
                    replay = false;
                    m.Play();
                }
            };
            m.Play();
            s.Tick(0.1f);
            PlayerTestBinding.Log.Clear();
            m.Restore();
            string restoredTo = X(m7);
            string events = Events();
            s.Tick(0.1f);
            Check(events == "|Stopped |Played" && restoredTo == "0" && X(m7) == "4.375", "a play the Stopped subscriber starts: " + events + " · " + restoredTo + " → " + X(m7));
            // the main camera changed: the old camera's first value goes too (the scene's camera off)
            SilenceSceneCameras();
            GameObject camA = Go("camA");
            camA.tag = "MainCamera";
            camA.AddComponent<Camera>();
            FeedbackPlayer c = PlayerOn(s, Item<PositionItem>(Tween(TweenMode.To, Vector3.one), Ref(TargetRefMode.MainCamera), 0.2f));
            c.Play();
            Run(s, 2, 0.1f);
            camA.tag = "Untagged";
            GameObject camB = At("camB", new Vector3(7f, 7f, 7f));
            camB.tag = "MainCamera";
            camB.AddComponent<Camera>();
            c.Play();
            Run(s, 2, 0.1f);
            c.Restore();
            Check(P(camB) == "(7, 7, 7)" && P(camA) == "(1, 1, 1)", "the camera changed: B " + P(camB) + " · A " + P(camA));
            End();
        }

        // ---- Signal

        [Test]
        public void DE14_Signal()
        {
            FeedbackScheduler s = Sched();
            // outside a tick: the next tick's start
            FeedbackPlayer p = PlayerOn(s, new WaitForSignalItem(), Act("after", 0.2f, 0f, StartRule.AfterPrevious));
            p.Play();
            s.Tick(0.1f);
            int warned = s_Warnings.Count;
            p.Signal();
            PlayerTestBinding.Log.Clear();
            s.Tick(0.1f);
            Check(TakeLog() == "after.Begin#0 after.Evaluate(0.5)" && Count(FeedbackPlayer.SignalWarning, warned) == 0, "outside a tick");
            // no wait: one warning for two calls
            s = Sched();
            FeedbackPlayer n = PlayerOn(s, Act("n", 0.2f));
            n.Play();
            warned = s_Warnings.Count;
            n.Signal();
            n.Signal();
            Check(Count(FeedbackPlayer.SignalWarning, warned) == 1, "no wait: warnings " + Count(FeedbackPlayer.SignalWarning, warned));
            // inside a tick's event: that instant — a's end at 0.3 ends the wait that began with it, b runs from 0.3
            s = Sched();
            FeedbackPlayer q = PlayerOn(s, Act("a", 0.3f), new WaitForSignalItem(), Act("b", 0.2f, 0f, StartRule.AfterPrevious));
            int ended = -1;
            Hooks((b, what, r) =>
            {
                if (what == "End" && b.Item.m_Label == "a")
                    ended = q.Runtime.Signal();
            });
            q.Play();
            PlayerTestBinding.Log.Clear();
            s.Tick(0.35f);
            string inside = TakeLog();
            Check(ended == 1 && inside.Contains("b.Begin#0") && inside.EndsWith("b.Evaluate(0.25)", StringComparison.Ordinal), "inside an event: " + ended + " · " + inside);
            // an instant ahead of the wait at the same instant: 0 — missed, the wait stays
            s = Sched();
            FeedbackPlayer i = PlayerOn(s, Act("i", 0f, 0.1f), new WaitForSignalItem(), Act("after", 0.1f, 0f, StartRule.AfterPrevious));
            int early = -1;
            Hooks((b, what, r) =>
            {
                if (what == "Execute" && b.Item.m_Label == "i")
                    early = i.Runtime.Signal();
            });
            i.Play();
            Run(s, 4, 0.1f);
            Check(early == 0 && !PlayerTestBinding.Log.Any(l => l.StartsWith("after.Begin", StringComparison.Ordinal)), "ahead of the wait: " + early);
            Hooks();
            // before the wait: missed, a warning, still waiting 0.6 s on
            s = Sched();
            FeedbackPlayer w = PlayerOn(s, Act("a", 0.3f), Timed(new WaitForSignalItem(), 0f, StartRule.AfterPrevious), Act("after", 0.1f, 0f, StartRule.AfterPrevious));
            w.Play();
            s.Tick(0.1f);
            warned = s_Warnings.Count;
            w.Signal();
            PlayerTestBinding.Log.Clear();
            Run(s, 6, 0.1f);
            Check(Count(FeedbackPlayer.SignalWarning, warned) == 1 && !PlayerTestBinding.Log.Any(l => l.StartsWith("after.Begin", StringComparison.Ordinal)) && w.IsPlaying,
                "before the wait: warnings " + Count(FeedbackPlayer.SignalWarning, warned) + " · " + TakeLog());
            // a player that is not prepared warns too
            FeedbackPlayer lazy = PlayerOn(Go("lazy14"), null, Act("l", 0.1f));
            warned = s_Warnings.Count;
            lazy.Signal();
            Check(Count(FeedbackPlayer.SignalWarning, warned) == 1 && lazy.Runtime == null, "not prepared: " + Count(FeedbackPlayer.SignalWarning, warned));
            End();
        }

        // ---- the first frame

        [Test]
        public void DE15_InitialState()
        {
            FeedbackScheduler s = Sched();
            var one = new Vector3(1f, 0f, 0f);
            var five = new Vector3(0f, 5f, 0f);
            // the prepare writes a late item's t = 0 — no instant, no event, no play
            GameObject t = At("t15", one);
            GameObject g = Go("g15");
            int called = 0;
            FeedbackPlayer p = PlayerOn(s, Timed(Pos(t, TweenMode.From, five, 0.2f), 0.5f), SetActive(g, false), Event(() => called++));
            p.m_ApplyInitialState = true;
            Watch(p);
            p.EnsureRuntime();
            Check(P(t) == "(0, 5, 0)" && g.activeSelf && called == 0 && TakeLog() == "" && !p.IsPlaying, "the prepare: " + P(t) + " · " + g.activeSelf + " · " + called);
            // a play: the From stands at its first frame until it starts, then reaches the value before it
            p.Play();
            Run(s, 4, 0.1f);
            string held = P(t);
            Run(s, 3, 0.1f);
            Check(held == "(0, 5, 0)" && P(t) == "(1, 0, 0)", "the play: " + held + " → " + P(t));
            // two Froms of one target: the earlier wins
            GameObject two = At("two15", one);
            FeedbackPlayer q = PlayerOn(s, Pos(two, TweenMode.From, five, 0.2f), Pos(two, TweenMode.From, new Vector3(0f, 9f, 0f), 0.2f));
            q.m_ApplyInitialState = true;
            q.EnsureRuntime();
            Check(P(two) == "(0, 5, 0)", "two Froms: " + P(two));
            // Restore goes back to before it; a Rebuild before any play writes again; a backward play then starts from before it
            GameObject r = At("r15", one);
            FeedbackPlayer pr = PlayerOn(s, Pos(r, TweenMode.From, five, 0.2f));
            pr.m_ApplyInitialState = true;
            pr.EnsureRuntime();
            pr.Restore();
            string restored = P(r);
            r.transform.localPosition = new Vector3(2f, 0f, 0f);
            pr.Rebuild();
            string rebuilt = P(r);
            pr.Play(Backward);
            string backStart = P(r);
            Run(s, 2, 0.1f);
            Check(restored == "(1, 0, 0)" && rebuilt == "(0, 5, 0)" && backStart == "(2, 0, 0)" && P(r) == "(0, 5, 0)",
                "Restore · Rebuild · backward: " + restored + " · " + rebuilt + " · " + backStart + " → " + P(r));
            // Lazy: the first Play's prepare
            GameObject lz = At("lz15", one);
            FeedbackPlayer lazy = PlayerOn(s, Pos(lz, TweenMode.From, five, 0.2f));
            lazy.m_ApplyInitialState = true;
            lazy.m_Initialization = PlayerInitialization.Lazy;
            string unprepared = P(lz);
            lazy.Play();
            Check(unprepared == "(1, 0, 0)" && P(lz) == "(0, 5, 0)", "Lazy: " + unprepared + " → " + P(lz));
            // a To does not change it, a FromTo writes its From, an offset writes nothing
            GameObject tt = At("to15", one);
            GameObject ft = At("fromTo15", one);
            GameObject ot = At("offset15", one);
            FeedbackPlayer kinds = PlayerOn(s, Pos(tt, TweenMode.To, five, 0.2f), Item<PositionItem>(Tween(TweenMode.FromTo, Vector3.zero, five, Ease.Of(EaseType.Linear)), Direct(ft.transform), 0.2f),
                Item<PositionItem>(Shake(Vector4.one), Direct(ot.transform), 0.2f));
            kinds.m_ApplyInitialState = true;
            kinds.EnsureRuntime();
            Check(P(tt) == "(1, 0, 0)" && P(ft) == "(0, 0, 0)" && P(ot) == "(1, 0, 0)", "To · FromTo · offset: " + P(tt) + " · " + P(ft) + " · " + P(ot));
            // Backward: each item's end — a From stays, a To writes its target and the backward play does not jump
            GameObject bf = At("bFrom", one);
            GameObject bt = At("bTo", one);
            FeedbackPlayer back = PlayerOn(s, Pos(bf, TweenMode.From, five, 0.2f), Pos(bt, TweenMode.To, five, 0.2f));
            back.m_ApplyInitialState = true;
            back.m_Direction = PlayerDirection.Backward;
            back.EnsureRuntime();
            string prepared = P(bf) + " " + P(bt);
            back.Play();
            string atPlay = P(bf) + " " + P(bt);
            Run(s, 2, 0.1f);
            Check(prepared == "(1, 0, 0) (0, 5, 0)" && atPlay == prepared && P(bf) + " " + P(bt) == "(0, 5, 0) (1, 0, 0)", "Backward: " + prepared + " · " + atPlay + " → " + P(bf) + " " + P(bt));
            // the direction's conditions: an item the set direction leaves out writes nothing
            GameObject rof = At("reverseOnly", one);
            GameObject fob = At("forwardOnly", one);
            FeedbackPlayer fwd = PlayerOn(s, Only(Pos(rof, TweenMode.From, five, 0.2f), DirectionCondition.ReverseOnly));
            fwd.m_ApplyInitialState = true;
            fwd.EnsureRuntime();
            FeedbackPlayer bwd = PlayerOn(s, Only(Pos(fob, TweenMode.To, five, 0.2f), DirectionCondition.ForwardOnly));
            bwd.m_ApplyInitialState = true;
            bwd.m_Direction = PlayerDirection.Backward;
            bwd.EnsureRuntime();
            Check(P(rof) == "(1, 0, 0)" && P(fob) == "(1, 0, 0)", "conditions: " + P(rof) + " · " + P(fob));
            // Backward and two items of one target: list order — the later wins; the backward play ends at the value before
            GameObject two2 = At("two2", Vector3.zero);
            FeedbackPlayer tb = PlayerOn(s, Pos(two2, TweenMode.To, new Vector3(5f, 0f, 0f), 0.2f), Timed(Pos(two2, TweenMode.To, new Vector3(10f, 0f, 0f), 0.2f), 0f, StartRule.AfterPrevious));
            tb.m_ApplyInitialState = true;
            tb.m_Direction = PlayerDirection.Backward;
            tb.EnsureRuntime();
            var seq = new List<string> { X(two2) };
            tb.Play();
            seq.Add(X(two2));
            for (int k = 0; k < 4; k++)
            {
                s.Tick(0.1f);
                seq.Add(X(two2));
            }
            Check(string.Join(" ", seq) == "10 10 7.5 5 2.5 0", "Backward · two items: " + string.Join(" ", seq));
            // the intensity of a Play without an argument: By and a Custom ease from 0.5 do not jump
            var custom = new Ease { m_Type = EaseType.Custom, m_Curve = AnimationCurve.Linear(0f, 0.5f, 1f, 1f) };
            GameObject by = At("by15", Vector3.zero);
            FeedbackPlayer pb = PlayerOn(s, Item<PositionItem>(Tween(TweenMode.By, Vector3.zero, new Vector3(2f, 0f, 0f), custom), Direct(by.transform), 0.2f));
            pb.m_ApplyInitialState = true;
            pb.m_Intensity = 0.5f;
            pb.EnsureRuntime();
            string appliedBy = X(by);
            pb.Play();
            Check(appliedBy == "0.5" && X(by) == "0.5", "By · Custom at the base intensity 0.5: " + appliedBy + " → " + X(by));
            // a Rebuild after a play leaves the value; the next forward play goes from its first frame
            GameObject vis = At("visible", one);
            FeedbackPlayer pv = PlayerOn(s, Pos(vis, TweenMode.From, five, 0.2f));
            pv.m_ApplyInitialState = true;
            pv.EnsureRuntime();
            pv.Play();
            Run(s, 2, 0.1f);
            pv.Rebuild();
            string kept = P(vis);
            pv.Play();
            string again = P(vis);
            Run(s, 2, 0.1f);
            Check(kept == "(1, 0, 0)" && again == "(0, 5, 0)" && P(vis) == "(1, 0, 0)", "Rebuild after a play: " + kept + " · " + again + " → " + P(vis));
            // a lost runtime's prepare does not write over a play (a recompile's shape)
            GameObject cut = At("cut15", one);
            FeedbackPlayer pc = PlayerOn(s, Pos(cut, TweenMode.From, five, 0.2f));
            pc.m_ApplyInitialState = true;
            pc.EnsureRuntime();
            pc.Play();
            s.Tick(0.1f);
            string middle = P(cut);
            pc.ReleaseRuntime();
            pc.EnsureRuntime();
            Check(middle == "(0.5, 2.5, 0)" && P(cut) == middle, "a lost runtime: " + middle + " · " + P(cut));
            End();
        }

        // ---- the backward instants

        [Test]
        public void DE16_BackwardInstants()
        {
            FeedbackScheduler s = Sched();
            GameObject g = Go("g16");
            int called = 0;
            FeedbackPlayer p = PlayerOn(s, SetActive(g, false), Event(() => called++));
            p.Play();
            bool off = !g.activeSelf;
            p.Play(Backward);
            Check(off && g.activeSelf && called == 1, "Set(0) then backward: " + off + " · " + g.activeSelf + " · events " + called);
            // a backward play first has nothing to write
            GameObject h = Go("h16");
            FeedbackPlayer q = PlayerOn(s, SetActive(h, false));
            q.Play(Backward);
            Check(h.activeSelf, "backward first");
            // Relative: the origin too
            GameObject r = Go("r16");
            FeedbackPlayer pr = PlayerOn(s, SetActive(r, true, true));
            pr.Play();
            bool toggled = !r.activeSelf;
            pr.Play(Backward);
            Check(toggled && r.activeSelf, "Relative: " + toggled + " · " + r.activeSelf);
            // after Rebuild
            GameObject rb = Go("rb16");
            FeedbackPlayer pb = PlayerOn(s, SetActive(rb, false));
            pb.Play();
            pb.Rebuild();
            pb.Play(Backward);
            Check(rb.activeSelf, "a backward Set after Rebuild");
            End();
        }

        // ---- Looped

        [Test]
        public void DE17_Looped()
        {
            FeedbackScheduler s = Sched();
            foreach (bool backward in new[] { false, true })
            {
                FeedbackPlayer p = PlayerOn(s, new LoopStartItem(), Act("a", 0.1f), LoopEnd(3));
                Watch(p);
                p.Play(backward ? Backward : PlayArgs.Default);
                Run(s, 4, 0.1f);
                Check(Events() == "|Played |Looped |Looped |Looped |Completed", (backward ? "backward" : "forward") + " Loop ×3: " + Events());
                PlayerTestBinding.Log.Clear();
            }
            // a zero-period loop at t = 0
            FeedbackPlayer z = PlayerOn(s, new LoopStartItem(), Act("i", 0f), LoopEnd(3));
            Watch(z);
            z.Play();
            Check(Events() == "|Played |Looped |Looped |Looped |Completed", "a zero-period loop: " + Events());
            PlayerTestBinding.Log.Clear();
            // Seek crosses the boundaries — back too
            FeedbackPlayer k = PlayerOn(s, new LoopStartItem(), Act("a", 0.1f), LoopEnd(3));
            Watch(k);
            k.Play();
            PlayerTestBinding.Log.Clear();
            k.Seek(0.25f);
            string ahead = Events();
            PlayerTestBinding.Log.Clear();
            k.Seek(0.05f);
            Check(ahead == "|Looped |Looped" && Events() == "|Looped |Looped", "Seek: " + ahead + " · back " + Events());
            // Played's subscriber ends the play: no Looped after its end
            foreach ((string name, Func<FeedbackPlayer, bool> end, bool backward, bool zero, string want) in new (string, Func<FeedbackPlayer, bool>, bool, bool, string)[]
            {
                ("backward Stop", pl => { pl.Stop(); return true; }, true, false, "|Played |Stopped"),
                // the skip's sweep crosses two boundaries before the end — the Begin window's one is dropped
                ("backward Skip", pl => pl.Skip(), true, false, "|Played |Looped |Looped |Completed"),
                ("zero-period Restore", pl => { pl.Restore(); return true; }, false, true, "|Played |Stopped"),
                ("zero-period Skip", pl => pl.Skip(), false, true, "|Played |Completed"),
            })
            {
                FeedbackScheduler own = Sched();
                FeedbackPlayer e = PlayerOn(own, new LoopStartItem(), Act(zero ? "i" : "a", zero ? 0f : 0.1f), LoopEnd(3));
                if (zero)
                    e.m_Items.Add(Act("tail", 0.1f, 0f, StartRule.AfterPrevious));
                Watch(e);
                bool once = true;
                e.Played += (pl, h) =>
                {
                    if (once)
                    {
                        once = false;
                        end(pl);
                    }
                };
                PlayerTestBinding.Log.Clear();
                e.Play(backward ? Backward : PlayArgs.Default);
                Run(own, 4, 0.1f);
                Check(Events() == want, name + ": " + Events());
            }
            End();
        }

        // ---- the surface

        [Test]
        public void DE18_Surface()
        {
            Check(Values(typeof(PlayerDirection)) == "Forward=0 Backward=1 Toggle=2", Values(typeof(PlayerDirection)));
            Check(Values(typeof(PlayerDisableBehavior)) == "Continue=0 StopHold=1 StopRevert=2", Values(typeof(PlayerDisableBehavior)));
            Check(typeof(PlayerDirection).IsPublic && typeof(PlayerDisableBehavior).IsPublic, "the two public types");
            const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
            MethodInfo seek = typeof(FeedbackPlayer).GetMethod("Seek", Declared);
            MethodInfo skip = typeof(FeedbackPlayer).GetMethod("Skip", Declared);
            MethodInfo restore = typeof(FeedbackPlayer).GetMethod("Restore", Declared);
            MethodInfo signal = typeof(FeedbackPlayer).GetMethod("Signal", Declared);
            Check(seek != null && seek.ReturnType == typeof(bool) && seek.GetParameters().Single().ParameterType == typeof(float)
                  && skip != null && skip.ReturnType == typeof(bool) && restore != null && restore.ReturnType == typeof(void) && signal != null && signal.ReturnType == typeof(void),
                "the four public methods");
            Check(typeof(PlayArgs).GetProperty("HasDirection") != null && typeof(PlayArgs).GetProperty("Direction")?.PropertyType == typeof(PlayerDirection)
                  && typeof(PlayArgs).GetMethod("WithDirection") != null, "PlayArgs' three");
            PlayArgs none = default;
            Check(!none.HasDirection && none.Direction == PlayerDirection.Forward, "no direction is Forward");
            // the three saved fields, read by Core
            string[] fields = { "m_Direction", "m_ApplyInitialState", "m_DisableBehavior" };
            Check(fields.All(n => DataModelTraitsTests.SavedFieldInfos(typeof(FeedbackPlayer)).Any(f => f.Name == n)), "the three saved fields");
            GameObject go = Go("defaults18");
            FeedbackPlayer d = go.AddComponent<FeedbackPlayer>();
            Check(d.m_Direction == PlayerDirection.Forward && !d.m_ApplyInitialState && d.m_DisableBehavior == PlayerDisableBehavior.Continue, "the defaults");
            // the warning causes — each once per player, the eighth and the ninth too (a byte would wrap)
            FeedbackScheduler s = Sched();
            FeedbackPlayer w = PlayerOn(s, Endless());
            int warned = s_Warnings.Count;
            w.Play(Backward);
            w.Play(Backward);
            w.Signal();
            w.Signal();
            w.Play(PlayArgs.Default.WithDirection((PlayerDirection)3));
            w.Play(PlayArgs.Default.WithDirection((PlayerDirection)3));
            Check(Count(FeedbackPlayer.ReverseWarning, warned) == 1 && Count(FeedbackPlayer.SignalWarning, warned) == 1 && Count(FeedbackPlayer.ArgumentWarning, warned) == 1,
                "each cause once: " + string.Join(" | ", s_Warnings.Skip(warned)));
            // the argument's bound is the last value
            FeedbackPlayer b = PlayerOn(s, Act("b", 0.1f));
            Check(b.Play(PlayArgs.Default.WithDirection(PlayerDirection.Toggle)) != default && b.Play(PlayArgs.Default.WithDirection((PlayerDirection)3)) == default, "the argument's bound");
            End();
        }

        static string Values(Type type) => string.Join(" ", Enum.GetNames(type).Select(n => n + "=" + (int)Enum.Parse(type, n)));
    }
}
