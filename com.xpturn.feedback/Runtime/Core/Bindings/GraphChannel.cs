namespace xpTURN.Feedback
{
    // one value a target item writes, as the editor's graph draws it — the evaluator its binding's Prepare builds, the space
    // its offsets take and how to read the samples. Made by a binding that was not prepared: no target, no slot
    internal readonly struct GraphChannel
    {
        // empty: the item's only value
        internal readonly string Name;
        internal readonly MotionEvaluator Evaluator;
        internal readonly LayerSpace Space;
        // an on/off value — on where the sample's value is not 0 and its weight is above 0
        internal readonly bool Toggle;
        // the occurrence's seed tagged as a copy (a camera shake's rotation)
        internal readonly bool CopySeed;
        // empty: the property's own units — else what the motion's value means to the property
        internal readonly string Unit;

        internal GraphChannel(string name, MotionEvaluator evaluator, LayerSpace space, bool toggle = false, bool copySeed = false, string unit = "")
        {
            Name = name;
            Evaluator = evaluator;
            Space = space;
            Toggle = toggle;
            CopySeed = copySeed;
            Unit = unit;
        }

        internal int Dimension => Evaluator.Dimension;
    }
}
