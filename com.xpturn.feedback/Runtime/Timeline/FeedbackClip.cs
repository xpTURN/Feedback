using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace xpTURN.Feedback
{
    // a Feedback track's clip — one play of the track's player. Extrapolation is a Driven clip's only: a hold before the clip
    // turns the clip on before its start, where a triggered play would start early
    /// <summary>A clip of a Feedback track: one play of the track's FeedbackPlayer — its list, or the linked preset or one of its Variants the clip's Preset picks — moved by the timeline's time or started with the clip.</summary>
    public sealed class FeedbackClip : PlayableAsset, ITimelineClipAsset
    {
        [SerializeField] internal FeedbackClipTiming m_Timing = FeedbackClipTiming.Auto;
        [SerializeField] internal FeedbackClipEnd m_End = FeedbackClipEnd.Revert;
        [SerializeField, Min(0f)] internal float m_Intensity = 1f;
        [SerializeField] internal uint m_Seed;
        [SerializeField] internal bool m_Loop;
        // the list the clip plays — none: the player's list; else the player's linked preset or one of its Variants
        [SerializeField] internal FeedbackPreset m_Preset;
        // the clip's row in its director's FeedbackTimelineOptions — clips may share one
        [SerializeField] internal string m_HitKey = "";

        // the not-scrubbable warning, once a clip
        [NonSerialized] internal bool m_Warned;

        ClipCaps ITimelineClipAsset.clipCaps
            => ClipCaps.ClipIn | ClipCaps.SpeedMultiplier | (m_Loop ? ClipCaps.Looping : ClipCaps.None) | (m_Timing == FeedbackClipTiming.Driven ? ClipCaps.Extrapolation : ClipCaps.None);

        // the Timeline editor's features read it — a new clip's length, Match Content, the loop marks; a play's length is the
        // clip's, so play mode and a player keep the base's (infinite)
        /// <summary>The length of the track's FeedbackPlayer's list while the Timeline window shows the clip in the Editor's Edit mode — a new clip, Match Content and the loop marks take it; infinite otherwise.</summary>
        public override double duration => !Application.isPlaying && TrackPreview.Hook != null ? TrackPreview.Hook.Length(this) : base.duration;

        /// <summary>Creates the clip's playable; the timeline calls it when it builds its graph.</summary>
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<FeedbackClipBehaviour> playable = ScriptPlayable<FeedbackClipBehaviour>.Create(graph);
            FeedbackClipBehaviour behaviour = playable.GetBehaviour();
            behaviour.m_Clip = this;
            behaviour.m_Owner = owner;
            return playable;
        }
    }
}
