using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // a stand-in for Cinemachine's brain where the package is not in — FB-H05 finds the brain by its short type name.
    // Where the package is in, that name finds the package's type and not this one
    public sealed class CinemachineBrain : MonoBehaviour
    {
    }
}
