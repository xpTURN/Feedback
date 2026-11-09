using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // the game's Update · LateUpdate marks — an action can be put at a frame's Update
    public sealed class CompletionFrameMarks : MonoBehaviour
    {
        internal static readonly List<string> Log = new List<string>();
        internal static Action<int> OnUpdate;

        void Update()
        {
            int f = Time.frameCount;
            OnUpdate?.Invoke(f);
            Log.Add("U" + f + "<");
        }

        void LateUpdate() => Log.Add("L" + Time.frameCount + ">");
    }
}
