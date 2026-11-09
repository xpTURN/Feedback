using UnityEngine;

namespace xpTURN.Feedback
{
    // a GameObject's own active state — a toggle
    [RecordPaths(typeof(GameObject), "m_IsActive")]
    internal sealed class GameObjectActiveSlot : ToggleSlot
    {
        readonly GameObject m_Target;

        internal GameObjectActiveSlot(GameObject target)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override bool Read() => m_Target.activeSelf;

        protected override void Write(bool value) => m_Target.SetActive(value);
    }

    // a Behaviour's enabled — a toggle (a Collider2D is a Behaviour)
    [RecordPaths(typeof(Behaviour), "m_Enabled")]
    internal sealed class BehaviourEnabledSlot : ToggleSlot
    {
        readonly Behaviour m_Target;

        internal BehaviourEnabledSlot(Behaviour target)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override bool Read() => m_Target.enabled;

        protected override void Write(bool value) => m_Target.enabled = value;
    }

    // a Collider's enabled — Core does not know Collider: the Physics assembly's hooks read and write it. The row's owner is
    // Component (the record's rows go on the collider itself)
    [RecordPaths(typeof(Component), "m_Enabled")]
    internal sealed class ColliderEnabledSlot : ToggleSlot
    {
        readonly Component m_Target;

        internal ColliderEnabledSlot(Component target)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override bool Read() => LayerHooks.ColliderEnabledRead(m_Target);

        protected override void Write(bool value) => LayerHooks.ColliderEnabledWrite(m_Target, value);
    }

    // a GameObject's layer — a state, Persist only (MaterialSwapSlot's form)
    [RecordPaths(typeof(GameObject), "m_Layer")]
    internal sealed class GameObjectLayerSlot : LayerSlot
    {
        readonly GameObject m_Target;
        int m_Reserved;

        internal GameObjectLayerSlot(GameObject target)
            : base(ConsumptionStage.Update)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        internal override bool Pending => false;

        internal override int LiveContributions => 0;

        internal override int Reserved => m_Reserved;

        internal override Vector4 BaseVector => Vector4.zero;

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

        internal int Read() => m_Target.layer;

        // Persist — the door
        internal void WriteLayer(int layer)
        {
            RecordOnce();
            m_Target.layer = layer;
        }
    }
}
