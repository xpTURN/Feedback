using System.Collections.Generic;
using System.Reflection;
using Unity.Cinemachine;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback
{
    // the third extension service — our impulses' book (events taken ahead · the count fired), the blend override's chain
    // (a pending activation's blend over the brain's) and the preview's Cinemachine time (the preview context writes it
    // before the brain at each tick). It owns no object
    internal sealed class CinemachineFeedbackService : FeedbackService
    {
        internal static readonly ServiceKey Key = new ServiceKey();

        internal static CinemachineFeedbackService Of(FeedbackContext context) => context.GetExtension(Key) as CinemachineFeedbackService;

        readonly Stack<CinemachineImpulseManager.ImpulseEvent> m_Ahead = new Stack<CinemachineImpulseManager.ImpulseEvent>();
        readonly CinemachineCore.GetBlendOverrideDelegate m_Mine;
        CinemachineCore.GetBlendOverrideDelegate m_Previous;
        ICinemachineCamera m_BlendTo;
        CinemachineBlendDefinition m_Blend;
        bool m_HasBlend;

        internal CinemachineFeedbackService(FeedbackContext context)
            : base(context)
        {
            m_Mine = Override;
        }

        // observation
        internal int Fired { get; private set; }
        internal int Ahead => m_Ahead.Count;
        internal bool Pending => m_HasBlend;
        internal bool Installed => CinemachineCore.GetBlendOverride == m_Mine;

        internal bool WritesTime => Context.Kind == ContextKind.Preview;

        // the preparation's warmth: the manager's events taken now — fired first
        internal void TakeAhead(int n)
        {
            CinemachineImpulseManager manager = CinemachineImpulseManager.Instance;
            for (int i = 0; i < n; i++)
                m_Ahead.Push(manager.NewImpulseEvent());
        }

        internal CinemachineImpulseManager.ImpulseEvent NewEvent() => m_Ahead.Count > 0 ? m_Ahead.Pop() : CinemachineImpulseManager.Instance.NewImpulseEvent();

        internal void Fire(CinemachineImpulseManager.ImpulseEvent e)
        {
            if (WritesTime)
            {
                SyncTime();
                CinemachinePreviewTime.Fired = true;
            }
            CinemachineImpulseManager.Instance.AddImpulseEvent(e);
            Fired++;
        }

        // installed at an activation's Begin and checked at each — Cinemachine 3.1's editor clears the delegate after the
        // first scene's load
        internal void PendBlend(ICinemachineCamera to, in CinemachineBlendDefinition blend)
        {
            if (CinemachineCore.GetBlendOverride != m_Mine)
            {
                m_Previous = CinemachineCore.GetBlendOverride;
                CinemachineCore.GetBlendOverride = m_Mine;
            }
            m_BlendTo = to;
            m_Blend = blend;
            m_HasBlend = true;
        }

        CinemachineBlendDefinition Override(ICinemachineCamera from, ICinemachineCamera to, CinemachineBlendDefinition fallback, Object owner)
        {
            if (m_HasBlend && to == m_BlendTo)
            {
                m_HasBlend = false;
                m_BlendTo = null;
                return m_Blend;
            }
            return m_Previous != null ? m_Previous(from, to, fallback, owner) : fallback;
        }

        void SyncTime()
        {
            CinemachineCore.CurrentTimeOverride = (float)Context.Now;
            if (CinemachineCore.UniformDeltaTimeOverride < 0f)
                CinemachineCore.UniformDeltaTimeOverride = 0f;
            CinemachineImpulseManager.Instance.IgnoreTimeScale = false;
            CinemachinePreviewTime.Wrote = true;
        }

        // the preview's Cinemachine time before the brain — the session's update runs before its player loop
        internal override void Update(in FrameTime time)
        {
            if (!WritesTime)
                return;
            CinemachineCore.UniformDeltaTimeOverride = time.ScaledDelta;
            SyncTime();
        }

        internal override void Shutdown()
        {
            if (CinemachineCore.GetBlendOverride == m_Mine)
                CinemachineCore.GetBlendOverride = m_Previous;
            m_Previous = null;
            m_HasBlend = false;
            m_BlendTo = null;
            m_Ahead.Clear();
        }

        internal override string DebugName => "Cinemachine";

        internal override void Describe(List<ServiceRow> into)
        {
            into.Clear();
            into.Add(new ServiceRow("impulses fired", Fired));
            into.Add(new ServiceRow("taken ahead", m_Ahead.Count));
            into.Add(new ServiceRow("blend override", Installed ? 1f : 0f));
            into.Add(new ServiceRow("pending", m_HasBlend ? 1f : 0f));
            into.Add(new ServiceRow("preview time", WritesTime ? 1f : 0f));
        }
    }

    // the preview's Cinemachine time — Core's hooks the editor's global state wraps. The capture takes the overrides, the
    // impulse clock's flag and the open scenes' noise seeds (the time moves them without dirtying the scene)
    internal static class CinemachinePreviewTime
    {
        internal static bool Wrote;
        internal static bool Fired;
        static float s_Delta;
        static float s_Time;
        static bool s_Ignore;
        static readonly List<CinemachineBasicMultiChannelPerlin> s_Perlins = new List<CinemachineBasicMultiChannelPerlin>();
        static readonly List<Vector3> s_Offsets = new List<Vector3>();
        static FieldInfo s_NoiseOffsets;

        internal static void Capture()
        {
            s_Delta = CinemachineCore.UniformDeltaTimeOverride;
            s_Time = CinemachineCore.CurrentTimeOverride;
            s_Ignore = CinemachineImpulseManager.Instance.IgnoreTimeScale;
            Wrote = false;
            Fired = false;
            s_Perlins.Clear();
            s_Offsets.Clear();
            s_NoiseOffsets ??= typeof(CinemachineBasicMultiChannelPerlin).GetField("m_NoiseOffsets", BindingFlags.NonPublic | BindingFlags.Instance);
            if (s_NoiseOffsets == null)
                return;
            foreach (CinemachineBasicMultiChannelPerlin p in Resources.FindObjectsOfTypeAll<CinemachineBasicMultiChannelPerlin>())
            {
                if (p != null && p.gameObject.scene.IsValid())
                {
                    s_Perlins.Add(p);
                    s_Offsets.Add((Vector3)s_NoiseOffsets.GetValue(p));
                }
            }
        }

        internal static void Restore()
        {
            CinemachineCore.UniformDeltaTimeOverride = s_Delta;
            CinemachineCore.CurrentTimeOverride = s_Time;
            CinemachineImpulseManager.Instance.IgnoreTimeScale = s_Ignore;
            for (int i = 0; i < s_Perlins.Count; i++)
            {
                if (s_Perlins[i] != null && (Vector3)s_NoiseOffsets.GetValue(s_Perlins[i]) != s_Offsets[i])
                    s_NoiseOffsets.SetValue(s_Perlins[i], s_Offsets[i]);
            }
            if (Fired)
                CinemachineImpulseManager.Instance.Clear();
#if UNITY_EDITOR
            if (Wrote || Fired)
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
            Wrote = false;
            Fired = false;
            s_Perlins.Clear();
            s_Offsets.Clear();
        }

        internal static string Describe() => "udt " + CinemachineCore.UniformDeltaTimeOverride.ToString("R") + " · time " + CinemachineCore.CurrentTimeOverride.ToString("R")
            + " · ignore " + CinemachineImpulseManager.Instance.IgnoreTimeScale;
    }

    // a camera's brain's live Cinemachine camera — before the brain's first update the top of the priority queue
    internal static class CinemachineTargets
    {
        internal static CinemachineVirtualCameraBase Live(Camera camera)
        {
            if (camera == null || !camera.TryGetComponent(out CinemachineBrain brain) || !brain.isActiveAndEnabled)
                return null;
            var live = brain.ActiveVirtualCamera as CinemachineVirtualCameraBase;
            if (live == null && CinemachineCore.VirtualCameraCount > 0)
                live = CinemachineCore.GetVirtualCamera(0);
            return live;
        }

        internal static Component MainCameraTarget(Camera camera, System.Type type)
        {
            CinemachineVirtualCameraBase live = Live(camera);
            if (live == null)
                return null;
            return type.IsInstanceOfType(live) ? live : live.GetComponent(type);
        }
    }

    // Save During Play snapshots at the play's exit before Feedback's runtime context closes (quitting) — its hot-save
    // delegate, just before the snapshot, puts the Cinemachine slots' first values back
    internal interface ICinemachineSaved
    {
        void PutBackFirst();
    }

    internal static class CinemachineSaved
    {
        internal static readonly List<ICinemachineSaved> Live = new List<ICinemachineSaved>();

        internal static void Add(ICinemachineSaved slot)
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
                Live.Add(slot);
#endif
        }

        internal static void Remove(ICinemachineSaved slot)
        {
#if UNITY_EDITOR
            Live.Remove(slot);
#endif
        }

        internal static void PutBack()
        {
            for (int i = Live.Count - 1; i >= 0; i--)
                Live[i].PutBackFirst();
        }

#if UNITY_EDITOR
        // the editor's load: Cinemachine's editor type by its name — the hot-save delegate gets ours once
        [UnityEditor.InitializeOnLoadMethod]
        static void Hook()
        {
            FieldInfo f = System.Type.GetType("Unity.Cinemachine.Editor.SaveDuringPlay, Unity.Cinemachine.Editor")?.GetField("OnHotSave", BindingFlags.Static | BindingFlags.Public);
            if (f == null)
                return;
            System.Delegate mine = System.Delegate.CreateDelegate(f.FieldType, typeof(CinemachineSaved).GetMethod(nameof(PutBack), BindingFlags.Static | BindingFlags.NonPublic));
            f.SetValue(null, System.Delegate.Combine(System.Delegate.Remove((System.Delegate)f.GetValue(null), mine), mine));
        }
#endif
    }
}
