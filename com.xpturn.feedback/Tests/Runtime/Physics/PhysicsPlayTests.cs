using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // the real loop: an instant with no delay runs inside Play() (a velocity is written when it returns) and a force lands at
    // the next physics step · Force acts for that one step · the explosion pushes each body once with the engine's falloff · a
    // sleeping body wakes. The waits are physics steps (the frames until one differ between versions); no UnityEditor here
    public sealed class PhysicsPlayTests
    {
        readonly List<Object> m_Made = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Object o in m_Made)
                if (o != null) Object.Destroy(o);
            m_Made.Clear();
            yield return null;
        }

        Rigidbody Body(string name, Vector3 at)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            go.transform.position = at;
            Rigidbody rb = PhysicsFixtures.Body(go);
            return rb;
        }

        FeedbackPlayer Player(params TargetItem[] items)
        {
            var go = new GameObject("Player");
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            foreach (TargetItem item in items)
                p.m_Items.Add(item);
            return p;
        }

        static T With<T>(T item, Object target) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = new SetMotion();
            item.m_Timing.m_Duration = 0f;
            return item;
        }

        // an impulse of (0, 5, 0) lands at the next step · a velocity is written inside Play() · Force (0, 10, 0) acts for one
        // step (0.2 at a step of 0.02, not again)
        [UnityTest]
        public IEnumerator BQ05a_TheRealLoop()
        {
            Rigidbody pushed = Body("Pushed", Vector3.zero);
            Rigidbody set = Body("Set", new Vector3(3f, 0f, 0f));
            Rigidbody once = Body("Once", new Vector3(6f, 0f, 0f));
            RigidbodyForceItem force = With(new RigidbodyForceItem(), once);
            force.m_Mode = PhysicsForceMode.Force;
            force.m_Min = force.m_Max = new Vector3(0f, 10f, 0f);
            FeedbackPlayer p = Player(With(new RigidbodyForceItem(), pushed), With(new RigidbodyVelocityItem(), set), force);
            yield return null;
            p.Play();
            Assert.That((pushed.linearVelocity, set.linearVelocity), Is.EqualTo((Vector3.zero, new Vector3(0f, 5f, 0f))), "when Play() returns");
            yield return new WaitForFixedUpdate();
            Assert.That(pushed.linearVelocity.y, Is.EqualTo(5f).Within(1e-4f), "the impulse at the next step");
            float step = once.linearVelocity.y;
            yield return new WaitForFixedUpdate();
            Assert.That(step, Is.EqualTo(10f * Time.fixedDeltaTime).Within(1e-4f), "Force acts for one step");
            Assert.That(once.linearVelocity.y, Is.EqualTo(step).Within(1e-5f), "and not again");
        }

        // the explosion in play — a body of two colliders once (10 × (1 − 1.5 / 5) = 7), another body 7, a kinematic body
        // not at all
        [UnityTest]
        public IEnumerator BQ07e_TheExplosionInPlay()
        {
            var center = new GameObject("Center");
            m_Made.Add(center);
            center.transform.position = new Vector3(0f, 0f, 20f);
            Rigidbody compound = Body("Compound", new Vector3(2f, 0f, 20f));
            var part = new GameObject("Part");
            part.transform.SetParent(compound.transform, false);
            part.transform.localPosition = new Vector3(0.5f, 0f, 0f);
            part.AddComponent<BoxCollider>();
            Rigidbody plain = Body("Plain", new Vector3(-2f, 0f, 20f));
            Rigidbody kinematic = Body("Kinematic", new Vector3(0f, 2f, 20f));
            kinematic.isKinematic = true;
            yield return new WaitForFixedUpdate();
            FeedbackPlayer p = Player(With(new RigidbodyExplosionItem(), center.transform));
            yield return null;
            p.Play();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(compound.linearVelocity.x, Is.EqualTo(7f).Within(1e-3f), "a body of two colliders once");
            Assert.That(plain.linearVelocity.x, Is.EqualTo(-7f).Within(1e-3f), "the engine's falloff");
            Assert.That(kinematic.linearVelocity, Is.EqualTo(Vector3.zero), "a kinematic body skipped");
        }

        // a sleeping body wakes at the force and moves at the next step (no WakeUp)
        [UnityTest]
        public IEnumerator BQ10b_ASleepingBodyWakes()
        {
            Rigidbody body = Body("Sleeping", new Vector3(0f, 0f, -20f));
            yield return new WaitForFixedUpdate();
            body.Sleep();
            bool slept = body.IsSleeping();
            FeedbackPlayer p = Player(With(new RigidbodyForceItem(), body));
            yield return null;
            p.Play();
            yield return new WaitForFixedUpdate();
            Assert.That((slept, body.IsSleeping()), Is.EqualTo((true, false)), "asleep, then awake");
            Assert.That(body.linearVelocity.y, Is.EqualTo(5f).Within(1e-4f), "it moved");
        }
    }
}
