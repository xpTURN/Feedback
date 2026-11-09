using UnityEngine;

namespace xpTURN.Feedback
{
    // the transform targets. The target and its parent are read at the prepare (the engine makes an object's C# Transform at
    // the first read and holds it after: read and dropped)
    internal sealed class PositionBinding : ValueTargetBinding<Vector3, Vector3Ops>
    {
        readonly PositionItem m_Position;
        Transform m_Target;
        RectTransform m_Anchored;
        LayerSpace m_Space;

        internal PositionBinding(PositionItem item)
            : base(item)
        {
            m_Position = item;
        }

        protected override LayerSpace Space => m_Space;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector3, Vector3Ops> slot)
        {
            slot = null;
            Transform target = TargetResolver.Find<Transform>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (target == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Target = target;
            m_Space = m_Position.m_Space == PositionSpace.World ? LayerSpace.World : LayerSpace.Local;
            // an Anchored item on a plain Transform moves as Local (FB-G14)
            m_Anchored = m_Position.m_Space == PositionSpace.Anchored ? target as RectTransform : null;
            _ = target.parent;
            if (direct)
                slot = Layers.CreatePrivate(target, 0, 0, () => new LocalPositionSlot(target));
            else
                slot = Layers.GetOrCreate(target, 0, 0, () => new LocalPositionSlot(target));
            return true;
        }

        // the anchor reference point in the parent's space — local = anchored + this. From the geometry: the anchored
        // position reads stale while the slot writes the local position
        internal static Vector3 AnchorReference(RectTransform rect)
        {
            if (!(rect.parent is RectTransform parent))
                return Vector3.zero;
            Rect r = parent.rect;
            Vector2 min = rect.anchorMin, max = rect.anchorMax, pivot = rect.pivot;
            return new Vector3(r.xMin + r.width * (min.x + (max.x - min.x) * pivot.x), r.yMin + r.height * (min.y + (max.y - min.y) * pivot.y), 0f);
        }

        // World Persist: the motion runs in world space, the slot holds the local value. Anchored: the motion runs in the
        // anchored position (an offset is the local one's)
        protected override Vector4 ToMotion(in Vector3 local)
        {
            if (m_Anchored != null)
                return local - AnchorReference(m_Anchored);
            Transform parent = m_Space == LayerSpace.World ? m_Target.parent : null;
            return parent == null ? (Vector4)local : (Vector4)parent.TransformPoint(local);
        }

        protected override Vector3 FromMotion(in Vector4 value)
        {
            var v = (Vector3)value;
            if (m_Anchored != null)
                return v + AnchorReference(m_Anchored);
            Transform parent = m_Space == LayerSpace.World ? m_Target.parent : null;
            return parent == null ? v : parent.InverseTransformPoint(v);
        }
    }

    internal sealed class RotationBinding : ValueTargetBinding<Quaternion, QuaternionOps>
    {
        readonly RotationItem m_Rotation;
        Transform m_Target;
        LayerSpace m_Space;
        TweenEvaluator m_Shortest;

        internal RotationBinding(RotationItem item)
            : base(item)
        {
            m_Rotation = item;
        }

