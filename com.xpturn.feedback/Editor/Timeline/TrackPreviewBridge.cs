using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine.Playables;

namespace xpTURN.Feedback.Editor
{
    // the Timeline window's preview for Feedback tracks — the hook the runtime track and clips call, the track session behind
    // it, and the rebuild a session asks of the window when it ends on our side under Timeline's lock. Set at each load
    [InitializeOnLoad]
    internal sealed class TrackPreviewBridge : ITrackPreview
    {
        static TrackPreviewBridge()
        {
            TrackPreview.Hook = new TrackPreviewBridge();
            PreviewSession.TrackRebuild = Rebuild;
        }

        static void Rebuild() => TimelineEditor.Refresh(RefreshReason.ContentsModified);

        public void Gather(PlayableDirector director, FeedbackPlayer player) => PreviewSession.GatherTrack(director, player);

        public bool Owns(FeedbackPlayer player) => PreviewSession.Current != null && PreviewSession.Current.HoldsTrackPlayer(player);

        public SeekRefusal Scrub(FeedbackPlayer player, PlaybackHandle handle)
            => PreviewSession.Current != null && PreviewSession.Current.IsTrack ? PreviewSession.Current.TrackScrub(player, handle) : SeekRefusal.NotLive;

        public bool Drives(FeedbackPlayer player) => PreviewSession.Current != null && PreviewSession.Current.IsTrack && PreviewSession.Current.TrackDrives(player);

        public void Seeked() => PreviewSession.Current?.TrackSeeked();

        public void GraphDestroyed() => PreviewSession.Current?.TrackGraphDestroyed();

        public double Length(FeedbackClip clip) => TimelinePlayerInfo.Length(clip);
    }
}
