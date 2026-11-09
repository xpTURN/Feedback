using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;
using T = xpTURN.Feedback.Tests.Editor.DebuggerTesting;

namespace xpTURN.Feedback.Tests.Editor
{
    // the private-slot flag · the play rows · the selection · the services · the missed signals · the source. The reader
    // without the window (direct-tick schedulers)
    internal sealed class DebuggerReadTests
    {
        [TearDown]
        public void TearDown() => T.Clean();

        // a shared slot and two direct-write slots of one transform: the query's flag in creation order, the reader's rows the
        // same
        [Test]
        public void DT01a_TheLayerRowsCarryTheDirectFlag()
        {
            FeedbackScheduler s = T.NewScheduler();
            GameObject target = T.Go("T");
            FeedbackPlayer shared = target.AddComponent<FeedbackPlayer>();
            shared.m_Items.Add(T.Tween(1f, T.Self));
            shared.Bind(s);
            PositionItem position = T.Tween(1f, T.Direct(target.transform));
            position.m_DirectWrite = true;
            RotationItem rotation = T.Turn(1f, T.Direct(target.transform));
            rotation.m_DirectWrite = true;
            FeedbackPlayer b = T.Player("B", s, position);
            FeedbackPlayer c = T.Player("C", s, rotation);
            shared.Play();
            b.Play();
            c.Play();
            s.Tick(0.1f);
            var flags = new List<bool>();
            PropertyLayerRegistry layers = s.Context.Layers;
            for (int i = 0; i < layers.SlotCount; i++)
            {
                if (layers.TryGetSlot(i, out LayerSlotInfo info) && ReferenceEquals(info.Target, target.transform))
                    flags.Add(info.Direct);
            }
            Assert.That(flags, Is.EqualTo(new[] { false, true, true }));
            var reader = new DebuggerReader();
            reader.Read(s);
            Assert.That(reader.Slots.Where(x => ReferenceEquals(x.Target, target.transform)).Select(x => x.Direct), Is.EqualTo(flags));
        }

        // a shader property's number: its name where the engine has the table (6000.6), the number elsewhere
        [Test]
        public void DT01b_ShaderPropertyNames()
        {
            int id = Shader.PropertyToID("_BaseColor");
#if UNITY_6000_6_OR_NEWER
            Assert.That(DebuggerReader.AuxName(id), Is.EqualTo("_BaseColor"));
#else
            Assert.That(DebuggerReader.AuxName(id), Is.EqualTo(id.ToString()));
#endif
        }

