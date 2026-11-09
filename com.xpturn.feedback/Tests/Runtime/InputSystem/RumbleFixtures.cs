using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;

namespace xpTURN.Feedback.Tests
{
    // the rumble's fixtures (a global item — no leaf) and the recording gamepad's layout at the runtime's load (the editor's load
    // registers it from the editor test assembly — a domain reload recreates only the devices whose layout is registered by then)
    static class RumbleFixtures
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            ModuleFixtures.Add("rumble", _ => { }, new[] { typeof(GamepadRumbleItem) }, null);
            InputSystem.RegisterLayout<RecordingGamepad>();
        }
    }

    // a gamepad that records its haptic calls (frame · low · high · kind) before the base sends them — no unsafe code. A
    // managed device: the test context drives it
    [InputControlLayout(stateType = typeof(GamepadState), displayName = "Feedback Recording Gamepad")]
    public class RecordingGamepad : Gamepad
    {
        internal static readonly int[] Frames = new int[4096];
        internal static readonly Vector2[] Values = new Vector2[4096];
        internal static readonly RecordingGamepad[] Pads = new RecordingGamepad[4096];
        internal static readonly char[] Kinds = new char[4096];
        internal static int Count;

        void Record(char kind, float low, float high)
        {
            if (Count == Frames.Length)
                return;
            Frames[Count] = Time.frameCount;
            Values[Count] = new Vector2(low, high);
            Pads[Count] = this;
            Kinds[Count] = kind;
            Count++;
        }

        public override void SetMotorSpeeds(float lowFrequency, float highFrequency)
        {
            Record('S', lowFrequency, highFrequency);
            base.SetMotorSpeeds(lowFrequency, highFrequency);
        }

        public override void ResetHaptics()
        {
            Record('R', 0f, 0f);
            base.ResetHaptics();
        }

        public override void PauseHaptics()
        {
            Record('P', 0f, 0f);
            base.PauseHaptics();
        }

        internal static int CountFor(RecordingGamepad pad, int from = 0)
        {
            int n = 0;
            for (int i = from; i < Count; i++)
            {
                if (Pads[i] == pad)
                    n++;
            }
            return n;
        }

        internal static Vector2 LastFor(RecordingGamepad pad)
        {
            for (int i = Count - 1; i >= 0; i--)
            {
                if (Pads[i] == pad)
                    return Values[i];
            }
            return new Vector2(-1f, -1f);
        }

        internal static Vector2 Peak(RecordingGamepad pad, int from)
        {
            Vector2 m = Vector2.zero;
            for (int i = from; i < Count; i++)
            {
                if (Pads[i] == pad)
                    m = Vector2.Max(m, Values[i]);
            }
            return m;
        }

        internal static string Tail(RecordingGamepad pad, int from)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = from; i < Count; i++)
            {
                if (pad == null || Pads[i] == pad)
                    sb.Append($"{Kinds[i]}({Values[i].x:0.00},{Values[i].y:0.00})@{Frames[i]} ");
            }
            return sb.ToString();
        }

        internal static void RemoveAll()
        {
            foreach (InputDevice d in InputSystem.devices.ToArray())
            {
                if (d is RecordingGamepad)
                    InputSystem.RemoveDevice(d);
            }
        }
    }
}
