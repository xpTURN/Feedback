using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // O01 ~ O08 speak in their cases only — their severities and fixes · a waiting item over a backward player is C02's (the open
    // item makes the list unable to play backward)
    public sealed class FlowRulesTests
    {
        F m_Fx;
        readonly List<Object> m_Made = new List<Object>();

        [SetUp]
        public void SetUp() => m_Fx = new F();

        [TearDown]
        public void TearDown()
        {
            m_Fx.Clean();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
        }

        GameObject Go(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        static PlayPlayerItem Play(FeedbackPlayer child, bool wait = true)
        {
            var item = new PlayPlayerItem { m_Player = child };
            item.m_Play.m_Wait = wait;
            return item;
        }

        static void Want(Object host, string id, int[] items, RuleSeverity severity)
        {
            List<RuleResult> results = F.Of(host, id);
            Assert.That(results.Select(r => r.Item).OrderBy(i => i), Is.EqualTo(items), id);
            Assert.That(results.All(r => r.Severity == severity), Is.True, id + " " + severity);
        }

        // O01: a play item that waits on its own player — the Play Player, a Random Player's entry, a chain's waiting entry; not a
        // play that does not wait, not a Player Control. Its fix clears the reference
        [Test]
        public void FQ20a_SelfWait()
        {
            FeedbackPlayer p = m_Fx.Player("FQ20a");
            var random = new RandomPlayerItem();
            random.m_Entries.Add(new WeightedPlayer { m_Player = p });
            var chain = new PlayerChainItem();
            chain.m_Entries.Add(new ChainEntry { m_Player = p, m_Wait = false });
            chain.m_Entries.Add(new ChainEntry { m_Player = p });
            var control = new PlayerControlItem();
            control.m_Players.Add(p);
            p.m_Items.AddRange(new FeedbackItem[] { Play(p), Play(p, false), random, chain, control });
            Want(p, "FB-O01", new[] { 0, 2, 3 }, RuleSeverity.Error);
            RuleResult r = F.Of(p, "FB-O01").Single(x => x.Item == 3);
            Assert.That(r.Fixes.Single().Name, Is.EqualTo("Clear"));
            FeedbackValidator.Apply(r, r.Fixes.Single());
            Assert.That((((PlayerChainItem)p.m_Items[3]).m_Entries[0].m_Player, ((PlayerChainItem)p.m_Items[3]).m_Entries[1].m_Player), Is.EqualTo((p, (FeedbackPlayer)null)), "the waiting entry cleared");
        }

        // O02: the scene's references close a waiting cycle (A waits B waits A — and through a third) — not a chain without a way back
        [Test]
        public void FQ20b_WaitCycle()
        {
            FeedbackPlayer a = m_Fx.Player("FQ20b A");
            FeedbackPlayer b = m_Fx.Player("FQ20b B");
            FeedbackPlayer c = m_Fx.Player("FQ20b C");
            FeedbackPlayer d = m_Fx.Player("FQ20b D");
            a.m_Items.Add(Play(b));
            b.m_Items.Add(Play(c));
            c.m_Items.Add(Play(a));
            d.m_Items.Add(Play(a));
            Want(a, "FB-O02", new[] { 0 }, RuleSeverity.Warning);
            Want(b, "FB-O02", new[] { 0 }, RuleSeverity.Warning);
            Want(d, "FB-O02", new int[0], RuleSeverity.Warning);
            Assert.That(F.Of(a, "FB-O02").Single().Message, Does.Contain("FQ20b B"));
            c.m_Items[0] = Play(a, false);
            Want(a, "FB-O02", new int[0], RuleSeverity.Warning);
        }

        // O03: nothing to play — no player · an empty list · empty entries (a Random Unity Event's: no listener); its fix removes them
        [Test]
        public void FQ20c_Empty()
        {
            FeedbackPlayer target = m_Fx.Player("FQ20c target");
            GameObject go = Go("FQ20c listener");
            var listener = go.AddComponent<FeedbackTestListener>();
            var random = new RandomPlayerItem();
            random.m_Entries.Add(new WeightedPlayer { m_Player = target });
            random.m_Entries.Add(new WeightedPlayer());
            var control = new PlayerControlItem();
            var rue = new RandomUnityEventItem();
            var e = new WeightedUnityEvent();
            UnityEventTools.AddVoidPersistentListener(e.m_Event, listener.Touch);
            rue.m_Entries.Add(e);
            rue.m_Entries.Add(new WeightedUnityEvent());
            var full = new PlayerChainItem();
            full.m_Entries.Add(new ChainEntry { m_Player = target });
            FeedbackPlayer p = m_Fx.Player("FQ20c", new PlayPlayerItem(), random, control, rue, full);
            Want(p, "FB-O03", new[] { 0, 1, 2, 3 }, RuleSeverity.Warning);
            Assert.That(F.Of(p, "FB-O03").Single(r => r.Item == 0).Fixes, Is.Empty);
            foreach (int item in new[] { 1, 3 })
            {
                RuleResult r = F.Of(p, "FB-O03").Single(x => x.Item == item);
                Assert.That(r.Fixes.Single().Name, Is.EqualTo("Remove empty entries"));
                FeedbackValidator.Apply(r, r.Fixes.Single());
            }
            Assert.That((((RandomPlayerItem)p.m_Items[1]).m_Entries.Count, ((RandomUnityEventItem)p.m_Items[3]).m_Entries.Count), Is.EqualTo((1, 1)));
            Want(p, "FB-O03", new[] { 0, 2 }, RuleSeverity.Warning);
        }

        // O04: no entry with a positive weight (0 · −1 · NaN) — not an empty list (O03's)
        [Test]
        public void FQ20d_Weights()
        {
            FeedbackPlayer target = m_Fx.Player("FQ20d target");
            var none = new RandomPlayerItem();
            none.m_Entries.Add(new WeightedPlayer { m_Player = target, m_Weight = 0f });
            none.m_Entries.Add(new WeightedPlayer { m_Player = target, m_Weight = float.NaN });
            var one = new RandomPlayerItem();
            one.m_Entries.Add(new WeightedPlayer { m_Player = target, m_Weight = -1f });
            one.m_Entries.Add(new WeightedPlayer { m_Player = target, m_Weight = 2f });
            var events = new RandomUnityEventItem();
            events.m_Entries.Add(new WeightedUnityEvent { m_Weight = 0f });
            FeedbackPlayer p = m_Fx.Player("FQ20d", none, one, events, new RandomPlayerItem());
            Want(p, "FB-O04", new[] { 0, 2 }, RuleSeverity.Warning);
        }

        // O05: no receiver component in the open scenes takes the channel — one on another channel does not; one on it does
        [Test]
        public void FQ20e_SignalReceiver()
        {
            FeedbackPlayer p = m_Fx.Player("FQ20e", new SignalBroadcastItem { m_Channel = ChannelRef.Of(21) });
            var other = Go("FQ20e other").AddComponent<FeedbackSignalReceiver>();
            other.m_Channel = ChannelRef.Of(22);
            Want(p, "FB-O05", new[] { 0 }, RuleSeverity.Info);
            var receiver = Go("FQ20e receiver").AddComponent<FeedbackSignalReceiver>();
            receiver.m_Channel = ChannelRef.Of(21);
            Want(p, "FB-O05", new int[0], RuleSeverity.Info);
        }

        // O06: a Debug Break — its fix makes it a Log
        [Test]
        public void FQ20f_DebugBreak()
        {
            FeedbackPlayer p = m_Fx.Player("FQ20f", new DebugItem { m_Action = DebugAction.Break }, new DebugItem { m_Action = DebugAction.Note });
            Want(p, "FB-O06", new[] { 0 }, RuleSeverity.Info);
            RuleResult r = F.Of(p, "FB-O06").Single();
            FeedbackValidator.Apply(r, r.Fixes.Single());
            Assert.That(((DebugItem)p.m_Items[0]).m_Action, Is.EqualTo(DebugAction.Log));
        }

        // O07: a waiting item in the background — not a background play that does not wait, not a wait in the foreground; its fix
        // takes it out
        [Test]
        public void FQ20g_BackgroundWait()
        {
            FeedbackPlayer target = m_Fx.Player("FQ20g target");
            PlayPlayerItem waits = Play(target);
            waits.m_Timing.m_Background = true;
            PlayPlayerItem free = Play(target, false);
            free.m_Timing.m_Background = true;
            FeedbackPlayer p = m_Fx.Player("FQ20g", waits, free, Play(target));
            Want(p, "FB-O07", new[] { 0 }, RuleSeverity.Warning);
            RuleResult r = F.Of(p, "FB-O07").Single();
            FeedbackValidator.Apply(r, r.Fixes.Single());
            Assert.That(p.m_Items[0].m_Timing.m_Background, Is.False);
        }

        // O08: a preset holding a player — its fix clears every reference of the item. C02: a waiting item over a backward player
        [Test]
        public void FQ20h_PresetPlayersAndTheBackwardPlayer()
        {
            FeedbackPlayer target = m_Fx.Player("FQ20h target");
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            preset.m_Items.Add(Play(target));
            preset.m_Items.Add(new SignalBroadcastItem());
            var control = new PlayerControlItem();
            control.m_Players.Add(target);
            preset.m_Items.Add(control);
            AssetDatabase.CreateAsset(preset, F.Dir + "/FQ20h.asset");
            Want(preset, "FB-O08", new[] { 0, 2 }, RuleSeverity.Error);
            RuleResult r = F.Of(preset, "FB-O08").Single(x => x.Item == 2);
            FeedbackValidator.Apply(r, r.Fixes.Single());
            Assert.That(((PlayerControlItem)preset.m_Items[2]).m_Players.Single(), Is.Null);
            FeedbackPlayer back = m_Fx.Player("FQ20h back", Play(target));
            back.m_Direction = PlayerDirection.Backward;
            List<RuleResult> c02 = F.Of(back, "FB-C02");
            Assert.That(c02.Single().Message, Does.Contain("a wait on another player"));
            back.m_Items[0] = Play(target, false);
            Assert.That(F.Of(back, "FB-C02"), Is.Empty, "a play that does not wait plays backward");
        }
    }
}
