using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace xpTURN.Feedback.Tests.Editor
{
    // The stepper itself is the dotnet SpringStepperTests' — these compare against it, so they see the wiring (each
    // component, its target, the order) and the public shape. Shape conditions: all four types, a moving start, at the
    // target but moving, serialized as a field
    public sealed class SpringStructTests
    {
        const float Dt = 1f / 60f;

        static readonly System.Type[] Types = { typeof(SpringFloat), typeof(SpringVector2), typeof(SpringVector3), typeof(SpringColor) };

        [Test]
        public void E10a_StepIsTheStepperOnEachComponent()
        {
            var single = new SpringFloat(1f) { Target = 4f, Velocity = 2f };
            var pair = new SpringVector2(new Vector2(1f, -1f)) { Target = new Vector2(4f, 3f), Velocity = new Vector2(2f, -2f) };
            var triple = new SpringVector3(new Vector3(1f, -1f, 0.5f)) { Target = new Vector3(4f, 3f, -2f), Velocity = new Vector3(2f, -2f, 1f) };
            var color = new SpringColor(new Color(0.1f, 0.2f, 0.3f, 0.4f)) { Target = new Color(0.9f, 0.8f, 0.7f, 0.2f), Velocity = new Color(1f, -1f, 0.5f, 2f) };
            SpringStepper s1 = default, s2 = default, s3 = default, s4 = default;
            float v1 = 1f, w1 = 2f;
            Vector2 v2 = new Vector2(1f, -1f), w2 = new Vector2(2f, -2f);
            Vector3 v3 = new Vector3(1f, -1f, 0.5f), w3 = new Vector3(2f, -2f, 1f);
            Color v4 = new Color(0.1f, 0.2f, 0.3f, 0.4f), w4 = new Color(1f, -1f, 0.5f, 2f);
            for (int i = 0; i < 30; i++)
            {
                single.Step(Dt);
                pair.Step(Dt);
                triple.Step(Dt);
                color.Step(Dt);
                s1.Prepare(Dt, 6f, 0.4f);
                s1.Advance(ref v1, ref w1, 4f);
                s2.Prepare(Dt, 6f, 0.4f);
                s2.Advance(ref v2.x, ref w2.x, 4f);
                s2.Advance(ref v2.y, ref w2.y, 3f);
                s3.Prepare(Dt, 6f, 0.4f);
                s3.Advance(ref v3.x, ref w3.x, 4f);
                s3.Advance(ref v3.y, ref w3.y, 3f);
                s3.Advance(ref v3.z, ref w3.z, -2f);
                s4.Prepare(Dt, 6f, 0.4f);
                s4.Advance(ref v4.r, ref w4.r, 0.9f);
                s4.Advance(ref v4.g, ref w4.g, 0.8f);
                s4.Advance(ref v4.b, ref w4.b, 0.7f);
                s4.Advance(ref v4.a, ref w4.a, 0.2f);
            }
            Assert.That((single.Value, single.Velocity), Is.EqualTo((v1, w1)), "SpringFloat");
            Assert.That((pair.Value, pair.Velocity), Is.EqualTo((v2, w2)), "SpringVector2");
            Assert.That((triple.Value, triple.Velocity), Is.EqualTo((v3, w3)), "SpringVector3");
            Assert.That((color.Value, color.Velocity), Is.EqualTo((v4, w4)), "SpringColor");
        }

        // a moving start — Bump from rest cannot tell adding from setting
        [Test]
        public void E10b_Reg17_BumpAddsToTheVelocity()
        {
            var single = new SpringFloat(0f) { Velocity = 1f };
            var pair = new SpringVector2(Vector2.zero) { Velocity = Vector2.one };
            var triple = new SpringVector3(Vector3.zero) { Velocity = Vector3.one };
            var color = new SpringColor(Color.clear) { Velocity = new Color(1f, 1f, 1f, 1f) };
            single.Bump(3f);
            pair.Bump(new Vector2(3f, 3f));
            triple.Bump(new Vector3(3f, 3f, 3f));
            color.Bump(new Color(3f, 3f, 3f, 3f));
            Assert.That((single.Velocity, pair.Velocity, triple.Velocity, color.Velocity),
                Is.EqualTo((4f, new Vector2(4f, 4f), new Vector3(4f, 4f, 4f), new Color(4f, 4f, 4f, 4f))));
        }

        [Test]
        public void E10b_Reg17_MoveToKeepsTheVelocity()
        {
            var single = new SpringFloat(0f) { Velocity = 1f };
            var pair = new SpringVector2(Vector2.zero) { Velocity = Vector2.one };
            var triple = new SpringVector3(Vector3.zero) { Velocity = Vector3.one };
            var color = new SpringColor(Color.clear) { Velocity = Color.white };
            single.MoveTo(5f);
            pair.MoveTo(Vector2.one * 5f);
            triple.MoveTo(Vector3.one * 5f);
            color.MoveTo(Color.red);
            Assert.That((single.Velocity, pair.Velocity, triple.Velocity, color.Velocity), Is.EqualTo((1f, Vector2.one, Vector3.one, Color.white)), "velocity");
            Assert.That((single.Target, pair.Target, triple.Target, color.Target), Is.EqualTo((5f, Vector2.one * 5f, Vector3.one * 5f, Color.red)), "target");
        }

        [Test]
        public void E10b_Reg17_SnapStopsAtTheTarget()
        {
            var single = new SpringFloat(0f) { Target = 5f, Velocity = 1f };
            var pair = new SpringVector2(Vector2.zero) { Target = Vector2.one, Velocity = Vector2.one };
            var triple = new SpringVector3(Vector3.zero) { Target = Vector3.one, Velocity = Vector3.one };
            var color = new SpringColor(Color.clear) { Target = Color.white, Velocity = Color.white };
            single.Snap();
            pair.Snap();
            triple.Snap();
            color.Snap();
            Assert.That((single.Value, pair.Value, triple.Value, color.Value), Is.EqualTo((5f, Vector2.one, Vector3.one, Color.white)), "value");
            Assert.That((single.Velocity, pair.Velocity, triple.Velocity, color.Velocity), Is.EqualTo((0f, Vector2.zero, Vector3.zero, Color.clear)), "velocity");
        }

        // at the target but moving — after Snap the velocity is 0 and a position-only check passes
        [Test]
        public void E10b_Reg17_IsSettledSeesPositionAndVelocity()
        {
            var single = new SpringFloat(5f) { Velocity = 3f };
            var pair = new SpringVector2(Vector2.one) { Velocity = new Vector2(0f, 3f) };
            var triple = new SpringVector3(Vector3.one) { Velocity = new Vector3(0f, 0f, 3f) };
            var color = new SpringColor(Color.white) { Velocity = new Color(0f, 0f, 0f, 3f) };
            Assert.That((single.IsSettled(1e-3f), pair.IsSettled(1e-3f), triple.IsSettled(1e-3f), color.IsSettled(1e-3f)), Is.EqualTo((false, false, false, false)), "moving");
            var away = new SpringColor(Color.white) { Target = new Color(1f, 1f, 1f, 0.5f) };
            Assert.That(away.IsSettled(1e-3f), Is.False, "alpha away");
            single.Snap();
            Assert.That(single.IsSettled(1e-3f), Is.True, "snapped");
        }

        [Test]
        public void E10c_TheConstructorGivesTheMotionDefaults()
        {
            var single = new SpringFloat(2f);
            var triple = new SpringVector3(Vector3.one);
            var color = new SpringColor(Color.red, 3f, 0.7f);
            Assert.That((single.Frequency, single.Damping, single.Target, single.Velocity), Is.EqualTo((6f, 0.4f, 2f, 0f)), "SpringFloat");
            Assert.That((triple.Frequency, triple.Damping, triple.Target, triple.Velocity), Is.EqualTo((6f, 0.4f, Vector3.one, Vector3.zero)), "SpringVector3");
            Assert.That((color.Frequency, color.Damping, color.Target), Is.EqualTo((3f, 0.7f, Color.red)), "given");
            var none = default(SpringVector3);
            none.Target = Vector3.one;
            none.Step(Dt);
            Assert.That(float.IsNaN(none.Value.x) || float.IsInfinity(none.Value.x), Is.False, "default steps finitely");
        }

        [Test]
        public void E10d_OutOfDomainFieldsStayFinite()
        {
            var single = new SpringFloat(0f) { Target = 5f, Frequency = 0f, Damping = -1f };
            var color = new SpringColor(Color.clear) { Target = Color.white, Frequency = float.NaN, Damping = float.PositiveInfinity };
            for (int i = 0; i < 60; i++)
            {
                single.Step(Dt);
                color.Step(Dt);
            }
            Assert.That(float.IsNaN(single.Value) || float.IsInfinity(single.Value) || float.IsNaN(color.Value.a) || float.IsInfinity(color.Value.a), Is.False);
        }

        [Test]
        public void E10e_FieldsRoundTripThroughAHolder()
        {
            var holder = ScriptableObject.CreateInstance<SpringTestHolder>();
            var back = ScriptableObject.CreateInstance<SpringTestHolder>();
            try
            {
                holder.single = new SpringFloat(1f, 3f, 0.7f) { Target = 2f, Velocity = 0.5f };
                holder.pair = new SpringVector2(Vector2.one, 3f, 0.7f) { Target = Vector2.up };
                holder.triple = new SpringVector3(Vector3.one, 3f, 0.7f) { Target = Vector3.up };
                holder.color = new SpringColor(Color.red, 3f, 0.7f) { Target = Color.blue };
                holder.triple.Step(Dt);
                string json = JsonUtility.ToJson(holder);
                JsonUtility.FromJsonOverwrite(json, back);
                Assert.That((back.single.Value, back.single.Velocity, back.single.Target, back.single.Frequency, back.single.Damping), Is.EqualTo((1f, 0.5f, 2f, 3f, 0.7f)), "SpringFloat");
                Assert.That((back.pair.Frequency, back.pair.Target), Is.EqualTo((3f, Vector2.up)), "SpringVector2");
                Assert.That((back.triple.Value, back.triple.Velocity, back.triple.Target), Is.EqualTo((holder.triple.Value, holder.triple.Velocity, Vector3.up)), "SpringVector3");
                Assert.That((back.color.Value, back.color.Target), Is.EqualTo((Color.red, Color.blue)), "SpringColor");
                Assert.That(json, Does.Not.Contain("Stepper").And.Not.Contain("_valid").And.Not.Contain("_matrix"), "the cache is not written");
            }
            finally
            {
                Object.DestroyImmediate(holder);
                Object.DestroyImmediate(back);
            }
        }

        // the verdict — the constraint also counts others' allocations (6000.3), so five reports in a row
        static bool Allocates(TestDelegate run)
        {
            for (int i = 0; i < 5; i++)
                if (!Is.AllocatingGCMemory().ApplyTo(run).IsSuccess)
                    return false;
            return true;
        }

        [Test]
        public void E10f_NoAllocation()
        {
            var single = new SpringFloat(0f) { Target = 1f };
            var pair = new SpringVector2(Vector2.zero) { Target = Vector2.one };
            var triple = new SpringVector3(Vector3.zero) { Target = Vector3.one };
            var color = new SpringColor(Color.clear) { Target = Color.white };
            bool settled = false;

            void Run()
            {
                single.Step(Dt);
                single.Bump(0.1f);
                single.MoveTo(1f);
                settled |= single.IsSettled(1e-3f);
                pair.Step(Dt);
                pair.Bump(Vector2.one);
                pair.MoveTo(Vector2.one);
                settled |= pair.IsSettled(1e-3f);
                triple.Step(Dt);
                triple.Bump(Vector3.one);
                triple.MoveTo(Vector3.one);
                settled |= triple.IsSettled(1e-3f);
                color.Step(Dt);
                color.Bump(Color.white);
                color.MoveTo(Color.white);
                settled |= color.IsSettled(1e-3f);
                single.Snap();
                pair.Snap();
                triple.Snap();
                color.Snap();
            }

            TestDelegate run = Run;
            run();
            Assert.That(Allocates(run), Is.False);
        }

        // the positive control — boxing a value allocates
        [Test]
        public void E10f_BoxingIsSeenAllocating()
        {
            var triple = new SpringVector3(Vector3.one);
            int sink = 0;
            TestDelegate run = () => sink += ((object)triple.Value).GetHashCode();
            run();
            Assert.That(Allocates(run), Is.True);
        }

        // [Min] and readonly change no behavior, so only reflection sees them
        [Test]
        public void E10g_SerializableRangesAndReadonly()
        {
            foreach (var t in Types)
            {
                Assert.That(t.IsSerializable, $"{t.Name} [Serializable]");
                Assert.That(t.GetField("Frequency").GetCustomAttribute<MinAttribute>()?.min, Is.EqualTo(0.001f), $"{t.Name} Frequency");
                Assert.That(t.GetField("Damping").GetCustomAttribute<MinAttribute>()?.min, Is.EqualTo(0.01f), $"{t.Name} Damping");
                Assert.That(t.GetMethod("IsSettled").GetCustomAttributes().Any(a => a.GetType().Name == "IsReadOnlyAttribute"), $"{t.Name} readonly IsSettled");
            }
        }
    }
}
