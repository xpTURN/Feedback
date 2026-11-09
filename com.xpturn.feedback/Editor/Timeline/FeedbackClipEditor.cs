using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine.Timeline;

namespace xpTURN.Feedback.Editor
{
    // a Feedback clip in the Timeline window — a new clip takes its player's name (its length is the asset's: the list's), a
    // duplicate or a paste draws a new seed; the reasons it does not play as set, its explanation and the list's bars come
    // from its player's info
    [CustomTimelineEditor(typeof(FeedbackClip))]
    internal sealed class FeedbackClipEditor : ClipEditor
    {
        static readonly List<TimelinePlayerInfo.StripRect> s_Rects = new List<TimelinePlayerInfo.StripRect>();

        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            if (!(clip.asset is FeedbackClip asset))
                return;
            if (clonedFrom != null)
            {
                asset.m_Seed = FeedbackTrack.NextSeed();
                return;
            }
            FeedbackPlayer player = TimelinePlayerInfo.Bound(track);
            if (player != null)
                clip.displayName = player.name;
        }

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            if (string.IsNullOrEmpty(options.errorText))
            {
                TimelinePlayerInfo.Texts(clip, out string error, out string tooltip);
                options.errorText = error;
                options.tooltip = tooltip;
            }
            return options;
        }

        public override void DrawBackground(TimelineClip clip, ClipBackgroundRegion region)
        {
            if (!TimelinePlayerInfo.Strip(clip, region.position, region.startTime, region.endTime, s_Rects))
                return;
            foreach (TimelinePlayerInfo.StripRect r in s_Rects)
                EditorGUI.DrawRect(r.Rect, r.Color);
        }
    }
}