        protected override LayerSpace Space => m_Space;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Quaternion, QuaternionOps> slot)
        {
            slot = null;
            Transform target = TargetResolver.Find<Transform>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (target == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Target = target;
            m_Space = m_Rotation.m_Space == TransformSpace.World ? LayerSpace.World : LayerSpace.Local;
            m_Shortest = m_Rotation.m_Interpolation == RotationInterpolation.Shortest ? Evaluator as TweenEvaluator : null;
            _ = target.parent;
            if (direct)
                slot = Layers.CreatePrivate(target, 0, 0, () => new LocalRotationSlot(target));
            else
                slot = Layers.GetOrCreate(target, 0, 0, () => new LocalRotationSlot(target));
            return true;
        }

        Quaternion ToWorld(in Quaternion local)
        {
            Transform parent = m_Space == LayerSpace.World ? m_Target.parent : null;
            return parent == null ? local : parent.rotation * local;
        }

        Quaternion ToLocal(in Quaternion world)
        {
            Transform parent = m_Space == LayerSpace.World ? m_Target.parent : null;
            return parent == null ? world : Quaternion.Inverse(parent.rotation) * world;
        }

        // Euler angles of the start (0 ~ 360°) — an Euler tween's start is unwrapped toward the motion's own value, ±180° per
        // axis, so a negative target does not spin the axis and 350° → 10° passes 0°
        protected override Vector4 ToMotion(in Quaternion local)
        {
            Vector3 e = ToWorld(local).eulerAngles;
            if (m_Shortest == null && Toward(out Vector4 toward))
            {
                e.x += 360f * Mathf.Round((toward.x - e.x) / 360f);
                e.y += 360f * Mathf.Round((toward.y - e.y) / 360f);
                e.z += 360f * Mathf.Round((toward.z - e.z) / 360f);
            }
            return new Vector4(e.x, e.y, e.z, 0f);
        }

        // the value a Persist goes to or comes from: Tween To · From, an absolute Spring MoveTo
        bool Toward(out Vector4 toward)
        {
            switch (m_Item.m_Motion)
            {
                case TweenMotion tween when tween.m_Params.m_Mode == TweenMode.To:
                    toward = tween.m_Params.m_To;
                    return true;
                case TweenMotion tween when tween.m_Params.m_Mode == TweenMode.From:
                    toward = tween.m_Params.m_From;
                    return true;
                case SpringMotion spring when spring.m_Params.m_Mode == SpringMode.MoveTo && !spring.m_Params.m_Relative:
                    toward = spring.m_Params.m_Value;
                    return true;
                default:
                    toward = default;
                    return false;
            }
        }

        protected override Quaternion FromMotion(in Vector4 value) => ToLocal(Quaternion.Euler(value.x, value.y, value.z));

        // Shortest: SlerpUnclamped(q(a), q(b), Progress) — an overshooting ease keeps its overshoot
        protected override bool TryPersist(in MotionState s, in MotionTime time, out Quaternion value)
        {
            if (m_Shortest == null)
            {
                value = default;
                return false;
            }
            m_Shortest.Endpoints(in s, out Vector4 a, out Vector4 b);
            value = ToLocal(Quaternion.SlerpUnclamped(Quaternion.Euler(a.x, a.y, a.z), Quaternion.Euler(b.x, b.y, b.z), m_Shortest.Progress(in time)));
            return true;
        }
    }

    internal sealed class ScaleBinding : ValueTargetBinding<Vector3, Vector3Ops>
    {
        internal ScaleBinding(ScaleItem item)
            : base(item)
        {
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector3, Vector3Ops> slot)
        {
            slot = null;
            Transform target = TargetResolver.Find<Transform>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (target == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(target, 0, 0, () => new LocalScaleSlot(target));
            else
                slot = Layers.GetOrCreate(target, 0, 0, () => new LocalScaleSlot(target));
            return true;
        }
    }

    // a transform item's second reference — found at the prepare like the target. The main camera with none there waits: a
    // use finds it (Camera.main), and finds it again when it is gone
    internal struct TransformReference
    {
        Transform m_Found;
        bool m_Waits;

        // null, or why the prepare refuses
        internal string Find(in TargetRef reference, in PrepareContext context)
        {
            m_Found = TargetResolver.Find<Transform>(in reference, in context, out TargetFailure failure);
            m_Waits = reference.m_Mode == TargetRefMode.MainCamera;
            return m_Found != null || failure == TargetFailure.NoMainCamera ? null : TargetResolver.Describe(failure);
        }

        internal Transform Current
        {
            get
            {
                if (m_Waits && m_Found == null)
                {
                    Camera main = Camera.main;
                    if (main != null)
                        m_Found = main.transform;
                }
                return m_Found;
            }
        }
    }

