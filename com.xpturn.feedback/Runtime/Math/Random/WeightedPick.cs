namespace xpTURN.Feedback
{
    // one index by its weight — a weight that is not finite and positive counts 0. −1 when none counts
    internal static class WeightedPick
    {
        // unit in [0, 1)
        internal static int Index(float[] weights, int count, float unit)
        {
            double total = 0;
            for (int i = 0; i < count; i++)
                total += Weight(weights[i]);
            if (!(total > 0))
                return -1;
            double at = unit * total;
            int last = -1;
            for (int i = 0; i < count; i++)
            {
                double w = Weight(weights[i]);
                if (w <= 0)
                    continue;
                last = i;
                if (at < w)
                    return i;
                at -= w;
            }
            return last;
        }

        static double Weight(float w) => w > 0f && w < float.PositiveInfinity ? w : 0.0;
    }
}
