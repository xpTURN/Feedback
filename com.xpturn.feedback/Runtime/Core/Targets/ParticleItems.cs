using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a Control's action — saved (values only at the end)
    public enum ParticleAction
    {
        Play = 0,
        Stop = 1,
        Pause = 2,
        Clear = 3,
    }

    // a Control Stop's way — Core can't name the engine's stop behavior (the implementation maps it)
    public enum ParticleStopMode
    {
        StopEmitting = 0,
        StopEmittingAndClear = 1,
    }

    // the names Core can't type
    internal static class ParticleNames
    {
        internal const string System = "UnityEngine.ParticleSystem, UnityEngine.ParticleSystemModule";
        internal const string Assembly = "xpTURN.Feedback.Particles";
    }

    // a particle system's play state — Play · Stop · Pause · Clear. Set — a Revert puts its record's start state back (Clear
    // is not undone). The value is not read
    [FeedbackTarget("Particles/Control", PreviewGrade.Simulated, Aliases = "particle play stop pause clear",
        RequiresName = ParticleNames.System, Implementation = ParticleNames.Assembly)]
    [Serializable]
    public sealed class ParticleControlItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal ParticleAction m_Action;
        [SerializeField] internal bool m_WithChildren = true;
        [SerializeField] internal ParticleStopMode m_StopMode;

        internal override TargetTraits Traits => s_Traits;
    }

    // a burst — the count × the play's intensity (rounded), the root system only. Not undone
    [FeedbackTarget("Particles/Emit", PreviewGrade.Simulated, Aliases = "particle emit burst",
        RequiresName = ParticleNames.System, Implementation = ParticleNames.Assembly)]
    [Serializable]
    public sealed class ParticleEmitItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal int m_Count = 30;

        internal override TargetTraits Traits => s_Traits;
    }

    // an instance of a particle prefab at the target — the pools, played from its start, given back when its particles are
    // gone. Stop After stops the emission of a looping one
    [FeedbackTarget("Particles/Spawn", PreviewGrade.DisplayOnly, Aliases = "particle spawn instantiate pool prefab",
        Requires = typeof(Transform), Implementation = ParticleNames.Assembly)]
    [Serializable]
    public sealed class ParticleSpawnItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal GameObject m_Prefab;
        [SerializeField] internal bool m_Pooled = true;
        [SerializeField] internal int m_PoolSize = 5;
        [SerializeField] internal SpawnPlace m_Place;
        [SerializeField] internal Vector3 m_Offset;
        [SerializeField] internal bool m_MatchRotation;
        [SerializeField] internal SpawnParent m_Parent;
        [SerializeField] internal float m_StopAfter;

        internal override TargetTraits Traits => s_Traits;
    }

    // the main module's simulation speed — applied in the Update stage (before the particles' update)
    [FeedbackTarget("Particles/Simulation Speed", PreviewGrade.Simulated, Aliases = "particle simulation speed playback",
        RequiresName = ParticleNames.System, Implementation = ParticleNames.Assembly)]
    [Serializable]
    public sealed class ParticleSimulationSpeedItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Flash,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }

    // the emission's rate over time (its multiplier — two constants scale together) — the Update stage
    [FeedbackTarget("Particles/Emission Rate", PreviewGrade.Simulated, Aliases = "particle emission rate over time",
        RequiresName = ParticleNames.System, Implementation = ParticleNames.Assembly)]
    [Serializable]
    public sealed class ParticleEmissionRateItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Float, supportedMotions: MotionMask.Tween | MotionMask.Flash,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        internal override TargetTraits Traits => s_Traits;
    }
}
