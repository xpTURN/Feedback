using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback
{
    // Control — Set (Set Layer's form): the play's start state, the first (Restore), the origin (a backward play). Clear
    // changes no state and is not undone
    internal sealed class ParticleControlBinding : ItemBinding
    {
        readonly ParticleControlItem m_Item;
        PropertyLayerRegistry m_Layers;
        ParticlePlayStateSlot m_Slot;
        int m_Reserved;
        long[] m_Serials;
        bool[] m_HasStart;
        ParticlePlayState[] m_Starts;
        ParticlePlayState m_Origin;
        ParticlePlayState m_First;
        bool m_HasOrigin;
        bool m_HasFirst;

        internal ParticleControlBinding(ParticleControlItem item)
        {
            m_Item = item;
        }

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            ParticleSystem system = TargetResolver.Find<ParticleSystem>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (system == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Layers = context.Context.Layers;
            m_Slot = m_Layers.GetOrCreate(system, 0, 0, () => new ParticlePlayStateSlot(system));
            int n = context.MaxOverlap;
            m_Slot.Reserve(n);
            m_Reserved = n;
            m_Serials = new long[n];
            m_HasStart = new bool[n];
            m_Starts = new ParticlePlayState[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        bool Live => m_Slot != null && m_Slot.IsAlive && !m_Slot.Detached;

        internal override bool IsAlive => Live;

        internal override void Execute(int record, in BeginContext context)
        {
            bool fresh = m_Serials[record] != context.Serial || !m_HasStart[record];
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_HasStart[record] = false;
            }
            if (!Live)
                return;
            if (!m_HasStart[record])
            {
                m_Slot.Touch();
                m_Starts[record] = m_Slot.Read();
                m_HasStart[record] = true;
            }
            if (fresh)
            {
                if (!m_HasFirst)
                {
                    m_First = m_Starts[record];
                    m_HasFirst = true;
                }
                if (!context.Reverse)
                {
                    m_Origin = m_Starts[record];
                    m_HasOrigin = true;
                }
            }
            bool children = m_Item.m_WithChildren;
            if (context.Reverse)
            {
                if (m_HasOrigin)
                    m_Slot.Write(m_Origin, children);
                return;
            }
            switch (m_Item.m_Action)
            {
                case ParticleAction.Play: m_Slot.Play(children); break;
                case ParticleAction.Stop: m_Slot.Stop(children, m_Item.m_StopMode == ParticleStopMode.StopEmittingAndClear); break;
                case ParticleAction.Pause: m_Slot.Pause(children); break;
                default: m_Slot.Clear(children); break;
            }
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
            if (reason == EndReason.Revert && m_HasStart[record] && Live && m_Item.m_Action != ParticleAction.Clear)
                m_Slot.Write(m_Starts[record], m_Item.m_WithChildren);
        }

        internal override void Restore()
        {
            if (m_HasFirst && Live && m_Item.m_Action != ParticleAction.Clear)
                m_Slot.Write(m_First, m_Item.m_WithChildren);
            m_HasOrigin = false;
            m_HasFirst = false;
        }

        internal override void Inherit(ItemBinding old)
        {
            if (!(old is ParticleControlBinding from) || m_Slot == null || from.m_Slot == null || !m_Slot.Key.Equals(from.m_Slot.Key))
                return;
            m_Origin = from.m_Origin;
            m_HasOrigin = from.m_HasOrigin;
            m_First = from.m_First;
            m_HasFirst = from.m_HasFirst;
        }

        internal override void Release()
        {
            if (m_Slot == null)
                return;
            m_Layers.Release(m_Slot, m_Reserved);
            m_Slot = null;
        }
    }

    // Emit — the count × the play's intensity (rounded — 0 or less emits nothing), the root only. Not undone. It
    // touches the play state slot so the preview simulates the system
    internal sealed class ParticleEmitBinding : ItemBinding
    {
        readonly ParticleEmitItem m_Item;
        PropertyLayerRegistry m_Layers;
        ParticlePlayStateSlot m_Slot;

        internal ParticleEmitBinding(ParticleEmitItem item)
        {
            m_Item = item;
        }

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            ParticleSystem system = TargetResolver.Find<ParticleSystem>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (system == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Layers = context.Context.Layers;
            m_Slot = m_Layers.GetOrCreate(system, 0, 0, () => new ParticlePlayStateSlot(system));
            return true;
        }

        internal override bool IsAlive => m_Slot != null && m_Slot.IsAlive && !m_Slot.Detached;

        internal override void Execute(int record, in BeginContext context)
        {
            if (context.Reverse || !IsAlive)
                return;
            int n = Mathf.RoundToInt(m_Item.m_Count * context.Intensity);
            if (n <= 0)
                return;
            m_Slot.Touch();
            m_Slot.Target.Emit(n);
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
        }

        internal override void Release()
        {
            if (m_Slot == null)
                return;
            m_Layers.Release(m_Slot, 0);
            m_Slot = null;
        }
    }

    // a particle prefab's instance — SpawnBinding's spawn (the pools keep who took what) played (a pooled one comes back
    // through the inactive container, which clears its particles), given back when its particles are gone. Stop After is
    // the pools' expiry — the emission stops, the end gives it back
    internal sealed class ParticleSpawnBinding : ItemBinding
    {
        static readonly Action<GameObject> s_Expire = Expire;
        static readonly Func<GameObject, bool> s_Done = Done;
        // the edit mode: the instances whose emission an expiry stopped (the flags after a simulate can't tell)
        static readonly HashSet<GameObject> s_Stopped = new HashSet<GameObject>();

        readonly ParticleSpawnItem m_Item;
        FeedbackContext m_Context;
        Transform m_Target;
        bool m_Unscaled;
        long[] m_Serials;
        long m_Origin = -1;

        internal ParticleSpawnBinding(ParticleSpawnItem item)
        {
            m_Item = item;
        }

        internal override bool Prepare(in PrepareContext context)
        {
            FeedbackMotion motion = m_Item.m_Motion;
            if (motion == null)
                return Refuse("no motion");
            if (!m_Item.Traits.Supports(motion.Kind))
            {
#if DEBUG
                return Refuse("the target does not take " + motion.Kind);
#else
                return Refuse(null);
#endif
            }
            m_Target = TargetResolver.Find<Transform>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (m_Target == null)
                return Refuse(TargetResolver.Describe(failure));
            if (m_Item.m_Prefab == null)
                return Refuse("no prefab to spawn");
            if (!m_Item.m_Prefab.TryGetComponent(out ParticleSystem _))
                return Refuse("the prefab's root has no particle system");
            m_Context = context.Context;
            if (m_Item.m_Pooled)
                m_Context.Pools.Reserve(m_Item.m_Prefab, m_Item.m_PoolSize);
            ItemClock clock = m_Item.EffectiveClock;
            m_Unscaled = clock == ItemClock.Unscaled || (clock == ItemClock.Player && context.Player != null && context.Player.m_TimeMode == PlayerTimeMode.Unscaled);
            int n = context.MaxOverlap;
            m_Serials = new long[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        internal override bool IsAlive => m_Target != null;

        internal override void Execute(int record, in BeginContext context)
        {
            PoolService pools = m_Context.Pools;
            if (context.Reverse)
            {
                if (m_Origin >= 0)
                    m_Context.PoolsIfMade?.ReturnOwned(this, -1, m_Origin);
                return;
            }
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_Origin = context.Serial;
            }
            if (m_Target == null)
                return;
            Vector3 position = m_Item.m_Place == SpawnPlace.Target ? m_Target.TransformPoint(m_Item.m_Offset) : m_Item.m_Offset;
            Quaternion rotation = m_Item.m_MatchRotation ? m_Target.rotation : m_Item.m_Prefab.transform.rotation;
            Transform parent = m_Item.m_Parent == SpawnParent.Target ? m_Target : null;
            GameObject instance = pools.Spawn(m_Item.m_Prefab, m_Item.m_Pooled, position, rotation, parent, m_Item.m_StopAfter, m_Unscaled, this, record, context.Serial,
                m_Item.m_StopAfter > 0f ? s_Expire : null, s_Done);
            if (instance == null)
                return;
            if (!instance.TryGetComponent(out ParticleSystem system))
                return;
            s_Stopped.Remove(instance);
            system.Play(true);
        }

        static void Expire(GameObject instance)
        {
            if (!instance.TryGetComponent(out ParticleSystem system))
                return;
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (!Application.isPlaying)
                s_Stopped.Add(instance);
        }

        // play: the particles gone, or the emission stopped where no camera sees it. The edit mode: a simulated system stays
        // paused and alive — its particles gone after its end (a one-shot's duration · an expiry)
        static bool Done(GameObject instance)
        {
            if (!instance.TryGetComponent(out ParticleSystem system))
                return true;
            if (Application.isPlaying)
            {
                if (!system.IsAlive(true))
                    return true;
                return !system.isEmitting && system.TryGetComponent(out ParticleSystemRenderer renderer) && !renderer.isVisible;
            }
            bool ended = s_Stopped.Contains(instance) || (!system.main.loop && system.time >= system.main.duration);
            if (!ended)
                return false;
            foreach (ParticleSystem s in instance.GetComponentsInChildren<ParticleSystem>())
            {
                if (s.particleCount > 0)
                    return false;
            }
            s_Stopped.Remove(instance);
            return true;
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
            if (reason == EndReason.Revert && m_Serials[record] >= 0)
                m_Context.PoolsIfMade?.ReturnOwned(this, record, m_Serials[record]);
        }

        internal override void Restore()
        {
            m_Context?.PoolsIfMade?.ReturnOwned(this, -1, -1);
            m_Origin = -1;
        }

        internal override void Inherit(ItemBinding old)
        {
            if (!(old is ParticleSpawnBinding from) || m_Context == null || from.m_Context != m_Context)
                return;
            m_Context.PoolsIfMade?.Reown(from, this);
            m_Origin = from.m_Origin;
        }

        internal override void Release()
        {
            m_Context?.PoolsIfMade?.Disown(this);
        }
    }

    // the two values — the Light Range form
    internal sealed class ParticleSimulationSpeedBinding : ValueTargetBinding<float, FloatOps>
    {
        internal ParticleSimulationSpeedBinding(ParticleSimulationSpeedItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            ParticleSystem system = TargetResolver.Find<ParticleSystem>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (system == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(system, 0, 0, () => new ParticleSimulationSpeedSlot(system));
            else
                slot = Layers.GetOrCreate(system, 0, 0, () => new ParticleSimulationSpeedSlot(system));
            return true;
        }
    }

    internal sealed class ParticleEmissionRateBinding : ValueTargetBinding<float, FloatOps>
    {
        internal ParticleEmissionRateBinding(ParticleEmissionRateItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<float, FloatOps> slot)
        {
            slot = null;
            ParticleSystem system = TargetResolver.Find<ParticleSystem>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (system == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(system, 0, 0, () => new ParticleEmissionRateSlot(system));
            else
                slot = Layers.GetOrCreate(system, 0, 0, () => new ParticleEmissionRateSlot(system));
            return true;
        }
    }
}
