using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor
{
    // the springs as fields — a struct serialized on its own is written without [Serializable]
    public sealed class SpringTestHolder : ScriptableObject
    {
        public SpringFloat single;
        public SpringVector2 pair;
        public SpringVector3 triple;
        public SpringColor color;
    }
}
