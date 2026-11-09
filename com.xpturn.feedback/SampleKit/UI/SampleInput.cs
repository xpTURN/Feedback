using UnityEngine;
using UnityEngine.InputSystem;

namespace xpTURN.Feedback.Samples
{
    // the samples' actions — the Common asset (a sample finds an action by name once and holds it) — and whether the
    // pointer is over UI this frame (measured in Update, outside the input callbacks)
    public static class SampleInput
    {
        public static InputActionAsset Asset { get; private set; }
        public static bool PointerOverUI { get; internal set; }

        public static InputAction Find(string name) => Asset != null ? Asset.FindAction("Sample/" + name) : null;

        internal static void Use(InputActionAsset asset)
        {
            if (asset == null || asset == Asset)
                return;
            Asset = asset;
            asset.FindActionMap("Sample", true).Enable();
        }

        internal static void Release()
        {
            if (Asset != null)
                Asset.FindActionMap("Sample", true).Disable();
            Asset = null;
            PointerOverUI = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Asset = null;
            PointerOverUI = false;
        }
    }
}
