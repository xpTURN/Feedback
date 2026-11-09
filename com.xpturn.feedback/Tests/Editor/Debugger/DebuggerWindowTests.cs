using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using xpTURN.Feedback.Editor;
using T = xpTURN.Feedback.Tests.Editor.DebuggerTesting;

namespace xpTURN.Feedback.Tests.Editor
{
    // no making read, no runtime write (the IL) · the ring and the subscription (a reload · play entries) · the stage costs —
    // the tables are DebuggerTableTests'
    internal sealed class DebuggerWindowTests
    {
        [TearDown]
        public void TearDown() => T.Clean();

        // ---- the IL of the debugger's types (nested ones too) against the names it must not call

        static readonly string[] s_Making =
        {
            "FeedbackCategories.IsEnabled", "FeedbackCategories.GetScale", "FeedbackCategories.get_FlashLimit", "FeedbackTimeScale.get_BaseScale",
            "FeedbackPlayer.get_TotalDuration", "PlayerRuntime.get_TotalDuration", "FeedbackContext.get_Categories", "FeedbackContext.get_TimeScale",
            "FeedbackContext.get_Channels", "FeedbackContext.get_Settings", "FeedbackPlayer.EnsureRuntime",
        };

        static readonly string[] s_Writes =
        {
            "FeedbackCategories.SetEnabled", "FeedbackCategories.SetScale", "FeedbackCategories.SetFlashLimit", "FeedbackTimeScale.set_BaseScale",
            "CategoryGate.SetEnabled", "CategoryGate.SetScale", "CategoryGate.SetFlashLimit", "CategoryGate.TryFlash",
            "FeedbackPlayer.Play", "FeedbackPlayer.Stop", "FeedbackPlayer.Pause", "FeedbackPlayer.Resume", "FeedbackPlayer.Seek", "FeedbackPlayer.Skip",
            "FeedbackPlayer.Restore", "FeedbackPlayer.Signal", "FeedbackPlayer.SignalQuietly", "FeedbackPlayer.Rebuild",
            "TimeScaleService.Request", "TimeScaleService.Release", "TimeScaleService.SetBaseScale", "ChannelService.Send", "FeedbackChannels.Send",
        };

        static readonly Type[] s_Debugger = { typeof(FeedbackDebuggerWindow), typeof(DebuggerReader), typeof(DebuggerEvents), typeof(StageCosts), typeof(DebuggerSource), typeof(DebuggerTable), typeof(DebuggerCallSites) };

        static IEnumerable<Type> DebuggerTypes() => typeof(FeedbackDebuggerWindow).Assembly.GetTypes().Where(t =>
        {
            for (Type x = t; x != null; x = x.DeclaringType)
            {
                if (s_Debugger.Contains(x))
                    return true;
            }
            return false;
        });

