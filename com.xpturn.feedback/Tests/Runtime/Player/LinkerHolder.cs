using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // a Resources prefab's reference to a prefab outside Resources — the build carries it, and the linker processor reads
    // what a Resources prefab uses
    public sealed class LinkerHolder : MonoBehaviour
    {
        public GameObject m_Target;
    }
}
