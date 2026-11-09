using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // N01 ~ N04 speak in their cases only — their severities, N03's fix · A01 tells a target without the item's
    // IFeedbackTarget<T> (the rules resolve an interface the binding's way)
    public sealed class TimeCustomRulesTests
    {
        F m_Fx;
        readonly List<Object> m_Made = new List<Object>();

        [SetUp]
        public void SetUp() => m_Fx = new F();

        [TearDown]
        public void TearDown()
        {
            m_Fx.Clean();
            foreach (Object o in m_Made)
            {
                if (o != null)
                    Object.DestroyImmediate(o);
            }
            m_Made.Clear();
        }

        GameObject Go(string name)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go;
        }

        static T On<T>(T item, Object target, FeedbackMotion motion) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion;
            item.m_Timing.m_Duration = 0.5f;
            return item;
        }

        static TweenMotion TweenTo(float to)
        {
            TweenParams p = TweenParams.Default;
            p.m_To = new Vector4(to, to, to, to);
            return new TweenMotion { m_Params = p };
        }

        static FlashMotion FlashTo(float to)
        {
            var flash = new FlashMotion();
            flash.m_Params.m_Target = new Vector4(to, to, to, to);
            return flash;
        }

        static SetMotion SetTo(float v) => new SetMotion { m_Params = new SetParams { m_Value = new Vector4(v, 0f, 0f, 0f) } };

        static void Want(Component host, string id, int[] items, RuleSeverity severity)
        {
            List<RuleResult> results = F.Of(host, id);
            Assert.That(results.Select(r => r.Item).OrderBy(i => i), Is.EqualTo(items), id);
            Assert.That(results.All(r => r.Severity == severity), Is.True, id + " " + severity);
        }

        static string Message(Component host, string id, int item) => F.Of(host, id).Single(r => r.Item == item).Message;

        // N01: a static listener of another type (Object · Int · String · Bool) boxes — a dynamic float, a float static, a void one do not
        [Test]
        public void CQ16a_TheBoxingRule()
        {
            GameObject go = Go("CQ16a");
            var listener = go.AddComponent<FeedbackTestListener>();
            var dynamicFloat = new CustomFloatEventItem { m_Motion = TweenTo(1f) };
            UnityEventTools.AddPersistentListener(dynamicFloat.m_Event, listener.Take);
            UnityEventTools.AddFloatPersistentListener(dynamicFloat.m_Event, listener.Take, 2f);
            UnityEventTools.AddVoidPersistentListener(dynamicFloat.m_Event, go.transform.DetachChildren);
            var boxed = new CustomFloatEventItem { m_Motion = TweenTo(1f) };
            UnityEventTools.AddIntPersistentListener(boxed.m_Event, listener.TakeInt, 3);
            UnityEventTools.AddBoolPersistentListener(boxed.m_Event, go.SetActive, true);
            UnityEventTools.AddStringPersistentListener(boxed.m_Event, go.SendMessage, "x");
            UnityEventTools.AddObjectPersistentListener<Object>(boxed.m_Event, listener.Keep, go);
            var off = new CustomFloatEventItem { m_Motion = TweenTo(1f), m_Active = false };
            UnityEventTools.AddIntPersistentListener(off.m_Event, listener.TakeInt, 3);
            FeedbackPlayer p = m_Fx.Player("CQ16a", dynamicFloat, boxed, off);
            Want(p, "FB-N01", new[] { 1 }, RuleSeverity.Warning);
            Assert.That(Message(p, "FB-N01", 1), Does.StartWith("4 listener(s) with a static argument of another type"));
            Assert.That(F.Of(p, "FB-N01").Single().Fixes, Is.Empty);
        }

        // N02: two implementers on the target — the first is used · a dragged implementer, a single one speak not · A01 tells a target
        // without the form (its readable name)
        [Test]
        public void CQ16b_TheImplementersRuleAndA01()
        {
            GameObject two = Go("CQ16bTwo");
            var first = two.AddComponent<FeedbackTestFloatTarget>();
            var second = two.AddComponent<FeedbackTestTarget>();
            GameObject one = Go("CQ16bOne");
            one.AddComponent<FeedbackTestTarget>();
            GameObject none = Go("CQ16bNone");
            none.AddComponent<FeedbackTestFloatTarget>();
            FeedbackPlayer p = m_Fx.Player("CQ16b",
                On(new CustomFloatItem(), two, TweenTo(1f)),
                On(new CustomFloatItem(), second, TweenTo(1f)),
                On(new CustomFloatItem(), one, TweenTo(1f)),
                On(new CustomColorItem(), none, TweenTo(1f)),
                On(new CustomVector3Item(), two, TweenTo(1f)));
            Want(p, "FB-N02", new[] { 0 }, RuleSeverity.Info);
            Assert.That(Message(p, "FB-N02", 0), Is.EqualTo("2 components implement IFeedbackTarget<float> — the first (FeedbackTestFloatTarget) is used"));
            Want(p, "FB-A01", new[] { 3 }, RuleSeverity.Warning);
            Assert.That(Message(p, "FB-A01", 3), Is.EqualTo("Direct: the target has no such component (IFeedbackTarget<Color>)"));
            Assert.That(first != null, Is.True);
        }

        // N03: a Time Scale Tween · Set stays — the fix 'Use Flash' keeps the value (a Set's zero length becomes the flash's) · a Flash
        // speaks not
        [Test]
        public void CQ16c_TheStaysRule()
        {
            FeedbackPlayer p = m_Fx.Player("CQ16c",
                On(new TimeScaleItem(), null, TweenTo(0.4f)),
                On(new TimeScaleItem(), null, SetTo(0.3f)),
                On(new TimeScaleItem(), null, FlashTo(0.2f)));
            p.m_Items[1].m_Timing.m_Duration = 0f;
            Want(p, "FB-N03", new[] { 0, 1 }, RuleSeverity.Info);
            Assert.That(Message(p, "FB-N03", 0), Is.EqualTo("The time scale stays at 0.4 after the play — until a revert or a restore"));
            foreach (int i in new[] { 0, 1 })
            {
                RuleResult r = F.Of(p, "FB-N03").Single(x => x.Item == i);
                Assert.That(r.Fixes.Single().Name, Is.EqualTo("Use Flash"));
                FeedbackValidator.Apply(r, r.Fixes.Single());
            }
            var a = (TimeScaleItem)p.m_Items[0];
            var b = (TimeScaleItem)p.m_Items[1];
            Assert.That((a.m_Motion as FlashMotion)?.m_Params.m_Target.x, Is.EqualTo(0.4f));
            Assert.That((b.m_Motion as FlashMotion)?.m_Params.m_Target.x, Is.EqualTo(0.3f));
            Assert.That((a.m_Timing.m_Duration, b.m_Timing.m_Duration), Is.EqualTo((0.5f, TimeScaleStaysRule.FlashDuration)));
            Want(p, "FB-N03", new int[0], RuleSeverity.Info);
        }

        // N04: out of 0 ~ 100 — a Set · a Flash · a Tween To · a From's · a FromTo's (a By adds — not judged)
        [Test]
        public void CQ16d_TheRangeRule()
        {
            TweenMotion from = TweenTo(1f);
            from.m_Params.m_Mode = TweenMode.From;
            from.m_Params.m_From = new Vector4(-1f, 0f, 0f, 0f);
            TweenMotion fromTo = TweenTo(200f);
            fromTo.m_Params.m_Mode = TweenMode.FromTo;
            TweenMotion by = TweenTo(500f);
            by.m_Params.m_Mode = TweenMode.By;
            FeedbackPlayer p = m_Fx.Player("CQ16d",
                On(new TimeScaleItem(), null, SetTo(-0.5f)),
                On(new TimeScaleItem(), null, FlashTo(120f)),
                On(new TimeScaleItem(), null, TweenTo(101f)),
                On(new TimeScaleItem(), null, from),
                On(new TimeScaleItem(), null, fromTo),
                On(new TimeScaleItem(), null, by),
                On(new TimeScaleItem(), null, TweenTo(100f)),
                On(new TimeScaleItem(), null, SetTo(0f)));
            Want(p, "FB-N04", new[] { 0, 1, 2, 3, 4 }, RuleSeverity.Warning);
            Assert.That(Message(p, "FB-N04", 0), Is.EqualTo("The time scale -0.5 is out of 0 ~ 100 — it is clamped"));
        }
    }
}
