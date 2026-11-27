using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor
{
    // a user's object with Feedback Player fields, and a default object field beside them
    internal sealed class PlayerHolder : ScriptableObject
    {
        [Tooltip("A user's tooltip")] public FeedbackPlayer One;
        public List<FeedbackPlayer> Many = new List<FeedbackPlayer>();
        public Light Other;
    }
}
