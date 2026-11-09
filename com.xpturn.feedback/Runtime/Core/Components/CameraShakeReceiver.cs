using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // a camera (or any transform) a channel's camera shakes reach. OnEnable takes its position and rotation shared slots and
    // reserves the contributions (settings — C), so the play path never looks a slot up. OnDisable gives them back and
    // raises the generation: a shake that reached it ends its contributions there, even while another binding holds the slot
    /// <summary>Moves this camera, or any transform, with the camera shakes sent to its channel.</summary>
    [AddComponentMenu("xpTURN/Feedback/Camera Shake Receiver")]
    [Icon("Packages/com.xpturn.feedback/Editor/Icons/CameraShakeReceiver.png")]
    public sealed class CameraShakeReceiver : MonoBehaviour
    {
        [SerializeField] internal ChannelRef m_Channel;
        // 0 takes every shake; above, a shake reaches it within this distance of its place
        [SerializeField] internal float m_Range;

        [NonSerialized] FeedbackScheduler m_Bound;
        [NonSerialized] ChannelService m_Registered;
        [NonSerialized] ChannelKey m_Key;
        [NonSerialized] PropertyLayerRegistry m_Layers;
        [NonSerialized] int m_Reserved;
        [NonSerialized] internal Transform Transform;
        [NonSerialized] internal LocalPositionSlot PositionSlot;
        [NonSerialized] internal LocalRotationSlot RotationSlot;
        [NonSerialized] internal int Generation;

        internal float Range => m_Range;

        // observation
        internal bool IsLive => PositionSlot != null && PositionSlot.IsAlive && !PositionSlot.Detached;
        internal bool IsRegistered => m_Registered != null;

        void OnEnable() => Register();

        void OnDisable() => Unregister();

        // edit mode runs no OnEnable: the direct-tick tests and the preview session bind with it (null unregisters)
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
            Transform t = transform;
            Transform = t;
            _ = t.parent;
            m_Layers = s.Context.Layers;
            m_Reserved = s.Context.Settings.ReceiverContributions;
            PositionSlot = m_Layers.GetOrCreate(t, 0, 0, () => new LocalPositionSlot(t));
            PositionSlot.Reserve(m_Reserved);
            // always — the receiver cannot know whether a shake turns
            RotationSlot = m_Layers.GetOrCreate(t, 0, 0, () => new LocalRotationSlot(t));
            RotationSlot.Reserve(m_Reserved);
            m_Key = new ChannelKey(in m_Channel);
            if (s.Context.Channels.Register(this, in m_Key))
                m_Registered = s.Context.Channels;
        }

        void Unregister()
        {
            m_Registered?.Unregister(this, in m_Key);
            m_Registered = null;
            Generation++;
            if (PositionSlot != null)
                m_Layers.Release(PositionSlot, m_Reserved);
            if (RotationSlot != null)
                m_Layers.Release(RotationSlot, m_Reserved);
            PositionSlot = null;
            RotationSlot = null;
        }
    }
}
