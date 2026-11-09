using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a force's mode — saved (the engine's ForceMode has other values: 0 · 5 · 1 · 2 — the implementation maps it;
    // Core can't name it)
    public enum PhysicsForceMode
    {
        Force = 0,
        Acceleration = 1,
        Impulse = 2,
        VelocityChange = 3,
    }

    public enum PhysicsForceMode2D
    {
        Force = 0,
        Impulse = 1,
    }

    public enum PhysicsForceKind
    {
        Force = 0,
        Torque = 1,
    }

    public enum VelocityWrite
    {
        Set = 0,
        Add = 1,
    }

    // the names Core can't type
    internal static class PhysicsNames
    {
        internal const string Rigidbody = "UnityEngine.Rigidbody, UnityEngine.PhysicsModule";
        internal const string Rigidbody2D = "UnityEngine.Rigidbody2D, UnityEngine.Physics2DModule";
        internal const string Assembly = "xpTURN.Feedback.Physics";
        internal const string Assembly2D = "xpTURN.Feedback.Physics2D";
    }

    // a force or a torque, once — each axis a random between the two (the play's seed) × the intensity · local is the body's
    // own axes · the next physics step takes it (Force · Acceleration act for that one step). Nothing is put back
    [FeedbackTarget("Physics/Force", PreviewGrade.DisplayOnly, Aliases = "rigidbody force torque impulse push knockback relative",
        RequiresName = PhysicsNames.Rigidbody, Implementation = PhysicsNames.Assembly)]
    [Serializable]
    public sealed class RigidbodyForceItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal PhysicsForceKind m_Kind;
        [SerializeField] internal TransformSpace m_Space;
        [SerializeField] internal PhysicsForceMode m_Mode = PhysicsForceMode.Impulse;
        [SerializeField] internal Vector3 m_Min = new Vector3(0f, 5f, 0f);
        [SerializeField] internal Vector3 m_Max = new Vector3(0f, 5f, 0f);

        internal override TargetTraits Traits => s_Traits;
    }

    // an explosion — every body with a collider in the radius around the target (the target's own physics scene),
    // each once · the centre the play's position, else the target's plus its local offset · one random force for all
    [FeedbackTarget("Physics/Explosion", PreviewGrade.DisplayOnly, Aliases = "rigidbody explosion blast radial push",
        Requires = typeof(Transform), Implementation = PhysicsNames.Assembly)]
    [Serializable]
    public sealed class RigidbodyExplosionItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal float m_MinForce = 10f;
        [SerializeField] internal float m_MaxForce = 10f;
        [SerializeField] internal float m_Radius = 5f;
        [SerializeField] internal float m_Upwards;
        [SerializeField] internal PhysicsForceMode m_Mode = PhysicsForceMode.Impulse;
        [SerializeField] internal Vector3 m_Offset;
        [SerializeField] internal LayerMask m_Layers = ~0;
        [SerializeField] internal int m_MaxBodies = 32;
        [SerializeField] internal bool m_Triggers;

        internal override TargetTraits Traits => s_Traits;
    }

    // the linear velocity — set or added (each axis a random between the two × the intensity)
    [FeedbackTarget("Physics/Velocity", PreviewGrade.DisplayOnly, Aliases = "rigidbody velocity speed launch",
        RequiresName = PhysicsNames.Rigidbody, Implementation = PhysicsNames.Assembly)]
    [Serializable]
    public sealed class RigidbodyVelocityItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal VelocityWrite m_Write;
        [SerializeField] internal TransformSpace m_Space;
        [SerializeField] internal Vector3 m_Min = new Vector3(0f, 5f, 0f);
        [SerializeField] internal Vector3 m_Max = new Vector3(0f, 5f, 0f);

        internal override TargetTraits Traits => s_Traits;
    }

    // a 2D force (a vector) or a torque (a float — radians: the angular velocity reads degrees)
    [FeedbackTarget("Physics 2D/Force", PreviewGrade.DisplayOnly, Aliases = "rigidbody2d force torque impulse push knockback relative",
        RequiresName = PhysicsNames.Rigidbody2D, Implementation = PhysicsNames.Assembly2D)]
    [Serializable]
    public sealed class Rigidbody2DForceItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal PhysicsForceKind m_Kind;
        [SerializeField] internal TransformSpace m_Space;
        [SerializeField] internal PhysicsForceMode2D m_Mode = PhysicsForceMode2D.Impulse;
        [SerializeField] internal Vector2 m_Min = new Vector2(0f, 5f);
        [SerializeField] internal Vector2 m_Max = new Vector2(0f, 5f);
        [SerializeField] internal float m_MinTorque = 1f;
        [SerializeField] internal float m_MaxTorque = 1f;

        internal override TargetTraits Traits => s_Traits;
    }

    // a 2D linear velocity — set or added
    [FeedbackTarget("Physics 2D/Velocity", PreviewGrade.DisplayOnly, Aliases = "rigidbody2d velocity speed launch",
        RequiresName = PhysicsNames.Rigidbody2D, Implementation = PhysicsNames.Assembly2D)]
    [Serializable]
    public sealed class Rigidbody2DVelocityItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal VelocityWrite m_Write;
        [SerializeField] internal TransformSpace m_Space;
        [SerializeField] internal Vector2 m_Min = new Vector2(0f, 5f);
        [SerializeField] internal Vector2 m_Max = new Vector2(0f, 5f);

        internal override TargetTraits Traits => s_Traits;
    }
}
