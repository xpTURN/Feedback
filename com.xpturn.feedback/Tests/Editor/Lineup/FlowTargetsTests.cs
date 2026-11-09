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
    // the flow items on test schedulers ticked by hand (0.05 s — a child bound to the parent's scheduler ends at a tick and its
    // node ends at the next one), the previews on the test's clock (an update is 1/120 s); the linker's walk is
    // UnityEventLinkerTests', which the players' negative control leaves out with the processor. The binding calls are read
    // from the test binding's log — "--" marks a tick
    public sealed class FlowTargetsTests
    {
        const float Step = 0.05f;

        static double s_Now;

        static readonly List<string> s_Warnings = new List<string>();
        static readonly List<string> s_Errors = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();

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
            s_Warnings.Clear();
            s_Errors.Clear();
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerRuntime.ResetSession();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            DebugBinding.s_Break = Debug.Break;
            DebugBinding.s_DebugBuild = () => Debug.isDebugBuild;
            PreviewSession.Current?.Stop();
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                {
                    p.Bind(null);
                    p.ReleaseRuntime();
                }
            }
            m_Players.Clear();
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
            PlayerTestBinding.Reset();
        }

        sealed class Ends : IPlaybackListener
        {
            internal readonly List<PlaybackEnd> List = new List<PlaybackEnd>();

            public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end) => List.Add(end);

            internal bool Once(PlaybackEnd end) => List.Count == 1 && List[0] == end;
        }

        FeedbackScheduler Sched(ContextKind kind = ContextKind.Test)
        {
            var s = new FeedbackScheduler(kind);
            m_Schedulers.Add(s);
            return s;
        }

        FeedbackPlayer PlayerOn(FeedbackScheduler s, Action<FeedbackPlayer> set, params FeedbackItem[] items)
        {
            var go = new GameObject("player");
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            m_Players.Add(p);
            p.m_Items.AddRange(items);
            set?.Invoke(p);
            if (s != null)
            {
                p.Bind(s);
                p.EnsureRuntime();
            }
            return p;
        }

        // a player whose items name players made later (a cycle) — bound and prepared by the caller
        FeedbackPlayer Bare()
        {
            var go = new GameObject("bare");
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            m_Players.Add(p);
            return p;
        }

        static void Ready(FeedbackScheduler s, params FeedbackPlayer[] players)
        {
            foreach (FeedbackPlayer p in players)
                p.Bind(s);
            foreach (FeedbackPlayer p in players)
                p.EnsureRuntime();
        }

        static PlayerTestAction Act(string label, float length) => new PlayerTestAction { m_Label = label, m_Length = length };

        static PlayPlayerItem Play(FeedbackPlayer child, bool wait = true, float timeout = 0f)
        {
            var item = new PlayPlayerItem { m_Player = child };
            item.m_Play.m_Wait = wait;
            item.m_Play.m_Timeout = timeout;
            return item;
        }

        static T After<T>(T item) where T : FeedbackItem
        {
            item.m_Timing.m_Rule = StartRule.AfterPrevious;
            return item;
        }

        static T Delayed<T>(T item, float delay) where T : FeedbackItem
        {
            item.m_Timing.m_Delay = delay;
            return item;
        }

        static void Run(FeedbackScheduler s, int n)
        {
            for (int i = 0; i < n; i++)
            {
                PlayerTestBinding.Log.Add("--");
                s.Tick(Step);
            }
        }

        static string Trace()
        {
            string t = string.Join(" ", PlayerTestBinding.Log);
            PlayerTestBinding.Log.Clear();
            return t;
        }

        // the tick an entry of the trace came in (0 = before the first tick)
        static int TickOf(string trace, string what)
        {
            int at = trace.IndexOf(what, StringComparison.Ordinal);
            if (at < 0)
                return -1;
            int n = 0;
            for (int i = trace.IndexOf("--", StringComparison.Ordinal); i >= 0 && i < at; i = trace.IndexOf("--", i + 2, StringComparison.Ordinal))
                n++;
            return n;
        }

        static PlayerTestBinding BindingOf(string label) => PlayerTestBinding.Made.Last(b => b.Item.m_Label == label);

        static int Count(string warning, int from) => s_Warnings.Skip(from).Count(w => w == warning);

        // ---- the open program — untimed · timed · no wait, the backward refusal, one node alone, a loop, Hold

        [Test]
        public void FQ02a_TheOpenProgram()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer c = PlayerOn(s, null, Act("c", 0.3f));
            foreach ((string name, FeedbackItem item, bool open, float total) in new (string, FeedbackItem, bool, float)[]
            {
                ("wait", Play(c), true, float.PositiveInfinity),
                ("wait with a time limit", Play(c, true, 0.5f), true, 0.6f),
                ("no wait", Play(c, false), false, 0.1f),
            })
            {
                FeedbackPlayer p = PlayerOn(s, null, item, After(Act("B", 0.1f)));
                TimelineProgram program = p.Runtime.Program;
                PlaybackHandle h = p.Play();
                SeekRefusal refusal = p.SeekRange(h, out _, out _);
                p.Stop();
                c.Stop();
                int warned = s_Warnings.Count;
                PlaybackHandle back = p.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward));
                p.Stop();
                c.Stop();
                Assert.That((program.HasOpenItem, program.IsSeekable), Is.EqualTo((open, !open)), name);
                Assert.That(p.TotalDuration, Is.EqualTo(total).Within(1e-5f), name + " total");
                Assert.That(refusal, Is.EqualTo(open ? SeekRefusal.UntimedWait : SeekRefusal.None), name + " refusal");
                Assert.That(back == default, Is.EqualTo(open), name + " backward");
                Assert.That(Count(FeedbackPlayer.ReverseWarning, warned), Is.EqualTo(open ? 1 : 0), name + " the backward warning");
            }
            // two open nodes side by side: the first child's end ends its node alone — Hold waits for both
            FeedbackPlayer c1 = PlayerOn(s, null, Act("c1", 0.1f));
            FeedbackPlayer c2 = PlayerOn(s, null, Act("c2", 0.3f));
            FeedbackPlayer two = PlayerOn(s, null, Play(c1), Play(c2), new HoldItem(), Act("B2", 0.05f));
            Trace();
            two.Play();
            Run(s, 10);
            string t = Trace();
            Assert.That(TickOf(t, "B2.Begin"), Is.EqualTo(TickOf(t, "c2.End") + 1), "one node ends alone · " + t);
            // a loop block opens it again each run
            FeedbackPlayer loop = PlayerOn(s, null, new LoopStartItem(), Play(c1), new LoopEndItem { m_LoopCount = 2 }, After(Act("B3", 0.05f)));
            var ends = new Ends();
            loop.Play(PlayArgs.Default.WithListener(ends));
            Run(s, 10);
            string tl = Trace();
            Assert.That(Regex.Matches(tl, "c1.Begin").Count, Is.EqualTo(2), "the loop opens it twice · " + tl);
            Assert.That(ends.Once(PlaybackEnd.Completed), Is.True, "the loop's play completes");
            // the open play's Skip ends it Stopped (it is not seekable — Stop(Complete))
            var skipped = new Ends();
            FeedbackPlayer sk = PlayerOn(s, null, Play(c2), After(Act("B4", 0.1f)));
            sk.Play(PlayArgs.Default.WithListener(skipped));
            Run(s, 1);
            sk.Skip();
            Assert.That(skipped.Once(PlaybackEnd.Stopped), Is.True, "Skip of an open play");
        }

        // ---- Play Player — the next tick on time, what it hands on, the time limit, no wait, the refusals, an instant child,
        // a slowed child (no cached length), a stale end after a time limit

        [Test]
        public void FQ03a_PlayPlayer()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer c = PlayerOn(s, null, Act("c", 0.12f));
            FeedbackPlayer p = PlayerOn(s, null, Play(c), After(Act("B", 0.2f)));
            var ends = new Ends();
            p.Play(PlayArgs.Default.WithListener(ends).WithIntensity(0.5f).WithPosition(new Vector3(1, 2, 3)).WithSeed(7));
            Run(s, 10);
            string t = Trace();
            int ce = TickOf(t, "c.End:Completed");
            Assert.That(ce, Is.GreaterThan(0), t);
            Assert.That(TickOf(t, "B.Begin"), Is.EqualTo(ce + 1), "the tick after the child's end · " + t);
            Assert.That(t, Does.Contain("B.Begin#0 B.Evaluate(0.25)"), "on time — it began at the end of the child's tick");
            Assert.That(ends.Once(PlaybackEnd.Completed), Is.True);
            BeginContext cb = BindingOf("c").LastBegin;
            Assert.That(cb.Intensity, Is.EqualTo(0.5f).Within(1e-6f), "the intensity");
            Assert.That((cb.HasPosition, cb.Position), Is.EqualTo((true, new Vector3(1, 2, 3))), "the position");
            uint seed7 = cb.ItemSeed;
            p.Play(PlayArgs.Default.WithSeed(7));
            uint again = BindingOf("c").LastBegin.ItemSeed;
            p.Stop();
            c.Stop();
            p.Play(PlayArgs.Default.WithSeed(8));
            uint other = BindingOf("c").LastBegin.ItemSeed;
            p.Stop();
            c.Stop();
            Assert.That((seed7 == again, seed7 == other), Is.EqualTo((true, false)), "the seed — the same parent's seed, the same child's");
            // the time limit ends the node, the child goes on
            FeedbackPlayer c2 = PlayerOn(s, null, Act("c2", 1f));
            FeedbackPlayer q = PlayerOn(s, null, Play(c2, true, 0.1f), After(Act("Q", 0.05f)));
            Trace();
            q.Play();
            Run(s, 4);
            Assert.That(TickOf(Trace(), "Q.Begin"), Is.EqualTo(2), "the time limit");
            Assert.That(c2.IsPlaying, Is.True, "the time limit leaves the child");
            c2.Stop();
            // no wait: an instant — the next item at t = 0
            FeedbackPlayer c3 = PlayerOn(s, null, Act("c3", 1f));
            FeedbackPlayer r = PlayerOn(s, null, Play(c3, false), After(Act("R", 0.05f)));
            Trace();
            r.Play();
            Assert.That(Trace(), Does.Contain("R.Begin"), "no wait is an instant");
            c3.Stop();
            // the refusals end the node at once — none · a busy Ignore child · an unbound child (edit mode)
            FeedbackPlayer busy = PlayerOn(s, x => x.m_Policy = PlayPolicy.Ignore, Act("busy", 5f));
            busy.Play();
            FeedbackPlayer unbound = PlayerOn(null, null, Act("u", 0.1f));
            foreach (FeedbackPlayer child in new[] { null, busy, unbound })
            {
                FeedbackPlayer x = PlayerOn(s, null, Play(child), After(Act("X", 0.05f)));
                Trace();
                x.Play();
                Assert.That(Trace(), Does.Contain("X.Begin"), "a refused child ends the node at once");
            }
            busy.Stop();
            // an instant child ends inside its Play — the node ends in the parent's Begin window
            FeedbackPlayer ci = PlayerOn(s, null, Act("ci", 0f));
            FeedbackPlayer ti = PlayerOn(s, null, Play(ci), After(Act("B2", 0.05f)));
            Trace();
            PlaybackHandle hi = ti.Play();
            Assert.That(Trace(), Does.Contain("B2.Begin"), "an instant child");
            Assert.That(hi != default, Is.True);
            // a slowed child (Speed 0.5): its real end, not its nominal length
            FeedbackPlayer slow = PlayerOn(s, x => x.m_Speed = 0.5f, Act("cs", 0.12f));
            FeedbackPlayer u = PlayerOn(s, null, Play(slow), After(Act("B3", 0.05f)));
            Trace();
            u.Play();
            Run(s, 8);
            string ts = Trace();
            Assert.That(TickOf(ts, "cs.End"), Is.GreaterThanOrEqualTo(5), ts);
            Assert.That(TickOf(ts, "B3.Begin"), Is.EqualTo(TickOf(ts, "cs.End") + 1), "a slowed child is waited for · " + ts);
            // a time limit, then the play again: the earlier child's end (its Restart, inside the new Play) does not end the new node
            FeedbackPlayer cl = PlayerOn(s, null, Act("cl", 1f));
            FeedbackPlayer v = PlayerOn(s, null, Play(cl, true, 0.1f), After(Act("B4", 0.3f)));
            v.Play();
            Run(s, 3);
            Trace();
            v.Play();
            Assert.That(Trace(), Does.Not.Contain("B4.Begin"), "a stale end");
            // the intensity not passed
            var flat = Play(c);
            flat.m_Play.m_PassIntensity = false;
            FeedbackPlayer w = PlayerOn(s, null, flat);
            w.Play(PlayArgs.Default.WithIntensity(0.25f));
            Assert.That(BindingOf("c").LastBegin.Intensity, Is.EqualTo(1f), "the intensity not passed");
            Assert.That(s_Errors, Is.Empty, string.Join(" | ", s_Errors));
        }

        // ---- the waiting cycle — across ticks refused at Begin (one warning) · the depth net · a play that does not wait ·
        // itself

        [Test]
        public void FQ04a_TheWaitingCycle()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer a = Bare();
            FeedbackPlayer b = Bare();
            a.m_Items.Add(Delayed(Play(b), 0.05f));
            b.m_Items.Add(Delayed(Play(a), 0.05f));
            Ready(s, a, b);
            var ends = new Ends();
            a.Play(PlayArgs.Default.WithListener(ends));
            Run(s, 8);
            Assert.That(ends.Once(PlaybackEnd.Completed) && !a.IsPlaying && !b.IsPlaying, Is.True, "A ⇄ B closes");
            Assert.That(Count(NestedPlayBinding.CycleWarning, 0), Is.EqualTo(1), "one warning");
            var chain = new List<FeedbackPlayer>();
            for (int i = 0; i < 20; i++)
                chain.Add(Bare());
            var played = new bool[20];
            for (int i = 0; i < 20; i++)
            {
                chain[i].m_Items.Add(i < 19 ? (FeedbackItem)Play(chain[i + 1]) : Act("leaf", 0.1f));
                int k = i;
                chain[i].Played += (pl, h) => played[k] = true;
            }
            Ready(s, chain.ToArray());
            var root = new Ends();
            chain[0].Play(PlayArgs.Default.WithListener(root));
            Assert.That(played.Count(x => x), Is.EqualTo(PlayerRuntime.WaitDepth), "the depth net");
            Assert.That(root.Once(PlaybackEnd.Completed), Is.True, "the chain ends inside the root's Play");
            FeedbackPlayer n = Bare();
            n.m_Items.Add(Delayed(Play(n, false), 0.05f));
            Ready(s, n);
            int plays = 0;
            n.Played += (pl, h) => plays++;
            n.Play();
            Run(s, 4);
            Assert.That(plays, Is.GreaterThanOrEqualTo(3), "a play that does not wait is not refused");
            n.Stop();
            FeedbackPlayer self = Bare();
            self.m_Items.Add(Play(self));
            self.m_Items.Add(After(Act("S", 0.05f)));
            Ready(s, self);
            Trace();
            self.Play();
            Assert.That(Trace(), Does.Contain("S.Begin"), "itself refused — the node ends");
        }

        // ---- the ends' propagation — the same mode to a waited child · a child not waited on is left · Restore reaches it

        [Test]
        public void FQ05a_TheEndsPropagation()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer c = PlayerOn(s, null, Act("c", 1f));
            FeedbackPlayer p = PlayerOn(s, null, Play(c), After(Act("B", 0.1f)));
            foreach ((string name, Action stop, string want) in new (string, Action, string)[]
            {
                ("Stop Hold", () => p.Stop(StopMode.Hold), "c.End:Hold"),
                ("Stop Revert", () => p.Stop(StopMode.Revert), "c.End:Revert"),
                ("Stop Complete", () => p.Stop(StopMode.Complete), "c.End:Complete"),
                ("Skip", () => p.Skip(), "c.End:Complete"),
                ("Restart", () => p.Play(), "c.End:Hold"),
            })
            {
                p.Stop();
                c.Stop();
                p.Play();
                Run(s, 2);
                Trace();
                stop();
                Assert.That(Trace(), Does.Contain(want), name);
            }
            p.Stop();
            c.Stop();
            FeedbackPlayer cn = PlayerOn(s, null, Act("cn", 1f));
            FeedbackPlayer q = PlayerOn(s, null, Play(cn, false), Act("pad", 1f));
            q.Play();
            Run(s, 2);
            q.Stop(StopMode.Revert);
            Assert.That(cn.IsPlaying, Is.True, "a child not waited on is left");
            var go = new GameObject("FQ05a");
            m_Made.Add(go);
            go.transform.localPosition = new Vector3(1, 2, 3);
            var tween = new PositionItem { m_Motion = new TweenMotion { m_Params = new TweenParams { m_To = new Vector4(5, 5, 5, 0), m_Loops = 1 } } };
            tween.m_Target.m_Mode = TargetRefMode.Direct;
            tween.m_Target.m_Object = go.transform;
            tween.m_Timing.m_Duration = 0.2f;
            FeedbackPlayer mover = PlayerOn(s, null, tween);
            FeedbackPlayer r = PlayerOn(s, null, Play(mover));
            r.Play();
            Run(s, 2);
            Assert.That(go.transform.localPosition, Is.Not.EqualTo(new Vector3(1, 2, 3)), "it moved");
            r.Restore();
            Assert.That(go.transform.localPosition, Is.EqualTo(new Vector3(1, 2, 3)), "Restore reaches the child");
        }

        // ---- the chain — waiting (order · delay · a dead entry), timed (the offsets in µs), backward (reverse order, one
        // offset's two reversed, the children backward)

        [Test]
        public void FQ06a_TheChain()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer c1 = PlayerOn(s, null, Act("c1", 0.1f));
            FeedbackPlayer c2 = PlayerOn(s, null, Act("c2", 0.3f));
            FeedbackPlayer c3 = PlayerOn(s, null, Act("c3", 0.1f));
            var chain = new PlayerChainItem();
            chain.m_Entries.Add(new ChainEntry { m_Player = c1 });
            chain.m_Entries.Add(new ChainEntry { m_Player = null });
            chain.m_Entries.Add(new ChainEntry { m_Player = c2, m_Delay = 0.05f, m_Wait = false });
            chain.m_Entries.Add(new ChainEntry { m_Player = c3, m_Active = false });
            chain.m_Entries.Add(new ChainEntry { m_Player = c3 });
            FeedbackPlayer p = PlayerOn(s, null, chain, After(Act("B", 0.05f)));
            var ends = new Ends();
            p.Play(PlayArgs.Default.WithListener(ends));
            Run(s, 12);
            string t = Trace();
            int e1 = TickOf(t, "c1.End"), b2 = TickOf(t, "c2.Begin"), b3 = TickOf(t, "c3.Begin"), e3 = TickOf(t, "c3.End");
            Assert.That(b2 > e1 && b3 == b2 && e3 > b3, Is.True, "order · delay · wait · the dead entries skipped · " + t);
            Assert.That(TickOf(t, "B.Begin"), Is.EqualTo(e3 + 1), "the last waited entry ends the node");
            Assert.That(ends.Once(PlaybackEnd.Completed), Is.True);
            c2.Stop();
            var timed = new PlayerChainItem();
            timed.m_Entries.Add(new ChainEntry { m_Player = c1, m_Delay = 0.05f, m_Wait = false });
            timed.m_Entries.Add(new ChainEntry { m_Player = c3, m_Delay = 0.05f, m_Wait = false });
            FeedbackPlayer q = PlayerOn(s, null, timed);
            Assert.That((q.Runtime.Program.HasOpenItem, q.TotalDuration), Is.EqualTo((false, 0.1f)), "timed — the delays' sum");
            Trace();
            q.Play();
            Run(s, 4);
            string tf = Trace();
            Assert.That((TickOf(tf, "c1.Begin"), TickOf(tf, "c3.Begin")), Is.EqualTo((1, 2)), "each entry at its offset · " + tf);
            c1.Stop();
            c3.Stop();
            Trace();
            q.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward));
            Run(s, 4);
            string tb = Trace();
            int r3 = tb.IndexOf("c3.Begin", StringComparison.Ordinal), r1 = tb.IndexOf("c1.Begin", StringComparison.Ordinal);
            Assert.That(r3 >= 0 && r1 > r3, Is.True, "backward — reverse order · " + tb);
            Assert.That(BindingOf("c1").LastBegin.Reverse && BindingOf("c3").LastBegin.Reverse, Is.True, "the children backward");
            c1.Stop();
            c3.Stop();
            var same = new PlayerChainItem();
            same.m_Entries.Add(new ChainEntry { m_Player = c1, m_Delay = 0.05f, m_Wait = false });
            same.m_Entries.Add(new ChainEntry { m_Player = c3, m_Wait = false });
            same.m_Entries.Add(new ChainEntry { m_Player = c2, m_Delay = 0.05f, m_Wait = false });
            FeedbackPlayer z = PlayerOn(s, null, same);
            Trace();
            z.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward));
            Run(s, 4);
            string tz = Trace();
            int z3 = tz.IndexOf("c3.Begin", StringComparison.Ordinal), z1 = tz.IndexOf("c1.Begin", StringComparison.Ordinal);
            Assert.That(z3 >= 0 && z1 > z3, Is.True, "one offset's two in reverse list order · " + tz);
            int warned = s_Warnings.Count;
            Assert.That(p.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward)) == default, Is.True, "a waiting chain cannot play backward");
            Assert.That(Count(FeedbackPlayer.ReverseWarning, warned), Is.EqualTo(1));
        }

        // ---- Player Control — the eight acts, a null skipped, nothing backward

        [Test]
        public void FQ07a_PlayerControl()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer x = PlayerOn(s, null, Act("x", 1f));
            FeedbackPlayer y = PlayerOn(s, null, new WaitForSignalItem(), After(Act("y", 0.1f)));
            foreach (PlayerControlAction action in Enum.GetValues(typeof(PlayerControlAction)))
            {
                var item = new PlayerControlItem { m_Action = action, m_StopMode = StopMode.Revert };
                item.m_Players.Add(x);
                item.m_Players.Add(null);
                item.m_Players.Add(y);
                FeedbackPlayer p = PlayerOn(s, null, item);
                x.Stop();
                y.Stop();
                if (action != PlayerControlAction.Play && action != PlayerControlAction.PlayBackward)
                {
                    x.Play();
                    y.Play();
                }
                if (action == PlayerControlAction.Resume)
                {
                    x.Pause();
                    y.Pause();
                }
                Trace();
                p.Play();
                Run(s, 1);
                string t = Trace();
                switch (action)
                {
                    case PlayerControlAction.Play: Assert.That(x.IsPlaying && y.IsPlaying, Is.True, "Play"); break;
                    case PlayerControlAction.PlayBackward: Assert.That(x.IsPlaying && x.Runtime.Record(0).Reverse && !y.IsPlaying, Is.True, "PlayBackward (y's signal wait refuses it)"); break;
                    case PlayerControlAction.Stop: Assert.That(t.Contains("x.End:Revert") && !x.IsPlaying, Is.True, "Stop · " + t); break;
                    case PlayerControlAction.Pause: Assert.That(x.Runtime.HasPaused, Is.True, "Pause"); break;
                    case PlayerControlAction.Resume: Assert.That(x.Runtime.HasPaused, Is.False, "Resume"); break;
                    case PlayerControlAction.Skip: Assert.That(!x.IsPlaying && t.Contains("x.End:Completed"), Is.True, "Skip · " + t); break;
                    case PlayerControlAction.Restore: Assert.That(!x.IsPlaying && t.Contains("x.End:Hold"), Is.True, "Restore · " + t); break;
                    default: Assert.That(t, Does.Contain("y.Begin"), "Signal"); break;
                }
            }
            x.Stop();
            y.Stop();
            int missed = x.MissedQuietSignals;
            int warned = s_Warnings.Count;
            var signal = new PlayerControlItem { m_Action = PlayerControlAction.Signal };
            signal.m_Players.Add(x);
            PlayerOn(s, null, signal).Play();
            Assert.That((x.MissedQuietSignals - missed, x.MissedSignals, s_Warnings.Count - warned), Is.EqualTo((1, 0, 0)), "the quiet way — no warning");
            var play = new PlayerControlItem { m_Action = PlayerControlAction.Play };
            play.m_Players.Add(x);
            FeedbackPlayer back = PlayerOn(s, null, play, Act("pad", 0.1f));
            back.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward));
            Run(s, 3);
            Assert.That(x.IsPlaying, Is.False, "nothing backward");
        }

        // ---- the weighted pick · Random Player · Random Unity Event

        [Test]
        public void FQ08a_TheWeightedPick()
        {
            float[] w = { 1f, 3f, 0f, float.NaN, -1f, 6f };
            var counts = new int[w.Length];
            for (int k = 0; k < 10000; k++)
                counts[WeightedPick.Index(w, w.Length, k / 10000f)]++;
            Assert.That(counts, Is.EqualTo(new[] { 1000, 3000, 0, 0, 0, 6000 }), "the proportions");
            Assert.That(WeightedPick.Index(w, w.Length, 0.99999f), Is.EqualTo(5), "the last one reached");
            Assert.That(WeightedPick.Index(new[] { 0f, -2f, float.NaN }, 3, 0.5f), Is.EqualTo(-1), "none counts");
            FeedbackScheduler s = Sched();
            var children = new List<FeedbackPlayer>();
            var random = new RandomPlayerItem();
            float[] weights = { 1f, 0f, 3f };
            for (int i = 0; i < 3; i++)
            {
                FeedbackPlayer c = PlayerOn(s, null, Act("r" + i, 0.05f));
                children.Add(c);
                random.m_Entries.Add(new WeightedPlayer { m_Player = c, m_Weight = weights[i] });
            }
            random.m_Play.m_Wait = false;
            FeedbackPlayer p = PlayerOn(s, null, random);
            string Pick(uint seed)
            {
                Trace();
                p.Play(PlayArgs.Default.WithSeed(seed));
                string t = Trace();
                foreach (FeedbackPlayer c in children)
                    c.Stop();
                Trace();
                return string.Concat(Enumerable.Range(0, 3).Where(i => t.Contains("r" + i + ".Begin")).Select(i => i.ToString()));
            }
            var picks = new int[3];
            for (uint seed = 0; seed < 1000; seed++)
                picks[int.Parse(Pick(seed))]++;
            Assert.That(picks[1] == 0 && picks[0] > 180 && picks[0] < 320 && picks.Sum() == 1000, Is.True, "by weight " + string.Join(",", picks));
            var first = Enumerable.Range(100, 20).Select(seed => Pick((uint)seed)).ToList();
            var second = Enumerable.Range(100, 20).Select(seed => Pick((uint)seed)).ToList();
            Assert.That(second, Is.EqualTo(first), "twenty seeds pick the same twice");
            var none = new RandomPlayerItem();
            none.m_Entries.Add(new WeightedPlayer { m_Player = children[0], m_Weight = 0f });
            FeedbackPlayer q = PlayerOn(s, null, none, After(Act("B", 0.05f)));
            int warned = s_Warnings.Count;
            q.Play();
            Assert.That(Trace(), Does.Contain("B.Begin"), "no weight — it ends at once");
            Assert.That(Count(RandomPlayerBinding.NoWeightWarning, warned), Is.EqualTo(1));
            var calls = new int[2];
            var rue = new RandomUnityEventItem();
            for (int i = 0; i < 2; i++)
            {
                int k = i;
                var e = new WeightedUnityEvent();
                e.m_Event.AddListener(() => calls[k]++);
                rue.m_Entries.Add(e);
            }
            FeedbackPlayer r = PlayerOn(s, null, rue, Act("pad", 0.05f));
            for (uint seed = 0; seed < 200; seed++)
                r.Play(PlayArgs.Default.WithSeed(seed));
            Assert.That(calls.Sum() == 200 && calls.Min() > 70, Is.True, "Random Unity Event " + string.Join(",", calls));
            r.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward));
            Run(s, 3);
            Assert.That(calls.Sum(), Is.EqualTo(200), "none backward");
            var throws = new RandomUnityEventItem();
            var bad = new WeightedUnityEvent();
            bad.m_Event.AddListener(() => throw new InvalidOperationException("FQ08a listener"));
            throws.m_Entries.Add(bad);
            FeedbackPlayer x = PlayerOn(s, null, throws, After(Act("X", 0.05f)));
            LogAssert.Expect(LogType.Exception, new Regex("FQ08a listener"));
            x.Play();
            Assert.That(Trace(), Does.Contain("X.Begin"), "a throwing listener is logged, the play goes on");
        }

        // ---- a played-back play with a position hands the child its direction and the position (no wait)

        [Test]
        public void FQ09a_APlayedBackPositionPlay()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer c = PlayerOn(s, null, Act("c", 0.1f));
            FeedbackPlayer p = PlayerOn(s, null, Play(c, false), Act("pad", 0.1f));
            p.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward).WithPosition(new Vector3(4, 5, 6)));
            Run(s, 4);
            BeginContext b = BindingOf("c").LastBegin;
            Assert.That((b.Reverse, b.HasPosition, b.Position), Is.EqualTo((true, true, new Vector3(4, 5, 6))));
        }

        // ---- the signals — value × intensity, the range, the channels, a throwing receiver, a removal in the walk, the
        // component off, the resend depth, the public send outside play

        sealed class Receiver : IFeedbackSignalReceiver
        {
            internal readonly List<FeedbackSignal> Got = new List<FeedbackSignal>();
            internal Action<FeedbackSignal> Also;

            public void OnFeedbackSignal(in FeedbackSignal signal)
            {
                Got.Add(signal);
                Also?.Invoke(signal);
            }
        }

        [Test]
        public void FQ10a_TheSignals()
        {
            FeedbackScheduler s = Sched();
            ChannelService channels = s.Context.Channels;
            var code = new Receiver();
            ChannelRef three = ChannelRef.Of(3);
            Assert.That(channels.Register(code, new ChannelKey(in three)), Is.True);
            var go = new GameObject("FQ10a");
            m_Made.Add(go);
            go.transform.position = new Vector3(10, 0, 0);
            var comp = go.AddComponent<FeedbackSignalReceiver>();
            comp.m_Channel = three;
            var heard = new List<float>();
            comp.m_OnSignal.AddListener(v => heard.Add(v));
            comp.Bind(s);
            Assert.That(comp.IsRegistered, Is.True);
            FeedbackPlayer p = PlayerOn(s, null, new SignalBroadcastItem { m_Channel = three, m_Value = 2f }, Act("pad", 0.05f));
            p.Play(PlayArgs.Default.WithIntensity(0.5f));
            Assert.That(code.Got.Select(g => g.Value), Is.EqualTo(new[] { 1f }), "the value × the intensity");
            FeedbackSignal got = code.Got[0];
            Assert.That((got.Intensity, got.Sender, got.Channel.m_Id, got.HasPosition), Is.EqualTo((0.5f, p, 3, false)));
            Assert.That(heard, Is.EqualTo(new[] { 1f }), "the component");
            comp.m_Range = 5f;
            p.Play(PlayArgs.Default.WithPosition(Vector3.zero));
            p.Play(PlayArgs.Default.WithPosition(new Vector3(8, 0, 0)));
            Assert.That((heard.Count, code.Got.Count), Is.EqualTo((2, 3)), "the range");
            var asset = ScriptableObject.CreateInstance<FeedbackChannel>();
            m_Made.Add(asset);
            var byAsset = new Receiver();
            channels.Register(byAsset, new ChannelKey(ChannelRef.Of(asset)));
            PlayerOn(s, null, new SignalBroadcastItem { m_Channel = ChannelRef.Of(asset) }, new SignalBroadcastItem { m_Channel = ChannelRef.Of(4) }).Play();
            Assert.That((byAsset.Got.Count, code.Got.Count), Is.EqualTo((1, 3)), "the channels apart");
            Assert.That(channels.SignalReceivers(in three), Is.EqualTo(2));
            var thrower = new Receiver { Also = sig => throw new InvalidOperationException("FQ10a receiver") };
            var after = new Receiver();
            ChannelRef six = ChannelRef.Of(6);
            var remover = new Receiver { Also = sig => channels.Unregister(after, new ChannelKey(in six)) };
            channels.Register(thrower, new ChannelKey(in six));
            channels.Register(remover, new ChannelKey(in six));
            channels.Register(after, new ChannelKey(in six));
            LogAssert.Expect(LogType.Exception, new Regex("FQ10a receiver"));
            int taken = channels.Send(new FeedbackSignal(in six, 1f));
            Assert.That((taken, after.Got.Count), Is.EqualTo((2, 0)), "a throwing receiver · a removal in the walk");
            comp.Bind(null);
            Assert.That(comp.IsRegistered, Is.False);
            p.Play();
            Assert.That(heard.Count, Is.EqualTo(2), "the component off");
            int depth = 0;
            var again = new Receiver();
            ChannelRef five = ChannelRef.Of(5);
            again.Also = sig => { depth++; channels.Send(in sig); };
            channels.Register(again, new ChannelKey(in five));
            int warned = s_Warnings.Count;
            channels.Send(new FeedbackSignal(in five, 1f));
            Assert.That(depth, Is.EqualTo(ChannelService.SignalDepth), "the resend depth");
            Assert.That(Count(ChannelService.SignalDepthWarning, warned), Is.EqualTo(1));
            Assert.That(FeedbackSignals.Send(new FeedbackSignal(in three, 1f)), Is.EqualTo(0), "outside play nothing is sent");
            Assert.That(FeedbackSignals.Register(in three, new Receiver()), Is.False);
        }

        // ---- Debug — the kinds, the player as the context, a note writes nothing, the build's kind, the break's hook

        [Test]
        public void FQ11a_Debug()
        {
            int breaks = 0;
            DebugBinding.s_Break = () => breaks++;
            FeedbackScheduler s = Sched();
            var logs = new List<(LogType, string)>();
            Application.LogCallback cb = (c, st, t) => logs.Add((t, c));
            Application.logMessageReceived += cb;
            try
            {
                FeedbackPlayer p = PlayerOn(s, null,
                    new DebugItem { m_Action = DebugAction.Log, m_Message = "FQ11a log" },
                    new DebugItem { m_Action = DebugAction.Warning, m_Message = "FQ11a warning" },
                    new DebugItem { m_Action = DebugAction.Note, m_Message = "FQ11a note" },
                    new DebugItem { m_Action = DebugAction.Break });
                p.Play();
                DebugBinding.s_DebugBuild = () => false;
                PlayerOn(s, null, new DebugItem { m_Action = DebugAction.Log, m_Message = "FQ11a dev" },
                    new DebugItem { m_Action = DebugAction.Log, m_Message = "FQ11a always", m_DevelopmentOnly = false }).Play();
            }
            finally
            {
                Application.logMessageReceived -= cb;
            }
            Assert.That(logs, Does.Contain((LogType.Log, "FQ11a log")));
            Assert.That(logs, Does.Contain((LogType.Warning, "FQ11a warning")));
            Assert.That(logs.Any(l => l.Item2.Contains("FQ11a note")), Is.False, "a note writes nothing");
            Assert.That(breaks, Is.EqualTo(1), "the break's hook");
            Assert.That(logs.Any(l => l.Item2 == "FQ11a dev"), Is.False, "a release build — the development-only line is not written");
            Assert.That(logs, Does.Contain((LogType.Log, "FQ11a always")));
        }

        // ---- the preview — the players its flow items play are bound to the session (and theirs), the auto end waits for them,
        // they come back and are let go; the display-only three count

        static void Pump(int updates)
        {
            for (int i = 0; i < updates; i++)
            {
                s_Now += 1.0 / 120.0;
                PreviewSession.Current?.Update();
            }
        }

        [Test]
        public void FQ16a_ThePreviewBindsThePlayers()
        {
            const string scenePath = "Assets/FlowTargetsTests.unity";
            Func<double> clock = PreviewClock.Source;
            Scene active = SceneManager.GetActiveScene();
            File.WriteAllText(Path.GetFullPath(scenePath), "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceUpdate);
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            s_Now = 0;
            PreviewClock.Source = () => s_Now;
            try
            {
                foreach (bool wait in new[] { true, false })
                {
                    var home = new Vector3(1f, 2f, 3f);
                    var grandGo = new GameObject("grand");
                    grandGo.transform.localPosition = home;
                    FeedbackPlayer grand = grandGo.AddComponent<FeedbackPlayer>();
                    var shake = new PositionItem { m_Motion = new ShakeMotion() };
                    shake.m_Target.m_Mode = TargetRefMode.Self;
                    shake.m_Timing.m_Duration = 0.5f;
                    grand.m_Items.Add(shake);
                    var childGo = new GameObject("child");
                    FeedbackPlayer child = childGo.AddComponent<FeedbackPlayer>();
                    child.m_Items.Add(Play(grand, wait));
                    var subjectGo = new GameObject("subject");
                    FeedbackPlayer subject = subjectGo.AddComponent<FeedbackPlayer>();
                    subject.m_Items.Add(Play(child, wait));
                    Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                    Assert.That(PreviewSession.Start(subject, default), Is.EqualTo(PreviewRefusal.None));
                    Assert.That(PreviewSession.Current.Adopted, Is.EquivalentTo(new[] { child, grand }), "bound — and theirs");
                    var notices = new List<PreviewNotice>();
                    PreviewNotices.Collect(PreviewSession.Current, notices);
                    Assert.That(notices.Any(n => n.Kind == PreviewNoticeKind.NestedPlayers && n.Text == "Plays 2 other player(s) — their targets come back at the end too."), Is.True);
                    bool moved = false;
                    int updates = 0;
                    for (; updates < 240 && PreviewSession.Current != null; updates++)
                    {
                        Pump(1);
                        moved |= grandGo.transform.localPosition != home;
                    }
                    Assert.That(moved, Is.True, "the grandchild played");
                    Assert.That(updates, Is.GreaterThanOrEqualTo(55), "the auto end waits for the bound players (wait " + wait + ")");
                    Assert.That(PreviewSession.LastEnd, Is.EqualTo(PreviewEndReason.AutoEnd));
                    Assert.That(grandGo.transform.localPosition, Is.EqualTo(home), "back");
                    Assert.That(scene.isDirty, Is.False, "the scene clean");
                    Assert.That((grand.Runtime, child.Runtime), Is.EqualTo(((PlayerRuntime)null, (PlayerRuntime)null)), "let go");
                    Assert.That(AnimationMode.InAnimationMode(), Is.False);
                    Object.DestroyImmediate(subjectGo);
                    Object.DestroyImmediate(childGo);
                    Object.DestroyImmediate(grandGo);
                }
            }
            finally
            {
                PreviewSession.Current?.Stop();
                PreviewClock.Source = clock;
                if (active.IsValid())
                    SceneManager.SetActiveScene(active);
                if (scene.IsValid())
                    EditorSceneManager.CloseScene(scene, true);
                AssetDatabase.DeleteAsset(scenePath);
            }
        }

        [Test]
        public void FQ17a_TheDisplayOnlyThree()
        {
            int breaks = 0;
            DebugBinding.s_Break = () => breaks++;
            FeedbackScheduler pv = Sched(ContextKind.Preview);
            var r = new Receiver();
            ChannelRef three = ChannelRef.Of(3);
            pv.Context.Channels.Register(r, new ChannelKey(in three));
            int calls = 0;
            var rue = new RandomUnityEventItem();
            var e = new WeightedUnityEvent();
            e.m_Event.AddListener(() => calls++);
            rue.m_Entries.Add(e);
            var logs = new List<string>();
            Application.LogCallback cb = (c, st, t) => logs.Add(c);
            Application.logMessageReceived += cb;
            try
            {
                PlayerOn(pv, null, new SignalBroadcastItem { m_Channel = three }, rue,
                    new DebugItem { m_Action = DebugAction.Log, m_Message = "FQ17a" }, new DebugItem { m_Action = DebugAction.Break }).Play();
            }
            finally
            {
                Application.logMessageReceived -= cb;
            }
            Assert.That((r.Got.Count, pv.Context.SkippedSignals), Is.EqualTo((0, 1)), "the signal counted, not sent");
            Assert.That((calls, pv.Context.SkippedUnityEvents), Is.EqualTo((0, 1)), "the event counted, not called");
            Assert.That(logs, Does.Contain(DebugBinding.PreviewPrefix + "FQ17a"), "the prefix");
            Assert.That(breaks, Is.Zero, "no break");
        }

        // ---- the inspector — the fields shown and named, the header's words, the drops

        [Test]
        public void FQ21a_TheInspector()
        {
            var play = new PlayPlayerItem();
            var chain = new PlayerChainItem();
            var control = new PlayerControlItem();
            var signal = new SignalBroadcastItem();
            var rue = new RandomUnityEventItem();
            var debug = new DebugItem();
            Assert.That(new[] { play, (FeedbackItem)new RandomPlayerItem(), chain, signal }.All(i => ItemRules.Visible(i, "m_Intensity")), Is.True, "the intensity handed on");
            Assert.That((ItemRules.Visible(rue, "m_Intensity"), ItemRules.Visible(debug, "m_Intensity")), Is.EqualTo((false, false)), "nothing reads theirs");
            control.m_Action = PlayerControlAction.Pause;
            Assert.That(ItemRules.Visible(control, "m_Intensity"), Is.False, "a Pause hands nothing on");
            control.m_Action = PlayerControlAction.Play;
            Assert.That(ItemRules.Visible(control, "m_Intensity"), Is.True, "a Play hands it on");
            Assert.That(ItemRules.Visible(control, "m_StopMode"), Is.False, "the stop mode — a Stop's");
            control.m_Action = PlayerControlAction.Stop;
            Assert.That(ItemRules.Visible(control, "m_StopMode"), Is.True);
            Assert.That((ItemRules.Visible(rue, ItemRules.Background), ItemRules.Visible(play, ItemRules.Background)), Is.EqualTo((false, true)), "the background");
            Assert.That((ItemRules.Visible(play, "m_Play.m_Wait"), ItemRules.Visible(chain, "m_Play.m_Wait")), Is.EqualTo((true, false)), "a chain's entries wait each");
            Assert.That(ItemRules.Visible(play, "m_Play.m_Timeout"), Is.True);
            play.m_Play.m_Wait = false;
            Assert.That(ItemRules.Visible(play, "m_Play.m_Timeout"), Is.False, "the time limit — a wait's");
            Assert.That(ItemRules.Label(play, "m_Play.m_Wait"), Is.EqualTo("Wait For Its End"));
            // the body draws the play's fields one level in (their names above), not the struct
            var holder = new GameObject("FQ21a holder");
            m_Made.Add(holder);
            FeedbackPlayer host = holder.AddComponent<FeedbackPlayer>();
            host.m_Items.Add(new PlayPlayerItem());
            List<string> fields = ItemBody.FieldsOf(new SerializedObject(host).FindProperty("m_Items.Array.data[0]"), host.m_Items[0]);
            Assert.That(fields, Has.Member("m_Play.m_Wait").And.Member("m_Play.m_Timeout").And.No.Member("m_Play"), string.Join(", ", fields));
            Assert.That(ItemRules.Label(debug, "m_DevelopmentOnly"), Is.EqualTo("Development Builds Only"));
            Assert.That(ItemRules.Label(signal, "m_Value"), Is.EqualTo("Value (× intensity)"));
            var go = new GameObject("FQ21a");
            m_Made.Add(go);
            FeedbackPlayer target = go.AddComponent<FeedbackPlayer>();
            play.m_Player = target;
            play.m_Play.m_Wait = true;
            Assert.That(ItemWords.Summary(play), Is.EqualTo("Play Player · FQ21a"));
            Assert.That(ItemWords.Time(play), Is.EqualTo("wait"));
            play.m_Play.m_Timeout = 0.5f;
            Assert.That(ItemWords.Time(play), Is.EqualTo("≤0.5"));
            debug.m_Action = DebugAction.Note;
            debug.m_Message = "first line\nsecond";
            Assert.That(ItemWords.Summary(debug), Is.EqualTo("Debug · first line"));
            // the drops: a player — Play Player · Player Control with it; a signal receiver — Signal Broadcast on its channel
            var receiver = go.AddComponent<FeedbackSignalReceiver>();
            receiver.m_Channel = ChannelRef.Of(7);
            List<CatalogLeaf> forPlayer = ComponentDrop.Suggest(FeedbackCatalog.All, target);
            Assert.That(forPlayer.Select(l => l.Type), Has.Member(typeof(PlayPlayerItem)).And.Member(typeof(PlayerControlItem)).And.No.Member(typeof(SignalBroadcastItem)));
            var made = (PlayPlayerItem)ComponentDrop.Create(forPlayer.First(l => l.Type == typeof(PlayPlayerItem)), go);
            Assert.That(made.m_Player, Is.SameAs(target), "a GameObject dropped — its player");
            var controls = (PlayerControlItem)ComponentDrop.Create(forPlayer.First(l => l.Type == typeof(PlayerControlItem)), target);
            Assert.That(controls.m_Players, Is.EqualTo(new[] { target }));
            List<CatalogLeaf> forReceiver = ComponentDrop.Suggest(FeedbackCatalog.All, receiver);
            Assert.That(forReceiver.Select(l => l.Type), Has.Member(typeof(SignalBroadcastItem)).And.No.Member(typeof(PlayPlayerItem)));
            var broadcast = (SignalBroadcastItem)ComponentDrop.Create(forReceiver.First(l => l.Type == typeof(SignalBroadcastItem)), receiver);
            Assert.That((broadcast.m_Channel.m_Mode, broadcast.m_Channel.m_Id), Is.EqualTo((ChannelRefMode.Int, 7)), "its channel");
        }

        // ---- the debugger — the waiting play's row names the play that waits on it · a channel's signal receivers

        [Test]
        public void FQ22a_TheDebugger()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer c = PlayerOn(s, null, Act("c", 1f));
            c.gameObject.name = "FQ22a child";
            FeedbackPlayer p = PlayerOn(s, null, Play(c));
            p.gameObject.name = "FQ22a parent";
            p.Play();
            var reader = new DebuggerReader();
            reader.Read(s);
            PlayRow childRow = reader.Plays.Single(r => r.Owner == c);
            PlayRow parentRow = reader.Plays.Single(r => r.Owner == p);
            Assert.That((childRow.Parent, childRow.ParentSerial), Is.EqualTo(((Object)p, parentRow.Serial)));
            Assert.That(parentRow.Parent, Is.Null);
            var free = PlayerOn(s, null, Act("f", 1f));
            free.Play(PlayArgs.Default.WithListener(new Ends()));
            reader.Read(s);
            PlayRow freeRow = reader.Plays.Single(r => r.Owner == free);
            Assert.That((freeRow.Parent, freeRow.HasListener), Is.EqualTo(((Object)null, true)), "a user's listener is no parent");
            s.Context.Channels.Register(new Receiver(), new ChannelKey(ChannelRef.Of(11)));
            var rows = new List<ChannelRow>();
            s.Context.Channels.Describe(rows);
            Assert.That(rows.Single(r => r.Key.Id == 11).Signals, Is.EqualTo(1));
        }

        // ---- the keep codec (a saved scene) — the players, the entries' events and their targets, a channel, the enums

        [Test]
        public void FQ23a_TheKeepCodec()
        {
            const string folder = "Assets/FlowTargetsTests";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "FlowTargetsTests");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var listener = new GameObject("FQ23a listener").AddComponent<FeedbackTestListener>();
                FeedbackPlayer target = new GameObject("FQ23a target").AddComponent<FeedbackPlayer>();
                FeedbackPlayer p = new GameObject("FQ23a").AddComponent<FeedbackPlayer>();
                FeedbackPlayer into = new GameObject("FQ23a into").AddComponent<FeedbackPlayer>();
                var channel = ScriptableObject.CreateInstance<FeedbackChannel>();
                AssetDatabase.CreateAsset(channel, folder + "/FQ23a.asset");
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), folder + "/FQ23a.unity");
                var play = Play(target, true, 0.5f);
                var chain = new PlayerChainItem();
                chain.m_Entries.Add(new ChainEntry { m_Player = target, m_Delay = 0.25f, m_Wait = false });
                var control = new PlayerControlItem { m_Action = PlayerControlAction.Stop, m_StopMode = StopMode.Complete };
                control.m_Players.Add(target);
                var rue = new RandomUnityEventItem();
                var e = new WeightedUnityEvent { m_Weight = 3f };
                UnityEventTools.AddVoidPersistentListener(e.m_Event, listener.Touch);
                rue.m_Entries.Add(e);
                var signal = new SignalBroadcastItem { m_Channel = ChannelRef.Of(channel), m_Value = 2.5f };
                var debug = new DebugItem { m_Action = DebugAction.Note, m_Message = "line1\nline2", m_DevelopmentOnly = false };
                p.m_Items.AddRange(new FeedbackItem[] { play, chain, control, rue, signal, debug });
                into.m_Items.AddRange(new FeedbackItem[] { new PlayPlayerItem(), new PlayerChainItem(), new PlayerControlItem(), new RandomUnityEventItem(), new SignalBroadcastItem(), new DebugItem() });
                string json = PlayModeRecord.Capture(p, false, out int unknown);
                PlayModeRecord.Apply(into, json, out PlayModeRecord.Stats stats);
                Assert.That((unknown, stats.Dropped), Is.EqualTo((0, 0)));
                var kp = (PlayPlayerItem)into.m_Items[0];
                Assert.That((kp.m_Player, kp.m_Play.m_Wait, kp.m_Play.m_Timeout), Is.EqualTo((target, true, 0.5f)));
                var kc = (PlayerChainItem)into.m_Items[1];
                Assert.That((kc.m_Entries.Single().m_Player, kc.m_Entries[0].m_Delay, kc.m_Entries[0].m_Wait), Is.EqualTo((target, 0.25f, false)));
                var kx = (PlayerControlItem)into.m_Items[2];
                Assert.That((kx.m_Action, kx.m_StopMode, kx.m_Players.Single()), Is.EqualTo((PlayerControlAction.Stop, StopMode.Complete, target)));
                var kr = (RandomUnityEventItem)into.m_Items[3];
                Assert.That((kr.m_Entries.Single().m_Weight, kr.m_Entries[0].m_Event.GetPersistentEventCount(), kr.m_Entries[0].m_Event.GetPersistentTarget(0)), Is.EqualTo((3f, 1, (Object)listener)));
                var ks = (SignalBroadcastItem)into.m_Items[4];
                Assert.That((ks.m_Channel.m_Asset, ks.m_Value), Is.EqualTo((channel, 2.5f)));
                var kd = (DebugItem)into.m_Items[5];
                Assert.That((kd.m_Action, kd.m_Message, kd.m_DevelopmentOnly), Is.EqualTo((DebugAction.Note, "line1\nline2", false)));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
