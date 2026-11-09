using System;
using UnityEngine;
using UnityEngine.Events;

namespace xpTURN.Feedback
{
    // FeedbackReceiver's shape
    /// <summary>Raises a UnityEvent with each signal sent to its channel while it is enabled; a signal with a place reaches it only within its range (0 reaches it everywhere).</summary>
    [AddComponentMenu("xpTURN/Feedback/Feedback Signal Receiver")]
    public sealed class FeedbackSignalReceiver : MonoBehaviour, IFeedbackSignalReceiver
    {
        [SerializeField] internal ChannelRef m_Channel;
        [SerializeField] internal float m_Range;
        [SerializeField] internal UnityEvent<float> m_OnSignal = new UnityEvent<float>();

        [NonSerialized] FeedbackScheduler m_Bound;
        [NonSerialized] ChannelService m_Registered;
        [NonSerialized] ChannelKey m_Key;
        [NonSerialized] Transform m_Transform;

        internal bool IsRegistered => m_Registered != null;

        void OnEnable() => Register();

        void OnDisable() => Unregister();

        // the direct-tick tests' seam: edit mode runs no OnEnable (null unregisters)
        internal void Bind(FeedbackScheduler scheduler)
        {
            Unregister();
            m_Bound = scheduler;
            Register();
        }

        void Register()
        {
            FeedbackScheduler s = m_Bound;
            if (s == null && Application.isPlaying)
                s = FeedbackScheduler.Default;
            if (s == null || s.IsShutdown || m_Registered != null)
                return;
            m_Transform = transform;
            m_Key = new ChannelKey(in m_Channel);
            if (s.Context.Channels.Register(this, in m_Key))
                m_Registered = s.Context.Channels;
        }

        void Unregister()
        {
            m_Registered?.Unregister(this, in m_Key);
            m_Registered = null;
        }

        void IFeedbackSignalReceiver.OnFeedbackSignal(in FeedbackSignal signal)
        {
            if (signal.HasPosition && m_Range > 0f && (m_Transform.position - signal.Position).sqrMagnitude > m_Range * m_Range)
                return;
            m_OnSignal?.Invoke(signal.Value);
        }
    }
}
