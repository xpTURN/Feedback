namespace xpTURN.Feedback
{
    // the scheduler calls the layers through this — the registry and a test's recording layer stand in the same place
    internal interface ILayerStages
    {
        void Revert();

        void Apply(ConsumptionStage stage);
    }
}