    // the scale slot's product — the value is stops: the main axis takes s = 2^v, the others keep the volume (two axes 1/√s
    // each · one axis 1/s, the third untouched). Persist goes from the play's start (Origin) at 0 stops, an offset becomes a
    // factor − 1 (MapOffset)
    internal sealed class SquashAndStretchBinding : ValueTargetBinding<Vector3, Vector3Ops>
    {
        readonly SquashAndStretchItem m_Squash;

        internal SquashAndStretchBinding(SquashAndStretchItem item)
            : base(item)
        {
            m_Squash = item;
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector3, Vector3Ops> slot)
        {
            slot = null;
            Transform target = TargetResolver.Find<Transform>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (target == null)
                return Refuse(TargetResolver.Describe(failure));
            if (direct)
                slot = Layers.CreatePrivate(target, 0, 0, () => new LocalScaleSlot(target));
            else
                slot = Layers.GetOrCreate(target, 0, 0, () => new LocalScaleSlot(target));
            return true;
        }

        internal static Vector3 Factors(SquashAxis axis, float stops)
        {
            float s = Mathf.Pow(2f, stops);
            float two = 1f / Mathf.Sqrt(s);
            float one = 1f / s;
            switch (axis)
            {
                case SquashAxis.XtoYZ: return new Vector3(s, two, two);
                case SquashAxis.XtoY: return new Vector3(s, one, 1f);
                case SquashAxis.XtoZ: return new Vector3(s, 1f, one);
                case SquashAxis.YtoXZ: return new Vector3(two, s, two);
                case SquashAxis.YtoX: return new Vector3(one, s, 1f);
                case SquashAxis.YtoZ: return new Vector3(1f, s, one);
                case SquashAxis.ZtoXY: return new Vector3(two, two, s);
                case SquashAxis.ZtoX: return new Vector3(one, 1f, s);
                default: return new Vector3(1f, one, s);
            }
        }

        static int Main(SquashAxis axis) => axis <= SquashAxis.XtoZ ? 0 : axis <= SquashAxis.YtoZ ? 1 : 2;

        Vector3 Reference => HasOrigin ? Origin : Slot != null ? Slot.Base : Vector3.one;

        protected override Vector4 ToMotion(in Vector3 value)
        {
            int a = Main(m_Squash.m_Axis);
            float r = Reference[a];
            float ratio = r != 0f ? value[a] / r : 1f;
            return new Vector4(ratio > 0f ? Mathf.Log(ratio, 2f) : 0f, 0f, 0f, 0f);
        }

        protected override Vector3 FromMotion(in Vector4 value) => Vector3.Scale(Reference, Factors(m_Squash.m_Axis, value.x));

        protected override Vector3 MapOffset(in Vector3 offset) => Factors(m_Squash.m_Axis, offset.x) - Vector3.one;
    }

    // the position slot's orbit — the value is degrees about the world axis through the pivot, read each frame. The target's
    // rotation stays. Persist goes from the play's start at 0°; an oscillation's degrees become the world offset from the
    // slot's base (MapOffset — offsets in World)
    internal sealed class RotateAroundBinding : ValueTargetBinding<Vector3, Vector3Ops>
    {
        readonly RotateAroundItem m_Around;
        Transform m_Target;
        TransformReference m_Pivot;

        internal RotateAroundBinding(RotateAroundItem item)
            : base(item)
        {
            m_Around = item;
        }

        protected override LayerSpace Space => LayerSpace.World;

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Vector3, Vector3Ops> slot)
        {
            slot = null;
            Transform target = TargetResolver.Find<Transform>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (target == null)
                return Refuse(TargetResolver.Describe(failure));
            string pivot = m_Pivot.Find(in m_Around.m_Pivot, in context);
            if (pivot != null)
                return Refuse("pivot: " + pivot);
            m_Target = target;
            _ = target.parent;
            if (direct)
                slot = Layers.CreatePrivate(target, 0, 0, () => new LocalPositionSlot(target));
            else
                slot = Layers.GetOrCreate(target, 0, 0, () => new LocalPositionSlot(target));
            return true;
        }

