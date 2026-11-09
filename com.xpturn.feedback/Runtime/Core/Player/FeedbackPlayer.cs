using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the component — the list, the settings, and the runtime's lifetime. The playback API is FeedbackPlayer.Api.cs — every
    // saved field is in this file: the language orders no fields across the files of a partial class
    /// <summary>Plays a list of feedback items: play, stop, pause, seek and wait for the end of each play.</summary>
    [AddComponentMenu("xpTURN/Feedback/Feedback Player")]
    [Icon("Packages/com.xpturn.feedback/Editor/Icons/FeedbackPlayer.png")]
    public sealed partial class FeedbackPlayer : MonoBehaviour, ISerializationCallbackReceiver
    {
        internal const int OverlapLimit = 16;
        internal const string NewerFormatWarning = "A FeedbackPlayer was saved by a newer version of the package — fields and item types this version does not know are lost: ";
        internal const string EditModeWarning = "FeedbackPlayer.Play was called in edit mode without a preview and was ignored.";
        internal const string ReentryWarning = "FeedbackPlayer.Play was called from inside the same player's own Play or initialization and was refused.";
        internal const string NeverAwakeWarning = "FeedbackPlayer.Play was called on a player that never woke (an object that was never active, or a prefab asset) and was refused.";
        internal const string ArgumentWarning = "A FeedbackPlayer call had an invalid argument and was ignored: ";
        internal const string ReverseWarning = "A FeedbackPlayer list cannot play backward (an endless repeat or item, a signal wait without a timeout, or a wait on another player) — the backward play was refused.";
        internal const string SignalWarning = "FeedbackPlayer.Signal found no play waiting — a wait that has not begun yet does not take the signal.";

        [SerializeReference] internal List<FeedbackItem> m_Items = new List<FeedbackItem>();
        [SerializeField] internal FeedbackPreset m_Preset;
        [SerializeField] internal List<PlayerAnchor> m_Anchors = new List<PlayerAnchor>();
        [SerializeField] internal int m_FormatVersion = FeedbackFormat.Current;
        [SerializeField] internal PlayerInitialization m_Initialization = PlayerInitialization.Start;
        [SerializeField] internal PlayPolicy m_Policy = PlayPolicy.Restart;
        [SerializeField] internal int m_MaxOverlap = 4;
        [SerializeField] internal float m_DurationMultiplier = 1f;
        [SerializeField] internal float m_StartDelay;
        // read when a Play or a tick reads them — no Rebuild needed
        [SerializeField] internal PlayerAutoPlay m_AutoPlay;
        [SerializeField] internal PlayerTimeMode m_TimeMode;
        [SerializeField] internal float m_Speed = 1f;
        [SerializeField] internal bool m_RandomizeDuration;
        [SerializeField] internal Vector2 m_DurationRange = new Vector2(0.5f, 2f);
        [SerializeField] internal float m_Cooldown;
        [SerializeField] internal float m_Chance = 100f;
        [SerializeField] internal float m_Intensity = 1f;
        [SerializeField] internal bool m_RemapIntensity;
        [SerializeField] internal AnimationCurve m_IntensityRemap = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] internal bool m_UseDistanceGate;
        [SerializeField] internal DistanceReference m_GateReference;
        [SerializeField] internal string m_GateAnchor = "";
        [SerializeField] internal float m_GateDistance = 5f;
        [SerializeField] internal bool m_UseGateFalloff;
        [SerializeField] internal AnimationCurve m_GateFalloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        [SerializeField] internal List<PlayerUnityEvent> m_UnityEvents = new List<PlayerUnityEvent>();
        // read at a Play, at the prepare's end and at OnDisable · OnDestroy
        [SerializeField] internal PlayerDirection m_Direction;
        [SerializeField] internal bool m_ApplyInitialState;
        [SerializeField] internal PlayerDisableBehavior m_DisableBehavior;

        // the runtime state is one unserialized object — "prepared" is this reference: a plain private field would survive a
        // recompile during play while the objects it points to do not
        [NonSerialized] PlayerRuntime m_Runtime;
        // runtimes made — the inspector's play-mode edit banner clears when it moves, a Rebuild from anywhere
        [NonSerialized] int m_PrepareCount;
        [NonSerialized] FeedbackScheduler m_Bound;
        [NonSerialized] bool m_Destroyed;
        [NonSerialized] bool m_Preparing;
        [NonSerialized] bool m_InPlay;
        [NonSerialized] bool m_Disabling;
        // a preview's masks by list index (0 · 1 Mute · 2 Solo — null is none) and the item's ▶ (its list index while its
        // Play runs) — the Preview kind reads them
        [NonSerialized] internal byte[] m_PreviewMasks;
        [NonSerialized] internal int m_PreviewSoloOnce = -1;
        // the first frame's marks: plain private fields on purpose — they survive a recompile during
        // play, so the prepare after it does not write the first frame over a play again, and they are not saved (a copy
        // writes its own). Written · a play accepted since
        bool m_InitialWritten;
        bool m_InitialPlayed;
        // the Signal calls that ended no wait — the direct one (its warning's cue) and the quiet one (a channel's broadcast,
        // the preview's), for the debugger
        [NonSerialized] int m_MissedSignals;
        [NonSerialized] int m_MissedQuietSignals;
