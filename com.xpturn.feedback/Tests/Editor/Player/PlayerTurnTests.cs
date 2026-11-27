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

        static void TwoTurnsBack()
        {
            s_Alloc.PlayTurning(PlayerDirection.Forward);
            s_Alloc.PlayTurning(PlayerDirection.Backward);
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

                s_Alloc = Delayed(out FeedbackScheduler d, 5f, Act("a", 1f));
                s_Alloc.Play(Forward);
                d.Tick(0.25f);
                TwoTurns();
                Assert.That(Allocates(TwoTurns), Is.False, "two turns in a forward delay");
                s_Alloc.Stop();
                s_Alloc.Play(Backward);
                d.Tick(0.25f);
                TwoTurnsBack();
                Assert.That(Allocates(TwoTurnsBack), Is.False, "two turns in a backward delay");
            }
            finally
            {
                s_Alloc = null;
            }
        }

        // ---- the start delay

        FeedbackPlayer Delayed(out FeedbackScheduler s, float delay, params FeedbackItem[] items)
        {
            s = Sched();
            FeedbackPlayer p = PlayerOn(s, "delayed", items);
            p.m_StartDelay = delay;
            p.Played += (pl, h) => PlayerTestBinding.Log.Add("|Played");
            p.Completed += (pl, h) => PlayerTestBinding.Log.Add("|Completed");
            p.Stopped += (pl, h) => PlayerTestBinding.Log.Add("|Stopped");
            p.Looped += (pl, h) => PlayerTestBinding.Log.Add("|Looped");
            return p;
        }

        static long Place(FeedbackPlayer p) => p.Runtime.Position(0);

        static string Ticked(FeedbackScheduler s, float dt)
        {
            s.Tick(dt);
            return "[" + TakeLog() + "]";
        }

        static string Ticked(FeedbackScheduler s, int n, float dt) => string.Join(" ", Enumerable.Range(0, n).Select(_ => Ticked(s, dt)));

        // a backward play turned forward in its delay: no event in the call, Completed alone on the next tick — after a pause on
        // the first tick after Resume, at once on Skip, on the tick after a seek to the end, and a seek before the end plays on
        [Test]
        public void PTN14_ABackwardDelayTurnedForward()
        {
            FeedbackPlayer p = Delayed(out FeedbackScheduler s, 0.5f, Act("a", 1f));
            PlaybackHandle h = p.Play(Backward);
            s.Tick(0.25f);
            TakeLog();
            PlaybackHandle turned = Turn(p, true);
            Assert.That((turned == h, TakeLog(), p.IsPlaying), Is.EqualTo((true, "", true)));
            Assert.That((Ticked(s, 0.01f), p.IsPlaying), Is.EqualTo(("[|Completed]", false)));
            Assert.That(Ticked(s, 3f), Is.EqualTo("[]"));

            FeedbackPlayer q = Delayed(out FeedbackScheduler qs, 0.5f, Act("a", 1f));
            q.Play(Backward);
            qs.Tick(0.25f);
            q.Pause();
            TakeLog();
            Turn(q, true);
            Assert.That((Ticked(qs, 0.1f), q.IsPlaying), Is.EqualTo(("[]", true)), "paused");
            q.Resume();
            Assert.That((Ticked(qs, 0.01f), q.IsPlaying), Is.EqualTo(("[|Completed]", false)), "resumed");

            FeedbackPlayer k = Delayed(out FeedbackScheduler ks, 0.5f, Act("a", 1f));
            k.Play(Backward);
            ks.Tick(0.25f);
            TakeLog();
            Turn(k, true);
            k.Skip();
            Assert.That((TakeLog(), k.IsPlaying), Is.EqualTo(("|Completed", false)), "skip");

            FeedbackPlayer e = Delayed(out FeedbackScheduler es, 0.5f, Act("a", 1f));
            e.Play(Backward);
            es.Tick(0.25f);
            TakeLog();
            Turn(e, true);
            e.Seek(1.5f);
            Assert.That((TakeLog(), e.IsPlaying), Is.EqualTo(("", true)), "seek to the end");
            Assert.That((Ticked(es, 0.01f), e.IsPlaying), Is.EqualTo(("[|Completed]", false)), "after the seek");

            FeedbackPlayer m = Delayed(out FeedbackScheduler ms, 0.5f, Act("a", 1f));
            m.Play(Backward);
            ms.Tick(0.25f);
            TakeLog();
            Turn(m, true);
            m.Seek(0.75f);
            Assert.That((TakeLog(), Place(m)), Is.EqualTo(("a.Begin#0~ a.Evaluate(0.25)", 750000L)), "seek before the end");
            Assert.That(Ticked(ms, 3, 0.25f), Is.EqualTo("[a.Evaluate(0.5)] [a.Evaluate(0.75)] [a.Evaluate(1) a.End:Completed a.Settle |Completed]"));
        }

        // two turns in a forward delay keep the place and the time left of the delay
        [Test]
        public void PTN15_TwoTurnsKeepTheForwardDelay()
        {
            FeedbackPlayer p = Delayed(out FeedbackScheduler s, 1f, Act("a", 1f));
            p.Play(Forward);
            s.Tick(0.25f);
            TakeLog();
            Turn(p, false);
            Assert.That(Place(p), Is.EqualTo(250001L));
            Turn(p, true);
            Assert.That((TakeLog(), Place(p)), Is.EqualTo(("", 250000L)));
            p.Runtime.SeekAt(Place(p));
            Assert.That((TakeLog(), p.IsPlaying), Is.EqualTo(("", true)), "the same place");
            Assert.That(Ticked(s, 0.5f), Is.EqualTo("[]"));
            Assert.That(Ticked(s, 0.5f), Is.EqualTo("[a.Begin#0 a.Evaluate(0.25)]"));
            Assert.That(Ticked(s, 3, 0.5f), Is.EqualTo("[a.Evaluate(0.75)] [a.Evaluate(1) a.End:Completed a.Settle |Completed] []"));
        }

        // two turns in a backward delay keep the time left of it
        [Test]
        public void PTN16_TwoTurnsKeepTheBackwardDelay()
        {
            FeedbackPlayer p = Delayed(out FeedbackScheduler s, 0.5f, Act("a", 1f));
            p.Play(Backward);
            s.Tick(0.25f);
            TakeLog();
            Turn(p, true);
            Turn(p, false);
            Assert.That((TakeLog(), Place(p)), Is.EqualTo(("", 1500001L)));
            Assert.That(Ticked(s, 0.125f), Is.EqualTo("[]"));
            Assert.That(Ticked(s, 0.25f), Is.EqualTo("[a.Begin#0~ a.Evaluate(0.875)]"));
            Assert.That(Ticked(s, 3, 0.5f), Is.EqualTo("[a.Evaluate(0.375)] [a.Evaluate(0) a.End:Completed a.Settle |Completed] []"));
        }

        // a forward delay turned backward, then a seek past the delay: the play goes on backward from there; inside the delay it ends
        [Test]
        public void PTN17_ASeekBringsATurnedDelayBack()
        {
            FeedbackPlayer p = Delayed(out FeedbackScheduler s, 1f, Act("a", 1f));
            p.Play(Forward);
            s.Tick(0.25f);
            TakeLog();
            Turn(p, false);
            p.Seek(2f);
            Assert.That((TakeLog(), Place(p)), Is.EqualTo(("a.Begin#0 a.Evaluate(1)", 2000000L)));
            Assert.That(Ticked(s, 4, 0.25f), Is.EqualTo("[a.Evaluate(0.75)] [a.Evaluate(0.5)] [a.Evaluate(0.25)] [a.Evaluate(0) a.End:Completed a.Settle |Completed]"));

            FeedbackPlayer q = Delayed(out FeedbackScheduler qs, 1f, Act("a", 1f));
            q.Play(Forward);
            qs.Tick(0.25f);
            TakeLog();
            Turn(q, false);
            q.Seek(0.5f);
            Assert.That((TakeLog(), q.IsPlaying), Is.EqualTo(("|Completed", false)), "a target inside the delay ends it");
        }

        // two turns in the delay are nothing: tick by tick the same log as the twin that never turned, over mixed clocks
        [Test]
        public void PTN18_TwoTurnsInTheDelayAreNothing([Values(false, true)] bool reverse)
        {
            int compared = 0;
            for (int seed = 0; seed < 200; seed++)
            {
                DelayPlan plan = DelayPlan.Make(seed);
                List<string> control = RunPlan(plan, reverse, -1, out bool controlRan);
                if (!controlRan)
                    continue;
                List<string> turned = RunPlan(plan, reverse, plan.TurnTick, out bool turnedRan);
                if (!turnedRan)
                    continue;
                compared++;
                Assert.That(turned, Is.EqualTo(control), "seed " + seed);
            }
            Assert.That(compared, Is.GreaterThan(100));
        }

        sealed class DelayPlan
        {
            public float Delay;
            public (float Length, float Delay, StartRule Rule, ItemClock Clock)[] Items;
            public float[] Scaled;
            public float[] Unscaled;
            public int TurnTick;

            public static DelayPlan Make(int seed)
            {
                var rng = new System.Random(seed);
                var plan = new DelayPlan { Delay = rng.Next(16, 65) / 64f };
                int n = rng.Next(1, 5);
                plan.Items = new (float, float, StartRule, ItemClock)[n];
                bool mixed = seed % 2 == 1;
                for (int i = 0; i < n; i++)
                    plan.Items[i] = (rng.Next(0, 33) / 64f, rng.Next(0, 17) / 64f, rng.Next(2) == 0 ? StartRule.WithPrevious : StartRule.AfterPrevious,
                        mixed ? (rng.Next(2) == 0 ? ItemClock.Scaled : ItemClock.Unscaled) : ItemClock.Scaled);
                const int ticks = 120;
                plan.Scaled = new float[ticks];
                plan.Unscaled = new float[ticks];
                for (int k = 0; k < ticks; k++)
                {
                    float a = rng.Next(1, 9) / 64f;
                    plan.Scaled[k] = a;
                    plan.Unscaled[k] = seed % 4 == 3 ? rng.Next(1, 9) / 64f : a;
                }
                plan.TurnTick = rng.Next(0, 3);
                return plan;
            }
        }

        List<string> RunPlan(DelayPlan plan, bool reverse, int turnAt, out bool ran)
        {
            ran = false;
            var rows = new List<string>();
            PlayerTestBinding.Log.Clear();
            var items = new FeedbackItem[plan.Items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                PlayerTestAction action = Act("a" + i, plan.Items[i].Length, plan.Items[i].Delay);
                action.m_Timing.m_Rule = plan.Items[i].Rule;
                action.m_Timing.m_Clock = plan.Items[i].Clock;
                items[i] = action;
            }
            FeedbackPlayer p = Delayed(out FeedbackScheduler s, plan.Delay, items);
            PlaybackHandle h = p.Play(reverse ? Backward : Forward);
            if (h == default)
                return rows;
            rows.Add(TakeLog());
            long delay = TimelineTime.FromSeconds(plan.Delay);
            for (int k = 0; k < plan.Scaled.Length; k++)
            {
                if (turnAt == k)
                {
                    if (!p.IsPlaying)
                        return rows;
                    long at = Place(p);
                    bool inDelay = reverse ? at > p.Runtime.Record(0).Resolved.Total : at < delay;
                    if (!inDelay)
                        return rows;
                    Turn(p, reverse);
                    Turn(p, !reverse);
                    TakeLog();
                }
                s.Tick(new FrameTime(plan.Scaled[k], plan.Unscaled[k]));
                rows.Add(TakeLog());
                if (!p.IsPlaying)
                    break;
            }
            ran = true;
            if (p.IsPlaying)
                p.Stop();
            PlayerTestBinding.Log.Clear();
            return rows;
        }

        // the values: a play at its end value keeps it through a turned delay, a seek after the turn takes its value, a seek in
        // the last event plays again from the start to the end value, and the next backward play goes to the origin
        [Test]
        public void PTN19_TheValuesThroughATurnedDelay()
        {
            GameObject t = At("t", Origin);
            FeedbackPlayer p = Delayed(out FeedbackScheduler s, 0.25f, To(t, 0.5f));
            p.Play(Forward);
            Ticked(s, 8, 0.125f);
            string atEnd = V(t);
            Turn(p, false);
            s.Tick(0.125f);
            Turn(p, true);
            string turned = V(t);
            s.Tick(0.125f);
            string done = V(t) + " " + p.IsPlaying;
            Turn(p, false);
            Ticked(s, 10, 0.125f);
            Assert.That(atEnd + " " + turned + " " + done + " back " + V(t) + " " + p.IsPlaying, Is.EqualTo("(0,8,0) (0,8,0) (0,8,0) False back (1,0,0) False"));

            GameObject u = At("u", Origin);
            FeedbackPlayer q = Delayed(out FeedbackScheduler qs, 0.25f, To(u, 0.5f));
            q.Play(Forward);
            Ticked(qs, 8, 0.125f);
            Turn(q, false);
            qs.Tick(0.125f);
            Turn(q, true);
            q.Seek(0.5f);
            string sought = V(u);
            qs.Tick(0.125f);
            string next = V(u);
            Ticked(qs, 4, 0.125f);
            string end = V(u) + " " + q.IsPlaying;
            Assert.That(sought + " " + next + " " + end, Is.EqualTo("(0.5,4,0) (0.25,6,0) (0,8,0) False"));

            GameObject w = At("w", Origin);
            FeedbackPlayer r = Delayed(out FeedbackScheduler rs, 0f, To(w, 0.5f), Act("z", 0f, 0.5f));
            bool once = true;
            Action<PlayerTestBinding, string, int> mark = PlayerTestBinding.Hook;
            PlayerTestBinding.Hook = (b, what, record) =>
            {
                mark(b, what, record);
                if (once && what == "Execute" && b.Item.m_Label == "z" && !b.LastBegin.Reverse)
                {
                    once = false;
                    r.Seek(0f);
                }
            };
            r.Play(Forward);
            rs.Tick(0.625f);
            string first = V(w) + " " + Place(r);
            rs.Tick(0.25f);
            string mid = V(w);
            Ticked(rs, 4, 0.125f);
            string last = V(w) + " " + r.IsPlaying;
            Turn(r, false);
            Ticked(rs, 6, 0.125f);
            Assert.That(first + " " + mid + " " + last + " back " + V(w), Is.EqualTo("(1,0,0) 0 (0.5,4,0) (0,8,0) False back (1,0,0)"));
        }

        // ---- a turn asked in the play's last events

        static PlayerTestAction After(PlayerTestAction action)
        {
            action.m_Timing.m_Rule = StartRule.AfterPrevious;
            return action;
        }

        static PlayerTestAction Only(PlayerTestAction action, DirectionCondition condition)
        {
            action.m_Conditions.m_Direction = condition;
            return action;
        }

        // the Begin · End · Execute lines of the log, in order
        static string TakeCalls()
        {
            string text = string.Join(" ", PlayerTestBinding.Log.Where(l => l.Contains(".Begin") || l.Contains(".End") || l.Contains(".Execute")));
            PlayerTestBinding.Log.Clear();
            return text;
        }

        // the turn of `p` asked once, inside the binding call `what` of the item `label` going `reverse`
        static void TurnInside(FeedbackPlayer p, string label, string what, bool reverse, bool forward, Action before = null)
        {
            bool asked = false;
            Action<PlayerTestBinding, string, int> mark = PlayerTestBinding.Hook;
            PlayerTestBinding.Hook = (b, call, record) =>
            {
                mark(b, call, record);
                if (asked || call != what || b.Item.m_Label != label || b.LastBegin.Reverse != reverse)
                    return;
                asked = true;
                before?.Invoke();
                s_Asked = Turn(p, forward);
            };
        }

        // a turn from the last pass's events — the end instant forward, the first item's end backward — turns the same play at
        // its end: it goes back to the other end, the items run again the other way, one Played and one Completed
        [Test]
        public void PTN20_ATurnInTheLastPassGoesBack([Values(0f, 0.3f)] float delay)
        {
            var rows = new List<string>();
            foreach (bool back in new[] { false, true })
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = PlayerOn(s, "last", Act("a", 0.5f), After(Act("x", 0f)));
                p.m_StartDelay = delay;
                s_Asked = default;
                if (back)
                    TurnInside(p, "a", "End", true, true);
                else
                    TurnInside(p, "x", "Execute", false, false);
                PlaybackHandle h = p.Play(back ? Backward : Forward);
                for (int k = 0; k < 40 && p.IsPlaying; k++)
                    s.Tick(0.05f);
                rows.Add((s_Asked == h) + " " + TakeCalls() + " | " + TakeEvents());
            }
            Assert.That(rows, Is.EqualTo(new[]
            {
                "True a.Begin#0 a.End:Completed x.Execute#0 x.Execute#0~ a.Begin#0~ a.End:Completed | last.Played last.Completed",
                "True x.Execute#0~ a.Begin#0~ a.End:Completed a.Begin#0 a.End:Completed x.Execute#0 | last.Played last.Completed",
            }));
        }

        // a Pause and a turn in the last event: the play stays, paused and turned, and goes back after Resume
        [Test]
        public void PTN20b_APauseAndATurnInTheLastPass()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = PlayerOn(s, "last", Act("a", 0.5f), After(Act("x", 0f)));
            TurnInside(p, "x", "Execute", false, false, () => p.Pause());
            PlaybackHandle h = p.Play();
            for (int k = 0; k < 10; k++)
                s.Tick(0.05f);
            for (int k = 0; k < 3; k++)
                s.Tick(0.05f);
            string paused = p.IsPlaying + " " + p.HasPaused + " " + p.Position(h) + " " + TakeCalls();
            p.Resume();
            for (int k = 0; k < 20 && p.IsPlaying; k++)
                s.Tick(0.05f);
            Assert.That((paused, TakeCalls(), TakeEvents()),
                Is.EqualTo(("True True 500001 a.Begin#0 a.End:Completed x.Execute#0", "x.Execute#0~ a.Begin#0~ a.End:Completed", "last.Played last.Completed")));
        }

        // a play turned forward at its start reads 0 until its next pass — its cursor stands 1 µs before 0, where the instants
        // at 0 count as undone
        [Test]
        public void PTN21_APlayTurnedForwardAtItsStartIsNotBelowZero()
        {
            var rows = new List<string>();
            foreach (float delay in new[] { 0f, 0.3f })
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = PlayerOn(s, "start", Act("a", 0.5f), After(Act("x", 0f)));
                p.m_StartDelay = delay;
                s_Asked = default;
                TurnInside(p, "a", "End", true, true);
                PlaybackHandle h = p.Play(Backward);
                var places = new List<string>();
                for (int k = 0; k < 40 && p.IsPlaying && places.Count < 3; k++)
                {
                    s.Tick(0.05f);
                    if (s_Asked != default)
                        places.Add(p.Position(h) + "/" + p.Runtime.Position(0));
                }
                rows.Add(string.Join(" ", places));
            }
            Assert.That(rows, Is.EqualTo(new[] { "0/0 49999/49999 99999/99999", "299999/299999 349999/349999 399999/399999" }));
        }

        // a play turned at its end waits for no start delay left by a backward play before it on the record, nor by its own
        // backward start: it goes back from the next tick
        [Test]
        public void PTN22_ATurnAtTheEndKeepsNoStaleDelay()
        {
            var rows = new List<string>();
            foreach (int which in new[] { 0, 1, 2 })
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = PlayerOn(s, "stale", Act("a", 0.5f), After(Act("x", 0f)));
                p.m_StartDelay = 0.3f;
                PlaybackHandle h;
                if (which == 2)
                {
                    h = p.Play(Backward);
                    s.Tick(0.1f);
                    Turn(p, true);
                    p.Seek(0.75f);
                }
                else
                {
                    PlaybackHandle before = p.Play(Backward);
                    s.Tick(0.1f);
                    if (which == 0)
                        p.Stop(before);
                    else
                    {
                        Turn(p, true);
                        for (int k = 0; k < 10 && p.IsPlaying; k++)
                            s.Tick(0.05f);
                    }
                    h = p.Play(Forward);
                }
                s_Asked = default;
                TurnInside(p, "x", "Execute", false, false);
                var places = new List<long>();
                for (int k = 0; k < 40 && p.IsPlaying && places.Count < 3; k++)
                {
                    s.Tick(0.05f);
                    if (s_Asked != default)
                        places.Add(p.Position(h));
                }
                rows.Add(string.Join(" ", places));
            }
            Assert.That(rows, Is.EqualTo(new[] { "800001 750001 700001", "800001 750001 700001", "800001 750001 700001" }));
        }

        // a Reverse Only instant does its act in a backward play's own pass and is undone passed the other way — turned or a
        // Seek toward the end; a Forward Only instant mirrors it
        [Test]
        public void PTN23_AOneDirectionInstantPassedTheOtherWayIsUndone()
        {
            var rows = new List<string>();
            foreach (string row in new[] { "r turn", "r seek", "f turn", "f seek" })
            {
                bool r = row[0] == 'r';
                FeedbackScheduler s = Sched();
                string label = r ? "r" : "f";
                FeedbackPlayer p = PlayerOn(s, "one", Act("a", 0.5f), Only(Act(label, 0f, 0.25f), r ? DirectionCondition.ReverseOnly : DirectionCondition.ForwardOnly));
                p.Play(r ? Backward : Forward);
                s.Tick(0.3f);
                if (row.EndsWith("turn", StringComparison.Ordinal))
                    Turn(p, r);
                else
                    p.Seek(r ? 0.4f : 0.1f);
                for (int k = 0; k < 4 && p.IsPlaying; k++)
                    s.Tick(0.05f);
                rows.Add(string.Join(" ", PlayerTestBinding.Log.Where(l => l.StartsWith(label + ".Execute", StringComparison.Ordinal))));
                PlayerTestBinding.Log.Clear();
            }
            Assert.That(rows, Is.EqualTo(new[] { "r.Execute#0 r.Execute#0~", "r.Execute#0 r.Execute#0~ r.Execute#0", "f.Execute#0 f.Execute#0~", "f.Execute#0 f.Execute#0~ f.Execute#0" }));
        }

        // the round trips: an instant at the end of the way that turns the play — the list's end forward, a Reverse Only one at
        // its start backward — runs once, and the play goes back and completes once
        [Test]
        public void PTN24_TheRoundTrips()
        {
            var rows = new List<string>();
            foreach (bool back in new[] { false, true })
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = back
                    ? PlayerOn(s, "trip", Only(Act("r", 0f), DirectionCondition.ReverseOnly), Act("a", 0.5f))
                    : PlayerOn(s, "trip", Act("a", 0.5f), After(Act("x", 0f)));
                string label = back ? "r" : "x";
                int acts = 0;
                Action<PlayerTestBinding, string, int> mark = PlayerTestBinding.Hook;
                PlayerTestBinding.Hook = (b, call, record) =>
                {
                    mark(b, call, record);
                    if (call == "Execute" && b.Item.m_Label == label && !b.LastBegin.Reverse && ++acts < 4)
                        Turn(p, back);
                };
                p.Play(back ? Backward : Forward);
                for (int k = 0; k < 40 && p.IsPlaying; k++)
                    s.Tick(0.05f);
                PlayerTestBinding.Hook = mark;
                rows.Add(acts + " " + TakeCalls() + " | " + TakeEvents());
            }
            Assert.That(rows, Is.EqualTo(new[]
            {
                "1 a.Begin#0 a.End:Completed x.Execute#0 x.Execute#0~ a.Begin#0~ a.End:Completed | trip.Played trip.Completed",
                "1 a.Begin#0~ a.End:Completed r.Execute#0 r.Execute#0~ a.Begin#0 a.End:Completed | trip.Played trip.Completed",
            }));
        }

        // other calls in the last event with a turn: Seek to the end and Skip end the play, a Seek before the end turns it there,
        // Stop ends it and the turn is a new play
        [Test]
        public void PTN25_OtherCallsWithATurnInTheLastPass()
        {
            var rows = new List<string>();
            foreach (string row in new[] { "Seek(end)", "Skip", "Seek(0.2)", "Stop" })
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = PlayerOn(s, "other", Act("a", 0.5f), After(Act("x", 0f)));
                s_Asked = default;
                TurnInside(p, "x", "Execute", false, false, () =>
                {
                    if (row == "Seek(end)") p.Seek(0.5f);
                    else if (row == "Skip") p.Skip();
                    else if (row == "Seek(0.2)") p.Seek(0.2f);
                    else p.Stop();
                });
                PlaybackHandle h = p.Play();
                var places = new List<long>();
                for (int k = 0; k < 40 && p.IsPlaying; k++)
                {
                    s.Tick(0.05f);
                    if (s_Asked != default && places.Count < 2)
                        places.Add(p.Position(h));
                }
                rows.Add((s_Asked == h) + " " + string.Join(" ", places) + " | " + TakeEvents());
                PlayerTestBinding.Log.Clear();
            }
            Assert.That(rows, Is.EqualTo(new[]
            {
                "True -1 | other.Played other.Completed",
                "True -1 | other.Played other.Completed",
                "True 200001 150001 | other.Played other.Completed",
                "False -1 -1 | other.Played other.Stopped other.Played other.Completed",
            }));
        }

        // a list that ends at its start turned in its own t = 0 events: Played in the Play, the instant undone and Completed on
        // the next tick
        [Test]
        public void PTN26_ATurnInTheBeginWindowOfAListThatEndsAtItsStart()
        {
            var rows = new List<string>();
            foreach (bool back in new[] { false, true })
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = PlayerOn(s, "zero", Act("x", 0f));
                s_Asked = default;
                TurnInside(p, "x", "Execute", back, back);
                PlaybackHandle h = p.Play(back ? Backward : Forward);
                string inPlay = (s_Asked == h) + " " + p.IsPlaying + " " + p.Position(h) + " " + TakeEvents();
                s.Tick(0.05f);
                rows.Add(inPlay + " | " + TakeCalls() + " | " + TakeEvents() + " " + p.IsPlaying);
            }
            Assert.That(rows, Is.EqualTo(new[]
            {
                "True True 1 zero.Played | x.Execute#0 x.Execute#0~ | zero.Completed False",
                "True True 0 zero.Played | x.Execute#0~ x.Execute#0 | zero.Completed False",
            }));
        }

        static readonly List<PlaybackHandle> s_Turns = new List<PlaybackHandle>();

        // the turns `turns` (F · B) of `p` back to back, asked inside the Execute of the item `label` going `reverse`
        static void TurnsInside(FeedbackPlayer p, string label, bool reverse, string turns)
        {
            bool asked = false;
            Action<PlayerTestBinding, string, int> mark = PlayerTestBinding.Hook;
            PlayerTestBinding.Hook = (b, call, record) =>
            {
                mark(b, call, record);
                if (asked || call != "Execute" || b.Item.m_Label != label || b.LastBegin.Reverse != reverse)
                    return;
                asked = true;
                TurnsNow(p, turns);
            };
        }

        static void TurnsNow(FeedbackPlayer p, string turns)
        {
            foreach (char c in turns)
                s_Turns.Add(Turn(p, c == 'F'));
        }

        // which play each turn gave (same · new · none), the place from now to the play's end at 0.1 s ticks, the events and
        // the warnings
        static string TurnsRow(FeedbackScheduler s, FeedbackPlayer p, GameObject t, PlaybackHandle h)
        {
            var places = new List<string> { V(t) };
            for (int k = 0; k < 12 && p.IsPlaying; k++)
            {
                s.Tick(0.1f);
                places.Add(V(t));
            }
            string turns = string.Join(" ", s_Turns.Select(g => g == default ? "none" : g == h ? "same" : "new"));
            s_Turns.Clear();
            return turns + " | " + string.Join(" ", places) + " | " + TakeEvents() + " | " + s_Warnings.Count;
        }

        // a turn back after a turn the play waits to make — asked in the same event (a tick's, the Play's t = 0, the last
        // pass's, a backward play's, a Variant's, a nested player's) or in a Seek's sweep — takes that one back: the same play
        // goes on as it was. One turn, two the same way and turns outside the events go on as before
        [Test]
        public void PTN27_ATurnBackTakesBackTheWaitingTurn()
        {
            var rows = new List<string>();
            foreach (string row in new[] { "tick", "overlap", "begin", "last", "backward", "three", "variant", "nested", "seek F", "seek BF", "once", "twice", "last once", "begin once", "seek", "seek B", "seek BFB", "nested tick" })
            {
                FeedbackScheduler s = Sched();
                GameObject t = At("t", Origin);
                s_Turns.Clear();
                s_Warnings.Clear();
                bool backward = row == "backward";
                float at = row.StartsWith("begin", StringComparison.Ordinal) ? 0f : row.StartsWith("last", StringComparison.Ordinal) ? 0.4f : backward ? 0.25f : 0.15f;
                FeedbackPlayer p;
                PlaybackHandle h;
                if (row == "variant")
                {
                    var variant = ScriptableObject.CreateInstance<FeedbackPreset>();
                    m_Made.Add(variant);
                    variant.m_Alias = "v";
                    variant.m_Items.Add(Item<PositionItem>(Tween(TweenMode.To, Goal, Goal), new TargetRef { m_Mode = TargetRefMode.Anchor, m_Path = "", m_Anchor = "t" }, 0.4f));
                    variant.m_Items.Add(Act("i", 0f, at));
                    p = PlayerOn(s, "p", Act("own", 0.4f));
                    p.SetAnchor("t", t);
                    p.m_Variants.Add(variant);
                    TurnsInside(p, "i", false, "BF");
                    h = p.Play(PlayArgs.Default.WithPreset(variant).WithDirection(PlayerDirection.Forward));
                }
                else if (row.StartsWith("nested", StringComparison.Ordinal))
                {
                    FeedbackPlayer child = PlayerOn(s, "c", Act("c", 0f, row == "nested" ? 0f : 0.05f));
                    var nested = new PlayPlayerItem { m_Player = child };
                    nested.m_Play.m_Wait = false;
                    nested.m_Timing.m_Delay = at;
                    p = PlayerOn(s, "p", To(t), nested);
                    TurnsInside(p, "c", false, "BF");
                    h = p.Play(Forward);
                }
                else
                {
                    p = PlayerOn(s, "p", To(t), Act("i", 0f, at));
                    if (row == "overlap")
                        p.m_Policy = PlayPolicy.Overlap;
                    bool seek = row.StartsWith("seek", StringComparison.Ordinal);
                    string turns = seek || row.EndsWith("once", StringComparison.Ordinal) ? "B" : backward ? "FB" : row == "three" ? "BFB" : row == "twice" ? "BB" : "BF";
                    TurnsInside(p, "i", backward, turns);
                    h = p.Play(backward ? Backward : Forward);
                    if (seek)
                    {
                        s.Tick(0.1f);
                        p.Seek(0.3f);
                        TurnsNow(p, row.Length > 4 ? row.Substring(5) : "");
                    }
                }
                rows.Add(row + ": " + TurnsRow(s, p, t, h));
                PlayerTestBinding.Log.Clear();
            }
            Assert.That(rows, Is.EqualTo(new[]
            {
                "tick: same same | (1,0,0) (0.75,2,0) (0.5,4,0) (0.25,6,0) (0,8,0) | p.Played p.Completed | 0",
                "overlap: same same | (1,0,0) (0.75,2,0) (0.5,4,0) (0.25,6,0) (0,8,0) | p.Played p.Completed | 0",
                "begin: same same | (1,0,0) (0.75,2,0) (0.5,4,0) (0.25,6,0) (0,8,0) | p.Played p.Completed | 0",
                "last: same same | (1,0,0) (0.75,2,0) (0.5,4,0) (0.25,6,0) (0,8,0) | p.Played p.Completed | 0",
                "backward: same same | (0,8,0) (0.25,6,0) (0.5,4,0) (0.75,2,0) (1,0,0) | p.Played p.Completed | 0",
                "three: same same same | (1,0,0) (0.75,2,0) (0.5,4,0) (0.75,2,0) (1,0,0) (1,0,0) | p.Played p.Completed | 0",
                "variant: same same | (1,0,0) (0.75,2,0) (0.5,4,0) (0.25,6,0) (0,8,0) | p.Played p.Completed | 0",
                "nested: same same | (1,0,0) (0.75,2,0) (0.5,4,0) (0.25,6,0) (0,8,0) | p.Played c.Played c.Completed p.Completed | 0",
                "seek F: same same | (0.25,6,0) (0,8,0) | p.Played p.Completed | 0",
                "seek BF: same same same | (0.25,6,0) (0,8,0) | p.Played p.Completed | 0",
                "once: same | (1,0,0) (0.75,2,0) (0.5,4,0) (0.75,2,0) (1,0,0) (1,0,0) | p.Played p.Completed | 0",
                "twice: same same | (1,0,0) (0.75,2,0) (0.5,4,0) (0.75,2,0) (1,0,0) (1,0,0) | p.Played p.Completed | 0",
                "last once: same | (1,0,0) (0.75,2,0) (0.5,4,0) (0.25,6,0) (0,8,0) (0.25,6,0) (0.5,4,0) (0.75,2,0) (1,0,0) (1,0,0) | p.Played p.Completed | 0",
                "begin once: same | (1,0,0) (1,0,0) | p.Played p.Completed | 0",
                "seek: same | (0.25,6,0) (0,8,0) (0.25,6,0) (0.5,4,0) (0.75,2,0) (1,0,0) (1,0,0) | p.Played p.Completed | 0",
                "seek B: same same | (0.25,6,0) (0.5,4,0) (0.75,2,0) (1,0,0) (1,0,0) | p.Played p.Completed | 0",
                "seek BFB: same same same same | (0.25,6,0) (0.5,4,0) (0.75,2,0) (1,0,0) (1,0,0) | p.Played p.Completed | 0",
                "nested tick: same same | (1,0,0) (0.75,2,0) (0.5,4,0) (0.25,6,0) (0,8,0) | p.Played c.Played c.Completed p.Completed | 0",
            }));
        }
    }
}
