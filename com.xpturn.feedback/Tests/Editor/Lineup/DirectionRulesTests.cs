using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // FB-C06 (a way that is not read) · FB-C07 (Backward on an item whose reverse only undoes its forward act) · the declaration
    // they read — by its rule over the registered types, and by the act on hand fixtures
    public sealed class DirectionRulesTests
    {
        const float Step = 0.05f;
        F m_Fx;
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();

        [SetUp]
        public void SetUp() => m_Fx = new F();

        [TearDown]
        public void TearDown()
        {
            m_Fx.Clean();
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

        GameObject Go(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        static SetMotion SetTo(float v) => new SetMotion { m_Params = new SetParams { m_Value = new Vector4(v, 0f, 0f, 0f) } };

        static TweenMotion TweenTo(float v)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = new Vector4(v, 0f, 0f, 0f);
            return new TweenMotion { m_Params = p };
        }

        static T Direct<T>(T item, Object target, FeedbackMotion motion, float duration) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        // ---- the rules

        [Test]
        public void DQ14a_TheRules()
        {
            var cond = F.Position();
            cond.m_PlayDirection = ItemPlayDirection.Backward;
            cond.m_Conditions.m_Direction = DirectionCondition.ReverseOnly;
            var endless = F.Position();
            endless.m_PlayDirection = ItemPlayDirection.Opposite;
            endless.m_Timing.m_Infinite = true;
            var wait = new PlayPlayerItem { m_PlayDirection = ItemPlayDirection.Forward };
            wait.m_Play.m_Wait = true;
            var set = F.Item<SetActiveItem>(SetTo(0f));
            set.m_PlayDirection = ItemPlayDirection.Backward;
            var ue = new UnityEventItem { m_PlayDirection = ItemPlayDirection.Backward };
            var tween = F.Position();
            tween.m_PlayDirection = ItemPlayDirection.Backward;
            var ueForward = new UnityEventItem { m_PlayDirection = ItemPlayDirection.Forward };
            var debug = new DebugItem { m_PlayDirection = ItemPlayDirection.Backward };
            var plain = F.Position();
            plain.m_Timing.m_Infinite = true;
            var setOpposite = F.Item<SetActiveItem>(SetTo(0f));
            setOpposite.m_PlayDirection = ItemPlayDirection.Opposite;
            FeedbackPlayer p = m_Fx.Player("DQ14a", cond, endless, wait, set, ue, tween, ueForward, debug, plain, setOpposite);
            wait.m_Player = p;
            List<RuleResult> c06 = F.Of(p, "FB-C06");
            Assert.That(c06.Select(r => r.Item + ":" + r.Message), Is.EquivalentTo(new[]
            {
                "0:The play direction is not read — the direction condition decides it.",
                "1:The play direction is not read — it has no end.",
                "2:The play direction is not read — it waits for another player.",
            }));
            Assert.That(c06.All(r => r.Severity == RuleSeverity.Info), Is.True);
            List<RuleResult> c07 = F.Of(p, "FB-C07");
            Assert.That(c07.Select(r => r.Item), Is.EquivalentTo(new[] { 3, 4 }), "a Set · a UnityEvent — not the tween, a Forward event, the debug, Opposite, a condition");
            Assert.That(c07[0].Message, Is.EqualTo("Backward does nothing when played — this item only undoes its own forward act."));
            Assert.That(c07.All(r => r.Severity == RuleSeverity.Warning), Is.True);
            RuleResult first = c07.First();
            FeedbackValidator.Apply(first, first.Fixes.Single());
            Assert.That(((EffectItem)p.m_Items[first.Item]).m_PlayDirection, Is.EqualTo(ItemPlayDirection.FollowPlayer), "Follow the player");
            Assert.That(F.Of(p, "FB-C07").Count, Is.EqualTo(1));
            RuleResult unread = F.Of(p, "FB-C06").First();
            FeedbackValidator.Apply(unread, unread.Fixes.Single());
            Assert.That(F.Of(p, "FB-C06").Count, Is.EqualTo(2));
        }

        // ---- the declaration by its rule: every registered target type — a Set is inert, any other motion acts; the action types —
        // exactly the five

        static bool OfThePackage(Type t)
        {
            string assembly = t.Assembly.GetName().Name;
            return assembly.StartsWith("xpTURN.Feedback", StringComparison.Ordinal) && !assembly.StartsWith("xpTURN.Feedback.Tests", StringComparison.Ordinal);
        }

        [Test]
        public void DQ14b_TheDeclarationByItsRule()
        {
            Type[] motions = typeof(FeedbackMotion).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(FeedbackMotion)) && !t.IsAbstract).ToArray();
            var fails = new List<string>();
            int targets = 0;
            foreach (Type type in FeedbackImplementations.RegisteredTypes.Where(t => t.IsSubclassOf(typeof(TargetItem)) && OfThePackage(t)))
            {
                var item = (TargetItem)Activator.CreateInstance(type);
                foreach (Type mt in motions)
                {
                    var motion = (FeedbackMotion)Activator.CreateInstance(mt);
                    if (!item.Traits.Supports(motion.Kind))
                        continue;
                    item.m_Motion = motion;
                    targets++;
                    if (item.ActsBackward == motion is SetMotion)
                        fails.Add(type.Name + " " + mt.Name);
                }
            }
            string inert = string.Join(",", FeedbackImplementations.RegisteredTypes.Where(t => t.IsSubclassOf(typeof(ActionItem)) && OfThePackage(t))
                .Where(t => !((ActionItem)Activator.CreateInstance(t)).ActsBackward).Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.That(fails, Is.Empty, string.Join(" · ", fails));
            Assert.That(targets, Is.GreaterThan(150));
            // the impulse and its clear where their assembly compiles
            Assert.That(inert, Is.EqualTo((FeedbackImplementations.RegisteredTypes.Contains(typeof(CinemachineImpulseItem)) ? "CinemachineImpulseClearItem,CinemachineImpulseItem," : "")
                + "HitStopItem,PlayerControlItem,RandomUnityEventItem,SignalBroadcastItem,UnityEventItem"));
        }

        // ---- the declaration by the act: played Backward alone (a forward play) — did anything change? A row counts when the plain
        // forward play changes it

        sealed class Receiver : IFeedbackSignalReceiver
        {
            internal int Heard;

            public void OnFeedbackSignal(in FeedbackSignal signal) => Heard++;
        }

        (bool forward, bool backward, bool declared) Act(Func<FeedbackScheduler, (EffectItem, Func<string>)> make)
        {
            bool Changes(ItemPlayDirection d, out EffectItem item)
            {
                var s = new FeedbackScheduler(ContextKind.Test);
                m_Schedulers.Add(s);
                (EffectItem made, Func<string> read) = make(s);
                item = made;
                made.m_PlayDirection = d;
                FeedbackPlayer p = Go("Player").AddComponent<FeedbackPlayer>();
                p.m_Items.Add(made);
                p.Bind(s);
                p.EnsureRuntime();
                m_Players.Add(p);
                string before = read();
                p.Play();
                bool changed = read() != before;
                for (int i = 0; i < 6; i++)
                {
                    s.Tick(Step);
                    changed |= read() != before;
                }
                return changed;
            }
            bool forward = Changes(ItemPlayDirection.FollowPlayer, out _);
            bool backward = Changes(ItemPlayDirection.Backward, out EffectItem e);
            return (forward, backward, e.ActsBackward);
        }

        FeedbackPlayer Other(FeedbackScheduler s, float length)
        {
            FeedbackPlayer x = Go("other").AddComponent<FeedbackPlayer>();
            x.m_Items.Add(Direct(new PositionItem(), Go("other target"), TweenTo(5f), length));
            x.Bind(s);
            x.EnsureRuntime();
            m_Players.Add(x);
            return x;
        }

        [Test]
        public void DQ14c_TheDeclarationByTheAct()
        {
            var rows = new List<(string, Func<FeedbackScheduler, (EffectItem, Func<string>)>)>
            {
                ("UnityEvent", s => { int n = 0; var u = new UnityEventItem(); u.m_Event.AddListener(() => n++); return (u, () => n.ToString()); }),
                ("Random Unity Event", s =>
                {
                    int n = 0;
                    var r = new RandomUnityEventItem();
                    var w = new WeightedUnityEvent();
                    w.m_Event.AddListener(() => n++);
                    r.m_Entries.Add(w);
                    return (r, () => n.ToString());
                }),
                ("Signal Broadcast", s =>
                {
                    var heard = new Receiver();
                    ChannelRef three = ChannelRef.Of(3);
                    s.Context.Channels.Register(heard, new ChannelKey(in three));
                    return (new SignalBroadcastItem { m_Channel = three }, () => heard.Heard.ToString());
                }),
                ("Player Control", s =>
                {
                    FeedbackPlayer x = Other(s, 1f);
                    x.Play();
                    var pause = new PlayerControlItem { m_Action = PlayerControlAction.Pause };
                    pause.m_Players.Add(x);
                    return (pause, () => x.HasPaused.ToString());
                }),
                // a hit stop's act is its time scale request
                ("Hit Stop", s => (new HitStopItem { m_FreezeDuration = 0.1f }, () => s.Context.TimeScale.Count.ToString())),
                ("SetActive Set", s => { GameObject t = Go("t"); return (Direct(new SetActiveItem(), t, SetTo(0f), 0f), () => t.activeSelf.ToString()); }),
                ("Set Layer", s => { GameObject t = Go("t"); return (Direct(new SetLayerItem { m_Layer = 5 }, t, new SetMotion(), 0f), () => t.layer.ToString()); }),
                ("Position Tween", s => { GameObject t = Go("t"); return (Direct(new PositionItem(), t, TweenTo(5f), 0.2f), () => t.transform.localPosition.ToString("R")); }),
                ("Play Player", s =>
                {
                    FeedbackPlayer child = Other(s, 1f);
                    var play = new PlayPlayerItem { m_Player = child };
                    play.m_Play.m_Wait = false;
                    return (play, () => child.IsPlaying.ToString());
                }),
            };
            var fails = new List<string>();
            foreach ((string name, Func<FeedbackScheduler, (EffectItem, Func<string>)> make) in rows)
            {
                (bool forward, bool backward, bool declared) = Act(make);
                if (!forward)
                    fails.Add(name + " — the plain play changes nothing (blind)");
                else if (backward != declared)
                    fails.Add(name + (backward ? " acts but is declared inert" : " is inert but declared acting"));
            }
            Assert.That(fails, Is.Empty, string.Join(" · ", fails));
        }
    }
}
