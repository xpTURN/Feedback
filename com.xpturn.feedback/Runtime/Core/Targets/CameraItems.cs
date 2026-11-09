using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the rotation is the same motion evaluated again with the rotation amplitude
    [FeedbackTarget("Camera/Shake", PreviewGrade.Full, Aliases = "screen shake", Requires = typeof(Camera))]
    [Serializable]
    public sealed class CameraShakeItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector3,
            supportedMotions: MotionMask.Shake | MotionMask.Punch | MotionMask.Wiggle | MotionMask.Curve,
            category: FeedbackCategory.Camera, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal Vector3 m_RotationAmplitude;      // degrees, zero = no rotation shake

        public CameraShakeItem()
        {
            m_Target.m_Mode = TargetRefMode.MainCamera;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // the camera's value targets start on the main camera, as the shake does
    [FeedbackTarget("Camera/Field of View", PreviewGrade.Full, Requires = typeof(Camera))]
    [Serializable]
    public sealed class CameraFieldOfViewItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Shake | MotionMask.Spring,
            category: FeedbackCategory.Camera, flashSensitive: false, applyMode: ApplyMode.Offset);

        public CameraFieldOfViewItem()
        {
            m_Target.m_Mode = TargetRefMode.MainCamera;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Camera/Orthographic Size", PreviewGrade.Full, Requires = typeof(Camera))]
    [Serializable]
    public sealed class CameraOrthographicSizeItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float,
            supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Shake | MotionMask.Spring,
            category: FeedbackCategory.Camera, flashSensitive: false, applyMode: ApplyMode.Offset);

        public CameraOrthographicSizeItem()
        {
            m_Target.m_Mode = TargetRefMode.MainCamera;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // the motion's value is the zoom in stops — 1 is twice as close, −1 half (CameraZoomBinding)
    [FeedbackTarget("Camera/Zoom", PreviewGrade.Full, Requires = typeof(Camera))]
    [Serializable]
    public sealed class CameraZoomItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Punch,
            category: FeedbackCategory.Camera, flashSensitive: false, applyMode: ApplyMode.Offset);

        public CameraZoomItem()
        {
            m_Target.m_Mode = TargetRefMode.MainCamera;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // (near, far)
    [FeedbackTarget("Camera/Clipping Planes", PreviewGrade.Full, Requires = typeof(Camera))]
    [Serializable]
    public sealed class CameraClippingPlanesItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector2, supportedMotions: MotionMask.Tween,
            category: FeedbackCategory.Camera, flashSensitive: false, applyMode: ApplyMode.Offset);

        public CameraClippingPlanesItem()
        {
            m_Target.m_Mode = TargetRefMode.MainCamera;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Camera/Background Color", PreviewGrade.Full, Requires = typeof(Camera))]
    [Serializable]
    public sealed class CameraBackgroundColorItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Color, supportedMotions: MotionMask.Tween | MotionMask.Flash,
            category: FeedbackCategory.Camera, flashSensitive: true, applyMode: ApplyMode.Offset);

        public CameraBackgroundColorItem()
        {
            m_Target.m_Mode = TargetRefMode.MainCamera;
        }

        internal override TargetTraits Traits => s_Traits;
    }
}
