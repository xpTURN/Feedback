using UnityEngine.Playables;

namespace xpTURN.Feedback
{
    // the editor's side of a Feedback track in the Timeline window's preview — set by the editor assembly at each load, null in
    // a player. The runtime track and clips reach the preview session only through it
    internal interface ITrackPreview
    {
        // Timeline gathers the track's properties with its preview's lock held: the player joins that lock's session
        void Gather(PlayableDirector director, FeedbackPlayer player);

        // the player is bound to the open track session — else a clip writes nothing in edit mode
        bool Owns(FeedbackPlayer player);

        // the session's refusal for a driven play — a simulated target refuses
        SeekRefusal Scrub(FeedbackPlayer player, PlaybackHandle handle);

        // the session lets a driven play of the player scrub — asked before the play: a simulated target refuses
        bool Drives(FeedbackPlayer player);

        // after a clip's seek: the session's zero tick shows the values in that evaluation
        void Seeked();

        // a clip's graph goes, its play reverted: the session takes the renderer blocks again before Timeline's stop
        void GraphDestroyed();

        // a clip asset's length in edit mode — the list's of the player bound to its track in the Timeline window's timeline,
        // infinite when the window does not show the clip or the list is endless
        double Length(FeedbackClip clip);
    }

    internal static class TrackPreview
    {
        internal static ITrackPreview Hook;
    }
}
