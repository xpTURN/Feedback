using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests
{
    // the seven's first runs allocate nothing (fresh fixtures — the children prepared with the parent — a cycle of Play and six
    // ticks on a test scheduler, three of each after one warm) · the real loop: a child's end reaches its parent's node at the
    // next frame
    public sealed class FlowPlayTests
    {
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();

        [SetUp]
        public void SetUp()
        {
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.Destroy(o);
            }
            m_Made.Clear();
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            m_Schedulers.Clear();
            PlayerTestBinding.Reset();
            yield return null;
        }

        sealed class Receiver : IFeedbackSignalReceiver
        {
            internal float Last;

            public void OnFeedbackSignal(in FeedbackSignal signal) => Last = signal.Value;
        }

        static void Noop()
        {
        }

        FeedbackPlayer Make(FeedbackScheduler s, params FeedbackItem[] items)
        {
            var go = new GameObject("flow");
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            if (s != null)
                p.Bind(s);
            return p;
        }

        static PlayerTestAction Act(string label, float length) => new PlayerTestAction { m_Label = label, m_Length = length };

        static FeedbackScheduler s_S;
        static FeedbackPlayer s_P;

        static readonly TestDelegate s_Cycle = () =>
        {
            s_P.Play();
            for (int i = 0; i < 6; i++)
                s_S.Tick(0.05f);
        };

        FeedbackPlayer Fixture(string kind, FeedbackScheduler s)
        {
            switch (kind)
            {
                case "play player":
                {
                    var b = Act("a", 0.05f);
                    b.m_Timing.m_Rule = StartRule.AfterPrevious;
                    return Make(s, new PlayPlayerItem { m_Player = Make(s, Act("a", 0.05f)) }, b);
                }
                case "chain":
                {
                    var chain = new PlayerChainItem();
                    chain.m_Entries.Add(new ChainEntry { m_Player = Make(s, Act("a", 0.05f)) });
                    chain.m_Entries.Add(new ChainEntry { m_Player = Make(s, Act("a", 0.05f)) });
                    return Make(s, chain);
                }
                case "control":
                {
                    FeedbackPlayer x = Make(s, Act("a", 1f));
                    x.EnsureRuntime();
                    x.Play();
                    var pause = new PlayerControlItem { m_Action = PlayerControlAction.Pause };
                    pause.m_Players.Add(x);
                    var resume = new PlayerControlItem { m_Action = PlayerControlAction.Resume };
                    resume.m_Players.Add(x);
                    resume.m_Timing.m_Delay = 0.05f;
                    return Make(s, pause, resume);
                }
                case "random player":
                {
                    var random = new RandomPlayerItem();
                    random.m_Entries.Add(new WeightedPlayer { m_Player = Make(s, Act("a", 0.05f)) });
                    random.m_Entries.Add(new WeightedPlayer { m_Player = Make(s, Act("a", 0.05f)) });
                    return Make(s, random);
                }
                case "random unity event":
                {
                    var item = new RandomUnityEventItem();
                    for (int i = 0; i < 2; i++)
                    {
                        var e = new WeightedUnityEvent();
                        e.m_Event.AddListener(Noop);
                        // measured at its second invoke — the first builds the invoke list
                        e.m_Event.Invoke();
                        item.m_Entries.Add(e);
                    }
                    return Make(s, item);
                }
                case "signal broadcast":
                {
                    ChannelRef three = ChannelRef.Of(3);
                    s.Context.Channels.Register(new Receiver(), new ChannelKey(in three));
                    return Make(s, new SignalBroadcastItem { m_Channel = three });
                }
                default:
                    return Make(s, new DebugItem { m_Action = DebugAction.Note, m_Message = "note" });
            }
        }

        [UnityTest]
        public IEnumerator FQ14a_TheFirstRuns()
        {
            yield return null;
            PlayerTestBinding.Logging = false;
            var fails = new List<string>();
            foreach (string kind in new[] { "play player", "chain", "control", "random player", "random unity event", "signal broadcast", "debug note" })
            {
                var s = new FeedbackScheduler(ContextKind.Test);
                m_Schedulers.Add(s);
                s_S = s;
                s_P = Fixture(kind, s);
                s_P.EnsureRuntime();
                s_Cycle();
                int hits = 0, prepared = 0;
                for (int k = 0; k < 3; k++)
                {
                    s_P = Fixture(kind, s);
                    if (s_P.EnsureRuntime() && s_P.Runtime.Binding(0) != null)
                        prepared++;
                    if (UIs.AllocatingGCMemory().ApplyTo(s_Cycle).IsSuccess)
                        hits++;
                }
                if (hits > 0 || prepared < 3)
                    fails.Add(kind + " allocated " + hits + "/3 · prepared " + prepared + "/3");
            }
            int positive = 0;
            for (int i = 0; i < 3; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => s_S = new FeedbackScheduler(ContextKind.Test))).IsSuccess)
                    positive++;
                m_Schedulers.Add(s_S);
            }
            if (positive != 3)
                fails.Add("positive control " + positive + "/3");
            Assert.That(fails, Is.Empty, string.Join(" · ", fails));
        }

        // the loop's own scheduler: the child ends in a frame, its parent's next item begins at the next frame (a fixed 0.02 s frame —
        // a batch run's frames are short)
        [UnityTest]
        public IEnumerator FQ15a_TheRealLoop()
        {
            float capture = Time.captureDeltaTime;
            Time.captureDeltaTime = 0.02f;
            try
            {
                yield return Loop();
            }
            finally
            {
                Time.captureDeltaTime = capture;
                PlayerTestBinding.Hook = null;
            }
        }

        IEnumerator Loop()
        {
            yield return null;
            var frames = new Dictionary<string, int>();
            PlayerTestBinding.Hook = (b, call, record) =>
            {
                string key = b.Item.m_Label + "." + call;
                if (!frames.ContainsKey(key))
                    frames.Add(key, Time.frameCount);
            };
            FeedbackPlayer child = Make(null, Act("c", 0.1f));
            var next = Act("B", 0.05f);
            next.m_Timing.m_Rule = StartRule.AfterPrevious;
            FeedbackPlayer parent = Make(null, new PlayPlayerItem { m_Player = child }, next);
            yield return null;
            bool completed = false;
            parent.Completed += (p, h) => completed = true;
            parent.Play();
            for (int i = 0; i < 120 && !completed; i++)
                yield return null;
            PlayerTestBinding.Hook = null;
            Assert.That(completed, Is.True, "the parent completes");
            Assert.That(frames.ContainsKey("c.End") && frames.ContainsKey("B.Begin"), Is.True, string.Join(", ", frames.Keys));
            Assert.That(frames["B.Begin"] - frames["c.End"], Is.EqualTo(1), "the next frame");
        }
    }
}