#if DEBUG
        [NonSerialized] bool m_WarnedFormat;
        [NonSerialized] ushort m_Warned;
#endif

        // why the entry did not prepare — the warnings tell them apart
        enum Refusal : byte
        {
            None,
            Destroyed,
            Reentry,
            EditMode,
            Closed,
            NeverAwake,
            Argument,
            Reverse,
            Signal,
        }

        internal PlayerRuntime Runtime => m_Runtime;

        internal int PrepareCount => m_PrepareCount;

        // inside OnDisable's stop — a SetActive revert reads it
        internal bool IsDisabling => m_Disabling;

        // a play was accepted: the next Rebuild does not write the first frame over it
        internal void NoteAccepted()
        {
            if (m_InitialWritten)
                m_InitialPlayed = true;
        }

        // the list the prepare reads — the linked preset's, else the own one (kept and hidden while linked). The editor asks
        // the list's owner of PresetLinks
        internal List<FeedbackItem> Items => m_Preset != null ? m_Preset.m_Items : m_Items;

        // the play records and the scheduler's reservation: Ignore · Restart play one at a time
        internal int MaxOverlap => m_Policy == PlayPolicy.Overlap ? Mathf.Clamp(m_MaxOverlap, 1, OverlapLimit) : 1;

        // ---- the public surface (the playback API is FeedbackPlayer.Api.cs)

        // makes the runtime again from the list and the settings as they are now — after the list, a target or an anchor
        // changed. Prepares a player that is not prepared yet. Allocates. The plays that run stop (Hold). false = not
        // prepared (edit mode without a preview, a closed scheduler, a destroyed player or one that never woke, inside its
        // own prepare or its own Play)
        /// <summary>Rebuilds the player from its list and settings as they are now and stops the plays that run (Hold); false when it cannot be prepared, or when called inside its own Play, where nothing stops.</summary>
        public bool Rebuild()
        {
            // inside its own Play the pipeline's runtime would be released under it
            if (m_InPlay)
                return false;
            m_Runtime?.StopAll(StopMode.Hold);
            // the new runtime takes the old one's snapshots and Toggle cell, then the old one goes
            PlayerRuntime old = m_Runtime;
            m_Runtime = null;
            try
            {
                return Prepare(old) == Refusal.None;
            }
            finally
            {
                old?.Release(PlaybackEnd.Stopped);
            }
        }

        /// <summary>Points the anchor <paramref name="name"/> at <paramref name="target"/>, adding it when there is none; distance gates see it at the next Play, item targets after Rebuild.</summary>
        public void SetAnchor(string name, UnityEngine.Object target)
        {
            if (string.IsNullOrEmpty(name))
                return;
            for (int i = 0; i < m_Anchors.Count; i++)
            {
                if (m_Anchors[i].m_Name != name)
                    continue;
                PlayerAnchor anchor = m_Anchors[i];
                anchor.m_Object = target;
                m_Anchors[i] = anchor;
                Touch(target);
                return;
            }
            m_Anchors.Add(new PlayerAnchor { m_Name = name, m_Object = target });
            Touch(target);
        }

        // the engine makes an object's C# transform on the first read — the prepare and SetAnchor read them, not a Play
        internal void TouchAnchors()
        {
            for (int i = 0; i < m_Anchors.Count; i++)
                Touch(m_Anchors[i].m_Object);
        }

        static void Touch(UnityEngine.Object target)
        {
            if (target is Component component && component != null)
                _ = component.transform;
            else if (target is GameObject go && go != null)
                _ = go.transform;
        }

        // the first live entry of that name
        internal bool TryGetAnchor(string name, out UnityEngine.Object target)
        {
            for (int i = 0; i < m_Anchors.Count; i++)
            {
                if (m_Anchors[i].m_Name != name || m_Anchors[i].m_Object == null)
                    continue;
                target = m_Anchors[i].m_Object;
                return true;
            }
            target = null;
            return false;
        }

        // ---- the seams the preview session and the direct-tick tests share: edit mode runs no Awake · OnDestroy

        // null unbinds. The runtime is left as it is — the next entry releases one made for another scheduler
        internal void Bind(FeedbackScheduler scheduler) => m_Bound = scheduler;

        // the loop's scheduler — never kept: a new one each session and each recompile during play
        static FeedbackScheduler PlayingDefault()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return null;
