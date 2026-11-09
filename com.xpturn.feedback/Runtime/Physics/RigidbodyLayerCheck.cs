using UnityEngine;

namespace xpTURN.Feedback
{
    // the rigidbody check the registry calls once per transform in development builds. Warnings per transform; the cost
    // notices once per session
    internal static class RigidbodyLayerCheck
    {
        internal const string InterpolatedWarning = "Feedback layers write '{0}', whose Rigidbody interpolates: the revert reaches physics as a teleport, and other bodies in the scene drift with it. Move the visuals to a child pivot and shake that.";
        internal const string DirectWarning = "A Feedback direct write shakes '{0}', which has a Rigidbody: it is written over the start snapshot each frame, so physics cannot move it during the effect and its velocity piles up. Shake a child pivot.";
        internal const string CompoundWarning = "Feedback shakes '{0}', a collider of the Rigidbody '{1}': moving it changes that body's physics even when reverted. Shake a child without a collider.";
        internal const string BodyInfo = "Feedback layers write '{0}', which has a Rigidbody: physics resyncs it each step (its path stays the same). Further bodies are not listed.";
        internal const string ColliderInfo = "Feedback layers write '{0}', which has a static Collider: physics resyncs it each step and a body resting on it drifts slightly. Further colliders are not listed.";

        static bool s_BodyInfo;
        static bool s_ColliderInfo;

        // a new session — the notices once more
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Register()
        {
            s_BodyInfo = false;
            s_ColliderInfo = false;
            LayerHooks.TransformSlotCreated = Check;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => LayerHooks.TransformSlotCreated = Check;
#endif

        static void Check(Transform target, bool direct)
        {
            var body = target.GetComponent<Rigidbody>();
            if (body != null)
            {
                if (direct)
                    Debug.LogWarning(string.Format(DirectWarning, target.name), target);
                else if (body.interpolation != RigidbodyInterpolation.None)
                    Debug.LogWarning(string.Format(InterpolatedWarning, target.name), target);
                else if (!s_BodyInfo)
                {
                    s_BodyInfo = true;
                    Debug.Log(string.Format(BodyInfo, target.name), target);
                }
                return;
            }
            var collider = target.GetComponent<Collider>();
            if (collider == null)
                return;
            Rigidbody owner = collider.attachedRigidbody;
            if (owner != null)
                Debug.LogWarning(string.Format(CompoundWarning, target.name, owner.name), target);
            else if (!s_ColliderInfo)
            {
                s_ColliderInfo = true;
                Debug.Log(string.Format(ColliderInfo, target.name), target);
            }
        }
    }
}
