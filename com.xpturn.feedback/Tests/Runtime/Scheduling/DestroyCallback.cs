using System;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // a scene object's cleanup at play exit asks a service again. This assembly does not know
    // the editor tests' internal services, so the editor test sets the callback
    public sealed class DestroyCallback : MonoBehaviour
    {
        public static Action Action;

        void OnDestroy() => Action?.Invoke();
    }
}
