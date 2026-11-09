using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // F03's words name no stage · F12 ~ F15 speak in their cases only · F13's reason — a direct write's end puts its own start
    // back over another's Persist on the same sprite renderer
    public sealed class SpriteRulesTests
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

        T Made<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return go.AddComponent<T>();
        }

        Sprite Sprite(string name)
        {
            var texture = new Texture2D(4, 4);
            m_Made.Add(texture);
            Sprite sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            m_Made.Add(sprite);
            return sprite;
        }

        static T On<T>(T item, Object target, FeedbackMotion motion = null) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = motion ?? new TweenMotion();
            return item;
        }

        static void Want(Component host, string id, int[] items, RuleSeverity severity)
        {
            List<RuleResult> results = F.Of(host, id);
            Assert.That(results.Select(r => r.Item).OrderBy(i => i), Is.EqualTo(items), id);
            Assert.That(results.All(r => r.Severity == severity), Is.True, id + " " + severity);
        }

        static string Message(Component host, string id, int item) => F.Of(host, id).Single(r => r.Item == item).Message;

        [Test]
        public void SQ17a_GroupFSpeaksInItsCases()
        {
            var sr = Made<SpriteRenderer>("SQ17Sprite");
            var alone = Made<SpriteRenderer>("SQ17Alone");
            var keys = Made<LineRenderer>("SQ17Keys");
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.green, 0.5f), new GradientColorKey(Color.blue, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            keys.colorGradient = gradient;
            var two = Made<LineRenderer>("SQ17Two");
            var off = Made<TrailRenderer>("SQ17Off");
            off.emitting = false;
            var timeless = Made<TrailRenderer>("SQ17Timeless");
            timeless.time = 0f;
            var turnedOn = Made<TrailRenderer>("SQ17TurnedOn");
            turnedOn.emitting = false;
            Sprite a = Sprite("A");
            var skipped = On(new SpriteSwapItem(), sr, new SetMotion());
            skipped.m_Active = false;
            var direct = On(new SpriteColorItem(), sr);
            direct.m_DirectWrite = true;
            var directAlone = On(new SpriteColorItem(), alone);
            directAlone.m_DirectWrite = true;
            FeedbackPlayer host = m_Fx.Player("SQ17", new FeedbackItem[]
            {
                On(new SpriteSwapItem(), sr, new SetMotion()),                                           // 0 F12 — no sprite
                On(new SpriteSwapItem { m_Sprite = a }, sr, new SetMotion()),                            // 1
                On(new SpriteSheetItem(), sr),                                                           // 2 F12 — no frame
                On(new SpriteSheetItem { m_Frames = new List<Sprite> { a, null } }, sr),                 // 3 F12 — an empty frame
                On(new SpriteSheetItem { m_Frames = new List<Sprite> { a } }, sr),                       // 4
                direct,                                                                                  // 5 F13
                On(new SpriteAlphaItem(), sr),                                                           // 6
                directAlone,                                                                             // 7
                On(new LineColorItem(), keys),                                                           // 8 F14
                On(new LineColorItem(), two),                                                            // 9
                On(new TrailTimeItem(), off),                                                            // 10 F15 — not emitting
                On(new TrailClearItem(), timeless, new SetMotion()),                                     // 11 F15 — time 0
                On(new TrailWidthItem(), turnedOn),                                                      // 12
                On(new TrailEmittingItem(), turnedOn, new SetMotion { m_Params = new SetParams { m_Value = Vector4.one } }), // 13
                skipped,                                                                                 // 14
            });
            Want(host, "FB-F12", new[] { 0, 2, 3 }, RuleSeverity.Warning);
            Want(host, "FB-F13", new[] { 5 }, RuleSeverity.Warning);
            Want(host, "FB-F14", new[] { 8 }, RuleSeverity.Info);
            Want(host, "FB-F15", new[] { 10, 11 }, RuleSeverity.Info);
            Assert.That(Message(host, "FB-F12", 0), Is.EqualTo("No sprite to swap in — the item is skipped"));
            Assert.That(Message(host, "FB-F12", 2), Is.EqualTo("No frame — the item is skipped"));
            Assert.That(Message(host, "FB-F12", 3), Is.EqualTo("1 of 2 frames are empty — they are skipped"));
            Assert.That(Message(host, "FB-F14", 8), Is.EqualTo("The gradient has 3 color and 2 alpha keys — only the first ones change"));
            Assert.That(Message(host, "FB-F15", 10), Does.StartWith("The trail does not emit"));
            Assert.That(Message(host, "FB-F15", 11), Does.StartWith("The trail's time is 0"));
            RuleResult mix = F.Of(host, "FB-F13").Single();
            Assert.That(mix.Fixes.Select(f => f.Name), Is.EqualTo(new[] { "Write through the shared layer" }));
            mix.Fixes[0].Run();
            Assert.That(((TargetItem)host.m_Items[5]).m_DirectWrite, Is.False, "the fix writes through the shared layer");
            Assert.That(F.Of(host, "FB-F13"), Is.Empty);
        }

        // F03: a sprite's material color — the words point at Sprite Color, no stage
        [Test]
        public void SQ17b_F03Words()
        {
            var sr = Made<SpriteRenderer>("SQ17bSprite");
            FeedbackPlayer host = m_Fx.Player("SQ17b", On(new MaterialColorItem(), sr, new PunchMotion()));
            Assert.That(Message(host, "FB-F03", 0), Is.EqualTo("A sprite shader may not show a block's color — use Sprite Color"));
            Assert.That(F.Of(host, "FB-F03").Single().Fixes, Is.Empty);
        }

        // F13's reason: a direct color flash's end puts its own start (the alpha before) back over an alpha's Persist made during it —
        // through the shared layer the Persist stays
        [Test]
        public void SQ17c_ADirectWritesEndPutsItsStartBack([Values(true, false)] bool direct)
        {
            var sr = Made<SpriteRenderer>("SQ17cSprite");
            sr.sprite = Sprite("S");
            sr.color = new Color(0.25f, 0.5f, 0.75f, 0.9f);
            var s = new FeedbackScheduler(ContextKind.Test);
            try
            {
                var flash = new FlashMotion();
                flash.m_Params.m_Target = new Vector4(1f, 0f, 0f, 1f);
                SpriteColorItem color = On(new SpriteColorItem(), sr, flash);
                color.m_DirectWrite = direct;
                color.m_Timing.m_Duration = 1f;
                SpriteAlphaItem alpha = On(new SpriteAlphaItem(), sr, new TweenMotion { m_Params = new TweenParams { m_To = new Vector4(0.25f, 0f, 0f, 0f), m_Loops = 1 } });
                alpha.m_Timing.m_Duration = 0.2f;
                FeedbackPlayer pc = Made<FeedbackPlayer>("SQ17cColor");
                pc.m_Items.Add(color);
                pc.Bind(s);
                FeedbackPlayer pa = Made<FeedbackPlayer>("SQ17cAlpha");
                pa.m_Items.Add(alpha);
                pa.Bind(s);
                pc.Play();
                s.Tick(0.05f);
                pa.Play();
                for (int i = 0; i < 30; i++)
                    s.Tick(0.05f);
                s.Tick(0f);
                Assert.That(sr.color.a, Is.EqualTo(direct ? 0.9f : 0.25f), direct ? "the direct write's end took the alpha's Persist back" : "the alpha's Persist stays");
                pc.ReleaseRuntime();
                pa.ReleaseRuntime();
            }
            finally
            {
                s.Shutdown();
            }
        }
    }
}