        Vector3 ToWorld(in Vector3 local)
        {
            Transform parent = m_Target.parent;
            return parent == null ? local : parent.TransformPoint(local);
        }

        Vector3 ToLocal(in Vector3 world)
        {
            Transform parent = m_Target.parent;
            return parent == null ? world : parent.InverseTransformPoint(world);
        }

        // no pivot yet (a main camera to come) — no orbit
        Vector3 Orbit(in Vector3 world, float degrees)
        {
            Transform pivot = m_Pivot.Current;
            if (pivot == null)
                return world;
            Vector3 center = pivot.position;
            return center + Quaternion.AngleAxis(degrees, m_Around.m_Axis) * (world - center);
        }

        Vector3 Reference => HasOrigin ? Origin : Slot != null ? Slot.Base : Vector3.zero;

        protected override Vector4 ToMotion(in Vector3 local)
        {
            Transform pivot = m_Pivot.Current;
            if (pivot == null)
                return Vector4.zero;
            Vector3 center = pivot.position;
            Vector3 from = Vector3.ProjectOnPlane(ToWorld(Reference) - center, m_Around.m_Axis);
            Vector3 to = Vector3.ProjectOnPlane(ToWorld(local) - center, m_Around.m_Axis);
            return new Vector4(Vector3.SignedAngle(from, to, m_Around.m_Axis), 0f, 0f, 0f);
        }

        protected override Vector3 FromMotion(in Vector4 value) => ToLocal(Orbit(ToWorld(Reference), value.x));

        protected override Vector3 MapOffset(in Vector3 offset)
        {
            Vector3 world = ToWorld(Slot.Base);
            return Orbit(world, offset.x) - world;
        }
    }

    // the rotation slot's Persist — between the play's start rotation and this frame's look rotation by the progress (Set:
    // at once — ValueTargetBinding.Execute). The look rotation follows its reference while the tween runs, not after. A
    // direction near 0 or along the up keeps the rotation (LookRotation snaps without a log)
    internal sealed class LookAtBinding : ValueTargetBinding<Quaternion, QuaternionOps>
    {
        readonly LookAtItem m_Look;
        Transform m_Target;
        TransformReference m_LookTarget;
        TweenEvaluator m_Tween;
        bool m_Arrival;

        internal LookAtBinding(LookAtItem item)
            : base(item)
        {
            m_Look = item;
        }

        protected override bool Acquire(in PrepareContext context, bool direct, out ValueSlot<Quaternion, QuaternionOps> slot)
        {
            slot = null;
            Transform target = TargetResolver.Find<Transform>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (target == null)
                return Refuse(TargetResolver.Describe(failure));
            if (m_Look.m_Mode == LookAtMode.Transform)
            {
                string look = m_LookTarget.Find(in m_Look.m_LookTarget, in context);
                if (look != null)
                    return Refuse("look target: " + look);
            }
            m_Target = target;
            m_Tween = Evaluator as TweenEvaluator;
            m_Arrival = m_Item.m_Motion is TweenMotion tween && tween.m_Params.m_Mode == TweenMode.From;
            _ = target.parent;
            if (direct)
                slot = Layers.CreatePrivate(target, 0, 0, () => new LocalRotationSlot(target));
            else
                slot = Layers.GetOrCreate(target, 0, 0, () => new LocalRotationSlot(target));
            return true;
        }

        Vector3 Up()
        {
            switch (m_Look.m_Up)
            {
                case LookAtUp.Forward: return Vector3.forward;
                case LookAtUp.Right: return Vector3.right;
                default: return Vector3.up;
            }
        }

