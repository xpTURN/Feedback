using UnityEditor;

namespace xpTURN.Feedback.Editor
{
    // a product global state — the preview's Cinemachine time through Core's hooks (the Cinemachine assembly fills them; none
    // without the package — nothing to capture). Registered at the editor's load
    internal sealed class CinemachineTimeState : IPreviewGlobalState
    {
        internal static readonly CinemachineTimeState Instance = new CinemachineTimeState();

        public string Name => "Cinemachine time";

        public void Capture() => CameraHooks.TimeCapture?.Invoke();

        public void Restore() => CameraHooks.TimeRestore?.Invoke();

        public string Describe() => CameraHooks.TimeDescribe != null ? CameraHooks.TimeDescribe() : "none";

        [InitializeOnLoadMethod]
        static void Register() => PreviewGlobalState.Register(Instance);
    }
}
