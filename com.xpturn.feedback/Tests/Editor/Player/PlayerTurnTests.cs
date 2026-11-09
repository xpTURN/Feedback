using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using xpTURN.Feedback.Tests.Extension;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the turn: the newest live play turns around where it is and goes on — the motions retrace without a move at the turn,
    // no instant runs in the turn's call, no Played or Stopped, no policy, cooldown or chance, the newest of Overlap's plays,
    // the items' own directions, a Loop block, a pause, Seek · Skip · Restore after it, the plain play when nothing turns, the
    // origin after a cut turned play, a timed chain, two turns in a frame, a turn inside the play's events, a custom action's
    // Begin and ticks, no allocation
    public sealed class PlayerTurnTests
    {
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        static readonly List<string> s_Events = new List<string>();
        static readonly List<string> s_Warnings = new List<string>();

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Warning)
                s_Warnings.Add(condition);
        }

        [SetUp]
        public void SetUp()
        {
            s_Events.Clear();
            s_Warnings.Clear();
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerRuntime.ResetSession();
            Application.logMessageReceived += OnLog;
            PlayerTestBinding.Hook = (b, what, record) =>
            {
                if ((what == "Begin" || what == "Execute") && b.LastBegin.Reverse && PlayerTestBinding.Log.Count > 0)
                    PlayerTestBinding.Log[PlayerTestBinding.Log.Count - 1] += "~";
            };
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            foreach (FeedbackPlayer player in m_Players)
            {
                if (player != null)
                    player.ReleaseRuntime();
            }
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Players.Clear();
            m_Made.Clear();
            m_Schedulers.Clear();
            PlayerTestBinding.Reset();
        }

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        GameObject At(string name, Vector3 at)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            go.transform.localPosition = at;
            return go;
        }

        FeedbackPlayer PlayerOn(FeedbackScheduler s, string name, params FeedbackItem[] items)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            var player = go.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Items.AddRange(items);
            player.Bind(s);
            player.Played += (pl, h) => s_Events.Add(pl.name + ".Played");
            player.Stopped += (pl, h) => s_Events.Add(pl.name + ".Stopped");
            player.Completed += (pl, h) => s_Events.Add(pl.name + ".Completed");
            return player;
        }

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };

        static T Item<T>(FeedbackMotion motion, TargetRef target, float duration, float delay = 0f) where T : TargetItem, new()
        {
            var item = new T { m_Motion = motion, m_Target = target };
            item.m_Timing.m_Duration = duration;
            item.m_Timing.m_Delay = delay;
            return item;
        }

        static TweenMotion Tween(TweenMode mode, Vector3 from, Vector3 to)
            => new TweenMotion { m_Params = new TweenParams { m_Mode = mode, m_From = from, m_To = to, m_Ease = Ease.Of(EaseType.Linear), m_Loops = 1 } };

        static PositionItem To(GameObject t, float duration = 0.4f) => Item<PositionItem>(Tween(TweenMode.To, Goal, Goal), Direct(t.transform), duration);

        static PlayerTestAction Act(string label, float length, float delay = 0f)
        {
            var action = new PlayerTestAction { m_Label = label, m_Length = length };
            action.m_Timing.m_Delay = delay;
            return action;
        }

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        static string V(GameObject g)
        {
            Vector3 p = g.transform.localPosition;
            return "(" + F(p.x) + "," + F(p.y) + "," + F(p.z) + ")";
        }

        static string TakeLog()
        {
            string text = string.Join(" ", PlayerTestBinding.Log);
            PlayerTestBinding.Log.Clear();
            return text;
        }

        static string TakeEvents()
        {
            string text = string.Join(" ", s_Events);
            s_Events.Clear();
            return text;
        }

        static readonly Vector3 Origin = new Vector3(1f, 0f, 0f);
        static readonly Vector3 Goal = new Vector3(0f, 8f, 0f);
        static PlayArgs Forward => PlayArgs.Default.WithDirection(PlayerDirection.Forward);
        static PlayArgs Backward => PlayArgs.Default.WithDirection(PlayerDirection.Backward);

        static PlaybackHandle Turn(FeedbackPlayer p, bool forward) => p.PlayTurning(forward ? PlayerDirection.Forward : PlayerDirection.Backward);

        // the motions on t's position over 0.4 s
        static (string Name, FeedbackItem Item) Motion(int which, GameObject t)
        {
            switch (which)
            {
                case 0: return ("To", To(t));
                case 1: return ("From", Item<PositionItem>(Tween(TweenMode.From, Goal, Goal), Direct(t.transform), 0.4f));
                case 2: return ("FromTo", Item<PositionItem>(Tween(TweenMode.FromTo, Origin, Goal), Direct(t.transform), 0.4f));
                case 3: return ("By", Item<PositionItem>(Tween(TweenMode.By, new Vector3(-1f, 8f, 0f), new Vector3(-1f, 8f, 0f)), Direct(t.transform), 0.4f));
                case 4:
                    PunchParams punch = PunchParams.Default;
                    punch.m_Amplitude = new Vector4(0f, 2f, 0f, 0f);
                    return ("Punch", Item<PositionItem>(new PunchMotion { m_Params = punch }, Direct(t.transform), 0.4f));
                case 5:
                    ShakeParams shake = ShakeParams.Default;
                    shake.m_Amplitude = new Vector4(1f, 1f, 0f, 0f);
                    return ("Shake", Item<PositionItem>(new ShakeMotion { m_Params = shake }, Direct(t.transform), 0.4f));
                case 6:
                    OscillateParams oscillate = OscillateParams.Default;
                    oscillate.m_Amplitude = new Vector4(0f, 2f, 0f, 0f);
                    return ("Oscillate", Item<PositionItem>(new OscillateMotion { m_Params = oscillate }, Direct(t.transform), 0.4f));
                default:
                    SpringParams spring = SpringParams.Default;
                    spring.m_Mode = SpringMode.MoveTo;
                    spring.m_Value = new Vector4(0f, 8f, 0f, 0f);
                    return ("Spring MoveTo", Item<PositionItem>(new SpringMotion { m_Params = spring }, Direct(t.transform), 0.4f));
            }
        }

        // E · X · E · X · E 0.2 · 0.1 · 0.2 · 0.1 s apart: no move inside a turn's call (a From's first E is its start), the
        // values retrace — the value after each turn's tick is the one a tick before the turn — and the ends
        [Test]
        public void PTN01_TheMotionsRetrace()
        {
            var fails = new List<string>();
            for (int m = 0; m < 8; m++)
            {
                FeedbackScheduler s = Sched();
                GameObject t = At("t", Origin);
                (string name, FeedbackItem item) = Motion(m, t);
                FeedbackPlayer p = PlayerOn(s, "p", item);
                var v = new List<string>();
                var jumps = new List<string>();
                (bool forward, int ticks)[] steps = { (true, 2), (false, 1), (true, 2), (false, 1), (true, 0) };
                for (int k = 0; k < steps.Length; k++)
                {
                    Vector3 before = t.transform.localPosition;
                    Turn(p, steps[k].forward);
                    float jump = (t.transform.localPosition - before).magnitude;
                    if (k > 0 && jump > 1e-3f)
                        jumps.Add("event " + k + " moved " + F(jump));
                    v.Add(V(t));
                    for (int i = 0; i < steps[k].ticks; i++)
                    {
                        s.Tick(0.1f);
                        v.Add(V(t));
                    }
                }
                for (int i = 0; i < 5; i++)
                    s.Tick(0.1f);
                string end = V(t) + " " + p.IsPlaying;
                Turn(p, false);
                for (int i = 0; i < 5; i++)
                    s.Tick(0.1f);
                string exitEnd = V(t) + " " + p.IsPlaying;
                // v: 0 E · 1 · 2 · 3 X · 4 · 5 E · 6 · 7 · 8 X · 9 · 10 E
                bool retraces = v[4] == v[1] && v[6] == v[2] && v[9] == v[6];
                string wantEnd = name == "From" ? "(1,0,0) False" : name == "To" || name == "FromTo" || name == "By" || name == "Spring MoveTo" ? "(0,8,0) False" : "(1,0,0) False";
                string wantExit = name == "From" ? "(0,8,0) False" : "(1,0,0) False";
                if (jumps.Count > 0 || !retraces || end != wantEnd || exitEnd != wantExit)
                    fails.Add(name + ": " + string.Join(" · ", jumps) + " values " + string.Join(" ", v) + " end " + end + " exit end " + exitEnd);
                if (m == 0 && string.Join(" ", v) != "(1,0,0) (0.75,2,0) (0.5,4,0) (0.5,4,0) (0.75,2,0) (0.75,2,0) (0.5,4,0) (0.25,6,0) (0.25,6,0) (0.5,4,0) (0.5,4,0)")
                    fails.Add("To's values: " + string.Join(" ", v));
            }
            Assert.That(fails, Is.Empty);
        }

        // pos To 0 ~ 0.4 · i0 at 0 · a 0 ~ 0.3 · i1 at 0.3 · late SetActive(false) at 0.3 · b 0.3 ~ 0.4 · i2 at 0.4
        FeedbackItem[] Mixed(GameObject t, GameObject late)
        {
            var set = Item<SetActiveItem>(new SetMotion { m_Params = new SetParams { m_Value = Vector4.zero } }, Direct(late), 0f);
            return new FeedbackItem[] { To(t), Act("i0", 0f), Act("a", 0.3f), Act("i1", 0f, 0.3f), set, Act("b", 0.1f), Act("i2", 0f, 0.1f) };
        }

        static string Late(GameObject late) => " late " + (late.activeSelf ? 1 : 0);

        // no instant runs in a turn's call — the call evaluates the place once; the instants passed going back are undone as
        // they pass (i1 · the late Set at 0.3), and run again going forward again
        [Test]
        public void PTN02_NoInstantRunsInATurn()
        {
            var rows = new List<string>();
            foreach (int cut in new[] { 1, 35 })
            {
                FeedbackScheduler s = Sched();
                GameObject t = At("t", Origin);
                GameObject late = At("late", Vector3.zero);
                FeedbackPlayer p = PlayerOn(s, "p", Mixed(t, late));
                Turn(p, true);
                TakeLog();
                foreach (float dt in cut == 1 ? new[] { 0.1f } : new[] { 0.1f, 0.1f, 0.1f, 0.05f })
                    s.Tick(dt);
                string ran = TakeLog();
                string at = V(t) + Late(late);
                Turn(p, false);
                string back = TakeLog();
                var ticks = new List<string>();
                for (int k = 0; k < 2; k++)
                {
                    s.Tick(0.1f);
                    ticks.Add("[" + TakeLog() + "] " + V(t) + Late(late));
                }
                Turn(p, true);
                string again = TakeLog();
                for (int k = 0; k < 5; k++)
                {
                    s.Tick(0.1f);
                    ticks.Add("[" + TakeLog() + "] " + V(t) + Late(late));
                }
                rows.Add("ran[" + ran + "] at " + at + " | back [" + back + "] | " + string.Join(" ", ticks.Take(2)) + " | fwd [" + again + "] | " + string.Join(" ", ticks.Skip(2)) + " | " + TakeEvents());
            }
            Assert.That(rows, Is.EqualTo(new[]
            {
                "ran[a.Evaluate(0.333)] at (0.75,2,0) late 1 | back [a.Evaluate(0.333)] | [a.Evaluate(0)] (1,0,0) late 1 [a.Evaluate(0) a.End:Completed i0.Execute#0~ a.Settle] (1,0,0) late 1"
                    + " | fwd [i0.Execute#0 a.Begin#0 a.Evaluate(0)] | [a.Evaluate(0.333)] (0.75,2,0) late 1 [a.Evaluate(0.667)] (0.5,4,0) late 1 [a.Evaluate(1) a.End:Completed i1.Execute#0 b.Begin#0 a.Settle b.Evaluate(0)] (0.25,6,0) late 0"
                    + " [b.Evaluate(1) b.End:Completed i2.Execute#0 b.Settle] (0,8,0) late 0 [] (0,8,0) late 0 | p.Played p.Completed p.Played p.Completed",
                "ran[a.Evaluate(0.333) a.Evaluate(0.667) a.Evaluate(1) a.End:Completed i1.Execute#0 b.Begin#0 a.Settle b.Evaluate(0) b.Evaluate(0.5)] at (0.125,7,0) late 0 | back [b.Evaluate(0.5)]"
                    + " | [b.Evaluate(0) b.End:Completed i1.Execute#0~ a.Begin#0~ b.Settle a.Evaluate(0.833)] (0.375,5,0) late 1 [a.Evaluate(0.5)] (0.625,3,0) late 1 | fwd [a.Evaluate(0.5)]"
                    + " | [a.Evaluate(0.833)] (0.375,5,0) late 1 [a.Evaluate(1) a.End:Completed i1.Execute#0 b.Begin#0 a.Settle b.Evaluate(0.5)] (0.125,7,0) late 0 [b.Evaluate(1) b.End:Completed i2.Execute#0 b.Settle] (0,8,0) late 0"
                    + " [] (0,8,0) late 0 [] (0,8,0) late 0 | p.Played p.Completed",
            }));
        }

        // an instant at the turn's very place (0.2 = 0.1 + 0.1): done forward, undone by the backward head's first tick
        [Test]
        public void PTN02b_AnInstantAtThePlace()
        {
            FeedbackScheduler s = Sched();
            GameObject t = At("t", Origin);
            FeedbackPlayer p = PlayerOn(s, "p", To(t), Act("i", 0f, 0.2f));
            Turn(p, true);
            s.Tick(0.1f);
            s.Tick(0.1f);
            string forward = TakeLog();
            Turn(p, false);
            string turn = TakeLog();
            s.Tick(0.1f);
            string back = TakeLog();
            Assert.That((forward, turn, back), Is.EqualTo(("i.Execute#0", "", "i.Execute#0~")));
        }

        // one play: the same handle, Played and Completed only — Restart, Ignore and a cooldown turn alike; Overlap turns its
        // newest play
        [Test]
        public void PTN03_OnePlayNoPolicy()
        {
            var rows = new List<string>();
            foreach ((string name, PlayPolicy policy, float cooldown) in new[] { ("restart", PlayPolicy.Restart, 0f), ("ignore", PlayPolicy.Ignore, 0f), ("cooldown", PlayPolicy.Restart, 5f) })
            {
                FeedbackScheduler s = Sched();
                GameObject t = At("t", Origin);
                FeedbackPlayer p = PlayerOn(s, name, To(t));
                p.m_Policy = policy;
                p.m_Cooldown = cooldown;
                PlaybackHandle h1 = Turn(p, true);
                s.Tick(0.2f);
                PlaybackHandle h2 = Turn(p, false);
                s.Tick(0.1f);
                PlaybackHandle h3 = Turn(p, true);
                for (int k = 0; k < 6; k++)
                    s.Tick(0.1f);
                rows.Add(name + " " + (h1 != default && h1 == h2 && h2 == h3) + " " + V(t) + " " + TakeEvents());
            }
            Assert.That(rows, Is.EqualTo(new[]
            {
                "restart True (0,8,0) restart.Played restart.Completed",
                "ignore True (0,8,0) ignore.Played ignore.Completed",
                "cooldown True (0,8,0) cooldown.Played cooldown.Completed",
            }));
            FeedbackScheduler so = Sched();
            FeedbackPlayer o = PlayerOn(so, "overlap", Act("a", 0.4f));
            o.m_Policy = PlayPolicy.Overlap;
            PlaybackHandle first = o.Play(Forward);
            so.Tick(0.1f);
            PlaybackHandle second = o.Play(Forward);
            so.Tick(0.1f);
            PlaybackHandle turned = Turn(o, false);
            Assert.That((turned == second, turned == first), Is.EqualTo((true, false)), "the newest play turns");
            Assert.That(o.Position(first) > o.Position(second), Is.True, "the older play goes on forward, the newer one back");
        }

        // the items' own directions: each goes on where it is — Follow and Forward, Backward and Opposite alike
        [Test]
        public void PTN04_TheItemsOwnDirections()
        {
            FeedbackScheduler s = Sched();
            var targets = new List<GameObject>();
            var items = new List<FeedbackItem>();
            foreach (ItemPlayDirection d in new[] { ItemPlayDirection.FollowPlayer, ItemPlayDirection.Forward, ItemPlayDirection.Backward, ItemPlayDirection.Opposite })
            {
                GameObject t = At(d.ToString(), Origin);
                targets.Add(t);
                PositionItem item = To(t);
                item.m_PlayDirection = d;
                items.Add(item);
            }
            FeedbackPlayer p = PlayerOn(s, "dirs", items.ToArray());
            var rows = targets.Select(t => new List<string>()).ToList();
            void Snap()
            {
                for (int i = 0; i < targets.Count; i++)
                    rows[i].Add(V(targets[i]));
            }
            Turn(p, true);
            Snap();
            s.Tick(0.1f);
            s.Tick(0.1f);
            Snap();
            Turn(p, false);
            Snap();
            s.Tick(0.1f);
            Snap();
            Turn(p, true);
            Snap();
            s.Tick(0.1f);
            Snap();
            Assert.That(rows.Select(r => string.Join(" ", r)), Is.EqualTo(new[]
            {
                "(1,0,0) (0.5,4,0) (0.5,4,0) (0.75,2,0) (0.75,2,0) (0.5,4,0)",
                "(1,0,0) (0.5,4,0) (0.5,4,0) (0.75,2,0) (0.75,2,0) (0.5,4,0)",
                "(0,8,0) (0.5,4,0) (0.5,4,0) (0.25,6,0) (0.25,6,0) (0.5,4,0)",
                "(0,8,0) (0.5,4,0) (0.5,4,0) (0.25,6,0) (0.25,6,0) (0.5,4,0)",
            }));
        }

        // a Loop block (0.2 s × 3) turned in its second pass: no move, back through the block to the start, Looped at each
        // boundary passed
        [Test]
        public void PTN05_ALoopBlock()
        {
            FeedbackScheduler s = Sched();
            GameObject t = At("t", Origin);
            var end = new LoopEndItem { m_LoopCount = 3 };
            end.m_Timing.m_Rule = StartRule.AfterPrevious;
            FeedbackPlayer p = PlayerOn(s, "loop", new LoopStartItem(), To(t, 0.2f), end);
            p.Looped += (pl, h) => s_Events.Add("Looped");
            var seq = new List<string>();
            Turn(p, true);
            for (int k = 0; k < 3; k++)
            {
                s.Tick(0.1f);
                seq.Add(V(t));
            }
            Turn(p, false);
            seq.Add(V(t));
            for (int k = 0; k < 5; k++)
            {
                s.Tick(0.1f);
                seq.Add(V(t));
            }
            Assert.That(string.Join(" ", seq) + " " + p.IsPlaying + " " + TakeEvents(),
                Is.EqualTo("(0.5,4,0) (1,0,0) (0.5,4,0) (0.5,4,0) (1,0,0) (0.5,4,0) (1,0,0) (1,0,0) (1,0,0) False loop.Played Looped Looped loop.Completed"));
        }

        // a paused play turns without a move and goes back after Resume; Seek, Skip and Restore act on a turned play as on a
        // backward one
        [Test]
        public void PTN06_PauseSeekSkipRestore()
        {
            FeedbackScheduler s = Sched();
            GameObject t = At("t", Origin);
            FeedbackPlayer p = PlayerOn(s, "paused", To(t));
            var seq = new List<string>();
            Turn(p, true);
            s.Tick(0.1f);
            s.Tick(0.1f);
            p.Pause();
            Turn(p, false);
            seq.Add(V(t));
            s.Tick(0.1f);
            seq.Add(V(t));
            p.Resume();
            s.Tick(0.1f);
            seq.Add(V(t));
            Assert.That(string.Join(" ", seq), Is.EqualTo("(0.5,4,0) (0.5,4,0) (0.75,2,0)"), "paused · paused tick · resumed");

            GameObject sought = At("sought", Origin);
            FeedbackPlayer ps = PlayerOn(s, "seek", To(sought));
            Turn(ps, true);
            s.Tick(0.1f);
            s.Tick(0.1f);
            Turn(ps, false);
            bool moved = ps.Seek(0.3f);
            string atSeek = V(sought);
            s.Tick(0.1f);
            Assert.That((moved, atSeek, V(sought)), Is.EqualTo((true, "(0.25,6,0)", "(0.5,4,0)")), "Seek on a turned play");

            GameObject skipped = At("skipped", Origin);
            FeedbackPlayer pk = PlayerOn(s, "skip", To(skipped));
            Turn(pk, true);
            s.Tick(0.1f);
            s.Tick(0.1f);
            Turn(pk, false);
            bool ended = pk.Skip();
            Assert.That((ended, V(skipped), pk.IsPlaying), Is.EqualTo((true, "(1,0,0)", false)), "Skip runs the turned play to its start");

            GameObject restored = At("restored", Origin);
            FeedbackPlayer pr = PlayerOn(s, "restore", To(restored));
            Turn(pr, true);
            s.Tick(0.1f);
            s.Tick(0.1f);
            Turn(pr, false);
            s.Tick(0.1f);
            pr.Restore();
            Assert.That((V(restored), pr.IsPlaying), Is.EqualTo(("(1,0,0)", false)), "Restore");
        }

        // nothing to turn — the plain play of that direction: no live play, a play the same way, a list that cannot play
        // backward (refused with the warning, the forward play goes on)
        [Test]
        public void PTN07_ThePlainPlay()
        {
            FeedbackScheduler s = Sched();
            GameObject idle = At("idle", Origin);
            FeedbackPlayer pi = PlayerOn(s, "idle", To(idle));
            Turn(pi, false);
            Assert.That((V(idle), TakeEvents()), Is.EqualTo(("(0,8,0)", "idle.Played")), "no live play: a backward play from the end");

            GameObject same = At("same", Origin);
            FeedbackPlayer pm = PlayerOn(s, "same", To(same));
            PlaybackHandle a = Turn(pm, true);
            s.Tick(0.1f);
            PlaybackHandle b = Turn(pm, true);
            Assert.That((a != b, TakeEvents()), Is.EqualTo((true, "same.Played same.Stopped same.Played")), "the same way: a new play (Restart)");

            GameObject open = At("open", Origin);
            FeedbackPlayer po = PlayerOn(s, "open", To(open), new WaitForSignalItem());
            Turn(po, true);
            s.Tick(0.1f);
            s_Warnings.Clear();
            PlaybackHandle refused = Turn(po, false);
            s.Tick(0.1f);
            Assert.That((refused == default, po.IsPlaying, V(open)), Is.EqualTo((true, true, "(0.5,4,0)")), "a list that cannot play backward");
            Assert.That(s_Warnings, Is.EqualTo(new[] { FeedbackPlayer.ReverseWarning }));
        }

        // a turned play cut, then plain plays forward and backward: the backward one ends at the origin — the binding took
        // the turned direction (the plain sequence alike)
        [Test]
        public void PTN08_TheOriginAfterACut()
        {
            var rows = new List<string>();
            foreach (bool turned in new[] { true, false })
            {
                FeedbackScheduler s = Sched();
                GameObject t = At("t", Origin);
                FeedbackPlayer p = PlayerOn(s, "origin", To(t));
                if (turned)
                    Turn(p, true);
                else
                    p.Play(Forward);
                s.Tick(0.1f);
                s.Tick(0.1f);
                if (turned)
                    Turn(p, false);
                else
                    p.Play(Backward);
                s.Tick(0.1f);
                string cut = V(t);
                p.Stop(StopMode.Hold);
                p.Play(Forward);
                for (int k = 0; k < 5; k++)
                    s.Tick(0.1f);
                string forward = V(t);
                p.Play(Backward);
                for (int k = 0; k < 5; k++)
                    s.Tick(0.1f);
                rows.Add(cut + " " + forward + " " + V(t));
            }
            Assert.That(rows, Is.EqualTo(new[] { "(0.75,2,0) (0,8,0) (1,0,0)", "(0.25,6,0) (0,8,0) (1,0,0)" }), "turned · plain");
        }

        // a timed chain (c1 0.1 · c2 0.2 · c3 0.3) turned at 0.25: no child in the turn's call, c2 then c1 as the play goes
        // back, c3 never
        [Test]
        public void PTN09_ATimedChain()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer c1 = PlayerOn(s, "c1", Act("c1", 0.05f));
            FeedbackPlayer c2 = PlayerOn(s, "c2", Act("c2", 0.05f));
            FeedbackPlayer c3 = PlayerOn(s, "c3", Act("c3", 0.05f));
            var chain = new PlayerChainItem();
            foreach (FeedbackPlayer child in new[] { c1, c2, c3 })
                chain.m_Entries.Add(new ChainEntry { m_Player = child, m_Delay = 0.1f, m_Wait = false });
            FeedbackPlayer p = PlayerOn(s, "chain", chain);
            Turn(p, true);
            s.Tick(0.1f);
            s.Tick(0.1f);
            s.Tick(0.05f);
            string forward = TakeEvents();
            Turn(p, false);
            string turn = TakeEvents();
            var ticks = new List<string>();
            for (int k = 0; k < 4; k++)
            {
                s.Tick(0.1f);
                ticks.Add("[" + TakeEvents() + "]");
            }
            Assert.That(forward + " | " + turn + " | " + string.Join(" ", ticks),
                Is.EqualTo("chain.Played c1.Played c2.Played c1.Completed c2.Completed |  | [c2.Played] [c1.Played c2.Completed] [chain.Completed c1.Completed] []"));
        }

        // two turns in a frame: back to the way it went, no move, then on forward
        [Test]
        public void PTN10_TwoTurnsInAFrame()
        {
            FeedbackScheduler s = Sched();
            GameObject t = At("t", Origin);
            FeedbackPlayer p = PlayerOn(s, "twice", To(t));
            PlaybackHandle h = Turn(p, true);
            s.Tick(0.1f);
            s.Tick(0.1f);
            PlaybackHandle back = Turn(p, false);
            PlaybackHandle again = Turn(p, true);
            string at = V(t);
            s.Tick(0.1f);
            Assert.That((h == back && back == again, at, V(t), TakeEvents()), Is.EqualTo((true, "(0.5,4,0)", "(0.25,6,0)", "twice.Played")));
        }

        static FeedbackPlayer s_Turner;
        static PlaybackHandle s_Asked;

        // a turn asked inside the play's events (an instant's binding at 0.3) waits for the pass's end: the same play, back
        // from the next tick
        [Test]
        public void PTN11_ATurnInsideThePlaysEvents()
        {
            FeedbackScheduler s = Sched();
            GameObject t = At("t", Origin);
            FeedbackPlayer p = PlayerOn(s, "inside", To(t), Act("i", 0f, 0.3f));
            s_Turner = p;
            s_Asked = default;
            Action<PlayerTestBinding, string, int> mark = PlayerTestBinding.Hook;
            PlayerTestBinding.Hook = (b, what, record) =>
            {
                mark(b, what, record);
                if (what == "Execute" && b.Item.m_Label == "i" && !b.LastBegin.Reverse && s_Turner != null)
                {
                    FeedbackPlayer turner = s_Turner;
                    s_Turner = null;
                    s_Asked = turner.PlayTurning(PlayerDirection.Backward);
                }
            };
            PlaybackHandle h = Turn(p, true);
            s.Tick(0.1f);
            s.Tick(0.1f);
            s.Tick(0.1f);
            string atPass = V(t);
            s.Tick(0.1f);
            Assert.That((s_Asked == h, atPass, V(t), TakeEvents()), Is.EqualTo((true, "(0.25,6,0)", "(0.5,4,0)", "inside.Played")));
        }

        // a custom timed action: one Begin (its Reverse stays), the ticks' T goes back after the turn, one end
        [Test]
        public void PTN12_ACustomTimedAction()
        {
            ProbeLog.Clear();
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = PlayerOn(s, "custom", new ProbeTimed { m_Length = 0.4f });
            Turn(p, true);
            s.Tick(0.1f);
            s.Tick(0.1f);
            Turn(p, false);
            s.Tick(0.1f);
            for (int k = 0; k < 3; k++)
                s.Tick(0.1f);
            List<string> calls = ProbeLog.Calls.Where(c => !c.StartsWith("Prepare", StringComparison.Ordinal) && !c.StartsWith("Release", StringComparison.Ordinal)).ToList();
            string ts = string.Join(" ", ProbeLog.Values.Take(5).Select(F));
            Assert.That((string.Join(" | ", calls), ts), Is.EqualTo(("Begin s0 r0 | End s0 Completed", "0 0.25 0.5 0.5 0.25")));
        }

        static FeedbackPlayer s_Alloc;
        static object s_Sink;

        static void TwoTurns()
        {
            s_Alloc.PlayTurning(PlayerDirection.Backward);
            s_Alloc.PlayTurning(PlayerDirection.Forward);
        }

        // true only when five runs in a row allocate — 6000.3 counts unrelated allocations now and then
        static bool Allocates(TestDelegate run)
        {
            for (int i = 0; i < 5; i++)
            {
                if (!UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory().ApplyTo(run).IsSuccess)
                    return false;
            }
            return true;
        }

        // two turns allocate nothing once warm (the positive control allocates)
        [Test]
        public void PTN13_NoAllocation()
        {
            FeedbackScheduler s = Sched();
            GameObject t = At("t", Origin);
            s_Alloc = PlayerOn(s, "alloc", To(t, 10f));
            try
            {
                s_Alloc.PlayTurning(PlayerDirection.Forward);
                s.Tick(0.1f);
                TwoTurns();
                Assert.That(Allocates(TwoTurns), Is.False, "two turns");
                Assert.That(Allocates(() => s_Sink = new object[8]), Is.True, "the positive control");
                Assert.That(s_Alloc.IsPlaying, Is.True);
            }
            finally
            {
                s_Alloc = null;
            }
        }
    }
}
