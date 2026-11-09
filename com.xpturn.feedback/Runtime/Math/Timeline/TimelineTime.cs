using System;

namespace xpTURN.Feedback
{
    // times are integer microseconds; Infinite is a marker, never a converted +inf.
    internal static class TimelineTime
    {
        public const long Infinite = long.MaxValue;
        public const double MicrosPerSecond = 1e6;

        // Below long.MaxValue with margin — +inf, NaN and out-of-range values become the marker instead of an
        // unspecified conversion (dotnet arm64 saturates to MaxValue, IL2CPP returns INT64_MIN).
        const double FiniteLimit = 9.2e18;

        public static long FromSeconds(double seconds)
        {
            double micros = Math.Round(seconds * MicrosPerSecond);
            return micros < FiniteLimit ? (long)micros : Infinite;
        }

        public static double ToSeconds(long micros)
            => micros == Infinite ? double.PositiveInfinity : micros / MicrosPerSecond;

        public static bool IsInfinite(long micros) => micros == Infinite;

        public static long Add(long a, long b)
            => b > 0 && a > Infinite - b ? Infinite : a + b;

        public static long Multiply(long a, long count)
            => count <= 0 ? 0 : a > Infinite / count ? Infinite : a * count;

        public static long Max(long a, long b) => a > b ? a : b;

        public static long Min(long a, long b) => a < b ? a : b;
    }
}
