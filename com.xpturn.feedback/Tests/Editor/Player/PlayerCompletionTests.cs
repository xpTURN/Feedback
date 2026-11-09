using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // EditMode — the six ends' paths, the ends inside Play, SetListener, a throwing listener, the record reused, the quiet
    // releases, the tokens on the main thread, the store's size and eviction, a cancel off the main thread, the release, a
    // channel's listener, a listener's Play, the surface. Players are bound to test schedulers the row ticks directly. A row
    // checks everything and judges at the end. Warnings and errors are counted by a log listener; a logged exception is
    // expected with LogAssert. A second play's 'not yet' is seen at the tick the first ends, a failure off the main thread by
    // its state
    public sealed class PlayerCompletionTests
    {
        static readonly List<string> s_Warnings = new List<string>();
        static readonly List<string> s_Errors = new List<string>();
        readonly List<string> m_Fail = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        static int s_Main;

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
                s_Warnings.Add(condition);
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                s_Errors.Add(type + ": " + condition);
        }

        [SetUp]
        public void SetUp()
        {
            m_Fail.Clear();
            s_Warnings.Clear();
            s_Errors.Clear();
            s_Main = Thread.CurrentThread.ManagedThreadId;
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerRuntime.ResetSession();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                {
                    try
                    {
                        p.ReleaseRuntime();
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            m_Players.Clear();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Schedulers.Clear();
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
            internal readonly List<PlaybackHandle> Handles = new List<PlaybackHandle>();
            internal readonly List<int> Threads = new List<int>();
            internal Action<PlaybackHandle, PlaybackEnd> Also;
            internal bool Throws;

            internal L(string name) => Name = name;

            public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end)
            {
                Ends.Add(end);
                Handles.Add(handle);
                Threads.Add(Thread.CurrentThread.ManagedThreadId);
                lock (PlayerTestBinding.Log)
                    PlayerTestBinding.Log.Add("|L" + Name + ":" + end);
                Also?.Invoke(handle, end);
                if (Throws)
                    throw new InvalidOperationException("listener " + Name);
            }

            internal bool Once(PlaybackEnd end) => Ends.Count == 1 && Ends[0] == end;

            internal string S => Name + "[" + string.Join(",", Ends) + "]";
        }

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        FeedbackPlayer PlayerOn(FeedbackScheduler s, Action<FeedbackPlayer> set, params FeedbackItem[] items)
        {
            var go = new GameObject("player");
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            m_Players.Add(p);
            p.m_Items.AddRange(items);
            set?.Invoke(p);
            if (s != null)
            {
                p.Bind(s);
                p.EnsureRuntime();
            }
            return p;
        }

        static PlayerTestAction Act(string label, float length) => new PlayerTestAction { m_Label = label, m_Length = length };

        static T Repeating<T>(T item) where T : FeedbackItem
        {
            item.m_Timing.m_RepeatForever = true;
            return item;
        }

        static T Delayed<T>(T item, float delay) where T : FeedbackItem
        {
            item.m_Timing.m_Delay = delay;
            return item;
        }

        static PlayArgs With(IPlaybackListener l) => PlayArgs.Default.WithListener(l);

        static void Run(FeedbackScheduler s, int n, float dt = 0.05f)
        {
            for (int i = 0; i < n; i++)
                s.Tick(dt);
        }

        static void Watch(FeedbackPlayer p)
        {
            p.Played += (pl, h) => PlayerTestBinding.Log.Add("|Played");
            p.Completed += (pl, h) => PlayerTestBinding.Log.Add("|Completed");
            p.Stopped += (pl, h) => PlayerTestBinding.Log.Add("|Stopped");
        }

        static void Clear() => PlayerTestBinding.Log.Clear();

        static string Log() => string.Join(" ", PlayerTestBinding.Log);

        static string Events() => string.Join(" ", PlayerTestBinding.Log.Where(l => l.Length > 0 && l[0] == '|'));

        static int Count(string text, string part)
        {
            int n = 0;
            for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
                n++;
            return n;
        }

        static bool CellsEmpty(FeedbackPlayer p)
        {
            for (int r = 0; r < p.Runtime.RecordCount; r++)
            {
                PlaybackRecord record = p.Runtime.Record(r);
                if (!record.Active && record.Listener != null)
                    return false;
            }
            return true;
        }

        static int Used(FeedbackPlayer p)
        {
            PlaybackTokens store = p.Runtime.Tokens;
            if (store == null)
                return 0;
            int n = 0;
            for (int i = 0; i < store.Size; i++)
            {
                if (store.At(i) != null && store.At(i).Used)
                    n++;
            }
            return n;
        }

        // which source each entry holds — the index in sources, '-' for an empty entry
        static string Held(FeedbackPlayer p, CancellationTokenSource[] sources)
        {
            PlaybackTokens store = p.Runtime.Tokens;
            if (store == null)
                return "none";
            var parts = new List<string>();
            for (int i = 0; i < store.Size; i++)
            {
                PlaybackTokens.Entry e = store.At(i);
                parts.Add(e == null || !e.Used ? "-" : Array.FindIndex(sources, t => t.Token == e.Token).ToString());
            }
            return string.Join(",", parts);
        }

        static bool Try(Action a)
        {
            try
            {
                a();
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        // ---- the six ends' paths — once each, after the event, the Play's handle, the cell emptied
        [Test]
        public void OE01_SixEnds()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = PlayerOn(s, null, Act("a", 0.1f));
            Watch(p);
            // Completed in a tick
            {
                var c = new L("c");
                Clear();
                PlaybackHandle h = p.Play(With(c));
                Run(s, 4);
                Check(c.Once(PlaybackEnd.Completed), "Completed in a tick: " + c.S);
                Check(c.Handles.Count == 1 && c.Handles[0] == h, "the Play's handle");
                Check(Events().Contains("|Completed |Lc:Completed"), "after the event: " + Events());
                Check(CellsEmpty(p), "the cell emptied after the end");
            }
            // Skip · Seek · backward
            {
                p.m_Items[0] = Act("a", 1f);
                p.Rebuild();
                var k1 = new L("skip");
                p.Play(With(k1));
                Run(s, 1);
                p.Skip();
                var k2 = new L("seek");
                p.Play(With(k2));
                p.Seek(5f);
                var k3 = new L("back");
                p.Play(PlayArgs.Default.WithDirection(PlayerDirection.Backward).WithListener(k3));
                Run(s, 25);
                Check(k1.Once(PlaybackEnd.Completed) && k2.Once(PlaybackEnd.Completed) && k3.Once(PlaybackEnd.Completed), "Skip · Seek(end) · backward: " + k1.S + " " + k2.S + " " + k3.S);
            }
            // Stopped five
            {
                var st = new[] { new L("s1"), new L("s2"), new L("s3"), new L("s4"), new L("s5") };
                Clear();
                PlaybackHandle h = p.Play(With(st[0]));
                p.Stop(h);
                p.Play(With(st[1]));
                p.Stop();
                p.Play(With(st[2]));
                p.Stop(StopMode.Revert);
                p.Play(With(st[3]));
                p.Stop(StopMode.Complete);
                p.Play(With(st[4]));
                p.Restore();
                Check(st.All(x => x.Once(PlaybackEnd.Stopped)), "Stop(handle) · Stop() · Revert · Complete · Restore: " + string.Join(" ", st.Select(x => x.S)));
                Check(Events().Contains("|Stopped |Ls1:Stopped"), "the event first: " + Events());
                Check(CellsEmpty(p), "the cells emptied");
            }
            // the policy
            {
                var r1 = new L("r1");
                var r2 = new L("r2");
                p.Play(With(r1));
                p.Play(With(r2));
                Check(r1.Once(PlaybackEnd.Stopped) && r2.Ends.Count == 0, "Restart: " + r1.S + " " + r2.S);
                p.Stop();
                FeedbackPlayer o = PlayerOn(s, x => { x.m_Policy = PlayPolicy.Overlap; x.m_MaxOverlap = 2; }, Act("o", 1f));
                var o1 = new L("o1");
                var o2 = new L("o2");
                var o3 = new L("o3");
                o.Play(With(o1));
                o.Play(With(o2));
                o.Play(With(o3));
                Check(o1.Once(PlaybackEnd.Stopped) && o2.Ends.Count == 0 && o3.Ends.Count == 0, "Overlap's oldest: " + o1.S + " " + o2.S + " " + o3.S);
                o.Stop();
            }
            // not seekable Skip
            {
                FeedbackPlayer e = PlayerOn(s, null, Repeating(Act("e", 0.2f)));
                var ek = new L("ek");
                e.Play(With(ek));
                Run(s, 1);
                e.Skip();
                Check(ek.Once(PlaybackEnd.Stopped), "a not seekable play's Skip: " + ek.S);
            }
            // Faulted in a tick
            {
                FeedbackPlayer f = PlayerOn(s, null, Act("f", 1f));
                var lf = new L("f");
                int before = s_Errors.Count;
                int at = -1;
                lf.Also = (hh, ee) => at = s_Errors.Count;
                f.Play(With(lf));
                PlayerTestBinding.ThrowsIn.Add("f.Evaluate");
                LogAssert.Expect(LogType.Exception, "InvalidOperationException: test f.Evaluate");
                Run(s, 1);
                PlayerTestBinding.ThrowsIn.Clear();
                Check(lf.Once(PlaybackEnd.Faulted), "Faulted in a tick: " + lf.S);
                Check(at == before + 1, "after the exception's log: " + (at - before));
            }
            // ContextShutdown
            {
                FeedbackPlayer k = PlayerOn(s, x => x.m_Policy = PlayPolicy.Overlap, Act("k", 1f));
                var k1 = new L("k1");
                var k2 = new L("k2");
                PlaybackHandle hk = k.Play(With(k1));
                k.Play(With(k2));
                s.Shutdown();
                Check(k1.Once(PlaybackEnd.ContextShutdown) && k2.Once(PlaybackEnd.ContextShutdown), "ContextShutdown: " + k1.S + " " + k2.S);
                Check(!k.Stop(hk), "after the shutdown Stop(handle) is false");
            }
            End();
        }

        // ---- the ends inside Play and the refusals
        [Test]
        public void OE02_InsidePlay()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = PlayerOn(s, null, Act("i", 0f));
            Watch(p);
            // instants only
            {
                var a = new L("a");
                Clear();
                PlaybackHandle h = p.Play(With(a));
                Check(h != default && a.Once(PlaybackEnd.Completed), "instants only: " + a.S);
                Check(Log() == "i.Execute#0 |Played |Completed |La:Completed", "Played → Completed → the listener, inside Play: " + Log());
            }
            // a t = 0 self-stop
            {
                PlayerTestBinding.Hook = (b, what, record) =>
                {
                    if (what == "Execute" && b.Item.m_Label == "i")
                        p.Stop();
                };
                var b2 = new L("b");
                Clear();
                PlaybackHandle h = p.Play(With(b2));
                PlayerTestBinding.Hook = null;
                Check(h != default && b2.Once(PlaybackEnd.Stopped), "a t = 0 self-stop: " + b2.S);
                Check(Log() == "i.Execute#0 |Stopped |Lb:Stopped |Played", "Stopped → the listener → Played: " + Log());
            }
            // a t = 0 exception
            {
                FeedbackPlayer z = PlayerOn(s, null, Act("z", 1f));
                Watch(z);
                var lz = new L("z");
                PlayerTestBinding.ThrowsIn.Add("z.Begin");
                LogAssert.Expect(LogType.Exception, "InvalidOperationException: test z.Begin");
                Clear();
                PlaybackHandle hz = z.Play(With(lz));
                PlayerTestBinding.ThrowsIn.Clear();
                Check(hz == default && lz.Once(PlaybackEnd.Faulted) && lz.Handles[0] != default, "t = 0 exception: returned " + hz + " · " + lz.S);
                Check(Events() == "|Lz:Faulted", "no events: " + Events());
            }
            // refusals
            {
                var refused = new List<L>();
                FeedbackPlayer q = PlayerOn(s, x => x.m_Cooldown = 10f, Act("q", 1f));
                q.Play();
                var r = new L("cooldown");
                refused.Add(r);
                q.Play(With(r));
                q.m_Cooldown = 0f;
                q.m_Chance = 0f;
                r = new L("chance");
                refused.Add(r);
                q.Play(With(r));
                q.m_Chance = 100f;
                q.m_Policy = PlayPolicy.Ignore;
                r = new L("ignore");
                refused.Add(r);
                q.Play(With(r));
                q.m_Policy = PlayPolicy.Restart;
                r = new L("argument");
                refused.Add(r);
                q.Play(PlayArgs.Default.WithIntensity(float.NaN).WithListener(r));
                var dead = new CancellationTokenSource();
                dead.Cancel();
                r = new L("token");
                refused.Add(r);
                q.Play(With(r), dead.Token);
                FeedbackPlayer re = PlayerOn(s, null, Act("re", 1f));
                var inner = new L("reentry");
                refused.Add(inner);
                Action<FeedbackPlayer, PlaybackHandle> played = (pl, hh) => pl.Play(With(inner));
                re.Played += played;
                re.Play();
                re.Played -= played;
                FeedbackScheduler closed = Sched();
                FeedbackPlayer x2 = PlayerOn(closed, null, Act("x", 1f));
                closed.Shutdown();
                r = new L("closed");
                refused.Add(r);
                x2.Play(With(r));
                Check(refused.All(l => l.Ends.Count == 0), "refusals call nothing: " + string.Join(" ", refused.Where(l => l.Ends.Count > 0).Select(l => l.S)));
            }
            // SetListener inside Played
            {
                var c = new L("inPlayed");
                bool set = false;
                Action<FeedbackPlayer, PlaybackHandle> pp = (pl, hh) => set = pl.SetListener(hh, c);
                p.Played += pp;
                p.Play();
                p.Played -= pp;
                Check(set && c.Once(PlaybackEnd.Completed), "SetListener inside Played: " + set + " " + c.S);
            }
            End();
        }

        // ---- SetListener
        [Test]
        public void OE03_SetListener()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = PlayerOn(s, x => x.m_Policy = PlayPolicy.Overlap, Act("a", 1f));
            FeedbackPlayer other = PlayerOn(s, null, Act("b", 1f));
            // the cell
            {
                PlaybackHandle h = p.Play();
                PlaybackHandle ho = other.Play();
                var l1 = new L("1");
                var l2 = new L("2");
                int w = s_Warnings.Count;
                Check(!p.SetListener(default, l1), "the default handle");
                Check(!p.SetListener(ho, l1), "another player's handle");
                Check(p.SetListener(h, l1), "the first");
                Check(!p.SetListener(h, l2), "the second (taken)");
                bool n1 = p.SetListener(h, null);
                PlaybackHandle h3 = p.Play();
                bool n2 = p.SetListener(h3, null);
                Check(!n1 && !n2, "null is false (taken · empty): " + n1 + " " + n2);
                Check(s_Warnings.Count - w == 1 && s_Warnings.Last().Contains("listener null"), "one warning: " + (s_Warnings.Count - w));
                PlaybackHandle h2 = p.Play(With(l2));
                Check(!p.SetListener(h2, l1), "the args listener's cell");
                p.Stop(h);
                Check(!p.SetListener(h, new L("late")), "an ended play");
                p.Stop();
                Check(l1.Once(PlaybackEnd.Stopped) && l2.Once(PlaybackEnd.Stopped), "the first stays and is called once: " + l1.S + " " + l2.S);
            }
            End();
        }

        // ---- a throwing listener
        [Test]
        public void OE04_Throwing()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = PlayerOn(s, null, Act("a", 0.1f));
            Watch(p);
            // in a tick
            {
                var t = new L("t") { Throws = true };
                Clear();
                p.Play(With(t));
                int e0 = s_Errors.Count;
                LogAssert.Expect(LogType.Exception, "InvalidOperationException: listener t");
                bool threw = Try(() => Run(s, 4));
                Check(!threw && t.Once(PlaybackEnd.Completed), "a tick's throwing listener: " + t.S);
                Check(s_Errors.Count - e0 == 1 && s_Errors.Last().Contains("listener t"), "one exception log: " + (s_Errors.Count - e0));
                Check(s.ActiveCount == 0, "the slot left: " + s.ActiveCount);
                Check(Events().Contains("|Completed"), "the event: " + Events());
                Check(p.Play() != default, "the next Play");
                p.Stop();
            }
            // the policy's
            {
                var t2 = new L("t2") { Throws = true };
                p.Play(With(t2));
                PlaybackHandle hn = default;
                LogAssert.Expect(LogType.Exception, "InvalidOperationException: listener t2");
                bool threw = Try(() => hn = p.Play());
                Check(!threw && hn != default && t2.Once(PlaybackEnd.Stopped), "Restart's stopped listener throws: threw " + threw + " · accepted " + (hn != default) + " " + t2.S);
                p.Stop();
            }
            // Stop's
            {
                var t3 = new L("t3") { Throws = true };
                p.Play(With(t3));
                LogAssert.Expect(LogType.Exception, "InvalidOperationException: listener t3");
                bool threw = Try(() => p.Stop());
                Check(!threw && t3.Once(PlaybackEnd.Stopped), "Stop does not throw: " + threw + " " + t3.S);
            }
            End();
        }

        // ---- the record reused inside Completed
        [Test]
        public void OE05_Reuse()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = PlayerOn(s, null, Act("a", 0.1f));
            Watch(p);
            // reuse
            {
                var first = new L("first");
                var again = new L("again");
                bool once = false;
                p.Completed += (pl, hh) =>
                {
                    if (once)
                        return;
                    once = true;
                    pl.Play(With(again));
                };
                Clear();
                PlaybackHandle h1 = p.Play(With(first));
                Run(s, 2);
                Check(first.Once(PlaybackEnd.Completed), "the first once: " + first.S);
                Check(Events().Contains("|Completed |Played |Lfirst:Completed"), "after the subscriber's Played: " + Events());
                Check(again.Ends.Count == 0, "the second not yet: " + again.S);
                Run(s, 4);
                Check(again.Once(PlaybackEnd.Completed), "the second at its own end: " + again.S);
                Check(first.Handles.Count == 1 && again.Handles.Count == 1 && first.Handles[0] == h1 && again.Handles[0] != h1, "the handles");
            }
            End();
        }

        // ---- the quiet releases — Stopped without an event
        [Test]
        public void OE06_QuietReleases()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = PlayerOn(s, null, Act("a", 1f));
            Watch(p);
            // Rebuild
            {
                var r1 = new L("r1");
                var r2 = new L("r2");
                bool once = false;
                p.Stopped += (pl, hh) =>
                {
                    if (once)
                        return;
                    once = true;
                    pl.Play(With(r2));
                };
                p.Play(With(r1));
                Clear();
                p.Rebuild();
                Check(r1.Once(PlaybackEnd.Stopped) && r2.Once(PlaybackEnd.Stopped), "Rebuild: " + r1.S + " " + r2.S);
                Check(Count(Events(), "|Stopped ") + (Events().EndsWith("|Stopped") ? 1 : 0) == 1, "one Stopped event: " + Events());
            }
            // ReleaseRuntime
            {
                var rr = new L("rr");
                p.Play(With(rr));
                Clear();
                p.ReleaseRuntime();
                Check(rr.Once(PlaybackEnd.Stopped) && Events() == "|Lrr:Stopped", "ReleaseRuntime: " + rr.S + " · " + Events());
            }
            // a re-prepare for another scheduler
            {
                FeedbackScheduler s2 = Sched();
                p.EnsureRuntime();
                var rb = new L("rb");
                p.Play(With(rb));
                Clear();
                p.Bind(s2);
                p.Play();
                Check(rb.Once(PlaybackEnd.Stopped) && !Events().StartsWith("|Stopped", StringComparison.Ordinal), "rebind: " + rb.S + " · " + Events());
            }
            End();
        }

        // ---- tokens on the main thread
        [Test]
        public void OE07_Tokens()
        {
            FeedbackScheduler s = Sched();
            // one entry
            {
                FeedbackPlayer p = PlayerOn(s, null, Act("a", 0.1f));
                var cts = new CancellationTokenSource();
                p.Play(default, cts.Token);
                Run(s, 4);
                p.Play(default, cts.Token);
                Run(s, 4);
                Check(Used(p) == 1, "one entry for one token: " + Used(p));
            }
            // no store
            {
                FeedbackPlayer n = PlayerOn(s, null, Act("n", 0.1f));
                n.Play(default, default);
                n.Play(default, CancellationToken.None);
                FeedbackPlayer fresh = PlayerOn(s, null, Act("f", 0.1f));
                fresh.Play();
                Check(n.Runtime.Tokens == null && fresh.Runtime.Tokens == null, "default · None · no token: no store");
            }
            FeedbackPlayer q = PlayerOn(s, null, Act("q", 1f));
            Watch(q);
            // a canceled token
            {
                q.Play();
                var dead = new CancellationTokenSource();
                dead.Cancel();
                var ld = new L("dead");
                int w = s_Warnings.Count;
                Clear();
                PlaybackHandle hd = q.Play(With(ld), dead.Token);
                Check(hd == default && ld.Ends.Count == 0 && Events() == "" && s_Warnings.Count == w, "refused quietly: " + hd + " " + ld.S + " · " + Events() + " · warnings " + (s_Warnings.Count - w));
                Check(q.IsPlaying && q.Runtime.Tokens == null, "the running play goes on · no store: playing " + q.IsPlaying + " · store " + (q.Runtime.Tokens != null));
                q.Stop();
            }
            // canceled in the policy's Stopped
            {
                var b = new CancellationTokenSource();
                bool cut = false;
                q.Stopped += (pl, hh) =>
                {
                    if (cut)
                        return;
                    cut = true;
                    b.Cancel();
                };
                q.Play();
                var lb = new L("b");
                PlaybackHandle hb = q.Play(With(lb), b.Token);
                Check(hb == default && lb.Ends.Count == 0, "canceled in the policy's Stopped: refused " + hb + " " + lb.S);
            }
            // the main-thread cancel
            {
                FeedbackPlayer o = PlayerOn(s, x => { x.m_Policy = PlayPolicy.Overlap; x.m_MaxOverlap = 3; }, Act("o", 1f));
                Watch(o);
                var ca = new CancellationTokenSource();
                var cb = new CancellationTokenSource();
                var la = new L("a");
                var la2 = new L("a2");
                var lc = new L("c");
                o.Play(With(la), ca.Token);
                o.Play(With(la2), ca.Token);
                o.Play(With(lc), cb.Token);
                Run(s, 1);
                Clear();
                ca.Cancel();
                Check(la.Once(PlaybackEnd.Stopped) && la2.Once(PlaybackEnd.Stopped), "at once: " + la.S + " " + la2.S);
                Check(Count(Events(), "|Stopped") == 2, "with the events: " + Events());
                Check(lc.Ends.Count == 0 && o.IsPlaying, "the other token's play goes on: " + lc.S);
                Check(la.Threads.All(t => t == s_Main), "on the main thread");
                o.Stop();
            }
            // inside its own instant
            {
                FeedbackPlayer i = PlayerOn(s, null, Act("i1", 0.3f), Delayed(Act("i2", 0f), 0.1f));
                var ci = new CancellationTokenSource();
                var li = new L("i");
                PlayerTestBinding.Hook = (bb, what, record) =>
                {
                    if (what == "Execute" && bb.Item.m_Label == "i2")
                        ci.Cancel();
                };
                i.Play(With(li), ci.Token);
                Run(s, 4);
                PlayerTestBinding.Hook = null;
                Check(li.Once(PlaybackEnd.Stopped), "inside its own instant: " + li.S);
            }
            Check(s_Errors.Count == 0, "errors 0: " + string.Join(" | ", s_Errors.Take(2)));
            End();
        }

        // ---- the store's size and its eviction
        [Test]
        public void OE08_SizeAndEviction()
        {
            FeedbackScheduler s = Sched();
            // sizes
            {
                int Size(Action<FeedbackPlayer> set)
                {
                    FeedbackPlayer x = PlayerOn(s, set, Act("z", 0.1f));
                    x.Play(default, new CancellationTokenSource().Token);
                    x.Stop();
                    return x.Runtime.Tokens.Size;
                }
                int s1 = Size(null);
                int s4 = Size(x => { x.m_Policy = PlayPolicy.Overlap; x.m_MaxOverlap = 4; });
                int s16 = Size(x => { x.m_Policy = PlayPolicy.Overlap; x.m_MaxOverlap = 16; });
                Check(s1 == 4 && s4 == 4 && s16 == 16, "sizes 1 → 4 · 4 → 4 · 16 → 16: " + s1 + " " + s4 + " " + s16);
            }
            var ts = Enumerable.Range(0, 6).Select(_ => new CancellationTokenSource()).ToArray();
            // the longest unused goes
            {
                FeedbackPlayer p = PlayerOn(s, null, Act("a", 0.1f));
                for (int k = 0; k < 5; k++)
                {
                    p.Play(default, ts[k].Token);
                    p.Stop();
                    s.Tick(0.05f);
                }
                Check(Held(p, ts) == "4,1,2,3", "five tokens in turn, the first goes: " + Held(p, ts));
            }
            // a waited entry is never given
            {
                var us = Enumerable.Range(0, 5).Select(_ => new CancellationTokenSource()).ToArray();
                FeedbackPlayer o = PlayerOn(s, x => { x.m_Policy = PlayPolicy.Overlap; x.m_MaxOverlap = 4; }, Act("o", 5f));
                var lo = Enumerable.Range(0, 4).Select(k => new L("o" + k)).ToArray();
                var hs = new PlaybackHandle[4];
                for (int k = 0; k < 4; k++)
                    hs[k] = o.Play(With(lo[k]), us[k].Token);
                o.Stop(hs[1]);
                var l4 = new L("o4");
                o.Play(With(l4), us[4].Token);
                Check(Held(o, us) == "0,4,2,3", "the fifth takes the free entry of the stopped play: " + Held(o, us));
                us[0].Cancel();
                Check(lo[0].Once(PlaybackEnd.Stopped), "the waited first stays registered — its cancel stops its play: " + lo[0].S);
                us[1].Cancel();
                Check(lo[1].Once(PlaybackEnd.Stopped) && l4.Ends.Count == 0, "the given token's cancel does nothing: " + lo[1].S + " " + l4.S);
                o.Stop();
            }
            End();
        }

        // ---- a cancel off the main thread
        [Test]
        public void OE09_OffThread()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = PlayerOn(s, x => { x.m_Policy = PlayPolicy.Overlap; x.m_MaxOverlap = 3; }, Act("a", 2f));
            Watch(p);
            // Task.Run
            {
                var cts = new CancellationTokenSource();
                var l = new L("off");
                p.Play(With(l), cts.Token);
                Run(s, 1);
                Clear();
                Task.Run(() => cts.Cancel()).Wait();
                Check(p.IsPlaying && l.Ends.Count == 0, "still playing right after the cancel: " + p.IsPlaying + " " + l.S);
                Run(s, 1);
                Check(l.Once(PlaybackEnd.Stopped) && l.Threads.All(t => t == s_Main), "the next tick, on the main thread: " + l.S);
                Check(Events().Contains("|Stopped |Loff:Stopped"), "with the event: " + Events());
            }
            // CancelAfter
            {
                var c2 = new CancellationTokenSource();
                var l2 = new L("after");
                p.Play(With(l2), c2.Token);
                c2.CancelAfter(10);
                Thread.Sleep(200);
                bool still = p.IsPlaying && l2.Ends.Count == 0;
                Run(s, 1);
                Check(still && l2.Once(PlaybackEnd.Stopped), "CancelAfter: " + still + " " + l2.S);
            }
            // a paused play
            {
                var c3 = new CancellationTokenSource();
                var l3 = new L("paused");
                p.Play(With(l3), c3.Token);
                p.Pause();
                Task.Run(() => c3.Cancel()).Wait();
                Run(s, 1);
                Check(l3.Once(PlaybackEnd.Stopped), "a paused play stops at the next tick: " + l3.S);
                p.Resume();
            }
            Check(s_Errors.Count == 0, "errors · asserts 0: " + string.Join(" | ", s_Errors.Take(2)));
            End();
        }

        // ---- the release disposes the registrations
        [Test]
        public void OE10_Release()
        {
            FeedbackScheduler s = Sched();
            // ReleaseRuntime
            {
                FeedbackPlayer p = PlayerOn(s, null, Act("a", 2f));
                var cts = new CancellationTokenSource();
                var l = new L("rel");
                p.Play(With(l), cts.Token);
                PlaybackTokens.Entry e = p.Runtime.Tokens.At(0);
                p.ReleaseRuntime();
                cts.Cancel();
                Check(l.Once(PlaybackEnd.Stopped), "the release's Stopped: " + l.S);
                Check(!e.Canceled, "a cancel after the release calls nothing: mark " + e.Canceled);
            }
            // Rebuild
            {
                FeedbackPlayer q = PlayerOn(s, null, Act("b", 0.1f));
                var c2 = new CancellationTokenSource();
                q.Play(default, c2.Token);
                q.Stop();
                q.Rebuild();
                q.Play(default, c2.Token);
                q.Stop();
                Check(q.Runtime.Tokens != null && Used(q) == 1, "the new runtime holds the token again");
            }
            // a disposed source
            {
                FeedbackPlayer r = PlayerOn(s, null, Act("c", 0.1f));
                var gone = new CancellationTokenSource();
                CancellationToken tk = gone.Token;
                gone.Dispose();
                bool threw = Try(() =>
                {
                    r.Play(default, tk);
                    r.Stop();
                    r.ReleaseRuntime();
                });
                Check(!threw && s_Errors.Count == 0, "a disposed source's token: threw " + threw);
            }
            End();
        }

        // ---- a channel's Play with a listener
        [Test]
        public void OE11_Channel()
        {
            FeedbackScheduler s = Sched();
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("r" + i);
                m_Made.Add(go);
                var rp = go.AddComponent<FeedbackPlayer>();
                m_Players.Add(rp);
                rp.m_Items.Add(Act("r" + i, 0.1f));
                rp.Bind(s);
                rp.EnsureRuntime();
                var rx = go.AddComponent<FeedbackReceiver>();
                rx.m_Channel = ChannelRef.Of(1);
                rx.Bind(s);
            }
            // three receivers
            {
                var l = new L("ch");
                int taken = s.Context.Channels.Send(FeedbackCommand.Play(ChannelRef.Of(1), PlayArgs.Default.WithListener(l)), false, default);
                Run(s, 4);
                Check(taken == 3 && l.Ends.Count == 3 && l.Ends.All(e => e == PlaybackEnd.Completed) && l.Handles.Distinct().Count() == 3, "three receivers: taken " + taken + " " + l.S);
            }
            // a send position
            {
                var l2 = new L("pos");
                s.Context.Channels.Send(FeedbackCommand.Play(ChannelRef.Of(1), PlayArgs.Default.WithListener(l2)), true, Vector3.zero);
                Run(s, 4);
                Check(l2.Ends.Count == 3, "with a send position (the receivers' WithPosition): " + l2.S);
            }
            End();
        }

        // ---- a listener's Play
        [Test]
        public void OE12_ListenerPlay()
        {
            FeedbackScheduler s = Sched();
            // inside Play
            {
                FeedbackPlayer i = PlayerOn(s, null, Act("i", 0f));
                var inside = new L("inside");
                PlaybackHandle nested = default;
                inside.Also = (h, e) => nested = i.Play();
                int w = s_Warnings.Count;
                i.Play(With(inside));
                Check(inside.Once(PlaybackEnd.Completed) && nested == default && s_Warnings.Skip(w).Count(x => x == FeedbackPlayer.ReentryWarning) == 1,
                      "inside Play: refused with one warning: " + inside.S + " " + nested);
            }
            // in a tick
            {
                FeedbackPlayer t = PlayerOn(s, null, Act("t", 0.1f));
                var tick = new L("tick");
                PlaybackHandle next = default;
                tick.Also = (h, e) => next = t.Play();
                t.Play(With(tick));
                Run(s, 4);
                Check(tick.Once(PlaybackEnd.Completed) && next != default, "in a tick: accepted " + (next != default));
            }
            End();
        }

        // ---- the surface
        [Test]
        public void OE13_Surface()
        {
            // types and members
            {
                Check(typeof(PlaybackEnd).IsPublic && typeof(IPlaybackListener).IsPublic && typeof(IPlaybackListener).IsInterface, "the public types");
                Check((int)PlaybackEnd.Completed == 0 && (int)PlaybackEnd.Stopped == 1 && (int)PlaybackEnd.Faulted == 2 && (int)PlaybackEnd.Destroyed == 3 && (int)PlaybackEnd.ContextShutdown == 4
                      && (int)PlaybackEnd.Refused == 5 && Enum.GetNames(typeof(PlaybackEnd)).Length == 6, "PlaybackEnd 0 ~ 5 (Refused — PlayAsync's own)");
                MethodInfo set = typeof(FeedbackPlayer).GetMethod("SetListener", BindingFlags.Public | BindingFlags.Instance);
                Check(set != null && set.ReturnType == typeof(bool), "SetListener");
                Check(typeof(PlayArgs).GetMethod("WithListener", BindingFlags.Public | BindingFlags.Instance) != null && typeof(PlayArgs).GetProperty("Listener") != null, "WithListener · Listener");
                Check(UnsafeUtility.SizeOf<PlayArgs>() == 32, "PlayArgs 32 B: " + UnsafeUtility.SizeOf<PlayArgs>());
                Check(!typeof(PlaybackTokens).IsPublic, "the store is internal");
            }
            // every With carries the listener
            {
                var l = new L("x");
                PlayArgs a = PlayArgs.Default.WithListener(l);
                Check(a.WithIntensity(2f).Listener == l && a.WithSeed(1u).Listener == l && a.WithPosition(Vector3.one).Listener == l && a.WithDirection(PlayerDirection.Backward).Listener == l,
                      "every With… carries the listener");
            }
            End();
        }

        // a throwing handler: logged, and the handlers after it are not called while it throws — a C# event's later
        // subscribers and a UnityEvent row's later listeners; the next row still runs
        [Test]
        public void OE14_AThrowingHandler()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = PlayerOn(s, null, Act("i", 0f));
            int first = 0, second = 0, listener = 0, nextRow = 0;
            p.Played += (pl, h) => { first++; throw new InvalidOperationException("OE14 handler"); };
            p.Played += (pl, h) => second++;
            var row = new UnityEngine.Events.UnityEvent();
            row.AddListener(() => throw new InvalidOperationException("OE14 listener"));
            row.AddListener(() => listener++);
            var next = new UnityEngine.Events.UnityEvent();
            next.AddListener(() => nextRow++);
            p.m_UnityEvents.Add(new PlayerUnityEvent { m_Kind = PlayerEventKind.Played, m_Event = row });
            p.m_UnityEvents.Add(new PlayerUnityEvent { m_Kind = PlayerEventKind.Played, m_Event = next });
            for (int n = 0; n < 2; n++)
            {
                LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("OE14 handler"));
                LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("OE14 listener"));
                p.Play();
            }
            Assert.That((first, second, listener, nextRow), Is.EqualTo((2, 0, 0, 2)));
        }
    }
}
