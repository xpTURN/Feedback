using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using xpTURN.Feedback.Editor;
using Object = UnityEngine.Object;
using F = xpTURN.Feedback.Tests.Editor.ValidationFixtures;

namespace xpTURN.Feedback.Tests.Editor
{
    // M01 ~ M05 speak in their cases only — their severities, M03's fixes. The rules name the module's types (the editor
    // references them with the module off)
    public sealed class PhysicsRulesTests
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

        Rigidbody Body(string name, bool kinematic = false)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = kinematic;
            return rb;
        }

        Rigidbody2D Body2D(string name, RigidbodyType2D type)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = type;
            return rb;
        }

        static T On<T>(T item, Object target) where T : TargetItem
        {
            item.m_Target = new TargetRef { m_Mode = TargetRefMode.Direct, m_Object = target, m_Path = "", m_Anchor = "" };
            item.m_Motion = new SetMotion();
            return item;
        }

        static void Want(Component host, string id, int[] items, RuleSeverity severity)
        {
            List<RuleResult> results = F.Of(host, id);
            Assert.That(results.Select(r => r.Item).OrderBy(i => i), Is.EqualTo(items), id);
            Assert.That(results.All(r => r.Severity == severity), Is.True, id + " " + severity);
        }

        static string Message(Component host, string id, int item) => F.Of(host, id).Single(r => r.Item == item).Message;

        // M01 · M02: a kinematic 3D body under a Force · a Velocity (not an explosion's centre) · a static 2D body under both · a
        // kinematic 2D body under a Force only (a velocity moves it)
        [Test]
        public void BQ20a_TheBodyRules()
        {
            Rigidbody kinematic = Body("BQ20aKinematic", true);
            Rigidbody dynamic = Body("BQ20aDynamic");
            Rigidbody2D still = Body2D("BQ20aStatic", RigidbodyType2D.Static);
            Rigidbody2D moved = Body2D("BQ20aKinematic2D", RigidbodyType2D.Kinematic);
            Rigidbody2D free = Body2D("BQ20aDynamic2D", RigidbodyType2D.Dynamic);
            FeedbackPlayer p = m_Fx.Player("BQ20a",
                On(new RigidbodyForceItem(), kinematic),
                On(new RigidbodyVelocityItem(), kinematic),
                On(new RigidbodyExplosionItem(), kinematic.transform),
                On(new RigidbodyForceItem(), dynamic),
                On(new Rigidbody2DForceItem(), still),
                On(new Rigidbody2DVelocityItem(), still),
                On(new Rigidbody2DForceItem(), moved),
                On(new Rigidbody2DVelocityItem(), moved),
                On(new Rigidbody2DForceItem(), free),
                On(new RigidbodyForceItem { m_Active = false }, kinematic));
            Want(p, "FB-M01", new[] { 0, 1 }, RuleSeverity.Warning);
            Want(p, "FB-M02", new[] { 4, 5, 6 }, RuleSeverity.Warning);
            Assert.That((Message(p, "FB-M02", 4), Message(p, "FB-M02", 6)),
                Is.EqualTo(("The Rigidbody2D is static — a force or a velocity does not move it", "The Rigidbody2D is kinematic — a force does not move it (a velocity does)")));
        }

        // M03: Force · Acceleration act for one step — the fixes Impulse · VelocityChange (2D: Impulse) · an Impulse, a VelocityChange
        // speak not
        [Test]
        public void BQ20b_TheOneStepRule()
        {
            Rigidbody body = Body("BQ20b");
            Rigidbody2D body2 = Body2D("BQ20b2D", RigidbodyType2D.Dynamic);
            FeedbackPlayer p = m_Fx.Player("BQ20b",
                On(new RigidbodyForceItem { m_Mode = PhysicsForceMode.Force }, body),
                On(new RigidbodyForceItem { m_Mode = PhysicsForceMode.Acceleration }, body),
                On(new RigidbodyForceItem { m_Mode = PhysicsForceMode.Impulse }, body),
                On(new RigidbodyExplosionItem { m_Mode = PhysicsForceMode.Force }, body.transform),
                On(new RigidbodyExplosionItem { m_Mode = PhysicsForceMode.VelocityChange }, body.transform),
                On(new Rigidbody2DForceItem { m_Mode = PhysicsForceMode2D.Force }, body2),
                On(new Rigidbody2DForceItem { m_Mode = PhysicsForceMode2D.Impulse }, body2));
            Want(p, "FB-M03", new[] { 0, 1, 3, 5 }, RuleSeverity.Info);
            Assert.That(F.Of(p, "FB-M03").OrderBy(r => r.Item).Select(r => r.Fixes.Single().Name), Is.EqualTo(new[] { "Use Impulse", "Use VelocityChange", "Use Impulse", "Use Impulse" }));
            foreach (int i in new[] { 0, 1, 3, 5 })
            {
                RuleResult r = F.Of(p, "FB-M03").Single(x => x.Item == i);
                FeedbackValidator.Apply(r, r.Fixes.Single());
            }
            Assert.That((((RigidbodyForceItem)p.m_Items[0]).m_Mode, ((RigidbodyForceItem)p.m_Items[1]).m_Mode, ((RigidbodyExplosionItem)p.m_Items[3]).m_Mode, ((Rigidbody2DForceItem)p.m_Items[5]).m_Mode),
                Is.EqualTo((PhysicsForceMode.Impulse, PhysicsForceMode.VelocityChange, PhysicsForceMode.Impulse, PhysicsForceMode2D.Impulse)));
            Want(p, "FB-M03", new int[0], RuleSeverity.Info);
        }

        // M04 · M05: a range of zero (a velocity's Set to zero stops the body — not a zero range; a 2D torque reads its own) · an
        // explosion that finds nothing (a radius of 0, no layer) · a buffer below one
        [Test]
        public void BQ20c_TheRangeAndTheQueryRules()
        {
            Rigidbody body = Body("BQ20c");
            Rigidbody2D body2 = Body2D("BQ20c2D", RigidbodyType2D.Dynamic);
            FeedbackPlayer p = m_Fx.Player("BQ20c",
                On(new RigidbodyForceItem { m_Min = Vector3.zero, m_Max = Vector3.zero }, body),
                On(new RigidbodyVelocityItem { m_Min = Vector3.zero, m_Max = Vector3.zero }, body),
                On(new RigidbodyVelocityItem { m_Write = VelocityWrite.Add, m_Min = Vector3.zero, m_Max = Vector3.zero }, body),
                On(new RigidbodyExplosionItem { m_MinForce = 0f, m_MaxForce = 0f }, body.transform),
                On(new Rigidbody2DForceItem { m_Kind = PhysicsForceKind.Torque, m_Min = Vector2.zero, m_Max = Vector2.zero }, body2),
                On(new Rigidbody2DForceItem { m_Kind = PhysicsForceKind.Torque, m_MinTorque = 0f, m_MaxTorque = 0f }, body2),
                On(new Rigidbody2DVelocityItem { m_Write = VelocityWrite.Add, m_Min = Vector2.zero, m_Max = Vector2.zero }, body2),
                On(new RigidbodyExplosionItem { m_Radius = 0f }, body.transform),
                On(new RigidbodyExplosionItem { m_Layers = 0 }, body.transform),
                On(new RigidbodyExplosionItem { m_MaxBodies = 0 }, body.transform),
                On(new RigidbodyExplosionItem(), body.transform));
            Want(p, "FB-M04", new[] { 0, 2, 3, 5, 6 }, RuleSeverity.Warning);
            Want(p, "FB-M05", new[] { 7, 8, 9 }, RuleSeverity.Warning);
            Assert.That(Message(p, "FB-M05", 9), Does.Contain("below 1"));
        }
    }
}
