using UnityEngine;

namespace xpTURN.Feedback
{
    // which defaults a trauma starts from when its transform has no TraumaShaker
    internal enum TraumaKind
    {
        Camera,
        Object,
    }

    // a trauma's shaker values — a TraumaShaker's or a kind's defaults
    internal struct TraumaParams
    {
        internal Vector3 MaxOffset;
        internal Vector3 MaxAngle;
        internal float Frequency;
        internal float Exponent;
        internal float Decay;
        internal bool Unscaled;

        internal static TraumaParams CameraDefault => new TraumaParams { MaxOffset = new Vector3(0.3f, 0.3f, 0f), MaxAngle = new Vector3(0f, 0f, 3f), Frequency = 12f, Exponent = 2f, Decay = 1f };

        internal static TraumaParams ObjectDefault => new TraumaParams { MaxOffset = new Vector3(0.1f, 0.1f, 0.1f), MaxAngle = new Vector3(5f, 5f, 5f), Frequency = 12f, Exponent = 2f, Decay = 1f };
    }

    // each transform's trauma (0 ~ 1): an add raises it, it fades at Decay a second, and while above 0 the transform shakes by
    // MaxOffset · MaxAngle × trauma^Exponent × a smooth noise of its own — the noise's time runs on, so an add never jumps.
    // A fixed number of cells; a cell keeps its transform's position and rotation slots (the shared ones, or a Cinemachine
    // extension's pair) with one reservation each, as a binding does — taken on the preparation path (Warm), given back when
    // the cell goes. Its contributions run while the trauma is above 0. The category is the target's: a camera, a camera shake
    // receiver or a Cinemachine pair follows Camera and the settings' shake caps, any other transform Motion
    internal sealed class TraumaService : FeedbackService
    {
        internal const string FullWarning = "The Feedback trauma cells were full: the one with the least trauma was let go. Raise the settings' Trauma Capacity.";

        struct Cell
        {
            internal Transform Target;
            internal float T;
            internal double Tau;
            internal uint Seed;
            internal TraumaParams P;
            internal bool Camera;
            internal bool Running;
            internal ValueSlot<Vector3, Vector3Ops> Position;
            internal ValueSlot<Quaternion, QuaternionOps> Rotation;
            internal ContributionHandle PositionHandle;
            internal ContributionHandle RotationHandle;
        }

        readonly Cell[] m_Cells;
        readonly float m_MaximumOffset;
        readonly float m_MaximumAngle;
        int m_Count;
        // each new cell's noise seed — a running number, hashed
        uint m_Seeds;
#if DEBUG
        bool m_WarnedFull;
#endif

        internal TraumaService(FeedbackContext context, SettingsValues settings)
            : base(context)
        {
            m_Cells = new Cell[Mathf.Max(1, settings.TraumaCapacity)];
            m_MaximumOffset = settings.MaximumShakeOffset;
            m_MaximumAngle = settings.MaximumShakeAngle;
        }

        // observation
        internal int Count => m_Count;
        internal int Capacity => m_Cells.Length;
        internal Transform TargetAt(int i) => m_Cells[i].Target;
        internal float TraumaAt(int i) => m_Cells[i].T;
        internal bool CameraAt(int i) => m_Cells[i].Camera;
        internal uint SeedAt(int i) => m_Cells[i].Seed;
        internal double NoiseTimeAt(int i) => m_Cells[i].Tau;
        internal bool RunningAt(int i) => m_Cells[i].Running;
        internal ValueSlot<Vector3, Vector3Ops> PositionSlotAt(int i) => m_Cells[i].Position;
        internal ValueSlot<Quaternion, QuaternionOps> RotationSlotAt(int i) => m_Cells[i].Rotation;

        internal int IndexOf(Transform target)
        {
            for (int i = 0; i < m_Count; i++)
            {
                if (ReferenceEquals(m_Cells[i].Target, target))
                    return i;
            }
            return -1;
        }

