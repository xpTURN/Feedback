namespace xpTURN.Feedback.Editor
{
    // the words of the bar's status line — an end, a refused start, a refused scrub, a refused replay
    internal static class PreviewTexts
    {
        internal const string ReplayRefused = "The player's conditions refused the replay — turn on Unconditional.";
        internal const string TrackHeld = "This player plays in the Timeline window's preview — its Preview waits until that preview ends.";

        internal static string Of(PreviewEndReason reason)
        {
            switch (reason)
            {
                case PreviewEndReason.Stopped: return "Stopped.";
                case PreviewEndReason.Replaced: return "Another preview started.";
                case PreviewEndReason.AutoEnd: return "Ended — nothing plays.";
                case PreviewEndReason.LockLost: return "Ended — another tool took AnimationMode.";
                case PreviewEndReason.SubjectDestroyed: return "Ended — the subject was destroyed.";
                case PreviewEndReason.PrepareFailed: return "Ended — the subject could not be prepared.";
                case PreviewEndReason.PlayMode: return "Ended for play mode.";
                case PreviewEndReason.Reload: return "Ended for a script reload.";
                case PreviewEndReason.SceneClosing: return "Ended — a scene it touched closed.";
                case PreviewEndReason.StageClosing: return "Ended — a prefab stage it touched closed.";
                case PreviewEndReason.UndoRedo: return "Ended for Undo / Redo — press ▶ again.";
                case PreviewEndReason.UserEdit: return "Ended — a previewed property was edited; the edit stays.";
                case PreviewEndReason.Deselected: return "Ended — the subject left the selection.";
                case PreviewEndReason.Quit: return "Ended — the editor quits.";
                case PreviewEndReason.PrefabOperation: return "Ended before a prefab apply, revert or unpack.";
                case PreviewEndReason.ActiveSceneChanged: return "Ended — the active scene changed (fog, skybox and ambient light are its own).";
                default: return "";
            }
        }

        internal static string Of(PreviewRefusal refusal)
        {
            switch (refusal)
            {
                case PreviewRefusal.Playing: return "No preview in play mode.";
                case PreviewRefusal.Busy: return "No preview while compiling or importing.";
                case PreviewRefusal.Asset: return "Open the prefab to preview it.";
                case PreviewRefusal.NotInScene: return "The subject is not in an open scene or prefab stage.";
                case PreviewRefusal.Locked: return "Timeline or the Animation window holds AnimationMode — close its preview first.";
                case PreviewRefusal.PrepareFailed: return "The subject could not be prepared.";
                default: return "";
            }
        }

        internal static string Of(SeekRefusal refusal)
        {
            switch (refusal)
            {
                case SeekRefusal.NotLive: return "Nothing plays to scrub.";
                case SeekRefusal.UntimedWait: return "A signal wait without a timeout or a wait on another player — no scrub.";
                case SeekRefusal.Endless: return "An endless list — no scrub.";
                case SeekRefusal.NotSeekable: return "This play cannot seek.";
                case SeekRefusal.OffNominal: return "A hit stop or an early signal moved this play off its timeline — scrub comes back with the next play.";
                case SeekRefusal.Simulated: return "A simulated target (particles · animators) plays on the editor's clock — no scrub.";
                default: return "";
            }
        }
    }
}
