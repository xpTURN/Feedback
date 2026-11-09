using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests.Editor
{
    // saves items the way a preset does
    public sealed class DataModelTestHolder : ScriptableObject
    {
        [SerializeReference] public List<FeedbackItem> items = new List<FeedbackItem>();

        // a persistent listener target
        public void OnTestEvent()
        {
        }
    }
}
