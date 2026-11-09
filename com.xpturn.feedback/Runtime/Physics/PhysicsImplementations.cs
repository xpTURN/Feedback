using UnityEngine;

namespace xpTURN.Feedback
{
    // the runtime's and the editor's registration
    static class PhysicsImplementations
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            FeedbackImplementations.Register<RigidbodyForceItem>(item => new RigidbodyForceBinding(item));
            FeedbackImplementations.Register<RigidbodyExplosionItem>(item => new RigidbodyExplosionBinding(item));
            FeedbackImplementations.Register<RigidbodyVelocityItem>(item => new RigidbodyVelocityBinding(item));
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();
#endif
    }
}
