using System;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // the game's Update and LateUpdate — an action at a frame's Update or LateUpdate
    public sealed class MotionDriverFrames : MonoBehaviour
    {
        internal static Action<int> OnUpdate;
        internal static Action<int> OnLateUpdate;

        void Update() => OnUpdate?.Invoke(Time.frameCount);

        void LateUpdate() => OnLateUpdate?.Invoke(Time.frameCount);
    }
}
