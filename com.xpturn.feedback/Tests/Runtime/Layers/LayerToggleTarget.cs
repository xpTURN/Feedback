using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // the toggle's test target — OnEnable · OnDisable counted in edit mode too ([ExecuteAlways]), the frame of the first
    // LateUpdate after it was turned on, the LateUpdate frames, the light intensity LateUpdate saw, and hooks for the
    // callbacks. No UnityEditor here
    [ExecuteAlways]
    public sealed class LayerToggleTarget : MonoBehaviour
    {
        public int Enables;
        public int Disables;
        public int EnabledFrame = -1;
        public int DisabledFrame = -1;
        public int FirstLateAfterEnable = -1;
        public Light Light;
        public readonly List<int> LateFrames = new List<int>();
        public readonly List<(int Frame, float Intensity)> Seen = new List<(int, float)>();
        public Action<LayerToggleTarget> OnEnableHook;
        public Action<LayerToggleTarget> OnDisableHook;

        void OnEnable()
        {
            Enables++;
            EnabledFrame = Time.frameCount;
            FirstLateAfterEnable = -1;
            OnEnableHook?.Invoke(this);
        }

        void OnDisable()
        {
            Disables++;
            DisabledFrame = Time.frameCount;
            OnDisableHook?.Invoke(this);
        }

        void LateUpdate()
        {
            if (!Application.isPlaying)
                return;
            LateFrames.Add(Time.frameCount);
            if (FirstLateAfterEnable < 0)
                FirstLateAfterEnable = Time.frameCount;
            if (Light != null)
                Seen.Add((Time.frameCount, Light.intensity));
        }
    }
}
