using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // the value the test slot reads and writes, and the ways it misbehaves. Value touches the engine as a real property does —
    // a destroyed target throws instead of reading its managed field
    public sealed class LayerTestTarget : MonoBehaviour
    {
        float m_Value;
        public int Writes;
        public bool Throws;
        public bool ThrowsOnRead;
        public bool Clamps;
        public bool Flips;
        public Action OnWrite;
        // what FixedUpdate · Update · LateUpdate saw, by frame — edit mode runs none of them
        public readonly List<(int Frame, string Where, float Value)> Seen = new List<(int, string, float)>();

        public float Value
        {
            get
            {
                _ = transform;
                return m_Value;
            }
            set
            {
                _ = transform;
                m_Value = value;
            }
        }

        void FixedUpdate() => Seen.Add((Time.frameCount, "FixedUpdate", Value));

        void Update() => Seen.Add((Time.frameCount, "Update", Value));

        void LateUpdate() => Seen.Add((Time.frameCount, "LateUpdate", Value));
    }
}
