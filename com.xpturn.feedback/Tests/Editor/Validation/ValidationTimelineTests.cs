using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using xpTURN.Feedback.Editor;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // C — the timeline
    internal sealed class ValidationTimelineTests
    {
        F m_Fx;

        [SetUp]
        public void SetUp() => m_Fx = new F();

        [TearDown]
        public void TearDown() => m_Fx.Clean();

        static FeedbackItem Timed(FeedbackItem item, System.Action<FeedbackItem> set)
        {
            set(item);
            return item;
        }

        static List<FeedbackItem> Nested(int depth)
        {
            var list = new List<FeedbackItem>();
            for (int i = 0; i < depth; i++)
                list.Add(new LoopStartItem());
            list.Add(F.Position());
            for (int i = 0; i < depth; i++)
                list.Add(new LoopEndItem());
            return list;
        }

        // each code at its list slot — an inactive flow item first shifts the program's items
        [Test]
        public void VT05a_EachIssueCodeAtItsSlot()
        {
            var cases = new List<(TimelineIssueCode Code, FeedbackItem[] Items)>
            {
                (TimelineIssueCode.UnpairedLoopStart, new FeedbackItem[] { new LoopStartItem(), F.Position() }),
                (TimelineIssueCode.UnpairedLoopEnd, new FeedbackItem[] { F.Position(), new LoopEndItem() }),
                (TimelineIssueCode.LoopTooDeep, Nested(5).ToArray()),
                (TimelineIssueCode.HoldWithoutNext, new FeedbackItem[] { F.Position(), new HoldItem() }),
                (TimelineIssueCode.BackgroundInLoop, new FeedbackItem[] { new LoopStartItem(), Timed(F.Position(), i => i.m_Timing.m_Background = true), F.Position(), new LoopEndItem() }),
                (TimelineIssueCode.BackgroundOnFlowItem, new FeedbackItem[] { Timed(new WaitItem(), i => i.m_Timing.m_Background = true), F.Position() }),
                (TimelineIssueCode.BackgroundWithoutForeground, new FeedbackItem[] { Timed(F.Position(), i => i.m_Timing.m_Background = true) }),
                (TimelineIssueCode.UnreachableAfterInfinite, new FeedbackItem[] { Timed(F.Position(), i => i.m_Timing.m_Infinite = true), Timed(F.Position(), i => i.m_Timing.m_Rule = StartRule.AfterPrevious) }),
            };
            string text(TimelineIssueCode code) => TimelineIssueRule.Text(code);
            foreach (var (code, items) in cases)
            {
                FeedbackPlayer p = m_Fx.Player("VT05a", items);
                List<RuleResult> r = F.Of(p, "FB-C01");
                Assert.That(r.Select(x => x.Message), Does.Contain(text(code)), code.ToString());
            }
            // the slot: an inactive Wait first — the unpaired Loop Start is list slot 1
            FeedbackPlayer shifted = m_Fx.Player("VT05aShift", new WaitItem { m_Active = false }, new LoopStartItem(), F.Position());
            Assert.That(F.Of(shifted, "FB-C01").Select(x => x.Item), Is.EqualTo(new[] { 1 }));
            Assert.That(F.Of(m_Fx.Player("VT05aQuiet", F.Position(), F.Position()), "FB-C01"), Is.Empty);
        }

        [Test]
        public void VT05b_BackwardOverAnEndlessList()
        {
            PositionItem forever = F.Position();
            forever.m_Timing.m_RepeatForever = true;
            foreach (PlayerDirection direction in new[] { PlayerDirection.Backward, PlayerDirection.Toggle })
            {
                FeedbackPlayer p = m_Fx.Player("VT05b", forever);
                p.m_Direction = direction;
                RuleResult r = F.Of(p, "FB-C02").Single();
                FeedbackValidator.Apply(r, r.Fixes.Single());
                Assert.That(p.m_Direction, Is.EqualTo(PlayerDirection.Forward), direction.ToString());
                Assert.That(F.Of(p, "FB-C02"), Is.Empty);
            }
            FeedbackPlayer wait = m_Fx.Player("VT05bWait", new WaitForSignalItem(), F.Position());
            wait.m_Direction = PlayerDirection.Backward;
            Assert.That(F.Of(wait, "FB-C02"), Has.Count.EqualTo(1), "a signal wait without a timeout");
            Assert.That(F.Of(m_Fx.Player("VT05bForward", F.Position()), "FB-C02"), Is.Empty);
        }

        // the period 1 ms and the runs 10⁴
        [Test]
        public void VT05c_TheRepeatsCost()
        {
            FeedbackItem Forever(float duration, float gap) => Timed(F.Position(), i =>
            {
                i.m_Timing.m_Duration = duration;
                i.m_Timing.m_RepeatGap = gap;
                i.m_Timing.m_RepeatForever = true;
            });
            Assert.That(F.Of(m_Fx.Player("VT05cShort", Forever(0.0003f, 0.0002f)), "FB-C03").Single().Item, Is.EqualTo(0), "every 0.5 ms");
            Assert.That(F.Of(m_Fx.Player("VT05cLong", Forever(0.0015f, 0.0005f)), "FB-C03"), Is.Empty, "every 2 ms");
            FeedbackPlayer Loop(int n) => m_Fx.Player("VT05cLoop" + n, new LoopStartItem(), F.Position(), new LoopEndItem { m_LoopCount = n });
            Assert.That(F.Of(Loop(10000), "FB-C03").Single().Item, Is.EqualTo(-1), "10⁴ runs");
            Assert.That(F.Of(Loop(9000), "FB-C03"), Is.Empty, "9,000 runs");
            // nested: 10 × 10 × 100 = 10⁴
            FeedbackPlayer nested = m_Fx.Player("VT05cNested", new LoopStartItem(), new LoopStartItem(), Timed(F.Position(), i => i.m_Timing.m_RepeatCount = 99), new LoopEndItem { m_LoopCount = 10 }, new LoopEndItem { m_LoopCount = 10 });
            Assert.That(F.Of(nested, "FB-C03"), Has.Count.EqualTo(1));
        }

        [Test]
        public void VT05d_ConsecutiveHoldsOnTwoClocks()
        {
            HoldItem Hold(ItemClock clock) => (HoldItem)Timed(new HoldItem(), i => i.m_Timing.m_Clock = clock);
            Assert.That(F.Of(m_Fx.Player("VT05dTwo", Hold(ItemClock.Unscaled), Hold(ItemClock.Scaled), F.Position()), "FB-C04").Single().Item, Is.EqualTo(1));
            Assert.That(F.Of(m_Fx.Player("VT05dOne", Hold(ItemClock.Scaled), Hold(ItemClock.Scaled), F.Position()), "FB-C04"), Is.Empty);
            Assert.That(F.Of(m_Fx.Player("VT05dApart", Hold(ItemClock.Unscaled), F.Position(), Hold(ItemClock.Scaled), F.Position()), "FB-C04"), Is.Empty, "not consecutive");
        }

        // each contract row: the fix writes what plays now — the nominal total is the same before and after
        [Test]
        public void VT05e_TheContractsFixIsWhatPlaysNow()
        {
            var timing = new List<(string Name, FeedbackItem[] Items)>
            {
                ("delay -1", new[] { Timed(F.Position(), i => i.m_Timing.m_Delay = -1f) }),
                ("delay NaN", new[] { Timed(F.Position(), i => i.m_Timing.m_Delay = float.NaN) }),
                ("duration -1", new[] { Timed(F.Position(), i => i.m_Timing.m_Duration = -1f) }),
                ("gap -1", new[] { Timed(F.Position(), i => { i.m_Timing.m_RepeatCount = 1; i.m_Timing.m_RepeatGap = -1f; }) }),
                ("repeat -2", new[] { Timed(F.Position(), i => i.m_Timing.m_RepeatCount = -2) }),
                ("loop count -1", new FeedbackItem[] { new LoopStartItem(), F.Position(), new LoopEndItem { m_LoopCount = -1 } }),
                ("loop gap -1", new FeedbackItem[] { new LoopStartItem(), F.Position(), new LoopEndItem { m_LoopCount = 2, m_LoopGap = -1f } }),
                ("timeout -1", new FeedbackItem[] { new WaitForSignalItem { m_Timeout = -1f } }),
                ("freeze -1", new FeedbackItem[] { new HitStopItem { m_FreezeDuration = -1f } }),
            };
            foreach (var (name, items) in timing)
            {
                FeedbackPlayer p = m_Fx.Player("VT05e", items);
                double before = TimelineModel.Build(p.m_Items, 1f, 0f).Total;
                List<RuleResult> r = F.Of(p, "FB-C05");
                Assert.That(r, Has.Count.EqualTo(1), name);
                Assert.That(r[0].Fixes.Single().Name, Does.Contain("what plays now"), name);
                FeedbackValidator.Apply(r[0], r[0].Fixes.Single());
                Assert.That(F.Of(p, "FB-C05"), Is.Empty, name);
                Assert.That(TimelineModel.Build(p.m_Items, 1f, 0f).Total, Is.EqualTo(before), name + " — the same total");
            }
            // the chance · cooldown · player's values: the runtime's cut
            PositionItem chance = F.Position();
            chance.m_Conditions.m_Chance = -5f;
            PositionItem nan = F.Position();
            nan.m_Conditions.m_Chance = float.NaN;
            nan.m_Conditions.m_Cooldown = float.NaN;
            FeedbackPlayer values = m_Fx.Player("VT05eValues", chance, nan);
            values.m_DurationMultiplier = -1f;
            values.m_StartDelay = float.NaN;
            values.m_Speed = float.PositiveInfinity;
            values.m_Intensity = -2f;
            values.m_Chance = 150f;
            values.m_Cooldown = -1f;
            foreach (RuleResult r in F.Of(values, "FB-C05"))
                FeedbackValidator.Apply(r, r.Fixes.Single());
            Assert.That(((EffectItem)values.m_Items[0]).m_Conditions.m_Chance, Is.EqualTo(0f), "never");
            Assert.That(((EffectItem)values.m_Items[1]).m_Conditions.m_Chance, Is.EqualTo(100f), "NaN plays");
            Assert.That(((EffectItem)values.m_Items[1]).m_Conditions.m_Cooldown, Is.EqualTo(0f));
            Assert.That(values.m_DurationMultiplier, Is.EqualTo(PlayerRuntime.Factor(-1f)));
            Assert.That(values.m_StartDelay, Is.EqualTo(PlayerRuntime.Offset(float.NaN)));
            Assert.That(values.m_Speed, Is.EqualTo(1f));
            Assert.That(values.m_Intensity, Is.EqualTo(1f));
            Assert.That(values.m_Chance, Is.EqualTo(100f));
            Assert.That(values.m_Cooldown, Is.EqualTo(0f));
            Assert.That(F.Of(values, "FB-C05"), Is.Empty);
            // the gate's distance 0: off is what plays
            FeedbackPlayer gate = m_Fx.Player("VT05eGate");
            gate.m_UseDistanceGate = true;
            gate.m_GateDistance = 0f;
            RuleResult g = F.Of(gate, "FB-C05").Single();
            FeedbackValidator.Apply(g, g.Fixes.Single());
            Assert.That(gate.m_UseDistanceGate, Is.False);
        }

        // a freeze of +∞ plays endless: an error, its fix the default
        [Test]
        public void VT05f_AnEndlessFreezeIsAnError()
        {
            FeedbackPlayer p = m_Fx.Player("VT05f", new HitStopItem { m_FreezeDuration = float.PositiveInfinity });
            Assert.That(TimelineModel.Build(p.m_Items, 1f, 0f).Total, Is.EqualTo(double.PositiveInfinity), "the fact — endless");
            RuleResult r = F.Of(p, "FB-C05").Single();
            Assert.That(r.Severity, Is.EqualTo(RuleSeverity.Error));
            Assert.That(r.Fixes.Single().Name, Does.Contain("changes what plays"));
            FeedbackValidator.Apply(r, r.Fixes.Single());
            Assert.That(((HitStopItem)p.m_Items[0]).m_FreezeDuration, Is.EqualTo(0.1f));
        }
    }
}
