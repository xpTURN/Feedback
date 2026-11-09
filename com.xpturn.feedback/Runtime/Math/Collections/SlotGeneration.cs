namespace xpTURN.Feedback
{
    // one counter for every slot map in the domain, so an old handle of any map — another scheduler's, a previous
    // session's — never matches a playback until the counter wraps. Domain lifetime: never reset. Main thread only.
    internal static class SlotGeneration
    {
        static uint s_Last;

        // 0 is the invalid generation — skipped on wrap
        internal static uint Next()
        {
            unchecked
            {
                if (++s_Last == 0)
                    s_Last = 1;
            }
            return s_Last;
        }
    }
}
