using Unity.Cinemachine;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a Cinemachine camera's lens and a noise's gains — written at the Update stage, before the brain reads them in its
    // LateUpdate. Each keeps its first value for Save During Play's snapshot
    [RecordPaths(typeof(CinemachineCamera), "Lens.FieldOfView")]
    internal sealed class CinemachineLensFieldOfViewSlot : ValueSlot<float, FloatOps>, ICinemachineSaved
    {
        readonly CinemachineCamera m_Target;
        readonly float m_First;

        internal CinemachineLensFieldOfViewSlot(CinemachineCamera target)
            : base(ConsumptionStage.Update, Composition.Additive, false)
        {
            m_Target = target;
            m_First = target.Lens.FieldOfView;
        }

        internal override bool IsAlive => m_Target != null;

        internal override void Attached() => CinemachineSaved.Add(this);

        internal override void Dropped() => CinemachineSaved.Remove(this);

        protected override float Read() => m_Target.Lens.FieldOfView;

        protected override void Write(in float value) => m_Target.Lens.FieldOfView = Mathf.Clamp(value, 1f, 179f);

        void ICinemachineSaved.PutBackFirst()
        {
            if (m_Target != null)
                m_Target.Lens.FieldOfView = m_First;
        }
    }

    [RecordPaths(typeof(CinemachineCamera), "Lens.OrthographicSize")]
    internal sealed class CinemachineLensOrthographicSizeSlot : ValueSlot<float, FloatOps>, ICinemachineSaved
    {
        internal const float MinSize = 0.0001f;

        readonly CinemachineCamera m_Target;
        readonly float m_First;

        internal CinemachineLensOrthographicSizeSlot(CinemachineCamera target)
            : base(ConsumptionStage.Update, Composition.Additive, false)
        {
            m_Target = target;
            m_First = target.Lens.OrthographicSize;
        }

        internal override bool IsAlive => m_Target != null;

        internal override void Attached() => CinemachineSaved.Add(this);

        internal override void Dropped() => CinemachineSaved.Remove(this);

        protected override float Read() => m_Target.Lens.OrthographicSize;

        protected override void Write(in float value) => m_Target.Lens.OrthographicSize = Mathf.Max(value, MinSize);

        void ICinemachineSaved.PutBackFirst()
        {
            if (m_Target != null)
                m_Target.Lens.OrthographicSize = m_First;
        }
    }

    [RecordPaths(typeof(CinemachineCamera), "Lens.Dutch")]
    internal sealed class CinemachineLensDutchSlot : ValueSlot<float, FloatOps>, ICinemachineSaved
    {
        readonly CinemachineCamera m_Target;
        readonly float m_First;

        internal CinemachineLensDutchSlot(CinemachineCamera target)
            : base(ConsumptionStage.Update, Composition.Additive, false)
        {
            m_Target = target;
            m_First = target.Lens.Dutch;
        }

        internal override bool IsAlive => m_Target != null;

        internal override void Attached() => CinemachineSaved.Add(this);

        internal override void Dropped() => CinemachineSaved.Remove(this);

        protected override float Read() => m_Target.Lens.Dutch;

        protected override void Write(in float value) => m_Target.Lens.Dutch = Mathf.Clamp(value, -180f, 180f);

        void ICinemachineSaved.PutBackFirst()
        {
            if (m_Target != null)
                m_Target.Lens.Dutch = m_First;
        }
    }

    [RecordPaths(typeof(CinemachineBasicMultiChannelPerlin), "AmplitudeGain")]
    internal sealed class CinemachineNoiseAmplitudeGainSlot : ValueSlot<float, FloatOps>, ICinemachineSaved
    {
        readonly CinemachineBasicMultiChannelPerlin m_Target;
        readonly float m_First;

        internal CinemachineNoiseAmplitudeGainSlot(CinemachineBasicMultiChannelPerlin target)
            : base(ConsumptionStage.Update, Composition.Additive, false)
        {
            m_Target = target;
            m_First = target.AmplitudeGain;
        }

        internal override bool IsAlive => m_Target != null;

        internal override void Attached() => CinemachineSaved.Add(this);

        internal override void Dropped() => CinemachineSaved.Remove(this);

        protected override float Read() => m_Target.AmplitudeGain;

        protected override void Write(in float value) => m_Target.AmplitudeGain = value;

        void ICinemachineSaved.PutBackFirst()
        {
            if (m_Target != null)
                m_Target.AmplitudeGain = m_First;
        }
    }

    [RecordPaths(typeof(CinemachineBasicMultiChannelPerlin), "FrequencyGain")]
    internal sealed class CinemachineNoiseFrequencyGainSlot : ValueSlot<float, FloatOps>, ICinemachineSaved
    {
        readonly CinemachineBasicMultiChannelPerlin m_Target;
        readonly float m_First;

        internal CinemachineNoiseFrequencyGainSlot(CinemachineBasicMultiChannelPerlin target)
            : base(ConsumptionStage.Update, Composition.Additive, false)
        {
            m_Target = target;
            m_First = target.FrequencyGain;
        }

        internal override bool IsAlive => m_Target != null;

        internal override void Attached() => CinemachineSaved.Add(this);

        internal override void Dropped() => CinemachineSaved.Remove(this);

        protected override float Read() => m_Target.FrequencyGain;

        protected override void Write(in float value) => m_Target.FrequencyGain = value;

        void ICinemachineSaved.PutBackFirst()
        {
            if (m_Target != null)
                m_Target.FrequencyGain = m_First;
        }
    }

    // a camera shake on a camera Cinemachine drives — its brain's offsets, read by the shake extensions of its live cameras
    // inside the pipeline. The Update stage: the brain reads them in the same frame
    [NotRecorded("the shake extension's value — no field of the scene")]
    internal sealed class CinemachineShakePositionSlot : ValueSlot<Vector3, Vector3Ops>
    {
        readonly CinemachineBrain m_Brain;

        internal CinemachineShakePositionSlot(CinemachineBrain brain)
            : base(ConsumptionStage.Update, Composition.Additive, false)
        {
            m_Brain = brain;
        }

        internal override bool IsAlive => m_Brain != null;

        internal override void Dropped() => CinemachineShakeExtension.SetPosition(m_Brain, Vector3.zero);

        protected override Vector3 Read() => CinemachineShakeExtension.PositionOf(m_Brain);

        protected override void Write(in Vector3 value) => CinemachineShakeExtension.SetPosition(m_Brain, value);
    }

    [NotRecorded("the shake extension's value — no field of the scene")]
    internal sealed class CinemachineShakeRotationSlot : ValueSlot<Quaternion, QuaternionOps>
    {
        readonly CinemachineBrain m_Brain;

        internal CinemachineShakeRotationSlot(CinemachineBrain brain)
            : base(ConsumptionStage.Update, Composition.Rotation, true)
        {
            m_Brain = brain;
        }

        protected override Quaternion Offset(in Quaternion v, in Quaternion o, LayerSpace space) => v * o;

        internal override bool IsAlive => m_Brain != null;

        internal override void Dropped() => CinemachineShakeExtension.SetRotation(m_Brain, Quaternion.identity);

        protected override Quaternion Read() => CinemachineShakeExtension.RotationOf(m_Brain);

        protected override void Write(in Quaternion value) => CinemachineShakeExtension.SetRotation(m_Brain, value);
    }
}
