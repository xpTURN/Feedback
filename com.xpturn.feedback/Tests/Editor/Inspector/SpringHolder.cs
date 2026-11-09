using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor
{
    // a user's object with spring fields — one left without an initialiser
    internal sealed class SpringHolder : ScriptableObject
    {
        public SpringFloat Bare;
        public SpringVector3 Initialised = new SpringVector3(Vector3.zero);
    }
}
