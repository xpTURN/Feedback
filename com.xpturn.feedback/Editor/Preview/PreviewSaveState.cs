using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace xpTURN.Feedback.Editor
{
    // serialized state that a preview writes and AnimationMode cannot record — RenderSettings: the scene's, a setter leaves
    // the scene clean but a save writes it. Revert keeps the value it replaces and writes the one from before the preview;
    // Reapply writes the kept one. Both go the preview's way — a setter or SerializedObject without Undo: an Undo-recorded
    // write leaves the scene dirty after the save and a step the user's next Undo takes, ending the preview
    internal interface IPreviewSaveState
    {
        // the scene the state is saved with — a prefab stage's own scene for a prefab
        Scene Scene { get; }

        void Revert();

        void Reapply();
    }

    // a state a save of more than one scene touches — a parent's, when the object
    // went to another scene's parent
    internal interface IPreviewSaveStateScenes
    {
        bool Covers(Scene scene);
    }

    // Added while a session lives, before the first write. A save of the state's scene or prefab stage goes on: the state goes
    // back before it and on again after — two scenes in one save raise their savings first and their saveds after, the scene
    // pairs them. The end reverts them all, the last added first — the stop does not. A state's scene closing ends the
    // session (PreviewSession.Touches). Ours never throw: a throwing handler before ours stops the invocation and that save
    // writes the preview's value
    internal static class PreviewSaveState
    {
        static readonly List<IPreviewSaveState> s_States = new List<IPreviewSaveState>();
        static readonly List<IPreviewSaveState> s_Reverted = new List<IPreviewSaveState>();

        internal static int Count => s_States.Count;

        internal static IPreviewSaveState At(int index) => s_States[index];

        static bool Matches(IPreviewSaveState state, Scene scene) => state is IPreviewSaveStateScenes scenes ? scenes.Covers(scene) : state.Scene == scene;

        // false outside a session and for a state already added
        internal static bool Add(IPreviewSaveState state)
        {
            if (PreviewSession.Current == null || state == null || s_States.Contains(state))
                return false;
            s_States.Add(state);
            return true;
        }

        // a state whose object the user moved — it leaves without its revert
        internal static void Remove(IPreviewSaveState state)
        {
            s_States.Remove(state);
            s_Reverted.Remove(state);
        }

        internal static bool Touches(Scene scene)
        {
            for (int i = 0; i < s_States.Count; i++)
            {
                if (Matches(s_States[i], scene))
                    return true;
            }
            return false;
        }

        // sceneSaving · prefabSaving: that scene's states, the last added first
        internal static void Saving(Scene scene)
        {
            for (int i = s_States.Count - 1; i >= 0; i--)
            {
                IPreviewSaveState state = s_States[i];
                if (!Matches(state, scene) || s_Reverted.Contains(state))
                    continue;
                try
                {
                    state.Revert();
                    s_Reverted.Add(state);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        // sceneSaved · prefabSaved: what that save reverted, on again in the order added
        internal static void Saved(Scene scene)
        {
            for (int i = s_Reverted.Count - 1; i >= 0; i--)
            {
                IPreviewSaveState state = s_Reverted[i];
                if (!Matches(state, scene))
                    continue;
                s_Reverted.RemoveAt(i);
                try
                {
                    state.Reapply();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        // the end's step 4
        internal static void RevertAll()
        {
            for (int i = s_States.Count - 1; i >= 0; i--)
            {
                try
                {
                    s_States[i].Revert();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            s_States.Clear();
            s_Reverted.Clear();
        }
    }
}
