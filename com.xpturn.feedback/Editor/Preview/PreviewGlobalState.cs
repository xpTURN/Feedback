using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // state outside serialization that a preview writes — the Cinemachine time override, shader globals. A user registers
    // once at load (the registry is the domain's — a reload empties it)
    internal interface IPreviewGlobalState
    {
        // for the global state check's message
        string Name { get; }

        void Capture();

        void Restore();

        // the value now as text — GlobalStateCheck compares it before and after, apart from the capture
        string Describe();
    }

    // Every registered state is captured at each start — after the driver, before the bind: nothing is written yet — and
    // restored at the end after the stop, the other way round, only what was captured. A throwing state is logged and the
    // others go on, the end to its end. A reload with its hook ends the session before it; one whose hook was missed leaves
    // a native value as the preview's — the capture is gone with the domain
    internal static class PreviewGlobalState
    {
        static readonly List<IPreviewGlobalState> s_Registered = new List<IPreviewGlobalState>();
        static readonly List<IPreviewGlobalState> s_Captured = new List<IPreviewGlobalState>();

        internal static IReadOnlyList<IPreviewGlobalState> Registered => s_Registered;

        internal static void Register(IPreviewGlobalState state)
        {
            if (state != null && !s_Registered.Contains(state))
                s_Registered.Add(state);
        }

        internal static void Unregister(IPreviewGlobalState state) => s_Registered.Remove(state);

        internal static void CaptureAll()
        {
            s_Captured.Clear();
            for (int i = 0; i < s_Registered.Count; i++)
            {
                IPreviewGlobalState state = s_Registered[i];
                try
                {
                    state.Capture();
                    s_Captured.Add(state);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        internal static void RestoreAll()
        {
            for (int i = s_Captured.Count - 1; i >= 0; i--)
            {
                try
                {
                    s_Captured[i].Restore();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            s_Captured.Clear();
        }
    }
}
