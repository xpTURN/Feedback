using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // a user's component with Feedback Player fields that an IMGUI editor draws — the editor's player field tests
    public sealed class PlayerFieldImguiHolder : MonoBehaviour
    {
        public FeedbackPlayer One;
        public List<FeedbackPlayer> Many = new List<FeedbackPlayer>();
    }
}
