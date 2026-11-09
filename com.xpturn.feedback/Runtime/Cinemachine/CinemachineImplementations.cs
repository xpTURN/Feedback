using UnityEngine;

namespace xpTURN.Feedback
{
    // the Cinemachine assembly's registration — the items, the extension service and Core's camera hooks. The runtime
    // at SubsystemRegistration (every play entry — the same again), the editor at its load (the preview prepares them).
    // Lambdas without captures
    static class CinemachineImplementations
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            FeedbackImplementations.Register<CinemachineImpulseItem>(item => new CinemachineImpulseBinding(item));
            FeedbackImplementations.Register<CinemachineImpulseClearItem>(item => new CinemachineImpulseClearBinding(item));
            FeedbackImplementations.Register<CinemachineLensFieldOfViewItem>(item => new CinemachineLensFieldOfViewBinding(item));
            FeedbackImplementations.Register<CinemachineLensOrthographicSizeItem>(item => new CinemachineLensOrthographicSizeBinding(item));
            FeedbackImplementations.Register<CinemachineLensDutchItem>(item => new CinemachineLensDutchBinding(item));
            FeedbackImplementations.Register<CinemachineNoiseAmplitudeGainItem>(item => new CinemachineNoiseAmplitudeGainBinding(item));
            FeedbackImplementations.Register<CinemachineNoiseFrequencyGainItem>(item => new CinemachineNoiseFrequencyGainBinding(item));
            FeedbackImplementations.Register<CinemachineActivateCameraItem>(item => new CinemachineActivateCameraBinding(item));
            FeedbackContext.RegisterExtension(CinemachineFeedbackService.Key, context => new CinemachineFeedbackService(context));
            CameraHooks.ShakeSlots = CinemachineShakeHooks.Slots;
            CameraHooks.MainCameraTarget = CinemachineTargets.MainCameraTarget;
            CameraHooks.TimeCapture = CinemachinePreviewTime.Capture;
            CameraHooks.TimeRestore = CinemachinePreviewTime.Restore;
            CameraHooks.TimeDescribe = CinemachinePreviewTime.Describe;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();
#endif
    }
}
