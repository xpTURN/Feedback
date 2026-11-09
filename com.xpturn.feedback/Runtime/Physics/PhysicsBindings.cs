using UnityEngine;

namespace xpTURN.Feedback
{
    // the instants' common parts — the engine's mode, the play's seeded random per axis × the intensity (the value
    // targets' seed), which bodies move
    internal static class PhysicsPrepare
    {
        internal static ForceMode Mode(PhysicsForceMode mode)
            => mode == PhysicsForceMode.Force ? ForceMode.Force
                : mode == PhysicsForceMode.Acceleration ? ForceMode.Acceleration
                : mode == PhysicsForceMode.Impulse ? ForceMode.Impulse : ForceMode.VelocityChange;

        internal static uint Seed(in BeginContext context) => Hash32.Tagged(context.ItemSeed, Hash32.Occurrence, Hash32.Fold(context.Occurrence));

        internal static float Unit(uint seed, uint axis) => Hash32.ToUnit(Hash32.Of(seed, axis));

        internal static Vector3 Pick(in BeginContext context, Vector3 min, Vector3 max)
        {
            uint seed = Seed(in context);
            return new Vector3(Mathf.Lerp(min.x, max.x, Unit(seed, 0)), Mathf.Lerp(min.y, max.y, Unit(seed, 1)), Mathf.Lerp(min.z, max.z, Unit(seed, 2))) * context.Intensity;
        }

        // a kinematic body takes no force and warns at a velocity write · an inactive one drops both (6000.3 keeps a
        // velocity)
        internal static bool Moves(Rigidbody body) => body != null && !body.isKinematic && body.gameObject.activeInHierarchy;
    }

    // Force · Torque — world or the body's own axes
    internal sealed class RigidbodyForceBinding : ItemBinding
    {
        readonly RigidbodyForceItem m_Item;
        FeedbackContext m_Context;
        Rigidbody m_Body;

        internal RigidbodyForceBinding(RigidbodyForceItem item) => m_Item = item;

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            m_Body = TargetResolver.Find<Rigidbody>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (m_Body == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Context = context.Context;
            return true;
        }

        internal override bool IsAlive => m_Body != null;

        internal override void Execute(int record, in BeginContext context)
        {
            if (context.Reverse || m_Body == null)
                return;
            Vector3 v = PhysicsPrepare.Pick(in context, m_Item.m_Min, m_Item.m_Max);
            bool local = m_Item.m_Space == TransformSpace.Local;
            bool torque = m_Item.m_Kind == PhysicsForceKind.Torque;
            if (m_Context.Kind == ContextKind.Preview)
            {
                // the transform's pose — an edit mode body's is not synced
                Transform t = m_Body.transform;
                m_Context.Mark(torque ? PreviewMarkKind.Torque : PreviewMarkKind.Force, t.position, local ? t.TransformDirection(v) : v, 0f);
                return;
            }
            if (!PhysicsPrepare.Moves(m_Body))
                return;
            ForceMode mode = PhysicsPrepare.Mode(m_Item.m_Mode);
            if (torque)
            {
                if (local) m_Body.AddRelativeTorque(v, mode);
                else m_Body.AddTorque(v, mode);
            }
            else
            {
                if (local) m_Body.AddRelativeForce(v, mode);
                else m_Body.AddForce(v, mode);
            }
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
        }
    }

    // the linear velocity — set or added · local is the body's rotation
    internal sealed class RigidbodyVelocityBinding : ItemBinding
    {
        readonly RigidbodyVelocityItem m_Item;
        FeedbackContext m_Context;
        Rigidbody m_Body;

        internal RigidbodyVelocityBinding(RigidbodyVelocityItem item) => m_Item = item;

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            m_Body = TargetResolver.Find<Rigidbody>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (m_Body == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Context = context.Context;
            return true;
        }

        internal override bool IsAlive => m_Body != null;

