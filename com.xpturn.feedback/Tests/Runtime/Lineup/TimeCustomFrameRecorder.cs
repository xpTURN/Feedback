using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // each frame's game Update — the frame, the time scale and the scaled dt it sees; an action at a frame
    public sealed class TimeCustomFrameRecorder : MonoBehaviour
    {
        [NonSerialized] public readonly List<(int Frame, float Scale, float Delta)> Rows = new List<(int, float, float)>();
        [NonSerialized] public int ActAt = -1;
        [NonSerialized] public Action Act;

        void Update()
        {
            Rows.Add((Time.frameCount, Time.timeScale, Time.deltaTime));
            if (Time.frameCount == ActAt)
                Act?.Invoke();
        }
    }
}
