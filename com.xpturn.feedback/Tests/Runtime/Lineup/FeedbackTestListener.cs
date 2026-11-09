using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // a Float Event's listener — every value in order (a throwing one too) · a void one
    public sealed class FeedbackTestListener : MonoBehaviour
    {
        [NonSerialized] public readonly List<float> Values = new List<float>();
        [NonSerialized] public int Ints;
        [NonSerialized] public int Voids;
        public bool m_Throws;

        public void Take(float value)
        {
            Values.Add(value);
            if (m_Throws)
                throw new InvalidOperationException("FeedbackTestListener throws");
        }

        public void TakeInt(int value) => Ints++;

        // a void listener (a Random Unity Event's entry)
        public void Touch() => Voids++;

        public void Keep(UnityEngine.Object o)
        {
        }
    }
}
