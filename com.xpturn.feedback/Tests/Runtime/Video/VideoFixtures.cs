using UnityEngine;
using UnityEngine.Video;

namespace xpTURN.Feedback.Tests
{
    // the Video item's fixture — a player with no clip (a play state is still read and written)
    static class VideoFixtures
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register() => ModuleFixtures.Add("video", Leaf, new[] { typeof(VideoPlayerControlItem) }, null);

        static void Leaf(GameObject leaf)
        {
            var player = leaf.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.renderMode = VideoRenderMode.APIOnly;
        }
    }
}
