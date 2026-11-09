using System;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // the frame's camera after the brain's LateUpdate (its order 100)
    [DefaultExecutionOrder(32000)]
    public sealed class CinemachineLateRecorder : MonoBehaviour
    {
        public Action<int> OnLate;

        void LateUpdate() => OnLate?.Invoke(Time.frameCount);
    }
}
