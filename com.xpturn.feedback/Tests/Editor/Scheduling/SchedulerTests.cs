using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace xpTURN.Feedback.Tests.Editor
{
    // EditMode, direct Tick
    public class SchedulerTests
    {
        static readonly FrameTime s_Frame = new FrameTime(0.016f, 0.016f);

        static FeedbackScheduler Create(RecordingLayers layers = null, ContextKind kind = ContextKind.Test)
            => new FeedbackScheduler(kind, layers);

        // ---- frame boundary

        [Test]
        public void KT01_AddAfterRevertWaitsForTheNextFrame()
        {
            var scheduler = Create();
            scheduler.Reserve(4);
            var runner = new FakeRunner("A");
            scheduler.RunRevert();
            scheduler.Add(runner, 0);
            scheduler.RunUpdate(s_Frame);
            scheduler.RunLateApply();
            Assert.That(runner.Advances, Is.EqualTo(0));
            scheduler.Tick(s_Frame);
            Assert.That(runner.Advances, Is.EqualTo(1));
        }

        [Test]
        public void KT01_AddDuringThePassWaitsForTheNextFrame()
        {
            var scheduler = Create();
            scheduler.Reserve(4);
            var spawned = new FakeRunner("B");
            var spawner = new FakeRunner("A");
            bool done = false;
            spawner.OnAdvance = _ =>
            {
                if (!done)
                {
                    done = true;
                    scheduler.Add(spawned, 1);
                }
                return true;
            };
            scheduler.Add(spawner, 0);
            scheduler.Tick(s_Frame);
            Assert.That(spawned.Advances, Is.EqualTo(0));
            scheduler.Tick(s_Frame);
            Assert.That(spawned.Advances, Is.EqualTo(1));
        }

        [Test]
        public void KT01_AddThenTickAdvancesInThatTick()
        {
            var scheduler = Create();
            scheduler.Reserve(4);
            var runner = new FakeRunner("A");
            scheduler.Add(runner, 0);
            scheduler.Tick(s_Frame);
            Assert.That(runner.Advances, Is.EqualTo(1));
        }

        [Test]
        public void KT01_FrameRisesOnlyInRunRevert()
        {
            var scheduler = Create();
            int frame = scheduler.Context.Frame;
            scheduler.RunRevert();
            Assert.That(scheduler.Context.Frame, Is.EqualTo(frame + 1));
            scheduler.RunUpdate(s_Frame);
            scheduler.RunLateApply();
            Assert.That(scheduler.Context.Frame, Is.EqualTo(frame + 1));
        }

        // the pass-limit assert's partner: a replacement on full slots after RunRevert grows, never compacts
        [Test]
        public void KT01_ReplacementOnFullSlotsAfterRevertGrows()
        {
            var scheduler = Create();
            scheduler.Reserve(4);
            var handles = new PlaybackHandle[4];
            for (int i = 0; i < 4; i++)
                handles[i] = scheduler.Add(new FakeRunner("R"), i);
            scheduler.Tick(s_Frame);
            for (int i = 0; i < 4; i++)
            {
                scheduler.Remove(handles[i]);
                handles[i] = scheduler.Add(new FakeRunner("R"), i);
            }
            scheduler.RunRevert();
            LogAssert.Expect(LogType.Warning, FeedbackScheduler.ChurnWarning);
            scheduler.Remove(handles[0]);
            handles[0] = scheduler.Add(new FakeRunner("X"), 0);
            scheduler.RunUpdate(s_Frame);
            scheduler.RunLateApply();
        }

        [Test]
        public void KT02_RemovedDuringThePassIsNotAdvancedAndOrderIsKept()
        {
            var scheduler = Create();
            scheduler.Reserve(4);
            var a = new FakeRunner("A");
            var b = new FakeRunner("B");
            var c = new FakeRunner("C");
            scheduler.Add(a, 0);
            scheduler.Add(b, 1);
            PlaybackHandle hc = scheduler.Add(c, 2);
            bool done = false;
            a.OnAdvance = _ =>
            {
                if (!done)
                {
                    done = true;
                    scheduler.Remove(hc);
                }
                return true;
            };
            scheduler.Tick(s_Frame);
            Assert.That(c.Advances, Is.EqualTo(0));
            Assert.That(b.Advances, Is.EqualTo(1));
            Assert.That(scheduler.SlotCount, Is.EqualTo(2));
            Assert.That(scheduler.TryGetActive(0, out ActivePlayback first), Is.True);
            Assert.That(first.Runner, Is.SameAs(a));
            Assert.That(scheduler.TryGetActive(1, out ActivePlayback second), Is.True);
            Assert.That(second.Runner, Is.SameAs(b));
        }

        [Test]
        public void KT03_StopAndPlayAgainInsideAdvanceRemovesOnce()
        {
            var scheduler = Create();
            scheduler.Reserve(4);
            var runner = new FakeRunner("R");
            PlaybackHandle first = scheduler.Add(runner, 0);
            PlaybackHandle second = default;
            bool once = true;
            runner.OnAdvance = _ =>
            {
                if (!once)
                    return true;
                once = false;
                scheduler.Remove(first);
                second = scheduler.Add(runner, 1);
                return false;
            };
            scheduler.Tick(s_Frame);
            Assert.That(scheduler.Remove(first), Is.False);
            Assert.That(scheduler.Remove(second), Is.True);
        }

        // ---- fault isolation — slots on both sides of the thrower

        [Test]
        public void KT04_ThrowerEndsAloneAndNeighboursAdvanceOnce()
        {
            var scheduler = Create();
            scheduler.Reserve(4);
            var before = new FakeRunner("A");
            var thrower = new FakeRunner("T") { OnAdvance = _ => throw new InvalidOperationException("test advance") };
            var after = new FakeRunner("B");
            scheduler.Add(before, 0);
            PlaybackHandle ht = scheduler.Add(thrower, 1);
            scheduler.Add(after, 2);
            LogAssert.Expect(LogType.Exception, new Regex("test advance"));
            scheduler.Tick(s_Frame);
            Assert.That(before.Advances, Is.EqualTo(1));
            Assert.That(after.Advances, Is.EqualTo(1));
            Assert.That(thrower.Aborts, Is.EqualTo(1));
            Assert.That(thrower.LastReason, Is.EqualTo(AbortReason.Faulted));
            Assert.That(scheduler.Remove(ht), Is.False);
            scheduler.Tick(s_Frame);
            Assert.That(thrower.Advances, Is.EqualTo(1));
            Assert.That(before.Advances, Is.EqualTo(2));
            Assert.That(after.Advances, Is.EqualTo(2));
        }

        [Test]
        public void KT04_RunnerThatStoppedItselfBeforeThrowingGetsNoAbort()
        {
            var scheduler = Create();
            scheduler.Reserve(4);
            var runner = new FakeRunner("T");
            PlaybackHandle handle = default;
            runner.OnAdvance = _ =>
            {
                scheduler.Remove(handle);
                throw new InvalidOperationException("test advance");
            };
            handle = scheduler.Add(runner, 0);
            LogAssert.Expect(LogType.Exception, new Regex("test advance"));
            scheduler.Tick(s_Frame);
            Assert.That(runner.Aborts, Is.EqualTo(0));
        }

        [Test]
        public void KT04_HandleIsDeadInsideAbort()
        {
            var scheduler = Create();
            scheduler.Reserve(4);
            var runner = new FakeRunner("T") { OnAdvance = _ => throw new InvalidOperationException("test advance") };
            PlaybackHandle handle = default;
            bool removedInAbort = true;
            runner.OnAbort = (state, reason) => removedInAbort = scheduler.Remove(handle);
            handle = scheduler.Add(runner, 0);
            LogAssert.Expect(LogType.Exception, new Regex("test advance"));
            scheduler.Tick(s_Frame);
            Assert.That(runner.Aborts, Is.EqualTo(1));
            Assert.That(removedInAbort, Is.False);
        }

        [Test]
        public void KT05_ThrowingAbortStillLetsThePassFinish()
        {
            var layers = new RecordingLayers();
            var scheduler = Create(layers);
            scheduler.Reserve(4);
            var thrower = new FakeRunner("T")
            {
                OnAdvance = _ => throw new InvalidOperationException("test advance"),
                OnAbort = (state, reason) => throw new InvalidOperationException("test abort"),
            };
            var after = new FakeRunner("B");
            scheduler.Add(thrower, 0);
            scheduler.Add(after, 1);
            LogAssert.Expect(LogType.Exception, new Regex("test advance"));
            LogAssert.Expect(LogType.Exception, new Regex("test abort"));
            Assert.DoesNotThrow(() =>
            {
                scheduler.RunRevert();
                scheduler.RunUpdate(s_Frame);
            });
            Assert.That(after.Advances, Is.EqualTo(1));
            Assert.That(layers.UpdateApplies, Is.EqualTo(1));
            Assert.That(scheduler.SlotCount, Is.EqualTo(1));
            scheduler.RunLateApply();
        }

        [Test]
        public void KT06_FalseEndsWithoutAbortOrLog()
        {
            var scheduler = Create();
            scheduler.Reserve(4);
            var runner = new FakeRunner("R") { OnAdvance = _ => false };
            scheduler.Add(runner, 0);
            scheduler.Tick(s_Frame);
            Assert.That(scheduler.SlotCount, Is.EqualTo(0));
            Assert.That(runner.Aborts, Is.EqualTo(0));
            LogAssert.NoUnexpectedReceived();
        }

        // ---- growth warning — enough Adds for a second growth

        [Test]
        public void KT07_ShortageWarnsOnce()
        {
            var scheduler = Create();
            scheduler.Reserve(2);
            LogAssert.Expect(LogType.Warning, FeedbackScheduler.ShortageWarning);
            for (int i = 0; i < 12; i++)
                scheduler.Add(new FakeRunner("R"), i);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void KT07_ReplacingTwiceInAFrameWarnsChurnOnce()
        {
            var scheduler = Create();
            scheduler.Reserve(2);
            var handles = new PlaybackHandle[2];
            for (int i = 0; i < 2; i++)
                handles[i] = scheduler.Add(new FakeRunner("R"), i);
            scheduler.Tick(s_Frame);
            for (int i = 0; i < 2; i++)
            {
                scheduler.Remove(handles[i]);
                handles[i] = scheduler.Add(new FakeRunner("R"), i);
            }
            LogAssert.Expect(LogType.Warning, FeedbackScheduler.ChurnWarning);
            for (int round = 0; round < 8; round++)
            {
                scheduler.Remove(handles[0]);
                handles[0] = scheduler.Add(new FakeRunner("R"), 0);
            }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void KT07_InsideTheReservationAndReserveGrowthAreSilent()
        {
            var scheduler = Create();
            scheduler.Reserve(2);
            scheduler.Add(new FakeRunner("R"), 0);
            scheduler.Add(new FakeRunner("R"), 1);
            scheduler.Reserve(10);
            for (int i = 0; i < 5; i++)
                scheduler.Add(new FakeRunner("R"), i);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void KT07_PreviewDoesNotWarn()
        {
            var scheduler = Create(null, ContextKind.Preview);
            for (int i = 0; i < 12; i++)
                scheduler.Add(new FakeRunner("R"), i);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void KT08_GrowthDuringThePassThenFalseEndsThePlayback()
        {
            var scheduler = Create();
            scheduler.Reserve(1);
            var runner = new FakeRunner("R");
            var spawned = new FakeRunner("S");
            bool once = true;
            runner.OnAdvance = _ =>
            {
                if (!once)
                    return true;
                once = false;
                scheduler.Add(spawned, 1);
                return false;
            };
            scheduler.Add(runner, 0);
            LogAssert.Expect(LogType.Warning, FeedbackScheduler.ShortageWarning);
            scheduler.Tick(s_Frame);
            scheduler.Tick(s_Frame);
            Assert.That(runner.Advances, Is.EqualTo(1));
            Assert.That(spawned.Advances, Is.EqualTo(1));
        }

        // ---- service isolation

        [Test]
        public void KT09_ThrowingServiceIsClosedAtOnceAndOthersGoOn()
        {
            var scheduler = Create();
            scheduler.Reserve(2);
            var thrower = new TestService(scheduler.Context, "S1") { ThrowOnUpdate = true };
            var other = new TestService(scheduler.Context, "S2");
            scheduler.Context.AddService(thrower);
            scheduler.Context.AddService(other);
            var runner = new FakeRunner("A");
            scheduler.Add(runner, 0);
            LogAssert.Expect(LogType.Exception, new Regex("test service update S1"));
            scheduler.Tick(s_Frame);
            Assert.That(runner.Advances, Is.EqualTo(1));
            Assert.That(other.Updates, Is.EqualTo(1));
            Assert.That(thrower.Shutdowns, Is.EqualTo(1));
            Assert.That(thrower.IsDisabled && thrower.IsClosed, Is.True);
            scheduler.Tick(s_Frame);
            Assert.That(thrower.Updates, Is.EqualTo(1));
            Assert.That(other.Updates, Is.EqualTo(2));
            scheduler.Shutdown();
            Assert.That(thrower.Shutdowns, Is.EqualTo(1));
            Assert.That(other.Shutdowns, Is.EqualTo(1));
        }

        [Test]
        public void KT09_ServiceShutdownThrowingIsLoggedAfterTheUpdateException()
        {
            var scheduler = Create();
            var service = new TestService(scheduler.Context, "S1") { ThrowOnUpdate = true, ThrowOnShutdown = true };
            scheduler.Context.AddService(service);
            LogAssert.Expect(LogType.Exception, new Regex("test service update S1"));
            LogAssert.Expect(LogType.Exception, new Regex("test service shutdown S1"));
            scheduler.Tick(s_Frame);
        }

        [Test]
        public void KT09_ServiceThatClosedTheSchedulerThenThrewIsShutDownOnce()
        {
            var scheduler = Create();
            var service = new TestService(scheduler.Context, "S1") { ThrowOnUpdate = true };
            service.OnUpdate = () => scheduler.Shutdown();
            scheduler.Context.AddService(service);
            LogAssert.Expect(LogType.Exception, new Regex("test service update S1"));
            scheduler.Tick(s_Frame);
            Assert.That(service.Shutdowns, Is.EqualTo(1));
            Assert.That(service.IsDisabled && service.IsClosed, Is.True);
        }

        [Test]
        public void KT09_ServiceAddedDuringTheLoopStartsNextFrame()
        {
            var scheduler = Create();
            var added = new TestService(scheduler.Context, "S3");
            var adder = new TestService(scheduler.Context, "S1");
            bool done = false;
            adder.OnUpdate = () =>
            {
                if (!done)
                {
                    done = true;
                    scheduler.Context.AddService(added);
                }
            };
            scheduler.Context.AddService(adder);
            scheduler.Tick(s_Frame);
            Assert.That(added.Updates, Is.EqualTo(0));
            scheduler.Tick(s_Frame);
            Assert.That(added.Updates, Is.EqualTo(1));
        }

        // ---- closing

        [Test]
        public void KT10_ShutdownEndsPlaybacksClosesServicesAndRefusesAfterwards()
        {
            var layers = new RecordingLayers();
            var scheduler = Create(layers);
            scheduler.Reserve(4);
            var service = new TestService(scheduler.Context, "S");
            scheduler.Context.AddService(service);
            var a = new FakeRunner("A");
            var b = new FakeRunner("B");
            PlaybackHandle ha = scheduler.Add(a, 0);
            scheduler.Add(b, 1);
            scheduler.Shutdown();
            Assert.That(a.Aborts, Is.EqualTo(1));
            Assert.That(a.LastReason, Is.EqualTo(AbortReason.ContextShutdown));
            Assert.That(b.Aborts, Is.EqualTo(1));
            Assert.That(service.Shutdowns, Is.EqualTo(1));
            scheduler.Shutdown();
            Assert.That(a.Aborts + b.Aborts, Is.EqualTo(2));
            Assert.That(service.Shutdowns, Is.EqualTo(1));
            Assert.That(scheduler.Add(new FakeRunner("C"), 2), Is.EqualTo(default(PlaybackHandle)));
            Assert.That(scheduler.Remove(ha), Is.False);
            Assert.DoesNotThrow(() =>
            {
                scheduler.Reserve(3);
                scheduler.Unreserve(3);
            });
            int reverts = layers.Reverts;
            int applies = layers.UpdateApplies + layers.LateApplies;
            scheduler.Tick(s_Frame);
            Assert.That(a.Advances + b.Advances, Is.EqualTo(0));
            Assert.That(layers.Reverts, Is.EqualTo(reverts));
            Assert.That(layers.UpdateApplies + layers.LateApplies, Is.EqualTo(applies));
        }

        [Test]
        public void KT10_ListenerPlayingAgainAfterTheCloseGetsDefault()
        {
            var scheduler = Create();
            scheduler.Reserve(4);
            var runner = new FakeRunner("A");
            PlaybackHandle again = new PlaybackHandle(new SlotHandle(99, 99));
            bool once = true;
            runner.OnAbort = (state, reason) =>
            {
                if (!once)
                    return;
                once = false;
                again = scheduler.Add(runner, 5);
            };
            scheduler.Add(runner, 0);
            scheduler.Shutdown();
            Assert.That(again, Is.EqualTo(default(PlaybackHandle)));
        }

        [Test]
        public void KT10_OneThrowingAbortDoesNotStopTheRest()
        {
            var scheduler = Create();
            scheduler.Reserve(4);
            var service = new TestService(scheduler.Context, "S");
            scheduler.Context.AddService(service);
            var a = new FakeRunner("A") { OnAbort = (state, reason) => throw new InvalidOperationException("test abort") };
            var b = new FakeRunner("B");
            scheduler.Add(a, 0);
            scheduler.Add(b, 1);
            LogAssert.Expect(LogType.Exception, new Regex("test abort"));
            scheduler.Shutdown();
            Assert.That(b.Aborts, Is.EqualTo(1));
            Assert.That(service.Shutdowns, Is.EqualTo(1));
        }

        [Test]
        public void KT10_ServiceAddedAfterTheCloseIsClosedAtOnce()
        {
            var scheduler = Create();
            scheduler.Shutdown();
            var late = new TestService(scheduler.Context, "L");
            scheduler.Context.AddService(late);
            Assert.That(late.Shutdowns, Is.EqualTo(1));
            Assert.That(late.IsClosed, Is.True);
        }

        // ---- shutdown during the pass

        [Test]
        public void KT11_RunnerShuttingDownDuringThePass()
        {
            var scheduler = Create();
            scheduler.Reserve(4);
            var a = new FakeRunner("A");
            var b = new FakeRunner("B");
            var c = new FakeRunner("C");
            bool once = true;
            a.OnAdvance = _ =>
            {
                if (once)
                {
                    once = false;
                    scheduler.Shutdown();
                }
                return true;
            };
            scheduler.Add(a, 0);
            scheduler.Add(b, 1);
            scheduler.Add(c, 2);
            Assert.DoesNotThrow(() => scheduler.Tick(s_Frame));
            Assert.That(a.Aborts, Is.EqualTo(1));
            Assert.That(b.Aborts, Is.EqualTo(1));
            Assert.That(c.Aborts, Is.EqualTo(1));
            Assert.That(b.Advances + c.Advances, Is.EqualTo(0));
            Assert.That(scheduler.SlotCount, Is.EqualTo(0));
        }

        [Test]
        public void KT11_ServiceShuttingDownDuringStageOne()
        {
            var scheduler = Create();
            var first = new TestService(scheduler.Context, "S1");
            var second = new TestService(scheduler.Context, "S2");
            bool once = true;
            first.OnUpdate = () =>
            {
                if (once)
                {
                    once = false;
                    scheduler.Shutdown();
                }
            };
            scheduler.Context.AddService(first);
            scheduler.Context.AddService(second);
            scheduler.Tick(s_Frame);
            Assert.That(second.Updates, Is.EqualTo(0));
            Assert.That(second.Shutdowns, Is.EqualTo(1));
        }

        static FakeRunner[] AddThree(FeedbackScheduler scheduler)
        {
            scheduler.Reserve(4);
            var runners = new[] { new FakeRunner("A"), new FakeRunner("B"), new FakeRunner("C") };
            for (int i = 0; i < runners.Length; i++)
                scheduler.Add(runners[i], i);
            return runners;
        }

        static int Aborts(FakeRunner[] runners)
        {
            int count = 0;
            foreach (FakeRunner runner in runners)
                count += runner.Aborts;
            return count;
        }

        // a Shutdown inside a stage other than Update — the layers' Revert, the fourth stage the loop runs, the late layers, a
        // service's LateApply, and a registry slot's write: the Revert stage's put-back and the LateApply stage's apply. The
        // stage compacts as it ends, and the registry's values are the base
        [Test]
        public void KT11_ShutdownInsideAStageCompactsAtItsEnd()
        {
            var fails = new List<string>();
            foreach (string where in new[] { "Revert", "RevertAfterFixed", "the late layers", "a service's LateApply" })
            {
                var layers = new RecordingLayers();
                var scheduler = Create(layers);
                var service = new TestService(scheduler.Context, "S");
                scheduler.Context.AddService(service);
                FakeRunner[] runners = AddThree(scheduler);
                scheduler.Tick(s_Frame);
                Action close = () => scheduler.Shutdown();
                switch (where)
                {
                    case "Revert":
                        layers.OnRevert = close;
                        scheduler.RunRevert();
                        break;
                    case "RevertAfterFixed":
                        layers.OnRevertAfterFixed = close;
                        scheduler.RunRevert();
                        scheduler.RunRevertAfterFixed();
                        break;
                    case "the late layers":
                        layers.OnLateApply = close;
                        scheduler.Tick(s_Frame);
                        break;
                    default:
                        service.OnLateApply = close;
                        scheduler.Tick(s_Frame);
                        break;
                }
                if (!(scheduler.IsShutdown && scheduler.SlotCount == 0 && Aborts(runners) == 3))
                    fails.Add(where + " — closed " + scheduler.IsShutdown + " · slots " + scheduler.SlotCount + " · aborts " + Aborts(runners));
            }
            foreach (bool putBack in new[] { true, false })
            {
                var scheduler = Create();
                var a = new GameObject("scheduler test a").AddComponent<LayerTestTarget>();
                var c = new GameObject("scheduler test c").AddComponent<LayerTestTarget>();
                try
                {
                    foreach (LayerTestTarget target in new[] { a, c })
                    {
                        target.Value = 2f;
                        TestFloatSlot slot = scheduler.Context.Layers.GetOrCreate(target, 0, 0, () => new TestFloatSlot(target, ConsumptionStage.LateApply, Composition.Additive, false));
                        slot.Reserve(1);
                        slot.SetOffset(slot.Begin(ContributionKind.Offset), 5f);
                    }
                    FakeRunner[] runners = AddThree(scheduler);
                    scheduler.Tick(s_Frame);
                    if (putBack)
                    {
                        a.OnWrite = () => scheduler.Shutdown();
                        scheduler.RunRevert();
                    }
                    else
                    {
                        scheduler.RunRevert();
                        scheduler.RunUpdate(s_Frame);
                        a.OnWrite = () => scheduler.Shutdown();
                        scheduler.RunLateApply();
                    }
                    a.OnWrite = null;
                    if (!(scheduler.IsShutdown && scheduler.SlotCount == 0 && Aborts(runners) == 3 && a.Value == 2f && c.Value == 2f))
                        fails.Add((putBack ? "a registry put-back" : "a registry apply") + " — closed " + scheduler.IsShutdown + " · slots " + scheduler.SlotCount
                                  + " · aborts " + Aborts(runners) + " · values " + a.Value + "/" + c.Value);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(a.gameObject);
                    UnityEngine.Object.DestroyImmediate(c.gameObject);
                }
            }
            Assert.That(fails, Is.Empty);
        }

        // a scheduler closed in its LateApply stage — by the late layers or a service — raises no LayersApplied; its frame
        // before raised one (the subscription is live)
        [Test]
        public void KT11_ShutdownInsideLateApplyRaisesNothing()
        {
            var fails = new List<string>();
            FeedbackScheduler current = null;
            int applied = 0;
            Action<FeedbackScheduler> onApplied = s =>
            {
                if (s == current)
                    applied++;
            };
            FeedbackDiagnostics.LayersApplied += onApplied;
            try
            {
                foreach (bool byLayers in new[] { true, false })
                {
                    var layers = new RecordingLayers();
                    var scheduler = Create(layers);
                    current = scheduler;
                    var service = new TestService(scheduler.Context, "S");
                    scheduler.Context.AddService(service);
                    applied = 0;
                    scheduler.Tick(s_Frame);
                    int before = applied;
                    applied = 0;
                    if (byLayers)
                        layers.OnLateApply = () => scheduler.Shutdown();
                    else
                        service.OnLateApply = () => scheduler.Shutdown();
                    scheduler.Tick(s_Frame);
                    if (!(before == 1 && applied == 0 && scheduler.IsShutdown))
                        fails.Add((byLayers ? "the late layers" : "a service") + " — before " + before + " · after the close " + applied);
                }
            }
            finally
            {
                FeedbackDiagnostics.LayersApplied -= onApplied;
            }
            Assert.That(fails, Is.Empty);
        }

        // the fourth stage RunUpdate runs (the loop has not) closes the scheduler: RunUpdate stops there — no service update,
        // no pass, no assert — and the slots are compacted
        [Test]
        public void KT11_ShutdownInsideTheFourthStageOfUpdate()
        {
            var layers = new RecordingLayers();
            var scheduler = Create(layers);
            var service = new TestService(scheduler.Context, "S");
            scheduler.Context.AddService(service);
            FakeRunner[] runners = AddThree(scheduler);
            scheduler.Tick(s_Frame);
            layers.OnRevertAfterFixed = () => scheduler.Shutdown();
            scheduler.RunRevert();
            scheduler.RunUpdate(s_Frame);
            scheduler.RunLateApply();
            LogAssert.NoUnexpectedReceived();
            Assert.That(scheduler.IsShutdown, "closed");
            Assert.That(service.Updates, Is.EqualTo(1), "the first frame's update only");
            Assert.That(scheduler.SlotCount, Is.EqualTo(0), "slots");
            Assert.That(Aborts(runners), Is.EqualTo(3), "aborts");
        }

        // ---- reentrancy · once per RunRevert

        [Test]
        public void KT12_ReentrantTickIsIgnoredAndWarnedOnce()
        {
            var scheduler = Create();
            scheduler.Reserve(4);
            var a = new FakeRunner("A");
            var b = new FakeRunner("B");
            bool aOnce = true, bOnce = true;
            a.OnAdvance = _ =>
            {
                if (aOnce)
                {
                    aOnce = false;
                    scheduler.Tick(s_Frame);
                }
                return true;
            };
            b.OnAdvance = _ =>
            {
                if (bOnce)
                {
                    bOnce = false;
                    scheduler.Tick(s_Frame);
                }
                return true;
            };
            scheduler.Add(a, 0);
            scheduler.Add(b, 1);
            LogAssert.Expect(LogType.Warning, FeedbackScheduler.ReentryWarning);
            scheduler.Tick(s_Frame);
            Assert.That(a.Advances, Is.EqualTo(1));
            Assert.That(b.Advances, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void KT12_SecondStagesWithoutRevertDoNothing()
        {
            var layers = new RecordingLayers();
            var scheduler = Create(layers);
            scheduler.Reserve(4);
            var service = new TestService(scheduler.Context, "S");
            scheduler.Context.AddService(service);
            var runner = new FakeRunner("A");
            scheduler.Add(runner, 0);
            scheduler.RunRevert();
            scheduler.RunUpdate(s_Frame);
            scheduler.RunUpdate(s_Frame);
            Assert.That(runner.Advances, Is.EqualTo(1));
            Assert.That(service.Updates, Is.EqualTo(1));
            Assert.That(layers.UpdateApplies, Is.EqualTo(1));
            scheduler.RunLateApply();
            scheduler.RunLateApply();
            Assert.That(layers.LateApplies, Is.EqualTo(1));
        }

        [Test]
        public void KT12_FrameWithAReentrantTickStillAppliesLateOnce()
        {
            var layers = new RecordingLayers();
            var scheduler = Create(layers);
            scheduler.Reserve(4);
            var runner = new FakeRunner("A");
            bool once = true;
            runner.OnAdvance = _ =>
            {
                if (once)
                {
                    once = false;
                    scheduler.Tick(s_Frame);
                }
                return true;
            };
            scheduler.Add(runner, 0);
            LogAssert.Expect(LogType.Warning, FeedbackScheduler.ReentryWarning);
            scheduler.Tick(s_Frame);
            Assert.That(layers.LateApplies, Is.EqualTo(1));
        }

        // the fourth stage once per RunRevert: none before the first, the loop's and then none from RunUpdate, RunUpdate's when
        // the loop has not
        [Test]
        public void KT12_TheFourthStageRunsOncePerRevert()
        {
            var layers = new RecordingLayers();
            var scheduler = Create(layers);
            scheduler.RunRevertAfterFixed();
            Assert.That(layers.RevertsAfterFixed, Is.EqualTo(0), "before any Revert");
            scheduler.RunRevert();
            scheduler.RunRevertAfterFixed();
            scheduler.RunUpdate(s_Frame);
            scheduler.RunUpdate(s_Frame);
            Assert.That(layers.RevertsAfterFixed, Is.EqualTo(1), "the loop's");
            scheduler.RunLateApply();
            scheduler.RunRevert();
            scheduler.RunUpdate(s_Frame);
            Assert.That(layers.RevertsAfterFixed, Is.EqualTo(2), "RunUpdate's");
        }

        // ---- stage order

        [Test]
        public void KT13_TickRunsTheStagesInOrder()
        {
            var record = new CallRecord();
            var scheduler = Create(new RecordingLayers(record));
            scheduler.Context.AddService(new TestService(scheduler.Context, "S", record));
            scheduler.Add(new FakeRunner("A", record), 0);
            scheduler.Tick(s_Frame);
            Assert.That(record.Joined, Is.EqualTo("Revert,RevertAfterFixed,S.Update,A.Advance,Apply(Update),Apply(LateApply)"));
        }

        [Test]
        public void KT13_StagesCalledOnTheirOwnRecordOnlyThemselves()
        {
            var record = new CallRecord();
            var scheduler = Create(new RecordingLayers(record));
            scheduler.Add(new FakeRunner("A", record), 0);
            scheduler.RunUpdate(s_Frame);
            scheduler.RunLateApply();
            Assert.That(record.Joined, Is.EqualTo(""));
            scheduler.RunRevert();
            Assert.That(record.Joined, Is.EqualTo("Revert"));
            scheduler.RunLateApply();
            Assert.That(record.Joined, Is.EqualTo("Revert,Apply(LateApply)"));
        }

        // the stages are called one by one, as the player loop does — Tick would stop at the throw
        [Test]
        public void KT13_ThrowingLayerRevertSkipsThatFrame()
        {
            var record = new CallRecord();
            var layers = new RecordingLayers(record) { ThrowOnRevert = true };
            var scheduler = Create(layers);
            scheduler.Context.AddService(new TestService(scheduler.Context, "S", record));
            scheduler.Add(new FakeRunner("A", record), 0);
            Assert.Throws<InvalidOperationException>(() => scheduler.RunRevert());
            scheduler.RunUpdate(s_Frame);
            scheduler.RunLateApply();
            Assert.That(record.Joined, Is.EqualTo("Revert"));
            layers.ThrowOnRevert = false;
            scheduler.Tick(s_Frame);
            Assert.That(record.Joined, Is.EqualTo("Revert,Revert,RevertAfterFixed,S.Update,A.Advance,Apply(Update),Apply(LateApply)"));
        }

        // ---- query — before compaction, with a dead slot

        [Test]
        public void KT14_QueryReadsLiveSlotsOnly()
        {
            var scheduler = Create();
            scheduler.Reserve(4);
            var owner = new GameObject("scheduler test owner");
            try
            {
                var a = new FakeRunner("A") { Owner = owner };
                PlaybackHandle ha = scheduler.Add(a, 7);
                PlaybackHandle hb = scheduler.Add(new FakeRunner("B"), 8);
                var c = new FakeRunner("C");
                scheduler.Add(c, 9);
                scheduler.Remove(hb);
                Assert.That(scheduler.SlotCount, Is.EqualTo(3));
                Assert.That(scheduler.ActiveCount, Is.EqualTo(2));
                Assert.That(scheduler.TryGetActive(0, out ActivePlayback first), Is.True);
                Assert.That(first.Handle, Is.EqualTo(ha));
                Assert.That(first.Runner, Is.SameAs(a));
                Assert.That(first.State, Is.EqualTo(7));
                Assert.That(first.DebugOwner, Is.SameAs(owner));
                Assert.That(scheduler.TryGetActive(1, out _), Is.False);
                Assert.That(scheduler.TryGetActive(2, out ActivePlayback third), Is.True);
                Assert.That(third.Runner, Is.SameAs(c));
                Assert.That(scheduler.TryGetActive(scheduler.SlotCount, out _), Is.False);
                Assert.That(scheduler.TryGetActive(-1, out _), Is.False);
                Assert.That(scheduler.TryGetActive(1000, out _), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        // ---- handles

        [Test]
        public void KT15_HandlesCompareByNumberAndGeneration()
        {
            var first = Create();
            first.Reserve(2);
            var second = Create();
            second.Reserve(2);
            PlaybackHandle handle = first.Add(new FakeRunner("A"), 0);
            second.Add(new FakeRunner("B"), 0);
            Assert.That(second.Remove(handle), Is.False);
            Assert.That(first.Remove(default), Is.False);
            PlaybackHandle copy = handle;
            Assert.That(copy == handle, Is.True);
            Assert.That(copy.Equals(handle), Is.True);
            Assert.That(copy.Equals((object)handle), Is.True);
            Assert.That(copy != default, Is.True);
            Assert.That(handle.GetHashCode(), Is.EqualTo((int)handle.Generation));
            var table = new Dictionary<PlaybackHandle, int> { { handle, 5 } };
            Assert.That(table[copy], Is.EqualTo(5));
        }

        // ---- marker names — ten ticks, since one empty sample can read 0

        [Test]
        public void KT17_MarkerNamesAreRecorded()
        {
            var scheduler = Create();
            scheduler.Reserve(2);
            scheduler.Add(new FakeRunner("A"), 0);
            using (var revert = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Feedback.Revert"))
            using (var update = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Feedback.Update"))
            using (var lateApply = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Feedback.LateApply"))
            {
                for (int i = 0; i < 10; i++)
                    scheduler.Tick(s_Frame);
                Assert.That(revert.Valid && update.Valid && lateApply.Valid, Is.True);
                Assert.That(revert.CurrentValue, Is.GreaterThan(0));
                Assert.That(update.CurrentValue, Is.GreaterThan(0));
                Assert.That(lateApply.CurrentValue, Is.GreaterThan(0));
            }
        }

        // ---- main-thread assert

        [Test]
        public void KT18_AddOffTheMainThreadAsserts()
        {
            var scheduler = Create();
            scheduler.Reserve(2);
            scheduler.Add(new FakeRunner("A"), 0);
            LogAssert.Expect(LogType.Assert, FeedbackScheduler.ThreadMessage);
            Task.Run(() => scheduler.Add(new FakeRunner("B"), 1)).Wait();
        }
    }
}
