using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // a behaviour that runs in the edit mode too — how many OnEnable, and where its object was at the last one (a spawn
    // wakes placed)
    [ExecuteAlways]
    public sealed class EnabledTestBehaviour : MonoBehaviour
    {
        public static int Enables;
        public static Vector3 EnabledAt;

        void OnEnable()
        {
            Enables++;
            EnabledAt = transform.position;
        }
    }
}
