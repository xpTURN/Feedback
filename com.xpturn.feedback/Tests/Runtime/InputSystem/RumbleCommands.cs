using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace xpTURN.Feedback.Tests
{
    // the device commands seen through InputSystem.onDeviceCommand — the rumble's 'RMBL' (its two floats after the command's
    // header). The callback returns null: a value holds the callback list's removal back
    static unsafe class RumbleCommands
    {
        static readonly FourCC s_Rumble = new FourCC('R', 'M', 'B', 'L');
        static readonly System.Text.StringBuilder s_Log = new System.Text.StringBuilder();

        internal static string Log => s_Log.ToString();

        internal static void Clear() => s_Log.Clear();

        static long? On(InputDevice device, InputDeviceCommand* command)
        {
            if (command->type == s_Rumble)
            {
                float* f = (float*)((byte*)command + InputDeviceCommand.BaseCommandSize);
                s_Log.Append($"{device.name}:RMBL[{command->sizeInBytes}]({f[0]:0.00},{f[1]:0.00}) ");
            }
            else
                s_Log.Append($"{device.name}:{command->type} ");
            return null;
        }

        internal static void Listen(bool on)
        {
            if (on)
                InputSystem.onDeviceCommand += On;
            else
                InputSystem.onDeviceCommand -= On;
        }
    }
}
