using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // EditMode — the conversion, the prepare, the records and the nominal, the release and the rebuild, the scheduler choice,
    // the format hook, the anchors, the golden rows and the surface, the re-entry. Edit mode runs no Awake · OnDestroy: a
    // player is bound to a test scheduler and EnsureRuntime · ReleaseRuntime are called directly — TearDown releases before
    // it destroys. The test items are registered with the test binding (Core's own list is empty). Warnings are counted by a
    // log listener (number and order); a logged exception is expected with LogAssert. A row checks everything and judges at
    // the end
    public sealed class PlayerPrepareTests
    {
        static readonly List<string> s_Warnings = new List<string>();
        readonly List<string> m_Fail = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        FeedbackScheduler m_SavedDefault;
        bool m_DefaultSwapped;

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
                s_Warnings.Add(condition);
        }

        [SetUp]
        public void SetUp()
        {
            m_Fail.Clear();
            s_Warnings.Clear();
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerRuntime.ResetSession();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            if (m_DefaultSwapped)
            {
                FeedbackScheduler.Default = m_SavedDefault;
                m_DefaultSwapped = false;
            }
            // edit mode's destruction brings no OnDestroy — the one that bound releases first
            foreach (FeedbackPlayer player in m_Players)
                player.ReleaseRuntime();
            m_Players.Clear();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            foreach (FeedbackScheduler scheduler in m_Schedulers)
                scheduler.Shutdown();
            m_Schedulers.Clear();
            PlayerTestBinding.Reset();
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        static bool Near(double a, double b) => (double.IsPositiveInfinity(a) && double.IsPositiveInfinity(b)) || Math.Abs(a - b) < 1e-4;

        FeedbackScheduler Scheduler(ContextKind kind = ContextKind.Test)
        {
            var scheduler = new FeedbackScheduler(kind);
            m_Schedulers.Add(scheduler);
            return scheduler;
        }

        FeedbackPlayer Player(FeedbackScheduler scheduler, params FeedbackItem[] items)
        {
            var go = new GameObject("player");
            m_Made.Add(go);
            var player = go.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Items.AddRange(items);
            if (scheduler != null)
                player.Bind(scheduler);
            return player;
        }

        GameObject Anchor(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        static PlayerTestAction Action(string label, float length, StartRule rule = StartRule.WithPrevious, bool active = true)
        {
            var action = new PlayerTestAction { m_Label = label, m_Length = length, m_Active = active };
            action.m_Timing.m_Rule = rule;
            return action;
        }

        static PlayerTestAction Unscaled(string label)
        {
            PlayerTestAction action = Action(label, 0.2f);
            action.m_Timing.m_Clock = ItemClock.Unscaled;
            return action;
        }

        static PlayerTestTarget Target(FeedbackMotion motion, float duration)
        {
            var target = new PlayerTestTarget { m_Motion = motion };
            target.m_Timing.m_Duration = duration;
            return target;
        }

        static TimelineItemDesc D(FeedbackItem item)
        {
            TimelineConversion.TryDescribe(item, out TimelineItemDesc desc);
            return desc;
        }

        static PlayerTestBinding Bound(string label) => PlayerTestBinding.Made.LastOrDefault(b => b.Item.m_Label == label);

        static int Warnings(string prefix) => s_Warnings.Count(l => l.StartsWith(prefix, StringComparison.Ordinal));

        // ---- the conversion table · the conversion names every flow type

        [Test]
        public void CE01_ConversionTable()
        {
            Check(D(new WaitItem()).Kind == TimelineItemKind.Wait, "a wait's kind");
            TimelineItemDesc signal = D(new WaitForSignalItem { m_Timeout = 2f });
            Check(signal.Kind == TimelineItemKind.SignalWait && Near(signal.Timeout, 2f), "a signal wait's kind and timeout");
            Check(D(new LoopStartItem()).Kind == TimelineItemKind.LoopStart, "a loop start's kind");
            TimelineItemDesc end = D(new LoopEndItem { m_LoopCount = 3, m_LoopGap = 0.5f });
            Check(end.Kind == TimelineItemKind.LoopEnd && end.LoopCount == 3 && Near(end.LoopGap, 0.5f), "a loop end's kind, count and gap");
            Check(D(new LoopEndItem { m_LoopCount = 0 }).LoopCount == 0, "a loop end's 0 stays 0 (infinite)");
            Check(D(new HoldItem()).Kind == TimelineItemKind.Hold, "a hold's kind");
            TimelineItemDesc none = D(null);
            Check(none.Kind == TimelineItemKind.Effect && none.Instant && none.Duration == 0f, "a null slot is an instant effect of length 0");

            // instants
            TimelineItemDesc set = D(Target(new SetMotion(), 0.7f));
            Check(set.Kind == TimelineItemKind.Effect && set.Instant && set.Duration == 0f, "a Set target is instant");
            TimelineItemDesc fire = D(Action("a", 0f));
            Check(fire.Kind == TimelineItemKind.Effect && fire.Instant && fire.Duration == 0f, "an action of length 0 is instant");

            // the duration's order
            var spring = new SpringMotion();
            TimelineItemDesc sprung = D(Target(spring, 0.7f));
            Check(spring.NaturalDuration > 0f && Near(sprung.Duration, spring.NaturalDuration) && !sprung.Instant, "a spring's natural duration wins over the timing's");
            PlayerTestAction timed = Action("a", 0.3f);
            timed.m_Timing.m_Duration = 0.7f;
            Check(Near(D(timed).Duration, 0.3f) && !D(timed).Instant, "an action's own length wins over the timing's");
            Check(Near(D(Target(new TweenMotion(), 0.7f)).Duration, 0.7f), "a tween takes the timing's duration");
            Check(Near(D(Target(null, 0.7f)).Duration, 0.7f) && D(Target(null, 0.7f)).Kind == TimelineItemKind.Effect, "a target without a motion keeps its place with the timing's duration");

            // the infinite flag
            var forever = new WaitItem();
            forever.m_Timing.m_Infinite = true;
            Check(float.IsPositiveInfinity(D(forever).Duration), "an infinite wait");
            PlayerTestTarget tweenForever = Target(new TweenMotion(), 0.7f);
            tweenForever.m_Timing.m_Infinite = true;
            Check(float.IsPositiveInfinity(D(tweenForever).Duration), "an infinite tween");
            PlayerTestTarget springForever = Target(new SpringMotion(), 0.7f);
            springForever.m_Timing.m_Infinite = true;
            Check(Near(D(springForever).Duration, spring.NaturalDuration), "a natural duration wins over the infinite flag");

            // repeats
            PlayerTestAction repeated = Action("r", 0.1f);
            repeated.m_Timing.m_RepeatCount = 2;
            repeated.m_Timing.m_RepeatGap = 0.05f;
            Check(D(repeated).Repeat == 2 && Near(D(repeated).RepeatGap, 0.05f), "a repeat count is the additional runs");
            repeated.m_Timing.m_RepeatForever = true;
            Check(D(repeated).Repeat == -1, "repeat forever is -1");
            var waitTwice = new WaitItem();
            waitTwice.m_Timing.m_RepeatCount = 1;
            Check(D(waitTwice).Repeat == 1, "a wait's repeat");

            // clocks by name
            foreach ((ItemClock data, TimelineClock expected) in new[] { (ItemClock.Player, TimelineClock.Player), (ItemClock.Scaled, TimelineClock.Scaled), (ItemClock.Unscaled, TimelineClock.Unscaled) })
            {
                PlayerTestAction clocked = Action("c", 0.1f);
                clocked.m_Timing.m_Clock = data;
                Check(D(clocked).Clock == expected, "the clock " + data);
            }
            var stop = new HitStopItem();
            stop.m_Timing.m_Clock = ItemClock.Scaled;
            Check(D(stop).Clock == TimelineClock.Unscaled, "a hit stop counts on the unscaled clock whatever its data says");

            // the rule, the delay, the background flag
            PlayerTestAction placed = Action("p", 0.1f, StartRule.AfterAll);
            placed.m_Timing.m_Delay = 0.3f;
            placed.m_Timing.m_Background = true;
            Check(D(placed).Rule == StartRule.AfterAll && Near(D(placed).Delay, 0.3f) && D(placed).Background, "the rule, the delay and the background flag");
            Check(D(Action("p", 0.1f, StartRule.AfterPrevious)).Rule == StartRule.AfterPrevious, "the rule After previous");

            // the conversion's IL names every concrete flow type of Core — the linker keeps a type the code names
            MethodInfo method = typeof(TimelineConversion).GetMethod("TryDescribe", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var named = new HashSet<Type>(TypeTokens(method));
            Type[] flows = typeof(FeedbackItem).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(FlowItem).IsAssignableFrom(t)).ToArray();
            Check(flows.Length == 5, "Core's concrete flow types (" + flows.Length + ")");
            foreach (Type flow in flows)
                Check(named.Contains(flow), "the conversion names " + flow.Name);
            End();
        }

        // ---- the cuts — the timeline does not filter its input

        [Test]
        public void CE02_Cuts()
        {
            foreach (float bad in new[] { -1f, float.NaN, float.PositiveInfinity })
            {
                PlayerTestAction a = Action("a", 0.1f);
                a.m_Timing.m_Delay = bad;
                a.m_Timing.m_RepeatGap = bad;
                Check(D(a).Delay == 0f && D(a).RepeatGap == 0f, "a delay · a repeat gap of " + bad + " is 0");
                TimelineItemDesc end = D(new LoopEndItem { m_LoopCount = 2, m_LoopGap = bad });
                Check(end.LoopGap == 0f, "a loop gap of " + bad + " is 0");
                Check(D(new WaitForSignalItem { m_Timeout = bad }).Timeout == 0f, "a timeout of " + bad + " is 0");
            }
            Check(D(Target(null, -2f)).Duration == 0f, "a duration of -2 is 0");
            Check(D(Target(null, float.NaN)).Duration == 0f, "a duration of NaN is 0");
            Check(float.IsPositiveInfinity(D(Target(null, float.PositiveInfinity)).Duration), "a duration of +inf stays");
            PlayerTestAction negative = Action("r", 0.1f);
            negative.m_Timing.m_RepeatCount = -3;
            Check(D(negative).Repeat == 0, "a negative repeat count is 0");

            // an action's own length outside the contract is 0 — an action has no "none"
            foreach (float bad in new[] { -1f, float.NaN })
            {
                TimelineItemDesc desc = D(Action("n", bad));
                Check(desc.Duration == 0f && desc.Instant, "an action's length of " + bad + " is 0 and instant");
            }
            Check(float.IsPositiveInfinity(D(Action("n", float.PositiveInfinity)).Duration), "an action's length of +inf stays");
            TimelineItemDesc stop = D(new HitStopItem { m_FreezeDuration = -1f });
            Check(stop.Duration == 0f && stop.Instant, "a hit stop of -1 is instant");

            // the player's multiplier and start delay
            FeedbackScheduler scheduler = Scheduler();
            FeedbackPlayer player = Player(scheduler, Action("a", 0.5f), Action("b", 0.25f, StartRule.AfterPrevious));
            Check(player.EnsureRuntime() && Near(player.Runtime.Nominal.TotalSeconds, 0.75f), "the nominal of the plain list");
            foreach (float bad in new[] { -1f, float.NaN, float.PositiveInfinity })
            {
                player.m_DurationMultiplier = bad;
                player.Runtime.ResolveNominal();
                Check(Near(player.Runtime.Nominal.TotalSeconds, 0.75f), "a multiplier of " + bad + " is 1");
            }
            player.m_DurationMultiplier = 1f;
            foreach (float bad in new[] { -1f, float.NaN, float.PositiveInfinity })
            {
                player.m_StartDelay = bad;
                player.Runtime.ResolveNominal();
                Check(Near(player.Runtime.Nominal.TotalSeconds, 0.75f), "a start delay of " + bad + " is 0");
            }
            player.m_StartDelay = 0f;
            player.m_DurationMultiplier = 0f;
            player.Runtime.ResolveNominal();
            Check(player.Runtime.Nominal.TotalSeconds == 0.0, "a multiplier of 0 is 0");
            player.m_DurationMultiplier = 2f;
            player.m_StartDelay = 0.1f;
            player.Runtime.ResolveNominal();
            Check(Near(player.Runtime.Nominal.TotalSeconds, 1.6f), "the start delay takes no multiplier");

            // a list of values outside the contract resolves finite and seekable
            PlayerTestAction pulled = Action("b", 0.5f, StartRule.AfterPrevious);
            pulled.m_Timing.m_Delay = -0.3f;
            FeedbackPlayer cut = Player(scheduler, Action("a", 0.5f), pulled, Target(null, float.NaN));
            Check(cut.EnsureRuntime() && Near(cut.Runtime.Nominal.TotalSeconds, 1f) && cut.Runtime.Nominal.IsSeekable && cut.Runtime.Program.IsSeekable, "a cut list is finite and seekable");
            End();
        }

        // ---- the list's shape · a null slot's clock

        [Test]
        public void CE03_ListShape()
        {
            FeedbackScheduler scheduler = Scheduler();
            FeedbackPlayer player = Player(scheduler,
                Action("a", 0.5f),                              // 0
                null,                                           // 1 a missing type
                Action("off", 0.2f, active: false),             // 2 an inactive effect
                new WaitItem { m_Active = false },              // 3 an inactive flow item — not in the program
                Action("b", 0f));                               // 4
            Check(player.EnsureRuntime(), "prepared");
            PlayerRuntime runtime = player.Runtime;
            Check(runtime.ItemCount == 4, "program items " + runtime.ItemCount + " (4)");
            if (runtime.ItemCount == 4)
            {
                Check(runtime.ListIndex(0) == 0 && runtime.ListIndex(1) == 1 && runtime.ListIndex(2) == 2 && runtime.ListIndex(3) == 4, "the number table 0 1 2 4");
                Check(runtime.Binding(0) != null && runtime.Binding(3) != null, "the active effects are bound");
                Check(runtime.Binding(1) == null && runtime.Skip[1] && runtime.Program.ItemInstant(1), "the null slot keeps its place, skipped and instant");
                Check(runtime.Binding(2) == null && runtime.Skip[2], "the inactive effect keeps its place, skipped");
                Check(!runtime.Skip[0] && !runtime.Skip[3], "the bound items are not skipped");
            }
            FeedbackPlayer empty = Player(scheduler);
            Check(empty.EnsureRuntime() && empty.Runtime.ItemCount == 0 && empty.Runtime.Nominal.TotalSeconds == 0.0, "an empty list prepares with a nominal of 0");

            // a null slot takes the clock of the nearest item of the list — it brings no clock the list does not have
            FeedbackPlayer middle = Player(scheduler, Unscaled("a"), null, Unscaled("b"));
            middle.EnsureRuntime();
            Check(middle.Runtime.ItemCount == 3 && middle.Runtime.Program.ItemClock(1) == TimelineClock.Unscaled, "a null slot takes the clock of the item before");
            Check(middle.Runtime.Program.ClockMask == 1 << (int)TimelineClock.Unscaled, "a null slot adds no clock to a list of one clock");
            FeedbackPlayer leading = Player(scheduler, null, Unscaled("a"));
            leading.EnsureRuntime();
            Check(leading.Runtime.ItemCount == 2 && leading.Runtime.Program.ItemClock(0) == TimelineClock.Unscaled, "a leading null slot takes the clock of the item after");
            FeedbackPlayer plain = Player(scheduler, Action("a", 0.2f), null);
            plain.EnsureRuntime();
            Check(plain.Runtime.ItemCount == 2 && plain.Runtime.Program.ItemClock(1) == TimelineClock.Player, "in a list of default clocks a null slot follows the player");
            FeedbackPlayer only = Player(scheduler, null, null);
            only.EnsureRuntime();
            Check(only.Runtime.ItemCount == 2 && only.Runtime.Program.ItemClock(0) == TimelineClock.Player, "a list of null slots follows the player");
            End();
        }

        // ---- the bindings' prepare — an item's failure is its own

        [Test]
        public void CE04_BindingPrepare()
        {
            FeedbackScheduler scheduler = Scheduler();
            FeedbackPlayer player = Player(scheduler, Action("a", 0.5f), new PlayerTestUnregistered(), Action("refuse", 0.4f), Action("throw", 0.3f), Action("b", 0.2f));
            player.m_Policy = PlayPolicy.Overlap;
            player.m_MaxOverlap = 3;
            PlayerTestBinding.Refuses.Add("refuse");
            PlayerTestBinding.Throws.Add("throw");
            // logged once — a second one fails the test
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: test prepare throw");
            Check(player.EnsureRuntime(), "prepared");
            PlayerRuntime runtime = player.Runtime;
            Check(PlayerTestBinding.Made.Count == 4 && PlayerTestBinding.Made.All(b => b.Prepared == 1), "Create and Prepare once per active effect item that is registered");
            PlayerTestBinding a = Bound("a");
            Check(a != null && a.Context.Owner == player.gameObject && a.Context.Player == player && a.Context.Context == scheduler.Context && a.Context.MaxOverlap == 3, "the prepare context: the owner, the player, the context, N");
            Check(runtime.ItemCount == 5, "every item keeps its place (" + runtime.ItemCount + " of 5)");
            if (runtime.ItemCount == 5)
            {
                Check(runtime.Binding(0) != null && runtime.Binding(4) != null && !runtime.Skip[0] && !runtime.Skip[4], "the others are prepared");
                for (int i = 1; i <= 3; i++)
                    Check(runtime.Binding(i) == null && runtime.Skip[i], "item " + i + " is skipped");
            }
            Check(Bound("refuse") != null && Bound("refuse").Released == 1, "a binding that refused is released once");
            Check(Bound("throw") != null && Bound("throw").Released == 1, "a binding that threw is released once");
            Check(a != null && a.Released == 0 && Bound("b") != null && Bound("b").Released == 0, "a prepared binding is not released");
            End();
        }

        // ---- the warnings' units and the order of one prepare's warnings

        [Test]
        public void CE05_Warnings()
        {
            FeedbackScheduler scheduler = Scheduler();
            for (int i = 0; i < 3; i++)
                Player(scheduler, new PlayerTestUnregistered(), new PlayerTestUnregistered(), new PlayerTestUnregisteredToo()).EnsureRuntime();
            Check(Warnings(PlayerRuntime.UnregisteredWarning) == 2, "three players and two unregistered types warn twice (" + Warnings(PlayerRuntime.UnregisteredWarning) + ")");
            PlayerRuntime.ResetSession();
            Player(scheduler, new PlayerTestUnregistered()).EnsureRuntime();
            Check(Warnings(PlayerRuntime.UnregisteredWarning) == 3, "a new session warns again (" + Warnings(PlayerRuntime.UnregisteredWarning) + ")");

            PlayerTestBinding.Refuses.Add("refuse");
            Player(scheduler, Action("refuse", 0.1f), Action("refuse", 0.1f)).EnsureRuntime();
            Check(Warnings(PlayerRuntime.SkippedWarning) == 2, "two refusing items warn twice (" + Warnings(PlayerRuntime.SkippedWarning) + ")");

            Player(scheduler, null, null, Action("a", 0.1f)).EnsureRuntime();
            Check(Warnings(PlayerRuntime.MissingItemWarning) == 1, "two null slots warn once (" + Warnings(PlayerRuntime.MissingItemWarning) + ")");

            FeedbackPlayer newer = Player(scheduler, Action("a", 0.1f));
            newer.m_FormatVersion = FeedbackFormat.Current + 1;
            newer.EnsureRuntime();
            Check(Warnings(FeedbackPlayer.NewerFormatWarning) == 1, "a newer format warns");
            newer.Rebuild();
            Check(Warnings(FeedbackPlayer.NewerFormatWarning) == 1, "a newer format warns once per player (" + Warnings(FeedbackPlayer.NewerFormatWarning) + ")");
            Check(newer.m_FormatVersion == FeedbackFormat.Current + 1, "the prepare leaves a newer version as it is");

            // one prepare: the newer version → the items' (list order) → the null slots, even when the null slot comes first
            s_Warnings.Clear();
            PlayerRuntime.ResetSession();
            FeedbackPlayer all = Player(scheduler, null, new PlayerTestUnregistered(), Action("refuse", 0.1f), Action("a", 0.1f));
            all.m_FormatVersion = FeedbackFormat.Current + 1;
            all.EnsureRuntime();
            string[] order = { FeedbackPlayer.NewerFormatWarning, PlayerRuntime.UnregisteredWarning, PlayerRuntime.SkippedWarning, PlayerRuntime.MissingItemWarning };
            Check(s_Warnings.Count == 4, "one prepare's warnings (" + s_Warnings.Count + " of 4)");
            for (int i = 0; i < order.Length && i < s_Warnings.Count; i++)
                Check(s_Warnings[i].StartsWith(order[i], StringComparison.Ordinal), "warning " + i + " is: " + order[i].Substring(0, 30));
            End();
        }

        // ---- the records and the reservation

        [Test]
        public void CE06_RecordsAndReservation()
        {
            var cases = new (PlayPolicy Policy, int Overlap, int Expected)[]
            {
                (PlayPolicy.Overlap, 0, 1), (PlayPolicy.Overlap, 1, 1), (PlayPolicy.Overlap, 4, 4), (PlayPolicy.Overlap, 16, 16), (PlayPolicy.Overlap, 40, 16),
                (PlayPolicy.Ignore, 4, 1), (PlayPolicy.Restart, 4, 1),
            };
            foreach (var c in cases)
            {
                FeedbackScheduler scheduler = Scheduler();
                FeedbackPlayer player = Player(scheduler, Action("a", 0.5f));
                player.m_Policy = c.Policy;
                player.m_MaxOverlap = c.Overlap;
                player.EnsureRuntime();
                string name = c.Policy + " " + c.Overlap;
                Check(player.Runtime != null && player.Runtime.RecordCount == c.Expected, name + ": records " + (player.Runtime != null ? player.Runtime.RecordCount : -1) + " (" + c.Expected + ")");
                Check(scheduler.ReservedCount == c.Expected, name + ": reserved " + scheduler.ReservedCount + " (" + c.Expected + ")");
                PlayerTestBinding binding = PlayerTestBinding.Made.LastOrDefault();
                Check(binding != null && binding.Context.MaxOverlap == c.Expected, name + ": the context's N");
            }

            // a record's set is its own
            {
                FeedbackPlayer player = Player(Scheduler(), Action("a", 0.25f), Action("b", 0.25f, StartRule.AfterPrevious));
                player.m_Policy = PlayPolicy.Overlap;
                player.m_MaxOverlap = 2;
                player.EnsureRuntime();
                PlayerRuntime runtime = player.Runtime;
                Check(runtime.RecordCount == 2, "two records");
                if (runtime.RecordCount == 2)
                {
                    PlaybackRecord r0 = runtime.Record(0), r1 = runtime.Record(1);
                    runtime.Program.Resolve(default, runtime.Skip, 1f, 0f, r0.Resolved);
                    runtime.Program.Resolve(default, runtime.Skip, 2f, 0f, r1.Resolved);
                    Check(Near(r0.Resolved.TotalSeconds, 0.5f) && Near(r1.Resolved.TotalSeconds, 1f), "a record's resolve does not reach another's (" + r0.Resolved.TotalSeconds + " · " + r1.Resolved.TotalSeconds + ")");
                    Check(Near(Finish(r0.Cursor), 0.5f) && Near(Finish(r1.Cursor), 1f), "each cursor plays its own record");
                }
            }
            End();
        }

        // the time a cursor finishes at, 0.125 s ticks
        static float Finish(TimelineCursor cursor)
        {
            cursor.Begin(TimelineClock.Scaled);
            while (cursor.TryNext(out _))
            {
            }
            cursor.EndAdvance();
            float t = 0f;
            for (int i = 0; i < 64 && !cursor.IsFinished; i++)
            {
                t += 0.125f;
                cursor.BeginAdvance(0.125, 0.125);
                while (cursor.TryNext(out _))
                {
                }
                cursor.EndAdvance();
            }
            return t;
        }

        // ---- the nominal

        [Test]
        public void CE07_Nominal()
        {
            FeedbackScheduler scheduler = Scheduler();
            PlayerTestBinding.Refuses.Add("refuse");
            var wait = new WaitItem();
            wait.m_Timing.m_Rule = StartRule.AfterPrevious;
            wait.m_Timing.m_Duration = 0.25f;
            FeedbackPlayer player = Player(scheduler, Action("a", 0.5f), Action("refuse", 0.4f, StartRule.AfterPrevious), wait);
            player.EnsureRuntime();
            Check(player.Runtime != null && Near(player.Runtime.Nominal.TotalSeconds, 1.15f), "a skipped item keeps its length: 0.5 + 0.4 + 0.25 (" + (player.Runtime != null ? player.Runtime.Nominal.TotalSeconds : -1.0) + ")");
            player.m_DurationMultiplier = 2f;
            player.m_StartDelay = 0.1f;
            Check(player.Rebuild() && Near(player.Runtime.Nominal.TotalSeconds, 2.4f), "the multiplier 2 and the start delay 0.1: 2 × 1.15 + 0.1 (" + (player.Runtime != null ? player.Runtime.Nominal.TotalSeconds : -1.0) + ")");

            FeedbackPlayer loop = Player(scheduler, new LoopStartItem(), Action("x", 0.25f), new LoopEndItem { m_LoopCount = 2 });
            loop.EnsureRuntime();
            Check(loop.Runtime != null && Near(loop.Runtime.Nominal.TotalSeconds, 0.5f), "a loop of two");

            var forever = new WaitItem();
            forever.m_Timing.m_Infinite = true;
            FeedbackPlayer infinite = Player(scheduler, Action("a", 0.5f), forever);
            infinite.EnsureRuntime();
            Check(infinite.Runtime != null && double.IsPositiveInfinity(infinite.Runtime.Nominal.TotalSeconds) && !infinite.Runtime.Nominal.IsSeekable, "an infinite list is infinite and not seekable");
            End();
        }

        // ---- the release and the rebuild

        [Test]
        public void CE08_ReleaseAndRebuild()
        {
            FeedbackScheduler scheduler = Scheduler();
            PlayerTestBinding.Refuses.Add("refuse");
            FeedbackPlayer player = Player(scheduler, Action("a", 0.5f), Action("refuse", 0.4f), Action("b", 0f));
            player.m_Policy = PlayPolicy.Overlap;
            player.m_MaxOverlap = 2;
            player.EnsureRuntime();
            Check(scheduler.ReservedCount == 2, "prepared: reserved 2 (" + scheduler.ReservedCount + ")");
            player.ReleaseRuntime();
            Check(Bound("a").Released == 1 && Bound("b").Released == 1 && Bound("refuse").Released == 1, "every prepared binding is released once");
            Check(scheduler.ReservedCount == 0 && player.Runtime == null, "the reservation is back and the runtime is gone (" + scheduler.ReservedCount + ")");
            player.ReleaseRuntime();
            Check(Bound("a").Released == 1 && Bound("b").Released == 1 && scheduler.ReservedCount == 0, "a second release does nothing");

            // a release that throws: logged once, the rest is released
            PlayerTestBinding.ThrowsInRelease.Add("bad");
            FeedbackPlayer faulty = Player(scheduler, Action("bad", 0.2f), Action("c", 0.5f));
            faulty.EnsureRuntime();
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: test release bad");
            faulty.ReleaseRuntime();
            Check(Bound("c").Released == 1 && scheduler.ReservedCount == 0, "a release that throws leaves the rest released (" + scheduler.ReservedCount + ")");

            // Rebuild
            FeedbackPlayer rebuilt = Player(scheduler, Action("d", 0.5f), Action("e", 0.5f));
            rebuilt.m_Policy = PlayPolicy.Overlap;
            rebuilt.m_MaxOverlap = 2;
            rebuilt.EnsureRuntime();
            PlayerRuntime old = rebuilt.Runtime;
            PlayerTestBinding oldBinding = Bound("d");
            rebuilt.m_Items.Add(Action("f", 0.25f));
            Check(rebuilt.Rebuild(), "Rebuild is true");
            Check(rebuilt.Runtime != null && !ReferenceEquals(old, rebuilt.Runtime) && rebuilt.Runtime.ItemCount == 3, "Rebuild makes a new runtime with the edited list");
            Check(oldBinding.Released == 1 && scheduler.ReservedCount == 2, "Rebuild releases the old runtime: reserved 2 (" + scheduler.ReservedCount + ")");
            FeedbackPlayer fresh = Player(scheduler, Action("g", 0.5f));
            Check(fresh.Rebuild() && fresh.Runtime != null, "Rebuild prepares a player that is not prepared");
            End();
        }

        // ---- the scheduler — a false entry leaves no runtime. A closed scheduler is PlayerLifetimePlayTests': in edit mode a
        // closed bound one is dropped and none is left, so every shape is false

        [Test]
        public void CE09_Scheduler()
        {
            FeedbackPlayer unbound = Player(null, Action("a", 0.5f));
            Check(!unbound.EnsureRuntime() && unbound.Runtime == null && !unbound.Rebuild() && unbound.Runtime == null, "edit mode without a bound scheduler is false");

            FeedbackScheduler a = Scheduler();
            FeedbackScheduler b = Scheduler();
            FeedbackPlayer player = Player(a, Action("a", 0.5f));
            player.m_Policy = PlayPolicy.Overlap;
            player.m_MaxOverlap = 2;
            player.EnsureRuntime();
            PlayerTestBinding first = Bound("a");
            player.Bind(b);
            Check(player.EnsureRuntime(), "bound to another scheduler: true");
            Check(player.Runtime != null && player.Runtime.Scheduler == b && a.ReservedCount == 0 && b.ReservedCount == 2 && first.Released == 1, "the old runtime is released and a new one is made on the new scheduler (" + a.ReservedCount + " · " + b.ReservedCount + ")");

            // false leaves no runtime: its scheduler closed · unbound
            FeedbackScheduler c = Scheduler(ContextKind.Preview);
            FeedbackPlayer closing = Player(c, Action("a", 0.5f));
            closing.EnsureRuntime();
            c.Shutdown();
            Check(!closing.EnsureRuntime() && closing.Runtime == null, "its scheduler closed: false and no runtime");
            FeedbackScheduler d = Scheduler(ContextKind.Preview);
            FeedbackPlayer unbinding = Player(d, Action("a", 0.5f));
            unbinding.EnsureRuntime();
            unbinding.Bind(null);
            Check(!unbinding.EnsureRuntime() && unbinding.Runtime == null && d.ReservedCount == 0, "unbound in edit mode: false, no runtime, the reservation back (" + d.ReservedCount + ")");

            // a destroyed object on a bound scheduler: Rebuild is false, not an exception
            FeedbackScheduler e = Scheduler();
            FeedbackPlayer destroyed = Player(e, Action("a", 0.5f));
            destroyed.EnsureRuntime();
            Object.DestroyImmediate(destroyed.gameObject);
            bool rebuilt = true;
            string thrown = null;
            try
            {
                rebuilt = destroyed.Rebuild();
            }
            catch (Exception exception)
            {
                thrown = exception.GetType().Name;
            }
            Check(thrown == null && !rebuilt && destroyed.Runtime == null && e.ReservedCount == 0, "Rebuild on a destroyed object is false, not an exception (" + (thrown ?? rebuilt.ToString()) + ")");

            // an open Default in edit mode is not the player's
            m_SavedDefault = FeedbackScheduler.Default;
            m_DefaultSwapped = true;
            FeedbackScheduler open = Scheduler();
            FeedbackScheduler.Default = open;
            FeedbackPlayer loose = Player(null, Action("a", 0.5f));
            Check(!loose.EnsureRuntime() && loose.Runtime == null && open.ReservedCount == 0, "edit mode without a bound scheduler is false even with an open Default");
            End();
        }

        // ---- the format hook

        [Test]
        public void CE10_FormatHook()
        {
            FeedbackPlayer player = Player(null, Action("a", 0.5f));
            Check(player.m_FormatVersion == FeedbackFormat.Current, "a new component has the current version");
            player.m_FormatVersion = 0;
            ((ISerializationCallbackReceiver)player).OnAfterDeserialize();
            Check(player.m_FormatVersion == FeedbackFormat.Current, "an older version goes up to the current");
            player.m_FormatVersion = FeedbackFormat.Current + 1;
            ((ISerializationCallbackReceiver)player).OnAfterDeserialize();
            Check(player.m_FormatVersion == FeedbackFormat.Current + 1, "a newer version stays");

            player.m_FormatVersion = 0;
            FeedbackPlayer clone = Object.Instantiate(player);
            m_Made.Add(clone.gameObject);
            m_Players.Add(clone);
            Check(clone.m_FormatVersion == FeedbackFormat.Current && clone.m_Items.Count == 1 && clone.Runtime == null, "a clone goes through the hook, has the list and no runtime");
            End();
        }

        // ---- the anchors

        [Test]
        public void CE11_Anchors()
        {
            GameObject x = Anchor("x"), y = Anchor("y"), z = Anchor("z");
            FeedbackPlayer player = Player(Scheduler(), Action("a", 0.5f));
            player.SetAnchor("spawn", x);
            Check(player.m_Anchors.Count == 1 && player.TryGetAnchor("spawn", out Object found) && found == x, "SetAnchor adds a new name");
            player.SetAnchor("spawn", y);
            Check(player.m_Anchors.Count == 1 && player.TryGetAnchor("spawn", out found) && found == y, "SetAnchor replaces the same name (" + player.m_Anchors.Count + ")");
            player.SetAnchor("", x);
            player.SetAnchor(null, x);
            Check(player.m_Anchors.Count == 1, "an empty name and a null name are ignored (" + player.m_Anchors.Count + ")");
            Check(!player.TryGetAnchor("none", out _), "a name that is not there");
            var dead = new GameObject("dead");
            player.SetAnchor("dead", dead);
            Object.DestroyImmediate(dead);
            Check(!player.TryGetAnchor("dead", out _), "the lookup gives a live entry only");

            player.EnsureRuntime();
            PlayerTestBinding first = Bound("a");
            Check(first != null && first.Anchor == y, "a binding reads the anchor through the context's player");
            player.SetAnchor("spawn", z);
            Check(first != null && first.Anchor == y, "a prepared player does not see the new anchor");
            player.Rebuild();
            Check(Bound("a") != first && Bound("a").Anchor == z, "it does after Rebuild");

            // two entries of one name: the lookup gives the first live one, SetAnchor changes the first
            FeedbackPlayer twice = Player(null);
            twice.m_Anchors.Add(new PlayerAnchor { m_Name = "a", m_Object = null });
            twice.m_Anchors.Add(new PlayerAnchor { m_Name = "a", m_Object = x });
            Check(twice.TryGetAnchor("a", out found) && found == x, "two entries of one name: the lookup gives the first live one");
            twice.SetAnchor("a", y);
            Check(twice.m_Anchors.Count == 2 && twice.TryGetAnchor("a", out found) && found == y, "SetAnchor changes the first entry of the name");
            End();
        }

        // ---- the golden rows and the surface · every saved field has a reader

        static readonly string[] PlayerFields =
        {
            "m_Items", "m_Preset", "m_Anchors", "m_FormatVersion", "m_Initialization", "m_Policy", "m_MaxOverlap", "m_DurationMultiplier", "m_StartDelay",
            "m_AutoPlay", "m_TimeMode", "m_Speed", "m_RandomizeDuration", "m_DurationRange", "m_Cooldown", "m_Chance", "m_Intensity", "m_RemapIntensity",
            "m_IntensityRemap", "m_UseDistanceGate", "m_GateReference", "m_GateAnchor", "m_GateDistance", "m_UseGateFalloff", "m_GateFalloff", "m_UnityEvents",
            "m_Direction", "m_ApplyInitialState", "m_DisableBehavior",
        };
        static readonly string[] AnchorFields = { "m_Name", "m_Object" };

        [Test]
        public void CE12_GoldenAndSurface()
        {
            Check(DataModelTraitsTests.SavedFieldInfos(typeof(FeedbackPlayer)).Select(f => f.Name).OrderBy(n => n).SequenceEqual(PlayerFields.OrderBy(n => n)), "the player's saved fields are the twenty-nine");
            Check(DataModelTraitsTests.SavedFieldInfos(typeof(PlayerAnchor)).Select(f => f.Name).OrderBy(n => n).SequenceEqual(AnchorFields.OrderBy(n => n)), "the anchor's saved fields are the two");
            foreach (Type type in new[] { typeof(FeedbackPlayer), typeof(PlayerAnchor) })
            {
                foreach (FieldInfo field in DataModelTraitsTests.SavedFieldInfos(type))
                    Check(field.IsAssembly && field.Name.StartsWith("m_", StringComparison.Ordinal), type.Name + "." + field.Name + " is internal m_");
            }
            Check((int)PlayerInitialization.Awake == 0 && (int)PlayerInitialization.Start == 1 && (int)PlayerInitialization.Lazy == 2 && Enum.GetNames(typeof(PlayerInitialization)).Length == 3, "PlayerInitialization's values");
            Check((int)PlayPolicy.Ignore == 0 && (int)PlayPolicy.Restart == 1 && (int)PlayPolicy.Overlap == 2 && Enum.GetNames(typeof(PlayPolicy)).Length == 3, "PlayPolicy's values");
            Check(typeof(FeedbackPlayer).IsPublic && typeof(PlayerInitialization).IsPublic && typeof(PlayPolicy).IsPublic, "the three public types");
            Check(!typeof(PlayerAnchor).IsPublic && !typeof(PlayerRuntime).IsPublic && !typeof(PlaybackRecord).IsPublic && !typeof(TimelineConversion).IsPublic && !typeof(PrepareContext).IsPublic, "the rest is internal");
            // the surface: the playback API, two properties, six events, Seek · Skip · Restore · Signal, SetListener, the token
            // Play and PlayAsync, PlayFromEvent's three
            const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;
            Check(typeof(FeedbackPlayer).GetMethods(Declared).Where(m => !m.IsSpecialName).Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal)
                .SequenceEqual(new[] { "GetDuration", "Pause", "Play", "Play", "Play", "PlayAsync", "PlayAsync", "PlayAsync", "PlayFromEvent", "PlayFromEvent", "PlayFromEvent", "Rebuild", "Restore", "Resume", "Seek", "SetAnchor", "SetListener", "Signal", "Skip", "Stop", "Stop" }), "the declared public methods");
            Check(typeof(FeedbackPlayer).GetProperties(Declared).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(new[] { "IsPlaying", "TotalDuration" }), "the two public properties");
            Check(typeof(FeedbackPlayer).GetEvents(Declared).Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal)
                .SequenceEqual(new[] { "Completed", "Looped", "Paused", "Played", "Resumed", "Stopped" }), "the six public events");
            Check(typeof(FeedbackPlayer).GetFields(Declared).Length == 0 && typeof(FeedbackPlayer).IsSealed, "no public field, sealed");

            FeedbackPlayer player = Player(null);
            Check(player.m_Initialization == PlayerInitialization.Start && player.m_Policy == PlayPolicy.Restart && player.m_MaxOverlap == 4
                  && player.m_DurationMultiplier == 1f && player.m_StartDelay == 0f && player.m_FormatVersion == FeedbackFormat.Current
                  && player.m_Items != null && player.m_Items.Count == 0 && player.m_Anchors != null && player.m_Anchors.Count == 0, "a new component's defaults");

            // every saved field of the player has a reader in Core
            var read = new HashSet<FieldInfo>(FieldReads(typeof(FeedbackPlayer).Assembly));
            foreach (Type type in new[] { typeof(FeedbackPlayer), typeof(PlayerAnchor) })
            {
                foreach (FieldInfo field in DataModelTraitsTests.SavedFieldInfos(type))
                    Check(read.Contains(field), type.Name + "." + field.Name + " has a reader");
            }
            // the control: a field only the inspector reads
            Check(!read.Contains(typeof(FeedbackItem).GetField("m_Label", BindingFlags.Instance | BindingFlags.NonPublic)), "the control (m_Label) has no reader in Core");
            End();
        }

        // ---- a prepare does not enter itself

        [Test]
        public void CE13_Reentry([Values("ensure", "rebuild")] string label)
        {
            FeedbackScheduler scheduler = Scheduler();
            FeedbackPlayer player = Player(scheduler, Action("a", 0.5f), Action(label, 0.25f), Action("b", 0.1f));
            player.m_Policy = PlayPolicy.Overlap;
            player.m_MaxOverlap = 2;
            PlayerTestBinding.Reenters.Add(label);
            Check(player.EnsureRuntime(), "the outer prepare is true");
            Check(PlayerTestBinding.Reentered.Count == 1 && PlayerTestBinding.Reentered[0] == label + ":False", "the inner call is false (" + string.Join(",", PlayerTestBinding.Reentered) + ")");
            Check(PlayerTestBinding.Made.Count == 3 && scheduler.ReservedCount == 2, "one runtime is made (" + PlayerTestBinding.Made.Count + " bindings · reserved " + scheduler.ReservedCount + ")");
            Check(player.Runtime != null && player.Runtime.ItemCount == 3 && !player.Runtime.Skip[1], "every item is prepared");
            player.ReleaseRuntime();
            Check(scheduler.ReservedCount == 0 && PlayerTestBinding.Made.All(b => b.Released == 1), "nothing is left after the release (" + scheduler.ReservedCount + ")");
            End();
        }

        // ---- IL by reflection (no Cecil — the minimal manifest runs these too)

        static readonly Dictionary<int, OpCode> s_OpCodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => (int)(ushort)o.Value);

        // the 4-byte operands of a method's instructions
        internal static IEnumerable<(OpCode Op, int Token)> Operands(MethodBase method)
        {
            MethodBody body = method.GetMethodBody();
            if (body == null)
                yield break;
            byte[] il = body.GetILAsByteArray();
            int i = 0;
            while (i < il.Length)
            {
                int code = il[i++];
                if (code == 0xFE)
                    code = 0xFE00 | il[i++];
                OpCode op = s_OpCodes[code];
                int size;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: size = 0; break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: size = 1; break;
                    case OperandType.InlineVar: size = 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: size = 8; break;
                    case OperandType.InlineSwitch: size = 4 + 4 * BitConverter.ToInt32(il, i); break;
                    default: size = 4; break;
                }
                if (size == 4 && op.OperandType != OperandType.InlineSwitch)
                    yield return (op, BitConverter.ToInt32(il, i));
                i += size;
            }
        }

        // the types a method's instructions name (isinst · castclass · newobj's type …)
        internal static IEnumerable<Type> TypeTokens(MethodBase method)
        {
            foreach ((OpCode op, int token) in Operands(method))
            {
                if (op.OperandType != OperandType.InlineType)
                    continue;
                Type type = null;
                try
                {
                    type = method.Module.ResolveType(token);
                }
                catch (Exception)
                {
                }
                if (type != null)
                    yield return type;
            }
        }

        // the fields an assembly's methods read (ldfld · ldflda)
        internal static IEnumerable<FieldInfo> FieldReads(Assembly assembly)
        {
            const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (Type type in assembly.GetTypes())
            {
                foreach (MethodBase method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
                {
                    foreach ((OpCode op, int token) in Operands(method))
                    {
                        if (op != OpCodes.Ldfld && op != OpCodes.Ldflda)
                            continue;
                        FieldInfo field = null;
                        try
                        {
                            field = method.Module.ResolveField(token, type.IsGenericType ? type.GetGenericArguments() : null, method.IsGenericMethod ? method.GetGenericArguments() : null);
                        }
                        catch (Exception)
                        {
                        }
                        if (field != null)
                            yield return field;
                    }
                }
            }
        }
    }
}