        bool LookRotation(out Quaternion world)
        {
            Vector3 direction;
            switch (m_Look.m_Mode)
            {
                case LookAtMode.Transform:
                {
                    Transform look = m_LookTarget.Current;
                    if (look == null)
                    {
                        world = default;
                        return false;
                    }
                    direction = look.position - m_Target.position;
                    break;
                }
                case LookAtMode.Position:
                    direction = m_Look.m_Position - m_Target.position;
                    break;
                default:
                    direction = m_Look.m_Direction;
                    break;
            }
            Vector3 up = Up();
            if (direction.sqrMagnitude < 1e-10f || Vector3.Cross(direction.normalized, up).sqrMagnitude < 1e-10f)
            {
                world = default;
                return false;
            }
            world = Quaternion.LookRotation(direction, up);
            return true;
        }

        Quaternion ParentRotation
        {
            get
            {
                Transform parent = m_Target.parent;
                return parent == null ? Quaternion.identity : parent.rotation;
            }
        }

        protected override bool TryPersist(in MotionState s, in MotionTime time, out Quaternion value)
        {
            if (!LookRotation(out Quaternion look))
            {
                value = Slot.Base;
                return true;
            }
            float p = m_Tween != null ? m_Tween.Progress(in time) : 1f;
            Quaternion parent = ParentRotation;
            Quaternion start = parent * (HasOrigin ? Origin : Slot.Base);
            Quaternion world = m_Arrival ? Quaternion.SlerpUnclamped(look, start, p) : Quaternion.SlerpUnclamped(start, look, p);
            value = Quaternion.Inverse(parent) * world;
            return true;
        }
    }

    // one play over three slots — position (World) · rotation (World) · scale (local), the channels on. Persist only: the
    // Tween's progress from where the play starts to the destination (taken at each Begin, each frame with Track).
    // ValueTargetBinding's shapes: the play's start for Revert, the origin a backward play goes to, the first value for
    // Restore, the first frame, the handed-over snapshots, a direct write's private slots, a main camera that comes later
    internal sealed class MatchTransformBinding : ItemBinding
    {
        struct Pose
        {
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal Vector3 Scale;
        }

        readonly MatchTransformItem m_Item;
        PropertyLayerRegistry m_Layers;
        MotionEvaluator m_Evaluator;
        TweenEvaluator m_Tween;
        TransformReference m_TargetRef;
        TransformReference m_Destination;
        Transform m_Target;
        LocalPositionSlot m_Position;
        LocalRotationSlot m_Rotation;
        LocalScaleSlot m_Scale;
        int m_Reserved;
        bool m_Arrival;
        MotionState[] m_States;
        long[] m_Serials;
        bool[] m_HasStart;
        bool[] m_Reverse;
        Pose[] m_Starts;
        Pose[] m_From;
        Pose[] m_Destinations;
        Pose m_Origin;
        Pose m_First;
        bool m_HasOrigin;
        bool m_HasFirst;
        bool m_Applied;
        bool m_AfterReverse;
        bool m_ReverseCut;
        long m_LastSerial = -1;
        MotionState m_Initial;

        internal MatchTransformBinding(MatchTransformItem item)
        {
            m_Item = item;
        }

        // observation
        internal Transform Target => m_Target;

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
            m_Evaluator = MotionEvaluators.Create(motion, 1);
            if (m_Evaluator == null)
                return Refuse("an unknown motion");
            m_Tween = m_Evaluator as TweenEvaluator;
            m_Arrival = motion is TweenMotion tween && tween.m_Params.m_Mode == TweenMode.From;
            string target = m_TargetRef.Find(in m_Item.m_Target, in context);
            if (target != null)
                return Refuse(target);
            string destination = m_Destination.Find(in m_Item.m_Destination, in context);
            if (destination != null)
                return Refuse("destination: " + destination);
            m_Layers = context.Context.Layers;
            int n = context.MaxOverlap;
            m_Reserved = n;
            m_States = new MotionState[n];
            m_Serials = new long[n];
            m_HasStart = new bool[n];
            m_Reverse = new bool[n];
            m_Starts = new Pose[n];
            m_From = new Pose[n];
            m_Destinations = new Pose[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            // no main camera yet — the first Begin takes the slots (allocation once)
            Take();
            return true;
        }

