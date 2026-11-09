using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the names Core can't type: Cinemachine's types, so the editor and the tests name them by their strings too
    internal static class CinemachineNames
    {
        internal const string Camera = "Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine";
        internal const string Perlin = "Unity.Cinemachine.CinemachineBasicMultiChannelPerlin, Unity.Cinemachine";
        internal const string ImpulseSource = "Unity.Cinemachine.CinemachineImpulseSource, Unity.Cinemachine";
        internal const string Brain = "Unity.Cinemachine.CinemachineBrain, Unity.Cinemachine";
        internal const string Listener = "Unity.Cinemachine.CinemachineImpulseListener, Unity.Cinemachine";
        internal const string FollowZoom = "Unity.Cinemachine.CinemachineFollowZoom, Unity.Cinemachine";
        internal const string Recomposer = "Unity.Cinemachine.CinemachineRecomposer, Unity.Cinemachine";
        internal const string FreeLookModifier = "Unity.Cinemachine.CinemachineFreeLookModifier, Unity.Cinemachine";
        internal const string ShakeExtension = "xpTURN.Feedback.CinemachineShakeExtension, xpTURN.Feedback.Cinemachine";
        internal const string Assembly = "xpTURN.Feedback.Cinemachine";
    }

    // Cinemachine's impulse shapes — the package's order (Custom is the item's curve)
    public enum CinemachineImpulseShape
    {
        Custom = 0,
        Recoil = 1,
        Bump = 2,
        Explosion = 3,
        Rumble = 4,
    }

    // the package's impulse types without Legacy
    public enum CinemachineImpulseSpread
    {
        Uniform = 0,
        Dissipating = 1,
        Propagating = 2,
    }

    public enum CinemachineImpulseMode
    {
        Definition = 0,
        Source = 1,
    }

    // the package's blend styles
    public enum CinemachineBlendStyle
    {
        Cut = 0,
        EaseInOut = 1,
        EaseIn = 2,
        EaseOut = 3,
        HardIn = 4,
        HardOut = 5,
        Linear = 6,
        Custom = 7,
    }

    [Serializable]
    public struct CinemachineBlendSpec
    {
        [SerializeField] internal bool m_Override;
        [SerializeField] internal CinemachineBlendStyle m_Style;
        [SerializeField] internal float m_Time;
        [SerializeField] internal AnimationCurve m_Curve;

        internal static CinemachineBlendSpec Default => new CinemachineBlendSpec { m_Style = CinemachineBlendStyle.EaseInOut, m_Time = 1f };
    }

    // an impulse into Cinemachine's manager — an action with its own length (its signal's). At the play's place or the
    // origin's position; the velocity times the play's intensity
    [FeedbackTarget("Cinemachine/Impulse", PreviewGrade.Full, Aliases = "impulse shake", Implementation = CinemachineNames.Assembly)]
    [Serializable]
    public sealed class CinemachineImpulseItem : ActionItem
    {
        [SerializeField] internal TargetRef m_Origin = new TargetRef { m_Mode = TargetRefMode.Self, m_Path = "", m_Anchor = "" };
        [SerializeField] internal CinemachineImpulseMode m_Mode;
        [SerializeField] internal Component m_Source;
        [SerializeField] internal CinemachineImpulseShape m_Shape = CinemachineImpulseShape.Bump;
        [SerializeField] internal AnimationCurve m_CustomShape;
        [SerializeField] internal CinemachineImpulseSpread m_Spread;
        [SerializeField] internal float m_Duration = 0.2f;
        [SerializeField] internal int m_Channel = 1;
        [SerializeField] internal float m_DissipationDistance = 100f;
        [SerializeField] internal float m_DissipationRate = 0.25f;
        [SerializeField] internal float m_PropagationSpeed = 343f;
        [SerializeField] internal Vector3 m_Velocity = new Vector3(0f, -1f, 0f);

        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.Camera;
        internal override float NaturalDuration => m_Duration > 0f ? m_Duration : 0f;
        internal override bool ActsBackward => false;
    }

    // every impulse in Cinemachine's manager gone — an act, instant, any category
    [FeedbackTarget("Cinemachine/Impulse Clear", PreviewGrade.Full, Aliases = "impulse clear stop", Implementation = CinemachineNames.Assembly)]
    [Serializable]
    public sealed class CinemachineImpulseClearItem : ActionItem
    {
        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;
        internal override float NaturalDuration => 0f;
        internal override bool ActsBackward => false;
    }

    // a Cinemachine camera's lens — written before the brain (the Update stage). The lens and noise targets start on the main
    // camera (its brain's live Cinemachine camera), the activation on its own object
    [FeedbackTarget("Cinemachine/Lens FOV", PreviewGrade.Full, Aliases = "field of view zoom cinemachine", RequiresName = CinemachineNames.Camera, Implementation = CinemachineNames.Assembly)]
    [Serializable]
    public sealed class CinemachineLensFieldOfViewItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Spring,
            category: FeedbackCategory.Camera, flashSensitive: false, applyMode: ApplyMode.Offset);

        public CinemachineLensFieldOfViewItem()
        {
            m_Target.m_Mode = TargetRefMode.MainCamera;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Cinemachine/Lens Orthographic Size", PreviewGrade.Full, Aliases = "orthographic size zoom cinemachine", RequiresName = CinemachineNames.Camera, Implementation = CinemachineNames.Assembly)]
    [Serializable]
    public sealed class CinemachineLensOrthographicSizeItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Spring,
            category: FeedbackCategory.Camera, flashSensitive: false, applyMode: ApplyMode.Offset);

        public CinemachineLensOrthographicSizeItem()
        {
            m_Target.m_Mode = TargetRefMode.MainCamera;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Cinemachine/Lens Dutch", PreviewGrade.Full, Aliases = "dutch roll tilt cinemachine", RequiresName = CinemachineNames.Camera, Implementation = CinemachineNames.Assembly)]
    [Serializable]
    public sealed class CinemachineLensDutchItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Punch | MotionMask.Spring,
            category: FeedbackCategory.Camera, flashSensitive: false, applyMode: ApplyMode.Offset);

        public CinemachineLensDutchItem()
        {
            m_Target.m_Mode = TargetRefMode.MainCamera;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // a Cinemachine noise's gains — the noise runs in the preview on the Cinemachine time (forward only)
    [FeedbackTarget("Cinemachine/Noise Amplitude Gain", PreviewGrade.Simulated, Aliases = "noise shake cinemachine", RequiresName = CinemachineNames.Perlin, Implementation = CinemachineNames.Assembly)]
    [Serializable]
    public sealed class CinemachineNoiseAmplitudeGainItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Punch,
            category: FeedbackCategory.Camera, flashSensitive: false, applyMode: ApplyMode.Offset);

        public CinemachineNoiseAmplitudeGainItem()
        {
            m_Target.m_Mode = TargetRefMode.MainCamera;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    [FeedbackTarget("Cinemachine/Noise Frequency Gain", PreviewGrade.Simulated, Aliases = "noise frequency cinemachine", RequiresName = CinemachineNames.Perlin, Implementation = CinemachineNames.Assembly)]
    [Serializable]
    public sealed class CinemachineNoiseFrequencyGainItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Punch,
            category: FeedbackCategory.Camera, flashSensitive: false, applyMode: ApplyMode.Offset);

        public CinemachineNoiseFrequencyGainItem()
        {
            m_Target.m_Mode = TargetRefMode.MainCamera;
        }

        internal override TargetTraits Traits => s_Traits;
    }

    // a Cinemachine camera made the live one — its priority (the struct whole) and Prioritize, a blend of the item's over the
    // brain's. Instant, not undone, any category; the preview shows it only
    [FeedbackTarget("Cinemachine/Activate Camera", PreviewGrade.DisplayOnly, Aliases = "activate camera priority transition cinemachine", RequiresName = CinemachineNames.Camera, Implementation = CinemachineNames.Assembly)]
    [Serializable]
    public sealed class CinemachineActivateCameraItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal bool m_ToTop = true;
        [SerializeField] internal int m_Priority = 10;
        [SerializeField] internal CinemachineBlendSpec m_Blend = CinemachineBlendSpec.Default;

        public CinemachineActivateCameraItem()
        {
            m_Target.m_Mode = TargetRefMode.Self;
        }

        internal override TargetTraits Traits => s_Traits;
    }
}
