using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    internal static class VideoNames
    {
        internal const string Player = "UnityEngine.Video.VideoPlayer, UnityEngine.VideoModule";
        internal const string Assembly = "xpTURN.Feedback.Video";
    }

    // saved — add members at the end only
    public enum VideoAction
    {
        Play = 0,
        Pause = 1,
        Seek = 2,
    }

    // a Video Player's play state (Play · Pause — Particle Control's form) and a seek (not undone). No preview — the binding
    // skips in a preview context
    [FeedbackTarget("Video/Player Control", PreviewGrade.None, Aliases = "movie play pause seek", RequiresName = VideoNames.Player, Implementation = VideoNames.Assembly)]
    [Serializable]
    public sealed class VideoPlayerControlItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Bool, supportedMotions: MotionMask.Set,
            category: FeedbackCategory.None, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal VideoAction m_Action;
        [SerializeField, Min(0f)] internal float m_Time;

        internal override TargetTraits Traits => s_Traits;
    }
}
