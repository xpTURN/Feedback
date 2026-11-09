using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace xpTURN.Feedback.Tests
{
    // an item's UnityEvents in the shapes the linker's walk finds — a list, a nested class, a UnityEvent<float> under another
    // name (never played: no binding)
    [Serializable]
    public sealed class LinkerShapesTestItem : ActionItem
    {
        [Serializable]
        public sealed class Inner
        {
            public UnityEvent<float> m_OnValue = new UnityEvent<float>();
        }

        [SerializeField] internal List<UnityEvent> m_Events = new List<UnityEvent>();
        [SerializeField] internal Inner m_Inner = new Inner();
        [SerializeField] internal string m_Note = "m_PersistentCalls";

        internal override FeedbackCategory DeclaredCategory => FeedbackCategory.None;
        internal override float NaturalDuration => 0f;
    }
}
