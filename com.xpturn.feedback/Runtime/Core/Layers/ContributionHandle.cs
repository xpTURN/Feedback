namespace xpTURN.Feedback
{
    // one contribution of a slot, the ledger's handle in a Core type (like PlaybackHandle — a binding in an assembly
    // referencing Core only compiles). The binding keeps the slot beside it. default is invalid
    internal readonly struct ContributionHandle
    {
        internal readonly SlotHandle Slot;

        internal ContributionHandle(SlotHandle slot)
        {
            Slot = slot;
        }

        // a closed registry's or a detached slot's Begin gives an invalid one
        internal bool IsValid => Slot.Generation != 0;
    }
}
