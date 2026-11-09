using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Scripting;

namespace xpTURN.Feedback.Tests
{
    // the release probe's rumble — the main probe can't name the Input System, so it calls these by name: a recording gamepad
    // made current and a rumble played on it from the Default; the pad's last motor values (after the Default's close they are
    // (0, 0))
    static class RumbleReleaseProbe
    {
        static RecordingGamepad s_Pad;

        [Preserve]
        internal static void Begin(GameObject owner)
        {
            s_Pad = InputSystem.AddDevice<RecordingGamepad>("release probe pad");
            s_Pad.MakeCurrent();
            var player = owner.AddComponent<FeedbackPlayer>();
            var item = new GamepadRumbleItem { m_Motion = new FlashMotion { m_Params = new FlashParams { m_Target = new Vector4(0.5f, 0.25f, 0f, 0f), m_Attack = 0f, m_Hold = 1f, m_Release = 0f } } };
            item.m_Timing.m_Duration = 100f;
            player.m_Items.Add(item);
            player.Play();
        }

        [Preserve]
        internal static string Last()
        {
            Vector2 v = RecordingGamepad.LastFor(s_Pad);
            return v.x.ToString("0.00", CultureInfo.InvariantCulture) + "," + v.y.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
