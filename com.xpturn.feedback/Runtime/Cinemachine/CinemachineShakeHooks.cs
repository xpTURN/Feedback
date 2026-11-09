using Unity.Cinemachine;
using UnityEngine;

namespace xpTURN.Feedback
{
    // Core's camera shake asks here — a camera its brain drives with a shake extension on the live camera (the queue's top
    // before the brain's first update) takes the brain's extension slots
    static class CinemachineShakeHooks
    {
        internal static bool Slots(Transform camera, PropertyLayerRegistry layers, bool direct, bool rotation,
            out ValueSlot<Vector3, Vector3Ops> position, out ValueSlot<Quaternion, QuaternionOps> turn)
        {
            position = null;
            turn = null;
            if (!camera.TryGetComponent(out Camera cam) || !camera.TryGetComponent(out CinemachineBrain brain))
                return false;
            CinemachineVirtualCameraBase live = CinemachineTargets.Live(cam);
            if (live == null || !live.TryGetComponent(out CinemachineShakeExtension extension) || !extension.isActiveAndEnabled)
                return false;
            position = direct ? layers.CreatePrivate(brain, 0, 0, () => new CinemachineShakePositionSlot(brain)) : layers.GetOrCreate(brain, 0, 0, () => new CinemachineShakePositionSlot(brain));
            if (rotation)
                turn = direct ? layers.CreatePrivate(brain, 0, 0, () => new CinemachineShakeRotationSlot(brain)) : layers.GetOrCreate(brain, 0, 0, () => new CinemachineShakeRotationSlot(brain));
            return true;
        }
    }
}
