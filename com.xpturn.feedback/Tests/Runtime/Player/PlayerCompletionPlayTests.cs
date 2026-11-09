using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests
{
    // PlayMode — the destroy's ends and the disable's, the engine's tokens, the first runs. Default is a test scheduler on
    // the real loop (captureDeltaTime 1/90): the engine's destroy, OnDisable and destroyCancellationToken are seen in real
    // frames. A row checks everything and judges at the end. The first runs keep the test binding's log off
    public sealed class PlayerCompletionPlayTests
    {
        static readonly List<string> s_Errors = new List<string>();
        static FeedbackScheduler s_Saved;
        static FeedbackScheduler s_Test;
        readonly List<string> m_Fail = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                s_Errors.Add(type + ": " + condition);
        }

        [SetUp]
        public void SetUp()
        {
            m_Fail.Clear();
            s_Errors.Clear();
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            Application.logMessageReceived += OnLog;
            Time.captureDeltaTime = 1f / 90f;
            s_Saved = FeedbackScheduler.Default;
            s_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = s_Test;
            CompletionFrameMarks.Log.Clear();
            CompletionFrameMarks.OnUpdate = null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            CompletionFrameMarks.OnUpdate = null;
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Made.Clear();
            yield return null;
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    p.ReleaseRuntime();
            }
            m_Players.Clear();
            FeedbackScheduler.Default = s_Saved;
            if (!s_Test.IsShutdown)
                s_Test.Shutdown();
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Schedulers.Clear();
            Time.captureDeltaTime = 0f;
            Application.logMessageReceived -= OnLog;
            PlayerTestBinding.Reset();
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        sealed class L : IPlaybackListener
        {
            internal readonly string Name;
            internal readonly List<PlaybackEnd> Ends = new List<PlaybackEnd>();
            internal FeedbackPlayer Player;
            internal bool RuntimeOn;
            internal bool InCall;
            internal static bool s_InCall;

            internal L(string name, FeedbackPlayer player = null)
            {
                Name = name;
                Player = player;
            }

            public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end)
            {
                Ends.Add(end);
                InCall = s_InCall;
                if (Player is object)
                    RuntimeOn = Player.Runtime != null;
                CompletionFrameMarks.Log.Add("[" + Name + ":" + end + "]");
            }

            internal bool Once(PlaybackEnd end) => Ends.Count == 1 && Ends[0] == end;

            internal string S => Name + "[" + string.Join(",", Ends) + "]";
        }

        GameObject Go(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        FeedbackPlayer PlayerOn(GameObject owner, Action<FeedbackPlayer> set, params FeedbackItem[] items)
        {
            var p = owner.AddComponent<FeedbackPlayer>();
            m_Players.Add(p);
            p.m_Items.AddRange(items);
            set?.Invoke(p);
            p.EnsureRuntime();
            return p;
        }

        static PlayerTestAction Act(string label, float length) => new PlayerTestAction { m_Label = label, m_Length = length };

        static T Repeating<T>(T item) where T : FeedbackItem
        {
            item.m_Timing.m_RepeatForever = true;
            return item;
        }

        static PlayArgs With(IPlaybackListener l) => PlayArgs.Default.WithListener(l);

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
                yield return null;
        }

        static void DestroyInCall(GameObject go)
        {
            L.s_InCall = true;
            Object.Destroy(go);
            L.s_InCall = false;
        }

        // ---- the destroy's ends and the disable's
        [UnityTest]
        public IEnumerator OP01_Destroy()
        {
            yield return null;
            // Continue · finite: it goes on and completes · no C# events
            var events = new List<string>();
            FeedbackPlayer a = PlayerOn(Go("a"), null, Act("a", 0.3f));
            a.Completed += (pl, h) => events.Add("Completed");
            a.Stopped += (pl, h) => events.Add("Stopped");
            var la = new L("a");
            a.Play(With(la));
            yield return Frames(2);
            Object.Destroy(a.gameObject);
            yield return null;
            Check(la.Ends.Count == 0, "Continue · finite goes on past the destroy: " + la.S);
            yield return Frames(40);
            Check(la.Once(PlaybackEnd.Completed), "… and completes: " + la.S);
            Check(events.Count == 0, "no C# events from a detached runtime: " + string.Join(",", events));
            // SetListener between the Destroy call and OnDestroy · after OnDestroy
            FeedbackPlayer sb = PlayerOn(Go("sb"), null, Act("sb", 0.3f));
            PlaybackHandle hsb = sb.Play();
            yield return null;
            Object.Destroy(sb.gameObject);
            var lsb = new L("sb");
            bool between = sb.SetListener(hsb, lsb);
            yield return null;
            bool after = sb.SetListener(hsb, new L("after"));
            yield return Frames(40);
            Check(between && !after && lsb.Once(PlaybackEnd.Completed), "SetListener between true · after false: " + between + " " + after + " " + lsb.S);
            // Continue · endless and paused: Destroyed at OnDestroy
            FeedbackPlayer b = PlayerOn(Go("b"), null, Repeating(Act("b", 0.2f)));
            var lb = new L("b");
            b.Play(With(lb));
            FeedbackPlayer c = PlayerOn(Go("c"), null, Act("c", 0.5f));
            var lc = new L("c");
            c.Play(With(lc));
            c.Pause();
            yield return null;
            Object.Destroy(b.gameObject);
            Object.Destroy(c.gameObject);
            yield return null;
            Check(lb.Once(PlaybackEnd.Destroyed) && lc.Once(PlaybackEnd.Destroyed), "endless · paused: Destroyed: " + lb.S + " " + lc.S);
            // the emptied detached play: Destroyed at the next tick
            FeedbackPlayer d = PlayerOn(Go("d"), null, Act("d", 0.5f));
            var ld = new L("d");
            d.Play(With(ld));
            yield return null;
            Object.Destroy(d.gameObject);
            yield return null;
            PlayerTestBinding.Dead.Add("d");
            yield return Frames(2);
            PlayerTestBinding.Dead.Clear();
            Check(ld.Once(PlaybackEnd.Destroyed), "the emptied detached play: Destroyed: " + ld.S);
            // a detached play's token stops it — at once
            FeedbackPlayer t = PlayerOn(Go("t"), null, Act("t", 2f));
            var cts = new CancellationTokenSource();
            var lt = new L("t");
            t.Play(With(lt), cts.Token);
            yield return null;
            Object.Destroy(t.gameObject);
            yield return null;
            cts.Cancel();
            Check(lt.Once(PlaybackEnd.Stopped), "a detached play's token stops it: " + lt.S);
            // StopHold: Stopped inside the Destroy call · the Play after the call: Destroyed at OnDestroy
            FeedbackPlayer e = PlayerOn(Go("e"), x => x.m_DisableBehavior = PlayerDisableBehavior.StopHold, Act("e", 0.5f));
            var le = new L("e");
            e.Play(With(le));
            DestroyInCall(e.gameObject);
            var lf = new L("f");
            PlaybackHandle hf = e.Play(With(lf));
            yield return null;
            Check(le.Once(PlaybackEnd.Stopped) && le.InCall, "StopHold: Stopped inside the Destroy call: " + le.S + " in call " + le.InCall);
            Check(hf != default && lf.Once(PlaybackEnd.Destroyed), "the Play after the Destroy call: Destroyed: " + lf.S);
            // StopRevert: the Stopped subscriber's replay: Destroyed
            FeedbackPlayer g = PlayerOn(Go("g"), x => x.m_DisableBehavior = PlayerDisableBehavior.StopRevert, Act("g", 0.5f));
            var lg = new L("g");
            var lg2 = new L("g2");
            bool once = false;
            g.Stopped += (pl, h) =>
            {
                if (once)
                    return;
                once = true;
                pl.Play(With(lg2));
            };
            g.Play(With(lg));
            Object.Destroy(g.gameObject);
            yield return null;
            Check(lg.Once(PlaybackEnd.Stopped) && lg2.Once(PlaybackEnd.Destroyed), "StopRevert's replay: " + lg.S + " " + lg2.S);
            // SetActive(false) with StopHold: Stopped
            FeedbackPlayer sa = PlayerOn(Go("sa"), x => x.m_DisableBehavior = PlayerDisableBehavior.StopHold, Act("sa", 0.5f));
            var lsa = new L("sa");
            sa.Play(With(lsa));
            sa.gameObject.SetActive(false);
            Check(lsa.Once(PlaybackEnd.Stopped), "SetActive(false): Stopped: " + lsa.S);
            sa.gameObject.SetActive(true);
            // the timeline: a destroy (Continue · endless) from the game's Update ends before that frame's LateUpdate
            Go("marks").AddComponent<CompletionFrameMarks>();
            FeedbackPlayer tl = PlayerOn(Go("tl"), null, Repeating(Act("tl", 0.2f)));
            var ltl = new L("tl");
            tl.Play(With(ltl));
            yield return null;
            int ft = Time.frameCount + 1;
            CompletionFrameMarks.OnUpdate = f =>
            {
                if (f == ft)
                    Object.Destroy(tl.gameObject);
            };
            yield return Frames(3);
            CompletionFrameMarks.OnUpdate = null;
            string seq = string.Join(" ", CompletionFrameMarks.Log);
            Check(seq.Contains("U" + ft + "< [tl:Destroyed] L" + ft + ">"), "Destroyed between the game's Update and LateUpdate: " + seq);
            // a reservation back to where it was
            int reserved = s_Test.ReservedCount;
            FeedbackPlayer r = PlayerOn(Go("r"), null, Act("r", 0.1f));
            r.Play(With(new L("r")));
            Object.Destroy(r.gameObject);
            yield return Frames(20);
            Check(s_Test.ReservedCount == reserved, "the reservation back: " + reserved + " → " + s_Test.ReservedCount);
            // the detached play and the shutdown
            FeedbackPlayer hh = PlayerOn(Go("h"), null, Act("h", 2f));
            var lh = new L("h");
            hh.Play(With(lh));
            Object.Destroy(hh.gameObject);
            yield return Frames(2);
            s_Test.Shutdown();
            Check(lh.Once(PlaybackEnd.ContextShutdown), "detached + shutdown: " + lh.S);
            Check(s_Errors.Count == 0, "errors 0: " + string.Join(" | ", s_Errors.Take(2)));
            End();
        }

        static readonly List<(PlaybackHandle h, PlaybackEnd e, bool q)> s_Ends = new List<(PlaybackHandle, PlaybackEnd, bool)>();

        static void OnEnded(FeedbackPlayer p, PlaybackHandle h, PlaybackEnd e, bool q) => s_Ends.Add((h, e, q));

        // the diagnostics hear a destroy's ends: an endless play and a paused one end Destroyed, without an event (quiet)
        [UnityTest]
        public IEnumerator OP01b_TheDiagnosticsHearADestroy()
        {
            s_Ends.Clear();
            FeedbackDiagnostics.PlaybackEnded += OnEnded;
            try
            {
                FeedbackPlayer b = PlayerOn(Go("b"), null, Repeating(Act("b", 0.2f)));
                PlaybackHandle hb = b.Play();
                FeedbackPlayer c = PlayerOn(Go("c"), null, Act("c", 0.5f));
                PlaybackHandle hc = c.Play();
                c.Pause();
                yield return null;
                Object.Destroy(b.gameObject);
                Object.Destroy(c.gameObject);
                yield return null;
                Check(s_Ends.Where(z => z.h == hb).Select(z => (z.e, z.q)).SequenceEqual(new[] { (PlaybackEnd.Destroyed, true) }), "endless: " + string.Join(",", s_Ends.Where(z => z.h == hb)));
                Check(s_Ends.Where(z => z.h == hc).Select(z => (z.e, z.q)).SequenceEqual(new[] { (PlaybackEnd.Destroyed, true) }), "paused: " + string.Join(",", s_Ends.Where(z => z.h == hc)));
            }
            finally
            {
                FeedbackDiagnostics.PlaybackEnded -= OnEnded;
            }
            End();
        }

        // ---- the engine's tokens
        [UnityTest]
        public IEnumerator OP02_EngineTokens()
        {
            yield return null;
            FeedbackPlayer a = PlayerOn(Go("own"), null, Act("own", 0.5f));
            var la = new L("own", a);
            a.Play(With(la), a.destroyCancellationToken);
            yield return null;
            DestroyInCall(a.gameObject);
            yield return null;
            Check(la.Once(PlaybackEnd.Stopped) && !la.InCall && la.RuntimeOn, "the player's own token: Stopped at the frame's end before OnDestroy: " + la.S + " in call " + la.InCall + " runtime " + la.RuntimeOn);
            GameObject other = Go("other");
            var witness = other.AddComponent<PlayerReleaseWitness>();
            FeedbackPlayer b = PlayerOn(Go("b"), x => x.m_Policy = PlayPolicy.Overlap, Act("b", 2f));
            var lb = new L("b");
            var lb2 = new L("b2");
            b.Play(With(lb), witness.destroyCancellationToken);
            b.Play(With(lb2));
            yield return null;
            DestroyInCall(other);
            yield return null;
            Check(lb.Once(PlaybackEnd.Stopped) && !lb.InCall && lb2.Ends.Count == 0 && b.IsPlaying, "another object's token: " + lb.S + " in call " + lb.InCall + " · " + lb2.S + " playing " + b.IsPlaying);
            Check(s_Errors.Count == 0, "errors 0: " + string.Join(" | ", s_Errors.Take(2)));
            End();
        }

        // ---- the first runs — five fresh fixtures for each path
        sealed class Fixture
        {
            public FeedbackScheduler Scheduler;
            public FeedbackPlayer Player;
            public IPlaybackListener Listener;
            public CancellationTokenSource Source;
        }

        sealed class Quiet : IPlaybackListener
        {
            internal int Count;
            public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end) => Count++;
        }

        static Fixture s_F;

        Fixture Fresh(string kind)
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            var p = Go("fx").AddComponent<FeedbackPlayer>();
            m_Players.Add(p);
            p.m_Items.Add(Act("fx", 0.1f));
            p.Bind(s);
            p.EnsureRuntime();
            var f = new Fixture { Scheduler = s, Player = p, Listener = new Quiet(), Source = new CancellationTokenSource() };
            if (kind == "token" || kind == "cancel")
            {
                p.Play(default, f.Source.Token);
                p.Stop();
                s.Tick(0.05f);
            }
            return f;
        }

        static readonly (string Name, string Kind, TestDelegate Run)[] s_Paths =
        {
            ("completed", "plain", () => { s_F.Player.Play(PlayArgs.Default.WithListener(s_F.Listener)); for (int i = 0; i < 4; i++) s_F.Scheduler.Tick(0.05f); }),
            ("stop", "plain", () => { PlaybackHandle h = s_F.Player.Play(PlayArgs.Default.WithListener(s_F.Listener)); s_F.Player.Stop(h); }),
            ("set listener", "plain", () => { PlaybackHandle h = s_F.Player.Play(); s_F.Player.SetListener(h, s_F.Listener); s_F.Player.Stop(); }),
            ("restart", "plain", () => { s_F.Player.Play(PlayArgs.Default.WithListener(s_F.Listener)); s_F.Player.Play(PlayArgs.Default.WithListener(s_F.Listener)); s_F.Player.Stop(); }),
            ("shutdown", "plain", () => { s_F.Player.Play(PlayArgs.Default.WithListener(s_F.Listener)); s_F.Scheduler.Shutdown(); }),
            ("token again", "token", () => { s_F.Player.Play(PlayArgs.Default.WithListener(s_F.Listener), s_F.Source.Token); s_F.Player.Stop(); }),
            ("cancel", "cancel", () => { s_F.Player.Play(PlayArgs.Default.WithListener(s_F.Listener), s_F.Source.Token); s_F.Source.Cancel(); }),
        };

        [UnityTest]
        public IEnumerator OP03_FirstRuns()
        {
            yield return null;
            PlayerTestBinding.Logging = false;
            var rows = new List<string>();
            foreach ((string name, string kind, TestDelegate run) in s_Paths)
            {
                s_F = Fresh(kind);
                run();
                int hits = 0;
                for (int i = 0; i < 5; i++)
                {
                    s_F = Fresh(kind);
                    if (UIs.AllocatingGCMemory().ApplyTo(run).IsSuccess)
                        hits++;
                }
                if (hits == 5)
                    rows.Add(name);
            }
            int positive = 0;
            for (int i = 0; i < 5; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => s_Errors.Add(new string('x', 3)))).IsSuccess)
                    positive++;
            }
            s_Errors.RemoveAll(e => e == "xxx");
            PlayerTestBinding.Logging = true;
            Check(rows.Count == 0 && positive == 5, "first runs allocate — " + string.Join(", ", rows) + " · positive " + positive + "/5");
            End();
        }
    }
}
