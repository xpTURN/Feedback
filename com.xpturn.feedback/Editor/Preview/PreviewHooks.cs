using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace xpTURN.Feedback.Editor
{
    // the editor's events are subscribed once a domain — a subscription per session would move behind the others — and sent
    // to the current session: the nine events, and the step an undo or redo ended a session with, done again at the next
    // update; the saves, and the prefab instance operations that end it first. A scene opened during a session brings its
    // camera shake receivers
    [InitializeOnLoad]
    internal static class PreviewHooks
    {
        const int Undone = 1;
        const int Redone = 2;

        // the step to do again: 0 · Undone · Redone, and how many steps that way came before it ran
        static int s_Replay;
        static int s_Count;
        static bool s_InReplay;
        // the undo events seen — whether PerformRedo · PerformUndo did something
        static int s_Events;

        static PreviewHooks()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorSceneManager.sceneClosing += SceneClosing;
            EditorSceneManager.activeSceneChangedInEditMode += ActiveSceneChanged;
            PrefabStage.prefabStageClosing += StageClosing;
            Undo.undoRedoEvent += UndoRedo;
            Undo.postprocessModifications += Modifications;
            Selection.selectionChanged += SelectionChanged;
            EditorApplication.quitting += Quitting;
            EditorSceneManager.sceneSaving += SceneSaving;
            EditorSceneManager.sceneSaved += SceneSaved;
            PrefabStage.prefabSaving += PrefabSaving;
            PrefabStage.prefabSaved += PrefabSaved;
            PrefabUtility.prefabInstanceApplying += PrefabOperation;
            PrefabUtility.prefabInstanceReverting += PrefabOperation;
            PrefabUtility.prefabInstanceUnpacking += PrefabUnpacking;
            EditorSceneManager.sceneOpened += SceneOpened;
            ObjectChangeEvents.changesPublished += ChangesPublished;
        }

        // a user's reparent of an object whose place the preview keeps
        internal static void ChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (PreviewSession.Current != null)
                PreviewSlotStates.Changes(ref stream);
        }

        // a scene opened during a session — its camera shake receivers join (a script's OpenScene raises it too)
        internal static void SceneOpened(Scene scene, OpenSceneMode mode) => PreviewSession.Current?.SceneOpened(scene);

        // the step again first — the session's update must not tick over the values from before it
        static void Update()
        {
            // a track session's rebuild that never came — the window closed, the lock gone: what waited goes on now
            if (PreviewSession.TrackRebuildPending && !AnimationMode.InAnimationMode())
                PreviewSession.SettleTrackRebuild();
            ReplayPending();
            PreviewSession.Current?.Update();
        }

        // the stop put the recorded values back over what the step wrote, and inside the event the undo system does not take
        // it again — done here, with nothing recorded. An undo is redone and then undone, as many as came, and only as many as
        // the redo did: a revert records no step to redo, and an undo after it would take the user's step under it. A redo
        // the other way round. A session's start calls it too
        internal static void ReplayPending()
        {
            int replay = s_Replay;
            // a track session's: after Timeline's stop (PreviewSession.SettleTrackRebuild)
            if (replay == 0 || PreviewSession.TrackRebuildPending)
                return;
            int count = s_Count;
            s_Replay = 0;
            s_Count = 0;
            s_InReplay = true;
            try
            {
                int done = 0;
                for (int i = 0; i < count; i++)
                {
                    int events = s_Events;
                    if (replay == Undone)
                        Undo.PerformRedo();
                    else
                        Undo.PerformUndo();
                    if (s_Events == events)
                        break;
                    done++;
                }
                for (int i = 0; i < done; i++)
                {
                    if (replay == Undone)
                        Undo.PerformUndo();
                    else
                        Undo.PerformRedo();
                }
            }
            finally
            {
                s_InReplay = false;
            }
        }

        // before play mode — with both reloads off the edit-mode objects go into play as they are
        internal static void PlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
                PreviewSession.Current?.End(PreviewEndReason.PlayMode);
        }

        // missed, the recovery stops the driver by name after the reload (PreviewRecovery)
        internal static void BeforeReload() => PreviewSession.Current?.End(PreviewEndReason.Reload);

        // inside the closing call — the stop puts the values back before the scene goes. An unload comes here too
        internal static void SceneClosing(Scene scene, bool removingScene)
        {
            PreviewSession session = PreviewSession.Current;
            if (session != null && session.Touches(scene))
                session.End(PreviewEndReason.SceneClosing);
        }

        // a prefab stage's scene raises no sceneClosing
        internal static void StageClosing(PrefabStage stage)
        {
            PreviewSession session = PreviewSession.Current;
            if (session != null && session.Touches(stage.scene))
                session.End(PreviewEndReason.StageClosing);
        }

        // a session that wrote RenderSettings ends when the active scene changes — its values are the scene it took them from
        // (the end writes them there: that scene is active for the write and back — the session is gone by then). An editor
        // SetActiveScene comes here too
        internal static void ActiveSceneChanged(Scene from, Scene to)
        {
            PreviewSession session = PreviewSession.Current;
            if (session != null && PreviewRenderSettings.Touched())
                session.End(PreviewEndReason.ActiveSceneChanged);
        }

        // the event comes inside PerformUndo · PerformRedo and inside the reverts
        internal static void UndoRedo(in UndoRedoInfo info)
        {
            s_Events++;
            // the step again raises events of its own: they end nothing and ask for nothing — an overlapping guard, a pending
            // step runs before a session starts
            if (s_InReplay)
                return;
            PreviewSession session = PreviewSession.Current;
            if (session == null)
            {
                // a later step of the same update the same way goes again too — the first way only
                if (s_Replay != 0 && (s_Replay == Redone) == info.isRedo)
                    s_Count++;
                return;
            }
            session.End(PreviewEndReason.UndoRedo);
            s_Replay = info.isRedo ? Redone : Undone;
            s_Count = 1;
        }

        // read only — the list goes back as it came, other tools' order is not ours
        internal static UndoPropertyModification[] Modifications(UndoPropertyModification[] modifications)
        {
            PreviewSession.Current?.Intercept(modifications);
            return modifications;
        }

        // an update after the change
        internal static void SelectionChanged() => PreviewSession.Current?.SelectionChanged();

        internal static void Quitting() => PreviewSession.Current?.End(PreviewEndReason.Quit);

        // a save goes on — what AnimationMode cannot record goes back before it and on again after
        internal static void SceneSaving(Scene scene, string path)
        {
            if (PreviewSession.Current != null)
                PreviewSaveState.Saving(scene);
        }

        internal static void SceneSaved(Scene scene)
        {
            if (PreviewSession.Current != null)
                PreviewSaveState.Saved(scene);
        }

        // the stage's prefab root — its scene is the stage's
        internal static void PrefabSaving(GameObject root)
        {
            if (PreviewSession.Current != null && root != null)
                PreviewSaveState.Saving(root.scene);
        }

        internal static void PrefabSaved(GameObject root)
        {
            if (PreviewSession.Current != null && root != null)
                PreviewSaveState.Saved(root.scene);
        }

        // an instance applied, reverted or unpacked under the preview breaks the stop's revert — the preview's values stay.
        // Before it acts, the session of its scene ends and the operation meets the real values. A single property's apply or
        // revert the engine refuses in the animation mode
        internal static void PrefabOperation(GameObject root)
        {
            PreviewSession session = PreviewSession.Current;
            if (session != null && root != null && session.Touches(root.scene))
                session.End(PreviewEndReason.PrefabOperation);
        }

        internal static void PrefabUnpacking(GameObject root, PrefabUnpackMode mode) => PrefabOperation(root);
    }
}
