using UnityEngine;

namespace xpTURN.Feedback.Samples
{
    // in every sample scene: its card (the HUD shows its controls) — a scene that starts puts the samples' state back first
    public sealed class SampleSceneMarker : MonoBehaviour
    {
        public SampleCard Card;
        public static SampleSceneMarker Current { get; private set; }

        void Awake()
        {
            SampleState.Restore();
            Current = this;
        }

        void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }
    }
}
