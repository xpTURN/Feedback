using UnityEngine;
using UnityEngine.SceneManagement;

namespace xpTURN.Feedback
{
    // the three transform slots. Each gives its property's read and write and its composition; a space turns an offset where
    // it is applied, so it sees this frame's parent. Only offsets take a space — overrides, Persist and Base are the slot
    // value's own space
    // a RectTransform's anchored position too: a write through the Transform leaves its serialized value behind and a
    // hierarchy change brings it up to date — a save during a preview would take the moved one
    [RecordPaths(typeof(Transform), "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_AnchoredPosition.x", "m_AnchoredPosition.y")]
    internal sealed class LocalPositionSlot : ValueSlot<Vector3, Vector3Ops>
    {
        readonly Transform m_Target;
        // the rotation slot of the same binding (a direct pair) — Self turns by its Base
        internal LocalRotationSlot SelfPair;

        internal LocalPositionSlot(Transform target)
            : base(ConsumptionStage.LateApply, Composition.Additive, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override Vector3 Read() => m_Target.localPosition;

        protected override void Write(in Vector3 value) => m_Target.localPosition = value;

        // Local: the parent's axes (localPosition's space). World: the parent's inverse — exact under a skewed parent. Self:
        // the target's own axes in world units — a World offset turned by the target's base world rotation
        protected override Vector3 Offset(in Vector3 v, in Vector3 o, LayerSpace space)
        {
            Transform parent;
            switch (space)
            {
                case LayerSpace.World:
                    parent = m_Target.parent;
                    return parent == null ? v + o : v + parent.InverseTransformVector(o);
                case LayerSpace.Self:
                    Vector3 turned = SelfRotation() * o;
                    parent = m_Target.parent;
                    return parent == null ? v + turned : v + parent.InverseTransformVector(parent.rotation * turned);
                default:
                    return v + o;
            }
        }

        // the rotation slot's Base — the value before composition, whatever the order in the stage. Found where it is
        // applied: the one lookup on the playback path (0 B, always the registry's current state)
        Quaternion SelfRotation()
        {
            if (SelfPair != null)
                return SelfPair.Base;
            return Registry.TryFind(m_Target, 0, 0, out LocalRotationSlot rotation) ? rotation.Base : m_Target.localRotation;
        }
    }

    // a rotation written back is not always the value read — the base goes on
    [RecordPaths(typeof(Transform), "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w")]
    internal sealed class LocalRotationSlot : ValueSlot<Quaternion, QuaternionOps>
    {
        readonly Transform m_Target;

        internal LocalRotationSlot(Transform target)
            : base(ConsumptionStage.LateApply, Composition.Rotation, true)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override Quaternion Read() => m_Target.localRotation;

        protected override void Write(in Quaternion value) => m_Target.localRotation = value;

        // Local and Self: the target's own axes, v · o. World: the offset conjugated by the parent's world rotation — a
        // world-axis turn in Transform.rotation's sense, the drawn shape's only under a uniformly scaled parent
        protected override Quaternion Offset(in Quaternion v, in Quaternion o, LayerSpace space)
        {
            if (space != LayerSpace.World)
                return v * o;
            Transform parent = m_Target.parent;
            if (parent == null)
                return o * v;
            Quaternion p = parent.rotation;
            return Quaternion.Inverse(p) * o * p * v;
        }
    }

    // the frame's MulOffset, v ⊙ (1 + o) — local only, whatever the space
    [RecordPaths(typeof(Transform), "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z")]
    internal sealed class LocalScaleSlot : ValueSlot<Vector3, Vector3Ops>
    {
        readonly Transform m_Target;

        internal LocalScaleSlot(Transform target)
            : base(ConsumptionStage.LateApply, Composition.Multiplicative, false)
        {
            m_Target = target;
        }

        internal override bool IsAlive => m_Target != null;

        protected override Vector3 Read() => m_Target.localScale;

        protected override void Write(in Vector3 value) => m_Target.localScale = value;
    }

    // a transform's place — its parent, sibling index and local TRS, a root's scene, a RectTransform's anchors, pivot,
    // anchored position and size (worldPositionStays moves the last two)
    internal struct ParentState
    {
        internal Transform Parent;
        internal int Sibling;
        internal Vector3 LocalPosition;
        internal Quaternion LocalRotation;
        internal Vector3 LocalScale;
        internal Scene Scene;
        internal bool Rect;
        internal Vector2 AnchorMin;
        internal Vector2 AnchorMax;
        internal Vector2 Pivot;
        internal Vector2 AnchoredPosition;
        internal Vector2 SizeDelta;

        internal static ParentState Of(Transform t)
        {
            var s = new ParentState
            {
                Parent = t.parent,
                Sibling = t.GetSiblingIndex(),
                LocalPosition = t.localPosition,
                LocalRotation = t.localRotation,
                LocalScale = t.localScale,
                Scene = t.gameObject.scene,
            };
            if (t is RectTransform r)
            {
                s.Rect = true;
                s.AnchorMin = r.anchorMin;
                s.AnchorMax = r.anchorMax;
                s.Pivot = r.pivot;
                s.AnchoredPosition = r.anchoredPosition;
                s.SizeDelta = r.sizeDelta;
            }
            return s;
        }

        // the parent without moving the local values, then the root's scene (SetParent took it to the parent's — null leaves
        // it there), the sibling index and the local values: the same bits whatever AnimationMode's stop does before or after
        internal void Put(Transform t)
        {
            t.SetParent(Parent, false);
            if (Parent == null && Scene.IsValid() && Scene.isLoaded && t.gameObject.scene != Scene)
                SceneManager.MoveGameObjectToScene(t.gameObject, Scene);
            t.SetSiblingIndex(Sibling);
            t.localPosition = LocalPosition;
            t.localRotation = LocalRotation;
            t.localScale = LocalScale;
            if (Rect && t is RectTransform r)
            {
                r.anchorMin = AnchorMin;
                r.anchorMax = AnchorMax;
                r.pivot = Pivot;
                r.anchoredPosition = AnchoredPosition;
                r.sizeDelta = SizeDelta;
            }
        }
    }

    // a state, not a value — no composition, Persist only (Set Parent). The parent is not recorded (m_Father): the editor's
    // slot-state table keeps it for a preview. The local TRS and a RectTransform's fields are its rows — recorded before its
    // first write, so another slot of the object recorded after the reparent does not take the moved values for the start
    [RecordPaths(typeof(Transform), "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
        "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
        "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z",
        "m_AnchorMin.x", "m_AnchorMin.y", "m_AnchorMax.x", "m_AnchorMax.y", "m_Pivot.x", "m_Pivot.y",
        "m_AnchoredPosition.x", "m_AnchoredPosition.y", "m_SizeDelta.x", "m_SizeDelta.y")]
    internal sealed class ParentSlot : LayerSlot
    {
        readonly Transform m_Target;
        int m_Reserved;

        internal ParentSlot(Transform target)
            : base(ConsumptionStage.Update)
        {
            m_Target = target;
        }

        internal Transform Target => m_Target;

        internal override bool IsAlive => m_Target != null;

        internal override bool Pending => false;

        internal override int LiveContributions => 0;

        internal override int Reserved => m_Reserved;

        internal override Vector4 BaseVector => Vector4.zero;

        internal ParentState Base => ParentState.Of(m_Target);

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

        // Persist — the door
        internal void WriteParent(Transform parent, bool worldPositionStays)
        {
            RecordOnce();
            m_Target.SetParent(parent, worldPositionStays);
        }

        internal void WriteState(in ParentState state)
        {
            RecordOnce();
            state.Put(m_Target);
        }
    }
}
