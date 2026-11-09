using System.Collections.Generic;
using UnityEngine;

namespace xpTURN.Feedback.Editor
{
    // F's next numbers — the sprite, line and trail items

    // FB-F12: a Sprite Swap · Sprite Sheet the prepare refuses — no sprite to swap in · no frame; a sheet's empty frames are skipped
    internal sealed class SpriteSetupRule : FeedbackRule
    {
        internal override string Id => "FB-F12";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (c.Items[i] is SpriteSwapItem w && w.m_Active && w.m_Sprite == null)
                {
                    into.Add(Result(c, i, "No sprite to swap in — the item is skipped"));
                    continue;
                }
                if (!(c.Items[i] is SpriteSheetItem h) || !h.m_Active)
                    continue;
                int count = h.m_Frames != null ? h.m_Frames.Count : 0;
                int empty = 0;
                for (int k = 0; k < count; k++)
                {
                    if (h.m_Frames[k] == null)
                        empty++;
                }
                if (empty == count)
                    into.Add(Result(c, i, "No frame — the item is skipped"));
                else if (empty > 0)
                    into.Add(Result(c, i, empty + " of " + count + " frames are empty — they are skipped"));
            }
        }
    }

    // FB-F13: a Sprite Color or Alpha written directly beside another on the same sprite renderer — the direct write's end puts
    // its own start back, over what the other left
    internal sealed class SpriteDirectMixRule : FeedbackRule
    {
        internal override string Id => "FB-F13";
        internal override RuleSeverity Severity => RuleSeverity.Warning;
        internal override bool Applies(Component host) => OnItems(host);

        static bool Colors(FeedbackItem item) => (item is SpriteColorItem || item is SpriteAlphaItem) && item.m_Active;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!Colors(c.Items[i]) || !((TargetItem)c.Items[i]).m_DirectWrite || !(c.Target(i) is SpriteRenderer r))
                    continue;
                bool shared = false;
                for (int k = 0; k < c.Items.Count && !shared; k++)
                    shared = k != i && Colors(c.Items[k]) && c.Target(k) == r;
                if (!shared)
                    continue;
                RuleResult res = Result(c, i, "A direct write beside another color item of " + r.name + " — its end puts its own start back over the other's");
                string path = c.ItemPath(i, "m_DirectWrite");
                res.Fixes.Add(c.ItemFix("Write through the shared layer", path, x => x.boolValue = false));
                into.Add(res);
            }
        }
    }

    // FB-F14: a Line Color on a gradient of more than two keys — only its first and last keys change (the ends' colors)
    internal sealed class LineKeysRule : FeedbackRule
    {
        internal override string Id => "FB-F14";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!(c.Items[i] is LineColorItem l) || !l.m_Active || !(c.Target(i) is LineRenderer r))
                    continue;
                Gradient g = r.colorGradient;
                int colors = g.colorKeys.Length, alphas = g.alphaKeys.Length;
                if (colors > 2 || alphas > 2)
                    into.Add(Result(c, i, "The gradient has " + colors + " color and " + alphas + " alpha keys — only the " + (l.m_End == LineEnd.End ? "last" : "first") + " ones change"));
            }
        }
    }

    // FB-F15: a trail item whose trail shows nothing — it does not emit and no item of the list turns it on · its time is 0 and no
    // item of the list changes it
    internal sealed class TrailHiddenRule : FeedbackRule
    {
        internal override string Id => "FB-F15";
        internal override RuleSeverity Severity => RuleSeverity.Info;
        internal override bool Applies(Component host) => OnItems(host);

        static bool Trail(FeedbackItem item) => item is TrailTimeItem || item is TrailWidthItem || item is TrailEmittingItem || item is TrailClearItem;

        internal override void Check(RuleContext c, List<RuleResult> into)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                if (!Trail(c.Items[i]) || !c.Items[i].m_Active || !(c.Target(i) is TrailRenderer r))
                    continue;
                bool emits = r.emitting, timed = r.time > 0f;
                for (int k = 0; k < c.Items.Count && (!emits || !timed); k++)
                {
                    if (!c.Items[k].m_Active || c.Target(k) != r)
                        continue;
                    emits |= c.Items[k] is TrailEmittingItem;
                    timed |= c.Items[k] is TrailTimeItem;
                }
                if (!emits)
                    into.Add(Result(c, i, "The trail does not emit and no item turns it on — it shows nothing"));
                else if (!timed)
                    into.Add(Result(c, i, "The trail's time is 0 and no item changes it — it shows nothing"));
            }
        }
    }
}
