using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the awaiting code runs inside SetResult, so a direct Tick decides every row. The pool's invariant (every source back,
    // once) holds at each test's start and end — a broken pool names the test that broke it
    public sealed class PlayerAsyncTests
    {
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        static readonly StringBuilder s_Log = new StringBuilder();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            PlayerTestBinding.Logging = false;
            s_Log.Clear();
            Assert.That(PlaybackAwaiter.Pooled, Is.EqualTo(PlaybackAwaiter.Created), "an earlier test broke the pool");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (FeedbackScheduler s in m_Schedulers)
                if (!s.IsShutdown) s.Shutdown();
            m_Schedulers.Clear();
            foreach (Object o in m_Made)
                if (o != null) Object.DestroyImmediate(o);
            m_Made.Clear();
            PlayerTestBinding.Reset();
        }

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        FeedbackPlayer Player(FeedbackScheduler s, PlayPolicy policy, params FeedbackItem[] items)
        {
            var go = new GameObject("player");
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Policy = policy;
            p.m_Items.AddRange(items);
            if (s != null)
            {
                p.Bind(s);
                p.EnsureRuntime();
            }
            return p;
        }

        static PlayerTestAction Act(string label, float length) => new PlayerTestAction { m_Label = label, m_Length = length };

        static async Awaitable Watch(Awaitable<PlaybackEnd> a, string tag)
        {
            PlaybackEnd e = await a;
            s_Log.Append(tag + ":" + e + " ");
        }

        sealed class L : IPlaybackListener
        {
            readonly string m_Name;
            readonly bool m_Throws;
            internal int Calls;
            internal L(string name, bool throws = false) { m_Name = name; m_Throws = throws; }
            public void OnPlaybackEnded(PlaybackHandle handle, PlaybackEnd end)
            {
                Calls++;
                s_Log.Append("L" + m_Name + ":" + end + " ");
                if (m_Throws)
                    throw new InvalidOperationException("listener " + m_Name);
            }
        }

        static void Idle() => Assert.That(PlaybackAwaiter.Pooled, Is.EqualTo(PlaybackAwaiter.Created), "every source back in the pool, once");

        [Test]
        public void AT01_Surface()
        {
            MethodInfo a0 = typeof(FeedbackPlayer).GetMethod("PlayAsync", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            MethodInfo a1 = typeof(FeedbackPlayer).GetMethod("PlayAsync", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(CancellationToken) }, null);
            MethodInfo a2 = typeof(FeedbackPlayer).GetMethod("PlayAsync", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(PlayArgs).MakeByRefType(), typeof(CancellationToken) }, null);
            MethodInfo pt = typeof(FeedbackPlayer).GetMethod("Play", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(PlayArgs).MakeByRefType(), typeof(CancellationToken) }, null);
            Assert.That((a0?.ReturnType, a1?.ReturnType, a2?.ReturnType), Is.EqualTo((typeof(Awaitable<PlaybackEnd>), typeof(Awaitable<PlaybackEnd>), typeof(Awaitable<PlaybackEnd>))));
            Assert.That(pt?.ReturnType, Is.EqualTo(typeof(PlaybackHandle)), "the token Play is public");
            Assert.That((int)PlaybackEnd.Refused, Is.EqualTo(5));
        }

        [Test]
        public void AT02_TheFiveEnds()
        {
            FeedbackScheduler s = Sched();
            _ = Watch(Player(s, PlayPolicy.Overlap, Act("c", 0.1f)).PlayAsync(), "completed");
            FeedbackPlayer st = Player(s, PlayPolicy.Overlap, Act("s", 5f));
            _ = Watch(st.PlayAsync(), "stopped");
            FeedbackPlayer f = Player(s, PlayPolicy.Overlap, Act("f", 5f));
            _ = Watch(f.PlayAsync(), "faulted");
            s.Tick(0.2f);
            st.Stop();
            PlayerTestBinding.Logging = true;
            PlayerTestBinding.ThrowsIn.Add("f.Evaluate");
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: test f.Evaluate");
            s.Tick(0.05f);
            PlayerTestBinding.ThrowsIn.Clear();
            PlayerTestBinding.Logging = false;
            FeedbackScheduler s2 = Sched();
            _ = Watch(Player(s2, PlayPolicy.Overlap, Act("x", 5f)).PlayAsync(), "shutdown");
            s2.Shutdown();
            string log = s_Log.ToString();
            foreach (string want in new[] { "completed:Completed", "stopped:Stopped", "faulted:Faulted", "shutdown:ContextShutdown" })
                Assert.That(log, Does.Contain(want), log);
            Assert.That(log.Split(' ').Length - 1, Is.EqualTo(4), "each once (Destroyed is the PlayMode row — OnDestroy): " + log);
            Idle();
        }

        [Test]
        public void AT03_AnEndInsidePlay()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer i = Player(s, PlayPolicy.Overlap, Act("i", 0f));
            Awaitable<PlaybackEnd> a = i.PlayAsync();
            s_Log.Append("returned ");
            _ = Watch(a, "instant");
            Assert.That(s_Log.ToString(), Is.EqualTo("returned instant:Completed "));
            Idle();
        }

        [Test]
        public void AT04_Refusals()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer c = Player(s, PlayPolicy.Overlap, Act("c", 1f));
            c.m_Cooldown = 100f;
            _ = Watch(c.PlayAsync(), "first");
            _ = Watch(c.PlayAsync(), "cooldown");
            FeedbackPlayer g = Player(s, PlayPolicy.Ignore, Act("g", 1f));
            _ = Watch(g.PlayAsync(), "g1");
            _ = Watch(g.PlayAsync(), "ignore");
            _ = Watch(Player(s, PlayPolicy.Overlap, Act("a", 1f)).PlayAsync(PlayArgs.Default.WithIntensity(-1f)), "argument");
            var cts = new CancellationTokenSource();
            cts.Cancel();
            _ = Watch(Player(s, PlayPolicy.Overlap, Act("k", 1f)).PlayAsync(cts.Token), "canceled");
            FeedbackPlayer z = Player(s, PlayPolicy.Overlap, Act("z", 1f));
            PlayerTestBinding.Logging = true;
            PlayerTestBinding.ThrowsIn.Add("z.Begin");
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: test z.Begin");
            _ = Watch(z.PlayAsync(), "fault0");
            PlayerTestBinding.ThrowsIn.Clear();
            PlayerTestBinding.Logging = false;
            FeedbackScheduler closed = Sched();
            FeedbackPlayer x = Player(closed, PlayPolicy.Overlap, Act("x", 1f));
            closed.Shutdown();
            _ = Watch(x.PlayAsync(), "closed");
            string log = s_Log.ToString();
            foreach (string want in new[] { "cooldown:Refused", "ignore:Refused", "argument:Refused", "canceled:Refused", "fault0:Faulted", "closed:Refused" })
                Assert.That(log, Does.Contain(want), log);
            Assert.That(log, Does.Not.Contain("fault0:Refused"), log);
            s.Tick(2f);
            Idle();
            // two plays after a t = 0 fault each get their own end (a source pushed twice would serve both)
            FeedbackPlayer two = Player(s, PlayPolicy.Overlap, Act("two", 0.1f), Act("two2", 0.3f));
            s_Log.Clear();
            Awaitable<PlaybackEnd> p1 = two.PlayAsync();
            Awaitable<PlaybackEnd> p2 = two.PlayAsync();
            two.Stop();
            _ = Watch(p1, "p1");
            _ = Watch(p2, "p2");
            Assert.That(s_Log.ToString(), Is.EqualTo("p1:Stopped p2:Stopped "), "two sources");
            Idle();
        }

        static async Awaitable ThenPlay(Awaitable<PlaybackEnd> a, FeedbackPlayer p, string tag)
        {
            PlaybackEnd e = await a;
            s_Log.Append(tag + ":" + e + " ");
            _ = Watch(p.PlayAsync(), tag + ".next");
        }

        [Test]
        public void AT05_ThePool()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer p = Player(s, PlayPolicy.Overlap, Act("p", 0.1f));
            for (int i = 0; i < 3; i++)
            {
                _ = p.PlayAsync();
                s.Tick(0.2f);
            }
            int made = PlaybackAwaiter.Created;
            for (int i = 0; i < 10; i++)
            {
                _ = Watch(p.PlayAsync(), "seq" + i);
                s.Tick(0.2f);
            }
            Assert.That(PlaybackAwaiter.Created, Is.EqualTo(made), "reused");
            s_Log.Clear();
            _ = ThenPlay(p.PlayAsync(), p, "outer");
            s.Tick(0.2f);
            s.Tick(0.2f);
            Assert.That(s_Log.ToString(), Is.EqualTo("outer:Completed outer.next:Completed "), "the play inside the awaiting code");
            s_Log.Clear();
            FeedbackPlayer q = Player(s, PlayPolicy.Overlap, Act("q", 5f));
            Awaitable<PlaybackEnd> first = q.PlayAsync();
            q.Stop();
            _ = Watch(first, "first");
            Awaitable<PlaybackEnd> second = q.PlayAsync();
            s.Tick(6f);
            _ = Watch(second, "second");
            Assert.That(s_Log.ToString(), Is.EqualTo("first:Stopped second:Completed "), "no stale result");
            Idle();
        }

        [Test]
        public void AT06_TheCallersListener()
        {
            FeedbackScheduler s = Sched();
            var l = new L("a");
            _ = Watch(Player(s, PlayPolicy.Overlap, Act("a", 0.1f)).PlayAsync(PlayArgs.Default.WithListener(l)), "a");
            s.Tick(0.2f);
            Assert.That(s_Log.ToString(), Is.EqualTo("La:Completed a:Completed "), "first, once");
            s_Log.Clear();
            var t = new L("t", true);
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: listener t");
            _ = Watch(Player(s, PlayPolicy.Overlap, Act("t", 0.1f)).PlayAsync(PlayArgs.Default.WithListener(t)), "t");
            s.Tick(0.2f);
            Assert.That(s_Log.ToString(), Is.EqualTo("Lt:Completed t:Completed "), "a throwing listener — the awaiting code still runs");
            s_Log.Clear();
            var r = new L("r");
            FeedbackPlayer c = Player(s, PlayPolicy.Overlap, Act("r", 1f));
            c.m_Cooldown = 100f;
            _ = c.PlayAsync();
            _ = Watch(c.PlayAsync(PlayArgs.Default.WithListener(r)), "r");
            Assert.That((s_Log.ToString(), r.Calls), Is.EqualTo(("r:Refused ", 0)), "a refusal calls no listener");
            s.Tick(2f);
            Idle();
        }

        [Test]
        public void AT07_Tokens()
        {
            FeedbackScheduler s = Sched();
            var main = new CancellationTokenSource();
            _ = Watch(Player(s, PlayPolicy.Overlap, Act("m", 5f)).PlayAsync(main.Token), "main");
            s_Log.Append("|cancel ");
            main.Cancel();
            s_Log.Append("|after ");
            Assert.That(s_Log.ToString(), Is.EqualTo("|cancel main:Stopped |after "), "inside Cancel");
            s_Log.Clear();
            var other = new CancellationTokenSource();
            _ = Watch(Player(s, PlayPolicy.Overlap, Act("o", 5f)).PlayAsync(other.Token), "other");
            Task.Run(() => other.Cancel()).Wait();
            Assert.That(s_Log.ToString(), Is.Empty, "not on the other thread");
            s.Tick(0.01f);
            Assert.That(s_Log.ToString(), Is.EqualTo("other:Stopped "), "the next tick");
            s.Tick(6f);
            Idle();
        }

        [Test]
        public void AT08_OverlapAndRestart()
        {
            FeedbackScheduler s = Sched();
            FeedbackPlayer o = Player(s, PlayPolicy.Overlap, Act("o", 0.1f));
            _ = Watch(o.PlayAsync(), "o1");
            s.Tick(0.05f);
            _ = Watch(o.PlayAsync(), "o2");
            s.Tick(0.07f);
            Assert.That(s_Log.ToString(), Is.EqualTo("o1:Completed "));
            s.Tick(0.05f);
            Assert.That(s_Log.ToString(), Is.EqualTo("o1:Completed o2:Completed "));
            s_Log.Clear();
            FeedbackPlayer r = Player(s, PlayPolicy.Restart, Act("r", 1f));
            _ = ThenPlay(r.PlayAsync(), r, "first");
            s_Log.Append("|second ");
            _ = Watch(r.PlayAsync(), "second");
            s.Tick(2f);
            Assert.That(s_Log.ToString(), Is.EqualTo("|second first:Stopped first.next:Refused second:Completed "));
            Idle();
        }

        const string ShutdownKey = "xpTURN.Feedback.PlayerAsyncTests.shutdown";
        const string ExitKey = "xpTURN.Feedback.PlayerAsyncTests.exit";

        static async Awaitable Record(Awaitable<PlaybackEnd> a, string key)
        {
            PlaybackEnd e = await a;
            SessionState.SetString(key, e + " playing=" + Application.isPlaying);
        }

        [UnityTest]
        public IEnumerator AT09_TheEndOfPlay()
        {
            SessionState.EraseString(ShutdownKey);
            SessionState.EraseString(ExitKey);
            yield return new EnterPlayMode();
            PlayerTestBinding.Register();
            PlayerTestBinding.Logging = false;
            var go = new GameObject("long");
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Policy = PlayPolicy.Overlap;
            p.m_Items.Add(Act("long", 10f));
            yield return null;
            _ = Record(p.PlayAsync(), ShutdownKey);
            _ = Record(p.PlayAsync(Application.exitCancellationToken), ExitKey);
            yield return null;
            yield return new ExitPlayMode();
            Assert.That(SessionState.GetString(ShutdownKey, ""), Is.EqualTo("ContextShutdown playing=True"), "the end of play reaches the awaiting code — still playing");
            Assert.That(SessionState.GetString(ExitKey, ""), Is.EqualTo("Stopped playing=True"), "the exit token stops its play");
        }
    }
}
