using System;

namespace xpTURN.Feedback.Editor
{
    // a new item from a leaf — the type's parameterless constructor, a target item's (or a user action's) empty Direct made Self (the camera shake
    // keeps its MainCamera · a global item has no target), a motion leaf's motion and duration from the defaults table
    internal static class ItemFactory
    {
        internal static FeedbackItem Create(in CatalogLeaf leaf)
        {
            var item = (FeedbackItem)Activator.CreateInstance(leaf.Type, true);
            if (item is TargetItem t)
            {
                if (t.m_Target.m_Mode == TargetRefMode.Direct && t.m_Target.m_Object == null && !TargetChecks.Global(leaf.Type))
                    t.m_Target.m_Mode = TargetRefMode.Self;
                if (leaf.Motion != CatalogLeaf.NoMotion)
                {
                    t.m_Motion = MotionDefaults.Create(leaf.Type, (MotionKind)leaf.Motion, out float duration);
                    if (duration > 0f)
                        t.m_Timing.m_Duration = duration;
                }
            }
            else if (item is CustomActionItem a && a.m_Target.m_Mode == TargetRefMode.Direct && a.m_Target.m_Object == null && !TargetChecks.Global(leaf.Type))
            {
                a.m_Target.m_Mode = TargetRefMode.Self;
            }
            return item;
        }
    }
}
