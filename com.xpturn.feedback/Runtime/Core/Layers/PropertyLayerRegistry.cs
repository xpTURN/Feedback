using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace xpTURN.Feedback
{
    // (target, slot type, two aux keys — a shader property ID and a material index). Reference identity, hashed without
    // Unity's Equals. The slot type is the kind: no number table, so an optional assembly adds a kind without Core, and a type
    // and a number cannot disagree
    internal readonly struct LayerKey : IEquatable<LayerKey>
    {
        internal readonly UnityEngine.Object Target;
        internal readonly Type SlotType;
        internal readonly int Aux;
        internal readonly int Aux2;

        internal LayerKey(UnityEngine.Object target, Type slotType, int aux, int aux2)
        {
            Target = target;
            SlotType = slotType;
            Aux = aux;
            Aux2 = aux2;
        }

        public bool Equals(LayerKey other)
            => ReferenceEquals(Target, other.Target) && ReferenceEquals(SlotType, other.SlotType) && Aux == other.Aux && Aux2 == other.Aux2;

        public override bool Equals(object obj) => obj is LayerKey key && Equals(key);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = RuntimeHelpers.GetHashCode(Target);
                h = h * 397 ^ RuntimeHelpers.GetHashCode(SlotType);
                h = h * 397 ^ Aux;
                return h * 397 ^ Aux2;
            }
        }
    }

    // one slot per (target, property), shared, owned by the context. Revert walks the active lists (a slot with anything to
    // put back is active) and is the only pass that changes them — a first Begin appends past a running stage's count. Layer
    // stages never throw (the scheduler does not catch them)
    internal sealed class PropertyLayerRegistry : ILayerStages
    {
        internal const string ShortageWarning = "Feedback layer contributions outgrew the reservation and a slot's ledger grew. Reserve more (the bindings' maximum overlap).";
        internal const string ChurnWarning = "A Feedback layer slot's ledger grew because contributions were replaced several times in one frame.";
        internal const string PrivateToggleError = "A Feedback direct write takes value slots only: a toggle is shared (GetOrCreate).";
        // a preview copy's name — the editor's recovery after a missed reload finds it
        internal const string PreviewPrefix = "xpTURN.Feedback.Preview.";
        // a runtime · test context's own objects — the preview recovery leaves them
        internal const string RuntimePrefix = "xpTURN.Feedback.Runtime.";

        readonly FeedbackContext m_Context;
        readonly Dictionary<LayerKey, LayerSlot> m_Map = new Dictionary<LayerKey, LayerSlot>();
        // the query's list — every prepared slot, in creation order. No stage walks it
        readonly List<LayerSlot> m_All = new List<LayerSlot>();
        readonly List<LayerSlot> m_Update = new List<LayerSlot>();
        readonly List<LayerSlot> m_LateApply = new List<LayerSlot>();
        // a stage applies, reverts and closes its direct slots first — a direct snapshot never takes a shared slot's write
        readonly List<LayerSlot> m_UpdateDirect = new List<LayerSlot>();
        readonly List<LayerSlot> m_LateApplyDirect = new List<LayerSlot>();
        int m_UpdateSlots;
        int m_LateApplySlots;
        int m_UpdateDirectSlots;
        int m_LateApplyDirectSlots;
        // the cursors of the running pass — fields, so the isolation's restart goes on from them
        int m_Read;
        int m_Write;
        bool m_Closed;
        ILayerRecorder m_Recorder;
        // what a released slot hands to the close — run after the slots, the last handed first (the earliest value is written
        // last)
        readonly List<Action> m_OnClose = new List<Action>();
#if DEBUG
        bool m_WarnedShortage;
        bool m_WarnedChurn;
#endif

        internal PropertyLayerRegistry(FeedbackContext context)
        {
            m_Context = context;
        }

        internal bool IsClosed => m_Closed;

        // what a slot does at a preview's end and with its copies
        internal bool IsPreview => m_Context.Kind == ContextKind.Preview;

        internal int SlotCount => m_All.Count;

        List<LayerSlot> ListOf(LayerSlot slot)
        {
            if (slot.Stage == ConsumptionStage.Update)
                return slot.Direct ? m_UpdateDirect : m_Update;
            return slot.Direct ? m_LateApplyDirect : m_LateApply;
        }

        ref int CountOf(LayerSlot slot)
        {
            if (slot.Stage == ConsumptionStage.Update)
                return ref slot.Direct ? ref m_UpdateDirectSlots : ref m_UpdateSlots;
            return ref slot.Direct ? ref m_LateApplyDirectSlots : ref m_LateApplySlots;
        }

        // the preparation path (allocation allowed). A slot waiting to be dropped comes back; a detached one is not found
        internal TSlot GetOrCreate<TSlot>(UnityEngine.Object target, int aux, int aux2, Func<TSlot> make) where TSlot : LayerSlot
        {
            var key = new LayerKey(target, typeof(TSlot), aux, aux2);
            if (m_Map.TryGetValue(key, out LayerSlot found))
            {
                found.ReleasePending = false;
                found.RefCount++;
                return (TSlot)found;
            }
            TSlot slot = make();
            slot.Registry = this;
            slot.Key = key;
            slot.RefCount = 1;
            m_Map[key] = slot;
            File(slot);
            return slot;
        }

        // GetOrCreate without the dictionary — the release, the close, the liveness check and the drop go the shared slots'
        // way. GetOrCreate · TryFind never find it. A toggle has no direct mode
        internal TSlot CreatePrivate<TSlot>(UnityEngine.Object target, int aux, int aux2, Func<TSlot> make) where TSlot : LayerSlot
        {
            TSlot slot = make();
#if DEBUG
            if (slot is ToggleSlot)
                Debug.LogError(PrivateToggleError);
#endif
            slot.Registry = this;
            slot.Key = new LayerKey(target, typeof(TSlot), aux, aux2);
            slot.RefCount = 1;
            slot.Direct = true;
            File(slot);
            return slot;
        }

        void File(LayerSlot slot)
        {
            m_All.Add(slot);
            // the active list's capacity for every slot of its stage — a first Begin appends without growing
            Grow(ListOf(slot), ++CountOf(slot));
            slot.Attached();
            HookTransform(slot);
        }

        // taken in a preview context only
        internal ILayerRecorder Recorder
        {
            set
            {
                if (m_Context.Kind == ContextKind.Preview)
                    m_Recorder = value;
            }
        }

        // once per slot before its first write — isolated, the write goes on
        internal void Record(LayerSlot slot)
        {
            if (m_Recorder == null)
                return;
            try
            {
                m_Recorder.Record(slot.Key);
            }
            catch (Exception e)
            {
                m_Context.LogException(e);
            }
        }

        // development builds — once per transform and mode: not when another slot of the same target and mode is prepared
        // (the query's list, no set). Previews stay quiet
        void HookTransform(LayerSlot slot)
        {
#if DEBUG
            Action<Transform, bool> hook = LayerHooks.TransformSlotCreated;
            if (hook == null || m_Context.Kind == ContextKind.Preview || !(slot.Key.Target is Transform target))
                return;
            for (int i = 0; i < m_All.Count; i++)
            {
                LayerSlot other = m_All[i];
                if (!ReferenceEquals(other, slot) && other.Direct == slot.Direct && ReferenceEquals(other.Key.Target, target))
                    return;
            }
            try
            {
                hook(target, slot.Direct);
            }
            catch (Exception e)
            {
                m_Context.LogException(e);
            }
#endif
        }

        // another slot of the same target (the Self space reads a rotation slot's Base)
        // a slot by a recorded key — the editor can't name an optional assembly's slot type
        internal bool TryFind(in LayerKey key, out LayerSlot slot) => m_Map.TryGetValue(key, out slot);

        internal bool TryFind<TSlot>(UnityEngine.Object target, int aux, int aux2, out TSlot slot) where TSlot : LayerSlot
        {
            if (m_Map.TryGetValue(new LayerKey(target, typeof(TSlot), aux, aux2), out LayerSlot found))
            {
                slot = (TSlot)found;
                return true;
            }
            slot = null;
            return false;
        }

        static void Grow(List<LayerSlot> list, int slots)
        {
            if (list.Capacity < slots)
                list.Capacity = Math.Max(slots, list.Capacity * 2);
        }

        // a slot's first Begin
        internal void Activate(LayerSlot slot)
        {
            slot.Active = true;
            ListOf(slot).Add(slot);
        }

        // once per cause per registry. More live contributions than reserved = a shortage; otherwise ended contributions
        // filled the doubled ledger before compaction. Previews stay quiet
        internal void LedgerGrew(LayerSlot slot)
        {
#if DEBUG
            if (m_Context.Kind == ContextKind.Preview)
                return;
            if (slot.LiveContributions > slot.Reserved)
            {
                if (m_WarnedShortage)
                    return;
                m_WarnedShortage = true;
                Debug.LogWarning(ShortageWarning);
            }
            else
            {
                if (m_WarnedChurn)
                    return;
                m_WarnedChurn = true;
                Debug.LogWarning(ChurnWarning);
            }
#endif
        }

        // the binding gives back what it reserved, then its reference. At 0 no binding owns the contributions left — they end.
        // An active slot waits for the next Revert to put its value back; any other goes now (no stage walks it). A detached
        // slot's release touches nothing
        internal void Release(LayerSlot slot, int reserved)
        {
            if (slot.Detached)
                return;
            slot.Unreserve(reserved);
            if (--slot.RefCount > 0)
                return;
            slot.EndAll();
            if (slot.Active)
                slot.ReleasePending = true;
            else
                Forget(slot);
        }

        // out of the dictionary — only while it holds this slot — and the query's list. The slot hears it
        void Forget(LayerSlot slot)
        {
            if (!m_All.Remove(slot))
                return;
            if (m_Map.TryGetValue(slot.Key, out LayerSlot held) && ReferenceEquals(held, slot))
                m_Map.Remove(slot.Key);
            CountOf(slot)--;
            DropQuietly(slot);
        }

        void DropQuietly(LayerSlot slot)
        {
            try
            {
                slot.Dropped();
            }
            catch (Exception e)
            {
                m_Context.LogException(e);
            }
        }

        // failed or its target destroyed — the next Revert pass takes it out of its active list
        void Detach(LayerSlot slot)
        {
            slot.Detached = true;
            Forget(slot);
        }

        // logged once — a detached slot is never called again
        void Fail(LayerSlot slot, Exception e)
        {
            m_Context.LogException(e);
            Detach(slot);
        }

        public void Revert()
        {
            if (m_Closed)
                return;
            RevertPass(m_UpdateDirect);
            RevertPass(m_Update);
            RevertPass(m_LateApplyDirect);
            RevertPass(m_LateApply);
        }

        // the loop is a method without try, the pass wraps it and goes on after a failed slot
        void RevertPass(List<LayerSlot> list)
        {
            int limit = list.Count;
            m_Read = 0;
            m_Write = 0;
            while (m_Read < limit)
            {
                try
                {
                    RevertFrom(list, limit);
                    break;
                }
                catch (Exception e)
                {
                    LayerSlot failed = list[m_Read];
                    Fail(failed, e);
                    failed.Active = false;
                    m_Read++;
                }
            }
            // activations during the pass went to the end — they move down behind the kept ones
            int count = list.Count;
            for (int i = limit; i < count; i++)
                list[m_Write++] = list[i];
            list.RemoveRange(m_Write, count - m_Write);
        }

        // no try here — Mono pays per slot for a loop inside a try region
        void RevertFrom(List<LayerSlot> list, int limit)
        {
            for (; m_Read < limit; m_Read++)
            {
                LayerSlot slot = list[m_Read];
                if (Keep(slot))
                    list[m_Write++] = slot;
                else
                    slot.Active = false;
            }
        }

        // one slot of the pass: compaction · judgment · leaving the list, a released slot put back and dropped — false leaves
        bool Keep(LayerSlot slot)
        {
            if (slot.Detached)
                return false;
            if (!slot.IsAlive)
            {
                Detach(slot);
                return false;
            }
            slot.Revert();
            if (slot.ReleasePending)
            {
                Forget(slot);
                return false;
            }
            return slot.LiveContributions > 0 || slot.Pending;
        }

        public void Apply(ConsumptionStage stage)
        {
            ApplyList(stage == ConsumptionStage.Update ? m_UpdateDirect : m_LateApplyDirect);
            ApplyList(stage == ConsumptionStage.Update ? m_Update : m_LateApply);
        }

        void ApplyList(List<LayerSlot> list)
        {
            if (m_Closed)
                return;
            int limit = list.Count;
            m_Read = 0;
            while (m_Read < limit)
            {
                try
                {
                    ApplyFrom(list, limit);
                    break;
                }
                catch (Exception e)
                {
                    Fail(list[m_Read], e);
                    m_Read++;
                }
            }
        }

        void ApplyFrom(List<LayerSlot> list, int limit)
        {
            for (; m_Read < limit; m_Read++)
            {
                LayerSlot slot = list[m_Read];
                if (slot.Detached || slot.ReleasePending)
                    continue;
                if (!slot.IsAlive)
                {
                    Detach(slot);
                    continue;
                }
                slot.Apply();
                if (m_Closed)
                {
                    // closed inside this slot's write — it puts its own value back, and the pass stops
                    slot.Close();
                    m_Read = limit;
                    return;
                }
            }
        }

        // after the playbacks were aborted, before the services close — each written slot once more, one at a time. Then
        // every prepared slot hears it (a resting instance slot too)
        internal void Shutdown()
        {
            if (m_Closed)
                return;
            m_Closed = true;
            CloseList(m_UpdateDirect);
            CloseList(m_Update);
            CloseList(m_LateApplyDirect);
            CloseList(m_LateApply);
            for (int i = 0; i < m_All.Count; i++)
                DropQuietly(m_All[i]);
            for (int i = m_OnClose.Count - 1; i >= 0; i--)
            {
                try
                {
                    m_OnClose[i]();
                }
                catch (Exception e)
                {
                    m_Context.LogException(e);
                }
            }
            m_OnClose.Clear();
        }

        // a slot released before the close leaves what the close puts back
        internal void OnClose(Action restore)
        {
            if (!m_Closed)
                m_OnClose.Add(restore);
        }

        void CloseList(List<LayerSlot> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                LayerSlot slot = list[i];
                if (slot.Detached)
                    continue;
                try
                {
                    if (slot.IsAlive)
                        slot.Close();
                }
                catch (Exception e)
                {
                    Fail(slot, e);
                }
            }
        }

        // every prepared slot in creation order — liveness first, the base only of a live target
        internal bool TryGetSlot(int index, out LayerSlotInfo info)
        {
            if ((uint)index >= (uint)m_All.Count)
            {
                info = default;
                return false;
            }
            LayerSlot slot = m_All[index];
            bool alive = slot.IsAlive;
            info = new LayerSlotInfo(slot.Key.Target, slot.Key.SlotType, slot.Key.Aux, slot.Key.Aux2, slot.Stage, slot.LiveContributions, slot.Pending,
                alive, alive ? slot.BaseVector : default, slot.Direct);
            return true;
        }
    }
}
