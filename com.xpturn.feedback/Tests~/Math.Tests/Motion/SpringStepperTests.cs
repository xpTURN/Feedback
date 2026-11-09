using System;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests.Motion
{
    // the shape conditions are chosen against mutations: a start from rest hides a transposed matrix, one changed key hides
    // a missing one
    public sealed class SpringStepperTests
    {
        static (float Value, float Velocity) Run(float y0, float v0, float target, float frequency, float damping, float dt, int steps)
        {
            var stepper = new SpringStepper();
            float value = target + y0, velocity = v0;
            for (int i = 0; i < steps; i++)
            {
                stepper.Prepare(dt, frequency, damping);
                stepper.Advance(ref value, ref velocity, target);
            }
            return (value, velocity);
        }

        // away from 0 and moving: from rest at 0 a transposed matrix follows the same positions
        [Test]
        public void SS1_StepsFollowTheClosedForm()
        {
            foreach (float damping in new[] { 0.05f, 0.4f, 1f, 3f })
            {
                SpringShape shape = SpringShape.Of(6f, damping);
                var r = Run(1f, 2f, 7f, 6f, damping, 1f / 60f, 30);
                Assert.That(r.Value, Is.EqualTo(7f + SpringMath.Y(shape, 1f, 2f, 30f / 60f)).Within(1e-4f), $"ζ {damping}");
            }
        }

        [Test]
        public void SS2_SplittingDtKeepsThePath()
        {
            var sixty = Run(1f, 0f, 3f, 6f, 0.4f, 1f / 60f, 60);
            var twice = Run(1f, 0f, 3f, 6f, 0.4f, 1f / 120f, 120);
            Assert.That(sixty.Value, Is.EqualTo(twice.Value).Within(1e-5f));
        }

        // one step at 1/60 · 6 · 0.4, then one at the given keys — against a fresh stepper for the second
        static (float, float) Changed(float dt, float frequency, float damping, bool fresh)
        {
            var stepper = new SpringStepper();
            float value = 1f, velocity = 0f;
            stepper.Prepare(1f / 60f, 6f, 0.4f);
            stepper.Advance(ref value, ref velocity, 0f);
            if (fresh)
                stepper = new SpringStepper();
            stepper.Prepare(dt, frequency, damping);
            stepper.Advance(ref value, ref velocity, 0f);
            return (value, velocity);
        }

        // each key alone (a key set missing Damping passes a Frequency change)
        [Test]
        public void SS3_TheCacheRebuildsOnEachKeyAndReusesOtherwise()
        {
            Assert.That(Changed(1f / 60f, 6f, 0.4f, false), Is.EqualTo(Changed(1f / 60f, 6f, 0.4f, true)), "same keys");
            Assert.That(Changed(1f / 30f, 6f, 0.4f, false), Is.EqualTo(Changed(1f / 30f, 6f, 0.4f, true)), "dt");
            Assert.That(Changed(1f / 60f, 2f, 0.4f, false), Is.EqualTo(Changed(1f / 60f, 2f, 0.4f, true)), "Frequency");
            Assert.That(Changed(1f / 60f, 6f, 1.5f, false), Is.EqualTo(Changed(1f / 60f, 6f, 1.5f, true)), "Damping");
        }

        // a new stepper's keys are all 0: without the flag Step(0) would reuse an all-zero matrix
        [Test]
        public void SS4_TheFirstStepOfANewStepperIsBuilt()
        {
            var stepper = new SpringStepper();
            float value = 1f, velocity = 0f;
            stepper.Prepare(0f, 0f, 0f);
            stepper.Advance(ref value, ref velocity, 0f);
            Assert.That((value, velocity), Is.EqualTo((1f, 0f)));
        }

        // a regression check: SpringShape.Of is the only way to a matrix
        [Test]
        public void SS5_OutOfDomainKeysStayFinite()
        {
            float[] odd = { 0f, -1f, float.NaN, float.PositiveInfinity };
            foreach (float frequency in odd)
            foreach (float damping in odd)
            foreach (float dt in new[] { 1f / 60f, float.NaN, -1f, float.PositiveInfinity })
            {
                var r = Run(1f, 0f, 0f, frequency, damping, dt, 3);
                Assert.That(float.IsFinite(r.Value) && float.IsFinite(r.Velocity), $"f {frequency} ζ {damping} dt {dt}");
            }
        }

        [Test]
        public void SS6_NonFiniteStateIsCut()
        {
            var stepper = new SpringStepper();
            float value = 0f, velocity = 0f;
            stepper.Prepare(1f / 60f, 6f, 0.4f);
            stepper.Advance(ref value, ref velocity, 5f);
            (float, float) before = (value, velocity);
            stepper.Advance(ref value, ref velocity, float.NaN);
            Assert.That((value, velocity), Is.EqualTo(before), "a non-finite target skips the step");
            for (int i = 0; i < 600; i++)
                stepper.Advance(ref value, ref velocity, 5f);
            Assert.That(value, Is.EqualTo(5f).Within(1e-3f), "on again once the target is back");

            value = float.NaN;
            velocity = 3f;
            stepper.Advance(ref value, ref velocity, 5f);
            var fresh = new SpringStepper();
            fresh.Prepare(1f / 60f, 6f, 0.4f);
            float expected = 5f, expectedVelocity = 3f;
            fresh.Advance(ref expected, ref expectedVelocity, 5f);
            Assert.That((value, velocity), Is.EqualTo((expected, expectedVelocity)), "a non-finite value restarts at the target");

            value = 5f;
            velocity = float.PositiveInfinity;
            stepper.Advance(ref value, ref velocity, 5f);
            Assert.That((value, velocity), Is.EqualTo((5f, 0f)), "a non-finite velocity restarts at 0");
        }
    }
}
