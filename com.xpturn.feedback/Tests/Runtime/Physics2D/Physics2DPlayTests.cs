using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests
{
    // 2D on the real loop: an impulse and a torque impulse are a velocity and an angular velocity when Play() returns (the
    // torque in radians, read in degrees) · a kinematic body's velocity moves it. No UnityEditor here
    public sealed class Physics2DPlayTests
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

        Rigidbody2D Body(string name, Vector3 at)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            go.transform.position = at;
            return Physics2DFixtures.Body(go);
        }

        static T With<T>(T item, Object target) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = new SetMotion();
            item.m_Timing.m_Duration = 0f;
            return item;
        }

        [UnityTest]
        public IEnumerator BQ08c_TwoDOnTheRealLoop()
        {
            Rigidbody2D pushed = Body("Pushed", Vector3.zero);
            Rigidbody2D turned = Body("Turned", new Vector3(3f, 0f, 0f));
            Rigidbody2D kinematic = Body("Kinematic", new Vector3(6f, 0f, 0f));
            kinematic.bodyType = RigidbodyType2D.Kinematic;
            Rigidbody2DForceItem torque = With(new Rigidbody2DForceItem(), turned);
            torque.m_Kind = PhysicsForceKind.Torque;
            var go = new GameObject("Player");
            m_Made.Add(go);
            var p = go.AddComponent<FeedbackPlayer>();
            p.m_Items.Add(With(new Rigidbody2DForceItem(), pushed));
            p.m_Items.Add(torque);
            p.m_Items.Add(With(new Rigidbody2DVelocityItem(), kinematic));
            yield return null;
            p.Play();
            Assert.That((pushed.linearVelocity, kinematic.linearVelocity), Is.EqualTo((new Vector2(0f, 5f), new Vector2(0f, 5f))), "an impulse · a kinematic velocity when Play() returns");
            Assert.That(turned.angularVelocity, Is.EqualTo(Mathf.Rad2Deg / turned.inertia).Within(1e-2f), "a torque impulse of 1 in radians, read in degrees");
            float y = kinematic.position.y;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(kinematic.position.y, Is.GreaterThan(y), "the kinematic body moves");
        }
    }
}