        // the preparation path (allocation allowed): the transform's cell with its slots — −1 when none can be made. A full
        // service lets the cell with the least trauma go (a resting one first)
        internal int Warm(Transform target, TraumaKind kind)
        {
            if (target == null || Refuses())
                return -1;
            int i = IndexOf(target);
            if (i >= 0)
                return i;
            PropertyLayerRegistry layers = Context.Layers;
            if (layers.IsClosed)
                return -1;
            if (m_Count == m_Cells.Length)
            {
#if DEBUG
                if (!m_WarnedFull)
                {
                    m_WarnedFull = true;
                    Debug.LogWarning(FullWarning);
                }
#endif
                Drop(Lowest());
            }
            _ = target.parent;
            bool camera = target.TryGetComponent(out Camera _) || target.TryGetComponent(out CameraShakeReceiver _);
            if (CameraHooks.ShakeSlots != null && CameraHooks.ShakeSlots(target, layers, false, true, out ValueSlot<Vector3, Vector3Ops> position, out ValueSlot<Quaternion, QuaternionOps> rotation))
                camera = true;
            else
            {
                position = PositionOf(layers, target);
                rotation = RotationOf(layers, target);
            }
            position.Reserve(1);
            rotation.Reserve(1);
            TraumaParams p = target.TryGetComponent(out TraumaShaker shaker) ? shaker.Params
                : kind == TraumaKind.Camera ? TraumaParams.CameraDefault : TraumaParams.ObjectDefault;
            i = m_Count++;
            m_Cells[i] = new Cell { Target = target, Seed = Hash32.Fold(++m_Seeds), P = p, Camera = camera, Position = position, Rotation = rotation };
            return i;
        }

        // the slot factories capture the target — apart from Warm, whose found-cell path would otherwise allocate the closure on
        // entry
        static ValueSlot<Vector3, Vector3Ops> PositionOf(PropertyLayerRegistry layers, Transform target) => layers.GetOrCreate(target, 0, 0, () => new LocalPositionSlot(target));

        static ValueSlot<Quaternion, QuaternionOps> RotationOf(PropertyLayerRegistry layers, Transform target) => layers.GetOrCreate(target, 0, 0, () => new LocalRotationSlot(target));

        // raises the transform's trauma and writes its shake at once — an add in a play shows in the same frame. A cell made here
        // allocates: a feedback warms its target when it prepares
        internal void Add(Transform target, float amount, TraumaKind kind)
        {
            int i = Warm(target, kind);
            if (i < 0)
                return;
            if (!Live(in m_Cells[i]))
            {
                Drop(i);
                return;
            }
            ref Cell c = ref m_Cells[i];
            c.T = MotionKernels.TraumaAdd(c.T, amount);
            if (c.T <= 0f)
                return;
            if (!c.Running)
            {
                c.PositionHandle = c.Position.Begin(ContributionKind.Offset, LayerSpace.Self, true);
                c.RotationHandle = c.Rotation.Begin(ContributionKind.Offset, LayerSpace.Local, true);
                c.Running = true;
            }
            Write(ref c);
        }

        internal float Get(Transform target)
        {
            int i = IndexOf(target);
            return i >= 0 ? m_Cells[i].T : 0f;
        }

        // the trauma to 0 — the shake ends now, the cell stays warm
        internal void Clear(Transform target)
        {
            int i = IndexOf(target);
            if (i < 0)
                return;
            m_Cells[i].T = 0f;
            Stop(ref m_Cells[i]);
        }

        internal override void Update(in FrameTime time)
        {
            for (int i = m_Count - 1; i >= 0; i--)
            {
                if (!Live(in m_Cells[i]))
                {
                    Drop(i);
                    continue;
                }
                ref Cell c = ref m_Cells[i];
                if (!c.Running)
                    continue;
                double dt = c.P.Unscaled ? time.UnscaledDelta : time.ScaledDelta;
                c.T = MotionKernels.TraumaDecay(c.T, c.P.Decay, dt);
                if (dt > 0.0)
                    c.Tau += dt;
                if (c.T <= 0f)
                {
                    Stop(ref c);
                    continue;
                }
                Write(ref c);
            }
        }

        // a close with the context's open layers gives the slots back; the context closes its layers first (they put the
        // values back) and then nothing is left to give
        internal override void Shutdown()
        {
            bool open = !Context.Layers.IsClosed;
            for (int i = 0; i < m_Count; i++)
            {
                if (open)
                {
                    Stop(ref m_Cells[i]);
                    Context.Layers.Release(m_Cells[i].Position, 1);
                    Context.Layers.Release(m_Cells[i].Rotation, 1);
                }
                m_Cells[i] = default;
            }
            m_Count = 0;
        }

        static bool Live(in Cell c) => c.Target != null && c.Position.IsAlive && !c.Position.Detached && c.Rotation.IsAlive && !c.Rotation.Detached;

