using UnityEngine;

namespace xpTURN.Feedback
{
    // the Video assembly's registration
    static class VideoImplementations
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            FeedbackImplementations.Register<VideoPlayerControlItem>(item => new VideoPlayerControlBinding(item));
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();
#endif
    }
}
