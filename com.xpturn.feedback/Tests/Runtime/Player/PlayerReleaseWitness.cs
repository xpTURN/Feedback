using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // a script on the player's object, after the player, that calls into the player from its own OnDestroy — the player's
    // OnDestroy has run and the object is not null yet
    public sealed class PlayerReleaseWitness : MonoBehaviour
    {
        internal FeedbackPlayer Player;
        internal string Called = "not called";

        void OnDestroy()
        {
            if (Player is null)
                return;
            bool had = Player.Runtime != null;
            Called = $"the player's runtime {(had ? "present" : "none")} → ensure {Player.EnsureRuntime()} · rebuild {Player.Rebuild()}";
        }
    }
}
