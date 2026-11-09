using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // a RectTransform's size delta · pivot · anchors · offsets and the Anchored space (Core — no uGUI)
    public sealed class RectTransformTargetsTests
    {
        const float Step = 0.05f;
        readonly List<Object> m_Made = new List<Object>();
        readonly List<FeedbackScheduler> m_Schedulers = new List<FeedbackScheduler>();
        readonly List<FeedbackPlayer> m_Players = new List<FeedbackPlayer>();

        [TearDown]
        public void TearDown()
        {
            foreach (FeedbackPlayer p in m_Players)
            {
                if (p != null)
                    p.ReleaseRuntime();
            }
            foreach (FeedbackScheduler s in m_Schedulers)
            {
                if (!s.IsShutdown)
                    s.Shutdown();
            }
            for (int i = m_Made.Count - 1; i >= 0; i--)
            {
                if (m_Made[i] != null)
                    Object.DestroyImmediate(m_Made[i]);
            }
            m_Made.Clear();
            m_Schedulers.Clear();
            m_Players.Clear();
        }

        static string B(float f) => BitConverter.SingleToInt32Bits(f).ToString("X8");

        static string Bits(RectTransform r) => B(r.sizeDelta.x) + B(r.sizeDelta.y) + "/" + B(r.pivot.x) + B(r.pivot.y) + "/" + B(r.anchorMin.x) + B(r.anchorMin.y) + B(r.anchorMax.x) + B(r.anchorMax.y)
            + "/" + B(r.localPosition.x) + B(r.localPosition.y) + B(r.localPosition.z);

        static TargetRef Direct(Object o) => new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = o, m_Path = "", m_Anchor = "" };

        static TweenMotion TweenTo(Vector4 to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = to;
            return new TweenMotion { m_Params = p };
        }

        static PunchMotion Punch(Vector4 amplitude) => new PunchMotion { m_Params = new PunchParams { m_Amplitude = amplitude, m_Vibrato = 2f, m_Decay = 1f } };

        static T On<T>(T item, Object target, FeedbackMotion motion, float duration) where T : TargetItem
        {
            item.m_Target = Direct(target);
            item.m_Motion = motion;
            item.m_Timing.m_Duration = duration;
            return item;
        }

        static PositionItem Keeper()
        {
            var keeper = new PositionItem { m_Target = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" }, m_Motion = Punch(new Vector4(0.5f, 0.5f, 0.5f, 0f)) };
            keeper.m_Timing.m_Duration = 3f;
            return keeper;
        }

        FeedbackScheduler Sched()
        {
            var s = new FeedbackScheduler(ContextKind.Test);
            m_Schedulers.Add(s);
            return s;
        }

        FeedbackPlayer Player(FeedbackScheduler s, params FeedbackItem[] items)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.AddRange(items);
            p.Bind(s);
            p.EnsureRuntime();
            m_Players.Add(p);
            return p;
        }

        static void Tick(FeedbackScheduler s, int n)
        {
            for (int i = 0; i < n; i++)
                s.Tick(Step);
        }

        RectTransform Parent()
        {
            var go = new GameObject("parent", typeof(RectTransform));
            m_Made.Add(go);
            var r = (RectTransform)go.transform;
            r.sizeDelta = new Vector2(400f, 300f);
            return r;
        }

        static RectTransform Child(RectTransform parent, Vector2 min, Vector2 max, Vector2 pivot)
        {
            var go = new GameObject("rect", typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = min;
            r.anchorMax = max;
            r.pivot = pivot;
            r.sizeDelta = new Vector2(40f, 30f);
            r.anchoredPosition = new Vector2(7f, -5f);
            return r;
        }

        // ---- size · pivot · anchors go and come back bit for bit (the anchors move the local position — a derived value)

        [Test]
        public void RT10_SizePivotAnchors()
        {
            var fails = new List<string>();
            RectTransform parent = Parent();
            foreach ((string name, Func<RectTransform, TargetItem> item) in new (string, Func<RectTransform, TargetItem>)[]
            {
                ("size punch", r => On(new RectSizeDeltaItem(), r, Punch(new Vector4(20f, 10f, 0f, 0f)), 1f)),
                ("size tween", r => On(new RectSizeDeltaItem(), r, TweenTo(new Vector4(200f, 100f, 0f, 0f)), 0.5f)),
                ("pivot tween", r => On(new RectPivotItem(), r, TweenTo(new Vector4(0f, 1f, 0f, 0f)), 0.5f)),
                ("anchors tween", r => On(new RectAnchorsItem(), r, TweenTo(new Vector4(0f, 0f, 1f, 1f)), 0.5f)),
            })
            {
                FeedbackScheduler s = Sched();
                RectTransform r = Child(parent, new Vector2(0.2f, 0.3f), new Vector2(0.6f, 0.7f), new Vector2(0.3f, 0.6f));
                string before = Bits(r);
                FeedbackPlayer p = Player(s, item(r), Keeper());
                p.Play();
                bool moved = false;
                for (int i = 0; i < 30; i++)
                {
                    s.Tick(Step);
                    moved |= Bits(r) != before;
                }
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                if (!moved || Bits(r) != before)
                    fails.Add(name + " — moved " + moved + " · " + before + " → " + Bits(r));
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- the offsets — a tween reaches its offsets (any pivot) and comes back over both slots

        [Test]
        public void RT10b_Offsets()
        {
            var fails = new List<string>();
            RectTransform parent = Parent();
            foreach (Vector2 pivot in new[] { new Vector2(0.5f, 0.5f), new Vector2(0.2f, 0.7f) })
            {
                FeedbackScheduler s = Sched();
                RectTransform r = Child(parent, Vector2.zero, Vector2.one, pivot);
                string before = Bits(r);
                var to = new Vector4(10f, 20f, -30f, -40f);
                FeedbackPlayer p = Player(s, On(new RectOffsetsItem(), r, TweenTo(to), 0.5f), Keeper());
                p.Play();
                Tick(s, 12);
                Vector2 min = r.offsetMin, max = r.offsetMax;
                if ((min - new Vector2(to.x, to.y)).sqrMagnitude > 1e-6f || (max - new Vector2(to.z, to.w)).sqrMagnitude > 1e-6f)
                    fails.Add("pivot " + pivot + " — offsets " + min + " " + max);
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                if (Bits(r) != before)
                    fails.Add("pivot " + pivot + " — back " + before + " → " + Bits(r));
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- Anchored — a tween ends at its anchored position · an offset is the local one's · a plain Transform moves as Local
        // · the slot is Local's (no cast to Self)

        [Test]
        public void RT11_Anchored()
        {
            var fails = new List<string>();
            RectTransform parent = Parent();
            {
                FeedbackScheduler s = Sched();
                RectTransform r = Child(parent, new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.8f), new Vector2(0.3f, 0.6f));
                string before = Bits(r);
                var item = On(new PositionItem { m_Space = PositionSpace.Anchored }, r, TweenTo(new Vector4(100f, 50f, 0f, 0f)), 0.5f);
                FeedbackPlayer p = Player(s, item, Keeper());
                p.Play();
                Tick(s, 12);
                Vector3 expected = new Vector3(100f, 50f, 0f) + PositionBinding.AnchorReference(r);
                Vector2 read = r.anchoredPosition;
                if ((r.localPosition - expected).sqrMagnitude > 1e-6f)
                    fails.Add("anchored tween — local " + r.localPosition + " expected " + expected);
                // the truth apart from the reference point under test: the anchored position property (fresh)
                if ((read - new Vector2(100f, 50f)).sqrMagnitude > 1e-6f)
                    fails.Add("anchored tween — anchoredPosition " + read + " not (100, 50)");
                p.Stop(StopMode.Revert);
                s.Tick(0f);
                if (Bits(r) != before)
                    fails.Add("anchored back " + before + " → " + Bits(r));
            }
            {
                // a punch in Anchored is the punch in Local — on a turned rect (a slot space cast to Self turns the offset)
                var traces = new List<string>[2];
                for (int k = 0; k < 2; k++)
                {
                    FeedbackScheduler s = Sched();
                    RectTransform r = Child(parent, new Vector2(0.2f, 0.2f), new Vector2(0.8f, 0.8f), new Vector2(0.3f, 0.6f));
                    r.localRotation = Quaternion.Euler(0f, 0f, 30f);
                    Player(s, On(new PositionItem { m_Space = k == 0 ? PositionSpace.Anchored : PositionSpace.Local }, r, Punch(new Vector4(20f, 10f, 0f, 0f)), 1f)).Play();
                    traces[k] = new List<string>();
                    for (int i = 0; i < 10; i++)
                    {
                        s.Tick(Step);
                        traces[k].Add(B(r.localPosition.x) + B(r.localPosition.y));
                    }
                }
                if (!traces[0].SequenceEqual(traces[1]))
                    fails.Add("an anchored punch is not the local one");
            }
            {
                // a plain Transform moves as Local
                FeedbackScheduler s = Sched();
                var go = new GameObject("plain");
                m_Made.Add(go);
                go.transform.localPosition = new Vector3(1f, 2f, 3f);
                FeedbackPlayer p = Player(s, On(new PositionItem { m_Space = PositionSpace.Anchored }, go.transform, TweenTo(new Vector4(5f, 6f, 7f, 0f)), 0.5f));
                p.Play();
                Tick(s, 12);
                if (go.transform.localPosition != new Vector3(5f, 6f, 7f))
                    fails.Add("plain — " + go.transform.localPosition);
            }
            Assert.That(fails, Is.Empty);
        }

        // ---- FB-G08 takes the four · FB-G14 says an Anchored plain Transform moves as Local

        [Test]
        public void RT12_TheRules()
        {
            RectTransform parent = Parent();
            RectTransform driven = Child(parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var tracker = new DrivenRectTransformTracker();
            tracker.Add(parent, driven, DrivenTransformProperties.SizeDelta);
            try
            {
                var go = new GameObject("host");
                m_Made.Add(go);
                var plain = new GameObject("plain");
                m_Made.Add(plain);
                var host = go.AddComponent<FeedbackPlayer>();
                host.m_Items.AddRange(new FeedbackItem[]
                {
                    On(new RectSizeDeltaItem(), driven, TweenTo(Vector4.one), 1f),                               // 0 G08
                    On(new RectPivotItem(), driven, TweenTo(Vector4.one), 1f),                                   // 1 G08
                    On(new RectAnchorsItem(), driven, TweenTo(Vector4.one), 1f),                                 // 2 G08
                    On(new RectOffsetsItem(), driven, TweenTo(Vector4.one), 1f),                                 // 3 G08
                    On(new PositionItem { m_Space = PositionSpace.Anchored }, plain.transform, TweenTo(Vector4.one), 1f),   // 4 G14
                    On(new PositionItem { m_Space = PositionSpace.Anchored }, parent, TweenTo(Vector4.one), 1f),            // 5
                });
                List<RuleResult> got = FeedbackValidator.Run(host).Where(r => r.Id == "FB-G08" || r.Id == "FB-G14").ToList();
                Assert.That(got.Select(r => r.Id + "@" + r.Item).OrderBy(x => x), Is.EqualTo(new[] { "FB-G08@0", "FB-G08@1", "FB-G08@2", "FB-G08@3", "FB-G14@4" }));
            }
            finally
            {
                tracker.Clear();
            }
        }
    }
}
