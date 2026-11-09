using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Editor
{
    // the AnimationMode lock is first come and a start under someone else's lock does nothing and says nothing — recorded
    // there, our values would go back with their stop. Without HideAndDontSave a scene change destroys the driver, and a
    // destroyed one reads as in the mode while nobody holds the lock: alive is "not destroyed and in the mode", polled each
    // update (the state events are internal)
    internal sealed class PreviewDriver
    {
        // the recovery after a reload finds it by name
        internal const string Name = "xpTURN.Feedback.Preview.Driver";

        internal readonly AnimationModeDriver Instance;

        PreviewDriver(AnimationModeDriver instance) => Instance = instance;

        // null while someone else holds the lock — nothing recorded, nothing written
        internal static PreviewDriver Start()
        {
            var instance = ScriptableObject.CreateInstance<AnimationModeDriver>();
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.name = Name;
            AnimationMode.StartAnimationMode(instance);
            if (AnimationMode.InAnimationMode(instance))
                return new PreviewDriver(instance);
            Object.DestroyImmediate(instance);
            return null;
        }

        internal bool IsAlive => Instance != null && AnimationMode.InAnimationMode(Instance);

        // stop puts the recorded values back (last). A lost lock is not stopped: a destroyed driver was reverted by the
        // engine, and someone else's stop would end their session
        internal void Stop()
        {
            if (IsAlive)
                AnimationMode.StopAnimationMode(Instance);
        }

        // after what AnimationMode does not keep is put back
        internal void Destroy()
        {
            if (Instance != null)
                Object.DestroyImmediate(Instance);
        }
    }
}
