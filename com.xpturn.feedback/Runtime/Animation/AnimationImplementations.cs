using UnityEngine;

namespace xpTURN.Feedback
{
    // the runtime's and the editor's registration
    static class AnimationImplementations
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            FeedbackImplementations.Register<AnimatorParameterItem>(item => new AnimatorParameterBinding(item));
            FeedbackImplementations.Register<AnimatorFloatItem>(item => new AnimatorFloatBinding(item));
            FeedbackImplementations.Register<AnimatorSpeedItem>(item => new AnimatorSpeedBinding(item));
            FeedbackImplementations.Register<AnimatorLayerWeightItem>(item => new AnimatorLayerWeightBinding(item));
            FeedbackImplementations.Register<AnimatorStateItem>(item => new AnimatorStateBinding(item));
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();
#endif
    }
}
