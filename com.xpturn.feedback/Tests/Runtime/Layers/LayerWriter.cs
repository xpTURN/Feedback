using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // the game writing a transform once — in Update (Mode 0) or LateUpdate (Mode 1) at AtFrame, and what it read back right
    // after — and the localPosition each callback saw, by frame
    public sealed class LayerWriter : MonoBehaviour
    {
        public int Mode;
        public int AtFrame = -1;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 ReadPosition;
        public Quaternion ReadRotation;
        public bool Wrote;
        public readonly List<(int Frame, string Where, Vector3 Value)> Seen = new List<(int, string, Vector3)>();

        void FixedUpdate() => Seen.Add((Time.frameCount, "FixedUpdate", transform.localPosition));

        void Update()
        {
            Seen.Add((Time.frameCount, "Update", transform.localPosition));
            if (Mode == 0)
                TryWrite();
        }

        void LateUpdate()
        {
            Seen.Add((Time.frameCount, "LateUpdate", transform.localPosition));
            if (Mode == 1)
                TryWrite();
        }

        void TryWrite()
        {
            if (Time.frameCount != AtFrame)
                return;
            transform.localPosition = Position;
            ReadPosition = transform.localPosition;
            transform.localRotation = Rotation;
            ReadRotation = transform.localRotation;
            Wrote = true;
        }
    }
}