        void Take()
        {
            Transform target = m_TargetRef.Current;
            if (target == null || target == m_Target)
                return;
            m_Target = target;
            _ = target.parent;
            bool direct = m_Item.m_DirectWrite;
            if (m_Item.m_Position)
                m_Position = direct ? m_Layers.CreatePrivate(target, 0, 0, () => new LocalPositionSlot(target)) : m_Layers.GetOrCreate(target, 0, 0, () => new LocalPositionSlot(target));
            if (m_Item.m_Rotation)
                m_Rotation = direct ? m_Layers.CreatePrivate(target, 0, 0, () => new LocalRotationSlot(target)) : m_Layers.GetOrCreate(target, 0, 0, () => new LocalRotationSlot(target));
            if (m_Item.m_Scale)
                m_Scale = direct ? m_Layers.CreatePrivate(target, 0, 0, () => new LocalScaleSlot(target)) : m_Layers.GetOrCreate(target, 0, 0, () => new LocalScaleSlot(target));
            m_Position?.Reserve(m_Reserved);
            m_Rotation?.Reserve(m_Reserved);
            m_Scale?.Reserve(m_Reserved);
        }

        static bool Alive(LayerSlot slot) => slot != null && slot.IsAlive && !slot.Detached;

        bool Live => m_Target != null && (Alive(m_Position) || Alive(m_Rotation) || Alive(m_Scale));

        // a main camera to come with nothing taken yet
        internal override bool IsAlive => Live || ReferenceEquals(m_Target, null);

        Pose Base => new Pose
        {
            Position = m_Position != null ? m_Position.Base : m_Target.localPosition,
            Rotation = m_Rotation != null ? m_Rotation.Base : m_Target.localRotation,
            Scale = m_Scale != null ? m_Scale.Base : m_Target.localScale,
        };

        // a destination gone keeps the last one taken
        void TakeDestination(ref Pose into)
        {
            Transform destination = m_Destination.Current;
            if (destination == null)
                return;
            into.Position = destination.position;
            into.Rotation = destination.rotation;
            into.Scale = destination.localScale;
        }

        internal override void Begin(int record, in BeginContext context)
        {
            if (ReferenceEquals(m_Target, null))
                Take();
            if (m_Serials[record] != context.Serial)
            {
                m_Serials[record] = context.Serial;
                m_HasStart[record] = false;
                m_Reverse[record] = context.Reverse;
            }
            if (Live && !m_HasStart[record])
                StartPlay(record, context.Serial);
            TakeDestination(ref m_Destinations[record]);
            uint seed = Hash32.Tagged(context.ItemSeed, Hash32.Occurrence, Hash32.Fold(context.Occurrence));
            m_Evaluator.Begin(ref m_States[record], Vector4.zero, new MotionBegin { Seed = seed, Intensity = context.Intensity });
        }

        // as ValueTargetBinding's — the direction for the end and the next play's start; the start values and the origin stay
        internal override void Turn(int record, long serial, bool reverse)
        {
            if (m_Serials == null || m_Serials[record] != serial)
                return;
            m_Reverse[record] = reverse;
            if (m_LastSerial == serial)
                m_AfterReverse = reverse;
        }

