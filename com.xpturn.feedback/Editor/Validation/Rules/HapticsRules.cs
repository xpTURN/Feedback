using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // group S — FB-S01: a rumble item while Active Input Handling is the old Input Manager only — the Input System has no
    // device and the rumble does nothing (the editor compiles with the setting's define; a change of it restarts the editor —
    // no fix). Quiet without the rumble's implementation (no Input System package)
    internal sealed class HapticsInputHandlingRule : FeedbackRule
    {
        // the setting the editor compiled with — a seam for the tests
        internal static Func<bool> NewInputOn = () =>
        {
#if ENABLE_INPUT_SYSTEM
            return true;
#else
            return false;
#endif
        };

        internal override string Id => "FB-S01";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (NewInputOn() || !FeedbackImplementations.RegisteredTypes.Contains(typeof(GamepadRumbleItem)))
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is GamepadRumbleItem t && t.m_Active)
                    into.Add(Result(c, i, "Gamepad Rumble does nothing while Active Input Handling is the old Input Manager only"));
            }
        }
    }
}
