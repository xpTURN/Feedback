using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // PlayMode — a player's Variants through the engine's own calls: the disable behaviors and the destruction reach the
    // variants' plays too. Default is a test scheduler the real loop runs and the frames are fixed with captureDeltaTime
    // 1/90; TearDown puts both back. A row checks everything and judges at the end. No Math names · no UnityEditor here
    public sealed class PlayerVariantPlayTests
    {
        static readonly List<string> s_Errors = new List<string>();
        static FeedbackScheduler s_Saved;
        static FeedbackScheduler s_Test;
        readonly List<string> m_Fail = new List<string>();
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();
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
            Time.captureDeltaTime = 0f;
            Application.logMessageReceived -= OnLog;
        }

        void Check(bool ok, string what)
        {
            if (!ok)
                m_Fail.Add(what);
        }

        void End() => Assert.That(m_Fail, Is.Empty);

        GameObject Go(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        FeedbackPreset Preset(params FeedbackItem[] items)
        {
            var preset = ScriptableObject.CreateInstance<FeedbackPreset>();
            preset.m_Items.AddRange(items);
            m_Made.Add(preset);
            return preset;
        }

        // the list and one variant, each with room for one play
        FeedbackPlayer PlayerOn(GameObject owner, FeedbackItem item, FeedbackPreset variant)
        {
            var player = owner.AddComponent<FeedbackPlayer>();
            m_Players.Add(player);
            player.m_Items.Add(item);
            player.m_Variants.Add(variant);
            player.m_Policy = PlayPolicy.Overlap;
            player.m_MaxOverlap = 1;
            player.EnsureRuntime();
            player.Played += (pl, h) => m_Events.Add("Played");
            player.Completed += (pl, h) => m_Events.Add("Completed");
            player.Stopped += (pl, h) => m_Events.Add("Stopped");
            return player;
        }

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };

        static PositionItem To(GameObject target, Vector3 to, float duration)
        {
            var item = new PositionItem { m_Target = Direct(target.transform), m_Motion = new TweenMotion { m_Params = new TweenParams { m_Mode = TweenMode.To, m_To = to, m_Ease = default, m_Loops = 1 } } };
            item.m_Timing.m_Duration = duration;
            return item;
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

        static string F(Vector3 v) => v.ToString("F2", CultureInfo.InvariantCulture);

        static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-6f;

        // ---- T33 — the disable behaviors stop the variants' plays, StopRevert the latest first

        [UnityTest]
        public IEnumerator VS33a_DisableStopsTheVariants()
        {
            foreach (PlayerDisableBehavior behavior in new[] { PlayerDisableBehavior.StopHold, PlayerDisableBehavior.StopRevert })
            {
                foreach (bool variantFirst in new[] { false, true })
                {
                    GameObject owner = Go("owner");
                    GameObject t = Go("t");
                    FeedbackPreset variant = Preset(To(t, new Vector3(0f, 2f, 0f), 1f));
                    FeedbackPlayer p = PlayerOn(owner, To(t, new Vector3(1f, 0f, 0f), 1f), variant);
                    p.m_DisableBehavior = behavior;
                    PlayArgs first = variantFirst ? PlayArgs.Default.WithPreset(variant) : PlayArgs.Default;
                    PlayArgs second = variantFirst ? PlayArgs.Default : PlayArgs.Default.WithPreset(variant);
                    p.Play(first);
                    yield return Frames(30);
                    p.Play(second);
                    yield return Frames(10);
                    Vector3 atOff = t.transform.localPosition;
                    TakeEvents();
                    owner.SetActive(false);
                    string stopped = TakeEvents();
                    Vector3 right = t.transform.localPosition;
                    string row = behavior + (variantFirst ? " · variant first" : " · list first");
                    Check(stopped == "Stopped Stopped" && !p.IsPlaying, row + ": both plays stop — '" + stopped + "'");
                    if (behavior == PlayerDisableBehavior.StopHold)
                        Check(Near(right, atOff), row + ": the values stay — " + F(atOff) + " → " + F(right));
                    else
                        Check(Near(right, Vector3.zero), row + ": the value before both plays — " + F(atOff) + " → " + F(right));
                }
            }
            Check(s_Errors.Count == 0, "no error: " + string.Join(" | ", s_Errors));
            End();
        }

        // ---- T33 — the destruction: Continue lets the variants' finite plays end, StopHold stops them inside the Destroy

        [UnityTest]
        public IEnumerator VS33b_DestructionReachesTheVariants()
        {
            GameObject o = Go("o");
            GameObject t1 = Go("t1"), t2 = Go("t2");
            FeedbackPreset variant = Preset(To(t2, new Vector3(10f, 0f, 0f), 0.5f));
            FeedbackPlayer c = PlayerOn(o, To(t1, new Vector3(10f, 0f, 0f), 0.5f), variant);
            c.Play();
            c.Play(PlayArgs.Default.WithPreset(variant));
            yield return Frames(9);
            int reserved = s_Test.ReservedCount;
            TakeEvents();
            Object.Destroy(o);
            yield return null;
            int afterDestroy = s_Test.ReservedCount;
            yield return Frames(50);
            Check(Near(t1.transform.localPosition, new Vector3(10f, 0f, 0f)) && Near(t2.transform.localPosition, new Vector3(10f, 0f, 0f)), "Continue: both finite plays end — " + F(t1.transform.localPosition) + " · " + F(t2.transform.localPosition));
            Check(reserved == 2 && afterDestroy == 2 && s_Test.ReservedCount == 0 && TakeEvents() == "", "Continue: reserved " + reserved + " → " + afterDestroy + " → " + s_Test.ReservedCount);
            GameObject h = Go("h");
            GameObject u1 = Go("u1"), u2 = Go("u2");
            FeedbackPreset hv = Preset(To(u2, new Vector3(10f, 0f, 0f), 0.5f));
            FeedbackPlayer hold = PlayerOn(h, To(u1, new Vector3(10f, 0f, 0f), 0.5f), hv);
            hold.m_DisableBehavior = PlayerDisableBehavior.StopHold;
            hold.Play();
            hold.Play(PlayArgs.Default.WithPreset(hv));
            yield return Frames(9);
            TakeEvents();
            Object.Destroy(h);
            string inside = TakeEvents();
            yield return null;
            Check(inside == "Stopped Stopped" && s_Test.ReservedCount == 0, "StopHold: '" + inside + "' · reserved " + s_Test.ReservedCount);
            Check(s_Errors.Count == 0, "no error: " + string.Join(" | ", s_Errors));
            End();
        }
    }
}
