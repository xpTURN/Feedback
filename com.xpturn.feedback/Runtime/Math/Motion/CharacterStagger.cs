namespace xpTURN.Feedback
{
    // a character's share of the item — k = i / (n − 1), mirrored for a backward order. A Tween runs a character on its
    // window of the item, T_i = clamp01((T − s · k) / (1 − s)), so every character ends inside the item's length; an
    // Oscillate lags a character by s · k turns (the text holds s turns; an item of u < 1 turns holds u · s — its share s of
    // the item). A late character waits at the wave's start (the first character's value at the press) until its turn, and
    // the lag closes over the item's last s, at most 2s / F seconds, so a character never runs more than twice the wave's
    // pace and, in an item that runs to its end, ends where the first does
    internal static class CharacterStagger
    {
        internal const float MaxStagger = 0.95f;

        internal static float Share(int index, int count, bool backward)
        {
            if (count <= 1)
                return 0f;
            float k = (float)index / (count - 1);
            return backward ? 1f - k : k;
        }

        internal static float Window(float t, float stagger, float share)
        {
            if (!(stagger > 0f))
                return t;
            float s = stagger > MaxStagger ? MaxStagger : stagger;
            return MotionKernels.Clamp01((t - s * share) / (1f - s));
        }

        // the lag of share 1 — the item's turns u = F · Seconds / T (T 0 is an item without an end: the whole lag)
        internal static double Lag(double seconds, float t, float stagger, float frequency)
        {
            if (!(stagger > 0f) || !(frequency > 0f))
                return 0.0;
            float s = stagger > MaxStagger ? MaxStagger : stagger;
            double lag = (double)s / frequency;
            float window = s;
            if (t > 0f)
            {
                double turns = frequency * seconds / t;
                if (turns < 1.0)
                    lag *= turns;
                else if (turns > 2.0)
                    window = (float)(2.0 * s / turns);
            }
            return lag * MotionKernels.Clamp01((1f - t) / window);
        }

        internal static double Lagged(double seconds, double lag, float share)
        {
            double lagged = seconds - lag * share;
            return lagged > 0.0 ? lagged : 0.0;
        }
    }
}