        internal override void Execute(int record, in BeginContext context)
        {
            if (context.Reverse || m_Body == null)
                return;
            Vector3 v = PhysicsPrepare.Pick(in context, m_Item.m_Min, m_Item.m_Max);
            bool local = m_Item.m_Space == TransformSpace.Local;
            if (m_Context.Kind == ContextKind.Preview)
            {
                Transform t = m_Body.transform;
                m_Context.Mark(PreviewMarkKind.Velocity, t.position, local ? t.TransformDirection(v) : v, 0f);
                return;
            }
            if (!PhysicsPrepare.Moves(m_Body))
                return;
            Vector3 w = local ? m_Body.rotation * v : v;
            if (m_Item.m_Write == VelocityWrite.Add)
                m_Body.linearVelocity += w;
            else
                m_Body.linearVelocity = w;
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
        }
    }

    // an explosion — the target's physics scene, a buffer made at the prepare, each body once
    internal sealed class RigidbodyExplosionBinding : ItemBinding
    {
        readonly RigidbodyExplosionItem m_Item;
        FeedbackContext m_Context;
        Transform m_Center;
        Collider[] m_Hits;
        Rigidbody[] m_Bodies;
#if DEBUG
        bool m_Warned;
#endif

        internal RigidbodyExplosionBinding(RigidbodyExplosionItem item) => m_Item = item;

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            m_Center = TargetResolver.Find<Transform>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (m_Center == null)
                return Refuse(TargetResolver.Describe(failure));
            int n = Mathf.Max(1, m_Item.m_MaxBodies);
            m_Hits = new Collider[n];
            m_Bodies = new Rigidbody[n];
            m_Context = context.Context;
            return true;
        }

        internal override bool IsAlive => m_Center != null;

        // the bodies in the radius, each once (a body's colliders come apart)
        int Find(Vector3 center)
        {
            int n = m_Center.gameObject.scene.GetPhysicsScene().OverlapSphere(center, m_Item.m_Radius, m_Hits, m_Item.m_Layers,
                m_Item.m_Triggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore);
#if DEBUG
            if (n == m_Hits.Length && !m_Warned)
            {
                m_Warned = true;
                Debug.LogWarning("Feedback: an explosion found as many colliders as its buffer holds (" + n + ") — raise Max Bodies.");
            }
#endif
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                Rigidbody body = m_Hits[i].attachedRigidbody;
                m_Hits[i] = null;
                if (body == null)
                    continue;
                bool seen = false;
                for (int j = 0; j < k && !seen; j++)
                    seen = m_Bodies[j] == body;
                if (!seen)
                    m_Bodies[k++] = body;
            }
            return k;
        }

        internal override void Execute(int record, in BeginContext context)
        {
            if (context.Reverse || m_Center == null)
                return;
            Vector3 center = context.HasPosition ? context.Position : m_Center.TransformPoint(m_Item.m_Offset);
            float force = Mathf.Lerp(m_Item.m_MinForce, m_Item.m_MaxForce, PhysicsPrepare.Unit(PhysicsPrepare.Seed(in context), 0)) * context.Intensity;
            bool preview = m_Context.Kind == ContextKind.Preview;
            // an edit mode physics world follows no transform — a play's is the last step's
            if (preview)
                Physics.SyncTransforms();
            int k = Find(center);
            if (preview)
            {
                m_Context.Mark(PreviewMarkKind.Explosion, center, Vector3.zero, m_Item.m_Radius);
                for (int i = 0; i < k; i++)
                {
                    Vector3 to = m_Bodies[i].transform.position - center;
                    float fall = m_Item.m_Radius > 0f ? Mathf.Clamp01(1f - to.magnitude / m_Item.m_Radius) : 0f;
                    m_Context.Mark(PreviewMarkKind.Force, m_Bodies[i].transform.position, to.normalized * (force * fall), 0f);
                    m_Bodies[i] = null;
                }
                return;
            }
            ForceMode mode = PhysicsPrepare.Mode(m_Item.m_Mode);
            for (int i = 0; i < k; i++)
            {
                if (PhysicsPrepare.Moves(m_Bodies[i]))
                    m_Bodies[i].AddExplosionForce(force, center, m_Item.m_Radius, m_Item.m_Upwards, mode);
                m_Bodies[i] = null;
            }
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
        }
    }
}
