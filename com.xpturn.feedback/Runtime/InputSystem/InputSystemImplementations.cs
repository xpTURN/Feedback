using UnityEngine;

namespace xpTURN.Feedback
{
    // the InputSystem assembly's registration — the rumble item and its extension service. The runtime at
    // SubsystemRegistration (every play entry), the editor at its load (the preview prepares them)
    static class InputSystemImplementations
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            FeedbackImplementations.Register<GamepadRumbleItem>(item => new GamepadRumbleBinding(item));
            FeedbackContext.RegisterExtension(GamepadRumbleService.Key, context => new GamepadRumbleService(context));
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void RegisterInEditor() => Register();
#endif
    }
}