        // ValueTargetBinding.StartPlay's rule, the three values together
        void StartPlay(int record, long serial)
        {
            Pose value = Base;
            m_Starts[record] = value;
            m_HasStart[record] = true;
            if (!m_HasFirst)
            {
                m_First = value;
                m_HasFirst = true;
            }
            m_LastSerial = serial;
            if (m_Reverse[record])
            {
                if (!m_HasOrigin)
                {
                    m_Origin = value;
                    m_HasOrigin = true;
                }
                m_From[record] = m_Origin;
                m_AfterReverse = true;
            }
            else if (m_HasOrigin && (m_Applied || (m_AfterReverse && m_Arrival)))
            {
                m_From[record] = m_Origin;
                m_AfterReverse = false;
            }
            else
            {
                m_From[record] = value;
                if (!(m_AfterReverse && m_ReverseCut))
                {
                    m_Origin = value;
                    m_HasOrigin = true;
                }
                m_AfterReverse = false;
            }
            m_ReverseCut = false;
            m_Applied = false;
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
            if (!Live || !m_HasStart[record])
                return;
            if (m_Item.m_Track)
                TakeDestination(ref m_Destinations[record]);
            Write(in m_From[record], in m_Destinations[record], m_Tween != null ? m_Tween.Progress(in time) : 1f);
        }

        void Write(in Pose from, in Pose destination, float progress)
        {
            float p = m_Arrival ? 1f - progress : progress;
            Transform parent = m_Target.parent;
            if (Alive(m_Position))
            {
                Vector3 start = parent == null ? from.Position : parent.TransformPoint(from.Position);
                Vector3 world = Vector3.LerpUnclamped(start, destination.Position, p);
                m_Position.WriteValue(parent == null ? world : parent.InverseTransformPoint(world));
            }
            if (Alive(m_Rotation))
            {
                Quaternion parentRotation = parent == null ? Quaternion.identity : parent.rotation;
                Quaternion world = Quaternion.SlerpUnclamped(parentRotation * from.Rotation, destination.Rotation, p);
                m_Rotation.WriteValue(Quaternion.Inverse(parentRotation) * world);
            }
            if (Alive(m_Scale))
                m_Scale.WriteValue(Vector3.LerpUnclamped(from.Scale, destination.Scale, p));
        }

        void WritePose(in Pose pose)
        {
            if (Alive(m_Position))
                m_Position.WriteValue(pose.Position);
            if (Alive(m_Rotation))
                m_Rotation.WriteValue(pose.Rotation);
            if (Alive(m_Scale))
                m_Scale.WriteValue(pose.Scale);
        }

        // Revert puts the play's start back
        internal override void End(int record, EndReason reason)
        {
            if (reason != EndReason.Completed && m_Reverse[record] && m_Serials[record] == m_LastSerial)
                m_ReverseCut = true;
            if (reason == EndReason.Revert && m_HasStart[record] && Live)
                WritePose(in m_Starts[record]);
        }

        internal override bool ApplyInitial(in MotionTime time, float intensity)
        {
            if (!Live)
                return false;
            Pose value = Base;
            if (!m_HasFirst)
            {
                m_First = value;
                m_HasFirst = true;
            }
            if (!m_Applied)
            {
                m_Origin = value;
                m_HasOrigin = true;
                m_Applied = true;
            }
            Pose destination = m_Origin;
            TakeDestination(ref destination);
            m_Evaluator.Begin(ref m_Initial, Vector4.zero, new MotionBegin { Seed = 0, Intensity = intensity });
            Write(in m_Origin, in destination, m_Tween != null ? m_Tween.Progress(in time) : 1f);
            return true;
        }

        internal override void Restore()
        {
            if (m_HasFirst && Live)
                WritePose(in m_First);
            m_HasOrigin = false;
            m_HasFirst = false;
            m_Applied = false;
            m_AfterReverse = false;
            m_ReverseCut = false;
        }

        // the same item's old binding of the same object
        internal override void Inherit(ItemBinding old)
        {
            if (!(old is MatchTransformBinding from) || m_Target == null || from.m_Target != m_Target)
                return;
            m_First = from.m_First;
            m_HasFirst = from.m_HasFirst;
            m_Origin = from.m_Origin;
            m_HasOrigin = from.m_HasOrigin;
            m_Applied = from.m_Applied;
            m_AfterReverse = from.m_AfterReverse;
            m_ReverseCut = from.m_ReverseCut;
        }

