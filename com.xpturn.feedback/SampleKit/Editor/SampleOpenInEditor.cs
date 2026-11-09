using UnityEditor;
using UnityEditor.SceneManagement;

namespace xpTURN.Feedback.Samples.Editor
{
    // after the hub's 'Open in Editor' ends the play, the next update asks to save a dirty scene, opens the card's scene and
    // selects the card (the inspector shows its steps). A refused save opens nothing — OpenScene would drop the changes
    // without asking
    [InitializeOnLoad]
    public static class SampleOpenInEditor
    {
        static SampleOpenInEditor() => EditorApplication.playModeStateChanged += Changed;

        static void Changed(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(SessionState.GetString(SampleKitPaths.OpenInEditorKey, "")))
                EditorApplication.update += Next;
        }

        static void Next()
        {
            EditorApplication.update -= Next;
            OpenPending();
        }

        internal static bool OpenPending()
        {
            string pending = SessionState.GetString(SampleKitPaths.OpenInEditorKey, "");
            SessionState.EraseString(SampleKitPaths.OpenInEditorKey);
            int bar = pending.IndexOf('|');
            if (bar < 0 || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;
            EditorSceneManager.OpenScene(pending.Substring(0, bar), OpenSceneMode.Single);
            var card = AssetDatabase.LoadAssetAtPath<SampleCard>(AssetDatabase.GUIDToAssetPath(pending.Substring(bar + 1)));
            if (card != null)
            {
                Selection.activeObject = card;
                EditorGUIUtility.PingObject(card);
            }
            return true;
        }
    }
}