        // A — a signal wait then a tween, two plays (the first with a listener and a token) · B backward · B2 paused · D a
        // driver · L a linked player
        static FeedbackScheduler Rows(out FeedbackPlayer a, out FeedbackPlayer linked, out MotionDriver driver, out CancellationTokenSource cts)
        {
            FeedbackScheduler s = T.NewScheduler();
            a = T.Player("A", s, new WaitForSignalItem(), T.Tween(1f, T.Self));
            a.m_Policy = PlayPolicy.Overlap;
            a.m_MaxOverlap = 2;
            cts = new CancellationTokenSource();
            a.Play(PlayArgs.Default.WithListener(new DebuggerTestListener()), cts.Token);
            a.Play();
            T.Player("B", s, T.Tween(1f, T.Self)).Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward));
            FeedbackPlayer paused = T.Player("B2", s, T.Tween(1f, T.Self));
            paused.Play();
            paused.Pause();
            driver = T.Go("D").AddComponent<MotionDriver>();
            driver.m_Item = new PositionItem { m_Motion = new OscillateMotion(), m_Target = T.Self };
            driver.m_Mode = MotionDriverMode.Loop;
            driver.Bind(s);
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            T.Keep(preset);
            preset.m_Items.Add(T.Tween(1f, T.Self));
            preset.m_Items.Add(T.Turn(1f, T.Self));
            linked = T.Player("L", s);
            linked.m_Preset = preset;
            linked.Play();
            s.Tick(0.1f);
            return s;
        }

        // the rows against the records (position · serial · direction · pause · seed · intensity · listener · token) · the
        // groups · the driver's · the open wait · a linked player's names
        [Test]
        public void DT02a_PlayRowsMatchTheRecords()
        {
            FeedbackScheduler s = Rows(out FeedbackPlayer a, out FeedbackPlayer linked, out MotionDriver driver, out CancellationTokenSource cts);
            var reader = new DebuggerReader();
            reader.Read(s);
            Assert.That(reader.Plays.Count, Is.EqualTo(6));
            foreach (PlayRow r in reader.Plays.Where(x => x.Kind == RunnerKind.Player))
            {
                PlayerRuntime runtime = ((FeedbackPlayer)r.Owner).Runtime;
                PlaybackRecord record = runtime.Record(r.State);
                Assert.That((r.Position, r.Serial, r.Reverse, r.Paused, r.Seed, r.Intensity, r.HasListener, r.Token),
                    Is.EqualTo((runtime.Position(r.State), record.Serial, record.Reverse, record.Paused, record.Seed, record.Intensity, record.Listener != null, record.Token)), r.Owner.name);
                Assert.That(r.Total, Is.EqualTo(record.Resolved.TotalSeconds));
            }
            Assert.That(reader.Groups.Select(g => g.Owner.name + g.Count), Is.EqualTo(new[] { "A2", "B1", "B21", "D1", "L1" }));
            PlayRow d = reader.Plays.Single(x => x.Kind == RunnerKind.Driver);
            Assert.That((d.Seed, d.DriverSeconds), Is.EqualTo((driver.Seed, driver.Seconds)));
            PlayRow first = reader.Plays.First(x => ReferenceEquals(x.Owner, a));
            Assert.That((first.OpenWaits, first.UntimedWaits, first.Reverse, first.HasListener), Is.EqualTo((1, 1, false, true)));
            Assert.That(double.IsPositiveInfinity(first.Total), Is.True);
            Assert.That(reader.Plays.Count(x => x.Reverse), Is.EqualTo(1));
            Assert.That(reader.Plays.Count(x => x.Paused), Is.EqualTo(1));
            reader.Select(reader.Plays.FindIndex(x => ReferenceEquals(x.Owner, linked)));
            reader.Read(s);
            Assert.That(Enumerable.Range(0, reader.Items.Count).Select(reader.ItemName), Is.EqualTo(new[] { "PositionItem", "RotationItem" }));
            cts.Dispose();
        }

        // off the nominal axis: a timed signal wait ended early, a hit stop under a tick whose clocks differ (a direct tick's
        // clocks are the same — a hit stop alone does not leave the axis) · a link let go before the Rebuild
        [Test]
        public void DT02b_OffNominalAndChangedNames()
        {
            FeedbackScheduler s = T.NewScheduler();
            FeedbackPlayer early = T.Player("E", s, new WaitForSignalItem { m_Timeout = 1f }, T.Tween(1f, T.Self));
            early.Play();
            FeedbackPlayer stop = T.Player("C", s, new HitStopItem { m_FreezeDuration = 0.5f, m_TimeScale = 0f }, T.Tween(1f, T.Self));
            stop.Play();
            s.Tick(0.1f);
            early.Signal();
            s.Tick(0.1f);
            var reader = new DebuggerReader();
            reader.Read(s);
            Assert.That(reader.Plays.Select(r => r.Owner.name + (r.OffNominal ? "+" : "-")), Is.EqualTo(new[] { "E+", "C-" }));
            s.Tick(new FrameTime(0f, 0.1f));
            reader.Read(s);
            Assert.That(reader.Plays.Select(r => r.Owner.name + (r.OffNominal ? "+" : "-")), Is.EqualTo(new[] { "E+", "C+" }));
            FeedbackScheduler s2 = Rows(out _, out FeedbackPlayer linked, out _, out CancellationTokenSource cts);
            reader.Read(s2);
            reader.Select(reader.Plays.FindIndex(r => ReferenceEquals(r.Owner, linked)));
            linked.m_Preset = null;
            reader.Read(s2);
            Assert.That(Enumerable.Range(0, reader.Items.Count).Select(reader.ItemName), Is.EqualTo(new[] { DebuggerReader.Changed, DebuggerReader.Changed }));
            cts.Dispose();
        }

        // a read of 200 plays and their slots allocates nothing once its rows hold (a selection too)
        [Test]
        public void DT02c_TheReadAllocatesNothing()
        {
            FeedbackScheduler s = T.NewScheduler();
            for (int i = 0; i < 200; i++)
                T.Player("r" + i, s, T.Tween(5f, T.Self)).Play();
            s.Tick(0.016f);
            var reader = new DebuggerReader();
            reader.Read(s);
            Assert.That(T.Positive(), Is.EqualTo(5), "the positive control");
            Assert.That(T.Allocating(() => reader.Read(s)), Is.Zero);
            reader.Select(5);
            reader.Read(s);
            Assert.That(T.Allocating(() => reader.Read(s)), Is.Zero, "with a selection");
            Assert.That(reader.Slots.Count, Is.EqualTo(200));
        }

        // three plays: the second picked, the first ended — the third takes the old slot number, the selection stays on its
        // play · ended · a source change clears it · the items
        [Test]
        public void DT03a_TheSelectionFollowsItsPlay()
        {
            FeedbackScheduler s = T.NewScheduler();
            FeedbackPlayer p1 = T.Player("p1", s, T.Tween(1f, T.Self));
            FeedbackPlayer p2 = T.Player("p2", s, T.Tween(1f, T.Self), T.Tween(0.5f, T.Self));
            FeedbackPlayer p3 = T.Player("p3", s, T.Tween(1f, T.Self));
            p1.Play();
            p2.Play();
            p3.Play();
            s.Tick(0.1f);
            var reader = new DebuggerReader();
            reader.Read(s);
            reader.Select(reader.Plays.FindIndex(r => ReferenceEquals(r.Owner, p2)));
            PlaybackHandle picked = reader.Selected;
            int slot = reader.Plays.Single(r => r.Handle == picked).Slot;
            p1.Stop();
            s.Tick(0.1f);
            reader.Read(s);
            Assert.That(reader.Plays.Single(r => r.Handle == reader.Selected).Owner, Is.SameAs(p2));
            Assert.That(reader.Plays.Single(r => r.Slot == slot).Owner, Is.SameAs(p3), "the old slot number holds another play");
            Assert.That(reader.Selected, Is.EqualTo(picked));
            Assert.That(reader.Plays.Single(r => r.Handle == picked).Position, Is.EqualTo(p2.Position(picked)));
            Assert.That(reader.Items.Select(i => i.ListIndex + ":" + i.Running), Is.EqualTo(new[] { "0:True", "1:True" }));
            p2.Stop();
            s.Tick(0.1f);
            reader.Read(s);
            Assert.That(reader.SelectedEnded, Is.True);
            Assert.That(reader.Items, Is.Empty);
            reader.Read(T.NewScheduler());
            Assert.That((reader.Selected, reader.SelectedOwner), Is.EqualTo((default(PlaybackHandle), (Object)null)));
        }

        // the tokens are the selected player's: a sibling play's token canceled · the selected play's own ended by its cancel,
        // the canceled token still shown
        [Test]
        public void DT03b_TheSelectedPlayersTokens()
        {
            FeedbackScheduler s = T.NewScheduler();
            FeedbackPlayer a = T.Player("A", s, new WaitForSignalItem(), T.Tween(1f, T.Self));
            a.m_Policy = PlayPolicy.Overlap;
            a.m_MaxOverlap = 2;
            var cts = new CancellationTokenSource();
            a.Play(PlayArgs.Default, cts.Token);
            a.Play();
            s.Tick(0.1f);
            var reader = new DebuggerReader();
            reader.Read(s);
            reader.Select(reader.Plays.FindLastIndex(r => ReferenceEquals(r.Owner, a)));
            reader.Read(s);
            Assert.That(reader.Tokens.Select(t => t.Used + ":" + t.Waited), Is.EqualTo(new[] { "True:True" }));
            cts.Cancel();
            reader.Read(s);
            Assert.That(reader.Tokens.Select(t => t.Canceled + ":" + t.Waited), Is.EqualTo(new[] { "True:False" }));
            cts.Dispose();
            FeedbackPlayer b = T.Player("B", s, new WaitForSignalItem(), T.Tween(1f, T.Self));
            var own = new CancellationTokenSource();
            b.Play(PlayArgs.Default, own.Token);
            s.Tick(0.1f);
            reader.Read(s);
            reader.Select(reader.Plays.FindIndex(r => ReferenceEquals(r.Owner, b)));
            own.Cancel();
            reader.Read(s);
            Assert.That(reader.SelectedEnded, Is.True);
            Assert.That(reader.Tokens.Select(t => t.Canceled), Is.EqualTo(new[] { true }), "the canceled token after its play ended");
            own.Dispose();
        }

        // before a play a read makes nothing (no service, no settings — 'not made yet' three times) · a play makes the gate ·
        // a hit stop's stack · the receivers of two channels · a channel walked with a hole · the flashes of the last second,
        // a read changing no outcome
        // the open request's row — under a timed one it says so
        [Test]
        public void DT04c_TheOpenRequestRow()
        {
            FeedbackScheduler s = T.NewScheduler();
            DebuggerSource.Override = s;
            FeedbackDebuggerWindow w = T.Window();
            var item = new TimeScaleItem { m_Motion = new SetMotion { m_Params = new SetParams { m_Value = new Vector4(0.5f, 0f, 0f, 0f) } } };
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };
            item.m_Timing.m_Duration = 0f;
            T.Player("t", s, item).Play();
            s.Tick(0.05f);
            w.Refresh();
            Assert.That(w.ServiceLines, Does.Contain("  ×0.5 open"));
            T.Player("h", s, new HitStopItem { m_FreezeDuration = 0.5f, m_TimeScale = 0.2f }).Play();
            s.Tick(0.05f);
            w.Refresh();
            Assert.That(w.ServiceLines, Does.Contain("  ×0.5 open (under the timed)"));
        }

        [Test]
        public void DT04a_ServicesAreReadNotMade()
        {
            FeedbackScheduler s = T.NewScheduler();
            FieldInfo services = typeof(FeedbackContext).GetField("m_Services", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo settings = typeof(FeedbackContext).GetField("m_Settings", BindingFlags.NonPublic | BindingFlags.Instance);
            DebuggerSource.Override = s;
            FeedbackDebuggerWindow w = T.Window();
            w.Refresh();
            Assert.That(((IList)services.GetValue(s.Context)).Count, Is.Zero);
            Assert.That(settings.GetValue(s.Context), Is.Null);
            Assert.That(s.Context.CategoriesIfMade, Is.Null);
            Assert.That(s.Context.TimeScaleIfMade, Is.Null);
            Assert.That(s.Context.ChannelsIfMade, Is.Null);
            Assert.That(s.Context.PoolsIfMade, Is.Null);
            Assert.That(w.ServiceLines.Count(x => x.EndsWith(FeedbackDebuggerWindow.NotMadeText)), Is.EqualTo(4), "the categories · the time scale · the channels · the pools");
            FeedbackPlayer p = T.Player("p", s, T.Tween(1f, T.Self));
            p.Play();
            s.Tick(0.1f);
            w.Refresh();
            Assert.That((w.Reader.Gate != null, w.Reader.TimeScale != null, w.Reader.ChannelService != null), Is.EqualTo((true, false, false)));
            T.Player("h", s, new HitStopItem { m_FreezeDuration = 0.5f, m_TimeScale = 0.2f }).Play();
            s.Tick(0.05f);
            w.Refresh();
            Assert.That((w.Reader.TimeScale.Count, w.Reader.TimeScale.ScaleAt(0)), Is.EqualTo((1, 0.2f)));
            var rx = T.Go("rx").AddComponent<FeedbackReceiver>();
            rx.m_Channel = ChannelRef.Of(1);
            rx.m_Player = p;
            rx.Bind(s);
            GameObject cam = T.Go("cam");
            cam.AddComponent<Camera>();
            var shake = cam.AddComponent<CameraShakeReceiver>();
            shake.m_Channel = ChannelRef.Of(2);
            shake.Bind(s);
            w.Refresh();
            Assert.That(w.Reader.Channels.Select(r => r.Key.Id + ":" + r.Commands + ":" + r.Shakes).OrderBy(x => x), Is.EqualTo(new[] { "1:1:0", "2:0:1" }));
            ChannelService channels = s.Context.Channels;
            var key = new ChannelKey(ChannelRef.Of(9));
            var x1 = new DebuggerTestReceiver();
            var x2 = new DebuggerTestReceiver();
            var x3 = new DebuggerTestReceiver();
            channels.Register(x1, key);
            channels.Register(x2, key);
            channels.Register(x3, key);
            int inside = -1;
            x1.OnReceive = () =>
            {
                channels.Unregister(x2, key);
                var rows = new List<ChannelRow>();
                channels.Describe(rows);
                inside = rows.Single(r => r.Key.Id == 9).Commands;
            };
            channels.Send(FeedbackCommand.Play(ChannelRef.Of(9)), false, default);
            Assert.That(inside, Is.EqualTo(2), "the live receivers inside the walk");
            FeedbackScheduler twin = T.NewScheduler();
            T.Player("t", twin, T.Tween(1f, T.Self)).Play();
            twin.Tick(0.1f);
            CategoryGate gate = s.Context.CategoriesIfMade;
            CategoryGate other = twin.Context.CategoriesIfMade;
            gate.TryFlash(2);
            other.TryFlash(2);
            Assert.That(gate.FlashesInLastSecond(s.Context.Now), Is.EqualTo(2));
            s.Tick(0.6f);
            s.Tick(0.6f);
            twin.Tick(0.6f);
            twin.Tick(0.6f);
            Assert.That(gate.FlashesInLastSecond(s.Context.Now), Is.Zero);
            Assert.That(gate.TryFlash(1), Is.EqualTo(other.TryFlash(1)));
        }

        // the read of the last second's flashes trims nothing (the window's entries stay)
        [Test]
        public void DT04b_TheFlashReadTrimsNothing()
        {
            FeedbackScheduler s = T.NewScheduler();
            T.Player("p", s, T.Tween(1f, T.Self)).Play();
            s.Tick(0.1f);
            CategoryGate gate = s.Context.CategoriesIfMade;
            gate.TryFlash(2);
            s.Tick(0.6f);
            s.Tick(0.6f);
            int before = gate.FlashEntries;
            gate.FlashesInLastSecond(s.Context.Now);
            Assert.That((before, gate.FlashEntries), Is.EqualTo((1, 1)));
        }

        // the direct way (no wait · no runtime) and the quiet way (a channel's broadcast · the preview's call) · a wait that
        // took it counts nothing · a Rebuild keeps them · the window's Missed cell and missed line
        [Test]
        public void DT06a_MissedSignals()
        {
            FeedbackScheduler s = T.NewScheduler();
            FeedbackPlayer p = T.Player("p", s, T.Tween(5f, T.Self));
            p.Play();
            p.Signal();
            p.Signal();
            FeedbackPlayer waiting = T.Player("w", s, new WaitForSignalItem(), T.Tween(1f, T.Self));
            waiting.Play();
            s.Tick(0.05f);
            waiting.Signal();
            var rx = T.Go("rx").AddComponent<FeedbackReceiver>();
            rx.m_Channel = ChannelRef.Of(3);
            rx.m_Player = p;
            rx.Bind(s);
            s.Context.Channels.Send(new FeedbackCommand(FeedbackCommandKind.Signal, ChannelRef.Of(3)), false, default);
            p.SignalQuietly();
            Assert.That((p.MissedSignals, p.MissedQuietSignals), Is.EqualTo((2, 2)));
            Assert.That((waiting.MissedSignals, waiting.MissedQuietSignals), Is.EqualTo((0, 0)));
            FeedbackPlayer bare = T.Player("bare", null);
            bare.Signal();
            bare.SignalQuietly();
            Assert.That((bare.MissedSignals, bare.MissedQuietSignals), Is.EqualTo((1, 1)));
            p.Rebuild();
            Assert.That((p.MissedSignals, p.MissedQuietSignals), Is.EqualTo((2, 2)), "a rebuild keeps them");
            DebuggerSource.Override = s;
            p.Play();
            FeedbackDebuggerWindow w = T.Window();
            w.Refresh();
            Assert.That(w.Plays.Text("Missed", w.Reader.Plays.FindIndex(r => ReferenceEquals(r.Owner, p))), Is.EqualTo("2 · 2"));
            Assert.That(w.MissedLine.text, Does.Contain("p 2 / 2"));
        }

        // the test source first · a closed Default outside play is not taken · the preview session's · none · picked again at
        // each refresh
        [Test]
        public void DT07a_TheSource()
        {
            FeedbackScheduler old = FeedbackScheduler.Default;
            try
            {
                FeedbackScheduler s = T.NewScheduler();
                DebuggerSource.Override = s;
                Assert.That(DebuggerSource.Pick(out DebuggerSourceKind test), Is.SameAs(s));
                Assert.That(test, Is.EqualTo(DebuggerSourceKind.Test));
                DebuggerSource.Override = null;
                FeedbackScheduler closed = T.NewScheduler();
                closed.Shutdown();
                FeedbackScheduler.Default = closed;
                Assert.That(DebuggerSource.Pick(out DebuggerSourceKind none), Is.Null);
                Assert.That(none, Is.EqualTo(DebuggerSourceKind.None));
                FeedbackPlayer p = T.Player("p", null, T.Tween(1f, T.Self));
                PreviewSession.Start(p, PlayArgs.Default);
                Assert.That(DebuggerSource.Pick(out DebuggerSourceKind preview), Is.SameAs(PreviewSession.Current.Scheduler));
                Assert.That(preview, Is.EqualTo(DebuggerSourceKind.Preview));
                PreviewSession.Current.End(PreviewEndReason.Stopped);
                Assert.That(DebuggerSource.Pick(out _), Is.Null);
                FeedbackScheduler s1 = T.NewScheduler();
                FeedbackScheduler s2 = T.NewScheduler();
                FeedbackDebuggerWindow w = T.Window();
                DebuggerSource.Override = s1;
                w.Refresh();
                Assert.That(w.Reader.Source, Is.SameAs(s1));
                DebuggerSource.Override = s2;
                w.Refresh();
                Assert.That(w.Reader.Source, Is.SameAs(s2), "picked again at each refresh");
            }
            finally
            {
                FeedbackScheduler.Default = old;
            }
        }

        // an open Default outside play is not taken either
        [Test]
        public void DT07b_AnOpenDefaultOutsidePlay()
        {
            FeedbackScheduler old = FeedbackScheduler.Default;
            try
            {
                FeedbackScheduler.Default = T.NewScheduler();
                Assert.That(DebuggerSource.Pick(out DebuggerSourceKind kind), Is.Null);
                Assert.That(kind, Is.EqualTo(DebuggerSourceKind.None));
            }
            finally
            {
                FeedbackScheduler.Default = old;
            }
        }

        // ---- the start and the end of a play

        // the record's start on the context's clock: a play accepted at 0.5 — its row's start 0.5 · a Seek leaves it
        [Test]
        public void DT11a_TheStartOnTheContextsClock()
        {
            FeedbackScheduler s = T.NewScheduler();
            s.Tick(0.5f);
            FeedbackPlayer p = T.Player("p", s, T.Tween(5f, T.Self));
            PlaybackHandle h = p.Play();
            var reader = new DebuggerReader();
            reader.Read(s);
            Assert.That(reader.Plays.Single().StartedAt, Is.EqualTo(0.5).Within(1e-6));
            s.Tick(0.25f);
            p.Seek(1f);
            s.Tick(0.01f);
            reader.Read(s);
            PlayRow r = reader.Plays.Single(x => x.Handle == h);
            Assert.That(r.StartedAt, Is.EqualTo(0.5).Within(1e-6), "after a Seek");
            Assert.That(reader.Now - r.StartedAt, Is.EqualTo(0.26).Within(1e-6), "the elapsed");
        }

        static readonly List<(PlaybackHandle h, PlaybackEnd e, bool q)> s_Ends = new List<(PlaybackHandle, PlaybackEnd, bool)>();
        static readonly List<(PlayerEventKind k, PlaybackHandle h)> s_Events = new List<(PlayerEventKind, PlaybackHandle)>();

        static void OnEnd(FeedbackPlayer p, PlaybackHandle h, PlaybackEnd e, bool q) => s_Ends.Add((h, e, q));

        static void OnEvent(FeedbackPlayer p, PlayerEventKind k, PlaybackHandle h) => s_Events.Add((k, h));

        // every accepted play ends once in the diagnostics, a listener or none: its reason, and quiet exactly when no Completed
        // or Stopped event came — a completion · a Stop · the policy's Restart · a Rebuild (an event) · a re-prepare for another
        // scheduler · an item's exception · the scheduler's shutdown (no event)
        [Test]
        public void DT11b_EveryEndOnceWithItsReason()
        {
            s_Ends.Clear();
            s_Events.Clear();
            PlayerTestBinding.Register();
            FeedbackDiagnostics.PlaybackEnded += OnEnd;
            FeedbackDiagnostics.PlayerEvent += OnEvent;
            var expected = new List<(string name, PlaybackHandle h, PlaybackEnd e, bool q)>();
            try
            {
                FeedbackScheduler s = T.NewScheduler();
                FeedbackPlayer c = T.Player("c", s, T.Tween(0.1f, T.Self));
                expected.Add(("a completion", c.Play(), PlaybackEnd.Completed, false));
                s.Tick(0.2f);
                FeedbackPlayer st = T.Player("st", s, T.Tween(5f, T.Self));
                expected.Add(("a Stop", st.Play(), PlaybackEnd.Stopped, false));
                st.Stop();
                FeedbackPlayer r = T.Player("r", s, T.Tween(5f, T.Self));
                expected.Add(("the Restart policy", r.Play(), PlaybackEnd.Stopped, false));
                r.Play();
                FeedbackPlayer rb = T.Player("rb", s, T.Tween(5f, T.Self));
                expected.Add(("a Rebuild", rb.Play(), PlaybackEnd.Stopped, false));
                rb.Rebuild();
                FeedbackPlayer b = T.Player("b", s, T.Tween(5f, T.Self));
                expected.Add(("another scheduler", b.Play(), PlaybackEnd.Stopped, true));
                b.Bind(T.NewScheduler());
                b.Play();
                FeedbackPlayer f = T.Player("f", s, new PlayerTestAction { m_Label = "dt11", m_Length = 1f });
                expected.Add(("an item's exception", f.Play(), PlaybackEnd.Faulted, true));
                PlayerTestBinding.ThrowsIn.Add("dt11.Evaluate");
                LogAssert.Expect(LogType.Exception, new Regex(@"test dt11\.Evaluate"));
                s.Tick(0.05f);
                PlayerTestBinding.ThrowsIn.Clear();
                FeedbackScheduler other = T.NewScheduler();
                FeedbackPlayer x = T.Player("x", other, T.Tween(5f, T.Self));
                expected.Add(("the shutdown", x.Play(), PlaybackEnd.ContextShutdown, true));
                other.Shutdown();
                foreach (var e in expected)
                    Assert.That(s_Ends.Where(z => z.h == e.h).Select(z => (z.e, z.q)), Is.EqualTo(new[] { (e.e, e.q) }), e.name);
                foreach (var z in s_Ends)
                {
                    bool loud = s_Events.Any(v => v.h == z.h && (v.k == PlayerEventKind.Completed || v.k == PlayerEventKind.Stopped));
                    Assert.That(z.q, Is.EqualTo(!loud), "quiet ⇔ no event: " + z.e);
                }
            }
            finally
            {
                FeedbackDiagnostics.PlaybackEnded -= OnEnd;
                FeedbackDiagnostics.PlayerEvent -= OnEvent;
                PlayerTestBinding.ThrowsIn.Clear();
            }
        }

        // the window's end ring: a quiet end gets a row of its own with its reason, an end with an event none · the ring keeps
        // 256 · the handler allocates nothing · a closed window hears no end
        [Test]
        public void DT11c_TheWindowHearsTheEnds()
        {
            FeedbackDebuggerWindow w = T.Window();
            DebuggerEvents events = w.Events;
            FeedbackScheduler s = T.NewScheduler();
            FeedbackPlayer p = T.Player("p", s, T.Tween(5f, T.Self));
            PlaybackHandle hp = p.Play();
            p.Bind(T.NewScheduler());
            p.Play();
            FeedbackPlayer q = T.Player("q", s, T.Tween(5f, T.Self));
            PlaybackHandle hq = q.Play();
            q.Stop();
            List<EventEntry> rows = Enumerable.Range(0, events.Count).Select(events.At).ToList();
            Assert.That(rows.Count(e => e.Quiet && e.Handle == hp && e.End == PlaybackEnd.Stopped), Is.EqualTo(1), "the re-prepare's quiet row");
            Assert.That(rows.Count(e => e.Quiet && e.Handle == hq), Is.Zero, "no quiet row for the Stop");
            Assert.That(rows.Count(e => !e.Quiet && e.Handle == hq && e.Kind == PlayerEventKind.Stopped), Is.EqualTo(1), "the Stop's row");
            Assert.That(events.EndOf(q, hq, out PlaybackEnd end) && end == PlaybackEnd.Stopped, Is.True, "the Stop's reason");
            FeedbackPlayer many = T.Player("many", s, T.Tween(5f, T.Self));
            long before = events.EndTotal;
            for (int i = 0; i < 300; i++)
            {
                many.Play();
                many.Stop();
            }
            Assert.That((events.EndCount, events.EndTotal - before), Is.EqualTo((DebuggerEvents.EndSize, 300L)));
            for (int i = 0; i < 5; i++)
                FeedbackDiagnostics.RaiseEnded(p, hp, PlaybackEnd.Stopped, true);
            // red only when all five runs allocate — 6000.3's EditMode constraint also counts allocations the delegate did not make
            Assert.That(T.Positive(), Is.EqualTo(5), "the positive control");
            Assert.That(T.Allocating(() =>
            {
                for (int i = 0; i < 10; i++)
                    FeedbackDiagnostics.RaiseEnded(p, hp, PlaybackEnd.Stopped, true);
            }), Is.LessThan(5), "the end handler allocates");
            w.Close();
            long heard = events.EndTotal;
            FeedbackDiagnostics.RaiseEnded(p, hp, PlaybackEnd.Stopped, true);
            Assert.That(events.EndTotal, Is.EqualTo(heard), "a closed window");
        }
    }
}
