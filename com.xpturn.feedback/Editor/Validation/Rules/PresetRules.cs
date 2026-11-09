using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // the preset's rules

    // FB-A09: a Direct target in a preset — a preset cannot hold an object for each player
    internal sealed class PresetDirectRule : FeedbackRule
    {
        internal const string Message = "A preset cannot hold an object — use Self, Child, Anchor or Main Camera";

        internal override string Id => "FB-A09";
        internal override RuleSeverity Severity => RuleSeverity.Error;
        internal override bool Applies(Component host) => false;
        internal override bool ListOnly => true;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (!(c.Owner is FeedbackPreset))
                return;
            for (int i = 0; i < c.Items.Count; i++)
            {
                FeedbackItem item = c.Items[i];
                if (!RuleFixes.TargetOf(item, out TargetRef target) || target.m_Mode != TargetRefMode.Direct || TargetChecks.Global(item.GetType()))
                    continue;
                RuleResult r = Result(c, i, Message);
                bool camera = item is CameraShakeItem;
                r.Fixes.Add(c.ItemFix(camera ? "Target the main camera" : "Target Self", c.ItemPath(i, "m_Target"), p =>
                {
                    p.FindPropertyRelative("m_Mode").enumValueIndex = (int)(camera ? TargetRefMode.MainCamera : TargetRefMode.Self);
                    p.FindPropertyRelative("m_Object").objectReferenceValue = null;
                }));
                into.Add(r);
            }
        }
    }

    // FB-B08: the linked preset was deleted — the player plays its own hidden items
    internal sealed class MissingPresetRule : FeedbackRule
    {
        internal const string Message = "The linked preset is missing — the player plays its own hidden items";

        internal override string Id => "FB-B08";
        internal override RuleSeverity Severity => RuleSeverity.Warning;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            if (!PresetLinks.Missing(c.Player))
                return;
            RuleResult r = Result(c, -1, Message);
            Component host = c.Host;
            r.Fixes.Add(new RuleFix("Clear the link", () => RuleFixes.Write(host, "m_Preset", p => p.objectReferenceValue = null)));
            into.Add(r);
        }
    }
}
