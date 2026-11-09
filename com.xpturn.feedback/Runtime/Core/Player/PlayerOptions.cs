using System;
using UnityEngine;
using UnityEngine.Events;

namespace xpTURN.Feedback
{
    // the settings' enums are saved values — add members at the end only. The defaults are not 0: a component's field
    // initializers give them, a struct's field would not
    public enum PlayerInitialization
    {
        Awake = 0,
        Start = 1,
        Lazy = 2,
    }

    public enum PlayPolicy
    {
        Ignore = 0,
        Restart = 1,
        Overlap = 2,
    }

    // saved values too — the defaults are 0
    public enum PlayerAutoPlay
    {
        None = 0,
        OnStart = 1,
        OnEnable = 2,
    }

    public enum PlayerTimeMode
    {
        Scaled = 0,
        Unscaled = 1,
    }

    // the distance gate's reference: the main camera, an anchor by name, the play's position, the context's range center
    // (FeedbackRange.Center)
    public enum DistanceReference
    {
        MainCamera = 0,
        Anchor = 1,
        PlayPosition = 2,
        Global = 3,
    }

    // a saved value and Play's argument
    /// <summary>The way a player plays its list.</summary>
    public enum PlayerDirection
    {
        /// <summary>From the start to the end.</summary>
        Forward = 0,
        /// <summary>From the end to the start.</summary>
        Backward = 1,
        /// <summary>Turns at each accepted play; the first play is Forward.</summary>
        Toggle = 2,
    }

    // what OnDisable does to the plays that run. Continue reaches a destroy too — the finite plays go on without the player
    public enum PlayerDisableBehavior
    {
        Continue = 0,
        StopHold = 1,
        StopRevert = 2,
    }

    public enum PlayerEventKind
    {
        Played = 0,
        Completed = 1,
        Stopped = 2,
        Paused = 3,
        Resumed = 4,
        Looped = 5,
    }

    // one inspector-wired event: only the kinds in use have an entry
    [Serializable]
    internal struct PlayerUnityEvent
    {
        [SerializeField] internal PlayerEventKind m_Kind;
        [SerializeField] internal UnityEvent m_Event;
    }

    // a named reference a target resolves by name
    [Serializable]
    internal struct PlayerAnchor
    {
        [SerializeField] internal string m_Name;
        [SerializeField] internal UnityEngine.Object m_Object;
    }
}
