using System;
using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Tests
{
    // one list for everything a frame records — the MonoBehaviour callbacks here, the stages and the other injectors in the
    // tests — with the frame number beside each entry. The editor interop assembly and the real loop tests share it: public,
    // and no UnityEditor — this assembly compiles for players
    public sealed class FrameRecorder : MonoBehaviour
    {
        public static readonly List<string> Record = new List<string>();
        public static readonly List<int> Frames = new List<int>();
        public static Action UpdateHook;

        public static void Add(string entry)
        {
            Record.Add(entry);
            Frames.Add(Time.frameCount);
        }

        public static void Clear()
        {
            Record.Clear();
            Frames.Clear();
        }

        void FixedUpdate() => Add("MB.FixedUpdate");

        void Update()
        {
            Add("MB.Update");
            UpdateHook?.Invoke();
        }

        void LateUpdate() => Add("MB.LateUpdate");
    }
}
