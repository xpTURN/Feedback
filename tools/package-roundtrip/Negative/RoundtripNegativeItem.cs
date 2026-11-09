using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // check-package-roundtrip.sh's negative control: a saved type in the Cinemachine optional assembly — saved types belong
    // in Core, this one is there on purpose. The tool copies it into a package copy's Runtime/Cinemachine/; never into the
    // package
    [FeedbackTarget("Probe/Package Roundtrip Negative", PreviewGrade.DisplayOnly)]
    [Serializable]
    public sealed class RoundtripNegativeItem : ActionItem
    {
        [SerializeField] internal float m_Value = 1f;

        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.Time;
        internal override float NaturalDuration => 0f;
    }
}