#endif
            return FeedbackScheduler.Default;
        }

        // prepares if it has not — "at the latest at the configured time": an earlier call prepares, Play too.
        // false leaves no runtime
        internal bool EnsureRuntime() => Prepare(null) == Refusal.None;

        // old: Rebuild's runtime — the new one takes its snapshots
        Refusal Prepare(PlayerRuntime old)
        {
            // the C# object of a destroyed component can still be called · a prepare does not enter itself
            if (m_Destroyed)
                return Refusal.Destroyed;
            if (m_Preparing)
                return Refusal.Reentry;
            // a preview's or a test's scheduler that is gone — with the scene reload off the edit-mode object goes into play as it is
            if (m_Bound != null && m_Bound.IsShutdown)
                m_Bound = null;
            FeedbackScheduler scheduler = m_Bound ?? PlayingDefault();
            if (m_Runtime != null)
            {
                if (m_Runtime.Scheduler == scheduler && !scheduler.IsShutdown)
                    return Refusal.None;
                // made for a scheduler that is gone, closed or unbound
                ReleaseRuntime();
            }
            if (scheduler == null)
                return Application.isPlaying ? Refusal.Closed : Refusal.EditMode;
            if (scheduler.IsShutdown)
                return Refusal.Closed;
            // the build path: a destroyed object is refused before anything touches it, bound or not
            if (this == null)
                return Refusal.Destroyed;
            // OnDestroy comes only to a player that woke — a runtime on one that never does would never be released: an
            // object that was never active, a prefab asset. An active object wakes its components in this activation (a
            // script before the player calls from its own Awake · OnEnable)
            if (m_Bound == null && !didAwake && !gameObject.activeInHierarchy)
                return Refusal.NeverAwake;
#if DEBUG
            if (m_FormatVersion > FeedbackFormat.Current && !m_WarnedFormat)
            {
                m_WarnedFormat = true;
                Debug.LogWarning(NewerFormatWarning + m_FormatVersion + " > " + FeedbackFormat.Current, this);
            }
            FeedbackPreset preset = m_Preset;
            if (preset != null && preset.m_FormatVersion > FeedbackFormat.Current && !preset.m_WarnedFormat)
            {
                preset.m_WarnedFormat = true;
                Debug.LogWarning(FeedbackPreset.NewerFormatWarning + preset.name + " " + preset.m_FormatVersion + " > " + FeedbackFormat.Current, preset);
            }
#endif
            // the first frame: at the first prepare and at a Rebuild before any play since it wrote — not at the prepare after
            // a recompile (the marks survive it) and not over a play
            bool initial = m_ApplyInitialState && (!m_InitialWritten || (old != null && !m_InitialPlayed));
            m_Preparing = true;
            try
            {
                m_Runtime = new PlayerRuntime(this, scheduler, old, initial);
                m_PrepareCount++;
            }
            finally
            {
                m_Preparing = false;
            }
            if (initial)
            {
                m_InitialWritten = true;
                m_InitialPlayed = false;
            }
            return Refusal.None;
        }

        // once: the reference goes first
        internal void ReleaseRuntime()
        {
            PlayerRuntime runtime = m_Runtime;
            if (runtime == null)
                return;
            m_Runtime = null;
            runtime.Release(PlaybackEnd.Stopped);
        }

        // ---- the lifetime

        void Awake()
        {
            if (m_Initialization == PlayerInitialization.Awake)
                EnsureRuntime();
        }

        // the auto play of Start: once — off and on again does not play it again
        void Start()
        {
            if (m_Initialization == PlayerInitialization.Start)
                EnsureRuntime();
            if (m_AutoPlay == PlayerAutoPlay.OnStart)
                Play();
        }

        // after a recompile during play Awake · Start do not run again and the runtime is gone — this does. didStart: the
        // engine's own mark — it survives the reload and a clone does not have it. The auto play of OnEnable comes at every
        // enable, the one of Start again with the runtime it lost
        void OnEnable()
        {
            bool lost = m_Runtime == null;
            if (lost && (m_Initialization == PlayerInitialization.Awake || (m_Initialization == PlayerInitialization.Start && didStart)))
                EnsureRuntime();
            if (m_AutoPlay == PlayerAutoPlay.OnEnable || (m_AutoPlay == PlayerAutoPlay.OnStart && lost && didStart))
                Play();
        }

        // the plays that run now stop in the set mode — a Destroy calls this inside the call, before the destroyed mark.
        // Continue does nothing: the plays go on while off, and on through a destroy
        void OnDisable()
        {
            PlayerDisableBehavior behavior = m_DisableBehavior;
            if (m_Runtime == null || (behavior != PlayerDisableBehavior.StopHold && behavior != PlayerDisableBehavior.StopRevert))
                return;
            m_Disabling = true;
            try
            {
                m_Runtime.StopAll(behavior == PlayerDisableBehavior.StopRevert ? StopMode.Revert : StopMode.Hold);
            }
            finally
            {
                m_Disabling = false;
            }
        }

        // the mark first: a script after the player can call from its own OnDestroy — the object is not null yet. The plays
        // end quietly — with Continue (an unknown saved value too) the finite ones go on, detached
        void OnDestroy()
        {
            m_Destroyed = true;
            PlayerRuntime runtime = m_Runtime;
            if (runtime == null)
                return;
            m_Runtime = null;
            if (m_DisableBehavior != PlayerDisableBehavior.StopHold && m_DisableBehavior != PlayerDisableBehavior.StopRevert && runtime.Detach())
                return;
            runtime.Release(PlaybackEnd.Destroyed);
        }

        // ---- the format version: data only, no Unity API — any thread

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize() => FeedbackFormat.Upgrade(ref m_FormatVersion, m_Items);
    }
}
