using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests
{
    // PlayMode — the disable behaviors, the destruction and the plays that go on, a Destroy inside the player's own event,
    // the control paths' first runs. Default is a test scheduler the real loop runs and the frames are fixed with
    // captureDeltaTime 1/90; TearDown puts both back. A row checks everything and judges at the end. Errors are counted by a
    // log listener. No Math names (default(Ease) is Linear, the timing's rule stays WithPrevious) · no UnityEditor here
    public sealed class PlayerControlPlayTests
    {
        static readonly List<string> s_Errors = new List<string>();
        static FeedbackScheduler s_Saved;
        static FeedbackScheduler s_Test;
        readonly List<string> m_Fail = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<string> m_Events = new List<string>();

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                s_Errors.Add(type + ": " + condition);
        }

        [SetUp]
        public void SetUp()
        {
            m_Fail.Clear();
            m_Events.Clear();
            s_Errors.Clear();
            PlayerTestBinding.Reset();
            PlayerTestBinding.Register();
            Application.logMessageReceived += OnLog;
            Time.captureDeltaTime = 1f / 90f;
            s_Saved = FeedbackScheduler.Default;
            s_Test = new FeedbackScheduler(ContextKind.Test);
            FeedbackScheduler.Default = s_Test;
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
            yield return null;
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    p.ReleaseRuntime();
            }
            m_Players.Clear();
            FeedbackScheduler.Default = s_Saved;
            s_Test.Shutdown();
            s_Test = null;
            s_Saved = null;
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

        // ---- fixtures

        GameObject Go(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            if (parent != null)
                go.transform.SetParent(parent, false);
            return go;
        }

        FeedbackPlayer PlayerOn(GameObject owner, params FeedbackItem[] items)
        {
            var player = owner.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Items.AddRange(items);
            player.EnsureRuntime();
            return player;
        }

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };

        static T Item<T>(FeedbackMotion motion, TargetRef target, float duration) where T : TargetItem, new()
        {
            var item = new T { m_Motion = motion, m_Target = target };
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static PositionItem To(GameObject target, float x, float duration)
            => Item<PositionItem>(new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.To, m_To = new Vector4(x, 0f, 0f, 0f), m_Ease = default, m_Loops = 1 } }, Direct(target.transform), duration);

        static PositionItem Shake(GameObject target, float duration)
            => Item<PositionItem>(new ShakeMotion { m_Params = new ShakeParams { m_Amplitude = Vector4.one, m_Frequency = 10f } }, Direct(target.transform), duration);

        static SetActiveItem SetOff(GameObject target) => Item<SetActiveItem>(new SetMotion { m_Params = new SetParams { m_Value = Vector4.zero } }, Direct(target), 0f);

        static UnityEventItem Event(UnityAction action)
        {
            var item = new UnityEventItem();
            item.m_Event.AddListener(action);
            return item;
        }

        static T Delayed<T>(T item, float delay) where T : FeedbackItem
        {
            item.m_Timing.m_Delay = delay;
            return item;
        }

        static T Repeating<T>(T item) where T : FeedbackItem
        {
            item.m_Timing.m_RepeatForever = true;
            return item;
        }

        static PlayArgs Backward => PlayArgs.Default.WithDirection(PlayerDirection.Backward);

        void Watch(FeedbackPlayer p, string name = "")
        {
            p.Played += (pl, h) => m_Events.Add(name + "Played");
            p.Completed += (pl, h) => m_Events.Add(name + "Completed");
            p.Stopped += (pl, h) => m_Events.Add(name + "Stopped");
        }

        string TakeEvents()
        {
            string text = string.Join(" ", m_Events);
            m_Events.Clear();
            return text;
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
                yield return null;
        }

        static float X(GameObject go) => go.transform.localPosition.x;
        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // ---- the three disable behaviors

        [UnityTest]
        public IEnumerator DP01_DisableBehaviors()
        {
            foreach (PlayerDisableBehavior behavior in new[] { PlayerDisableBehavior.Continue, PlayerDisableBehavior.StopHold, PlayerDisableBehavior.StopRevert })
            {
                GameObject owner = Go("owner-" + behavior);
                GameObject t = Go("t-" + behavior);
                FeedbackPlayer p = PlayerOn(owner, To(t, 10f, 1f));
                p.m_DisableBehavior = behavior;
                Watch(p);
                p.Play();
                yield return Frames(30);
                float atOff = X(t);
                TakeEvents();
                owner.SetActive(false);
                string stopped = TakeEvents();
                float right = X(t);
                yield return Frames(30);
                float later = X(t);
                owner.SetActive(true);
                yield return Frames(80);
                string after = TakeEvents();
                switch (behavior)
                {
                    case PlayerDisableBehavior.Continue:
                        Check(stopped == "" && later > atOff + 1f && after == "Completed" && X(t) == 10f, "Continue: " + stopped + " · " + F(atOff) + " → " + F(later) + " · " + after + " · " + F(X(t)));
                        break;
                    case PlayerDisableBehavior.StopHold:
                        Check(stopped == "Stopped" && right == atOff && later == atOff && after == "" && X(t) == atOff, "StopHold: " + stopped + " · " + F(atOff) + " " + F(right) + " " + F(later) + " · " + after + " · " + F(X(t)));
                        break;
                    default:
                        Check(stopped == "Stopped" && right == 0f && later == 0f && after == "" && X(t) == 0f, "StopRevert: " + stopped + " · " + F(right) + " " + F(later) + " · " + after + " · " + F(X(t)));
                        break;
                }
            }
            // a Set that turns the player's own object or an ancestor off, and StopRevert: no error, it stays off, the outer
            // value goes back
            GameObject self = Go("self");
            GameObject outer = Go("outer");
            FeedbackPlayer q = PlayerOn(self, To(outer, 10f, 0.5f), Delayed(SetOff(self), 0.1f));
            q.m_DisableBehavior = PlayerDisableBehavior.StopRevert;
            q.Play();
            yield return Frames(20);
            Check(!self.activeSelf && X(outer) == 0f && !q.IsPlaying && s_Errors.Count == 0, "the player's own object: " + self.activeSelf + " · " + F(X(outer)) + " · " + string.Join(" | ", s_Errors));
            GameObject ancestor = Go("ancestor");
            GameObject holder = Go("holder", ancestor.transform);
            GameObject outer2 = Go("outer2");
            FeedbackPlayer r = PlayerOn(holder, To(outer2, 10f, 0.5f), Delayed(SetOff(ancestor), 0.1f));
            r.m_DisableBehavior = PlayerDisableBehavior.StopRevert;
            r.Play();
            yield return Frames(20);
            Check(!ancestor.activeSelf && X(outer2) == 0f && s_Errors.Count == 0, "an ancestor: " + ancestor.activeSelf + " · " + F(X(outer2)) + " · " + string.Join(" | ", s_Errors));
            // a child turned off by the play is turned on again by the revert (the engine takes it)
            GameObject parent = Go("parent");
            GameObject child = Go("child", parent.transform);
            FeedbackPlayer c = PlayerOn(parent, SetOff(child), To(Go("outer3"), 10f, 1f));
            c.m_DisableBehavior = PlayerDisableBehavior.StopRevert;
            c.Play();
            yield return Frames(5);
            bool childOff = !child.activeSelf;
            parent.SetActive(false);
            Check(childOff && child.activeSelf && s_Errors.Count == 0, "a child: " + childOff + " · " + child.activeSelf + " · " + string.Join(" | ", s_Errors));
            End();
        }

        // ---- the destruction

        sealed class Subscriber : MonoBehaviour
        {
            internal int Heard;

            internal void OnCompleted(FeedbackPlayer player, PlaybackHandle handle)
            {
                // a destroyed subscriber throws here (MissingReferenceException)
                _ = transform.position;
                Heard++;
            }
        }

        [UnityTest]
        public IEnumerator DP02_Destruction()
        {
            // Continue: the outer finite play goes on to its end, its own child's item ends, the endless and the paused plays
            // end quietly, no event at all (the detached runtime raises the diagnostics alone), the last end gives the
            // reservation back
            GameObject o = Go("o");
            GameObject child = Go("child", o.transform);
            GameObject t1 = Go("t1"), t2 = Go("t2"), t3 = Go("t3");
            FeedbackPlayer f = PlayerOn(o, To(t1, 10f, 0.5f), Shake(child, 0.5f));
            FeedbackPlayer i = PlayerOn(o, Repeating(Shake(t2, 0.3f)));
            FeedbackPlayer z = PlayerOn(o, To(t3, 10f, 1f));
            int unity = 0;
            f.m_UnityEvents.Add(new PlayerUnityEvent { m_Kind = PlayerEventKind.Completed, m_Event = new UnityEvent() });
            f.m_UnityEvents[0].m_Event.AddListener(() => unity++);
            foreach (FeedbackPlayer p in new[] { f, i, z })
                Watch(p);
            f.Play();
            i.Play();
            z.Play();
            yield return Frames(9);
            z.Pause();
            float paused = X(t3);
            int reserved = s_Test.ReservedCount;
            TakeEvents();
            Object.Destroy(o);
            yield return null;
            int afterDestroy = s_Test.ReservedCount;
            Check(f.Play() == default, "a destroyed player's Play");
            yield return Frames(50);
            Check(X(t1) == 10f && X(t2) == 0f && X(t3) == paused && TakeEvents() == "" && unity == 0, "Continue: " + F(X(t1)) + " · " + F(X(t2)) + " · " + F(X(t3)) + "/" + F(paused) + " · unity " + unity);
            Check(reserved == 3 && afterDestroy == 1 && s_Test.ReservedCount == 0 && s_Errors.Count == 0, "Continue: reserved " + reserved + " → " + afterDestroy + " → " + s_Test.ReservedCount + " · " + string.Join(" | ", s_Errors));
            // StopHold: Stopped inside the Destroy call, the UnityEvent too (before the destroyed mark), the reservation at once
            GameObject h = Go("h");
            GameObject u1 = Go("u1"), u2 = Go("u2");
            FeedbackPlayer hf = PlayerOn(h, To(u1, 10f, 0.5f));
            FeedbackPlayer hi = PlayerOn(h, Repeating(Shake(u2, 0.3f)));
            int unityStopped = 0;
            hf.m_UnityEvents.Add(new PlayerUnityEvent { m_Kind = PlayerEventKind.Stopped, m_Event = new UnityEvent() });
            hf.m_UnityEvents[0].m_Event.AddListener(() => unityStopped++);
            foreach (FeedbackPlayer p in new[] { hf, hi })
            {
                p.m_DisableBehavior = PlayerDisableBehavior.StopHold;
                Watch(p);
            }
            hf.Play();
            hi.Play();
            yield return Frames(9);
            TakeEvents();
            Object.Destroy(h);
            string inside = TakeEvents();
            yield return null;
            Check(inside == "Stopped Stopped" && unityStopped == 1 && s_Test.ReservedCount == 0 && s_Errors.Count == 0, "StopHold: " + inside + " · unity " + unityStopped + " · reserved " + s_Test.ReservedCount);
            // the scheduler shuts down under a detached play: the reservation goes, no event
            GameObject d = Go("d");
            GameObject u3 = Go("u3");
            FeedbackPlayer df = PlayerOn(d, To(u3, 10f, 2f));
            Watch(df);
            df.Play();
            yield return Frames(5);
            Object.Destroy(d);
            yield return null;
            int detached = s_Test.ReservedCount;
            TakeEvents();
            s_Test.Shutdown();
            Check(detached == 1 && s_Test.ReservedCount == 0 && TakeEvents() == "", "a shutdown under a detached play: " + detached + " → " + s_Test.ReservedCount);
            End();
        }

        // a scene unloaded under the plays: a play whose targets all went ends quietly at its next tick, the one with a live
        // target goes on — and no C# event reaches the destroyed subscriber; the user cannot stop a detached play. The outer
        // play is longer than the unload's frames
        [UnityTest]
        public IEnumerator DP02_SceneUnload()
        {
            Scene scene = SceneManager.CreateScene("dp2-unload-" + Guid.NewGuid().ToString("N"));
            GameObject owner = new GameObject("owner");
            GameObject inner = new GameObject("inner");
            GameObject inner2 = new GameObject("inner2");
            GameObject other = new GameObject("other");
            GameObject listener = new GameObject("listener");
            foreach (GameObject go in new[] { owner, inner, inner2, other, listener })
                SceneManager.MoveGameObjectToScene(go, scene);
            GameObject outer = Go("outer");
            var subscriber = listener.AddComponent<Subscriber>();
            FeedbackPlayer p = PlayerOn(owner, To(outer, 10f, 20f), To(inner, 10f, 1f));
            FeedbackPlayer q = PlayerOn(other, To(inner2, 10f, 20f));
            p.Completed += subscriber.OnCompleted;
            q.Completed += subscriber.OnCompleted;
            PlaybackHandle handle = p.Play();
            q.Play();
            yield return Frames(9);
            int reserved = s_Test.ReservedCount;
            AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
            while (!unload.isDone)
                yield return null;
            yield return null;
            int afterUnload = s_Test.ReservedCount;
            bool stop = p.Stop(handle);
            // the async unload's frames vary with the machine's load — the plays outlast it by far, and the end is waited for,
            // not counted
            for (int n = 0; n < 3000 && s_Test.ReservedCount > 0; n++)
                yield return null;
            Check(reserved == 2 && afterUnload == 1 && !stop && X(outer) == 10f && s_Test.ReservedCount == 0 && s_Errors.Count == 0,
                "the unload: reserved " + reserved + " → " + afterUnload + " → " + s_Test.ReservedCount + " · Stop " + stop + " · outer " + F(X(outer)) + " · " + string.Join(" | ", s_Errors));
            End();
        }

        // ---- a Destroy inside the player's own UnityEvent

        [UnityTest]
        public IEnumerator DP03_DestroyInOwnEvent()
        {
            foreach (PlayerDisableBehavior behavior in new[] { PlayerDisableBehavior.Continue, PlayerDisableBehavior.StopHold })
            {
                GameObject o = Go("o-" + behavior);
                GameObject t = Go("t-" + behavior);
                FeedbackPlayer p = PlayerOn(o, To(t, 10f, 1f), Delayed(Event(() => Object.Destroy(o)), 0.2f));
                p.m_DisableBehavior = behavior;
                Watch(p);
                p.Play();
                yield return Frames(100);
                string events = TakeEvents();
                if (behavior == PlayerDisableBehavior.Continue)
                    Check(events == "Played" && X(t) == 10f, "Continue: " + events + " · " + F(X(t)));
                else
                    Check(events == "Played Stopped" && X(t) > 1.5f && X(t) < 2.5f, "StopHold: " + events + " · " + F(X(t)));
            }
            // DestroyImmediate inside it — the three behaviors
            foreach (PlayerDisableBehavior behavior in new[] { PlayerDisableBehavior.Continue, PlayerDisableBehavior.StopHold, PlayerDisableBehavior.StopRevert })
            {
                GameObject o = Go("i-" + behavior);
                GameObject t = Go("ti-" + behavior);
                GameObject own = Go("own", o.transform);
                FeedbackPlayer p = PlayerOn(o, To(t, 10f, 1f), To(own, 10f, 1f), Delayed(Event(() => Object.DestroyImmediate(o)), 0.2f));
                p.m_DisableBehavior = behavior;
                Watch(p);
                p.Play();
                yield return Frames(100);
                string events = TakeEvents();
                string want = behavior == PlayerDisableBehavior.Continue ? "Played" : "Played Stopped";
                bool value = behavior == PlayerDisableBehavior.Continue ? X(t) == 10f : behavior == PlayerDisableBehavior.StopHold ? X(t) > 1.5f && X(t) < 2.5f : X(t) == 0f;
                Check(events == want && value && s_Test.ReservedCount == 0, "DestroyImmediate · " + behavior + ": " + events + " · " + F(X(t)) + " · reserved " + s_Test.ReservedCount);
            }
            Check(s_Errors.Count == 0, "errors: " + string.Join(" | ", s_Errors));
            End();
        }

        // ---- the control paths' first runs — five fresh fixtures, their first run

        sealed class Fixture
        {
            public FeedbackScheduler Scheduler;
            public FeedbackPlayer Player;
        }

        static Fixture s_Fixture;
        static int s_Heard;

        static void Heard(FeedbackPlayer p, PlaybackHandle h) => s_Heard++;

        static void Tick(int n)
        {
            for (int i = 0; i < n; i++)
                s_Fixture.Scheduler.Tick(0.05f);
        }

        // a To (Persist), a repeating Shake, an empty UnityEvent, a wait with a timeout beside them (Signal has one to end),
        // one-direction instants (a Set and an empty UnityEvent that act backward)
        Fixture Fresh(bool toggle)
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            GameObject owner = Go("fixture");
            GameObject target = Go("target");
            GameObject shaken = Go("shaken");
            GameObject switched = Go("switched");
            PositionItem shake = Shake(shaken, 0.1f);
            shake.m_Timing.m_RepeatCount = 2;
            SetActiveItem set = SetOff(switched);
            set.m_Conditions.m_Direction = DirectionCondition.ReverseOnly;
            var reverseEvent = new UnityEventItem();
            reverseEvent.m_Conditions.m_Direction = DirectionCondition.ReverseOnly;
            var player = owner.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Items.AddRange(new FeedbackItem[] { To(target, 10f, 0.4f), shake, new UnityEventItem(), set, reverseEvent, new WaitForSignalItem { m_Timeout = 0.5f } });
            if (toggle)
                player.m_Direction = PlayerDirection.Toggle;
            player.Bind(s);
            player.Played += Heard;
            player.Completed += Heard;
            player.Stopped += Heard;
            player.EnsureRuntime();
            return new Fixture { Scheduler = s, Player = player };
        }

        static readonly (string Name, bool Toggle, TestDelegate Run)[] s_Paths =
        {
            ("backward", false, () => { s_Fixture.Player.Play(Backward); Tick(12); }),
            ("seek ahead and back", false, () => { s_Fixture.Player.Play(); s_Fixture.Player.Seek(0.3f); s_Fixture.Player.Seek(0.1f); Tick(2); }),
            ("seek to the end", false, () => { s_Fixture.Player.Play(); s_Fixture.Player.Seek(10f); }),
            ("a backward play's seek", false, () => { s_Fixture.Player.Play(Backward); Tick(1); s_Fixture.Player.Seek(0.2f); s_Fixture.Player.Seek(0.45f); Tick(1); }),
            ("forward skip", false, () => { s_Fixture.Player.Play(); Tick(1); s_Fixture.Player.Skip(); }),
            ("backward skip", false, () => { s_Fixture.Player.Play(Backward); Tick(1); s_Fixture.Player.Skip(); }),
            ("restore", false, () => { s_Fixture.Player.Play(); Tick(2); s_Fixture.Player.Restore(); }),
            ("signal", false, () => { s_Fixture.Player.Play(); Tick(1); s_Fixture.Player.Signal(); Tick(1); }),
            ("toggle", true, () => { s_Fixture.Player.Play(); Tick(2); s_Fixture.Player.Play(); Tick(2); }),
            ("three seeks in a frame", false, () => { s_Fixture.Player.Play(); s_Fixture.Player.Seek(0.1f); s_Fixture.Player.Seek(0.2f); s_Fixture.Player.Seek(0.3f); Tick(1); }),
            ("a backward play first", false, () => { s_Fixture.Player.Play(Backward); Tick(2); s_Fixture.Player.Play(); Tick(2); }),
            ("forward after a cut backward play", false, () => { s_Fixture.Player.Play(); Tick(9); s_Fixture.Player.Play(Backward); Tick(1); s_Fixture.Player.Play(); Tick(1); }),
            ("one-direction instants", false, () => { s_Fixture.Player.Play(Backward); Tick(12); s_Fixture.Player.Play(); Tick(12); }),
        };

        // red when the first run of all five fresh fixtures allocates — the warm-up is another fixture with the same delegate
        string Judge(string name, bool toggle, TestDelegate run)
        {
            s_Fixture = Fresh(toggle);
            run();
            int hits = 0;
            for (int i = 0; i < 5; i++)
            {
                s_Fixture = Fresh(toggle);
                if (UIs.AllocatingGCMemory().ApplyTo(run).IsSuccess)
                    hits++;
            }
            s_Fixture = null;
            return hits == 5 ? name + " allocated 5/5" : null;
        }

        [UnityTest]
        public IEnumerator DP04_FirstRuns()
        {
            yield return null;
            foreach ((string name, bool toggle, TestDelegate run) in s_Paths)
            {
                string bad = Judge(name, toggle, run);
                if (bad != null)
                    m_Fail.Add(bad);
            }
            int positive = 0;
            for (int i = 0; i < 5; i++)
            {
                if (UIs.AllocatingGCMemory().ApplyTo(new TestDelegate(() => s_Heard += new int[4].Length)).IsSuccess)
                    positive++;
            }
            Check(positive == 5, "positive control " + positive + "/5");
            End();
        }
    }
}
