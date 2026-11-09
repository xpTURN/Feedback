using System;
using UnityEngine;

namespace xpTURN.Feedback
{
    // the implementation assembly's names (the device's fields are read there only)
    internal static class HapticsNames
    {
        internal const string PlayerInput = "UnityEngine.InputSystem.PlayerInput, Unity.InputSystem";
        internal const string Assembly = "xpTURN.Feedback.InputSystem";
    }

    // the gamepads a rumble drives — the current one, every one, or a player's paired ones
    public enum GamepadRumbleDevice
    {
        Current = 0,
        All = 1,
        Player = 2,
    }

    // the gamepad's two motors (x the low frequency, y the high — 0 to 1). Nothing stays on the motors — a play that does not
    // write in a frame asks for nothing; the clock starts unscaled (a pause menu at time scale 0 does not hold the motors)
    [FeedbackTarget("Haptics/Gamepad Rumble", PreviewGrade.None, Aliases = "rumble vibration haptics gamepad motor", Global = true, Implementation = HapticsNames.Assembly)]
    [Serializable]
    public sealed class GamepadRumbleItem : TargetItem
    {
        static readonly TargetTraits s_Traits = new TargetTraits(valueKind: ValueKind.Vector2, supportedMotions: MotionMask.Tween | MotionMask.Flash | MotionMask.Curve,
            category: FeedbackCategory.Haptics, flashSensitive: false, applyMode: ApplyMode.Offset);

        [SerializeField] internal GamepadRumbleDevice m_Device;
        [SerializeField] internal TargetRef m_Player = new TargetRef { m_Mode = TargetRefMode.Parent, m_Path = "", m_Anchor = "" };

        public GamepadRumbleItem()
        {
            m_Timing.m_Clock = ItemClock.Unscaled;
        }

        internal override TargetTraits Traits => s_Traits;
    }
}
