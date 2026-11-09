using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // saved — add members at the end only. Local = the parent's axes (localPosition)
    public enum PositionSpace
    {
        World = 0,
        Local = 1,
        // a RectTransform's anchored position — another target moves as Local
        Anchored = 2,
    }

    // Local = the target's own axes (rotation offsets compose v · q(o))
    public enum TransformSpace
    {
        World = 0,
        Local = 1,
    }

    // the Tween of a rotation — Euler interpolates the angles, Shortest slerps
    public enum RotationInterpolation
    {
        Euler = 0,
        Shortest = 1,
    }

    // the axis Squash & Stretch stretches and the ones it presses
    public enum SquashAxis
    {
        XtoYZ = 0,
        XtoY = 1,
        XtoZ = 2,
        YtoXZ = 3,
        YtoX = 4,
        YtoZ = 5,
        ZtoXY = 6,
        ZtoX = 7,
        ZtoY = 8,
    }

    // what Look At turns toward — a reference's position, a world position or a world direction
    public enum LookAtMode
    {
        Transform = 0,
        Position = 1,
        Direction = 2,
    }

    // the world axis Look At keeps up
    public enum LookAtUp
    {
        Up = 0,
        Forward = 1,
        Right = 2,
    }

    // the Transform targets. No default motion — the add menu picks one
    [FeedbackTarget("Transform/Position", PreviewGrade.Full, Aliases = "move translate", Requires = typeof(Transform))]
    [Serializable]
    public sealed class PositionItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector3,
            supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Shake | MotionMask.Curve | MotionMask.Oscillate | MotionMask.Wiggle | MotionMask.Spring,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal PositionSpace m_Space;

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Transform/Rotation", PreviewGrade.Full, Aliases = "rotate turn spin", Requires = typeof(Transform))]
    [Serializable]
    public sealed class RotationItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector3,
            supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Shake | MotionMask.Curve | MotionMask.Oscillate | MotionMask.Wiggle | MotionMask.Spring,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal TransformSpace m_Space;
        [SerializeField] internal RotationInterpolation m_Interpolation;      // read by Tween only

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Transform/Scale", PreviewGrade.Full, Aliases = "size grow shrink", Requires = typeof(Transform))]
    [Serializable]
    public sealed class ScaleItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector3,
            supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Shake | MotionMask.Curve | MotionMask.Oscillate | MotionMask.Wiggle | MotionMask.Spring,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // the value is stops — the main axis takes 2^v, the others keep the volume
    [FeedbackTarget("Transform/Squash & Stretch", PreviewGrade.Full, Requires = typeof(Transform))]
    [Serializable]
    public sealed class SquashAndStretchItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Curve | MotionMask.Spring,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal SquashAxis m_Axis = SquashAxis.YtoXZ;

        internal override TargetTraits Traits => s_Traits;
    }

    // the value is not read — Set turns at once, Tween by its progress
    [FeedbackTarget("Transform/Look At", PreviewGrade.Full, Requires = typeof(Transform))]
    [Serializable]
    public sealed class LookAtItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Set | MotionMask.Tween,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal LookAtMode m_Mode;
        [SerializeField] internal TargetRef m_LookTarget = TargetRef.Default;
        [SerializeField] internal Vector3 m_Position;
        [SerializeField] internal Vector3 m_Direction = Vector3.forward;
        [SerializeField] internal LookAtUp m_Up;

        internal override TargetTraits Traits => s_Traits;
    }

    // the value is degrees about the world axis through the pivot — the target's rotation stays
    [FeedbackTarget("Transform/Rotate Around", PreviewGrade.Full, Requires = typeof(Transform))]
    [Serializable]
    public sealed class RotateAroundItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Oscillate,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal TargetRef m_Pivot = TargetRef.Default;
        [SerializeField] internal Vector3 m_Axis = Vector3.up;

        internal override TargetTraits Traits => s_Traits;
    }

    // the value is not read — the Tween's progress from the play's start to the destination (its world position and
    // rotation, its local scale)
    [FeedbackTarget("Transform/Match Transform", PreviewGrade.Full, Requires = typeof(Transform))]
    [Serializable]
    public sealed class MatchTransformItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal TargetRef m_Destination = TargetRef.Default;
        [SerializeField] internal bool m_Position = true;
        [SerializeField] internal bool m_Rotation = true;
        [SerializeField] internal bool m_Scale = true;
        [SerializeField] internal bool m_Track;

        internal override TargetTraits Traits => s_Traits;
    }

    // the value is not read. An empty Direct new parent is the scene's root
    [FeedbackTarget("Transform/Set Parent", PreviewGrade.Full, Requires = typeof(Transform))]
    [Serializable]
    public sealed class SetParentItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.Motion, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal TargetRef m_NewParent = TargetRef.Default;
        [SerializeField] internal bool m_WorldPositionStays = true;

        internal override TargetTraits Traits => s_Traits;
    }
}
