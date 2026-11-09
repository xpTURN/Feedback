using UnityEngine;

namespace xpTURN.Feedback
{
    // a Collider's enabled for Core's Component Enabled — the check, the read and the write (RigidbodyLayerCheck's
    // registrations: a new session · the editor's load). Lambdas without captures — no allocation a call
    internal static class ColliderHooks
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Register()
        {
            LayerHooks.ColliderCheck = c => c is Collider;
            LayerHooks.ColliderEnabledRead = c => ((Collider)c).enabled;
            LayerHooks.ColliderEnabledWrite = (c, value) => ((Collider)c).enabled = value;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();
#endif
    }
}
