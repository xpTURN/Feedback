using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using xpTURN.Feedback.Editor;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // D — settings and playback
    internal sealed class ValidationSettingsTests
    {
        F m_Fx;

        [SetUp]
        public void SetUp() => m_Fx = new F();

        [TearDown]
        public void TearDown()
        {
            m_Fx.Clean();
            AssetDatabase.DeleteAsset("Assets/ValidationTestsSettingsA");
            AssetDatabase.DeleteAsset("Assets/ValidationTestsSettingsB");
        }

        [Test]
        public void VT06a_LazyWithTheFirstFrame()
        {
            FeedbackPlayer p = m_Fx.Player("VT06a", F.Position());
            p.m_Initialization = PlayerInitialization.Lazy;
            Assert.That(F.Of(p, "FB-D01"), Is.Empty, "without the first frame");
            p.m_ApplyInitialState = true;
            RuleResult r = F.Of(p, "FB-D01").Single();
            FeedbackValidator.Apply(r, r.Fixes.Single());
            Assert.That(p.m_Initialization, Is.EqualTo(PlayerInitialization.Awake));
            Assert.That(F.Of(p, "FB-D01"), Is.Empty);
        }

        [Test]
        public void VT06b_PlaysOnEnable()
        {
            FeedbackPlayer p = m_Fx.Player("VT06b", F.Position());
            Assert.That(F.Of(p, "FB-D02"), Is.Empty);
            p.m_AutoPlay = PlayerAutoPlay.OnEnable;
            Assert.That(F.Of(p, "FB-D02").Single().Severity, Is.EqualTo(RuleSeverity.Info));
        }

        // two Persist writes on one property with the first frame — the later item
        [Test]
        public void VT06c_TwoPersistsWithTheFirstFrame()
        {
            PositionItem local = F.Position();
            local.m_Space = PositionSpace.Local;
            FeedbackPlayer p = m_Fx.Player("VT06c", F.Position(), local, F.Item<PositionItem>(new PunchMotion()));
            Assert.That(F.Of(p, "FB-D03"), Is.Empty, "without the first frame");
            p.m_ApplyInitialState = true;
            Assert.That(F.Of(p, "FB-D03").Select(r => r.Item), Is.EqualTo(new[] { 1 }), "World and Local share the slot · a punch is an offset");
            FeedbackPlayer other = m_Fx.Player("VT06cOther", F.Position(), F.Item<RotationItem>(F.Tween));
            other.m_ApplyInitialState = true;
            Assert.That(F.Of(other, "FB-D03"), Is.Empty, "another property");
        }

        [Test]
        public void VT06d_ASpringBumpUnderRestart()
        {
            FeedbackPlayer p = m_Fx.Player("VT06d", F.Item<PositionItem>(new SpringMotion()));
            RuleResult r = F.Of(p, "FB-D04").Single();
            FeedbackValidator.Apply(r, r.Fixes.Single());
            Assert.That(p.m_Policy, Is.EqualTo(PlayPolicy.Overlap));
            Assert.That(F.Of(p, "FB-D04"), Is.Empty);
            var moveTo = new SpringMotion { m_Params = new SpringParams { m_Mode = SpringMode.MoveTo, m_Value = Vector4.one, m_Frequency = 6f, m_Damping = 0.4f, m_Settle = 0.001f } };
            Assert.That(F.Of(m_Fx.Player("VT06dMoveTo", F.Item<PositionItem>(moveTo)), "FB-D04"), Is.Empty, "a MoveTo");
        }

        [Test]
        public void VT06e_AMotionOutsideItsDomain()
        {
            var punch = new PunchMotion { m_Params = new PunchParams { m_Amplitude = Vector4.one, m_Vibrato = 5f, m_Decay = 0.001f } };
            Assert.That(F.Of(m_Fx.Player("VT06ePunch", F.Item<ScaleItem>(punch)), "FB-D05").Single().Message, Does.Contain("Decay 0.001 is below 0.01"));
            var spring = new SpringMotion { m_Params = new SpringParams { m_Mode = SpringMode.Bump, m_Value = Vector4.one, m_Frequency = 0f, m_Damping = 0.4f, m_Settle = 0.001f } };
            Assert.That(F.Of(m_Fx.Player("VT06eSpring", F.Item<PositionItem>(spring)), "FB-D05").Single().Message, Does.Contain("Frequency 0"));
            Assert.That(F.Of(m_Fx.Player("VT06eQuiet", F.Item<ScaleItem>(new PunchMotion()), F.Item<PositionItem>(new SpringMotion())), "FB-D05"), Is.Empty);
        }

        // an authored x outside ±90° — Shortest for a Tween, words only for a spring
        [Test]
        public void VT06f_AnEulerStartFlipped()
        {
            FeedbackPlayer tween = m_Fx.Player("VT06fTween", F.Item<RotationItem>(F.Tween));
            TransformUtils.SetInspectorRotation(tween.transform, new Vector3(120f, 0f, 0f));
            RuleResult r = F.Of(tween, "FB-D06").Single();
            FeedbackValidator.Apply(r, r.Fixes.Single());
            Assert.That(((RotationItem)tween.m_Items[0]).m_Interpolation, Is.EqualTo(RotationInterpolation.Shortest));
            Assert.That(F.Of(tween, "FB-D06"), Is.Empty);
            var moveTo = new SpringMotion { m_Params = new SpringParams { m_Mode = SpringMode.MoveTo, m_Value = Vector4.one, m_Frequency = 6f, m_Damping = 0.4f, m_Settle = 0.001f } };
            FeedbackPlayer spring = m_Fx.Player("VT06fSpring", F.Item<RotationItem>(moveTo));
            TransformUtils.SetInspectorRotation(spring.transform, new Vector3(120f, 0f, 0f));
            Assert.That(F.Of(spring, "FB-D06").Single().Fixes, Is.Empty, "a spring reads no interpolation");
            FeedbackPlayer quiet = m_Fx.Player("VT06fQuiet", F.Item<RotationItem>(F.Tween));
            TransformUtils.SetInspectorRotation(quiet.transform, new Vector3(30f, 0f, 0f));
            Assert.That(F.Of(quiet, "FB-D06"), Is.Empty);
        }

        [Test]
        public void VT06g_AnActionsInfinite()
        {
            var stop = new HitStopItem();
            stop.m_Timing.m_Infinite = true;
            var ev = new UnityEventItem();
            ev.m_Timing.m_Infinite = true;
            FeedbackPlayer p = m_Fx.Player("VT06g", stop, ev);
            List<RuleResult> r = F.Of(p, "FB-D07");
            Assert.That(r.Select(x => x.Item), Is.EqualTo(new[] { 0, 1 }));
            FeedbackValidator.Apply(r[0], r[0].Fixes.Single());
            Assert.That(p.m_Items[0].m_Timing.m_Infinite, Is.False);
        }

        // a project rule: two Resources/FeedbackSettings
        [Test]
        public void VT06h_TwoSettings()
        {
            foreach (string root in new[] { "ValidationTestsSettingsA", "ValidationTestsSettingsB" })
            {
                AssetDatabase.CreateFolder("Assets", root);
                AssetDatabase.CreateFolder("Assets/" + root, "Resources");
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<FeedbackSettings>(), "Assets/" + root + "/Resources/FeedbackSettings.asset");
            }
            List<RuleResult> r = FeedbackValidator.RunProject().Where(x => x.Id == "FB-D08").ToList();
            Assert.That(r.Select(x => AssetDatabase.GetAssetPath(x.Host)), Is.SupersetOf(new[] { "Assets/ValidationTestsSettingsA/Resources/FeedbackSettings.asset", "Assets/ValidationTestsSettingsB/Resources/FeedbackSettings.asset" }));
            Assert.That(F.Of(m_Fx.Player("VT06h"), "FB-D08"), Is.Empty, "not in a host's check");
        }

        // two Persist writes on one property with the first frame, played Backward — the later item
        [Test]
        public void VT06i_TwoPersistsBackward()
        {
            PositionItem local = F.Position();
            local.m_Space = PositionSpace.Local;
            FeedbackPlayer p = m_Fx.Player("VT06i", F.Position(), local, F.Item<PositionItem>(new PunchMotion()));
            p.m_ApplyInitialState = true;
            p.m_Direction = PlayerDirection.Backward;
            RuleResult r = F.Of(p, "FB-D03").Single();
            Assert.That(r.Item, Is.EqualTo(0), "the later item wins — the earlier one is reported");
            Assert.That(r.Message, Does.Contain("slot 1's end").And.Contain("later item wins"));
            p.m_Direction = PlayerDirection.Toggle;
            Assert.That(F.Of(p, "FB-D03").Select(x => x.Item), Is.EqualTo(new[] { 1 }), "Toggle's first play is Forward");
        }
    }
}