        static List<string> Calls(IEnumerable<Type> types, string[] banned)
        {
            const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var found = new List<string>();
            foreach (Type type in types)
            {
                foreach (MethodBase m in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
                {
                    foreach ((OpCode op, int token) in PlayerPrepareTests.Operands(m))
                    {
                        if (op != OpCodes.Call && op != OpCodes.Callvirt && op != OpCodes.Newobj && op != OpCodes.Ldftn)
                            continue;
                        MethodBase target = null;
                        try
                        {
                            target = m.Module.ResolveMethod(token, type.IsGenericType ? type.GetGenericArguments() : null, m.IsGenericMethod ? m.GetGenericArguments() : null);
                        }
                        catch (Exception)
                        {
                        }
                        if (target != null && banned.Contains(target.DeclaringType.Name + "." + target.Name))
                            found.Add(type.Name + "." + m.Name + " → " + target.DeclaringType.Name + "." + target.Name);
                    }
                }
            }
            return found;
        }

        // no public read that makes a service or prepares a player, no making property of the context
        [Test]
        public void DT05a_NoMakingRead()
        {
            Assert.That(DebuggerTypes().Count(), Is.GreaterThan(s_Debugger.Length), "the nested types walked too");
            Assert.That(Calls(new[] { typeof(DebuggerIlControl) }, s_Making), Is.Not.Empty, "the positive control");
            Assert.That(Calls(DebuggerTypes(), s_Making), Is.Empty);
        }

        // no runtime write: the gate's switches, the player's commands, the time service's requests, a send
        [Test]
        public void DT05b_NoRuntimeWrite()
        {
            Assert.That(Calls(new[] { typeof(DebuggerIlControl) }, s_Writes), Is.Not.Empty, "the positive control");
            Assert.That(Calls(DebuggerTypes(), s_Writes), Is.Empty);
        }

        // ---- the ring and the subscription

        // one entry per event (opened twice — one window) · 205 events in 200 entries, newest first · Clear empties the ring only
        // · the handler allocates nothing · a closed window takes no event · a reopened one does
        [Test]
        public void DT08a_TheRingAndTheSubscription()
        {
            FeedbackPlayer p = T.Player("p", null);
            FeedbackDebuggerWindow w = T.Window();
            Assert.That(T.Window(), Is.SameAs(w));
            DebuggerEvents events = w.Events;
            long t0 = events.Total;
            FeedbackDiagnostics.Raise(p, PlayerEventKind.Played, default);
            Assert.That(events.Total - t0, Is.EqualTo(1), "one entry per event");
            events.Clear();
            for (int i = 0; i < 205; i++)
                FeedbackDiagnostics.Raise(p, (PlayerEventKind)(i % 6), default);
            Assert.That(events.Count, Is.EqualTo(DebuggerEvents.Size));
            Assert.That((events.At(0).Kind, events.At(DebuggerEvents.Size - 1).Kind), Is.EqualTo(((PlayerEventKind)(204 % 6), (PlayerEventKind)(5 % 6))));
            Assert.That(events.At(0).Player, Is.SameAs(p));
            long total = events.Total;
            events.Clear();
            Assert.That((events.Count, events.Total), Is.EqualTo((0, total)));
            Assert.That(T.Positive(), Is.EqualTo(5), "the positive control");
            Assert.That(T.Allocating(() =>
            {
                for (int i = 0; i < 200; i++)
                    FeedbackDiagnostics.Raise(p, PlayerEventKind.Played, default);
            }), Is.Zero, "the handler allocates");
            w.Close();
            long closed = events.Total;
            FeedbackDiagnostics.Raise(p, PlayerEventKind.Played, default);
            Assert.That(events.Total, Is.EqualTo(closed), "a closed window takes no event");
            FeedbackDebuggerWindow again = T.Window();
            long t2 = again.Events.Total;
            FeedbackDiagnostics.Raise(p, PlayerEventKind.Played, default);
            Assert.That(again.Events.Total - t2, Is.EqualTo(1));
        }

        // the overwritten count, zero after a Clear · an edit-mode row has its frame
        [Test]
        public void DT08b_OverwrittenAndFrame()
        {
            FeedbackPlayer p = T.Player("p", null);
            FeedbackDebuggerWindow w = T.Window();
            DebuggerEvents events = w.Events;
            events.Clear();
            for (int i = 0; i < 205; i++)
                FeedbackDiagnostics.Raise(p, PlayerEventKind.Played, default);
            Assert.That(events.Overwritten, Is.EqualTo(5));
            events.Clear();
            Assert.That(events.Overwritten, Is.Zero, "after a Clear");
            FeedbackDiagnostics.Raise(p, PlayerEventKind.Played, default);
            Assert.That(w.EventsTable.Text("Frame", 0), Is.EqualTo(Time.frameCount.ToString()), "the frame in edit mode");
        }

        // subscribed again after a domain reload (OnDisable before it, OnEnable after)
        [UnityTest]
        public IEnumerator DT08c_SubscribedAgainAfterAReload()
        {
            T.Window();
            yield return null;
            EditorUtility.RequestScriptReload();
            yield return new WaitForDomainReload();
            FeedbackDebuggerWindow w = Resources.FindObjectsOfTypeAll<FeedbackDebuggerWindow>().Single();
            FeedbackPlayer p = T.Player("p", null);
            long t = w.Events.Total;
            FeedbackDiagnostics.Raise(p, PlayerEventKind.Played, default);
            Assert.That(w.Events.Total - t, Is.EqualTo(1));
        }

        static IEnumerator InPlay(EnterPlayModeOptions options)
        {
            T.SetOptions(options);
            T.Window();
            yield return null;
            yield return new EnterPlayMode();
            FeedbackDebuggerWindow w = Resources.FindObjectsOfTypeAll<FeedbackDebuggerWindow>().Single();
            long t = w.Events.Total;
            var p = new GameObject("DebuggerInPlay").AddComponent<FeedbackPlayer>();
            p.m_Items.Add(T.Tween(1f, T.Self));
            p.Play();
            yield return null;
            w.Refresh();
            long got = w.Events.Total - t;
            DebuggerSourceKind kind = w.Kind;
            int plays = w.Reader.Plays.Count;
            yield return new ExitPlayMode();
            Assert.That(got, Is.GreaterThanOrEqualTo(1), "the play's events");
            Assert.That((kind, plays), Is.EqualTo((DebuggerSourceKind.Play, 1)));
        }

        // the window open across a play entry with the reload off · on: the play's events, the loop's scheduler
        [UnityTest]
        public IEnumerator DT08d_APlayEntryReloadOff() => InPlay(EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload);

        [UnityTest]
        public IEnumerator DT08e_APlayEntryReloadOn() => InPlay(EnterPlayModeOptions.None);

        // the recorders while the window is open: the markers' names, a tick's current value, a frame's last value and
        // the average, let go at the close
        [UnityTest]
        public IEnumerator DT09a_StageCosts()
        {
            FeedbackDebuggerWindow w = T.Window();
            StageCosts costs = w.Costs;
            Assert.That(StageCosts.Names, Is.EqualTo(new[] { "Feedback.Revert", "Feedback.Update", "Feedback.LateApply" }));
            Assert.That(costs.Valid(0) && costs.Valid(1) && costs.Valid(2), Is.True);
            FeedbackScheduler s = T.NewScheduler();
            T.Player("p", s, T.Tween(5f, T.Self)).Play();
            s.Tick(0.016f);
            Assert.That(costs.Current(0) > 0 && costs.Current(1) > 0 && costs.Current(2) > 0, Is.True);
            Assert.That(costs.Current(1), Is.GreaterThan(costs.Current(2)));
            Assert.That(w.Line(3, 1), Does.EndWith(" —"), "no frame yet");
            for (int i = 0; i < 40; i++)
            {
                s.Tick(0.016f);
                yield return null;
            }
            Assert.That(costs.Count(1) > 0 && costs.Last(1) > 0 && costs.Average(1) > 0, Is.True);
            Assert.That(w.Line(3, 1), Does.Contain(" µs · avg "));
            w.Close();
            yield return null;
            Assert.That(costs.Valid(0) || costs.Valid(1) || costs.Valid(2), Is.False, "the recorders let go");
        }
    }
}
