using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // moves its transform in Update or LateUpdate — what a Follow driver reads, and when
    public sealed class MotionDriverMover : MonoBehaviour
    {
        internal bool Late;
        internal Vector3 Step;

        void Update()
        {
            if (!Late)
                transform.localPosition += Step;
        }

        void LateUpdate()
        {
            if (Late)
                transform.localPosition += Step;
        }
    }
}
