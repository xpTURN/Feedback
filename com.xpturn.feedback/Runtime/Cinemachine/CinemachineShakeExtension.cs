using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the user's extension on a Cinemachine camera — a camera shake on a camera its brain drives goes into the pipeline
    // here (the brain would write the camera over a transform offset in the preview). At Finalize — over the noise and
    // the other extensions — the offsets of the brain it is live in, the position on the camera's own axes. No fields;
    // the deltaTime is not read (the edit mode passes −1)
    /// <summary>Add it to a Cinemachine camera so camera shakes reach that camera, in play and in the edit-mode preview.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("xpTURN/Feedback/Cinemachine Shake Extension")]
    public sealed class CinemachineShakeExtension : CinemachineExtension
    {
        static readonly List<CinemachineBrain> s_Brains = new List<CinemachineBrain>();
        static readonly List<Vector3> s_Positions = new List<Vector3>();
        static readonly List<Quaternion> s_Rotations = new List<Quaternion>();

        static int IndexOf(CinemachineBrain brain)
        {
            for (int i = 0; i < s_Brains.Count; i++)
            {
                if (s_Brains[i] == brain)
                    return i;
            }
            return -1;
        }

        static int Entry(CinemachineBrain brain)
        {
            int i = IndexOf(brain);
            if (i >= 0)
                return i;
            s_Brains.Add(brain);
            s_Positions.Add(Vector3.zero);
            s_Rotations.Add(Quaternion.identity);
            return s_Brains.Count - 1;
        }

        internal static Vector3 PositionOf(CinemachineBrain brain)
        {
            int i = IndexOf(brain);
            return i >= 0 ? s_Positions[i] : Vector3.zero;
        }

        internal static Quaternion RotationOf(CinemachineBrain brain)
        {
            int i = IndexOf(brain);
            return i >= 0 ? s_Rotations[i] : Quaternion.identity;
        }

        internal static void SetPosition(CinemachineBrain brain, Vector3 value) => s_Positions[Entry(brain)] = value;

        internal static void SetRotation(CinemachineBrain brain, Quaternion value) => s_Rotations[Entry(brain)] = value;

        protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Finalize)
                return;
            for (int i = 0; i < s_Brains.Count; i++)
            {
                CinemachineBrain brain = s_Brains[i];
                if (brain == null || !brain.IsLiveChild(vcam))
                    continue;
                state.PositionCorrection += state.GetFinalOrientation() * s_Positions[i];
                state.OrientationCorrection = state.OrientationCorrection * s_Rotations[i];
                return;
            }
        }
    }
}
