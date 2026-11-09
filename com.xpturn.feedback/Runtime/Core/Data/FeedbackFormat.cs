using System.Collections.Generic;

namespace xpTURN.Feedback
{
    // the format version is per container — FeedbackPlayer and FeedbackPreset call this from OnAfterDeserialize
    internal static class FeedbackFormat
    {
        internal const int Current = 1;

        // data only — no Unity API, no static state. Current and newer versions keep their version
        internal static void Upgrade(ref int version, List<FeedbackItem> items)
        {
            // field steps — every call, whatever the version: a prefab instance keeps old fields under the current version.
            // There are none
            if (version >= Current)
                return;
            // structure steps, oldest first — there are none
            version = Current;
        }

        // a container of one item — MotionDriver. The same steps as the list's
        internal static void Upgrade(ref int version, FeedbackItem item)
        {
            if (version >= Current)
                return;
            version = Current;
        }
    }
}
