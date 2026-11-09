using UnityEngine;

namespace xpTURN.Feedback
{
    // the 2D instants' common parts (PhysicsPrepare's in the other assembly — the two don't see each other)
    internal static class Physics2DPrepare
    {
        internal static uint Seed(in BeginContext context) => Hash32.Tagged(context.ItemSeed, Hash32.Occurrence, Hash32.Fold(context.Occurrence));

        internal static float Unit(uint seed, uint axis) => Hash32.ToUnit(Hash32.Of(seed, axis));

        internal static Vector2 Pick(in BeginContext context, Vector2 min, Vector2 max)
        {
            uint seed = Seed(in context);
            return new Vector2(Mathf.Lerp(min.x, max.x, Unit(seed, 0)), Mathf.Lerp(min.y, max.y, Unit(seed, 1))) * context.Intensity;
        }

        // a force moves a dynamic body only · a velocity a kinematic one too (a static one warns) · a body not simulated
        // drops both
        internal static bool Pushes(Rigidbody2D body) => body != null && body.gameObject.activeInHierarchy && body.simulated && body.bodyType == RigidbodyType2D.Dynamic;

        internal static bool Moves(Rigidbody2D body) => body != null && body.gameObject.activeInHierarchy && body.simulated && body.bodyType != RigidbodyType2D.Static;
    }

    // a 2D force (world or the body's own axes) or a torque
    internal sealed class Rigidbody2DForceBinding : ItemBinding
    {
        readonly Rigidbody2DForceItem m_Item;
        FeedbackContext m_Context;
        Rigidbody2D m_Body;

        internal Rigidbody2DForceBinding(Rigidbody2DForceItem item) => m_Item = item;

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
            m_Body = TargetResolver.Find<Rigidbody2D>(in m_Item.m_Target, in context, out TargetFailure failure);
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
            bool torque = m_Item.m_Kind == PhysicsForceKind.Torque;
            bool local = m_Item.m_Space == TransformSpace.Local;
            float amount = torque ? Mathf.Lerp(m_Item.m_MinTorque, m_Item.m_MaxTorque, Physics2DPrepare.Unit(Physics2DPrepare.Seed(in context), 0)) * context.Intensity : 0f;
            Vector2 v = torque ? Vector2.zero : Physics2DPrepare.Pick(in context, m_Item.m_Min, m_Item.m_Max);
            if (m_Context.Kind == ContextKind.Preview)
            {
                Transform t = m_Body.transform;
                if (torque)
                    m_Context.Mark(PreviewMarkKind.Torque, t.position, new Vector3(0f, 0f, amount), 0f);
                else
                    m_Context.Mark(PreviewMarkKind.Force, t.position, local ? t.TransformDirection(v) : (Vector3)v, 0f);
                return;
            }
            if (!Physics2DPrepare.Pushes(m_Body))
                return;
            ForceMode2D mode = m_Item.m_Mode == PhysicsForceMode2D.Force ? ForceMode2D.Force : ForceMode2D.Impulse;
            if (torque)
                m_Body.AddTorque(amount, mode);
            else if (local)
                m_Body.AddRelativeForce(v, mode);
            else
                m_Body.AddForce(v, mode);
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

    // a 2D linear velocity — set or added · local is the body's rotation
    internal sealed class Rigidbody2DVelocityBinding : ItemBinding
    {
        readonly Rigidbody2DVelocityItem m_Item;
        FeedbackContext m_Context;
        Rigidbody2D m_Body;

        internal Rigidbody2DVelocityBinding(Rigidbody2DVelocityItem item) => m_Item = item;

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
            m_Body = TargetResolver.Find<Rigidbody2D>(in m_Item.m_Target, in context, out TargetFailure failure);
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
            Vector2 v = Physics2DPrepare.Pick(in context, m_Item.m_Min, m_Item.m_Max);
            bool local = m_Item.m_Space == TransformSpace.Local;
            if (m_Context.Kind == ContextKind.Preview)
            {
                Transform t = m_Body.transform;
                m_Context.Mark(PreviewMarkKind.Velocity, t.position, local ? t.TransformDirection(v) : (Vector3)v, 0f);
                return;
            }
            if (!Physics2DPrepare.Moves(m_Body))
                return;
            Vector2 w = local ? m_Body.GetRelativeVector(v) : v;
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
}
