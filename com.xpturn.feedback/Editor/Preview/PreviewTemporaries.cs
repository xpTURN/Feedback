using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // the preview's temporary objects — a seam no item uses (the pools and the overlay volumes and canvases are their
    // services' own, named and hidden the same way and destroyed at the service's close). Named with the recovery's prefix
    // and HideAndDontSave: they belong to no scene, a save takes neither them nor their children, and making or destroying
    // one leaves the scene clean. Made while a session lives — inside its end the session is gone and the answer is null —
    // and destroyed at the end, the last made first; a reload whose hook was missed leaves them to the recovery's prefix.
    // GameObjects only: the recovery walks GameObjects
    internal static class PreviewTemporaries
    {
        static readonly List<GameObject> s_Made = new List<GameObject>();

        internal static int Count => s_Made.Count;

        // null outside a session
        internal static GameObject Create(string name, params Type[] components)
        {
            if (PreviewSession.Current == null)
                return null;
            GameObject go = EditorUtility.CreateGameObjectWithHideFlags(PreviewRecovery.TemporaryPrefix + name, HideFlags.HideAndDontSave, components);
            s_Made.Add(go);
            return go;
        }

        // the end's step 4 — with their children
        internal static void DestroyAll()
        {
            for (int i = s_Made.Count - 1; i >= 0; i--)
            {
                if (s_Made[i] != null)
                    Object.DestroyImmediate(s_Made[i]);
            }
            s_Made.Clear();
        }
    }
}
