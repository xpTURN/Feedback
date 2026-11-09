using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // timeline structure. Field names are the saved format
    [FeedbackTarget("Flow/Wait", PreviewGrade.Full, Aliases = "pause delay")]
    [Serializable]
    public sealed class WaitItem : FlowItem
    {
        public WaitItem()
        {
            m_Timing.m_Duration = 1f;
        }
    }

    [FeedbackTarget("Flow/Wait For Signal", PreviewGrade.DisplayOnly, Aliases = "signal resume script pause")]
    [Serializable]
    public sealed class WaitForSignalItem : FlowItem
    {
        [SerializeField] internal float m_Timeout;          // seconds, 0 = none
    }

    [FeedbackTarget("Flow/Loop Start", PreviewGrade.Full, Aliases = "loop repeat")]
    [Serializable]
    public sealed class LoopStartItem : FlowItem
    {
    }

    [FeedbackTarget("Flow/Loop End", PreviewGrade.Full, Aliases = "loop repeat looper")]
    [Serializable]
    public sealed class LoopEndItem : FlowItem
    {
        [SerializeField] internal int m_LoopCount = 2;      // total runs, 0 = infinite
        [SerializeField] internal float m_LoopGap;          // seconds between runs, on this item's clock
    }

    [FeedbackTarget("Flow/Hold", PreviewGrade.Full, Aliases = "holding pause after all")]
    [Serializable]
    public sealed class HoldItem : FlowItem
    {
    }
}