        internal override void Release()
        {
            if (m_Position != null)
                m_Layers.Release(m_Position, m_Reserved);
            if (m_Rotation != null)
                m_Layers.Release(m_Rotation, m_Reserved);
            if (m_Scale != null)
                m_Layers.Release(m_Scale, m_Reserved);
            m_Position = null;
            m_Rotation = null;
            m_Scale = null;
        }
    }

    // the parent slot — Set is an instant through the door (SetActiveBinding's form). Revert puts the play's start place
    // back, Restore the first one; backward it writes the origin. A cycle is refused at the prepare. The parent is read there
    // (the first read of a Transform's parent makes its C# object)
    internal sealed class SetParentBinding : ItemBinding
    {
        readonly SetParentItem m_Item;
        PropertyLayerRegistry m_Layers;
        ParentSlot m_Slot;
        TransformReference m_NewParent;
        bool m_Root;
        int m_Reserved;
        long[] m_Serials;
        bool[] m_HasStart;
        ParentState[] m_Starts;
        ParentState m_Origin;
        ParentState m_First;
        bool m_HasOrigin;
        bool m_HasFirst;

        internal SetParentBinding(SetParentItem item)
        {
            m_Item = item;
        }

        // observation
        internal ParentSlot Slot => m_Slot;

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
            Transform target = TargetResolver.Find<Transform>(in m_Item.m_Target, in context, out TargetFailure failure);
            if (target == null)
                return Refuse(TargetResolver.Describe(failure));
            m_Root = m_Item.m_NewParent.m_Mode == TargetRefMode.Direct && m_Item.m_NewParent.m_Object == null;
            if (!m_Root)
            {
                string parent = m_NewParent.Find(in m_Item.m_NewParent, in context);
                if (parent != null)
                    return Refuse("new parent: " + parent);
                if (Cycles(m_NewParent.Current, target))
                    return Refuse("the new parent is the target or under it");
            }
            m_Layers = context.Context.Layers;
            _ = target.parent;
            m_Slot = m_Layers.GetOrCreate(target, 0, 0, () => new ParentSlot(target));
            int n = context.MaxOverlap;
            m_Slot.Reserve(n);
            m_Reserved = n;
            m_Serials = new long[n];
            m_HasStart = new bool[n];
            m_Starts = new ParentState[n];
            for (int i = 0; i < n; i++)
                m_Serials[i] = -1;
            return true;
        }

        static bool Cycles(Transform parent, Transform target) => parent != null && (parent == target || parent.IsChildOf(target));

        bool Live => m_Slot != null && m_Slot.IsAlive && !m_Slot.Detached;

        internal override bool IsAlive => Live;

        // Set — at any time. Backward it writes the origin — nothing before a forward play. A new parent that is gone (or a main
        // camera not there yet), or one under the target now, writes nothing
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
                m_Starts[record] = m_Slot.Base;
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
            if (context.Reverse)
            {
                if (m_HasOrigin)
                    m_Slot.WriteState(in m_Origin);
                return;
            }
            Transform parent = null;
            if (!m_Root)
            {
                parent = m_NewParent.Current;
                if (parent == null || Cycles(parent, m_Slot.Target))
                    return;
            }
            m_Slot.WriteParent(parent, m_Item.m_WorldPositionStays);
        }

        internal override void Begin(int record, in BeginContext context)
        {
        }

        internal override void Evaluate(int record, in MotionTime time)
        {
        }

        internal override void End(int record, EndReason reason)
        {
            if (reason == EndReason.Revert && m_HasStart[record] && Live)
                m_Slot.WriteState(in m_Starts[record]);
        }

        internal override void Restore()
        {
            if (m_HasFirst && Live)
                m_Slot.WriteState(in m_First);
            m_HasOrigin = false;
            m_HasFirst = false;
        }

        // the same item's old binding of the same object
        internal override void Inherit(ItemBinding old)
        {
            if (!(old is SetParentBinding from) || m_Slot == null || from.m_Slot == null || !m_Slot.Key.Equals(from.m_Slot.Key))
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
}
