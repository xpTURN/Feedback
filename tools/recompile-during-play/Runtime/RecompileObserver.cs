using UnityEngine;

namespace RecompileCheck
{
    // a scene object: what OnDisable sees before the reload and OnEnable after it, and the exit order
    public sealed class RecompileObserver : MonoBehaviour
    {
        void OnEnable() => RecompileRecord.Write("observer-enable default=" + RecompileRecord.DefaultState() + " ours=" + RecompileRecord.CountOurs() + " frame=" + Time.frameCount);

        void OnDisable() => RecompileRecord.Write("observer-disable default=" + RecompileRecord.DefaultState() + " frame=" + Time.frameCount + " ini=" + RecompilePlayers.Place("ini"));

        void OnApplicationQuit() => RecompileRecord.Write("observer-quit default=" + RecompileRecord.DefaultState());

        void OnDestroy() => RecompileRecord.Write("observer-destroy default=" + RecompileRecord.DefaultState());
    }
}
