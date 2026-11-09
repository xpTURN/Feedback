using UnityEditor;
using UnityEngine.InputSystem;

namespace xpTURN.Feedback.Tests.Editor
{
    // the recording gamepad's layout at the editor's load — a domain reload recreates only the devices whose layout is
    // registered by then (the runtime test assembly references no editor assembly)
    static class RumbleEditorLoad
    {
        [InitializeOnLoadMethod]
        static void Register() => InputSystem.RegisterLayout<RecordingGamepad>();
    }
}