        int Lowest()
        {
            int low = 0;
            for (int i = 1; i < m_Count; i++)
            {
                if (m_Cells[i].T < m_Cells[low].T)
                    low = i;
            }
            return low;
        }

        // ends the contributions, gives the slots back and closes the gap — the last cell moves in
        void Drop(int i)
        {
            Stop(ref m_Cells[i]);
            PropertyLayerRegistry layers = Context.Layers;
            if (!layers.IsClosed)
            {
                layers.Release(m_Cells[i].Position, 1);
                layers.Release(m_Cells[i].Rotation, 1);
            }
            m_Cells[i] = m_Cells[--m_Count];
            m_Cells[m_Count] = default;
        }

        static void Stop(ref Cell c)
        {
            if (!c.Running)
                return;
            c.Running = false;
            c.Position.End(c.PositionHandle);
            c.Rotation.End(c.RotationHandle);
            c.PositionHandle = default;
            c.RotationHandle = default;
        }

        void Write(ref Cell c)
        {
            CategoryGate gate = Context.Categories;
            float k = MotionKernels.TraumaShape(c.T, c.P.Exponent) * gate.Scale(c.Camera ? FeedbackCategory.Camera : FeedbackCategory.Motion);
            float f = c.P.Frequency;
            double s = c.Tau;
            var offset = new Vector3(c.P.MaxOffset.x * N(c.Seed, 0u, f, s) * k, c.P.MaxOffset.y * N(c.Seed, 1u, f, s) * k, c.P.MaxOffset.z * N(c.Seed, 2u, f, s) * k);
            Quaternion turn = Quaternion.Euler(c.P.MaxAngle.x * N(c.Seed, 3u, f, s) * k, c.P.MaxAngle.y * N(c.Seed, 4u, f, s) * k, c.P.MaxAngle.z * N(c.Seed, 5u, f, s) * k);
            c.Position.SetOffset(c.PositionHandle, in offset);
            c.Rotation.SetOffset(c.RotationHandle, in turn);
            if (!c.Camera)
                return;
            float camera = gate.Scale(FeedbackCategory.Camera);
            if (m_MaximumOffset > 0f)
                c.Position.SetLimit(m_MaximumOffset * camera);
            if (m_MaximumAngle > 0f)
                c.Rotation.SetLimit(m_MaximumAngle * camera);
        }

        // component c's smooth noise of the cell's seed
        internal static float N(uint seed, uint component, float frequency, double seconds) => Noise.SmoothHz(Hash32.Tagged(seed, Hash32.Component, component), frequency, seconds);
    }

    // the public trauma — the loop's context (Default)
    /// <summary>Adds to, reads and clears the trauma that shakes a camera or any transform.</summary>
    public static class FeedbackTrauma
    {
        internal const string NoContextWarning = "FeedbackTrauma was used with no running Feedback context (outside play) and did nothing.";
#if DEBUG
        static bool s_WarnedContext;
#endif

        static TraumaService Made()
        {
            FeedbackScheduler s = FeedbackScheduler.Default;
            return s != null && !s.IsShutdown ? s.Context.TraumaIfMade : null;
        }

        /// <summary>Adds an amount (0 to 1) to the target's trauma, which shakes it and then fades on its own; the first add to a transform no feedback has prepared allocates once.</summary>
        public static void Add(Transform target, float amount)
        {
            if (target == null)
                return;
            FeedbackScheduler s = FeedbackScheduler.Default;
            if (s == null || s.IsShutdown)
            {
#if DEBUG
                if (!s_WarnedContext)
                {
                    s_WarnedContext = true;
                    Debug.LogWarning(NoContextWarning);
                }
#endif
                return;
            }
            s.Context.Trauma.Add(target, amount, target.TryGetComponent(out Camera _) ? TraumaKind.Camera : TraumaKind.Object);
        }

        /// <summary>Adds an amount (0 to 1) to the camera's trauma.</summary>
        public static void Add(Camera camera, float amount)
        {
            if (camera != null)
                Add(camera.transform, amount);
        }

        /// <summary>The target's trauma, 0 to 1; 0 when it has none.</summary>
        public static float Get(Transform target)
        {
            TraumaService t = Made();
            return t != null && target != null ? t.Get(target) : 0f;
        }

        /// <summary>Takes the target's trauma away at once; its shake stops.</summary>
        public static void Clear(Transform target)
        {
            TraumaService t = Made();
            if (t != null && target != null)
                t.Clear(target);
        }
    }
}
