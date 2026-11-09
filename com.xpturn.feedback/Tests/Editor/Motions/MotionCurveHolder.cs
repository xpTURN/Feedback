using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor
{
    // non-finite: NaN keys are put in through SerializedProperty — code drops them
    public sealed class MotionCurveHolder : ScriptableObject
    {
        public AnimationCurve curve;
        public Gradient gradient;
    }
}
