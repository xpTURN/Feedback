using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // takes a channel's commands for a player — its own when none is set. Registered while enabled: OnEnable takes the
    // context's registry and OnDisable gives it back (never registered twice). A recompile's reload registers it again in
    // the new Default
    /// <summary>Takes the commands sent to its channel for a player — its own player when none is set — while it is enabled.</summary>
    [AddComponentMenu("xpTURN/Feedback/Feedback Receiver")]
    [Icon("Packages/com.xpturn.feedback/Editor/Icons/FeedbackReceiver.png")]
    public sealed class FeedbackReceiver : MonoBehaviour, ICommandReceiver
    {
        internal const string NoPlayerWarning = "A FeedbackReceiver has no FeedbackPlayer to take its channel's commands (none set and none on its object); it is not registered.";

        [SerializeField] internal ChannelRef m_Channel;
        [SerializeField] internal FeedbackPlayer m_Player;
        // 0 takes every send; above, a send with a position takes it within this distance
        [SerializeField] internal float m_Range;

        [NonSerialized] FeedbackScheduler m_Bound;
        [NonSerialized] ChannelService m_Registered;
        [NonSerialized] ChannelKey m_Key;
        [NonSerialized] Transform m_Transform;
        [NonSerialized] FeedbackPlayer m_Target;
#if DEBUG
        [NonSerialized] bool m_WarnedNoPlayer;
#endif

        Vector3 ICommandReceiver.Position => m_Transform.position;
        float ICommandReceiver.Range => m_Range;

        // observation
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
            m_Target = m_Player != null ? m_Player : GetComponent<FeedbackPlayer>();
            if (m_Target == null)
            {
#if DEBUG
                if (!m_WarnedNoPlayer)
                {
                    m_WarnedNoPlayer = true;
                    Debug.LogWarning(NoPlayerWarning, this);
                }
#endif
                return;
            }
            m_Key = new ChannelKey(in m_Channel);
            if (s.Context.Channels.Register(this, in m_Key))
                m_Registered = s.Context.Channels;
        }

        // to the service it registered with — a context that changed meanwhile (a new session, a reload) is left alone
        void Unregister()
        {
            m_Registered?.Unregister(this, in m_Key);
            m_Registered = null;
        }

        // the player's public API — its refusals and warnings stay. A Play without a position takes the send's; a broadcast
        // Signal reaches players with no wait, so it does not warn
        void ICommandReceiver.Receive(in FeedbackCommand command, bool hasPosition, Vector3 position)
        {
            FeedbackPlayer player = m_Target;
            if (player == null)
                return;
            switch (command.Kind)
            {
                case FeedbackCommandKind.Play:
                    player.Play(hasPosition && !command.Args.HasPosition ? command.Args.WithPosition(position) : command.Args);
                    return;
                case FeedbackCommandKind.Stop:
                    player.Stop(command.Mode);
                    return;
                case FeedbackCommandKind.Pause:
                    player.Pause();
                    return;
                case FeedbackCommandKind.Resume:
                    player.Resume();
                    return;
                case FeedbackCommandKind.Seek:
                    player.Seek(command.Time);
                    return;
                case FeedbackCommandKind.Skip:
                    player.Skip();
                    return;
                case FeedbackCommandKind.Restore:
                    player.Restore();
                    return;
                case FeedbackCommandKind.Signal:
                    player.SignalQuietly();
                    return;
            }
        }
    }
}
