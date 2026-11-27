using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // a user's component with Feedback Player fields, and a default object field beside them — the editor's player field
    // tests draw it
    public sealed class PlayerFieldHolder : MonoBehaviour
    {
        public FeedbackPlayer One;
        public List<FeedbackPlayer> Many = new List<FeedbackPlayer>();
        public FeedbackPlayer[] Group = new FeedbackPlayer[1];
        public Light Other;
    }
}
