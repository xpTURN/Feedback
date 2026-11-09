using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the Cinemachine optional assembly fills these — Core does not know a brain. Core never clears them: the order inside
    // SubsystemRegistration is not set
    internal static class CameraHooks
    {
        // a camera shake's slots on a camera Cinemachine drives with a shake extension on its live camera — false: the
        // transform's own slots
        internal delegate bool ShakeSlotsHook(Transform camera, PropertyLayerRegistry layers, bool direct, bool rotation,
            out ValueSlot<Vector3, Vector3Ops> position, out ValueSlot<Quaternion, QuaternionOps> turn);

        internal static ShakeSlotsHook ShakeSlots;

        // the main camera's target of a type the camera does not have — its brain's live camera's
        internal static Func<Camera, Type, Component> MainCameraTarget;

        // the preview's Cinemachine time — the editor's global state wraps these
        internal static Action TimeCapture;
        internal static Action TimeRestore;
        internal static Func<string> TimeDescribe;
    }
}
