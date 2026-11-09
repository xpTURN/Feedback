using UnityEngine;

namespace xpTURN.Feedback.Samples
{
    // one per bundle — the hub gathers the catalogs of the imported bundles, in Order
    public sealed class SampleCatalog : ScriptableObject
    {
        public string Bundle;
        public int Order;
        public SampleCard[] Cards;
    }
}
