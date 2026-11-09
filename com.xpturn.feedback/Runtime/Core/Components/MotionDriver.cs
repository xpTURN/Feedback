using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // saved — add members at the end only
    public enum MotionDriverMode
    {
        Loop = 0,
        Follow = 1,
    }

    // one target item driven without a player. Loop evaluates the item's endless time-based motion (T = 0, the seconds since
    // it was enabled); Follow springs the target's value toward a target (SetTarget, or another transform's same property)
    // and writes it through the slot's door. The binding is prepared at the first enable and kept while off; enabled = one
    // scheduler slot. A container of a saved item: the format version
    /// <summary>Drives one target item without a player: Loop plays its endless motion, Follow springs its value toward a target.</summary>
    [AddComponentMenu("xpTURN/Feedback/Motion Driver")]
    [Icon("Packages/com.xpturn.feedback/Editor/Icons/MotionDriver.png")]
    public sealed class MotionDriver : MonoBehaviour, IPlaybackRunner, ISerializationCallbackReceiver
    {
        internal const string NoItemWarning = "MotionDriver has no item.";
        internal const string LoopMotionWarning = "MotionDriver Loop takes an endless time-based motion (Oscillate · Wiggle · Shake · Blink): ";
        internal const string FollowMotionWarning = "MotionDriver Follow takes a value target with a Spring motion: ";
        internal const string SkippedWarning = "MotionDriver could not prepare its item";
        internal const string FollowTargetWarning = "MotionDriver Follow's target transform has no same property for this item and is ignored: ";
        internal const string OwnToggleWarning = "MotionDriver does not turn its own object or a parent of it off (the disable would end the blink and the next frame turn it back on): ";
        internal const string LoopTargetWarning = "MotionDriver.SetTarget is for Follow — a Loop driver keeps the value for a later Follow.";
        internal const string NewerFormatWarning = "A MotionDriver was saved by a newer version of the package — fields and item types this version does not know are lost: ";

        [SerializeReference] internal TargetItem m_Item;
        [SerializeField] internal MotionDriverMode m_Mode;
        // the item's Player clock follows this (the player's time mode)
        [SerializeField] internal PlayerTimeMode m_TimeMode;
        [SerializeField] internal float m_Intensity = 1f;
        // Follow: the other transform whose same property is the target — null: SetTarget's value
        [SerializeField] internal Transform m_FollowTarget;
        [SerializeField] internal int m_FormatVersion = FeedbackFormat.Current;

        [NonSerialized] FeedbackScheduler m_Bound;
        [NonSerialized] FeedbackScheduler m_Prepared;
        [NonSerialized] ItemBinding m_Binding;
        [NonSerialized] bool m_Dirty;
        [NonSerialized] bool m_Running;
        [NonSerialized] PlaybackHandle m_Handle;
        [NonSerialized] bool m_Begun;
        [NonSerialized] bool m_Unscaled;
        [NonSerialized] double m_Seconds;
        [NonSerialized] long m_Serial;
        [NonSerialized] uint m_Seed;
        // follow
        [NonSerialized] IFollowBinding m_Follow;
        [NonSerialized] SpringStepper m_Stepper;
        [NonSerialized] float m_Frequency;
        [NonSerialized] float m_Damping;
        [NonSerialized] int m_Dimension;
        [NonSerialized] Vector4 m_Value;
        [NonSerialized] Vector4 m_Velocity;
        [NonSerialized] Vector4 m_Target;
        [NonSerialized] bool m_HasTarget;
        [NonSerialized] bool m_Angles;
        [NonSerialized] bool m_ReadsOther;
#if DEBUG
        [NonSerialized] int m_WarnedCauses;
        [NonSerialized] bool m_WarnedFormat;
#endif

        // observation
        internal bool IsRunning => m_Running;
        internal bool IsPrepared => m_Binding != null;
        internal bool IsDirty => m_Dirty;
        internal ItemBinding Binding => m_Binding;
        internal PlaybackHandle Handle => m_Handle;
        internal double Seconds => m_Seconds;
        internal uint Seed => m_Seed;
        internal Vector4 FollowValue => m_Value;
        internal Vector4 FollowVelocity => m_Velocity;
        internal Vector4 FollowTarget => m_Target;

        UnityEngine.Object IPlaybackRunner.DebugOwner => this;

        /// <summary>Follow: sets the value the spring moves toward, in the motion's space (Euler angles for a rotation); it is kept across enables. While a follow transform is set, the target takes the followed value each frame.</summary>
        public void SetTarget(Vector4 target)
        {
            if (m_Mode == MotionDriverMode.Loop)
                Warn(LoopTargetWarning);
            m_Target = target;
            m_HasTarget = true;
        }

        /// <summary>Follow: follows the same property of <paramref name="target"/>; null stops following, and the spring keeps moving toward the last value it took.</summary>
        public void SetTarget(Transform target)
        {
            if (m_Mode == MotionDriverMode.Loop)
                Warn(LoopTargetWarning);
            m_FollowTarget = target;
            if (target != null && m_Binding != null && !m_ReadsOther)
                Warn(FollowTargetWarning + m_Item.GetType().Name);
        }

        void OnEnable() => Run();

        void OnDisable() => Halt(true);

        void OnDestroy() => ReleaseBinding();

#if UNITY_EDITOR
        // an inspector edit during play: the next frame prepares again (the binding copied the motion's parameters)
        void OnValidate()
        {
            if (m_Binding != null)
                m_Dirty = true;
        }
#endif

        // edit mode runs no OnEnable: the direct-tick tests and the preview session bind with it (null unbinds and releases)
        internal void Bind(FeedbackScheduler scheduler)
        {
            Halt(true);
            ReleaseBinding();
            m_Bound = scheduler;
            if (scheduler != null)
                Run();
        }

        internal void MarkDirty() => m_Dirty = true;

        FeedbackScheduler Scheduler()
        {
            FeedbackScheduler s = m_Bound;
            if (s != null && s.IsShutdown)
                m_Bound = s = null;
#if UNITY_EDITOR
            if (s == null && !Application.isPlaying)
                return null;
#endif
            return s ?? FeedbackScheduler.Default;
        }

        // the enable: prepares when there is no binding for this scheduler (or it died, or an edit came), begins, takes a slot
        internal void Run()
        {
            if (m_Running)
                return;
            FeedbackScheduler s = Scheduler();
            if (s == null || s.IsShutdown)
                return;
            if (m_Binding != null && (m_Prepared != s || m_Dirty || !m_Binding.IsAlive))
                ReleaseBinding();
            if (m_Binding == null && !Prepare(s))
                return;
            if (!Begin(s))
                return;
            m_Running = true;
            m_Handle = s.Add(this, 0);
        }

        bool Prepare(FeedbackScheduler s)
        {
            TargetItem item = m_Item;
            if (item == null)
            {
                Warn(NoItemWarning);
                return false;
            }
            // an unchecked item does nothing — the user's choice, no warning (the player's rule)
            if (!item.m_Active)
                return false;
#if DEBUG
            if (m_FormatVersion > FeedbackFormat.Current && !m_WarnedFormat)
            {
                m_WarnedFormat = true;
                Debug.LogWarning(NewerFormatWarning + m_FormatVersion + " > " + FeedbackFormat.Current, this);
            }
#endif
            MotionKind kind = item.m_Motion != null ? item.m_Motion.Kind : MotionKind.Set;
            bool loopKind = kind == MotionKind.Oscillate || kind == MotionKind.Wiggle || kind == MotionKind.Shake || kind == MotionKind.Blink;
            if (m_Mode == MotionDriverMode.Loop && (item.m_Motion == null || !loopKind))
            {
                Warn(LoopMotionWarning + (item.m_Motion != null ? kind.ToString() : "no motion"));
                return false;
            }
            if (m_Mode != MotionDriverMode.Loop && kind != MotionKind.Spring)
            {
                Warn(FollowMotionWarning + (item.m_Motion != null ? kind.ToString() : "no motion"));
                return false;
            }
            ItemBinding binding = null;
            try
            {
                binding = FeedbackImplementations.Create(item);
                if (binding == null)
                {
                    PlayerRuntime.WarnUnregistered(item.GetType(), this);
                    return false;
                }
                var context = new PrepareContext(gameObject, null, s.Context, 1);
                if (!binding.Prepare(in context))
                {
                    Warn(SkippedWarning + (binding.SkipReason != null ? ": " + binding.SkipReason : ""));
                    binding.Release();
                    return false;
                }
                // a SetActive item on the driver's own object or an ancestor would switch the driver off — the next
                // frame's revert turns it back on and its enable evaluates 0 s again: a loop that cannot end
                if (binding is SetActiveBinding toggle && toggle.Target != null && transform.IsChildOf(toggle.Target.transform))
                {
                    Warn(OwnToggleWarning + toggle.Target.name);
                    binding.Release();
                    return false;
                }
                if (m_Mode != MotionDriverMode.Loop && !(binding is IFollowBinding))
                {
                    Warn(FollowMotionWarning + item.GetType().Name);
                    binding.Release();
                    return false;
                }
            }
            catch (Exception e)
            {
                s.Context.LogException(e);
                try
                {
                    binding?.Release();
                }
                catch (Exception e2)
                {
                    s.Context.LogException(e2);
                }
                return false;
            }
            s.Reserve(1);
            m_Prepared = s;
            m_Binding = binding;
            m_Dirty = false;
            ItemClock clock = item.EffectiveClock;
            m_Unscaled = clock == ItemClock.Unscaled || (clock == ItemClock.Player && m_TimeMode == PlayerTimeMode.Unscaled);
            if (m_Mode != MotionDriverMode.Loop)
            {
                m_Follow = (IFollowBinding)binding;
                var spring = (SpringMotion)item.m_Motion;
                m_Frequency = spring.m_Params.m_Frequency;
                m_Damping = spring.m_Params.m_Damping;
                m_Dimension = m_Follow.MotionDimension;
                m_Angles = item is RotationItem;
                m_ReadsOther = item is PositionItem || item is RotationItem || item is ScaleItem;
                if (m_FollowTarget != null && !m_ReadsOther)
                    Warn(FollowTargetWarning + item.GetType().Name);
            }
            return true;
        }

        // Loop: a new seed and the seconds from 0 (t = 0 at once). Follow: from where the value is, at rest
        bool Begin(FeedbackScheduler s)
        {
            m_Seconds = 0.0;
            m_Serial++;
            if (m_Mode == MotionDriverMode.Loop)
            {
                m_Seed = PlayerRuntime.NextSessionSeed();
                m_Begun = false;
                return BeginLoop(s);
            }
            if (!m_Follow.TryReadMotion(out m_Value))
            {
                Warn(SkippedWarning + ": the target is not live");
                ReleaseBinding();
                return false;
            }
            m_Velocity = default;
            if (!m_HasTarget)
                m_Target = m_Value;
            return true;
        }

        bool BeginLoop(FeedbackScheduler s)
        {
            TargetItem item = m_Item;
            float intensity = item.m_Intensity == IntensityResponse.Ignore ? 1f : PlayerRuntime.Factor(m_Intensity);
            if (item.m_Random.m_RandomizeOutput)
            {
                Vector2 r = item.m_Random.m_OutputRange;
                intensity *= PlayerRuntime.Factor(Mathf.Lerp(r.x, r.y, Hash32.ToUnit(Hash32.Tagged(m_Seed, Hash32.Output, 0u))));
            }
            try
            {
                // the place: the driver's at the start — a binding without a player has no transform of its own to read
                m_Binding.Begin(0, new BeginContext(intensity, m_Seed, 0, m_Serial, false, double.PositiveInfinity, true, transform.position));
            }
            catch (Exception e)
            {
                s.Context.LogException(e);
                return false;
            }
            m_Begun = true;
            // the player's t = 0 at once (a slot added after the frame's revert advances from the next frame)
            m_Binding.Evaluate(0, new MotionTime(0f, 0.0));
            if (!m_Binding.IsAlive)
            {
                Warn(SkippedWarning + ": the target is not live");
                m_Begun = false;
                m_Binding.End(0, EndReason.Hold);
                ReleaseBinding();
                return false;
            }
            return true;
        }

        // the disable: the contribution goes (Hold — a Persist value stays), the binding stays. From an abort the slot is dead
        internal void Halt(bool remove)
        {
            if (!m_Running)
                return;
            m_Running = false;
            FeedbackScheduler s = m_Prepared;
            if (remove)
                s.Remove(m_Handle);
            m_Handle = default;
            if (m_Begun)
            {
                m_Begun = false;
                try
                {
                    m_Binding.End(0, EndReason.Hold);
                    m_Binding.Settle(0);
                }
                catch (Exception e)
                {
                    s.Context.LogException(e);
                }
            }
        }

        // the destroy · a scheduler that is gone · a dead target · an edit
        internal void ReleaseBinding()
        {
            Halt(true);
            ItemBinding binding = m_Binding;
            if (binding == null)
                return;
            FeedbackScheduler s = m_Prepared;
            m_Binding = null;
            m_Follow = null;
            m_Prepared = null;
            try
            {
                binding.Release();
            }
            catch (Exception e)
            {
                s.Context.LogException(e);
            }
            s.Unreserve(1);
        }

        bool IPlaybackRunner.Advance(int state, in FrameTime time)
        {
            ItemBinding binding = m_Binding;
            if (binding == null)
                return false;
            if (!binding.IsAlive)
            {
                // the target is gone: the driver stops quietly — the slot ends with this false
                m_Running = false;
                m_Handle = default;
                m_Begun = false;
                ReleaseBinding();
                return false;
            }
            if (m_Dirty)
            {
                // an inspector edit: the same slot, a new binding
                FeedbackScheduler s = m_Prepared;
                m_Running = false;
                PlaybackHandle handle = m_Handle;
                m_Handle = default;
                if (m_Begun)
                {
                    m_Begun = false;
                    binding.End(0, EndReason.Hold);
                    binding.Settle(0);
                }
                m_Binding = null;
                m_Follow = null;
                m_Prepared = null;
                binding.Release();
                s.Unreserve(1);
                if (!Prepare(s) || !Begin(s))
                    return false;
                m_Running = true;
                m_Handle = handle;
                binding = m_Binding;
            }
            float dt = m_Unscaled ? time.UnscaledDelta : time.ScaledDelta;
            if (m_Mode == MotionDriverMode.Loop)
            {
                m_Seconds += dt;
                binding.Evaluate(0, new MotionTime(0f, m_Seconds));
                return true;
            }
            Vector4 target = m_Target;
            if (m_ReadsOther && m_FollowTarget != null && ReadOther(out Vector4 other))
                m_Target = target = other;
            if (m_Angles)
                target = NearestAngles(target, m_Value);
            // a zero step is skipped — the kernel's identity still rounds the value onto the grid around the target
            // (timeScale 0 would move it by an ulp of the target)
            if (dt > 0f)
            {
                m_Stepper.Prepare(dt, m_Frequency, m_Damping);
                m_Stepper.Advance(ref m_Value.x, ref m_Velocity.x, target.x);
                if (m_Dimension > 1)
                    m_Stepper.Advance(ref m_Value.y, ref m_Velocity.y, target.y);
                if (m_Dimension > 2)
                    m_Stepper.Advance(ref m_Value.z, ref m_Velocity.z, target.z);
                if (m_Dimension > 3)
                    m_Stepper.Advance(ref m_Value.w, ref m_Velocity.w, target.w);
            }
            m_Seconds += dt;
            m_Follow.WriteMotion(in m_Value);
            return true;
        }

        void IPlaybackRunner.Abort(int state, AbortReason reason) => Halt(false);

        // another transform's same property in the item's space
        bool ReadOther(out Vector4 value)
        {
            Transform t = m_FollowTarget;
            switch (m_Item)
            {
                case PositionItem p:
                    value = p.m_Space == PositionSpace.World ? (Vector4)t.position
                        : p.m_Space == PositionSpace.Anchored && t is RectTransform rect ? (Vector4)(t.localPosition - PositionBinding.AnchorReference(rect)) : (Vector4)t.localPosition;
                    return true;
                case RotationItem r:
                    value = r.m_Space == TransformSpace.World ? (Vector4)t.eulerAngles : (Vector4)t.localEulerAngles;
                    return true;
                case ScaleItem _:
                    value = t.localScale;
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        // the angles nearest the value: each within ±180°, and of the two Euler triples of one rotation — (x, y, z) and
        // (180 − x, y + 180, z + 180) — the nearer (a pitch past ±90° flips y and z)
        static Vector4 NearestAngles(Vector4 target, Vector4 value)
        {
            Vector4 a = Unwrap(target, value);
            // y and z near: the other triple is no nearer (its y · z are 180 − these away) — the common case stops here
            if (Mathf.Abs(a.y - value.y) + Mathf.Abs(a.z - value.z) <= 90f)
                return a;
            Vector4 b = Unwrap(new Vector4(180f - target.x, target.y + 180f, target.z + 180f, target.w), value);
            float da = Mathf.Abs(a.x - value.x) + Mathf.Abs(a.y - value.y) + Mathf.Abs(a.z - value.z);
            float db = Mathf.Abs(b.x - value.x) + Mathf.Abs(b.y - value.y) + Mathf.Abs(b.z - value.z);
            return db < da ? b : a;
        }

        static Vector4 Unwrap(Vector4 target, Vector4 value)
        {
            target.x = value.x + Mathf.DeltaAngle(value.x, target.x);
            target.y = value.y + Mathf.DeltaAngle(value.y, target.y);
            target.z = value.z + Mathf.DeltaAngle(value.z, target.z);
            return target;
        }

        // once per cause (the message's constant head)
        void Warn(string message)
        {
#if DEBUG
            int bit = Cause(message);
            if ((m_WarnedCauses & bit) != 0)
                return;
            m_WarnedCauses |= bit;
            Debug.LogWarning(message, this);
#endif
        }

#if DEBUG
        static int Cause(string message)
        {
            if (message.StartsWith(NoItemWarning, StringComparison.Ordinal)) return 1;
            if (message.StartsWith(LoopMotionWarning, StringComparison.Ordinal)) return 2;
            if (message.StartsWith(FollowMotionWarning, StringComparison.Ordinal)) return 4;
            if (message.StartsWith(SkippedWarning, StringComparison.Ordinal)) return 8;
            if (message.StartsWith(FollowTargetWarning, StringComparison.Ordinal)) return 16;
            if (message.StartsWith(OwnToggleWarning, StringComparison.Ordinal)) return 32;
            if (message.StartsWith(LoopTargetWarning, StringComparison.Ordinal)) return 64;
            return 128;
        }
#endif

        // ---- the format version: data only — any thread

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize() => FeedbackFormat.Upgrade(ref m_FormatVersion, m_Item);
    }

    // a value target MotionDriver's Follow writes through
    internal interface IFollowBinding
    {
        int MotionDimension { get; }
        bool TryReadMotion(out Vector4 value);
        bool WriteMotion(in Vector4 value);
    }
}
