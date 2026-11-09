namespace xpTURN.Feedback.Samples
{
    // the layer gate — a sample plays a layer's feedback only while the HUD says After and that layer is on.
    // It writes no category (a category None item — dust, a trail — is gated as well); every layer is on again when the samples'
    // state starts over (a sample change · the hub · a new play). A sample asks IsOn and calls the player itself
    public static class SampleLayers
    {
        public const int Count = 8;
        static readonly bool[] s_Off = new bool[Count];

        // the layer's own switch (the HUD's After aside)
        public static bool Enabled(int layer) => (uint)layer < Count && !s_Off[layer];

        public static bool IsOn(int layer) => SampleState.After && Enabled(layer);

        public static void Set(int layer, bool on)
        {
            if ((uint)layer < Count)
                s_Off[layer] = !on;
        }


        internal static void Reset()
        {
            for (int i = 0; i < Count; i++)
                s_Off[i] = false;
        }
    }
}
