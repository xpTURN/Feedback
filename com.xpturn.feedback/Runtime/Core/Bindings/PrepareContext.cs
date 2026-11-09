using UnityEngine;

namespace xpTURN.Feedback
{
    // what a binding prepares against
    internal readonly struct PrepareContext
    {
        internal readonly GameObject Owner;         // the base of Self · Parent · Child
        internal readonly FeedbackPlayer Player;    // the anchors — null without a player (MotionDriver)
        internal readonly FeedbackContext Context;  // the layers and the services
        internal readonly int MaxOverlap;           // the per-play state count
        // the runtime and the item's list index — an open item ends its own node. null · −1 without a player
        internal readonly PlayerRuntime Runtime;
        internal readonly int ListIndex;

        internal PrepareContext(GameObject owner, FeedbackPlayer player, FeedbackContext context, int maxOverlap)
            : this(owner, player, context, maxOverlap, null, -1)
        {
        }

        internal PrepareContext(GameObject owner, FeedbackPlayer player, FeedbackContext context, int maxOverlap, PlayerRuntime runtime, int listIndex)
        {
            Owner = owner;
            Player = player;
            Context = context;
            MaxOverlap = maxOverlap;
            Runtime = runtime;
            ListIndex = listIndex;
        }

        internal PrepareContext ForItem(int listIndex) => new PrepareContext(Owner, Player, Context, MaxOverlap, Runtime, listIndex);
    }
}
