using UnityEngine;

namespace xpTURN.Feedback
{
    // what a Control reads and puts back
    internal enum ParticlePlayState
    {
        Stopped = 0,
        Playing = 1,
        Stopping = 2,
        Paused = 3,
    }

    // a system's play state — Persist in the Update stage. Nothing serialized (the preview's simulation puts
    // the system back at its end). It keeps its last action for the preview (the engine's simulate ignores a pause)
    [NotRecorded("a play state — the preview's simulation puts the system back at its end")]
    internal sealed class ParticlePlayStateSlot : LayerSlot, ISimulationState
    {
        readonly ParticleSystem m_Target;
        int m_Reserved;
        bool m_Held;
        bool m_Emits;

        internal ParticlePlayStateSlot(ParticleSystem target)
            : base(ConsumptionStage.Update)
        {
            m_Target = target;
            m_Emits = target.isPlaying && target.isEmitting;
        }

        internal override bool IsAlive => m_Target != null;

        internal override bool Pending => false;

        internal override int LiveContributions => 0;

        internal override int Reserved => m_Reserved;

        internal override Vector4 BaseVector => Vector4.zero;

        public bool Held => m_Held;

        public bool Emits => m_Emits;

        internal ParticleSystem Target => m_Target;

        internal void Reserve(int count) => m_Reserved += count;

        internal override void Unreserve(int count) => m_Reserved -= System.Math.Min(count, m_Reserved);

        internal override void Revert()
        {
        }

        internal override void Apply()
        {
        }

        internal override void Close()
        {
        }

        internal override void EndAll()
        {
        }

        internal ParticlePlayState Read()
        {
            if (m_Held)
                return ParticlePlayState.Paused;
            if (m_Target.isStopped)
                return ParticlePlayState.Stopped;
            // the engine's flags hold unless the preview's simulate left the system paused
            if (!m_Target.isPaused)
                return m_Target.isEmitting ? ParticlePlayState.Playing : ParticlePlayState.Stopping;
            if (Application.isPlaying)
                return ParticlePlayState.Paused;
            return m_Emits ? ParticlePlayState.Playing : ParticlePlayState.Stopping;
        }

        // the door — the record before the first write (the preview's simulation learns the system)
        internal void Touch() => RecordOnce();

        internal void Play(bool children)
        {
            RecordOnce();
            m_Held = false;
            m_Emits = true;
            m_Target.Play(children);
        }

        internal void Pause(bool children)
        {
            RecordOnce();
            m_Held = true;
            m_Target.Pause(children);
        }

        internal void Stop(bool children, bool clear)
        {
            RecordOnce();
            m_Held = false;
            m_Emits = false;
            m_Target.Stop(children, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
        }

        internal void Clear(bool children)
        {
            RecordOnce();
            m_Target.Clear(children);
        }

        internal void Write(ParticlePlayState state, bool children)
        {
            switch (state)
            {
                case ParticlePlayState.Playing: Play(children); break;
                case ParticlePlayState.Paused: Pause(children); break;
                case ParticlePlayState.Stopping: Stop(children, false); break;
                default: Stop(children, true); break;
            }
        }
    }

    // the main module's simulation speed — the engine keeps it in 0 ~ 100 (the judgment on the value read back takes
    // that in)
    [RecordPaths(typeof(ParticleSystem), "simulationSpeed")]
    internal sealed class ParticleSimulationSpeedSlot : ValueSlot<float, FloatOps>
    {
        readonly ParticleSystem m_Target;

        internal ParticleSimulationSpeedSlot(ParticleSystem target)
            : base(ConsumptionStage.Update, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.main.simulationSpeed;

        protected override void Write(in float value)
        {
            ParticleSystem.MainModule main = m_Target.main;
            main.simulationSpeed = value;
        }
    }

    // the emission's rate over time — its multiplier (a constant's value, a curve's multiplier). Two constants
    // scale together: every write (the layer's · the Persist door's) keeps the minimum's ratio to the source pair — the pair it
    // finds that is not its own write — and the source's maximum puts the source pair back exactly
    [RecordPaths(typeof(ParticleSystem), "EmissionModule.rateOverTime.scalar", "EmissionModule.rateOverTime.minScalar")]
    internal sealed class ParticleEmissionRateSlot : ValueSlot<float, FloatOps>
    {
        readonly ParticleSystem m_Target;
        readonly bool m_Two;
        float m_SourceMin;
        float m_SourceMax;
        float m_WrittenMin = float.NaN;
        float m_WrittenMax = float.NaN;

        internal ParticleEmissionRateSlot(ParticleSystem target)
            : base(ConsumptionStage.Update, Composition.Additive, false)
        {
            m_Target = target;
            m_Two = target.emission.rateOverTime.mode == ParticleSystemCurveMode.TwoConstants;
        }

        internal override bool IsAlive => m_Target != null;

        protected override float Read() => m_Target.emission.rateOverTimeMultiplier;

        protected override void Write(in float value)
        {
            ParticleSystem.EmissionModule e = m_Target.emission;
            if (!m_Two)
            {
                e.rateOverTimeMultiplier = value;
                return;
            }
            ParticleSystem.MinMaxCurve c = e.rateOverTime;
            if (c.constantMin != m_WrittenMin || c.constantMax != m_WrittenMax)
            {
                m_SourceMin = c.constantMin;
                m_SourceMax = c.constantMax;
            }
            c.constantMin = value == m_SourceMax ? m_SourceMin : m_SourceMax != 0f ? m_SourceMin * (value / m_SourceMax) : m_SourceMin;
            c.constantMax = value;
            e.rateOverTime = c;
            m_WrittenMin = c.constantMin;
            m_WrittenMax = value;
        }
    }
}
