using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // each item's own play direction on a test scheduler ticked by hand (0.05 s, seed 7). A turned item is its own reverse play
    // bit for bit
    public sealed class DirectionTests
    {
        const float Step = 0.05f;
        static double s_Now;
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();

        [SetUp]
        public void SetUp()
        {
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerTestBinding.Hook = null;
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
            PlayerTestBinding.Reset();
        }

        static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

        static string B(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };

        static PlayArgs Fwd => PlayArgs.Default.WithSeed(7u).WithDirection(PlayerDirection.Forward);

        static PlayArgs Bwd => PlayArgs.Default.WithSeed(7u).WithDirection(PlayerDirection.Backward);

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

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            FeedbackPlayer p = Go("Player").AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            if (s != null)
                p.Bind(s);
            p.EnsureRuntime();
            m_Players.Add(p);
            return p;
        }

        static T On<T>(T item, Object target, FeedbackMotion motion, float duration, ItemPlayDirection dir) where T : TargetItem
        {
            item.m_Target = Direct(target);
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            item.m_PlayDirection = dir;
            return item;
        }

        static TweenMotion Tween(TweenMode mode, float v)
        {
            TweenParams p = TweenParams.Default;
            p.m_Mode = mode;
            if (mode == TweenMode.From)
                p.m_From = new Vector4(v, 0f, 0f, 0f);
            else
                p.m_To = new Vector4(v, 0f, 0f, 0f);
            return new TweenMotion { m_Params = p };
        }

        static SpringMotion SpringTo(float v)
        {
            var m = new SpringMotion();
            m.m_Params.m_Mode = SpringMode.MoveTo;
            m.m_Params.m_Value = new Vector4(v, 0f, 0f, 0f);
            return m;
        }

        static SetMotion SetTo(float v) => new SetMotion { m_Params = new SetParams { m_Value = new Vector4(v, 0f, 0f, 0f) } };

        static string Pos(GameObject g) => B(g.transform.localPosition.x) + B(g.transform.localPosition.y) + B(g.transform.localPosition.z);

        static string PosX(GameObject g) => F(g.transform.localPosition.x);

        static Gradient RedBlue()
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        // the record's turn, read from the runtime the scheduler runs
        static bool TurnedAt(FeedbackScheduler s, FeedbackPlayer p, int item)
        {
            for (int i = 0; i < s.SlotCount; i++)
            {
                if (s.TryGetActive(i, out ActivePlayback a) && a.Runner is PlayerRuntime r && ReferenceEquals(r, p.Runtime))
                    return r.Record(a.State).Turned[item];
            }
            return false;
        }

        // a family: the item on a fresh target and its reading. Punch's vibrato 3.7 — the default 5 is at its zeros on every tick
        sealed class Family
        {
            internal string Name;
            internal Func<GameObject, ItemPlayDirection, FeedbackItem> Make;
            internal Func<GameObject, string> Read;
            internal Action<GameObject> Init;
        }

        List<string> Run(Family f, ItemPlayDirection dir, PlayArgs args)
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("t");
            f.Init?.Invoke(t);
            FeedbackPlayer p = Player(s, f.Make(t, dir));
            var seen = new List<string>();
            p.Play(in args);
            for (int i = 0; i < 13; i++)
            {
                s.Tick(Step);
                seen.Add(f.Read(t));
            }
            return seen;
        }

        static IEnumerable<Family> Families()
        {
            Func<GameObject, string> pos = Pos;
            yield return new Family { Name = "To", Read = pos, Make = (t, d) => On(new PositionItem(), t, Tween(TweenMode.To, 5f), 0.5f, d) };
            yield return new Family { Name = "From", Read = pos, Make = (t, d) => On(new PositionItem(), t, Tween(TweenMode.From, 5f), 0.5f, d) };
            yield return new Family { Name = "By", Read = pos, Make = (t, d) => On(new PositionItem(), t, Tween(TweenMode.By, 2f), 0.5f, d) };
            yield return new Family { Name = "Punch", Read = pos, Make = (t, d) => On(new PositionItem(), t, new PunchMotion { m_Params = new PunchParams { m_Amplitude = Vector4.one, m_Vibrato = 3.7f, m_Decay = 1f } }, 0.5f, d) };
            yield return new Family { Name = "Shake", Read = pos, Make = (t, d) => On(new PositionItem(), t, new ShakeMotion(), 0.5f, d) };
            yield return new Family { Name = "Wiggle", Read = pos, Make = (t, d) => On(new PositionItem(), t, new WiggleMotion(), 0.5f, d) };
            yield return new Family { Name = "Oscillate", Read = pos, Make = (t, d) => On(new PositionItem(), t, new OscillateMotion { m_Params = new OscillateParams { m_Amplitude = Vector4.one, m_Frequency = 1f, m_Phase = 0.1f } }, 0.5f, d) };
            yield return new Family { Name = "Spring", Read = pos, Make = (t, d) => On(new PositionItem(), t, SpringTo(5f), 0.5f, d) };
            yield return new Family
            {
                Name = "Flash", Init = t => t.AddComponent<Light>().intensity = 3f, Read = t => B(t.GetComponent<Light>().intensity),
                Make = (t, d) => On(new LightIntensityItem(), t.GetComponent<Light>(), new FlashMotion(), 0.5f, d),
            };
            yield return new Family
            {
                Name = "Gradient", Init = t => t.AddComponent<Light>().color = Color.white, Read = t => t.GetComponent<Light>().color.ToString("F6"),
                Make = (t, d) => On(new LightColorItem(), t.GetComponent<Light>(), new GradientMotion { m_Params = new GradientParams { m_Gradient = RedBlue(), m_Mode = GradientWrite.Persist } }, 0.5f, d),
            };
            yield return new Family { Name = "Blink", Init = t => t.SetActive(false), Read = t => t.activeSelf ? "1" : "0", Make = (t, d) => On(new SetActiveItem(), t, new BlinkMotion(), 0.5f, d) };
        }

        // ---- the data — the values · the default · the saved field · an old asset without it

        [Test]
        public void DQ01a_TheData()
        {
            Assert.That(Enum.GetValues(typeof(ItemPlayDirection)).Cast<ItemPlayDirection>().Select(v => v + "=" + (int)v), Is.EqualTo(new[] { "FollowPlayer=0", "Forward=1", "Backward=2", "Opposite=3" }));
            Assert.That(new PositionItem().m_PlayDirection, Is.EqualTo(ItemPlayDirection.FollowPlayer));
            FeedbackPlayer p = Player(null, On(new PositionItem(), Go("t"), Tween(TweenMode.To, 5f), 0.5f, ItemPlayDirection.Backward));
            string json = EditorJsonUtility.ToJson(p);
            Assert.That(json, Does.Contain("\"m_PlayDirection\":2"));
            FeedbackPlayer old = Player(null);
            EditorJsonUtility.FromJsonOverwrite(json.Replace("\"m_PlayDirection\":2,", ""), old);
            Assert.That(((EffectItem)old.m_Items.Single()).m_PlayDirection, Is.EqualTo(ItemPlayDirection.FollowPlayer), "an old asset plays as before");
        }

        // ---- the table — the record's turn under a forward play, a backward one and a toggle's second

        [Test]
        public void DQ02a_TheTable()
        {
            var want = new Dictionary<ItemPlayDirection, string>
            {
                { ItemPlayDirection.FollowPlayer, "···" }, { ItemPlayDirection.Forward, "·↺↺" }, { ItemPlayDirection.Backward, "↺··" }, { ItemPlayDirection.Opposite, "↺↺↺" },
            };
            foreach (KeyValuePair<ItemPlayDirection, string> kv in want)
            {
                string got = "";
                foreach (string play in new[] { "F", "B", "T2" })
                {
                    FeedbackScheduler s = Sched();
                    FeedbackPlayer p = Player(s, On(new PositionItem(), Go("t"), Tween(TweenMode.To, 5f), 1f, kv.Key));
                    if (play == "T2")
                    {
                        p.m_Direction = PlayerDirection.Toggle;
                        p.Play();
                        p.Stop();
                        p.Play();
                    }
                    else
                        p.Play(play == "F" ? Fwd : Bwd);
                    got += TurnedAt(s, p, 0) ? "↺" : "·";
                }
                Assert.That(got, Is.EqualTo(kv.Value), kv.Key.ToString());
            }
        }

        // ---- a turned item is its own reverse play — bit for bit; the forward and backward columns differ

        [Test]
        public void DQ03a_ATurnedItemIsItsOwnReversePlay()
        {
            var fails = new List<string>();
            foreach (Family f in Families())
            {
                List<string> forward = Run(f, ItemPlayDirection.FollowPlayer, Fwd);
                List<string> backward = Run(f, ItemPlayDirection.FollowPlayer, Bwd);
                if (forward.SequenceEqual(backward))
                    fails.Add(f.Name + " — the columns are the same (vacuous)");
                if (!Run(f, ItemPlayDirection.Backward, Fwd).SequenceEqual(backward))
                    fails.Add(f.Name + " — Backward in a forward play ≠ its backward play");
            }
            Assert.That(fails, Is.Empty, string.Join(" · ", fails));
        }

        // ---- Forward in a backward play · Opposite both ways

        [Test]
        public void DQ04a_TheOtherWays()
        {
            var fails = new List<string>();
            foreach (Family f in Families().Where(x => x.Name == "To" || x.Name == "Shake" || x.Name == "Spring"))
            {
                List<string> forward = Run(f, ItemPlayDirection.FollowPlayer, Fwd);
                List<string> backward = Run(f, ItemPlayDirection.FollowPlayer, Bwd);
                if (!Run(f, ItemPlayDirection.Forward, Bwd).SequenceEqual(forward))
                    fails.Add(f.Name + " — Forward in a backward play");
                if (!Run(f, ItemPlayDirection.Opposite, Fwd).SequenceEqual(backward))
                    fails.Add(f.Name + " — Opposite forward");
                if (!Run(f, ItemPlayDirection.Opposite, Bwd).SequenceEqual(forward))
                    fails.Add(f.Name + " — Opposite backward");
            }
            Assert.That(fails, Is.Empty, string.Join(" · ", fails));
        }

        // ---- instants — a Set's reverse writes the origin its forward act took; a UnityEvent is not called backward

        string Sequence(ItemPlayDirection d, PlayerDirection[] plays, DirectionCondition condition = DirectionCondition.Always)
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("t");
            int fired = 0;
            var set = On(new SetActiveItem(), t, SetTo(0f), 0f, d);
            set.m_Conditions.m_Direction = condition;
            var ue = new UnityEventItem { m_PlayDirection = d };
            ue.m_Conditions.m_Direction = condition;
            ue.m_Event.AddListener(() => fired++);
            FeedbackPlayer p = Player(s, set, ue);
            var line = new List<string>();
            foreach (PlayerDirection pd in plays)
            {
                t.SetActive(true);
                p.Play(PlayArgs.Default.WithDirection(pd));
                s.Tick(Step);
                line.Add((pd == PlayerDirection.Forward ? "F:" : "B:") + (t.activeSelf ? "on" : "off") + "/" + fired);
            }
            return string.Join(" ", line);
        }

        [Test]
        public void DQ05a_Instants()
        {
            PlayerDirection f = PlayerDirection.Forward, b = PlayerDirection.Backward;
            Assert.That(Sequence(ItemPlayDirection.Backward, new[] { f, b, f }), Is.EqualTo("F:on/0 B:on/0 F:on/0"), "Backward — no play runs it");
            Assert.That(Sequence(ItemPlayDirection.Forward, new[] { b, b }), Is.EqualTo("B:off/1 B:off/2"), "Forward — its forward act in a backward play");
            Assert.That(Sequence(ItemPlayDirection.Opposite, new[] { b, f, b, f }), Is.EqualTo("B:off/1 F:on/1 B:off/2 F:on/2"), "Opposite");
            Assert.That(Sequence(ItemPlayDirection.Backward, new[] { f, b }, DirectionCondition.ReverseOnly), Is.EqualTo("F:on/0 B:off/1"), "a direction condition decides");
            foreach (ItemPlayDirection d in new[] { ItemPlayDirection.FollowPlayer, ItemPlayDirection.Backward })
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer p = Player(s, new HitStopItem { m_FreezeDuration = 0.2f, m_PlayDirection = d });
                p.Play(Fwd);
                s.Tick(Step);
                Assert.That(s.Context.TimeScale.Count, Is.EqualTo(d == ItemPlayDirection.Backward ? 0 : 1), "a hit stop's request — " + d);
            }
        }

        // ---- the unread — a direction condition · an item its binding ends (as the program wrote it) · an endless item

        List<string> Calls(FeedbackItem item)
        {
            var calls = new List<string>();
            PlayerTestBinding.Hook = (b, call, record) =>
            {
                if (call == "Begin" || call == "Execute")
                    calls.Add(call + (b.LastBegin.Reverse ? "<" : ">"));
                else if (call == "Evaluate")
                    calls.Add("T" + F(b.LastTime.T) + "/" + b.LastTime.Seconds.ToString("0.##", CultureInfo.InvariantCulture));
            };
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, item);
            p.Play(Fwd);
            for (int i = 0; i < 3; i++)
                s.Tick(Step);
            PlayerTestBinding.Hook = null;
            return calls;
        }

        string Open(float limit, bool waitAfterPrepare)
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("child target");
            FeedbackPlayer child = Player(s, On(new PositionItem(), t, Tween(TweenMode.To, 5f), 0.2f, ItemPlayDirection.FollowPlayer));
            var play = new PlayPlayerItem { m_Player = child, m_PlayDirection = ItemPlayDirection.Backward };
            play.m_Play.m_Wait = !waitAfterPrepare;
            play.m_Play.m_Timeout = limit;
            play.m_Play.m_PassDirection = true;
            GameObject after = Go("after");
            var next = On(new PositionItem(), after, Tween(TweenMode.To, 1f), 0.1f, ItemPlayDirection.FollowPlayer);
            next.m_Timing.m_Rule = StartRule.AfterPrevious;
            FeedbackPlayer parent = Player(s, play, next);
            if (waitAfterPrepare)
                play.m_Play.m_Wait = true;
            parent.Play(Fwd);
            var xs = new List<string>();
            for (int i = 0; i < 6; i++)
            {
                s.Tick(Step);
                xs.Add(PosX(t) + "/" + PosX(after));
            }
            return string.Join(",", xs);
        }

        [Test]
        public void DQ06a_TheUnread()
        {
            {
                FeedbackScheduler s = Sched();
                GameObject t = Go("t");
                PositionItem item = On(new PositionItem(), t, Tween(TweenMode.To, 5f), 0.5f, ItemPlayDirection.Backward);
                item.m_Conditions.m_Direction = DirectionCondition.ForwardOnly;
                FeedbackPlayer p = Player(s, item);
                p.Play(Fwd);
                for (int i = 0; i < 4; i++)
                    s.Tick(Step);
                Assert.That(PosX(t), Is.EqualTo("2"), "ForwardOnly + Backward follows the play");
            }
            // a waiting play — with or without a time limit — is open: its child plays forward, the item after waits for it
            Assert.That(Open(0f, false), Does.StartWith("1.25/0,2.5/0,3.75/0,5/0,5/0.5"));
            Assert.That(Open(0.3f, false), Does.StartWith("1.25/0,2.5/0,3.75/0,5/0,5/0.5"));
            // the program decides: a wait switched on after the prepare is still the instant it was prepared as
            Assert.That(Open(0f, true), Does.StartWith("3.75/"));
            List<string> endless = Calls(new PlayerTestAction { m_Label = "a", m_Length = float.PositiveInfinity, m_PlayDirection = ItemPlayDirection.Backward });
            Assert.That(endless.Take(4), Is.EqualTo(new[] { "Begin>", "T0/0", "T0/0.05", "T0/0.1" }), "an endless item has no other end");
            Assert.That(Calls(new PlayerTestAction { m_Label = "a", m_Length = 0.2f, m_PlayDirection = ItemPlayDirection.Backward }).Take(3), Is.EqualTo(new[] { "Begin<", "T1/0.2", "T0.75/0.15" }));
            {
                FeedbackScheduler s = Sched();
                GameObject t = Go("t");
                PositionItem item = On(new PositionItem(), t, Tween(TweenMode.To, 5f), 0.2f, ItemPlayDirection.Backward);
                item.m_Timing.m_RepeatCount = 2;
                FeedbackPlayer p = Player(s, item);
                p.Play(Fwd);
                var xs = new List<string>();
                for (int i = 0; i < 6; i++)
                {
                    s.Tick(Step);
                    xs.Add(PosX(t));
                }
                Assert.That(string.Join(",", xs), Is.EqualTo("3.75,2.5,1.25,5,3.75,2.5"), "each run of a repeat turns");
            }
        }

        // ---- the first frame — the start of the progress way; two items of two ways on one target leave the turned one's end
        // (documented)

        string Initial(PlayerDirection setting, ItemPlayDirection d, DirectionCondition c = DirectionCondition.Always)
        {
            GameObject t = Go("t");
            t.transform.localPosition = new Vector3(1f, 0f, 0f);
            PositionItem item = On(new PositionItem(), t, Tween(TweenMode.To, 5f), 0.5f, d);
            item.m_Conditions.m_Direction = c;
            Prepared(setting, item);
            return PosX(t);
        }

        void Prepared(PlayerDirection setting, params FeedbackItem[] items)
        {
            FeedbackPlayer p = Go("Player").AddComponent<FeedbackPlayer>();
            m_Players.Add(p);
            p.m_Items.AddRange(items);
            p.m_ApplyInitialState = true;
            p.m_Direction = setting;
            p.Bind(Sched());
            p.EnsureRuntime();
        }

        [Test]
        public void DQ07a_TheFirstFrame()
        {
            Assert.That(Initial(PlayerDirection.Forward, ItemPlayDirection.Backward), Is.EqualTo("5"));
            Assert.That(Initial(PlayerDirection.Backward, ItemPlayDirection.Forward), Is.EqualTo("1"));
            Assert.That(Initial(PlayerDirection.Forward, ItemPlayDirection.Opposite), Is.EqualTo("5"));
            Assert.That(Initial(PlayerDirection.Backward, ItemPlayDirection.Opposite), Is.EqualTo("1"));
            Assert.That(Initial(PlayerDirection.Forward, ItemPlayDirection.Backward, DirectionCondition.ReverseOnly), Is.EqualTo("1"), "a direction condition keeps the rule");
            GameObject t = Go("t");
            PositionItem a = On(new PositionItem(), t, Tween(TweenMode.To, 5f), 0.5f, ItemPlayDirection.FollowPlayer);
            PositionItem b = On(new PositionItem(), t, Tween(TweenMode.To, 10f), 0.5f, ItemPlayDirection.Backward);
            b.m_Timing.m_Delay = 0.5f;
            Prepared(PlayerDirection.Forward, a, b);
            Assert.That(PosX(t), Is.EqualTo("10"), "two ways on one target — the turned item's end stays (documented)");
        }

        // ---- Seek is the backward play at the mirror place · Skip · Stop · Revert · Restore

        string Seek(ItemPlayDirection d, PlayArgs args, float to)
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("t");
            FeedbackPlayer p = Player(s, On(new PositionItem(), t, Tween(TweenMode.To, 5f), 0.5f, d), On(new PositionItem(), Go("keeper"), new PunchMotion(), 1f, ItemPlayDirection.FollowPlayer));
            p.Play(args);
            s.Tick(Step);
            p.Pause();
            p.Seek(to);
            return Pos(t);
        }

        string End(ItemPlayDirection d, PlayArgs args, string what)
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("t");
            t.transform.localPosition = new Vector3(1f, 0f, 0f);
            FeedbackPlayer p = Player(s, On(new PositionItem(), t, Tween(TweenMode.To, 5f), 0.5f, d), On(new PositionItem(), Go("keeper"), new PunchMotion(), 1f, ItemPlayDirection.FollowPlayer));
            p.Play(args);
            s.Tick(Step);
            s.Tick(Step);
            if (what == "skip")
                p.Skip();
            else if (what == "complete")
                p.Stop(StopMode.Complete);
            else if (what == "revert")
                p.Stop(StopMode.Revert);
            else
            {
                for (int i = 0; i < 25; i++)
                    s.Tick(Step);
                p.Restore();
            }
            s.Tick(0f);
            return PosX(t);
        }

        [Test]
        public void DQ08a_SeekSkipStop()
        {
            // the keeper makes the total 1 s; the item spans [0, 0.5]
            foreach (float at in new[] { 0.1f, 0.2f, 0.35f })
            {
                Assert.That(Seek(ItemPlayDirection.Backward, Fwd, at), Is.EqualTo(Seek(ItemPlayDirection.FollowPlayer, Bwd, 0.5f - at)), "Backward at " + F(at));
                Assert.That(Seek(ItemPlayDirection.Forward, Bwd, 0.5f - at), Is.EqualTo(Seek(ItemPlayDirection.FollowPlayer, Fwd, at)), "Forward at " + F(0.5f - at));
            }
            Assert.That(End(ItemPlayDirection.Backward, Fwd, "skip"), Is.EqualTo("1"), "Skip — the progress way's end");
            Assert.That(End(ItemPlayDirection.Forward, Bwd, "skip"), Is.EqualTo("5"));
            Assert.That(End(ItemPlayDirection.Backward, Fwd, "complete"), Is.EqualTo("1"), "Stop(Complete) — what runs goes to its end");
            Assert.That(End(ItemPlayDirection.Forward, Bwd, "complete"), Is.EqualTo("1"), "a turned item not yet begun stays");
            Assert.That(End(ItemPlayDirection.Backward, Fwd, "revert"), Is.EqualTo("1"));
            Assert.That(End(ItemPlayDirection.Backward, Fwd, "restore"), Is.EqualTo("1"));
        }

        // ---- the origins over five plays — no drift

        string Plays(Func<FeedbackMotion> motion, ItemPlayDirection first, string pattern)
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("t");
            t.transform.localPosition = new Vector3(1f, 0f, 0f);
            PositionItem item = On(new PositionItem(), t, motion(), 0.5f, first);
            FeedbackPlayer p = Player(s, item);
            if (pattern == "toggle")
                p.m_Direction = PlayerDirection.Toggle;
            var ends = new List<string>();
            for (int k = 0; k < 5; k++)
            {
                if (pattern == "B2F3" && k == 2)
                    item.m_PlayDirection = ItemPlayDirection.FollowPlayer;
                if (pattern == "F2B3" && k == 2)
                    item.m_PlayDirection = ItemPlayDirection.Backward;
                p.Play(PlayArgs.Default.WithSeed(7u));
                for (int i = 0; i < 15; i++)
                    s.Tick(Step);
                ends.Add(PosX(t));
            }
            return string.Join(" ", ends);
        }

        [Test]
        public void DQ09a_TheOrigins()
        {
            Func<FeedbackMotion> to = () => Tween(TweenMode.To, 5f), from = () => Tween(TweenMode.From, 5f), by = () => Tween(TweenMode.By, 2f);
            Assert.That(Plays(to, ItemPlayDirection.Backward, ""), Is.EqualTo("1 1 1 1 1"));
            Assert.That(Plays(to, ItemPlayDirection.Opposite, "toggle"), Is.EqualTo("1 5 1 5 1"));
            Assert.That(Plays(to, ItemPlayDirection.Backward, "B2F3"), Is.EqualTo("1 1 5 5 5"));
            Assert.That(Plays(to, ItemPlayDirection.FollowPlayer, "F2B3"), Is.EqualTo("5 5 5 5 5"), "back to the latest forward play's start");
            Assert.That(Plays(from, ItemPlayDirection.Backward, ""), Is.EqualTo("5 5 5 5 5"));
            Assert.That(Plays(from, ItemPlayDirection.Opposite, "toggle"), Is.EqualTo("5 1 5 1 5"));
            Assert.That(Plays(from, ItemPlayDirection.Backward, "B2F3"), Is.EqualTo("5 5 1 1 1"));
            Assert.That(Plays(by, ItemPlayDirection.Backward, ""), Is.EqualTo("1 1 1 1 1"));
            Assert.That(Plays(by, ItemPlayDirection.Opposite, "toggle"), Is.EqualTo("1 3 1 3 1"));
            Assert.That(Plays(by, ItemPlayDirection.Backward, "B2F3"), Is.EqualTo("1 1 3 5 7"));
            Assert.That(Plays(by, ItemPlayDirection.FollowPlayer, "F2B3"), Is.EqualTo("3 5 3 3 3"));
            Assert.That(Plays(() => SpringTo(5f), ItemPlayDirection.Backward, ""), Is.EqualTo("1 1 1 1 1"));
        }

        // ---- the record keeps the way the play was accepted with; an edit reaches the next play

        [Test]
        public void DQ10a_TheRecordKeepsItsWay()
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("t");
            PositionItem item = On(new PositionItem(), t, Tween(TweenMode.To, 5f), 0.5f, ItemPlayDirection.FollowPlayer);
            FeedbackPlayer p = Player(s, item);
            p.Play(Fwd);
            for (int i = 0; i < 12; i++)
            {
                if (i == 3)
                    item.m_PlayDirection = ItemPlayDirection.Backward;
                s.Tick(Step);
            }
            Assert.That(PosX(t), Is.EqualTo("5"), "the running play keeps its way");
            p.Play(Fwd);
            s.Tick(Step);
            Assert.That(PosX(t), Is.EqualTo("4.5"), "the next play turns");
            // the play-mode edit branch: the next play (a managed reference id exists once serialized)
            FeedbackPlayer host = Go("holder").AddComponent<FeedbackPlayer>();
            var probe = new PositionItem();
            host.m_Items.Add(probe);
            new SerializedObject(host).FindProperty("m_Items");
            long id = ManagedReferenceUtility.GetManagedReferenceIdForObject(host, probe);
            Assert.That(PlayModeEdits.Classify(host, "managedReferences[" + id + "].m_PlayDirection", out _), Is.EqualTo(EditBranch.NextPlay));
        }

        // ---- flow — a no-wait Play Player passes the progress way · a timed chain turns its order · a Player Control does
        // nothing backward (a NestedPlay waits by default — these switch it off)

        [Test]
        public void DQ11a_Flow()
        {
            {
                FeedbackScheduler s = Sched();
                GameObject t = Go("t");
                FeedbackPlayer child = Player(s, On(new PositionItem(), t, Tween(TweenMode.To, 5f), 0.2f, ItemPlayDirection.FollowPlayer));
                var play = new PlayPlayerItem { m_Player = child, m_PlayDirection = ItemPlayDirection.Backward };
                play.m_Play.m_PassDirection = true;
                play.m_Play.m_Wait = false;
                FeedbackPlayer parent = Player(s, play);
                parent.Play(Fwd);
                s.Tick(Step);
                Assert.That(PosX(t), Is.EqualTo("3.75"), "the child plays backward");
            }
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer c1 = Player(s, On(new PositionItem(), Go("c1"), Tween(TweenMode.To, 5f), 0.3f, ItemPlayDirection.FollowPlayer));
                FeedbackPlayer c2 = Player(s, On(new PositionItem(), Go("c2"), Tween(TweenMode.To, 5f), 0.3f, ItemPlayDirection.FollowPlayer));
                var chain = new PlayerChainItem { m_PlayDirection = ItemPlayDirection.Backward };
                chain.m_Entries.Add(new ChainEntry { m_Player = c1, m_Wait = false });
                chain.m_Entries.Add(new ChainEntry { m_Player = c2, m_Delay = 0.1f, m_Wait = false });
                FeedbackPlayer parent = Player(s, chain);
                parent.Play(Fwd);
                s.Tick(Step);
                Assert.That((c2.IsPlaying, c1.IsPlaying), Is.EqualTo((true, false)), "the last entry first");
            }
            {
                FeedbackScheduler s = Sched();
                FeedbackPlayer x = Player(s, On(new PositionItem(), Go("x"), Tween(TweenMode.To, 5f), 1f, ItemPlayDirection.FollowPlayer));
                x.Play();
                var pause = new PlayerControlItem { m_Action = PlayerControlAction.Pause, m_PlayDirection = ItemPlayDirection.Backward };
                pause.m_Players.Add(x);
                FeedbackPlayer parent = Player(s, pause);
                parent.Play(Fwd);
                s.Tick(Step);
                Assert.That(x.HasPaused, Is.False);
            }
        }

        // ---- the body's field (Advanced, after the conditions) · the header's mark · the debugger's line (the selected play
        // — the first read clears the selection)

        [Test]
        public void DQ15a_TheInspectorAndTheDebugger()
        {
            FeedbackPlayer host = Go("holder").AddComponent<FeedbackPlayer>();
            var item = new PositionItem { m_Motion = Tween(TweenMode.To, 1f) };
            host.m_Items.Add(item);
            List<string> fields = ItemBody.FieldsOf(new SerializedObject(host).FindProperty("m_Items.Array.data[0]"), item);
            int at = fields.IndexOf("m_PlayDirection");
            Assert.That(at > 0 && fields[at - 1] == "m_Conditions.m_IntensityRange", Is.True, string.Join(", ", fields));
            Assert.That(ItemRules.Advanced("m_PlayDirection"), Is.True);
            Assert.That(ItemRules.Label(item, "m_PlayDirection") ?? ObjectNames.NicifyVariableName("m_PlayDirection"), Is.EqualTo("Play Direction"));
            var words = new List<string>();
            foreach (ItemPlayDirection d in new[] { ItemPlayDirection.FollowPlayer, ItemPlayDirection.Forward, ItemPlayDirection.Backward, ItemPlayDirection.Opposite })
            {
                item.m_PlayDirection = d;
                words.Add(ItemWords.Time(item));
            }
            Assert.That(words, Is.EqualTo(new[] { "0.2", "0.2 ▶", "0.2 ◀", "0.2 ⇄" }));
            FeedbackScheduler s = Sched();
            item.m_Target = Direct(Go("t"));
            item.m_Timing.m_Duration = 1f;
            item.m_PlayDirection = ItemPlayDirection.Backward;
            host.Bind(s);
            host.EnsureRuntime();
            m_Players.Add(host);
            PlaybackHandle handle = host.Play();
            var reader = new DebuggerReader();
            reader.Read(s);
            reader.Selected = handle;
            reader.Read(s);
            Assert.That(reader.Items.Count > 0 && reader.Items[0].Turned, Is.True, "the item row");
        }

        // ---- the preview — a turned item's scrub is the runtime's seek; the scene comes back clean

        static void Pump(int updates)
        {
            for (int i = 0; i < updates; i++)
            {
                s_Now += 1.0 / 120.0;
                PreviewSession.Current?.Update();
            }
        }

        string SeekAlone(float to)
        {
            FeedbackScheduler s = Sched();
            GameObject t = Go("t");
            FeedbackPlayer p = Player(s, On(new PositionItem(), t, Tween(TweenMode.To, 5f), 0.5f, ItemPlayDirection.Backward));
            p.Play();
            s.Tick(Step);
            p.Pause();
            p.Seek(to);
            return Pos(t);
        }

        [Test]
        public void DQ17a_ThePreview()
        {
            const string scenePath = "Assets/DirectionTests.unity";
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
                var go = new GameObject("subject");
                FeedbackPlayer subject = go.AddComponent<FeedbackPlayer>();
                subject.m_Items.Add(On(new PositionItem(), go, Tween(TweenMode.To, 5f), 0.5f, ItemPlayDirection.Backward));
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                Assert.That(PreviewSession.Start(subject, default), Is.EqualTo(PreviewRefusal.None));
                Pump(2);
                Assert.That(PreviewSession.Current.Scrub(0.2), Is.EqualTo(SeekRefusal.None));
                string scrub = Pos(go);
                PreviewSession.Current.Stop();
                Assert.That(scrub, Is.EqualTo(SeekAlone(0.2f)));
                Assert.That(go.transform.localPosition, Is.EqualTo(Vector3.zero), "back");
                Assert.That(scene.isDirty, Is.False);
                Object.DestroyImmediate(go);
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
    }
}
