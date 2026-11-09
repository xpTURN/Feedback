namespace xpTURN.Feedback
{
    // a component that takes a Custom Value item's value — the getter is the base (a revert reads it back), the setter the
    // layers' composed value. A setter that throws: in the layers' pass the slot is let go; in a Persist write (Tween · Set)
    // or the start's read the play ends faulted
    /// <summary>A component a Custom Value item drives: the getter gives the base value, the setter takes the composed value. The items drive float, Vector3 and Color; another T is driven by none.</summary>
    public interface IFeedbackTarget<T>
    {
        /// <summary>The value — read as the base (a revert reads it back), written with the composed value.</summary>
        T FeedbackValue { get; set; }
    }

    /// <summary>Chooses when a custom target's value is written; without it, Update.</summary>
    public interface IFeedbackTargetStage
    {
        /// <summary>Update or LateUpdate — read once when the player prepares.</summary>
        FeedbackTargetStage Stage { get; }
    }

    /// <summary>When a custom target's composed value is written.</summary>
    public enum FeedbackTargetStage
    {
        /// <summary>In the Update stage; the frame's LateUpdate sees the value.</summary>
        Update = 0,
        /// <summary>After LateUpdate, before rendering.</summary>
        LateUpdate = 1,
    }
}
