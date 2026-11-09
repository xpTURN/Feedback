using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // PlayMode — the real loop: the auto play, where in the frame a Play is called, the unscaled dt's cap, the destruction,
    // the other refusals. A row checks everything and judges at the end. Frames are fixed with captureDeltaTime — the scaled
    // dt only: what reads the unscaled time is EditMode's. A row that closes a scheduler closes one it put in as Default;
    // TearDown puts the loop's back, lets a frame pass so Destroy releases, then lets go what is left. No UnityEditor here
    public sealed class PlayerPlaybackPlayTests
    {
        static readonly List<string> s_Warnings = new List<string>();
        readonly List<string> m_Fail = new List<string>();
        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        FeedbackScheduler m_SavedDefault;
        bool m_DefaultSwapped;
        float m_SavedFixed;
        float m_SavedMax;

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Warning)
                s_Warnings.Add(condition);
        }

        // a runner that keeps the unscaled dt it gets and the engine's of the same frame
        sealed class DeltaRunner : IPlaybackRunner
        {
            internal readonly List<float> Received = new List<float>();
            internal readonly List<float> Engine = new List<float>();
            internal bool Recording;

            public bool Advance(int state, in FrameTime time)
            {
                if (Recording)
                {
                    Received.Add(time.UnscaledDelta);
                    Engine.Add(Time.unscaledDeltaTime);
                }
                return true;
            }

            public void Abort(int state, AbortReason reason)
            {
            }

            public Object DebugOwner => null;
        }

        [SetUp]
        public void SetUp()
        {
            m_Fail.Clear();
            s_Warnings.Clear();
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            m_SavedFixed = Time.fixedDeltaTime;
            m_SavedMax = Time.maximumDeltaTime;
            Application.logMessageReceived += OnLog;
            Time.captureDeltaTime = 1f / 60f;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PlayerTestBinding.Hook = null;
            Time.captureDeltaTime = 0f;
            Time.fixedDeltaTime = m_SavedFixed;
            Time.maximumDeltaTime = m_SavedMax;
            if (m_DefaultSwapped)
            {
                FeedbackScheduler.Default = m_SavedDefault;
                m_DefaultSwapped = false;
            }
            foreach (GameObject go in m_Made)
            {
                if (go != null)
                    Object.Destroy(go);
            }
            m_Made.Clear();
            yield return null;
            // a red row can leave a runtime behind
            foreach (FeedbackPlayer player in m_Players)
            {
                if (player != null)
                    player.ReleaseRuntime();
            }
            m_Players.Clear();
            foreach (FeedbackScheduler scheduler in m_Schedulers)
                scheduler.Shutdown();
            m_Schedulers.Clear();
            Application.logMessageReceived -= OnLog;
            PlayerTestBinding.Reset();
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        void SwapDefault(FeedbackScheduler scheduler)
        {
            if (!m_DefaultSwapped)
            {
                m_SavedDefault = FeedbackScheduler.Default;
                m_DefaultSwapped = true;
            }
            FeedbackScheduler.Default = scheduler;
        }

        GameObject Make(string name, bool active)
        {
            var go = new GameObject(name);
            if (!active)
                go.SetActive(false);
            m_Made.Add(go);
            return go;
        }

        FeedbackPlayer Add(GameObject go, string label, float length)
        {
            var player = go.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Items.Add(new PlayerTestAction { m_Label = label, m_Length = length });
            return player;
        }

        static bool Near(float a, float b, float eps = 0.002f) => Mathf.Abs(a - b) < eps;

        // ---- the auto play

        [UnityTest]
        public IEnumerator BP01_AutoPlay()
        {
            GameObject a = Make("bp1-enable", false);
            FeedbackPlayer pa = Add(a, "ea", 0.5f);
            pa.m_AutoPlay = PlayerAutoPlay.OnEnable;
            int playedA = 0;
            pa.Played += (p, h) => playedA++;
            a.SetActive(true);
            Check(playedA == 1, "OnEnable plays inside the enabling call: " + playedA);
            GameObject b = Make("bp1-start", false);
            FeedbackPlayer pb = Add(b, "eb", 0.5f);
            pb.m_Initialization = PlayerInitialization.Lazy;
            pb.m_AutoPlay = PlayerAutoPlay.OnStart;
            int playedB = 0;
            pb.Played += (p, h) => playedB++;
            b.SetActive(true);
            int sameFrame = playedB;
            yield return null;
            Check(sameFrame == 0 && playedB == 1, "OnStart plays the next frame: " + sameFrame + " · " + playedB);
            a.SetActive(false);
            b.SetActive(false);
            a.SetActive(true);
            b.SetActive(true);
            Check(playedA == 2 && playedB == 1, "off and on: OnEnable only: " + playedA + " · " + playedB);
            pa.ReleaseRuntime();
            pb.ReleaseRuntime();
            a.SetActive(false);
            b.SetActive(false);
            a.SetActive(true);
            b.SetActive(true);
            Check(playedA == 3 && playedB == 2, "the runtime let go, off and on: both again: " + playedA + " · " + playedB);
            GameObject c = Make("bp1-none", false);
            FeedbackPlayer pc = Add(c, "ec", 0.5f);
            int playedC = 0;
            pc.Played += (p, h) => playedC++;
            c.SetActive(true);
            yield return null;
            Check(playedC == 0, "no auto play: " + playedC);
            End();
        }

        // ---- where in the frame a Play is called — t = 0 inside the call, the first step in the next frame's Update stage
        // with that frame's dt

        IEnumerator SiteTrace(string site, List<string> into)
        {
            var evals = new List<string>();
            var frames = new List<int>();
            GameObject go = Make("bp2-" + site, true);
            FeedbackPlayer x = Add(go, "x", 0.05f);
            x.Rebuild();
            var caller = go.AddComponent<PlaybackFrameCaller>();
            FeedbackPlayer trigger = null;
            if (site == "Event")
            {
                GameObject t = Make("bp2-trigger", true);
                trigger = Add(t, "t", 0.02f);
                trigger.Rebuild();
                trigger.Completed += (pl, h) =>
                {
                    caller.PlayFrame = Time.frameCount;
                    x.Play();
                };
            }
            caller.Player = x;
            caller.Trigger = trigger;
            caller.Site = site;
            caller.AtFrame = Time.frameCount + 2;
            PlayerTestBinding.Hook = (b, what, rec) =>
            {
                if (what == "Evaluate" && b.Item.m_Label == "x")
                {
                    frames.Add(Time.frameCount);
                    evals.Add(b.LastTime.T.ToString("0.###", CultureInfo.InvariantCulture));
                }
            };
            for (int k = 0; k < 8; k++)
                yield return null;
            PlayerTestBinding.Hook = null;
            int f = caller.PlayFrame;
            into.Add(f < 0 ? "no play" : string.Join(" ", frames.Select((fr, i) => (fr - f) + ":" + evals[i])));
            Object.Destroy(caller);
        }

        [UnityTest]
        public IEnumerator BP02_FrameEdge()
        {
            Time.fixedDeltaTime = 1f / 60f;
            string[] sites = { "Update", "FixedUpdate", "Coroutine", "LateUpdate", "Event" };
            var lines = new List<string>();
            foreach (string site in sites)
                yield return SiteTrace(site, lines);
            for (int i = 0; i < sites.Length; i++)
                Check(lines[i] == "0:0 1:0.333 2:0.667 3:1", "a Play in " + sites[i] + ": " + lines[i]);
            End();
        }

        // ---- the unscaled dt's cap

        IEnumerator Hitch(int sleepMs, List<float> received, List<float> engine)
        {
            Time.captureDeltaTime = 0f;
            yield return null;
            var runner = new DeltaRunner();
            FeedbackScheduler d = FeedbackScheduler.Default;
            d.Reserve(1);
            PlaybackHandle h = d.Add(runner, 0);
            yield return null;
            runner.Recording = true;
            yield return null;
            Thread.Sleep(sleepMs);
            yield return null;
            yield return null;
            runner.Recording = false;
            d.Remove(h);
            d.Unreserve(1);
            received.AddRange(runner.Received);
            engine.AddRange(runner.Engine);
        }

        [UnityTest]
        public IEnumerator BP03_UnscaledCap()
        {
            var received = new List<float>();
            var engine = new List<float>();
            yield return Hitch(450, received, engine);
            int hitch = engine.IndexOf(engine.Max());
            int usual = hitch == 0 ? 1 : 0;
            Check(engine.Count >= 2 && engine[hitch] > 0.4f && Near(received[hitch], Time.maximumDeltaTime), "the hitch: engine " + (engine.Count > 0 ? engine[hitch] : -1f) + " · received " + (received.Count > 0 ? received[hitch] : -1f) + " · cap " + Time.maximumDeltaTime);
            Check(engine.Count >= 2 && received[usual] == engine[usual], "a usual frame is the engine's value");
            // the cap follows the game's maximumDeltaTime
            Time.maximumDeltaTime = 0.1f;
            received.Clear();
            engine.Clear();
            yield return Hitch(200, received, engine);
            hitch = engine.IndexOf(engine.Max());
            Check(engine.Count >= 2 && engine[hitch] > 0.15f && Near(received[hitch], 0.1f), "a lowered cap: engine " + (engine.Count > 0 ? engine[hitch] : -1f) + " · received " + (received.Count > 0 ? received[hitch] : -1f));
            End();
        }

        // ---- the destruction. With the default Continue a finite play goes on without the player — the quiet end is an
        // endless play's and a paused one's

        [UnityTest]
        public IEnumerator BP04_Destruction()
        {
            FeedbackScheduler d = FeedbackScheduler.Default;
            GameObject go = Make("bp4", true);
            FeedbackPlayer p = Add(go, "da", float.PositiveInfinity);
            p.Rebuild();
            int events = 0;
            p.Stopped += (x, h) => events++;
            p.Completed += (x, h) => events++;
            int reserved = d.ReservedCount;
            int live = d.ActiveCount;
            p.Play();
            PlayerTestBinding.Log.Clear();
            Object.Destroy(go);
            bool playingRightAfter = p.IsPlaying;
            yield return null;
            string log = string.Join(" ", PlayerTestBinding.Log);
            Check(playingRightAfter && log.Contains("da.End:Hold") && events == 0 && d.ReservedCount == reserved - 1 && d.ActiveCount == live,
                "Destroy: " + log + " · events " + events + " · reserved " + d.ReservedCount + "/" + (reserved - 1) + " · live " + d.ActiveCount + "/" + live);
            Check(p.Play() == default, "a destroyed player's Play");
            GameObject g2 = Make("bp4-immediate", true);
            FeedbackPlayer q = Add(g2, "dl", float.PositiveInfinity);
            var instant = new PlayerTestAction { m_Label = "di", m_Length = 0f };
            instant.m_Timing.m_Delay = 0.05f;
            q.m_Items.Add(instant);
            q.Rebuild();
            int events2 = 0;
            q.Stopped += (x, h) => events2++;
            q.Completed += (x, h) => events2++;
            PlayerTestBinding.Hook = (b, what, rec) =>
            {
                if (what == "Execute" && b.Item.m_Label == "di")
                    Object.DestroyImmediate(g2);
            };
            q.Play();
            PlayerTestBinding.Log.Clear();
            for (int k = 0; k < 6; k++)
                yield return null;
            PlayerTestBinding.Hook = null;
            string log2 = string.Join(" ", PlayerTestBinding.Log);
            Check(g2 == null && log2.Contains("dl.End:Hold") && events2 == 0, "DestroyImmediate in a tick's event: " + log2 + " · events " + events2);
            // a finite play goes on to its end with no event, then gives the reservation back
            GameObject g3 = Make("bp4-finite", true);
            FeedbackPlayer r = Add(g3, "df", 0.2f);
            r.Rebuild();
            int events3 = 0;
            r.Stopped += (x, h) => events3++;
            r.Completed += (x, h) => events3++;
            int reserved3 = d.ReservedCount;
            r.Play();
            PlayerTestBinding.Log.Clear();
            Object.Destroy(g3);
            yield return null;
            int during = d.ReservedCount;
            for (int k = 0; k < 30; k++)
                yield return null;
            string log3 = string.Join(" ", PlayerTestBinding.Log);
            Check(!log3.Contains("df.End:Hold") && log3.Contains("df.End:Completed") && events3 == 0 && during == reserved3 && d.ReservedCount == reserved3 - 1,
                "a finite play goes on: " + log3 + " · events " + events3 + " · reserved " + during + "/" + reserved3 + " → " + d.ReservedCount);
            // a paused one ends quietly
            GameObject g4 = Make("bp4-paused", true);
            FeedbackPlayer s4 = Add(g4, "dp", 1f);
            s4.Rebuild();
            int events4 = 0;
            s4.Stopped += (x, h) => events4++;
            s4.Completed += (x, h) => events4++;
            int reserved4 = d.ReservedCount;
            s4.Play();
            s4.Pause();
            PlayerTestBinding.Log.Clear();
            Object.Destroy(g4);
            yield return null;
            string log4 = string.Join(" ", PlayerTestBinding.Log);
            Check(log4.Contains("dp.End:Hold") && events4 == 0 && d.ReservedCount == reserved4 - 1, "a paused play ends quietly: " + log4 + " · events " + events4 + " · reserved " + d.ReservedCount + "/" + (reserved4 - 1));
            End();
        }

        // ---- the other refusals

        [UnityTest]
        public IEnumerator BP05_OtherRefusals()
        {
            GameObject n = Make("bp5-never", false);
            FeedbackPlayer pn = Add(n, "na", 0.5f);
            pn.m_AutoPlay = PlayerAutoPlay.OnEnable;
            s_Warnings.Clear();
            bool refused = pn.Play() == default && pn.Play() == default;
            int warned = s_Warnings.Count;
            int played = 0;
            pn.Played += (x, h) => played++;
            n.SetActive(true);
            Check(refused && warned == 1 && played == 1, "never active: " + refused + " · warnings " + warned + " · then " + played);
            GameObject c = Make("bp5-copy", true);
            FeedbackPlayer pc = Add(c, "ca", 1f);
            pc.Rebuild();
            pc.Play();
            GameObject copy = Object.Instantiate(c);
            m_Made.Add(copy);
            FeedbackPlayer copied = copy.GetComponent<FeedbackPlayer>();
            m_Players.Add(copied);
            Check(pc.IsPlaying && !copied.IsPlaying, "a copy of a playing player does not play");
            // a closed Default: the play ends with no event and a Play is refused quietly
            var own = new FeedbackScheduler(ContextKind.Runtime);
            m_Schedulers.Add(own);
            SwapDefault(own);
            GameObject e = Make("bp5-close", true);
            FeedbackPlayer pe = Add(e, "ce", 1f);
            pe.Rebuild();
            int events = 0;
            pe.Stopped += (x, h) => events++;
            pe.Completed += (x, h) => events++;
            pe.Play();
            own.Shutdown();
            s_Warnings.Clear();
            bool quiet = pe.Play() == default;
            Check(events == 0 && !pe.IsPlaying && quiet && s_Warnings.Count == 0, "the Default closed: events " + events + " · playing " + pe.IsPlaying + " · " + quiet + " · warnings " + s_Warnings.Count);
            FeedbackScheduler.Default = m_SavedDefault;
            m_DefaultSwapped = false;
            Check(pe.Play() != default, "an open Default plays again");
            yield return null;
            End();
        }
    }
}
