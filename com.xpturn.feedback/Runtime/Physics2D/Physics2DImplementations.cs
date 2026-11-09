using UnityEngine;

namespace xpTURN.Feedback
{
    // the runtime's and the editor's registration
    static class Physics2DImplementations
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            FeedbackImplementations.Register<Rigidbody2DForceItem>(item => new Rigidbody2DForceBinding(item));
            FeedbackImplementations.Register<Rigidbody2DVelocityItem>(item => new Rigidbody2DVelocityBinding(item));
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();
#endif
    }
}
